// Real saved-map Play fixture. Only the player is arranged; animal movement and shelter choices are production code.
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
using WeatherHousePrototype;
using Object = UnityEngine.Object;

[InitializeOnLoad]
public static class RainShelterHoldReview
{
    const string Key="WeatherHouse.RainShelterHoldReview", ScenePath="Assets/Scenes/WeatherHouse_WatersideV2.unity";
    const BindingFlags Flags=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
    static readonly List<string> lines=new List<string>();
    static AnimalAutonomy director;
    static LifeSession life;
    static LifeLocalWeather weather;
    static WeatherRegion sky;
    static WildlifeAgent agent;
    static Transform player;
    static object subject;
    static Vector3 start,last,held,playerSite,recoveryStart;
    static float stageAt,wallAt,path,lastLog,settledAt=-1,maxDrift,recoveryPathStart=-1,recoveryMaximumSpeed,maxSampledBodyOverlap,clearAt=-1;
    static int stage,lastFrame=-1,failures;
    static bool outside,moving;
    static bool closeLocked;
    static Vector3 closePosition,closeLook;
    static string parameters;
    static string Folder=>SessionState.GetString(Key+".folder","");
    static object Field(object value,string name)=>value?.GetType().GetField(name,Flags)?.GetValue(value);
    static object Property(object value,string name)=>value?.GetType().GetProperty(name,Flags)?.GetValue(value);
    static float Flat(Vector3 a,Vector3 b){a.y=b.y=0;return Vector3.Distance(a,b);}
    static string Action=>(string)Field(subject,"action");
    static Vector3 Goal=>(Vector3)Field(subject,"goal");
    static void Require(bool ok,string message){if(!ok)throw new InvalidOperationException(message);}
    static void Log(string value){lines.Add("t="+Time.time.ToString("F3")+" frame="+Time.frameCount+" "+value);File.WriteAllLines(Path.Combine(Folder,"observations.txt"),lines);}
    static string Hash(string path){if(!File.Exists(path))return "MISSING";using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-","");}
    static void Next(int value){stage=value;stageAt=Time.time;}
    static string Parameters()=>"scale="+agent.transform.localScale+" timidity="+agent.timidity+" sociability="+agent.sociability+" habitat="+agent.habitatCenter+" radius="+agent.habitatRadius;
    static string State()=>"stage="+stage+" action="+Action+" position="+agent.transform.position.ToString("F3")+" goal="+Goal.ToString("F3")+
        " speed="+agent.ActualSpeed.ToString("F3")+" lease="+agent.HasLifeTarget+" autonomous="+agent.AutonomousTarget+
        " localWet="+sky.Contains(agent.transform.position)+" originWet="+sky.Contains(start)+" strength="+sky.SoundStrength+
        " active="+sky.SoundActive+" reaction="+Property(Field(subject,"rainReaction"),"NeedsShelter")+
        " path="+path.ToString("F3")+" player="+player.position.ToString("F3");
    static RainShelterHoldReview()
    {
        EditorApplication.playModeStateChanged+=state=>{
            if(!SessionState.GetBool(Key,false))return;
            if(state==PlayModeStateChange.EnteredPlayMode){wallAt=Time.realtimeSinceStartup;Application.runInBackground=true;stage=0;}
            if(state==PlayModeStateChange.EnteredEditMode && SessionState.GetBool(Key+".finishing",false))VerifyAndExit();
        };
        EditorApplication.update+=Tick;
    }
    public static void Batch()
    {
        Require(Application.isBatchMode && Application.dataPath.Contains("ValidationProject"),"Independent validation project only");
        string folder=Environment.GetEnvironmentVariable("WEATHERHOUSE_SHELTER_HOLD_REPORT");
        Require(!string.IsNullOrWhiteSpace(folder) && !Directory.Exists(folder),"Use a fresh evidence folder");
        Directory.CreateDirectory(folder);SessionState.SetString(Key+".folder",folder);
        SessionState.SetString(Key+".scene",Hash(ScenePath));
        SessionState.SetString(Key+".saves",string.Join("\n",new[]{"language-notebook.json","life-progress.json","onboarding.json"}
            .Select(name=>Path.Combine(Application.persistentDataPath,name)).Select(path=>path+"|"+Hash(path))));
        Environment.SetEnvironmentVariable("WEATHERHOUSE_LIFE_REVIEW_SAVE",Path.Combine(folder,"isolated-save"));
        Environment.SetEnvironmentVariable("WEATHERHOUSE_ONBOARDING_REVIEW",null);
        Directory.CreateDirectory(Path.Combine(folder,"isolated-save"));
        SessionState.SetBool(Key,true);SessionState.SetBool(Key+".finishing",false);
        try
        {
            var config=JsonUtility.FromJson<AnimalPlannerClient.Configuration>(File.ReadAllText(AnimalPlannerClient.ConfigurationPath));
            Require(config!=null && !config.enabled,"Validation network must be disabled");
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            var contracts=new List<string>();
            try
            {
                foreach(string line in ShelterPersistenceChecks.Run()){contracts.Add(line);File.WriteAllLines(Path.Combine(folder,"contracts.txt"),contracts);}
                foreach(string line in RainHerdLeaseChecks.Run()){contracts.Add(line);File.WriteAllLines(Path.Combine(folder,"contracts.txt"),contracts);}
                contracts.Add("RESULT failures=0");
            }
            catch(Exception error){contracts.Add("FAIL "+error);throw;}
            finally{File.WriteAllLines(Path.Combine(folder,"contracts.txt"),contracts);}
            EditorSceneManager.OpenScene(ScenePath);EditorApplication.isPlaying=true;
        }
        catch(Exception error){Log("FAIL "+error);failures++;Finish();}
    }
    static bool PlacePlayer()
    {
        var terrain=Terrain.activeTerrain;var capsule=player.GetComponent<CharacterController>();
        if(!terrain || !capsule)return false;
        var water=player.GetComponent<WatersideWaterBoundary>();
        Vector3 offset=player.TransformVector(capsule.center),size=terrain.terrainData.size;
        float height=capsule.height*Mathf.Abs(player.lossyScale.y),radius=capsule.radius*Mathf.Max(Mathf.Abs(player.lossyScale.x),Mathf.Abs(player.lossyScale.z));
        float half=Mathf.Max(0,height*.5f-radius);
        for(int i=0;i<24;i++)
        {
            Vector3 candidate=agent.transform.position+Quaternion.Euler(0,i*15,0)*Vector3.forward*6.5f;
            Vector3 local=candidate-terrain.transform.position;
            if(local.x<0 || local.z<0 || local.x>size.x || local.z>size.z || terrain.terrainData.GetSteepness(local.x/size.x,local.z/size.z)>30)continue;
            float ground=terrain.SampleHeight(candidate)+terrain.transform.position.y;
            if(water && water.surface && water.terrain && water.SampleSupportedDepth(candidate,ground)>.02f || WatersideLayout.SeaSignedDistance(candidate.x,candidate.z)<.2f)continue;
            candidate.y=ground-offset.y+height*.5f+.08f;Vector3 center=candidate+offset;
            if(Mathf.Abs(candidate.y-agent.transform.position.y)>1.5f || Physics.OverlapCapsule(center+Vector3.up*half,center-Vector3.up*half,radius*.95f,
                Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore).Any(c=>!(c is TerrainCollider) && !c.transform.IsChildOf(player)))continue;
            bool enabled=capsule.enabled;capsule.enabled=false;player.position=candidate;capsule.enabled=enabled;Physics.SyncTransforms();playerSite=candidate;
            if(life.Gameplay.forestSprite)life.Gameplay.forestSprite.SetScriptedInput(Vector2.zero,false,false);
            return true;
        }
        return false;
    }
    static string ShelterSource()
    {
        foreach(object shelter in (IEnumerable)Field(director,"shelters"))
        {
            var source=Field(shelter,"source") as Transform;
            if(source && source.gameObject.activeInHierarchy && Flat(Goal,(Vector3)Property(shelter,"Point"))<.25f)return source.name;
        }
        foreach(LifeObject item in life.World.Objects)
            if(item && item.State.sheltered && !item.State.held && Flat(Goal,item.transform.position)<.25f)return item.name;
        float height=Mathf.Max(.7f,agent.BodyEnvelope.half.y*2+.15f);
        foreach(RaycastHit hit in Physics.RaycastAll(Goal+Vector3.up*height,Vector3.up,8,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore))
            if(!(hit.collider is TerrainCollider) && !hit.collider.transform.IsChildOf(agent.transform) &&
                !hit.collider.GetComponentInParent<WildlifeAgent>() && Mathf.Abs(hit.normal.y)>=.5f)return hit.collider.name;
        return null;
    }
    static void Tick()
    {
        if(!SessionState.GetBool(Key,false) || SessionState.GetBool(Key+".finishing",false) || !EditorApplication.isPlaying || Time.frameCount==lastFrame)return;
        lastFrame=Time.frameCount;
        try
        {
            Require(Time.realtimeSinceStartup-wallAt<650,"650 second wall-clock timeout");
            if(stage==0)
            {
                if(Time.time<5)return;
                director=Object.FindObjectOfType<AnimalAutonomy>();if(!director || director.AnimalCount==0)return;
                life=Object.FindObjectOfType<LifeSession>();player=life.Gameplay.PlayerTransform;
                weather=(LifeLocalWeather)Field(life,"weather");sky=(WeatherRegion)Field(weather,"sky");
                subject=((IList)Field(director,"animals")).Cast<object>().SingleOrDefault(a=>Field(a,"agent") is WildlifeAgent w && w && w.spawnId==6101 && w.speciesId=="sheep");
                Require(subject!=null,"Original harbor sheep missing");agent=(WildlifeAgent)Field(subject,"agent");
                Require((bool)Property(subject,"Available"),"Original sheep unavailable");
                Require(Mathf.Approximately(sky.radius,8),"Use normal 8m weather radius");
                Require(PlacePlayer(),"No supported, dry capsule-clear test player site");
                parameters=Parameters();start=last=agent.transform.position;
                Require(weather.Cast("rain",out _) && weather.Cast("strong",out _),"Strong rain cast failed");weather.Apply(life.World,.000001f);
                Require(sky.SoundActive && sky.SoundStrength==2 && sky.Contains(start),"Original animal not exposed to the normal strong rain circle");
                Log("ARRANGE saved formal map; original sheep, normal 8m strong rain; one-time player placement at6.5m; no animal transform/parameter writes; network disabled");
                Capture("00-strong-rain-start");Next(1);return;
            }
            Require(Flat(player.position,playerSite)<.2f,"Fixture player moved unexpectedly");
            float step=Flat(agent.transform.position,last);Require(step<1,"Animal unexpectedly jumped more than1m in one frame");path+=step;last=agent.transform.position;
            if(!sky.Contains(agent.transform.position))outside=true;
            if(!moving && Flat(agent.transform.position,start)>.3f){moving=true;Log("EVENT actual shelter locomotion; "+State());Capture("01-moving-to-shelter");}
            if(Time.time-lastLog>=1){lastLog=Time.time;Log("SAMPLE "+State());}
            float age=Time.time-stageAt;
            if(stage==1)
            {
                Require(age<180,"No actual arrival within180s; "+State());
                bool reached=Action=="shelter" && !(bool)Field(subject,"shelterRetry") && Field(subject,"shelterQuery")==null &&
                    Flat(agent.transform.position,Goal)<.3f && agent.ActualSpeed<.05f && ShelterSource()!=null;
                if(!reached)settledAt=-1;else if(settledAt<0)settledAt=Time.time;
                if(settledAt>=0 && Time.time-settledAt>=1)
                {
                    Require(moving && path>1,"Arrival requires actual travel from the original animal position");
                    Require(sky.Contains(start),"Rain no longer covers the original activity area");
                    held=agent.transform.position;Log("PASS actual arrival at "+ShelterSource()+" after path="+path.ToString("F3")+"m; outsideCurrentCircle="+outside+"; "+State());
                    Capture("02-arrived");Next(2);
                }
            }
            else if(stage==2 || stage==3)
            {
                Require(Action=="shelter" && agent.HasLifeTarget && agent.AutonomousTarget,"Shelter released while original activity area still has rain; "+State());
                Require(sky.SoundActive && sky.SoundStrength==(stage==2?2:1) && sky.Contains(start),"Expected continuing rain no longer covers the original activity area");
                Require(!(bool)Field(subject,"shelterRetry") && Flat(Goal,held)<.45f && ShelterSource()!=null,"Reached shelter changed into a failed-route holding pose");
                maxDrift=Mathf.Max(maxDrift,Flat(agent.transform.position,held));
                Require(maxDrift<.45f,"Animal left its sheltered resting position; "+State());
                if(stage==2 && age>=20)
                {
                    Log("PASS continuous20s strong-rain stay, maxDrift="+maxDrift.ToString("F4"));Capture("03-still-sheltered-after20s");
                    Require(weather.Cast("weak",out _),"Weak rain cast failed");weather.Apply(life.World,.000001f);Log("CAST weak, same exposure remains active");Next(3);
                }
                else if(stage==3 && age>=10)
                {
                    Log("PASS another10s weak-rain stay after strong rain; no reroll or departure");Capture("04-still-sheltered-in-weaker-rain");
                    Require(weather.Cast("clear",out _),"Clear cast failed");weather.Apply(life.World,.000001f);clearAt=Time.time;Log("CAST clear");Next(4);
                }
            }
            else if(stage==4)
            {
                if(age>1 && Action!="shelter" && !(bool)Property(Field(subject,"rainReaction"),"NeedsShelter"))
                {
                    Require(!sky.SoundActive,"Sky remained active after clear");
                    Require(Parameters()==parameters,"Animal personality/scale/habitat changed during the fixture");
                    Log("PASS clear releases shelter and returns control to ordinary activity; "+State());Capture("05-after-clear");
                    recoveryStart=agent.transform.position;recoveryPathStart=path;Next(5);
                }
                else Require(age<8,"Shelter did not release after clear");
            }
            else if(stage==5 || stage==6)
            {
                Require(!sky.SoundActive && Action!="shelter" && !(bool)Property(Field(subject,"rainReaction"),"NeedsShelter"),
                    "Shelter returned after clear during the ordinary activity observation; "+State());
                Require(!(Action=="resume" && agent.HasLifeTarget && agent.AutonomousTarget),
                    "An autonomous lease reclaimed the animal while its director was in ordinary resume; "+State());
                recoveryMaximumSpeed=Mathf.Max(recoveryMaximumSpeed,agent.ActualSpeed);
                if(stage==5 && age>=10)
                {
                    Require(Parameters()==parameters,"Animal parameters changed during post-rain observation");
                    Log("PASS10s ordinary activity after clear without shelter or stale lease reclaim; natural idle allowed; displacement="+
                        Flat(agent.transform.position,recoveryStart).ToString("F3")+" travelled="+(path-recoveryPathStart).ToString("F3")+
                        " maxSpeed="+recoveryMaximumSpeed.ToString("F3")+"; "+State());
                    Capture("06-ten-seconds-after-clear");Next(6);
                }
                if(stage==6 && path-recoveryPathStart>=1)
                {
                    Require(Parameters()==parameters,"Animal parameters changed during natural movement recovery");
                    Log("PASS actual ordinary locomotion after clear without a forced target; elapsed="+(Time.time-clearAt).ToString("F3")+
                        "s travelled="+(path-recoveryPathStart).ToString("F3")+"m displacement="+Flat(agent.transform.position,recoveryStart).ToString("F3")+
                        " maxSpeed="+recoveryMaximumSpeed.ToString("F3")+"; "+State());
                    Capture("07-natural-movement-resumed");Finish();
                }
                else Require(Time.time-clearAt<60,"No actual ordinary movement of at least1m within60s after clear; "+State());
            }
        }
        catch(Exception error){failures++;Log("FAIL "+error);Finish();}
    }
    static void Capture(string name)
    {
        WildlifeAgent companion=agent.Population.agents.FirstOrDefault(value=>value && value.spawnId==6102);
        if(companion)
        {
            MethodInfo overlap=typeof(WildlifeBodyPhysics).GetMethod("BodyOverlap",BindingFlags.NonPublic|BindingFlags.Static);
            float depth=(float)overlap.Invoke(null,new object[]{agent.BodyEnvelope,agent.transform.position,agent.transform.rotation,
                companion.BodyEnvelope,companion.transform.position,companion.transform.rotation});
            maxSampledBodyOverlap=Mathf.Max(maxSampledBodyOverlap,depth);
            Log("BODY SNAPSHOT "+name+" sheep="+agent.transform.position.ToString("F3")+" deer="+companion.transform.position.ToString("F3")+
                " separation="+Flat(agent.transform.position,companion.transform.position).ToString("F4")+
                " envelopeOverlap="+depth.ToString("F6")+"; measured collision envelopes, not animated mesh intersection");
        }
        Vector3 midpoint=(agent.transform.position+player.position)*.5f;
        float distance=Mathf.Max(17,Flat(agent.transform.position,player.position)*.65f/Mathf.Tan(27.5f*Mathf.Deg2Rad));
        Render(name,midpoint+new Vector3(6,10,-12).normalized*distance,midpoint+Vector3.up*.6f);
        if(!closeLocked || stage<2 || stage>=5)
        {
            var parts=agent.GetComponentsInChildren<Renderer>().Where(r=>r.enabled && r.gameObject.activeInHierarchy && !(r is ParticleSystemRenderer)).ToArray();
            Bounds bounds=new Bounds(agent.transform.position+Vector3.up*.5f,Vector3.one);
            if(parts.Length>0){bounds=parts[0].bounds;foreach(var part in parts)bounds.Encapsulate(part.bounds);}
            closeLook=bounds.center;float range=Mathf.Clamp(bounds.extents.magnitude*3.2f,3.2f,7);
            closePosition=closeLook+new Vector3(4,2,-5).normalized*range;
            for(int i=0;i<12;i++)
            {
                Vector3 candidate=closeLook+Quaternion.Euler(0,i*30,0)*(new Vector3(4,2,-5).normalized*range);
                Vector3 direction=closeLook-candidate;
                if(Physics.RaycastAll(candidate,direction.normalized,direction.magnitude,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore)
                    .Any(hit=>!hit.collider.transform.IsChildOf(agent.transform)))continue;
                closePosition=candidate;break;
            }
            if(name=="02-arrived")closeLocked=true;
        }
        Render(name+"-close",closePosition,closeLook);
    }
    static void Render(string name,Vector3 position,Vector3 look)
    {
        Require(Camera.main,"Main camera missing");
        var go=new GameObject("Shelter evidence camera");var camera=go.AddComponent<Camera>();camera.CopyFrom(Camera.main);camera.enabled=false;camera.orthographic=false;camera.fieldOfView=55;
        camera.transform.position=position;camera.transform.LookAt(look);
        var target=new RenderTexture(1280,720,24);var image=new Texture2D(1280,720,TextureFormat.RGB24,false);var previous=RenderTexture.active;
        try{camera.targetTexture=target;camera.Render();RenderTexture.active=target;image.ReadPixels(new Rect(0,0,1280,720),0,0);image.Apply();File.WriteAllBytes(Path.Combine(Folder,name+".png"),image.EncodeToPNG());Log("CAPTURE "+name+"; camera="+position.ToString("F3")+" look="+look.ToString("F3")+"; actual world, evidence camera; not player UI capture");}
        finally{RenderTexture.active=previous;camera.targetTexture=null;target.Release();Object.Destroy(target);Object.Destroy(image);Object.Destroy(go);}
    }
    static void Finish()
    {
        Log("RESULT failures="+failures);SessionState.SetInt(Key+".exit",failures==0?0:1);SessionState.SetBool(Key+".finishing",true);
        SessionState.SetString(Key+".metrics","outsideCurrentRainCircle="+outside+"; maxHoldDrift="+maxDrift.ToString("F4")+"; path="+path.ToString("F3")+
            "; recoverySeconds="+(clearAt>=0?Time.time-clearAt:0).ToString("F3")+
            "; recoveryPath="+(recoveryPathStart>=0?path-recoveryPathStart:0).ToString("F3")+"; recoveryMaximumSpeed="+recoveryMaximumSpeed.ToString("F3")+
            "; maxSnapshotBodyEnvelopeOverlap="+maxSampledBodyOverlap.ToString("F6"));
        if(EditorApplication.isPlaying)EditorApplication.isPlaying=false;else VerifyAndExit();
    }
    static void VerifyAndExit()
    {
        int exit=SessionState.GetInt(Key+".exit",1);var protection=new List<string>();
        foreach(string value in SessionState.GetString(Key+".saves","").Split('\n'))
        {
            int split=value.LastIndexOf('|');if(split<0)continue;
            bool same=Hash(value.Substring(0,split))==value.Substring(split+1);if(!same)exit=1;
            protection.Add((same?"PASS ":"FAIL ")+"Real save unchanged: "+Path.GetFileName(value.Substring(0,split)));
        }
        bool scene=Hash(ScenePath)==SessionState.GetString(Key+".scene","");if(!scene)exit=1;
        protection.Add((scene?"PASS ":"FAIL ")+"Saved scene unchanged; Play=False");
        File.WriteAllLines(Path.Combine(Folder,"save-protection.txt"),protection);File.WriteAllText(Path.Combine(Folder,"result.txt"),"exit="+exit+"; "+SessionState.GetString(Key+".metrics","missing metrics"));
        SessionState.SetBool(Key,false);EditorApplication.Exit(exit);
    }
}
