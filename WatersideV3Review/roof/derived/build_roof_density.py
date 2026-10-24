"""Derive only the two roof meshes from the baked v05 copy. Never writes source files or Assets."""
import bpy, json, hashlib, math, random, struct, traceback
from pathlib import Path
from collections import Counter
from mathutils import Vector
from mathutils.kdtree import KDTree
import numpy as np

OUT = Path(__file__).resolve().parent
PACKAGE = Path(r'C:\Users\31483\Desktop\天气屋\Unity导入包_v05')
SOURCE = PACKAGE / '天气屋_v05_Unity烘焙预览.blend'
V05 = PACKAGE.parent / '整体精修样板_20261024_v05' / '天气屋_山墙木构优化_v05.blend'
SEED = 24102451
KINDS = ('Moss', 'Foliage')
REPORT = {'status': 'RUNNING', 'seed': SEED, 'source': str(SOURCE), 'geometry': {}, 'lods': [], 'roundtrip': [], 'meshValidation': []}

def digest(path): return hashlib.sha256(path.read_bytes()).hexdigest()
def select(obs):
    bpy.ops.object.select_all(action='DESELECT')
    for o in obs: o.hide_set(False); o.select_set(True)
    bpy.context.view_layer.objects.active = obs[0]

def apply(o, mod):
    select([o]); bpy.ops.object.modifier_apply(modifier=mod.name)

def components(me):
    parent = list(range(len(me.vertices)))
    def find(a):
        while parent[a] != a: parent[a] = parent[parent[a]]; a = parent[a]
        return a
    for e in me.edges:
        a, b = (find(i) for i in e.vertices)
        if a != b: parent[b] = a
    faces = {}
    for p in me.polygons: faces.setdefault(find(p.vertices[0]), []).append(p.index)
    out = []
    for ids in faces.values():
        vertices = sorted({v for i in ids for v in me.polygons[i].vertices})
        center = sum((me.vertices[i].co for i in vertices), Vector()) / len(vertices)
        out.append(dict(faces=ids, vertices=vertices, center=center))
    return sorted(out, key=lambda c: min(c['faces']))

def bounds(o):
    coords = np.asarray([tuple(v.co) for v in o.data.vertices], dtype=np.float32)
    lo, hi = coords.min(axis=0), coords.max(axis=0)
    world = np.asarray([tuple(o.matrix_world @ v.co) for v in o.data.vertices], dtype=np.float32)
    return dict(localMin=lo.tolist(), localMax=hi.tolist(), worldMin=world.min(axis=0).tolist(), worldMax=world.max(axis=0).tolist())

def stat(o):
    o.data.calc_loop_triangles()
    return dict(name=o.name, vertices=len(o.data.vertices), triangles=len(o.data.loop_triangles),
        matrix=[list(r) for r in o.matrix_world], uv=[u.name for u in o.data.uv_layers],
        materials=[m.name for m in o.data.materials], bounds=bounds(o))

def sparse_choice(candidates, parts, me, fraction, centers):
    # Broad seeded colonies keep neighbouring leaves together instead of creating evenly scattered glitter.
    def score(index):
        p = parts[index]['center']
        field = min(((p.x-x)**2+(p.y-y)**2)/(r*r) for x,y,r in centers)
        noise = int(hashlib.sha256(struct.pack('<Ifff',SEED,p.x,p.y,p.z)).hexdigest()[:8],16)/0xffffffff
        return field + noise*.12
    count = round(len(candidates)*fraction)
    keep = set(sorted(candidates, key=score)[:count])
    # Preserve every visible extremum; roof silhouette and original mesh extents stay stable.
    for axis in range(3):
        for upper in (False, True):
            extreme = (max if upper else min)(range(len(me.vertices)),key=lambda i:me.vertices[i].co[axis])
            for i,c in enumerate(parts):
                if extreme in c['vertices'] and i in candidates: keep.add(i); break
    return keep

