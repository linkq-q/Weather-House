[CmdletBinding()]
param(
    [string]$InputDirectory = (Join-Path (Split-Path -Parent $PSScriptRoot) 'PrototypeReview\UiInventory'),
    [string]$OutputDirectory = 'C:\Users\31483\Desktop\天气屋\UI\界面截图集与手账动画方案_20261021'
)

$ErrorActionPreference = 'Stop'
$utf8 = New-Object System.Text.UTF8Encoding($false)
$metadataPath = Join-Path $InputDirectory 'metadata.json'
if (-not (Test-Path -LiteralPath $metadataPath -PathType Leaf)) {
    throw "截图清单尚未生成：$metadataPath"
}
$manifest = Get-Content -LiteralPath $metadataPath -Raw -Encoding UTF8 | ConvertFrom-Json
if ([string]$manifest.result -notmatch '^PASS(?:\s|:|$)') {
    throw "截图检查尚未通过，未导出：$($manifest.result)"
}
$items = @($manifest.items | Sort-Object { [int]$_.index })
if ($items.Count -ne 19 -or (($items | ForEach-Object { [int]$_.index }) -join ',') -ne ((1..19) -join ',')) {
    throw '截图清单必须完整包含编号 1 至 19，且每个编号只出现一次。'
}

# Validate the complete input before writing any delivery files.
$names = @{}
foreach ($item in $items) {
    $name = [string]$item.filename
    if ([string]::IsNullOrWhiteSpace($name) -or [string]$item.provenance -match '^SKIP') {
        throw "第 $($item.index) 张缺少实际截图：$($item.provenance)"
    }
    if ([System.IO.Path]::GetFileName($name) -ne $name -or $name -notmatch '^[0-9]{2}\.png$' -or $names.ContainsKey($name)) {
        throw "截图文件名无效或重复：$name"
    }
    $names[$name] = $true
    $source = Join-Path $InputDirectory $name
    if (-not (Test-Path -LiteralPath $source -PathType Leaf)) { throw "找不到截图：$source" }
    $header = New-Object byte[] 24
    $stream = [System.IO.File]::OpenRead($source)
    try { $length = $stream.Read($header, 0, 24) } finally { $stream.Dispose() }
    if ($length -ne 24 -or [BitConverter]::ToString($header[0..7]) -ne '89-50-4E-47-0D-0A-1A-0A') {
        throw "文件不是有效 PNG：$source"
    }
    $width = ([int]$header[16] -shl 24) -bor ([int]$header[17] -shl 16) -bor ([int]$header[18] -shl 8) -bor [int]$header[19]
    $height = ([int]$header[20] -shl 24) -bor ([int]$header[21] -shl 16) -bor ([int]$header[22] -shl 8) -bor [int]$header[23]
    if ($width -le 0 -or $height -le 0 -or $width -ne [int]$item.width -or $height -ne [int]$item.height) {
        throw "PNG 尺寸与清单不符：$name，图片 ${width}x${height}，清单 $($item.width)x$($item.height)"
    }
}

