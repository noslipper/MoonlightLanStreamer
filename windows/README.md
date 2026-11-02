# 月光极速电子教室 · C# 版（.NET Framework 4.8）

用 **Windows 自带的 .NET Framework 4.8 + WinForms** 重写的版本：目标机**不需要安装任何运行时**
（Win10 1903+ / Win11 出厂自带），编译产物只有几百 KB，剩下的体积全部是随包的
Sunshine / Moonlight 运行时（`runtimes.zip`，约 72 MB）。

与 Python 版**共用同一套数据目录**（`%LOCALAPPDATA%\LanStream`），包括：

- 已释放的 `sunshine\`、`client\` 运行时（两个版本可互用，不用重复解压 72 MB）
- `lanstream_state.json`（主机、码率、帧率、麦克风、开机自动收看等设置）
- `config\sunshine.conf`、`config\sunshine_state.json`、`sunshine.log`

---

## 编译

已实测通过的环境：**VS 2022 Build Tools 17.14 + .NET Framework 4.8 目标包**（本机已安装并成功构建）。

任选其一安装即可：

```powershell
# 方式 A：Build Tools（体积小、无 IDE，推荐给只编译的机器）
winget install Microsoft.VisualStudio.2022.BuildTools -e --override `
  "--quiet --wait --norestart --add Microsoft.VisualStudio.Workload.ManagedDesktopBuildTools --add Microsoft.Net.Component.4.8.TargetingPack --includeRecommended"

# 方式 B：Visual Studio 2022（勾选“.NET 桌面开发”工作负载）
# 方式 C：.NET Framework 4.8 Developer Pack（只提供 4.8 引用程序集，
#         还需系统自带的老版 MSBuild，路径见 build.ps1 的兜底查找）
```

> 常见坑：VS 安装器在有**同时设置** `HTTPS_PROXY` 和 `https_proxy` 的机器上会直接报 5002
> （.NET 的字典大小写不敏感，重复键崩溃）。装之前 `unset https_proxy http_proxy` 即可。

```powershell
# 在 windows\ 目录下
powershell -ExecutionPolicy Bypass -File .\build.ps1 -Version 1.3.0
```

也可以直接用 VS 打开 `MoonClassLanStream.csproj`（经典项目格式，无需转换）后 F5 / 生成。

