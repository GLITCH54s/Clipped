# Renders the widget package icons from the canonical smirk logo so the package
# matches the app icon: same artwork, transparent background, no plate.
Add-Type -AssemblyName System.Drawing
$root = Join-Path $PSScriptRoot 'Package'
$source = Join-Path (Split-Path $PSScriptRoot -Parent) 'docs\assets\logo.png'
New-Item -ItemType Directory -Force "$root\Assets" | Out-Null
New-Item -ItemType Directory -Force "$root\ProviderAssets" | Out-Null

function Save-Logo([int]$size, [string]$path) {
  $src = [System.Drawing.Image]::FromFile($source)
  try {
    $bmp = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CompositingMode = 'SourceCopy'
    $g.Clear([System.Drawing.Color]::Transparent)
    $g.CompositingMode = 'SourceOver'
    $g.InterpolationMode = 'HighQualityBicubic'
    $g.SmoothingMode = 'AntiAlias'
    $g.PixelOffsetMode = 'HighQuality'
    $g.DrawImage($src, 0, 0, $size, $size)
    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $g.Dispose(); $bmp.Dispose()
  } finally {
    $src.Dispose()
  }
}

Save-Logo 300 "$root\Assets\StoreLogo.png"
Save-Logo 150 "$root\Assets\Square150x150Logo.png"
Save-Logo 44  "$root\Assets\Square44x44Logo.png"
Save-Logo 64  "$root\ProviderAssets\Clipped_Icon.png"

# Screenshot: 300x150 mock of the card
$bmp = New-Object System.Drawing.Bitmap 300, 150
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.SmoothingMode = 'AntiAlias'
$g.TextRenderingHint = 'ClearTypeGridFit'
$g.Clear([System.Drawing.Color]::FromArgb(255, 32, 30, 46))
$white = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 240, 238, 250))
$grey = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 150, 146, 170))
$accentBrush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 139, 124, 246))
$fHead = New-Object System.Drawing.Font 'Segoe UI', 10, ([System.Drawing.FontStyle]::Bold)
$fBody = New-Object System.Drawing.Font 'Segoe UI', 8.5
$fSmall = New-Object System.Drawing.Font 'Segoe UI', 7
$g.DrawString('Recent clips', $fHead, $white, 14, 10)
$g.DrawString('Best regards, Your Name', $fBody, $white, 14, 40)
$g.DrawString('5 min ago', $fSmall, $grey, 14, 58)
$g.DrawString('12 Example Street, Apt 4', $fBody, $white, 14, 80)
$g.DrawString('1 h ago', $fSmall, $grey, 14, 98)
$g.FillRectangle($accentBrush, 14, 124, 90, 16)
$g.DrawString('Open Clipped', $fSmall, $white, 22, 124)
$bmp.Save("$root\ProviderAssets\Clipped_Screenshot.png", [System.Drawing.Imaging.ImageFormat]::Png)
$g.Dispose(); $bmp.Dispose()
Write-Host "assets written to $root"
