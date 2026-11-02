# MoonlightLanStreamer

**简体中文** ｜ [English](#english)

> 面向机房的**一键局域网串流**教学软件：教师机点「一键开播」，学生机点「一键收看」，
> 数秒内看到教师屏幕。内置 **Sunshine（服务端）+ Moonlight（客户端）**，单个 EXE、双击即用、无需安装。
>
> Windows 桌面端产品名为「月光极速电子教室」（即 exe 的文件说明 / 产品名称）。

**当前版本：v1.3.4** ｜ 更新记录见 [CHANGELOG.md](CHANGELOG.md) ｜ 技术栈：.NET Framework 4.8 + WinForms（纯代码自绘，无 XAML）

---

## 这是什么

基于开源 [Sunshine](https://github.com/LizardByte/Sunshine)（服务端）与
[Moonlight](https://github.com/moonlight-stream/moonlight-qt)（客户端）二次开发的桌面程序，
把原本需要手工处理的「配对 / 防火墙 / 编码参数 / 多客户端 / 麦克风」流程全部自动化，
面向局域网教学场景做取舍：**低延迟优先、局域网明文串流、免手工配对**。

- 单个 EXE 分发，运行时（Sunshine + Moonlight，`runtimes.zip` 约 73 MB）内嵌在程序里，
  首次运行在后台线程释放到 `%LOCALAPPDATA%\LanStream\`（约 1 s，不阻塞窗口出现）
- 目标机**不需要安装任何运行时**：.NET Framework 4.8 是 Win10 1903+ / Win11 出厂自带
- 已修复/规避的坑：`bind_address` 导致 502、`-0` 参数导致配对会话 408、`PIPE` 未读取导致配对进程阻塞、
  证书与私钥不匹配导致 mTLS 失败、`WHDR_PREPARED` 被清导致采集 160 ms 后永久停止、
  独占全屏被系统关闭导致"刚连上就闪退"、按注册表 `hostname` 判断配对状态导致反复重配重连、
  探活阻塞 UI 线程导致"启动就卡死"等

---

## 功能特性

### 开播（教师机）

| 功能 | 说明 |
|---|---|
| 一键开播 | 自动停掉系统 Sunshine 服务 → 写入优化配置 → 启动服务端 → 放行防火墙 |
| 码率 / 帧率 | 下拉可选也可手输，默认 **10 Mb/s + 60 FPS**，改完即写入配置 |
| 状态指示 | ● 未开播 / ● 已开播 / ● 正在直播（按真实端口与活动连接判断） |
| 开播进度条 | 状态栏显示阶段与百分比（检查环境 → 写配置 → 同步证书 → 放行端口 → 启动服务端 → 等待就绪） |
| 客户端计数 | 实时显示「已连接 N 台客户端」（解析 Sunshine 会话日志，连上/断开 1~2 s 内刷新） |
| 自愈 | 检测到 `WSASendMsg 10055` 风暴（本地发送队列积压 → 僵尸会话）时自动重启 Sunshine（90 s 冷却） |
| 音频串流 | 默认串流**系统声音**（课件/视频声），可另选音频输入设备 |
| 麦克风 | 开关默认开启；麦克风走独立 UDP 通道直达学生端，教师机本地**不外放、无回声** |
| 电平指示 | 横向 30 段电平条（绿/黄/红），用来确认"有没有输入"；关麦即停采集并归零 |
| 完成后自动关机 | 可选，课程结束后自动关机 |

### 收看（学生机）

| 功能 | 说明 |
|---|---|
| 一键收看 | 自动配对（固定 PIN `0000`，无需人工输入）→ 直接进入画面 |
| 目标主机 | IP 输入框支持手输 + 下拉历史（自动记忆上次检测到的主机） |
| 一键检测 | 扫描局域网内已开播主机，加入下拉列表 |
| 开机自动收看 | 默认关闭；开启后持续跟随目标主机（每 1.5 s 探活） |
| 随开播/停播自动进出 | 教师端开播即自动进入收看；教师端停播（含主动停播信令 `LMSQ`）立即退出 Moonlight 并继续监听 |
| 断线自愈 | Moonlight 意外退出自动重连，失败按 8 / 16 / 30 s 递增退避，不刷屏重试 |
| 麦克风收听 | 接收教师端 UDP 音频流，只在收看期间播放，退出即静音 |

### 通用

- **中英双语界面**：自动跟随系统语言（中文系统 → 中文，其他 → English）
- **系统托盘常驻**：单击呼出/最小化，右键菜单（开播 / 停止 / 收看 / 退出）
- **单实例运行**：重复打开不启动第二份，而是把已运行窗口弹到最前
- **后台无弹窗**：开播与配对全程无命令行黑框、无 Moonlight 配对窗口，连接成功才出现画面
- **设置全部持久化**：主机、历史主机、码率、帧率、麦克风、自动收看均写入 `lanstream_state.json`
- 极简扁平 UI：直角卡片/按钮、日志区（无滚动条，滚轮可滚）+ 状态栏，PerMonitorV2 高 DPI

---

## 音频

开播卡片里**画质在上、音频在下**：

| 控件 | 说明 |
|---|---|
| 音频输入设备 | 下拉选择录音设备；首项「系统声音（默认输出）」表示只串流系统声音、不采集麦克风 |
| 麦克风 开关 | 开启（默认）= 把所选麦克风的实时声音发给学生端；关闭 = 只串流系统声音 |
| 电平条 | 横向 30 段指示器，有输入就亮；关麦即停采集并归零 |

实现说明：Sunshine 只捕获**系统播放设备**（WASAPI loopback），其协议在 Windows 上没有可用的
Mic 通道，因此麦克风走**独立 UDP 通道**（自定义 `LMS1` 音频块，端口 `48050`）：教师端广播 +
学生端心跳订阅（`LMSJ`）后改为单播，学生端只在收看期间播放。教师机本地不再把麦克风回放到
扬声器，所以**不会从音响里听到自己的声音**，也不需要系统「侦听此设备」。设备名取注册表
`MMDevices\Audio\Capture`（64 位视图），不受 winmm 32 字符限制。

> 建议用耳麦：麦克风混入后教师机扬声器也会随课件外放，用桌面音箱容易啸叫。

---

## 系统要求

- **Windows 10 / 11 x64**
- 教师机建议有硬件编码能力的 GPU（NVENC / AMF / QSV，默认配置优先 NVENC）
- 学生机无需额外安装任何东西
- 首次运行会请求一次管理员权限（接管 Sunshine 服务、放行防火墙端口）

---

## 快速开始

1. 运行 `月光极速电子教室-v1.3.4.exe`（教师机、学生机是同一个程序）
2. **教师机**：确认码率/帧率 → 点「一键开播」，状态变「正在直播」即开始
3. **学生机**：输入教师机 IP（或点「一键检测」自动发现）→ 点「一键收看」
4. 机房批量部署：把学生机的「开机自动收看」打开并填好教师机 IP，
   之后开机会自动跟随教师端的开播/停播进出画面

---

## 项目结构

```
MoonlightLanStreamer/
├─ README.md / CHANGELOG.md / LICENSE / THIRD_PARTY.md
├─ .gitignore / .gitattributes
└─ src/                        # 全部源码（经典 csproj，可直接用 VS 2022 打开）
   ├─ MoonlightLanStreamer.csproj
   ├─ Program.cs               # 入口：单实例 + 崩溃日志 + 全局异常
   ├─ MainForm.cs              # 主界面与全部交互（开播/收看/自动跟随/日志）
   ├─ SunCore.cs               # 教师端：配置生成、服务启停、客户端计数、10055 自愈
   ├─ ClientCore.cs            # 学生端：配对检查、串流、进程输出捕获
   ├─ MicAudio.cs              # 麦克风采集（winmm）+ UDP 收发 + 停播信令
   ├─ Runtime.cs               # 路径、内嵌资源释放、版本号
   ├─ Settings.cs              # 配置模型（JSON 持久化）
   ├─ Ui.cs / I18n.cs / TrayIcon.cs
   ├─ AssemblyInfo.cs / app.manifest
   ├─ Resources/
   │  ├─ icon.ico              # 应用/窗口/托盘图标（唯一图标源）
   │  ├─ runtime_config/       # apps.json + Sunshine Web 凭据模板
   │  └─ runtimes.zip          # 运行时包（约 73 MB，本地生成，不入库）
   ├─ build.ps1                # 一键构建（编译 + 组装 + 数字签名）
   └─ make_runtimes.ps1        # 把本机 Sunshine + Moonlight 打成 runtimes.zip
```

---

## 从源码构建

```powershell
# 1) 编译器：任选其一
#    A. VS 2022 Build Tools（体积小、无 IDE）
winget install Microsoft.VisualStudio.2022.BuildTools -e --override `
  "--quiet --wait --norestart --add Microsoft.VisualStudio.Workload.ManagedDesktopBuildTools --add Microsoft.Net.Component.4.8.TargetingPack --includeRecommended"
#    B. Visual Studio 2022（勾选“.NET 桌面开发”）
#    C. .NET Framework 4.8 Developer Pack（build.ps1 会兜底找系统自带 MSBuild）

# 2) 打包运行时（本机需已安装 Sunshine 与 Moonlight，路径可用参数覆盖）
cd src
powershell -ExecutionPolicy Bypass -File .\make_runtimes.ps1

# 3) 构建单文件 exe（不加 -SkipSign 时会用代码签名证书签名并打时间戳）
powershell -ExecutionPolicy Bypass -File .\build.ps1 -Version 1.3.4
# 没有证书或不想签：
powershell -ExecutionPolicy Bypass -File .\build.ps1 -Version 1.3.4 -SkipSign
```

产物：`dist\月光极速电子教室-v1.3.4.exe`（约 73 MB，运行时已内嵌）。

也可以直接用 VS 2022 打开 `src\MoonlightLanStreamer.csproj`（经典项目格式，无需转换）后 F5 / 生成。

**没有 `runtimes.zip` 也能编译**：产物是约 108 KB 的 exe，但运行时找不到 Sunshine/Moonlight；
真机部署请务必先跑一次 `make_runtimes.ps1`。

> 常见坑：VS 安装器在有**同时设置** `HTTPS_PROXY` 与 `https_proxy` 的机器上会报 5002
> （.NET 字典大小写不敏感，重复键崩溃）。装之前 `unset https_proxy http_proxy` 即可。

---

## 配置文件与端口

运行时数据目录：`%LOCALAPPDATA%\LanStream\`

| 文件 | 用途 |
|---|---|
| `lanstream_state.json` | 界面全部设置（目标主机、历史主机、码率、帧率、麦克风、自动收看、自动关机） |
| `config\sunshine.conf` | 每次开播时按界面设置生成的 Sunshine 配置 |
| `config\sunshine_state.json` | Sunshine 自身状态（已配对设备 `named_devices`、Web 凭据） |
| `sunshine.log` | 服务端日志（排查开播问题先看这里） |
| `moonlight.log` | 学生端日志（配对判定结果、Moonlight 全部输出、退出码） |
| `gui.log` / `startup.log` / `crash.log` | 界面关键流程、启动耗时、未捕获异常 |

`lanstream_state.json` 主要字段：

```json
{
  "host": "192.168.1.100",
  "hosts": ["192.168.1.100", "192.168.1.55"],
  "bitrate": 10000,
  "fps": 60,
  "mic": true,
  "autowatch": false
}
```

| 端口 | 协议 | 用途 |
|---|---|---|
| 47989 | TCP | 控制 / serverinfo（HTTP 入口） |
| 47984 | TCP | 配对与 applist（HTTPS） |
| 47990 | TCP | Sunshine Web 管理页 |
| 48010 | TCP/UDP | 串流数据 |
| 48050 | UDP | 本程序自有：`LMS1` 麦克风音频块 / `LMSJ` 订阅心跳 / `LMSQ` 停播信令 |

---

## 常见问题

**Q：学生机刚显示"配对成功"就闪退，然后一直重连？**
v1.3.4 修了两个根因：① 判断"是否已配对"曾读注册表 `hostname` 与 IP 比对（那里存的是对方**主机名**，
永远不等）→ 每次都误判未配对、反复清空有效配对；现已改回执行 `moonlight list <host>` 判定。
② 独占全屏（`--display-mode fullscreen`）在锁屏/远程会话下会被系统 WM_CLOSE，Moonlight 正常退出
被当成闪退 → 已改为 borderless（观感一样铺满、无标题栏）。

**Q：学生机 Moonlight 起不来，也没有日志？**
缺 VC++ 运行库（`VCRUNTIME140` / `MSVCP140`）时 Moonlight 直接 loader 失败。v1.3.3 起
`runtimes.zip` 会把 8 个 VC DLL 一起打进 `client\`（app-local），并用 `client\vcruntime140.dll`
作哨兵自动补装。若仍失败，看 `%LOCALAPPDATA%\LanStream\moonlight.log`（现已实时落盘，含退出码）。

**Q：启动后窗口"未响应"？**
v1.3.3 修掉了在 UI 线程里做阻塞 TCP 探活的问题（目标 IP 不通时每轮阻塞 1.75 s）。
现在探活在后台线程，20 次采样 0 次未响应。

**Q：开麦后教师机音响放出自己的声音？**
v1.3.1 起麦克风走独立 UDP 通道，不再回放到本地输出设备，正常不会有回声。
若听到回声，先确认没有同时开着旧版本实例（单实例机制会让新 exe 只是唤出旧窗口）。

**Q：教师端停播后学生端还停在"会话已结束"对话框？**
那是用 Moonlight 裸连的学生端。装本程序的学生端会收到停播信令（`LMSQ`）或探活失败后自动退出。

**Q：开播失败？**
先看 `%LOCALAPPDATA%\LanStream\sunshine.log` 尾部；确认 47984 / 47989 / 48010 未被其它程序占用，
教师机防火墙已放行（开播时会自动加规则，需要管理员权限）。

---

## 安全说明（重要）

本程序面向**可信局域网**，为易用性做了以下妥协，请勿暴露到公网：

- 串流与控制流量**未加密**（局域网明文）
- 学生端自动配对使用**固定 PIN `0000`**
- Sunshine Web 管理页使用**固定凭据**（`admin` / `admin1234`，见 `src\Resources\runtime_config\sunshine_state.json`），仅监听本机端口
- 开播时会修改系统 Sunshine 服务状态并添加防火墙规则（需管理员权限）

在教室/机房之外的网络使用前，请自行改掉 Web 凭据并限制端口访问范围。

---

## 第三方组件

见 [THIRD_PARTY.md](THIRD_PARTY.md)。核心是 [Sunshine](https://github.com/LizardByte/Sunshine)（GPL-3.0）与
[Moonlight](https://github.com/moonlight-stream/moonlight-qt)（GPL-3.0），运行时可执行文件随包分发，
源码与许可见各自仓库。

## 许可

本项目源码以 [GPL-3.0](LICENSE) 发布（因随包分发 GPL-3.0 的 Sunshine / Moonlight）。

## 免责声明

本项目与 Sunshine、Moonlight、NVIDIA、Microsoft 均无隶属关系。仅供教学与自建局域网使用，
使用前请确认符合所在单位与当地法律对屏幕监控、录屏、音频采集的规定。

---

<a id="english"></a>

# MoonlightLanStreamer (English)

**One-click LAN screen streaming for classrooms.** The teacher clicks **Start**, students click **Watch**,
and the teacher's desktop appears in seconds. Sunshine (host) and Moonlight (client) are bundled inside a
single EXE — nothing to install, no separate runtime download (the app targets .NET Framework 4.8, which
ships with Windows 10 1903+ / 11).

**Current version: v1.3.4** — see [CHANGELOG.md](CHANGELOG.md). The Windows product name shown in Explorer
is 「月光极速电子教室」(Chinese); the project itself is MoonlightLanStreamer.

### Features

**Host (teacher)**

- One-click start: stops the system Sunshine service → writes an optimized config → starts the host → opens firewall ports
- Bitrate / FPS dropdowns (default 10 Mb/s @ 60 FPS), persisted on change
- Live status (● not started / ● started / ● streaming) plus a staged progress bar
- Connected-client counter parsed from the Sunshine session log (refreshes within 1–2 s)
- Self-healing: a storm of `WSASendMsg 10055` (stuck local send queue → zombie session) restarts Sunshine automatically (90 s cooldown)
- System audio by default; optional microphone with a 30-segment level meter
- Optional "shutdown when finished"

**Client (student)**

- One-click watch with automatic pairing (fixed PIN `0000`, no PIN dialog)
- Target host by IP with a dropdown history, plus LAN discovery
- Auto-watch: continuously follows the target host (1.5 s probe) and enters/leaves the stream as the teacher starts/stops
- Reconnect with 8 / 16 / 30 s backoff; listens to the teacher's microphone only while watching

**Common**

- Bilingual UI (follows the system language), tray icon, single instance, no console windows,
  flat custom-drawn UI with PerMonitorV2 high-DPI support

### Audio design

Sunshine only captures the system playback device (WASAPI loopback) and its Mic channel is not implemented
on Windows. This app therefore ships the microphone over its **own UDP channel** (custom `LMS1` blocks on
port `48050`; broadcast from the host, then unicast after the client's `LMSJ` heartbeat). Nothing is played
back locally on the teacher machine, so there is **no feedback loop** and no need for "Listen to this device".
An `LMSQ` packet tells clients to leave when the teacher stops.

### Build

```powershell
winget install Microsoft.VisualStudio.2022.BuildTools -e --override `
  "--quiet --wait --norestart --add Microsoft.VisualStudio.Workload.ManagedDesktopBuildTools --add Microsoft.Net.Component.4.8.TargetingPack --includeRecommended"

cd src
powershell -ExecutionPolicy Bypass -File .\make_runtimes.ps1            # packs Sunshine+Moonlight into Resources\runtimes.zip
powershell -ExecutionPolicy Bypass -File .\build.ps1 -Version 1.3.4     # add -SkipSign to skip code signing
```

Output: `dist\月光极速电子教室-v1.3.4.exe` (~73 MB, runtimes embedded). You can also open
`src\MoonlightLanStreamer.csproj` in Visual Studio 2022 directly (classic project format).

### Configuration

Data directory: `%LOCALAPPDATA%\LanStream\` — `lanstream_state.json` (all UI settings),
`config\sunshine.conf`, `config\sunshine_state.json`, and the logs `sunshine.log`, `moonlight.log`,
`gui.log`, `startup.log`, `crash.log`.

Ports: 47989 TCP (control), 47984 TCP (pairing/applist), 47990 TCP (web UI), 48010 TCP/UDP (stream),
48050 UDP (this app: `LMS1` audio / `LMSJ` subscribe / `LMSQ` stop).

### Security

Intended for **trusted LANs only**: streams are unencrypted, pairing uses a fixed PIN `0000`, and the
Sunshine web UI uses fixed credentials (`admin` / `admin1234`). Do not expose it to the internet.

### License

[GPL-3.0](LICENSE) — the bundled Sunshine and Moonlight binaries are GPL-3.0 as well.
See [THIRD_PARTY.md](THIRD_PARTY.md).
