# Builds the Store-ready MSIX for Clipped (dashboard + widget provider in one package).
#
#   powershell -File installer\msix\build-msix.ps1                  # package only
#   powershell -File installer\msix\build-msix.ps1 -Register        # ...and sideload it (Developer Mode)
#   powershell -File installer\msix\build-msix.ps1 -PfxPath dev.pfx -PfxPassword pw   # sign for local testing
#   powershell -File installer\msix\build-msix.ps1 -NoWidget -IdentityName 12345Dev.Clipped `
#       -Publisher 'CN=00000000-0000-0000-0000-000000000000' -PublisherDisplayName 'Your Name' -Version 1.0.0.0
#
# -NoWidget produces the app-only package shape the store lane tracks (no widget COM server and no
# Windows App Runtime dependency); MinVersion defaults to 10.0.19041.0 either way.
#
# Store submission: upload the unsigned .msix. Microsoft signs Store packages, which is what keeps
# SmartScreen and Defender quiet for users - do not ship the signed test build to the Store.
# Set -IdentityName/-Publisher/-PublisherDisplayName from Partner Center > Product identity and
# bump -Version for every upload. Defaults are the local placeholders the store lane uses.
#
# unvirtualizedResources is a restricted capability (needed so %APPDATA%\Clipped is not virtualized).
# Partner Center asks for a short justification during certification; keep it to "user data must
# survive uninstall and stay readable by the user".
param(
    [ValidateSet('Release', 'Debug')][string]$Configuration = 'Release',
    [string]$Version,
    [string]$StageDir,
    [string]$IdentityName = 'Clipped',
    [string]$Publisher = 'CN=Clipped OSS',
    [string]$PublisherDisplayName = 'Clipped OSS',
    [string]$MinVersion = '10.0.19041.0',
    [switch]$NoWidget,
    [string]$PfxPath,
    [string]$PfxPassword,
    [switch]$Register,
    [switch]$SkipPublish
)

$ErrorActionPreference = 'Stop'
$msixDir = $PSScriptRoot
$repo = (Resolve-Path (Join-Path $msixDir '..\..')).Path

# Scratch lives next to the repo, never inside it.
if (-not $StageDir) {
    $StageDir = Join-Path (Split-Path $repo -Parent) 'build\clipped-msix-full'
}

if (-not $Version) {
    $declared = (Select-String -Path (Join-Path $repo 'Clipped.csproj') -Pattern '<Version>([0-9.]+)</Version>').Matches
    if (-not $declared) { throw 'could not read <Version> from Clipped.csproj' }
    $Version = $declared[0].Groups[1].Value
    $parts = $Version.Split('.')
    $Version = ($parts + @('0', '0', '0'))[0..3] -join '.'
}

$dotnet = $null
if ($env:DOTNET_ROOT -and (Test-Path "$env:DOTNET_ROOT\dotnet.exe")) {
    $dotnet = "$env:DOTNET_ROOT\dotnet.exe"
} else {
    $onPath = (Get-Command dotnet -ErrorAction SilentlyContinue)
    # A dotnet without an SDK still exits 0 on --list-sdks, so check for output.
    if ($onPath -and (& $onPath.Source --list-sdks 2>$null)) { $dotnet = $onPath.Source }
}
if (-not $dotnet -and (Test-Path 'C:\Program Files\dotnet\dotnet.exe')) { $dotnet = 'C:\Program Files\dotnet\dotnet.exe' }
if (-not $dotnet) { throw 'dotnet SDK not found (set DOTNET_ROOT)' }

$appDir = Join-Path $StageDir 'app'
$pkg = Join-Path $StageDir 'pkg'
$dist = Join-Path $StageDir 'dist'

New-Item -ItemType Directory -Force $StageDir, $dist | Out-Null

