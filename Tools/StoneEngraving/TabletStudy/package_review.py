"""Package unmodified Unity captures into two independent local review pages."""
from pathlib import Path
import hashlib
import json
import re
import shutil

ROOT = Path(__file__).resolve().parents[3]
REVIEW = ROOT / 'StoneEngravingReview/20261030'
DESKTOP = Path('C:/Users/31483/Desktop/天气屋')
PAINT = DESKTOP / '20261030_雨水石刻上色实验'
TABLETS = DESKTOP / '20261030_石碑选石实验'
STYLE = '''
:root{font-family:"Microsoft YaHei",sans-serif;color:#353c32;background:#efeee6}*{box-sizing:border-box}body{margin:0}main{max-width:1450px;margin:auto;padding:38px}header{border-bottom:1px solid #c8cabb;padding-bottom:22px}h1{font-size:34px;margin:12px 0}p{line-height:1.8;color:#5c6456}small,.eyebrow{color:#78816e}nav,.switch{display:flex;gap:10px;flex-wrap:wrap;margin:20px 0}a{color:#536a48}button{font:inherit;padding:10px 18px;border:1px solid #b7beaa;border-radius:5px;background:transparent;color:#495640;cursor:pointer}button[aria-pressed=true]{background:#526449;color:white}.grid{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:22px}.three{grid-template-columns:repeat(3,minmax(0,1fr))}article{background:#faf9f2;border:1px solid #d8dbcc;border-radius:8px;overflow:hidden}article img{width:100%;display:block;cursor:zoom-in}article .caption{padding:18px 22px}h2{font-size:22px;margin:0 0 9px}article p{margin:0}.hero{width:100%;display:block;cursor:zoom-in;border-radius:8px}.note{padding:18px 22px;background:#e0e5d6;border-radius:5px;margin:22px 0}table{width:100%;border-collapse:collapse;margin:24px 0;background:#f8f8f1}th,td{text-align:left;padding:14px;border-bottom:1px solid #d2d6c9;font-size:14px;line-height:1.7}code{overflow-wrap:anywhere}footer{font-size:13px;margin-top:32px;color:#6a7560;line-height:1.8}dialog{padding:12px;background:#242a22;border:none;max-width:97vw;max-height:96vh}dialog::backdrop{background:#000b}dialog img{display:block;max-width:92vw;max-height:86vh;object-fit:contain}dialog button{color:white;float:right;margin-bottom:8px}.label{display:inline-block;background:#e4e9dc;padding:4px 8px;font-size:12px;border-radius:3px;margin-left:8px}@media(max-width:800px){main{padding:20px}.grid,.three{grid-template-columns:1fr}h1{font-size:27px}table{display:block;overflow-x:auto}}
'''
SCRIPT = '''
const modal=document.querySelector('dialog');
document.querySelectorAll('img[data-zoom]').forEach(img=>img.addEventListener('click',()=>{modal.querySelector('img').src=img.src;modal.showModal()}));
modal.querySelector('button').addEventListener('click',()=>modal.close());
modal.addEventListener('click',e=>{if(e.target===modal)modal.close()});
document.querySelectorAll('[data-view]').forEach(button=>button.addEventListener('click',()=>{
 document.querySelectorAll('[data-view]').forEach(b=>b.setAttribute('aria-pressed',b===button));
 document.querySelectorAll('img[data-base]').forEach(img=>img.src=img.dataset.base+button.dataset.view+'.png');
 document.querySelector('#view-name').textContent=button.textContent;
}));
'''
def write_page(folder, name, title, body):
    folder.mkdir(parents=True, exist_ok=True)
    (folder/name).write_text(f'<!doctype html><html lang="zh-CN"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>{title}</title><style>{STYLE}</style><main>{body}<footer>2026-10-30 · Unity 编辑态独立预览场景渲染。主场景尚未替换；俯视图用于比较缩小后的辨识度，并非当前玩家相机截图。点击图片放大。</footer></main><dialog><button>关闭</button><img alt="放大预览"></dialog><script>{SCRIPT}</script></html>', encoding='utf-8')

