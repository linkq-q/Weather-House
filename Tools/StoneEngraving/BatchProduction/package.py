"""Package actual Unity renders; keeps the user's approved overlay gallery unchanged."""
from pathlib import Path
import json,shutil
from PIL import Image,ImageDraw,ImageFont

ROOT=Path(__file__).resolve().parents[3]
REVIEW=ROOT/'StoneEngravingReview/20261029/SevenStoneProduction'
DEST=Path('C:/Users/31483/Desktop/天气屋/20261029_七块石刻_刻槽成品')

def package():
    DEST.mkdir(exist_ok=True)
    items=json.loads((REVIEW/'maps-report.json').read_text(encoding='utf-8'))
    checks=(REVIEW/'unity-checks.txt').read_text()
    assert len(items)==7 and checks.count('missing=0, steep=0')==7
    font=ImageFont.truetype('C:/Windows/Fonts/msyh.ttc',28)
    title=ImageFont.truetype('C:/Windows/Fonts/msyhbd.ttc',42)
    sheet=Image.new('RGB',(1600,2690),'#eeeee6');draw=ImageDraw.Draw(sheet)
    draw.text((38,26),'七块石刻 · 刻槽与动物字',font=title,fill='#29382e')
    draw.text((40,90),'固定破边 / 同等线宽的动物字 / Unity 真实材质渲染',font=font,fill='#566051')
    cards=[]
    for i,item in enumerate(items):
        folder=DEST/item['id'];folder.mkdir(exist_ok=True)
        for name in ['01-close.png','02-oblique.png','03-photo-thumbnail.png','04-gameplay-thumbnail.png','stencil.png']:
            shutil.copy2(REVIEW/item['id']/name,folder/name)
        x=30+i%2*790;y=155+i//2*630
        draw.text((x+12,y),f'{i+1:02d}  {item["title"]}',font=font,fill='#29382e')
        sheet.paste(Image.open(folder/'01-close.png').resize((770,578),Image.Resampling.LANCZOS),(x,y+42))
        widths='；'.join(f'{g["id"]} {g["before"]:.0f} → {g["after"]:.0f} px' for g in item['glyphs'])
        key=item['id']
        live=REVIEW/'MainScene/Play'/key
        actual=''
        if (live/'checks.txt').exists():
            for src,dest in [('02-gameplay-after.png','05-actual-gameplay.png'),('03-photo-mode.png','06-actual-photo.png')]:
                shutil.copy2(live/src,folder/dest)
            actual=f'<details><summary>查看制作时的主场景实拍</summary><p>相机增强隔离已通过检查。此处为初始摆放，最终角度由你在场景中调整。</p><img class="hero" src="{key}/05-actual-gameplay.png"><img class="hero" src="{key}/06-actual-photo.png"></details>'
        cards.append(f'''<article id="{key}"><h2>{item['title']}</h2><p>插图代表线宽 {item['picture_width_px']:.1f} px · 动物字 {widths}</p>
<a href="{key}/01-close.png"><img class="hero" src="{key}/01-close.png" alt="{item['title']}成品"></a>
<div class="views"><figure><img src="{key}/02-oblique.png"><figcaption>斜角下的刻槽与崩边</figcaption></figure>
<figure><img class="thumb" src="{key}/03-photo-thumbnail.png"><figcaption>原始材质缩略图</figcaption></figure>
<figure><img class="thumb" src="{key}/04-gameplay-thumbnail.png"><figcaption>游玩视角增强缩略图</figcaption></figure></div>
<details><summary>查看用于生成凹槽的线稿</summary><img class="hero" src="{key}/stencil.png"></details>{actual}</article>''')
    sheet.save(DEST/'七块成品_总览.png')
    document='''<!doctype html><html lang="zh-CN"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>七块石刻成品</title>
<style>body{margin:0;background:#eeeee6;color:#29382e;font:17px/1.65 system-ui,"Microsoft YaHei"}main{max-width:1100px;margin:auto;padding:32px 24px}h1{font-size:34px}article{background:#faf9f4;padding:24px;margin:30px 0;border-radius:12px}.hero{width:100%;display:block}a{color:#385b48}nav{display:flex;flex-wrap:wrap;gap:24px}.views{display:flex;align-items:center;gap:18px}figure{flex:1;margin:16px 0}figure:first-child{flex:2}figure img{width:100%;height:auto}figure img.thumb{width:192px;max-width:100%}figcaption{font-size:14px;color:#596253}summary{cursor:pointer}@media(max-width:700px){main{padding:20px 12px}article{padding:14px}.views{flex-wrap:wrap}figure:first-child{flex-basis:100%}}</style>
<main><h1>七块石刻 · 刻槽成品</h1><p>保留已确认的石形与排版。破边位置固定，不会闪动；动物字保留原字库轮廓，笔画加粗到不低于对应插图的代表线宽。这里是独立 Unity 工程的真实材质渲染，缩略图用于比较发现性，不是主场景相机截图。</p><nav>'''
    document+=''.join(f'<a href="#{i["id"]}">{i["title"]}</a>' for i in items)+'</nav>'+''.join(cards)+'</main></html>'
    (DEST/'成品预览.html').write_text(document,encoding='utf-8')
    shutil.copy2(REVIEW/'maps-report.json',DEST/'生成与线宽检查.json')
    shutil.copy2(REVIEW/'unity-checks.txt',DEST/'刻面覆盖检查.txt')
    print(DEST)

if __name__=='__main__':package()
