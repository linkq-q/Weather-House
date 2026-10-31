// Stage in the isolated ValidationProject only. No animal transforms or production parameters are changed.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Unity.Profiling;
using Unity.Profiling.LowLevel.Unsafe;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using WeatherHouse.Hillside;
using WeatherHouse.Life;
using WeatherHouse.Vegetation;
using WeatherHouse.WatersideV2;
using WeatherHouse.WildlifeJourney;
using WeatherHousePrototype;
using Object=UnityEngine.Object;

[InitializeOnLoad]
public static class AnimalPerformanceReview
{
    const string Key="WeatherHouse.AnimalPerformanceReview",ScenePath="Assets/Scenes/WeatherHouse_WatersideV2.unity";
    const int Capacity=60000,MinFrames=300;
    const float SampleSeconds=20,InitialWarmSeconds=8;
    static readonly BindingFlags Flags=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
    static readonly CultureInfo Invariant=CultureInfo.InvariantCulture;
    static string Folder=>SessionState.GetString(Key+".folder","");
    static double Clock=>System.Diagnostics.Stopwatch.GetTimestamp()/(double)System.Diagnostics.Stopwatch.Frequency;
    static object Field(object value,string name)=>value?.GetType().GetField(name,Flags)?.GetValue(value);
    static void Require(bool okay,string message){if(!okay)throw new InvalidOperationException(message);}
    static string Hash(string path){if(!File.Exists(path))return "MISSING";using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-","");}
    static float Flat(Vector3 a,Vector3 b){a.y=b.y=0;return Vector3.Distance(a,b);}
    static string Num(double v)=>v<0?"":v.ToString("F6",Invariant);
    static string Raw(double v)=>v.ToString("F6",Invariant);
    static string Csv(string v)=>"\""+(v??"").Replace("\"","\"\"")+"\"";
    static void Event(string value)=>File.AppendAllText(Path.Combine(Folder,"checks.txt"),"t="+Time.time.ToString("F3",Invariant)+" "+value+"\n");

    [Serializable] public sealed class Metric
    {
        public string name,unit,status;public int samples,missing;
        public double mean=-1,p50=-1,p95=-1,p99=-1,max=-1;
    }
    [Serializable] public sealed class CounterInfo {public string metric,registeredName,category,unit,status;}
    [Serializable] public sealed class Phase
    {
        public string name;public int frames,missedFrameIntervals,firstFrame,lastFrame;
        public double simSeconds,wallSeconds;public Metric[] metrics;
        public Metric[] workload;
        public int initialAnimals,finalAnimals,initialExposed,finalExposed;
    }
    [Serializable] public sealed class FileStamp {public string path,sha256;}
    [Serializable] public sealed class Report
    {
        public string status="RUNNING",variant,utcStart,utcEnd,error,unityVersion,graphicsDevice,graphicsApi,scene,harnessSha256;
        public string boundary="Isolated batch Editor Play diagnostic, not release-build FPS. Fixed player/camera; natural animals/world keep running; water/render/distant animals are not disabled. Same machine/settings/source-scene/harness are required for comparison. Editor wall intervals and Main Thread include scheduling/render/wait overhead and do not equal NPC CPU cost; BehaviourUpdate is also all behaviours. Recorder samples are latest newly completed records and may be offset from the census frame. Absent/unemitted counters are unavailable (CSV empty, summary -1 plus status), never zero. Census reflection runs every 0.5 simulated seconds; frame arrays are preallocated and CSV is written only after sampling. Fresh isolated saves, no network; no animal relocation, personality, speed or path changes. Rain begins after sunny phase; its first transition frame is excluded from collection, and natural world evolution remains a comparison limitation. Compare shelter/planning/movement workload: a broken baseline that abandons shelter early may execute less work. Population census distinguishes all active scene animals from ground animals owned by autonomy; nearby means 8m xz distance to fixed player.";
        public int width,height,quality,vSync,targetFrameRate,processorCount;public float timeScale,fixedDeltaTime;
        public bool focused,networkDisabled;public Vector3 playerPosition,cameraPosition;public Quaternion cameraRotation;
        public CounterInfo[] counters;public FileStamp[] sourceFiles;public List<Phase> phases=new List<Phase>();
    }
    struct Frame
    {
        public int phase,frame,gap,animals,allScene,nearby,actions,moving,manual,automatic,exposed,sheltering,shelterHolding,planning,following;public float simulation,meanSpeed,maxSpeed;
        public double wallMs,engineMs,playerLoop,behaviour,main,render,allocation,physics;
    }
    struct CensusRow
    {
        public int phase,frame;public float simulation,speed;public string id,action;public bool live,lease,autonomous,wet,planning,following;public Vector3 position;
    }
    sealed class AnimalView
    {
        public object state;public string id;public Transform root;public WildlifeAgent agent;public JourneyHerd herd;public JourneyMember member;
        public FieldInfo action,shelterQuery,search,route;
    }
    sealed class Counter:IDisposable
    {
        public CounterInfo info=new CounterInfo();public ProfilerRecorder recorder;int seen;
        public void StartPhase(){seen=0;if(recorder.Valid){recorder.Reset();recorder.Start();}}
        public double Read()
        {
            if(!recorder.Valid || !recorder.IsRunning)return -1;
            int count=recorder.Count;if(count==0 || count==seen)return -1;
            if(count>=Capacity){info.status="unavailable: recorder capacity reached";return -1;}
            seen=count;info.status="available";return recorder.LastValue*((info.unit=="TimeNanoseconds" || info.unit=="Nanoseconds")?0.000001:1);
        }
        public void Dispose(){if(recorder.Valid)recorder.Dispose();}
    }
    static readonly string[][] CounterNames={new[]{"PlayerLoop"},new[]{"BehaviourUpdate","Update.ScriptRunBehaviourUpdate"},new[]{"Main Thread"},new[]{"Render Thread"},new[]{"GC Allocated In Frame"},new[]{"Physics.Simulate","Physics.Processing"}};
    static readonly string[] MetricNames={"playerLoopMs","behaviourUpdateMs","mainThreadMs","renderThreadMs","gcAllocatedBytes","physicsMs"};
    static readonly List<Frame> frames=new List<Frame>(Capacity);
    static readonly List<CensusRow> census=new List<CensusRow>(16000);
    static List<AnimalView> animals;
    static WildlifeAgent[] allWildlife;static JourneyHerd[] allHerds;
    static Counter[] counters;
    static Report report;
    static LifeSession life;static AnimalAutonomy director;static LifeLocalWeather weather;static WeatherRegion sky;
    static WildlifeAgent reference;static Transform player;static Camera camera;static Vector3 playerSite,cameraSite;static Quaternion cameraRotation;
    static int stage,lastFrame=-1,phaseFirst,phaseFirstFrame,censusAnimals,censusAllScene,censusNearby,censusActions,censusMoving,censusManual,censusAuto,censusExposed,censusSheltering,censusHolding,censusPlanning,censusFollowing;
    static float preparedAt,phaseAt,censusAt,censusMean,censusMax;
    static double began,lastClock,phaseClock;

    static AnimalPerformanceReview()
    {
        EditorApplication.update+=Tick;
        EditorApplication.playModeStateChanged+=state=>{
            if(!SessionState.GetBool(Key,false))return;
            if(state==PlayModeStateChange.EnteredPlayMode){began=Clock;Application.runInBackground=true;stage=0;}
            if(state==PlayModeStateChange.EnteredEditMode && SessionState.GetBool(Key+".finishing",false))VerifyAndExit();
        };
    }
    public static void Batch()
    {
        Require(Application.isBatchMode && Application.dataPath.Contains("ValidationProject") && !EditorApplication.isPlaying,"Independent ValidationProject batch only");
        string folder=Environment.GetEnvironmentVariable("WEATHERHOUSE_ANIMAL_PERF_REPORT"),variant=Environment.GetEnvironmentVariable("WEATHERHOUSE_ANIMAL_PERF_VARIANT");
        Require(!string.IsNullOrWhiteSpace(folder) && !string.IsNullOrWhiteSpace(variant),"Set WEATHERHOUSE_ANIMAL_PERF_REPORT and WEATHERHOUSE_ANIMAL_PERF_VARIANT");
        folder=Path.GetFullPath(folder);Require(!Directory.Exists(folder) || !Directory.EnumerateFileSystemEntries(folder).Any(),"Use an empty report folder; keep the Unity log outside");
        Directory.CreateDirectory(folder);SessionState.SetString(Key+".folder",folder);SessionState.SetString(Key+".variant",variant);
        SessionState.SetBool(Key,true);SessionState.SetBool(Key+".finishing",false);SessionState.SetInt(Key+".exit",1);
        var protectedFiles=new List<string>();
        foreach(string path in new[]{Path.GetFullPath(ScenePath),Path.Combine("C:/unity project/Weather House",ScenePath),AnimalPlannerClient.ConfigurationPath,
            "C:/unity project/Weather House/UserSettings/WeatherHouseAnimalsAI.json"}.Concat(new[]{"language-notebook.json","life-progress.json","onboarding.json"}.Select(n=>Path.Combine(Application.persistentDataPath,n))))
            protectedFiles.Add(Path.GetFullPath(path)+"|"+Hash(path));
        SessionState.SetString(Key+".protected",string.Join("\n",protectedFiles));
        try
        {
            var config=JsonUtility.FromJson<AnimalPlannerClient.Configuration>(File.ReadAllText(AnimalPlannerClient.ConfigurationPath));
            Require(config!=null && !config.enabled,"Clone AI config must be disabled on disk");
            Environment.SetEnvironmentVariable("WEATHERHOUSE_LIFE_REVIEW_SAVE",Path.Combine(folder,"isolated-save"));
            Environment.SetEnvironmentVariable("WEATHERHOUSE_ONBOARDING_REVIEW",null);Directory.CreateDirectory(Path.Combine(folder,"isolated-save"));
            Event("ARRANGE variant="+variant+"; original formal scene; fresh isolated save; network disabled; same harness for both source variants");
            EditorSceneManager.OpenScene(ScenePath);EditorApplication.isPlaying=true;
        }
        catch(Exception error){Event("FAIL "+error);Finish(error);}
    }
    static bool PlacePlayer()
    {
        Terrain terrain=Terrain.activeTerrain;var capsule=player.GetComponent<CharacterController>();if(!terrain || !capsule)return false;
        var water=player.GetComponent<WatersideWaterBoundary>();Vector3 offset=player.TransformVector(capsule.center),size=terrain.terrainData.size;
        float height=capsule.height*Mathf.Abs(player.lossyScale.y),radius=capsule.radius*Mathf.Max(Mathf.Abs(player.lossyScale.x),Mathf.Abs(player.lossyScale.z)),half=Mathf.Max(0,height*.5f-radius);
        for(int i=0;i<24;i++)
        {
            Vector3 candidate=reference.habitatCenter+Quaternion.Euler(0,i*15,0)*Vector3.forward*6.5f,local=candidate-terrain.transform.position;
            if(local.x<0 || local.z<0 || local.x>size.x || local.z>size.z || terrain.terrainData.GetSteepness(local.x/size.x,local.z/size.z)>30)continue;
            float ground=terrain.SampleHeight(candidate)+terrain.transform.position.y;
            if(water && water.surface && water.terrain && water.SampleSupportedDepth(candidate,ground)>.02f || WatersideLayout.SeaSignedDistance(candidate.x,candidate.z)<.2f)continue;
            candidate.y=ground-offset.y+height*.5f+.08f;Vector3 center=candidate+offset;
            if(Mathf.Abs(candidate.y-reference.transform.position.y)>1.5f || Physics.OverlapCapsule(center+Vector3.up*half,center-Vector3.up*half,radius*.95f,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore)
                .Any(c=>!(c is TerrainCollider) && !c.transform.IsChildOf(player)))continue;
            bool enabled=capsule.enabled;capsule.enabled=false;player.position=candidate;capsule.enabled=enabled;Physics.SyncTransforms();playerSite=candidate;
            if(life.Gameplay.forestSprite)life.Gameplay.forestSprite.SetScriptedInput(Vector2.zero,false,false);
            return true;
        }
        return false;
    }
    static void Prepare()
    {
        life=Object.FindObjectOfType<LifeSession>();director=Object.FindObjectOfType<AnimalAutonomy>();Require(life && director && director.AnimalCount>0,"Scene autonomy missing");
        player=life.Gameplay.PlayerTransform;weather=(LifeLocalWeather)Field(life,"weather");sky=(WeatherRegion)Field(weather,"sky");
        reference=Object.FindObjectsOfType<WildlifeAgent>().SingleOrDefault(a=>a.spawnId==6101 && a.speciesId=="sheep");Require(reference,"Original harbor sheep missing");
        Require(PlacePlayer(),"No safe supported dry player point at 6.5m");
        camera=life.Gameplay.CurrentView;Require(camera,"Actual current camera missing");var orbit=camera.GetComponent<VegetationTopDownCamera>();
        if(orbit){Vector3 toward=reference.habitatCenter-player.position;float yaw=Mathf.Atan2(toward.x,toward.z)*Mathf.Rad2Deg;orbit.StepOrbit(Mathf.DeltaAngle(orbit.Yaw,yaw),46-orbit.pitch,orbit.distance-22);orbit.SnapToPlayer();orbit.enabled=false;}
        cameraSite=camera.transform.position;cameraRotation=camera.transform.rotation;
        Require(Mathf.Approximately(sky.radius,8),"Expected normal 8m rain radius");Require(weather.Cast("clear",out _),"Clear failed");weather.Apply(life.World,.000001f);
        animals=new List<AnimalView>();
        foreach(object state in (IEnumerable)Field(director,"animals"))animals.Add(new AnimalView{state=state,id=(string)Field(state,"id"),root=(Transform)Field(state,"root"),agent=(WildlifeAgent)Field(state,"agent"),herd=(JourneyHerd)Field(state,"herd"),member=(JourneyMember)Field(state,"member"),action=state.GetType().GetField("action",Flags),shelterQuery=state.GetType().GetField("shelterQuery",Flags),search=state.GetType().GetField("search",Flags),route=state.GetType().GetField("route",Flags)});
        allWildlife=Object.FindObjectsOfType<WildlifeAgent>();allHerds=Object.FindObjectsOfType<JourneyHerd>();
        string[] sources={"Assets/Game/World/Wildlife/Runtime/AnimalAutonomy.cs","Assets/Game/World/Wildlife/Runtime/WildlifePopulation.cs","Assets/Game/World/Wildlife/Runtime/WildlifeAgent.cs","Assets/Game/World/Wildlife/Journey/Runtime/JourneyHerd.cs","Assets/Game/World/Wildlife/Journey/Runtime/JourneyNavigation.cs","Assets/Game/World/Wildlife/Runtime/AnimalLocalRoute.cs","Assets/Game/World/Wildlife/Runtime/AnimalEmotionGlyph.cs","Assets/Game/World/Wildlife/Journey/Runtime/JourneyCrowdPath.cs"};
        string harness=Directory.GetFiles(Application.dataPath,"AnimalPerformanceReview.cs",SearchOption.AllDirectories).SingleOrDefault();
        report=new Report{variant=SessionState.GetString(Key+".variant",""),utcStart=DateTime.UtcNow.ToString("O"),unityVersion=Application.unityVersion,graphicsDevice=SystemInfo.graphicsDeviceName,graphicsApi=SystemInfo.graphicsDeviceType.ToString(),scene=ScenePath,
            width=camera.pixelWidth,height=camera.pixelHeight,quality=QualitySettings.GetQualityLevel(),vSync=QualitySettings.vSyncCount,targetFrameRate=Application.targetFrameRate,processorCount=SystemInfo.processorCount,timeScale=Time.timeScale,fixedDeltaTime=Time.fixedDeltaTime,
            focused=Application.isFocused,networkDisabled=true,playerPosition=playerSite,cameraPosition=cameraSite,cameraRotation=cameraRotation,harnessSha256=harness==null?"MISSING":Hash(harness),sourceFiles=sources.Concat(new[]{ScenePath}).Select(p=>new FileStamp{path=p,sha256=Hash(p)}).ToArray()};
        preparedAt=Time.time;stage=1;Event("PASS fixed player/camera arranged; no animal mutation; total autonomy population="+animals.Count);
        WriteSummary();
    }
    static void OpenCounters()
    {
        var handles=new List<ProfilerRecorderHandle>();ProfilerRecorderHandle.GetAvailable(handles);
        var inventory=new List<string>{"category,name,unit,dataType"};
        foreach(var h in handles){var d=ProfilerRecorderHandle.GetDescription(h);inventory.Add(Csv(d.Category.Name)+","+Csv(d.Name)+","+Csv(d.UnitType.ToString())+","+Csv(d.DataType.ToString()));}
        File.WriteAllLines(Path.Combine(Folder,"available-counters.csv"),inventory);
        counters=new Counter[CounterNames.Length];
        for(int i=0;i<counters.Length;i++)
        {
            var counter=new Counter();counters[i]=counter;counter.info.metric=MetricNames[i];counter.info.status="unavailable: not registered";
            foreach(string name in CounterNames[i])
            {
                var matching=handles.Where(h=>ProfilerRecorderHandle.GetDescription(h).Name==name).ToArray();if(matching.Length!=1)continue;
                var d=ProfilerRecorderHandle.GetDescription(matching[0]);counter.info.registeredName=d.Name;counter.info.category=d.Category.Name;counter.info.unit=d.UnitType.ToString();
                bool expected=i==4?counter.info.unit=="Bytes":counter.info.unit=="TimeNanoseconds" || counter.info.unit=="Nanoseconds";
                if(!expected){counter.info.status="unavailable: unexpected unit "+counter.info.unit;break;}
                try{counter.recorder=new ProfilerRecorder(matching[0],Capacity,ProfilerRecorderOptions.Default);counter.info.status=counter.recorder.Valid?"registered; awaiting samples":"unavailable: invalid recorder";}
                catch(Exception error){counter.info.status="unavailable: "+error.GetType().Name;}
                break;
            }
        }
        report.counters=counters.Select(c=>c.info).ToArray();Event("COUNTERS inventory="+handles.Count+"; "+string.Join("; ",report.counters.Select(c=>c.metric+"="+c.status)));
    }
    static void Census(int phase)
    {
        censusAnimals=censusAllScene=censusNearby=censusActions=censusMoving=censusManual=censusAuto=censusExposed=censusSheltering=censusHolding=censusPlanning=censusFollowing=0;censusMean=censusMax=0;
        foreach(var a in allWildlife)if(a && a.isActiveAndEnabled){censusAllScene++;if(Flat(a.transform.position,playerSite)<=8)censusNearby++;}
        foreach(var herd in allHerds)if(herd && herd.isActiveAndEnabled)foreach(var member in herd.members)if(member!=null && member.root && member.root.gameObject.activeInHierarchy){censusAllScene++;if(Flat(member.root.position,playerSite)<=8)censusNearby++;}
        foreach(var a in animals)
        {
            bool live=a.root && a.root.gameObject.activeInHierarchy && (a.agent?a.agent.isActiveAndEnabled:a.herd && a.herd.isActiveAndEnabled);if(!live)continue;
            bool lease=a.agent?a.agent.HasLifeTarget:a.herd.HasLifeTarget(a.member.id),automatic=lease && (a.agent?a.agent.AutonomousTarget:a.herd.IsAutonomous(a.member.id));
            float speed=a.agent?a.agent.ActualSpeed:a.member.ActualSpeed;string action=(string)a.action.GetValue(a.state);bool wet=sky.SoundActive && sky.Contains(a.root.position);
            bool planning=a.shelterQuery?.GetValue(a.state)!=null || a.search?.GetValue(a.state)!=null,following=a.route?.GetValue(a.state) is IList route && route.Count>0;
            if(action=="shelter"){censusSheltering++;if(!planning && speed<=.05f)censusHolding++;}if(planning)censusPlanning++;if(following)censusFollowing++;
            censusAnimals++;if(!string.IsNullOrEmpty(action) && action!="resume")censusActions++;if(speed>.05f)censusMoving++;if(lease){if(automatic)censusAuto++;else censusManual++;}if(wet)censusExposed++;
            censusMean+=speed;censusMax=Mathf.Max(censusMax,speed);census.Add(new CensusRow{phase=phase,frame=Time.frameCount,simulation=Time.time,id=a.id,action=action,position=a.root.position,speed=speed,live=live,lease=lease,autonomous=automatic,wet=wet,planning=planning,following=following});
        }
        if(censusAnimals>0)censusMean/=censusAnimals;censusAt=Time.time;
    }
    static void BeginPhase(int phase)
    {
        phaseFirst=frames.Count;phaseFirstFrame=Time.frameCount;phaseAt=Time.time;phaseClock=lastClock=Clock;
        foreach(var counter in counters)counter.StartPhase();Census(phase);
        report.phases.Add(new Phase{name=phase==0?"sunny":"strong-rain-8m",firstFrame=Time.frameCount,initialAnimals=censusAnimals,initialExposed=censusExposed});stage=phase==0?2:4;
    }
    static void Sample(int phase)
    {
        double now=Clock;if(Time.time-censusAt>=.5f)Census(phase);
        Require(frames.Count<Capacity,"Frame capacity reached; benchmark incomplete");
        frames.Add(new Frame{phase=phase,frame=Time.frameCount,gap=Time.frameCount-lastFrame,simulation=Time.time,wallMs=(now-lastClock)*1000,engineMs=Time.unscaledDeltaTime*1000,
            playerLoop=counters[0].Read(),behaviour=counters[1].Read(),main=counters[2].Read(),render=counters[3].Read(),allocation=counters[4].Read(),physics=counters[5].Read(),
            animals=censusAnimals,allScene=censusAllScene,nearby=censusNearby,actions=censusActions,moving=censusMoving,manual=censusManual,automatic=censusAuto,exposed=censusExposed,sheltering=censusSheltering,shelterHolding=censusHolding,planning=censusPlanning,following=censusFollowing,meanSpeed=censusMean,maxSpeed=censusMax});lastClock=now;
        if(Time.time-phaseAt>=SampleSeconds && frames.Count-phaseFirst>=MinFrames)EndPhase(phase);
    }
    static void EndPhase(int phase)
    {
        foreach(var counter in counters)if(counter.recorder.Valid)counter.recorder.Stop();
        Phase p=report.phases[phase];p.frames=frames.Count-phaseFirst;p.lastFrame=Time.frameCount;p.simSeconds=Time.time-phaseAt;p.wallSeconds=Clock-phaseClock;p.finalAnimals=censusAnimals;p.finalExposed=censusExposed;
        Frame[] slice=frames.Skip(phaseFirst).ToArray();p.missedFrameIntervals=slice.Sum(f=>Mathf.Max(0,f.gap-1));
        p.metrics=new[]{Stats("editorFrameIntervalMs","ms",slice.Select(f=>f.wallMs),p.frames),Stats("engineFrameMs","ms",slice.Select(f=>f.engineMs),p.frames),
            Stats(MetricNames[0],"ms",slice.Select(f=>f.playerLoop),p.frames),Stats(MetricNames[1],"ms",slice.Select(f=>f.behaviour),p.frames),Stats(MetricNames[2],"ms",slice.Select(f=>f.main),p.frames),
            Stats(MetricNames[3],"ms",slice.Select(f=>f.render),p.frames),Stats(MetricNames[4],"bytes/frame",slice.Select(f=>f.allocation),p.frames),Stats(MetricNames[5],"ms",slice.Select(f=>f.physics),p.frames)};
        p.workload=new[]{Stats("allSceneAnimals","animals",slice.Select(f=>(double)f.allScene),p.frames),Stats("autonomyAnimals","animals",slice.Select(f=>(double)f.animals),p.frames),Stats("nearbyAnimals8m","animals",slice.Select(f=>(double)f.nearby),p.frames),
            Stats("actions","animals",slice.Select(f=>(double)f.actions),p.frames),Stats("shelterActions","animals",slice.Select(f=>(double)f.sheltering),p.frames),Stats("stationaryShelterActions","animals",slice.Select(f=>(double)f.shelterHolding),p.frames),
            Stats("pendingRouteAnimals","animals",slice.Select(f=>(double)f.planning),p.frames),Stats("routeFollowingAnimals","animals",slice.Select(f=>(double)f.following),p.frames),Stats("movingAnimals","animals",slice.Select(f=>(double)f.moving),p.frames),
            Stats("manualLeases","animals",slice.Select(f=>(double)f.manual),p.frames),Stats("autonomousLeases","animals",slice.Select(f=>(double)f.automatic),p.frames),Stats("rainCoveredAnimals","animals",slice.Select(f=>(double)f.exposed),p.frames),
            Stats("meanAnimalSpeed","m/s",slice.Select(f=>(double)f.meanSpeed),p.frames),Stats("maximumAnimalSpeed","m/s",slice.Select(f=>(double)f.maxSpeed),p.frames)};
        Event("PASS sampled "+p.name+" simSeconds="+p.simSeconds.ToString("F3",Invariant)+" frames="+p.frames+" missedIntervals="+p.missedFrameIntervals);WriteSummary();
        if(phase==0){Require(weather.Cast("rain",out _) && weather.Cast("strong",out _),"Strong rain cast failed");weather.Apply(life.World,.000001f);Census(1);Require(sky.SoundActive && sky.SoundStrength==2 && censusExposed>0,"Normal strong rain must cover at least one actual animal");stage=3;}
        else Finish(null);
    }
    static Metric Stats(string name,string unit,IEnumerable<double> values,int total)
    {
        double[] sorted=values.Where(v=>v>=0 && !double.IsNaN(v) && !double.IsInfinity(v)).OrderBy(v=>v).ToArray();
        var m=new Metric{name=name,unit=unit,samples=sorted.Length,missing=total-sorted.Length,status=sorted.Length==0?"unavailable: no emitted samples":sorted.Length<total?"available: partial coverage":"available"};
        if(sorted.Length>0){m.mean=sorted.Average();m.p50=Percentile(sorted,.5);m.p95=Percentile(sorted,.95);m.p99=Percentile(sorted,.99);m.max=sorted[sorted.Length-1];}return m;
    }
    static double Percentile(double[] sorted,double p){double index=(sorted.Length-1)*p;int low=(int)Math.Floor(index),high=(int)Math.Ceiling(index);return sorted[low]+(sorted[high]-sorted[low])*(index-low);}
    static void Tick()
    {
        if(!SessionState.GetBool(Key,false) || SessionState.GetBool(Key+".finishing",false) || !EditorApplication.isPlaying || Time.frameCount==lastFrame)return;
        try
        {
            Require(Clock-began<900,"900 second wall timeout");Require(!EditorApplication.isPaused,"Paused benchmark is invalid");
            if(stage==0){if(Time.time>=3)Prepare();}
            else
            {
                Require(Flat(player.position,playerSite)<.2f,"Player moved during fixed-view benchmark");Require(Vector3.Distance(camera.transform.position,cameraSite)<.01f && Quaternion.Angle(camera.transform.rotation,cameraRotation)<.1f,"Camera changed during fixed-view benchmark");
                if(stage==1 && Time.time-preparedAt>=InitialWarmSeconds && Time.frameCount>=150){OpenCounters();stage=5;preparedAt=Time.time;}
                else if(stage==5 && Time.time-preparedAt>=1){BeginPhase(0);}
                else if(stage==2)Sample(0);
                else if(stage==3)BeginPhase(1);
                else if(stage==4){Require(sky.SoundActive && sky.SoundStrength==2 && Mathf.Approximately(sky.radius,8),"Rain phase weather changed");Sample(1);}
            }
        }
        catch(Exception error){Event("FAIL "+error);Finish(error);}
        lastFrame=Time.frameCount;
    }
    static void WriteSummary(){if(report!=null)File.WriteAllText(Path.Combine(Folder,"summary.json"),JsonUtility.ToJson(report,true));}
    static void WriteCsv()
    {
        using(var writer=new StreamWriter(Path.Combine(Folder,"frames.csv"),false,new UTF8Encoding(false)))
        {
            writer.WriteLine("phase,frame,frameGap,simulationSeconds,editorIntervalMs,engineFrameMs,playerLoopMs,behaviourUpdateMs,mainThreadMs,renderThreadMs,gcAllocatedBytes,physicsMs,autonomyAnimals,allSceneAnimals,nearbyAnimals8m,actions,moving,manualLeases,autonomousLeases,exposed,shelterActions,stationaryShelterActions,pendingRouteAnimals,routeFollowingAnimals,meanSpeed,maxSpeed");
            foreach(var f in frames)writer.WriteLine(string.Join(",",f.phase,f.frame,f.gap,Num(f.simulation),Num(f.wallMs),Num(f.engineMs),Num(f.playerLoop),Num(f.behaviour),Num(f.main),Num(f.render),Num(f.allocation),Num(f.physics),f.animals,f.allScene,f.nearby,f.actions,f.moving,f.manual,f.automatic,f.exposed,f.sheltering,f.shelterHolding,f.planning,f.following,Num(f.meanSpeed),Num(f.maxSpeed)));
        }
        using(var writer=new StreamWriter(Path.Combine(Folder,"animals.csv"),false,new UTF8Encoding(false)))
        {
            writer.WriteLine("phase,frame,simulationSeconds,id,action,x,y,z,actualSpeed,live,hasLease,autonomous,rainCovered,routePending,routeFollowing");
            foreach(var a in census)writer.WriteLine(string.Join(",",a.phase,a.frame,Num(a.simulation),Csv(a.id),Csv(a.action),Raw(a.position.x),Raw(a.position.y),Raw(a.position.z),Num(a.speed),a.live,a.lease,a.autonomous,a.wet,a.planning,a.following));
        }
    }
    static void Finish(Exception error)
    {
        if(SessionState.GetBool(Key+".finishing",false))return;SessionState.SetBool(Key+".finishing",true);
        if(counters!=null)foreach(var counter in counters){if(counter.info.status=="registered; awaiting samples")counter.info.status="unavailable: no emitted samples";counter.Dispose();}
        if(report!=null){report.status=error==null?"SAMPLED_PENDING_PROTECTION":"FAIL";report.error=error?.Message;report.utcEnd=DateTime.UtcNow.ToString("O");WriteCsv();WriteSummary();}
        SessionState.SetInt(Key+".exit",error==null?0:1);Event("RESULT failures="+(error==null?0:1));
        if(EditorApplication.isPlaying)EditorApplication.isPlaying=false;else VerifyAndExit();
    }
    static void VerifyAndExit()
    {
        int exit=SessionState.GetInt(Key+".exit",1);var lines=new List<string>();
        foreach(string saved in SessionState.GetString(Key+".protected","").Split('\n'))
        {
            int separator=saved.LastIndexOf('|');if(separator<0)continue;string path=saved.Substring(0,separator);bool same=Hash(path)==saved.Substring(separator+1);if(!same)exit=1;lines.Add((same?"PASS ":"FAIL ")+"Protected file unchanged: "+path);
        }
        if(EditorApplication.isPlaying)exit=1;lines.Add((!EditorApplication.isPlaying?"PASS ":"FAIL ")+"Play ended; no scene save requested");
        File.WriteAllLines(Path.Combine(Folder,"save-protection.txt"),lines);
        string summary=Path.Combine(Folder,"summary.json");if(File.Exists(summary)){var saved=JsonUtility.FromJson<Report>(File.ReadAllText(summary));saved.status=exit==0?"PASS":"FAIL";File.WriteAllText(summary,JsonUtility.ToJson(saved,true));}
        File.WriteAllText(Path.Combine(Folder,"result.txt"),"RESULT exit="+exit+"\n");SessionState.SetBool(Key,false);EditorApplication.Exit(exit);
    }
}
