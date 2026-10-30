# 月光极速电子教室（C# / .NET Framework 4.8 版）—— 一键构建脚本
# 用法（在 windows\ 目录下）：
#   powershell -ExecutionPolicy Bypass -File .\build.ps1 -Version 1.3.0
#
# 前置：Windows 10/11 自带 .NET Framework 4.8 运行时；编译需要下列任一项：
#   - Visual Studio 2019/2022（含 ".NET 桌面开发" 工作负载）
#   - Visual Studio Build Tools + ".NET Framework 4.8 SDK / 目标包"
#   - .NET Framework 4.8 Developer Pack（提供 csc/msbuild）
param(
    [string]$Version = "1.3.1",
    [string]$Configuration = "Release",
    [string]$MSBuild = "",
    [switch]$SkipSign
)

$ErrorActionPreference = "Stop"
$dir = Split-Path -Parent $MyInvocation.MyCommand.Path
$root = Split-Path -Parent $dir

function Find-MSBuild {
    if ($MSBuild -and (Test-Path -LiteralPath $MSBuild)) { return $MSBuild }
    $patterns = @(
        "$env:ProgramFiles\Microsoft Visual Studio\2022\*\MSBuild\Current\Bin\MSBuild.exe",
        "${env:ProgramFiles(x86)}\Microsoft Visual Studio\2022\*\MSBuild\Current\Bin\MSBuild.exe",
        "$env:ProgramFiles\Microsoft Visual Studio\2019\*\MSBuild\Current\Bin\MSBuild.exe",
        "${env:ProgramFiles(x86)}\Microsoft Visual Studio\2019\*\MSBuild\Current\Bin\MSBuild.exe",
        "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
    )
    foreach ($p in $patterns) {
        $hit = Get-ChildItem -Path $p -ErrorAction SilentlyContinue | Select-Object -First 1
        if (-not $hit) { continue }
        if ($hit.Name -eq "vswhere.exe") {
            $found = & $hit.FullName -latest -products * -requires Microsoft.Component.MSBuild `
                      -find "MSBuild\**\Bin\MSBuild.exe" 2>$null | Select-Object -First 1
            if ($found -and (Test-Path -LiteralPath $found)) { return $found }
            continue
        }
        return $hit.FullName
    }
    foreach ($p in @(
        "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\MSBuild.exe",
        "$env:WINDIR\Microsoft.NET\Framework\v4.0.30319\MSBuild.exe")) {
        if (Test-Path -LiteralPath $p) { return $p }
    }
    return $null
}

$msbuildExe = Find-MSBuild
if (-not $msbuildExe) {
    Write-Host "未找到 MSBuild。请安装以下任一项后重试：" -ForegroundColor Yellow
    Write-Host "  - Visual Studio 2022（.NET 桌面开发工作负载）"
    Write-Host "  - .NET Framework 4.8 Developer Pack"
    exit 1
}
Write-Host "使用 MSBuild: $msbuildExe"

Push-Location $dir
try {
    & $msbuildExe "$dir\MoonClassLanStream.csproj" `
        /p:Configuration=$Configuration /p:Platform=AnyCPU `
        /p:Version=$Version /p:FileVersion=$Version /p:AssemblyVersion=$Version `
        /v:minimal /nologo
    if ($LASTEXITCODE -ne 0) { Write-Error "编译失败（MSBuild 退出码 $LASTEXITCODE）" }
}
finally { Pop-Location }

$outDir = Join-Path $dir "bin\$Configuration"
$exe = Join-Path $outDir "MoonClassLanStream.exe"
if (-not (Test-Path -LiteralPath $exe)) { Write-Error "未生成 $exe" }

# 组装交付：单个 exe 直接放 dist 根目录
$dist = Join-Path $root "dist"
New-Item -ItemType Directory -Force -Path $dist | Out-Null
$target = Join-Path $dist "月光极速电子教室-v$Version.exe"
Copy-Item -LiteralPath $exe -Destination $target -Force

$embedded = Join-Path $dir "Resources\runtimes.zip"
if (Test-Path -LiteralPath $embedded) {
    # runtimes.zip 已作为嵌入资源打进 exe（单文件分发）
    Write-Host "运行时已内嵌进 exe（Resources\runtimes.zip，$([math]::Round((Get-Item $embedded).Length / 1MB, 1)) MB）"
}
else {
    $zip = Join-Path $root "runtimes.zip"
    if (Test-Path -LiteralPath $zip) {
        Copy-Item -LiteralPath $zip -Destination $dist -Force
        Write-Host "提示：运行时以外置 runtimes.zip 分发（复制 72 MB）" -ForegroundColor Yellow
    }
    else {
        Write-Host "提示：未找到 runtimes.zip，运行 python make_runtimes.py 生成后再分发" -ForegroundColor Yellow
    }
}

# ---- 数字签名（代码签名证书 + 时间戳）----
if (-not $SkipSign) {
    try {
        $cert = Get-ChildItem Cert:\CurrentUser\My -CodeSigningCert -ErrorAction SilentlyContinue |
                Sort-Object NotAfter -Descending | Select-Object -First 1
        if (-not $cert) {
            Write-Host "未找到代码签名证书，创建自签名证书（CN=MoonClass，有效期 3 年）..."
            $cert = New-SelfSignedCertificate -Type CodeSigningCert -Subject "CN=MoonClass, O=MoonClass, C=CN" `
                -KeyUsage DigitalSignature -KeyAlgorithm RSA -KeyLength 2048 -HashAlgorithm SHA256 `
                -NotAfter (Get-Date).AddYears(3) -CertStoreLocation Cert:\CurrentUser\My
            $cert = Get-ChildItem ("Cert:\CurrentUser\My\" + $cert.Thumbprint)
        }
        Write-Host ("签名证书: " + $cert.Subject + "（指纹 " + $cert.Thumbprint + "）")
        $sig = Set-AuthenticodeSignature -FilePath $target -Certificate $cert `
            -TimestampServer "http://timestamp.digicert.com" -HashAlgorithm SHA256
        Write-Host ("签名状态: " + $sig.Status + " " + $sig.StatusMessage)
        if ($sig.Status -ne "Valid") { Write-Host "警告：签名未通过校验，exe 仍会交付（未签名状态）" -ForegroundColor Yellow }
    }
    catch {
        Write-Host ("签名失败（不影响交付）: " + $_.Exception.Message) -ForegroundColor Yellow
    }
}

Write-Host ""
Write-Host "== 构建完成 =="
Get-ChildItem -LiteralPath $dist | ForEach-Object {
    $mb = if ($_.PSIsContainer) { "" } else { "  ($([math]::Round($_.Length / 1MB, 1)) MB)" }
    Write-Host ("  " + $_.Name + $mb)
}
Write-Host "输出目录: $dist"
