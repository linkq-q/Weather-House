"""Prepare approval-only stone overlays in the existing isolated Unity review project."""
from pathlib import Path
import json
import shutil
import hashlib
import numpy as np
from PIL import Image

ROOT = Path(__file__).resolve().parents[3]
HERE = Path(__file__).resolve().parent
PROJECT = ROOT / 'StoneEngravingReview/20261029/UnityProject'
OUTPUT = ROOT / 'StoneEngravingReview/20261029/SevenStonePreview'
NAMES = ['风', '湿与干', '强与弱', '草与树', '先与后', '一起', '这里与那里']
GLYPHS = [[('wind', .5)], [('wet', .28), ('dry', .72)], [('weak', .25), ('strong', .75)],
          [('grass', .5)], [('first', 1/6), ('then', 5/6)], [('together', .5)], [('here', .43), ('there', .82)]]

def copy_asset(path):
    dst = PROJECT/path
    dst.parent.mkdir(parents=True, exist_ok=True)
    shutil.copy2(ROOT/path, dst)
    if Path(str(ROOT/path)+'.meta').exists():
        shutil.copy2(str(ROOT/path)+'.meta', str(dst)+'.meta')

def glyph(key):
    path = ROOT/'Assets/Game/Art/AnimalGlyphsV04'/f'{key}.png'
    a = Image.open(path).convert('RGBA').getchannel('A')
    return a.crop(a.getbbox()), hashlib.sha256(path.read_bytes()).hexdigest()

def prepare():
    source = HERE/'Source'; source.mkdir(exist_ok=True)
    out = PROJECT/'Assets/StoneApproval'; out.mkdir(exist_ok=True)
    OUTPUT.mkdir(parents=True, exist_ok=True)
    items = []
    for i, entry in enumerate(json.loads((HERE/'generation.json').read_text(encoding='utf-8'))['entries']):
        src = source/(entry['id']+'.png')
        if not src.exists(): shutil.copy2(entry['source'], src)
        # Convert AI line data to a linear coverage mask. No drawing or glyph redesign.
        gray = np.asarray(Image.open(src).convert('L'), dtype=np.float32)/255
        a = Image.fromarray(np.uint8(np.clip((.8-gray)/.6,0,1)*255))
        a = a.crop(a.getbbox())
        a = a.resize((1536, round(a.height*1536/a.width)), Image.Resampling.LANCZOS)
        top = 250 if entry['id']=='grass_tree' else 90
        canvas = Image.new('L', (1716, top+a.height+290), 0)
        canvas.paste(a,(90,top))
        glyph_info=[]
        positions=[(key,x,top+a.height+65) for key,x in GLYPHS[i]]
        if entry['id']=='grass_tree': positions.append(('tree',.39,50))
        for key,x,y in positions:
            g,sha = glyph(key)
            scale=min(165/g.height,205/g.width)
            g=g.resize((round(g.width*scale),round(g.height*scale)),Image.Resampling.LANCZOS)
            px=round(90+x*1536-g.width/2)
            canvas.paste(g,(px,y))
            glyph_info.append(dict(id=key,sha256=sha,rect=[px,y,g.width,g.height]))
        assert canvas.getbbox()[0]>=60 and canvas.getbbox()[2]<=canvas.width-60
        assert canvas.getbbox()[1]>=40 and canvas.getbbox()[3]<=canvas.height-40
        dst=out/(entry['id']+'.png'); canvas.save(dst)
        items.append(dict(id=entry['id'],title=NAMES[i],texture=dst.relative_to(PROJECT).as_posix(),
            aspect=canvas.width/canvas.height,width_m=5.5 if i==4 else 3.6,glyphs=glyph_info))
    candidates=[]
    for folder,patterns in [
        ('Assets/HIGHLANDS - Stylized Environment/Meshes/Forest',['Rock_*.fbx','Stone_*.fbx']),
        ('Assets/PureMeadow/Models/Rocks',['Stone*.fbx']),
        ('Assets/Environment/DDriveLibrary/Dreamscapes/Nature/Rocks/RockOrigin',['rock*.fbx'])]:
        for pattern in patterns:
            for f in sorted((ROOT/folder).glob(pattern)):
                rel=f.relative_to(ROOT).as_posix(); copy_asset(rel); candidates.append(rel)
    for name in ['StoneApprovalBuilder.cs','StoneApproval.shader']:
        dst=PROJECT/('Assets/Editor' if name.endswith('.cs') else 'Assets/StoneApproval')/name
        dst.parent.mkdir(parents=True,exist_ok=True); shutil.copy2(HERE/name,dst)
    spec=dict(output=OUTPUT.as_posix(),candidates=candidates,items=items)
    (PROJECT/'stone-approval.json').write_text(json.dumps(spec,ensure_ascii=False,indent=2),encoding='utf-8')
    (OUTPUT/'layout-source.json').write_text(json.dumps(spec,ensure_ascii=False,indent=2),encoding='utf-8')
    print(f'{len(items)} layouts; {len(candidates)} real mesh candidates; sources preserved; no main-scene edits.')

if __name__=='__main__': prepare()
