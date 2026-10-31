from pathlib import Path
import json,subprocess,html,wave,hashlib,re
import numpy as np
out=Path(r'C:/Users/31483/Desktop/天气屋/草地脚步试听_20261030'); rows=json.loads((out/'来源与许可.json').read_text(encoding='utf-8'))
ff=r'C:/Users/31483/AppData/Roaming/bilibili/ffmpeg/ffmpeg.exe'
choices=[('owl1','轻草单步 A'),('owl2','轻草单步 B'),('owl3','轻草单步 C'),('owl4','轻草单步 D'),('ali','草叶单步'),('silent','草地连续行走')]
rows.append(dict(id='current',author='TinyWorlds / pdsounds',page='https://opengameart.org/content/different-steps-on-wood-stone-leaves-gravel-and-mud',license='CC0-1.0',file=str(Path.cwd()/'Assets/Game/Life/Audio/Resources/LifeAudio/Clips/step_grass_a.wav')))
choices.append(('current','当前游戏版本（对照）')); manifest=[];cards=[]
def save(path,x):
 x=x-x.mean(); gain=min(10**(-23/20)/max(1e-9,np.sqrt(np.mean(x*x))),10**(-3/20)/max(1e-9,np.max(np.abs(x)))); x=x*gain
 n=min(240,len(x)//10);x[:n]*=np.linspace(0,1,n); x[-n:]*=np.linspace(1,0,n)
 pcm=np.round(x*32767).astype('<i2'); assert np.max(np.abs(pcm.astype(int)))<32767
 with wave.open(str(path),'wb') as f:f.setnchannels(1);f.setsampwidth(2);f.setframerate(48000);f.writeframes(pcm.tobytes())
 return pcm.astype(float)/32767
(out/'试听片段').mkdir(exist_ok=True)
for i,(ident,title) in enumerate(choices,1):
 row=next(x for x in rows if x['id']==ident); source=out/row['file'];raw=subprocess.check_output([ff,'-v','error','-i',str(source),'-ac','1','-ar','48000','-f','f32le','pipe:1']); x=np.frombuffer(raw,dtype='<f4').astype(float);assert np.isfinite(x).all()
 onset=np.where(np.abs(x)>max(abs(x))*.12)[0][0];start=max(0,onset-240)
 one=x[start:min(len(x),start+24000)].copy(); single=f'试听片段/{i:02d}_单步.wav'; one=save(out/single,one)
 demo=np.zeros(48000*4)
 for k in range(6):
  begin=round((.25+k*.6)*48000);demo[begin:begin+len(one)]+=one
 sequence=f'试听片段/{i:02d}_六步对比.wav';save(out/sequence,demo)
 row=dict(row,number=f'{i:02d}',title=title,single=single,sequence=sequence,source_cut_seconds=[start/48000,(start+len(one))/48000],processing='单声道48kHz；单步最多0.5秒；统一试听响度；六步以0.6秒等间隔排列，仅用于比较材质；无变调',single_sha256=hashlib.sha256((out/single).read_bytes()).hexdigest());manifest.append(row)
 e=html.escape
 cards.append(f'<article><small>{i:02d} / {e(row["author"])}</small><h2>{e(title)}</h2><p>单步</p><audio controls preload="metadata" src="{single}"></audio><p>统一节奏 · 六步对比</p><audio controls preload="metadata" src="{sequence}"></audio><p><a href="{e(row["page"])}">来源与 CC0 许可</a> · <a href="{e(row["file"])}">完整原音</a></p><label><input type="checkbox" data-id="{i:02d}">保留</label></article>')
css=re.search(r'<style>(.*?)</style>',Path(r'C:/Users/31483/Desktop/天气屋/魔法音效试听_20261030/打开魔法试听.html').read_text(encoding='utf-8'),re.S)[1]
page='<!doctype html><html lang="zh-CN"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>天气屋 · 草地脚步试听</title><style>'+css+'</style><header><h1>草地上的脚步</h1><p>6 条新候选，来自 3 位作者；01–04 是同一作者的四个轻草单步，05 是另一种草叶触感，06 保留连续行走原音，07 是当前版本对照。每条都有单步和相同节奏的六步对比。完整原音可单独打开试听。</p><p>对比节奏固定为每步 0.6 秒，不代表最终人物步频。候选尚未替换进游戏。请选择喜欢的编号。</p><button id="export">导出选择</button></header><main><div id="grid">'+''.join(cards)+'</div></main><script>document.querySelectorAll("audio").forEach(a=>a.onplay=()=>document.querySelectorAll("audio").forEach(b=>{if(a!==b)b.pause()}));document.querySelector("#export").onclick=()=>{const picked=[...document.querySelectorAll("input:checked")].map(x=>x.dataset.id);const url=URL.createObjectURL(new Blob([JSON.stringify({picked},null,2)],{type:"application/json"}));const a=document.createElement("a");a.href=url;a.download="天气屋_草地脚步选择.json";a.click();setTimeout(()=>URL.revokeObjectURL(url),1000)};</script></html>'
(out/'打开草地试听.html').write_text(page,encoding='utf-8');(out/'试听清单.json').write_text(json.dumps(manifest,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
review=Path('InteractionReview/20261030/Footsteps');review.mkdir(exist_ok=True)
(review/'草地试听清单.json').write_text(json.dumps(manifest,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
assert len(manifest)==7 and page.count('<audio ')==14
print('PASS 7 candidates including current comparison, 14 PCM previews, source and license metadata preserved; no raw web HTML saved')
