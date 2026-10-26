"""Build an articulated journal from the existing AlbumBook artwork; Blender 5.1."""
import bpy
import json
import math
from pathlib import Path
from mathutils import Vector, Quaternion

ROOT=Path(__file__).resolve().parents[2]
OUT=ROOT/'Assets/Game/Life/Art/Resources/LifeAssets'
SRC=ROOT/'Tools/LifeAssets/Source'
DOC=ROOT/'Docs/LifeGameplay/Assets/Journal'
TEX=OUT/'JournalTextures'
for p in (OUT,SRC,DOC,TEX):p.mkdir(parents=True,exist_ok=True)
for n in ['PaperAged','CoverAged']:
    assert (TEX/f'{n}.png').is_file(),f'Missing approved full-surface texture: {n}'
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.context.preferences.filepaths.save_version=0
scene=bpy.context.scene
scene.unit_settings.system='METRIC';scene.unit_settings.scale_length=1
scene.render.fps=30
objects=[]
ART_WIDTH=.472
ART_HEIGHT=ART_WIDTH*742/1228
HALF_HEIGHT=ART_HEIGHT/2
PAGE_Y0=(-.5+.032)*ART_HEIGHT
PAGE_Y1=(-.5+.032+.942)*ART_HEIGHT
LEFT_X0=(-.5+.026)*ART_WIDTH
LEFT_X1=(-.5+.026+.472)*ART_WIDTH
RIGHT_X0=(-.5+.523)*ART_WIDTH
RIGHT_X1=(-.5+.523+.454)*ART_WIDTH
HINGE_Z=.037
PAPER_BASE=.0287
BEND_COLS=25
BEND_ROWS=3

def paper_height(u,v):
    """Only height varies: the original content's XY projection stays exact."""
    return (PAPER_BASE+.0062*math.sin(math.pi*u)+.0028*u**4
            +.00125*(abs(v-.5)*2)**4*(.25+.75*u)
            -.00035*math.exp(-((u-.87)/.045)**2)*math.sin(math.pi*v)**2)

def mat(name,rgb,texture=None,offset=(0,0),scale=(1,1)):
    m=bpy.data.materials.new(name);m.diffuse_color=(*rgb,1);m.use_nodes=True
    p=m.node_tree.nodes.get('Principled BSDF');p.inputs['Base Color'].default_value=(*rgb,1)
    p.inputs['Roughness'].default_value=.82
    if texture:
        uv=m.node_tree.nodes.new('ShaderNodeTexCoord')
        mapping=m.node_tree.nodes.new('ShaderNodeMapping')
        mapping.inputs['Location'].default_value=(*offset,0)
        mapping.inputs['Scale'].default_value=(*scale,1)
        tex=m.node_tree.nodes.new('ShaderNodeTexImage');tex.image=bpy.data.images.load(str(TEX/(texture+'.png')),check_existing=True)
        m.node_tree.links.new(uv.outputs['UV'],mapping.inputs['Vector'])
        m.node_tree.links.new(mapping.outputs['Vector'],tex.inputs['Vector'])
        m.node_tree.links.new(tex.outputs['Color'],p.inputs['Base Color'])
        m['sourceTexture']=texture;m['uvOffset']=list(offset);m['uvScale']=list(scale)
    return m

cloth=mat('JournalCloth',(1,1,1),'CoverAged',(.20,.20),(.15,.15))
cover=mat('JournalCover',(1,1,1),'CoverAged')
paper=mat('JournalPaperEdge',(.84,.80,.67))
paperlight=mat('JournalPaperEdgeLight',(.93,.89,.77))
leftmat=mat('JournalPageLeft',(1,1,1),'PaperAged')
rightmat=mat('JournalPageRight',(1,1,1),'PaperAged')
guttermat=mat('JournalGutter',(1,1,1),'PaperAged',(.35,.10),(.1,.8))

