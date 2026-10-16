$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

# Original, deterministic placeholder art. Development-only; never runs in the app.
# Writes only the three Campfire PNGs, leaving Rainy Forest and replacement art alone
# unless this script is explicitly invoked again.
$assetDirectory = Join-Path $PSScriptRoot '../src/PixelWallpaper.Renderer/Assets'
[void][IO.Directory]::CreateDirectory((Join-Path $assetDirectory 'Backgrounds'))
[void][IO.Directory]::CreateDirectory((Join-Path $assetDirectory 'Sprites'))
$brushes = @{}
function Brush([string]$hex) {
    if (-not $brushes.ContainsKey($hex)) {
        $brushes[$hex] = [Drawing.SolidBrush]::new([Drawing.ColorTranslator]::FromHtml($hex))
    }
    return $brushes[$hex]
}
function Rect([string]$color, [int]$x, [int]$y, [int]$w, [int]$h) {
    $graphics.FillRectangle((Brush $color), $x, $y, $w, $h)
}
function Polygon([string]$color, [int[]]$xy) {
    $points = for ($i = 0; $i -lt $xy.Length; $i += 2) { [Drawing.Point]::new($xy[$i], $xy[$i + 1]) }
    $graphics.FillPolygon((Brush $color), [Drawing.Point[]]$points)
}
function Pine([int]$x, [int]$top, [int]$height, [string]$color) {
    Rect $color ($x - 2) ($top + 8) 4 ($height - 8)
    for ($level = 0; $level -lt 4; $level++) {
        $tip = $top + [int]($height * .15 * $level)
        $half = 7 + $level * 5 + [int]($height / 15)
        $bottom = [Math]::Min($top + $height - 5, $tip + [int]($height * .45))
        Polygon $color @($x, $tip, ($x - $half), $bottom, ($x + $half), $bottom)
    }
}
$bitmap = $null; $graphics = $null
try {
    $bitmap = [Drawing.Bitmap]::new(480, 270, [Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::None
    $graphics.Clear([Drawing.ColorTranslator]::FromHtml('#10192E'))
    Rect '#18223A' 0 71 480 90
    $random = [Random]::new(6016)
    for ($i = 0; $i -lt 64; $i++) { Rect '#65758C' ($random.Next(480)) ($random.Next(8, 120)) 1 1 }
    # Pixel moon, distant ridge and two bands of static evergreens.
    Rect '#B8C4C0' 360 30 16 12; Rect '#B8C4C0' 364 26 8 20
    Rect '#10192E' 369 26 9 13
    Polygon '#202D40' @(0, 136, 69, 95, 137, 134, 207, 106, 290, 146, 380, 108, 480, 139, 480, 200, 0, 200)
    for ($x = -10; $x -lt 500; $x += 27) { Pine $x (85 + $random.Next(32)) 95 '#192F39' }
    Rect '#172C30' 0 177 480 93
    for ($x = -8; $x -lt 510; $x += 47) { Pine $x (35 + $random.Next(34)) 148 '#102428' }
    Polygon '#25342E' @(75, 230, 156, 187, 298, 184, 394, 224, 382, 270, 88, 270)
    Polygon '#303D31' @(152, 236, 206, 209, 296, 206, 344, 235, 299, 252, 209, 250)
    # Small canvas tent, warm face pointing toward the fire.
    Polygon '#545947' @(102, 216, 154, 151, 211, 216)
    Polygon '#888162' @(154, 151, 192, 161, 239, 216, 211, 216)
    Polygon '#2A3934' @(122, 216, 154, 170, 183, 216)
    Polygon '#B09A65' @(154, 151, 157, 155, 213, 216, 209, 216)
    Rect '#3B4031' 101 216 139 3
    Rect '#646951' 88 219 3 7; Rect '#646951' 247 218 3 8
    # Pebble ring and two crossed logs sit in the background, below the fire sprite.
    foreach ($stone in @(@(235, 212, 8, 4), @(242, 219, 8, 4), @(253, 222, 8, 4), @(265, 219, 8, 4), @(273, 212, 7, 4))) {
        Rect '#717063' $stone[0] $stone[1] $stone[2] $stone[3]
        Rect '#414A40' $stone[0] ($stone[1] + 3) $stone[2] 2
    }
    Polygon '#6D4930' @(240, 207, 244, 204, 272, 215, 268, 219)
    Polygon '#9B6940' @(240, 215, 267, 204, 271, 208, 244, 220)
    Rect '#C08B4F' 244 212 4 2; Rect '#B78248' 261 209 4 2
    # Sparse foreground grass, backpack, and framing trunks; no runtime terrain layers.
    Rect '#3E5147' 334 208 12 16; Rect '#526451' 336 204 8 5; Rect '#253D37' 338 211 4 7
    for ($i = 0; $i -lt 100; $i++) {
        $x = $random.Next(480); $y = $random.Next(228, 270)
        Rect '#36483A' $x $y 2 3
    }
    Rect '#0A1B21' 25 0 13 270; Rect '#0A1B21' 439 0 17 270
    Rect '#152B2C' 28 0 2 266; Rect '#152B2C' 442 0 3 262
    $bitmap.Save((Join-Path $assetDirectory 'Backgrounds/campfire.png'), [Drawing.Imaging.ImageFormat]::Png)
    $graphics.Dispose(); $graphics = $null; $bitmap.Dispose(); $bitmap = $null

    # Eight 32x40 straight-alpha cells, left-to-right, drawn on a transparent strip.
    $bitmap = [Drawing.Bitmap]::new(256, 40, [Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    $graphics.Clear([Drawing.Color]::Transparent)
    for ($frame = 0; $frame -lt 8; $frame++) {
        $offset = $frame * 32
        $sway = @(0, 1, 2, 1, 0, -1, -2, -1)[$frame]
        $tip = @(4, 2, 5, 7, 3, 1, 4, 6)[$frame]
        Rect '#D4582C' ($offset + 7) 28 18 9
        Rect '#E66B2E' ($offset + 5) 24 22 9
        Rect '#E66B2E' ($offset + 9) 16 15 14
        Rect '#E66B2E' ($offset + 14 + $sway) $tip 5 (24 - $tip)
        Rect '#E66B2E' ($offset + 8 - $sway) (13 + ($frame % 3)) 4 12
        Rect '#E66B2E' ($offset + 22 - $sway) (17 - ($frame % 4)) 3 12
        Rect '#F79E3C' ($offset + 9) 25 15 9
        Rect '#F79E3C' ($offset + 12 + $sway) 17 8 13
        Rect '#F79E3C' ($offset + 15 + $sway) (10 + $tip) 3 11
        Rect '#FFD16A' ($offset + 12) 29 9 7
        Rect '#FFD16A' ($offset + 14 - $sway) (23 + ($frame % 3)) 5 10
        Rect '#FFE7A0' ($offset + 15) 32 4 4
    }
    $bitmap.Save((Join-Path $assetDirectory 'Sprites/campfire-fire.png'), [Drawing.Imaging.ImageFormat]::Png)
    $graphics.Dispose(); $graphics = $null; $bitmap.Dispose(); $bitmap = $null

    # Quantized alpha bands in a small ellipse. No blur/postprocessing at runtime.
    $bitmap = [Drawing.Bitmap]::new(64, 48, [Drawing.Imaging.PixelFormat]::Format32bppArgb)
    for ($y = 0; $y -lt 48; $y++) {
        for ($x = 0; $x -lt 64; $x++) {
            $distance = [Math]::Sqrt([Math]::Pow(($x - 31.5) / 32, 2) + [Math]::Pow(($y - 28) / 23, 2))
            $alpha = [int]([Math]::Floor([Math]::Max(0, 1 - $distance) * 5) * 13)
            $bitmap.SetPixel($x, $y, [Drawing.Color]::FromArgb($alpha, 234, 133, 49))
        }
    }
    $bitmap.Save((Join-Path $assetDirectory 'Sprites/campfire-glow.png'), [Drawing.Imaging.ImageFormat]::Png)
}
finally {
    if ($graphics) { $graphics.Dispose() }
    if ($bitmap) { $bitmap.Dispose() }
    foreach ($brush in $brushes.Values) { $brush.Dispose() }
}
Write-Output 'Generated original Campfire placeholders: background 480x270, fire strip 256x40, glow 64x48.'