if (-not $SkipPublish) {
    Write-Host "==> publishing Clipped ($Configuration, win-x64, self-contained)"
    if (Test-Path $appDir) { Remove-Item $appDir -Recurse -Force }
    & $dotnet publish (Join-Path $repo 'Clipped.csproj') -c $Configuration -r win-x64 --self-contained true `
        -o $appDir -p:DebugType=none -v q -nologo | Out-Host
    if ($LASTEXITCODE -ne 0) { throw 'publish failed' }
}

if (-not (Test-Path (Join-Path $appDir 'Clipped.exe'))) { throw "missing $appDir\Clipped.exe" }

$widgetOut = $null
if (-not $NoWidget) {
    Write-Host "==> building widget provider"
    & $dotnet build (Join-Path $repo 'Widget\ClippedWidget.csproj') -c $Configuration -v q -nologo | Out-Host
    if ($LASTEXITCODE -ne 0) { throw 'widget build failed' }
    $widgetOut = Join-Path $repo "Widget\bin\$Configuration\net9.0-windows10.0.19041.0\win-x64"
    if (-not (Test-Path (Join-Path $widgetOut 'ClippedWidget.exe'))) { throw "missing $widgetOut\ClippedWidget.exe" }
}

if (-not (Test-Path (Join-Path $msixDir 'Assets\Square150x150Logo.png'))) {
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $msixDir 'make-assets.ps1')
}

Write-Host '==> staging package layout'
if (Test-Path $pkg) { Remove-Item $pkg -Recurse -Force }
New-Item -ItemType Directory -Force $pkg | Out-Null

Copy-Item "$appDir\*" $pkg -Recurse -Force
if (-not $NoWidget) {
    New-Item -ItemType Directory -Force (Join-Path $pkg 'Widget') | Out-Null
    Copy-Item "$widgetOut\*" (Join-Path $pkg 'Widget') -Recurse -Force
    Get-ChildItem (Join-Path $pkg 'Widget') -Recurse -Include *.pdb, *.xml | Remove-Item -Force
    Copy-Item (Join-Path $repo 'Widget\Package\ProviderAssets') (Join-Path $pkg 'ProviderAssets') -Recurse -Force
}
Copy-Item (Join-Path $msixDir 'Assets') (Join-Path $pkg 'Assets') -Recurse -Force

# Patch the manifest as XML: a regex over Version="..." would also hit PackageDependency MinVersion.
$doc = New-Object System.Xml.XmlDocument
$doc.PreserveWhitespace = $true
$doc.Load((Join-Path $msixDir 'Package.appxmanifest'))

$identity = $doc.SelectSingleNode("/*[local-name()='Package']/*[local-name()='Identity']")
if (-not $identity) { throw 'Identity element missing from Package.appxmanifest' }
$identity.SetAttribute('Name', $IdentityName)
$identity.SetAttribute('Publisher', $Publisher)
$identity.SetAttribute('Version', $Version)

$display = $doc.SelectSingleNode("/*[local-name()='Package']/*[local-name()='Properties']/*[local-name()='PublisherDisplayName']")
if ($display) { $display.InnerText = $PublisherDisplayName }

$family = $doc.SelectSingleNode("//*[local-name()='TargetDeviceFamily']")
if ($family) { $family.SetAttribute('MinVersion', $MinVersion) }

if ($NoWidget) {
    foreach ($node in @($doc.SelectNodes("//*[local-name()='Extension']"))) {
        if ($node.Category -in @('windows.comServer', 'windows.appExtension')) { [void]$node.ParentNode.RemoveChild($node) }
    }
    foreach ($node in @($doc.SelectNodes("//*[local-name()='PackageDependency']"))) {
        [void]$node.ParentNode.RemoveChild($node)
    }
}

$doc.Save((Join-Path $pkg 'AppxManifest.xml'))

# The widget's MRT index must sit next to its exe inside the package.
if (-not $NoWidget) {
    $pri = Join-Path $pkg 'Widget\ClippedWidget.pri'
    if (Test-Path $pri) { Copy-Item $pri (Join-Path $pkg 'Widget\resources.pri') -Force }
}

if ($Publisher -eq 'CN=Clipped OSS') {
    Write-Warning 'Publisher is still the local placeholder (CN=Clipped OSS); Partner Center rejects Store uploads until it is replaced with the account Publisher ID.'
}

$makeappx = $null
foreach ($kit in Get-ChildItem 'C:\Program Files (x86)\Windows Kits\10\bin' -Directory -ErrorAction SilentlyContinue |
        Sort-Object Name -Descending) {
    $candidate = Join-Path $kit.FullName 'x64\makeappx.exe'
    if (Test-Path $candidate) { $makeappx = $candidate; break }
}
if (-not $makeappx) {
    $found = Get-ChildItem (Join-Path $env:USERPROFILE '.nuget\packages\microsoft.windows.sdk.buildtools') `
        -Recurse -Filter makeappx.exe -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -match '\\x64\\' } | Sort-Object FullName -Descending | Select-Object -First 1
    if ($found) { $makeappx = $found.FullName }
}
if (-not $makeappx) { throw 'MakeAppx.exe not found (install the Windows SDK or restore Microsoft.Windows.SDK.BuildTools)' }

$suffix = if ($NoWidget) { '_x64_nowidget' } else { '_x64' }
$msix = Join-Path $dist "Clipped_${Version}${suffix}.msix"
if (Test-Path $msix) { Remove-Item $msix -Force }

Write-Host '==> packing'
& $makeappx pack /o /d $pkg /p $msix | Out-Host
if ($LASTEXITCODE -ne 0) { throw 'MakeAppx failed' }

if ($PfxPath) {
    $signtool = Get-ChildItem (Join-Path $env:USERPROFILE '.nuget\packages\microsoft.windows.sdk.buildtools') `
        -Recurse -Filter signtool.exe -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -match '\\x64\\' } | Sort-Object FullName -Descending | Select-Object -First 1
    if (-not $signtool) { throw 'signtool.exe not found' }
    Write-Host '==> signing'
    & $signtool.FullName sign /fd SHA256 /f $PfxPath /p $PfxPassword $msix | Out-Host
    if ($LASTEXITCODE -ne 0) { throw 'signing failed' }
}

if ($Register) {
    Add-AppxPackage -Register (Join-Path $pkg 'AppxManifest.xml') -ForceUpdateFromAnyVersion
    Write-Host 'registered the Clipped package (dev layout)'
}

Write-Host "msix:     $msix"
Write-Host "identity: Name=$IdentityName Publisher=$Publisher Version=$Version MinVersion=$MinVersion widget=$(-not $NoWidget)"
