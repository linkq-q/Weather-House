import bpy,json,math
import numpy as np
from pathlib import Path
S=Path(__file__).parent
audit=json.loads((S/'oga-material-and-motion-source.json').read_text(encoding='utf-8'))
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=str(S/'candidate/Squirrel_OGA_Natural_Unity.fbx'))
rig=next(o for o in bpy.data.objects if o.type=='ARMATURE')
mesh=[o for o in bpy.data.objects if o.type=='MESH']
bone_names=[b.name for b in rig.data.bones]
missing=set(b['name'] for b in audit['armature']['bones'])-set(bone_names)
assert not missing,missing
assert not any('SourceDisabled' in o.name or o.name in {'Head','Teeth','Ears'} for o in mesh)
def active(a):
 rig.animation_data_create();rig.animation_data.action=a
 if len(a.slots):rig.animation_data.action_slot=a.slots[0]
 for t in rig.animation_data.nla_tracks:t.mute=True
def points(o):
 e=o.evaluated_get(bpy.context.evaluated_depsgraph_get());m=e.to_mesh();p=np.array([e.matrix_world@v.co for v in m.vertices]);e.to_mesh_clear();return p
rows=[]
rig_actions=[a for a in bpy.data.actions if len(a.slots) and a.slots[0].target_id_type=='OBJECT']
for a in rig_actions:
 active(a);lo,hi=map(float,a.frame_range);data=[]
 for f in np.linspace(lo,hi,13):
  bpy.context.scene.frame_set(int(f),subframe=f%1);bpy.context.view_layer.update();p=np.concatenate([points(o) for o in mesh]);assert np.isfinite(p).all();data.append(p)
 rows.append({'action':a.name,'frames':[lo,hi],'lowestZRange':[min(float(p[:,2].min()) for p in data),max(float(p[:,2].min()) for p in data)],'endpointMaxVertexGap':float(np.linalg.norm(data[-1]-data[0],axis=1).max())})
assert len(rows)==13,len(rows)
head=next(o for o in mesh if o.name.startswith('SquirrelHeadNaturalMesh'))
weight_sums=[sum(g.weight for g in v.groups) for v in head.data.vertices]
print('HEAD_WEIGHT_RANGE',min(weight_sums),max(weight_sums),'unweighted',sum(x<.0001 for x in weight_sums))
assert min(weight_sums)>.999 and max(weight_sums)<1.001
assert head.data.shape_keys and 'EyesClosed' in head.data.shape_keys.key_blocks
by_name={a.name.split('|')[-1]:a for a in rig_actions}
def sampled(name,end):
 active(by_name[name]);bpy.context.scene.frame_set(int(by_name[name].frame_range[1 if end else 0]));bpy.context.view_layer.update();return np.concatenate([points(o) for o in mesh])
seams={'down_to_loop':float(np.linalg.norm(sampled('RestDown_Derived',True)-sampled('RestLoop_Derived',False),axis=1).max()),'loop_to_up':float(np.linalg.norm(sampled('RestLoop_Derived',True)-sampled('RestUp_Derived',False),axis=1).max())}
assert max(seams.values())<2e-5,seams
report={'status':'PASS_BLENDER_FBX_ROUNDTRIP_UNITY_PENDING','rigName':rig.name,'boneCount':len(bone_names),'boneNames':bone_names,'sourceBonesMissing':list(missing),'meshes':[{'name':o.name,'vertices':len(o.data.vertices),'triangles':sum(len(p.vertices)-2 for p in o.data.polygons),'materials':[m.name for m in o.data.materials]} for o in mesh],'headWeightSumRange':[min(weight_sums),max(weight_sums)],'headWeightedBones':sorted({head.vertex_groups[g.group].name for v in head.data.vertices for g in v.groups if g.weight>.001}),'blendshapes':[k.name for k in head.data.shape_keys.key_blocks],'restSeamsMetres':seams,'actions':rows,'boundary':'FBX roundtrip in Blender only. Unity importer/avatar/forward/material/contacts not verified by this script.'}
(S/'fbx-roundtrip-report.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8')
print('EXPORT_VERIFY_PASS',json.dumps({'bones':len(bone_names),'meshes':len(mesh),'actions':len(rows),'weightRange':report['headWeightSumRange']}))
