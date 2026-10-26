"""Read-only FBX and Unity texture-reference checks for the authored Life assets."""
import bpy
import hashlib
import json
import re
from pathlib import Path

ROOT=Path(__file__).resolve().parents[2]
ART=ROOT/'Assets/Game/Life/Art/Resources/LifeAssets'
DOC=ROOT/'Docs/LifeGameplay/Assets'
manifest=json.loads((DOC/'Journal/journal-manifest.json').read_text(encoding='utf-8'))
texture_names={m['texture'] for m in manifest['materials'].values() if m['texture']}
textures=[]
for name in sorted(texture_names):
    for suffix in ['', '_Normal', '_AO']:
        path=ART/'JournalTextures'/(name+suffix+'.png')
        if not path.is_file() or not path.with_suffix('.png.meta').is_file():
            raise RuntimeError('Missing runtime texture or Unity meta: '+str(path))
        meta=path.with_suffix('.png.meta').read_text(encoding='utf-8')
        normal=suffix=='_Normal'; linear=bool(suffix)
        texture_type=int(re.search(r'^  textureType: (\d+)',meta,re.M).group(1))
        srgb=int(re.search(r'^    sRGBTexture: (\d+)',meta,re.M).group(1))
        if texture_type!=(1 if normal else 0) or srgb!=(0 if linear else 1):
            raise RuntimeError('Texture import settings mismatch: '+path.name)
        textures.append(dict(path=path.relative_to(ROOT).as_posix(),bytes=path.stat().st_size,
                             sha256=hashlib.sha256(path.read_bytes()).hexdigest(),textureType=texture_type,sRGB=bool(srgb)))

models=[]
for path in sorted(ART.glob('*.fbx')):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=str(path))
    materials=sorted({m.name for o in bpy.data.objects if o.type=='MESH' for m in o.data.materials if m})
    images=[]
    for material in bpy.data.materials:
        if not material.use_nodes: continue
        for node in material.node_tree.nodes:
            if node.type!='TEX_IMAGE' or not node.image: continue
            image=node.image; image_path=Path(bpy.path.abspath(image.filepath))
            if not image.packed_file and not image_path.is_file():
                raise RuntimeError('Missing FBX image: '+image.filepath)
            images.append(image_path.name)
    if path.stem=='JournalBook':
        if set(materials)!=set(manifest['materials']): raise RuntimeError('Journal material names differ from runtime texture contract')
        if set(images)!={name+'.png' for name in texture_names}: raise RuntimeError('Journal colour texture references differ')
    elif images: raise RuntimeError('Unexpected texture dependency in colour-only prop '+path.name)
    models.append(dict(path=path.relative_to(ROOT).as_posix(),materials=materials,images=sorted(set(images))))
report=dict(status='PASS',models=models,textures=textures,
    boundary='FBX import and source/meta existence. Normal/AO are assigned by LifeArtImporter; animation-only FBX use no runtime materials. Unity native render validation remains separate.')
(DOC/'asset-reference-check.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8')
print('ASSET_REFERENCE_PASS',len(models),'models',len(textures),'runtime textures')
