// A single real request against a prior Play evidence snapshot. Never enters Play or writes configuration.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;
using WeatherHouse.Hillside;
using WeatherHousePrototype;
using Object = UnityEngine.Object;

public static class AnimalStoryNetworkReview
{
    const string SourceRoot="C:/unity project/Weather House";
    const string ScenePath="Assets/Scenes/WeatherHouse_WatersideV2.unity";
    static readonly BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
    static readonly List<string> checks=new List<string>();
    static readonly Dictionary<string,string> protectedFiles=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
    static readonly Stopwatch clock=new Stopwatch();
    static string folder,sourceConfig,cloneConfig,inputPath,phase="initializing";
    static GameObject host;
    static AnimalPlannerClient client;
    static AnimalObservation observation;
    static AnimalPlan returnedPlan;
    static IEnumerator routine;
    static bool cancelled,finished;
    static int callbackCount,failures;

    [Serializable] sealed class Context
    {
        public string evidenceBoundary,inputFile,inputSha256;
        public string scenario;
        public int maximumRequests=1;
        public float deadlineSeconds=20;
    }
    static void Require(bool value,string message){if(!value)throw new InvalidOperationException(message);}
    static string Hash(string path)
    {if(!File.Exists(path))return "MISSING";using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-","");}
    static void Protect(string path){path=Path.GetFullPath(path);protectedFiles[path]=Hash(path);}
    static void Set(object target,string name,object value)=>target.GetType().GetField(name,Flags).SetValue(target,value);
    static void Check(bool value,string message)
    {if(!value)failures++;checks.Add((value?"PASS ":"FAIL ")+message);WriteChecks();}
    static void WriteChecks(){if(!string.IsNullOrEmpty(folder))File.WriteAllLines(Path.Combine(folder,"checks.txt"),checks);}
    static bool Finite(float value)=>!float.IsNaN(value) && !float.IsInfinity(value);

    public static void Batch()
    {
        // This guard precedes every possible source-configuration read and request.
        Require(Application.isBatchMode && Application.dataPath.Contains("ValidationProject") && !EditorApplication.isPlaying,
            "Independent ValidationProject batch in Edit mode only");
        string destination=Environment.GetEnvironmentVariable("WEATHERHOUSE_STORY_NETWORK_REPORT");
        Require(!string.IsNullOrWhiteSpace(destination),"Set WEATHERHOUSE_STORY_NETWORK_REPORT to an empty evidence folder");
        destination=Path.GetFullPath(destination);
        Require(!Directory.Exists(destination) || !Directory.EnumerateFileSystemEntries(destination).Any(),"Network evidence folder must be empty");
        folder=destination;Directory.CreateDirectory(folder);
        try
        {
            phase="capture protected source/clone configuration, saved scenes and real saves";
            sourceConfig=Path.GetFullPath(Path.Combine(SourceRoot,"UserSettings/WeatherHouseAnimalsAI.json"));
            cloneConfig=Path.GetFullPath(AnimalPlannerClient.ConfigurationPath);
            Require(!string.Equals(sourceConfig,cloneConfig,StringComparison.OrdinalIgnoreCase),"Source and validation configurations must differ");
            Protect(sourceConfig);Protect(cloneConfig);Protect(ScenePath);Protect(Path.Combine(SourceRoot,ScenePath));
            foreach(string name in new[]{"language-notebook.json","life-progress.json","onboarding.json"})
                Protect(Path.Combine(Application.persistentDataPath,name));

            phase="read completed Play evidence ledger";
            inputPath=Environment.GetEnvironmentVariable("WEATHERHOUSE_STORY_NETWORK_INPUT");
            Require(!string.IsNullOrWhiteSpace(inputPath),"Set WEATHERHOUSE_STORY_NETWORK_INPUT to the completed Play story-state.json");
            inputPath=Path.GetFullPath(inputPath);
            Require(Path.GetFileName(inputPath)=="story-state.json" && File.Exists(inputPath),"The prior Play story-state.json is required");
            Protect(inputPath);
            AnimalStoryState state=JsonUtility.FromJson<AnimalStoryState>(File.ReadAllText(inputPath));
            Require(state!=null && state.animalId=="life.harbor.sheep" && state.events!=null,
                "Expected the actual harbor sheep's closed Play story state");
            // Unity may deserialize a serialized null nested object as an empty instance.
            // A real active ID remains disallowed; normalization must not hide an ongoing activity.
            Require(state.active==null || string.IsNullOrEmpty(state.active.id),"Snapshot has a real pending activity, not a closed Play state");
            state.NormalizeAfterLoad();
            Require(state.events.Any(e=>e!=null && e.activity=="watch_sea" && e.outcome=="completed" &&
                e.placeId=="harbor.tide" && Finite(e.dwellSeconds) && e.dwellSeconds>=AnimalStoryState.RequiredDwellSeconds),
                "Input contains no actual completed watch_sea evidence with the required dwell");
            Check(true,"Input is the prior Play harbor-sheep ledger with a completed watch_sea; input preserved unchanged");

            phase="validate disabled clone configuration";
            Require(File.Exists(cloneConfig),"Validation configuration must exist and remain disabled");
            var disabled=JsonUtility.FromJson<AnimalPlannerClient.Configuration>(File.ReadAllText(cloneConfig));
            Require(disabled!=null && !disabled.enabled,"Validation configuration must be disabled on disk");
            phase="validate source configuration in memory";
            var config=ReadSourceConfiguration();
            Check(true,"Source configuration passed production-equivalent required-field/HTTPS endpoint checks; retained only in memory");

            phase="build snapshot from formal vocabulary and actual ledger";
            var text=Resources.Load<TextAsset>("WeatherHouseGlyphs");Require(text,"Formal glyph catalog is missing");
            GlyphCatalog catalog=JsonUtility.FromJson<GlyphCatalog>(text.text);
            Require(catalog?.glyphs!=null && catalog.glyphs.Length==37 && catalog.glyphs.Select(g=>g.id).Distinct().Count()==37,
                "Expected the formal 37-word vocabulary");
            observation=new AnimalObservation{
                animal=state.animalId,species=state.species,revision=1,current="resume",previous="watch_sea",
                outgoing=state.outgoing,raining=false,night=false,inHerd=false,fatigue=.2f,playerDistance=3.1f,
                character=state,canShareStory=true,allowedWishes=(string[])AnimalStoryRules.WishIds.Clone(),
                vocabulary=catalog.glyphs.Select(g=>new AnimalStoryWord{id=g.id,meaning=g.name+"："+g.boundary}).ToArray(),
                options=new[]{new AnimalOption{id="resume",action="resume",target="previous",score=1},
                    new AnimalOption{id="watch_sea",action="watch_sea",target="harbor.tide",score=1.45f}}};
            File.WriteAllText(Path.Combine(folder,"input-observation.json"),JsonUtility.ToJson(observation,true));
            File.WriteAllText(Path.Combine(folder,"review-context.json"),JsonUtility.ToJson(new Context{
                inputFile=inputPath,inputSha256=Hash(inputPath),
                evidenceBoundary="One real DeepSeek protocol request using a previous actual Play event ledger. This is an arranged observation snapshot, not a claim about a live position. Network acceptance does not prove locomotion, dwelling, visuals, or new-player tutorial flow.",
                scenario="Snapshot controls: resume after watch_sea; dry daytime; fatigue 0.2; player distance 3.1m; sharing allowed; resume/watch_sea capabilities. Character preferences, relationship and event ledger come unchanged from the prior Play evidence. Silence is a legal response; no line of speech is required."},true));

            phase="schedule one production Plan request";
            host=new GameObject("Single story network review"){hideFlags=HideFlags.HideAndDontSave};
            client=host.AddComponent<AnimalPlannerClient>();
            // Production Plan still owns Payload, transport, ReadResponse and AnimalPlan validation.
            // Only the fixture configuration source differs; the real credential stays in memory.
            Set(client,"config",config);Set(client,"nextConfigurationRead",float.PositiveInfinity);
            routine=client.Plan(observation,plan=>{callbackCount++;returnedPlan=plan;},()=>cancelled);
            clock.Start();EditorApplication.update+=Advance;
            checks.Add("INFO Exactly one production Plan invocation scheduled; no retries. Credentials and raw response/headers are not written.");WriteChecks();
        }
        catch(Exception error)
        {
            // Never serialize exception messages or configuration: a parsing/transport error could include secret text.
            Check(false,"Setup failed during "+phase+" ("+error.GetType().Name+"); no network retry was attempted.");
            Finish();
        }
    }
    public static AnimalPlannerClient.Configuration ReadSourceConfiguration()
    {
        string path=Path.Combine(SourceRoot,"UserSettings/WeatherHouseAnimalsAI.json");
        Require(File.Exists(path),"Source planner configuration is missing");
        var config=JsonUtility.FromJson<AnimalPlannerClient.Configuration>(File.ReadAllText(path));
        Require(config!=null && config.enabled && !string.IsNullOrWhiteSpace(config.apiKey) && !string.IsNullOrWhiteSpace(config.model),
            "Source configuration is not enabled or lacks required fields");
        Require(Uri.TryCreate(config.baseUrl,UriKind.Absolute,out Uri address) && address.Scheme=="https" &&
            address.Host=="api.deepseek.com" && string.IsNullOrEmpty(address.UserInfo) && string.IsNullOrEmpty(address.Query) &&
            string.IsNullOrEmpty(address.Fragment) && !config.apiKey.Contains("\r") && !config.apiKey.Contains("\n"),
            "Source configuration violates production endpoint/header validation");
        config.timeoutSeconds=Mathf.Clamp(config.timeoutSeconds,3,12);
        return config;
    }
    static void Advance()
    {
        if(finished)return;
        try
        {
            if(clock.Elapsed.TotalSeconds>=20)cancelled=true;
            bool more=routine.MoveNext();
            if(client.Requests>1)throw new InvalidOperationException("Request-count guard");
            // Current production Plan yields null while UnityWebRequest progresses in the Editor update loop.
            if(more && routine.Current!=null)throw new InvalidOperationException("Unsupported production coroutine yield");
            if(more && !cancelled)return;
            if(more){Check(false,"20s total deadline reached; request cancelled and no retry attempted");Finish();return;}
            Check(!cancelled,"Request returned before the 20s total deadline; elapsed="+clock.Elapsed.TotalSeconds.ToString("F3")+"s");
            Check(client.Requests==1 && callbackCount==1,"Exactly one production network request and one completion callback; requests="+client.Requests+" callbacks="+callbackCount);
            Check(returnedPlan!=null && client.Accepted==1,"Production ReadResponse/AnimalPlan accepted a valid plan; status="+client.LastStatus);
            if(returnedPlan!=null)
            {
                string json=JsonUtility.ToJson(returnedPlan,true);
                Check(AnimalPlan.TryRead(json,observation,out _),"Returned plan still passes the same revision, capability, vocabulary and evidence checks");
                File.WriteAllText(Path.Combine(folder,"response-plan.json"),json);
                checks.Add(returnedPlan.speech==null?"INFO Model chose silence: valid; no fixed speech was forced.":
                    "INFO Model supplied an allowed structured expression; this file is a protocol result, not a displayed or executed live action.");
            }
            Finish();
        }
        catch(Exception error)
        {Check(false,"Request runner failed ("+error.GetType().Name+"); cancelled without retry or raw transport logging.");Finish();}
    }
    static void Finish()
    {
        if(finished)return;finished=true;cancelled=true;EditorApplication.update-=Advance;clock.Stop();
        try{(routine as IDisposable)?.Dispose();}catch{Check(false,"Production iterator disposal failed");}
        routine=null;
        if(client){Set(client,"config",null);Set(client,"nextConfigurationRead",float.PositiveInfinity);}
        if(host)Object.DestroyImmediate(host);
        foreach(var file in protectedFiles)
        {
            bool unchanged=Hash(file.Key)==file.Value;
            Check(unchanged,"Protected file unchanged: "+file.Key+"; beforeSHA256="+file.Value+"; afterSHA256="+Hash(file.Key));
        }
        if(!string.IsNullOrEmpty(cloneConfig))
        {
            bool stillDisabled=false;
            try{stillDisabled=File.Exists(cloneConfig) && JsonUtility.FromJson<AnimalPlannerClient.Configuration>(File.ReadAllText(cloneConfig))?.enabled==false;}
            catch{ }
            Check(stillDisabled,"Validation configuration remains disabled on disk");
        }
        Check(!EditorApplication.isPlaying,"Stayed in Edit mode; no scene or user save was written by the fixture");
        checks.Add("RESULT failures="+failures+"; protocol acceptance only; prior Play evidence owns actual animal behavior.");WriteChecks();
        EditorApplication.Exit(failures==0?0:1);
    }
}