def mesh(name,verts,faces,material,uvs=None):
    data=bpy.data.meshes.new(name);data.from_pydata(verts,[],faces);data.update()
    o=bpy.data.objects.new(name,data);bpy.context.collection.objects.link(o);o.data.materials.append(material)
    uv=data.uv_layers.new(name='UVMap')
    for poly in data.polygons:
        for li in poly.loop_indices:
            idx=data.loops[li].vertex_index
            uv.data[li].uv=uvs[idx] if uvs else (.5+verts[idx][0]/.47,.5+verts[idx][1]/.28)
    objects.append(o);return o

def outline(x0,x1,y0,y1,r):
    points=[]
    for cx,cy,a in [(x1-r,y1-r,0),(x0+r,y1-r,90),(x0+r,y0+r,180),(x1-r,y0+r,270)]:
        for j in range(7):
            angle=math.radians(a+j*90/6);points.append((cx+r*math.cos(angle),cy+r*math.sin(angle)))
    return points

def solid(name,x0,x1,y0,y1,z0,z1,r,material,bevel=.001):
    vv=[];rings=[]
    for inset,z in [(bevel,z0),(0,z0+bevel),(0,z1-bevel),(bevel,z1)]:
        points=outline(x0+inset,x1-inset,y0+inset,y1-inset,max(.0005,r-inset))
        rings.append(len(vv));vv.extend((x,y,z) for x,y in points)
    n=len(points);ff=[tuple(reversed(range(n))),tuple(range(3*n,4*n))]
    for k in range(3):
        ff.extend((k*n+i,k*n+(i+1)%n,(k+1)*n+(i+1)%n,(k+1)*n+i) for i in range(n))
    o=mesh(name,vv,ff,material,[( (x-x0)/(x1-x0),(y-y0)/(y1-y0)) for x,y,z in vv]);return o

def tube(name,points,r,material):
    vv=[];ff=[]
    for i,p in enumerate(points):
        tangent=(Vector(points[min(i+1,len(points)-1)])-Vector(points[max(i-1,0)])).normalized()
        side=tangent.cross(Vector((0,0,1))).normalized();up=tangent.cross(side).normalized()
        for j in range(6):vv.append(Vector(p)+r*(math.cos(j*math.tau/6)*side+math.sin(j*math.tau/6)*up))
        if i:
            for j in range(6):ff.append(((i-1)*6+j,(i-1)*6+(j+1)%6,i*6+(j+1)%6,i*6+j))
    return mesh(name,vv,ff,material)

bpy.ops.object.armature_add(enter_editmode=True,location=(0,0,0))
rig=bpy.context.object;rig.name='JournalRig';rig.data.name='JournalSkeleton'
rig.data.edit_bones.remove(rig.data.edit_bones[0])
root=rig.data.edit_bones.new('BookRoot');root.head=(0,0,0);root.tail=(0,.05,0)
for name in ['CoverLeft','CoverRight']:
    b=rig.data.edit_bones.new(name);b.head=(0,0,HINGE_Z);b.tail=(0,.1,HINGE_Z);b.parent=root
for j in range(BEND_ROWS):
    for i in range(BEND_COLS):
        u=i/(BEND_COLS-1);v=j/(BEND_ROWS-1)
        b=rig.data.edit_bones.new(f'PageBend{j:02d}_{i:02d}')
        b.head=(RIGHT_X0+(RIGHT_X1-RIGHT_X0)*u,PAGE_Y0+(PAGE_Y1-PAGE_Y0)*v,PAPER_BASE)
        b.tail=b.head+Vector((0,.012,0));b.parent=root
bpy.ops.object.mode_set(mode='OBJECT')

def bind(o,bone):
    o.parent=rig
    vg=o.vertex_groups.new(name=bone);vg.add(list(range(len(o.data.vertices))),1,'REPLACE')
    m=o.modifiers.new('JournalArmature','ARMATURE');m.object=rig

