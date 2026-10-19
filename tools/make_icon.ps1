# Рисует значок браузера (снежинка на тёмно-синем квадрате) и собирает Assets\app.ico + Assets\icon-256.png
param([string]$OutDir = (Join-Path $PSScriptRoot '..\Assets'))

Add-Type -AssemblyName System.Drawing
$sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256
$frames = @()

function Draw-Icon([int]$s) {
    $bmp = New-Object System.Drawing.Bitmap $s, $s
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality

    # скруглённый квадрат с градиентом
    $r = [Math]::Max(3.0, $s * 0.23)
    $d = $r * 2
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddArc(0, 0, $d, $d, 180, 90)
    $path.AddArc($s - $d - 0.5, 0, $d, $d, 270, 90)
    $path.AddArc($s - $d - 0.5, $s - $d - 0.5, $d, $d, 0, 90)
    $path.AddArc(0, $s - $d - 0.5, $d, $d, 90, 90)
    $path.CloseFigure()
    $p1 = New-Object System.Drawing.PointF 0, 0
    $p2 = New-Object System.Drawing.PointF $s, $s
    $c1 = [System.Drawing.Color]::FromArgb(255, 0x27, 0x4B, 0x78)
    $c2 = [System.Drawing.Color]::FromArgb(255, 0x0F, 0x18, 0x26)
    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush $p1, $p2, $c1, $c2
    $g.FillPath($brush, $path)

    # снежинка
    $width = [Math]::Max(1.4, $s * 0.072)
    $pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(255, 0xD4, 0xEC, 0xFF)), $width
    $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $c = $s / 2.0
    $R = $s * 0.34
    for ($i = 0; $i -lt 6; $i++) {
        $a = [Math]::PI / 2 + $i * [Math]::PI / 3
        $ex = $c + $R * [Math]::Cos($a); $ey = $c - $R * [Math]::Sin($a)
        $g.DrawLine($pen, [float]$c, [float]$c, [float]$ex, [float]$ey)
        if ($s -ge 24) {
            foreach ($pair in @(@(0.52, 0.30), @(0.78, 0.20))) {
                $bx = $c + $R * $pair[0] * [Math]::Cos($a); $by = $c - $R * $pair[0] * [Math]::Sin($a)
                foreach ($side in -1, 1) {
                    $ba = $a + $side * [Math]::PI / 4
                    $len = $R * $pair[1]
                    $g.DrawLine($pen, [float]$bx, [float]$by, [float]($bx + $len * [Math]::Cos($ba)), [float]($by - $len * [Math]::Sin($ba)))
                }
            }
        }
    }
    $g.Dispose()
    return $bmp
}

foreach ($s in $sizes) {
    $bmp = Draw-Icon $s
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    if ($s -eq 256) { $bmp.Save((Join-Path $OutDir 'icon-256.png'), [System.Drawing.Imaging.ImageFormat]::Png) }
    $bmp.Dispose()
    $frames += , $ms.ToArray()
}

$fs = [System.IO.File]::Create((Join-Path $OutDir 'app.ico'))
$bw = New-Object System.IO.BinaryWriter $fs
$bw.Write([UInt16]0); $bw.Write([UInt16]1); $bw.Write([UInt16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]
    $b = if ($s -ge 256) { 0 } else { $s }
    $bw.Write([byte]$b); $bw.Write([byte]$b); $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([UInt16]1); $bw.Write([UInt16]32)
    $bw.Write([UInt32]$frames[$i].Length); $bw.Write([UInt32]$offset)
    $offset += $frames[$i].Length
}
foreach ($f in $frames) { $bw.Write($f) }
$bw.Close()
Write-Output "Готово: $(Join-Path $OutDir 'app.ico')"
