# -*- coding: utf-8 -*-
"""
月光极速电子教室 —— 核心逻辑（v1.0.0）
基于开源 Sunshine(服务端) + Moonlight(客户端) 二次开发，只保留核心串流功能。
- 串流不加密（局域网直连）
- 最低延迟（NVENC P1 / 单趟 / 高码率）
- 一键开播 / 一键收看（自动配对 + 自动开播）
- 服务端与客户端运行时均内置在 EXE 中，首次使用时释放
- 键鼠控制默认禁用，F1 全局热键切换
"""
import os
import sys
import io
import time
import json
import socket
import random
import shutil
import zipfile
import subprocess
import threading
import ctypes
import datetime
from pathlib import Path

APP_NAME = "月光极速电子教室"
VERSION = "1.0.3"
SUNSHINE_PORT = 47989          # 控制端口（web UI = 47990）
STREAM_PORT_LO = 47984
STREAM_PORT_HI = 48010
FIXED_PIN = "0000"             # 固定配对 PIN：客户端自动配对，界面不显示（局域网开放访问）
# Sunshine 经 mDNS 广播的主机名必须为 ASCII，否则 Moonlight 无法解析会判定主机离线而配对挂起。
HOST_NAME = "MoonClass-Room"

# ---------------- 路径 ----------------
def is_frozen() -> bool:
    return getattr(sys, "frozen", False)

def app_data_dir() -> Path:
    if is_frozen():
        base = Path(os.environ.get("LOCALAPPDATA", Path.home()))
        return base / "LanStream"
    return Path(__file__).parent / ".runtime"

def _log_extract(*parts):
    try:
        with open(app_data_dir() / "extract_log.txt", "a", encoding="utf-8") as f:
            f.write(" ".join(str(x) for x in parts) + "\n")
    except Exception:
        pass


_runtime_lock = threading.Lock()

def _ensure_runtimes():
    """把 EXE 内置的 runtimes.zip（sunshine + client 压缩包）释放到本地缓存。
    仅首次释放；后续启动缓存已存在则直接跳过，加快启动。"""
    if not is_frozen():
        return
    base = app_data_dir()
    sun = base / "sunshine" / "sunshine.exe"
    cli = base / "client" / "Moonlight.exe"
    if sun.exists() and cli.exists():
        return
    meip = getattr(sys, "_MEIPASS", "")
    z = Path(meip) / "runtimes.zip" if meip else None
    with _runtime_lock:
        if sun.exists() and cli.exists():
            return
        if z is not None and z.exists():
            try:
                base.mkdir(parents=True, exist_ok=True)
                root = str(base.resolve())
                with zipfile.ZipFile(z) as zf:
                    for name in zf.namelist():
                        target = (base / name).resolve()
                        if not str(target).startswith(root):
                            _log_extract("zip-slip", name)
                            continue
                    zf.extractall(str(base))
                # Sunshine 需要其自身 config 目录存在，否则 Web/HTTPS 接口无法启动
                try:
                    (base / "sunshine" / "config").mkdir(parents=True, exist_ok=True)
                except Exception:
                    pass
                _log_extract("runtimes-extracted", "sunshine", "client")
            except Exception as e:
                _log_extract("zip-extract-fail", repr(e))
        else:
            _log_extract("no-runtimes-zip", str(z))


def _runtime_exe(subdir: str, exe: str) -> Path | None:
    p = app_data_dir() / subdir / exe
    if is_frozen() and p.exists():
        return p
    return None

def find_sunshine() -> Path | None:
    _ensure_runtimes()
    b = _runtime_exe("sunshine", "sunshine.exe")
    if b is not None:
        return b
    for c in (r"C:\Program Files\Sunshine\sunshine.exe",
              r"C:\Program Files (x86)\Sunshine\sunshine.exe"):
        if Path(c).exists():
            return Path(c)
    f = shutil.which("sunshine")
    return Path(f) if f else None

def find_moonlight() -> Path | None:
    _ensure_runtimes()
    b = _runtime_exe("client", "Moonlight.exe")
    if b is not None:
        return b
    for c in (r"C:\Program Files\Moonlight Game Streaming\Moonlight.exe",
              r"C:\Program Files (x86)\Moonlight Game Streaming\Moonlight.exe"):
        if Path(c).exists():
            return Path(c)
    f = shutil.which("Moonlight")
    return Path(f) if f else None

def binaries_ready() -> dict:
    """供 GUI 在开播/收看前检查。返回 {sunshine, moonlight: bool} 及缺失提示。"""
    out = {"sunshine": find_sunshine() is not None,
           "moonlight": find_moonlight() is not None}
    return out

# ---------------- 无窗口化启动（抑制后台弹窗）----------------
_CREATE_NO_WINDOW = 0x08000000
_CREATE_NEW_PROCESS_GROUP = 0x00000200


