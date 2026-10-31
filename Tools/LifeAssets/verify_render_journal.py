"""Audit exported journal actions and render model/animation evidence."""
import bpy
import json
import math
import sys
import subprocess
from pathlib import Path
from mathutils import Vector

ROOT=Path(__file__).resolve().parents[2]
DOC=ROOT/'Docs/LifeGameplay/Assets/Journal'
FBX=ROOT/'Assets/Game/Life/Art/Resources/LifeAssets/JournalBook.fbx'
SOURCE=ROOT/'Tools/LifeAssets/Source/JournalBook.blend'

def rigs():return [o for o in bpy.context.scene.objects if o.type=='ARMATURE']
def points():
    deps=bpy.context.evaluated_depsgraph_get();out={}
    for o in bpy.context.scene.objects:
        if o.type!='MESH':continue
        ev=o.evaluated_get(deps);m=ev.to_mesh();out[o.name]=[ev.matrix_world@v.co for v in m.vertices];ev.to_mesh_clear()
    return out
def sample(rig,action,t):
    rig.animation_data.action=action
    if action.slots:rig.animation_data.action_slot=action.slots[0]
    lo,hi=action.frame_range;bpy.context.scene.frame_set(int(lo+(hi-lo)*t),subframe=(lo+(hi-lo)*t)%1)
    bpy.context.view_layer.update();return points()
def box(pp):
    p=[p for v in pp.values() for p in v]
    return {'min':[min(v[i] for v in p) for i in range(3)],'max':[max(v[i] for v in p) for i in range(3)]}

bpy.ops.wm.open_mainfile(filepath=str(SOURCE));rig=rigs()[0]
expected={}
for name in ['BookOpen','PageTurn']:
    action=bpy.data.actions[name]
    expected[name]={str(t):sample(rig,action,t) for t in [0,.25,.5,.75,1]}

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=str(FBX));rig=rigs()[0]
checks=[]
assert len(rig.data.bones)==78
assert all(n in bpy.data.objects for n in ['PageLeft','PageRight','TurningPage'])
assert len(bpy.data.objects['TurningPage'].data.materials)==2
for name in ['BookOpen','PageTurn']:
    actions=[a for a in bpy.data.actions if a.name.split('|')[-1]==name]
    assert len(actions)==1,(name,[a.name for a in bpy.data.actions])
    action=actions[0];lo,hi=action.frame_range
    assert abs((hi-lo)/30-{'BookOpen':1.,'PageTurn':.9}[name])<.00001
    maxerr=0
    for t in [0,.25,.5,.75,1]:
        actual=sample(rig,action,t);before=expected[name][str(t)]
        assert actual.keys()==before.keys(),(actual.keys(),before.keys())
        for mesh,verts in before.items():
            assert len(verts)==len(actual[mesh])
            maxerr=max(maxerr,max((a-b).length for a,b in zip(verts,actual[mesh])))
    assert maxerr<.0002,(name,maxerr)
    checks.append({'clip':name,'status':'PASS','seconds':(hi-lo)/30,'maxReimportVertexErrorMetres':maxerr})

openp=expected['BookOpen']['1'];closedp=expected['BookOpen']['0'];turnp=expected['PageTurn']['0.5']
# Open content front normals and footprints; curved sheet truly rises above the spine.
assert max(v.x for v in openp['PageLeft'])<0 and min(v.x for v in openp['PageRight'])>0
assert min(v.z for v in closedp['PageLeft'])>max(v.z for v in closedp['PageRight'])
assert max(v.z for v in turnp['TurningPage'])>.18
assert all(n not in openp for n in ['LeatherSpine','SpineStitches','SymbolsTab','PhotosTab'])
height_span=max(v.z for v in openp['PageRight'])-min(v.z for v in openp['PageRight'])
assert height_span>.005
# Midturn normals change along the leaf: it must not be a flat board rotating around a hinge.
leaf=turnp['TurningPage'];cols=73;row=10
p0=leaf[row*cols];pm=leaf[row*cols+36];p1=leaf[row*cols+72]
curl_angle=(pm-p0).angle(p1-pm)
assert curl_angle>math.radians(35)
assert abs(box(openp)['max'][0]-box(openp)['min'][0]-.472)<.002
for t,target_name in [('0','PageRight'),('1','PageLeft')]:
    leafpoints=expected['PageTurn'][t]['TurningPage']
    targetpoints=openp[target_name]
    for axis in (0,1):
        assert abs(min(v[axis] for v in leafpoints)-min(v[axis] for v in targetpoints))<.00001
        assert abs(max(v[axis] for v in leafpoints)-max(v[axis] for v in targetpoints))<.00001
for n in ['PageLeft','PageRight','TurningPage']:
    for uv in bpy.data.objects[n].data.uv_layers[0].data:assert all(-.00001<=v<=1.00001 for v in uv.uv)

def reading_height_at(points,p):
    xmin=min(q.x for q in points);xmax=max(q.x for q in points)
    ymin=min(q.y for q in points);ymax=max(q.y for q in points)
    if not (xmin<=p.x<=xmax and ymin<=p.y<=ymax):return None
    # Source reading grid runs from inner edge to outer edge on either half.
    u=(p.x-xmin)/(xmax-xmin)
    if points[0].x>points[40].x:u=1-u
    v=(p.y-ymin)/(ymax-ymin)
    a=min(39,int(u*40));b=min(19,int(v*20));du=u*40-a;dv=v*20-b
    return sum(points[j*41+i].z*w for i,j,w in [(a,b,(1-du)*(1-dv)),(a+1,b,du*(1-dv)),(a,b+1,(1-du)*dv),(a+1,b+1,du*dv)])

