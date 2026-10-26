from pathlib import Path
from PIL import Image,ImageDraw
import os,json,re,shutil,hashlib,urllib.parse
ST=Path(r'C:\Users\31483\.codex\visualizations\2026\10\26\01a178cf-479b-7f41-af26-309b1776f6d5\map-staging')
ROOT=Path(r'C:\unity project\Weather House')
DESKTOP=Path(r'C:\Users\31483\Desktop\天气屋\夜间完善_20261027\地图与群落')
DATA=DESKTOP/'候选与数据';OUT=ROOT/'Docs/NightReview20261027/地图与群落'
OUT.mkdir(parents=True,exist_ok=True);SMALL=OUT/'小图';SMALL.mkdir(exist_ok=True);EVID=OUT/'证据摘要';EVID.mkdir(exist_ok=True)
def read(name):return json.loads((DATA/name).read_text(encoding='utf-8'))
def write(path,value):path.write_text(json.dumps(value,ensure_ascii=False,indent=2),encoding='utf-8')
def sha(path):return hashlib.sha256(path.read_bytes()).hexdigest().upper()
def rel(path):return os.path.relpath(path,OUT).replace('\\','/')
proof=read('landscape-apply-proof.json');saved=read('landscape-save-result.json');phase=read('map-acceptance-status.json');plan=read('landscape-plan.json')
smallFiles=[]
for asset,direction in [('A01','east'),('A02','south'),('A03','east'),('A04','south'),('A06','south'),('A07','east'),('A09','east')]:
    key=asset+'-'+direction;canvas=Image.new('RGB',(1280,386),'#f8fbfc');draw=ImageDraw.Draw(canvas)
    for i,label in enumerate(['before','after']):
        source=DATA/label/(key+'.png');pic=Image.open(source).convert('RGB').resize((640,360));canvas.paste(pic,(i*640,26))
        draw.text((i*640+10,7),key+' / '+label.upper()+' native capture (resized QA pair)',fill='#20384a')
    output=SMALL/(key+'-before-after.jpg');canvas.save(output,quality=88);smallFiles.append(output)
copyEvidence=['landscape-apply-proof.json','landscape-save-result.json','landscape-presave.json','landscape-view-review.json','map-acceptance-status.json','map-index-isolation-proof.json','a07-diagnostic.json','boathouse-search.json','flower-source-audit.json','flower-extension-build-result.json']
for name in copyEvidence:shutil.copy2(DATA/name,EVID/name)
REPRO=OUT/'隔离复核';REPRO.mkdir(exist_ok=True)
shutil.copy2(DATA/'prepare_map_index_scene.py',REPRO/'prepare_map_index_scene.py')
summary=dict(status=phase['status'],playStatus=phase['playStatus'],performance=phase.get('performanceEvidence'),localImplementationCommit=phase.get('localImplementationCommit'),counts=proof,save=saved,stableOriginalBackup=phase.get('stableOriginalBackup'),
    implemented='首批96株植物重组、11个家具组与12丛蓝/橙花副本；未改中央岛/天气屋原布局。',
    perNode=[dict(name=n['name'],plantMoves=sum(m['node']==Path(n['source']).stem for m in plan['moves']),newProps=sum(a['node']==n['name'] and not a.get('purpose','').startswith('Full existing R') for a in plan['additions']),newFlowers=sum(a['node']==n['name'] and a.get('purpose','').startswith('Full existing R') for a in plan['additions']),approachStatus=n['approachStatus']) for n in plan['nodes']],
    pending=phase['pending'],planningOnly=phase['planningOnly'],limits='收据计数与截图审议，不等同当前Editor实时读数、逐区人物穿行或用户验收。')