def _hidden_startupinfo():
    """隐藏窗口的 STARTUPINFO：GUI 程序（Moonlight 的 Qt 界面）启动时不闪窗。"""
    try:
        si = subprocess.STARTUPINFO()
        si.dwFlags |= subprocess.STARTF_USESHOWWINDOW
        si.wShowWindow = 0          # SW_HIDE
        return si
    except Exception:
        return None


def _sp_run(args, **kw):
    """subprocess.run 的无窗口封装：控制台程序绝不弹黑框。"""
    kw.setdefault("creationflags", _CREATE_NO_WINDOW)
    kw.setdefault("startupinfo", _hidden_startupinfo())
    return subprocess.run(args, **kw)


def _sp_popen(args, hidden=True, **kw):
    """subprocess.Popen 的无窗口封装。hidden=True 时连 GUI 窗口也隐藏。"""
    kw.setdefault("creationflags", _CREATE_NO_WINDOW)
    if hidden:
        kw.setdefault("startupinfo", _hidden_startupinfo())
    return subprocess.Popen(args, **kw)


# ---------------- 管理员 / 服务 ----------------
def is_admin() -> bool:
    try:
        return bool(ctypes.windll.shell32.IsUserAnAdmin())
    except Exception:
        return False

def _run(*args, **kw):
    return _sp_run(args, capture_output=True, text=True, errors="replace", **kw)

SERVICE = "SunshineService"

def stop_sunshine_service():
    _run("sc", "config", SERVICE, "start=", "demand")
    _run("sc", "stop", SERVICE)
    for _ in range(20):
        st = _run("sc", "query", SERVICE).stdout.upper()
        if "STOPPED" in st or "RUNNING" not in st:
            break
        time.sleep(0.5)

def service_status():
    # sc.exe 可能被安全软件/策略拦截而无法执行，这里绝不能让异常冒泡打断开播流程
    try:
        st = _run("sc", "query", SERVICE).stdout.upper()
    except Exception:
        return "absent"
    if "RUNNING" in st:
        return "running"
    if "STOPPED" in st:
        return "stopped"
    return "absent"

# ---------------- 配置 ----------------
def _runtime_config_dir() -> Path:
    """Sunshine 运行配置目录：全部位于可写位置（LocalAppData / .runtime）。"""
    d = app_data_dir() / "config"
    d.mkdir(parents=True, exist_ok=True)
    return d

def _cfg_path():
    return _runtime_config_dir() / "sunshine.conf"

def _bundled_path(rel: str) -> Path | None:
    """在打包(_MEIPASS)或源码目录中定位内置资源。"""
    bases = []
    meip = getattr(sys, "_MEIPASS", "")
    if meip:
        bases.append(Path(meip))
    bases.append(Path(__file__).parent)
    for b in bases:
        p = b / rel
        if p.exists():
            return p
    return None

def _ensure_server_config():
    """确保服务端运行所需配置（CA 证书、凭据、apps）就绪；首次从内置模板释放。"""
    d = _runtime_config_dir()
    cred = d / "credentials"
    cred.mkdir(parents=True, exist_ok=True)
    need = {
        cred / "cacert.pem": "runtime_config/credentials/cacert.pem",
        cred / "cakey.pem": "runtime_config/credentials/cakey.pem",
        d / "apps.json": "runtime_config/apps.json",
        d / "sunshine_state.json": "runtime_config/sunshine_state.json",
    }
    for dst, rel in need.items():
        if not dst.exists():
            src = _bundled_path(rel)
            if src is not None and src.exists():
                try:
                    shutil.copyfile(src, dst)
                    _log_extract("server-config", dst.name, "released")
                except Exception as e:
                    _log_extract("server-config-fail", rel, repr(e))

def _apps_path():
    return _runtime_config_dir() / "apps.json"

def _state_path():
    return app_data_dir() / "lanstream_state.json"

def load_state() -> dict:
    try:
        return json.loads(_state_path().read_text(encoding="utf-8"))
    except Exception:
        return {}

def _save_state(state):
    try:
        _state_path().write_text(json.dumps(state, ensure_ascii=False), encoding="utf-8")
    except Exception:
        pass

def input_enabled() -> bool:
    """学生端键鼠控制：固定永久禁用（学生机只收看，不可控制教师机）。"""
    return False

def _set_input_enabled(v: bool):
    # 学生端键鼠控制功能已移除：服务端始终禁输入，调用方无法开启
    return False

