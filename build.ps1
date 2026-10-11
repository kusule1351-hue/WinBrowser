# Builds WinBrowser end to end:
#   1. downloads the CEF binary distribution (once) into third_party\cef
#   2. builds the native CEF host (WinBrowserCore.dll + WinBrowser.Helper.exe) with CMake
#   3. builds the WinUI 3 shell, which copies the native output next to WinBrowser.exe
#
#   .\build.ps1            # Release build
#   .\build.ps1 -Run       # build and launch
param(
    [ValidateSet("Release", "Debug")] [string]$Configuration = "Release",
    [switch]$Run
)
$ErrorActionPreference = "Stop"
$root = $PSScriptRoot

# ---- 1. CEF -----------------------------------------------------------------------
$cefVersion = "154.0.34+g14c5a08+chromium-154.0.8037.98"
$cefSha1 = "be54bbaf3222191dec59ffd9c9ec7e8a7f44a26d"
$cefDir = Join-Path $root "third_party\cef"
if (-not (Test-Path (Join-Path $cefDir "include\cef_version.h"))) {
    $name = "cef_binary_${cefVersion}_windows64_minimal"
    $url = "https://cef-builds.spotifycdn.com/" + [Uri]::EscapeDataString("$name.tar.bz2")
    $archive = Join-Path $root "third_party\cef.tar.bz2"
    New-Item -ItemType Directory -Force (Split-Path $archive) | Out-Null
    Write-Host "Downloading CEF $cefVersion ..."
    curl.exe -L --fail -o $archive $url
    if ((Get-FileHash $archive -Algorithm SHA1).Hash -ne $cefSha1) { throw "CEF archive checksum mismatch" }
    tar -xjf $archive -C (Split-Path $archive)
    Move-Item (Join-Path $root "third_party\$name") $cefDir
    Remove-Item $archive
}

# ---- 2. Native host ---------------------------------------------------------------
$cmake = (Get-Command cmake -ErrorAction SilentlyContinue).Source
if (-not $cmake) {
    $vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
    $vs = & $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
    $cmake = Join-Path $vs "Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin\cmake.exe"
}
$obj = Join-Path $root "build\native\obj"
& $cmake -S (Join-Path $root "native") -B $obj -G "Visual Studio 17 2022" -A x64
if ($LASTEXITCODE) { throw "CMake configure failed" }
& $cmake --build $obj --config $Configuration -- /m /v:minimal
if ($LASTEXITCODE) { throw "Native build failed" }

# ---- 3. WinUI shell ---------------------------------------------------------------
$proj = Join-Path $root "src\WinBrowser\WinBrowser.csproj"
dotnet build $proj -c $Configuration -p:Platform=x64
if ($LASTEXITCODE) { throw "WinUI build failed" }

$exe = Join-Path $root "src\WinBrowser\bin\x64\$Configuration\net10.0-windows10.0.26100.0\win-x64\WinBrowser.exe"
Write-Host "`nBuilt $exe" -ForegroundColor Green
if ($Run) { Start-Process $exe }
