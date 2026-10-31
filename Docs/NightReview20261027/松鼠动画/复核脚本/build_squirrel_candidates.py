"""Isolated squirrel locomotion candidates and Blender-only previews. Never edits Unity Assets."""
import bpy,json,math,hashlib,subprocess
import numpy as np
from pathlib import Path
from mathutils import Vector,Matrix,Quaternion
ROOT=Path(r'C:\unity project\Weather House')
OUT=Path(r'C:\Users\31483\Desktop\天气屋\夜间完善_20261027\松鼠动画')
STAGE=Path(r'C:\Users\31483\.codex\visualizations\2026\10\26\01a178cf-479b-7f41-af26-309b1776f6d5\squirrel-staging')
FFMPEG=Path(r'C:\Users\31483\Desktop\天气屋\3d资产\松鼠骨骼与UniMate实验_20261022\runtime\.venv\Lib\site-packages\imageio_ffmpeg\binaries\ffmpeg-win-x86_64-v7.1.exe')
CAND=OUT/'原模型候选';CAND.mkdir(exist_ok=True)
PREVIEW=OUT/'预览';PREVIEW.mkdir(exist_ok=True)
TARGET_FLOOR=-.4175209426637803
reports=[]

def mesh_points(mesh):
 graph=bpy.context.evaluated_depsgraph_get();e=mesh.evaluated_get(graph);m=e.to_mesh()
 a=np.empty(len(m.vertices)*3);m.vertices.foreach_get('co',a);a=a.reshape(-1,3)
 transform=np.array(e.matrix_world);res=a@transform[:3,:3].T+transform[:3,3];e.to_mesh_clear();return res

def raw_signature(mesh):
 return hashlib.sha256(b''.join(np.asarray([list(v.co) for v in mesh.data.vertices],dtype='<f8').tobytes() for mesh in [mesh])).hexdigest()

def assign(rig,act):
 rig.animation_data_create();rig.animation_data.action=act
 if len(act.slots):rig.animation_data.action_slot=act.slots[0]
 for track in rig.animation_data.nla_tracks:track.mute=True

def load(path,action_name=None):
 bpy.ops.wm.read_factory_settings(use_empty=True)
 if path.suffix=='.blend':bpy.ops.wm.open_mainfile(filepath=str(path))
 elif path.suffix=='.fbx':bpy.ops.import_scene.fbx(filepath=str(path))
 else:bpy.ops.import_scene.gltf(filepath=str(path))
 rig=next(o for o in bpy.data.objects if o.type=='ARMATURE')
 meshes=[o for o in bpy.data.objects if o.type=='MESH' and any(m.type=='ARMATURE' for m in o.modifiers)]
 for o in bpy.data.objects:
  if o.type=='MESH' and o not in meshes:o.hide_render=True
 action=bpy.data.actions.get(action_name) if action_name else next(a for a in bpy.data.actions if a.frame_range[1]>a.frame_range[0])
 assign(rig,action);bpy.context.scene.frame_set(round(action.frame_range[0]));return rig,meshes,action

