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

需要 **Visual Studio 2019/2022**（勾选“.NET 桌面开发”工作负载），或
**.NET Framework 4.8 Developer Pack**（提供 MSBuild / csc）。

```powershell
# 在 windows\ 目录下
powershell -ExecutionPolicy Bypass -File .\build.ps1 -Version 1.3.0
```

也可以直接用 VS 打开 `MoonClassLanStream.csproj`（经典项目格式，无需转换）后 F5 / 生成。

产物目录：`dist\cs-v1.3.0\`

```
月光极速电子教室-v1.3.0.exe    ← 主程序（几百 KB）
runtimes.zip                  ← Sunshine + Moonlight 运行时（72 MB，由 python make_runtimes.py 生成）
runtime_config\                ← apps.json + Sunshine Web 凭据模板
icon.ico                       ← 窗口/托盘图标
```

`runtimes.zip` 由仓库根目录的 `python make_runtimes.py` 生成；也可删掉它改由程序内嵌
（把 zip 放到 `windows\Resources\` 下，csproj 会自动作为嵌入资源，但 exe 会变成 ~75 MB）。

---

## 与 Python 版的功能对齐

| 功能 | 实现位置 |
|---|---|
| 一键开播（写配置 → 停服务 → 放行端口 → 启动 Sunshine → PIN 守护） | `SunCore.cs` |
| 在线客户端计数（解析 sunshine.log 的 CLIENT CONNECTED/DISCONNECTED） | `SunCore.ClientCount()` |
| 一键收看（自动配对 → 串流） | `ClientCore.cs` |
| 音频：系统声音串流 + 麦克风混入 + 横向电平条 | `MicAudio.cs` |
| 中英双语（跟随系统语言，`LANSTREAM_LANG` 可强制） | `I18n.cs` |
| 扁平化自绘控件（按钮/开关/徽章/电平条） | `Ui.cs` |
| 单实例 + 唤醒、系统托盘、开机自动收看 | `Program.cs`、`TrayIcon.cs`、`MainForm.cs` |

## 说明

- 界面布局与 Python 版逐行对齐：**视频参数在上、音频在下**，左右卡片共用同一套行高，
  “开播/停止”与“一键收看”严格水平对齐。
- 麦克风混入的原理与 Python 版相同：winmm 采集麦克风 → 实时回放到默认输出设备 →
  Sunshine 的 loopback 一并串流（等价系统“侦听此设备”，但只在程序内生效）。**建议用耳麦**，否则会啸叫。
- 界面用纯代码构建（无 XAML），自绘控件全部 GDI+ 抗锯齿绘制，支持 PerMonitorV2 高 DPI。
