$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

# Original deterministic placeholder art; development-only, never run by the app.
# Explicit invocation overwrites only the four Maple Tree PNGs.
$assetDirectory = Join-Path $PSScriptRoot '../src/PixelWallpaper.Renderer/Assets'
[void][IO.Directory]::CreateDirectory((Join-Path $assetDirectory 'Backgrounds'))
[void][IO.Directory]::CreateDirectory((Join-Path $assetDirectory 'Sprites'))
$brushes = @{}
function Brush([string]$hex) {
    if (-not $brushes.ContainsKey($hex)) { $brushes[$hex] = [Drawing.SolidBrush]::new([Drawing.ColorTranslator]::FromHtml($hex)) }
    return $brushes[$hex]
}
function Rect([string]$color, [int]$x, [int]$y, [int]$w, [int]$h) {
    $graphics.FillRectangle((Brush $color), $x, $y, $w, $h)
}
function Polygon([string]$color, [int[]]$xy) {
    $points = for ($i = 0; $i -lt $xy.Length; $i += 2) { [Drawing.Point]::new($xy[$i], $xy[$i + 1]) }
    $graphics.FillPolygon((Brush $color), [Drawing.Point[]]$points)
}
function Cluster([int]$x, [int]$y, [string]$dark, [string]$mid, [string]$light) {
    Polygon $dark @($x, ($y + 11), ($x + 4), ($y + 11), ($x + 4), ($y + 5), ($x + 10), ($y + 5), ($x + 10), $y, ($x + 20), $y, ($x + 20), ($y + 4), ($x + 27), ($y + 4), ($x + 27), ($y + 10), ($x + 31), ($y + 10), ($x + 31), ($y + 18), ($x + 24), ($y + 18), ($x + 24), ($y + 23), ($x + 7), ($y + 23), ($x + 7), ($y + 19), $x, ($y + 19))
    Rect $mid ($x + 5) ($y + 7) 21 10; Rect $mid ($x + 10) ($y + 3) 10 4
    Rect $light ($x + 9) ($y + 5) 7 3; Rect $light ($x + 4) ($y + 10) 5 3
    Rect $light ($x + 21) ($y + 9) 4 2
}
$bitmap = $null; $graphics = $null
try {
    $bitmap = [Drawing.Bitmap]::new(480, 270, [Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::None
    $graphics.Clear([Drawing.ColorTranslator]::FromHtml('#8FA8AE'))
    Rect '#A4B4AF' 0 68 480 44; Rect '#BAC0AE' 0 112 480 34; Rect '#D0C4A0' 0 146 480 40
    # Static clouds are baked once; they are not separate runtime textures/layers.
    Rect '#DAD8BE' 36 40 64 6; Rect '#DAD8BE' 44 34 40 8; Rect '#DAD8BE' 59 30 16 7
    Rect '#E0DAC1' 377 63 66 5; Rect '#E0DAC1' 388 57 40 6
    Polygon '#91A18C' @(0, 149, 61, 119, 118, 143, 173, 116, 251, 157, 325, 125, 399, 147, 480, 112, 480, 213, 0, 213)
    Polygon '#758F79' @(0, 177, 86, 152, 165, 183, 250, 151, 331, 179, 409, 156, 480, 172, 480, 232, 0, 232)
    Polygon '#637C57' @(0, 196, 103, 176, 192, 199, 302, 179, 389, 200, 480, 181, 480, 270, 0, 270)
    # Hill crest holds the immobile trunk; static contours extend to the foreground.
    Polygon '#586C3F' @(0, 250, 79, 235, 144, 211, 204, 190, 265, 184, 317, 197, 374, 220, 433, 247, 480, 261, 480, 270, 0, 270)
    Polygon '#455E3B' @(0, 266, 90, 247, 184, 231, 286, 225, 385, 246, 480, 269, 480, 270, 0, 270)
    Polygon '#6B7946' @(154, 214, 216, 196, 271, 192, 313, 201, 354, 220, 280, 213, 228, 211)
    Polygon '#394F36' @(231, 202, 250, 193, 278, 193, 299, 205, 276, 208, 250, 207)
    # Permanent trunk, branching silhouette and roots.
    Polygon '#654333' @(246, 197, 255, 161, 256, 104, 249, 80, 259, 83, 268, 121, 272, 167, 280, 199, 266, 195, 258, 200)
    Polygon '#49352F' @(258, 191, 260, 139, 256, 101, 261, 111, 268, 165, 273, 196)
    Polygon '#765039' @(256, 146, 233, 123, 207, 111, 213, 107, 242, 120, 261, 134)
    Polygon '#654333' @(263, 132, 279, 106, 299, 92, 303, 96, 285, 112, 271, 141)
    Polygon '#654333' @(254, 192, 238, 204, 262, 198, 271, 199, 289, 206, 278, 195)
    # Broad maple canopy is entirely static. Moving accents overlap selected edges only.
    Polygon '#A84A2E' @(164, 105, 172, 74, 196, 57, 207, 37, 242, 30, 265, 36, 283, 28, 311, 39, 330, 55, 335, 78, 350, 91, 348, 119, 327, 138, 301, 141, 278, 133, 254, 142, 227, 134, 205, 138, 184, 125, 165, 124)
    Polygon '#C36632' @(167, 94, 184, 68, 207, 53, 215, 37, 244, 33, 268, 43, 286, 34, 310, 43, 326, 61, 332, 86, 345, 97, 337, 117, 314, 124, 291, 119, 264, 130, 237, 121, 215, 127, 189, 117, 174, 117)
    Polygon '#D9843D' @(184, 75, 207, 60, 219, 42, 242, 38, 265, 47, 287, 40, 311, 48, 322, 66, 312, 85, 290, 93, 267, 87, 244, 102, 217, 93, 195, 103, 177, 99)
    Polygon '#E3A34D' @(202, 65, 220, 46, 241, 42, 264, 54, 285, 45, 303, 51, 313, 66, 296, 76, 277, 73, 258, 81, 234, 75, 216, 82, 198, 79)
    $random = [Random]::new(7107)
    for ($i = 0; $i -lt 155; $i++) {
        $x = $random.Next(180, 333); $y = $random.Next(48, 125)
        if ((($x - 258) * ($x - 258) / 6400 + ($y - 85) * ($y - 85) / 2000) -lt 1) {
            $color = @('#E3A34D', '#B95630', '#D9843D', '#EAC071')[$i % 4]
            Rect $color $x $y ($random.Next(2, 6)) 2
        }
    }
    # Foreground stone, grass marks and fallen leaves are also static.
    Polygon '#727B68' @(81, 244, 87, 234, 101, 233, 111, 243, 105, 249, 86, 249)
    Rect '#939582' 88 235 10 3; Rect '#4A5E45' 85 247 21 3
    for ($i = 0; $i -lt 115; $i++) {
        $x = $random.Next(90, 450); $y = $random.Next(235, 270)
        Rect '#829052' $x $y 1 3; Rect '#829052' ($x + 2) ($y + 1) 1 2
    }
    foreach ($point in @(@(241, 207), @(284, 216), @(219, 226), @(320, 230), @(267, 221))) {
        Rect '#B97538' $point[0] $point[1] 3 2
    }
    $bitmap.Save((Join-Path $assetDirectory 'Backgrounds/maple-tree.png'), [Drawing.Imaging.ImageFormat]::Png)
    $graphics.Dispose(); $graphics = $null; $bitmap.Dispose(); $bitmap = $null

    $bitmap = [Drawing.Bitmap]::new(32, 24, [Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [Drawing.Graphics]::FromImage($bitmap); $graphics.Clear([Drawing.Color]::Transparent)
    Cluster 0 0 '#B65730' '#D9843D' '#E3A34D'
    $bitmap.Save((Join-Path $assetDirectory 'Sprites/maple-leaf-cluster.png'), [Drawing.Imaging.ImageFormat]::Png)
    $graphics.Dispose(); $graphics = $null; $bitmap.Dispose(); $bitmap = $null

    $bitmap = [Drawing.Bitmap]::new(32, 12, [Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [Drawing.Graphics]::FromImage($bitmap); $graphics.Clear([Drawing.Color]::Transparent)
    for ($x = 2; $x -lt 30; $x += 4) {
        $top = 3 + ($x % 5)
        Rect '#829052' $x $top 1 (11 - $top); Rect '#829052' ($x - 1) ($top - 1) 1 3
        Rect '#A1A462' ($x + 1) ($top + 2) 1 (9 - $top)
    }
    $bitmap.Save((Join-Path $assetDirectory 'Sprites/maple-grass.png'), [Drawing.Imaging.ImageFormat]::Png)
    $graphics.Dispose(); $graphics = $null; $bitmap.Dispose(); $bitmap = $null

    # Two 8x8 silhouettes in one horizontal strip; all unused pixels are transparent.
    $bitmap = [Drawing.Bitmap]::new(16, 8, [Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [Drawing.Graphics]::FromImage($bitmap); $graphics.Clear([Drawing.Color]::Transparent)
    Polygon '#C66E31' @(3, 0, 4, 2, 6, 1, 6, 3, 7, 3, 5, 5, 4, 6, 4, 7, 3, 7, 3, 5, 1, 5, 0, 3, 2, 3, 1, 1, 3, 2)
    Rect '#E6AB51' 3 2 1 3; Rect '#87502E' 4 6 1 2
    Polygon '#C66E31' @(11, 1, 13, 2, 15, 2, 14, 4, 12, 5, 11, 7, 10, 5, 9, 4, 10, 3)
    Rect '#E6AB51' 11 2 1 3
    $bitmap.Save((Join-Path $assetDirectory 'Sprites/maple-falling-leaf.png'), [Drawing.Imaging.ImageFormat]::Png)
}
finally {
    if ($graphics) { $graphics.Dispose() }
    if ($bitmap) { $bitmap.Dispose() }
    foreach ($brush in $brushes.Values) { $brush.Dispose() }
}
Write-Output 'Generated Maple Tree originals: 480x270 background, 32x24 cluster, 32x12 grass, 16x8 leaf strip.'
