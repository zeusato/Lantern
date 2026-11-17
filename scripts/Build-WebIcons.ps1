$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$root = Split-Path $PSScriptRoot -Parent
[xml]$svg = Get-Content -LiteralPath (Join-Path $root 'pwa/icon.svg') -Raw
foreach ($size in @(192,512)) {
    $bitmap = [Drawing.Bitmap]::new($size,$size)
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.Clear([Drawing.ColorTranslator]::FromHtml('#18191d'))
        $graphics.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $graphics.ScaleTransform($size/192.0,$size/192.0)
        foreach ($rect in $svg.SelectNodes('//*[local-name()="rect"]')) {
            $x=[single]$rect.GetAttribute('x');$y=[single]$rect.GetAttribute('y');$w=[single]$rect.GetAttribute('width');$h=[single]$rect.GetAttribute('height');$radius=[Math]::Min([single]$rect.GetAttribute('rx'),[Math]::Min($w,$h)/2)
            $path=[Drawing.Drawing2D.GraphicsPath]::new()
            try {
                if($radius -gt 0){$d=$radius*2;$path.AddArc($x,$y,$d,$d,180,90);$path.AddArc($x+$w-$d,$y,$d,$d,270,90);$path.AddArc($x+$w-$d,$y+$h-$d,$d,$d,0,90);$path.AddArc($x,$y+$h-$d,$d,$d,90,90);$path.CloseFigure()}
                else{$path.AddRectangle([Drawing.RectangleF]::new($x,$y,$w,$h))}
                $fill=$rect.GetAttribute('fill');if(-not $fill){$fill=$rect.ParentNode.GetAttribute('fill')}
                if($fill -and $fill -ne 'none'){$brush=[Drawing.SolidBrush]::new([Drawing.ColorTranslator]::FromHtml($fill));try{$graphics.FillPath($brush,$path)}finally{$brush.Dispose()}}
                if($rect.GetAttribute('stroke')){$pen=[Drawing.Pen]::new([Drawing.ColorTranslator]::FromHtml($rect.GetAttribute('stroke')),[single]$rect.GetAttribute('stroke-width'));try{$graphics.DrawPath($pen,$path)}finally{$pen.Dispose()}}
            } finally {$path.Dispose()}
        }
        $bitmap.Save((Join-Path $root "pwa/icon-$size.png"),[Drawing.Imaging.ImageFormat]::Png)
    } finally {$graphics.Dispose();$bitmap.Dispose()}
}
