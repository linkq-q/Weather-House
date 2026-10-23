using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using ForestSpriteCharacter;

namespace WeatherHouse.Vegetation.Editor
{
    [InitializeOnLoad]
    public static class VegetationTopDownChecks
    {
        const string Root = "VegetationLabReview/TopDown20261022";
        const string ScenePath = "Assets/Scenes/Experimental/VegetationLab.unity";
        const string OldDemoPath = "Assets/Scenes/Experimental/ForestSprite_Demo.unity";
        const string Key = "VegetationTopDownChecks";
        [Serializable] class Request
        {
            public string id, command, name;
            public float yaw = 42, pitch = 28, distance = 14, lookAhead = 4.5f, fov = 48;
            public float spawnX = -8, spawnZ = -10;
        }
        [Serializable] class Result
        {
            public string id, command, result, message, utc, scene, action;
            public bool playing, grounded;
            public Vector3 player, camera, viewport;
            public float yaw, pitch, distance, lookAhead, fov, capeDepth;
            public string[] checks;
            public string playModeStartScene;
            public int enabledPlayers, enabledCameras, enabledListeners;
            public SceneInfo[] scenes;
        }
        [Serializable] class SceneInfo
        {
            public string name, path;
            public bool loaded, dirty;
            public PlayerInfo[] players;
            public CameraInfo[] cameras;
            public ListenerInfo[] listeners;
        }
        [Serializable] class PlayerInfo
        {
            public string name, cameraName, cameraScene;
            public int instanceId;
            public Vector3 position, cameraPosition;
            public float yaw, cameraYaw, configuredCameraYaw;
            public bool scriptedInput, externalCameraControl;
        }
        [Serializable] class CameraInfo
        {
            public string name, type;
            public int instanceId;
            public Vector3 position;
            public float yaw, depth;
        }
        [Serializable] class ListenerInfo
        {
            public string name;
            public int instanceId;
            public Vector3 position;
        }
        static double nextPoll;
        static string testId;
        static int stage;
        static float stageStart;
        static Vector3 moveStart;
        static bool jumpAirborne, jumpCaptured;
        static bool previousBackground;
        static int lastTestFrame;
        static float simulatedStageTime;
        static readonly List<string> checks = new List<string>();

