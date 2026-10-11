# Renders the app icon (a soft gradient squircle with an arc) into a multi-size .ico.
param([string]$Out = "$PSScriptRoot\..\src\WinBrowser\Assets\WinBrowser.ico")

Add-Type -AssemblyName System.Drawing

function New-IconPng([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.Clear([System.Drawing.Color]::Transparent)

    $r = [single]($size * 0.22)
    $rect = New-Object System.Drawing.RectangleF ([single]($size * 0.04)), ([single]($size * 0.04)), ([single]($size * 0.92)), ([single]($size * 0.92))
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddArc($rect.X, $rect.Y, $r * 2, $r * 2, 180, 90)
    $path.AddArc($rect.Right - $r * 2, $rect.Y, $r * 2, $r * 2, 270, 90)
    $path.AddArc($rect.Right - $r * 2, $rect.Bottom - $r * 2, $r * 2, $r * 2, 0, 90)
    $path.AddArc($rect.X, $rect.Bottom - $r * 2, $r * 2, $r * 2, 90, 90)
    $path.CloseFigure()

    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush $rect, ([System.Drawing.Color]::FromArgb(255, 124, 92, 255)), ([System.Drawing.Color]::FromArgb(255, 56, 189, 248)), 45
    $g.FillPath($brush, $path)

    $penWidth = [single]([Math]::Max(1.5, $size * 0.09))
    $pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(245, 255, 255, 255)), $penWidth
    $pen.StartCap = 'Round'; $pen.EndCap = 'Round'
    $inset = [single]($size * 0.27)
    $g.DrawArc($pen, $inset, $inset, [single]($size - 2 * $inset), [single]($size - 2 * $inset), 160, 220)

    $g.Dispose()
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    return , $ms.ToArray()
}

$sizes = 16, 24, 32, 48, 64, 128, 256
$pngs = $sizes | ForEach-Object { , (New-IconPng $_) }

$fs = [System.IO.File]::Create((Resolve-Path -LiteralPath (Split-Path $Out)).Path + "\" + (Split-Path $Out -Leaf))
$w = New-Object System.IO.BinaryWriter $fs
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]; $len = $pngs[$i].Length
    $w.Write([byte]($(if ($s -ge 256) { 0 } else { $s })))
    $w.Write([byte]($(if ($s -ge 256) { 0 } else { $s })))
    $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([uint16]1); $w.Write([uint16]32)
    $w.Write([uint32]$len); $w.Write([uint32]$offset)
    $offset += $len
}
foreach ($p in $pngs) { $w.Write($p) }
$w.Dispose()
Write-Host "Wrote $Out"
