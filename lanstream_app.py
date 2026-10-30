# -*- coding: utf-8 -*-
"""月光极速电子教室 v1.0.0 —— 主界面（tkinter + 托盘 + 局域网检测 + 一键开播/收看）"""
import os
import sys
import queue
import time
import threading
import subprocess
import tkinter as tk
from tkinter import ttk, messagebox

if getattr(sys, "frozen", False):
    _here = os.path.dirname(sys.executable)
else:
    _here = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, _here)

import lanstream_core as core
import icons
from lanstream_ui import Tray

APP_TITLE = "月光极速电子教室"
APP_VERSION = "v1.2.0"
DEFAULT_HOST = "192.168.110.20"

# 窗口默认尺寸（启动时居中显示）
WIN_W, WIN_H = 800, 600
# 左右两张卡片共用的行高（保证"开播/停止"与"一键收看"水平对齐）
ROW_HEAD = 26     # 标题行（标题 + 状态徽章）
ROW_LAB = 20      # 说明文字行
ROW_MID = 38      # IP 输入 / IP 徽章 / 下拉设置行
BTN_H = 42        # 按钮高度
FONT_UI = "Microsoft YaHei UI"
# 码率(Mb/s) / 帧率(FPS) 下拉候选（控件可手动输入任意值）
BITRATE_CHOICES = ["5", "8", "10", "15", "20", "30", "50"]
FPS_CHOICES = ["30", "60", "75", "90", "120", "144"]

# 现代扁平化主题（对齐 HTML 设计稿：emerald / rose / blue / cyan）
BG        = "#f3f4f6"   # 窗口底色
PANEL     = "#f3f4f6"
PANEL2    = "#ffffff"   # 卡片底色
CARD_BRD  = "#e7edf5"   # 卡片描边
FG        = "#1f2937"
MUT       = "#9ca3af"
ACCENT    = "#3b82f6"   # 收看（blue-500）
GREEN     = "#10b981"   # 开播（emerald-500）
RED       = "#f43f5e"   # 停止（rose-500）
CYAN      = "#06b6d4"   # 检测（cyan-500）
GHOST     = "#f3f4f6"
GHOST_FG  = "#55677c"
GHOST_BRD = "#e5e7eb"
LOG_BG    = "#111827"   # 日志区：深色终端（gray-900）
LOG_FG    = "#34d399"   # 终端绿（emerald-400）
LOG_DIM   = "#6b7280"
DISABLED  = "#d1d5db"


def _shade(hexc, k):
    """扁平按钮 hover/pressed 用：按比例微调颜色深浅。"""
    r = min(255, int(int(hexc[1:3], 16) * k))
    g = min(255, int(int(hexc[3:5], 16) * k))
    b = min(255, int(int(hexc[5:7], 16) * k))
    return "#%02x%02x%02x" % (r, g, b)


