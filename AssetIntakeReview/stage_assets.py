"""Select art, follow GUID dependencies, and stage without overwriting project assets."""
import collections, gzip, hashlib, json, pathlib, re, tarfile, uuid, zipfile, sys

PROJECT = pathlib.Path(__file__).resolve().parent.parent
REVIEW = PROJECT / 'AssetIntakeReview'
STAGE = PROJECT / 'Temp/AssetIntake/staged'
DEST = 'Assets/Environment/DDriveLibrary'
NS = uuid.UUID('9324e6cb-d469-44e7-81df-269e013f9d0d')
TEXT = {'.prefab', '.mat', '.asset', '.controller', '.anim', '.terrainlayer', '.physicmaterial', '.shader', '.shadergraph', '.shadersubgraph', '.hlsl', '.cginc', '.txt'}
ALLOWED = TEXT | {'.fbx', '.obj', '.png', '.tga', '.tif', '.tiff', '.jpg', '.jpeg', '.exr', '.mask'}
HEX = re.compile(r'\b[0-9a-f]{32}\b')
PACK_KEYS = ['AncientRuins', 'NatureBundle', 'EnchantedForest', 'Dreamscapes', 'TerrainTextures', 'Cliffs', 'CastleValley', 'Butterflies', 'PBRMaterials']

def archive_entries(source):
    entries = {}
    with gzip.open(source, 'rb') as compressed, tarfile.open(fileobj=compressed, mode='r|') as archive:
        for member in archive:
            parts = member.name.strip('/').split('/')
            if not member.isfile() or len(parts) != 2: continue
            guid, kind = parts
            if not re.fullmatch('[0-9a-f]{32}', guid): continue
            r = entries.setdefault(guid, {'guid': guid, 'source': str(source)})
            if kind == 'pathname': r['path'] = archive.extractfile(member).read().decode('utf-8-sig').splitlines()[0].rstrip('\x00')
            elif kind == 'asset.meta': r['meta'] = archive.extractfile(member).read().decode('utf-8-sig')
            elif kind == 'asset':
                r['size'] = member.size
                if member.size < 4*1024*1024:
                    data = archive.extractfile(member).read()
                    try:
                        value = data.decode('utf-8-sig')
                        if '\x00' not in value: r['text'] = value
                    except UnicodeDecodeError: pass
    return [r for r in entries.values() if 'path' in r]

def seed(key, r):
    p = r['path']; low = p.lower(); ext = pathlib.Path(p).suffix.lower()
    if ext not in ALLOWED: return False
    if ext == '.txt' and not any(x in low for x in ['/demo/', '/standard assets/']): return True
    if key == 'AncientRuins': return ext == '.prefab'
    if key == 'NatureBundle': return ext == '.prefab' and '/Prefabs/' in p and not any(x in p for x in ['/Particles/', '_Particles/', '/Terrain/', '/GrassFlower/']) and not re.search(r'/Grass_0[1-4]\.prefab$', p)
    if key == 'EnchantedForest': return ext == '.prefab' and '/PNB_Enchanted_Forest/Prefabs/' in p
    if key == 'Dreamscapes': return ext in {'.prefab', '.fbx'} and '/Nature/' in p and not any(x in low for x in ['crystal', 'lightline', 'lightbeam'])
    if key == 'TerrainTextures': return ext in {'.terrainlayer', '.mat'}
    if key == 'Cliffs': return ext == '.prefab' and '/Prefabs/' in p
    if key == 'CastleValley': return ext == '.prefab' and '/Prefabs/' in p and '/Extras/' not in p and not any(x in p for x in ['Deer_Skull', 'Sage_', 'RabbitBrush'])
    if key == 'Butterflies': return ext == '.prefab' and '/LP Butterflies/' in p
    if key == 'PBRMaterials': return ext == '.mat' and ('/Cartoon/' in p or '/Ground/' in p or ('/Stone/' in p and re.search('moss|old', low)) or '/Ultimate Wood Materials/' in p)
    return False