minimum_clearance=1
turn_action=next(a for a in bpy.data.actions if a.name.split('|')[-1]=='PageTurn')
for frame in range(28):
    pp=sample(rig,turn_action,frame/27)
    # Sample actual imported skin vertices; low frames must not dip into the stationary paper.
    for p in pp['TurningPage'][::8]:
        for page in ['PageLeft','PageRight']:
            height=reading_height_at(openp[page],p)
            if height is not None:minimum_clearance=min(minimum_clearance,p.z-height)
assert minimum_clearance>-.0001,minimum_clearance
report={'checks':checks,'meshCount':len(openp),'boneCount':78,'openBlenderBounds':box(openp),'closedBlenderBounds':box(closedp),
        'readingHeightSpan':height_span,'midTurnCrossSectionAngleDegrees':math.degrees(curl_angle),
        'minimumTurnClearanceMetres':minimum_clearance,
        'midTurnMaximumHeight':max(v.z for v in turnp['TurningPage']),
        'closedPageMinimumSeparation':min(v.z for v in closedp['PageLeft'])-max(v.z for v in closedp['PageRight'])}
(DOC/'journal-verification.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
print('JOURNAL_VERIFY_PASS',checks)

bpy.ops.wm.open_mainfile(filepath=str(SOURCE));rig=rigs()[0];scene=bpy.context.scene
leaf=bpy.data.objects['TurningPage']
bpy.ops.object.camera_add(location=(.31,-.50,.63));cam=bpy.context.object
target=Vector((0,0,.025));cam.rotation_euler=(target-cam.location).to_track_quat('-Z','Y').to_euler()
cam.data.type='ORTHO';cam.data.ortho_scale=.59;scene.camera=cam
for loc,energy,size in [((-.5,-.5,1.2),7,1.2),((.5,.4,.8),4,1)]:
    bpy.ops.object.light_add(type='AREA',location=loc);o=bpy.context.object;o.data.energy=energy;o.data.size=size
    o.rotation_euler=(target-o.location).to_track_quat('-Z','Y').to_euler()
scene.world=bpy.data.worlds.new('JournalPreviewWorld');scene.world.color=(.20,.20,.20)
scene.render.engine='CYCLES';scene.cycles.samples=24;scene.cycles.use_denoising=True
scene.view_settings.view_transform='Standard'
scene.render.film_transparent=False
scene.render.resolution_x=1100;scene.render.resolution_y=850;scene.render.resolution_percentage=100
for title,action,t in [('JournalOpen','BookOpen',1),('JournalClosed','BookOpen',0),('JournalHalfOpen','BookOpen',.5),('JournalPageTurn','PageTurn',.5)]:
    leaf.hide_render=action!='PageTurn'
    sample(rig,bpy.data.actions[action],t)
    if title=='JournalClosed':
        cam.location=(.38,-.35,.65);target=Vector((.115,0,.04));cam.data.ortho_scale=.48
    elif title=='JournalOpen':cam.location=(.31,-.50,.63);target=Vector((0,0,.045));cam.data.ortho_scale=.59
    else:cam.location=(.31,-.50,.70);target=Vector((0,0,.095));cam.data.ortho_scale=.68
    cam.rotation_euler=(target-cam.location).to_track_quat('-Z','Y').to_euler()
    scene.render.filepath=str(DOC/(title+'.png'));bpy.ops.render.render(write_still=True)
print('JOURNAL_RENDER_PASS')

if '--video' in sys.argv:
    frames=ROOT/'Tools/LifeAssets/.runtime/journal-preview';frames.mkdir(parents=True,exist_ok=True)
    scene.cycles.samples=8
    scene.render.resolution_x=800;scene.render.resolution_y=660
    cam.location=(.31,-.50,.70);target=Vector((0,0,.095));cam.data.ortho_scale=.68
    cam.rotation_euler=(target-cam.location).to_track_quat('-Z','Y').to_euler()
    sequence=[('BookOpen',i/15) for i in range(16)]+[('BookOpen',1)]*3
    sequence += [('PageTurn',i/14) for i in range(15)]+[('BookOpen',1)]*3
    sequence += [('BookOpen',1-i/15) for i in range(16)]
    for i,(name,t) in enumerate(sequence):
        leaf.hide_render=name!='PageTurn';sample(rig,bpy.data.actions[name],t)
        scene.render.filepath=str(frames/f'{i:04d}.png');bpy.ops.render.render(write_still=True)
    ffmpeg=Path('C:/Users/31483/Desktop/天气屋/3d资产/松鼠骨骼与UniMate实验_20261022/runtime/.venv/Lib/site-packages/imageio_ffmpeg/binaries/ffmpeg-win-x86_64-v7.1.exe')
    video=DOC/'JournalMotion.mp4'
    subprocess.run([str(ffmpeg),'-y','-hide_banner','-loglevel','error','-framerate','15','-i',str(frames/'%04d.png'),'-frames:v',str(len(sequence)),'-c:v','libx264','-crf','19','-pix_fmt','yuv420p','-movflags','+faststart',str(video)],check=True)
    subprocess.run([str(ffmpeg),'-hide_banner','-loglevel','error','-i',str(video),'-f','null','-'],check=True)
    print('JOURNAL_VIDEO_PASS',len(sequence))