产物目录：`dist\cs-v1.3.0\`

```
月光极速电子教室-v1.3.0.exe    ← 主程序（~72 MB，运行时已内嵌，单文件分发）
runtime_config\                ← apps.json + Sunshine Web 凭据模板
icon.ico                       ← 窗口/托盘图标
```

**单文件分发**：只要 `windows\Resources\runtimes.zip` 存在，构建脚本就会把它作为嵌入资源
打进 exe（不再复制外置 zip）。首次运行时在后台线程把运行时解压到
`%LOCALAPPDATA%\LanStream\`（实测 1.1 s，不阻塞窗口出现）。删除 `Resources\runtimes.zip`
则回退为“exe + 外置 runtimes.zip”的分发方式。

---

## 实测结果（本机 v1.3.0 构建产物）

| 项目 | 结果 |
|---|---|
| 编译 | 0 错误 0 警告（MSBuild /v:minimal 全量重建） |
| 产物体积 | 主程序 **108 KB**；运行时另放 `runtimes.zip` 75.8 MB |
| 启动 | 双击到窗口出现 **1.04 s**（onefile Python 版为 3~8 s） |
| 开播 | 点击后 **2.4 s** 端口 47989 就绪，`sunshine.exe` 正常运行 |
| 在线客户端 | 学生端连上后 **6 s 内**状态栏显示「● 正在直播 · 已连接 1 台客户端」，按钮同步为「直播中 · 1 台」 |
| 停止 | 点击后端口关闭、徽章回「未开播」、日志完整记录 |
| 单实例 | 二次启动后窗口仍只有 1 个、进程仍 1 个（第二个进程自动退出） |
| 中英双语 | 系统语言 / `LANSTREAM_LANG=zh\|en` 均正确切换，英文窗口 880px 无截断 |
| 电平条刷新 | 诊断模式（`LANSTREAM_METER_TEST=1` 合成扫幅）下 12 帧采样有 9 帧不同，亮格像素 238→2380 连续变化 |

### 关于电平条「不动」

先确认是不是没信号：本机麦克风实测 RMS 为 0（Python 侧同样读 0.000），电平条正确地停在 0 格。
控件本身的刷新已修好：原来用固定差值阈值 0.004 判断是否重绘，底噪附近的变化几乎全被吃掉，
看起来就像卡住。现在改成**按点亮的格数变化触发重绘**——格数变了才画，既省 CPU，说话时也一定会跳。
另外采集线程若因设备热插拔退出，会每 90 ms 自动重连一次。

想看电平条本身有没有坏，可用合成信号驱动（不依赖麦克风）：设置环境变量
`LANSTREAM_METER_TEST=1` 后启动程序即可。

想看真实电平数值（排查麦克风没声音时），设 `LANSTREAM_DEBUG=1` 启动，日志区每秒输出
一行 `[dbg] mic level=… dev=… playback=…`。若一直是 `0.000`，说明采集链路正常但设备
没给数据——检查系统「声音 → 输入」里该设备是否被静音、音量是否为 0。程序也会在持续
约 10 秒无输入时于日志提示一次。

### 自动跟随教室端（开机自动收看开关的新语义）

「开机自动收看」开关打开后不再只是启动时连一次，而是**持续跟随目标主机**（每 1.5 秒轮询）：

- 目标主机 Sunshine 端口出现（教室端开播）→ 自动发起收看（已配对则直接串流，未配对自动配对）
- 目标主机端口消失（教室端停播）→ 自动结束本机 Moonlight

开关关闭则一切手动。注意：只对**装了本程序的学生端**生效；用 Moonlight 裸连的学生
在教室端停播时只会看到"会话已结束"对话框。

### winmm 缓冲区标志位的坑（重要）

波形缓冲完成后**绝不能把 `dwFlags` 清 0** 再重新入队——`WHDR_PREPARED` 被清掉后，
`waveInAddBuffer` / `waveOutWrite` 会返回 `WAVERR_UNPREPARED`(33) 且从此全部失败，
采集在**前两个缓冲（约 160 ms）后永久停止**：症状正是"电平表不动 + 麦克风没有声音
串出去"。正确做法是保留 PREPARED，只重置 `dwBufferLength` / `dwBytesRecorded`，
让驱动在重新入队时自行清 DONE/INQUEUE。实测修复前后：3 秒完成缓冲 2 个 → 37 个。

## 与 Python 版的功能对齐

| 功能 | 实现位置 |
|---|---|
| 一键开播（写配置 → 停服务 → 放行端口 → 启动 Sunshine → PIN 守护） | `SunCore.cs` |
| 在线客户端计数（解析 sunshine.log 的 CLIENT CONNECTED/DISCONNECTED） | `SunCore.ClientCount()` |
| 一键收看（自动配对 → 串流） | `ClientCore.cs` |
| 自动跟随教室端：开播自动进入收看、停播自动退出（受「开机自动收看」开关控制） | `MainForm.FollowTeacher()` |
| 音频：系统声音串流 + 麦克风混入 + 横向电平条 | `MicAudio.cs` |
| 中英双语（跟随系统语言，`LANSTREAM_LANG` 可强制） | `I18n.cs` |
| 扁平化自绘控件（按钮/开关/徽章/电平条） | `Ui.cs` |
| 单实例 + 唤醒、系统托盘、开机自动收看 | `Program.cs`、`TrayIcon.cs`、`MainForm.cs` |

## 说明

- 界面布局与 Python 版逐行对齐：**视频参数在上、音频在下**，左右卡片共用同一套行高，
  “开播/停止”与“一键收看”严格水平对齐。
- 左右两卡的每一行都用 `Filled(控件, Margin)` 包成结构完全相同的整行容器（内容一律顶对齐），
  避免某一侧把 Label 直接塞进表格单元格后垂直居中、比另一侧低 3px 的错位。
- 开播帧率默认 **60 FPS**（配置里改过则沿用配置）。
- 麦克风混入的原理与 Python 版相同：winmm 采集麦克风 → 实时回放到默认输出设备 →
  Sunshine 的 loopback 一并串流（等价系统“侦听此设备”，但只在程序内生效）。**建议用耳麦**，否则会啸叫。
- 界面用纯代码构建（无 XAML），自绘控件全部 GDI+ 抗锯齿绘制，支持 PerMonitorV2 高 DPI。
