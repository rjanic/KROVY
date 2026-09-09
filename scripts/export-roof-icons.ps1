# Export the approved red atlas to the existing Ribbon/toolbar PNG names.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$repoRoot = Split-Path $PSScriptRoot -Parent
$resourceRoot = Join-Path $repoRoot 'src/AcKrovy.AutoCAD/Resources'
$atlas = [System.Drawing.Bitmap]::new((Join-Path $resourceRoot 'IconSources/roof-icons-red.png'))
try {
    if ($atlas.GetPixel(0, 0).A -ne 0) { throw 'The atlas must have a transparent background.' }
    $halfWidth = [int]($atlas.Width / 2)
    $halfHeight = [int]($atlas.Height / 2)
    $keys = @('roof_hip', 'roof_halfhip', 'roof_gable', 'roof_monopitch')
    for ($index = 0; $index -lt $keys.Count; $index++) {
        $originX = ($index % 2) * $halfWidth
        $originY = [Math]::Floor($index / 2) * $halfHeight
        $minX = $atlas.Width; $minY = $atlas.Height; $maxX = -1; $maxY = -1
        for ($y = $originY; $y -lt $originY + $halfHeight; $y++) {
            for ($x = $originX; $x -lt $originX + $halfWidth; $x++) {
                if ($atlas.GetPixel($x, $y).A -gt 16) {
                    $minX = [Math]::Min($minX, $x); $maxX = [Math]::Max($maxX, $x)
                    $minY = [Math]::Min($minY, $y); $maxY = [Math]::Max($maxY, $y)
                }
            }
        }
        if ($maxX -lt $minX) { throw "Empty icon: $($keys[$index])" }
        $bounds = [System.Drawing.Rectangle]::new($minX, $minY, $maxX - $minX + 1, $maxY - $minY + 1)
        foreach ($size in @(16, 32)) {
            $bitmap = [System.Drawing.Bitmap]::new($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
            $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
            try {
                $graphics.Clear([System.Drawing.Color]::Transparent)
                $graphics.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceCopy
                $graphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
                $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
                $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
                $scale = ($size - 2.0) / [Math]::Max($bounds.Width, $bounds.Height)
                $width = [single]($bounds.Width * $scale)
                $height = [single]($bounds.Height * $scale)
                $target = [System.Drawing.RectangleF]::new(($size - $width) / 2, ($size - $height) / 2, $width, $height)
                $graphics.DrawImage($atlas, $target, $bounds, [System.Drawing.GraphicsUnit]::Pixel)
                $path = Join-Path $resourceRoot "Icons/ak_$($keys[$index])_$size.png"
                $bitmap.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
                Write-Output "Exported ak_$($keys[$index])_$size.png"
            }
            finally { $graphics.Dispose(); $bitmap.Dispose() }
        }
    }
    # The roof menu uses the same gable silhouette as its default roof command.
    foreach ($size in @(16, 32)) {
        Copy-Item -LiteralPath (Join-Path $resourceRoot "Icons/ak_roof_gable_$size.png") -Destination (Join-Path $resourceRoot "Icons/ak_roof_$size.png")
    }
}
finally { $atlas.Dispose() }