for label in ['walk','run']:
 path=ROOT/f'Assets/Experiments/Squirrel/Models/{label}_biped.fbx'
 rig,meshes,action=load(path);mesh=max(meshes,key=lambda o:len(o.data.vertices));scene=bpy.context.scene
 first,last=map(round,action.frame_range);poses=[];source_floor=[]
 for frame in range(first,last+1):
  scene.frame_set(frame);bpy.context.view_layer.update()
  poses.append({b.name:b.matrix_basis.copy() for b in rig.pose.bones});source_floor.append(float(mesh_points(mesh)[:,2].min()))
 signature=raw_signature(mesh);old_action=action;rig.animation_data_clear();action=bpy.data.actions.new('Squirrel_'+label+'_GroundedLoop_Candidate');assign(rig,action)
 result=[];prepared=[];prepared_floors=[]
 blend_frames=8
 for index,frame in enumerate(range(first,last+1)):
  blend=max(0,(index-(last-first-blend_frames))/blend_frames);blend=blend*blend*(3-2*blend)
  for b in rig.pose.bones:
   loc,rot,scale=poses[index][b.name].decompose();target_loc,target_rot,target_scale=poses[0][b.name].decompose()
   b.matrix_basis=Matrix.LocRotScale(loc.lerp(target_loc,blend),rot.slerp(target_rot,blend),scale.lerp(target_scale,blend));b.rotation_mode='QUATERNION'
  bpy.context.view_layer.update();prepared_floors.append(float(mesh_points(mesh)[:,2].min()));prepared.append({b.name:b.matrix_basis.copy() for b in rig.pose.bones})
 delta=TARGET_FLOOR-min(prepared_floors)
 for index,frame in enumerate(range(first,last+1)):
  for b in rig.pose.bones:b.matrix_basis=prepared[index][b.name]
  bpy.context.view_layer.update()
  pelvis=rig.pose.bones['Pelvis'];m=pelvis.matrix.copy();m.translation+=rig.matrix_world.inverted().to_3x3()@Vector((0,0,delta));pelvis.matrix=m
  bpy.context.view_layer.update();points=mesh_points(mesh);result.append(points)
  for b in rig.pose.bones:
   b.keyframe_insert('location',frame=frame);b.keyframe_insert('rotation_quaternion',frame=frame);b.keyframe_insert('scale',frame=frame)
 scene.frame_start=first;scene.frame_end=last;scene.render.fps=30;scene.frame_set(first)
 bpy.ops.file.pack_all();bpy.context.preferences.filepaths.save_version=0
 blendfile=CAND/f'Squirrel_{label}_GroundedLoop_Candidate.blend';bpy.ops.wm.save_as_mainfile(filepath=str(blendfile))
 bpy.ops.object.select_all(action='DESELECT');rig.select_set(True)
 for m in meshes:m.select_set(True)
 bpy.context.view_layer.objects.active=rig
 fbx=CAND/f'Squirrel_{label}_GroundedLoop_Candidate.fbx'
 bpy.ops.export_scene.fbx(filepath=str(fbx),use_selection=True,object_types={'ARMATURE','MESH'},global_scale=1,apply_unit_scale=True,apply_scale_options='FBX_SCALE_NONE',axis_forward='-Z',axis_up='Y',bake_space_transform=False,add_leaf_bones=False,bake_anim=True,bake_anim_use_all_actions=False,bake_anim_use_nla_strips=False,bake_anim_simplify_factor=0)
 row={'id':label,'source':str(path),'candidate':str(fbx),'scope':'Original rig/skin, one constant root height alignment preserving authored bounce/air time, and last 8 source frames blended to first pose. Source-derived edited motion, no new native turn or locomotion source.','bones':len(rig.data.bones),'sourceLowestRange':[min(source_floor),max(source_floor)],'sourcePrefabFloorGapRange':[v*.5965396+.24906777 for v in [min(source_floor),max(source_floor)]],'candidatePenetrationMax':max(0,max(TARGET_FLOOR-float(p[:,2].min()) for p in result)),'candidateAirClearanceMax':max(float(p[:,2].min())-TARGET_FLOOR for p in result),'candidateEndpointMaxVertexGap':float(np.linalg.norm(result[-1]-result[0],axis=1).max()),'constantSourceHeightAdjustment':delta,'meshVerticesUnchanged':raw_signature(mesh)==signature,'frames':len(result),'fps':30}
 assert row['meshVerticesUnchanged'];assert row['candidatePenetrationMax']<1e-4;assert row['candidateEndpointMaxVertexGap']<1e-4
 reports.append(row);print('CANDIDATE_VERIFIED',json.dumps(row))
(OUT/'现有动作审计/grounded-loop-verification.json').write_text(json.dumps(reports,ensure_ascii=False,indent=2),encoding='utf-8')

SPECS=[
 ('current_walk',ROOT/'Assets/Experiments/Squirrel/Models/walk_biped.fbx',None,'current'),
 ('current_run',ROOT/'Assets/Experiments/Squirrel/Models/run_biped.fbx',None,'current'),
 ('current_turn',ROOT/'Assets/Experiments/Squirrel/Models/turn.fbx',None,'current'),
 ('current_idle',ROOT/'Assets/Game/Life/Art/Resources/LifeAssets/Animations/SquirrelIdle.fbx',None,'current'),
 ('grounded_walk',CAND/'Squirrel_walk_GroundedLoop_Candidate.blend','Squirrel_walk_GroundedLoop_Candidate','current'),
 ('grounded_run',CAND/'Squirrel_run_GroundedLoop_Candidate.blend','Squirrel_run_GroundedLoop_Candidate','current'),
 ('gobkit_squirrel',OUT/'候选模型/gobkit-animal006-pack/Squirrel.glb','walk','normal'),
 ('gobkit_ground_squirrel',OUT/'候选模型/gobkit-animal009-pack/Capegroundsquirrel.glb','walk','normal'),
 ('oga_run',OUT/'候选模型/OGA-undeadsquirrel/squirrel-anim.blend','Run','normal')]
