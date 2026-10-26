"""Original in-place life actions on the unchanged, existing 27-bone squirrel rig."""
import bpy
import math
import json
import numpy as np
from pathlib import Path
from mathutils import Vector, Matrix, Quaternion

ROOT=Path(__file__).resolve().parents[2]
OUT=ROOT/'Assets/Game/Life/Art/Resources/LifeAssets/Animations'
SRC=ROOT/'Tools/LifeAssets/Source/Animations'
REVIEW=ROOT/'Docs/LifeGameplay/Assets/Animations'
for p in (OUT,SRC,REVIEW):p.mkdir(parents=True,exist_ok=True)
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.context.preferences.filepaths.save_version=0
bpy.ops.import_scene.fbx(filepath=str(ROOT/'Assets/Experiments/Squirrel/Models/turn.fbx'))
scene=bpy.context.scene;scene.frame_set(1)
rig=next(o for o in scene.objects if o.type=='ARMATURE')
mesh=next(o for o in scene.objects if o.type=='MESH')
source_basis={b.name:b.matrix_basis.copy() for b in rig.pose.bones}
source_hierarchy=[(b.name,b.parent.name if b.parent else '') for b in rig.data.bones]

def update():bpy.context.view_layer.update()

def points():
    graph=bpy.context.evaluated_depsgraph_get();e=mesh.evaluated_get(graph);m=e.to_mesh()
    a=np.empty(len(m.vertices)*3);m.vertices.foreach_get('co',a);a=a.reshape(-1,3)
    transform=np.array(rig.matrix_world.inverted()@e.matrix_world)
    result=a@transform[:3,:3].T+transform[:3,3]
    e.to_mesh_clear();return result

source_points=points();floor=float(source_points[:,1].min())
heading=rig.pose.bones['Head'].head-rig.pose.bones['Pelvis'].head
yaw=math.atan2(heading.x,heading.z);yawq=Quaternion((0,1,0),yaw)
rig.animation_data_clear()
for b in rig.pose.bones:b.matrix_basis=Matrix.Identity(4)
update()
neutral_points=points()
neutral_floor=float(neutral_points[:,1].min())
floor_shift=floor-neutral_floor
rest={b.name:b.matrix.copy() for b in rig.pose.bones}
head_group=mesh.vertex_groups['Head'].index
head_ids=[v.index for v in mesh.data.vertices if any(g.group==head_group and g.weight>.75 for g in v.groups) and abs(neutral_points[v.index,0])<.07]
mouth_ids=sorted(head_ids,key=lambda i:neutral_points[i,2],reverse=True)[:12]
assert mouth_ids
foot_ids={}
for side in ['L','R']:
    gid=mesh.vertex_groups['Foot.'+side].index
    foot_ids[side]=[v.index for v in mesh.data.vertices if any(g.group==gid and g.weight>.8 for g in v.groups)]
    assert foot_ids[side]

def smooth(t):
    t=max(0,min(1,t));return t*t*(3-2*t)

def rotate(name,degrees,axis=None):
    b=rig.pose.bones[name];q=Quaternion(axis or (yawq@Vector((1,0,0))),math.radians(degrees))
    b.matrix=Matrix.LocRotScale(b.head,q@b.matrix.to_quaternion(),Vector((1,1,1)));update()

def aim(name,direction,current_direction):
    b=rig.pose.bones[name]
    q=current_direction.normalized().rotation_difference(direction.normalized())
    b.matrix=Matrix.LocRotScale(b.head,q@b.matrix.to_quaternion(),Vector((1,1,1)));update()

reach_errors=[]
ground_corrections=[]
def correct_floor():
    lowest=float(points()[:,1].min())
    correction=max(0,floor-lowest)
    ground_corrections.append(correction)
    if correction:
        b=rig.pose.bones['Pelvis'];m=b.matrix.copy();m.translation.y+=correction;b.matrix=m;update()

def solve_limb(upper,middle,end,target,pole,end_quat):
    a=rig.pose.bones[upper];b=rig.pose.bones[middle];c=rig.pose.bones[end]
    origin=a.head.copy();l1=(b.head-origin).length;l2=(c.head-b.head).length
    delta=target-origin;dist=delta.length
    reach_errors.append(max(0,dist-l1-l2))
    distance=max(abs(l1-l2)+1e-5,min(dist,l1+l2-1e-5))
    axis=delta.normalized();pole=pole-axis*pole.dot(axis)
    if pole.length<1e-5:pole=Vector((0,1,0))-axis*axis.y
    pole.normalize();x=(l1*l1-l2*l2+distance*distance)/(2*distance)
    knee=origin+axis*x+pole*math.sqrt(max(0,l1*l1-x*x))
    goal=origin+axis*distance
    aim(upper,knee-origin,b.head-origin)
    b=rig.pose.bones[middle];c=rig.pose.bones[end]
    aim(middle,goal-b.head,c.head-b.head)
    c=rig.pose.bones[end]
    c.matrix=Matrix.LocRotScale(c.head,end_quat,Vector((1,1,1)));update()

