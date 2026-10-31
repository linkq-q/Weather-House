"""Bake independent procedural fibre/weave normal and shallow occlusion maps, not colour-derived relief."""
import bpy
import json
import math
from pathlib import Path
from mathutils import Vector

ROOT=Path(__file__).resolve().parents[2]
TEX=ROOT/'Assets/Game/Life/Art/Resources/LifeAssets/JournalTextures'
DOC=ROOT/'Docs/LifeGameplay/Assets/Journal'
bpy.ops.wm.read_factory_settings(use_empty=True)
scene=bpy.context.scene;scene.render.engine='CYCLES';scene.cycles.samples=16
scene.render.bake.margin=8;scene.render.bake.use_clear=True
bpy.ops.mesh.primitive_plane_add(size=1)
plane=bpy.context.object;plane.name='JournalMaterialBakeSurface';plane.scale=(.23,.28,1)
bpy.ops.object.transform_apply(location=False,rotation=False,scale=True)

def node(tree,kind,name):
    n=tree.nodes.new(kind);n.name=name;return n
def mathnode(tree,op,a,b=None):
    n=node(tree,'ShaderNodeMath',op);n.operation=op
    for i,v in enumerate([a,b]):
        if v is None:continue
        if isinstance(v,(int,float)):n.inputs[i].default_value=v
        else:tree.links.new(v,n.inputs[i])
    return n.outputs[0]
def noise(tree,uv,scale):
    vec=node(tree,'ShaderNodeVectorMath','Directional fibres');vec.operation='MULTIPLY';vec.inputs[1].default_value=scale;tree.links.new(uv,vec.inputs[0])
    tex=node(tree,'ShaderNodeTexNoise','Fibre noise');tex.inputs['Scale'].default_value=1;tex.inputs['Detail'].default_value=2;tex.inputs['Roughness'].default_value=.7;tree.links.new(vec.outputs[0],tex.inputs['Vector'])
    return tex.outputs['Fac']
def create_material(kind):
    material=bpy.data.materials.new(kind+'ProceduralMicroSurface');material.use_nodes=True;material.use_fake_user=True;t=material.node_tree;t.nodes.clear()
    output=node(t,'ShaderNodeOutputMaterial','Output');shader=node(t,'ShaderNodeBsdfPrincipled','Surface');shader.inputs['Roughness'].default_value=.88
    t.links.new(shader.outputs[0],output.inputs['Surface']);uv=node(t,'ShaderNodeTexCoord','UV').outputs['UV']
    if kind=='PaperAged':
        h=mathnode(t,'ADD',mathnode(t,'MULTIPLY',noise(t,uv,(190,7,1)),.72),mathnode(t,'MULTIPLY',noise(t,uv,(8,240,1)),.28))
        distance=.00016
    else:
        waves=[]
        for axis in ['X','Y']:
            wave=node(t,'ShaderNodeTexWave','Woven '+axis);wave.wave_type='BANDS';wave.bands_direction=axis;wave.inputs['Scale'].default_value=140;wave.inputs['Distortion'].default_value=.8;wave.inputs['Detail Scale'].default_value=3;t.links.new(uv,wave.inputs['Vector']);waves.append(wave.outputs['Fac'])
        h=mathnode(t,'ADD',mathnode(t,'MULTIPLY',mathnode(t,'ADD',waves[0],waves[1]),.4),mathnode(t,'MULTIPLY',noise(t,uv,(95,95,1)),.2))
        distance=.00012
    bump=node(t,'ShaderNodeBump','Independent shallow relief');bump.inputs['Strength'].default_value=.5;bump.inputs['Distance'].default_value=distance;t.links.new(h,bump.inputs['Height']);t.links.new(bump.outputs['Normal'],shader.inputs['Normal'])
    separate=node(t,'ShaderNodeSeparateXYZ','Page UV');t.links.new(uv,separate.inputs[0]);u=separate.outputs['X'];v=separate.outputs['Y']
    edge=mathnode(t,'MINIMUM',mathnode(t,'MINIMUM',u,mathnode(t,'SUBTRACT',1,u)),mathnode(t,'MINIMUM',v,mathnode(t,'SUBTRACT',1,v)))
    edge=mathnode(t,'MAXIMUM',0,mathnode(t,'SUBTRACT',1,mathnode(t,'MULTIPLY',edge,16)))
    cavity=mathnode(t,'ADD',mathnode(t,'MULTIPLY',edge,.065),mathnode(t,'MULTIPLY',mathnode(t,'SUBTRACT',1,h),.025))
    ao=mathnode(t,'SUBTRACT',1,cavity)
    return material,shader,output,ao