write(EVID/'收据与范围摘要.json',summary)
page=(DESKTOP/'地图与群落_研究与候选.html').read_text(encoding='utf-8')
external=rel(DESKTOP)
page=page.replace('候选与数据/',external+'/候选与数据/')
for name in copyEvidence:page=page.replace(external+'/候选与数据/'+name,'证据摘要/'+name)
page=page.replace(external+'/候选与数据/prepare_map_index_scene.py','隔离复核/prepare_map_index_scene.py')
sources=['NightMapSnapshot.cs','NightMapLandscapeCandidate.cs','NightMapViewCapture.cs','NightMapTransactionObserver.cs','NightMapBoathouseSearch.cs','NightMapA07Diagnostic.cs','NightMapSaveObserver.cs','NightMapFlowerExtension.cs']
for name in sources:page=page.replace(external+'/候选与数据/'+name,rel(ROOT/'Assets/Game/Life/Editor'/name))
pairs=''.join('<figure><a href="'+p.relative_to(OUT).as_posix()+'"><img loading="lazy" src="'+p.relative_to(OUT).as_posix()+'" alt="'+p.stem+'"></a><figcaption>'+p.stem+'，左为前、右为后；缩小复核图，原始截图见下文完整画面。</figcaption></figure>' for p in smallFiles)
local='<section><h2>项目轻量复核副本</h2><p class="notice"><b>本页具有本机依赖。</b>项目内保存交互地图、收据与保护摘要、7张前后小拼图。下方30张前图、30张后图、全量快照/manifest仍相对指向本机桌面完整包，不随此Docs副本进入Git；原场景逐字节备份另存本机Review/map-backups，SHA校验见收据摘要；它不随此副本进入Git。移走本机完整包后，大图和全量记录链接将失效，本页小图与证据摘要仍可读。</p><nav><a href="../夜间工作总览.html">返回夜间总览</a><a href="证据摘要/收据与范围摘要.json">首批范围与收据计数</a><a href="交付复核.json">副本与本机依赖检查</a></nav><div class="gallery">'+pairs+'</div></section>'
page=page.replace('<body>','<body>'+local,1)
(OUT/'地图与群落_研究与候选.html').write_text(page,encoding='utf-8')
generator=OUT/'生成轻量副本.py'
if Path(__file__).resolve()!=generator.resolve():shutil.copy2(Path(__file__),generator)
refs=re.findall(r'(?:href|src)="([^"]+)"',page);localRefs=[];externalRefs=[];missing=[]
for ref in sorted(set(refs)):
    if ref.startswith(('#','https:','http:','data:')):continue
    target=(OUT/urllib.parse.unquote(ref.split('#',1)[0])).resolve();entry=dict(reference=ref,path=str(target),exists=target.exists())
    if not target.exists():missing.append(entry)
    if target.is_relative_to(OUT):localRefs.append(entry)
    else:externalRefs.append(entry)
# This check file is linked from the page and is produced below.
missing=[m for m in missing if Path(m['path'])!=OUT/'交付复核.json']
originals=[DATA/label/(v['key']+'.png') for label in ['before','after'] for v in read(label+'/views.json')['views']]
write(OUT/'本机依赖与文件清单.json',dict(status='LOCAL_DESKTOP_DEPENDENCY_EXPLICIT',desktopCompletePackage=str(DESKTOP),originalPngCount=len(originals),originalPngs=[dict(path=str(p),bytes=p.stat().st_size,sha256=sha(p)) for p in originals],externalReferences=externalRefs,smallPairs=[dict(path=p.relative_to(OUT).as_posix(),bytes=p.stat().st_size,sha256=sha(p)) for p in smallFiles]))
verification=dict(status='PASS_LIGHTWEIGHT_FILES_AND_CURRENT_LOCAL_LINKS' if not missing else 'FAIL_MISSING_REFERENCE',originalPngCount=len(originals),copiedOriginalPngCount=0,smallPairJpgCount=len(smallFiles),localReferenceCount=len(localRefs),externalReferenceCount=len(externalRefs),missing=missing,
    limitations='Only file/link checks and inherited minimal-DOM script execution; this does not assert browser rendering, final Play or human visual acceptance.',pagePhase=phase['status'],playStatus=phase['playStatus'])
write(OUT/'交付复核.json',verification)
docsFiles=sorted(p for p in OUT.rglob('*') if p.is_file());codeFiles=[ROOT/'Assets/Game/Life/Editor'/name for name in sources];repoFiles=docsFiles+[p for c in codeFiles for p in [c,Path(str(c)+'.meta')]]
owned=dict(status='EXACT_MAP_UNIT_PATHS_FOR_COORDINATOR_REVIEW_NOT_STAGED',files=[dict(path=p.relative_to(ROOT).as_posix(),bytes=p.stat().st_size,sha256=sha(p)) for p in repoFiles],
    sourceAgreement=[dict(path=p.relative_to(ROOT).as_posix(),matchesStaging=sha(p)==sha(ST/p.name)) for p in codeFiles],
    sceneTransaction=dict(path='Assets/Scenes/WeatherHouse_WatersideV2.unity',savedSHA=saved['savedSceneSha256'],originalBackupSHA=saved['backupSha256'],baselineWasAlreadyGitDirty=True,indexIsolation=read('map-index-isolation-proof.json'),note='仅本次manifest列出的96株移动和23组新增已由保护与绑定守卫验明。场景在本次前已有Git dirty；原字节备份可区分本次增量，场景提交由主协调者独立审查，不以整个原dirty diff自动归属本单元。'),
    exclude=['Other Docs/NightReview20261027 modules','All original PNGs and full snapshot/plan JSON','Scene byte backup','Other pre-existing scene/prefab/performance changes','map-staging compiled DLL/rsp and temporary requests'],docsFileCount=len(docsFiles),docsBytes=sum(p.stat().st_size for p in docsFiles))
write(ST/'map-owned-paths.json',owned)
print(json.dumps(dict(status=verification['status'],html=str(OUT/'地图与群落_研究与候选.html'),docsFiles=len(docsFiles),docsBytes=owned['docsBytes'],ownedRepoFiles=len(repoFiles),originalPngsInGit=0,missing=len(missing),allEditorSourcesMatch=all(r['matchesStaging'] for r in owned['sourceAgreement'])),ensure_ascii=False))
