// A new Unity process reopens a copy of a completed Play save through the actual formal-scene LifeSession.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using WeatherHouse.Hillside;
using WeatherHouse.Life;
using WeatherHouse.WatersideV2;
using WeatherHouse.WildlifeJourney;
using WeatherHousePrototype;
using Object = UnityEngine.Object;

[InitializeOnLoad]
public static class AnimalStoryReloadReview
{
    const string Key="WeatherHouse.AnimalStoryReloadReview", ScenePath="Assets/Scenes/WeatherHouse_WatersideV2.unity";
    const string SourceRoot="C:/unity project/Weather House", AnimalId="life.harbor.sheep";
    static readonly BindingFlags Flags=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
    static int lastFrame=-1;
    static float began;
    static bool inspected;
    static bool liveArmed,liveRequested,sawRequest,requestBodySaved;
    static float liveStarted,lastLiveSample;
    static int appliedBefore,rejectedBefore;
    static Vector3 quietPlayerPosition;
    static string liveStage="not started";
    static string Folder=>SessionState.GetString(Key+".folder","");
    [Serializable] sealed class ProcessEvidence
    {public int processId,exitCode;public string processStartedUtc,finishedUtc,reportPath;public bool playEnded;}
    [Serializable] sealed class ReloadContext
    {public int processId,previousProcessId;public string processStartedUtc,previousFinishedUtc,sourceDirectory,copiedDirectory,evidenceBoundary;}
    static object Field(object value,string name)=>value?.GetType().GetField(name,Flags)?.GetValue(value);
    static void Set(object value,string name,object content)=>value.GetType().GetField(name,Flags).SetValue(value,content);
    static void Require(bool ok,string message){if(!ok)throw new InvalidOperationException(message);}
    static void Check(bool ok,string message)
    {
        if(!ok)SessionState.SetInt(Key+".failures",SessionState.GetInt(Key+".failures",0)+1);
        File.AppendAllText(Path.Combine(Folder,"reload-checks.txt"),(ok?"PASS ":"FAIL ")+message+"\n");
    }
    static string Hash(string path)
    {if(!File.Exists(path))return "MISSING";using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-","");}
    static string Serialize(AnimalStoryState state)=>JsonUtility.ToJson(state);
    static AnimalStoryState Card(LifeProgress progress)=>progress?.animalStories?.SingleOrDefault(card=>card!=null && card.animalId==AnimalId);
    static AnimalStoryState ClearEmptyActive(AnimalStoryState card)
    {
        // Match only Unity's null-object placeholder; keep every real pending episode for the reload assertion.
        if(card?.active!=null && string.IsNullOrEmpty(card.active.id))card.active=null;
        return card;
    }

    static AnimalStoryReloadReview()
    {
        EditorApplication.update+=Tick;
        EditorApplication.playModeStateChanged+=state=>{
            if(!SessionState.GetBool(Key,false))return;
            if(state==PlayModeStateChange.EnteredPlayMode){began=Time.realtimeSinceStartup;Application.runInBackground=true;SessionState.SetBool(Key+".entered",true);}
            if(state==PlayModeStateChange.EnteredEditMode && SessionState.GetBool(Key+".finishing",false))VerifyAndExit();
        };
    }
    public static void Batch()
    {
        Require(Application.isBatchMode && Application.dataPath.Contains("ValidationProject") && !EditorApplication.isPlaying,
            "Use a fresh independent ValidationProject batch process");
        string input=Environment.GetEnvironmentVariable("WEATHERHOUSE_STORY_RELOAD_INPUT");
        string folder=Environment.GetEnvironmentVariable("WEATHERHOUSE_STORY_RELOAD_REPORT");
        Require(!string.IsNullOrWhiteSpace(input) && !string.IsNullOrWhiteSpace(folder),"Set reload INPUT isolated-save directory and REPORT empty directory");
        input=Path.GetFullPath(input);folder=Path.GetFullPath(folder);
        Require(!Directory.Exists(folder) || !Directory.EnumerateFileSystemEntries(folder).Any(),"Reload report must be an empty directory");
        Require(!string.Equals(input,folder,StringComparison.OrdinalIgnoreCase),"Source evidence and reload report must differ");
        Directory.CreateDirectory(folder);SessionState.SetString(Key+".folder",folder);SessionState.SetBool(Key,true);SessionState.SetBool(Key+".finishing",false);SessionState.SetBool(Key+".entered",false);
        SessionState.SetInt(Key+".failures",0);
        SessionState.SetBool(Key+".live",Environment.GetEnvironmentVariable("WEATHERHOUSE_STORY_RELOAD_LIVE_NETWORK")=="1");
        var protectedFiles=new List<string>();
        void Protect(string path){path=Path.GetFullPath(path);protectedFiles.Add(path+"|"+Hash(path));SessionState.SetString(Key+".protected",string.Join("\n",protectedFiles));}
        try
        {
            Protect(ScenePath);Protect(Path.Combine(SourceRoot,ScenePath));
            Protect(Path.Combine(SourceRoot,"UserSettings/WeatherHouseAnimalsAI.json"));Protect(AnimalPlannerClient.ConfigurationPath);
            foreach(string name in new[]{"language-notebook.json","life-progress.json","onboarding.json"})
                Protect(Path.Combine(Application.persistentDataPath,name));
            string progressPath=Path.Combine(input,"life-progress.json");
            Require(File.Exists(progressPath),"Prior actual isolated life-progress.json is missing");
            string previousResult=Path.Combine(Directory.GetParent(input).FullName,"process-result.json");Protect(previousResult);
            Require(File.Exists(previousResult),"Prior completed Play process metadata is required; JSON roundtrip alone is not a reopen test");
            var prior=JsonUtility.FromJson<ProcessEvidence>(File.ReadAllText(previousResult));
            Require(prior!=null && prior.playEnded && prior.exitCode==0 && prior.processId>0,"Prior Play run must have ended successfully");
            DateTime oldStarted=DateTime.Parse(prior.processStartedUtc,null,System.Globalization.DateTimeStyles.RoundtripKind).ToUniversalTime();
            DateTime oldFinished=DateTime.Parse(prior.finishedUtc,null,System.Globalization.DateTimeStyles.RoundtripKind).ToUniversalTime();
            using(var process=System.Diagnostics.Process.GetCurrentProcess())
            {
                DateTime newStarted=process.StartTime.ToUniversalTime();
                Require(newStarted>oldFinished && newStarted>File.GetLastWriteTimeUtc(progressPath),"New Unity process must start after prior Play and save writes");
                bool oldStillRunning=false;
                try{using(var old=System.Diagnostics.Process.GetProcessById(prior.processId))oldStillRunning=old.StartTime.ToUniversalTime()==oldStarted && !old.HasExited;}
                catch(ArgumentException){ }
                Require(!oldStillRunning,"Previous Unity process must actually exit before reopen verification");
                File.WriteAllText(Path.Combine(folder,"reload-context.json"),JsonUtility.ToJson(new ReloadContext{
                    processId=process.Id,previousProcessId=prior.processId,processStartedUtc=newStarted.ToString("O"),previousFinishedUtc=prior.finishedUtc,
                    sourceDirectory=input,copiedDirectory=Path.Combine(folder,"isolated-save"),
                    evidenceBoundary="New Unity process, actual formal scene and normal LifeSession.Start load of a copied prior Play save. Source evidence and real user saves stay unchanged. No offline time, animal activity or preferences are injected."},true));
            }
            Check(true,"Previous Unity process exited; this new process started after completed Play and its actual save writes");

            var cloneConfig=JsonUtility.FromJson<AnimalPlannerClient.Configuration>(File.ReadAllText(AnimalPlannerClient.ConfigurationPath));
            Require(cloneConfig!=null && !cloneConfig.enabled,"Clone planner configuration must remain disabled before scene load");
            string copy=Path.Combine(folder,"isolated-save");Directory.CreateDirectory(copy);
            foreach(string name in new[]{"language-notebook.json","life-progress.json","onboarding.json"})
            {
                string source=Path.Combine(input,name);Protect(source);
                if(File.Exists(source)){File.Copy(source,Path.Combine(copy,name),false);Check(Hash(source)==Hash(Path.Combine(copy,name)),"Copied prior "+name+" byte-for-byte into a separate reload save directory");}
            }
            var sourceProgress=JsonUtility.FromJson<LifeProgress>(File.ReadAllText(progressPath));AnimalStoryState expected=ClearEmptyActive(Card(sourceProgress));
            Require(expected!=null && expected.events!=null && expected.events.Any(e=>e.activity=="watch_sea" && e.outcome=="completed"),"Persisted source card lacks the prior real completed watch evidence");
            File.WriteAllText(Path.Combine(folder,"expected-card.json"),JsonUtility.ToJson(expected,true));
            Environment.SetEnvironmentVariable("WEATHERHOUSE_LIFE_REVIEW_SAVE",copy);
            Environment.SetEnvironmentVariable("WEATHERHOUSE_ONBOARDING_REVIEW",null);
            EditorSceneManager.OpenScene(ScenePath);EditorApplication.isPlaying=true;
        }
        catch(Exception error){Check(false,"Reopen setup: "+error.GetType().Name+": "+error.Message);SessionState.SetInt(Key+".exit",1);VerifyAndExit();}
    }
    static void Tick()
    {
        if(!SessionState.GetBool(Key,false) || !SessionState.GetBool(Key+".entered",false) || SessionState.GetBool(Key+".finishing",false) || !EditorApplication.isPlaying || EditorApplication.isCompiling || lastFrame==Time.frameCount)return;
        lastFrame=Time.frameCount;
        try
        {
            if(!liveArmed && Time.realtimeSinceStartup-began>60)throw new TimeoutException("Normal scene initialization/load did not become inspectable within 60s");
            var director=Object.FindObjectOfType<AnimalAutonomy>();
            if(!director)return;
            var client=director.GetComponent<AnimalPlannerClient>();
            if(!liveArmed)
            {
                Set(director,"nextRequest",float.PositiveInfinity);
                if(client){Set(client,"config",null);Set(client,"nextConfigurationRead",float.PositiveInfinity);Require(client.Requests==0,"Reload unexpectedly issued a network request");}
            }
            var life=Object.FindObjectOfType<LifeSession>();
            if(!life || !life.World || !life.Gameplay || director.AnimalCount==0)return;
            object animal=((IList)Field(director,"animals")).Cast<object>().SingleOrDefault(a=>(string)Field(a,"id")==AnimalId);
            if(animal==null)return;
            if(liveArmed){ObserveLive(director,client,life,animal);return;}
            if(inspected)return;inspected=true;
            var expected=ClearEmptyActive(JsonUtility.FromJson<AnimalStoryState>(File.ReadAllText(Path.Combine(Folder,"expected-card.json"))));
            AnimalStoryState loaded=Card(life.Progress),bound=(AnimalStoryState)Field(animal,"story");
            Require(loaded!=null && bound!=null,"Normal LifeSession did not restore and bind the persisted story");
            Check(Path.GetFullPath(life.SavePath)==Path.GetFullPath(Path.Combine(Folder,"isolated-save/life-progress.json")),"Actual LifeSession.SavePath uses the copied review save");
            Check(ReferenceEquals(loaded,bound),"Actual harbor sheep binds the LifeSession's restored card, not a recreated test card");
            Check(loaded.animalId==expected.animalId && loaded.species==expected.species && loaded.outgoing==expected.outgoing &&
                loaded.timidity==expected.timidity && loaded.sociability==expected.sociability && loaded.playerRelationship==expected.playerRelationship &&
                loaded.wish==expected.wish && loaded.nextEvent==expected.nextEvent,"Stable identity, personality, relationship, wish and next event identity survive actual reopen");
            Check(loaded.preferences.Count==expected.preferences.Count && expected.preferences.All(before=>loaded.preferences.Any(after=>after.id==before.id && after.value==before.value)),
                "Actual loaded preferences retain every persisted ID/value without seeding new defaults");
            int kept=Math.Min(expected.events.Count,AnimalStoryState.EventLimit-(expected.active!=null?1:0));
            var expectedEvents=expected.events.Skip(expected.events.Count-kept).ToArray();
            Check(expectedEvents.All(before=>loaded.events.Any(after=>JsonUtility.ToJson(after)==JsonUtility.ToJson(before))) &&
                loaded.events.Count==kept+(expected.active!=null?1:0),"Actual history preserves recorded event IDs, outcomes, dwell, companionship and reflection flags");
            if(expected.active==null)
                Check(loaded.active==null && loaded.events.Count(e=>e.outcome=="completed")==expected.events.Count(e=>e.outcome=="completed"),
                    "No pending activity or additional completed event appears merely because the application was closed");
            else
            {
                AnimalStoryEvent interrupted=loaded.FindEvent(expected.active.id);
                Check(loaded.active==null && interrupted!=null && interrupted.outcome=="interrupted" && interrupted.reason=="reload" &&
                    interrupted.dwellSeconds==expected.active.dwellSeconds && interrupted.companionSeconds==expected.active.companionSeconds &&
                    interrupted.playerParticipated==expected.active.playerParticipated && interrupted.playerLeftEarly==expected.active.playerLeftEarly,
                    "An actually persisted pending episode becomes one reload interruption with no offline dwell or false completion");
            }
            var observation=(AnimalObservation)typeof(AnimalAutonomy).GetMethod("Observe",Flags).Invoke(director,new[]{animal});
            Check(observation.character!=null && !ReferenceEquals(observation.character,loaded) && Serialize(observation.character)==Serialize(loaded),
                "Actual observation sent to the planner contains a faithful copy of the restored identity, preferences and real events");
            Check(observation.vocabulary?.Length==37 && observation.allowedWishes.Contains("watch_sea"),"Restored demo observation keeps formal vocabulary and its bounded activity capabilities");
            File.WriteAllText(Path.Combine(Folder,"loaded-card.json"),JsonUtility.ToJson(loaded,true));
            File.WriteAllText(Path.Combine(Folder,"loaded-observation.json"),JsonUtility.ToJson(observation,true));
            Check(string.IsNullOrEmpty(life.LastSaveError) && client.Requests==0,"Normal scene load/save reports no error and sends no network requests");
            if(SessionState.GetBool(Key+".live",false) && SessionState.GetInt(Key+".failures",0)==0)
                ArmLive(director,client,life,animal);
            else EndPlay();
        }
        catch(Exception error)
        {
            Check(false,liveRequested?"Live network stage "+liveStage+" failed ("+error.GetType().Name+"); no raw configuration/transport data logged":
                "Actual scene reload: "+error);
            EndPlay();
        }
    }
    static void ArmLive(AnimalAutonomy director,AnimalPlannerClient client,LifeSession life,object animal)
    {
        liveRequested=true;liveStage="arrange supported quiet player position";
        Require(client && !client.Busy && client.Requests==0,"Expected one unused production planner");
        Transform root=(Transform)Field(animal,"root"),player=life.Gameplay.PlayerTransform;
        Require(TryQuietPosition(life,root,out Vector3 destination),"No safe dry reachable 4.8m player position");
        CharacterController capsule=player.GetComponent<CharacterController>();bool enabled=capsule.enabled;
        capsule.enabled=false;player.position=destination;capsule.enabled=enabled;Physics.SyncTransforms();quietPlayerPosition=destination;
        if(life.Gameplay.forestSprite)life.Gameplay.forestSprite.SetScriptedInput(Vector2.zero,false,false);
        Require((bool)animal.GetType().GetProperty("Available").GetValue(animal),"Animal is not available for a live request");
        liveStage="validate source configuration only in memory";
        var config=AnimalStoryNetworkReview.ReadSourceConfiguration();
        Set(client,"config",config);Set(client,"nextConfigurationRead",float.PositiveInfinity);
        foreach(object other in (IList)Field(director,"animals"))Set(other,"nextAI",float.PositiveInfinity);
        appliedBefore=director.AppliedPlans;rejectedBefore=director.RejectedPlans;
        liveStarted=Time.realtimeSinceStartup;liveArmed=true;liveStage="wait for ordinary autonomy request";
        var snapshot=(AnimalObservation)typeof(AnimalAutonomy).GetMethod("Observe",Flags).Invoke(director,new[]{animal});
        File.WriteAllText(Path.Combine(Folder,"live-observation-at-arm.json"),JsonUtility.ToJson(snapshot,true));
        Set(animal,"nextAI",Time.time);Set(director,"nextRequest",Time.time);
        File.WriteAllText(Path.Combine(Folder,"live-network.txt"),"INFO Optional actual live integration: previous reload checks passed. Only request scheduling and one supported player position were arranged; no response, ApplyStoryPlan, action, personality or animal movement was injected. One request maximum, 20s deadline, no retry.\n"+
            "INFO player="+destination.ToString("F3")+" animal="+root.position.ToString("F3")+" distance="+Flat(destination,root.position).ToString("F3")+"\n");
    }
    static float Flat(Vector3 a,Vector3 b){a.y=b.y=0;return Vector3.Distance(a,b);}
    static bool TryQuietPosition(LifeSession life,Transform animal,out Vector3 result)
    {
        result=default;Terrain terrain=Terrain.activeTerrain;Transform player=life.Gameplay.PlayerTransform;
        CharacterController capsule=player.GetComponent<CharacterController>();var navigation=Object.FindObjectOfType<JourneyNavigation>();
        if(!terrain || !terrain.terrainData || !capsule || !navigation)return false;
        var water=player.GetComponent<WatersideWaterBoundary>();Vector3 offset=player.TransformVector(capsule.center),size=terrain.terrainData.size;
        float height=capsule.height*Mathf.Abs(player.lossyScale.y),radius=capsule.radius*Mathf.Max(Mathf.Abs(player.lossyScale.x),Mathf.Abs(player.lossyScale.z));
        float half=Mathf.Max(0,height*.5f-radius);
        for(int step=0;step<24;step++)
        {
            Vector3 candidate=animal.position+Quaternion.Euler(0,step*15,0)*Vector3.forward*4.8f,local=candidate-terrain.transform.position;
            if(local.x<0 || local.z<0 || local.x>size.x || local.z>size.z || terrain.terrainData.GetSteepness(local.x/size.x,local.z/size.z)>30)continue;
            float ground=terrain.SampleHeight(candidate)+terrain.transform.position.y;
            if(water && water.surface && water.terrain && water.SampleSupportedDepth(candidate,ground)>.02f || WatersideLayout.SeaSignedDistance(candidate.x,candidate.z)<.2f)continue;
            candidate.y=ground-offset.y+height*.5f+.08f;Vector3 center=candidate+offset;
            if(Mathf.Abs(candidate.y-animal.position.y)>1.5f || Physics.OverlapCapsule(center+Vector3.up*half,center-Vector3.up*half,radius*.95f,
                Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore).Any(c=>!(c is TerrainCollider) && !c.transform.IsChildOf(player)))continue;
            if(Physics.Linecast(animal.position+Vector3.up*.8f,candidate+Vector3.up,out RaycastHit hit,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore) &&
                !hit.transform.IsChildOf(animal) && !hit.transform.IsChildOf(player))continue;
            var profile=new JourneyProfile{bodyRadius=radius,canSwim=false,wadeDepth=0,walkSpeed=1};
            JourneyRoute route=navigation.TraceDirectRoute(animal.position,new Vector3(candidate.x,ground,candidate.z),profile);
            if(route==null || route.segments.Any(segment=>segment.mode!=JourneyTravelMode.Walk))continue;
            result=candidate;return true;
        }
        return false;
    }
    static void ObserveLive(AnimalAutonomy director,AnimalPlannerClient client,LifeSession life,object animal)
    {
        Require(client.Requests<=1,"Unexpected second network request");
        if(client.Requests>0)Set(director,"nextRequest",float.PositiveInfinity);
        Require(Flat(life.Gameplay.PlayerTransform.position,quietPlayerPosition)<.15f,"Fixture player did not remain stationary");
        float elapsed=Time.realtimeSinceStartup-liveStarted;
        var gate=(AnimalPlanGate)Field(animal,"gate");var speaker=(LifeSpeaker)Field(animal,"speaker");
        string state="elapsed="+elapsed.ToString("F3")+" requests="+client.Requests+" accepted="+client.Accepted+" busy="+client.Busy+
            " appliedDelta="+(director.AppliedPlans-appliedBefore)+" rejectedDelta="+(director.RejectedPlans-rejectedBefore)+
            " gatePending="+gate.Pending+" revision="+gate.Revision+" action="+Field(animal,"action")+
            " queued="+((ICollection)Field(animal,"plan")).Count+" story="+(speaker.Story?.id??"none")+" status="+client.LastStatus;
        if(Time.realtimeSinceStartup-lastLiveSample>=.25f)
        {lastLiveSample=Time.realtimeSinceStartup;File.AppendAllText(Path.Combine(Folder,"live-network.txt"),state+"\n");}
        if(client.Requests==1 && !sawRequest)
        {
            sawRequest=true;liveStage="await production gate callback";
            Check(gate.Pending || !client.Busy && client.Accepted==1 && director.RejectedPlans==rejectedBefore,
                "Normal Tick opened the actual demo gate: pending observed or callback already validated before this sample");
        }
        if(client.Busy && !requestBodySaved)
        {
            var request=Field(client,"active") as UnityEngine.Networking.UnityWebRequest;
            if(request?.uploadHandler?.data!=null)
            {
                string payload=Encoding.UTF8.GetString(request.uploadHandler.data);
                var config=(AnimalPlannerClient.Configuration)Field(client,"config");
                Require(config!=null && !payload.Contains(config.apiKey.Trim()),"Credential must never enter logged request content");
                File.WriteAllText(Path.Combine(Folder,"live-request.json"),payload);requestBodySaved=true;
            }
        }
        if(sawRequest && !client.Busy)
        {
            File.AppendAllText(Path.Combine(Folder,"live-network.txt"),"FINAL "+state+"\n");
            Check(elapsed<20,"Live request and production callback completed within the 20s deadline; elapsed="+elapsed.ToString("F3"));
            Check(client.Requests==1 && client.Accepted==1,"Exactly one actual network request returned a production-validated plan");
            Check(!gate.Pending && director.RejectedPlans==rejectedBefore && client.Accepted==1,
                "Actual autonomy gate accepted the context and ran its normal apply callback; "+state);
            File.WriteAllText(Path.Combine(Folder,"live-card-after.json"),JsonUtility.ToJson((AnimalStoryState)Field(animal,"story"),true));
            LifeRequest current=speaker.Story??speaker.Current;
            File.WriteAllText(Path.Combine(Folder,"live-speaker-after.json"),current==null?"null":JsonUtility.ToJson(current,true));
            File.AppendAllText(Path.Combine(Folder,"live-network.txt"),speaker.Story!=null?
                "INFO Actual LifeSpeaker.Story was published by the network callback. Its content is captured; no fixed response was injected.\n":
                "INFO No live Story bubble was observed. Silence/local expression suppression is allowed; this does not assert which one the model chose.\n");
            File.AppendAllText(Path.Combine(Folder,"live-network.txt"),requestBodySaved?
                "INFO Captured actual production request body without credentials/headers.\n":
                "INFO Request finished before a body sample; at-arm observation is labeled separately and is not claimed to be the exact wire payload.\n");
            EndPlay();return;
        }
        if(elapsed>=20)
        {
            File.AppendAllText(Path.Combine(Folder,"live-network.txt"),"FINAL timeout "+state+"\n");
            Check(false,"Live integration reached its 20s deadline without a completed valid callback; "+state);EndPlay();
        }
    }
    static void EndPlay()
    {
        var director=Object.FindObjectOfType<AnimalAutonomy>();
        if(director)
        {
            Set(director,"nextRequest",float.PositiveInfinity);var client=director.GetComponent<AnimalPlannerClient>();
            if(client)
            {
                (Field(client,"active") as UnityEngine.Networking.UnityWebRequest)?.Abort();
                Set(client,"config",null);Set(client,"nextConfigurationRead",float.PositiveInfinity);
            }
        }
        SessionState.SetInt(Key+".exit",SessionState.GetInt(Key+".failures",0)==0?0:1);
        SessionState.SetBool(Key+".finishing",true);EditorApplication.isPlaying=false;
    }
    static void VerifyAndExit()
    {
        int exit=SessionState.GetInt(Key+".exit",1);var results=new List<string>();
        foreach(string line in SessionState.GetString(Key+".protected","").Split('\n'))
        {
            int split=line.LastIndexOf('|');if(split<0)continue;
            string path=line.Substring(0,split),before=line.Substring(split+1),after=Hash(path);bool same=before==after;if(!same)exit=1;
            results.Add((same?"PASS ":"FAIL ")+"Protected file unchanged: "+path+"; beforeSHA256="+before+"; afterSHA256="+after);
        }
        results.Add((!EditorApplication.isPlaying?"PASS ":"FAIL ")+"Actual reload Play session ended; source evidence and real saves were not used as writable paths");
        File.WriteAllLines(Path.Combine(Folder,"save-protection.txt"),results);
        File.WriteAllText(Path.Combine(Folder,"result.txt"),"RESULT exit="+exit+"; actual new-process scene reload, not a standalone JSON roundtrip\n");
        SessionState.SetBool(Key,false);EditorApplication.Exit(exit);
    }
}
