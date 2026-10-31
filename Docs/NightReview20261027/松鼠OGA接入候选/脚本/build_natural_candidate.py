"""Offline only. Never reads/writes Unity scene, Assets, or player saves."""
import bpy,bmesh,json,math,hashlib,subprocess
import numpy as np
from pathlib import Path
from mathutils import Vector
STAGE=Path(__file__).parent
SOURCE=Path(r'C:/Users/31483/Desktop/天气屋/夜间完善_20261027/松鼠动画/候选模型/OGA-undeadsquirrel/squirrel-anim.blend')
OUT=STAGE/'candidate';OUT.mkdir(exist_ok=True)
PREVIEW=STAGE/'preview';PREVIEW.mkdir(exist_ok=True)
bpy.ops.wm.open_mainfile(filepath=str(SOURCE),load_ui=False,use_scripts=False)
scene=bpy.context.scene
rig=next(o for o in bpy.data.objects if o.type=='ARMATURE')
bpy.context.view_layer.objects.active=rig
if rig.mode!='OBJECT':bpy.ops.object.mode_set(mode='OBJECT')
if hasattr(bpy.context.preferences.filepaths,'file_preview_type'):bpy.context.preferences.filepaths.file_preview_type='NONE'
skin=[o for o in bpy.data.objects if o.type=='MESH' and any(m.type=='ARMATURE' for m in o.modifiers)]
source_names={o:o.name for o in skin}
source_hashes={p.name:hashlib.sha256(p.read_bytes()).hexdigest() for p in SOURCE.parent.iterdir() if p.is_file()}
def activate(a):
 rig.animation_data_create();rig.animation_data.action=a
 if len(a.slots):rig.animation_data.action_slot=a.slots[0]
 for t in rig.animation_data.nla_tracks:t.mute=True
activate(bpy.data.actions['Idle.000']);scene.frame_set(0)
def vec(v):return [round(float(x),8) for x in v]
def points(obj):
 e=obj.evaluated_get(bpy.context.evaluated_depsgraph_get());m=e.to_mesh();a=np.array([e.matrix_world@v.co for v in m.vertices]);e.to_mesh_clear();return a
def mesh_signature(o):
 return hashlib.sha256(np.array([list(v.co) for v in o.data.vertices],dtype='<f8').tobytes()+json.dumps([[(g.group,g.weight) for g in v.groups] for v in o.data.vertices]).encode()).hexdigest()
before_signatures={o:mesh_signature(o) for o in skin}
original_bones=[b.name for b in rig.data.bones]
for o in list(scene.objects):
 if o.type in {'LIGHT','CAMERA'} or (o.type=='MESH' and o not in skin):bpy.data.objects.remove(o,do_unlink=True)
root=bpy.data.objects.new('OGA_Squirrel_Natural_Candidate',None);scene.collection.objects.link(root)
matrix=rig.matrix_world.copy();rig.parent=root;rig.matrix_world=matrix
root.scale=(.2,.2,.2);root.location.z=.000115
scene.unit_settings.system='METRIC';scene.unit_settings.scale_length=1
scene.render.fps=24;scene.render.fps_base=1
def srgb(hex):
 vals=[int(hex[i:i+2],16)/255 for i in (0,2,4)]
 return tuple(v/12.92 if v<=.04045 else ((v+.055)/1.055)**2.4 for v in vals)+(1,)
def material(name,color,roughness=.88,specular=.18):
 m=bpy.data.materials.new(name);m.use_nodes=True;m.diffuse_color=srgb(color);m.roughness=roughness
 n=m.node_tree.nodes.get('Principled BSDF');n.inputs['Base Color'].default_value=srgb(color);n.inputs['Roughness'].default_value=roughness;n.inputs['Metallic'].default_value=0;n.inputs['Specular IOR Level'].default_value=specular;n.inputs['Coat Weight'].default_value=0
 return m
