# -*- coding: utf-8 -*-
"""音频采集 / 电平监测 / 麦克风回放（局域网教学"麦克风混入串流"用）。

实现要点
--------
Sunshine 只捕获**系统播放设备**（WASAPI loopback），其协议里的 Mic 通道在 Windows 上未实现，
所以"教师讲话让学生听到"不能直接走麦克风上行通道。
这里用最朴素也最可靠的办法：用 winmm 采集麦克风，再实时回放到默认播放设备
（等价于系统"侦听此设备"，但完全在本程序内、可即时开关、不改注册表、不重启音频服务），
Sunshine 的 loopback 就会把这段声音一起串流出去，学生端即可听到。

同时把采集数据算成 RMS 电平，供界面上的音频指示器显示"有没有输入"。
"""
import ctypes
import math
import threading
import time

WHDR_DONE = 0x00000001
WAVE_MAPPER = -1
CALLBACK_NULL = 0x00000000

SAMPLE_RATE = 22050          # 语音够用，延迟与负载均衡
CHANNELS = 1
BITS = 16
BLOCK = int(SAMPLE_RATE * 0.08)      # 80ms 一块，延迟可控
BUF_BYTES = BLOCK * CHANNELS * BITS // 8


class _WAVEFORMATEX(ctypes.Structure):
    _fields_ = [("wFormatTag", ctypes.c_ushort), ("nChannels", ctypes.c_ushort),
                ("nSamplesPerSec", ctypes.c_uint), ("nAvgBytesPerSec", ctypes.c_uint),
                ("nBlockAlign", ctypes.c_ushort), ("wBitsPerSample", ctypes.c_ushort),
                ("cbSize", ctypes.c_ushort)]


class _WAVEHDR(ctypes.Structure):
    _fields_ = [("lpData", ctypes.c_void_p), ("dwBufferLength", ctypes.c_uint),
                ("dwBytesRecorded", ctypes.c_uint), ("dwUser", ctypes.c_size_t),
                ("dwFlags", ctypes.c_uint), ("dwLoops", ctypes.c_uint),
                ("lpNext", ctypes.c_void_p), ("reserved", ctypes.c_size_t)]


class _WAVEINCAPS(ctypes.Structure):
    _fields_ = [("wMid", ctypes.c_ushort), ("wPid", ctypes.c_ushort),
                ("vDriverVersion", ctypes.c_uint), ("szPname", ctypes.c_wchar * 32),
                ("dwFormats", ctypes.c_uint), ("wChannels", ctypes.c_ushort),
                ("wReserved1", ctypes.c_ushort)]


_winmm = None


def _mm():
    global _winmm
    if _winmm is None:
        _winmm = ctypes.WinDLL("winmm")
    return _winmm


