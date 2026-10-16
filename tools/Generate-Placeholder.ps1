$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

# Development-only, deterministic pixel art. The application only loads the saved PNG.
$outputDirectory = Join-Path $PSScriptRoot '../src/PixelWallpaper.Renderer/Assets/Backgrounds'
[void][IO.Directory]::CreateDirectory($outputDirectory)
$bitmap = [Drawing.Bitmap]::new(480, 270, [Drawing.Imaging.PixelFormat]::Format32bppArgb)
$graphics = [Drawing.Graphics]::FromImage($bitmap)
$graphics.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::None
$brushes = @{}
function Brush([string]$hex) {
    if (-not $brushes.ContainsKey($hex)) {
        $brushes[$hex] = [Drawing.SolidBrush]::new([Drawing.ColorTranslator]::FromHtml($hex))
    }
    return $brushes[$hex]
}
function Rectangle([string]$color, [int]$x, [int]$y, [int]$width, [int]$height) {
    $graphics.FillRectangle((Brush $color), $x, $y, $width, $height)
}
function Pine([int]$x, [int]$top, [int]$height, [string]$color) {
    $baseY = $top + $height
    Rectangle $color ($x - 2) ($top + 8) 4 ($height - 8)
    for ($level = 0; $level -lt 4; $level++) {
        $tip = $top + [int]($height * 0.15 * $level)
        $halfWidth = 8 + $level * 4 + [int]($height / 12)
        $bottom = [Math]::Min($baseY - 8, $tip + [int]($height * 0.43))
        $points = [Drawing.Point[]]@(
            [Drawing.Point]::new($x, $tip),
            [Drawing.Point]::new($x - $halfWidth, $bottom),
            [Drawing.Point]::new($x + $halfWidth, $bottom)
        )
        $graphics.FillPolygon((Brush $color), $points)
    }
}
try {
    $graphics.Clear([Drawing.ColorTranslator]::FromHtml('#15252F'))
    Rectangle '#1A2D36' 0 72 480 110
    # Distant and near trees are painted once into this static image; no runtime layers.
    for ($x = -15; $x -lt 510; $x += 23) {
        Pine $x (76 + (($x + 15) % 31)) 122 '#1D353B'
    }
    Rectangle '#122D2D' 0 178 480 92
    for ($x = -12; $x -lt 510; $x += 39) {
        Pine $x (26 + (($x + 12) % 47)) 190 '#102627'
    }
    # A small stream makes rain movement easier to see against both dark and light areas.
    $stream = [Drawing.Point[]]@(
        [Drawing.Point]::new(258, 186), [Drawing.Point]::new(274, 186),
        [Drawing.Point]::new(253, 213), [Drawing.Point]::new(292, 234),
        [Drawing.Point]::new(331, 270), [Drawing.Point]::new(203, 270),
        [Drawing.Point]::new(237, 231), [Drawing.Point]::new(230, 214)
    )
    $graphics.FillPolygon((Brush '#233E48'), $stream)
    Rectangle '#34515A' 245 217 12 1
    Rectangle '#34515A' 254 240 24 1
    Rectangle '#34515A' 230 256 29 1
    # Chunky foreground trunks and discrete grass marks; no antialiasing or effects.
    Rectangle '#0B1C20' 23 0 15 270
    Rectangle '#0B1C20' 436 0 18 270
    Rectangle '#142B2C' 26 0 3 257
    Rectangle '#142B2C' 439 0 3 265
    $random = [Random]::new(12345)
    for ($i = 0; $i -lt 130; $i++) {
        $x = $random.Next(480)
        $y = $random.Next(209, 270)
        if ($x -lt 196 -or $x -gt 334) {
            Rectangle '#234035' $x $y 2 3
            Rectangle '#234035' ($x - 1) ($y + 2) 4 1
        }
    }
    $bitmap.Save((Join-Path $outputDirectory 'rainy-forest.png'), [Drawing.Imaging.ImageFormat]::Png)
}
finally {
    foreach ($brush in $brushes.Values) { $brush.Dispose() }
    $graphics.Dispose()
    $bitmap.Dispose()
}
Write-Output 'Generated 480x270 temporary forest: Assets/Backgrounds/rainy-forest.png'