def subset(source, parts, kept):
    original = source.data
    faces = sorted(i for part in kept for i in parts[part]['faces'])
    used = sorted({v for i in faces for v in original.polygons[i].vertices})
    remap = {v:i for i,v in enumerate(used)}
    mesh = bpy.data.meshes.new(source.name+'_Sparse')
    mesh.from_pydata([original.vertices[i].co[:] for i in used], [],
        [tuple(remap[v] for v in original.polygons[i].vertices) for i in faces])
    for m in original.materials: mesh.materials.append(m)
    uv = mesh.uv_layers.new(name='GameUV'); old_uv = original.uv_layers['GameUV']
    normals = []
    for new_p, old_i in zip(mesh.polygons, faces):
        old_p = original.polygons[old_i]
        new_p.use_smooth = old_p.use_smooth; new_p.material_index = old_p.material_index
        for new_l, old_l in zip(new_p.loop_indices, old_p.loop_indices):
            uv.data[new_l].uv = old_uv.data[old_l].uv
            normals.append(original.corner_normals[old_l].vector[:])
    mesh.update(); mesh.normals_split_custom_set(normals)
    assert all(tuple(mesh.vertices[i].co)==tuple(original.vertices[j].co) for i,j in enumerate(used))
    max_uv = max((uv.data[nl].uv-old_uv.data[ol].uv).length for p,old_i in zip(mesh.polygons,faces)
        for nl,ol in zip(p.loop_indices,original.polygons[old_i].loop_indices))
    assert max_uv == 0, max_uv
    new_components = components(mesh)
    assert len(new_components) == len(kept), (source.name, len(new_components),len(kept))
    assert sum(len(parts[i]['faces']) for i in kept) == len(mesh.polygons)
    # Each connected source part has either all faces or none in the derived LOD0 mesh.
    present=set(faces)
    assert all(sum(f in present for f in c['faces']) in (0,len(c['faces'])) for c in parts)
    source.data = mesh
    return dict(keptComponents=len(kept), originalComponents=len(parts), keptFaces=len(faces),
        coordinateMaxError=0, uvMaxError=max_uv, completeComponentsOnly=True,
        keptComponentIndices=sorted(kept))

def export(obs, path):
    select(obs)
    bpy.ops.export_scene.fbx(filepath=str(path),use_selection=True,object_types={'MESH'},
        apply_unit_scale=True,apply_scale_options='FBX_SCALE_UNITS',axis_forward='-Z',axis_up='Y',
        use_mesh_modifiers=True,mesh_smooth_type='FACE',use_tspace=True,bake_anim=False,
        add_leaf_bones=False,path_mode='STRIP',embed_textures=False)

def max_nearest(a,b):
    tree=KDTree(len(b))
    for i,p in enumerate(b): tree.insert(p,i)
    tree.balance()
    return max(tree.find(p)[2] for p in a)

def snapshot(o):
    return dict(stats=stat(o), points=[tuple(v.co) for v in o.data.vertices],
        uv=np.asarray([tuple(v.uv) for v in o.data.uv_layers['GameUV'].data],dtype=np.float32))