fur=material('OGA Natural Chestnut Fur','A16B42');belly=material('OGA Natural Cream Belly','D7C3A0');tail=material('OGA Natural Tail','8C5937');muzzle=material('OGA Natural Muzzle','CCAF85');eye=material('OGA Natural Eyes Nose','241A16',.55,.26);paws=material('OGA Natural Paws','5E402E');teeth=material('OGA Natural Incisors','DED3B7')
mats=[fur,belly,tail,muzzle,eye,paws,teeth]
assign_counts={}
for o in skin:
 name=source_names[o]
 o.data.materials.clear()
 for m in mats:o.data.materials.append(m)
 counts={}
 for face in o.data.polygons:
  c=sum((o.data.vertices[v].co for v in face.vertices),Vector())/len(face.vertices)
  index=0
  if name=='Squirrel':
   tail_weight=0
   for vi in face.vertices:
    tail_weight+=sum(g.weight for g in o.data.vertices[vi].groups if o.vertex_groups[g.group].name.startswith('Tail'))/len(face.vertices)
   if tail_weight>.5:index=2
  elif name=='Head':
   eye_distance=((abs(c.x)-.245)/.105)**2+((c.y+1.49)/.12)**2+((c.z-1.99)/.115)**2
   if eye_distance<1.0:index=4
   elif c.y<-1.715 and c.z<1.97:index=4
   elif c.y<-1.38 and c.z<1.86:index=3
  elif name=='Fingers':index=5
  elif name=='Teeth':index=6
  face.material_index=index;counts[str(index)]=counts.get(str(index),0)+1
  face.use_smooth=True
 assign_counts[name]=counts
 o.name={'Squirrel':'SquirrelBodyMesh','Head':'SquirrelHeadMesh','Fingers':'SquirrelPawsMesh','Teeth':'SquirrelTeethMesh','Ears':'SquirrelEarTufts_SourceDisabled'}[name]
 if name in {'Ears','Head','Teeth'}:o.hide_render=True;o.hide_viewport=True
assert all(mesh_signature(o)==before_signatures[o] for o in skin)
assert [b.name for b in rig.data.bones]==original_bones
assert len(bpy.data.actions)==28
source_mesh_stats={source_names[o]:{'vertices':len(o.data.vertices),'triangles':sum(len(p.vertices)-2 for p in o.data.polygons)} for o in skin}
body=next(o for o in skin if source_names[o]=='Squirrel')
bm=bmesh.new();bm.from_mesh(body.data)
caps=bmesh.ops.holes_fill(bm,edges=[e for e in bm.edges if e.is_boundary],sides=0).get('faces',[])
for face in caps:face.material_index=0;face.smooth=True
body_caps=len(caps)
if caps:bmesh.ops.triangulate(bm,faces=caps)
bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces));bm.to_mesh(body.data);bm.free()
assert mesh_signature(body)==before_signatures[body], 'Capping must not move source vertices or weights'
# The source head itself has sunken/open undead facial features. Color replacement cannot close them.
# Build a simple closed natural head on the same Head bone; keep the original head/teeth/tufts hidden in source blend.
head_parts=[]
inner_ear=material('OGA Natural Inner Ear','B69171',.9,.12)
def ellipsoid(name,location,scale,mat,tilt=0):
 bpy.ops.mesh.primitive_uv_sphere_add(segments=16,ring_count=10,radius=1,location=location)
 o=bpy.context.object;o.name=name;o.scale=scale;o.rotation_euler.y=tilt
 bpy.ops.object.transform_apply(location=True,rotation=True,scale=True)
 o.parent=rig;o.matrix_parent_inverse=next(s for s in skin if source_names[s]=='Head').matrix_parent_inverse.copy();o.data.materials.append(mat)
 group=o.vertex_groups.new(name='Head');group.add(list(range(len(o.data.vertices))),1,'REPLACE')
 mod=o.modifiers.new('Same original OGA rig','ARMATURE');mod.object=rig
 for f in o.data.polygons:f.use_smooth=True
 head_parts.append(o);return o
neck_part=ellipsoid('Natural closed neck',(0,-.98,1.64),(.33,.35,.48),fur)
# Copy source head/neck skin weights locally so the lower neck stays attached while the head turns.
source_head=next(o for o in skin if source_names[o]=='Head')
neck_part.vertex_groups.clear()
for group in source_head.vertex_groups:neck_part.vertex_groups.new(name=group.name)
for vertex in neck_part.data.vertices:
 nearest=min(source_head.data.vertices,key=lambda v:(v.co-vertex.co).length_squared)
 total=sum(group.weight for group in nearest.groups)
 assert total>0
 for group in nearest.groups:neck_part.vertex_groups[group.group].add([vertex.index],group.weight/total,'REPLACE')
ellipsoid('Natural closed head',(0,-1.24,1.99),(.395,.43,.39),fur)
ellipsoid('Natural muzzle',(0,-1.565,1.78),(.27,.29,.18),muzzle)
ellipsoid('Natural nose',(0,-1.86,1.805),(.10,.055,.065),eye)
for side in [-1,1]:
 ellipsoid('Natural ear '+str(side),(side*.285,-1.035,2.36),(.16,.125,.25),fur,side*.12)
 ellipsoid('Natural inner ear '+str(side),(side*.285,-1.151,2.37),(.09,.024,.16),inner_ear,side*.12)
 ellipsoid('Natural eye '+str(side),(side*.295,-1.52,2.06),(.085,.075,.105),eye)
