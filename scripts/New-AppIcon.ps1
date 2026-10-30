[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$root = Split-Path -Parent $PSScriptRoot
$assets = Join-Path $root 'assets'
[xml]$svg = Get-Content -LiteralPath (Join-Path $assets 'portharbor.svg') -Raw

function New-VectorPath([string]$Data) {
    $path = [Drawing.Drawing2D.GraphicsPath]::new()
    $parts = [regex]::Matches($Data, '[MLC]|-?\d+(?:\.\d+)?')
    $i = 0; $x = 0.0; $y = 0.0
    while ($i -lt $parts.Count) {
        $command = $parts[$i++].Value
        switch ($command) {
            'M' { $path.StartFigure(); $x = [single]$parts[$i++].Value; $y = [single]$parts[$i++].Value }
            'L' {
                $nextX = [single]$parts[$i++].Value; $nextY = [single]$parts[$i++].Value
                $path.AddLine([single]$x, [single]$y, $nextX, $nextY); $x = $nextX; $y = $nextY
            }
            'C' {
                $x1 = [single]$parts[$i++].Value; $y1 = [single]$parts[$i++].Value
                $x2 = [single]$parts[$i++].Value; $y2 = [single]$parts[$i++].Value
                $nextX = [single]$parts[$i++].Value; $nextY = [single]$parts[$i++].Value
                $path.AddBezier([single]$x, [single]$y, $x1, $y1, $x2, $y2, $nextX, $nextY)
                $x = $nextX; $y = $nextY
            }
            default { $path.Dispose(); throw "Unsupported SVG path command: $command" }
        }
    }
    return ,$path
}

function New-Raster([int]$Size) {
    $bitmap = [Drawing.Bitmap]::new($Size * 4, $Size * 4, [Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.Clear([Drawing.Color]::Transparent)
        $graphics.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $graphics.ScaleTransform($Size * 4 / 256.0, $Size * 4 / 256.0)
        $rect = $svg.svg.rect
        $x = [single]$rect.x; $y = [single]$rect.y; $w = [single]$rect.width; $h = [single]$rect.height
        $diameter = [single]$rect.rx * 2
        $border = [Drawing.Drawing2D.GraphicsPath]::new()
        $gradient = $svg.svg.defs.linearGradient
        $brush = [Drawing.Drawing2D.LinearGradientBrush]::new(
            [Drawing.PointF]::new([single]$gradient.x1, [single]$gradient.y1),
            [Drawing.PointF]::new([single]$gradient.x2, [single]$gradient.y2),
            [Drawing.ColorTranslator]::FromHtml($gradient.stop[0].GetAttribute('stop-color')),
            [Drawing.ColorTranslator]::FromHtml($gradient.stop[1].GetAttribute('stop-color')))
        try {
            $border.AddArc($x, $y, $diameter, $diameter, 180, 90)
            $border.AddArc($x + $w - $diameter, $y, $diameter, $diameter, 270, 90)
            $border.AddArc($x + $w - $diameter, $y + $h - $diameter, $diameter, $diameter, 0, 90)
            $border.AddArc($x, $y + $h - $diameter, $diameter, $diameter, 90, 90)
            $border.CloseFigure(); $graphics.FillPath($brush, $border)
        }
        finally { $brush.Dispose(); $border.Dispose() }
        foreach ($element in $svg.svg.path) {
            $path = New-VectorPath $element.d
            $pen = [Drawing.Pen]::new([Drawing.ColorTranslator]::FromHtml($element.stroke), [single]$element.GetAttribute('stroke-width'))
            try {
                $pen.StartCap = $pen.EndCap = [Drawing.Drawing2D.LineCap]::Round
                $pen.LineJoin = [Drawing.Drawing2D.LineJoin]::Round
                $graphics.DrawPath($pen, $path)
            }
            finally { $pen.Dispose(); $path.Dispose() }
        }
    }
    catch { $bitmap.Dispose(); throw }
    finally { $graphics.Dispose() }
    $result = [Drawing.Bitmap]::new($Size, $Size, [Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $downsample = [Drawing.Graphics]::FromImage($result)
    try {
        $downsample.CompositingMode = [Drawing.Drawing2D.CompositingMode]::SourceCopy
        $downsample.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $downsample.PixelOffsetMode = [Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $downsample.DrawImage($bitmap, 0, 0, $Size, $Size)
    }
    catch { $result.Dispose(); throw }
    finally { $downsample.Dispose(); $bitmap.Dispose() }
    return ,$result
}

$preview = New-Raster 512
try { $preview.Save((Join-Path $assets 'portharbor.png'), [Drawing.Imaging.ImageFormat]::Png) }
finally { $preview.Dispose() }
$frames = foreach ($size in @(16, 20, 24, 32, 48, 64, 128, 256)) {
    $bitmap = New-Raster $size
    $stream = [IO.MemoryStream]::new()
    try { $bitmap.Save($stream, [Drawing.Imaging.ImageFormat]::Png); [pscustomobject]@{ Size = $size; Bytes = $stream.ToArray() } }
    finally { $stream.Dispose(); $bitmap.Dispose() }
}
$file = [IO.File]::Create((Join-Path $assets 'portharbor.ico'))
$writer = [IO.BinaryWriter]::new($file)
try {
    $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$frames.Count)
    $offset = 6 + 16 * $frames.Count
    foreach ($frame in $frames) {
        $dimension = if ($frame.Size -eq 256) { 0 } else { $frame.Size }
        $writer.Write([byte]$dimension); $writer.Write([byte]$dimension)
        $writer.Write([byte]0); $writer.Write([byte]0); $writer.Write([uint16]1); $writer.Write([uint16]32)
        $writer.Write([uint32]$frame.Bytes.Length); $writer.Write([uint32]$offset)
        $offset += $frame.Bytes.Length
    }
    foreach ($frame in $frames) { $writer.Write([byte[]]$frame.Bytes) }
}
finally { $writer.Dispose() }
Write-Host 'Generated assets/portharbor.png and assets/portharbor.ico from SVG (8 sizes, 16–256 px).'
