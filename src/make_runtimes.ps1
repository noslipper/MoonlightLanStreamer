# 把本机的 Sunshine + Moonlight 打包成 src\Resources\runtimes.zip（sunshine/、client/ 两个目录）。
# 该 zip 会被 build.ps1 作为嵌入资源打进 exe，首次运行时解压到 %LOCALAPPDATA%\LanStream\。
#
# 用法（在 src\ 目录下）：
#   powershell -ExecutionPolicy Bypass -File .\make_runtimes.ps1
param(
    [string]$SunshineDir = "C:\Program Files\Sunshine",
    [string]$ClientDir   = "C:\Program Files\Moonlight Game Streaming"
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.IO.Compression.FileSystem

$dir = Split-Path -Parent $MyInvocation.MyCommand.Path
$out = Join-Path $dir "Resources\runtimes.zip"

# 只保留串流真正需要的文件，剔除两类"从不调用"的大件：
#   - sunshine\tools\      dxgi-info.exe(57MB) / audio-info.exe / sunshinesvc.exe
#     本项目用 sc.exe 停服务 + 直接启动 sunshine.exe，运行期不会调用它们
#   - client\translations\ Moonlight 的 33 种 Qt 翻译，只留英文
$keepTranslations = @("qt_en.qm")
$vcNames = @("msvcp140.dll", "msvcp140_1.dll", "msvcp140_2.dll",
             "msvcp140_atomic_wait.dll", "msvcp140_codecvt_ids.dll",
             "vcruntime140.dll", "vcruntime140_1.dll", "concrt140.dll")

New-Item -ItemType Directory -Force -Path (Split-Path -Parent $out) | Out-Null
if (Test-Path -LiteralPath $out) { Remove-Item -LiteralPath $out -Force }
if (Test-Path -LiteralPath ($out + ".tmp")) { Remove-Item -LiteralPath ($out + ".tmp") -Force }

$zipStream = [System.IO.File]::Open(($out + ".tmp"), [System.IO.FileMode]::Create)
$zip = New-Object System.IO.Compression.ZipArchive($zipStream, [System.IO.Compression.ZipArchiveMode]::Create)
$count = 0

function Add-Tree($rootDir, $prefix) {
    if (-not (Test-Path -LiteralPath $rootDir)) {
        Write-Host "跳过（不存在）: $rootDir" -ForegroundColor Yellow
        return 0
    }
    $n = 0
    $rootDir = (Resolve-Path -LiteralPath $rootDir).Path
    $stack = New-Object System.Collections.Stack
    $stack.Push($rootDir)
    while ($stack.Count -gt 0) {
        $base = $stack.Pop()
        $rel = $base.Substring($rootDir.Length).TrimStart('\', '/')
        $relLower = $rel.ToLower()

        # 不入包的目录：config（机器相关）、sunshine\tools（诊断工具，运行期不用）
        foreach ($d in (Get-ChildItem -LiteralPath $base -Directory -ErrorAction SilentlyContinue)) {
            if ($d.Name -ieq "config") { continue }
            if ($prefix -eq "sunshine" -and $rel -eq "" -and $d.Name -ieq "tools") { continue }
            $stack.Push($d.FullName)
        }

        $inTranslations = ($relLower -eq "translations")
        foreach ($f in (Get-ChildItem -LiteralPath $base -File -ErrorAction SilentlyContinue)) {
            $name = $f.Name
            if ($name -match '\.(log(\.\d+)?|lock)$') { continue }
            if ($inTranslations -and ($keepTranslations -notcontains $name.ToLower())) { continue }
            $arc = if ($rel) { "$prefix/$rel/$name" } else { "$prefix/$name" }
            [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
                $zip, $f.FullName, $arc.Replace('\', '/'),
                [System.IO.Compression.CompressionLevel]::Optimal) | Out-Null
            $n++
        }
    }
    return $n
}

try {
    foreach ($pair in @(@($SunshineDir, "sunshine"), @($ClientDir, "client"))) {
        $c = Add-Tree $pair[0] $pair[1]
        Write-Host ("{0} -> {1}/ ({2} files)" -f $pair[0], $pair[1], $c)
        $count += $c
    }

    # VC++ 运行库（app-local 部署）：Moonlight 是 MSVC/Qt 编译，缺了
    # VCRUNTIME140/MSVCP140 在干净机器上会直接 loader 失败（弹框卡住、无日志）
    $sys32 = Join-Path $env:SystemRoot "System32"
    foreach ($n in $vcNames) {
        $src = Join-Path $sys32 $n
        if (Test-Path -LiteralPath $src) {
            [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
                $zip, $src, "client/$n",
                [System.IO.Compression.CompressionLevel]::Optimal) | Out-Null
            $count++
            Write-Host "VC runtime -> client/$n"
        }
    }
}
finally {
    $zip.Dispose()
    $zipStream.Dispose()
}

Move-Item -LiteralPath ($out + ".tmp") -Destination $out -Force
$mb = [math]::Round((Get-Item -LiteralPath $out).Length / 1MB, 1)
Write-Host ""
Write-Host ("runtimes.zip = {0} MB, {1} files" -f $mb, $count)
Write-Host ("输出: " + $out)
