# 第三方组件与许可声明

月光极速电子教室（MoonClass LanStream）在运行时**内嵌并调用**以下开源组件。
仓库源码本身不重复分发它们的二进制，构建时由 `make_runtimes.py` 从本机安装目录打包。

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

## 3. Material Design Icons（界面图标）

- 项目：https://materialdesignicons.com/ （素材经 Iconify API 获取 SVG）
- 用途：按钮与托盘菜单图标；构建期用 `resvg` 栅格化为 96px alpha 遮罩，
  结果以 base64 形式内嵌在 `icon_data.py`，运行时仅做缩放与染色
- 许可：**Apache License 2.0**

## 4. Pillow（图像处理）

- 项目：https://python-pillow.org/
- 用途：图标缩放/染色、圆角与进度条等界面绘制
- 许可：**HPND License**（Pillow Software License）

## 5. pywin32（Windows API 封装）

- 项目：https://github.com/mhammond/pywin32
- 用途：系统托盘（Shell_NotifyIcon）、窗口置前、菜单位图
- 许可：**PSF-2.0**

## 6. Python / Tcl-Tk

- Python：https://www.python.org/ （PSF License）
- Tcl/Tk：https://www.tcl.tk/ （BSD-style License）
- 用途：运行时与图形界面

---

## 关于整体分发许可

由于打包产物包含 GPL-3.0 的 Sunshine 与 Moonlight，**整体分发须遵循 GPL-3.0**：
分发二进制时需同时提供对应源代码（含上游 Sunshine / Moonlight 源码获取方式与本项目源码）。
详见仓库根目录 `LICENSE`。

若你只使用本仓库源码（不打包上游二进制），源码本身亦以 GPL-3.0 发布。

---

## 商标声明

Sunshine、Moonlight、NVIDIA、AMD、Intel 等名称与商标归各自所有者所有。
本项目为第三方作品，与上述组织无隶属或背书关系。

---
---

# Third-Party Components and Licenses

MoonClass LanStream **embeds and invokes** the following open-source components at runtime.
The repository source does not redistribute their binaries; the build packs them from your local
installation directory via `make_runtimes.py`.

## 1. Sunshine (streaming server)

- Project: https://github.com/LizardByte/Sunshine
- Use: bundled as the server runtime (`sunshine/sunshine.exe`); launched by this app with a generated config
- License: **GNU GPL v3.0**
- Notes: invoked through command-line arguments and local HTTP/HTTPS endpoints (47984 / 47989 / 47990);
  its source is not modified. Window titles, logs and network identifiers come from the upstream implementation.

## 2. Moonlight (streaming client)

- Project: https://github.com/moonlight-stream/moonlight-qt
- Use: bundled as the client runtime (`client/Moonlight.exe`); renders the received stream
- License: **GNU GPL v3.0**
- Notes: driven through the `Moonlight.exe pair / stream / list` subcommands; its source is not modified.

## 3. Material Design Icons (UI icons)

- Project: https://materialdesignicons.com/ (SVG assets fetched via the Iconify API)
- Use: button and tray menu icons; rasterized to 96px alpha masks with `resvg` at build time and
  embedded as base64 in `icon_data.py`; only scaled and tinted at runtime
- License: **Apache License 2.0**

## 4. Pillow (image processing)

- Project: https://python-pillow.org/
- Use: icon scaling/tinting, rounded shapes and progress bar rendering
- License: **HPND License** (Pillow Software License)

## 5. pywin32 (Windows API bindings)

- Project: https://github.com/mhammond/pywin32
- Use: system tray (`Shell_NotifyIcon`), window foregrounding, menu bitmaps
- License: **PSF-2.0**

## 6. Python / Tcl-Tk

- Python: https://www.python.org/ (PSF License)
- Tcl/Tk: https://www.tcl.tk/ (BSD-style License)
- Use: runtime and graphical interface

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

Sunshine, Moonlight, NVIDIA, AMD, Intel and other names and trademarks belong to their respective
owners. This is a third-party project with no affiliation with or endorsement by those organizations.
