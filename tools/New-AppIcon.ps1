# Rebuild the multi-size ICO from the project's simple vector geometry.
Add-Type -AssemblyName System.Drawing
$iconRoot = Split-Path $PSScriptRoot -Parent
$iconPath = Join-Path $iconRoot 'HardwareMonitor/Assets/app.ico'
$frames = @()
foreach ($size in @(16,24,32,48,64,256)) {
    $bitmap = [Drawing.Bitmap]::new($size,$size)
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.ScaleTransform($size/16.0,$size/16.0)
    $background = [Drawing.SolidBrush]::new([Drawing.ColorTranslator]::FromHtml('#16313C'))
    $white = [Drawing.Pen]::new([Drawing.ColorTranslator]::FromHtml('#F4F6FA'),1.2)
    $teal = [Drawing.Pen]::new([Drawing.ColorTranslator]::FromHtml('#67E8DA'),1.2)
    $white.LineJoin = $teal.LineJoin = [Drawing.Drawing2D.LineJoin]::Round
    $shape = [Drawing.Drawing2D.GraphicsPath]::new()
    $shape.AddArc(0,0,6,6,180,90); $shape.AddArc(10,0,6,6,270,90)
    $shape.AddArc(10,10,6,6,0,90); $shape.AddArc(0,10,6,6,90,90); $shape.CloseFigure()
    $graphics.FillPath($background,$shape)
    $graphics.DrawRectangle($white,3,3,10,8)
    $graphics.DrawLine($white,8,11,8,14); $graphics.DrawLine($white,5,14,11,14)
    $points = @([Drawing.PointF]::new(4,7),[Drawing.PointF]::new(6,7),[Drawing.PointF]::new(7.5,5),[Drawing.PointF]::new(9,9),[Drawing.PointF]::new(11,6),[Drawing.PointF]::new(12,6))
    $graphics.DrawLines($teal,[Drawing.PointF[]]$points)
    $memory = [IO.MemoryStream]::new(); $bitmap.Save($memory,[Drawing.Imaging.ImageFormat]::Png)
    $frames += ,@($size,$memory.ToArray())
    $memory.Dispose(); $shape.Dispose(); $teal.Dispose(); $white.Dispose(); $background.Dispose(); $graphics.Dispose(); $bitmap.Dispose()
}
$stream = [IO.File]::Create($iconPath); $writer = [IO.BinaryWriter]::new($stream)
try {
    $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$frames.Count)
    $offset = 6 + 16*$frames.Count
    foreach ($frame in $frames) {
        $dimension = if ($frame[0] -eq 256) { 0 } else { $frame[0] }
        $writer.Write([byte]$dimension); $writer.Write([byte]$dimension); $writer.Write([byte]0); $writer.Write([byte]0)
        $writer.Write([uint16]1); $writer.Write([uint16]32); $writer.Write([uint32]$frame[1].Length); $writer.Write([uint32]$offset)
        $offset += $frame[1].Length
    }
    foreach ($frame in $frames) { $writer.Write([byte[]]$frame[1]) }
} finally { $writer.Dispose(); $stream.Dispose() }