bpy.ops.object.select_all(action='DESELECT')
for o in head_parts:o.select_set(True)
bpy.context.view_layer.objects.active=head_parts[0];bpy.ops.object.join()
natural_head=bpy.context.object;natural_head.name='SquirrelHeadNaturalMesh'
included=[o for o in skin if source_names[o] in {'Squirrel','Fingers'}]+[natural_head]
source_names[natural_head]='RebuiltNaturalHead'
bone=rig.pose.bones['Head']
headmesh=natural_head
front=[v.co for v in headmesh.data.vertices if abs(v.co.x)<.14 and v.co.z<1.78]
mouth_model=min(front,key=lambda v:v.y).copy();mouth_world=headmesh.matrix_world@mouth_model
mouth_head_local=(rig.matrix_world@bone.matrix).inverted()@mouth_world
scene.frame_start=0;scene.frame_end=19
for a in bpy.data.actions:a.use_fake_user=True
bpy.context.preferences.filepaths.save_version=0
bpy.ops.file.pack_all()
bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'Squirrel_OGA_Natural_Source.blend'))
selected_names=['Idle.000','Idle.001','Idle.002','Idle.004','Run','Run.Begin','Run.End']
for a in list(bpy.data.actions):
 if a.name not in selected_names:bpy.data.actions.remove(a)
activate(bpy.data.actions['Idle.000']);scene.frame_set(0)
bpy.ops.object.select_all(action='DESELECT')
for o in included+[rig,root]:o.hide_set(False);o.select_set(True)
bpy.context.view_layer.objects.active=rig
fbx=OUT/'Squirrel_OGA_Natural_Unity.fbx'
bpy.ops.export_scene.fbx(filepath=str(fbx),use_selection=True,object_types={'ARMATURE','MESH','EMPTY'},global_scale=1,apply_unit_scale=True,apply_scale_options='FBX_SCALE_UNITS',axis_forward='-Z',axis_up='Y',bake_space_transform=False,add_leaf_bones=False,use_armature_deform_only=False,bake_anim=True,bake_anim_use_all_actions=True,bake_anim_use_nla_strips=False,bake_anim_simplify_factor=0,bake_anim_step=1,path_mode='AUTO',embed_textures=False)
# Preview uses an identical camera/light rig for both materials. Source is a diffuse-compatibility reconstruction.
scene.render.engine='CYCLES';scene.cycles.samples=20;scene.cycles.use_denoising=True
scene.render.resolution_x=900;scene.render.resolution_y=700;scene.render.resolution_percentage=100;scene.render.image_settings.file_format='PNG';scene.render.film_transparent=False
scene.view_settings.view_transform='AgX';scene.view_settings.look='AgX - Medium High Contrast'
world=bpy.data.worlds.new('Neutral preview world');world.use_nodes=True;world.node_tree.nodes.get('Background').inputs[0].default_value=(.64,.71,.72,1);world.node_tree.nodes.get('Background').inputs[1].default_value=.65;scene.world=world
bpy.ops.mesh.primitive_plane_add(size=20,location=(0,0,-.003));floor=bpy.context.object;floor.data.materials.append(material('Preview ground','B8C4BA',.95,.1))
def area(name,at,power,size):
 bpy.ops.object.light_add(type='AREA',location=at);o=bpy.context.object;o.name=name;o.data.energy=power;o.data.shape='DISK';o.data.size=size;o.rotation_euler=(Vector((0,0,.3))-o.location).to_track_quat('-Z','Y').to_euler()
area('Soft key',(1.2,-2.0,3.2),450,3.2);area('Soft fill',(-2,-.5,1.7),100,3)
bpy.ops.object.camera_add(location=(1.25,-1.75,1.0));cam=bpy.context.object;scene.camera=cam;cam.data.type='ORTHO';cam.data.ortho_scale=1.25
def camera(at,target=(0,0,.31)):
 cam.location=at;cam.rotation_euler=(Vector(target)-cam.location).to_track_quat('-Z','Y').to_euler()
camera((1.25,-1.75,1.0));activate(bpy.data.actions['Idle.000']);scene.frame_set(0)
for name,at in [('front',(1.25,-1.75,1.0)),('side',(1.65,-.15,.85)),('back',(1.1,1.85,.9))]:
 camera(at);scene.render.filepath=str(PREVIEW/('natural-'+name+'.png'));bpy.ops.render.render(write_still=True)
