# -*- coding: utf-8 -*-
"""界面多语言（中文 / English）—— 按系统语言自动切换。

判定顺序：
1. 环境变量 ``LANSTREAM_LANG=zh|en``（便于开发与测试时强制指定）
2. Windows 系统默认 UI 语言 ``GetUserDefaultUILanguage()``
3. Python ``locale``
4. 兜底英文

简体 / 繁体 / 香港 / 澳门中文（primary langid 0x04）→ 中文界面，其余语言 → 英文界面。
"""
import os

_T = {
    # ---- 通用 / 标题 ----
    "app_title": ("月光极速电子教室", "MoonLight Classroom LanStream"),
    "local_ip": ("本机 IP", "Local IP"),

    # ---- 开播区 ----
    "start": ("一键开播", "Start"),
    "start_hint": ("一键开播", "Start Streaming"),
    "stop": ("停止", "Stop"),
    "quit": ("退出", "Exit"),
    "bitrate": ("码率 (Mb/s)", "Bitrate (Mb/s)"),
    "fps": ("帧率 (FPS)", "Frame Rate (FPS)"),
    "audio_input": ("音频输入设备", "Audio Input"),
    "audio_system": ("系统声音（默认输出）", "System Sound (default output)"),
    "audio_none": ("未检测到录音设备", "No recording device found"),
    "mic": ("麦克风", "Microphone"),
    "mic_on_log": ("麦克风已混入串流（建议使用耳麦，避免啸叫）",
                   "Microphone mixed into the stream (use a headset to avoid feedback)"),
    "mic_off_log": ("麦克风已关闭，仅串流系统声音",
                    "Microphone off — only system sound is streamed"),

    # ---- 收看区 ----
    "watch": ("一键收看", "Watch"),
    "watch_hint": ("一键收看", "Watch Stream"),
    "target_host": ("目标主机 IP", "Target Host IP"),
    "scan": ("检测", "Scan"),
    "autowatch": ("开机自动收看", "Auto-watch on launch"),
    "autowatch_tip": ("开启后启动软件即自动连接并收看",
                      "Connect and start watching automatically at startup"),

    # ---- 状态 ----
    "st_off": ("未开播", "Idle"),
    "st_ready": ("已开播", "Ready"),
    "st_live": ("正在直播", "Live"),
    "st_starting": ("正在开播…", "Starting…"),
    "st_ready_wait": ("● 已开播，等待学生端连接", "● Ready, waiting for clients"),
    "st_live_n": ("● 正在直播 · 已连接 %d 台客户端", "● Live · %d client(s) connected"),
    "st_live_dot": ("● 正在直播", "● Live"),
    "st_starting_dot": ("● 正在开播…", "● Starting…"),
    "st_idle_dot": ("● 未开播", "● Idle"),
    "btn_live_n": ("直播中 · %d 台", "Live · %d"),
    "clients_changed": ("客户端连接数：%d", "Connected clients: %d"),
    "status_ip": ("本机 IP %s  ·  %s", "Local IP %s  ·  %s"),

    # ---- 日志区 ----
    "log_title": ("系统日志", "System Log"),
    "ready_log": ("就绪 · 系统托盘常驻，单击呼出/最小化，右键更多操作",
                  "Ready · tray icon active; click to show/hide, right-click for more"),

    # ---- 开播进度阶段（core 回调文案）----
    "p_check_env": ("检查运行环境", "Checking environment"),
    "p_stop_svc": ("停止系统服务", "Stopping system service"),
    "p_write_cfg": ("写入串流配置", "Writing stream config"),
    "p_sync_cert": ("同步证书", "Syncing certificates"),
    "p_firewall": ("放行端口", "Opening ports"),
    "p_start_srv": ("启动服务端", "Starting server"),
    "p_wait_port": ("等待端口就绪", "Waiting for ports"),
    "p_pin_guard": ("启动配对守护", "Starting pairing guard"),
    "p_done": ("开播完成", "Started"),

    # ---- 日志消息 ----
    "starting_with": ("正在开播…（码率 %s Mb/s · %d FPS）",
                      "Starting… (%s Mb/s · %d FPS)"),
    "start_err": ("⚠ 开播异常：", "⚠ Start failed: "),
    "started": ("✔ 已开播", "✔ Streaming started"),
    "start_fail": ("开播失败", "Start failed"),
    "auto_on": ("开机自动收看已开启（已保存到配置文件）",
                "Auto-watch enabled (saved to config)"),
    "auto_off": ("开机自动收看已关闭（已保存到配置文件）",
                 "Auto-watch disabled (saved to config)"),
    "auto_connecting": ("开机自动收看：正在连接 %s …", "Auto-watch: connecting to %s …"),
    "stopping": ("正在停止…", "Stopping…"),
    "stopped": ("已停止", "Stopped"),
    "stop_err": ("⚠ 停止异常：", "⚠ Stop failed: "),
    "scanning": ("正在检测局域网已开播主机…", "Scanning LAN for streaming hosts…"),
    "scan_err": ("⚠ 检测异常：", "⚠ Scan failed: "),
    "scan_done": ("检测完成：", "Scan complete: "),
    "scan_none": ("未发现其他开播主机", "no other streaming host found"),
    "connecting": ("正在连接 %s …（码率 %s Mb/s · %d FPS）",
                   "Connecting to %s … (%s Mb/s · %d FPS)"),
    "local_not_started": ("本机未开播，先启动…", "Local host is idle, starting server…"),
    "watch_ok": ("✔ 已发起收看 %s", "✔ Watching %s"),
    "no_sunshine": ("未找到 Sunshine，请先安装", "Sunshine not found, please install it first"),
    "no_moonlight": ("未找到 Moonlight", "Moonlight not found"),
    "pair_ok": ("配对完成", "Paired"),
    "pair_fail": ("配对失败/超时", "Pairing failed / timed out"),
    "watching_host": ("已连接 %s", "Connected to %s"),
    "not_paired": ("未配对，请先开播", "Not paired, please start streaming first"),
    "pair_fail_prefix": ("配对失败：", "Pairing failed: "),
    "unknown_host": ("—", "—"),
}