        static VegetationTopDownChecks()
        {
            if (!SessionState.GetBool(Key + ":initialized", false))
            {
                string path=Root+"/request.json";
                if(File.Exists(path))
                {
                    try { var old=JsonUtility.FromJson<Request>(File.ReadAllText(path));if(old!=null)SessionState.SetString(Key,old.id??""); }
                    catch(Exception e) { Debug.LogWarning("Ignored an old top-down review request: "+e.Message); }
                }
                SessionState.SetBool(Key + ":initialized",true);
            }
            EditorApplication.update += Poll;
            EditorApplication.playModeStateChanged += state => {
                if (state == PlayModeStateChange.EnteredEditMode && SessionState.GetBool(Key + ":ownsPlay", false))
                {
                    EditorSceneManager.playModeStartScene=AssetDatabase.LoadAssetAtPath<SceneAsset>(SessionState.GetString(Key + ":previousStart", ""));
                    SessionState.SetBool(Key + ":ownsPlay", false);
                }
            };
        }
        static T[] Components<T>(Scene scene) where T : Component =>
            scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<T>(true)).ToArray();
        static T[] LoadedComponents<T>() where T : Component =>
            Enumerable.Range(0, SceneManager.sceneCount).Select(SceneManager.GetSceneAt)
                .Where(s => s.isLoaded).SelectMany(s => Components<T>(s)).ToArray();
        static SceneInfo[] InspectScenes() => Enumerable.Range(0, SceneManager.sceneCount).Select(index =>
        {
            var scene = SceneManager.GetSceneAt(index);
            return new SceneInfo
            {
                name = scene.name, path = scene.path, loaded = scene.isLoaded, dirty = scene.isDirty,
                players = scene.isLoaded ? Components<ForestSpriteDemoController>(scene).Where(p => p.isActiveAndEnabled)
                    .Select(p => new PlayerInfo
                    {
                        name = p.name, instanceId = p.GetInstanceID(), position = p.transform.position,
                        yaw = p.transform.eulerAngles.y, configuredCameraYaw = p.cameraYaw,
                        scriptedInput = p.ScriptedInput, externalCameraControl = p.externalCameraControl,
                        cameraName = p.demoCamera ? p.demoCamera.name : "",
                        cameraScene = p.demoCamera ? p.demoCamera.gameObject.scene.path : "",
                        cameraPosition = p.demoCamera ? p.demoCamera.transform.position : Vector3.zero,
                        cameraYaw = p.demoCamera ? p.demoCamera.transform.eulerAngles.y : 0
                    }).ToArray() : new PlayerInfo[0],
                cameras = scene.isLoaded ? Components<Camera>(scene).Where(c => c.isActiveAndEnabled)
                    .Select(c => new CameraInfo { name = c.name, instanceId = c.GetInstanceID(),
                        type = c.cameraType.ToString(), position = c.transform.position,
                        yaw = c.transform.eulerAngles.y, depth = c.depth }).ToArray() : new CameraInfo[0],
                listeners = scene.isLoaded ? Components<AudioListener>(scene).Where(l => l.isActiveAndEnabled)
                    .Select(l => new ListenerInfo { name = l.name, instanceId = l.GetInstanceID(),
                        position = l.transform.position }).ToArray() : new ListenerInfo[0]
            };
        }).ToArray();
        static VegetationCharacterPreview Adapter()
        {
            var scene = SceneManager.GetSceneByPath(ScenePath);
            Require(scene.IsValid() && scene.isLoaded, "VegetationLab must be loaded.");
            return Components<VegetationCharacterPreview>(scene).Single();
        }
        static void Require(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }
        static void Poll()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            try
            {
                if (testId != null) TickTest();
                if (EditorApplication.timeSinceStartup < nextPoll) return;
                nextPoll = EditorApplication.timeSinceStartup + .25;
                string path = Root + "/request.json";
                if (!File.Exists(path)) return;
                var r = JsonUtility.FromJson<Request>(File.ReadAllText(path));
                if (r == null || string.IsNullOrEmpty(r.id) || SessionState.GetString(Key, "") == r.id) return;
                SessionState.SetString(Key, r.id);
                Execute(r);
            }
            catch (Exception e)
            {
                if (testId != null)
                {
                    Adapter().player.ReleaseScriptedInput();
                    Application.runInBackground=previousBackground;
                    Write(new Request { id=testId, command="Test" }, "FAIL", e.ToString());
                    testId=null;
                }
                else Write(new Request { id=SessionState.GetString(Key,""), command="Request" }, "FAIL", e.ToString());
                Debug.LogException(e);
            }
        }
        static void Execute(Request r)
        {
            if (r.command == "Configure")
            {
                Require(!EditorApplication.isPlayingOrWillChangePlaymode, "Configure in Edit mode.");
                var scene = SceneManager.GetSceneByPath(ScenePath);
                if (!scene.IsValid() || !scene.isLoaded) scene=EditorSceneManager.OpenScene(ScenePath,OpenSceneMode.Additive);
                Require(!scene.isDirty, "Preserve unsaved scene edits before configuring.");
                Directory.CreateDirectory(Root + "/before");
                string backup=Root+"/before/VegetationLab.unity";
                if (!File.Exists(backup)) File.Copy(ScenePath,backup);
                SceneManager.SetActiveScene(scene);
                var a=Adapter();
                var c=a.playerCamera.GetComponent<VegetationTopDownCamera>();
                if(!c)c=a.playerCamera.gameObject.AddComponent<VegetationTopDownCamera>();
                c.player=a.player;
                c.pitchRange=new Vector2(24,46);
                a.player.externalCameraControl=true; a.player.showControls=false;
                a.startInCharacterMode=true;
                a.player.transform.rotation=Quaternion.Euler(0,r.yaw,0);
                Apply(a,c,r); c.SnapToPlayer();
                foreach(var w in Components<PainterlyWaterInteraction>(scene)) w.player=a.player;
                PrefabUtility.RecordPrefabInstancePropertyModifications(a.player);
                PrefabUtility.RecordPrefabInstancePropertyModifications(a.player.transform);
                EditorSceneManager.MarkSceneDirty(scene);
                Require(EditorSceneManager.SaveScene(scene),"Scene saved.");
                Write(r,"PASS","Saved only the existing pond scene camera, spawn and character wiring.");
            }
            else if(r.command=="Play")
            {
                Require(!EditorApplication.isPlayingOrWillChangePlaymode,"Already playing.");
                SessionState.SetString(Key + ":previousStart", EditorSceneManager.playModeStartScene ? AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene) : "");
                SessionState.SetBool(Key + ":ownsPlay", true);
                EditorSceneManager.playModeStartScene=AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath);
                EditorApplication.isPlaying=true; Write(r,"ACCEPTED","Entering pond scene.");
            }
            else if(r.command=="PlayCurrentSetup")
            {
                Require(!EditorApplication.isPlayingOrWillChangePlaymode,"Already playing.");
                Require(!EditorSceneManager.playModeStartScene,"Ordinary Play reproduction requires an empty playModeStartScene.");
                var pond=SceneManager.GetSceneByPath(ScenePath);
                Require(pond.IsValid() && pond.isLoaded,"Load the pond scene before reproducing ordinary Play.");
                EditorApplication.isPlaying=true;
                Write(r,"ACCEPTED","Entering ordinary Play with the current loaded scenes; no start scene override.");
            }
            else if(r.command=="AuditSceneOwnership")
                Write(r,"PASS","Recorded enabled characters, cameras and listeners across all loaded scenes.");
            else if(r.command=="CloseOldDemo")
            {
                Require(!EditorApplication.isPlayingOrWillChangePlaymode,"Close the old demo only in Edit mode.");
                var pond=SceneManager.GetSceneByPath(ScenePath);
                Require(pond.IsValid() && pond.isLoaded,"Keep the pond scene loaded while closing the old demo.");
                var oldDemo=SceneManager.GetSceneByPath(OldDemoPath);
                if(oldDemo.IsValid() && oldDemo.isLoaded)
                {
                    Require(!oldDemo.isDirty,"The old demo has unsaved edits; leave it open.");
                    Require(EditorSceneManager.CloseScene(oldDemo,true),"Could not close the old demo scene.");
                }
                Write(r,"PASS","The clean old demo is unloaded; its asset and all other scenes are retained.");
            }
            else if(r.command=="Stop")
            {
                Require(EditorApplication.isPlaying && SceneManager.GetActiveScene().path==ScenePath,"Only stop pond Play.");
                EditorApplication.isPlaying=false; Write(r,"ACCEPTED","Leaving pond Play.");
            }
            else if(r.command=="Tune")
            {
                var a=Adapter();var c=a.playerCamera.GetComponent<VegetationTopDownCamera>();
                Apply(a,c,r);
                if(EditorApplication.isPlaying) a.TeleportToRegion(0);
                c.SnapToPlayer();
                Write(r,"PASS","Live preview settings applied.");
            }
            else if(r.command=="Capture")
            {
                Capture(r.name);Write(r,"PASS","Native Unity camera frame captured.");
            }
            else if(r.command=="ApplyConceptLook")
            {
                ForestSpriteCharacter.Editor.ForestSpriteConceptLook.Apply();
                Write(r,"PASS","Saved concept textures on the existing shared character materials.");
            }
            else if(r.command=="CaptureConceptViews")
            {
                var a=Adapter();
                ForestSpriteCharacter.Editor.ForestSpriteConceptLook.CaptureViews(a.player,a.playerCamera);
                Write(r,"PASS","Captured actual front, side and back character materials.");
            }
            else if(r.command=="CaptureGame")
            {
                Require(EditorApplication.isPlaying,"Capture the actual Game view in Play.");
                Require(!string.IsNullOrEmpty(r.name) && Path.GetFileName(r.name)==r.name,"Capture filename only.");
                ScreenCapture.CaptureScreenshot(Root+"/"+r.name+".png");
                Write(r,"ACCEPTED","Capturing the actual Game view at end of frame.");
            }
            else if(r.command=="Test") BeginTest(r);
            else if(r.command=="Status") Write(r,"PASS","Current state.");
            else if(r.command=="ShowGame")
            {
                Require(EditorApplication.isPlaying && SceneManager.GetActiveScene().path==ScenePath,"Show the pond Play session.");
                EditorApplication.ExecuteMenuItem("Window/General/Game");
                Write(r,"PASS","Opened the pond Game view.");
            }
            else if(r.command=="RestorePlayStart")
            {
                EditorSceneManager.playModeStartScene=null;
                Write(r,"PASS","Restored normal active-scene Play entry.");
            }
            else throw new InvalidOperationException("Unsupported top-down review command.");
        }
        static void Apply(VegetationCharacterPreview a,VegetationTopDownCamera c,Request r)
        {
            a.mainSpawn=new Vector2(r.spawnX,r.spawnZ);
            c.baseYaw=r.yaw;c.yawOffset=0;c.pitch=r.pitch;c.distance=r.distance;c.lookAhead=r.lookAhead;c.fieldOfView=r.fov;
            if(!EditorApplication.isPlaying)
            {
                var m=a.labView.regions[0];var cc=a.player.GetComponent<CharacterController>();
                cc.enabled=false;
                a.player.transform.position=m.transform.TransformPoint(new Vector3(r.spawnX,m.GetGroundHeight(r.spawnX,r.spawnZ)+.1f,r.spawnZ));
                cc.enabled=true;
            }
        }
        static void Capture(string name)
        {
            Require(!string.IsNullOrEmpty(name) && Path.GetFileName(name)==name,"Capture filename only.");
            var camera=Adapter().playerCamera;
            var rt=new RenderTexture(1560,1000,24);
            var tex=new Texture2D(1560,1000,TextureFormat.RGB24,false);
            var old=camera.targetTexture;var active=RenderTexture.active;float aspect=camera.aspect;
            try
            {
                camera.targetTexture=rt;camera.aspect=1.56f;
                camera.Render();RenderTexture.active=rt;
                tex.ReadPixels(new Rect(0,0,1560,1000),0,0);tex.Apply();
                Directory.CreateDirectory(Root);File.WriteAllBytes(Root+"/"+name+".png",tex.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture=old;camera.aspect=aspect;RenderTexture.active=active;
                rt.Release();UnityEngine.Object.DestroyImmediate(rt);UnityEngine.Object.DestroyImmediate(tex);
            }
        }
        static void Write(Request r,string result,string message)
        {
            var answer=new Result{id=r.id,command=r.command,result=result,message=message,utc=DateTime.UtcNow.ToString("O"),
                scene=SceneManager.GetActiveScene().path,playing=EditorApplication.isPlaying,checks=checks.ToArray()};
            answer.playModeStartScene=EditorSceneManager.playModeStartScene ? AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene) : "";
            answer.scenes=InspectScenes();
            answer.enabledPlayers=answer.scenes.Sum(s=>s.players.Length);
            answer.enabledCameras=answer.scenes.Sum(s=>s.cameras.Length);
            answer.enabledListeners=answer.scenes.Sum(s=>s.listeners.Length);
            var scene=SceneManager.GetSceneByPath(ScenePath);
            if(scene.IsValid() && scene.isLoaded)
            {
                var a=Components<VegetationCharacterPreview>(scene).SingleOrDefault();
                if(a && a.player && a.playerCamera)
                {
                    answer.player=a.player.transform.position;answer.camera=a.playerCamera.transform.position;
                    answer.viewport=a.playerCamera.WorldToViewportPoint(answer.player+Vector3.up*.8f);
                    answer.action=a.player.CurrentState;answer.grounded=a.player.IsGrounded;
                    var c=a.playerCamera.GetComponent<VegetationTopDownCamera>();
                    if(c){answer.yaw=c.Yaw;answer.pitch=c.pitch;answer.distance=c.distance;answer.lookAhead=c.lookAhead;answer.fov=c.fieldOfView;}
                    var contact=a.player.capeCloth.GetComponent<ForestSpriteCapeContact>();
                    if(contact)answer.capeDepth=Mathf.Max(contact.ActualDepth,contact.SurfaceDepth);
                }
            }
            Directory.CreateDirectory(Root);File.WriteAllText(Root+"/response.json",JsonUtility.ToJson(answer,true));
            if(r.command=="Test")File.WriteAllText(Root+"/runtime-checks.json",JsonUtility.ToJson(answer,true));
            if(r.command=="AuditSceneOwnership")File.WriteAllText(Root+"/scene-ownership.json",JsonUtility.ToJson(answer,true));
        }
        static void BeginTest(Request r)
        {
            Require(EditorApplication.isPlaying,"Test the actual Play session.");
            checks.Clear();
            previousBackground=Application.runInBackground;testId=r.id;Application.runInBackground=true;
            int playerCount=LoadedComponents<ForestSpriteDemoController>().Count(p=>p.isActiveAndEnabled);
            Require(playerCount==1,"Exactly one enabled ForestSpriteDemoController across all loaded scenes; found "+playerCount+".");
            Require(SceneManager.GetActiveScene().path==ScenePath,"Test the actual pond Play session.");
            var a=Adapter();var c=a.playerCamera.GetComponent<VegetationTopDownCamera>();
            Require(a.player.visual.GetComponentsInChildren<SkinnedMeshRenderer>().Any(s=>s.name=="Body_Boots"),"Updated separate boots imported.");
            Require(a.player.visual.GetComponentsInChildren<SkinnedMeshRenderer>().Any(s=>s.name=="Body_Skirt"),"Updated separate skirt imported.");
            float yaw=c.yawOffset,pitch=c.pitch,distance=c.distance;
            c.StepOrbit(1000,1000,1000);Require(c.yawOffset==c.yawLimit && c.pitch==c.pitchRange.y && c.distance==c.distanceRange.x,"Upper orbit limits.");
            c.StepOrbit(-2000,-2000,-2000);Require(c.yawOffset==-c.yawLimit && c.pitch==c.pitchRange.x && c.distance==c.distanceRange.y,"Lower orbit limits.");
            c.pitch=c.pitchRange.y;c.distance=c.distanceRange.x;c.SnapToPlayer();
            Require(a.playerCamera.WorldToViewportPoint(a.player.transform.position).y>.1f,"Near/steep orbit keeps feet above bottom margin.");
            c.yawOffset=yaw;c.pitch=pitch;c.distance=distance;c.SnapToPlayer();checks.Add("Orbit, pitch and zoom clamps PASS");
            a.SetCharacterMode(false);Require(!a.player.enabled && !c.enabled && a.labView.Navigation.enabled,"Browse mode ownership.");
            a.SetCharacterMode(true);Require(a.player.enabled && c.enabled && !a.labView.Navigation.enabled,"Character mode ownership.");
            Require(LoadedComponents<Camera>().Count(x=>x.isActiveAndEnabled)==1,"Exactly one active camera across all loaded scenes.");
            Require(LoadedComponents<AudioListener>().Count(x=>x.isActiveAndEnabled)==1,"Exactly one active listener across all loaded scenes.");
            a.SetDebugPanels(true);Require(a.player.showControls && a.DebugPanelsVisible,"F7 panels open.");
            a.SetDebugPanels(false);Require(!a.player.showControls && !a.DebugPanelsVisible,"F7 panels close.");
            checks.Add("F6/F7 mode methods and camera/listener ownership PASS");
            a.TeleportToRegion(0);a.player.SetScriptedInput(Vector2.zero,false,false);c.SnapToPlayer();
            stage=0;stageStart=Time.time;simulatedStageTime=0;lastTestFrame=Time.frameCount;testId=r.id;Write(r,"RUNNING","Checking locomotion and rendering native frames.");
        }
        static void TickTest()
        {
            var a=Adapter();var p=a.player;var c=a.playerCamera.GetComponent<VegetationTopDownCamera>();
            if(lastTestFrame==Time.frameCount)return;
            lastTestFrame=Time.frameCount;
            simulatedStageTime+=Mathf.Min(Time.deltaTime,.05f);
            if(stage==3 && !p.IsGrounded && p.CurrentState=="Jump")
            {
                jumpAirborne=true;
                if(!jumpCaptured && Mathf.Abs(p.VerticalSpeed)<1f) { Capture("06_jump");jumpCaptured=true; }
            }
            Require(Time.time-stageStart<30f,"Native test stage timed out.");
            if(simulatedStageTime < (stage==3?1.2f:.7f))return;
            if(stage==0)
            {
                Capture("01_default");c.yawOffset=-35;c.SnapToPlayer();Capture("02_left");
                c.yawOffset=35;c.SnapToPlayer();Capture("03_right");c.yawOffset=0;c.SnapToPlayer();
                moveStart=p.transform.position;p.SetScriptedInput(Vector2.right,false,false);
            }
            if(stage==1)
            {
                Require(p.CurrentState=="Walk" && Vector3.Distance(moveStart,p.transform.position)>.3f,"Walk moves on actual meadow.");
                Capture("04_walk");checks.Add("Native Walk and movement PASS");
                moveStart=p.transform.position;p.SetScriptedInput(Vector2.right,true,false);
            }
            if(stage==2)
            {
                Require(p.CurrentState=="Run" && Vector3.Distance(moveStart,p.transform.position)>.6f,"Run moves on actual meadow.");
                Capture("05_run");checks.Add("Native Run and movement PASS");
                jumpAirborne=jumpCaptured=false;
                p.SetScriptedInput(Vector2.zero,false,true);
            }
            if(stage==3)
            {
                Require(jumpAirborne && p.IsGrounded,"Jump leaves and returns to meadow ground.");
                checks.Add("Jump landing PASS");Capture("06_landing");
                p.ReleaseScriptedInput();a.TeleportToRegion(0);c.SnapToPlayer();
                Application.runInBackground=previousBackground;
                Write(new Request{id=testId,command="Test"},"PASS","Camera and game integration checks passed. Cape clipping remains a known separate issue.");
                testId=null;return;
            }
            stage++;stageStart=Time.time;simulatedStageTime=0;
        }
    }
}
