"""Read-only Unity package inventory; previews stay outside Assets."""
import concurrent.futures, collections, gzip, json, pathlib, tarfile, re

ROOT = pathlib.Path(__file__).resolve().parent
PACKS = {
    'NatureBundle': 'Stylized Nature Bundle 1.5.unitypackage',
    'AncientRuins': 'Stylized_Ancient_Ruins_Environment_v1.0.unitypackage',
    'EnchantedForest': 'POLYGON-Enchanted Forest-Nature Biome 1.01.unitypackage',
    'PureNature': 'Pure Nature 2 Meadows v1.0.unitypackage',
    'Dreamscapes': 'Dreamscapes v2.0.unitypackage',
    'TerrainTextures': 'Stylized Terrain Textures 1.0.unitypackage',
    'Cliffs': 'Realistic Cliffs and Rocks 1.1.unitypackage',
    'CastleValley': 'Castle Valley Collection 1 1.8.unitypackage',
    'ForestAnimals': 'Forest animals v1.0.unitypackage',
    'Butterflies': 'Butterfly Effect.unitypackage',
    'PBRMaterials': 'PBR Material Bundle Vol1 445.unitypackage',
    'DesertStamps': 'Rocky Desert - StampIT 2.0.0.unitypackage',
}

def index(item):
    key, name = item
    output = ROOT / 'indices' / (key + '.json')
    if output.exists(): return json.loads(output.read_text(encoding='utf-8'))['summary']
    paths = (ROOT / 'd-drive-packages.txt').read_text(encoding='utf-8-sig').splitlines()
    source = next(p for p in paths if pathlib.Path(p).name == name)
    records = {}
    previews = ROOT / 'previews' / key
    previews.mkdir(parents=True, exist_ok=True)
    with gzip.open(source, 'rb') as compressed, tarfile.open(fileobj=compressed, mode='r|') as archive:
        for member in archive:
            if not member.isfile(): continue
            parts = member.name.replace('\\', '/').strip('/').split('/')
            if len(parts) != 2 or not re.fullmatch('[0-9a-fA-F]{32}', parts[0]): continue
            guid, kind = parts
            record = records.setdefault(guid, {'guid': guid})
            if kind == 'pathname':
                record['path'] = archive.extractfile(member).read().decode('utf-8-sig').splitlines()[0].rstrip('\x00')
            elif kind == 'asset': record['size'] = member.size
            elif kind == 'asset.meta': record['meta'] = archive.extractfile(member).read().decode('utf-8-sig', errors='replace')
            elif kind == 'preview.png' and member.size:
                (previews / (guid+'.png')).write_bytes(archive.extractfile(member).read())
    entries = [r for r in records.values() if 'path' in r]
    summary = {'key': key, 'source': source, 'count': len(entries), 'mb': round(sum(r.get('size', 0) for r in entries)/1048576),
               'extensions': dict(collections.Counter(pathlib.Path(r['path']).suffix.lower() for r in entries)),
               'roots': dict(collections.Counter('/'.join(r['path'].split('/')[:3]) for r in entries))}
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(json.dumps({'summary': summary, 'entries': entries}, ensure_ascii=False, indent=2), encoding='utf-8')
    return summary

if __name__ == '__main__':
    with concurrent.futures.ThreadPoolExecutor(max_workers=2) as pool:
        jobs = {pool.submit(index, item): item[0] for item in PACKS.items()}
        for job in concurrent.futures.as_completed(jobs):
            try: print(json.dumps(job.result(), ensure_ascii=False), flush=True)
            except Exception as error: print(json.dumps({'key': jobs[job], 'error': str(error)}, ensure_ascii=False), flush=True)