report=[]
for kind in ['PaperAged','CoverAged']:
    material,shader,output,ao=create_material(kind);plane.data.materials.clear();plane.data.materials.append(material)
    for mode in ['Normal','AO']:
        t=material.node_tree
        image=bpy.data.images.new(kind+'_'+mode,width=1024,height=1024,alpha=False,float_buffer=False);image.colorspace_settings.name='Non-Color'
        target=node(t,'ShaderNodeTexImage','Bake target');target.image=image;t.nodes.active=target;target.select=True
        if mode=='AO':
            emission=node(t,'ShaderNodeEmission','Shallow fibre and edge occlusion');t.links.new(ao,emission.inputs['Color']);t.links.new(emission.outputs[0],output.inputs['Surface'])
        bpy.ops.object.select_all(action='DESELECT');plane.select_set(True);bpy.context.view_layer.objects.active=plane
        bpy.ops.object.bake(type='NORMAL' if mode=='Normal' else 'EMIT',normal_space='TANGENT')
        image.filepath_raw=str(TEX/(kind+'_'+mode+'.png'));image.file_format='PNG';image.save()
        pixels=list(image.pixels);ranges=[[min(pixels[c::4]),max(pixels[c::4])] for c in range(3)]
        if mode=='Normal':
            assert ranges[0][1]-ranges[0][0]>.005 and ranges[2][0]>.6,('Flat or extreme normal map',kind,ranges)
        else:assert ranges[0][0]>.88 and ranges[0][1]>.98,('Dirty occlusion map',kind,ranges)
        report.append({'texture':image.name,'size':[1024,1024],'linearChannelRanges':ranges,'bake':'TANGENT NORMAL, +Y green' if mode=='Normal' else 'EMIT of procedural shallow cavity and symmetric edge occlusion; not whole-scene baked lighting'})
        print('BAKED',image.name,ranges,flush=True)
        t.nodes.remove(target)
        t.links.new(shader.outputs[0],output.inputs['Surface'])

# Preserve a standalone source with procedural nodes; the original journal/colour files are untouched.
bpy.ops.wm.save_as_mainfile(filepath=str(ROOT/'Tools/LifeAssets/Source/JournalMicroSurfaces.blend'))
(DOC/'micro-surface-bake.json').write_text(json.dumps(report,indent=2),encoding='utf-8')

# Compare the actual book using only the newly baked maps, so a successful bake is not the final visual check.
bpy.ops.wm.open_mainfile(filepath=str(ROOT/'Tools/LifeAssets/Source/JournalBook.blend'))
scene=bpy.context.scene;rig=bpy.data.objects['JournalRig'];rig.animation_data.action=bpy.data.actions['BookOpen'];scene.frame_set(30)
bpy.data.objects['TurningPage'].hide_render=True
for material in bpy.data.materials:
    kind='PaperAged' if material.name in ['JournalPageLeft','JournalPageRight','JournalGutter'] else 'CoverAged' if material.name in ['JournalCover','JournalCloth'] else None
    if not kind:continue
    t=material.node_tree;p=t.nodes.get('Principled BSDF');uv=node(t,'ShaderNodeTexCoord','Micro surface UV').outputs['UV']
    normal=node(t,'ShaderNodeTexImage','Baked tangent normal');normal.image=bpy.data.images.load(str(TEX/(kind+'_Normal.png')),check_existing=True);normal.image.colorspace_settings.name='Non-Color';t.links.new(uv,normal.inputs['Vector'])
    mapper=node(t,'ShaderNodeNormalMap','Subtle normal strength');mapper.inputs['Strength'].default_value=.65;t.links.new(normal.outputs['Color'],mapper.inputs['Color']);t.links.new(mapper.outputs['Normal'],p.inputs['Normal'])
    ao=node(t,'ShaderNodeTexImage','Baked shallow AO');ao.image=bpy.data.images.load(str(TEX/(kind+'_AO.png')),check_existing=True);ao.image.colorspace_settings.name='Non-Color';t.links.new(uv,ao.inputs['Vector'])
    original=p.inputs['Base Color'].links[0].from_socket
    multiply=node(t,'ShaderNodeMixRGB','Subtle occlusion');multiply.blend_type='MULTIPLY';multiply.inputs[0].default_value=.65;t.links.new(original,multiply.inputs[1]);t.links.new(ao.outputs['Color'],multiply.inputs[2]);t.links.new(multiply.outputs[0],p.inputs['Base Color'])

