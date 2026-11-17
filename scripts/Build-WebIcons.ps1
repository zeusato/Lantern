# Preserve the supplied artwork; only export the sizes consumed by browsers.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$root = Split-Path $PSScriptRoot -Parent
$sourcePath = Join-Path $root 'pwa/assets/calculator-icon-source.png'
$source = [Drawing.Image]::FromFile($sourcePath)
try {
    foreach ($size in @(180,192,512)) {
        $bitmap = [Drawing.Bitmap]::new($size,$size,[Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $graphics = [Drawing.Graphics]::FromImage($bitmap)
        $attributes = [Drawing.Imaging.ImageAttributes]::new()
        try {
            $graphics.Clear([Drawing.Color]::Transparent)
            $graphics.CompositingMode = [Drawing.Drawing2D.CompositingMode]::SourceCopy
            $graphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
            $graphics.PixelOffsetMode = [Drawing.Drawing2D.PixelOffsetMode]::HighQuality
            $attributes.SetWrapMode([Drawing.Drawing2D.WrapMode]::TileFlipXY)
            $graphics.DrawImage($source,[Drawing.Rectangle]::new(0,0,$size,$size),0,0,$source.Width,$source.Height,[Drawing.GraphicsUnit]::Pixel,$attributes)
            $bitmap.Save((Join-Path $root "pwa/icon-$size.png"),[Drawing.Imaging.ImageFormat]::Png)
        } finally {$attributes.Dispose();$graphics.Dispose();$bitmap.Dispose()}
    }
} finally {$source.Dispose()}
$encoded = [Convert]::ToBase64String([IO.File]::ReadAllBytes($sourcePath))
$svg = '<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 200 200"><image width="200" height="200" href="data:image/png;base64,' + $encoded + '"/></svg>'
[IO.File]::WriteAllText((Join-Path $root 'pwa/icon.svg'),$svg)
