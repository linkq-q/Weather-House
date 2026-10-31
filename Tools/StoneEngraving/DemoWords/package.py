"""Verify the isolated build, publish only the three owned prefabs, and package review."""
from pathlib import Path
import hashlib, json, shutil
import numpy as np
from PIL import Image
from prepare import ROOT,HERE,PROJECT,DEST,SPECS,CANONICAL

REVIEW=PROJECT.parent/'Renders'
DESKTOP=Path('C:/Users/31483/Desktop/天气屋/20261030_海看以前_三块彩绘石碑')
NOTES={
 'sea':('弧顶长壁碑','浅蓝海水与淡米灰沙滩，只保留海和岸边。已去掉船、水碗、道路、动物和远处石头。','建议在渡口入口、能同时看到海面的干岸旁；画面朝向玩家来路。'),
 'look':('收腰眼形立碑','一只眼睛，下面是正式的“看”字。','建议在渡口动物活动区外围，保留玩家、动物与被观察对象之间的视线。'),
 'past':('厚拱顶石碑 · 重制','碑体改为与图案呼应的拱形轮廓，刻画面内凹，外沿保留宽石边，基座加厚。完整拱门、圆形石台、浅蓝湖岸和正式“以前”字沿用原图，调整覆盖比例。与湖西岸遗址共同表达“这里以前的模样”。','用户已选②湖西岸圆台残柱。建议石碑放在进入石台的小路侧边，阅读时能同时望见遗址；倒塌拱顶只作为局部环境补充方案，主场景尚未修改。'),
}

