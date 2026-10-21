param(
    [string]$SourceDirectory = 'C:\Users\31483\Desktop\天气屋\UI\相册符号页UI拆分',
    [string]$ReferenceImage = (Join-Path (Split-Path -Parent $PSScriptRoot) 'PrototypeReview\AlbumBook\P1-reference.jpg')
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$projectRoot = Split-Path -Parent $PSScriptRoot
$assetDirectory = Join-Path $projectRoot 'Assets\WeatherHousePrototype\Resources\AlbumBook'
$previewDirectory = Join-Path $projectRoot 'PrototypeReview\AlbumBook\Assets'
New-Item -ItemType Directory -Force -Path $assetDirectory, $previewDirectory | Out-Null

function New-Canvas([int]$Width, [int]$Height) {
    $bitmap = [Drawing.Bitmap]::new($Width, $Height, [Drawing.Imaging.PixelFormat]::Format32bppArgb)
    return ,$bitmap
}

function Save-Asset([Drawing.Bitmap]$Bitmap, [string]$Name) {
    $Bitmap.Save((Join-Path $assetDirectory ($Name + '.png')), [Drawing.Imaging.ImageFormat]::Png)
    $Bitmap.Dispose()
}

function Export-Crop([Drawing.Bitmap]$Source, [string]$Name, [int[]]$Rectangle, [double[]]$Outline = @()) {
    $bitmap = New-Canvas $Rectangle[2] $Rectangle[3]
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    $graphics.CompositingMode = [Drawing.Drawing2D.CompositingMode]::SourceCopy
    if ($Outline.Count -gt 0) {
        $points = [Drawing.PointF[]]::new($Outline.Count / 2)
        for ($i = 0; $i -lt $points.Length; $i++) {
            $points[$i] = [Drawing.PointF]::new(($Outline[2 * $i] - $Rectangle[0]), ($Outline[2 * $i + 1] - $Rectangle[1]))
        }
        $path = [Drawing.Drawing2D.GraphicsPath]::new()
        $path.AddPolygon($points)
        $graphics.SetClip($path)
        $path.Dispose()
    }
    $destination = [Drawing.Rectangle]::new(0, 0, $Rectangle[2], $Rectangle[3])
    $graphics.DrawImage($Source, $destination, $Rectangle[0], $Rectangle[1], $Rectangle[2], $Rectangle[3], [Drawing.GraphicsUnit]::Pixel)
    $graphics.Dispose()
    Save-Asset $bitmap $Name
}

$book = [Drawing.Bitmap]::FromFile((Join-Path $SourceDirectory '书本.png'))
# Only the outside of the original cover is masked: this removes isolated export debris.
Export-Crop $book 'Book' @(130,375,3260,1968) @(
    146,489, 154,456, 181,438, 218,431, 220,409, 245,401,
    401,402, 611,389, 738,381, 897,382, 1127,393, 1437,410,
    1718,414, 1734,393, 1767,390, 1820,394, 1843,402, 1855,418,
    2186,410, 2537,400, 2837,398, 3188,402, 3335,404,
    3364,416, 3374,441, 3368,673, 3368,1091, 3368,1611,
    3372,2255, 3370,2308, 3359,2325, 3335,2331,
    2792,2325, 2251,2325, 1895,2325, 1802,2334, 1734,2326,
    1314,2324, 872,2327, 480,2328, 182,2327, 151,2320,
    135,2304, 136,2210, 145,2017, 148,1832, 144,1550,
    146,1300, 153,1050, 148,817
)
$book.Dispose()

$frame = [Drawing.Bitmap]::FromFile((Join-Path $SourceDirectory '相框.png'))
Export-Crop $frame 'Frame' @(102,133,722,854)
$frame.Dispose()
$sticker = [Drawing.Bitmap]::FromFile((Join-Path $SourceDirectory '贴纸.png'))
Export-Crop $sticker 'Sticker' @(79,50,195,167)
$sticker.Dispose()

$reference = [Drawing.Bitmap]::FromFile($ReferenceImage)
# P1 photo interiors. Irregular alpha corners exclude the paper frames and overlaid stickers.
Export-Crop $reference 'SampleHouse' @(301,159,231,293) @(301,168, 527,159, 530,449, 302,450)
Export-Crop $reference 'SampleFrog' @(577,173,207,281) @(
    579,173, 783,175, 781,421, 770,429, 758,440, 750,453, 577,453
)
Export-Crop $reference 'SampleSquirrel' @(291,488,239,286) @(
    294,494, 525,488, 529,772, 342,772, 334,756, 321,746, 310,739, 292,736
)
Export-Crop $reference 'SampleTraveler' @(568,492,219,291) @(
    568,492, 786,496, 786,781, 568,781
)
$reference.Dispose()

$cream = [Drawing.ColorTranslator]::FromHtml('#f7f0d8')
$brown = [Drawing.ColorTranslator]::FromHtml('#b78e6f')

function Export-Tab([string]$Kind, [bool]$Selected) {
    $state = if ($Selected) { 'Selected' } else { 'Idle' }
    $name = 'Tab' + $Kind + $state
    $fill = if ($Selected) { $brown } else { $cream }
    $ink = if ($Selected) { $cream } else { $brown }
    $bitmap = New-Canvas 336 256
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.ScaleTransform(2,2)
    $path = [Drawing.Drawing2D.GraphicsPath]::new()
    $path.AddLine(3,128,2,29)
    $path.AddBezier(2,29,2,11,17,2,35,2)
    $path.AddBezier(35,2,74,3,117,0,151,2)
    $path.AddBezier(151,2,161,4,163,18,165,34)
    $path.AddLine(165,34,168,128)
    $path.CloseFigure()
    $brush = [Drawing.SolidBrush]::new($fill)
    $graphics.FillPath($brush,$path)
    $brush.Dispose()
    $path.Dispose()
    $pen = [Drawing.Pen]::new($ink,10)
    $pen.StartCap = [Drawing.Drawing2D.LineCap]::Round
    $pen.EndCap = [Drawing.Drawing2D.LineCap]::Round
    $pen.LineJoin = [Drawing.Drawing2D.LineJoin]::Round
    if ($Kind -eq 'Symbols') {
        $graphics.DrawEllipse($pen,57,27,55,53)
        $brush = [Drawing.SolidBrush]::new($ink)
        $graphics.FillEllipse($brush,77,46,15,15)
        $brush.Dispose()
        $symbolSvg = '<ellipse cx="84.5" cy="53.5" rx="27.5" ry="26.5" fill="none" stroke="{INK}" stroke-width="10"/><circle cx="84.5" cy="53.5" r="7.5" fill="{INK}"/>'
    } else {
        $points = [Drawing.PointF[]]@([Drawing.PointF]::new(54,28),[Drawing.PointF]::new(116,27),[Drawing.PointF]::new(115,72),[Drawing.PointF]::new(53,74))
        $graphics.DrawPolygon($pen,$points)
        $graphics.DrawLine($pen,58,68,110,33)
        $symbolSvg = '<path d="M54 28L116 27L115 72L53 74Z M58 68L110 33" fill="none" stroke="{INK}" stroke-width="10" stroke-linecap="round" stroke-linejoin="round"/>'
    }
    $pen.Dispose()
    $graphics.Dispose()
    Save-Asset $bitmap $name
    $fillHex = '#' + $fill.R.ToString('x2') + $fill.G.ToString('x2') + $fill.B.ToString('x2')
    $inkHex = '#' + $ink.R.ToString('x2') + $ink.G.ToString('x2') + $ink.B.ToString('x2')
    $svg = '<svg xmlns="http://www.w3.org/2000/svg" width="336" height="256" viewBox="0 0 168 128"><path fill="' + $fillHex + '" d="M3 128L2 29C2 11 17 2 35 2C74 3 117 0 151 2C161 4 163 18 165 34L168 128Z"/>' + $symbolSvg.Replace('{INK}',$inkHex) + '</svg>'
    [IO.File]::WriteAllText((Join-Path $previewDirectory ($name + '.svg')), $svg, [Text.UTF8Encoding]::new($false))
}

Export-Tab 'Symbols' $true
Export-Tab 'Symbols' $false
Export-Tab 'Photos' $true
Export-Tab 'Photos' $false

function Export-Arrow {
    $bitmap = New-Canvas 200 144
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $path = [Drawing.Drawing2D.GraphicsPath]::new()
    $path.AddLine(198,4,89,4)
    $path.AddBezier(89,4,73,4,65,7,55,16)
    $path.AddLine(55,16,9,56)
    $path.AddBezier(9,56,0,65,0,74,9,84)
    $path.AddLine(9,84,52,132)
    $path.AddBezier(52,132,59,140,71,140,88,140)
    $path.AddLine(88,140,198,139)
    $path.CloseFigure()
    $brush = [Drawing.SolidBrush]::new($brown)
    $graphics.FillPath($brush,$path)
    $brush.Dispose()
    $path.Dispose()
    $pen = [Drawing.Pen]::new($cream,5)
    $pen.DashPattern = [single[]]@(2.2,2)
    $pen.DashCap = [Drawing.Drawing2D.DashCap]::Round
    $pen.LineJoin = [Drawing.Drawing2D.LineJoin]::Round
    $points = [Drawing.PointF[]]@([Drawing.PointF]::new(193,16),[Drawing.PointF]::new(87,16),[Drawing.PointF]::new(66,23),[Drawing.PointF]::new(20,67),[Drawing.PointF]::new(64,127),[Drawing.PointF]::new(193,127))
    $graphics.DrawLines($pen,$points)
    $pen.Dispose()
    $triangle = [Drawing.Drawing2D.GraphicsPath]::new()
    $triangle.AddLine(91,44,58,69)
    $triangle.AddBezier(58,69,54,72,55,77,60,81)
    $triangle.AddLine(60,81,90,104)
    $triangle.AddBezier(90,104,96,107,99,104,99,98)
    $triangle.AddLine(99,98,99,49)
    $triangle.AddBezier(99,49,99,43,96,41,91,44)
    $triangle.CloseFigure()
    $brush = [Drawing.SolidBrush]::new($cream)
    $graphics.FillPath($brush,$triangle)
    $brush.Dispose()
    $triangle.Dispose()
    $graphics.Dispose()
    Save-Asset $bitmap 'ArrowLeft'
}

Export-Arrow

$expected = @('Book','Frame','Sticker','SampleHouse','SampleFrog','SampleSquirrel','SampleTraveler','TabSymbolsSelected','TabSymbolsIdle','TabPhotosSelected','TabPhotosIdle','ArrowLeft')
foreach ($name in $expected) {
    $file = Join-Path $assetDirectory ($name + '.png')
    $bitmap = [Drawing.Bitmap]::FromFile($file)
    if ($bitmap.PixelFormat -ne [Drawing.Imaging.PixelFormat]::Format32bppArgb) { throw "Missing alpha channel: $name" }
    if ($bitmap.Width -lt 32 -or $bitmap.Height -lt 32) { throw "Invalid asset size: $name" }
    $covered = 0
    for ($y = 0; $y -lt 16; $y++) {
        for ($x = 0; $x -lt 16; $x++) {
            if ($bitmap.GetPixel([int](($x + 0.5) * $bitmap.Width / 16), [int](($y + 0.5) * $bitmap.Height / 16)).A -gt 0) { $covered++ }
        }
    }
    if ($covered -lt 64) { throw "Unexpectedly empty asset: $name" }
    Write-Output "$name $($bitmap.Width)x$($bitmap.Height) ARGB"
    $bitmap.Dispose()
}

$reference = [Drawing.Bitmap]::FromFile($ReferenceImage)
foreach ($crop in @(@('SampleHouse',301,159),@('SampleFrog',577,173),@('SampleSquirrel',291,488),@('SampleTraveler',568,492))) {
    $bitmap = [Drawing.Bitmap]::FromFile((Join-Path $assetDirectory ($crop[0] + '.png')))
    $x = [int]($bitmap.Width / 2)
    $y = [int]($bitmap.Height / 2)
    if ($bitmap.GetPixel($x,$y).ToArgb() -ne $reference.GetPixel(($x + $crop[1]),($y + $crop[2])).ToArgb()) { throw "Photo pixels differ from the P1 source: $($crop[0])" }
    $bitmap.Dispose()
}
$reference.Dispose()
Write-Output 'PASS: all 13 assets contain visible alpha content; photo center pixels match P1 exactly.'