for side in [-1,1]:
    x0,x1=(-.236,-.001) if side<0 else (.001,.236)
    name='CoverLeft' if side<0 else 'CoverRight'
    o=solid(name+'Board',x0,x1,-HALF_HEIGHT,HALF_HEIGHT,0,.006,.009,cloth,.0016)
    # The front design belongs on the underside of the left board, visible when closed.
    if side<0:
        o.data.materials.append(cover);o.data.polygons[0].material_index=1
        for li in o.data.polygons[0].loop_indices:
            x,y,z=o.data.vertices[o.data.loops[li].vertex_index].co
            o.data.uv_layers[0].data[li].uv=((-x-.001)/.235,(y+HALF_HEIGHT)/ART_HEIGHT)
    bind(o,name)
    for k in range(24):
        inset=.0006+.0008*(1+math.sin(k*2.37))
        ax,bx=(-.230+inset,-.002) if side<0 else (.009,.230-inset)
        o=solid(f'{name}PageLayer{k:02d}',ax,bx,-.138+inset*.4,.138-inset*.4,.0065+k*.00086,.0072+k*.00086,.004,paperlight if k%3 else paper,.00012)
        for vert in o.data.vertices:
            u=(abs(vert.co.x)-.004)/.226;u=max(0,min(1,u))
            v=(vert.co.y+.138)/.276
            vert.co.z+=(paper_height(u,v)-PAPER_BASE)*(k/24)**1.5
            # Uneven deckled edges and compressed signatures remain real geometry.
            vert.co.x+=side*.00028*math.sin(v*35+k*1.71)*u**4
            vert.co.y+=.00024*math.sin(u*27+k*1.31)*abs(v-.5)*2
        bind(o,name)
    # Continuous gutter-to-shoulder curvature, with a small outer-edge lift.
    vv=[];uv=[];ff=[]
    for j in range(21):
        v=j/20
        for i in range(41):
            u=i/40
            x=LEFT_X1+(LEFT_X0-LEFT_X1)*u if side<0 else RIGHT_X0+(RIGHT_X1-RIGHT_X0)*u
            y=PAGE_Y0+(PAGE_Y1-PAGE_Y0)*v
            z=paper_height(u,v)
            vv.append((x,y,z));uv.append((u if side>0 else 1-u,v))
    for j in range(20):
        for i in range(40):
            a=j*41+i;f=(a,a+1,a+42,a+41);ff.append(f if side>0 else tuple(reversed(f)))
    o=mesh('PageLeft' if side<0 else 'PageRight',vv,ff,leftmat if side<0 else rightmat,uv)
    for p in o.data.polygons:p.use_smooth=True
    bind(o,name)

def bridge(name,x0,x1,y0,y1,inside,material):
    vv=[];uv=[];ff=[];nx=32;ny=20
    for j in range(ny+1):
        v=j/ny
        for i in range(nx+1):
            u=i/nx
            # A continuous bound valley connects both reading edges, rather than a flat white strip.
            z=paper_height(0,v)-.0048*math.sin(math.pi*u)**2 if inside else -.001-.0012*math.sin(math.pi*u)
            vv.append((x0+(x1-x0)*u,y0+(y1-y0)*v,z));uv.append((u,v))
    for j in range(ny):
        for i in range(nx):
            a=j*(nx+1)+i;ff.append((a,a+1,a+nx+2,a+nx+1))
    o=mesh(name,vv,ff,material,uv);o.parent=rig
    left=o.vertex_groups.new(name='CoverLeft');right=o.vertex_groups.new(name='CoverRight')
    for vert in o.data.vertices:
        u=(vert.co.x-x0)/(x1-x0)
        left.add([vert.index],1-u,'REPLACE');right.add([vert.index],u,'REPLACE')
    for p in o.data.polygons:p.use_smooth=True
    mod=o.modifiers.new('JournalArmature','ARMATURE');mod.object=rig
    return o

# The old hanging brown strip is removed. Only paper gutter and flexible cloth binding remain.
bridge('PaperGutter',LEFT_X1,RIGHT_X0,PAGE_Y0,PAGE_Y1,True,guttermat)
bridge('LinenBinding',-.009,.009,-HALF_HEIGHT,HALF_HEIGHT,False,cloth)