def package(publish_ids=None):
    checks=(REVIEW/'checks.txt').read_text(encoding='utf-8')
    assert len(checks.splitlines())==2*len(SPECS) and all('PASS '+s['id'] in checks and 'PASS inscription '+s['id'] in checks and 'glyphReady=True' in checks for s in SPECS)
    maps=json.loads((HERE/'map-checks.json').read_text(encoding='utf-8'))
    for s,m in zip(SPECS,maps):
        src=PROJECT/DEST/s['id']
        assert m['id']==s['id']
        assert hashlib.sha256((src/'Illustration.png').read_bytes()).hexdigest()==m['sha256'], 'Source art changed'
        assert (src/'CanonicalGlyph.svg').read_bytes()==(CANONICAL/'SVG'/f"{s['id']}.svg").read_bytes()
        assert (src/'CanonicalGlyph.png').read_bytes()==(CANONICAL/'PNG'/f"{s['id']}.png").read_bytes()
        assert m['glyph']['stroke']['after']>=m['glyph']['stroke']['target'], 'Glyph thinner than artwork'
        for suffix in ['close','oblique','game-angle']:
            path=REVIEW/f"{s['id']}-{suffix}.png"
            assert path.exists() and path.stat().st_size>20000
            pixels=np.asarray(Image.open(path).convert('RGB'),np.float32)/255
            pigment=(pixels[...,0]-pixels[...,2]>.16)|(pixels[...,2]-pixels[...,0]>.12)
            # Detect a blank first render, not human readability; the small eye has only a tiny colored pupil.
            assert pigment.sum()>100, 'Missing pigment in captured frame: '+str(path)
        assert (src/'Stone.prefab').exists() and (src/'Stone.prefab.meta').exists()
    # Never copy review pipelines, shims, original models, or scene files into the shared project.
    target=ROOT/DEST;target.mkdir(parents=True,exist_ok=True)
    if publish_ids is None:
        shutil.copy2(PROJECT/(str(DEST)+'.meta'),ROOT/(str(DEST)+'.meta'))
    for item in (PROJECT/DEST).rglob('*'):
        if item.is_file():
            if publish_ids is not None and item.relative_to(PROJECT/DEST).parts[0] not in set(publish_ids)|{key+'.meta' for key in publish_ids}: continue
            dest=target/item.relative_to(PROJECT/DEST);dest.parent.mkdir(parents=True,exist_ok=True);shutil.copy2(item,dest)
    # Normalize only newly generated Unity YAML; original glyph SVG and PNG remain byte-identical.
    for path in [ROOT/(str(DEST)+'.meta')]+list(target.rglob('*')):
        if publish_ids is not None and (not path.is_relative_to(target) or path.relative_to(target).parts[0] not in set(publish_ids)|{key+'.meta' for key in publish_ids}): continue
        if path.is_file() and path.suffix in {'.meta','.mat','.prefab','.asset'}:
            source=path.read_text(encoding='utf-8-sig')
            path.write_text('\n'.join(line.rstrip() for line in source.splitlines())+'\n',encoding='utf-8',newline='\n')
    DESKTOP.mkdir(parents=True,exist_ok=True)
    for image in REVIEW.glob('*.png'): shutil.copy2(image,DESKTOP/image.name)
    cards=[]
    for s in SPECS:
        title,meaning,placement=NOTES[s['id']]
        cards.append(f'''<section id="{s['id']}"><h2>{s['title']} <small>{title}</small></h2><p>{meaning}</p>
<img src="{s['id']}-close.png" alt="{s['title']}近看"><div class="pair"><figure><img src="{s['id']}-oblique.png" alt="侧向厚度"><figcaption>碑体厚度与基座</figcaption></figure><figure><img src="{s['id']}-game-angle.png" alt="游戏镜头角度"><figcaption>俯角43° · 距离18m · FOV35°</figcaption></figure></div>
<p>{placement}</p></section>''')
    page='''<!doctype html><html lang="zh-CN"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>海、看、以前 · 彩绘石碑</title><style>
*{box-sizing:border-box}body{margin:0;background:#202b27;color:#eee7d3;font:17px/1.7 "Microsoft YaHei",sans-serif}main{max-width:1120px;margin:auto;padding:42px 24px}h1{font-size:30px}h2{font-size:25px}small{font-size:16px;color:#b9c5b1;margin-left:15px}p{max-width:920px}section{margin:45px 0;padding:24px;background:#2c3730;border:1px solid #61715e;border-radius:8px}img{width:100%;display:block}figure{margin:0}figcaption{font-size:14px;color:#c3cbb9;margin-top:6px}.pair{display:grid;grid-template-columns:1fr 1fr;gap:16px;margin-top:16px}.note{padding:18px;background:#3b4233;border-left:4px solid #c7bd82}@media(max-width:700px){.pair{grid-template-columns:1fr}main{padding:18px}section{padding:12px}small{display:block;margin:0}}</style><main><h1>海、看、以前 · 三块彩绘石碑</h1>
<p>三种独立外轮廓与基座比例，保持同一套低多边形灰石风格。白色刻槽与矿物彩绘沿用已确认效果，字形直接来自原作者正式四字扩展，未用图像生成重画。</p>
<div class="note">这是独立 Unity 场景的真实材质渲染。俯视图使用主场景镜头的默认参数，但不代表最终地形、光照和玩家站位。「以前」的碑体已重新制作成内凹的厚拱顶形，彩绘原图与字形保留。此前的偏肩碑体未采用。没有改动现有七块或主场景摆放。</div>'''+''.join(cards)+'''<section id="past-site"><h2>以前 · 遗址对照与摆放</h2><p>图画和实际遗址需要成对使用：柱脚间距、圆形石台、湖岸位置是识别同一地点的线索。画中拱门完整，眼前遗址只剩残柱与坍落石块。</p><img src="past-site-existing.png" alt="湖西岸原有地点"><p>原地点实景存档，拍摄于 2026-10-24。当前摆设可能有调整；最终落位需检查通行、地形和阅读视线。</p>'''+('''<details><summary>查看历史摆放概念（旧碑体，仅参考环境关系）</summary><img src="past-site-concept.png" alt="旧碑体的AI摆放概念"><p><strong>此图使用未采用的旧碑体，是 AI 概念，非游戏实拍。</strong>只保留石碑与遗址的视觉关系参考；当前模型以本页新版 Unity 单碑渲染为准。</p></details>''' if (DESKTOP/'past-site-concept.png').exists() else '')+'''</section><p>每块不超过 200 个三角面 · 3 个静态碰撞体 · 保留游戏视角刻线增强组件 · 照片视角保持原始刻槽。三块石碑的拍照碑文组件正面、宽高和正式字 ID 已配置；拍照入手账逻辑由集成任务验证。</p></main></html>'''
    shutil.copy2(ROOT/'AssetIntakeReview/scene-after/05_萤火圆台静潭.png',DESKTOP/'past-site-existing.png')
    shutil.copy2(HERE/'past-arch-prompt.json',DESKTOP/'以前拱门生成记录.json')
    (DESKTOP/'三块石碑预览.html').write_text(page,encoding='utf-8')
    report=dict(status='THREE_PREFABS_READY_PAST_ARCHED_BODY_AWAITING_VISUAL_REVIEW',prefabs=[str(DEST/s['id']/'Stone.prefab') for s in SPECS],
                scene_modified=False,existing_seven_modified=False,main_scene_play_tested=False,
                pending=[dict(id='past',requires_site_context=True,site='West lake circular terrace and ruined columns',reason='User selected this location. Intact arch mural must be paired with the matching ruined site; final scene placement and visual acceptance pending.')],
                images=3*len(SPECS),inscription_component='WeatherHouse.Life.LifeGlyphInscription',inscription_runtime_tested=False,
                review=str(DESKTOP/'三块石碑预览.html'),checks=checks.splitlines(),maps=maps)
    (HERE/'delivery.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8')
    (DESKTOP/'交付与验证.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8')
    print(str(DESKTOP/'三块石碑预览.html'))

if __name__=='__main__': package()
