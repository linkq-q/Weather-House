"""Re-export the six saved actions with the original squirrel FBX unit convention.

Blender -b --python Tools/LifeAssets/reexport_squirrel_actions.py
Does not resave .blend sources or alter poses, skin weights, or original FBXs.
"""
import bpy
from pathlib import Path

ROOT=Path(__file__).resolve().parents[2]
SOURCE=ROOT/'Tools/LifeAssets/Source/Animations'
EXPORT=ROOT/'Assets/Game/Life/Art/Resources/LifeAssets/Animations'
NAMES=('SquirrelIdle','SquirrelRestDown','SquirrelRestLoop','SquirrelRestUp','SquirrelDrink','SquirrelAccept')

for name in NAMES:
    bpy.ops.wm.open_mainfile(filepath=str(SOURCE/(name+'.blend')))
    scene=bpy.context.scene;scene.frame_set(0)
    rig=next(o for o in scene.objects if o.type=='ARMATURE')
    mesh=max((o for o in scene.objects if o.type=='MESH'),key=lambda o:len(o.data.vertices))
    bpy.ops.object.select_all(action='DESELECT')
    rig.select_set(True);mesh.select_set(True);bpy.context.view_layer.objects.active=rig
    bpy.ops.export_scene.fbx(filepath=str(EXPORT/(name+'.fbx')),use_selection=True,object_types={'ARMATURE','MESH'},
        global_scale=1,apply_unit_scale=True,apply_scale_options='FBX_SCALE_NONE',axis_forward='-Z',axis_up='Y',
        bake_space_transform=False,add_leaf_bones=False,bake_anim=True,bake_anim_use_all_actions=False,
        bake_anim_use_nla_strips=False,bake_anim_simplify_factor=0)
    print('SQUIRREL_ORIGINAL_UNITS_EXPORTED',name)
