import bpy, math, json, shutil, hashlib
import numpy as np
from pathlib import Path
from mathutils import Vector, Matrix, Quaternion

S=Path(__file__).parent; OUT=S/'candidate'; P=S/'preview'; P.mkdir(exist_ok=True)
original=OUT/'Squirrel_OGA_Natural_Unity.fbx'
native=OUT/'Squirrel_OGA_Natural_NativeOnly.fbx'
if not native.exists():shutil.copy2(original,native)
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=str(native))
scene=bpy.context.scene;scene.render.fps=24
rig=next(o for o in bpy.data.objects if o.type=='ARMATURE')
meshes=[o for o in bpy.data.objects if o.type=='MESH']
head=next(o for o in meshes if o.name.startswith('SquirrelHeadNaturalMesh'))
root=next(o for o in bpy.data.objects if o.type=='EMPTY')
native_actions={a.name.split('|')[-1]:a for a in bpy.data.actions}
for name,a in native_actions.items():a.name=name;a.use_fake_user=True
for b in rig.pose.bones:b.rotation_mode='QUATERNION'

def activate(a):
 rig.animation_data_create();rig.animation_data.action=a
 if a and len(a.slots):rig.animation_data.action_slot=a.slots[0]
 for t in rig.animation_data.nla_tracks:t.mute=True
def frame(f):
 scene.frame_set(math.floor(f),subframe=f%1);bpy.context.view_layer.update()
def pose():return {b.name:b.matrix_basis.copy() for b in rig.pose.bones}
def apply(p):
 for b in rig.pose.bones:b.matrix_basis=p[b.name]
 bpy.context.view_layer.update()
def evaluated(o):
 e=o.evaluated_get(bpy.context.evaluated_depsgraph_get());m=e.to_mesh()
 a=np.array([e.matrix_world@v.co for v in m.vertices]);e.to_mesh_clear();return a
def points():return np.concatenate([evaluated(o) for o in meshes])
def shift_world(name,delta):
 b=rig.pose.bones[name];m=b.matrix.copy();m.translation+=rig.matrix_world.inverted().to_3x3()@Vector(delta);b.matrix=m;bpy.context.view_layer.update()
def rotate_world(name,angle):
 b=rig.pose.bones[name];m=rig.matrix_world@b.matrix;p=m.translation.copy()
 m=Quaternion((1,0,0),math.radians(angle)).to_matrix().to_4x4()@m;m.translation=p
 b.matrix=rig.matrix_world.inverted()@m;bpy.context.view_layer.update()
def ground(z=.002):
 low=float(points()[:,2].min());shift_world('Main',(0,0,z-low));return low
def mix(a,b,t):
 result={}
 for name in a:
  la,qa,sa=a[name].decompose();lb,qb,sb=b[name].decompose()
  result[name]=Matrix.LocRotScale(la.lerp(lb,t),qa.slerp(qb,t),sa.lerp(sb,t))
 return result
def smooth(t):t=max(0,min(1,t));return t*t*(3-2*t)

# A pure surface eyelid control; the 34-bone hierarchy remains unchanged.
head.shape_key_add(name='Basis');closed=head.shape_key_add(name='EyesClosed')
eye_ids=[]
black_slot=next(i for i,m in enumerate(head.data.materials) if 'Eyes Nose' in m.name)
black_ids={vi for face in head.data.polygons if face.material_index==black_slot for vi in face.vertices}
for v in head.data.vertices:
 if v.index in black_ids and abs(v.co.x)>.18 and v.co.z>1.94 and v.co.y<-1.40:
  closed.data[v.index].co.z=2.06+(v.co.z-2.06)*.09;eye_ids.append(v.index)
assert 200<len(eye_ids)<350,len(eye_ids)
closed.value=0

# Capture source poses before creating new, constraint-free FK actions.
activate(native_actions['Idle.000']);frame(1);idle=pose();native_idle_points=points()
activate(native_actions['Run']);run_poses={}
for f in np.linspace(1,10,37):frame(float(f));run_poses[float(f)]=pose()
frame(1);quad=pose()
activate(None)
apply(quad);rotate_world('Head',18);ground();sleep=pose()

muzzle_slot=next(i for i,m in enumerate(head.data.materials) if 'Muzzle' in m.name)
muzzle_ids={vi for f in head.data.polygons if f.material_index==muzzle_slot for vi in f.vertices}
lip_ids=[i for i in muzzle_ids if abs(head.data.vertices[i].co.x)<.14 and head.data.vertices[i].co.z<1.78]
lip_id=min(lip_ids,key=lambda i:head.data.vertices[i].co.y)

