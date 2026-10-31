"""Blender 5.1: create the six original life-gameplay props and audit FBX exports."""
import bpy
import json
import math
import random
import sys
from pathlib import Path
from mathutils import Vector

ROOT = Path(__file__).resolve().parents[2]
MODELS = ROOT / "Assets/Game/Life/Art/Resources/LifeAssets"
SOURCE = Path(__file__).parent / "Source"
REVIEW = ROOT / "Docs/LifeGameplay/Assets"
for directory in (MODELS, SOURCE, REVIEW):
    directory.mkdir(parents=True, exist_ok=True)
random.seed(24026)


def material(name, rgb, roughness=.85):
    m = bpy.data.materials.new(name)
    m.diffuse_color = (*rgb, 1)
    m.use_nodes = True
    p = m.node_tree.nodes.get("Principled BSDF")
    p.inputs["Base Color"].default_value = (*rgb, 1)
    p.inputs["Roughness"].default_value = roughness
    return m


def mesh(name, vertices, faces, mat):
    data = bpy.data.meshes.new(name)
    data.from_pydata(vertices, [], faces)
    data.update()
    obj = bpy.data.objects.new(name, data)
    bpy.context.collection.objects.link(obj)
    obj.data.materials.append(mat)
    uv = data.uv_layers.new(name="UVMap")
    for polygon in data.polygons:
        for index in polygon.loop_indices:
            co = data.vertices[data.loops[index].vertex_index].co
            uv.data[index].uv = (co.x, co.y)
    return obj


def tube(name, points, radius, mat, sides=6):
    vertices, faces = [], []
    for i, point in enumerate(points):
        tangent = Vector(points[min(i + 1, len(points)-1)]) - Vector(points[max(0, i-1)])
        tangent.normalize()
        side = tangent.cross(Vector((0, 0, 1)))
        if side.length < .01:
            side = tangent.cross(Vector((1, 0, 0)))
        side.normalize()
        up = tangent.cross(side).normalized()
        for j in range(sides):
            a = math.tau * j / sides
            vertices.append(Vector(point) + radius * (math.cos(a)*side + math.sin(a)*up))
        if i:
            for j in range(sides):
                a = (i-1)*sides+j
                b = (i-1)*sides+(j+1)%sides
                faces.append((a, b, b+sides, a+sides))
    faces.extend([tuple(reversed(range(sides))), tuple((len(points)-1)*sides+j for j in range(sides))])
    return mesh(name, vertices, faces, mat)


def join(objects, name):
    bpy.ops.object.select_all(action="DESELECT")
    for obj in objects:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = objects[0]
    bpy.ops.object.join()
    obj = bpy.context.object
    obj.name = name
    bpy.context.scene.cursor.location = (0, 0, 0)
    bpy.ops.object.origin_set(type="ORIGIN_CURSOR")
    return obj


def blade(name, base, height, width, lean, mat):
    x, y, z = base
    dx, dy = lean
    vertices = [(x-width/2,y,z),(x+width/2,y,z),
                (x+dx*.5-width*.38,y+dy*.5,z+height*.55),
                (x+dx*.5+width*.38,y+dy*.5,z+height*.55),
                (x+dx,y+dy,z+height)]
    obj = mesh(name, vertices, [(0,1,3,2),(2,3,4),(2,3,1,0),(4,3,2)], mat)
    return obj


def grass(palette):
    parts = []
    for i in range(38):
        a, r = random.uniform(0,math.tau), random.uniform(0,.18)
        x,y = math.cos(a)*r, math.sin(a)*r
        height = random.uniform(.35,.58)
        parts.append(blade("Leaf",(x,y,0),height,.024,(x*.65,y*.65),palette[i%3]))
        if i%5 == 0:
            end = (x*1.3,y*1.3,height)
            parts.append(tube("SeedStem",[(x,y,0),end],.004,palette[0]))
            for k in range(4):
                z = height-.02*k
                parts.append(tube("Seed",[(end[0]-.025,end[1],z+.015),(end[0],end[1],z),(end[0]+.025,end[1],z+.015)],.008,palette[3],4))
    return [join(parts,"HarvestGrass")]


def bundle(palette):
    parts=[]
    for i in range(58):
        a = random.uniform(0,math.tau)
        r = random.uniform(0,.105)
        y,z=math.cos(a)*r,.14+math.sin(a)*r
        length=random.uniform(.49,.68)
        parts.append(tube("Straw",[(-length/2,y*1.4,z-.015),(0,y*.65,z),(length/2,y*1.35,z+.015)],random.uniform(.005,.009),palette[i%4],5))
    for x in [-.045,.045]:
        points=[(x,.09*math.cos(t),.14+.104*math.sin(t)) for t in [math.tau*j/24 for j in range(25)]]
        parts.append(tube("Tie",points,.012,palette[4],6))
    return [join(parts,"HayBundle")]


