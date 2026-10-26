"""Compress authored sources without changing their scene or animation data."""
import bpy
import hashlib
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
SOURCE = ROOT / 'Tools/LifeAssets/Source/Animations'
FBX = ROOT / 'Assets/Game/Life/Art/Resources/LifeAssets/Animations'

def digest_scene():
    bpy.context.view_layer.update()
    h = hashlib.sha256()
    def put(value): h.update(repr(value).encode('utf-8'))
    for obj in sorted(bpy.data.objects, key=lambda x: x.name):
        put((obj.name, obj.type, tuple(v for row in obj.matrix_basis for v in row)))
        if obj.type == 'MESH':
            mesh = obj.data
            put([tuple(v.co) for v in mesh.vertices])
            put([tuple(p.vertices) for p in mesh.polygons])
            put([[tuple(x.uv) for x in uv.data] for uv in mesh.uv_layers])
            put([[(g.group, g.weight) for g in v.groups] for v in mesh.vertices])
        elif obj.type == 'ARMATURE':
            put([(b.name, b.parent.name if b.parent else '', tuple(v for row in b.matrix_local for v in row)) for b in obj.data.bones])
    for action in sorted(bpy.data.actions, key=lambda x: x.name):
        put((action.name, tuple(action.frame_range)))
        for layer in action.layers:
            for strip in layer.strips:
                for bag in strip.channelbags:
                    for curve in bag.fcurves:
                        put((curve.data_path, curve.array_index, [(tuple(p.co), tuple(p.handle_left), tuple(p.handle_right), p.interpolation) for p in curve.keyframe_points]))
    for image in sorted(bpy.data.images, key=lambda x: x.name):
        # Blender discards the transient Render Result on the first save/reload.
        # It is not a packed texture, authored geometry or animation data.
        if image.type in {'RENDER_RESULT', 'COMPOSITING'}: continue
        # Image dimensions are lazy-loaded; the packed bytes are the source of truth.
        put((image.name, image.filepath))
        if image.packed_file: h.update(image.packed_file.data)
    return h.hexdigest()

bpy.context.preferences.filepaths.save_version = 0
fbx_before = {p.name: hashlib.sha256(p.read_bytes()).hexdigest() for p in FBX.glob('*.fbx')}
report = []
manifest = json.loads((ROOT / 'Docs/LifeGameplay/Assets/file-integrity.json').read_text(encoding='utf-8-sig'))
original_sizes = {Path(x['path']).name: x['bytes'] for x in manifest}
out = ROOT / 'Docs/LifeGameplay/Assets/Animations/source-compression.json'
if out.exists():
    original_sizes.update({x['file']: x['originalDeliveryBytes'] for x in json.loads(out.read_text(encoding='utf-8'))['sources']})
for path in sorted(SOURCE.glob('*.blend')):
    before_size = path.stat().st_size
    bpy.ops.wm.open_mainfile(filepath=str(path))
    before = digest_scene()
    bpy.context.preferences.filepaths.save_version = 0
    bpy.ops.wm.save_as_mainfile(filepath=str(path), compress=True)
    bpy.ops.wm.open_mainfile(filepath=str(path))
    after = digest_scene()
    if before != after: raise RuntimeError('Source scene data changed: ' + path.name)
    report.append(dict(file=path.name, originalDeliveryBytes=original_sizes.get(path.name, before_size), beforeBytes=before_size, afterBytes=path.stat().st_size, sceneDigestBefore=before, sceneDigestAfter=after))
fbx_after = {p.name: hashlib.sha256(p.read_bytes()).hexdigest() for p in FBX.glob('*.fbx')}
if fbx_before != fbx_after: raise RuntimeError('Animation FBX changed')
out.write_text(json.dumps(dict(sources=report, fbxUnchanged=fbx_before == fbx_after, fbxHashes=fbx_after, excludedTransientImageTypes=['RENDER_RESULT', 'COMPOSITING']), indent=2), encoding='utf-8')
print('COMPRESSED_SOURCES_PASS', sum(x['beforeBytes'] for x in report), sum(x['afterBytes'] for x in report))
