# Builds the standalone widget-provider package layout and optionally registers it.
#
#   powershell -File Widget\package.ps1                 # build + stage package-out\
#   powershell -File Widget\package.ps1 -Register       # ...and sideload it for testing
#
# -Register needs Developer Mode (Settings > System > For developers) or sideloading enabled.
# This package ships separately from the classic installer: the Widgets Board only loads
# widget providers that come from an MSIX package.
param(
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Debug',
    [switch]$Register
)

$ErrorActionPreference = 'Stop'
$widget = $PSScriptRoot
$build = Join-Path $widget "bin\$Configuration\net9.0-windows10.0.19041.0\win-x64"
$out = Join-Path $widget 'package-out'

$dotnet = if ($env:DOTNET_ROOT -and (Test-Path "$env:DOTNET_ROOT\dotnet.exe")) {
    "$env:DOTNET_ROOT\dotnet.exe"
} else { 'dotnet' }

& $dotnet build (Join-Path $widget 'ClippedWidget.csproj') -c $Configuration | Out-Host
if ($LASTEXITCODE -ne 0) { throw 'build failed' }

if (-not (Test-Path (Join-Path $widget 'Package\Assets\StoreLogo.png'))) {
    & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $widget 'make-assets.ps1') | Out-Host
}

if (Test-Path $out) { Remove-Item $out -Recurse -Force }
New-Item -ItemType Directory -Force $out | Out-Null

Copy-Item "$build\*" $out -Recurse -Force
Copy-Item (Join-Path $widget 'Package\AppxManifest.xml') $out -Force
Copy-Item (Join-Path $widget 'Package\Assets') (Join-Path $out 'Assets') -Recurse -Force
Copy-Item (Join-Path $widget 'Package\ProviderAssets') (Join-Path $out 'ProviderAssets') -Recurse -Force

# The Windows App SDK resource index must be named resources.pri inside an app package.
$pri = Join-Path $out 'ClippedWidget.pri'
if (Test-Path $pri) { Copy-Item $pri (Join-Path $out 'resources.pri') -Force }

Write-Host "layout: $out"

if ($Register) {
    Add-AppxPackage -Register (Join-Path $out 'AppxManifest.xml') -ForceUpdateFromAnyVersion
    Write-Host 'registered GLITCH54s.ClippedWidget - pin the widget from the Widgets board (Win+W)'
}
