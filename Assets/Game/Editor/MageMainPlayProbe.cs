using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using ForestSpriteCharacter;
using WeatherHouse.WatersideV2;
using WeatherHousePrototype;
using Object = UnityEngine.Object;

namespace WeatherHouse.Review
{
    // Called only by the existing protected RuralMainPlayReview lifecycle.
    public static class MageMainPlayProbe
    {
        [Serializable] public sealed class Sample
        {
            public float seconds, speed, soleGap, skinCalibrationError, hatAngle;
            public string phase, state; public Vector3 position; public bool grounded;public float[] shoeGaps;
        }
        [Serializable] public sealed class Report
        {
            public string result="RUNNING",error="",utc;
            public string evidence="Saved main scene, original spawn/camera/capsule and SetScriptedInput. A locally collision-checked 3 m out-and-back path, real stop and turn. Game screenshots are unmodified. Shoe coordinates use independent CPU skinning calibrated against BakeMesh(true); support is raycast below each shoe's lowest vertex. No teleport, direct Move, physics/quality/time changes or performance test. Numeric PASS does not assert visual acceptance.";
            public bool restored,configurationUnchanged;
            public Vector3 playerStart,playerEnd,turnPoint;
            public float elapsed,travelled,minSoleGap=999,maxSupportGap=-999,maxIdleShoeGap=-999,skinCalibrationError,maxHatAngle,maxHatOffset,walkFootRange,measuredWalkSpeed,turnDegrees;
            public int samples,walkSamples,idleSamples,particles,secondaryBones,bindings;
            public float walkSpeed,walkCycle,runCycle,clothSolver;
            public string[] shoeRendererNames;public Vector3[] shoeRendererScales;
            public List<Sample> trace=new List<Sample>();public List<string> images=new List<string>();
        }
        sealed class Skin
        {
            public SkinnedMeshRenderer renderer;public Vector3[] vertices;public BoneWeight[] weights;public Matrix4x4[] bind,matrices;
        }
        [Serializable] sealed class ContactPoint
        {
            public int vertex;public Vector3 cpu,baked,support,normal;public string collider;public float gap;
        }
        [Serializable] sealed class ShoeContact
        {
            public string renderer;public Vector3 scale;public Bounds bounds;public ContactPoint[] points;
        }
        [Serializable] sealed class ContactFailure
        {
            public int frame,phase;public double gameTime;public float phaseAge,capsuleBottom;public Vector3 root,velocity;public Quaternion rotation;
            public bool grounded,inTransition;public int currentHash,nextHash;public float currentTime,nextTime,transitionTime;
            public string[] currentClips,nextClips;public ShoeContact[] shoes;public string screenshotError;
        }
        static void SaveContactFailure()
        {
            var r=new ContactFailure {frame=Time.frameCount,phase=phase,gameTime=Time.timeAsDouble,phaseAge=Time.realtimeSinceStartup-phaseBegan,
                root=player.transform.position,rotation=player.transform.rotation,velocity=player.ActualVelocity,grounded=player.IsGrounded,
                capsuleBottom=capsule.bounds.min.y,inTransition=player.animator.IsInTransition(0),currentHash=player.animator.GetCurrentAnimatorStateInfo(0).fullPathHash,
                nextHash=player.animator.GetNextAnimatorStateInfo(0).fullPathHash,currentTime=player.animator.GetCurrentAnimatorStateInfo(0).normalizedTime,
                nextTime=player.animator.GetNextAnimatorStateInfo(0).normalizedTime,transitionTime=player.animator.GetAnimatorTransitionInfo(0).normalizedTime,
                currentClips=player.animator.GetCurrentAnimatorClipInfo(0).Select(c=>c.clip.name+" weight="+c.weight).ToArray(),
                nextClips=player.animator.GetNextAnimatorClipInfo(0).Select(c=>c.clip.name+" weight="+c.weight).ToArray()};
            r.shoes=skins.Select(s=>
            {
                s.renderer.BakeMesh(baked,true);var bakedPoints=baked.vertices;
                var points=Enumerable.Range(0,s.vertices.Length).Select(i=>
                {
                    var w=s.weights[i];var v=s.vertices[i];
                    var p=s.matrices[w.boneIndex0].MultiplyPoint3x4(v)*w.weight0+s.matrices[w.boneIndex1].MultiplyPoint3x4(v)*w.weight1+
                        s.matrices[w.boneIndex2].MultiplyPoint3x4(v)*w.weight2+s.matrices[w.boneIndex3].MultiplyPoint3x4(v)*w.weight3;
                    return new ContactPoint{vertex=i,cpu=p,baked=s.renderer.transform.TransformPoint(bakedPoints[i])};
                }).OrderBy(p=>p.cpu.y).Take(4).ToArray();
                foreach(var p in points){var h=Surface(p.cpu,capsule.bounds.min.y);p.support=h.point;p.normal=h.normal;p.gap=p.cpu.y-h.point.y;
                    p.collider=h.collider.GetType().Name+" layer="+h.collider.gameObject.layer+" /"+string.Join("/",h.collider.GetComponentsInParent<Transform>().Reverse().Select(t=>t.name));}
                return new ShoeContact{renderer=s.renderer.name,scale=s.renderer.transform.lossyScale,bounds=s.renderer.bounds,points=points};
            }).ToArray();
            Texture2D snapshot=null;
            try{snapshot=ScreenCapture.CaptureScreenshotAsTexture();File.WriteAllBytes(Path.Combine(directory,"contact_failure_Game.png"),snapshot.EncodeToPNG());}
            catch(Exception e){r.screenshotError=e.ToString();}
            finally{if(snapshot)Object.DestroyImmediate(snapshot);}
            File.WriteAllText(Path.Combine(directory,"contact-failure.json"),JsonUtility.ToJson(r,true));
        }
        static ForestSpriteDemoController player;static CharacterController capsule;static GameplaySession session;static GameplayHud hud;static Camera camera;
        static ForestSpriteCapeCloth cape;static ForestSpriteSecondaryMotion secondary;static Cloth cloth;static MageAccessoryPose pose;
        static Report report;static string directory,notebook,capsuleSettings,secondarySettings,capeSettings;
        static ForestSpriteDemoController.Demonstration demonstration;static Vector3 rootScale,previous;static Quaternion outboundHeading;
        static bool blocked,jumpBlocked,cameraBlocked,external,managed,running;static Camera movementOverride,demoCamera;
        static double previousGameTime; static float began,phaseBegan,lastProgress,bestDistance,nextSample,previousTime;static int phase,lastFrame,lastRenderFrame;
        static int quality,vsync,targetFps,captureFps;static float fixedDt,timeScale;static RenderPipelineAsset pipeline;
        static Transform[] originalBones;static ClothSphereColliderPair[] spheres;static CapsuleCollider[] clothCapsules;
        static Skin[] skins;static Mesh baked;static Transform foot;static float footMin,footMax,speedSum;static int speedCount;
        static readonly HashSet<string> captured=new HashSet<string>();
        public static bool IsComplete { get; private set; }
        public static string Failure=>IsComplete&&report!=null&&report.result!="PASS"?report.error:null;
        static void Require(bool value,string reason){if(!value)throw new InvalidOperationException(reason);}
        static float Flat(Vector3 a,Vector3 b)=>Vector2.Distance(new Vector2(a.x,a.z),new Vector2(b.x,b.z));
        static bool Finite(Vector3 v)=>float.IsFinite(v.x)&&float.IsFinite(v.y)&&float.IsFinite(v.z);
        static bool InputAvailable()=>!player.InputBlocked&&!hud.BlocksMovement&&!hud.CameraMode&&!hud.IsAlbumOpening&&!session.Judging&&!(session.PhotoRig&&session.PhotoRig.Active);
        static void BindPlayer()
        {
            session=Object.FindObjectOfType<GameplaySession>();player=session?session.forestSprite:null;camera=session?session.PlayerViewCamera:null;
            hud=session?session.GetComponent<GameplayHud>():null;capsule=player?player.GetComponent<CharacterController>():null;
            Require(session&&player&&camera&&hud&&capsule&&capsule.enabled&&player.enabled,"Missing enabled real scene player/camera/capsule.");
            var body=player.transform.Find("MageBodyVisual");
            Require(body&&body.GetComponent<Animator>()==player.animator&&player.animator.runtimeAnimatorController,"Real player is not bound to MageBodyVisual Animator.");
            cape=player.capeCloth;secondary=player.secondaryMotion;cloth=cape?cape.cloth:null;pose=player.GetComponent<MageAccessoryPose>();
            var accents=player.GetComponent<MageLocomotionPresentation>();
            Require(cape&&cape.enabled&&cloth&&cloth.enabled&&secondary&&secondary.enabled&&secondary.MotionEnabled&&secondary.animator==player.animator,"Original cape/secondary-motion binding is inactive.");
            Require(pose&&pose.enabled&&pose.bindings.Length==11&&pose.bindings.All(b=>b.source&&b.target)&&accents&&accents.enabled&&accents.player==player,"Accessory mapping/presentation binding missing.");
            Require(secondary.chains.Sum(c=>c.bones.Length)==13&&cloth.clothSolverFrequency==480,"Original 13 secondary bones / 480 Hz cloth changed.");
            Require(player.walkSpeed==2.925f&&player.originalWalkCycleDistance==1.7f&&player.walkCycleDistance==1.7f&&player.runCycleDistance==1.7f,"Expected 2.925 speed and 1.7 m cycles.");
        }
        public static void Preflight()
        {
            Require(!EditorApplication.isPlayingOrWillChangePlaymode,"Preflight requires Edit mode.");BindPlayer();
            Require(!player.ScriptedInput,"Another scripted-input owner is active.");
        }
        public static string Begin(string folder)
        {
            Require(!running&&EditorApplication.isPlaying&&!EditorApplication.isPaused,"Requires protected unpaused Play.");BindPlayer();
            Require(!player.ScriptedInput&&InputAvailable(),"Main-map input is blocked or owned; cannot certify locomotion.");
            directory=folder;Directory.CreateDirectory(directory);
            report=new Report{utc=DateTime.UtcNow.ToString("O"),playerStart=player.transform.position,walkSpeed=player.walkSpeed,walkCycle=player.walkCycleDistance,runCycle=player.runCycleDistance,
                particles=cloth.vertices.Length,secondaryBones=13,bindings=pose.bindings.Length,clothSolver=cloth.clothSolverFrequency};
            notebook=JsonUtility.ToJson(session.Notebook);demonstration=player.demonstration;blocked=player.InputBlocked;jumpBlocked=player.JumpBlocked;cameraBlocked=player.CameraInputBlocked;
            external=player.externalCameraControl;managed=player.GameplayInputManaged;movementOverride=player.MovementCameraOverride;demoCamera=player.demoCamera;
            capsuleSettings=EditorJsonUtility.ToJson(capsule);secondarySettings=JsonUtility.ToJson(secondary);capeSettings=JsonUtility.ToJson(cape.settings);
            originalBones=secondary.chains.SelectMany(c=>c.bones).ToArray();spheres=cloth.sphereColliders;clothCapsules=cloth.capsuleColliders;rootScale=player.transform.localScale;
            quality=QualitySettings.GetQualityLevel();pipeline=GraphicsSettings.currentRenderPipeline;vsync=QualitySettings.vSyncCount;targetFps=Application.targetFrameRate;
            fixedDt=Time.fixedDeltaTime;timeScale=Time.timeScale;captureFps=Time.captureFramerate;
            phase=0;lastFrame=lastRenderFrame=-1;began=phaseBegan=lastProgress=previousTime=Time.realtimeSinceStartup;nextSample=began;previous=player.transform.position;previousGameTime=Time.timeAsDouble;
            footMin=float.PositiveInfinity;footMax=float.NegativeInfinity;speedSum=0;speedCount=0;captured.Clear();IsComplete=false;running=true;
            EditorApplication.update+=Tick;RenderPipelineManager.endCameraRendering+=Rendered;
            try
            {
                Require(report.particles==411,"Original cloth topology is not 411 particles.");
                skins=player.GetComponentsInChildren<SkinnedMeshRenderer>().Where(r=>r.name.StartsWith("Mage_Leg",StringComparison.Ordinal)).Select(r=>new Skin{
                    renderer=r,vertices=r.sharedMesh.vertices,weights=r.sharedMesh.boneWeights,bind=r.sharedMesh.bindposes,matrices=new Matrix4x4[r.bones.Length]}).ToArray();
                Require(skins.Length==2,"Expected the two original Mage leg/shoe skins.");
                report.shoeRendererNames=skins.Select(s=>s.renderer.name).ToArray();report.shoeRendererScales=skins.Select(s=>s.renderer.transform.lossyScale).ToArray();
                foot=player.animator.GetComponentsInChildren<Transform>().Single(t=>t.name=="foot.l");baked=new Mesh();
                report.turnPoint=ChooseStraightPath();player.SetScriptedInput(Vector2.zero,false,false);
                File.WriteAllText(Path.Combine(directory,"planned.json"),JsonUtility.ToJson(report,true));
            }
            catch(Exception e){report.error=e.ToString();Finish("FAILED_TO_START");throw;}
            return directory;
        }
        static RaycastHit Surface(Vector3 p,float expectedY)
        {
            var hits=Physics.RaycastAll(new Vector3(p.x,expectedY+1,p.z),Vector3.down,2.5f,~0,QueryTriggerInteraction.Ignore)
                .Where(h=>!h.collider.transform.IsChildOf(player.transform)&&h.normal.y>.5f).OrderBy(h=>Mathf.Abs(h.point.y-expectedY)).ToArray();
            Require(hits.Length>0,"No physical ground below the shoe/path at "+p);return hits[0];
        }
        static Vector3 ChooseStraightPath()
        {
            var start=player.transform.position;float floor=Surface(start,capsule.bounds.min.y).point.y;
            float radius=capsule.radius*Mathf.Max(Mathf.Abs(player.transform.lossyScale.x),Mathf.Abs(player.transform.lossyScale.z));
            float half=Mathf.Max(radius,capsule.height*Mathf.Abs(player.transform.lossyScale.y)*.5f);
            foreach(float angle in new[]{0f,45f,-45f,90f,-90f,135f,-135f,180f})
            {
                Vector3 direction=Quaternion.Euler(0,angle,0)*Vector3.ProjectOnPlane(player.transform.forward,Vector3.up).normalized;bool clear=true;
                for(int step=1;step<=12&&clear;step++)
                {
                    Vector3 at=start+direction*(step*.25f);var support=Surface(at,floor);
                    if(Mathf.Abs(support.point.y-floor)>.12f||support.normal.y<.97f||(WatersideLayout.IsWater(at.x,at.z,.25f)&&WatersideLayout.CrossingAt(at.x,at.z,.4f)==null)){clear=false;break;}
                    Vector3 center=player.transform.TransformPoint(capsule.center)+(at-start)+Vector3.up*(support.point.y-floor);
                    float r=Mathf.Max(.01f,radius-capsule.skinWidth*.5f);
                    var overlaps=Physics.OverlapCapsule(center+Vector3.up*(half-radius),center-Vector3.up*(half-radius)+Vector3.up*.04f,r,~0,QueryTriggerInteraction.Ignore);
                    clear=!overlaps.Any(c=>!c.transform.IsChildOf(player.transform)&&c!=support.collider&&!(c is TerrainCollider));
                }
                if(clear)return start+direction*3;
            }
            throw new InvalidOperationException("No clear near-level 3 m path from actual spawn. Input traversal not run; actor/collisions retained.");
        }
        static void CheckConfiguration()
        {
            Require(player.enabled&&capsule.enabled&&player.ScriptedInput&&InputAvailable(),"Main input blocked, player/capsule disabled, or input ownership lost.");
            Require(EditorJsonUtility.ToJson(capsule)==capsuleSettings&&player.transform.localScale==rootScale,"Capsule/root scale changed during review.");
            Require(JsonUtility.ToJson(secondary)==secondarySettings&&JsonUtility.ToJson(cape.settings)==capeSettings&&originalBones.SequenceEqual(secondary.chains.SelectMany(c=>c.bones)),"Retained spring/cape settings or bone order changed.");
            Require(cloth.enabled&&cloth.clothSolverFrequency==480&&clothCapsules.SequenceEqual(cloth.capsuleColliders)&&spheres.SequenceEqual(cloth.sphereColliders),"Cloth solver/collision references changed.");
            Require(player.walkSpeed==2.925f&&player.originalWalkCycleDistance==1.7f&&player.walkCycleDistance==1.7f&&player.runCycleDistance==1.7f,"Production movement/cadence changed.");
            Require(QualitySettings.GetQualityLevel()==quality&&GraphicsSettings.currentRenderPipeline==pipeline&&QualitySettings.vSyncCount==vsync&&Application.targetFrameRate==targetFps&&Time.fixedDeltaTime==fixedDt&&Time.timeScale==timeScale&&Time.captureFramerate==captureFps,"Quality/time/physics options changed.");
            Require(secondary.HasFiniteState,"Secondary physics became non-finite.");
        }
        static void SetPhase(int next)
        {phase=next;phaseBegan=lastProgress=Time.realtimeSinceStartup;bestDistance=float.PositiveInfinity;player.SetScriptedInput(Vector2.zero,false,false);}
        static void Tick()
        {
            if(!running||!EditorApplication.isPlaying||EditorApplication.isPaused||lastFrame==Time.frameCount)return;lastFrame=Time.frameCount;
            try
            {
                float now=Time.realtimeSinceStartup;Require(now-began<25,"Mage sample exceeded 25 seconds; blocked/incomplete route.");CheckConfiguration();
                float moved=Vector3.Distance(player.transform.position,previous); double gameDt=Time.timeAsDouble-previousGameTime; Require(moved<Mathf.Max(.5f,player.walkSpeed*(float)gameDt*2),"Unexpected player displacement/reset: moved="+moved+", gameDt="+gameDt+", realDt="+(now-previousTime));
                report.travelled+=moved;previous=player.transform.position;previousTime=now;previousGameTime=Time.timeAsDouble;
                if(phase==0||phase==2||phase==4)
                {
                    player.SetScriptedInput(Vector2.zero,false,false);
                    if(now-phaseBegan>=1.5f&&player.CurrentState=="Idle"&&player.IsGrounded)
                    {
                        if(phase==4){Require(report.images.All(p=>File.Exists(p)&&new FileInfo(p).Length>1024),"Game capture not written.");Finish("COMPLETE");return;}
                        SetPhase(phase+1);
                    }
                    return;
                }
                Vector3 target=phase==1?report.turnPoint:report.playerStart;float distance=Flat(player.transform.position,target);
                if(distance<.16f&&player.IsGrounded){if(phase==1)outboundHeading=player.transform.rotation;SetPhase(phase+1);return;}
                if(distance<bestDistance-.04f){bestDistance=distance;lastProgress=now;}
                Require(now-lastProgress<2.5f,"Blocked original controller near "+player.transform.position+" toward "+target);
                Vector3 direction=Vector3.ProjectOnPlane(target-player.transform.position,Vector3.up).normalized;
                var inputCamera=player.MovementCameraOverride?player.MovementCameraOverride:player.demoCamera;
                Vector3 forward=inputCamera?Vector3.ProjectOnPlane(inputCamera.transform.forward,Vector3.up).normalized:Vector3.forward;
                Require(forward.sqrMagnitude>.5f,"Movement camera has no horizontal basis.");
                player.SetScriptedInput(new Vector2(Vector3.Dot(direction,Vector3.Cross(Vector3.up,forward)),Vector3.Dot(direction,forward)),false,false);
            }
            catch(Exception e){report.error=e.ToString();Finish("FAILED");}
        }
        static void Rendered(ScriptableRenderContext context,Camera rendered)
        {
            if(!running||rendered!=camera||lastRenderFrame==Time.frameCount)return;lastRenderFrame=Time.frameCount;
            try
            {
                float now=Time.realtimeSinceStartup;
                string label=phase==0?"initial_idle":phase==1?"outbound_walk":phase==2?"stopped_idle":phase==3?"return_walk":"final_idle";
                if(now-phaseBegan>.45f&&!captured.Contains(label))Capture(label);
                if(phase==3&&now-phaseBegan<.3f&&!captured.Contains("turn"))Capture("turn");
                if(now<nextSample)return;nextSample=now+.1f;CheckConfiguration();
                float minGap=float.PositiveInfinity,maxGap=float.NegativeInfinity,error=0;var shoeGaps=new float[skins.Length];int shoeIndex=0;
                foreach(var skin in skins)
                {
                    for(int b=0;b<skin.matrices.Length;b++)skin.matrices[b]=skin.renderer.bones[b].localToWorldMatrix*skin.bind[b];
                    skin.renderer.BakeMesh(baked,true);var points=baked.vertices;Vector3 lowest=Vector3.up*float.MaxValue;
                    Require(points.Length==skin.vertices.Length,"CPU/BakeMesh vertex topology mismatch.");
                    for(int i=0;i<points.Length;i++)
                    {
                        var w=skin.weights[i];var v=skin.vertices[i];
                        Vector3 cpu=skin.matrices[w.boneIndex0].MultiplyPoint3x4(v)*w.weight0+skin.matrices[w.boneIndex1].MultiplyPoint3x4(v)*w.weight1+skin.matrices[w.boneIndex2].MultiplyPoint3x4(v)*w.weight2+skin.matrices[w.boneIndex3].MultiplyPoint3x4(v)*w.weight3;
                        Require(Finite(cpu),"Non-finite shoe vertices.");error=Mathf.Max(error,Vector3.Distance(cpu,skin.renderer.transform.TransformPoint(points[i])));if(cpu.y<lowest.y)lowest=cpu;
                    }
                    float gap=lowest.y-Surface(lowest,capsule.bounds.min.y).point.y;
                    shoeGaps[shoeIndex++]=gap;
                    minGap=Mathf.Min(minGap,gap);maxGap=Mathf.Max(maxGap,gap);
                }
                Require(error<.0001f,"CPU / BakeMesh(true) calibration differs by at least 0.1 mm.");
                if(!(minGap>-.03f&&minGap<.05f))SaveContactFailure();
                Require(minGap>-.03f&&minGap<.05f,"Shoe support gap outside -30/+50 mm: "+minGap);
                Require(player.IsGrounded,"Original controller lost ground during short dry walk.");
                foreach(var v in cloth.vertices)Require(Finite(v),"Non-finite cloth vertex.");
                foreach(var r in player.GetComponentsInChildren<Renderer>())Require(Finite(r.bounds.center)&&Finite(r.bounds.extents),"Non-finite player bounds.");
                float speed=Vector3.ProjectOnPlane(player.ActualVelocity,Vector3.up).magnitude;
                report.samples++;report.minSoleGap=Mathf.Min(report.minSoleGap,minGap);report.maxSupportGap=Mathf.Max(report.maxSupportGap,minGap);report.skinCalibrationError=Mathf.Max(report.skinCalibrationError,error);
                report.maxHatAngle=Mathf.Max(report.maxHatAngle,secondary.MaxCurrentAngle);report.maxHatOffset=Mathf.Max(report.maxHatOffset,secondary.MaximumOffset);
                if((phase==1||phase==3)&&now-phaseBegan>.3f&&speed>.5f)
                {
                    report.walkSamples++;float z=player.transform.InverseTransformPoint(foot.position).z;footMin=Mathf.Min(footMin,z);footMax=Mathf.Max(footMax,z);speedSum+=speed;speedCount++;
                    if(phase==3)report.turnDegrees=Mathf.Max(report.turnDegrees,Quaternion.Angle(outboundHeading,player.transform.rotation));
                }
                else if(player.CurrentState=="Idle"&&now-phaseBegan>.5f)
                {report.idleSamples++;report.maxIdleShoeGap=Mathf.Max(report.maxIdleShoeGap,maxGap);Require(speed<.05f,"Stopped Idle still travelling.");Require(maxGap<.05f,"One stopped shoe remains more than 50 mm above its support.");}
                report.trace.Add(new Sample{seconds=now-began,phase=label,state=player.CurrentState,position=player.transform.position,grounded=player.IsGrounded,speed=speed,soleGap=minGap,shoeGaps=shoeGaps,skinCalibrationError=error,hatAngle=secondary.MaxCurrentAngle});
            }
            catch(Exception e){report.error=e.ToString();Finish("FAILED");}
        }
        static void Capture(string label)
        {string path=Path.Combine(directory,label+"_Game.png");ScreenCapture.CaptureScreenshot(path);captured.Add(label);report.images.Add(path);}
        public static string Finish(string status)
        {
            if(!running)return directory;running=false;EditorApplication.update-=Tick;RenderPipelineManager.endCameraRendering-=Rendered;
            try
            {
                report.elapsed=Time.realtimeSinceStartup-began;report.playerEnd=player.transform.position;report.walkFootRange=footMax-footMin;report.measuredWalkSpeed=speedCount>0?speedSum/speedCount:0;
                CheckConfiguration();report.configurationUnchanged=true;
                if(status=="COMPLETE")Require(phase==4&&report.walkSamples>=10&&report.idleSamples>=10&&report.turnDegrees>160&&report.travelled>5&&report.walkFootRange>.35f&&report.measuredWalkSpeed>2.5f&&report.maxHatAngle>.01f&&captured.Count==6,"Walk/stop/turn/stride/spring/Game evidence incomplete.");
            }
            catch(Exception e){report.error+="\n"+e;}
            finally
            {
                try
                {
                    player.SetScriptedInput(Vector2.zero,false,false);player.ReleaseScriptedInput();player.SetDemonstration(demonstration);
                    JsonUtility.FromJsonOverwrite(notebook,session.Notebook);
                    report.restored=!player.ScriptedInput&&player.demonstration==demonstration&&player.InputBlocked==blocked&&player.JumpBlocked==jumpBlocked&&player.CameraInputBlocked==cameraBlocked&&player.externalCameraControl==external&&player.GameplayInputManaged==managed&&player.MovementCameraOverride==movementOverride&&player.demoCamera==demoCamera&&JsonUtility.ToJson(session.Notebook)==notebook;
                }
                catch(Exception e){report.error+="\nRestore: "+e;report.restored=false;}
                if(baked)Object.DestroyImmediate(baked);baked=null;
            }
            report.result=status=="COMPLETE"&&report.restored&&report.configurationUnchanged&&string.IsNullOrEmpty(report.error)?"PASS":"FAIL";
            if(report.result!="PASS"&&string.IsNullOrEmpty(report.error))report.error="Interrupted/incomplete: "+status;
            IsComplete=true;File.WriteAllText(Path.Combine(directory,"sample.json"),JsonUtility.ToJson(report,true));return directory;
        }
    }
}