class FlatButton(tk.Canvas):
    """现代扁平圆角按钮：Canvas 精确圆角（圆弧拼接）+ 图标 + 文本。
    hover/pressed 微深色，disabled 置灰。"""

    def __init__(self, master, text="", icon=None, color="#22c55e", fg="#ffffff",
                 bg=None, radius=0, height=42, width=1, font=None, command=None,
                 state="normal", ghost=False):
        tk.Canvas.__init__(self, master, bg=bg or BG, highlightthickness=0, bd=0,
                           height=height, width=width)
        self._bg_cache = {}
        self._ph_img = None
        self._text = text
        self._icon = icon
        self._color = color
        self._fg = fg
        self._radius = radius
        self._cmd = command
        self._font = font or ("Microsoft YaHei UI", 11, "bold")
        self._ghost = ghost
        self._hover = _shade(color, 0.92)
        self._pressed = _shade(color, 0.84)
        self._disabled = (state == "disabled")
        self._clickable = True
        self.bind("<Configure>", lambda e: self._paint(None))
        self.bind("<Enter>", lambda e: self._paint(self._hover))
        self.bind("<Leave>", lambda e: self._paint(None))
        self.bind("<Button-1>", self._on_down)
        self.bind("<ButtonRelease-1>", self._on_up)

    # ---- 状态 ----
    def set_state(self, state):
        self._disabled = (state == "disabled")
        self._paint(None)

    def set_text(self, text):
        self._text = text
        self._paint(None)

    def set_clickable(self, v):
        """保持颜色显示但禁用点击（如开播中“正在直播”按钮）。"""
        self._clickable = bool(v)
        self._paint(None)

    # ---- 绘制 ----
    def _paint(self, hover):
        self.delete("all")
        w, h = self.winfo_width(), self.winfo_height()
        if w <= 16 or h <= 8:
            return
        if self._disabled:
            col = DISABLED
        elif hover:
            col = hover
        elif self._ghost:
            col = GHOST
        else:
            col = self._color
        r = min(self._radius, h // 2, w // 4)
        ghost = self._ghost and not self._disabled
        key = (w, h, col, r, ghost)
        if key not in self._bg_cache:
            from PIL import Image, ImageDraw, ImageTk
            ss = 4
            im = Image.new("RGBA", (w * ss, h * ss), (0, 0, 0, 0))
            d = ImageDraw.Draw(im)
            if ghost:
                d.rectangle((ss, ss, w * ss - 1 - ss, h * ss - 1 - ss),
                            outline=GHOST_BRD, width=ss)
            else:
                d.rounded_rectangle((0, 0, w * ss - 1, h * ss - 1), radius=r * ss, fill=col)
            im = im.resize((w, h), Image.LANCZOS)
            self._bg_cache[key] = ImageTk.PhotoImage(im)
        self._ph_img = self._bg_cache[key]
        self.create_image(0, 0, anchor="nw", image=self._ph_img)
        # 图标 + 文字整体水平居中
        fg = self._fg if not ghost else GHOST_FG
        icon_w = self._icon.width() if self._icon is not None else 0
        gap = 8 if self._icon is not None else 0
        try:
            import tkinter.font as _tkfont
            tw = _tkfont.Font(font=self._font).measure(self._text)
        except Exception:
            tw = len(self._text) * 10
        total = icon_w + gap + tw
        x0 = (w - total) / 2
        if self._icon is not None:
            self.create_image(x0, h / 2, anchor="w", image=self._icon)
            x0 += icon_w + gap
        self.create_text(x0, h / 2, text=self._text, fill=fg, font=self._font, anchor="w")

    def _on_down(self, e):
        if self._disabled or not self._clickable:
            return
        self._paint(self._pressed)

    def _on_up(self, e):
        if self._disabled or not self._clickable:
            self._paint(None)
            return
        self._paint(None)
        if self._cmd:
            self._cmd()


class PillTag(tk.Canvas):
    """圆角胶囊徽章：可选左侧圆点（状态指示），如 HTML 的 rounded-lg 徽章。"""

    def __init__(self, master, text="", fill="#f3f4f6", fg="#374151",
                 dot=None, height=26, font=None):
        self._font = font or ("Microsoft YaHei UI", 10)
        self._text = text
        self._dot = dot
        tk.Canvas.__init__(self, master, bg=BG, highlightthickness=0, bd=0,
                           height=height, width=self._calc_w())
        self._fill = fill
        self._fg = fg
        self.bind("<Configure>", lambda e: self._paint())

    def _calc_w(self):
        try:
            import tkinter.font as _tkfont
            tw = _tkfont.Font(font=self._font).measure(self._text)
        except Exception:
            tw = len(self._text) * 9
        return tw + 24 + (14 if self._dot is not None else 0)

    def set(self, text=None, fill=None, fg=None, dot=None):
        if text is not None:
            self._text = text
            self.configure(width=self._calc_w())     # 文字变化时同步胶囊宽度
        if fill is not None:
            self._fill = fill
        if fg is not None:
            self._fg = fg
        if dot is not None:
            self._dot = dot
        self._paint()

    def _paint(self):
        self.delete("all")
        w, h = self.winfo_width(), self.winfo_height()
        if w <= 10 or h <= 6:
            return
        r = 0          # 扁平化：徽章也采用直角
        # 背景绘制（按尺寸/颜色缓存）
        cache = getattr(self, "_bg_cache", None)
        if cache is None:
            cache = {}
            self._bg_cache = cache
        key = (w, h, self._fill)
        if key not in cache:
            from PIL import Image, ImageDraw, ImageTk
            ss = 4
            im = Image.new("RGBA", (w * ss, h * ss), (0, 0, 0, 0))
            d = ImageDraw.Draw(im)
            d.rectangle((0, 0, w * ss - 1, h * ss - 1), fill=self._fill)
            im = im.resize((w, h), Image.LANCZOS)
            cache[key] = ImageTk.PhotoImage(im)
        self.create_image(0, 0, anchor="nw", image=cache[key])
        # 内容：圆点在左、文字在右，整体水平居中（不再重叠）
        try:
            import tkinter.font as _tkfont
            tw = _tkfont.Font(font=self._font).measure(self._text)
        except Exception:
            tw = len(self._text) * 9
        dw = 6 if self._dot is not None else 0
        gap = 5 if self._dot is not None else 0
        total = dw + gap + tw
        x0 = max(10, (w - total) / 2)
        cy = h / 2
        if self._dot is not None:
            self.create_oval(x0, cy - 3, x0 + dw, cy + 3, fill=self._dot, outline="")
            x0 += dw + gap
        self.create_text(x0, cy, text=self._text, fill=self._fg, font=self._font,
                         anchor="w")


class ToggleSwitch(tk.Canvas):
    """开关控件：圆角轨道 + 白色滑块（PIL 抗锯齿），点击切换。"""

    def __init__(self, master, on=False, width=46, height=24, color=ACCENT,
                 command=None):
        tk.Canvas.__init__(self, master, bg=PANEL2, highlightthickness=0, bd=0,
                           width=width, height=height)
        self._bw, self._bh = width, height
        self._on = bool(on)
        self._color = color
        self._cmd = command
        self.bind("<Button-1>", self._click)
        self.bind("<Configure>", lambda e: self._paint())

    def get(self):
        return self._on

    def set(self, v, notify=False):
        v = bool(v)
        if v == self._on:
            return
        self._on = v
        self._paint()
        if notify and self._cmd:
            self._cmd(self._on)

    def _click(self, _e=None):
        self._on = not self._on
        self._paint()
        if self._cmd:
            self._cmd(self._on)

    def _paint(self):
        self.delete("all")
        w, h = self._bw, self._bh
        r = h // 2
        track = self._color if self._on else "#d1d5db"
        cache = getattr(self, "_cache", None)
        if cache is None:
            cache = {}
            self._cache = cache
        key = (w, h, track)
        if key not in cache:
            from PIL import Image, ImageDraw, ImageTk
            ss = 4
            im = Image.new("RGBA", (w * ss, h * ss), (0, 0, 0, 0))
            d = ImageDraw.Draw(im)
            d.rounded_rectangle((0, 0, w * ss - 1, h * ss - 1), radius=r * ss, fill=track)
            im = im.resize((w, h), Image.LANCZOS)
            cache[key] = ImageTk.PhotoImage(im)
        self.create_image(0, 0, anchor="nw", image=cache[key])
        pad = 3
        d2 = h - 2 * pad
        x0 = w - pad - d2 if self._on else pad
        self.create_oval(x0, pad, x0 + d2, pad + d2, fill="#ffffff", outline="")


class StatusProgress(tk.Frame):
    """状态栏内的扁平进度条：细长轨道 + 实心进度 + 右侧百分比/说明文字。"""

    def __init__(self, master, width=170, color=GREEN):
        tk.Frame.__init__(self, master, bg=BG)
        self._bar_w = width        # 注意：不能用 self._w，那是 Tk 控件的内部路径字段
        self._color = color
        self._cv = tk.Canvas(self, width=width, height=6, bg=BG,
                             highlightthickness=0, bd=0)
        self._cv.pack(side="left")
        self._lb = tk.Label(self, text="", bg=BG, fg=MUT, font=(FONT_UI, 9))
        self._lb.pack(side="left", padx=(8, 0))
        self.set(0, "正在开播…")

    def set(self, pct, text=""):
        pct = max(0, min(100, int(pct)))
        self._lb.configure(text=text or ("%d%%" % pct))
        cv = self._cv
        cv.delete("all")
        w, h = self._bar_w, 6
        cv.create_rectangle(0, 0, w, h, fill="#e5e7eb", outline="")
        fw = int(round(w * pct / 100.0))
        if fw > 0:
            cv.create_rectangle(0, 0, fw, h, fill=self._color, outline="")


class Card(tk.Frame):
    """扁平化卡片：纯白底 + 1px 浅描边，直角无圆角、无阴影（现代扁平风格）。
    inner 为普通 Frame（标准 grid/pack 布局）。"""

    def __init__(self, master, pad=18):
        tk.Frame.__init__(self, master, bg=PANEL2, bd=0,
                          highlightthickness=1,
                          highlightbackground=CARD_BRD, highlightcolor=CARD_BRD)
        self.inner = tk.Frame(self, bg=PANEL2)
        self.inner.pack(fill="both", expand=True, padx=pad, pady=pad)


# 兼容旧名
RoundedCard = Card


def _relaunch_as_admin():
    import ctypes
    if getattr(sys, "frozen", False):
        exe, params = sys.executable, " ".join(sys.argv[1:])
    else:
        exe, params = sys.executable, '"%s"' % os.path.abspath(__file__)
    try:
        ctypes.windll.shell32.ShellExecuteW(None, "runas", exe, params, None, 1)
    except Exception:
        pass


class App:
    def __init__(self, root):
        self.root = root
        root.title(APP_TITLE)
        root.minsize(720, 560)
        root.configure(bg=BG)
        self._center_window(WIN_W, WIN_H)
        try:
            root.tk.call("tk", "scaling", 1.25)
        except Exception:
            pass

        self.q = queue.Queue()
        self.state = core.load_state()
        self._icons = self._load_icons()
        self._app_icon()

        self._build_style()
        self._build_header()
        self._build_statusbar()
        self._build_body()

        # 托盘（常驻）
        self._tray = None
        self._start_tray()

        # 单实例：监听"用户再次双击图标"的唤醒事件
        self._start_activate_watcher()

        # 首次启动后台释放内置 sunshine/client（仅首启；后续缓存存在则秒过）
        threading.Thread(target=core._ensure_runtimes, daemon=True).start()

        # 状态轮询 + 事件队列
        self._refresh_status()
        self.root.after(200, self._poll)
        self.root.after(1200, self._start_polling)
        # 首次显示后把当前全部设置写入配置文件（默认值也会被记录）
        self.root.after(300, self._save_settings)
        # 开机自动收看：延迟启动，等运行时释放与界面就绪后再连
        if self.state.get("autowatch"):
            self.root.after(3500, self._auto_watch)

    # ---------- 窗口位置 ----------
    def _center_window(self, w, h):
        """按屏幕/多显示器工作区居中（略偏上，视觉更稳）。"""
        try:
            sw = self.root.winfo_screenwidth()
            sh = self.root.winfo_screenheight()
        except Exception:
            self.root.geometry("%dx%d" % (w, h))
            return
        x = max(0, (sw - w) // 2)
        y = max(0, (sh - h) // 2 - 24)
        self.root.geometry("%dx%d+%d+%d" % (w, h, x, y))

    # ---------- 图标 ----------
    def _load_icons(self, big=14, small=12):
        from PIL import ImageTk
        d = {"app": ImageTk.PhotoImage(icons.load_app_icon(28))}   # 顶部标题图标：icon.ico
        for name, size in [("play", big), ("stop", big), ("watch", big),
                           ("scan", small)]:
            d[name] = ImageTk.PhotoImage(icons.get(name, size))
        return d

    def _app_icon(self):
        """窗口/任务栏图标用项目内置 icon.ico（用户指定）。"""
        from PIL import ImageTk
        ico_path = None
        for b in (getattr(sys, "_MEIPASS", ""), _here, os.getcwd()):
            if b:
                p = os.path.join(b, "icon.ico")
                if os.path.exists(p):
                    ico_path = p
                    break
        if ico_path:
            try:
                self.root.iconbitmap(ico_path)
            except Exception:
                pass
        try:
            self._tk_app_icon = ImageTk.PhotoImage(icons.load_app_icon(64))
            self.root.iconphoto(True, self._tk_app_icon)
        except Exception:
            pass

    # ---------- 样式 ----------
    def _build_style(self):
        s = ttk.Style(self.root)
        s.theme_use("clam")
        s.configure(".", background=BG, foreground=FG, font=("Microsoft YaHei UI", 10))
        s.configure("Title.TLabel", font=("Microsoft YaHei UI", 15, "bold"),
                    foreground=FG, background=BG)
        s.configure("Ver.TLabel", font=("Microsoft YaHei UI", 8), foreground=MUT, background=BG)
        s.configure("Card.TFrame", background=PANEL2)
        s.configure("CardT.TLabel", background=PANEL2, foreground=FG,
                    font=("Microsoft YaHei UI", 11, "bold"))
        s.configure("CardDim.TLabel", background=PANEL2, foreground=MUT,
                    font=("Microsoft YaHei UI", 9))
        # 扁平化输入框：无深色高光，浅描边 + 白底
        s.configure("TCombobox", fieldbackground="#ffffff", background="#ffffff", foreground=FG,
                    arrowcolor=ACCENT, bordercolor=CARD_BRD, lightcolor="#ffffff",
                    darkcolor="#dde6ef", padding=4)
        s.map("TCombobox", bordercolor=[("focus", ACCENT)])
        s.configure("Status.TLabel", background=PANEL2, foreground=FG,
                    font=("Microsoft YaHei UI", 10, "bold"))

    # ---------- 布局 ----------
    def _build_header(self):
        head = tk.Frame(self.root, bg=PANEL)
        head.pack(fill="x", padx=16, pady=(14, 8))
        icon = tk.Label(head, image=self._icons["app"], bg=PANEL)
        icon.pack(side="left", padx=(2, 10))
        ttk.Label(head, text=APP_TITLE, style="Title.TLabel").pack(side="left")
        ttk.Label(head, text=APP_VERSION, style="Ver.TLabel").pack(side="left", padx=(8, 0), pady=(12, 0))

    def _build_body(self):
        wrap = tk.Frame(self.root, bg=BG)
        wrap.pack(fill="both", expand=True, padx=16, pady=(8, 4))
        wrap.columnconfigure(0, weight=1, uniform="c")
        wrap.columnconfigure(1, weight=1, uniform="c")
        wrap.rowconfigure(0, weight=0)      # 卡片按内容自然高度
        wrap.rowconfigure(1, weight=1)      # 日志区自适应填充剩余
        self._wrap = wrap
        self._build_server(wrap)
        self._build_client(wrap)
        self._build_log(wrap)

    # ---- 画质设置（码率 / 帧率）----
    @staticmethod
    def _num_text(v, default):
        """把配置里的数值转成下拉框文本（去掉多余的 .0）。"""
        try:
            f = float(v)
        except Exception:
            return default
        return str(int(f)) if abs(f - int(f)) < 1e-6 else str(f)

    def _state_mbps(self):
        """读取配置里的码率（Mb/s）。
        优先用新键 bitrate_mbps；兼容历史版本 bitrate（旧版存 kbps，默认 80000），
        这类"旧默认值"归位到新的默认 10Mb/s，避免升级后误显示 80。"""
        try:
            return min(200.0, max(1.0, float(self.state.get("bitrate_mbps"))))
        except Exception:
            pass
        try:
            v = float(self.state.get("bitrate"))
        except Exception:
            return 10.0
        if v > 1000:                      # 旧版存的是 kbps
            v = v / 1000.0
        if abs(v - 80.0) < 0.01 or v > 200:   # 旧默认值 / 越界值 → 新默认
            return 10.0
        return min(200.0, max(1.0, v))

    def _quality(self):
        """读取界面上的码率(Mb/s)与帧率(FPS)，非法输入回退默认（10 / 60）。"""
        raw = str(self.bit_var.get()).strip().lower()
        for unit in ("mb/s", "mbps", "m", "兆"):
            raw = raw.replace(unit, "")
        try:
            mb = float(raw)
        except Exception:
            mb = 10.0
        mb = min(200.0, max(1.0, mb))
        raw2 = str(self.fps_var.get()).strip().lower().replace("fps", "").replace("帧", "")
        try:
            fps = int(round(float(raw2)))
        except Exception:
            fps = 60
        fps = min(240, max(15, fps))
        self.bit_var.set(self._num_text(mb, "10"))   # 回写规范文本
        self.fps_var.set(str(fps))
        return mb, fps

    # ---- 配置持久化：界面上所有设置都写回配置文件 ----
    def _save_settings(self, *_):
        """把界面上的全部设置写入配置文件 lanstream_state.json：
        目标主机、主机历史、码率、帧率、开机自动收看。"""
        try:
            mb, fps = self._quality()
        except Exception:
            mb, fps = self._state_mbps(), 60
        host = (self.host_var.get() or "").strip()
        if host:
            self.state["host"] = host
        self.state["bitrate_mbps"] = mb
        self.state["bitrate"] = int(mb * 1000)
        self.state["fps"] = int(fps)
        try:
            self.state["autowatch"] = bool(self.sw_auto.get())
        except Exception:
            pass
        try:
            vals = [v for v in self.host_cb["values"] if v]
            if host and host not in vals:
                vals.insert(0, host)
            self.state["hosts"] = list(dict.fromkeys(vals))[:20]
        except Exception:
            pass
        try:
            core._save_state(self.state)
        except Exception:
            pass

    # ---- 固定行高容器：左右卡片行高一致，保证按钮水平对齐 ----
    @staticmethod
    def _fixed(parent, h):
        f = tk.Frame(parent, bg=PANEL2, height=h)
        f.grid_propagate(False)
        f.pack_propagate(False)
        return f

    def _build_server(self, wrap):
        card = Card(wrap)
        card.grid(row=0, column=0, sticky="nsew", padx=(0, 6))
        inner = card.inner
        inner.columnconfigure(0, weight=1)

        # row0 标题行：左"一键开播"，右状态徽章
        head = self._fixed(inner, ROW_HEAD)
        head.grid(row=0, column=0, sticky="ew", padx=16, pady=(14, 10))
        ttk.Label(head, text="一键开播", style="CardDim.TLabel",
                  font=(FONT_UI, 9, "bold")).pack(side="left", pady=(2, 0))
        self.srv = PillTag(head, text="未开播", fill="#f3f4f6",
                           fg="#6b7280", dot="#9ca3af")
        self.srv.pack(side="right")

        # row1 说明行
        lab = self._fixed(inner, ROW_LAB)
        lab.grid(row=1, column=0, sticky="ew", padx=16)
        ttk.Label(lab, text="本机 IP", style="CardDim.TLabel",
                  font=(FONT_UI, 10, "bold")).pack(side="left")

        # row2 信息行（与右侧 IP 输入框同高，底部对齐）
        mid = self._fixed(inner, ROW_MID)
        mid.grid(row=2, column=0, sticky="ew", padx=16, pady=(6, 12))
        self.srv_ip = PillTag(mid, text="—", fill="#f3f4f6", fg="#4b5563",
                              font=("Consolas", 10), height=24)
        self.srv_ip.place(x=0, rely=0.5, anchor="w")

        # row3/row4 画质设置：码率 / 帧率（下拉可选 + 可手动输入）
        slab = self._fixed(inner, ROW_LAB)
        slab.grid(row=3, column=0, sticky="ew", padx=16)
        slab.columnconfigure(0, weight=1, uniform="s")
        slab.columnconfigure(1, weight=1, uniform="s")
        ttk.Label(slab, text="码率 (Mb/s)", style="CardDim.TLabel",
                  font=(FONT_UI, 10, "bold")).grid(row=0, column=0, sticky="w")
        ttk.Label(slab, text="帧率 (FPS)", style="CardDim.TLabel",
                  font=(FONT_UI, 10, "bold")).grid(row=0, column=1, sticky="w", padx=(6, 0))

        setrow = self._fixed(inner, ROW_MID)
        setrow.grid(row=4, column=0, sticky="ew", padx=16, pady=(6, 12))
        setrow.columnconfigure(0, weight=1, uniform="s")
        setrow.columnconfigure(1, weight=1, uniform="s")
        setrow.rowconfigure(0, weight=1)
        self.bit_var = tk.StringVar(value=self._num_text(self._state_mbps(), "10"))
        self.cb_bit = ttk.Combobox(setrow, textvariable=self.bit_var,
                                   font=("Consolas", 11), values=BITRATE_CHOICES,
                                   height=8)
        self.cb_bit.grid(row=0, column=0, sticky="nsew", padx=(0, 6))
        self.fps_var = tk.StringVar(value=self._num_text(self.state.get("fps"), "60"))
        self.cb_fps = ttk.Combobox(setrow, textvariable=self.fps_var,
                                   font=("Consolas", 11), values=FPS_CHOICES,
                                   height=8)
        self.cb_fps.grid(row=0, column=1, sticky="nsew")
        for _cb in (self.cb_bit, self.cb_fps):
            for _ev in ("<<ComboboxSelected>>", "<FocusOut>", "<Return>"):
                _cb.bind(_ev, self._save_settings)

        # row5 按钮行（与右侧"一键收看"同一行高）
        btns = tk.Frame(inner, bg=PANEL2)
        btns.grid(row=5, column=0, sticky="ew", padx=16, pady=(4, 16))
        btns.columnconfigure(0, weight=1, uniform="b")
        btns.columnconfigure(1, weight=1, uniform="b")
        self.btn_start = FlatButton(btns, text="一键开播", icon=self._icons["play"],
                                    color=GREEN, height=BTN_H, command=self.do_start)
        self.btn_start.grid(row=0, column=0, sticky="ew", padx=(0, 6))
        self.btn_stop = FlatButton(btns, text="停止", icon=self._icons["stop"],
                                   color=RED, height=BTN_H, command=self.do_stop,
                                   state="disabled")
        self.btn_stop.grid(row=0, column=1, sticky="ew")

    def _build_client(self, wrap):
        card = Card(wrap)
        card.grid(row=0, column=1, sticky="nsew", padx=(6, 0))
        inner = card.inner
        inner.columnconfigure(0, weight=1)

        head = self._fixed(inner, ROW_HEAD)
        head.grid(row=0, column=0, sticky="ew", padx=16, pady=(14, 10))
        ttk.Label(head, text="一键收看", style="CardDim.TLabel",
                  font=(FONT_UI, 9, "bold")).pack(side="left", pady=(2, 0))

        lab = self._fixed(inner, ROW_LAB)
        lab.grid(row=1, column=0, sticky="ew", padx=16)
        ttk.Label(lab, text="目标主机 IP", style="CardDim.TLabel",
                  font=(FONT_UI, 10, "bold")).pack(side="left")

        mid = self._fixed(inner, ROW_MID)
        mid.grid(row=2, column=0, sticky="ew", padx=16, pady=(6, 12))
        mid.columnconfigure(0, weight=1)
        mid.rowconfigure(0, weight=1)
        self.host_var = tk.StringVar(value=self.state.get("host") or DEFAULT_HOST)
        hist = [h for h in (self.state.get("hosts") or []) if isinstance(h, str)]
        vals = list(dict.fromkeys([DEFAULT_HOST] + core.lan_ips() + hist))
        self.host_cb = ttk.Combobox(mid, textvariable=self.host_var,
                                    font=("Consolas", 11), values=vals, height=8)
        self.host_cb.grid(row=0, column=0, sticky="nsew")
        for _ev in ("<<ComboboxSelected>>", "<FocusOut>", "<Return>"):
            self.host_cb.bind(_ev, self._save_settings)
        self.btn_scan = FlatButton(mid, text="检测", icon=self._icons["scan"],
                                   color=CYAN, height=ROW_MID, width=86,
                                   font=(FONT_UI, 10, "bold"), command=self.do_scan)
        self.btn_scan.grid(row=0, column=1, sticky="nsew", padx=(6, 0))

        # row3/row4 与左侧画质设置行同高：开机自动收看开关
        lab3 = self._fixed(inner, ROW_LAB)
        lab3.grid(row=3, column=0, sticky="ew", padx=16)
        ttk.Label(lab3, text="开机自动收看", style="CardDim.TLabel",
                  font=(FONT_UI, 10, "bold")).pack(side="left")
        auto = self._fixed(inner, ROW_MID)
        auto.grid(row=4, column=0, sticky="ew", padx=16, pady=(6, 12))
        tk.Label(auto, text="开启后启动软件即自动连接并收看",
                 bg=PANEL2, fg=MUT, font=(FONT_UI, 9), anchor="w").pack(
            side="left", fill="both", expand=True)
        self.sw_auto = ToggleSwitch(auto, on=bool(self.state.get("autowatch")),
                                    color=ACCENT, command=self._on_autowatch)
        self.sw_auto.pack(side="right", pady=(ROW_MID - 24) // 2 - 6)

        # row5 按钮行
        btns = tk.Frame(inner, bg=PANEL2)
        btns.grid(row=5, column=0, sticky="ew", padx=16, pady=(4, 16))
        btns.columnconfigure(0, weight=1)
        self.btn_watch = FlatButton(btns, text="一键收看", icon=self._icons["watch"],
                                    color=ACCENT, height=BTN_H, command=self.do_watch)
        self.btn_watch.grid(row=0, column=0, sticky="ew")

    def _build_log(self, wrap):
        box = tk.Frame(wrap, bg=LOG_BG)
        box.grid(row=1, column=0, columnspan=2, sticky="nsew", pady=(10, 0))
        box.columnconfigure(0, weight=1)
        box.rowconfigure(1, weight=1)          # 日志 Text 填充剩余高度
        bar = tk.Frame(box, bg=LOG_BG)
        bar.grid(row=0, column=0, sticky="ew")
        bar.columnconfigure(0, weight=1)
        # 用 tk.Label（bg 与 bar 同色）避免 ttk 主题白底块
        tk.Label(bar, text="系统日志", bg=LOG_BG, fg=LOG_DIM,
                 font=(FONT_UI, 10, "bold")).grid(
            row=0, column=0, sticky="w", padx=14, pady=(8, 4))
        tk.Label(bar, text="CONSOLE ACTIVE", bg=LOG_BG, fg=LOG_DIM,
                 font=("Consolas", 8)).grid(
            row=0, column=1, sticky="e", padx=14, pady=(8, 4))
        self.log = tk.Text(box, height=5, bg=LOG_BG, fg=LOG_FG, relief="flat",
                           state="disabled", font=(FONT_UI, 10), wrap="word",
                           insertbackground=LOG_FG, padx=14, pady=6,
                           borderwidth=0, highlightthickness=0,
                           spacing1=2, spacing3=2)
        self.log.grid(row=1, column=0, sticky="nsew")
        self._log("就绪 · 系统托盘常驻，单击呼出/最小化，右键更多操作")

    def _build_statusbar(self):
        """窗口底部状态栏：左侧运行状态，右侧本机 IP 与版本。"""
        outer = tk.Frame(self.root, bg=BG)
        outer.pack(side="bottom", fill="x", padx=16, pady=(2, 8))
        tk.Frame(outer, bg="#e5e7eb", height=1).pack(side="top", fill="x")
        row = tk.Frame(outer, bg=BG)
        row.pack(side="top", fill="x", pady=(6, 0))
        row.columnconfigure(0, weight=1)
        self.sb_state = tk.Label(row, text="● 未开播", bg=BG, fg=MUT,
                                 font=(FONT_UI, 9))
        self.sb_state.grid(row=0, column=0, sticky="w")
        self.sb_info = tk.Label(row, text="本机 IP —  ·  " + APP_VERSION, bg=BG,
                                fg=MUT, font=(FONT_UI, 9))
        self.sb_info.grid(row=0, column=0, sticky="e")
        # 进度条用 place 叠在整行正中（不受左右文字宽度影响）
        self.sb_prog = StatusProgress(row, width=170)
        self._prog_visible = False          # 进度条默认隐藏，开播时才出现

    # ---------- 日志 / 事件 ----------
    def _log(self, s):
        self.log.configure(state="normal")
        self.log.insert("end", "> " + s + "\n")
        self.log.see("end")
        self.log.configure(state="disabled")

    def _emit(self, fn, *a):
        self.q.put((fn, a))

    def _poll(self):
        try:
            while True:
                fn, a = self.q.get_nowait()
                fn(*a)
        except queue.Empty:
            pass
        self.root.after(150, self._poll)

    def _start_polling(self):
        self._refresh_status()
        self.root.after(2000, self._start_polling)

    # ---------- 状态栏进度条 ----------
    def _progress_show(self, pct=0, text="正在开播…"):
        # 先更新内容再摆放，保证 place 用的是最新尺寸（居中才准）
        self.sb_prog.set(pct, text or ("%d%%" % int(pct)))
        if not self._prog_visible:
            self.sb_prog.place(relx=0.5, rely=0.5, anchor="center")
            self._prog_visible = True
        try:
            self.sb_state.configure(text="● 正在开播…", fg=GREEN)
        except Exception:
            pass

    def _progress_hide(self):
        if self._prog_visible:
            self.sb_prog.place_forget()
            self._prog_visible = False

    def _progress_done(self, text="开播完成"):
        self._progress_show(100, text)
        self.root.after(900, self._progress_hide)

    def _progress_fail(self, text="开播失败"):
        try:
            self.sb_state.configure(text="● " + text, fg=RED)
        except Exception:
            pass
        self.root.after(1600, self._progress_hide)

    def _refresh_status(self):
        st = core.server_status()
        clients = int(st.get("clients") or 0)
        if st["streaming"]:
            self.srv.set(text="正在直播", fill="#ffe4e6", fg="#e11d48", dot="#f43f5e")
            sb_txt = ("● 正在直播 · 已连接 %d 台客户端" % clients) if clients else "● 正在直播"
            sb_fg = "#f43f5e"
        elif st["running"]:
            self.srv.set(text="已开播", fill="#ecfdf5", fg="#047857", dot="#10b981")
            sb_txt, sb_fg = "● 已开播，等待学生端连接", "#10b981"
        else:
            self.srv.set(text="未开播", fill="#f3f4f6", fg="#6b7280", dot="#9ca3af")
            sb_txt, sb_fg = "● 未开播", MUT
        if st["running"]:
            self.btn_start.set_text("正在直播")
            self.btn_start.set_clickable(False)
        else:
            self.btn_start.set_text("一键开播")
            self.btn_start.set_clickable(True)
        self.btn_start.set_text("直播中 · %d 台" % clients if (st["running"] and clients) else
                                ("正在直播" if st["running"] else "一键开播"))
        self.btn_stop.set_state("normal" if st["running"] else "disabled")
        ips = core.lan_ips()
        self.srv_ip.set(text="、".join(ips) if ips else "—")
        try:
            if not self._prog_visible:      # 开播进度条显示时保留"正在开播…"文案
                self.sb_state.configure(text=sb_txt, fg=sb_fg)
            self.sb_info.configure(text="本机 IP %s  ·  %s" %
                                   ("、".join(ips) if ips else "—", APP_VERSION))
        except Exception:
            pass
        if self._tray:
            self._tray.update_disabled(() if st["running"] else ("stop",))

    # ---------- 动作 ----------
    def do_start(self):
        mb, fps = self._quality()
        self._save_settings()          # 开播前先落盘（码率/帧率/自动收看等全部设置）

        def work():
            self._emit(self._log, "正在开播…（码率 %s Mb/s · %d FPS）"
                       % (self._num_text(mb, "10"), fps))
            self._emit(self._set_btn, self.btn_start, False)
            self._emit(self._progress_show, 0, "正在开播…")

            def on_prog(pct, text=""):
                # 工作线程 → 主线程：进度条只在界面线程更新
                self._emit(self._progress_show, pct, text)

            try:
                res = core.start_server(bitrate_kbps=int(mb * 1000), fps=fps,
                                        on_progress=on_prog)
            except Exception as e:
                self._emit(self._log, "⚠ 开播异常：" + repr(e))
                self._emit(self._progress_fail)
                self._emit(self._set_btn, self.btn_start, True)
                self._emit(self._refresh_status)
                return
            core.ensure_firewall()
            if res["ok"]:
                self._emit(self._log, "✔ 已开播")
                self._emit(self._progress_done, "开播完成")
            else:
                self._emit(self._log, "⚠ " + res.get("msg", ""))
                self._emit(self._progress_fail)
            self._emit(self._refresh_status)
        threading.Thread(target=work, daemon=True).start()

    def _set_btn(self, w, on):
        w.set_state("normal" if on else "disabled")
        if hasattr(w, "set_clickable"):
            w.set_clickable(on)

    def _on_autowatch(self, v):
        """开机自动收看开关：立即写入配置文件（state.json）。"""
        self.state["autowatch"] = bool(v)
        self._save_settings()
        self._log("开机自动收看已%s（已保存到配置文件）" % ("开启" if v else "关闭"))

    def _auto_watch(self):
        """启动后自动连接并收看（仅在开关开启时）。"""
        if not getattr(self, "sw_auto", None) or not self.sw_auto.get():
            return
        host = (self.host_var.get() or "").strip() or DEFAULT_HOST
        self._log("开机自动收看：正在连接 %s …" % host)
        self.do_watch()

    def do_stop(self):
        def work():
            self._emit(self._log, "正在停止…")
            try:
                core.stop_server()
                self._emit(self._log, "已停止")
            except Exception as e:
                self._emit(self._log, "⚠ 停止异常：" + repr(e))
            self._emit(self._refresh_status)
        threading.Thread(target=work, daemon=True).start()

    def do_scan(self):
        def work():
            self._emit(self._log, "正在检测局域网已开播主机…")
            try:
                found = core.scan_lan_hosts()
            except Exception as e:
                found = []
                self._emit(self._log, "⚠ 检测异常：" + repr(e))
            vals = list(dict.fromkeys([DEFAULT_HOST] + core.lan_ips() + found))
            self._emit(self._set_hosts, vals)
            self._emit(self._log, "检测完成：" + ("、".join(found) if found else "未发现其他开播主机"))
        threading.Thread(target=work, daemon=True).start()

    def _set_hosts(self, vals):
        cur = self.host_var.get()
        self.host_cb["values"] = vals
        if cur not in vals:
            self.host_cb["values"] = list(dict.fromkeys(vals + [cur]))
        self._save_settings()          # 主机列表持久化，下次启动仍可下拉选择

    def do_watch(self):
        host = self.host_var.get().strip() or DEFAULT_HOST
        self.state["host"] = host
        mb, fps = self._quality()          # 收看沿用开播区的码率/帧率设置
        self._save_settings()

        def work():
            self._emit(self._log, "正在连接 %s …（码率 %s Mb/s · %d FPS）"
                       % (host, self._num_text(mb, "10"), fps))
            # 观看本机时确保服务端已开播
            if host in core.lan_ips() or host in ("127.0.0.1", "localhost"):
                if not core.server_status()["running"]:
                    self._emit(self._log, "本机未开播，先启动…")
                    core.start_server(bitrate_kbps=int(mb * 1000), fps=fps)
            res = core.watch(host, bitrate_kbps=int(mb * 1000), fps=fps)
            self._emit(self._log, "✔ 已发起收看 %s" % host if res["ok"] else "⚠ " + res.get("msg", ""))
            self._emit(self._refresh_status)
        threading.Thread(target=work, daemon=True).start()

    def toggle_input(self):
        # 学生端键鼠控制已移除：永久禁用，无操作
        return

    def _save_host(self, host):
        self.state["host"] = host
        core._save_state(self.state)

    def _self_default(self):
        return DEFAULT_HOST

    # ---------- 托盘 ----------
    def _start_tray(self):
        on = {
            "toggle": lambda: self._emit(self._toggle_window),
            "start": lambda: self._emit(self.do_start),
            "stop": lambda: self._emit(self.do_stop),
            "watch": lambda: self._emit(self.do_watch),
            "quit": lambda: self._emit(self.quit),
        }
        self._tray = Tray(on, disabled=("stop",))
        self._tray.start()

    # ---------- 单实例唤醒 ----------
    def _start_activate_watcher(self):
        """后台等待唤醒事件：另一份实例启动时会 SetEvent，这里把窗口弹到最前。"""
        h = globals().get("_ACTIVATE_EVENT")
        if not h:
            return

        def loop():
            k = _kernel32()
            while True:
                try:
                    r = k.WaitForSingleObject(h, 500)
                except Exception:
                    return
                if r == 0:                      # 被唤醒
                    try:
                        self._emit(self._raise_window)
                    except Exception:
                        return

        threading.Thread(target=loop, daemon=True, name="ActivateWatch").start()

    def _raise_window(self):
        """把主窗口还原、置前并获得焦点（托盘隐藏状态下也能拉出来）。"""
        try:
            self.root.deiconify()
            self.root.lift()
            self.root.focus_force()
        except Exception:
            pass
        try:
            import win32gui
            import win32con
            hwnd = int(self.root.wm_frame(), 16)
            win32gui.ShowWindow(hwnd, win32con.SW_RESTORE)
            try:
                win32gui.SetForegroundWindow(hwnd)
            except Exception:
                # 系统前台锁定：临时置顶一下再取消，等效于"真的跳到最前"
                win32gui.SetWindowPos(hwnd, win32con.HWND_TOPMOST, 0, 0, 0, 0,
                                      win32con.SWP_NOMOVE | win32con.SWP_NOSIZE)
                self.root.after(500, lambda: win32gui.SetWindowPos(
                    hwnd, win32con.HWND_NOTOPMOST, 0, 0, 0, 0,
                    win32con.SWP_NOMOVE | win32con.SWP_NOSIZE))
        except Exception:
            pass

    def _toggle_window(self):
        if self.root.state() == "withdrawn":
            self.root.deiconify()
            self.root.lift()
            self.root.focus_force()
        else:
            self.root.withdraw()

    def on_close(self):
        # 常驻托盘：关闭即最小化（同时保存全部设置）
        self._save_settings()
        self.root.withdraw()

    def quit(self):
        # 退出 App：同步杀掉 Sunshine（停播）与 Moonlight（停止收看），再结束本程序
        for exe in ("sunshine.exe", "Moonlight.exe"):
            try:
                core._sp_run(["taskkill", "/IM", exe, "/F", "/T"],
                             capture_output=True, timeout=15)
            except Exception:
                pass
        try:
            self._save_settings()
        except Exception:
            pass
        try:
            os._exit(0)
        except Exception:
            pass


def _selftest():
    """隐藏自检：LANSTREAM_SELFTEST=1 时，释放内置运行时并做开播/停止闭环，写日志后退出。"""
    def run():
        txt = ""
        try:
            sunshine = core.find_sunshine()
            client = core.find_moonlight()
            res = core.start_server()
            running = core.server_status()["running"]
            import subprocess as _sp
            kr = _sp.run(["taskkill", "/IM", "sunshine.exe", "/F", "/T"],
                         capture_output=True, text=True, errors="replace")
            _sp.run(["taskkill", "/IM", "Moonlight.exe", "/F", "/T"],
                    capture_output=True, text=True, errors="replace")
            time.sleep(2)
            stopped = not core.server_status()["running"]
            inp = core.input_enabled()
            txt = ("sunshine=%s\nclient=%s\nstart_ok=%s\nrunning=%s\nstopped=%s\n"
                   "input_default=%s\ntaskkill_rc=%s\ntaskkill_out=%s\n" %
                   (sunshine, client, res["ok"], running, stopped, inp,
                    kr.returncode, kr.stdout.strip()[-200:]))
        except Exception as e:
            import traceback
            txt = "ERROR:\n" + traceback.format_exc()
        try:
            p = os.path.join(core.app_data_dir(), "selftest.txt")
            with open(p, "w", encoding="utf-8") as f:
                f.write(txt)
        except Exception:
            pass
        os._exit(0)

    threading.Thread(target=run, daemon=True).start()


_SINGLE_MUTEX = None
_ACTIVATE_EVENT = None            # 单实例唤醒事件句柄（第一个实例持有）
_ACTIVATE_NAME = "Local\\MoonClassEduActivate"
_EVENT_MODIFY_STATE = 0x0002
_ERROR_ALREADY_EXISTS = 183


def _kernel32():
    import ctypes
    k = ctypes.WinDLL("kernel32", use_last_error=True)
    k.CreateMutexW.restype = ctypes.c_void_p
    k.CreateEventW.restype = ctypes.c_void_p
    k.OpenEventW.restype = ctypes.c_void_p
    return k


def _ensure_single_instance() -> bool:
    """单实例互斥：返回 False 表示已有实例在运行。
    同时创建"唤醒事件"（NULL DACL，允许任意权限进程 SetEvent），
    让重复打开的实例能请求第一个实例把窗口弹到前台。"""
    global _SINGLE_MUTEX, _ACTIVATE_EVENT
    try:
        import ctypes
        from ctypes import wintypes
        k = _kernel32()
        _SINGLE_MUTEX = k.CreateMutexW(None, False, "Local\\MoonClassEduApp")
        if k.GetLastError() == _ERROR_ALREADY_EXISTS:
            return False
        # 构造 NULL DACL 的安全描述符：非管理员（中等完整性）进程也能 OpenEvent/SetEvent，
        # 否则跨 UAC 完整性级别唤醒会被拒绝（"第二个实例唤不醒第一个"）。
        class _SD(ctypes.Structure):
            _fields_ = [("Revision", ctypes.c_ubyte), ("Sbz1", ctypes.c_ubyte),
                        ("Control", wintypes.WORD), ("Owner", ctypes.c_void_p),
                        ("Group", ctypes.c_void_p), ("Sacl", ctypes.c_void_p),
                        ("Dacl", ctypes.c_void_p)]

        class _SA(ctypes.Structure):
            _fields_ = [("nLength", wintypes.DWORD),
                        ("lpSecurityDescriptor", ctypes.c_void_p),
                        ("bInheritHandle", wintypes.BOOL)]

        advapi = ctypes.WinDLL("advapi32")
        sd = _SD()
        advapi.InitializeSecurityDescriptor(ctypes.byref(sd), 1)
        advapi.SetSecurityDescriptorDacl(ctypes.byref(sd), True, None, False)
        sa = _SA(ctypes.sizeof(_SA), ctypes.cast(ctypes.byref(sd), ctypes.c_void_p), False)
        _ACTIVATE_EVENT = k.CreateEventW(ctypes.byref(sa), False, False, _ACTIVATE_NAME)
        _ensure_single_instance._keep = (sd, sa)     # 防止被 GC 回收
        return True
    except Exception:
        return True


def _activate_running_instance() -> bool:
    """通知已运行实例把主窗口弹到前台（跨权限级别）。"""
    try:
        k = _kernel32()
        h = k.OpenEventW(_EVENT_MODIFY_STATE, False, _ACTIVATE_NAME)
        if not h:
            return False
        k.SetEvent(h)
        k.CloseHandle(h)
        return True
    except Exception:
        return False


def _activate_by_window() -> bool:
    """兜底：按窗口标题找到已运行实例的主窗口并还原/置前。"""
    ok = False
    try:
        import win32gui
        import win32con

        hits = []

        def _cb(hwnd, _):
            try:
                if APP_TITLE in win32gui.GetWindowText(hwnd):
                    hits.append(hwnd)
            except Exception:
                pass

        win32gui.EnumWindows(_cb, None)
        if hits:
            hwnd = hits[0]
            win32gui.ShowWindow(hwnd, win32con.SW_RESTORE)
            win32gui.SetForegroundWindow(hwnd)
            ok = True
    except Exception:
        pass
    return ok


def main():
    if os.environ.get("LANSTREAM_SELFTEST") == "1":
        _selftest()
        tk.Tk().mainloop()   # 挂起让自检线程执行完毕后 os._exit
        return
    if not _ensure_single_instance():
        # 已有实例在运行：不弹提示框，直接把那个程序的界面唤到最前面，然后静默退出
        if not _activate_running_instance():
            _activate_by_window()
        return
    if not core.is_admin():
        _relaunch_as_admin()
        return
    root = tk.Tk()
    app = App(root)
    root.protocol("WM_DELETE_WINDOW", app.on_close)
    root.mainloop()


if __name__ == "__main__":
    main()