def write_config(sunshine_exe: Path, port=SUNSHINE_PORT, bitrate_kbps=10000,
                 fps=60, hevc=True, name=HOST_NAME, input_on=False) -> Path:
    """写入核心 sunshine.conf 与 apps.json，返回 conf 路径。
    全部路径指向可写的运行时配置目录（LocalAppData/.runtime），无需管理员权限；
    首次开播前自动释放内置 CA 证书与凭据（服务端初始化），保证固定 PIN 自动配对可用。"""
    cfg = _cfg_path()
    d = _runtime_config_dir()
    _ensure_server_config()
    state = d / "sunshine_state.json"
    apps = d / "apps.json"
    cred = d / "credentials"
    inp = ["keyboard = enabled", "mouse = enabled", "controller = enabled"] if input_on else \
          ["keyboard = disabled", "mouse = disabled", "controller = disabled"]
    conf = [
        "# ==== 月光极速电子教室 核心配置 ====",
        "locale = zh",
        "sunshine_name = " + name,
        "min_log_level = info",
        "system_tray = disabled",
        "",
        "# 输入：默认禁用键鼠控制（F1 在客户端切换）",
    ] + inp + [
        "",
        "# 网络：局域网。bind_address=0.0.0.0 是关键——Sunshine 6.x 内部网关(47989)会把",
        "# serverinfo/applist 转发到 47984，若不绑定所有网卡，47984 只绑首个网卡 IP，",
        "# 导致 502 upstream connect failed，学生端配对与收看全部失败。",
        "port = " + str(port),
        "address_family = ipv4",
        "bind_address = 0.0.0.0",
        "upnp = disabled",
        "lan_encryption_mode = 0",
        "wan_encryption_mode = 0",
        "fec_percentage = 10",
        "",
        "# 固定 PIN：多台学生机（1v多）自动配对，无需 GUI 输入",
        "pin = " + FIXED_PIN,
        "",
        "# 编码：NVENC P1 最低延迟",
        "encoder = nvenc",
        "nvenc_preset = 1",
        "nvenc_twopass = disabled",
        "hevc_mode = " + ("2" if hevc else "1"),
        "av1_mode = 1",
        # 注：Sunshine 2026.x 已不再识别 fps / min_bitrate 配置项，
        # 码率上限用 max_bitrate(Kbps)，帧率由客户端串流请求的 --fps 决定；
        # minimum_fps_target 让画面静止时也维持该帧率下限。
        "max_bitrate = " + str(int(bitrate_kbps)),
        "minimum_fps_target = " + str(int(fps)),
        "",
        "# 全部指向可写运行时目录（绝对路径）",
        "file_state = " + str(state),
        "credentials_file = " + str(state),
        "log_path = " + str(d / "sunshine.log"),
        "pkey = " + str(cred / "cakey.pem"),
        "cert = " + str(cred / "cacert.pem"),
        "file_apps = " + str(apps),
    ]
    cfg.write_text("\n".join(conf) + "\n", encoding="utf-8")

    # 核心应用列表：仅“桌面”（Sunshine 6.x 新格式：env + image-path）
    apps_obj = {"env": {}, "apps": [{"name": "Desktop", "image-path": "desktop.png"}]}
    try:
        apps.write_text(json.dumps(apps_obj, ensure_ascii=False, indent=2),
                        encoding="utf-8")
    except Exception:
        pass
    return cfg

# ---------------- 服务端 ----------------
_server_proc: subprocess.Popen | None = None
_server_lock = threading.Lock()

def server_alive() -> bool:
    return bool(_server_proc) and _server_proc.poll() is None