LANG = "zh"


def _detect(mode="ui"):
    forced = (os.environ.get("LANSTREAM_LANG") or "").strip().lower()
    if forced in ("zh", "zh-cn", "zh_cn", "cn", "chinese"):
        return "zh"
    if forced in ("en", "en-us", "en_us", "english"):
        return "en"
    if os.name == "nt":
        try:
            import ctypes
            fn = (ctypes.windll.kernel32.GetUserDefaultUILanguage
                  if mode == "ui" else ctypes.windll.kernel32.GetSystemDefaultUILanguage)
            langid = int(fn()) & 0x3FF
            if langid:
                return "zh" if langid == 0x04 else "en"
        except Exception:
            pass
    try:
        import locale
        loc = (locale.getdefaultlocale()[0] or "").lower()
        if loc:
            return "zh" if loc.startswith("zh") else "en"
    except Exception:
        pass
    return "en"


LANG = _detect()


def is_zh():
    return LANG == "zh"


def t(key, *args, **kw):
    """取当前语言的文案；带 % 占位符时可传参格式化。"""
    pair = _T.get(key)
    if not pair:
        return key
    s = pair[0] if LANG == "zh" else pair[1]
    if args:
        try:
            s = s % (args if len(args) > 1 else args[0])
        except Exception:
            pass
    elif kw:
        try:
            s = s % kw
        except Exception:
            pass
    return s


def set_lang(lang):
    """手动切换语言（zh / en），仅用于运行时预览，不落盘。"""
    global LANG
    LANG = "en" if str(lang).lower().startswith("en") else "zh"
    return LANG
