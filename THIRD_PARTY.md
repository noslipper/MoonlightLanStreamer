# 第三方组件与许可声明

MoonlightLanStreamer（Windows 桌面产品名：月光极速电子教室）在运行时**内嵌并调用**以下开源组件。
仓库源码本身不重复分发它们的二进制，构建时由 `src/make_runtimes.ps1` 从本机安装目录打包。

---

## 1. Sunshine（串流服务端）

- 项目：https://github.com/LizardByte/Sunshine
- 用途：被内置为服务端运行时（`sunshine/sunshine.exe`），开播时由本程序启动并传入生成的配置
- 许可：**GNU GPL v3.0**
- 说明：本项目通过命令行参数与本地 HTTP/HTTPS 接口（47984 / 47989 / 47990）调用它，
  未修改其源码。窗口标题、日志与网络标识等以其原始实现为准。

## 2. Moonlight（串流客户端）

- 项目：https://github.com/moonlight-stream/moonlight-qt
- 用途：被内置为客户端运行时（`client/Moonlight.exe`），收看的画面由它渲染
- 许可：**GNU GPL v3.0**
- 说明：本项目通过 `Moonlight.exe pair / stream / list` 子命令与其交互，未修改其源码。

## 3. .NET Framework 4.8

- 项目：https://dotnet.microsoft.com/download/dotnet-framework
- 用途：运行框架（WinForms / GDI+ / System.Net / System.IO.Compression）
- 许可：随 Windows 分发，本项目不分发其运行时

## 4. Windows API（系统自带）

| 组件 | 用途 |
|---|---|
| winmm | 麦克风采集（waveIn）与播放（waveOut） |
| iphlpapi / ws2_32 | 网卡枚举、UDP 收发（`LMS1` / `LMSJ` / `LMSQ`） |
| advapi32 | 注册表（音频设备名、Moonlight 主机项）、服务控制 |
| user32 / shell32 / kernel32 | 窗口、系统托盘、进程与单实例互斥体 |

## 5. 图标

- 应用图标 `src/Resources/icon.ico` 为本项目自制素材，随仓库以 GPL-3.0 发布。

## 6. Moonlight 内含的第三方库

Moonlight 自身包含 Qt、SDL、FFmpeg 等组件，Sunshine 包含 Boost、FFmpeg、MiniUPnP 等组件，
其许可以上游各自声明为准，随上游一并适用。

---

## 关于整体分发许可

由于打包产物包含 GPL-3.0 的 Sunshine 与 Moonlight，**整体分发须遵循 GPL-3.0**：
分发二进制时需同时提供对应源代码（含上游 Sunshine / Moonlight 源码获取方式与本项目源码）。
详见仓库根目录 `LICENSE`。

若你只使用本仓库源码（不打包上游二进制），源码本身亦以 GPL-3.0 发布。

---

## 商标声明

Sunshine、Moonlight、NVIDIA、AMD、Intel、Microsoft 等名称与商标归各自所有者所有。
本项目为第三方作品，与上述组织无隶属或背书关系。

---
---

# Third-Party Components and Licenses

MoonlightLanStreamer **embeds and invokes** the following open-source components at runtime.
The repository source does not redistribute their binaries; the build packs them from your local
installation directory via `src/make_runtimes.ps1`.

## 1. Sunshine (streaming server)

- Project: https://github.com/LizardByte/Sunshine
- Use: bundled as the server runtime (`sunshine/sunshine.exe`); launched by this app with a generated config
- License: **GNU GPL v3.0**
- Notes: invoked through command-line arguments and local HTTP/HTTPS endpoints (47984 / 47989 / 47990);
  its source is not modified.

## 2. Moonlight (streaming client)

- Project: https://github.com/moonlight-stream/moonlight-qt
- Use: bundled as the client runtime (`client/Moonlight.exe`); renders the received stream
- License: **GNU GPL v3.0**
- Notes: driven through the `Moonlight.exe pair / stream / list` subcommands; its source is not modified.

## 3. .NET Framework 4.8

- Project: https://dotnet.microsoft.com/download/dotnet-framework
- Use: runtime framework (WinForms / GDI+ / System.Net / System.IO.Compression)
- License: distributed with Windows; this project does not redistribute the runtime

## 4. Windows API (system components)

winmm (microphone capture/playback), iphlpapi & ws2_32 (adapter enumeration, UDP transport),
advapi32 (registry, service control), user32 / shell32 / kernel32 (windows, tray, processes, mutex).

## 5. Icon

`src/Resources/icon.ico` is original artwork by this project, released under GPL-3.0 with the repository.

## 6. Libraries inside Moonlight / Sunshine

Moonlight bundles Qt, SDL and FFmpeg; Sunshine bundles Boost, FFmpeg and MiniUPnP. Their licenses
apply as declared upstream.

---

## About the combined distribution license

Because the packaged build contains the GPL-3.0 Sunshine and Moonlight, **the combined distribution
must comply with GPL-3.0**: when distributing binaries you must also provide the corresponding source
code (including how to obtain the upstream Sunshine / Moonlight sources and this project's source).
See `LICENSE` in the repository root.

If you only use this repository's source (without packaging the upstream binaries), the source itself
is also released under GPL-3.0.

---

## Trademarks

Sunshine, Moonlight, NVIDIA, AMD, Intel, Microsoft and other names and trademarks belong to their
respective owners. This is a third-party project with no affiliation with or endorsement by them.
