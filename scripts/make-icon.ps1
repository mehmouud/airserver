# Generates src/AirServer/app.ico (PNG-compressed multi-size ICO) with System.Drawing.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$out = Join-Path (Split-Path $PSScriptRoot -Parent) 'src\AirServer\app.ico'

function RoundRect([System.Drawing.Drawing2D.GraphicsPath]$p, [float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
    $d = 2 * $r
    $p.AddArc($x, $y, $d, $d, 180, 90); $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90); $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $p.CloseFigure()
}

function Render([int]$s) {
    $bmp = New-Object System.Drawing.Bitmap $s, $s
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'; $g.Clear([System.Drawing.Color]::Transparent)
    $k = $s / 256.0
    # background tile
    $bg = New-Object System.Drawing.Drawing2D.GraphicsPath; RoundRect $bg (8*$k) (8*$k) (240*$k) (240*$k) (52*$k)
    $br = New-Object System.Drawing.Drawing2D.LinearGradientBrush ([System.Drawing.PointF]::new(0,0)), ([System.Drawing.PointF]::new($s,$s)), ([System.Drawing.Color]::FromArgb(255,0,122,255)), ([System.Drawing.Color]::FromArgb(255,88,86,214))
    $g.FillPath($br, $bg)
    $white = [System.Drawing.Color]::White
    $pen = New-Object System.Drawing.Pen $white, ([Math]::Max(1.5, 12*$k))
    # monitor
    $mon = New-Object System.Drawing.Drawing2D.GraphicsPath; RoundRect $mon (52*$k) (60*$k) (152*$k) (104*$k) (12*$k)
    $g.DrawPath($pen, $mon)
    $g.FillRectangle((New-Object System.Drawing.SolidBrush $white), [float](108*$k), [float](164*$k), [float](40*$k), [float](20*$k))
    $g.FillRectangle((New-Object System.Drawing.SolidBrush $white), [float](84*$k), [float](182*$k), [float](88*$k), [float](12*$k))
    # phone, overlapping bottom-right
    $ph = New-Object System.Drawing.Drawing2D.GraphicsPath; RoundRect $ph (150*$k) (98*$k) (64*$k) (112*$k) (14*$k)
    $g.FillPath((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255,40,60,200))), $ph)
    $g.DrawPath($pen, $ph)
    $g.Dispose()
    $ms = New-Object System.IO.MemoryStream; $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
    return ,$ms.ToArray()
}

$sizes = 16, 24, 32, 48, 64, 128, 256
$imgs = foreach ($s in $sizes) { ,(Render $s) }
$fs = [System.IO.File]::Create($out); $w = New-Object System.IO.BinaryWriter $fs
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]; $b = if ($s -ge 256) { 0 } else { $s }
    $w.Write([byte]$b); $w.Write([byte]$b); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([uint16]1); $w.Write([uint16]32); $w.Write([uint32]$imgs[$i].Length); $w.Write([uint32]$offset)
    $offset += $imgs[$i].Length
}
foreach ($img in $imgs) { $w.Write($img) }
$w.Close()
Write-Host "icon written: $out"