function Html([object]$Value) { [System.Net.WebUtility]::HtmlEncode([string]$Value) }
$categories = @{
    1 = 'HUD交谈'; 2 = 'HUD交谈'; 3 = 'HUD交谈'; 4 = 'HUD交谈'; 5 = 'HUD交谈'
    6 = '相机'; 7 = '相机'; 8 = '相机'
    9 = '照片'; 10 = '字符详情'; 11 = '字符详情'; 12 = '字符详情'
    13 = '照片'; 14 = '照片'; 15 = '照片'; 16 = '反馈'; 17 = '反馈'
    18 = '照片'; 19 = '字符详情'
}
$designNotes = @{
    1 = '顶部状态文字、左下操作提示的底板、字重、留白与信息层级。'
    2 = '动物气泡轮廓、字形留白、动物名与交谈提示的组合。'
    3 = '回应气泡、标题、输入底板、占位文字、发送及关闭按钮。'
    4 = '同一回应组件的有文字状态；检查正文可读性与输入区域。'
    5 = '发送后人物气泡和中文正文；与输入态保持同一视觉体系。'
    6 = '取景框、快门、退出按钮、焦距及操作提示。'
    7 = '点击对焦的方框标记，以及对焦距离的文字反馈。'
    8 = '录制中的进度表达；当前只将相机提示替换为百分比文字。'
    9 = '空状态文案、相片分页箭头与页码。已美化书本仅作为上下文。'
    10 = '详情遮罩、矩形卡片、字符、关闭按钮与未记录提示。'
    11 = '猜测详情卡、输入框、记下来按钮、相关照片入口。'
    12 = '已确认释义的排版和证据入口，沿用字符详情的组件结构。'
    13 = '照片详情底板、观察记录标题、字语、播放及关闭按钮。'
    14 = '播放状态的暂停按钮、当前帧与总帧数；属于同一照片详情。'
    15 = '照片无法读取的占位文字；当前没有独立错误弹窗。'
    16 = '顶部判断中文字的等待感与可读性；当前没有独立加载弹窗。'
    17 = '同一顶部状态控件的失败与重试提示。'
    18 = '关联照片为空时的说明和分页控件。已美化书本仅作为上下文。'
    19 = '猜测输入框的空状态与占位文案，和有文字状态一起设计。'
}
$cards = New-Object System.Collections.Generic.List[string]
$rows = New-Object System.Collections.Generic.List[object]
$imageDirectory = Join-Path $OutputDirectory '截图\原始PNG'
[System.IO.Directory]::CreateDirectory($imageDirectory) | Out-Null
foreach ($item in $items) {
    $number = [int]$item.index
    $category = $categories[$number]
    $badge = '真实运行画面'
    $badgeClass = 'actual'
    if ($number -in @(3, 4)) { $badge = '临时输入状态'; $badgeClass = 'temporary' }
    if ($number -in @(9, 10, 11, 12, 15, 18, 19)) { $badge = '临时测试数据'; $badgeClass = 'temporary' }
    if ($number -in @(5, 8, 16, 17)) { $badge = '模拟状态'; $badgeClass = 'simulated' }
    $relativeImage = '截图/原始PNG/' + [Uri]::EscapeDataString([string]$item.filename)
    Copy-Item -LiteralPath (Join-Path $InputDirectory $item.filename) -Destination (Join-Path $imageDirectory $item.filename) -Force
    $id = $number.ToString('D2')
    $title = Html $item.title
    $trigger = Html $item.trigger
    $state = Html $item.state
    $provenance = Html $item.provenance
    $design = Html $designNotes[$number]
    $image = Html $relativeImage
    $cards.Add(@"
<article class="card" data-category="$category" id="ui-$id">
  <div class="card-head"><span class="number">$id</span><div><span class="eyebrow">$category</span><h2>$title</h2></div><span class="badge $badgeClass">$badge</span></div>
  <a class="image-link" href="$image" data-lightbox data-caption="$id · $title" aria-label="放大查看 $title"><img src="$image" alt="$title，$state" width="$($item.width)" height="$($item.height)" loading="lazy"><span class="zoom-hint">点击查看原图 ↗</span></a>
  <div class="card-body"><dl><div><dt>玩家入口</dt><dd>$trigger</dd></div><div><dt>画面状态</dt><dd>$state</dd></div><div><dt>待改部位</dt><dd>$design</dd></div></dl><details><summary>截图来源与状态说明 <span>$($item.width) × $($item.height)</span></summary><p>$provenance</p></details></div>
</article>
"@)
    $rows.Add([pscustomobject]@{
        '编号' = $id; '分类' = $category; '界面' = $item.title; '玩家入口' = $item.trigger
        '画面状态' = $item.state; '待改部位' = $designNotes[$number]; '状态标记' = $badge
        '截图来源' = $item.provenance; '图片' = $relativeImage; '宽度' = $item.width; '高度' = $item.height
    })
}
$rows | Export-Csv -LiteralPath (Join-Path $OutputDirectory '截图目录.csv') -NoTypeInformation -Encoding UTF8
Copy-Item -LiteralPath $metadataPath -Destination (Join-Path $OutputDirectory 'metadata.json') -Force
$scene = Html $manifest.scene
$utc = Html $manifest.utc
$content = $cards -join "`n"
$html = @"
<!doctype html>
<html lang="zh-CN"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><meta name="color-scheme" content="light"><title>天气屋 · 待美化 UI 截图集</title>
<style>
:root{--paper:#f3f0e7;--white:#fffcf4;--ink:#283b34;--muted:#6e7669;--line:#d7dace;--green:#496651;--pale:#e2e8da;--clay:#986b4c}*{box-sizing:border-box}body{margin:0;background:var(--paper);color:var(--ink);font-family:"Microsoft YaHei","PingFang SC",sans-serif;font-size:15px;line-height:1.7}a{color:var(--green)}button{font:inherit;cursor:pointer}button:focus-visible,a:focus-visible,summary:focus-visible{outline:3px solid #a57b4b;outline-offset:4px}header,main,footer{width:min(1420px,calc(100% - 64px));margin:auto}header{padding:56px 0 28px}.topline{display:flex;justify-content:space-between;gap:24px;align-items:center}.brand{font-size:13px;letter-spacing:.17em;font-weight:700}.plan-link{text-decoration:none;border-bottom:1px solid #9fac98;padding-bottom:3px}.intro{display:grid;grid-template-columns:1.1fr 1fr;gap:56px;align-items:end;margin-top:34px}h1{font-size:clamp(31px,3vw,46px);line-height:1.25;margin:0 0 15px;font-weight:700;letter-spacing:.03em}.subtitle{margin:0;color:var(--muted)}.scope{border-left:2px solid #afbea7;padding-left:22px;font-size:14px}.scope p{margin:0 0 9px}.scope p:last-child{margin:0}.toolbar{display:flex;flex-wrap:wrap;justify-content:space-between;align-items:center;gap:14px;padding:18px 0;border-top:1px solid var(--line);border-bottom:1px solid var(--line);margin:8px 0 28px}.filters{display:flex;flex-wrap:wrap;gap:7px}.filter{border:1px solid transparent;background:transparent;padding:6px 15px;color:var(--muted);border-radius:4px}.filter[aria-pressed=true]{background:var(--green);color:white}.filter:hover{border-color:var(--green)}.count{font-size:13px;color:var(--muted);white-space:nowrap}.gallery{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:28px}.card{background:var(--white);border:1px solid var(--line);overflow:hidden;border-radius:7px;scroll-margin-top:18px}.card[hidden]{display:none}.card-head{display:flex;align-items:center;gap:13px;padding:18px 20px}.number{font-family:Georgia,serif;font-size:31px;line-height:1;color:#9aa78e;min-width:39px}.eyebrow{font-size:11px;color:var(--muted);letter-spacing:.12em}.card h2{font-size:18px;margin:1px 0 0;line-height:1.4;font-weight:700}.badge{margin-left:auto;font-size:11px;white-space:nowrap;padding:3px 7px;border-radius:3px;background:#edf1e7;color:#45644a}.badge.temporary{background:#eee9dc;color:#806b4e}.badge.simulated{background:#f1e3d7;color:#97613d}.image-link{display:block;position:relative;background:#dce2d6;line-height:0}.image-link img{display:block;width:100%;height:auto;aspect-ratio:auto;object-fit:contain}.zoom-hint{position:absolute;right:12px;bottom:12px;color:#fff;background:rgba(31,47,39,.76);padding:7px 9px;line-height:1;font-size:11px;border-radius:3px;opacity:0;transition:opacity .15s}.image-link:hover .zoom-hint,.image-link:focus .zoom-hint{opacity:1}.card-body{padding:17px 20px 18px}dl,dd{margin:0}dl>div{display:grid;grid-template-columns:66px 1fr;gap:11px;margin:0 0 7px}dt{font-size:12px;color:var(--muted);padding-top:2px}dd{font-size:13px}details{border-top:1px solid #e5e6dc;margin-top:14px;padding-top:10px;font-size:12px;color:var(--muted)}summary{cursor:pointer;line-height:1.8}summary span{float:right;color:#8b9385}details p{margin:10px 0 0;color:#637064;overflow-wrap:anywhere}.legend{margin:24px 0 32px;padding:16px 20px;border-left:3px solid #b5bfa9;background:#e9eddf;font-size:13px}.legend p{margin:3px 0}footer{padding:28px 0 44px;border-top:1px solid var(--line);margin-top:35px;color:var(--muted);font-size:12px}.footer-links{display:flex;flex-wrap:wrap;gap:18px;margin-bottom:10px}.source{overflow-wrap:anywhere}dialog{border:0;background:#13221b;color:white;width:min(96vw,1800px);max-width:none;padding:0;border-radius:6px;max-height:96vh}dialog::backdrop{background:rgba(15,23,18,.88)}.lightbox-head{display:flex;align-items:center;gap:18px;padding:12px 18px;font-size:14px}.lightbox-head strong{font-weight:400;flex:1}.lightbox-head a{color:#dae7d3;font-size:12px}.close{background:transparent;color:white;border:1px solid #526256;border-radius:3px;font-size:14px;padding:5px 12px}.lightbox-image{display:block;max-width:100%;width:auto;height:auto;max-height:calc(96vh - 68px);margin:auto;object-fit:contain}.noscript{padding:10px;background:#eee5cf;font-size:13px}@media(max-width:1000px){header,main,footer{width:calc(100% - 40px)}.intro{gap:28px}.card-head{padding:15px 14px;gap:9px}.card h2{font-size:16px}.badge{font-size:10px}.number{font-size:26px;min-width:32px}.card-body{padding:15px}.gallery{gap:20px}}@media(max-width:720px){header,main,footer{width:calc(100% - 28px)}header{padding:27px 0 20px}.topline{align-items:flex-start;font-size:12px;gap:12px}.brand{font-size:11px;letter-spacing:.11em}.intro{display:block;margin-top:27px}h1{font-size:31px}.scope{margin-top:22px;font-size:12px;padding-left:14px}.subtitle{font-size:13px}.toolbar{padding:14px 0;margin-bottom:20px}.filter{font-size:12px;padding:6px 10px}.filters{gap:4px}.count{font-size:11px}.gallery{grid-template-columns:1fr;gap:21px}.card-head{padding:14px 13px}.card h2{font-size:17px}.badge{font-size:10px}.zoom-hint{opacity:1;font-size:10px;right:8px;bottom:8px}.card-body{padding:14px}dl>div{grid-template-columns:61px 1fr;gap:7px}dd{font-size:12px}.legend{padding:13px 14px;font-size:12px}summary span{float:none;margin-left:8px}.lightbox-head{padding:10px;gap:10px;font-size:12px}.lightbox-head a{display:none}.close{padding:4px 8px}.footer-links{gap:12px}footer{padding-bottom:28px}}@media(prefers-reduced-motion:reduce){*{transition:none!important}}
</style></head><body>
<header><div class="topline"><span class="brand">WEATHER HOUSE / UI INVENTORY</span><a class="plan-link" href="手账开合动画方案.html">手账开合动画方案 ↗</a></div><div class="intro"><div><h1>待美化 UI 截图集</h1><p class="subtitle">19 个代表状态 · 当前主游戏场景 · 点击图片查看完整原图</p></div><div class="scope"><p>覆盖 HUD、交谈、相机、字符详情、照片与状态反馈。重复控件按代表状态整理，方便逐项做设计。</p><p>书本、页签、贴纸与相册正文已完成美化，在本集中仅作为旧控件的背景上下文。</p><p>当前游戏没有独立的暂停、设置、退出确认或任务完成弹窗；成功、失败、天气与等待文案复用顶部状态控件。</p></div></div></header>
<main><nav class="toolbar" aria-label="截图分类"><div class="filters"><button class="filter" data-filter="全部" aria-pressed="true">全部 19</button><button class="filter" data-filter="HUD交谈" aria-pressed="false">HUD / 交谈</button><button class="filter" data-filter="相机" aria-pressed="false">相机</button><button class="filter" data-filter="字符详情" aria-pressed="false">字符详情</button><button class="filter" data-filter="照片" aria-pressed="false">照片</button><button class="filter" data-filter="反馈" aria-pressed="false">反馈</button></div><span class="count" id="visible-count" aria-live="polite">显示 19 / 19</span></nav>
<noscript><p class="noscript">当前浏览器未启用 JavaScript。全部截图仍可阅读，点击图片可直接打开原图。</p></noscript>
<div class="gallery">$content</div>
<aside class="legend"><p><strong>如何看这些截图</strong>　图片来自 Unity 实际 Canvas 渲染。为展示各分支，部分画面使用临时测试数据或模拟状态，卡片均有标记，展开底部说明可查看来源。</p><p>“模拟状态”表示没有真的发送 AI 请求、录制新照片或写入存档。输入空态与草稿、照片暂停与播放共用组件，但分别保留以便设计完整状态。</p></aside></main>
<footer><div class="footer-links"><a href="手账开合动画方案.html">查看手账开合动画方案</a><a href="截图目录.csv">下载截图目录 CSV</a><a href="metadata.json">原始截图清单 JSON</a></div><div class="source">场景：$scene<br>截图批次 UTC：$utc<br>本页与所有图片均可离线使用，请保留“截图 / 原始PNG”相对目录。</div></footer>
<dialog id="lightbox" aria-label="截图原图"><div class="lightbox-head"><strong id="lightbox-title"></strong><a id="original-link" target="_blank" rel="noopener">单独打开原图 ↗</a><button type="button" class="close" id="close-lightbox">关闭 ×</button></div><img id="lightbox-image" class="lightbox-image" alt=""></dialog>
<script>
(() => {
  const cards = [...document.querySelectorAll('.card')];
  const buttons = [...document.querySelectorAll('[data-filter]')];
  const count = document.getElementById('visible-count');
  buttons.forEach(button => button.addEventListener('click', () => {
    const category = button.dataset.filter;
    buttons.forEach(item => item.setAttribute('aria-pressed', String(item === button)));
    cards.forEach(card => { card.hidden = category !== '全部' && card.dataset.category !== category; });
    count.textContent = '显示 ' + cards.filter(card => !card.hidden).length + ' / ' + cards.length;
  }));
  const dialog = document.getElementById('lightbox');
  const picture = document.getElementById('lightbox-image');
  const title = document.getElementById('lightbox-title');
  const original = document.getElementById('original-link');
  document.querySelectorAll('[data-lightbox]').forEach(link => link.addEventListener('click', event => {
    if (typeof dialog.showModal !== 'function') return;
    event.preventDefault();
    picture.src = link.href;
    picture.alt = link.dataset.caption;
    title.textContent = link.dataset.caption;
    original.href = link.href;
    dialog.showModal();
  }));
  document.getElementById('close-lightbox').addEventListener('click', () => dialog.close());
  dialog.addEventListener('click', event => { if (event.target === dialog) dialog.close(); });
})();
</script></body></html>
"@
[System.IO.File]::WriteAllText((Join-Path $OutputDirectory '待美化UI截图集.html'), $html, $utf8)
[pscustomobject]@{
    Result = 'PASS'
    Images = $items.Count
    Html = Join-Path $OutputDirectory '待美化UI截图集.html'
    Directory = $OutputDirectory
}
