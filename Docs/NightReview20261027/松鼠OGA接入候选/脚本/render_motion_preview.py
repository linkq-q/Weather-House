import bpy,math,json,subprocess
from pathlib import Path
from mathutils import Vector
S=Path(__file__).parent;P=S/'preview';frames=S/'motion-frames';frames.mkdir(exist_ok=True)
bpy.ops.wm.open_mainfile(filepath=str(S/'candidate/Squirrel_OGA_Natural_Source.blend'),load_ui=False,use_scripts=False)
scene=bpy.context.scene;rig=next(o for o in bpy.data.objects if o.type=='ARMATURE');bpy.context.view_layer.objects.active=rig
if rig.mode!='OBJECT':bpy.ops.object.mode_set(mode='OBJECT')
act=bpy.data.actions['Run'];rig.animation_data_create();rig.animation_data.action=act
if len(act.slots):rig.animation_data.action_slot=act.slots[0]
for t in rig.animation_data.nla_tracks:t.mute=True
bpy.ops.mesh.primitive_plane_add(size=20,location=(0,0,-.003));floor=bpy.context.object;floor.color=(.47,.53,.49,1)
floor.data.materials.append(bpy.data.materials.new('Motion preview floor'));floor.data.materials[0].diffuse_color=(.47,.53,.49,1)
bpy.ops.object.camera_add(location=(1.25,-1.75,.94));cam=bpy.context.object;cam.rotation_euler=(Vector((0,0,.30))-cam.location).to_track_quat('-Z','Y').to_euler();cam.data.type='ORTHO';cam.data.ortho_scale=1.25;scene.camera=cam
scene.render.engine='BLENDER_WORKBENCH';scene.display.shading.light='STUDIO';scene.display.shading.color_type='MATERIAL';scene.display.shading.background_type='WORLD';scene.display.shading.show_shadows=True;scene.display.shading.show_cavity=False;scene.display.shading.show_specular_highlight=False
scene.world.color=(.62,.68,.65);scene.render.resolution_x=640;scene.render.resolution_y=480;scene.render.resolution_percentage=100;scene.render.image_settings.file_format='PNG';scene.render.film_transparent=False
for i in range(36):
 scene.frame_set(i%9);scene.render.filepath=str(frames/f'{i:04d}.png');bpy.ops.render.render(write_still=True)
ff=Path(r'C:/Users/31483/Desktop/天气屋/3d资产/松鼠骨骼与UniMate实验_20261022/runtime/.venv/Lib/site-packages/imageio_ffmpeg/binaries/ffmpeg-win-x86_64-v7.1.exe')
subprocess.run([str(ff),'-y','-v','error','-framerate','24','-i',str(frames/'%04d.png'),'-c:v','libx264','-crf','18','-pix_fmt','yuv420p','-movflags','+faststart',str(P/'natural-run-native24fps.mp4')],check=True)
subprocess.run([str(ff),'-v','error','-i',str(P/'natural-run-native24fps.mp4'),'-f','null','-'],check=True)
(S/'motion-preview-report.json').write_text(json.dumps({'status':'BLENDER_NATIVE_RUN_PREVIEW_COMPLETE_UNITY_PENDING','frames':36,'fps':24,'sourceAction':'Run','cycleFrames':'0..8; source end frame9 is boundary; original action not edited','renderer':'Workbench MATERIAL studio, specular disabled; visual motion proof, differs from Cycles material still lighting','animationRetimed':False,'file':'preview/natural-run-native24fps.mp4','unityProof':False},indent=2),encoding='utf-8');print('MOTION_PREVIEW_COMPLETE')
