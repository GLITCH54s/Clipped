# Renders the MSIX logo assets from the app's 512px logo.
#   powershell -File installer\msix\make-assets.ps1
param([string]$Source)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$root = $PSScriptRoot
$out = Join-Path $root 'Assets'
if (-not $Source) {
    $repo = Resolve-Path (Join-Path $root '..\..')
    foreach ($candidate in @(
            (Join-Path $repo 'docs\assets\logo-512.png'),
            (Join-Path $repo 'docs\assets\logo-256.png'),
            (Join-Path $repo 'assets\Clipped.ico'))) {
        if (Test-Path $candidate) { $Source = $candidate; break }
    }
}
if (-not $Source) { throw 'no logo source found' }

New-Item -ItemType Directory -Force $out | Out-Null

# Keep the source open for the whole run; ICO files load a frame via the Icon ctor below.
$icon = $null
$image = $null
if ($Source -match '\.ico$') {
    $icon = New-Object System.Drawing.Icon($Source, 256, 256)
    $image = $icon.ToBitmap()
} else {
    $image = [System.Drawing.Image]::FromFile((Resolve-Path $Source))
}

function Save-Logo([int]$size, [string]$name) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)
    $g.DrawImage($image, 0, 0, $size, $size)
    $g.Dispose()
    $bmp.Save((Join-Path $out $name), [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
}

Save-Logo 300 'StoreLogo.png'
Save-Logo 44 'Square44x44Logo.png'
Save-Logo 150 'Square150x150Logo.png'
Save-Logo 310 'Square310x310Logo.png'

$image.Dispose()
if ($icon) { $icon.Dispose() }
Write-Host "MSIX assets written to $out from $Source"