def format_bytes():
    """WAVEFORMATEX 的字节串（waveInOpen / waveOutOpen 直接传指针）。"""
    fmt = _WAVEFORMATEX(wFormatTag=1, nChannels=CHANNELS, nSamplesPerSec=SAMPLE_RATE,
                        nAvgBytesPerSec=SAMPLE_RATE * CHANNELS * BITS // 8,
                        nBlockAlign=CHANNELS * BITS // 8, wBitsPerSample=BITS, cbSize=0)
    return fmt


def list_inputs():
    """枚举录音设备名（waveIn 顺序索引即设备号）。"""
    out = []
    try:
        mm = _mm()
        n = mm.waveInGetNumDevs()
        for i in range(n):
            caps = _WAVEINCAPS()
            if mm.waveInGetDevCapsW(i, ctypes.byref(caps), ctypes.sizeof(caps)) == 0:
                name = (caps.szPname or "").strip()
                if name:
                    out.append(name)
    except Exception:
        pass
    return out


def find_index(name):
    """按设备名找 waveIn 索引，找不到返回 0（默认设备）。"""
    names = list_inputs()
    for i, n in enumerate(names):
        if n == name:
            return i
    return 0


def default_input_name():
    names = list_inputs()
    return names[0] if names else ""


class MicMonitor:
    """麦克风采集：实时电平 + 可选回放到默认播放设备（混入串流音频）。"""

    def __init__(self):
        self._lock = threading.Lock()
        self._level = 0.0
        self._peak = 0.0
        self._hwi = None
        self._hwo = None
        self._in_hdrs = []
        self._in_bufs = []
        self._out_hdrs = []
        self._out_bufs = []
        self._thread = None
        self._running = False
        self._playback = False
        self._volume = 0.9
        self._dev_name = ""
        self.last_error = ""

    # ---- 状态 ----
    def level(self):
        """0.0 ~ 1.0 的输入电平。读取本身无副作用（衰减放在采集侧），
        这样界面刷新频率不会影响读数。"""
        with self._lock:
            return min(1.0, self._level)

    def is_running(self):
        return self._running

    def device(self):
        return self._dev_name

    def playback(self):
        return self._playback

    # ---- 启停 ----
    def start(self, device_name="", playback=False, volume=0.9):
        self.stop()
        self.last_error = ""
        self._playback = bool(playback)
        self._volume = max(0.0, min(1.0, float(volume)))
        self._dev_name = device_name or ""
        try:
            mm = _mm()
            fmt = format_bytes()
            idx = find_index(device_name)
            self._hwi = ctypes.c_void_p()
            rc = mm.waveInOpen(ctypes.byref(self._hwi), idx, ctypes.byref(fmt),
                               0, 0, CALLBACK_NULL)
            if rc != 0:
                self.last_error = "waveInOpen rc=%d" % rc
                self._hwi = None
                return False

            if self._playback:
                self._hwo = ctypes.c_void_p()
                rc = mm.waveOutOpen(ctypes.byref(self._hwo), WAVE_MAPPER,
                                    ctypes.byref(fmt), 0, 0, CALLBACK_NULL)
                if rc != 0:
                    self.last_error = "waveOutOpen rc=%d" % rc
                    self._hwo = None
                    self._playback = False        # 回放失败仍然只做电平显示

            # 采集缓冲（2 块交替）
            self._in_hdrs, self._in_bufs = [], []
            for _ in range(2):
                buf = ctypes.create_string_buffer(BUF_BYTES)
                hdr = _WAVEHDR(lpData=ctypes.cast(buf, ctypes.c_void_p),
                               dwBufferLength=BUF_BYTES)
                mm.waveInPrepareHeader(self._hwi, ctypes.byref(hdr), ctypes.sizeof(hdr))
                mm.waveInAddBuffer(self._hwi, ctypes.byref(hdr), ctypes.sizeof(hdr))
                self._in_hdrs.append(hdr)
                self._in_bufs.append(buf)

            if self._hwo is not None:
                self._out_hdrs, self._out_bufs = [], []
                for _ in range(2):
                    buf = ctypes.create_string_buffer(BUF_BYTES)
                    hdr = _WAVEHDR(lpData=ctypes.cast(buf, ctypes.c_void_p),
                                   dwBufferLength=BUF_BYTES)
                    mm.waveOutPrepareHeader(self._hwo, ctypes.byref(hdr), ctypes.sizeof(hdr))
                    self._out_hdrs.append(hdr)
                    self._out_bufs.append(buf)

            mm.waveInStart(self._hwi)
            self._running = True
            self._thread = threading.Thread(target=self._loop, daemon=True,
                                            name="MicMonitor")
            self._thread.start()
            return True
        except Exception as e:
            self.last_error = repr(e)
            self.stop()
            return False

    def stop(self):
        self._running = False
        t = self._thread
        if t is not None and t.is_alive():
            try:
                t.join(timeout=0.6)
            except Exception:
                pass
        self._thread = None
        mm = _mm()
        for hdr, hwi in [(h, self._hwi) for h in self._in_hdrs]:
            try:
                if hwi:
                    mm.waveInUnprepareHeader(hwi, ctypes.byref(hdr), ctypes.sizeof(hdr))
            except Exception:
                pass
        for hdr in self._out_hdrs:
            try:
                if self._hwo:
                    mm.waveOutUnprepareHeader(self._hwo, ctypes.byref(hdr), ctypes.sizeof(hdr))
            except Exception:
                pass
        try:
            if self._hwi:
                mm.waveInStop(self._hwi)
                mm.waveInReset(self._hwi)
                mm.waveInClose(self._hwi)
        except Exception:
            pass
        try:
            if self._hwo:
                mm.waveOutReset(self._hwo)
                mm.waveOutClose(self._hwo)
        except Exception:
            pass
        self._hwi = self._hwo = None
        self._in_hdrs, self._in_bufs, self._out_hdrs, self._out_bufs = [], [], [], []
        with self._lock:
            self._level = 0.0
        self._running = False

    # ---- 采集循环 ----
    def _loop(self):
        mm = _mm()
        oi = 0
        while self._running:
            done = False
            for i, hdr in enumerate(self._in_hdrs):
                if not (hdr.dwFlags & WHDR_DONE):
                    continue
                done = True
                n = int(hdr.dwBytesRecorded) or BUF_BYTES
                data = ctypes.string_at(self._in_bufs[i], n)
                self._update_level(data)
                if self._hwo is not None and self._out_hdrs:
                    idx2 = oi % len(self._out_hdrs)
                    hdr2 = self._out_hdrs[idx2]
                    # 上一块还没播完就跳过（丢一小段，避免堆积延迟）
                    if not (hdr2.dwFlags & 0x00000010):        # WHDR_INQUEUE
                        oi += 1
                        try:
                            ctypes.memmove(self._out_bufs[idx2], data, n)
                            hdr2.dwBufferLength = n
                            # 保留 WHDR_PREPARED：清成 0 会让 waveOutWrite 返回
                            # UNPREPARED，回放（麦克风混入）从此失效
                            hdr2.dwFlags = 0x00000002
                            mm.waveOutWrite(self._hwo, ctypes.byref(hdr2),
                                            ctypes.sizeof(hdr2))
                        except Exception:
                            pass
                # 重新排队采集缓冲：保留 WHDR_PREPARED，只重置长度与已录字节数；
                # 清 dwFlags 会让 waveInAddBuffer 返回 UNPREPARED，采集在
                # 前两个缓冲（约 160ms）后永久停止（电平表不动/无麦克风输出的根因）
                hdr.dwBufferLength = BUF_BYTES
                hdr.dwBytesRecorded = 0
                try:
                    mm.waveInAddBuffer(self._hwi, ctypes.byref(hdr), ctypes.sizeof(hdr))
                except Exception:
                    pass
            time.sleep(0.02 if done else 0.01)

    def _update_level(self, data):
        try:
            n = len(data) // 2
            if n <= 0:
                return
            samples = ctypes.cast(data, ctypes.POINTER(ctypes.c_short))
            # 抽样计算 RMS（每 8 个点取 1 个，够快也够准）
            step = 8
            total = 0
            cnt = 0
            for i in range(0, n, step):
                v = samples[i]
                total += v * v
                cnt += 1
            if not cnt:
                return
            rms = (total / cnt) ** 0.5
            # 按 dB 映射到 0~1（-55dB≈静音，0dB≈满量程），指示器观感接近真实音频表
            db = 20.0 * math.log10(max(rms, 1.0) / 32768.0)
            lv = max(0.0, min(1.0, (db + 55.0) / 55.0))
            with self._lock:
                # 上升快、回落慢，观感接近真实音频表（每 80ms 采样一次）
                self._level = lv if lv >= self._level else self._level * 0.72
        except Exception:
            pass


_monitor = None


def monitor():
    """进程内单例（界面与配置共用同一个采集实例）。"""
    global _monitor
    if _monitor is None:
        _monitor = MicMonitor()
    return _monitor
