using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Diagnostics;
using Unity.Profiling;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using WeatherHouse.Vegetation;
using WeatherHousePrototype;
using Object = UnityEngine.Object;

namespace WeatherHouse.Life.Editor
{
    // A fixed-view diagnostic of the current scene. Root owns isolated saves and Play mode.
    public static class LifePerformanceChecks
    {
        const string Folder="LifeGameplayReview/20261026/performance";
        [Serializable] public struct FrameSample
        {
            public int phase,frame,frameGap;
            public double seconds,wallMs,engineMs,mainMs,renderMs,gcBytes,gpuMs;
            public ulong gpuTimestamp;
            public bool focused;
        }
        [Serializable] public sealed class Metric
        {
            public int samples;
            public double mean=-1,p50=-1,p95=-1,max=-1;
        }
        [Serializable] public sealed class Phase
        {
            public string name,screenshot;
            public double seconds;
            public int sampledFrames,missedFrameIntervals,activeReflections;
            public Metric wallMs,engineMs,mainMs,renderMs,gcBytes,gpuMs;
        }
        [Serializable] public sealed class ConfigEntry
        {
            public string path,type,json;
        }
        [Serializable] public sealed class Configuration
        {
            public string scene,graphicsDevice,graphicsApi;
            public bool dirty,frameTimingEnabled;
            public int quality,vSync,targetFrameRate,width,height;
            public float timeScale;
            public Vector3 playerPosition,cameraPosition;
            public Quaternion playerRotation,cameraRotation;
            public ConfigEntry[] components,waterMaterials;
        }
        [Serializable] public sealed class Report
        {
            public string status="RUNNING",utc,error;
            public string boundary="Current WatersideV2 Editor Play A/B/A diagnostic, not an old-water-version comparison. Player movement controller and orbit are temporarily disabled in all phases to hold the original pose; Animator/world continue. Frame intervals include Editor/wait overhead. Recorder values are latest completed samples; GPU data are delayed and deduplicated by timestamp. Missing measurements are -1. Screenshots and 2-second warmups are excluded. Natural world evolution and background workloads can still affect comparisons.";
            public Configuration before,after;
            public string gpuAvailability;
            public List<string> checks=new List<string>();
            public List<Phase> phases=new List<Phase>();
            public List<FrameSample> frames=new List<FrameSample>(12000);
        }
        static LifeSession owner;
        static Camera camera;
        static Behaviour playerController,orbit;
        static bool playerEnabled,orbitEnabled;
        static PainterlyWaterReflection[] reflections;
        static bool[] reflectionEnabled;
        static Material[] materials;
        static Vector3 playerPosition,cameraPosition;
        static Quaternion playerRotation,cameraRotation;
        static ProfilerRecorder main,render,gc;
        static readonly FrameTiming[] timing=new FrameTiming[1];
        static ulong lastGpuTimestamp;
        static bool timingAvailable,restoring;
        static Report report;
        static IEnumerator runner;
        static string output;
        public static bool Running=>runner!=null;
        public static string LastStatus {get;private set;}
        static double Clock=>Stopwatch.GetTimestamp()/(double)Stopwatch.Frequency;