def mat(palette):
    parts=[]
    for axis in (0,1):
        count=15 if axis==0 else 11
        span=.70 if axis==0 else 1.0
        crossspan=1.0 if axis==0 else .70
        for i in range(count):
            fixed=-crossspan/2+crossspan*i/(count-1)
            points=[]
            for j in range(33):
                d=-span/2+span*j/32
                height=.032+.009*math.cos(j*math.pi*.75+i*math.pi+axis*math.pi)
                points.append((fixed,d,height) if axis==0 else (d,fixed,height))
            parts.append(tube("WovenStrand",points,.016,palette[(i+axis)%4],5))
    border=[(-.51,-.36,.035),(.51,-.36,.035),(.51,.36,.035),(-.51,.36,.035),(-.51,-.36,.035)]
    parts.append(tube("Binding",border,.023,palette[4],6))
    return [join(parts,"GrassMat")]


def bowl(palette):
    profile=[(0,.015),(.20,.015),(.27,.035),(.35,.12),(.36,.16),(.34,.177),(.315,.158),(.285,.09),(.23,.055),(0,.055)]
    n=48
    vertices=[]
    faces=[]
    for r,z in profile:
        for j in range(n):
            a=math.tau*j/n
            vertices.append((r*math.cos(a),r*math.sin(a),z))
    for k in range(len(profile)-1):
        for j in range(n):
            a=k*n+j;b=k*n+(j+1)%n
            faces.append((a,b,b+n,a+n))
    shell=mesh("Bowl",vertices,faces,palette[5])
    water=mesh("WaterSurface",[(.29*math.cos(math.tau*j/n),.29*math.sin(math.tau*j/n),.12) for j in range(n)],[tuple(range(n))],palette[6])
    water.location.z=.12
    for vertex in water.data.vertices:
        vertex.co.z-=.12
    return [shell,water]


def ribbon(palette):
    post=tube("Post",[(0,0,0),(0,0,1.25)],.033,palette[4],8)
    parts=[]
    for offset in [0,-.13]:
        vertices=[];faces=[]
        for i in range(17):
            t=i/16
            x=t*.64
            y=.045*math.sin(t*7)
            z=-.12*t+offset
            width=.075*(1-.35*t)
            vertices.extend([(x,y,z-width/2),(x,y,z+width/2)])
            if i:
                a=(i-1)*2
                faces.extend([(a,a+1,a+3,a+2),(a+2,a+3,a+1,a)])
        parts.append(mesh("Strip",vertices,faces,palette[7]))
    cloth=join(parts,"Ribbon")
    cloth.location=(.025,0,1.13)
    colors=cloth.data.color_attributes.new(name="WindWeight",type="FLOAT_COLOR",domain="POINT")
    for v in cloth.data.vertices:
        weight=max(0,min(1,v.co.x/.64))
        colors.data[v.index].color=(weight,weight,weight,1)
    return [post,cloth]


def shelter(palette):
    parts=[]
    def board(name,location,size,mat,angle=0):
        bpy.ops.mesh.primitive_cube_add(size=1,location=location)
        obj=bpy.context.object;obj.name=name;obj.dimensions=size
        bpy.ops.object.transform_apply(location=False,rotation=False,scale=True)
        obj.rotation_euler.x=angle;obj.data.materials.append(mat)
        parts.append(obj)
    for x in [-.82,.82]:
        for y in [-.62,.62]:
            top=1.4 if y<0 else 1.15
            parts.append(tube('Post',[(x,y,.005),(x,y,top)],.045,palette[4],8))
    for y,z in [(-.62,1.37),(.62,1.12)]:
        parts.append(tube('Crossbeam',[(-.88,y,z),(.88,y,z)],.045,palette[4],8))
    for i in range(12):
        x=-.87+i*.158
        board('RoofBoard',(x,0,1.3),(.16,1.58,.055),palette[i%3],-math.atan(.25/1.24))
    for z in [.16,.36,.56,.76]:
        board('BackWall',(0,.65,z),(1.67,.045,.17),palette[1 if z<.5 else 2])
    for x in [-.82,.82]:
        for z in [.16,.36,.56]:
            board('SideWall',(x,.15,z),(.045,1.0,.17),palette[1])
    return [join(parts,'ShadeShelter')]


def bounds(objects):
    coords=[o.matrix_world@v.co for o in objects if o.type=="MESH" for v in o.data.vertices]
    lo=[min(v[i] for v in coords) for i in range(3)]
    hi=[max(v[i] for v in coords) for i in range(3)]
    return {"min":lo,"max":hi,"size":[hi[i]-lo[i] for i in range(3)]}