def strip_behaviours(text):
    blocks = re.split(r'(?=^--- !u!)', text, flags=re.M)
    ids = [re.match(r'--- !u!114 &(-?\d+)', b).group(1) for b in blocks if b.startswith('--- !u!114 &')]
    text = ''.join(b for b in blocks if not b.startswith('--- !u!114 &'))
    for ident in ids:
        text = re.sub(r'^  - (?:component: |114: )\{fileID: '+re.escape(ident)+r'\}\r?\n', '', text, flags=re.M)
    return text, len(ids)

def strip_static_animators(text):
    blocks = re.split(r'(?=^--- !u!)', text, flags=re.M)
    ids = []
    for b in blocks:
        m = re.match(r'--- !u!95 &(-?\d+)', b)
        if m and 'm_Controller: {fileID: 0}' in b: ids.append(m[1])
    text = ''.join(b for b in blocks if not any(b.startswith('--- !u!95 &'+i+'\n') for i in ids))
    for ident in ids: text = re.sub(r'^  - component: \{fileID: '+re.escape(ident)+r'\}\r?\n', '', text, flags=re.M)
    return text

def prepare(key):
    index = json.loads((REVIEW / 'indices' / (key+'.json')).read_text(encoding='utf-8'))
    source = index['summary']['source']
    cache = PROJECT / 'Temp/AssetIntake' / (key+'-contents.json')
    if cache.exists(): records = json.loads(cache.read_text(encoding='utf-8'))
    else:
        records = archive_entries(source)
        cache.write_text(json.dumps(records, ensure_ascii=False), encoding='utf-8')
    bypath = {r['path']: r for r in records}
    original_guids = {r['guid']: r['path'] for r in records}
    nested = {'NatureBundle':'NatureBundle_SNB_URP_10xxx.unitypackage', 'EnchantedForest':'EnchantedForest_Unity_2022.unitypackage'}
    if key in nested:
        for r in archive_entries(PROJECT / 'Temp/AssetIntake/nested' / nested[key]):
            if r['path'] in bypath or pathlib.Path(r['path']).suffix in {'.shader', '.hlsl', '.cginc'}: bypath[r['path']] = r
    if key == 'CastleValley':
        with zipfile.ZipFile(PROJECT / 'Temp/AssetIntake/nested/CastleValley_URP.zip') as z:
            for name in z.namelist():
                p = 'Assets/Castle Valley Collection 1/' + name
                if p not in bypath or pathlib.Path(name).suffix.lower() not in {'.mat','.shader','.hlsl','.cginc'}: continue
                r = dict(bypath[p]);r['text'] = z.read(name).decode('utf-8-sig');r['size'] = len(z.read(name));r['inline'] = True
                if name+'.meta' in z.namelist(): r['meta'] = z.read(name+'.meta').decode('utf-8-sig')
                bypath[p] = r
    records = list(bypath.values())
    byguid = {r['guid']: r for r in records}
    for guid, p in original_guids.items(): byguid[guid] = bypath[p]
    selected = {r['path']: r for r in records if seed(key, r)}
    pending = list(selected.values())
    unavailable = set()
    while pending:
        r = pending.pop()
        content = r.get('text', '') + r.get('meta', '')
        if r['path'].endswith('.prefab'):
            content = strip_behaviours(r.get('text',''))[0]
            if key == 'NatureBundle': content = strip_static_animators(content)
            content += r.get('meta','')
        for guid in HEX.findall(content):
            dep = byguid.get(guid)
            if dep is None: continue
            ext = pathlib.Path(dep['path']).suffix.lower()
            if ext not in ALLOWED: continue
            if ext == '.asset' and '--- !u!43 ' not in dep.get('text',''): continue
            if dep['path'] not in selected: selected[dep['path']] = dep;pending.append(dep)
    # Map ALL source GUIDs to the isolated library, including swapped URP versions.
    remap = {guid: uuid.uuid5(NS, key+'/'+r['path']).hex for guid,r in byguid.items()}
    def rewrite(data): return HEX.sub(lambda m: remap.get(m.group(), m.group()), data)
    files = []
    need = collections.defaultdict(dict)
    removed = 0
    for r in selected.values():
        old = pathlib.PurePosixPath(r['path'])
        if old.parts[0] != 'Assets' or '..' in old.parts or any('\n' in p for p in old.parts): raise ValueError(old)
        relative = '/'.join(old.parts[2:])
        target = DEST + '/' + key + '/' + relative
        out = STAGE / target
        out.parent.mkdir(parents=True, exist_ok=True)
        ext = old.suffix.lower()
        if ext in TEXT and 'text' in r:
            content = r['text']
            if ext == '.prefab': content, n = strip_behaviours(content); removed += n
            if ext == '.prefab' and key == 'NatureBundle': content = strip_static_animators(content)
            if ext == '.mat': content = strip_behaviours(content)[0]
            content = rewrite(content)
            if ext in {'.shader','.hlsl','.cginc','.shadergraph','.shadersubgraph'}:
                content = content.replace('/'.join(old.parts[:2])+'/', DEST+'/'+key+'/')
            out.write_text(content.replace('\r\n', '\n'), encoding='utf-8', newline='\n')
        elif not out.exists() or out.stat().st_size != r.get('size', -1): need[r['source']][r['guid']] = out
        meta = rewrite(r.get('meta', 'fileFormatVersion: 2\nguid: '+r['guid']+'\n'))
        # Baked demo scene bundles are irrelevant to these standalone resources.
        meta = re.sub(r'(?m)^  assetBundleName:.*$', '  assetBundleName:', meta)
        pathlib.Path(str(out)+'.meta').write_text(meta.replace('\r\n', '\n'), encoding='utf-8', newline='\n')
        files.append({'sourcePackage':source,'sourceAsset':r['path'],'path':target,'guid':remap[r['guid']], 'sourceSize':r.get('size',0), 'size':r.get('size',0)})
    for archive_path, needed in need.items():
        with gzip.open(archive_path,'rb') as compressed, tarfile.open(fileobj=compressed,mode='r|') as tar:
            for m in tar:
                parts=m.name.strip('/').split('/')
                if len(parts)==2 and parts[1]=='asset' and parts[0] in needed:
                    target=needed.pop(parts[0]);target.write_bytes(tar.extractfile(m).read())
            if needed: raise RuntimeError('Missing assets '+str(list(needed)))
    summary={'key':key,'source':source,'files':len(files),'mb':round(sum(r['size'] for r in files)/1048576,1),'removedDemoBehaviours':removed,
             'types':dict(collections.Counter(pathlib.Path(r['path']).suffix for r in files))}
    return files,summary

if __name__=='__main__':
    files=[]; summaries=[]
    for key in (sys.argv[1:] or PACK_KEYS):
        pack, summary=prepare(key);files.extend(pack);summaries.append(summary)
        print(json.dumps(summary,ensure_ascii=False),flush=True)
        (REVIEW/'import-manifest.json').write_text(json.dumps({'root':DEST,'packs':summaries,'files':files},ensure_ascii=False,indent=2),encoding='utf-8')
    for r in files:
        p=STAGE/r['path']
        if not p.is_file() or not pathlib.Path(str(p)+'.meta').is_file(): raise RuntimeError(p)
        r['sha256']=hashlib.sha256(p.read_bytes()).hexdigest()
        r['size']=p.stat().st_size
    (REVIEW/'import-manifest.json').write_text(json.dumps({'root':DEST,'packs':summaries,'files':files},ensure_ascii=False,indent=2),encoding='utf-8')
    print('STAGING COMPLETE',len(files),flush=True)
