import bpy, json, math, hashlib, traceback, argparse, sys
from pathlib import Path
from mathutils import Vector, Matrix

parser = argparse.ArgumentParser(description='Enlarge Walk foot travel while preserving geometry, rig, and other actions.')
parser.add_argument('--source', required=True, type=Path)
parser.add_argument('--author-report', required=True, type=Path)
parser.add_argument('--output', required=True, type=Path)
parser.add_argument('--factor', type=float, default=1.25)
args = parser.parse_args(sys.argv[sys.argv.index('--') + 1:])
OUT = args.output
OUT.mkdir(parents=True, exist_ok=True)
SOURCE = args.source
BLEND = OUT / 'ForestSprite_LargerWalk.blend'
FBX = OUT / 'ForestSprite_LargerWalk.fbx'
assert not BLEND.exists() and not FBX.exists(), 'Candidate already exists; do not overwrite.'
bpy.ops.wm.open_mainfile(filepath=str(SOURCE))
scene = bpy.context.scene
rig = bpy.data.objects['ForestSpriteRig']
boots = bpy.data.objects['Body_Boots']
scale = json.loads(args.author_report.read_text(encoding='utf-8'))['metres_per_unit']
rest = {b.name: b.matrix_local.copy() for b in rig.data.bones}
targets = ('Walk',)
changed = {'Hips', 'UpperLeg.L', 'UpperLeg.R', 'LowerLeg.L', 'LowerLeg.R', 'Foot.L', 'Foot.R'}

def digest(value):
    return hashlib.sha256(json.dumps(value, sort_keys=True, separators=(',', ':')).encode()).hexdigest()

def action_data(action, exclude_changed=False):
    curves = []
    for layer in action.layers:
        for strip in layer.strips:
            for bag in strip.channelbags:
                for fc in bag.fcurves:
                    if exclude_changed and any(fc.data_path.startswith('pose.bones["' + n + '"]') for n in changed):
                        continue
                    curves.append((fc.data_path, fc.array_index, fc.extrapolation,
                        [(tuple(k.co), tuple(k.handle_left), tuple(k.handle_right), k.interpolation,
                          k.handle_left_type, k.handle_right_type) for k in fc.keyframe_points]))
    return curves

def meshes():
    return {o.name: digest({'vertices': [tuple(v.co) for v in o.data.vertices],
        'polygons': [tuple(p.vertices) for p in o.data.polygons],
        'weights': [[(g.group, g.weight) for g in v.groups] for v in o.data.vertices],
        'groups': [g.name for g in o.vertex_groups],
        'uvs': [[tuple(v.uv) for v in uv.data] for uv in o.data.uv_layers],
        'matrix': [tuple(row) for row in o.matrix_world]})
        for o in bpy.data.objects if o.type == 'MESH'}

def skeleton():
    return digest([(b.name, b.parent.name if b.parent else None,
        [tuple(row) for row in b.matrix_local], b.length, b.use_connect) for b in rig.data.bones])

def use_action(name):
    action = bpy.data.actions[name]
    rig.animation_data.action = action
    rig.animation_data.action_slot = action.slots[0]

def evaluated_boots():
    ev = boots.evaluated_get(bpy.context.evaluated_depsgraph_get())
    mesh = ev.to_mesh()
    points = [boots.matrix_world @ v.co for v in mesh.vertices]
    ev.to_mesh_clear()
    return points

def setbone(name, head, tail):
    bone = rig.data.bones[name]
    q = (bone.tail_local - bone.head_local).rotation_difference(tail - head)
    rig.pose.bones[name].matrix = Matrix.Translation(head) @ (q @ rest[name].to_quaternion()).to_matrix().to_4x4()
    bpy.context.view_layer.update()

def solve(side, foot_matrix):
    upper = rig.pose.bones['UpperLeg.' + side]
    head = upper.head.copy()
    target = foot_matrix.translation
    l1 = upper.bone.length
    l2 = rig.data.bones['LowerLeg.' + side].length
    delta = target - head
    distance = delta.length
    margin = (l1 + l2 - distance) * scale
    assert distance < l1 + l2 - .001 and distance > abs(l1-l2) + .001, ('Unreachable', side, distance, l1, l2)
    axis = delta.normalized()
    pole = Vector((0, -1, 0))
    perp = (pole - axis * pole.dot(axis)).normalized()
    a = (l1*l1 - l2*l2 + distance*distance) / (2*distance)
    knee = head + axis*a + perp*math.sqrt(max(0, l1*l1 - a*a))
    setbone('UpperLeg.' + side, head, knee)
    setbone('LowerLeg.' + side, knee, target)
    rig.pose.bones['Foot.' + side].matrix = foot_matrix
    bpy.context.view_layer.update()
    return margin

def axis_error(a, b):
    return max(math.degrees(math.atan2(a.col[i].to_3d().cross(b.col[i].to_3d()).length,
        a.col[i].to_3d().dot(b.col[i].to_3d()))) for i in range(3))


use_action('Idle'); scene.frame_set(1)
floor = min(v.z for v in evaluated_boots())
base=[]
use_action('Walk')
for frame in range(1,122):
    scene.frame_set(frame)
    base.append({'hips':rig.pose.bones['Hips'].matrix.copy(),
      'feet':{s:rig.pose.bones['Foot.'+s].matrix.copy() for s in ('L','R')},
      'heads':{s:rig.pose.bones['UpperLeg.'+s].head.copy() for s in ('L','R')}})
