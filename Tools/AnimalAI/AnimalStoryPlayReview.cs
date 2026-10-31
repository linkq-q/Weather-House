// Independent ValidationProject batch fixture. Never moves animals or saves the scene.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using WeatherHouse.Hillside;
using WeatherHouse.Life;
using WeatherHouse.WatersideV2;
using WeatherHouse.WildlifeJourney;
using WeatherHouse.Vegetation;
using WeatherHousePrototype;
using Object = UnityEngine.Object;

[InitializeOnLoad]
public static class AnimalStoryPlayReview
{
    [Serializable] public sealed class ProcessEvidence
    {
        public int processId,exitCode;
        public string processStartedUtc,finishedUtc,reportPath;
        public bool playEnded;
    }
    const string Key="WeatherHouse.AnimalStoryPlayReview", ScenePath="Assets/Scenes/WeatherHouse_WatersideV2.unity";
    static readonly BindingFlags Flags=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
    static readonly List<string> log=new List<string>();
    static readonly List<AnimalStoryEvent> completed=new List<AnimalStoryEvent>();
    static AnimalAutonomy director;
    static AnimalPlannerClient client;
    static LifeSession life;
    static Transform player,root;
    static WildlifeAgent agent;
    static object animal;
    static AnimalStoryState story;
    static LifeSpeaker speaker;
    static int stage,episode,failures,lastFrame=-1;
    static float wallStart,stageStart,episodeStart,lastSample,arrivedAt=-1,participatedAt=-1,leaveAt=-1,pathLength;
    static Vector3 originalPosition,lastPosition,episodePosition,playerPlaced,point,view;
    static bool placedNear,sawMoving,started;
    static string episodeId,parameters,previousTeaching;
    static string Folder=>SessionState.GetString(Key+".folder","");
    static object Field(object value,string name)=>value?.GetType().GetField(name,Flags)?.GetValue(value);
    static object Property(object value,string name)=>value?.GetType().GetProperty(name,Flags)?.GetValue(value);
    static void Set(object value,string name,object content)=>value.GetType().GetField(name,Flags).SetValue(value,content);
    static object Call(object value,string name,params object[] args)=>value.GetType().GetMethod(name,Flags).Invoke(value,args);
    static float Flat(Vector3 a,Vector3 b){a.y=b.y=0;return Vector3.Distance(a,b);}
    static void Require(bool ok,string message){if(!ok)throw new InvalidOperationException(message);}
    static void Record(string message)
    {log.Add("t="+Time.time.ToString("F3")+" frame="+Time.frameCount+" "+message);File.WriteAllLines(Path.Combine(Folder,"observations.txt"),log);}
    static void Check(bool ok,string message){if(!ok)failures++;Record((ok?"PASS ":"FAIL ")+message);}
    static void Next(int value){stage=value;stageStart=Time.time;}
    static string Hash(string path)
    {if(!File.Exists(path))return "MISSING";using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-","");}

    static AnimalStoryPlayReview()
    {
        EditorApplication.playModeStateChanged+=state=>{
            if(!SessionState.GetBool(Key,false))return;
            if(state==PlayModeStateChange.EnteredPlayMode){wallStart=Time.realtimeSinceStartup;Application.runInBackground=true;stage=0;}
            if(state==PlayModeStateChange.EnteredEditMode && SessionState.GetBool(Key+".finishing",false))VerifyAndExit();
        };
        EditorApplication.update+=Tick;
    }
    public static void Batch()
    {
        Require(Application.isBatchMode && Application.dataPath.Contains("ValidationProject"),"Independent ValidationProject batch only");
        string folder=Environment.GetEnvironmentVariable("WEATHERHOUSE_ANIMAL_STORY_REPORT");
        Require(!string.IsNullOrWhiteSpace(folder),"Set WEATHERHOUSE_ANIMAL_STORY_REPORT to a fresh evidence folder");
        folder=Path.GetFullPath(folder);
        Require(!Directory.Exists(folder) || !Directory.EnumerateFileSystemEntries(folder).Any(),"Evidence folder must be empty");
        Directory.CreateDirectory(folder);SessionState.SetString(Key+".folder",folder);
        using(var process=System.Diagnostics.Process.GetCurrentProcess())
            File.WriteAllText(Path.Combine(folder,"run-context.json"),JsonUtility.ToJson(new ProcessEvidence{
                processId=process.Id,processStartedUtc=process.StartTime.ToUniversalTime().ToString("O"),reportPath=folder},true));
        SessionState.SetString(Key+".sceneHash",Hash(ScenePath));
        var saves=new List<string>();
        foreach(string name in new[]{"language-notebook.json","life-progress.json","onboarding.json"})
        {string path=Path.Combine(Application.persistentDataPath,name);saves.Add(path+"|"+Hash(path));}
        SessionState.SetString(Key+".saves",string.Join("\n",saves));
        Environment.SetEnvironmentVariable("WEATHERHOUSE_LIFE_REVIEW_SAVE",Path.Combine(folder,"isolated-save"));
        Environment.SetEnvironmentVariable("WEATHERHOUSE_ONBOARDING_REVIEW",null);
        Directory.CreateDirectory(Path.Combine(folder,"isolated-save"));
        SessionState.SetBool(Key,true);SessionState.SetBool(Key+".finishing",false);
        try
        {
            // Reject an accidentally enabled clone before any MonoBehaviour starts; never print configuration content.
            if(File.Exists(AnimalPlannerClient.ConfigurationPath))
            {
                var config=JsonUtility.FromJson<AnimalPlannerClient.Configuration>(File.ReadAllText(AnimalPlannerClient.ConfigurationPath));
                Require(config!=null && !config.enabled,"Clone planner configuration must explicitly disable network");
            }
            var checks=new List<string>();
            foreach(string name in new[]{"AnimalStoryStateChecks","AnimalStoryPlanChecks","AnimalStoryIntegrationChecks","AnimalStorySightlineChecks","DemoTeachingChecks","AnimalGlyphChecks"})
            {
                Type type=AppDomain.CurrentDomain.GetAssemblies().SelectMany(Types).FirstOrDefault(t=>t.Name==name);
                Require(type!=null,"Missing staged contract "+name);
                object output=type.GetMethod("Run",BindingFlags.Public|BindingFlags.Static).Invoke(null,null);
                if(output is IEnumerable<string> lines)checks.AddRange(lines.Select(line=>name+": "+line));
                else if(name=="AnimalGlyphChecks")
                {string source="PrototypeReview/animal-glyphs.txt";Require(File.Exists(source),"Glyph report missing");checks.AddRange(File.ReadAllLines(source).Select(line=>name+": "+line));}
                else checks.Add("PASS "+name+" returned normally");
                File.WriteAllLines(Path.Combine(folder,"contracts.txt"),checks);
            }
            Type photo=AppDomain.CurrentDomain.GetAssemblies().SelectMany(Types).FirstOrDefault(t=>t.Name=="LifePhotoFlowChecks");
            Require(photo!=null,"Missing LifePhotoFlowChecks");
            var catalog=JsonUtility.FromJson<GlyphCatalog>(Resources.Load<TextAsset>("WeatherHouseGlyphs").text);
            var photoChecks=(IEnumerable<string>)photo.GetMethod("CheckModel",BindingFlags.Public|BindingFlags.Static).Invoke(null,new object[]{catalog});
            checks.AddRange(photoChecks.Select(line=>"LifePhotoFlowChecks: "+line));
            File.WriteAllLines(Path.Combine(folder,"contracts.txt"),checks);
            Require(!checks.Any(line=>line.Contains("FAIL ")),"Prerequisite contracts failed");
            EditorSceneManager.OpenScene(ScenePath);EditorApplication.isPlaying=true;
        }
        catch(Exception error)
        {File.AppendAllText(Path.Combine(folder,"contracts.txt"),"\nFAIL "+Unwrap(error));SessionState.SetInt(Key+".exit",1);VerifyAndExit();}
    }
    static IEnumerable<Type> Types(Assembly assembly)
    {try{return assembly.GetTypes();}catch(ReflectionTypeLoadException error){return error.Types.Where(t=>t!=null);}}
    static Exception Unwrap(Exception error)=>error is TargetInvocationException invocation && invocation.InnerException!=null?invocation.InnerException:error;
    static string Parameters()=>"timidity="+agent.timidity+";sociability="+agent.sociability+";outgoing="+Field(animal,"outgoing")+
        ";habitat="+agent.habitatCenter.ToString("R")+";radius="+agent.habitatRadius+";scale="+root.localScale.ToString("R");
    static string State()
    {
        if(animal==null)return "scene initializing";
        AnimalStoryEpisode active=story.active;Vector3 facing=view-root.position;facing.y=0;
        string rawObstruction="clear";
        if(Physics.Linecast(root.position+Vector3.up*.8f,view,out RaycastHit hit,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore))rawObstruction=PathOf(hit.transform);
        bool visible=AnimalStorySightline.IsClear(root.position+Vector3.up*.8f,view,root,out Collider blocker);
        string obstruction=visible?"clear":blocker?PathOf(blocker.transform):"blocked/query-capacity";
        return "episode="+episode+";id="+episodeId+";action="+Field(animal,"action")+";root="+root.position.ToString("F3")+
            ";goal="+Field(animal,"goal")+";point="+point.ToString("F3")+";at="+(Flat(root.position,point)<.3f)+
            ";speed="+agent.ActualSpeed.ToString("F4")+";facing="+Vector3.Dot(root.forward,facing.normalized).ToString("F4")+
            ";seaLOS="+obstruction+";seaLOSRaw="+rawObstruction+";playerDistance="+Flat(root.position,player.position).ToString("F3")+";player="+player.position.ToString("F3")+
            ";active="+(active==null?"null":JsonUtility.ToJson(active))+";available="+Property(animal,"Available")+
            ";interaction="+Property(animal,"Interaction")+";agentState="+agent.State+";search="+(Field(animal,"search")!=null)+
            ";routeQueries="+(Field(animal,"search") as AnimalLocalRoute)?.Queries+";routeStep="+Field(animal,"routeStep")+";path="+pathLength.ToString("F3");
    }
    static string PathOf(Transform value)=>value.parent?PathOf(value.parent)+"/"+value.name:value.name;
    static void Initialize()
    {
        life=Object.FindObjectOfType<LifeSession>();Require(life && life.World && life.Gameplay,"Formal scene LifeSession is unavailable");
        player=life.Gameplay.PlayerTransform;
        animal=((IList)Field(director,"animals")).Cast<object>().SingleOrDefault(a=>(string)Field(a,"id")=="life.harbor.sheep");
        Require(animal!=null,"Actual life.harbor.sheep was not registered");
        root=(Transform)Field(animal,"root");agent=(WildlifeAgent)Field(animal,"agent");
        story=(AnimalStoryState)Field(animal,"story");speaker=(LifeSpeaker)Field(animal,"speaker");
        Require(agent && story!=null && speaker,"Demo actor has no actual movement/story/speaker binding");
        Require(story.events.Count==0 && story.active==null,"Fresh isolated save must start without story history");
        Require(life.World.TrySeaWatchPoint(out point,out view),"No valid sea watch destination");
        Require(!LifeOnboarding.Blocks(life.Gameplay) && LifeOnboarding.For(life.Gameplay)?.TutorialActive!=true,
            "Existing isolated-review onboarding suppression did not take effect");
        originalPosition=lastPosition=root.position;parameters=Parameters();
        Require(Flat(originalPosition,point)>1,"First episode must begin at the real spawn/wandering position over 1m from the goal");
        Record("SETUP actual saved formal scene; untouched animal pose/personality/motion; network disabled; legal watch_sea selection and player test positions only. Normal clocks and local autonomy remain enabled. Existing review-save bootstrap suppresses onboarding; this does not test the new-player tutorial. Fixed speech later is not a network response.");
        Record("ORIGINAL animal="+root.position.ToString("R")+";player="+player.position.ToString("R")+";"+parameters);
        PlacePlayer(false);Capture("00-original");started=true;Next(1);
    }
    static bool TryPlayerPoint(bool near,out Vector3 result)
    {
        result=default;Terrain terrain=Terrain.activeTerrain;CharacterController capsule=player.GetComponent<CharacterController>();
        JourneyNavigation navigation=Object.FindObjectOfType<JourneyNavigation>();
        if(!terrain || !terrain.terrainData || !capsule || !navigation)return false;
        var water=player.GetComponent<WatersideWaterBoundary>();
        Vector3 offset=player.TransformVector(capsule.center),size=terrain.terrainData.size;
        float height=capsule.height*Mathf.Abs(player.lossyScale.y),radius=capsule.radius*Mathf.Max(Mathf.Abs(player.lossyScale.x),Mathf.Abs(player.lossyScale.z));
        float half=Mathf.Max(0,height*.5f-radius);
        foreach(float distance in near?new[]{3.1f}:new[]{10f,12f,16f})
        for(int step=0;step<24;step++)
        {
            Vector3 candidate=root.position+Quaternion.Euler(0,step*15,0)*Vector3.forward*distance;
            Vector3 local=candidate-terrain.transform.position;
            if(local.x<0 || local.z<0 || local.x>size.x || local.z>size.z || terrain.terrainData.GetSteepness(local.x/size.x,local.z/size.z)>30)continue;
            float ground=terrain.SampleHeight(candidate)+terrain.transform.position.y;
            if(water && water.surface && water.terrain && water.SampleSupportedDepth(candidate,ground)>.02f)continue;
            if(WatersideLayout.SeaSignedDistance(candidate.x,candidate.z)<.2f)continue;
            candidate.y=ground-offset.y+height*.5f+.08f;
            Vector3 center=candidate+offset;
            if(Physics.OverlapCapsule(center+Vector3.up*half,center-Vector3.up*half,radius*.95f,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore)
                .Any(c=>!(c is TerrainCollider) && !c.transform.IsChildOf(player)))continue;
            if(near)
            {
                if(Mathf.Abs(candidate.y-root.position.y)>=1.5f)continue;
                if(Physics.Linecast(root.position+Vector3.up*.8f,candidate+Vector3.up,out RaycastHit hit,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore) &&
                    !hit.transform.IsChildOf(root) && !hit.transform.IsChildOf(player))continue;
                var profile=new JourneyProfile{bodyRadius=radius,canSwim=false,wadeDepth=0,walkSpeed=1};
                JourneyRoute route=navigation.TraceDirectRoute(root.position,new Vector3(candidate.x,ground,candidate.z),profile);
                if(route==null || route.segments.Any(segment=>segment.mode!=JourneyTravelMode.Walk))continue;
            }
            else if(Flat(candidate,point)<7)continue;
            result=candidate;return true;
        }
        return false;
    }
    static void PlacePlayer(bool near)
    {
        Require(TryPlayerPoint(near,out Vector3 destination),"No supported, dry, capsule-clear "+(near?"3.1m companion":"far solo")+" player site");
        var capsule=player.GetComponent<CharacterController>();bool wasEnabled=capsule && capsule.enabled;
        if(wasEnabled)capsule.enabled=false;player.position=destination;if(wasEnabled)capsule.enabled=true;
        Physics.SyncTransforms();playerPlaced=destination;
        if(life.Gameplay.forestSprite)life.Gameplay.forestSprite.SetScriptedInput(Vector2.zero,false,false);
        Record("PLAYER fixture placed once at="+destination.ToString("F3")+";near="+near+"; stationary thereafter, no per-frame following");
    }
    static void BeginEpisode()
    {
        Require((bool)Property(animal,"Available"),"Real animal unavailable before selected activity; "+State());
        Require(story.active==null,"An earlier activity is still active");
        Require(life.World.TrySeaWatchPoint(out point,out view),"Sea point vanished");
        episode++;episodePosition=lastPosition=root.position;pathLength=0;sawMoving=placedNear=false;
        arrivedAt=participatedAt=leaveAt=-1;episodeStart=Time.time;
        // Explicit action choice bypasses only the ordinary between-activity choice cooldown; no behavior state/timer is written.
        Call(director,"Begin",animal,new AnimalOption{id="watch_sea",action="watch_sea",target="harbor.tide"},point);
        episodeId=story.active?.id;Require(!string.IsNullOrEmpty(episodeId),"Begin did not create a real watch episode");
        Record("SELECT watch_sea via production Begin; scenario="+(episode==1?"solo":episode==2?"continuous companion":"joined then left")+"; "+State());
        Capture("episode"+episode+"-start");Next(2);
    }
    static void ObserveEpisode()
    {
        float step=Flat(root.position,lastPosition);pathLength+=step;lastPosition=root.position;
        if(agent.ActualSpeed>.05f && Flat(root.position,episodePosition)>.3f && !sawMoving)
        {sawMoving=true;Record("EVENT actual locomotion; "+State());Capture("episode"+episode+"-walking");}
        AnimalStoryEpisode active=story.active;
        if(active!=null && active.id==episodeId)
        {
            if(active.arrived && arrivedAt<0)
            {
                arrivedAt=Time.time;Record("EVENT arrived and stopped facing the sea; "+State());Capture("episode"+episode+"-arrived");
                if(episode>1){PlacePlayer(true);placedNear=true;Capture("episode"+episode+"-player-near");}
            }
            if(active.playerParticipated && participatedAt<0)
            {
                participatedAt=Time.time;Record("EVENT actual continuous participation >=3s; "+State());Capture("episode"+episode+"-participated");
                if(episode==3){PlacePlayer(false);leaveAt=Time.time;Capture("episode3-player-left");}
            }
            if(placedNear && leaveAt<0)Require(Flat(player.position,playerPlaced)<.15f,"Test player moved after its one-time companion placement");
            if(Time.time-episodeStart>121)throw new TimeoutException("Actual watch episode exceeded 120s; "+State());
            return;
        }
        AnimalStoryEvent evidence=story.FindEvent(episodeId);
        Require(evidence!=null,"Active episode disappeared without an event; "+State());
        Record("EVENT closed "+JsonUtility.ToJson(evidence));Capture("episode"+episode+"-complete");
        Check(evidence.outcome=="completed","Episode "+episode+" completed through real arrival/stopped-facing observation, outcome="+evidence.outcome+" reason="+evidence.reason);
        Check(evidence.dwellSeconds>=8 && arrivedAt>=0 && Time.time-arrivedAt>=7.5f,"Episode "+episode+" accumulated >=8 production dwell seconds and >=7.5 wall simulation seconds after first observed arrival");
        if(episode==1)Check(sawMoving && pathLength>1 && Flat(root.position,originalPosition)>1,"First episode genuinely walked from its original position; path="+pathLength.ToString("F3"));
        bool expectedParticipated=episode>1,expectedLeft=episode==3;
        Check(evidence.playerParticipated==expectedParticipated && evidence.playerLeftEarly==expectedLeft,
            "Episode "+episode+" evidence participant="+evidence.playerParticipated+" leftEarly="+evidence.playerLeftEarly);
        if(episode>1)Check(evidence.companionSeconds>=3 && participatedAt>=0,"Episode "+episode+" has actual >=3s companionship");
        else Check(evidence.companionSeconds==0,"Solo record contains no invented companion time");
        if(episode==3)Check(leaveAt>=0 && Time.time-leaveAt>=2,"Leaving evidence waits >=2s after actual player departure");
        completed.Add(evidence);File.WriteAllText(Path.Combine(Folder,"story-state.json"),JsonUtility.ToJson(story,true));
        Require(evidence.outcome=="completed","Stop after actual execution failure; "+State());
        if(episode<3){PlacePlayer(false);Next(1);}else Next(3);
    }
    static bool ExpressionReady()
    {
        object policy=Field(animal,"expression");var glyph=(AnimalEmotionGlyph)Field(animal,"emotion");
        if(glyph.IsPlaying || Time.time<(float)Field(policy,"nextOrdinary"))return false;
        var cooldowns=(Dictionary<AnimalEmotion,float>)Field(policy,"nextEmotion");
        return !cooldowns.TryGetValue(AnimalEmotion.Heart,out float until) || Time.time>=until;
    }
    static void ApplySpeech()
    {
        var observation=(AnimalObservation)Call(director,"Observe",animal);
        Require(observation.canShareStory,"Actual current observation refuses story sharing; "+State());
        previousTeaching=speaker.Current?.id;string tasksBefore=JsonUtility.ToJson(life.Progress);
        int taskCount=life.Progress.tasks.Count;
        var fixture=new AnimalPlan{revision=observation.revision,choices=new[]{"resume"},speech=new AnimalStorySpeech{
            intent="preference",subject="sea",emotion="heart",glyphs=new[]{"self","like","look","sea"}}};
        string json=JsonUtility.ToJson(fixture);File.WriteAllText(Path.Combine(Folder,"fixed-model-response.json"),json);
        Require(AnimalPlan.TryRead(json,observation,out AnimalPlan plan),"Legal fixed response was rejected by current plan protocol");
        Call(director,"ApplyStoryPlan",animal,plan);
        LifeRequest current=speaker.Current;var emotion=(AnimalEmotionGlyph)Field(animal,"emotion");
        Check(current!=null && current==speaker.Story && current.glyphIds.SequenceEqual(fixture.speech.glyphs),"Actual LifeSpeaker.Current preserves fixed-response self/like/look/sea order");
        Check(current!=null && !current.HasTask && !current.requiredForEvent && life.Progress.tasks.Count==taskCount,
            "Story expression creates no teaching task or required event");
        Check(emotion.IsPlaying && (AnimalEmotion)Field(emotion,"emotion")==AnimalEmotion.Heart,"Same accepted preference intent starts the actual Heart effect");
        Check(JsonUtility.ToJson(life.Progress)==tasksBefore,"Speech publication does not fabricate progress or story events");
        Record("FIXTURE speech passed real TryRead+ApplyStoryPlan; no network response used; current="+JsonUtility.ToJson(current));Next(5);
    }
    static void Tick()
    {
        if(!SessionState.GetBool(Key,false) || SessionState.GetBool(Key+".finishing",false) || !EditorApplication.isPlaying || EditorApplication.isCompiling || Time.frameCount==lastFrame)return;
        lastFrame=Time.frameCount;
        try
        {
            if(Time.realtimeSinceStartup-wallStart>900)throw new TimeoutException("900s wall-clock deadline; "+State());
            if(!director)director=Object.FindObjectOfType<AnimalAutonomy>();
            if(director)
            {
                Set(director,"nextRequest",float.PositiveInfinity);client=director.GetComponent<AnimalPlannerClient>();
                if(client){Set(client,"config",null);Set(client,"nextConfigurationRead",float.PositiveInfinity);Require(client.Requests==0,"Unexpected actual network request");}
            }
            if(stage==0)
            {if(Time.time<3 || !director || director.AnimalCount==0)return;Initialize();return;}
            if(Time.time-lastSample>=.5f){lastSample=Time.time;Record("SAMPLE "+State());}
            if(stage==1 && Time.time-stageStart>=1)BeginEpisode();
            else if(stage==2)ObserveEpisode();
            else if(stage==3)
            {
                // Let production expression cooldowns elapse naturally; never reset them for the fixture.
                if(Time.time-stageStart>150)throw new TimeoutException("Real Heart expression cooldown/sharing opportunity unavailable within 150s; "+State());
                if(ExpressionReady() && (bool)Property(animal,"Available") && !(bool)Property(animal,"Sleeping"))
                {PlacePlayer(true);Next(4);}
            }
            else if(stage==4 && Time.time-stageStart>=1)ApplySpeech();
            else if(stage==5 && Time.time-stageStart>=.6f)
            {
                Capture("04-preference-heart-fixed-response");speaker.StopStory();
                Check(speaker.Story==null && speaker.Current?.id==previousTeaching,"Cancelling story restores the prior authored teaching selection");
                Check(Parameters()==parameters,"Animal personality, habitat and scale remain unchanged");
                Check(client.Requests==0,"No network requests; movement and event results are local execution evidence");
                Check(completed.Count==3 && completed.Select(e=>e.id).Distinct().Count()==3,"Three different real episode identities recorded");
                Finish();
            }
        }
        catch(Exception error)
        {Check(false,Unwrap(error)+"; "+State());try{if(started)Capture("failure-stage"+stage);}catch(Exception capture){Record("Capture error "+capture.Message);}Finish();}
    }
    static void Capture(string name)
    {
        Camera camera=life.Gameplay.CurrentView;Require(camera,"Actual gameplay camera missing");
        // Use only the actual player orbit's supported controls; never create a replacement evidence camera.
        var orbit=camera.GetComponent<VegetationTopDownCamera>();
        if(orbit)
        {
            Vector3 direction=root.position-player.position;float yaw=Mathf.Atan2(direction.x,direction.z)*Mathf.Rad2Deg;
            orbit.StepOrbit(Mathf.DeltaAngle(orbit.Yaw,yaw),46-orbit.pitch,orbit.distance-22);orbit.SnapToPlayer();
        }
        Vector3 viewport=camera.WorldToViewportPoint(root.position+Vector3.up*.6f);
        RenderTexture oldTarget=camera.targetTexture,oldActive=RenderTexture.active;
        var target=new RenderTexture(1600,900,24);var image=new Texture2D(1600,900,TextureFormat.RGB24,false);
        try
        {
            camera.targetTexture=target;camera.Render();RenderTexture.active=target;
            image.ReadPixels(new Rect(0,0,1600,900),0,0);image.Apply();File.WriteAllBytes(Path.Combine(Folder,name+".png"),image.EncodeToPNG());
            Record("CAPTURE "+name+"; actual gameplay camera="+PathOf(camera.transform)+";fixture framed through existing orbit controls="+(orbit!=null)+";viewport="+viewport.ToString("F3")+";pose="+camera.transform.position.ToString("F3"));
        }
        finally{camera.targetTexture=oldTarget;RenderTexture.active=oldActive;target.Release();Object.Destroy(target);Object.Destroy(image);}
    }
    static void Finish()
    {
        Record("RESULT failures="+failures+"; closedEpisodes="+completed.Count+"; networkRequests="+(client?client.Requests:0));
        SessionState.SetInt(Key+".exit",failures>0?1:0);SessionState.SetBool(Key+".finishing",true);EditorApplication.isPlaying=false;
    }
    static void VerifyAndExit()
    {
        int exit=SessionState.GetInt(Key+".exit",1);var checks=new List<string>();
        foreach(string line in SessionState.GetString(Key+".saves","").Split('\n'))
        {
            int split=line.LastIndexOf('|');if(split<0)continue;
            bool same=Hash(line.Substring(0,split))==line.Substring(split+1);if(!same)exit=1;
            checks.Add((same?"PASS ":"FAIL ")+"Real save unchanged: "+Path.GetFileName(line.Substring(0,split)));
        }
        bool scene=Hash(ScenePath)==SessionState.GetString(Key+".sceneHash","");if(!scene)exit=1;
        checks.Add((scene?"PASS ":"FAIL ")+"Saved formal scene unchanged; Play=False");
        File.WriteAllLines(Path.Combine(Folder,"save-protection.txt"),checks);
        var context=JsonUtility.FromJson<ProcessEvidence>(File.ReadAllText(Path.Combine(Folder,"run-context.json")));
        context.finishedUtc=DateTime.UtcNow.ToString("O");context.exitCode=exit;context.playEnded=!EditorApplication.isPlaying;
        File.WriteAllText(Path.Combine(Folder,"process-result.json"),JsonUtility.ToJson(context,true));
        SessionState.SetBool(Key,false);EditorApplication.Exit(exit);
    }
}