def authored(kind,phase=0):
    for b in rig.pose.bones:b.matrix_basis=Matrix.Identity(4)
    update()
    breathing=.0025*(1-math.cos(math.tau*phase)) if kind in ('rest','neutral') else 0
    root=rig.pose.bones['Pelvis']
    root.matrix=Matrix.Translation(Vector((0,floor_shift+breathing,0)))@yawq.to_matrix().to_4x4()@rest['Pelvis']
    update()
    if kind in ('drink','rest'):
        # Fold the trunk above a seated pelvis, then place the front paws for support.
        rotate('Pelvis',8 if kind=='rest' else 5)
        rotate('Spine',28 if kind=='rest' else 20)
        rotate('Chest',18 if kind=='rest' else 16)
        rotate('Neck',10 if kind=='rest' else 8)
        rotate('Head',15 if kind=='rest' else 9+1.4*math.sin(phase*math.tau*3))
        rotate('TailRoot',-24)
        for name in ['Tail02','Tail03','Tail04']:
            rotate(name,-3)
    elif kind=='accept':
        rotate('Spine',4)
        rotate('Head',4+4*math.sin(phase*math.pi))
    for side,sign in [('L',1),('R',-1)]:
        # The hind soles remain on the same source-model floor throughout each hold.
        target=yawq@rest['Foot.'+side].translation+Vector((0,floor_shift,0))
        target.y-=float(neutral_points[foot_ids[side],1].min())-neutral_floor
        solve_limb('Thigh.'+side,'Shin.'+side,'Foot.'+side,target,yawq@Vector((sign,.2,.35)),yawq@rest['Foot.'+side].to_quaternion())
        if kind in ('rest','drink'):
            shoulder=rig.pose.bones['UpperArm.'+side].head
            forward=yawq@Vector((0,0,1));lateral=yawq@Vector((1,0,0))
            target=shoulder+forward*.115+Vector((0,-.16,0))+lateral*(sign*.015)
            solve_limb('UpperArm.'+side,'Forearm.'+side,'Hand.'+side,target,yawq@Vector((sign,-.3,0)),yawq@rest['Hand.'+side].to_quaternion())
        elif kind=='accept':
            shoulder=rig.pose.bones['UpperArm.'+side].head
            target=shoulder+(yawq@Vector((-sign*.025,-.035,.19)))
            solve_limb('UpperArm.'+side,'Forearm.'+side,'Hand.'+side,target,yawq@Vector((sign,.1,0)),yawq@rest['Hand.'+side].to_quaternion())
    update()
    correct_floor()
    return {b.name:b.matrix_basis.copy() for b in rig.pose.bones}

def apply_basis(pose):
    for b in rig.pose.bones:b.matrix_basis=pose[b.name]
    update()

def mix(a,b,t):
    result={}
    for name in a:
        pa,qa,sa=a[name].decompose();pb,qb,sb=b[name].decompose()
        result[name]=Matrix.LocRotScale(pa.lerp(pb,t),qa.slerp(qb,t),sa.lerp(sb,t))
    return result

def render(path):
    a=points();world=[rig.matrix_world@Vector(v) for v in a]
    lo=Vector([min(p[i] for p in world) for i in range(3)]);hi=Vector([max(p[i] for p in world) for i in range(3)])
    center=(lo+hi)/2;size=max(hi-lo)
    helpers=[]
    bpy.ops.mesh.primitive_plane_add(size=200,location=(0,0,floor-.004));helpers.append(bpy.context.object)
    m=bpy.data.materials.new('PreviewFloor');m.diffuse_color=(.20,.25,.21,1);helpers[-1].data.materials.append(m)
    direction=rig.matrix_world.to_3x3()@(yawq@Vector((1.8,1.7,3)))
    bpy.ops.object.camera_add(location=center+direction.normalized()*size*4)
    cam=bpy.context.object;helpers.append(cam);cam.rotation_euler=(center-cam.location).to_track_quat('-Z','Y').to_euler();cam.data.type='ORTHO';cam.data.ortho_scale=size*1.5;scene.camera=cam
    for loc,energy in [((1,-3,4),500),((-3,-1,2),400)]:
        bpy.ops.object.light_add(type='AREA',location=loc);o=bpy.context.object;helpers.append(o);o.data.energy=energy;o.data.size=4;o.rotation_euler=(center-o.location).to_track_quat('-Z','Y').to_euler()
    scene.world=bpy.data.worlds.new('PreviewWorld');scene.world.color=(.3,.3,.3)
    scene.render.engine='CYCLES';scene.cycles.samples=20;scene.cycles.use_denoising=True
    scene.render.resolution_x=960;scene.render.resolution_y=800;scene.render.resolution_percentage=100;scene.render.filepath=str(path)
    bpy.ops.render.render(write_still=True)
    for o in helpers:bpy.data.objects.remove(o,do_unlink=True)

