# -*- coding: utf-8 -*-
"""月光极速电子教室 —— 系统托盘（Win32 原生，右键菜单带图标，单击呼出，F1 全局热键）"""
import io
import struct
import ctypes
import threading
from ctypes import wintypes

import win32gui
import win32con
import win32api
from PIL import Image, ImageWin
import icons
import i18n

# ---- 常量（部分不在 win32con 中）----
NIM_ADD, NIM_MODIFY, NIM_DELETE = 0, 1, 2
NIF_MESSAGE, NIF_ICON, NIF_TIP = 0x1, 0x2, 0x4
NIN_SELECT = 0x400
WM_TRAY = win32con.WM_USER + 1

MIIM_BITMAP = 0x80
MENU_ICON = 12          # 托盘右键菜单图标尺寸（px）


class _MENUITEMINFO(ctypes.Structure):
    _fields_ = [
        ("cbSize", wintypes.UINT), ("fMask", wintypes.UINT),
        ("fType", wintypes.UINT), ("fState", wintypes.UINT),
        ("wID", wintypes.UINT), ("hSubMenu", ctypes.c_void_p),
        ("hbmpChecked", ctypes.c_void_p), ("hbmpUnchecked", ctypes.c_void_p),
        ("dwItemData", ctypes.c_void_p), ("dwTypeData", ctypes.c_wchar_p),
        ("cch", wintypes.UINT), ("hbmpItem", ctypes.c_void_p),
    ]


_user32 = ctypes.windll.user32
_user32.SetMenuItemInfoW.argtypes = [
    wintypes.HMENU, wintypes.UINT, wintypes.BOOL, ctypes.POINTER(_MENUITEMINFO)]
_user32.SetMenuItemInfoW.restype = wintypes.BOOL
_user32.GetSysColor.argtypes = [ctypes.c_int]
_user32.GetSysColor.restype = wintypes.DWORD


