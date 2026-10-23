import bpy, json, math, hashlib, traceback, argparse, sys
from pathlib import Path
from mathutils import Vector, Matrix

parser = argparse.ArgumentParser(description='Delay walk loading while preserving the original feet and all meshes.')
parser.add_argument('--source', required=True, type=Path)
parser.add_argument('--author-report', required=True, type=Path)
parser.add_argument('--output', required=True, type=Path)
args = parser.parse_args(sys.argv[sys.argv.index('--') + 1:])
OUT = args.output
OUT.mkdir(parents=True, exist_ok=True)
SOURCE = args.source
BLEND = OUT / '森林小精灵_走路加载候选.blend'
FBX = OUT / 'ForestSprite_WalkLoading.fbx'
assert not BLEND.exists() and not FBX.exists(), 'Candidate already exists; do not overwrite.'
bpy.ops.wm.open_mainfile(filepath=str(SOURCE))
scene = bpy.context.scene
rig = bpy.data.objects['ForestSpriteRig']
boots = bpy.data.objects['Body_Boots']
scale = json.loads(args.author_report.read_text(encoding='utf-8'))['metres_per_unit']
rest = {b.name: b.matrix_local.copy() for b in rig.data.bones}
targets = ('WalkShort', 'Walk')
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

report = {'result': 'RUNNING', 'source': str(SOURCE), 'source_sha256': hashlib.sha256(SOURCE.read_bytes()).hexdigest(),
    'change': 'Only Walk/WalkShort hips loading delay 0.08 cycle; original ankle world transforms retained with two-bone IK.',
    'metres_per_unit': scale, 'source_mesh_hashes': meshes(), 'source_skeleton_hash': skeleton(),
    'source_action_hashes': {a.name: digest(action_data(a)) for a in bpy.data.actions}, 'actions': {}}
