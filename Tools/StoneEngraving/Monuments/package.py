"""Package raw Unity renders; protect the approved artwork and original scene."""
from pathlib import Path
import json, hashlib, re, shutil, runpy

ROOT = Path(__file__).resolve().parents[3]
HERE = Path(__file__).resolve().parent
REVIEW = ROOT / 'StoneEngravingReview/20261030/Monuments'
ASSETS = ROOT / 'Assets/Experiments/StoneEngraving/MonumentPainted'
DEST = Path('C:/Users/31483/Desktop/天气屋/20261030_宏伟石碑重制')
ITEMS = [
    ('first_then', '先与后 · 叙事壁碑', '宽阔的连续碑面，三段叙事完整铺开；低基座托住整块墙体。'),
    ('here_there', '这里与那里 · 指向壁碑', '横向墙式碑体，为箭头、两处水面和下方动物字提供完整阅读面。'),
    ('together', '一起 · 合冠石碑', '收肩冠顶与厚实碑身，形成站立的纪念碑轮廓；叶冠和动物占据中央主面。'),
    ('grass_tree', '草与树 · 高立石碑', '更高的整块石碑，上下动物字与中央图画形成清晰的纵向阅读顺序。'),
]
VIEWS = [('scale','人物比例'), ('engraved','彩绘正面'), ('side','侧面与厚度'), ('blank','空白石形'), ('game','俯视观察'), ('before','上一版对照')]

def texture(text, key):
    return re.search(r'- '+key+r':\s+m_Texture: ([^\n]+)', text)[1]

def package():
    before = json.loads((HERE/'protected-before.json').read_text(encoding='utf-8-sig'))
    changed = [p for p, sha in before.items() if hashlib.sha256((ROOT/p).read_bytes()).hexdigest().upper() != sha]
    assert not changed, f'Protected files changed: {changed}'
    checks = (REVIEW/'checks.txt').read_text(encoding='utf-8')
    assert len(checks.splitlines()) == 4 and checks.count('missing=0; steep=0') == 4
    player_check = (REVIEW/'player-scale.txt').read_text(encoding='utf-8')
    assert player_check.startswith('PASS sourceHeight=')
    assert (ROOT/'StoneEngravingReview/20261030/status.txt').read_text(encoding='utf-8').startswith('PASS monuments ')
    cards = ''
    DEST.mkdir(parents=True, exist_ok=True)
    for id, title, description in ITEMS:
        target = DEST/id
        target.mkdir(exist_ok=True)
        for view, _ in VIEWS:
            source = ROOT/f'StoneEngravingReview/20261030/SevenPainted/{id}/engraved.png' if view == 'before' else REVIEW/id/f'{view}.png'
            assert source.stat().st_size > 10000
            shutil.copy2(source, target/f'{view}.png')
        mat = (ASSETS/f'{id}.mat').read_text(encoding='utf-8')
        original = (ROOT/f'Assets/Experiments/StoneEngraving/SevenPainted/{id}.mat').read_text(encoding='utf-8')
        for prop in ('_EngravingMask','_EngravingNormal','_PaintFill','_PaintGrain','_BumpMap','_BaseMap'):
            assert texture(mat, prop) == texture(original, prop), (id, prop)
        assert re.search(r'- _FillUV: ([^\n]+)', mat)[1] == re.search(r'- _FillUV: ([^\n]+)', original)[1]
        assert (ASSETS/f'{id}.prefab').exists()
        for part in ('body','foot','socket'):
            assert (ASSETS/f'{id}_{part}.asset').exists()
        info = next(line for line in checks.splitlines() if line.startswith(id+':'))
        size = re.search(r'size=\(([^)]+)\)', info)[1].replace(', ', ' × ')
        cards += f'<article><img data-zoom data-base="{id}/" src="{id}/scale.png" alt="{title}"><div class="caption"><h2>{title}</h2><p>{description}</p><small>宽 × 高 × 深：{size} 米 · 可独立摆放 Prefab</small></div></article>'
    buttons = ''.join(f'<button data-view="{v}" aria-pressed="{str(n==0).lower()}">{title}</button>' for n,(v,title) in enumerate(VIEWS))
    ui = runpy.run_path(str(HERE.parent/'TabletStudy/package_review.py'), run_name='gallery_library')
    body = f'''<header><span class="eyebrow">天气屋 / 四块模型重制</span><h1>有建造感的碑体与完整壁画</h1><p>本轮重做「先与后、这里与那里、一起、草与树」的模型。以平整主面、厚实侧壁、削角冠顶与低矮基座建立体量；沿用已确认的灰石、暖白刻线与矿物彩绘。</p></header>
<div class="note">默认用当前主角模型作比例参照，保持其场景中的实际尺寸。碑体采用新制作的低多边形网格，石材贴图仍来自雨水石。四幅原画、动物字、彩绘与刻槽贴图均未修改。</div>
<div class="switch">{buttons}</div><p id="view-name">人物比例</p><div class="grid">{cards}</div>
<p>正面、侧面和旧版均按各自边界取景，因此应看「人物比例」判断体量。新碑体没有额外纹饰；放大的主面用于解读已有线索。</p>
<p>Unity 资产目录：<code>Assets/Experiments/StoneEngraving/MonumentPainted/</code></p>
<details><summary>验证记录</summary><p>四块均通过闭合网格检查、刻纹落面检查与资源引用检查。独立编辑态预览已渲染；尚未放入游戏主场景，未执行 Play。</p><pre style="white-space:pre-wrap">{checks}</pre></details>'''
    ui['write_page'](DEST,'宏伟石碑预览.html','四块宏伟石碑 · 模型重制',body)
    (HERE/'verification.json').write_text(json.dumps({'protected_files':len(before), 'unchanged_textures':True, 'prefabs':4,'new_meshes':12,'raw_captures':20,'checks':checks,'player_scale':player_check},ensure_ascii=False,indent=2),encoding='utf-8')
    print(f'PASS {len(before)} protected files; 4 prefabs; 12 closed meshes; 20 raw captures; unchanged artwork')
    print(DEST/'宏伟石碑预览.html')

if __name__ == '__main__':
    package()
