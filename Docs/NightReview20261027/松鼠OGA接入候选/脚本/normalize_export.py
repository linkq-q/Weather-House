import bpy,json,shutil
import numpy as np
from pathlib import Path
S=Path(__file__).parent;src=S/'candidate/Squirrel_OGA_Natural_Source.blend'
bpy.ops.wm.open_mainfile(filepath=str(src),load_ui=False,use_scripts=False)
rig=next(o for o in bpy.data.objects if o.type=='ARMATURE');bpy.context.view_layer.objects.active=rig
if rig.mode!='OBJECT':bpy.ops.object.mode_set(mode='OBJECT')
if hasattr(bpy.context.preferences.filepaths,'file_preview_type'):bpy.context.preferences.filepaths.file_preview_type='NONE'
head=bpy.data.objects['SquirrelHeadNaturalMesh']
def p():
 e=head.evaluated_get(bpy.context.evaluated_depsgraph_get());m=e.to_mesh();a=np.array([e.matrix_world@v.co for v in m.vertices]);e.to_mesh_clear();return a
before=p();sums=[]
for v in head.data.vertices:
 weights=[(g.group,g.weight) for g in v.groups];total=sum(w for g,w in weights);assert total>0;sums.append(total)
 for group,weight in weights:head.vertex_groups[group].add([v.index],weight/total,'REPLACE')
bpy.context.view_layer.update();after=p();gap=float(np.linalg.norm(after-before,axis=1).max())
assert gap<1e-5,gap
bpy.context.preferences.filepaths.save_version=0;bpy.ops.wm.save_as_mainfile(filepath=str(src))
selected=['Idle.000','Idle.001','Idle.002','Idle.004','Run','Run.Begin','Run.End']
for a in list(bpy.data.actions):
 if a.name not in selected:bpy.data.actions.remove(a)
rig.animation_data_create();rig.animation_data.action=bpy.data.actions['Idle.000']
if len(rig.animation_data.action.slots):rig.animation_data.action_slot=rig.animation_data.action.slots[0]
bpy.context.scene.frame_set(0)
bpy.ops.object.select_all(action='DESELECT')
for name in ['SquirrelPawsMesh','SquirrelBodyMesh','SquirrelHeadNaturalMesh',rig.name,'OGA_Squirrel_Natural_Candidate']:bpy.data.objects[name].select_set(True)
bpy.context.view_layer.objects.active=rig
out=S/'candidate/Squirrel_OGA_Natural_Unity.fbx'
bpy.ops.export_scene.fbx(filepath=str(out),use_selection=True,object_types={'ARMATURE','MESH','EMPTY'},global_scale=1,apply_unit_scale=True,apply_scale_options='FBX_SCALE_UNITS',axis_forward='-Z',axis_up='Y',bake_space_transform=False,add_leaf_bones=False,use_armature_deform_only=False,bake_anim=True,bake_anim_use_all_actions=True,bake_anim_use_nla_strips=False,bake_anim_simplify_factor=0,bake_anim_step=1,path_mode='AUTO',embed_textures=False)
shutil.copy2(out,Path(r'C:/unity project/Weather House/Assets/Characters/SquirrelOGA20261027')/out.name)
report={'status':'PASS_EXPLICIT_NEW_HEAD_WEIGHT_NORMALIZATION','oldWeightSumRange':[min(sums),max(sums)],'newWeightSumRange':[min(sum(g.weight for g in v.groups) for v in head.data.vertices),max(sum(g.weight for g in v.groups) for v in head.data.vertices)],'evaluatedHeadMaxVertexDeltaMetres':gap,'bodyVerticesOrWeightsChanged':False,'existingPreviewsStillSamePose':gap<1e-5}
(S/'weight-normalization.json').write_text(json.dumps(report,indent=2),encoding='utf-8');print('NORMALIZE_EXPORT_PASS',report)
