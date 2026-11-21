# 纯代码矢量绘制原创音符图标（不依赖任何官方图片资产，无版权/商标风险），生成多尺寸 app.ico
Add-Type -AssemblyName System.Drawing

$sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256
$pngs = @()

foreach ($s in $sizes) {
    $bmp = New-Object System.Drawing.Bitmap $s, $s, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'HighQuality'
    $g.InterpolationMode = 'HighQualityBicubic'
    $g.PixelOffsetMode = 'HighQuality'
    $g.CompositingQuality = 'HighQuality'

    $pad = [Math]::Max(0.0, [Math]::Floor($s / 32.0))
    $w = [float]($s - 2.0 * $pad)
    $r = [float]($w * 0.225)
    $d = [float](2.0 * $r)

    # 圆角深色底座
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddArc($pad, $pad, $d, $d, 180, 90)
    $path.AddArc($pad + $w - $d, $pad, $d, $d, 270, 90)
    $path.AddArc($pad + $w - $d, $pad + $w - $d, $d, $d, 0, 90)
    $path.AddArc($pad, $pad + $w - $d, $d, $d, 90, 90)
    $path.CloseFigure()

    $rect = New-Object System.Drawing.RectangleF $pad, $pad, $w, $w
    $bgBrush = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        $rect,
        [System.Drawing.Color]::FromArgb(255, 36, 37, 46),
        [System.Drawing.Color]::FromArgb(255, 20, 21, 26),
        45.0
    )
    $g.FillPath($bgBrush, $path)
    $bgBrush.Dispose()

    # 内部渐变圆环脉冲装饰
    $ringInset = [float]($pad + $w * 0.14)
    $ringW = [float]($w * 0.72)
    $ringRect = New-Object System.Drawing.RectangleF $ringInset, $ringInset, $ringW, $ringW
    $accentBrush = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        $ringRect,
        [System.Drawing.Color]::FromArgb(255, 255, 75, 110),
        [System.Drawing.Color]::FromArgb(255, 138, 68, 255),
        60.0
    )
    $ringPen = New-Object System.Drawing.Pen($accentBrush, [Math]::Max(1.2, $w * 0.065))
    $g.DrawEllipse($ringPen, $ringRect)
    $ringPen.Dispose()

    # 原创几何双八分音符图形
    $noteBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 248, 248, 252))
    $stemW = [float][Math]::Max(1.2, $w * 0.06)
    $headW = [float]($w * 0.16)
    $headH = [float]($w * 0.125)

    $x1 = [float]($pad + $w * 0.31)
    $y1 = [float]($pad + $w * 0.56)
    $x2 = [float]($pad + $w * 0.54)
    $y2 = [float]($pad + $w * 0.51)
    $top1 = [float]($pad + $w * 0.30)
    $top2 = [float]($pad + $w * 0.25)

    $g.FillEllipse($noteBrush, $x1, $y1, $headW, $headH)
    $g.FillEllipse($noteBrush, $x2, $y2, $headW, $headH)

    $sx1 = [float]($x1 + $headW - $stemW)
    $sx2 = [float]($x2 + $headW - $stemW)
    $g.FillRectangle($noteBrush, $sx1, $top1, $stemW, [float]($y1 + $headH * 0.55 - $top1))
    $g.FillRectangle($noteBrush, $sx2, $top2, $stemW, [float]($y2 + $headH * 0.55 - $top2))

    $beam = New-Object System.Drawing.Drawing2D.GraphicsPath
    $beamH = [float][Math]::Max(1.4, $w * 0.075)
    $pts = [System.Drawing.PointF[]]@(
        (New-Object System.Drawing.PointF $sx1, $top1),
        (New-Object System.Drawing.PointF ($sx2 + $stemW), $top2),
        (New-Object System.Drawing.PointF ($sx2 + $stemW), ($top2 + $beamH)),
        (New-Object System.Drawing.PointF $sx1, ($top1 + $beamH))
    )
    $beam.AddPolygon($pts)
    $g.FillPath($noteBrush, $beam)

    $beam.Dispose()
    $noteBrush.Dispose()
    $accentBrush.Dispose()
    $path.Dispose()
    $g.Dispose()

    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $pngs += , $ms.ToArray()
    $bmp.Dispose()
}

$out = New-Object System.IO.MemoryStream
$bw = New-Object System.IO.BinaryWriter $out
$bw.Write([UInt16]0); $bw.Write([UInt16]1); $bw.Write([UInt16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]; $b = if ($s -ge 256) { 0 } else { $s }
    $bw.Write([byte]$b); $bw.Write([byte]$b); $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([UInt16]1); $bw.Write([UInt16]32); $bw.Write([UInt32]$pngs[$i].Length); $bw.Write([UInt32]$offset)
    $offset += $pngs[$i].Length
}
foreach ($p in $pngs) { $bw.Write($p) }
[IO.File]::WriteAllBytes("$PSScriptRoot\..\app.ico", $out.ToArray())
"Original app.ico written: $($out.Length) bytes"