# Choose a modest crouch/head bend that puts the visible muzzle at bowl height.
# The exact Unity point is measured again by the installer, never copied from the old rig.
search=[]
for neck_deg in [0,10,20,30,40]:
 for head_deg in [0,10,20,30,40,50,60]:
  apply(quad);rotate_world('Neck',neck_deg);rotate_world('Head',head_deg);ground()
  lip=evaluated(head)[lip_id]
  score=abs(lip[2]-.105)+(neck_deg+head_deg)*.0001
  search.append((score,neck_deg,head_deg,lip.tolist(),pose()))
best=min(search,key=lambda x:x[0]);drink_pose=best[-1]
assert abs(best[3][2]-.105)<.025,best[:4]

derived=[];clip_samples={}
def record_action(name,end,make_pose,loop):
 a=bpy.data.actions.new(name);a.use_fake_user=True;activate(a);samples=[]
 for f in range(end+1):
  frame(f);make_pose(f,end)
  for b in rig.pose.bones:
   b.keyframe_insert('location',frame=f,group=b.name);b.keyframe_insert('rotation_quaternion',frame=f,group=b.name);b.keyframe_insert('scale',frame=f,group=b.name)
  p=points();assert np.isfinite(p).all();samples.append(p)
 # Linear sampled interpolation avoids automatic overshoot through the floor.
 for slot in a.slots:
  for layer in a.layers:
   for strip in layer.strips:
    bag=strip.channelbag(slot)
    if bag:
     for fc in bag.fcurves:
      for k in fc.keyframe_points:k.interpolation='LINEAR'
 row={'name':name,'frames':[0,end],'durationSeconds':end/24,'kind':'derived same OGA 34-bone FK rig','loop':loop,'lowestZRange':[min(float(p[:,2].min()) for p in samples),max(float(p[:,2].min()) for p in samples)],'endpointMaxVertexGap':float(np.linalg.norm(samples[-1]-samples[0],axis=1).max())}
 derived.append(row);clip_samples[name]=samples;return a

def down(f,end):apply(mix(idle,sleep,smooth(f/end)));ground()
def up(f,end):apply(mix(sleep,idle,smooth(f/end)));ground()
def sleeping(f,end):
 apply(sleep);breath=math.sin(2*math.pi*f/end)*.65;rotate_world('Backbone.001',breath);ground()
def drinking(f,end):
 t=f/24
 if t<.75:apply(mix(idle,drink_pose,smooth(t/.75)))
 elif t<=2.30:apply(drink_pose)
 else:apply(mix(drink_pose,idle,smooth((t-2.30)/(.70))))
 ground()
record_action('RestDown_Derived',24,down,False)
record_action('RestLoop_Derived',48,sleeping,True)
record_action('RestUp_Derived',24,up,False)
record_action('Drink_Derived',72,drinking,False)

# Slower locomotion is explicitly a retimed Run, not an independent authored walk.
# A constant vertical lift preserves the native run's airborne phase.
native_run_points=[]
activate(native_actions['Run'])
for f in np.linspace(1,10,73):frame(float(f));native_run_points.append(points())
run_lift=max(0,.002-min(float(p[:,2].min()) for p in native_run_points))
activate(None)
def run_sample(f,end):
 source_f=1+9*f/end
 base=math.floor((source_f-1)*4)/4+1;upper=min(10,base+.25)
 if base>=10:apply(run_poses[10.0])
 else:apply(mix(run_poses[float(base)],run_poses[float(upper)],(source_f-base)/.25))
 shift_world('Main',(0,0,run_lift))
record_action('RunSlow_Derived',18,run_sample,True)
record_action('RunGrounded_Derived',9,run_sample,True)

# Transitions share exactly the same floor-aligned poses.
seams={
 'down_to_sleep':float(np.linalg.norm(clip_samples['RestDown_Derived'][-1]-clip_samples['RestLoop_Derived'][0],axis=1).max()),
 'sleep_to_up':float(np.linalg.norm(clip_samples['RestLoop_Derived'][-1]-clip_samples['RestUp_Derived'][0],axis=1).max()),
 'idle_to_down':float(np.linalg.norm(clip_samples['RestDown_Derived'][0]-clip_samples['RestUp_Derived'][-1],axis=1).max())}