def _pil_to_hicon(im, size=16):
    """托盘/标题图标：标准 ICONIMAGE 32bpp DIB（XOR 底部朝上 + AND 掩码），渲染清晰无毛边。"""
    im = im.resize((size, size), Image.Resampling.LANCZOS).convert("RGBA")
    w, h = im.size
    px = im.tobytes("raw", "BGRA")
    rows = b"".join(px[y * w * 4:(y + 1) * w * 4] for y in reversed(range(h)))
    and_stride = ((w + 31) // 32) * 4
    and_mask = b"\x00" * (and_stride * h)
    bmi = struct.pack("<IiiHHIIiiII", 40, w, h * 2, 1, 32, 0, len(rows), 0, 0, 0, 0)
    return win32gui.CreateIconFromResource(bmi + rows + and_mask, True)


class _BITMAPINFOHEADER(ctypes.Structure):
    _fields_ = [
        ("biSize", wintypes.DWORD), ("biWidth", wintypes.LONG), ("biHeight", wintypes.LONG),
        ("biPlanes", wintypes.WORD), ("biBitCount", wintypes.WORD), ("biCompression", wintypes.DWORD),
        ("biSizeImage", wintypes.DWORD), ("biXPelsPerMeter", wintypes.LONG),
        ("biYPelsPerMeter", wintypes.LONG), ("biClrUsed", wintypes.DWORD),
        ("biClrImportant", wintypes.DWORD),
    ]


class _BITMAPINFO(ctypes.Structure):
    _fields_ = [("bmiHeader", _BITMAPINFOHEADER), ("bmiColors", ctypes.c_byte * 4)]


_gdi32 = ctypes.windll.gdi32
_gdi32.CreateDIBSection.argtypes = [
    wintypes.HDC, ctypes.POINTER(_BITMAPINFO), wintypes.UINT,
    ctypes.POINTER(ctypes.c_void_p), ctypes.c_void_p, wintypes.DWORD]
_gdi32.CreateDIBSection.restype = wintypes.HBITMAP


def _pil_to_hbitmap(im, size=16):
    """菜单图标：先与系统菜单底色融合为不透明位图，彻底消除半透明边缘毛边
    （未预乘 alpha 的 DIB 在菜单首次绘制会出现暗边，悬停重绘才正常）。"""
    im = im.resize((size, size), Image.Resampling.LANCZOS).convert("RGBA")
    try:
        c = _user32.GetSysColor(4)          # COLOR_MENU
    except Exception:
        c = 0xF0F0F0
    bg = Image.new("RGBA", im.size, (c & 0xFF, (c >> 8) & 0xFF, (c >> 16) & 0xFF, 255))
    im = Image.alpha_composite(bg, im).convert("RGBA")   # 输出不透明
    w, h = im.size
    data = im.tobytes("raw", "BGRA")
    bh = _BITMAPINFOHEADER()
    bh.biSize = ctypes.sizeof(_BITMAPINFOHEADER)
    bh.biWidth = w
    bh.biHeight = -h            # 自上而下
    bh.biPlanes = 1
    bh.biBitCount = 32
    bh.biCompression = 0        # BI_RGB
    bh.biSizeImage = len(data)
    bmi = _BITMAPINFO()
    bmi.bmiHeader = bh
    bits = ctypes.c_void_p()
    hdc = win32gui.GetDC(0)
    try:
        hbmp = _gdi32.CreateDIBSection(hdc, ctypes.byref(bmi), 0,
                                       ctypes.byref(bits), None, 0)
        if bits and hbmp:
            ctypes.memmove(bits, data, len(data))
        return hbmp
    finally:
        win32gui.ReleaseDC(0, hdc)


class Tray:
    """系统托盘。on: dict，命令 -> 线程安全回调（建议推入队列由主线程消费）。"""

    # 菜单项按当前界面语言在运行时生成（text, icon, command）
    @staticmethod
    def items():
        return [
            (i18n.t("start"), "play", "start"),
            (i18n.t("stop"), "stop", "stop"),
            None,                               # 分割线：开播/停止 与 收看/退出 隔开
            (i18n.t("watch"), "watch", "watch"),
            (i18n.t("quit"), "quit", "quit"),
        ]

    # 兼容旧代码引用
    ITEMS = property(lambda self: self.items())

    def __init__(self, on, disabled=()):
        self.on = on
        self.disabled = disabled          # 需要置灰的命令（如未开播时“停止”）
        self.hwnd = None
        self._hicon = None
        self._nid = None
        self._thread = None
        self._keep_hbmp = []

    def start(self):
        self._thread = threading.Thread(target=self._run, daemon=True, name="LanStreamTray")
        self._thread.start()

    def update_disabled(self, disabled):
        self.disabled = disabled

    # ---- 主循环 ----
    def _run(self):
        try:
            self._run_inner()
        except Exception:
            import traceback
            try:
                import os
                base = os.environ.get("LOCALAPPDATA", os.path.expanduser("~"))
                d = os.path.join(base, "LanStream")
                os.makedirs(d, exist_ok=True)
                with open(os.path.join(d, "tray_error.txt"), "w", encoding="utf-8") as f:
                    f.write(traceback.format_exc())
            except Exception:
                pass

    def _run_inner(self):
        import ctypes as _ct
        # frozen(PyInstaller) 下 win32api.GetModuleHandle(None) 可能返回无效句柄，
        # 导致 RegisterClass/CreateWindowEx 失败、托盘图标不显示。优先用 kernel32
        # 取当前进程主模块句柄，拿不到再退回 win32api。
        hinst = _ct.windll.kernel32.GetModuleHandleW(None) or win32api.GetModuleHandle(None)
        wc = win32gui.WNDCLASS()
        wc.hInstance = hinst
        wc.lpszClassName = "LanStreamTrayWnd"
        wc.lpfnWndProc = self._wndproc
        try:
            win32gui.RegisterClass(wc)
        except win32gui.error:
            pass
        self.hwnd = win32gui.CreateWindowEx(
            0, wc.lpszClassName, "", win32con.WS_OVERLAPPED,
            0, 0, 0, 0, 0, 0, hinst, None)   # 真实（隐藏）顶层窗口，用于承载右键弹出菜单
        win32gui.ShowWindow(self.hwnd, win32con.SW_HIDE)
        # 托盘图标统一使用项目内置 icon.ico（用户指定）；用 32px 生成后由系统缩放，
        # 在高 DPI(125%/150%) 下比直接给 16px 更清晰。
        self._hicon = _pil_to_hicon(icons.load_app_icon(64), 32)
        self._nid = (self.hwnd, 0, NIF_MESSAGE | NIF_ICON | NIF_TIP,
                     WM_TRAY, self._hicon, i18n.t("app_title"))
        win32gui.Shell_NotifyIcon(NIM_ADD, self._nid)

        try:
            while True:
                res = win32gui.GetMessage(None, 0, 0)
                if res is None or res[0] == 0:
                    break
                msg = res[1]
                win32gui.TranslateMessage(msg)
                win32gui.DispatchMessage(msg)
        finally:
            try:
                win32gui.Shell_NotifyIcon(NIM_DELETE, self._nid)
            except Exception:
                pass
            try:
                win32gui.DestroyWindow(self.hwnd)
            except Exception:
                pass

    def stop(self):
        try:
            win32gui.PostMessage(self.hwnd, win32con.WM_CLOSE, 0, 0)
        except Exception:
            pass

    # ---- 窗口过程 ----
    def _wndproc(self, hwnd, msg, wparam, lparam):
        if msg == WM_TRAY:
            ev = lparam & 0xFFFF
            if ev in (win32con.WM_LBUTTONUP, NIN_SELECT):
                self._emit("toggle")
                return 0
            if ev in (win32con.WM_RBUTTONUP, win32con.WM_CONTEXTMENU):
                self._show_menu()
                return 0
        elif msg == win32con.WM_COMMAND:
            cmd = self._cmd_of(low16(wparam))
            if cmd:                       # 未知命令不当作退出（曾默认返回 quit，导致误退出）
                self._emit(cmd)
            return 0
        elif msg == win32con.WM_DESTROY:
            win32gui.PostQuitMessage(0)
            return 0
        return win32gui.DefWindowProc(hwnd, msg, wparam, lparam)

    # ---- 菜单 ----
    def _cmd_of(self, low):
        for item in self.ITEMS:
            if item is None:      # 分割线：跳过，避免解包报错导致菜单命令失效
                continue
            _, _, cmd = item
            if _id_of(cmd) == low:
                return cmd
        return None           # 未知命令：忽略（绝不能默认成 quit）

    def _show_menu(self):
        hm = win32gui.CreatePopupMenu()
        self._keep_hbmp = []
        for item in self.ITEMS:
            if item is None:
                win32gui.AppendMenu(hm, win32con.MF_SEPARATOR, 0, "")
                continue
            text, icon, cmd = item
            item_id = _id_of(cmd)
            flags = win32con.MF_STRING
            if cmd in self.disabled:
                flags |= win32con.MF_GRAYED
            win32gui.AppendMenu(hm, flags, item_id, text)
            try:
                hb = _pil_to_hbitmap(icons.get(icon, MENU_ICON, color=(51, 64, 78, 255)),
                                     MENU_ICON)
                self._keep_hbmp.append(hb)
                self._set_item_bitmap(hm, item_id, hb)
            except Exception:
                pass   # 图标失败不阻塞菜单

        x, y = win32gui.GetCursorPos()
        win32gui.SetForegroundWindow(self.hwnd)
        chosen = win32gui.TrackPopupMenu(
            hm, win32con.TPM_RIGHTBUTTON | win32con.TPM_RETURNCMD | win32con.TPM_NONOTIFY,
            x, y, 0, self.hwnd, None)
        win32gui.DestroyMenu(hm)
        for hb in self._keep_hbmp:
            try:
                win32gui.DeleteObject(hb)
            except Exception:
                pass
        self._keep_hbmp = []
        if chosen:
            cmd = self._cmd_of(chosen)
            if cmd:
                self._emit(cmd)

    def _set_item_bitmap(self, hm, item_id, hbmp):
        mii = _MENUITEMINFO()
        mii.cbSize = ctypes.sizeof(_MENUITEMINFO)
        mii.fMask = MIIM_BITMAP
        mii.hbmpItem = hbmp
        _user32.SetMenuItemInfoW(hm, item_id, False, ctypes.byref(mii))

    def _emit(self, cmd):
        fn = self.on.get(cmd)
        if fn:
            fn()


def _id_of(cmd):
    return {"start": 1001, "stop": 1002, "watch": 1003, "quit": 1004}.get(cmd, 1004)


def low16(v):
    return v & 0xFFFF
