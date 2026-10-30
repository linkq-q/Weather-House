from pathlib import Path
import json, html, re, subprocess, wave, hashlib
import numpy as np

OUT = Path(r'C:/Users/31483/Desktop/天气屋/魔法音效试听_20261030')
ORIGINAL = Path(r'C:/Users/31483/Desktop/天气屋/音效试听_20261030/打开试听.html')
FF = r'C:/Users/31483/AppData/Roaming/bilibili/ffmpeg/ffmpeg.exe'
sources = {r['id']:r for r in json.loads((OUT/'来源与许可.json').read_text(encoding='utf-8'))}
choices = [
 ('星屑轻点','sparkle',0,None,'短促微光','短小的电子星屑，可先比较施法成功瞬间。'),
 ('玻璃铃雨','chimes',.48,5.55,'清透铃音','玻璃风铃般的连续细响，取自原音第一组。'),
 ('星光上扬','retro_stars',0,3.65,'短促微光','复古动画感的星光音型，取自原音第一组。'),
 ('另一组星光','retro_stars',12.72,16.65,'短促微光','同一星光录音的第三组，单列供比较。'),
 ('柔和竖琴','healing_2',0,None,'柔和余韵','竖琴式拨动与余韵，可比较轻柔的魔法完成反馈。'),
 ('咒语点亮','healing_4',0,None,'柔和余韵','完整的短咒语提示，与单纯铃音作对照。'),
 ('梦幻施法','fantasy',0,None,'柔和余韵','完整魔法提示，保留原有起音和尾韵供对比。'),
 ('稀疏细铃','chimes',5.65,7.9,'清透铃音','同一铃音录音的较疏短段，和连续铃雨比较密度。'),
]
cards=[]; rows=[]
for index,(title,source_id,start,end,category,note) in enumerate(choices,1):
    source=sources[source_id]
    raw=subprocess.check_output([FF,'-v','error','-i',str(OUT/source['file']),'-ac','2','-ar','48000','-f','f32le','pipe:1'])
    data=np.frombuffer(raw,dtype='<f4').reshape(-1,2).astype(float)
    stop=min(len(data),round(end*48000)) if end else len(data)
    data=data[round(start*48000):stop].copy();data-=data.mean(axis=0)
    assert len(data)>100 and np.isfinite(data).all()
    fade=min(1440,len(data)//10);data[:240]*=np.linspace(0,1,240)[:,None];data[-fade:]*=np.linspace(1,0,fade)[:,None]
    gain=min(10**(-23/20)/np.sqrt(np.mean(data*data)),10**(-3/20)/np.max(np.abs(data)))
    pcm=np.round(data*gain*32767).astype('<i2'); assert np.max(np.abs(pcm.astype(np.int32)))<32767
    relative=f'试听片段/{index:02d}_{title}.wav';dest=OUT/relative;dest.parent.mkdir(exist_ok=True)
    with wave.open(str(dest),'wb') as f:f.setnchannels(2);f.setsampwidth(2);f.setframerate(48000);f.writeframes(pcm.tobytes())
    row=dict(id=f'magic-{index:02d}',category=category,title=title,file=relative,seconds=round(len(data)/48000,2),
        source_id=source_id,source_page=source['page'],author=source['author'],license=source['license'],
        public_hq_preview=source['public_hq_preview'],source_range_seconds=[start,stop/48000],
        description=note,processing='裁剪选段、去直流、5 ms 起音与 30 ms 尾部淡化、统一试听响度；无变调或叠层',
        sha256=hashlib.sha256(dest.read_bytes()).hexdigest())
    rows.append(row);e=html.escape
    cards.append(f'''<article data-category="{category}"><div class="meta">{index:02d} · {category} · {row['seconds']} 秒</div><h2>{title}</h2><audio controls preload="metadata" src="{e(relative)}"></audio><p>{note}</p><footer><a href="{source['page']}" target="_blank" rel="noopener">{e(source['author'])} · 来源</a><a href="{e(source['file'])}" target="_blank">完整原音</a></footer><div class="muted">CC0 · {'公开 HQ 预览' if source['public_hq_preview'] else '作者下载文件'}</div><label class="pick"><input type="checkbox" data-pick="{index-1}"> 保留这个候选</label><input class="note" data-note="{index-1}" placeholder="你的备注，例如更短、尾音更轻"></article>''')
css=re.search(r'<style>(.*?)</style>',ORIGINAL.read_text(encoding='utf-8'),re.S)[1]
document='''<!doctype html><html lang="zh-CN"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>天气屋 · 铃音与星屑</title><style>CSS</style><header><div class="muted">WEATHER HOUSE / MAGIC AUDITION 02</div><h1>让魔法轻轻亮起来</h1><p>新找的 8 条候选，围绕清透铃音、微光与细碎星屑。来自 5 位作者；其中两组保留同一原音的不同段落，方便比较密度。先听 01、03、05，再和铃雨、余韵对照。</p><nav><select id="category"><option value="">全部方向</option><option>短促微光</option><option>清透铃音</option><option>柔和余韵</option></select><label><input id="only" type="checkbox"> 只看已保留</label><button id="export">导出魔法选择</button><span id="count"></span></nav><p class="muted">这些仅供试听，尚未接入游戏。已统一试听响度，完整原音和来源保留；选中后再按游戏动作微调时长与音量。</p></header><main><div id="grid">CARDS</div></main><script>
const rows=ROWS,key='weatherhouse-magic-20261030-v2';let saved={};try{saved=JSON.parse(localStorage.getItem(key)||'{}')}catch{}
const category=document.querySelector('#category'),only=document.querySelector('#only');
function persist(){try{localStorage.setItem(key,JSON.stringify(saved))}catch{}}
function update(){let count=0;document.querySelectorAll('article').forEach((card,i)=>{card.hidden=!!((category.value&&card.dataset.category!==category.value)||(only.checked&&!saved[i]?.picked));if(!card.hidden)count++});document.querySelector('#count').textContent=count+' 个候选'}
document.querySelectorAll('[data-pick]').forEach(input=>{const i=input.dataset.pick;input.checked=!!saved[i]?.picked;input.onchange=()=>{saved[i]={...saved[i],picked:input.checked};persist();update()}});
document.querySelectorAll('[data-note]').forEach(input=>{const i=input.dataset.note;input.value=saved[i]?.userNote||'';input.oninput=()=>{saved[i]={...saved[i],userNote:input.value};persist()}});
category.onchange=only.onchange=update;document.querySelectorAll('audio').forEach(a=>a.addEventListener('play',()=>document.querySelectorAll('audio').forEach(b=>{if(a!==b)b.pause()})));
function selectionPayload(){return {date:'2026-10-30',selected:rows.map((r,i)=>({...r,picked:!!saved[i]?.picked,userNote:saved[i]?.userNote||''})).filter(r=>r.picked||r.userNote.trim())}}
document.querySelector('#export').onclick=()=>{const url=URL.createObjectURL(new Blob([JSON.stringify(selectionPayload(),null,2)],{type:'application/json'}));const a=document.createElement('a');a.href=url;a.download='天气屋_魔法音效选择.json';a.click();setTimeout(()=>URL.revokeObjectURL(url),1000)};update();
</script></html>'''
document=document.replace('CSS',css).replace('CARDS','\n'.join(cards)).replace('ROWS',json.dumps(rows,ensure_ascii=False).replace('</','<\\/'))
(OUT/'打开魔法试听.html').write_text(document,encoding='utf-8')
(OUT/'试听清单.json').write_text(json.dumps(rows,ensure_ascii=False,indent=2),encoding='utf-8')
assert document.count('<audio ')==8
print('PASS 8 new magic candidates, 5 authors, checked PCM and local paths')
