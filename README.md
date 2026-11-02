# 月光极速电子教室（MoonClass LanStream）

> 面向机房的**一键局域网串流**教学软件：教师机点「一键开播」，学生机点「一键收看」，
> 数秒内看到教师屏幕。内置 **Sunshine（服务端）+ Moonlight（客户端）**，单个 EXE，双击即用，无需安装。

![界面预览](docs/screenshot.png)

---

## 这是什么

基于开源 [Sunshine](https://github.com/LizardByte/Sunshine)（服务端）与
[Moonlight](https://github.com/moonlight-stream/moonlight-qt)（客户端）二次开发的桌面程序，
把原本需要手工配置的「配对 / 防火墙 / 编码参数 / 多客户端」流程全部自动化，
面向局域网教学场景做了取舍：**低延迟优先、局域网明文串流、免手工配对**。

- Python + tkinter 编写，PyInstaller 打包为单文件 EXE（内置两个运行时，首次运行自动释放）
- 已修复/规避以下常见坑：`bind_address` 导致 502、`-0` 参数导致配对会话 408、
  `PIPE` 未读取导致配对进程阻塞、证书与私钥不匹配导致 mTLS 失败、PyInstaller 下托盘句柄无效等

---

## 功能特性

### 开播（教师机）
| 功能 | 说明 |
|---|---|
| 一键开播 | 自动停掉系统 Sunshine 服务 → 写入优化配置 → 启动服务端 → 放行防火墙 |
| 码率 / 帧率设置 | 下拉可选，也可手动输入，默认 **10 Mb/s + 60 FPS**，自动写入配置文件 |
| 状态指示 | ● 未开播 / ● 已开播 / ● 正在直播（按真实端口与活动连接判断） |
| 开播进度条 | 状态栏实时显示开播阶段与百分比（检查环境 → 写配置 → 同步证书 → 放行端口 → 启动服务端 → 等待就绪） |
| 客户端计数 | 开播后状态栏实时显示「已连接 N 台客户端」 |

### 收看（学生机）
| 功能 | 说明 |
|---|---|
| 一键收看 | 自动配对（固定 PIN `0000`，无需人工输入）→ 直接进入画面 |
| 目标主机 | IP 输入框支持手动输入 + 下拉历史（自动记忆上次检测到的主机） |
| 一键检测 | 扫描局域网内已开播主机，加入下拉列表 |
| 开机自动收看 | 开关默认关闭，开启后启动软件即自动连接并收看，设置写入配置文件 |

### 通用
- **系统托盘常驻**：单击呼出/最小化，右键菜单（开播/停止/收看/退出）
- **单实例运行**：重复打开不会启动第二份，而是把已运行窗口弹到最前
- **后台无弹窗**：开播与配对过程全程无命令行黑框、无 Moonlight 配对窗口，只有连接成功才出现画面
- **设置全部持久化**：主机、历史主机、码率、帧率、开机自动收看均写入配置文件
- 极简扁平 UI：直角卡片 / 按钮，日志区 + 状态栏

---

## 系统要求

- **Windows 10 / 11**（x64）
- 教师机建议带硬件编码能力的 GPU（NVIDIA NVENC / AMD AMF / Intel QSV；默认配置优先使用 NVENC）
- 学生机无需任何额外安装
- 首次运行会请求一次管理员权限（用于接管 Sunshine 服务与开放防火墙端口）

---

## 快速开始

1. 运行 `月光极速电子教室-vX.Y.Z.exe`
2. 教师机点 **「一键开播」**，状态栏变为 `● 已开播`
3. 学生机在「目标主机 IP」填教师机 IP（或点「检测」自动发现），点 **「一键收看」**

> 教师机「本机 IP」与状态栏右下角都会显示本机地址，可直接口播给学生。

---

## 从源码构建

```powershell
# 1) 依赖（Python 3.12 x64，必须带 Tcl/Tk；pywin32 用于托盘与窗口操作）
winget install --id Python.Python.3.12 --scope user
python -m pip install pyinstaller pillow pywin32

# 2) 准备运行时：本机需已安装 Sunshine 与 Moonlight（默认路径见 make_runtimes.py），
#    把它们打包成 runtimes.zip（sunshine/ + client/）
python make_runtimes.py

# 3) 一键构建（生成图标 → 打包运行时 → PyInstaller 单文件）
powershell -ExecutionPolicy Bypass -File .\build_exe.ps1 -Version 1.2.0
```

产物：`dist\月光极速电子教室-v1.2.0.exe`

**注意**：`runtimes.zip`（约 94 MB）不随仓库分发，需由 `make_runtimes.py` 从本机已安装的
Sunshine / Moonlight 生成。仓库**不包含任何私钥/证书**：首次开播时 Sunshine 会在
`%LOCALAPPDATA%\LanStream\config\credentials\` 自动生成一套本机证书，每台机器独立。

源码运行（调试）：`python lanstream_app.py`。

---

## 配置文件

运行时数据目录：

- 打包版：`%LOCALAPPDATA%\LanStream\`
- 源码运行：项目下 `.runtime\`

| 文件 | 用途 |
|---|---|
| `lanstream_state.json` | 界面全部设置（目标主机、主机历史、码率、帧率、开机自动收看） |
| `config\sunshine.conf` | 每次开播时按界面设置生成的 Sunshine 配置 |
| `config\sunshine_state.json` | Sunshine 自身状态（含已配对设备 `named_devices`、Web 凭据） |
| `sunshine.log` | 服务端日志（排查开播问题先看这里） |

`lanstream_state.json` 主要字段：

```json
{
  "host": "192.168.110.20",
  "hosts": ["192.168.110.20", "192.168.1.55"],
  "bitrate_mbps": 10.0,
  "bitrate": 10000,
  "fps": 60,
  "autowatch": false
}
```

### 端口

| 端口 | 协议 | 用途 |
|---|---|---|
| 47989 | TCP | 控制 / serverinfo（HTTP 入口） |
| 47984 | TCP | 配对与 applist（HTTPS） |
| 47990 | TCP | Sunshine Web 管理页 |
| 48010 | TCP/UDP | 串流数据 |

开播时程序会自动为以上端口添加防火墙入站规则（仅私有/域网络）。

---

## 目录结构

```
lanstream_app.py      # 主界面（tkinter）+ 单实例 + 进度条 + 托盘联动
lanstream_core.py     # 核心：Sunshine 配置/启动、配对、串流、防火墙、状态与客户端计数
lanstream_ui.py       # 系统托盘（Win32 原生，右键菜单带图标）
icons.py              # 图标染色/缩放（MDI 矢量图标遮罩）
icon_data.py          # 图标 alpha 遮罩（base64，构建期由 SVG 栅格化生成）
icon.ico              # 应用/窗口/托盘图标
make_icon.py          # 生成 icon.ico
make_runtimes.py      # 把本机 Sunshine + Moonlight 打包成 runtimes.zip
build_exe.ps1         # 一键构建单文件 EXE
runtime_config/       # 基础配置（apps/凭据哈希）；证书在首次开播时由 Sunshine 自动生成
docs/screenshot.png   # 界面截图
scratch/              # 开发期临时脚本（已 .gitignore，可随时删除）
```

---

## 常见问题

**Q：学生机一直停在“需要输入 PIN”？**
配对由教师机自动完成（固定 PIN `0000`）。请确认：教师机已开播、双方在同一局域网、
教师机防火墙已放行 47984-48010（点一次开播会自动添加规则）。

**Q：托盘图标找不到了？**
Windows 11 会把新图标的托盘项收进右下角 `^` 隐藏区，拖到任务栏即可常显。
关闭窗口只是最小化到托盘，可从托盘菜单「退出」真正结束程序。

**Q：开播后状态栏显示“已开播，等待学生端连接”？**
说明服务端已就绪但还没有客户端连接，属于正常待连状态；学生端连上后会显示「正在直播 · 已连接 N 台客户端」。

**Q：重复双击图标没反应？**
程序是单实例运行的：第二次打开会把已运行的窗口弹到最前，而不会启动第二份。

**Q：想改默认码率/帧率？**
直接在「码率 (Mb/s)」「帧率 (FPS)」下拉或手动输入，开播/收看时生效并写入配置文件。

---

## 第三方组件

本项目在运行时**内嵌并调用**以下开源组件（仓库本身不重复分发其二进制）：

| 组件 | 用途 | 许可 |
|---|---|---|
| [Sunshine](https://github.com/LizardByte/Sunshine) | 串流服务端 | GPL-3.0 |
| [Moonlight](https://github.com/moonlight-stream/moonlight-qt) | 串流客户端 | GPL-3.0 |
| [Material Design Icons](https://materialdesignicons.com/) | 界面/菜单图标 | Apache-2.0 |
| [Pillow](https://python-pillow.org/) | 图像处理 | HPND |
| [pywin32](https://github.com/mhammond/pywin32) | Win32 托盘/窗口 | PSF-2.0 |

详见 [THIRD_PARTY.md](THIRD_PARTY.md)。

## 许可

本项目以 **GPL-3.0** 发布（见 [LICENSE](LICENSE)）。因其分发/捆绑了 GPL-3.0 的 Sunshine 与
Moonlight，整体分发须遵循 GPL-3.0；如需商用或闭源分发，请自行评估并遵守上游许可。

## 免责声明

本项目仅用于**自有设备的局域网屏幕共享与教学演示**。请勿用于未经授权的远程控制或屏幕采集；
使用者需自行确保符合所在学校/单位的信息安全规定及当地法律法规。
