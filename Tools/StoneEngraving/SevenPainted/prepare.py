from pathlib import Path
import json,hashlib
from PIL import Image
import numpy as np
root=Path(__file__).resolve().parents[3]
here=Path(__file__).resolve().parent
layout=json.loads((root/'Tools/StoneEngraving/BatchProduction/approved-layout.json').read_text(encoding='utf-8'))
specs={'wind':('Stone_1',3.4,3.0),'wet_dry':('Stone_2',3.8,3.1),'strong_weak':('Stone_2',4.5,3.1),'grass_tree':('Stone_1',3.7,3.7),'first_then':('Stone_2',5.8,3.2),'together':('Stone_1',4.2,3.5),'here_there':('Stone_2',4.4,3.0)}
items=[]
for item in layout['items']:
    id=item['id'];im=Image.open(root/f'Tools/StoneEngraving/BatchPreview/Source/{id}.png').convert('L')
    a=np.clip((.80-np.asarray(im,dtype=float)/255)/.60,0,1)
    x0,y0,x1,y1=Image.fromarray(np.uint8(a*255)).getbbox();w,h=im.size
    top=250 if id=='grass_tree' else 90;art_h=round((y1-y0)*1536/(x1-x0));mh=top+art_h+290
    uv=[1716/1536*(x1-x0)/w,mh/art_h*(y1-y0)/h,(x0-90/1536*(x1-x0))/w,(h-y1-290/art_h*(y1-y0))/h]
    mesh,width,height=specs[id]
    items.append(dict(id=id,title=item['title'],mesh=mesh,size={'x':width,'y':.72,'z':height},uv=dict(zip('xyzw',uv)),source_bbox=[x0,y0,x1,y1],source_size=[w,h]))
(here/'layout.json').write_text(json.dumps({'items':items},ensure_ascii=False,indent=2),encoding='utf-8')
paths=[Path('Assets/Scenes/WeatherHouse_WatersideV2.unity')]+list(Path('Assets/Experiments/StoneEngraving/Seven').glob('*/*'))
if not (here/'protected-before.json').exists():
    (here/'protected-before.json').write_text(json.dumps({p.as_posix():hashlib.sha256((root/p).read_bytes()).hexdigest() for p in paths if (root/p).is_file()},indent=2),encoding='utf-8')
asset=root/'Assets/Experiments/StoneEngraving/SevenPainted';asset.mkdir(exist_ok=True)
s=(root/'Assets/Experiments/StoneEngraving/TabletStudy/PaintedEngraving.shader').read_text(encoding='utf-8')
s=s.replace('Weather House/Experiments/Painted Stone Engraving','Weather House/Stone Engraving/Seven Mineral Paint')
s=s.replace('_FillStrength("Interior pigment strength", Range(0,1)) = 0','_FillStrength("Interior pigment strength", Range(0,1)) = 0\n        _FillUV("Pigment UV scale and offset", Vector) = (1,1,0,0)')
s=s.replace('half4 _PaintColor; half _PaintStrength, _FillStrength;','half4 _PaintColor; half _PaintStrength, _FillStrength; float4 _FillUV;')
s=s.replace('// The approved 1536x1024 artwork starts 376 pixels down the existing 1536-square glyph layout.','// Preserve the source-art registration within the canonical glyph layout.')
s=s.replace('float2 fillUV=float2(uv.x,uv.y*1.5-.1328125);','float2 fillUV=uv*_FillUV.xy+_FillUV.zw;')
s=s.replace('valid*step(0,fillUV.y)*step(fillUV.y,1)','valid*step(0,fillUV.x)*step(fillUV.x,1)*step(0,fillUV.y)*step(fillUV.y,1)')
(asset/'SevenPainted.shader').write_text(s,encoding='utf-8')
print('Prepared seven layouts; no images edited.')
