[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][datetime]$CapturedAfter,
    [string]$InputDirectory = (Join-Path (Split-Path -Parent $PSScriptRoot) 'PrototypeReview\AlbumBook\OpeningFrames'),
    [string]$OutputDirectory = 'C:\Users\31483\Desktop\天气屋\UI\界面截图集与手账动画方案_20261021\Unity动画预览',
    [string]$Ffmpeg = 'C:\Users\31483\AppData\Local\JianyingPro\Apps\11.5.0.14471\ffmpeg.exe',
    [double]$AnimationDuration = 1.15
)

$ErrorActionPreference = 'Stop'
$utf8 = New-Object System.Text.UTF8Encoding($false)
$checksPath = Join-Path (Split-Path -Parent $InputDirectory) 'checks.txt'
$capturePath = Join-Path $InputDirectory 'capture.txt'
$htmlPath = Join-Path $OutputDirectory '手账动画预览.html'
foreach ($path in @($Ffmpeg, $checksPath, $capturePath, $htmlPath)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "缺少文件：$path" }
}

# Reject a previous capture before replacing the user-facing preview.
$captureFiles = @($checksPath, $capturePath) + @(0..75 | ForEach-Object { Join-Path $InputDirectory ('frame-{0:000}.jpg' -f $_) })
foreach ($path in $captureFiles) {
    $item = Get-Item -LiteralPath $path
    if ($item.Length -eq 0 -or $item.LastWriteTimeUtc -lt $CapturedAfter.ToUniversalTime()) {
        throw "录制文件为空或早于指定时间，未导出旧帧：$path"
    }
}
$checks = Get-Content -LiteralPath $checksPath -Raw -Encoding UTF8
if ($checks -match '(?m)^FAIL' -or $checks -notmatch 'PASS: Play structure and interactions\.') {
    throw '本轮 Unity 交互验收尚未完成或没有通过，未导出。'
}

$inputPattern = Join-Path $InputDirectory 'frame-%03d.jpg'
$gifPath = Join-Path $OutputDirectory '手账开书v4.gif'
$mp4Path = Join-Path $OutputDirectory '手账开书v4.mp4'
$gifLog = Join-Path $OutputDirectory 'GIF编码记录.txt'
$mp4Log = Join-Path $OutputDirectory 'MP4编码记录.txt'

& $Ffmpeg -hide_banner -loglevel warning -y -framerate 30 -start_number 0 -i $inputPattern -frames:v 76 -filter_complex '[0:v]scale=960:-1:flags=lanczos,setsar=1,split[a][b];[a]palettegen=stats_mode=diff[p];[b][p]paletteuse=dither=sierra2_4a:diff_mode=rectangle' -loop 0 $gifPath 2> $gifLog
if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $gifPath)) { throw "GIF 编码失败，请查看：$gifLog" }

& $Ffmpeg -hide_banner -loglevel warning -y -framerate 30 -start_number 0 -i $inputPattern -frames:v 76 -vf 'scale=trunc(iw/2)*2:trunc(ih/2)*2:flags=lanczos' -c:v h264_mf -pix_fmt yuv420p -b:v 10M -movflags +faststart -an $mp4Path 2> $mp4Log
$mp4Available = $LASTEXITCODE -eq 0 -and (Test-Path -LiteralPath $mp4Path) -and (Get-Item -LiteralPath $mp4Path).Length -gt 0
if (-not $mp4Available) { Write-Warning "MP4 编码不可用；HTML 将显示 GIF。详情：$mp4Log" }

Copy-Item -LiteralPath (Join-Path $InputDirectory 'frame-075.jpg') -Destination (Join-Path $OutputDirectory '最终展开画面.jpg') -Force
Copy-Item -LiteralPath $checksPath -Destination (Join-Path $OutputDirectory 'Unity验收记录.txt') -Force
Copy-Item -LiteralPath $capturePath -Destination (Join-Path $OutputDirectory 'Unity录制说明.txt') -Force
$durationText = $AnimationDuration.ToString('0.##', [Globalization.CultureInfo]::InvariantCulture)
$html = Get-Content -LiteralPath $htmlPath -Raw -Encoding UTF8
$html = [regex]::Replace($html, '(?<=data-duration=")[^"]+', $durationText)
$html = [regex]::Replace($html, '(?<=data-video-available=")[^"]+', $mp4Available.ToString().ToLowerInvariant())
$html = [regex]::Replace($html, '(?<=id="duration">)[^<]+', $durationText)
[System.IO.File]::WriteAllText($htmlPath, $html, $utf8)
$manifest = [ordered]@{
    title = '渐变光晕 · 星尘显影 v4'
    capturedAfterUtc = $CapturedAfter.ToUniversalTime().ToString('o')
    exportedAtUtc = [datetime]::UtcNow.ToString('o')
    source = $InputDirectory
    frameRate = 30
    frames = 76
    animationDurationSeconds = $AnimationDuration
    sampling = 'Actual Unity gameplay Canvas; deterministic 30 fps samples, scene time paused. Not a real-time performance recording.'
    mp4Available = $mp4Available
    gif = '手账开书v4.gif'
    mp4 = $(if ($mp4Available) { '手账开书v4.mp4' } else { $null })
}
[System.IO.File]::WriteAllText((Join-Path $OutputDirectory '导出信息.json'), ($manifest | ConvertTo-Json), $utf8)
Write-Output "已导出 76 帧实际 Unity Canvas 预览：$htmlPath"
