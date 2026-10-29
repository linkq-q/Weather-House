"""Package unretouched Unity renders into a review contact sheet and offline gallery."""
from pathlib import Path
import json
import shutil
import html
from PIL import Image, ImageDraw, ImageFont

ROOT=Path(__file__).resolve().parents[3]
REVIEW=ROOT/'StoneEngravingReview/20261029/SevenStonePreview'
DEST=Path('C:/Users/31483/Desktop/天气屋/20261029_七块石刻_选石覆盖预览')
ORDER=['wind','wet_dry','strong_weak','grass_tree','first_then','together','here_there']
NOTES={
 'wind':'旗帜、风线和落叶集中在同一刻面，风字独立置于下方。',
 'wet_dry':'湿在左、干在右；两字对齐各自的布，底部留出完整字区。',
 'strong_weak':'左弱右强。风线数量是主要差别；需判断两幅图能否明确表达强弱。',
 'grass_tree':'上树下草，字形完整。原图树干与鹿背相连，图意仍可能被误读，建议再调整。',
 'first_then':'横长石容纳三格叙事；先、后分别放在第一格与末格下方。',
 'together':'四只动物和同一片遮雨叶完整保留，一起字位于下方独立留白内。',
 'here_there':'这里、那里分别对应近处与远处池塘；完整保留指向关系。'
}

