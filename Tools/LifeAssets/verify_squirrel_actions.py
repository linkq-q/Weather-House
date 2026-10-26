"""Verify exported FBX armatures against the saved Blender skin/pose authoring sources."""
import bpy,json,math
from pathlib import Path
from mathutils import Vector
ROOT=Path(__file__).resolve().parents[2]
SOURCE=ROOT/'Tools/LifeAssets/Source/Animations'
EXPORT=ROOT/'Assets/Game/Life/Art/Resources/LifeAssets/Animations'
OUT=ROOT/'Docs/LifeGameplay/Assets/Animations'
manifest=json.loads((OUT/'squirrel-animation-manifest.json').read_text(encoding='utf-8'))
checks=[];seams={}

def matrices(rig):return {b.name:rig.matrix_world@b.matrix for b in rig.pose.bones}

for item in manifest['clips']:
    name=item['id'];bpy.ops.wm.open_mainfile(filepath=str(SOURCE/(name+'.blend')))
    scene=bpy.context.scene;rig=next(o for o in scene.objects if o.type=='ARMATURE')
    signature=[(b.name,b.parent.name if b.parent else '') for b in rig.data.bones]
    source_rest={b.name:rig.matrix_world@b.matrix_local for b in rig.data.bones}
    frames=[scene.frame_end*t for t in (0,.1,.25,.5,.7,.9,1)]
    poses=[]
    for frame in frames:
        scene.frame_set(int(frame),subframe=frame-int(frame));poses.append(matrices(rig))
    seams[name]=(poses[0],poses[-1])
    bpy.ops.wm.read_factory_settings(use_empty=True);bpy.context.scene.render.fps=30
    bpy.ops.import_scene.fbx(filepath=str(EXPORT/(name+'.fbx')))
    imported=next(o for o in bpy.context.scene.objects if o.type=='ARMATURE')
    actual_signature=[(b.name,b.parent.name if b.parent else '') for b in imported.data.bones]
    assert actual_signature==signature,(name,'hierarchy changed')
    actions=list(bpy.data.actions);assert len(actions)==1,(name,len(actions))
    start,end=actions[0].frame_range
    assert abs((end-start)/30-item['durationSeconds'])<.0001,(name,'duration')
    max_position=max_rotation=max_rest=0
    for bone in imported.data.bones:
        max_rest=max(max_rest,((imported.matrix_world@bone.matrix_local).translation-source_rest[bone.name].translation).length)
    for frame,expected in zip(frames,poses):
        actual_frame=start+frame;bpy.context.scene.frame_set(int(actual_frame),subframe=actual_frame-int(actual_frame))
        actual=matrices(imported)
        for bone,matrix in actual.items():
            max_position=max(max_position,(matrix.translation-expected[bone].translation).length)
            max_rotation=max(max_rotation,matrix.to_quaternion().rotation_difference(expected[bone].to_quaternion()).angle)
    assert max_position<.0005,(name,'pose position',max_position)
    assert max_rotation<.003,(name,'pose angle',max_rotation)
    assert max_rest<.0005,(name,'rest pose',max_rest)
    assert item['maximumLimbReachExcess']<.00001,(name,'limb reach')
    assert item['minimumClearanceFromSourceFloor']>-.00001,(name,'ground penetration')
    checks.append({'clip':name,'status':'PASS','bones':len(signature),'durationSeconds':(end-start)/30,'maxReimportBonePositionError':max_position,
                   'maxReimportRotationErrorRadians':max_rotation,'maxRestPositionError':max_rest,'minimumSkinClearance':item['minimumClearanceFromSourceFloor']})
for a,ai,b,bi in [('SquirrelIdle',0,'SquirrelRestDown',0),('SquirrelRestDown',1,'SquirrelRestLoop',0),('SquirrelRestLoop',1,'SquirrelRestUp',0),('SquirrelRestLoop',0,'SquirrelRestLoop',1),('SquirrelRestUp',1,'SquirrelIdle',0),('SquirrelIdle',0,'SquirrelIdle',1)]:
    error=max((seams[a][ai][n].translation-seams[b][bi][n].translation).length for n in seams[a][ai])
    assert error<.00001,(a,b,'seam',error)
    checks.append({'seam':a+' -> '+b,'status':'PASS','maxBonePositionError':error})
(OUT/'squirrel-export-verification.json').write_text(json.dumps(checks,indent=2),encoding='utf-8')
print('SQUIRREL_FBX_VERIFY_PASS',len(checks))