def package():
    PAINT.mkdir(parents=True, exist_ok=True)
    TABLETS.mkdir(parents=True, exist_ok=True)
    for key in ('A-original', 'B-white-grooves', 'C-filled'):
        for view in ('close', 'game'):
            shutil.copy2(REVIEW/'Paint'/f'{key}-{view}.png', PAINT/f'{key}-{view}.png')
    cards=''
    for key,title,desc in [('A-original','A · 原始刻槽','保留原材质，作为对照。'),('B-white-grooves','B · 全部刻槽涂白','动物字与画面全部刻线统一强调。'),('C-filled','C · 白色刻槽＋内部上色','使用你选中的第一张彩绘图；保留原石面起伏和凹槽。')]:
        cards+=f'<article><img data-zoom data-base="{key}-" src="{key}-close.png" alt="{title}"><div class="caption"><h2>{title}</h2><p>{desc}</p></div></article>'
    write_page(PAINT,'上色对照.html','雨水石刻 · 上色实验',f'''<header><span class="eyebrow">天气屋 / 实验 01</span><h1>白色刻槽，内部矿物彩绘</h1><p>使用最初的 HIGHLANDS Rock_1。原有雨、水动物字、刻槽深度、破损边缘和石头形状保留。</p><nav><a href="{(TABLETS/'石碑候选.html').as_uri()}">查看独立的石碑选型 →</a></nav></header>
    <p>你选中的版本 · 实际材质渲染</p><img class="hero" data-zoom src="C-filled-close.png" alt="雨水石刻彩绘近景">
    <div class="note">色块来自你确认的第一张生成图。图像透明区域不会形成发光或雾状背景；白色刻槽仍接受场景光照，没有加入自发光。</div>
    <div class="switch"><button data-view="close" aria-pressed="true">近景</button><button data-view="game" aria-pressed="false">俯视缩小观察</button></div><p id="view-name">近景</p><div class="grid three">{cards}</div>
    <p>Unity 独立实验资产：<code>Assets/Experiments/StoneEngraving/TabletStudy/C-filled.prefab</code></p>''')
    specs=[('A-slate','A · 斜顶石板','推荐','Stone_1','3.40 × 3.05 × 1.79 m','2.104 × 1.649 m','风','完整石面与斜顶轮廓较像人为立起的碑，适合单幅信息。'),('B-square','B · 方形石板','','Stone_2','3.50 × 2.85 × 1.68 m','2.166 × 1.697 m','风','边缘更方正，图案有完整留白；轮廓较规整。'),('C-natural','C · 原岩竖立','对照','Rock_1','3.30 × 3.89 × 2.13 m','1.782 × 1.397 m','风','与最初雨水石同一模型，但天然缺口明显，可用刻画平面偏小。'),('D-wide','D · 横向叙事碑','推荐用于长图','Stone_2','5.50 × 3.06 × 1.82 m','3.589 × 1.803 m','先与后','容纳连续三幅叙事和底部动物字；需要更宽的摆放空间。')]
    cards='';rows=''
    for key,title,badge,source,size,face,art,desc in specs:
        destination=TABLETS/key;destination.mkdir(exist_ok=True)
        for view in ('engraved','blank','side','game'):
            shutil.copy2(REVIEW/'Tablets'/key/f'{view}.png', destination/f'{view}.png')
        cards+=f'<article><img data-zoom data-base="{key}/" src="{key}/engraved.png" alt="{title}"><div class="caption"><h2>{title}<span class="label">{badge or "候选"}</span></h2><p>{desc}</p><small>{source} · 刻画「{art}」</small></div></article>'
        rows+=f'<tr><td>{title}</td><td>HIGHLANDS / {source}</td><td>{size}</td><td>{face}</td><td>{key}.prefab</td></tr>'
    write_page(TABLETS,'石碑候选.html','石碑选型 · 四个候选',f'''<header><span class="eyebrow">天气屋 / 实验 02</span><h1>让石头先被认成一块碑</h1><p>这一组只比较石形与图案容纳范围。使用现有 HIGHLANDS 模型，通过缩放厚度和立起角度制作，不改动原模型。</p><nav><a href="{(PAINT/'上色对照.html').as_uri()}">← 查看雨水石上色实验</a></nav></header>
    <div class="note">建议单幅图先选 A，横向叙事图选 D。四个候选都有独立 Prefab、碰撞体和已有的俯视可读性组件，可拖入场景摆放。选型尚待你确认。</div>
    <div class="switch"><button data-view="engraved" aria-pressed="true">刻纹正面</button><button data-view="blank" aria-pressed="false">空白石形</button><button data-view="side" aria-pressed="false">侧面与厚度</button><button data-view="game" aria-pressed="false">俯视缩小观察</button></div><p id="view-name">刻纹正面</p><div class="grid">{cards}</div>
    <table><thead><tr><th>候选</th><th>原资产</th><th>当前宽 × 高 × 深</th><th>完整刻画区</th><th>Prefab</th></tr></thead><tbody>{rows}</tbody></table>
    <p>近景按各自边界取景，不代表相同世界尺寸。当前模型统一略后仰；最终大小、朝向及位置可再摆放调整。所有图案与动物字均通过落面检查，没有超出可用平面。</p>
    <details><summary>昨天的选石来源核查</summary><p>最初雨水石是 HIGHLANDS / Rock_1。昨天「风」「湿与干」来自 HIGHLANDS，另外五块来自 Dreamscapes；确实混用了资产包。这次四个候选统一来自 HIGHLANDS。</p></details>
    <p>Unity 目录：<code>Assets/Experiments/StoneEngraving/TabletStudy/</code></p>''')
    before=json.loads((REVIEW/'protected-before.json').read_text(encoding='utf-8-sig'))
    unchanged={p:hashlib.sha256((ROOT/p).read_bytes()).hexdigest()==sha for p,sha in before.items()}
    if not all(unchanged.values()):
        raise AssertionError(f'Protected inputs changed: {unchanged}')
    assets=ROOT/'Assets/Experiments/StoneEngraving'
    original_prefab=(assets/'RainWater_ExistingRock.prefab').read_text()
    original_mat=(assets/'RainWater_EngravedStone.mat').read_text()
    property_checks={}
    for variant in ('B-white-grooves','C-filled'):
        prefab=(assets/'TabletStudy'/f'{variant}.prefab').read_text()
        mat=(assets/'TabletStudy'/f'{variant}.mat').read_text()
        for prop in ('m_Mesh','m_LocalScale'):
            pattern=rf'{prop}: ([^\n]+)'
            assert re.search(pattern,prefab)[1]==re.search(pattern,original_prefab)[1], prop
        for prop in ('_EngravingMask','_EngravingNormal','_BumpMap','_BaseMap'):
            pattern=rf'{prop}:\s+m_Texture: ([^\n]+)'
            assert re.search(pattern,mat)[1]==re.search(pattern,original_mat)[1], prop
        for prop in ('_GrooveDepth','_GrooveShadow','_BumpScale','_ProjectionCenter','_ProjectionRight','_ProjectionUp','_ProjectionSize'):
            pattern=rf'- {prop}: ([^\n]+)'
            assert re.search(pattern,mat)[1]==re.search(pattern,original_mat)[1], prop
        assert re.search(r'- _PaintStrength: 1',mat)
        assert re.search(r'- _FillStrength: '+('1' if variant=='C-filled' else '0'),mat)
        property_checks[variant]='PASS mesh, scale, all original maps, depth, shadow, projection retained; selected pigment mode'
    selected=Path('C:/Users/31483/.codex/generated_images/01a1855d-40ec-7da2-920d-bfb41bc2a999/exec-cc175a34-abcb-4ad8-9766-eea1b42a6b1d.png')
    assert selected.read_bytes()==(assets/'TabletStudy/RainWaterPaint.png').read_bytes()
    report={'protected_assets_unchanged':unchanged,'paint_property_checks':property_checks,'paint_checks':(REVIEW/'Paint/checks.txt').read_text(),'tablet_checks':(REVIEW/'Tablets/checks.txt').read_text(),'paint_png_count':len(list(PAINT.glob('*.png'))),'tablet_png_count':len(list(TABLETS.glob('*/*.png'))),'accepted_paint_sha256':hashlib.sha256((assets/'TabletStudy/RainWaterPaint.png').read_bytes()).hexdigest()}
    (REVIEW/'verification.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8')
    print(json.dumps({'pages':[str(PAINT/'上色对照.html'),str(TABLETS/'石碑候选.html')],'protected':len(unchanged),'paint_images':report['paint_png_count'],'tablet_images':report['tablet_png_count']},ensure_ascii=False))

if __name__=='__main__':package()
