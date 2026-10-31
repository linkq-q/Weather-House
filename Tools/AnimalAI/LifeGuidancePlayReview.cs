// Stage only in the independent ValidationProject. Actual listening/locomotion, never animal relocation.
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
using WeatherHouse.Vegetation;
using WeatherHouse.WatersideV2;
using WeatherHouse.WildlifeJourney;
using WeatherHousePrototype;
using Object=UnityEngine.Object;

[InitializeOnLoad]
public static class LifeGuidancePlayReview
{
    const string Key="WeatherHouse.LifeGuidancePlayReview",ScenePath="Assets/Scenes/WeatherHouse_WatersideV2.unity";
    static readonly BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;
    static LifeSession life;
    static GameplaySession game;
    static LifeWorld world;
    static LifeSpeaker speaker;
    static WildlifeAgent agent;
    static LifeRequest request;
    static Transform player;
    static int stage,lastFrame=-1;
    static float began,phaseAt,listenAt,waitAt=-1,releasedAt,lastSample,pathLength,postReleaseDistance;
    static bool sawWalk,sawWait,sawStoppedWait;
    static float bestWaitFacing=-1;
    static Vector3 playerSite,original,lastPosition,releasePosition,target;
    static string parameters;
    static string Folder=>SessionState.GetString(Key+".folder","");
    static object Field(object value,string name)=>value?.GetType().GetField(name,Flags)?.GetValue(value);
    static void Set(object value,string name,object content)=>value.GetType().GetField(name,Flags).SetValue(value,content);
    static object Guide=>Field(world,"guidance");
    static float Flat(Vector3 a,Vector3 b){a.y=b.y=0;return Vector3.Distance(a,b);}
    static void Require(bool ok,string message){if(!ok)throw new InvalidOperationException(message);}
    static void Record(string value)=>File.AppendAllText(Path.Combine(Folder,"observations.txt"),"t="+Time.time.ToString("F3")+" frame="+Time.frameCount+" "+value+"\n");
    static void Check(bool ok,string message)
    {if(!ok)SessionState.SetInt(Key+".failures",SessionState.GetInt(Key+".failures",0)+1);Record((ok?"PASS ":"FAIL ")+message);}
    static string Hash(string path)
    {if(!File.Exists(path))return "MISSING";using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-","");}
    static string Parameters()=>"timidity="+agent.timidity+";sociability="+agent.sociability+";scale="+agent.transform.localScale.ToString("R")+
        ";habitat="+agent.habitatCenter.ToString("R")+";radius="+agent.habitatRadius;

    static LifeGuidancePlayReview()
    {
        EditorApplication.update+=Tick;
        EditorApplication.playModeStateChanged+=state=>{
            if(!SessionState.GetBool(Key,false))return;
            if(state==PlayModeStateChange.EnteredPlayMode)
            {began=Time.realtimeSinceStartup;Application.runInBackground=true;SessionState.SetBool(Key+".entered",true);}
            if(state==PlayModeStateChange.EnteredEditMode && SessionState.GetBool(Key+".finishing",false))VerifyAndExit();
        };
    }
    public static void Batch()
    {
        Require(Application.isBatchMode && Application.dataPath.Contains("ValidationProject") && !EditorApplication.isPlaying,"Independent ValidationProject batch only");
        string folder=Environment.GetEnvironmentVariable("WEATHERHOUSE_LIFE_GUIDANCE_REPORT");
        Require(!string.IsNullOrWhiteSpace(folder),"Set WEATHERHOUSE_LIFE_GUIDANCE_REPORT to an empty evidence folder");folder=Path.GetFullPath(folder);
        Require(!Directory.Exists(folder) || !Directory.EnumerateFileSystemEntries(folder).Any(),"Evidence folder must be empty; put Unity log outside it");
        Directory.CreateDirectory(folder);SessionState.SetString(Key+".folder",folder);SessionState.SetBool(Key,true);
        SessionState.SetBool(Key+".entered",false);SessionState.SetBool(Key+".finishing",false);SessionState.SetInt(Key+".failures",0);
        var protectedFiles=new List<string>();
        foreach(string path in new[]{Path.GetFullPath(ScenePath),Path.Combine("C:/unity project/Weather House",ScenePath),AnimalPlannerClient.ConfigurationPath,
            "C:/unity project/Weather House/UserSettings/WeatherHouseAnimalsAI.json"}.Concat(new[]{"language-notebook.json","life-progress.json","onboarding.json"}.Select(n=>Path.Combine(Application.persistentDataPath,n))))
            protectedFiles.Add(Path.GetFullPath(path)+"|"+Hash(path));
        SessionState.SetString(Key+".protected",string.Join("\n",protectedFiles));
        try
        {
            var config=JsonUtility.FromJson<AnimalPlannerClient.Configuration>(File.ReadAllText(AnimalPlannerClient.ConfigurationPath));
            Require(config!=null && !config.enabled,"Clone planner must remain disabled on disk");
            Environment.SetEnvironmentVariable("WEATHERHOUSE_LIFE_REVIEW_SAVE",Path.Combine(folder,"isolated-save"));
            Environment.SetEnvironmentVariable("WEATHERHOUSE_ONBOARDING_REVIEW",null);Directory.CreateDirectory(Path.Combine(folder,"isolated-save"));
            foreach(string name in new[]{"LifeQuietWorldChecks","LifeArrivalRegionChecks","LifeGuidanceChecks"})
            {
                Type type=AppDomain.CurrentDomain.GetAssemblies().SelectMany(Types).SingleOrDefault(t=>t.Name==name);
                Require(type!=null,"Missing staged contract "+name);
                var lines=(IEnumerable<string>)type.GetMethod("Run",BindingFlags.Public|BindingFlags.Static).Invoke(null,null);
                foreach(string line in lines){File.AppendAllText(Path.Combine(folder,"contracts.txt"),name+": "+line+"\n");Require(!line.StartsWith("FAIL"),"Contract failed: "+name);}
            }
            EditorSceneManager.OpenScene(ScenePath);EditorApplication.isPlaying=true;
        }
        catch(Exception error)
        {File.AppendAllText(Path.Combine(folder,"contracts.txt"),"FAIL "+Unwrap(error)+"\n");SessionState.SetInt(Key+".exit",1);VerifyAndExit();}
    }
    static IEnumerable<Type> Types(Assembly assembly)
    {try{return assembly.GetTypes();}catch(ReflectionTypeLoadException error){return error.Types.Where(t=>t!=null);}}
    static Exception Unwrap(Exception error)=>error is TargetInvocationException call && call.InnerException!=null?call.InnerException:error;
    static void SceneChecks()
    {
        Type type=AppDomain.CurrentDomain.GetAssemblies().SelectMany(Types).Single(t=>t.Name=="LifeQuietWorldChecks");
        foreach(string line in (IEnumerable<string>)type.GetMethod("CheckScene").Invoke(null,new object[]{life}))Record(line);
    }
    static string State()
    {
        if(!agent)return "initializing";object guide=Guide;var task=life.Progress.FindTask(request.id);
        return "actor="+speaker.Actor.animalId+";position="+agent.transform.position.ToString("F3")+";player="+player.position.ToString("F3")+
            ";distance="+Flat(agent.transform.position,player.position).ToString("F3")+";target="+target.ToString("F3")+
            ";speed="+agent.ActualSpeed.ToString("F3")+";state="+agent.State+";hasLease="+agent.HasLifeTarget+";autonomous="+agent.AutonomousTarget+
            ";revision="+agent.IntentRevision+";guide="+(guide!=null)+";waitingSince="+Field(guide,"waitingSince")+";step="+Field(guide,"step")+
            ";facingPlayer="+FacingPlayer().ToString("F3")+
            ";routeQueries="+(Field(guide,"search") as AnimalLocalRoute)?.Queries+";task="+task?.status+
            ";observed="+game.Observed?.animalId+";utterance="+speaker.Actor.UtteranceId+";glyphs="+string.Join(",",game.DisplayedGlyphIds(speaker.Actor))+
            ";path="+pathLength.ToString("F3");
    }
    static float FacingPlayer()
    {Vector3 direction=player.position-agent.transform.position;direction.y=0;return Vector3.Dot(agent.transform.forward,direction.normalized);}
    static void Initialize()
    {
        life=Object.FindObjectOfType<LifeSession>();game=life.Gameplay;world=life.World;player=game.PlayerTransform;
        SceneChecks();Require(!LifeOnboarding.Blocks(game),"Review onboarding overlay unexpectedly blocks interaction");
        speaker=world.FindSpeaker("harborSheep");Require(speaker && speaker.Actor,"Actual harbor sheep missing");
        agent=speaker.GetComponent<WildlifeAgent>();Require(agent && agent.isActiveAndEnabled,"Actual wildlife movement owner missing");
        request=speaker.requests.Single(r=>r.id=="sheep.go");Require(request.condition=="arrive" && request.targetId=="harbor.tide","Expected authored short arrival request");
        original=lastPosition=agent.transform.position;parameters=Parameters();target=world.AnchorPosition(request.targetId);
        Require(Flat(original,target)>4 && Flat(original,target)<16,"Existing guide route is not an appropriate short real route");
        Require(life.Progress.FindTask(request.id)==null,"Fresh review already contains the chosen task");
        // Unlock only the chosen authored topic; real Listen must create the Active record and guidance lease.
        if(!life.Progress.HasHeard(request.id))life.Progress.heardRequests.Add(request.id);
        speaker.topicIndex=Array.IndexOf(speaker.requests,request);
        Require(speaker.Current==request && life.Progress.CanPresent(request,world.Requests),"Authored request could not be selected");
        Require(TryPlayerSite(out Vector3 position),"No safe supported side-on player position within talking range");
        var capsule=player.GetComponent<CharacterController>();bool enabled=capsule.enabled;capsule.enabled=false;player.position=position;capsule.enabled=enabled;
        Physics.SyncTransforms();playerSite=position;if(game.forestSprite)game.forestSprite.SetScriptedInput(Vector2.zero,false,false);
        FrameCamera();
        Record("SETUP real formal scene; sheep.go heard-only fixture unlock and one safe 3.1m player placement. No inserted task/response, animal relocation, personality/speed change, direct BeginGuidance call or forced Observed target. Player stays still. Network and onboarding review overlay disabled. Screenshots are actual world-camera renders; overlay HUD absence is checked through runtime hierarchy, not inferred from those pixels.");
        Record("ORIGINAL "+State()+";parameters="+parameters);Capture("01-before-listen");stage=1;phaseAt=Time.time;
    }
    static bool TryPlayerSite(out Vector3 result)
    {
        result=default;var capsule=player.GetComponent<CharacterController>();var nav=Object.FindObjectOfType<JourneyNavigation>();Terrain terrain=Terrain.activeTerrain;
        if(!capsule || !nav || !terrain)return false;
        var water=player.GetComponent<WatersideWaterBoundary>();Vector3 size=terrain.terrainData.size,offset=player.TransformVector(capsule.center);
        float height=capsule.height*Mathf.Abs(player.lossyScale.y),radius=capsule.radius*Mathf.Max(Mathf.Abs(player.lossyScale.x),Mathf.Abs(player.lossyScale.z)),half=Mathf.Max(0,height*.5f-radius);
        Vector3 forward=target-agent.transform.position;forward.y=0;forward.Normalize();
        foreach(float angle in new[]{-90f,90f,-105f,105f,-120f,120f,180f,-150f,150f})
        {
            Vector3 candidate=agent.transform.position+Quaternion.Euler(0,angle,0)*forward*3.1f,local=candidate-terrain.transform.position;
            if(local.x<0 || local.z<0 || local.x>size.x || local.z>size.z || terrain.terrainData.GetSteepness(local.x/size.x,local.z/size.z)>30)continue;
            float ground=terrain.SampleHeight(candidate)+terrain.transform.position.y;
            if(water && water.surface && water.terrain && water.SampleSupportedDepth(candidate,ground)>.02f || WatersideLayout.SeaSignedDistance(candidate.x,candidate.z)<.2f)continue;
            candidate.y=ground-offset.y+height*.5f+.08f;Vector3 center=candidate+offset;
            if(Vector3.Distance(candidate,agent.transform.position)>3.45f || Flat(candidate,target)<5 ||
                Physics.OverlapCapsule(center+Vector3.up*half,center-Vector3.up*half,radius*.95f,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore)
                    .Any(c=>!(c is TerrainCollider) && !c.transform.IsChildOf(player)))continue;
            if(Physics.Linecast(agent.transform.position+Vector3.up*.8f,candidate+Vector3.up,out RaycastHit hit,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore) &&
                !hit.transform.IsChildOf(agent.transform) && !hit.transform.IsChildOf(player))continue;
            var profile=new JourneyProfile{bodyRadius=radius,canSwim=false,wadeDepth=0,walkSpeed=1};
            var route=nav.TraceDirectRoute(agent.transform.position,new Vector3(candidate.x,ground,candidate.z),profile);
            if(route==null || route.segments.Any(s=>s.mode!=JourneyTravelMode.Walk))continue;
            result=candidate;return true;
        }
        return false;
    }
    static void Tick()
    {
        if(!SessionState.GetBool(Key,false) || !SessionState.GetBool(Key+".entered",false) || SessionState.GetBool(Key+".finishing",false) ||
            !EditorApplication.isPlaying || EditorApplication.isCompiling || Time.frameCount==lastFrame)return;lastFrame=Time.frameCount;
        try
        {
            if(Time.realtimeSinceStartup-began>180)throw new TimeoutException("180s overall deadline; "+State());
            var director=Object.FindObjectOfType<AnimalAutonomy>();
            if(director)
            {Set(director,"nextRequest",float.PositiveInfinity);var client=director.GetComponent<AnimalPlannerClient>();if(client){Set(client,"config",null);Set(client,"nextConfigurationRead",float.PositiveInfinity);Require(client.Requests==0,"Unexpected network request");}}
            if(stage==0)
            {var session=Object.FindObjectOfType<LifeSession>();if(Time.time<2 || !session || !session.World || !session.Gameplay || !director || director.AnimalCount==0)return;Initialize();return;}
            Require(Flat(player.position,playerSite)<.15f,"Player must stay at the one arranged site, without following");
            if(Time.time-lastSample>.25f){lastSample=Time.time;Record("SAMPLE "+State());}
            if(stage==1)
            {
                FrameCamera();
                if(game.Observed==speaker.Actor && game.DisplayedGlyphIds(speaker.Actor).SequenceEqual(request.glyphIds) && life.CanTalk(speaker.Actor))
                {
                    Require(Guide==null,"Guidance began without listening");
                    Check(life.Listen(speaker.Actor,speaker.Actor.UtteranceId),"Actual current observed actor passes LifeSession.Listen guards");
                    Require(Guide!=null && life.Progress.FindTask(request.id)?.status==LifeTaskStatus.Active,"Real Listen did not create Active task and guidance");
                    listenAt=Time.time;lastPosition=agent.transform.position;Capture("02-listening-starts-guide");Record("EVENT listened; "+State());stage=2;
                }
                else if(Time.time-phaseAt>25)throw new TimeoutException("Actual camera observation/current utterance did not permit listening; "+State());
                return;
            }
            if(stage==2)
            {
                pathLength+=Flat(lastPosition,agent.transform.position);lastPosition=agent.transform.position;
                if(!sawWalk && agent.ActualSpeed>.05f && Flat(agent.transform.position,original)>.35f)
                {sawWalk=true;Record("EVENT real walking; "+State());Capture("03-actual-short-walk");}
                object guide=Guide;
                if(guide!=null)
                {
                    float waiting=(float)Field(guide,"waitingSince");
                    if(waiting>=0 && !sawWait)
                    {sawWait=true;waitAt=waiting;Check(Flat(agent.transform.position,target)>.4f,"Waiting is for the player behind, not early goal completion");Record("EVENT wait for absent follower; "+State());Capture("04-turn-and-wait");}
                    if(waiting>=0)
                    {sawStoppedWait|=agent.ActualSpeed<.05f;bestWaitFacing=Mathf.Max(bestWaitFacing,FacingPlayer());}
                    if(Time.time-listenAt>60)throw new TimeoutException("Guide did not complete/abandon its short request within 60s; "+State());
                    return;
                }
                Check(sawWalk && pathLength>.35f,"Actual animal walked a short route, path="+pathLength.ToString("F3"));
                Check(sawWait && Time.time-waitAt>=4 && Time.time-waitAt<6,"Guide waited approximately four actual seconds before releasing; duration="+(Time.time-waitAt).ToString("F3"));
                Check(sawStoppedWait && bestWaitFacing>.75f,"Animal actually stopped and turned back toward the waiting player; best facing="+bestWaitFacing.ToString("F3"));
                Check(!agent.HasLifeTarget || agent.AutonomousTarget,"Guidance no longer owns a manual movement lease");
                Check(life.Progress.FindTask(request.id)?.status==LifeTaskStatus.Active,"Unfollowed task stays Active without fabricated arrival");
                Record("EVENT released; "+State());Capture("05-released-with-active-record");releasedAt=Time.time;releasePosition=agent.transform.position;stage=3;return;
            }
            if(stage==3)
            {
                Require(Guide==null,"Guide automatically restarted without another listening interaction");
                Require(!agent.HasLifeTarget || agent.AutonomousTarget,"Old task reacquired a permanent manual hold after guide release");
                postReleaseDistance=Mathf.Max(postReleaseDistance,Flat(agent.transform.position,releasePosition));
                if(Time.time-releasedAt<12)return;
                Check(Guide==null,"No automatic repeat route during twelve seconds after release");
                Check(life.Progress.FindTask(request.id)?.status==LifeTaskStatus.Active,"Active task record remains after movement ownership was released");
                Check(Parameters()==parameters,"Animal personality, size and habitat parameters unchanged");
                Record("POST RELEASE max displacement="+postReleaseDistance.ToString("F3")+"; manual ownership absence proves released control, not a requirement that natural idle must move");
                SceneChecks();Capture("06-free-after-release");File.WriteAllText(Path.Combine(Folder,"progress-after.json"),JsonUtility.ToJson(life.Progress,true));Finish();
            }
        }
        catch(Exception error)
        {Check(false,Unwrap(error)+"; "+State());try{if(agent)Capture("failure-stage"+stage);}catch(Exception capture){Record("Capture error "+capture.Message);}Finish();}
    }
    static void FrameCamera()
    {
        Camera camera=game.CurrentView;Require(camera,"Actual game camera missing");var orbit=camera.GetComponent<VegetationTopDownCamera>();
        if(!orbit)return;Vector3 toward=agent.transform.position-player.position;float yaw=Mathf.Atan2(toward.x,toward.z)*Mathf.Rad2Deg;
        orbit.StepOrbit(Mathf.DeltaAngle(orbit.Yaw,yaw),46-orbit.pitch,orbit.distance-22);orbit.SnapToPlayer();
    }
    static void Capture(string name)
    {
        FrameCamera();Camera camera=game.CurrentView;var targetTexture=new RenderTexture(1600,900,24);var image=new Texture2D(1600,900,TextureFormat.RGB24,false);
        RenderTexture oldTarget=camera.targetTexture,oldActive=RenderTexture.active;
        try
        {camera.targetTexture=targetTexture;camera.Render();RenderTexture.active=targetTexture;image.ReadPixels(new Rect(0,0,1600,900),0,0);image.Apply();File.WriteAllBytes(Path.Combine(Folder,name+".png"),image.EncodeToPNG());Record("CAPTURE "+name+" actual world game-camera, overlay not captured; viewport="+camera.WorldToViewportPoint(agent.transform.position+Vector3.up*.6f).ToString("F3"));}
        finally{camera.targetTexture=oldTarget;RenderTexture.active=oldActive;targetTexture.Release();Object.Destroy(targetTexture);Object.Destroy(image);}
    }
    static void Finish()
    {int failures=SessionState.GetInt(Key+".failures",0);Record("RESULT failures="+failures+"; path="+pathLength.ToString("F3")+"; postReleaseDisplacement="+postReleaseDistance.ToString("F3"));SessionState.SetInt(Key+".exit",failures==0?0:1);SessionState.SetBool(Key+".finishing",true);EditorApplication.isPlaying=false;}
    static void VerifyAndExit()
    {
        int exit=SessionState.GetInt(Key+".exit",1);var results=new List<string>();
        foreach(string line in SessionState.GetString(Key+".protected","").Split('\n'))
        {int split=line.LastIndexOf('|');if(split<0)continue;string path=line.Substring(0,split);bool same=Hash(path)==line.Substring(split+1);if(!same)exit=1;results.Add((same?"PASS ":"FAIL ")+"Protected file unchanged: "+path);}
        results.Add((!EditorApplication.isPlaying?"PASS ":"FAIL ")+"Play ended; no scene save requested");File.WriteAllLines(Path.Combine(Folder,"save-protection.txt"),results);
        File.WriteAllText(Path.Combine(Folder,"result.txt"),"RESULT exit="+exit+"\n");SessionState.SetBool(Key,false);EditorApplication.Exit(exit);
    }
}