for label,path,act_name,mode in SPECS:
 rig,meshes,action=load(path,act_name);scene=bpy.context.scene;fps=scene.render.fps
 if mode=='current':fps=30
 first,last=map(float,action.frame_range);scene.frame_set(round(first));bpy.context.view_layer.update()
 for image in bpy.data.images:
  if image.source=='FILE' and image.packed_file is None:
   image.filepath=str(path.parent/Path(image.filepath.replace('\\','/')).name)
 if label=='oga_run':
  for mesh in meshes:
   material=bpy.data.materials.new(mesh.name+' original diffuse compatibility');material.use_nodes=True
   texture=bpy.data.images.load(str(path.parent/('squirrel-head.png' if mesh.name in {'Head','Ears','Teeth'} else 'squirrel-body.png')),check_existing=True)
   node=material.node_tree.nodes.new('ShaderNodeTexImage');node.image=texture;material.node_tree.links.new(node.outputs['Color'],material.node_tree.nodes.get('Principled BSDF').inputs['Base Color'])
   mesh.data.materials.clear();mesh.data.materials.append(material)
 points=np.concatenate([mesh_points(m) for m in meshes]);low=points.min(0);high=points.max(0)
 roots=[o for o in list(scene.objects) if o.parent is None and o.type in {'MESH','ARMATURE','EMPTY'}]
 wrapper=bpy.data.objects.new('Isolated preview placement',None);scene.collection.objects.link(wrapper)
 for o in roots:
  matrix=o.matrix_world.copy();o.parent=wrapper;o.matrix_world=matrix
 if mode=='current':
  head=rig.pose.bones['Head'];pelvis=rig.pose.bones['Pelvis'];direction=(rig.matrix_world@(head.head-pelvis.head));angle=math.atan2(direction.y,direction.x)
  wrapper.rotation_euler.z=math.pi/2-angle;wrapper.scale=(.5965396,)*3;wrapper.location.z=.24906777
 else:
  scale=.62/max(.1,high[2]-low[2]);wrapper.scale=(scale,)*3;wrapper.location.z=-low[2]*scale
  if label.startswith('gobkit'):wrapper.rotation_euler.z=math.pi
 bpy.context.view_layer.update();points=np.concatenate([mesh_points(m) for m in meshes]);center=(points.min(0)+points.max(0))*.5
 wrapper.location.x-=center[0];wrapper.location.y-=center[1]
 bpy.ops.mesh.primitive_plane_add(size=200,location=(0,0,-.004));floor=bpy.context.object;floor.color=(.35,.42,.38,1)
 for o in list(scene.objects):
  if o.type in {'LIGHT','CAMERA'}:bpy.data.objects.remove(o,do_unlink=True)
 bpy.ops.object.camera_add(location=(1.9,2.5,1.3));camera=bpy.context.object;camera.rotation_euler=(Vector((0,0,.40))-camera.location).to_track_quat('-Z','Y').to_euler();camera.data.type='ORTHO';camera.data.ortho_scale=1.65;scene.camera=camera
 if not scene.world:scene.world=bpy.data.worlds.new('Preview world')
 scene.render.engine='BLENDER_WORKBENCH';scene.display.shading.light='STUDIO';scene.display.shading.studiolight_rotate_z=0.5;scene.display.shading.color_type='TEXTURE';scene.display.shading.background_type='WORLD';scene.world.color=(.78,.82,.80)
 scene.display.shading.show_shadows=True;scene.display.shading.show_cavity=True;scene.display.shading.cavity_type='BOTH';scene.display.shading.show_specular_highlight=True
 scene.render.resolution_x=640;scene.render.resolution_y=480;scene.render.resolution_percentage=100;scene.render.image_settings.file_format='PNG';scene.render.film_transparent=False
 frames=STAGE/'frames'/label;frames.mkdir(parents=True,exist_ok=True)
 duration=max(1,last-first)
 for index in range(60):
  frame=first+(index*fps/15)%duration;scene.frame_set(math.floor(frame),subframe=frame%1);scene.render.filepath=str(frames/f'{index:04d}.png');bpy.ops.render.render(write_still=True)
 output=PREVIEW/f'{label}.mp4'
 subprocess.run([str(FFMPEG),'-y','-v','error','-framerate','15','-i',str(frames/'%04d.png'),'-c:v','libx264','-crf','19','-pix_fmt','yuv420p','-movflags','+faststart',str(output)],check=True)
 subprocess.run([str(FFMPEG),'-v','error','-i',str(output),'-f','null','-'],check=True)
 print('PREVIEW_COMPLETE',label,output)
print('ALL_PREVIEWS_COMPLETE')
