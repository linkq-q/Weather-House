from pathlib import Path
import json,hashlib,shutil,runpy
ROOT=Path(__file__).resolve().parents[3]
HERE=Path(__file__).resolve().parent
OUT=ROOT/'StoneEngravingReview/20261030/SevenPainted'
DEST=Path('C:/Users/31483/Desktop/天气屋/20261030_七块彩绘石碑')
ui=runpy.run_path(str(HERE.parent/'TabletStudy/package_review.py'),run_name='gallery_library')
items=json.loads((HERE/'layout.json').read_text(encoding='utf-8'))['items']
reason={'wind':'斜顶矮碑，保留旗杆、风线与底部动物字。','wet_dry':'横向方碑，左右布匹和两个动物字均有独立空间。','strong_weak':'加宽双图碑，保留左右人物、风线数量与两个动物字。','grass_tree':'较高斜顶碑，为顶部“树”和底部“草”的动物字预留空间。','first_then':'宽幅叙事碑，三幅画连续展示，底部动物字单独成行。','together':'中宽斜顶碑，大叶子与四只动物完整展示。','here_there':'横向方碑，保留指向虚线、两处水面与下方动物字。'}
DEST.mkdir(exist_ok=True,parents=True)
cards='';rows=''
for i in items:
    id=i['id'];folder=DEST/id;folder.mkdir(exist_ok=True)
    for view in ('engraved','side','blank','game'):
        shutil.copy2(OUT/id/f'{view}.png',folder/f'{view}.png')
    cards+=f'<article><img data-zoom data-base="{id}/" src="{id}/engraved.png" alt="{i["title"]}彩绘石碑"><div class="caption"><h2>{i["title"]}</h2><p>{reason[id]}</p><small>HIGHLANDS / {i["mesh"]} · {id}.prefab</small></div></article>'
    rows+=f'<tr><td>{i["title"]}</td><td>{i["mesh"]}</td><td>{i["size"]["x"]:.1f} × {i["size"]["z"]:.1f} m</td><td>{reason[id]}</td></tr>'
before=json.loads((HERE/'protected-before.json').read_text(encoding='utf-8'))
assert all(hashlib.sha256((ROOT/p).read_bytes()).hexdigest()==sha for p,sha in before.items()),'Original assets changed'
assets=ROOT/'Assets/Experiments/StoneEngraving'
reference=(assets/'RainWater_EngravedStone.mat').read_text(encoding='utf-8')
import re
def texture_guid(text,key):return re.search(r'- '+key+r':\s+m_Texture: ([^\n]+)',text)[1]
for i in items:
    mat=(assets/'SevenPainted'/f'{i["id"]}.mat').read_text(encoding='utf-8')
    old=(assets/'Seven'/i['id']/'Stone.mat').read_text(encoding='utf-8')
    for prop in ('_EngravingMask','_EngravingNormal'):
        assert texture_guid(mat,prop)==texture_guid(old,prop),prop
    for prop in ('_BaseMap','_BumpMap'):
        assert texture_guid(mat,prop)==texture_guid(reference,prop),prop
    assert (assets/'SevenPainted'/f'{i["id"]}.prefab').exists()
    # The cropped art must round-trip through the same affine registration used in the material.
    x0,y0,x1,y1=i['source_bbox'];w,h=i['source_size'];uv=i['uv']
    assert abs((90/1716*uv['x']+uv['z'])-x0/w)<1e-6
checks=(OUT/'checks.txt').read_text(encoding='utf-8')
assert len(checks.splitlines())==7 and checks.count('missing=0; steep=0')==7
(HERE/'verification.json').write_text(json.dumps({'original_files_unchanged':len(before),'prefabs':7,'images':28,'canonical_masks_and_normals_retained':True,'same_rain_water_stone_textures':True,'checks':checks},ensure_ascii=False,indent=2),encoding='utf-8')
body=f'''<header><span class="eyebrow">天气屋 / 七幅纹样 · 彩绘石碑</span><h1>同一组灰石、白色刻线与矿物彩绘</h1><p>以已确认的雨水石为基准，使用同资产包 HIGHLANDS 的低多边形石板，并复用雨水石的底色与表面纹理。按每幅图案的比例调整碑形，保留原动物字、刻槽深度和破损刻边。</p></header>
<div class="note">建议先看刻纹正面确认内容，再切到空白石形和侧面确认模型轮廓。俯视缩小图用于判断能否发现纹样；最终解读仍需近距离相机观察。每块均有独立 Prefab、碰撞体与可读性组件。</div>
<div class="switch"><button data-view="engraved" aria-pressed="true">彩绘正面</button><button data-view="blank" aria-pressed="false">空白石形</button><button data-view="side" aria-pressed="false">侧面与厚度</button><button data-view="game" aria-pressed="false">俯视缩小观察</button></div><p id="view-name">彩绘正面</p><div class="grid">{cards}</div>
<table><thead><tr><th>纹样</th><th>原模型</th><th>未倾斜前宽 × 高</th><th>选形依据</th></tr></thead><tbody>{rows}</tbody></table>
<p>Unity 资产目录：<code>Assets/Experiments/StoneEngraving/SevenPainted/</code></p><footer>本页为 Unity 独立编辑态场景渲染，未替换主场景现有石刻。图片均为原始渲染截图；近景按各自边界取景，不代表相同世界尺寸。点击图片可放大。</footer>'''
ui['write_page'](DEST,'彩绘石碑预览.html','七块彩绘石碑 · 选形与成品',body)
print('PASS seven prefabs / 28 captures / protected originals / original glyph maps / source registration')
