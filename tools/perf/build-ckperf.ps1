<#
  build-ckperf.ps1 — rebuilds the injected 32-bit diagnostic layer and refreshes the
  copy that CKToolkit.exe embeds.

  Why this is a script and not part of CKToolkit.sln: the solution is built with
  `dotnet build`, which cannot build a vcxproj. Rather than force everyone who touches
  the C# side to install an MSVC toolset, the DLL is built here and the result is
  checked in at assets/ckperf/ckperf.dll. The C# build just embeds that file.

  So the workflow after changing anything under src/CKPerf/ is:

      pwsh tools/perf/build-ckperf.ps1
      dotnet build CKToolkit.sln

  Forgetting the first step means CKToolkit keeps shipping the previous DLL, which is
  exactly the kind of silent staleness that wastes a debugging session. The script
  prints the resulting file hash so a mismatch is visible.
#>

[CmdletBinding()]
param(
    [ValidateSet('Release', 'Debug')]
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'

$repo    = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$vcxproj = Join-Path $repo 'src\CKPerf\CKPerf.vcxproj'
$asset   = Join-Path $repo 'assets\ckperf\ckperf.dll'

if (-not (Test-Path $vcxproj)) { throw "找不到 $vcxproj" }

$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
if (-not (Test-Path $vswhere)) {
    throw "找不到 vswhere.exe。請安裝 Visual Studio 並勾選「使用 C++ 的桌面開發」工作負載。"
}

$msbuild = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild `
                      -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1
if (-not $msbuild) {
    throw "vswhere 找不到 MSBuild。請安裝 Visual Studio 的 C++ 建置工具。"
}

Write-Host "MSBuild : $msbuild"
Write-Host "設定    : $Configuration|Win32"
Write-Host ""

# Win32 is not negotiable: the target process is a 2004 32-bit build.
& $msbuild $vcxproj /p:Configuration=$Configuration /p:Platform=Win32 /v:minimal /nologo
if ($LASTEXITCODE -ne 0) { throw "CKPerf 建置失敗 (exit $LASTEXITCODE)" }

$built = Join-Path $repo "dist\ckperf\$Configuration\ckperf.dll"
if (-not (Test-Path $built)) { throw "建置成功但找不到輸出：$built" }

New-Item -ItemType Directory -Force -Path (Split-Path -Parent $asset) | Out-Null
Copy-Item $built $asset -Force

$hash = (Get-FileHash $asset -Algorithm SHA256).Hash
$size = (Get-Item $asset).Length

Write-Host ""
Write-Host "已更新 $asset"
Write-Host "  大小   $size bytes"
Write-Host "  SHA256 $hash"
Write-Host ""
Write-Host "接著執行 dotnet build CKToolkit.sln 讓 CKToolkit.exe 內嵌這一版。"
