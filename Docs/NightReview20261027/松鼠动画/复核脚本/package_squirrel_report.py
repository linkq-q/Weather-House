from pathlib import Path
from PIL import Image,ImageDraw,ImageFont
import shutil,json,hashlib
OUT=Path(r'C:\Users\31483\Desktop\天气屋\夜间完善_20261027\松鼠动画')
STAGE=Path(r'C:\Users\31483\.codex\visualizations\2026\10\26\01a178cf-479b-7f41-af26-309b1776f6d5\squirrel-staging')
PREVIEW=OUT/'预览'
labels={'current_walk':'现有走路','current_run':'现有跑步','current_turn':'现有转身源片段','current_idle':'正式待机','grounded_walk':'原模型校正走路候选','grounded_run':'原模型校正跑步候选','gobkit_squirrel':'Gobkit 松鼠','gobkit_ground_squirrel':'Gobkit 地松鼠','oga_run':'OGA 四足 Run'}
font=ImageFont.truetype(r'C:\Windows\Fonts\msyh.ttc',19)
for name in labels:
 shutil.copyfile(STAGE/'frames'/name/'0000.png',PREVIEW/(name+'-poster.png'))
for group,names in [('原模型对照',['current_walk','grounded_walk','current_run','grounded_run','current_turn','current_idle']),('免费模型对照',['gobkit_squirrel','gobkit_ground_squirrel','oga_run'])]:
 sheet=Image.new('RGB',(1280,len(names)*270),'#edf3f5');draw=ImageDraw.Draw(sheet)
 for row,name in enumerate(names):
  draw.text((18,row*270+8),labels[name],font=font,fill='#122b3c')
  for col,index in enumerate([0,15,30,45]):
   im=Image.open(STAGE/'frames'/name/f'{index:04d}.png');im.thumbnail((320,240));sheet.paste(im,(col*320,row*270+30))
 sheet.save(PREVIEW/(group+'.jpg'),quality=90)
SCRIPTS=OUT/'复核脚本';SCRIPTS.mkdir(exist_ok=True)
for name in ['audit_squirrel_motion.py','build_squirrel_candidates.py','verify_squirrel_roundtrip.py','check_steering_candidate.py','WildlifeAgent.squirrel-facing-candidate.cs']:
 shutil.copyfile(STAGE/name,SCRIPTS/name)
source={'verifiedDate':'2026-10-27','primarySources':[
 {'name':'Gobkit 作者免费模型清单','url':'https://gobkit.com/api/free','author':'Gobkit','license':'CC0 1.0','localEvidence':['来源/gobkit-free-manifest.json','候选模型/gobkit-animal006-pack/LICENSE.txt','候选模型/gobkit-animal009-pack/LICENSE.txt'],'downloadedArchives':['来源/gobkit-animal006-pack.zip','来源/gobkit-animal009-pack.zip'],'verifiedModels':['Squirrel.glb','Capegroundsquirrel.glb'],'verifiedClips':['idle','attack','dead','walk'],'limitation':'包内模型可商用和再分发；没有 run 或 turn 命名片段。地松鼠骨名为不透明标识符，不能按英文骨名自动重定向。'},
 {'name':'Undead Squirrel (ANIMATED)','url':'https://opengameart.org/content/undead-squirrel-animated','author':'Čestmír Dammer / CDmir；页面协作者 TinyWorlds','license':'CC0','localEvidence':['候选模型/OGA-undeadsquirrel/lecense.txt'],'downloadedArchives':['来源/undeadsquirrell.zip'],'verifiedModels':['squirrel-anim.blend'],'limitation':'源外形为亡灵松鼠；只作四足动作与整套替换的备选，不能当作已适配本项目。旧材质已在预览内链接原 diffuse 图，源文件保持不变。'},
 {'name':'Kovonic Squirrel','url':'https://sketchfab.com/3d-models/squirrel-f442c20ca7dd4a988a17a12604556672','author':'Kovonic','license':'CC BY-NC','downloaded':False,'decision':'排除通用商用候选，免费获取不等于允许商业复用。'},
 {'name':'3DDisco Cartoon Squirrel Animated','url':'https://3ddisco.itch.io/cartoon-squirrel-animated-3d-model','author':'3DDisco','license':'Paid commercial asset','downloaded':False,'decision':'页面价格99.99美元，未购买，不列入免费候选。'}]}
(OUT/'来源/许可与来源核对.json').write_text(json.dumps(source,ensure_ascii=False,indent=2),encoding='utf-8')
manifest=[]
for folder in ['来源','原模型候选','候选模型']:
 for p in (OUT/folder).rglob('*'):
  if p.is_file():manifest.append({'path':str(p.relative_to(OUT)).replace('\\','/'),'bytes':p.stat().st_size,'sha256':hashlib.sha256(p.read_bytes()).hexdigest()})
(OUT/'来源/文件校验清单.json').write_text(json.dumps(manifest,ensure_ascii=False,indent=2),encoding='utf-8')
print('PACKAGED',len(manifest))
