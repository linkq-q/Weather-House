"""Render the saved clips at 10 FPS for motion review; no source assets are modified."""
import bpy,json,math,subprocess
from pathlib import Path
from mathutils import Vector,Quaternion
ROOT=Path(__file__).resolve().parents[2]
REPORT=ROOT/'Docs/LifeGameplay/Assets/Animations'
FRAMES=ROOT/'Tools/LifeAssets/.runtime/animation-preview'
FFMPEG=Path(r'C:\Users\31483\Desktop\天气屋\3d资产\松鼠骨骼与UniMate实验_20261022\runtime\.venv\Lib\site-packages\imageio_ffmpeg\binaries\ffmpeg-win-x86_64-v7.1.exe')
manifest=json.loads((REPORT/'squirrel-animation-manifest.json').read_text(encoding='utf-8'))
for clip in manifest['clips']:
    name=clip['id'];folder=FRAMES/name;folder.mkdir(parents=True,exist_ok=True)
    bpy.ops.wm.open_mainfile(filepath=str(ROOT/'Tools/LifeAssets/Source/Animations'/(name+'.blend')))
    scene=bpy.context.scene;rig=next(o for o in scene.objects if o.type=='ARMATURE')
    yaw=Quaternion((0,1,0),manifest['headingRadians'])
    center=rig.matrix_world@Vector((0,.02,0));direction=rig.matrix_world.to_3x3()@(yaw@Vector((1.7,1.4,3)))
    bpy.ops.object.camera_add(location=center+direction.normalized()*4)
    cam=bpy.context.object;cam.rotation_euler=(center-cam.location).to_track_quat('-Z','Y').to_euler();cam.data.type='ORTHO';cam.data.ortho_scale=1.5;scene.camera=cam
    bpy.ops.mesh.primitive_plane_add(size=200,location=(0,0,manifest['sourceFloorY']-.004))
    floor=bpy.context.object;mat=bpy.data.materials.new('Floor');mat.diffuse_color=(.24,.29,.25,1);floor.data.materials.append(mat)
    scene.render.engine='BLENDER_WORKBENCH';scene.display.shading.light='STUDIO';scene.display.shading.color_type='TEXTURE'
    scene.display.shading.show_shadows=True;scene.display.shading.show_cavity=True;scene.display.shading.cavity_type='BOTH'
    scene.render.resolution_x=720;scene.render.resolution_y=540;scene.render.resolution_percentage=100
    scene.render.image_settings.file_format='PNG'
    for index,frame in enumerate(range(0,scene.frame_end+1,3)):
        scene.frame_set(frame);scene.render.filepath=str(folder/(f'{index:04d}.png'));bpy.ops.render.render(write_still=True)
    video=REPORT/(name+'.mp4')
    subprocess.run([str(FFMPEG),'-y','-v','error','-framerate','10','-i',str(folder/'%04d.png'),'-c:v','libx264','-crf','20','-pix_fmt','yuv420p','-movflags','+faststart',str(video)],check=True)
    subprocess.run([str(FFMPEG),'-v','error','-i',str(video),'-f','null','-'],check=True)
print('SQUIRREL_PREVIEW_PASS',len(manifest['clips']))