# A paper-thin leaf with two independently assignable faces and a deformable 25 x 3 grid.
vv=[];uv=[];ff=[];cols=73;rows=21
for face in range(2):
    for j in range(rows):
        for i in range(cols):
            u=i/(cols-1);v=j/(rows-1)
            vv.append((RIGHT_X0+(RIGHT_X1-RIGHT_X0)*u,PAGE_Y0+(PAGE_Y1-PAGE_Y0)*v,PAPER_BASE+(.00015 if face==0 else -.00015)))
            uv.append((u if face==0 else 1-u,v))
    offset=face*cols*rows
    for j in range(rows-1):
        for i in range(cols-1):
            a=offset+j*cols+i;f=(a,a+1,a+cols+1,a+cols);ff.append(f if face==0 else tuple(reversed(f)))
o=mesh('TurningPage',vv,ff,rightmat,uv);o.data.materials.append(leftmat)
for p in o.data.polygons:p.material_index=int(p.index>=72*20);p.use_smooth=True
o.parent=rig
groups={(i,j):o.vertex_groups.new(name=f'PageBend{j:02d}_{i:02d}') for j in range(BEND_ROWS) for i in range(BEND_COLS)}
for vert in o.data.vertices:
    fu=(vert.co.x-RIGHT_X0)/(RIGHT_X1-RIGHT_X0)*(BEND_COLS-1)
    fv=(vert.co.y-PAGE_Y0)/(PAGE_Y1-PAGE_Y0)*(BEND_ROWS-1)
    a=min(BEND_COLS-2,max(0,int(fu)));b=min(BEND_ROWS-2,max(0,int(fv)));du=fu-a;dv=fv-b
    for i,j,w in [(a,b,(1-du)*(1-dv)),(a+1,b,du*(1-dv)),(a,b+1,(1-du)*dv),(a+1,b+1,du*dv)]:
        if w>0:groups[(i,j)].add([vert.index],w,'REPLACE')
mod=o.modifiers.new('JournalArmature','ARMATURE');mod.object=rig

def merge_named(prefix,name):
    selected=[o for o in objects if o.name.startswith(prefix)]
    bpy.ops.object.select_all(action='DESELECT')
    for o in selected:o.select_set(True)
    bpy.context.view_layer.objects.active=selected[0]
    bpy.ops.object.join();joined=bpy.context.object;joined.name=name
    for o in selected:objects.remove(o)
    objects.append(joined)

merge_named('CoverLeftPageLayer','PageStackLeft')
merge_named('CoverRightPageLayer','PageStackRight')

def reset():
    for b in rig.pose.bones:
        b.location=(0,0,0);b.rotation_mode='QUATERNION';b.rotation_quaternion=Quaternion();b.scale=(1,1,1)

def rot_world(bname,axis,angle):
    b=rig.pose.bones[bname];local=b.bone.matrix_local.to_3x3().inverted()@Vector(axis)
    b.rotation_quaternion=Quaternion(local,angle)

def smooth(t):return t*t*(3-2*t)

def turn_angle(u,v,t):
    pulse=math.sin(math.pi*t)
    # The attached edge lags; the leading corner curls first. The leaf is not a rigid plane.
    angle=math.pi*smooth(t)+pulse*(1.15*(2*u-1)+.24*(v-.5)*u+.16*math.sin(math.pi*u*2))
    return max(0,min(math.pi,angle))

def turn_point(u,v,t):
    s=smooth(t);width=(RIGHT_X1-RIGHT_X0)*(1-s)+(LEFT_X1-LEFT_X0)*s
    anchor=RIGHT_X0*(1-s)+LEFT_X1*s
    dx=dz=0;steps=80
    for k in range(steps):
        q=u*(k+.5)/steps;angle=turn_angle(q,v,t)
        dx+=math.cos(angle)*width*u/steps;dz+=math.sin(angle)*width*u/steps
    # Height-only curvature at both landing endpoints preserves the UI's projection.
    return Vector((anchor+dx,PAGE_Y0+(PAGE_Y1-PAGE_Y0)*v,paper_height(u,v)+dz+.00035))

def deform_leaf(t,hidden=False):
    for j in range(BEND_ROWS):
        for i in range(BEND_COLS):
            u=i/(BEND_COLS-1);v=j/(BEND_ROWS-1)
            b=rig.pose.bones[f'PageBend{j:02d}_{i:02d}']
            pos=turn_point(u,v,t)
            if hidden:pos.z-=.012
            b.location=b.bone.matrix_local.to_3x3().inverted()@(pos-b.bone.head_local)
            rot_world(b.name,(0,1,0),-turn_angle(u,v,t))