neutral=authored('neutral')
rest_pose=authored('rest')
drink_pose=authored('drink')
accept_pose=authored('accept',.5)
for name,pose in [('SquirrelRestPose',rest_pose),('SquirrelDrinkPose',drink_pose),('SquirrelAcceptPose',accept_pose)]:
    apply_basis(pose);render(REVIEW/(name+'.png'))

specs=[('SquirrelIdle',3.2,True),('SquirrelRestDown',2.0,False),('SquirrelRestLoop',3.2,True),('SquirrelRestUp',2.2,False),
       ('SquirrelDrink',3.0,False),('SquirrelAccept',2.0,False)]
report={'source':'Assets/Experiments/Squirrel/Models/turn.fbx','boneCount':len(source_hierarchy),'sourceFloorY':floor,'neutralFloorY':neutral_floor,'headingRadians':yaw,'clips':[]}
scene.render.fps=30
for name,duration,loop in specs:
    rig.animation_data_clear()
    for action in list(bpy.data.actions):bpy.data.actions.remove(action)
    rig.animation_data_create();action=bpy.data.actions.new(name);rig.animation_data.action=action
    frame_count=round(duration*30);scene.frame_start=0;scene.frame_end=frame_count
    samples=[]
    for frame in range(frame_count+1):
        scene.frame_set(frame);t=frame/frame_count
        if name=='SquirrelIdle':
            pose=authored('neutral',t)
        elif name=='SquirrelRestDown':
            pose=mix(neutral,rest_pose,smooth(t))
        elif name=='SquirrelRestLoop':
            pose=authored('rest',t)
        elif name=='SquirrelRestUp':
            # Head and ears lift first; the trunk and legs follow after a short supported pause.
            pose=mix(rest_pose,neutral,smooth((t-.18)/.82))
            for bone in ['Head','Neck','Ear.L','Ear.R']:
                pose[bone]=mix(rest_pose,neutral,smooth(t/.66))[bone]
        elif name=='SquirrelDrink':
            hold=authored('drink',t)
            weight=smooth(t/.23) if t<.23 else 1-smooth((t-.75)/.25) if t>.75 else 1
            pose=mix(neutral,hold,weight)
        else:
            hold=authored('accept',t)
            weight=smooth(t/.28) if t<.28 else 1-smooth((t-.7)/.3) if t>.7 else 1
            pose=mix(neutral,hold,weight)
        apply_basis(pose)
        correct_floor()
        a=points()
        assert np.isfinite(a).all()
        samples.append({'frame':frame,'minY':float(a[:,1].min()),'maxY':float(a[:,1].max()),'mouth':list(a[mouth_ids].mean(0)),'feet':{s:float(a[ids,1].min()) for s,ids in foot_ids.items()}})
        for b in rig.pose.bones:
            b.rotation_mode='QUATERNION'
            b.keyframe_insert('location',frame=frame);b.keyframe_insert('rotation_quaternion',frame=frame);b.keyframe_insert('scale',frame=frame)
    scene.frame_set(0)
    bpy.ops.file.pack_all()
    bpy.ops.wm.save_as_mainfile(filepath=str(SRC/(name+'.blend')))
    # FBX without its unchanged skin loses the explicit bind pose on reimport.
    # Keep that skin as bind-pose evidence; runtime loads only the AnimationClip.
    bpy.ops.object.select_all(action='DESELECT');rig.select_set(True);mesh.select_set(True);bpy.context.view_layer.objects.active=rig
    bpy.ops.export_scene.fbx(filepath=str(OUT/(name+'.fbx')),use_selection=True,object_types={'ARMATURE','MESH'},
        apply_scale_options='FBX_SCALE_ALL',axis_forward='-Z',axis_up='Y',bake_space_transform=False,add_leaf_bones=False,
        bake_anim=True,bake_anim_use_all_actions=False,bake_anim_use_nla_strips=False,bake_anim_simplify_factor=0)
    item={'id':name,'durationSeconds':duration,'fps':30,'loop':loop,'frameCount':frame_count+1,'samples':samples,
          'minimumClearanceFromSourceFloor':min(s['minY']-floor for s in samples),
          'maximumLimbReachExcess':max(reach_errors) if reach_errors else 0}
    item['maximumGroundCorrection']=max(ground_corrections)
    report['clips'].append(item)
(REVIEW/'squirrel-animation-manifest.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
print('SQUIRREL_ACTIONS_EXPORTED',len(report['clips']))
