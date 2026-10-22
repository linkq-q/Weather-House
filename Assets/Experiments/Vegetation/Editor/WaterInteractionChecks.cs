using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using ForestSpriteCharacter;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace WeatherHouse.Vegetation.Editor
{
    [InitializeOnLoad]
    public static class WaterInteractionChecks
    {
        const string Key = "WeatherHouse.WaterInteraction.Checks";
        const string Root = "VegetationLabReview";
        const string Output = Root + "/MaterialLab/WaterInteraction";
        const string Frames = Root + "/MaterialLab/WaterInteractionFrames";
        const string Result = Root + "/water-interaction-result.txt";
        const string Checks = Root + "/water-interaction-checks.txt";
        static IEnumerator steps;
        static double started, nextPoll;
        static int previousFrame, firstFrame;
        static string errors;
        static readonly StringBuilder report = new StringBuilder();

        static WaterInteractionChecks()
        {
            EditorApplication.update += Tick;
            EditorApplication.playModeStateChanged += Changed;
        }
        static void Tick()
        {
            if (steps != null)
            {
                try
                {
                    EditorApplication.QueuePlayerLoopUpdate();
                    Require(EditorApplication.timeSinceStartup-started < 300, "真实 Play 检查超过 300 秒。");
                    if (Time.frameCount == previousFrame) return;
                    previousFrame = Time.frameCount;
                    if (!steps.MoveNext()) Finish(null);
                }
                catch (Exception error) { Finish(error); }
                return;
            }
            if (SessionState.GetBool(Key+".pending",false) || EditorApplication.isCompiling || EditorApplication.isUpdating ||
                EditorApplication.timeSinceStartup < nextPoll) return;
            nextPoll = EditorApplication.timeSinceStartup+1;
            string request = Root+"/water-interaction-request.txt";
            if (!File.Exists(request)) return;
            try
            {
                string command = File.ReadAllText(request).Trim(); File.Delete(request);
                if (command == "verify") Begin();
                else if (command == "install")
                {
                    Require(!EditorApplication.isPlayingOrWillChangePlaymode && SceneManager.sceneCount == 1 &&
                        !SceneManager.GetActiveScene().isDirty && !string.IsNullOrEmpty(SceneManager.GetActiveScene().path), "接线需要已保存的单场景。");
                    string original = SceneManager.GetActiveScene().path;
                    try { WaterInteractionSetup.Install(); }
                    finally { if (original != VegetationLabBuilder.ScenePath) EditorSceneManager.OpenScene(original); }
                    File.WriteAllText(Result,"PASS: scene wiring saved; Play/GPU verification not requested.\n");
                }
                else throw new InvalidOperationException("Unknown water interaction request: "+command);
            }
            catch (Exception error) { Finish(error); }
        }

        [MenuItem("Weather House/Vegetation Lab/角色涉水 · 实际 Play 验证并截图")]
        public static void Begin()
        {
            Scene scene = SceneManager.GetActiveScene();
            Require(!SessionState.GetBool(Key+".pending",false) && !EditorApplication.isPlayingOrWillChangePlaymode && SceneManager.sceneCount == 1 && !scene.isDirty &&
                !string.IsNullOrEmpty(scene.path) && File.Exists(scene.path), "验证只从已保存的单场景、非 Play 状态开始。");
            Directory.CreateDirectory(Output); Directory.CreateDirectory(Frames);
            SessionState.SetString(Key+".scene",scene.path);
            SessionState.SetString(Key+".originalHash",Hash(scene.path));
            SessionState.SetBool(Key+".background",Application.runInBackground);
            SessionState.SetBool(Key+".paused",EditorApplication.isPaused);
            SessionState.SetString(Key+".labHash","");
            SessionState.SetString(Key+".materials","");
            SessionState.SetBool(Key+".pending",true);
            File.WriteAllText(Result,"RUNNING\n");
            try
            {
                WaterInteractionSetup.Install();
                SessionState.SetString(Key+".labHash",Hash(VegetationLabBuilder.ScenePath));
                string[] materials = WaterInteractionSetup.Components<Renderer>(SceneManager.GetActiveScene())
                    .SelectMany(r => r.sharedMaterials).Where(m => m && EditorUtility.IsPersistent(m))
                    // Unity's built-in default material has a virtual Library path, not a disk asset.
                    .Select(AssetDatabase.GetAssetPath).Where(File.Exists).Distinct().OrderBy(p => p).ToArray();
                SessionState.SetString(Key+".materials",string.Join("\n",materials.Select(p => p+"|"+Hash(p))));
                var interaction = WaterInteractionSetup.Components<PainterlyWaterInteraction>(SceneManager.GetActiveScene()).Single();
                foreach (Shader shader in new[] { interaction.simulationShader,interaction.waterRenderer.sharedMaterial.shader })
                    Require(shader && shader.isSupported && !ShaderUtil.ShaderHasError(shader), "涉水 shader 不支持或编译失败。");
                SessionState.SetBool(Key+".running",true);
                EditorApplication.isPlaying = true;
            }
            catch (Exception error) { Finish(error); }
        }
        static void Changed(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Key+".running",false))
            {
                report.Clear(); errors=""; started=EditorApplication.timeSinceStartup;
                previousFrame=firstFrame=Time.frameCount;
                report.AppendLine("Evidence: actual Unity Play frames, original ForestSpriteDemoController.SetScriptedInput, GPU normal-field readback and Camera.Render. Not manual keyboard/mouse testing.");
                Application.runInBackground=true; EditorApplication.isPaused=false;
                Application.logMessageReceived+=Log; steps=VerifyPlay();
            }
            if (state == PlayModeStateChange.ExitingPlayMode && SessionState.GetBool(Key+".running",false))
                Finish(new InvalidOperationException("Play stopped before verification completed."));
            if (state == PlayModeStateChange.EnteredEditMode && SessionState.GetBool(Key+".pending",false)) Restore();
        }

        public static IEnumerator VerifyPlay()
        {
            Require(Application.isPlaying && SceneManager.GetActiveScene().path == VegetationLabBuilder.ScenePath,"需要原实验场景实际 Play。");
            report.AppendLine("Readback diagnostic: graphicsDevice="+SystemInfo.graphicsDeviceType+", graphicsUVStartsAtTop="+SystemInfo.graphicsUVStartsAtTop+
                "; peakReadbackUV uses CPU GetPixels row order without a flip. peakWorld assumes that row UV directly matches the field; compare with theoreticalPlayerUV before interpreting Near/Corridor failures.");
            for (int i=0;i<12;i++) yield return null;
            var interaction=WaterInteractionSetup.Components<PainterlyWaterInteraction>(SceneManager.GetActiveScene()).Single();
            var adapter=WaterInteractionSetup.Components<VegetationCharacterPreview>(SceneManager.GetActiveScene()).Single();
            var player=adapter.player; var meadow=adapter.labView.regions[0]; Camera camera=adapter.playerCamera;
            Require(interaction.player == player && interaction.meadow == meadow && interaction.waterRenderer,"原角色/主草地/水 Renderer 接线有效。");
            float walk=player.walkSpeed, run=player.runSpeed, stride=player.walkStride, gravity=player.gravity;
            float oldYaw=player.cameraYaw,oldPitch=player.cameraPitch,oldDistance=player.cameraDistance,oldHeight=player.cameraHeight;
            Vector3 savedPosition=player.transform.position; Quaternion savedRotation=player.transform.rotation;
            bool savedMode=adapter.CharacterMode;
            var renderer=interaction.waterRenderer; Material savedMaterial=renderer.sharedMaterial;
            var frozenMaterial=new Material(savedMaterial) { hideFlags=HideFlags.HideAndDontSave };
            frozenMaterial.SetFloat("_RippleSpeed",0); frozenMaterial.SetFloat("_CausticSpeed",0);
            renderer.sharedMaterial=frozenMaterial;
            try
            {
                adapter.SetCharacterMode(true);
                adapter.TeleportToPondShallows();
                player.SetScriptedInput(Vector2.zero,false,false);
                yield return null; yield return null;
                Vector3 shallowLocal=meadow.transform.InverseTransformPoint(player.transform.position);
                float shallowGround=meadow.GetGroundHeight(shallowLocal.x,shallowLocal.z);
                Require(shallowGround>meadow.waterLevel && shallowGround<meadow.waterLevel+.25f &&
                    adapter.CharacterMode && adapter.labView.meadow==meadow && camera.isActiveAndEnabled,
                    "池塘浅滩入口应将角色放在水位略上的岸边并启用玩家相机。");
                report.AppendLine("Shallow entry: actual TeleportToPondShallows call in Play; ground above water="+
                    (shallowGround-meadow.waterLevel).ToString("F3")+"m; character mode and player camera enabled.");
                player.cameraYaw=215; player.cameraPitch=38; player.cameraDistance=4.6f; player.cameraHeight=.45f;
                player.SetScriptedInput(Vector2.zero,false,false);
                MovePlayer(player,ShorePoint(meadow,210,-.35f));
                var wait=Wait(.6f); while(wait.MoveNext()) yield return null;
                interaction.ClearField(); yield return null;
                Require(interaction.CurrentTexture && interaction.CurrentTexture.IsCreated(),"实际 Play 创建 normal RenderTexture。");
                var dryStart=ReadField(interaction,"00_Normal_Dry");
                Require(dryStart.Maximum < .01f,"空白场应是中性法线。");
                int dryCount=interaction.TotalStampCount, dryFrame=Time.frameCount;
                Vector3 dryPosition=player.transform.position;
                var move=DriveTo(player,ShorePoint(meadow,220,-.35f)); while(move.MoveNext()) yield return null;
                player.SetScriptedInput(Vector2.zero,false,false); yield return null;
                Require(Vector3.Distance(dryPosition,player.transform.position) > .5f && !interaction.IsInWater,"角色在干岸实际行走。");
                var dryEnd=ReadField(interaction,"01_Normal_DryWalk");
                Require(interaction.TotalStampCount == dryCount && dryEnd.Maximum < .01f,"干地行走不得写入水面法线。");
                report.AppendLine("Dry walk: realFrames="+(Time.frameCount-dryFrame)+", distance="+Vector3.Distance(dryPosition,player.transform.position).ToString("F3")+"m; stamps="+interaction.TotalStampCount+"; normalMax="+dryEnd.Maximum.ToString("F6"));

                int entryFrame=Time.frameCount;
                move=DriveTo(player,ShorePoint(meadow,220,.27f)); while(move.MoveNext()) yield return null;
                Require(interaction.IsInWater && interaction.TotalStampCount > dryCount,"真实角色由干岸走入浅水后产生印记。");
                Require(player.IsGrounded && player.CurrentState == "Walk","涉水保持原步行动画与地面接触。");
                report.AppendLine("Entry: realFrames="+(Time.frameCount-entryFrame)+", player="+player.transform.position.ToString("F3")+", stamps="+interaction.TotalStampCount);
                interaction.ClearField();
                Vector3 pathStart=player.transform.position;
                int pathFrame=Time.frameCount, previousStamp=0, movingSamples=0;
                float maxLocal=0, maxRemote=0;
                FieldSample movingField=null;
                for(int i=0;i<12;i++)
                {
                    move=DriveTo(player,ShorePoint(meadow,222.5f+i*2.5f,.27f)); while(move.MoveNext()) yield return null;
                    Require(interaction.IsInWater && interaction.TotalStampCount > previousStamp,"沿浅水移动须持续产生新的法线印记。");
                    previousStamp=interaction.TotalStampCount;
                    movingField=ReadField(interaction,i == 11 ? "02_Normal_Moving" : null);
                    float local=movingField.Near(player.transform.position,interaction.WorldRect,1.2f);
                    Vector3 remote=meadow.transform.TransformPoint(new Vector3(meadow.pondCenter.x,0,meadow.pondCenter.y));
                    float far=movingField.Near(remote,interaction.WorldRect,1);
                    maxLocal=Mathf.Max(maxLocal,local); maxRemote=Mathf.Max(maxRemote,far);
                    if(local > .0002f && local > far*1.5f) movingSamples++;
                    using(var shot=new Picture(Capture(Frames+"/"+i.ToString("000")+".png",camera,960,600)))
                        Require(Mean(shot.texture)>.025f,"移动序列不能为黑图。");
                    Vector4 rect=interaction.WorldRect;
                    Vector2 peakWorld=new Vector2(rect.x+movingField.PeakReadbackUV.x/rect.z,rect.y+movingField.PeakReadbackUV.y/rect.w);
                    report.AppendLine("Moving sample "+i+": frame="+Time.frameCount+", world="+player.transform.position.ToString("F3")+", stamps="+previousStamp+", localNormal="+local.ToString("F6")+", remoteNormal="+far.ToString("F6")+
                        ", peakReadbackUV="+movingField.PeakReadbackUV.ToString("F5")+", peakWorldAssumingDirectRows="+peakWorld.ToString("F3")+
                        ", theoreticalPlayerUV="+interaction.WorldToUV(player.transform.position).ToString("F5")+", peakNormal="+movingField.Maximum.ToString("F6"));
                }
                float pathDistance=Vector3.ProjectOnPlane(player.transform.position-pathStart,Vector3.up).magnitude;
                Require(pathDistance>2 && movingSamples>=6 && movingField.Maximum>.01f,"尾迹须在多个真实路径位置产生，并集中于角色轨迹附近。");
                report.AppendLine("Path direction evidence: "+movingSamples+"/12 GPU samples follow distinct actual world positions along the shore; displacement="+pathDistance.ToString("F3")+"m, realFrames="+(Time.frameCount-pathFrame)+", maxLocal="+maxLocal.ToString("F6")+", maxRemote="+maxRemote.ToString("F6")+". This proves spatial trail tracking, not a physical wake-direction model.");
                using(var shot=new Picture(Capture(Output+"/WaterInteractionMoving.png",camera))) Require(Mean(shot.texture)>.025f,"角色涉水近景有效。");
                VerifySurfaceGPU(renderer,camera);
                player.SetScriptedInput(Vector2.zero,false,false);
                yield return null; yield return null;
                int stoppedCount=interaction.TotalStampCount,stopFrame=Time.frameCount;
                Vector3 stoppedPosition=player.transform.position;
                float initialEnergy=ReadField(interaction,null).Energy;
                Require(initialEnergy>.00001f,"停止前法线场有可测信号。");
                float fadeTime=Mathf.Max(4,5/Mathf.Max(.05f,interaction.decay));
                for(int i=0;i<12;i++)
                {
                    wait=Wait(fadeTime/12); while(wait.MoveNext()) yield return null;
                    using(var shot=new Picture(Capture(Frames+"/"+(i+12).ToString("000")+".png",camera,960,600)))
                        Require(Mean(shot.texture)>.025f,"停止序列不能为黑图。");
                }
                var faded=ReadField(interaction,"03_Normal_Stopped");
                Require(interaction.TotalStampCount == stoppedCount && Vector3.Distance(player.transform.position,stoppedPosition)<.04f,"停止时不得持续写新印记或角色滑移。");
                report.AppendLine("Fade readback before assertion: initial="+initialEnergy.ToString("F9")+", stopped="+faded.Energy.ToString("F9")+", stoppedMax="+faded.Maximum.ToString("F9"));
                Require(faded.Energy < initialEnergy*.25f,"停止后的法线能量应衰减到初始 25% 以下。");
                using(var shot=new Picture(Capture(Output+"/WaterInteractionStopped.png",camera))) Require(Mean(shot.texture)>.025f,"停止对比近景有效。");
                report.AppendLine("Stop/fade: realFrames="+(Time.frameCount-stopFrame)+", simulatedWait="+fadeTime.ToString("F3")+"s; energy="+initialEnergy.ToString("F8")+" -> "+faded.Energy.ToString("F8")+"; no new stamps.");

                // Retrace the already traversed shallow route; avoid the unverified radial 250-degree exit that stalled in the first run.
                for(int i=10;i>=0;i--)
                {
                    move=DriveTo(player,ShorePoint(meadow,222.5f+i*2.5f,.27f)); while(move.MoveNext()) yield return null;
                }
                move=DriveTo(player,ShorePoint(meadow,220,.27f)); while(move.MoveNext()) yield return null;
                move=DriveTo(player,ShorePoint(meadow,220,-.35f)); while(move.MoveNext()) yield return null;
                player.SetScriptedInput(Vector2.zero,false,false); wait=Wait(.3f); while(wait.MoveNext()) yield return null;
                Require(!interaction.IsInWater,"角色真正走出浅水。");
                interaction.ClearField(); yield return null;
                move=DriveTo(player,ShorePoint(meadow,210,-.35f)); while(move.MoveNext()) yield return null;
                player.SetScriptedInput(Vector2.zero,false,false); yield return null;
                Require(interaction.TotalStampCount == 0 && ReadField(interaction,"04_Normal_Exited").Maximum < .01f,"离水后的干地移动不新增法线。");
                report.AppendLine("Exit: true scripted walk reached dry shore; cleared field remained neutral while walking on land.");

                interaction.ClearField(); yield return null;
                int skips=interaction.TeleportSkipCount;
                Vector3 teleportFrom=player.transform.position;
                MovePlayer(player,ShorePoint(meadow,70,.27f));
                player.SetScriptedInput(Vector2.zero,false,false);
                yield return null; yield return null;
                var teleported=ReadField(interaction,"05_Normal_Teleport");
                float bridge=teleported.CorridorMaximum(teleportFrom,player.transform.position,interaction.WorldRect);
                Require(interaction.TeleportSkipCount>skips && bridge<.01f,
                    "跨岸瞬移应跳过印记且不能画出连接整座池塘的长线。");
                report.AppendLine("Teleport: dry shore -> opposite shallow shore; skipCount increased; bridge normalMax="+bridge.ToString("F6")+
                    "; stamps="+interaction.TotalStampCount+". Destination contact ring is allowed; the interior corridor excludes 2m around either endpoint.");
                var allocated=interaction.CurrentTexture;
                interaction.enabled=false;
                yield return null;
                var block=new MaterialPropertyBlock(); renderer.GetPropertyBlock(block);
                Require(!interaction.CurrentTexture && (!allocated || !allocated.IsCreated()),"禁用涉水组件必须释放 RenderTexture。");
                Require(block.GetFloat("_InteractionStrength")==0 && block.GetVector("_InteractionWorldRect")==Vector4.zero && block.GetTexture("_InteractionNormalTex")!=allocated,
                    "禁用必须清除 MPB 强度、区域及失效纹理引用。");
                Require(block.GetFloat("_HasPlanarReflection")>.5f,"禁用涉水不可清除已有平面反射 MPB。");
                interaction.enabled=true; yield return null; yield return null;
                Require(interaction.CurrentTexture && interaction.CurrentTexture.IsCreated(),"重新启用可以重建临时 normal 场。");
                Require(player.walkSpeed==walk && player.runSpeed==run && player.walkStride==stride && player.gravity==gravity,"验证与涉水组件不得改变移速、步幅或重力。");
                report.AppendLine("Disable/re-enable: RT released and MPB interaction cleared; reflection retained; RT recreated. Original walk/run/stride/gravity unchanged.");
            }
            finally
            {
                player.ReleaseScriptedInput();
                player.cameraYaw=oldYaw; player.cameraPitch=oldPitch; player.cameraDistance=oldDistance; player.cameraHeight=oldHeight;
                MovePlayer(player,savedPosition); player.transform.rotation=savedRotation;
                adapter.SetCharacterMode(savedMode);
                interaction.enabled=true;
                if(renderer) renderer.sharedMaterial=savedMaterial;
                if(frozenMaterial) Object.DestroyImmediate(frozenMaterial);
            }
        }

        static IEnumerator DriveTo(ForestSpriteDemoController player,Vector3 target)
        {
            float elapsed=0;
            while(Vector3.ProjectOnPlane(target-player.transform.position,Vector3.up).magnitude>.06f)
            {
                Require(elapsed<15,"角色未能在 15 秒内走到实际浅滩目标。 current="+player.transform.position.ToString("F3")+
                    ", target="+target.ToString("F3")+", grounded="+player.IsGrounded+", collision="+player.GetComponent<CharacterController>().collisionFlags);
                Vector3 forward=Vector3.ProjectOnPlane(player.demoCamera.transform.forward,Vector3.up).normalized;
                Vector3 right=Vector3.Cross(Vector3.up,forward);
                Vector3 delta=Vector3.ProjectOnPlane(target-player.transform.position,Vector3.up).normalized;
                player.SetScriptedInput(new Vector2(Vector3.Dot(delta,right),Vector3.Dot(delta,forward)),false,false);
                yield return null; elapsed+=Mathf.Min(Time.deltaTime,.05f);
            }
        }
        static IEnumerator Wait(float seconds)
        {
            float elapsed=0; while(elapsed<seconds) { yield return null; elapsed+=Mathf.Min(Time.deltaTime,.05f); }
        }
        static Vector3 ShorePoint(GrassMeadow meadow,float degrees,float waterDepth)
        {
            float angle=degrees*Mathf.Deg2Rad,low=0,high=meadow.pondRadius*2;
            Vector2 direction=new Vector2(Mathf.Cos(angle),Mathf.Sin(angle)*.74f), point=Vector2.zero;
            for(int i=0;i<30;i++)
            {
                float radius=(low+high)*.5f; point=meadow.pondCenter+direction*radius;
                if(meadow.GetGroundHeight(point.x,point.y)<meadow.waterLevel-waterDepth) low=radius; else high=radius;
            }
            return meadow.transform.TransformPoint(new Vector3(point.x,meadow.GetGroundHeight(point.x,point.y)+.04f,point.y));
        }
        static void MovePlayer(ForestSpriteDemoController player,Vector3 position)
        {
            player.ResetPosition();
            var controller=player.GetComponent<CharacterController>(); controller.enabled=false;
            player.transform.position=position; controller.enabled=true;
            if(player.secondaryMotion) player.secondaryMotion.ResetSimulation();
            if(player.capeCloth) player.capeCloth.ResetSimulation();
        }
        static void VerifySurfaceGPU(Renderer renderer,Camera camera)
        {
            var saved=new MaterialPropertyBlock(); renderer.GetPropertyBlock(saved);
            Require(saved.GetFloat("_InteractionStrength")>0 && saved.GetTexture("_InteractionNormalTex"),"水面收到非零尾迹 MPB。");
            using(var on=new Picture(Capture(Output+"/WaterInteractionOn.png",camera)))
            {
                var off=new MaterialPropertyBlock(); renderer.GetPropertyBlock(off); off.SetFloat("_InteractionStrength",0);
                try
                {
                    renderer.SetPropertyBlock(off);
                    using(var bypass=new Picture(Capture(Output+"/WaterInteractionOff.png",camera)))
                    {
                        Color32[] a=on.texture.GetPixels32(),b=bypass.texture.GetPixels32(); int changed=0; double delta=0;
                        for(int i=0;i<a.Length;i++) { int d=Math.Abs(a[i].r-b[i].r)+Math.Abs(a[i].g-b[i].g)+Math.Abs(a[i].b-b[i].b); delta+=d; if(d>=3) changed++; }
                        float mean=(float)(delta/(a.Length*765.0));
                        Require(changed>=20 && mean>.000001f,"同帧尾迹开关必须改变水面 GPU 像素。");
                        report.AppendLine("Same-frame water interaction on/off GPU comparison: changedRGB>=3 pixels="+changed+", meanDelta="+mean.ToString("F8")+"; no frame advanced between renders.");
                    }
                }
                finally { renderer.SetPropertyBlock(saved); }
            }
        }
        sealed class FieldSample
        {
            public Color[] pixels; public int width,height; public float Energy,Maximum;
            public Vector2 PeakReadbackUV;
            public float CorridorMaximum(Vector3 from,Vector3 to,Vector4 rect)
            {
                Vector2 start=new Vector2(from.x,from.z), delta=new Vector2(to.x-from.x,to.z-from.z);
                float margin=2/delta.magnitude,maximum=0;
                Require(delta.magnitude>8,"瞬移验证需跨过足够长的水面。");
                for(int y=0;y<height;y++) for(int x=0;x<width;x++)
                {
                    Vector2 point=new Vector2((x+.5f)/width/rect.z+rect.x,(y+.5f)/height/rect.w+rect.y);
                    float t=Vector2.Dot(point-start,delta)/delta.sqrMagnitude;
                    if(t<margin || t>1-margin || (point-start-delta*t).sqrMagnitude>.64f) continue;
                    Color p=pixels[y*width+x]; maximum=Mathf.Max(maximum,Mathf.Abs(p.r-.5f)+Mathf.Abs(p.g-.5f));
                }
                return maximum;
            }
            public float Near(Vector3 world,Vector4 rect,float radius)
            {
                double sum=0; int count=0;
                for(int y=0;y<height;y++) for(int x=0;x<width;x++)
                {
                    float wx=(x+.5f)/width/rect.z+rect.x,wz=(y+.5f)/height/rect.w+rect.y;
                    if((wx-world.x)*(wx-world.x)+(wz-world.z)*(wz-world.z)>radius*radius) continue;
                    Color p=pixels[y*width+x]; sum+=Mathf.Abs(p.r-.5f)+Mathf.Abs(p.g-.5f); count++;
                }
                return count==0 ? 0 : (float)(sum/count);
            }
        }
        static FieldSample ReadField(PainterlyWaterInteraction interaction,string name)
        {
            var source=interaction.CurrentTexture;
            Require(source && source.IsCreated(),"GPU normal 场存在。");
            RenderTexture prior=RenderTexture.active;
            var read=new Texture2D(source.width,source.height,TextureFormat.RGBAFloat,false,true);
            try
            {
                RenderTexture.active=source; read.ReadPixels(new Rect(0,0,source.width,source.height),0,0); read.Apply();
                var sample=new FieldSample { pixels=read.GetPixels(),width=source.width,height=source.height };
                double sum=0;
                for(int index=0;index<sample.pixels.Length;index++)
                {
                    Color pixel=sample.pixels[index];
                    Require(!float.IsNaN(pixel.r) && !float.IsInfinity(pixel.r) && !float.IsNaN(pixel.g) && !float.IsInfinity(pixel.g) &&
                        !float.IsNaN(pixel.b) && !float.IsInfinity(pixel.b),"GPU 法线没有 NaN/Infinity。");
                    float signal=Mathf.Abs(pixel.r-.5f)+Mathf.Abs(pixel.g-.5f); sum+=signal;
                    if(signal>sample.Maximum)
                    {
                        sample.Maximum=signal;
                        sample.PeakReadbackUV=new Vector2((index%sample.width+.5f)/sample.width,(index/sample.width+.5f)/sample.height);
                    }
                }
                sample.Energy=(float)(sum/sample.pixels.Length);
                if(name!=null)
                {
                    var preview=new Texture2D(source.width,source.height,TextureFormat.RGB24,false,true);
                    try { preview.SetPixels(sample.pixels); preview.Apply(); File.WriteAllBytes(Output+"/"+name+".png",preview.EncodeToPNG()); }
                    finally { Object.DestroyImmediate(preview); }
                }
                return sample;
            }
            finally { RenderTexture.active=prior; Object.DestroyImmediate(read); }
        }
        static Texture2D Capture(string path,Camera camera,int width=1600,int height=1000)
        {
            var target=RenderTexture.GetTemporary(width,height,24,RenderTextureFormat.ARGB32);
            var previous=camera.targetTexture; var active=RenderTexture.active;
            var texture=new Texture2D(width,height,TextureFormat.RGB24,false);
            try
            {
                camera.targetTexture=target; camera.Render(); RenderTexture.active=target;
                texture.ReadPixels(new Rect(0,0,width,height),0,0); texture.Apply(); File.WriteAllBytes(path,texture.EncodeToPNG()); return texture;
            }
            catch { Object.DestroyImmediate(texture); throw; }
            finally { camera.targetTexture=previous; RenderTexture.active=active; RenderTexture.ReleaseTemporary(target); }
        }
        sealed class Picture : IDisposable { public readonly Texture2D texture; public Picture(Texture2D value) { texture=value; } public void Dispose() { if(texture) Object.DestroyImmediate(texture); } }
        static float Mean(Texture2D texture) => texture.GetPixels32().Average(c => (c.r+c.g+c.b)/765f);
        static void Finish(Exception error)
        {
            var pending=steps; steps=null; SessionState.SetBool(Key+".running",false);
            try { (pending as IDisposable)?.Dispose(); } catch(Exception cleanup) { error=error ?? cleanup; }
            Application.logMessageReceived-=Log;
            if(error==null && !string.IsNullOrEmpty(errors)) error=new Exception(errors);
            Directory.CreateDirectory(Output);
            report.AppendLine("Actual Play frames="+(Time.frameCount-firstFrame)+"; wallSeconds="+(EditorApplication.timeSinceStartup-started).ToString("F2"));
            File.WriteAllText(Checks,report.ToString());
            File.WriteAllText(Result,error==null ? "PASS: real character input, GPU normal field, water surface A/B and release checks.\n" : "FAIL: "+error+"\n");
            if(EditorApplication.isPlaying) EditorApplication.isPlaying=false;
            else if(SessionState.GetBool(Key+".pending",false)) Restore();
        }
        static void Restore()
        {
            SessionState.SetBool(Key+".pending",false);
            Application.runInBackground=SessionState.GetBool(Key+".background",false);
            EditorApplication.isPaused=SessionState.GetBool(Key+".paused",false);
            string original=SessionState.GetString(Key+".scene","");
            try
            {
                string expected=SessionState.GetString(Key+".labHash","");
                if(expected.Length>0) Require(Hash(VegetationLabBuilder.ScenePath)==expected,"Play 不得改写已接线场景。");
                if(original!=VegetationLabBuilder.ScenePath) Require(Hash(original)==SessionState.GetString(Key+".originalHash",""),"原始场景文件保持不变。");
                foreach(string line in SessionState.GetString(Key+".materials","").Split(new[] {'\n'},StringSplitOptions.RemoveEmptyEntries))
                {
                    int divider=line.LastIndexOf('|'); Require(Hash(line.Substring(0,divider))==line.Substring(divider+1),"持久材质文件不得改变："+line.Substring(0,divider));
                }
                File.AppendAllText(Checks,"PASS saved material files and installed lab scene unchanged during Play; installation intentionally saves only the new component wiring.\n");
            }
            catch(Exception error) { File.WriteAllText(Result,"FAIL RESTORE: "+error+"\nPrevious result: "+File.ReadAllText(Result)); }
            finally
            {
                try
                {
                    if(!string.IsNullOrEmpty(original) && File.Exists(original)) EditorSceneManager.OpenScene(original,OpenSceneMode.Single);
                    Require(SceneManager.GetActiveScene().path==original && !SceneManager.GetActiveScene().isDirty,"恢复原保存场景。");
                    File.AppendAllText(Checks,"PASS restored original saved scene: "+original+"\n");
                }
                catch(Exception error) { File.AppendAllText(Result,"FAIL SCENE RESTORE: "+error+"\n"); }
            }
        }
        static string Hash(string path) { using(var sha=SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))); }
        static void Log(string message,string stack,LogType type) { if(type==LogType.Error || type==LogType.Exception || type==LogType.Assert) errors+=message+"\n"; }
        static void Require(bool condition,string message) { if(!condition) throw new InvalidOperationException("WaterInteractionChecks: "+message); }
    }
}
