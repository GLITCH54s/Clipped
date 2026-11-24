Add-Type -AssemblyName System.Drawing
$root = Join-Path $PSScriptRoot 'Package'
New-Item -ItemType Directory -Force "$root\Assets" | Out-Null
New-Item -ItemType Directory -Force "$root\ProviderAssets" | Out-Null

function New-Icon([int]$size, [string]$path) {
  $bmp = New-Object System.Drawing.Bitmap $size, $size
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.SmoothingMode = 'AntiAlias'
  $g.Clear([System.Drawing.Color]::Transparent)
  $pad = [Math]::Max(1, [int]($size * 0.06))
  $accent = [System.Drawing.Color]::FromArgb(255, 139, 124, 246)
  $bg = [System.Drawing.Color]::FromArgb(255, 26, 23, 43)
  $r = [int]($size * 0.22)
  $path2 = New-Object System.Drawing.Drawing2D.GraphicsPath
  $rect = New-Object System.Drawing.RectangleF($pad, $pad, ($size - 2 * $pad), ($size - 2 * $pad))
  $d = $r * 2
  $path2.AddArc($rect.X, $rect.Y, $d, $d, 180, 90)
  $path2.AddArc($rect.Right - $d, $rect.Y, $d, $d, 270, 90)
  $path2.AddArc($rect.Right - $d, $rect.Bottom - $d, $d, $d, 0, 90)
  $path2.AddArc($rect.X, $rect.Bottom - $d, $d, $d, 90, 90)
  $path2.CloseFigure()
  $g.FillPath((New-Object System.Drawing.SolidBrush $bg), $path2)

  # clipboard body
  $bw = $size * 0.44; $bh = $size * 0.52
  $bx = ($size - $bw) / 2; $by = $size * 0.27
  $body = New-Object System.Drawing.RectangleF($bx, $by, $bw, $bh)
  $pen = New-Object System.Drawing.Pen($accent, ([Math]::Max(2, $size * 0.055)))
  $bp = New-Object System.Drawing.Drawing2D.GraphicsPath
  $br = [int]($size * 0.07)
  $bd = $br * 2
  $bp.AddArc($body.X, $body.Y, $bd, $bd, 180, 90)
  $bp.AddArc($body.Right - $bd, $body.Y, $bd, $bd, 270, 90)
  $bp.AddArc($body.Right - $bd, $body.Bottom - $bd, $bd, $bd, 0, 90)
  $bp.AddArc($body.X, $body.Bottom - $bd, $bd, $bd, 90, 90)
  $bp.CloseFigure()
  $g.DrawPath($pen, $bp)

  # clip on top
  $cw = $size * 0.22; $ch = $size * 0.09
  $clip = New-Object System.Drawing.RectangleF((($size - $cw) / 2), ($size * 0.22), $cw, $ch)
  $g.FillRectangle((New-Object System.Drawing.SolidBrush $accent), $clip)

  $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
  $g.Dispose(); $bmp.Dispose()
}

New-Icon 50 "$root\Assets\StoreLogo.png"
New-Icon 150 "$root\Assets\Square150x150Logo.png"
New-Icon 44 "$root\Assets\Square44x44Logo.png"
New-Icon 64 "$root\ProviderAssets\Clipped_Icon.png"

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
