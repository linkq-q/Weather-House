import bpy,json,math
from pathlib import Path
from mathutils import Vector
ROOT=Path(r'C:\unity project\Weather House')
OUT=Path(r'C:\Users\31483\Desktop\天气屋\夜间完善_20261027\松鼠动画')
SPECS=[('current_walk',ROOT/'Assets/Experiments/Squirrel/Models/walk_biped.fbx'),('current_run',ROOT/'Assets/Experiments/Squirrel/Models/run_biped.fbx'),('current_turn',ROOT/'Assets/Experiments/Squirrel/Models/turn.fbx'),('current_idle',ROOT/'Assets/Game/Life/Art/Resources/LifeAssets/Animations/SquirrelIdle.fbx'),('gobkit_squirrel',OUT/'候选模型/gobkit-animal006-pack/Squirrel.glb'),('gobkit_ground_squirrel',OUT/'候选模型/gobkit-animal009-pack/Capegroundsquirrel.glb')]
report=[]
for label,path in SPECS:
 bpy.ops.wm.read_factory_settings(use_empty=True)
 if path.suffix=='.fbx':bpy.ops.import_scene.fbx(filepath=str(path))
 else:bpy.ops.import_scene.gltf(filepath=str(path))
 rig=next(o for o in bpy.data.objects if o.type=='ARMATURE')
 meshes=[o for o in bpy.data.objects if o.type=='MESH' and any(m.type=='ARMATURE' for m in o.modifiers)]
 actions=list(bpy.data.actions)
 row={'id':label,'source':str(path),'fps':bpy.context.scene.render.fps,'bones':[b.name for b in rig.data.bones],'meshCount':len(meshes),'vertexCount':sum(len(o.data.vertices) for o in meshes),'triangles':sum(sum(len(p.vertices)-2 for p in o.data.polygons) for o in meshes),'actions':[]}
 for action in actions:
  rig.animation_data_create();rig.animation_data.action=action
  if len(action.slots):rig.animation_data.action_slot=action.slots[0]
  for track in rig.animation_data.nla_tracks:track.mute=True
  first,last=map(float,action.frame_range)
  samples=[]
  for frame in range(math.ceil(first),math.floor(last)+1):
   bpy.context.scene.frame_set(frame);bpy.context.view_layer.update()
   graph=bpy.context.evaluated_depsgraph_get()
   verts=[]
   for mesh in meshes:
    ev=mesh.evaluated_get(graph);data=ev.to_mesh();verts += [list(ev.matrix_world@v.co) for v in data.vertices];ev.to_mesh_clear()
   bones={b.name:list(rig.matrix_world@b.head) for b in rig.pose.bones if any(k in b.name.lower() for k in ['foot','hand','pelvis','hips','head','tailroot'])}
   bounds={'min':[min(p[i] for p in verts) for i in range(3)],'max':[max(p[i] for p in verts) for i in range(3)]}
   samples.append({'frame':frame,'bones':bones,'bounds':bounds})
  action_row={'name':action.name,'range':[first,last],'seconds':(last-first)/row['fps'],'samples':samples}
  for name in samples[0]['bones']:
   ps=[s['bones'][name] for s in samples]
   action_row.setdefault('boneMetrics',{})[name]={'min':[min(p[i] for p in ps) for i in range(3)],'max':[max(p[i] for p in ps) for i in range(3)],'endpointDistance':(Vector(ps[-1])-Vector(ps[0])).length}
  row['actions'].append(action_row)
 report.append(row)
 print('AUDIT',label,row['fps'],row['vertexCount'],row['triangles'],[(a['name'],a['range'],a['seconds']) for a in row['actions']])
(OUT/'现有动作审计/offline-motion-audit.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8')
print('AUDIT_COMPLETE',len(report))