assert max(seams.values())<2e-5,seams
assert min(row['lowestZRange'][0] for row in derived)>-.0001
activate(native_actions['Idle.000']);frame(1)
native_preservation=float(np.linalg.norm(points()-native_idle_points,axis=1).max());assert native_preservation<1e-5
if hasattr(bpy.context.preferences.filepaths,'file_preview_type'):bpy.context.preferences.filepaths.file_preview_type='NONE'
bpy.context.preferences.filepaths.save_version=0
scene.unit_settings.system='METRIC';scene.unit_settings.scale_length=1
scene.frame_start=0;scene.frame_end=72
bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'Squirrel_OGA_LifeDerived_Source.blend'))
bpy.ops.object.select_all(action='DESELECT')
for o in meshes+[rig,root]:o.select_set(True)
bpy.context.view_layer.objects.active=rig
bpy.ops.export_scene.fbx(filepath=str(original),use_selection=True,object_types={'ARMATURE','MESH','EMPTY'},global_scale=1,apply_unit_scale=True,apply_scale_options='FBX_SCALE_UNITS',axis_forward='-Z',axis_up='Y',bake_space_transform=False,add_leaf_bones=False,use_armature_deform_only=False,bake_anim=True,bake_anim_use_all_actions=True,bake_anim_use_nla_strips=False,bake_anim_simplify_factor=0,bake_anim_step=1,path_mode='AUTO',embed_textures=False)

# Quick actual renderer stills for posture review; no Unity proof is implied.
bpy.ops.mesh.primitive_plane_add(size=20,location=(0,0,0));floor=bpy.context.object
mat=bpy.data.materials.new('Derived preview ground');mat.diffuse_color=(.54,.60,.55,1);floor.data.materials.append(mat)
bpy.ops.object.camera_add(location=(1.35,-1.7,.95));cam=bpy.context.object
cam.rotation_euler=(Vector((0,0,.28))-cam.location).to_track_quat('-Z','Y').to_euler();cam.data.type='ORTHO';cam.data.ortho_scale=1.45;scene.camera=cam
scene.render.engine='BLENDER_WORKBENCH';scene.display.shading.light='STUDIO';scene.display.shading.color_type='MATERIAL';scene.display.shading.background_type='WORLD';scene.display.shading.show_shadows=True;scene.display.shading.show_cavity=False;scene.display.shading.show_specular_highlight=False
if not scene.world:scene.world=bpy.data.worlds.new('Derived preview world')
scene.world.color=(.62,.68,.65);scene.render.resolution_x=900;scene.render.resolution_y=700;scene.render.resolution_percentage=100;scene.render.image_settings.file_format='PNG';scene.render.film_transparent=False
for name,f,closed_value in [('RestDown_Derived',12,.5),('RestLoop_Derived',0,1),('RestUp_Derived',12,.5),('Drink_Derived',34,0),('RunSlow_Derived',0,0)]:
 activate(bpy.data.actions[name]);frame(f);closed.value=closed_value;scene.render.filepath=str(P/(name+'.png'));bpy.ops.render.render(write_still=True)
closed.value=0
report={'status':'DERIVED_OFFLINE_COMPLETE_REQUIRES_VISUAL_REVIEW_AND_UNITY','sourceFbxSha256':hashlib.sha256(native.read_bytes()).hexdigest(),'outputFbxSha256':hashlib.sha256(original.read_bytes()).hexdigest(),'nativeActionNames':sorted(native_actions),'derived':derived,'nativeIdleMaxVertexDeltaBeforeAfterMetres':native_preservation,'restSeamsMetres':seams,'eyeClosedVertexCount':len(eye_ids),'headVertexCount':len(head.data.vertices),'drinkPoseSearch':{'targetLipHeightMetres':.105,'neckPitchDegrees':best[1],'headPitchDegrees':best[2],'visibleLipWorld':best[3],'muzzleVertexId':lip_id},'runVerticalConstantLiftMetres':run_lift,'boundary':'All additional motions are derived FK clips, not author-provided Walk/Sleep/Drink. RunSlow is retimed Run; RunGrounded adds a constant vertical lift and preserves jump phases. Main X/Y is unchanged; vertical Main correction is skeletal grounding, not gameplay root motion. Runtime ApplyRootMotion must remain false. Unity importer, scene scale, bowl contact and visible appearance require explicit runtime validation.'}
(S/'derived-life-clips.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
print('DERIVED_LIFE_READY',json.dumps({k:v for k,v in report.items() if k in ['outputFbxSha256','restSeamsMetres','drinkPoseSearch','runVerticalConstantLiftMetres']}))