try:
    use_action('Idle'); scene.frame_set(1)
    floor = min(v.z for v in evaluated_boots())
    parts = {s: [v.index for v in boots.data.vertices if v.co.x * (1 if s == 'L' else -1) > 0] for s in ('L', 'R')}
    baseline = {}
    for name in targets:
        use_action(name)
        snapshots = []
        for frame in range(1, 122):
            scene.frame_set(frame)
            snapshots.append({'feet': {s: rig.pose.bones['Foot.' + s].matrix.copy() for s in ('L', 'R')},
                'upper_heads': {s: rig.pose.bones['UpperLeg.' + s].head.copy() for s in ('L', 'R')},
                'hips': rig.pose.bones['Hips'].matrix.copy(), 'boots': evaluated_boots(),
                'scale': {b.name: tuple(b.scale) for b in rig.pose.bones}})
        baseline[name] = snapshots
    untouched = {n: digest(action_data(bpy.data.actions[n], True)) for n in targets}
    for name in targets:
        use_action(name)
        minimum_margin = float('inf')
        # Preserve both planted feet and original bone lengths; retain the original
        # 0.001 source-unit IK reach margin with the smallest uniform hips adjustment.
        lowering = 0.0
        limiting_frame = None
        for frame, original in enumerate(baseline[name], 1):
            t = (frame - 1) / 120
            delta = .026 * (math.cos(4*math.pi*t) - math.cos(4*math.pi*(t-.08)))
            for side in ('L', 'R'):
                h = original['upper_heads'][side]; f = original['feet'][side].translation
                reach = sum(rig.data.bones[n+'.'+side].length for n in ('UpperLeg', 'LowerLeg')) - .00101
                vertical = math.sqrt(reach*reach - (h.x-f.x)**2 - (h.y-f.y)**2)
                required = h.z + delta - f.z - vertical
                if required > lowering:
                    lowering = required; limiting_frame = {'frame': frame, 'side': side}
        print('REACH_PREFLIGHT', name, 'lowering_m', lowering*scale, 'limiting', limiting_frame)
        for frame, original in enumerate(baseline[name], 1):
            scene.frame_set(frame)
            t = (frame - 1) / 120
            delta = .026 * (math.cos(4*math.pi*t) - math.cos(4*math.pi*(t-.08)))
            hips_matrix = original['hips'].copy()
            hips_matrix.translation.z += delta - lowering
            rig.pose.bones['Hips'].matrix = hips_matrix
            bpy.context.view_layer.update()
            for side in ('L', 'R'):
                minimum_margin = min(minimum_margin, solve(side, original['feet'][side]))
            for bone_name in changed:
                bone = rig.pose.bones[bone_name]
                bone.keyframe_insert(data_path='location', frame=frame, group=bone_name)
                if bone_name != 'Hips':
                    bone.keyframe_insert(data_path='rotation_quaternion', frame=frame, group=bone_name)
        samples = []
        for frame, original in enumerate(baseline[name], 1):
            scene.frame_set(frame)
            points = evaluated_boots()
            foot_errors = {}
            for side in ('L', 'R'):
                matrix = rig.pose.bones['Foot.'+side].matrix
                old = original['feet'][side]
                foot_errors[side] = {'position_error_m': (matrix.translation-old.translation).length*scale,
                    'orientation_error_deg': axis_error(matrix, old),
                    'sole_height_m': (min(points[i].z for i in parts[side])-floor)*scale}
            pose_scale_error = max(abs(rig.pose.bones[n].scale[i] - old[i]) for n,old in original['scale'].items() for i in range(3))
            length_error = max(abs((rig.pose.bones[n].tail-rig.pose.bones[n].head).length - rig.data.bones[n].length)*scale
                for n in changed if n.startswith(('UpperLeg', 'LowerLeg', 'Foot')))
            shoe_error = max((p-q).length*scale for p,q in zip(points, original['boots']))
            row = {'frame': frame, 'phase': (frame-1)/120, 'hips_before_z_m': original['hips'].translation.z*scale,
                'hips_after_z_m': rig.pose.bones['Hips'].head.z*scale, 'feet': foot_errors,
                'boot_vertex_max_displacement_m': shoe_error, 'bone_length_error_m': length_error,
                'pose_scale_error': pose_scale_error}
            assert max(f['position_error_m'] for f in foot_errors.values()) < 1e-5, row
            assert max(f['orientation_error_deg'] for f in foot_errors.values()) < .005, row
            assert min(f['sole_height_m'] for f in foot_errors.values()) > -1e-5, row
            assert shoe_error < 1e-5 and length_error < 1e-5 and pose_scale_error < 1e-5, row
            samples.append(row)
        minimum_frames = [min(range(a,b), key=lambda i: samples[i]['hips_after_z_m'])+1 for a,b in ((0,60),(60,120))]
        assert minimum_frames == [11, 71], minimum_frames
        report['actions'][name] = {'frames': 121, 'minimum_hips_frames': minimum_frames,
            'uniform_hips_lowering_m': lowering*scale, 'limiting_reach_sample': limiting_frame,
            'minimum_ik_reach_margin_m': minimum_margin, 'samples': samples,
            'maximum_foot_position_error_m': max(v['position_error_m'] for r in samples for v in r['feet'].values()),
            'maximum_foot_orientation_error_deg': max(v['orientation_error_deg'] for r in samples for v in r['feet'].values()),
            'maximum_boot_vertex_displacement_m': max(r['boot_vertex_max_displacement_m'] for r in samples),
            'minimum_sole_height_m': min(v['sole_height_m'] for r in samples for v in r['feet'].values()),
            'maximum_bone_length_error_m': max(r['bone_length_error_m'] for r in samples)}
        assert untouched[name] == digest(action_data(bpy.data.actions[name], True)), name
    report['candidate_mesh_hashes'] = meshes()
    report['candidate_skeleton_hash'] = skeleton()
    report['candidate_action_hashes'] = {a.name: digest(action_data(a)) for a in bpy.data.actions}
    assert report['source_mesh_hashes'] == report['candidate_mesh_hashes'], 'Mesh changed'
    assert report['source_skeleton_hash'] == report['candidate_skeleton_hash'], 'Skeleton changed'
    for name in report['source_action_hashes']:
        if name not in targets:
            assert report['source_action_hashes'][name] == report['candidate_action_hashes'][name], name
    assert hashlib.sha256(SOURCE.read_bytes()).hexdigest() == report['source_sha256'], 'Original file changed'
    bpy.ops.object.select_all(action='DESELECT')
    for name in ['Body','Hat','Cape','Hair','Eye_L','Eye_R','Smile','Body_Skirt','Underbody','Body_Boots',rig.name]:
        bpy.data.objects[name].select_set(True)
    bpy.context.view_layer.objects.active = rig
    use_action('Idle'); scene.frame_set(1)
    bpy.ops.export_scene.fbx(filepath=str(FBX), use_selection=True, object_types={'ARMATURE','MESH'},
        axis_forward='-Z', axis_up='Y', add_leaf_bones=False, use_armature_deform_only=False,
        armature_nodetype='NULL', bake_anim=True, bake_anim_use_all_actions=True,
        bake_anim_use_nla_strips=False, bake_anim_simplify_factor=0, path_mode='STRIP', mesh_smooth_type='FACE')
    use_action('Walk'); scene.frame_start=1; scene.frame_end=121; scene.frame_set(11)
    bpy.ops.wm.save_as_mainfile(filepath=str(BLEND))
    report['result'] = 'PASS'
    report['blend'] = str(BLEND); report['fbx'] = str(FBX)
    report['blend_sha256'] = hashlib.sha256(BLEND.read_bytes()).hexdigest()
    report['fbx_sha256'] = hashlib.sha256(FBX.read_bytes()).hexdigest()
except Exception:
    report['result'] = 'FAIL'; report['error'] = traceback.format_exc()
    raise
finally:
    (OUT/'逐帧验证.json').write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding='utf-8')
    print('WALK_LOADING_RESULT', report['result'])
    print(json.dumps({n:{k:v for k,v in a.items() if k!='samples'} for n,a in report['actions'].items()}, ensure_ascii=False))