def package():
    report=json.loads((REVIEW/'selection.json').read_text(encoding='utf-8'))
    selected={x['id']:x for x in report['selected']}
    assert set(selected)==set(ORDER)
    DEST.mkdir(parents=True,exist_ok=True)
    font=ImageFont.truetype('C:/Windows/Fonts/msyh.ttc',31)
    small=ImageFont.truetype('C:/Windows/Fonts/msyh.ttc',23)
    title=ImageFont.truetype('C:/Windows/Fonts/msyhbd.ttc',49)
    sheet=Image.new('RGB',(1800,3020),'#eeeae1');draw=ImageDraw.Draw(sheet)
    draw.text((54,30),'七块石刻 · 选石与覆盖预览',font=title,fill='#29382e')
    draw.text((56,100),'真实项目网格渲染  /  动物语来自项目字库  /  等待确认，尚未刻槽、入场景',font=small,fill='#566051')
    cards=[];readme=['# 七块石刻选石与覆盖预览','',
      '这批用于确认石形、图案范围与字形排版。图像是 Unity 实际网格渲染，采用平面线稿覆盖；没有最终凹槽、崩边或主场景光照。',
      '主项目场景和正式资产未修改。候选尺寸是提案，尚未验证摆放后与周围环境的比例。俯视缩略图用于发现性参考，不是当前游戏相机截图。','']
    for i,key in enumerate(ORDER):
        item=selected[key];name=f'{i+1:02d}_{item["title"]}';folder=DEST/name;folder.mkdir(exist_ok=True)
        checks=(REVIEW/key/'coverage-check.txt').read_text(encoding='utf-8')
        assert 'missing_surface=0' in checks and 'steep_surface=0' in checks
        for f in ['00-blank-stone.png','01-overlay.png','02-oblique.png','03-gameplay-thumbnail.png','coverage-check.txt']:
            shutil.copy2(REVIEW/key/f,folder/f)
        image=Image.open(folder/'01-overlay.png').convert('RGB')
        assert image.size==(1280,960)
        x=40+(i%2)*880;y=170+(i//2)*700
        draw.rectangle((x,y,x+840,y+670),fill='#f9f7f1')
        draw.text((x+24,y+14),f'{i+1:02d}  {item["title"]}',font=font,fill='#29382e')
        sheet.paste(image.resize((840,630),Image.Resampling.LANCZOS),(x,y+58))
        note=html.escape(NOTES[key]);path=html.escape(name)
        cards.append(f'''<article id="{key}"><h2>{i+1:02d}　{item['title']}</h2><p>{note}</p>
<a href="{path}/01-overlay.png"><img class="hero" src="{path}/01-overlay.png" alt="{item['title']}的真实石头覆盖预览"></a>
<div class="comparisons"><figure><img src="{path}/00-blank-stone.png" alt="空白候选石头"><figcaption>空白候选石头</figcaption></figure>
<figure><img src="{path}/02-oblique.png" alt="斜角覆盖预览"><figcaption>斜角：检查折面和完整字形</figcaption></figure>
<figure class="thumb"><img src="{path}/03-gameplay-thumbnail.png" width="192" height="144" alt="俯视缩略预览"><figcaption>俯视缩略（非主场景截图）</figcaption></figure></div>
<p class="meta">候选宽度 {item['width_m']:g} m · 图画与字形覆盖检查通过 · <a href="{path}/01-overlay.png">打开原尺寸</a></p></article>''')
        readme += [f'## {i+1:02d} {item["title"]}',NOTES[key],f'- 网格：`{item["fit"]["path"]}`',
                   f'- 三角面：{item["fit"]["triangles"]}；候选宽度：{item["width_m"]} m。',
                   '- 覆盖检查：图画与字形采样无落空、无陡面。图意是否可理解仍需人类审图。','']
    x=920;y=2270
    for n,line in enumerate(['确认时重点看', '① 石形是否适合这张画', '② 图案与字是否完整、留白合适', '③ 不看标题能否读出图意', '当前仍是覆盖预览', '通过后才制作刻槽并放进场景']):
        draw.text((x+35,y+65+n*66),line,font=font if n==0 else small,fill='#344334')
    sheet.save(DEST/'七块石刻_总览.png')
    document='''<!doctype html><html lang="zh-CN"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<title>七块石刻 · 选石覆盖预览</title><style>
body{margin:0;background:#eeeae1;color:#28362d;font:17px/1.7 system-ui,"Microsoft YaHei",sans-serif}main{max-width:1180px;margin:auto;padding:40px 28px}h1{font-size:36px;line-height:1.3}h2{font-size:27px;margin-bottom:8px}a{color:#315b47}nav{display:flex;gap:22px;flex-wrap:wrap;margin:30px 0}article{background:#faf8f3;border-radius:12px;padding:28px;margin:34px 0}article p{max-width:940px}.hero{display:block;width:100%;height:auto}.comparisons{display:flex;gap:18px;align-items:center}figure{flex:1;margin:20px 0}figure img{width:100%;height:auto}figcaption,.meta{font-size:14px;color:#596253}.thumb img{width:192px;max-width:100%}.intro{background:#e0e5d8;padding:20px;border-left:4px solid #6b7b58}@media(max-width:700px){main{padding:20px 12px}article{padding:16px}.comparisons{flex-wrap:wrap}figure{flex-basis:45%}}
</style><main><h1>七块石刻 · 选石与覆盖预览</h1><p class="intro">先确认石头形状、图案范围与字形排版。以下为真实项目网格上的平面线稿覆盖；尚未制作最终刻槽或放入主场景。点大图可查看原尺寸。</p>
<nav>'''+''.join(f'<a href="#{k}">{selected[k]["title"]}</a>' for k in ORDER)+'</nav>'+''.join(cards)+'''
<p>检查通过代表图案和字形完整落在可用表面；不等于玩家一定能读懂含义。尤其请审看“草与树”的重叠关系和“强与弱”的差异。</p></main></html>'''
    (DEST/'选石预览.html').write_text(document,encoding='utf-8')
    (DEST/'说明.md').write_text('\n'.join(readme),encoding='utf-8')
    shutil.copy2(REVIEW/'selection.json',DEST/'候选网格与投影参数.json')
    assert document.count('<article ')==7 and len(list(DEST.glob('*/01-overlay.png')))==7
    print(str(DEST))

if __name__=='__main__':package()