        public static void Begin(LifeSession life)
        {
            string save=Environment.GetEnvironmentVariable("WEATHERHOUSE_LIFE_REVIEW_SAVE");
            if(Running || !EditorApplication.isPlaying || EditorApplication.isPaused || !life || !life.Gameplay ||
                string.IsNullOrEmpty(save) || !string.Equals(Path.GetFullPath(save),Path.GetFullPath(GameplaySession.PersistenceDirectory),StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Performance review requires root-owned, unpaused Play mode and the isolated review save.");
            var game=life.Gameplay;
            var hud=game.GetComponent<GameplayHud>();
            if(SceneManager.GetActiveScene().path!="Assets/Scenes/WeatherHouse_WatersideV2.unity" ||
                !game.PlayerTransform || !game.PlayerViewCamera || game.CurrentView!=game.PlayerViewCamera ||
                game.Judging || game.Recorder.IsRecording || (hud && (hud.JournalOpen || hud.IsDialogueOpen || hud.CameraMode)))
                throw new InvalidOperationException("Start at the idle original WatersideV2 view with all modal/photo work closed.");
            if(!game.forestSprite || !game.forestSprite.IsGrounded || game.forestSprite.GaitHopsActive || game.forestSprite.ScriptedInput)
                throw new InvalidOperationException("The fixed-view fixture requires the grounded ForestSprite without an active movement demonstration.");
            owner=life;camera=game.PlayerViewCamera;playerController=game.forestSprite;
            orbit=camera.GetComponent<VegetationTopDownCamera>();
            reflections=Object.FindObjectsOfType<PainterlyWaterReflection>(true).Where(x=>x.gameObject.scene==life.gameObject.scene).ToArray();
            if(!reflections.Any(x=>x.isActiveAndEnabled))throw new InvalidOperationException("No active current reflection components to diagnose.");
            reflectionEnabled=reflections.Select(x=>x.enabled).ToArray();
            materials=reflections.SelectMany(x=>x.GetComponent<Renderer>().sharedMaterials).Where(x=>x).Distinct().ToArray();
            playerEnabled=playerController.enabled;orbitEnabled=orbit && orbit.enabled;
            playerPosition=game.PlayerTransform.position;playerRotation=game.PlayerTransform.rotation;
            cameraPosition=camera.transform.position;cameraRotation=camera.transform.rotation;
            output=Path.GetFullPath(Path.Combine(Folder,DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff")));
            Directory.CreateDirectory(output);Directory.CreateDirectory(Folder);
            File.WriteAllText(Folder+"/latest-run.txt",output);
            report=new Report{utc=DateTime.UtcNow.ToString("O")};report.before=ConfigurationNow();
            LastStatus="RUNNING";lastGpuTimestamp=0;
            runner=Run();
            EditorApplication.update+=Tick;
            EditorApplication.playModeStateChanged+=PlayChanged;
            AssemblyReloadEvents.beforeAssemblyReload+=Cancel;
            try
            {
                playerController.enabled=false;if(orbit)orbit.enabled=false;
                main=Recorder(ProfilerCategory.Internal,"Main Thread");
                render=Recorder(ProfilerCategory.Internal,"Render Thread");
                gc=Recorder(ProfilerCategory.Memory,"GC Allocated In Frame");
                timingAvailable=report.before.frameTimingEnabled;
                report.gpuAvailability=timingAvailable?"FrameTiming enabled; awaiting a nonzero, new completed GPU sample.":"Unavailable: FrameTimingManager is disabled in this Editor configuration. Project settings were not changed.";
                Write();
            }
            catch(Exception e){Finish("FAIL",e.ToString());throw;}
        }

        public static void Cancel(){if(Running)Finish("INTERRUPTED","Review cancelled; fixture state restored.");}
        static void PlayChanged(PlayModeStateChange state){if(state==PlayModeStateChange.ExitingPlayMode)Cancel();}
        static void Tick()
        {
            if(!Running || restoring)return;
            if(!EditorApplication.isPlaying){Cancel();return;}
            if(EditorApplication.isPaused){Finish("INTERRUPTED","Paused during timing; this run is not a complete comparison.");return;}
            try {if(!runner.MoveNext())Finish("PASS",null);}
            catch(Exception e){Finish("FAIL",e.ToString());}
        }
        static IEnumerator Run()
        {
            string[] names={"A-current-reflections","B-reflections-disabled","A-restored-reflections"};
            double[] durations={20,10,10};
            for(int phase=0;phase<3;phase++)
            {
                for(int i=0;i<reflections.Length;i++)reflections[i].enabled=phase==1?false:reflectionEnabled[i];
                double warmUntil=Clock+2;
                while(Clock<warmUntil){PinPose();Gpu(out _,out _);yield return null;}
                var result=new Phase{name=names[phase],activeReflections=reflections.Count(x=>x.isActiveAndEnabled)};
                report.phases.Add(result);
                double started=Clock,last=started;int lastFrame=Time.frameCount,first=report.frames.Count;
                while(Clock-started<durations[phase])
                {
                    yield return null;PinPose();
                    if(Time.frameCount==lastFrame)continue;
                    double now=Clock;Gpu(out double gpu,out ulong stamp);
                    report.frames.Add(new FrameSample{phase=phase,frame=Time.frameCount,frameGap=Time.frameCount-lastFrame,
                        seconds=now-started,wallMs=(now-last)*1000,engineMs=Time.unscaledDeltaTime*1000,
                        mainMs=Value(main,.000001),renderMs=Value(render,.000001),gcBytes=Value(gc,1),gpuMs=gpu,gpuTimestamp=stamp,focused=Application.isFocused});
                    lastFrame=Time.frameCount;last=now;
                }
                result.seconds=Clock-started;result.sampledFrames=report.frames.Count-first;
                if(result.sampledFrames<10)throw new InvalidOperationException("Too few actual Play frames: "+result.name);
                // Snapshot is queued only after sampling; do not change the reflection state until it is written.
                result.screenshot=names[phase]+".png";
                string screenshot=Path.Combine(output,result.screenshot);
                ScreenCapture.CaptureScreenshot(screenshot);
                double deadline=Clock+8;
                while(!File.Exists(screenshot) || new FileInfo(screenshot).Length<100)
                {
                    if(Clock>deadline)throw new IOException("Game-view screenshot was not written: "+screenshot);
                    PinPose();yield return null;
                }
            }
        }
        static void PinPose()
        {
            if(!owner || !camera || !owner.Gameplay.PlayerTransform)throw new InvalidOperationException("Fixed-view fixture was destroyed.");
            owner.Gameplay.PlayerTransform.SetPositionAndRotation(playerPosition,playerRotation);
            camera.transform.SetPositionAndRotation(cameraPosition,cameraRotation);
        }
        static ProfilerRecorder Recorder(ProfilerCategory category,string name)
        {try{return ProfilerRecorder.StartNew(category,name,1);}catch{return default;}}
        static double Value(ProfilerRecorder recorder,double scale)=>recorder.Valid && recorder.Count>0?recorder.LastValue*scale:-1;
        static void Gpu(out double milliseconds,out ulong timestamp)
        {
            milliseconds=-1;timestamp=0;
            if(!timingAvailable)return;
            FrameTimingManager.CaptureFrameTimings();
            if(FrameTimingManager.GetLatestTimings(1,timing)==0)return;
            var value=timing[0];
            if(value.gpuFrameTime<=0 || value.frameStartTimestamp==lastGpuTimestamp)return;
            lastGpuTimestamp=timestamp=value.frameStartTimestamp;milliseconds=value.gpuFrameTime;
        }
        static Configuration ConfigurationNow()
        {
            var components=new List<Component>(reflections);
            components.Add(playerController);if(orbit)components.Add(orbit);components.Add(camera);
            var data=camera.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();if(data)components.Add(data);
            return new Configuration{scene=SceneManager.GetActiveScene().path,dirty=SceneManager.GetActiveScene().isDirty,
                graphicsDevice=SystemInfo.graphicsDeviceName,graphicsApi=SystemInfo.graphicsDeviceType.ToString(),
                frameTimingEnabled=FrameTimingManager.IsFeatureEnabled(),quality=QualitySettings.GetQualityLevel(),vSync=QualitySettings.vSyncCount,
                targetFrameRate=Application.targetFrameRate,width=Screen.width,height=Screen.height,timeScale=Time.timeScale,
                playerPosition=owner.Gameplay.PlayerTransform.position,playerRotation=owner.Gameplay.PlayerTransform.rotation,
                cameraPosition=camera.transform.position,cameraRotation=camera.transform.rotation,
                components=components.Select(x=>new ConfigEntry{path=Hierarchy(x.transform),type=x.GetType().FullName,json=EditorJsonUtility.ToJson(x)}).ToArray(),
                waterMaterials=materials.Select(x=>new ConfigEntry{path=AssetDatabase.GetAssetPath(x),type=x.name,json=EditorJsonUtility.ToJson(x)}).ToArray()};
        }
        static string Hierarchy(Transform transform)=>transform.parent?Hierarchy(transform.parent)+"/"+transform.name:transform.name;
        static Metric Summary(IEnumerable<double> source)
        {
            double[] values=source.Where(x=>x>=0 && !double.IsNaN(x) && !double.IsInfinity(x)).OrderBy(x=>x).ToArray();
            if(values.Length==0)return new Metric();
            return new Metric{samples=values.Length,mean=values.Average(),p50=values[(int)Math.Ceiling(values.Length*.5)-1],p95=values[(int)Math.Ceiling(values.Length*.95)-1],max=values[values.Length-1]};
        }
        static void Finish(string status,string error)
        {
            if(restoring)return;restoring=true;
            runner=null;EditorApplication.update-=Tick;EditorApplication.playModeStateChanged-=PlayChanged;AssemblyReloadEvents.beforeAssemblyReload-=Cancel;
            try
            {
                for(int i=0;i<reflections.Length;i++)
                {
                    if(!reflections[i]){status="FAIL";error+="\nReflection object was destroyed.";continue;}
                    reflections[i].enabled=reflectionEnabled[i];
                }
                if(playerController)playerController.enabled=playerEnabled;
                if(orbit)orbit.enabled=orbitEnabled;
                PinPose();report.after=ConfigurationNow();
                if(JsonUtility.ToJson(report.before)!=JsonUtility.ToJson(report.after))
                    throw new InvalidOperationException("Restored configuration differs. Inspect report.before/after; no scene or material was saved.");
                report.checks.Add("PASS original player/camera pose, component enabled state, serialized reflection/camera/player settings, water materials, quality/vsync/frame cap, time scale and scene dirty flag restored.");
                if(status=="PASS" && (report.phases.Count!=3 || report.phases[1].activeReflections!=0 || report.phases[0].activeReflections!=report.phases[2].activeReflections))
                    throw new InvalidOperationException("A/B/A reflection counts did not match the intended experiment.");
            }
            catch(Exception e){status="FAIL";error+="\nRestoration: "+e;}
            finally
            {
                main.Dispose();render.Dispose();gc.Dispose();
                for(int i=0;i<report.phases.Count;i++)
                {
                    var result=report.phases[i];var samples=report.frames.Where(x=>x.phase==i).ToArray();
                    result.missedFrameIntervals=samples.Count(x=>x.frameGap!=1);
                    result.wallMs=Summary(samples.Where(x=>x.frameGap==1).Select(x=>x.wallMs));result.engineMs=Summary(samples.Select(x=>x.engineMs));
                    result.mainMs=Summary(samples.Select(x=>x.mainMs));result.renderMs=Summary(samples.Select(x=>x.renderMs));
                    result.gcBytes=Summary(samples.Select(x=>x.gcBytes));result.gpuMs=Summary(samples.Select(x=>x.gpuMs));
                }
                int gpuCount=report.frames.Count(x=>x.gpuMs>=0);
                if(timingAvailable)report.gpuAvailability=gpuCount>0?"Available: "+gpuCount+" unique completed GPU samples.":"Unavailable: feature enabled but this Editor/graphics backend returned no nonzero unique GPU timings.";
                report.status=LastStatus=status;report.error=error;
                try{Write();}finally{restoring=false;}
            }
        }
        static void Write()
        {
            File.WriteAllText(Path.Combine(output,"report.json"),JsonUtility.ToJson(report,true));
            File.WriteAllText(Folder+"/last-result.txt",report.status+"\n"+output+"\n"+report.error);
        }
    }
}