def preview(objects, path):
    box=bounds(objects)
    center=Vector([(box["min"][i]+box["max"][i])/2 for i in range(3)])
    size=max(box["size"])
    ground=material("PreviewGround",(.13,.18,.16))
    bpy.ops.mesh.primitive_plane_add(size=200, location=(0,0,-.02))
    bpy.context.object.data.materials.append(ground)
    bpy.ops.object.camera_add(location=center+Vector((1.8,-2.6,1.9))*size)
    cam=bpy.context.object
    cam.rotation_euler=(center-cam.location).to_track_quat('-Z','Y').to_euler()
    cam.data.type='ORTHO'
    cam.data.ortho_scale=size*1.7
    bpy.context.scene.camera=cam
    for position,energy,light_size in [((1,-2,4),500,4),((-2,1,3),350,3)]:
        bpy.ops.object.light_add(type='AREA',location=position)
        light=bpy.context.object
        light.data.energy=energy
        light.data.shape='DISK'
        light.data.size=light_size
        light.rotation_euler=(center-light.location).to_track_quat('-Z','Y').to_euler()
    scene=bpy.context.scene
    scene.render.engine='CYCLES'
    scene.cycles.samples=24
    scene.cycles.use_denoising=True
    scene.world=bpy.data.worlds.new("PreviewWorld")
    scene.world.color=(.25,.25,.25)
    scene.render.resolution_x=880
    scene.render.resolution_y=720
    scene.render.resolution_percentage=100
    scene.render.image_settings.file_format='PNG'
    scene.render.filepath=str(path)
    bpy.ops.render.render(write_still=True)


selected=set(sys.argv[sys.argv.index('--')+1:]) if '--' in sys.argv else None
manifest_path=REVIEW/'model-manifest.json'
reports=json.loads(manifest_path.read_text(encoding='utf-8')) if selected and manifest_path.exists() else []
for name,build,description in [
    ("GrassPatch",grass,"One collectible clump; visual identity distinct from decorative grass."),
    ("GrassBundle",bundle,"Single bundle sufficient for one mat; not medicinal licorice."),
    ("GrassMat",mat,"Reusable woven grass mat; moisture changes material, not inventory type."),
    ("WaterBowl",bowl,"Fixed ceramic catchment bowl; independent WaterSurface child."),
    ("RibbonFlag",ribbon,"Tied cloth indicator; Ribbon pivot at tie and vertex red weight from 0 to 1."),
    ("ShadeShelter",shelter,"Small three-sided shelter with opaque sloping roof; front opening faces Unity +Z.")]:
    if selected and name not in selected:
        continue
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.context.preferences.filepaths.save_version=0
    bpy.context.scene.unit_settings.system='METRIC'
    bpy.context.scene.unit_settings.scale_length=1
    palette=[material("GrassSage",(.28,.39,.16)),material("StrawOlive",(.43,.46,.22)),material("StrawGold",(.66,.59,.32)),
             material("SeedCream",(.78,.70,.43)),material("RopeBrown",(.28,.20,.12)),material("BowlClay",(.47,.57,.53)),
             material("Water",(.16,.40,.48),.2),material("RibbonCream",(.80,.70,.48))]
    objects=build(palette)
    root=bpy.data.objects.new(name+"Root",None)
    bpy.context.collection.objects.link(root)
    for obj in objects:
        obj.parent=root
    bpy.context.view_layer.update()
    box=bounds(objects)
    assert all(.005 < n < 2 for n in box["size"]), (name,box)
    assert box["min"][2]>=-.001, (name,box)
    for obj in objects:
        assert obj.data.uv_layers and len(obj.data.polygons)>0
        assert all(math.isfinite(n) for v in obj.data.vertices for n in v.co)
    bpy.ops.wm.save_as_mainfile(filepath=str(SOURCE/(name+".blend")))
    bpy.ops.object.select_all(action='DESELECT')
    for obj in [root]+objects:
        obj.select_set(True)
    fbx=MODELS/(name+".fbx")
    bpy.ops.export_scene.fbx(filepath=str(fbx),use_selection=True,object_types={'EMPTY','MESH'},apply_scale_options='FBX_SCALE_UNITS',
                             axis_forward='-Z',axis_up='Y',bake_space_transform=True,add_leaf_bones=False,bake_anim=False,path_mode='AUTO')
    report={"id":name,"description":description,"fbx":str(fbx.relative_to(ROOT)).replace('\\','/'),"blender":str((SOURCE/(name+'.blend')).relative_to(ROOT)).replace('\\','/'),
            "unit":"metres","blenderBoundsXYZ":box,"unitySizeXYZ":[box["size"][0],box["size"][2],box["size"][1]],
            "origin":"ground centre; root at zero","objects":[{"name":o.name,"vertices":len(o.data.vertices),"triangles":sum(len(p.vertices)-2 for p in o.data.polygons),"materials":[m.name for m in o.data.materials]} for o in objects]}
    preview(objects,REVIEW/(name+".png"))
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=str(fbx))
    imported=[o for o in bpy.context.scene.objects if o.type=='MESH']
    actual=bounds(imported)
    assert len(imported)==len(objects),(name,len(imported),len(objects))
    assert all(abs(a-b)<.0001 for a,b in zip(box["size"],actual["size"])),(name,box,actual)
    assert sum(len(o.data.vertices) for o in imported)==sum(o["vertices"] for o in report["objects"])
    report["fbxReimport"]={"status":"PASS","meshCount":len(imported),"blenderBoundsXYZ":actual}
    reports=[r for r in reports if r['id']!=name]+[report]
    (REVIEW/"model-manifest.json").write_text(json.dumps(reports,ensure_ascii=False,indent=2),encoding='utf-8')
print("LIFE_ASSETS_PASS " + str(len(reports)))