for name,duration in [('BookOpen',1.0),('PageTurn',.9)]:
    rig.animation_data_create();act=bpy.data.actions.new(name);rig.animation_data.action=act
    frames=round(duration*30);scene.frame_start=0;scene.frame_end=frames
    for f in range(frames+1):
        scene.frame_set(f);reset();t=f/frames;s=smooth(t)
        if name=='BookOpen':
            rot_world('CoverLeft',(0,1,0),math.pi*(1-s))
            # Hide the spare turning leaf below the visible right paper during opening.
            deform_leaf(0,True)
        else:
            deform_leaf(t)
        for b in rig.pose.bones:
            b.keyframe_insert('location',frame=f);b.keyframe_insert('rotation_quaternion',frame=f);b.keyframe_insert('scale',frame=f)
    for layer in act.layers:
        for strip in layer.strips:
            for slot in act.slots:
                bag=strip.channelbag(slot)
                if bag:
                    for curve in bag.fcurves:
                        for key in curve.keyframe_points:key.interpolation='LINEAR'
    act.use_fake_user=True

rig.animation_data.action=bpy.data.actions['BookOpen'];scene.frame_set(30)
scene.frame_end=30
bpy.ops.file.pack_all()
bpy.ops.wm.save_as_mainfile(filepath=str(SRC/'JournalBook.blend'))
bpy.ops.object.select_all(action='DESELECT');rig.select_set(True)
for o in objects:o.select_set(True)
bpy.context.view_layer.objects.active=rig
bpy.ops.export_scene.fbx(filepath=str(OUT/'JournalBook.fbx'),use_selection=True,object_types={'MESH','ARMATURE'},
    apply_scale_options='FBX_SCALE_ALL',axis_forward='-Z',axis_up='Y',bake_space_transform=False,add_leaf_bones=False,
    bake_anim=True,bake_anim_use_all_actions=True,bake_anim_use_nla_strips=False,bake_anim_simplify_factor=0,path_mode='RELATIVE')
report={'asset':'LifeAssets/JournalBook','metres':True,'bones':len(rig.data.bones),
    'meshCount':len(objects),'vertices':sum(len(o.data.vertices) for o in objects),
    'triangles':sum(sum(len(p.vertices)-2 for p in o.data.polygons) for o in objects),
    'sourceArtwork':['Assets/Game/Life/Art/Resources/LifeAssets/JournalTextures/'+n+'.png' for n in ['PaperAged','CoverAged']],
    'paperLayersPerSide':24,'bendingControlGrid':[BEND_COLS,BEND_ROWS],
    'gutterDepthMetres':.0048,'gutterWidthMetres':RIGHT_X0-LEFT_X1,
    'readingHeightMetres':[min(paper_height(i/40,j/20) for i in range(41) for j in range(21)),max(paper_height(i/40,j/20) for i in range(41) for j in range(21))],
    'clips':{'BookOpen':1.,'PageTurn':.9},
    'artworkBlenderRectangle':{'minX':-.236,'maxX':.236,'minY':-HALF_HEIGHT,'maxY':HALF_HEIGHT,'width':ART_WIDTH,'height':ART_HEIGHT},
    'pageBlenderRectangles':{'PageLeft':[LEFT_X0,PAGE_Y0,LEFT_X1-LEFT_X0,PAGE_Y1-PAGE_Y0],'PageRight':[RIGHT_X0,PAGE_Y0,RIGHT_X1-RIGHT_X0,PAGE_Y1-PAGE_Y0]},
    'materials':{m.name:{'texture':m.get('sourceTexture'),'offset':list(m.get('uvOffset',[0,0])),'scale':list(m.get('uvScale',[1,1]))} for m in bpy.data.materials},
    'readers':{'PageLeft':'UV0 0..1','PageRight':'UV0 0..1','TurningPage':'slot0 front UV0 0..1; slot1 back UV0 0..1, reversed U to read after turn'}}
(DOC/'journal-manifest.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
print('JOURNAL_BUILT',report['meshCount'],report['triangles'])
