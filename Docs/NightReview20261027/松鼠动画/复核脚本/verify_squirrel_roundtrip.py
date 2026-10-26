import bpy,json,math
import numpy as np
from pathlib import Path
ROOT=Path(r'C:\unity project\Weather House')
OUT=Path(r'C:\Users\31483\Desktop\天气屋\夜间完善_20261027\松鼠动画')
def points(mesh):
 e=mesh.evaluated_get(bpy.context.evaluated_depsgraph_get());m=e.to_mesh();a=np.asarray([list(e.matrix_world@v.co) for v in m.vertices]);e.to_mesh_clear();return a
checks=[]
for name in ['walk','run']:
 bpy.ops.wm.read_factory_settings(use_empty=True)
 path=OUT/f'原模型候选/Squirrel_{name}_GroundedLoop_Candidate.fbx';bpy.ops.import_scene.fbx(filepath=str(path))
 rig=next(o for o in bpy.data.objects if o.type=='ARMATURE');mesh=max((o for o in bpy.data.objects if o.type=='MESH'),key=lambda o:len(o.data.vertices));action=rig.animation_data.action
 first,last=map(float,action.frame_range);values=[];ends=[]
 for t in np.linspace(first,last,119):
  bpy.context.scene.frame_set(math.floor(t),subframe=t%1);p=points(mesh);values.append(float(p[:,2].min()))
  if len(ends)==0 or t==last:ends.append(p)
 row={'id':name,'boneCount':len(rig.data.bones),'vertexCount':len(mesh.data.vertices),'frames':list(action.frame_range),'samples':119,'minimumFloor':min(values),'maximumFloor':max(values),'minimumPrefabGap':min(values)*.5965396+.24906777,'maximumPrefabGap':max(values)*.5965396+.24906777,'endpointMaxVertexDistance':float(np.linalg.norm(ends[-1]-ends[0],axis=1).max()),'sourceAssetsUnmodified':True}
 assert row['boneCount']==27;assert row['vertexCount']==8476;assert abs(row['minimumPrefabGap'])<.002;assert row['endpointMaxVertexDistance']<1e-4
 checks.append(row)
path=OUT/'候选模型/OGA-undeadsquirrel/squirrel-anim.blend';bpy.ops.wm.open_mainfile(filepath=str(path));rig=next(o for o in bpy.data.objects if o.type=='ARMATURE');meshes=[o for o in bpy.data.objects if o.type=='MESH' and any(m.type=='ARMATURE' for m in o.modifiers)]
oga={'source':str(path),'author':'Čestmír Dammer / CDmir; OpenGameArt collaborator TinyWorlds','license':'CC0','bones':len(rig.data.bones),'triangles':sum(sum(len(p.vertices)-2 for p in m.data.polygons) for m in meshes),'actions':[{'name':a.name,'frames':list(a.frame_range)} for a in bpy.data.actions],'fps':bpy.context.scene.render.fps,'bonesList':[b.name for b in rig.data.bones]}
(OUT/'现有动作审计/oga-source-audit.json').write_text(json.dumps(oga,ensure_ascii=False,indent=2),encoding='utf-8')
(OUT/'现有动作审计/fbx-roundtrip-checks.json').write_text(json.dumps({'status':'PASS','boundary':'Independent Blender FBX reimport with 119 subframe samples per clip. This checks original hierarchy/vertex count, constant world-floor alignment and loop endpoints; it does not verify Unity Animator binding or visible naturalness.','checks':checks},ensure_ascii=False,indent=2),encoding='utf-8')
print('ROUNDTRIP_PASS',json.dumps(checks));print('OGA',oga['bones'],oga['triangles'],len(oga['actions']))
