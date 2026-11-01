# Full build: MSYS2 deps -> UxPlay engine -> .NET GUI, staged in dist\AirServer
# Usage: powershell -ExecutionPolicy Bypass -File scripts\build.ps1 [-SkipDeps]
param([switch]$SkipDeps)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$msys = Join-Path $PSScriptRoot 'msys.ps1'

if (-not (Test-Path C:\msys64\usr\bin\bash.exe)) {
    Write-Host '== installing MSYS2 (winget)'
    winget install --id MSYS2.MSYS2 -e --accept-source-agreements --accept-package-agreements --silent
}
# UxPlay is pinned to the exact commit this app was built and tested against.
$uxplayCommit = 'f40b121b6b932fb8ed138ce2e492b0a77cf284a3'
$vendor = Join-Path $root 'vendor\UxPlay'
if (-not (Test-Path (Join-Path $vendor 'uxplay.cpp'))) {
    git -c core.autocrlf=false init -q $vendor
    git -C $vendor config core.autocrlf false
    git -C $vendor fetch -q --depth 1 https://github.com/FDH2/UxPlay $uxplayCommit
    git -C $vendor checkout -q FETCH_HEAD
}
if (-not $SkipDeps) {
    & powershell -NoProfile -ExecutionPolicy Bypass -File $msys (Join-Path $PSScriptRoot 'setup-msys2.sh')
    if ($LASTEXITCODE) { throw 'MSYS2 package install failed' }
}
& powershell -NoProfile -ExecutionPolicy Bypass -File $msys (Join-Path $PSScriptRoot 'build-uxplay.sh')
if ($LASTEXITCODE) { throw 'UxPlay build failed' }

Write-Host '== building GUI'
# Self-contained: target PCs do not need the .NET runtime installed.
dotnet publish (Join-Path $root 'src\AirServer\AirServer.csproj') -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=None -o (Join-Path $root 'dist\AirServer')
if ($LASTEXITCODE) { throw 'dotnet publish failed' }
Copy-Item (Join-Path $root 'uninstall.ps1') (Join-Path $root 'dist\AirServer\uninstall.ps1') -Force

Write-Host '== packaging release zip'
$rel = Join-Path $root 'release'
New-Item -ItemType Directory -Force $rel | Out-Null
$zip = Join-Path $rel 'AirServer-win-x64.zip'
Remove-Item $zip -EA SilentlyContinue
Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::CreateFromDirectory((Join-Path $root 'dist\AirServer'), $zip, 'Optimal', $false)
$hash = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLower()
Set-Content -Path "$zip.sha256" -Value "$hash  AirServer-win-x64.zip" -Encoding ascii -NoNewline
Write-Host ("   {0} ({1:N0} MB) sha256 {2}" -f $zip, ((Get-Item $zip).Length / 1MB), $hash)
Write-Host "== done: $(Join-Path $root 'dist\AirServer\AirServer.exe')"