# Only rebind temporary preview materials, never the saved natural source or exported FBX.
natural_head.hide_render=True
original_head=next(o for o in skin if source_names[o]=='Head');original_head.hide_render=False
original_teeth=next(o for o in skin if source_names[o]=='Teeth');original_teeth.hide_render=False
before_meshes=[o for o in skin if source_names[o]!='Ears']
for o in before_meshes:
 name=source_names[o];m=material('Source diffuse compatibility '+name,'FFFFFF',.5,.5);tex=m.node_tree.nodes.new('ShaderNodeTexImage');tex.image=bpy.data.images.load(str(SOURCE.parent/('squirrel-head.png' if name in {'Head','Teeth','Fingers'} else 'squirrel-body.png')),check_existing=True);m.node_tree.links.new(tex.outputs['Color'],m.node_tree.nodes.get('Principled BSDF').inputs['Base Color']);o.data.materials.clear();o.data.materials.append(m)
 for f in o.data.polygons:f.material_index=0
camera((1.25,-1.75,1.0));scene.render.filepath=str(PREVIEW/'source-diffuse-compatibility.png');bpy.ops.render.render(write_still=True)
# Reload saved source before motion previews so all material assignments are exact again.
report={'status':'OFFLINE_NATURAL_MATERIAL_CANDIDATE_EXPORTED_UNITY_PENDING','sourceSha256':source_hashes,'candidateFiles':['Squirrel_OGA_Natural_Source.blend','Squirrel_OGA_Natural_Unity.fbx'],'bones':original_bones,'sourceActionCount':28,'exportedActionNames':selected_names,'originalGeometryAndWeightsPreservedInSourceBlend':True,'candidateBodyVerticesAndWeightsUnchanged':True,'candidateHeadRebuiltForNonUndeadShape':True,'meshStats':source_mesh_stats,'includedMeshes':[o.name for o in included],'excludedMeshes':'Original Head, Teeth, Ears alpha hair-card tufts hidden in source blend and omitted from FBX. New closed low-detail head/ears/eyes/nose weighted to unchanged original Head bone; body/feet mesh and all 34 bones/28 source actions retained.','naturalMaterialAssignmentCounts':assign_counts,'materials':[{'name':m.name,'baseColorLinear':list(m.diffuse_color),'roughness':m.roughness} for m in mats+[inner_ear]],'normalMapConnected':False,'oldDiffuseTextureUsed':False,'sourceUnits':'NONE, scale_length=1; no claimed physical real-world author scale','candidateUnits':'METRIC, 1 Blender unit = 1 metre, authored fixed parent scale .2; source bone matrices/skin local coordinates and animation keys preserved, except separately rebuilt natural head surface','candidateForward':'Source faces Blender -Y. FBX axis_forward=-Z, axis_up=Y; Unity visual forward/grounding still must be checked after import.','applyRootMotionProposal':False,'rootMainMotionSource':'No measured Main world translation in Run; see source audit. No root-motion animation has been newly added.','loopBoundary':'Native Run source endpoint mesh gap max 0.0268297624 source units; x.2 candidate scale about 5.37mm, not edited or described as perfectly seamless. Idle.000/.001/.002/.004 endpoints matched at sampled source boundaries.','mouthReferenceCandidateOnly':{'meshPointLocal':vec(mouth_model),'headBoneLocal':vec(mouth_head_local),'worldMetresAtIdle0':vec(mouth_world),'boundary':'Reference surface point only. No Drink clip; cannot reuse old hard-coded Head-local offsets or claim water-contact validation.'},'previewBeforeBoundary':'Original undead head/body geometry and diffuse PNG reconnected to a neutral Principled material roughness .5/specular .5, no normal map. Blender5.1-converted original source nodes have no image/normal links, so this is explicitly a diffuse compatibility comparison, not proof of untouched source node rendering.','unityOrAssetsTouched':False}
report['bodyBoundaryCapsAdded']=body_caps
report['bodyTopologyBoundary']='Source body vertices/weights retained; open body/neck boundaries capped with new faces. Original downloaded source package remains byte-identical.'
(STAGE/'candidate-build-report.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8')
assert all(hashlib.sha256((SOURCE.parent/name).read_bytes()).hexdigest()==value for name,value in source_hashes.items())
print('NATURAL_CANDIDATE_COMPLETE',fbx)