def start_server(port=SUNSHINE_PORT, bitrate_kbps=10000, fps=60, hevc=True,
                 input_on=None, on_progress=None) -> dict:
    """前台启动 Sunshine（首次开播时释放内置服务端）。

    on_progress(pct:int, text:str) 可选回调：汇报开播进度，供状态栏进度条显示。
    """
    global _server_proc

    def prog(pct, text=""):
        if on_progress is None:
            return
        try:
            on_progress(int(pct), text)
        except Exception:
            pass

    prog(3, "检查运行环境")
    sunshine = find_sunshine()
    if sunshine is None:
        return {"ok": False, "msg": "未找到 Sunshine，请先安装"}
    if service_status() == "running":
        prog(8, "停止系统服务")
        try:
            stop_sunshine_service()
        except Exception:
            pass
    time.sleep(1)
    if input_on is None:
        input_on = input_enabled()
    prog(18, "写入串流配置")
    cfg = write_config(sunshine, port, bitrate_kbps, fps, hevc, input_on=input_on)
    # Sunshine 6.x 需要自身 config 目录存在（Web/HTTPS 接口依赖），
    # 且其 config/credentials 必须与运行时配置目录一致，否则 HTTPS(47984/47990) 无法启动。
    try:
        sun_cfg = sunshine.parent / "config"
        sun_cfg.mkdir(parents=True, exist_ok=True)
        sun_cred = sun_cfg / "credentials"
        sun_cred.mkdir(parents=True, exist_ok=True)
        runtime_cred = _runtime_config_dir() / "credentials"
        for f in ("cacert.pem", "cakey.pem"):
            src = runtime_cred / f
            if src.exists():
                shutil.copyfile(src, sun_cred / f)
    except Exception:
        pass
    prog(38, "同步证书")
    # 一键开播时自动放行串流端口（HTTPS 配对 47984 / 控制 47989 / 流 48010）
    prog(52, "放行端口")
    try:
        ensure_firewall()
    except Exception:
        pass
    prog(66, "启动服务端")
    logf = open(app_data_dir() / "sunshine.log", "w", encoding="utf-8", errors="replace")
    # 固定 PIN：通过 /api/pin 守护线程自动确认（1v多自动配对）。
    # 关键：不要加 -0（stdin 读 PIN）——Sunshine 6.x 的 -0 模式会让配对会话
    # 无法出现在 /api/pin 列表，导致 pairchallenge 一直挂起（408 Pairing session expired）。
    cmd = [str(sunshine), str(cfg)]
    with _server_lock:
        proc = _sp_popen(
            cmd, stdin=subprocess.DEVNULL, stdout=logf, stderr=subprocess.STDOUT,
            cwd=str(sunshine.parent))
        _server_proc = proc
        ready = False
        for i in range(50):
            if proc.poll() is not None:
                break
            if _port_open(port):
                ready = True
                break
            if i % 3 == 0:
                prog(70 + min(20, i // 2), "等待端口就绪")
            time.sleep(0.4)
    st = load_state()
    st.update({"port": port, "bitrate": bitrate_kbps, "fps": fps,
               "hevc": hevc, "name": "月光极速电子教室", "pin": FIXED_PIN, "host": _self_ip()})
    _save_state(st)
    # 服务端就绪后启动 PIN 守护线程：自动确认所有学生端配对（1v多）
    prog(94, "启动配对守护")
    try:
        _start_pin_guard()
    except Exception:
        pass
    prog(100, "开播完成")
    return {"ok": ready, "pin": FIXED_PIN, "port": port,
            "msg": "已开播" if ready else "启动中", "ips": lan_ips()}


_pin_guard_stop = threading.Event()


def _feed_pin_stdin(proc, logf, log_path):
    """监控 sunshine 日志，出现 "Please insert pin" 时向 stdin 写入固定 PIN。
    Sunshine 6.x 用 -0 参数从 stdin 读取 PIN 来完成配对 challenge；写一次即可。"""
    fed = False
    deadline = time.time() + 120
    while not fed and time.time() < deadline:
        if proc.poll() is not None:
            return
        try:
            logf.flush()
            txt = ""
            try:
                with open(log_path, "r", encoding="utf-8", errors="replace") as f:
                    txt = f.read()
            except Exception:
                pass
            if "Please insert pin" in txt:
                try:
                    proc.stdin.write((FIXED_PIN + "\n").encode())
                    proc.stdin.flush()
                    fed = True
                except Exception:
                    pass
        except Exception:
            pass
        time.sleep(0.3)


def _start_pin_guard():
    """后台线程：轮询 GET /api/pin 拿到挂起配对会话，POST /api/pin 自动提交固定 PIN。
    学生端发起配对 → 服务端挂起 → 守护线程自动完成 → 学生端直接进入收看。"""
    _pin_guard_stop.clear()
    t = threading.Thread(target=_pin_guard_loop, daemon=True)
    t.start()


def _pin_guard_loop():
    import ssl as _ssl
    import urllib.request as _ur
    import urllib.error as _ue
    import json as _json
    import base64 as _b64
    ctx = _ssl.SSLContext(_ssl.PROTOCOL_TLS_CLIENT)
    ctx.check_hostname = False
    ctx.verify_mode = _ssl.CERT_NONE
    auth = _b64.b64encode(b"admin:admin1234").decode()
    url = "https://localhost:47990/api/pin"
    while not _pin_guard_stop.is_set():
        try:
            req = _ur.Request(url, headers={"Authorization": "Basic " + auth})
            r = _ur.urlopen(req, context=ctx, timeout=5)
            data = _json.loads(r.read().decode("utf-8", "replace"))
            for p in (data.get("pairings") or []):
                pid = p.get("id")
                if not pid:
                    continue
                body = _json.dumps({"pairing_id": pid, "pin": FIXED_PIN,
                                    "name": "ClassroomClient"}).encode("utf-8")
                req2 = _ur.Request(url, data=body,
                                   headers={"Authorization": "Basic " + auth,
                                            "Content-Type": "application/json"},
                                   method="POST")
                try:
                    r2 = _ur.urlopen(req2, context=ctx, timeout=10)
                    r2.read()
                except Exception:
                    pass
        except Exception:
            pass
        time.sleep(0.5)

def stop_server():
    """停止服务端：按进程名杀掉所有 Sunshine 进程树，确保端口与直播完全停止。"""
    global _server_proc
    _pin_guard_stop.set()
    _sp_run(["taskkill", "/IM", "sunshine.exe", "/F", "/T"],
            capture_output=True, text=True, errors="replace")
    with _server_lock:
        if _server_proc is not None:
            try:
                _server_proc.kill()
            except Exception:
                pass
        _server_proc = None

# ---------------- 状态检测 ----------------
_LOCAL_LOOPBACK = ("127.0.0.1", "0.0.0.0", "::1", "::", "[::1]")


def _stream_peers() -> set:
    """返回当前连到本机串流端口（47984-48010）的远端 IP 集合（含本机回环）。
    Sunshine 的串流/控制连接都落在这个端口区间，按远端 IP 去重即为在线客户端。"""
    peers = set()
    try:
        out = _sp_run(["netstat", "-ano"], capture_output=True, text=True,
                      errors="replace").stdout
        for ln in out.splitlines():
            if "ESTABLISHED" not in ln:
                continue
            parts = ln.split()
            if len(parts) < 3:
                continue
            local, remote = parts[1], parts[2]
            lp = local.rsplit(":", 1)[-1]
            if not (lp.isdigit() and STREAM_PORT_LO <= int(lp) <= STREAM_PORT_HI):
                continue
            rip = remote.rsplit(":", 1)[0].strip("[]")
            if rip:
                peers.add(rip)
    except Exception:
        pass
    return peers


def client_count() -> int:
    """当前在线客户端数量（不含本机回环连接）。"""
    return len([ip for ip in _stream_peers() if ip not in _LOCAL_LOOPBACK])


def server_status() -> dict:
    """真实开播/直播状态：端口、活动连接、在线客户端数量。"""
    port = int(load_state().get("port", SUNSHINE_PORT))
    running = _port_open(port, timeout=0.25)
    peers = _stream_peers()
    streaming = bool(peers)
    clients = len([ip for ip in peers if ip not in _LOCAL_LOOPBACK])
    return {"running": running, "streaming": streaming, "clients": clients,
            "text": "正在直播" if streaming else ("已开播" if running else "未开播")}


def _any_streaming() -> bool:
    return bool(_stream_peers())

def _port_open(port, host="127.0.0.1", timeout=0.4) -> bool:
    s = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
    s.settimeout(timeout)
    try:
        s.connect((host, port))
        return True
    except Exception:
        return False
    finally:
        s.close()

# ---------------- 局域网开播主机检测 ----------------
def lan_ips() -> list[str]:
    ips = []
    try:
        for info in socket.getaddrinfo(socket.gethostname(), None, socket.AF_INET):
            ip = info[4][0]
            if not ip.startswith("127.") and ip not in ips:
                ips.append(ip)
    except Exception:
        pass
    return ips

def _self_ip():
    ips = lan_ips()
    return ips[0] if ips else "192.168.110.20"

def scan_lan_hosts(timeout=0.3, max_threads=48) -> list[str]:
    """扫描局域网中已开播(Sunshine 端口开放)的主机 IP。返回列表。"""
    self_ip = _self_ip()
    base = self_ip.rsplit(".", 1)[0]
    found = [self_ip]
    lock = threading.Lock()

    def probe(i):
        ip = f"{base}.{i}"
        if ip == self_ip:
            return
        if _port_open(SUNSHINE_PORT, host=ip, timeout=timeout):
            with lock:
                found.append(ip)

    threads = []
    for i in range(1, 255):
        t = threading.Thread(target=probe, args=(i,), daemon=True)
        threads.append(t)
        t.start()
        if len(threads) >= max_threads:
            for t in threads:
                t.join(timeout + 0.4)
            threads = []
    for t in threads:
        t.join(timeout + 0.4)
    return sorted(set(found), key=lambda s: tuple(int(x) for x in s.split(".")))

# ---------------- 键鼠控制 ----------------
def set_input_enabled(enabled: bool, restart=True) -> dict:
    """切换键鼠控制。若服务端运行中则重启应用配置。"""
    _set_input_enabled(enabled)
    sunshine = find_sunshine()
    if sunshine is None:
        return {"ok": False, "msg": "未找到 Sunshine"}
    if server_alive():
        write_config(sunshine, input_on=enabled)   # 让下一次会话生效
        if restart:
            _restart_server()
    else:
        write_config(sunshine, input_on=enabled)
    return {"ok": True, "enabled": enabled}

def _restart_server():
    st = load_state()
    stop_server()
    time.sleep(1)
    start_server(port=int(st.get("port", SUNSHINE_PORT)),
                 bitrate_kbps=int(st.get("bitrate", 80000)),
                 fps=int(st.get("fps", 60)), hevc=bool(st.get("hevc", True)),
                 input_on=input_enabled())

# ---------------- 客户端 ----------------
def ensure_client_identity() -> bool:
    """学生端唯一身份：首次启动生成随机 uniqueid + 自签名证书写入 Moonlight 注册表。
    各学生机身份唯一 → 多台同时配对互不冲突（避免 409）；
    教师端 PIN 守护线程会自动确认每台学生机的配对请求（1v多）。"""
    try:
        import winreg as _wr
        k = _wr.OpenKey(_wr.HKEY_CURRENT_USER,
                        r"Software\Moonlight Game Streaming Project\Moonlight",
                        0, _wr.KEY_ALL_ACCESS)
    except FileNotFoundError:
        try:
            k = _wr.CreateKey(_wr.HKEY_CURRENT_USER,
                              r"Software\Moonlight Game Streaming Project\Moonlight")
        except Exception:
            return False
    try:
        # Moonlight 6.x 的身份键为 uniqueid / certificate / key（私钥键名是 "key"，
        # 不是旧版的 "privatekey"）。三项齐全即视为有效原生身份，绝不可再覆盖——
        # 否则会出现 certificate（新证书）与 key（原生私钥）不匹配，mTLS 握手失败、
        # pairchallenge 永远卡住。
        try:
            uid = _wr.QueryValueEx(k, "uniqueid")[0]
            cert = _wr.QueryValueEx(k, "certificate")[0]
            key = None
            for kn in ("key", "privatekey"):
                try:
                    key = _wr.QueryValueEx(k, kn)[0]
                    break
                except FileNotFoundError:
                    continue
            if uid and cert and key:
                return True  # 身份已存在，直接复用
        except FileNotFoundError:
            pass
        try:
            from cryptography import x509
            from cryptography.x509.oid import NameOID
            from cryptography.hazmat.primitives import hashes, serialization
            from cryptography.hazmat.primitives.asymmetric import rsa
            import urllib.parse as _up
            skey = rsa.generate_private_key(public_exponent=65537, key_size=2048)
            nm = x509.Name([x509.NameAttribute(NameOID.COMMON_NAME, "MoonClass-Client")])
            cert = (x509.CertificateBuilder().subject_name(nm).issuer_name(nm)
                    .public_key(skey.public_key())
                    .serial_number(random.randrange(1, 1 << 62))
                    .not_valid_before(datetime.datetime.now() - datetime.timedelta(days=1))
                    .not_valid_after(datetime.datetime.now() + datetime.timedelta(days=3650))
                    .sign(skey, hashes.SHA256()))
            cert_pem = cert.public_bytes(serialization.Encoding.PEM).decode()
            key_pem = skey.private_bytes(serialization.Encoding.PEM,
                                         serialization.PrivateFormat.TraditionalOpenSSL,
                                         serialization.NoEncryption()).decode()
            enc = lambda s: "@ByteArray(" + _up.quote(s.strip(), safe="") + ")"
            _wr.SetValueEx(k, "uniqueid", 0, _wr.REG_SZ, "%032x" % random.getrandbits(128))
            _wr.SetValueEx(k, "certificate", 0, _wr.REG_SZ, enc(cert_pem))
            _wr.SetValueEx(k, "key", 0, _wr.REG_SZ, enc(key_pem))          # Moonlight 6.x 键名
            _wr.SetValueEx(k, "privatekey", 0, _wr.REG_SZ, enc(key_pem))   # 兼容旧键名
            return True
        except Exception:
            return False
    finally:
        try:
            _wr.CloseKey(k)
        except Exception:
            pass

def is_paired(host) -> bool:
    m = find_moonlight()
    if m is None:
        return False
    try:
        r = _sp_run([str(m), "list", host], capture_output=True, text=True,
                    timeout=30, errors="replace")
        # Moonlight 6.x：已配对时 list 返回 rc=0 且 stdout 列出应用（如 "Desktop"）；
        # 未配对时 stderr 含 "has not been paired"。
        out = (r.stdout or "").strip()
        err = (r.stderr or "").strip()
        return r.returncode == 0 and len(out) > 0 and "not been paired" not in err
    except Exception:
        return False

def _clear_moonlight_hosts():
    """清除 Moonlight 客户端残留的主机身份缓存（hosts 下 N 数字子键）。
    这些残留会导致配对时出现 “Found unexpected PC” 而配对失败；
    每次自动配对前清理，保证干净客户端也能直接配对成功。"""
    import winreg

    def _del_tree(parent, name):
        # DeleteKey 无法删除含子键的键（hosts/N 下有 apps 子键），需递归删除，
        # 否则会抛 WinError 145 被吞掉后陷入死循环。
        try:
            child = winreg.OpenKey(parent, name, 0, winreg.KEY_ALL_ACCESS)
        except OSError:
            return
        while True:
            try:
                sub = winreg.EnumKey(child, 0)
            except OSError:
                break
            _del_tree(child, sub)
        winreg.CloseKey(child)
        try:
            winreg.DeleteKey(parent, name)
        except OSError:
            pass

    try:
        k = winreg.OpenKey(winreg.HKEY_CURRENT_USER,
                           r"Software\Moonlight Game Streaming Project\Moonlight\hosts",
                           0, winreg.KEY_ALL_ACCESS)
        subs = []
        i = 0
        while True:
            try:
                subs.append(winreg.EnumKey(k, i)); i += 1
            except OSError:
                break
        for s in subs:
            if s.isdigit():
                _del_tree(k, s)
        try:
            winreg.SetValueEx(k, "size", 0, winreg.REG_DWORD, 0)
        except Exception:
            pass
        winreg.CloseKey(k)
    except Exception:
        pass

def _fill_srvcert(host=None):
    """配对兜底：若 Moonlight 保存的服务端证书缺失/为空壳，
    从教师端 Web HTTPS 端口(47990)抓取真实服务端证书写入 hosts，
    使客户端本地判定“已配对”，后续 HTTPS serverinfo 直接返回应用列表。
    host 为教师端 IP；为空时回退本机。"""
    try:
        import winreg
        import urllib.parse
        import ssl as _ssl
        import socket as _sock
        pem = ""
        ctx = _ssl.SSLContext(_ssl.PROTOCOL_TLS_CLIENT)
        ctx.check_hostname = False
        ctx.verify_mode = _ssl.CERT_NONE
        target = host or "127.0.0.1"
        with _sock.create_connection((target, 47990), timeout=5) as sock:
            with ctx.wrap_socket(sock, server_hostname=target) as ssock:
                pem = _ssl.DER_cert_to_PEM_cert(ssock.getpeercert(binary_form=True))
        if not pem:
            return False
        enc = urllib.parse.quote(pem.strip(), safe="")
        srv = "@ByteArray(" + enc + ")"
        k = winreg.OpenKey(winreg.HKEY_CURRENT_USER,
                           r"Software\Moonlight Game Streaming Project\Moonlight\hosts",
                           0, winreg.KEY_ALL_ACCESS)
        i = 1
        while True:
            try:
                sk = winreg.OpenKey(k, str(i), 0, winreg.KEY_ALL_ACCESS)
            except FileNotFoundError:
                break
            try:
                cur = winreg.QueryValueEx(sk, "srvcert")[0]
                if isinstance(cur, str) and len(cur) <= 80:
                    winreg.SetValueEx(sk, "srvcert", 0, winreg.REG_SZ, srv)
            except FileNotFoundError:
                winreg.SetValueEx(sk, "srvcert", 0, winreg.REG_SZ, srv)
            winreg.CloseKey(sk)
            i += 1
        winreg.CloseKey(k)
        return True
    except Exception:
        return False

def _auto_pin(host, pin, proc, timeout=75):
    """学生端本地自动提交固定 PIN：pair 进程运行期间轮询教师端 /api/pin，
    对每个挂起配对会话 POST {pairing_id, pin}。PIN 在 stage1 前就绪，
    消除“等教师端输入 PIN / 密码不匹配”的时序竞态。"""
    import ssl as _ssl
    import urllib.request as _ur
    import urllib.parse as _up
    import json as _json
    import base64 as _b64
    try:
        ctx = _ssl.SSLContext(_ssl.PROTOCOL_TLS_CLIENT)
        ctx.check_hostname = False
        ctx.verify_mode = _ssl.CERT_NONE
        auth = _b64.b64encode(b"admin:admin1234").decode()
        base = "https://%s:47990/api/pin" % host
        deadline = time.time() + timeout
        posted = set()
        while proc.poll() is None and time.time() < deadline:
            try:
                req = _ur.Request(base, headers={"Authorization": "Basic " + auth})
                r = _ur.urlopen(req, context=ctx, timeout=3)
                data = _json.loads(r.read().decode("utf-8", "replace"))
                for p in (data.get("pairings") or []):
                    pid = p.get("id")
                    if not pid or pid in posted:
                        continue
                    body = _json.dumps({"pairing_id": pid, "pin": str(pin),
                                        "name": "ClassroomClient"}).encode("utf-8")
                    req2 = _ur.Request(base, data=body,
                                       headers={"Authorization": "Basic " + auth,
                                                "Content-Type": "application/json"},
                                       method="POST")
                    try:
                        r2 = _ur.urlopen(req2, context=ctx, timeout=10)
                        r2.read()
                        posted.add(pid)
                    except Exception:
                        pass
            except Exception:
                pass
            time.sleep(0.5)
    except Exception:
        pass


def _client_has_srvcert() -> bool:
    """检查 Moonlight 客户端 hosts 里是否已写入真实服务端证书（配对成功的标志）。
    空壳为 @ByteArray(-----BEGIN CERTIFICATE-----\n-----END CERTIFICATE-----\n)，真实证书 >100 字节。"""
    try:
        import winreg
        base = r"Software\Moonlight Game Streaming Project\Moonlight\hosts"
        hk = winreg.OpenKey(winreg.HKEY_CURRENT_USER, base)
        i = 0
        result = False
        while True:
            try:
                sub = winreg.EnumKey(hk, i)
            except OSError:
                break
            i += 1
            if not sub.isdigit():
                continue
            try:
                sk = winreg.OpenKey(hk, sub)
                v = winreg.QueryValueEx(sk, "srvcert")[0]
                winreg.CloseKey(sk)
                # 真实证书 >200 字节（空壳仅 ~66 字节）；不做 "END CERTIFICATE" 文本
                # 判断——srvcert 可能为 URL 编码（空格/换行被 %xx 转义）。
                if isinstance(v, str) and len(v) > 200 and "CERTIFICATE" in v:
                    result = True
                    break
            except Exception:
                pass
        winreg.CloseKey(hk)
        return result
    except Exception:
        return False


def pair_client(host, pin) -> tuple[bool, str]:
    m = find_moonlight()
    if m is None:
        return False, "未找到 Moonlight"
    _clear_moonlight_hosts()             # 先清残留，避免 “unexpected PC” 导致配对失败
    # PIN 由教师端 _pin_guard（localhost 轮询 /api/pin）自动提交；学生端不再跨机器
    # POST（Sunshine 的 CSRF/Origin 保护会挂起非 localhost 的 POST，反而阻塞教师端提交）。
    # 注意：输出必须丢弃（DEVNULL）——用 PIPE 且不读取会让大量 Qt 日志写满管道缓冲，
    # 导致 pair 进程阻塞、连 srvcert 都写不进来。
    # 全程隐藏窗口：配对的 Qt 进程不弹任何界面，只在配对成功后由 stream 打开正片窗口
    proc = _sp_popen([str(m), "pair", host, "--pin", str(pin)],
                     stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    # Moonlight 6.x 的 pair 进程在 PIN 提交后常卡在 pairchallenge（不退出），但此时
    # 客户端已写入真实服务端证书 srvcert = 实际已配对。故轮询 srvcert 即时返回，
    # 不再干等 pair 进程退出（把 25s 等待压缩到约 1.5s）。
    ok = False
    deadline = time.time() + 30
    while time.time() < deadline:
        if _client_has_srvcert():
            ok = True
            break
        if proc.poll() is not None:
            ok = (proc.returncode == 0)
            break
        time.sleep(0.3)
    # 清理仍卡住的 pair 进程（它卡在 pairchallenge，不影响已完成的配对）
    if proc.poll() is None:
        try:
            _sp_run(["taskkill", "/IM", "Moonlight.exe", "/F", "/T"],
                    capture_output=True, text=True, errors="replace")
        except Exception:
            pass
        try:
            proc.kill()
        except Exception:
            pass
    # 兜底：若客户端仍无有效证书，从教师端抓取补全
    if not ok:
        try:
            _fill_srvcert(host)
        except Exception:
            pass
        ok = _client_has_srvcert() or is_paired(host)
    return ok, ("配对完成" if ok else "配对失败/超时")

def stream_client(host, app="Desktop", bitrate_kbps=10000, fps=60, hevc=True,
                  fullscreen=True) -> tuple[bool, str]:
    m = find_moonlight()
    if m is None:
        return False, "未找到 Moonlight"
    args = [str(m), "stream", host, app, "--bitrate", str(bitrate_kbps),
            "--fps", str(fps), "--video-codec", "HEVC" if hevc else "H.264",
            "--display-mode", "fullscreen" if fullscreen else "windowed",
            "--no-vsync", "--no-hdr", "--video-decoder", "hardware"]
    # 只有确认配对成功后才调用这里：此时才让 Moonlight 主窗口出现
    _sp_popen(args, hidden=False)
    return True, "已连接 " + host

def watch(host, pin=None, auto_pair=True, bitrate_kbps=10000, fps=60) -> dict:
    m = find_moonlight()
    if m is None:
        return {"ok": False, "msg": "未找到 Moonlight"}
    if not is_paired(host):
        # 自动配对：使用固定 PIN，客户端本地自动提交，无需教师端输入、界面不显示
        if not auto_pair:
            return {"ok": False, "msg": "未配对，请先开播"}
        # 最多重试 3 次（每次配对含超时强杀，避免反复弹窗）
        last = ""
        for _try in range(3):
            ok, info = pair_client(host, FIXED_PIN)
            if ok and is_paired(host):
                break
            last = info
            time.sleep(8)
        else:
            return {"ok": False, "msg": "配对失败：" + last}
    # 配对就绪 → 立即启动收看（否则 Moonlight 进程退出即“闪退”）
    ok, msg = stream_client(host, bitrate_kbps=bitrate_kbps, fps=fps)
    return {"ok": ok, "msg": msg}

# ---------------- 其它 ----------------
def ensure_firewall():
    rules = [("LanStream_TCP", "TCP", "47984-48010"),
             ("LanStream_UDP", "UDP", "47984-48010"),
             ("LanStream_Web", "TCP", "47989-47990")]
    added = []
    for name, proto, ports in rules:
        exists = _run("netsh", "advfirewall", "firewall", "show", "rule",
                      "name=%s" % name).stdout
        if "No rules match" not in exists and name in exists:
            continue
        _run("netsh", "advfirewall", "firewall", "add", "rule",
             "name=%s" % name, "dir=in", "action=allow", "protocol=%s" % proto,
             "localport=%s" % ports, "profile=private,domain")
        added.append(name)
    return added

def _gen_pin() -> str:
    return FIXED_PIN

def install_via_winget(pkg: str) -> dict:
    r = _sp_run(["winget", "install", "--id", pkg, "--source", "winget",
                 "--silent", "--accept-source-agreements",
                 "--accept-package-agreements", "--disable-interactivity"],
                capture_output=True, text=True)
    return {"ok": r.returncode == 0, "msg": (r.stdout + r.stderr).strip()[-400:]}
