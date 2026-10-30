# 月光极速电子教室 —— 一键构建脚本（交付单文件 EXE）
# 用法：
#   powershell -ExecutionPolicy Bypass -File .\build_exe.ps1 -Version 1.3.2
# 前置：Python 3.12 x64（需自带 Tcl/Tk）、已安装 Sunshine 与 Moonlight（供 make_runtimes.py 收集运行时）
param(
    [string]$Python  = "",
    [string]$Version = "1.3.2"
)
$ErrorActionPreference = "Stop"

# 1) 定位带 Tcl/Tk 的 Python（优先 3.12）
if (-not $Python) {
    $cands = @(
        "$env:LOCALAPPDATA\Programs\Python\Python312\python.exe",
        "C:\Program Files\Python312\python.exe",
        "$env:LOCALAPPDATA\Programs\Python\Python313\python.exe"
    )
    foreach ($c in $cands) { if (Test-Path -LiteralPath $c) { $Python = $c; break } }
}
if (-not $Python) {
    Write-Error "未找到 Python，请先: winget install --id Python.Python.3.12 --scope user"
}
Write-Host "使用 Python: $Python"

$py  = $Python
$dir = Split-Path -Parent $MyInvocation.MyCommand.Path
Push-Location $dir
try {
    # 2) 安装依赖（pywin32 用于托盘/窗口操作，Pillow 用于图标与界面绘制）
    #    离线/代理环境 pip 可能失败：仅提示，不中断构建（依赖已装即可继续）
    try {
        & $py -m pip install --quiet --upgrade pyinstaller pillow pywin32 2>$null | Out-Null
    }
    catch {
        Write-Host "提示：pip 更新依赖失败，继续使用已安装版本"
    }

    # 3) 图标：icon.ico 为设计稿（16~256 多尺寸），构建时绝不能覆盖；
    #    仅在文件缺失时兜底生成一个
    if (-not (Test-Path -LiteralPath "$dir\icon.ico")) {
        & $py make_icon.py
    }

    # 4) 将 Sunshine + Moonlight 运行时打包为 runtimes.zip
    #    （首次运行时解压到 %LOCALAPPDATA%\LanStream，之后秒启动）
    & $py make_runtimes.py
    $zip = "$dir\runtimes.zip"
    if (-not (Test-Path -LiteralPath $zip)) {
        Write-Error "runtimes.zip 生成失败：请确认已安装 Sunshine 与 Moonlight（见 make_runtimes.py 中的路径）"
    }

    # 5) 构建单文件 EXE（窗口模式，无控制台）
    $addData = @(
        "--add-data", "$zip;.",
        "--add-data", "$dir\runtime_config;runtime_config",
        "--add-data", "$dir\icon.ico;."
    )
    $exeName = "月光极速电子教室-v$Version"

    & $py -m PyInstaller --noconfirm --clean --onefile --windowed `
        --name $exeName `
        --icon "$dir\icon.ico" `
        @addData `
        --distpath "$dir\dist" --workpath "$dir\build" --specpath "$dir" `
        "$dir\lanstream_app.py"
}
finally {
    Pop-Location
}

Write-Host ""
Write-Host "== 构建完成 =="
$exe = "$dir\dist\$exeName.exe"
if (Test-Path -LiteralPath $exe) {
    $size = [math]::Round((Get-Item -LiteralPath $exe).Length / 1MB, 1)
    Write-Host "产物: $exe  ($size MB)"
} else {
    Write-Error "构建失败，未生成 EXE"
}