center={s:sum(x['feet'][s].translation.y for x in base[:-1])/120 for s in ('L','R')}
required=0
for row in base:
    for s in ('L','R'):
        f=row['feet'][s].translation.copy(); f.y=center[s]+(f.y-center[s])*args.factor
        h=row['heads'][s]
        reach=sum(rig.data.bones[n+'.'+s].length for n in ('UpperLeg','LowerLeg'))-.00101
        horizontal=(h.x-f.x)**2+(h.y-f.y)**2
        if horizontal>=reach*reach: raise ValueError('Horizontal reach exceeded')
        required=max(required,h.z-f.z-math.sqrt(reach*reach-horizontal))
print('PREFLIGHT',json.dumps({'factor':args.factor,'hips_lowering_m':required*scale,'leg_length_m':reach*scale,'foot_center':center}))

report={'result':'RUNNING','factor':args.factor,'source':str(SOURCE), 'source_sha256':hashlib.sha256(SOURCE.read_bytes()).hexdigest(),
 'metres_per_unit':scale,'hips_lowering_m':required*scale,'walk_cycle_distance':.76*args.factor,
 'mesh_before':meshes(),'skeleton_before':skeleton(),
 'action_before':{a.name:digest(action_data(a)) for a in bpy.data.actions},'samples':[]}
try:
    for frame,row in enumerate(base,1):
        scene.frame_set(frame)
        hip=row['hips'].copy();hip.translation.z-=required
        rig.pose.bones['Hips'].matrix=hip; bpy.context.view_layer.update()
        for side in ('L','R'):
            foot=row['feet'][side].copy();foot.translation.y=center[side]+(foot.translation.y-center[side])*args.factor
            solve(side,foot)
        for n in changed:
            pb=rig.pose.bones[n]
            pb.keyframe_insert(data_path='location',frame=frame,group=n)
            if n!='Hips': pb.keyframe_insert(data_path='rotation_quaternion',frame=frame,group=n)
    for layer in bpy.data.actions['Walk'].layers:
        for strip in layer.strips:
            for bag in strip.channelbags:
                for fc in bag.fcurves:
                    if any(fc.data_path.startswith('pose.bones["'+n+'"]') for n in changed):
                        for key in fc.keyframe_points:key.interpolation='LINEAR'
    for frame,row in enumerate(base,1):
        scene.frame_set(frame);points=evaluated_boots()
        errors={}
        for side in ('L','R'):
            target=row['feet'][side].translation.copy(); target.y=center[side]+(target.y-center[side])*args.factor
            foot=rig.pose.bones['Foot.'+side].matrix
            errors[side]={'position_error_m':(target-foot.translation).length*scale,
              'orientation_error_deg':axis_error(row['feet'][side],foot),
              'before_y_m':row['feet'][side].translation.y*scale,'after_y_m':foot.translation.y*scale}
        assert max(e['position_error_m'] for e in errors.values())<1e-5,errors
        assert max(e['orientation_error_deg'] for e in errors.values())<.005,errors
        assert (min(v.z for v in points)-floor)*scale>-.00002,'Sole penetrates floor'
        report['samples'].append({'frame':frame,'phase':(frame-1)/120,'feet':errors,'sole_minimum_m':(min(v.z for v in points)-floor)*scale})
    report['mesh_after']=meshes();report['skeleton_after']=skeleton()
    report['action_after']={a.name:digest(action_data(a)) for a in bpy.data.actions}
    assert report['mesh_before']==report['mesh_after'],'Mesh changed'
    assert report['skeleton_before']==report['skeleton_after'],'Skeleton changed'
    for name,value in report['action_before'].items():
        if name!='Walk':assert value==report['action_after'][name],name
    bpy.ops.object.select_all(action='DESELECT')
    for name in ['Body','Hat','Cape','Hair','Eye_L','Eye_R','Smile','Body_Skirt','Underbody','Body_Boots',rig.name]:
        bpy.data.objects[name].select_set(True)
    bpy.context.view_layer.objects.active=rig
    use_action('Idle');scene.frame_set(1)
    bpy.ops.export_scene.fbx(filepath=str(FBX),use_selection=True,object_types={'ARMATURE','MESH'},axis_forward='-Z',axis_up='Y',
      add_leaf_bones=False,use_armature_deform_only=False,armature_nodetype='NULL',bake_anim=True,bake_anim_use_all_actions=True,
      bake_anim_use_nla_strips=False,bake_anim_simplify_factor=0,path_mode='STRIP',mesh_smooth_type='FACE')
    use_action('Walk');scene.frame_start=1;scene.frame_end=121;scene.frame_set(1)
    bpy.ops.wm.save_as_mainfile(filepath=str(BLEND))
    report['result']='PASS'; report['fbx_sha256']=hashlib.sha256(FBX.read_bytes()).hexdigest()
except Exception:
    report['result']='FAIL';report['error']=traceback.format_exc();raise
finally:
    (OUT/'author-checks.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8')
    print('LARGER_WALK',report['result'])