def main():
    immutable=[V05,SOURCE,PACKAGE/'检查记录'/'prepared_unbaked.blend',PACKAGE/'制作脚本'/'prepare_unity.py']
    immutable += [PACKAGE/f'WeatherHouse_v05_LOD{i}.fbx' for i in range(3)]
    immutable += sorted((PACKAGE/'Textures').glob('Moss_*.png')) + [PACKAGE/'Textures'/'Foliage_BaseColor.png']
    before_hash={str(p):digest(p) for p in immutable}
    REPORT['sourceHashes']=before_hash
    bpy.ops.wm.open_mainfile(filepath=str(SOURCE),load_ui=False)
    original_lods={i:{kind:stat(bpy.data.objects[f'WeatherHouse_{kind}_LOD{i}']) for kind in KINDS} for i in range(3)}
    source0={kind:bpy.data.objects[f'WeatherHouse_{kind}_LOD0'] for kind in KINDS}
    # The two authored source groups share this exact export object transform.
    assert source0['Moss'].matrix_world == source0['Foliage'].matrix_world
    rng=random.Random(SEED); centers=[]
    while len(centers)<13:
        p=(rng.uniform(-2.55,2.55),rng.uniform(-2.65,2.70),rng.uniform(.24,.40))
        if all(math.hypot(p[0]-q[0],p[1]-q[1])>.65 for q in centers): centers.append(p)
    REPORT['colonyCentersLocal'] = centers
    labels=json.loads((PACKAGE/'检查记录'/'geometry_report.json').read_text(encoding='utf8'))
    skin_faces=next(r['triangles_after'] for r in labels if r['source']=='苔草连续覆盖_带露瓦缺口')
    leaf_faces=next(r['triangles_after'] for r in labels if r['source']=='苔草细碎叶缘_真实几何')
    for kind,source in source0.items():
        parts=components(source.data)
        if kind=='Moss':
            protected={i for i,c in enumerate(parts) if min(c['faces'])<skin_faces}
            assert all(max(parts[i]['faces'])<skin_faces for i in protected)
            assert sum(len(parts[i]['faces']) for i in protected)==skin_faces==48984
            candidates=set(range(len(parts)))-protected
            assert len(candidates)==7149
            chosen=sparse_choice(candidates,parts,source.data,.25,[(x,y,r*1.5) for x,y,r in centers])
        else:
            candidates={i for i,c in enumerate(parts) if len(c['faces'])==9}
            assert len(candidates)==7598 and sum(len(parts[i]['faces']) for i in candidates)==leaf_faces
            assert all(max(parts[i]['faces'])<leaf_faces for i in candidates)
            protected=set(range(len(parts)))-candidates
            assert Counter(len(parts[i]['faces']) for i in protected)=={44:254,6:4219}
            chosen=sparse_choice(candidates,parts,source.data,.125,centers)
        record=subset(source,parts,protected|chosen)
        record.update(targetedComponents=len(candidates),retainedTargetedComponents=len(chosen),
            targetedRetention=len(chosen)/len(candidates),preservedOtherComponents=len(protected))
        record['sourceStats']=original_lods[0][kind]; record['derivedStats']=stat(source)
        assert record['sourceStats']['matrix']==record['derivedStats']['matrix']
        REPORT['geometry'][kind]=record
        print('FILTERED',kind,len(candidates),'->',len(chosen),'protected',len(protected),flush=True)
    output_col=bpy.data.collections.new('SPARSE_ROOF_EXPORT');bpy.context.scene.collection.children.link(output_col)
    expected={}
    for level,ratio in [(0,1),(1,.36),(2,.13)]:
        obs=[]
        for kind,base in source0.items():
            if level==0: o=base
            else:
                previous=bpy.data.objects[f'WeatherHouse_{kind}_LOD{level}'];previous.name += '_OriginalHidden'
                o=base.copy();o.data=base.data.copy();output_col.objects.link(o);o.name=f'WeatherHouse_{kind}_LOD{level}'
                mod=o.modifiers.new('LOD reduction','DECIMATE');mod.ratio=ratio;mod.use_collapse_triangulate=True;apply(o,mod)
            faces_before_validation=len(o.data.polygons)
            corrected=o.data.validate(verbose=True,clean_customdata=False)
            o.data.update();o.data.calc_loop_triangles()
            REPORT['meshValidation'].append(dict(level=level,kind=kind,
                corrected=corrected,facesBefore=faces_before_validation,facesAfter=len(o.data.polygons),
                removedFaces=faces_before_validation-len(o.data.polygons)))
            if level==0: assert not corrected,('LOD0 selected components unexpectedly changed',kind)
            assert len(o.data.polygons)>0 and len(o.data.uv_layers)==1 and len(o.data.materials)==1
            assert all(math.isfinite(c) for v in o.data.vertices for c in v.co)
            assert all(p.area>1e-14 for p in o.data.loop_triangles)
            expected[(level,kind)]=snapshot(o);obs.append(o)
        name=f'WeatherHouse_v05_RoofSparse_LOD{level}.fbx'
        export(obs,OUT/name)
        REPORT['lods'].append(dict(level=level,file=name,decimateRatio=ratio,meshes=[stat(o) for o in obs],
            originalMeshes=original_lods[level]))
        for o in obs:
            o.hide_render=level!=0
            if level: o.hide_set(True)
    # A separate editable copy keeps the full original house context for optional visual review.
    bpy.context.preferences.filepaths.save_version=0
    bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'屋顶疏密派生_仅供核对.blend'))
    for level in range(3):
        bpy.ops.wm.read_factory_settings(use_empty=True)
        bpy.ops.import_scene.fbx(filepath=str(PACKAGE/f'WeatherHouse_v05_LOD{level}.fbx'),use_image_search=False)
        old={kind:stat(bpy.data.objects[f'WeatherHouse_{kind}_LOD{level}']) for kind in KINDS}
        bpy.ops.wm.read_factory_settings(use_empty=True)
        path=OUT/f'WeatherHouse_v05_RoofSparse_LOD{level}.fbx'
        bpy.ops.import_scene.fbx(filepath=str(path),use_image_search=False)
        obs=[o for o in bpy.data.objects if o.type=='MESH']
        assert len(obs)==2 and all(any(kind in o.name for kind in KINDS) for o in obs)
        for kind in KINDS:
            o=bpy.data.objects[f'WeatherHouse_{kind}_LOD{level}'];s=stat(o);exp=expected[(level,kind)]
            err=max(abs(s['matrix'][r][c]-old[kind]['matrix'][r][c]) for r in range(4) for c in range(4))
            assert err<.000001,('matrix',level,kind,err)
            local=[tuple(v.co) for v in o.data.vertices]
            geom=max(max_nearest(local,exp['points']),max_nearest(exp['points'],local))
            assert geom<.00002,(level,kind,geom)
            assert s['triangles']==exp['stats']['triangles']
            actual_uv=np.asarray([tuple(v.uv) for v in o.data.uv_layers['GameUV'].data],dtype=np.float32)
            uv_error=float(np.abs(actual_uv-exp['uv']).max())
            assert uv_error<.000002,(level,kind,uv_error)
            extent_error=max(abs(s['bounds'][name][i]-old[kind]['bounds'][name][i])
                for name in ('localMin','localMax') for i in range(3))
            REPORT['roundtrip'].append(dict(level=level,kind=kind,triangles=s['triangles'],matrixMaxError=err,
                exportedGeometryMaxError=geom,exportedUVMaxError=uv_error,originalBoundsMaxDelta=extent_error,
                bounds=s['bounds'],originalBounds=old[kind]['bounds']))
        print('ROUNDTRIP_PASS',level,flush=True)
    after_hash={str(p):digest(p) for p in immutable}
    assert after_hash==before_hash
    REPORT['sourceHashesUnchanged']=True
    REPORT['texturesRebaked']=False
    REPORT['outputFiles']={p.name:digest(p) for p in OUT.glob('*.fbx')}
    REPORT['status']='PASS'
    REPORT['scope']='LOD0 full component selection with exact kept vertex coordinates and UVs; original .36/.13 LOD decimation and FBX export options; existing textures reused. Unity rendering and acceptance pending.'

try:
    main()
except Exception:
    REPORT['status']='FAIL';REPORT['error']=traceback.format_exc();raise
finally:
    (OUT/'checks.json').write_text(json.dumps(REPORT,ensure_ascii=False,indent=2),encoding='utf8')
    print('RESULT',REPORT['status'],flush=True)