bpy.ops.object.camera_add(location=(.30,-.43,.46));camera=bpy.context.object;camera.data.type='ORTHO';camera.data.ortho_scale=.57
target=Vector((0,0,.025));camera.rotation_euler=(target-camera.location).to_track_quat('-Z','Y').to_euler();scene.camera=camera
for position,power,size in [((-.5,-.5,.85),12,.65),((.45,.3,.6),3,.8)]:
    bpy.ops.object.light_add(type='AREA',location=position);lamp=bpy.context.object;lamp.data.energy=power;lamp.data.size=size;lamp.rotation_euler=(target-lamp.location).to_track_quat('-Z','Y').to_euler()
scene.world=bpy.data.worlds.new('MicroSurfacePreviewWorld');scene.world.color=(.18,.18,.18)
scene.render.engine='CYCLES';scene.cycles.samples=24;scene.cycles.use_denoising=True;scene.view_settings.view_transform='Standard'
scene.render.resolution_x=1100;scene.render.resolution_y=820;scene.render.resolution_percentage=100
for name,frame in [('JournalMicroOpen',30),('JournalMicroCover',0)]:
    scene.frame_set(frame)
    if frame==0:
        target=Vector((.115,0,.038));camera.location=(.38,-.35,.60);camera.data.ortho_scale=.46;camera.rotation_euler=(target-camera.location).to_track_quat('-Z','Y').to_euler()
    scene.render.filepath=str(DOC/(name+'.png'));bpy.ops.render.render(write_still=True)

# Same-camera controls prove that the normal maps actually change shading, separately from AO.
scene.render.resolution_x=640;scene.render.resolution_y=640
target=Vector((.115,-.025,.074));camera.location=(.17,-.16,.27);camera.data.ortho_scale=.145
camera.rotation_euler=(target-camera.location).to_track_quat('-Z','Y').to_euler()
samples={}
for label,normal_strength,occlusion_strength in [('Baseline',0,0),('NormalOnly',.65,0),('NormalAO',.65,.65)]:
    for material in bpy.data.materials:
        if not material.use_nodes:continue
        nodes=material.node_tree.nodes
        if nodes.get('Subtle normal strength'):nodes['Subtle normal strength'].inputs['Strength'].default_value=normal_strength
        if nodes.get('Subtle occlusion'):nodes['Subtle occlusion'].inputs[0].default_value=occlusion_strength
    scene.render.filepath=str(DOC/('JournalMicroDetail'+label+'.png'));bpy.ops.render.render(write_still=True)
    # Load the exported render, not a stale render-result pointer.
    preview=bpy.data.images.load(scene.render.filepath,check_existing=False);samples[label]=list(preview.pixels);bpy.data.images.remove(preview)
delta={}
for label in ['NormalOnly','NormalAO']:
    differences=[abs(a-b) for i,(a,b) in enumerate(zip(samples[label],samples['Baseline'])) if i%4!=3]
    delta[label]={'meanChannelDifference':sum(differences)/len(differences),'maxChannelDifference':max(differences)}
assert delta['NormalOnly']['meanChannelDifference']>1e-6,'Normal map had no rendered effect'
(DOC/'micro-surface-render-check.json').write_text(json.dumps(delta,indent=2),encoding='utf-8')
print('JOURNAL_MICRO_BAKE_PREVIEW_PASS',flush=True)
