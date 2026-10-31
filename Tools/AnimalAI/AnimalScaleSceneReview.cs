using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using WeatherHouse.Hillside;
using WeatherHouse.WatersideV2.Editor;
using Object=UnityEngine.Object;

[InitializeOnLoad]
public static class AnimalScaleSceneReview
{
    const string ScenePath="Assets/Scenes/WeatherHouse_WatersideV2.unity";
    const string SessionKey="AnimalScaleSceneReview.Active";
    const int CaptureLayer=31;
    static double entered;
    static int enteredFrame;
    static int editStage,editTicks;
    static readonly List<string> log=new List<string>();
    [Serializable] public sealed class Pose
    {
        public Vector3 visualScale,visualOffset,rootPosition,rootScale,bodySize;
        public float factor,radius;
    }
    [Serializable] public sealed class ImageMeasurement
    {public string name;public int x,y,width,height,pixels;}
    [Serializable] public sealed class Record
    {
        public string species;
        public int spawnId;
        public Pose before,after,play,secondInitialize;
        public Vector3 cameraPosition;
        public Quaternion cameraRotation;
        public float cameraSize;
        public List<ImageMeasurement> images=new List<ImageMeasurement>();
    }
    [Serializable] public sealed class Report
    {
        public string result,scene,rendering="Actual saved-scene animal; isolated orthographic inspection camera; 0.25m grid; identical camera before/after/Play.";
        public string[] fixtureResults;
        public Record[] animals;
    }
    static string Folder=>Environment.GetEnvironmentVariable("WEATHERHOUSE_SCALE_EVIDENCE")??Path.GetFullPath("Evidence/Scale");
    static string JsonPath=>Path.Combine(Folder,"scale-evidence.json");
    static AnimalScaleSceneReview()
    {
        EditorApplication.playModeStateChanged+=state=>{
            if(state==PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(SessionKey,false))
            {entered=EditorApplication.timeSinceStartup;enteredFrame=Time.frameCount;Application.runInBackground=true;}
            if(state==PlayModeStateChange.EnteredEditMode && SessionState.GetBool(SessionKey+".finishing",false))VerifyAndExit();
        };
        EditorApplication.update+=Tick;
    }
    static void Require(bool value,string message){if(!value)throw new InvalidOperationException(message);}
    static void Guard()
    {
        Require(Application.isBatchMode && Application.dataPath.IndexOf("ValidationProject",StringComparison.OrdinalIgnoreCase)>=0,
            "This entry saves only an independent ValidationProject clone in batch mode.");
    }
    static bool Chosen(WildlifeAgent a)=>a && (a.speciesId=="squirrel" && a.spawnId>=14 && a.spawnId<=19 || a.speciesId=="turtle" && a.spawnId>=43 && a.spawnId<=47);
    static WildlifeAgent[] Animals()=>Object.FindObjectsOfType<WildlifeAgent>(true).Where(Chosen).OrderBy(a=>a.spawnId).ToArray();
    static Pose Read(WildlifeAgent a)=>new Pose{visualScale=a.visualRoot.localScale,visualOffset=a.visualRoot.localPosition,
        rootPosition=a.transform.position,rootScale=a.transform.localScale,bodySize=WildlifeBodyEnvelope.Measure(a.transform,a.Radius).half*2,factor=a.ScaleFactor,radius=a.Radius};
    static void Same(Vector3 a,Vector3 b,string message){Require((a-b).magnitude<.0001f,message+" actual="+a.ToString("R")+" expected="+b.ToString("R"));}
    static void Write(Report report){Directory.CreateDirectory(Folder);File.WriteAllText(JsonPath,JsonUtility.ToJson(report,true));}

    public static void Batch()
    {
        try
        {
            Guard();Directory.CreateDirectory(Folder);
            SessionState.SetBool(SessionKey+".finishing",false);
            SessionState.SetString(SessionKey+".previousSave",Environment.GetEnvironmentVariable("WEATHERHOUSE_LIFE_REVIEW_SAVE")??"");
            var guards=new List<string>();
            foreach(string name in new[]{"language-notebook.json","life-progress.json","onboarding.json"})
            {string save=Path.Combine(Application.persistentDataPath,name);guards.Add(save+"|"+Hash(save));}
            SessionState.SetString(SessionKey+".saveHashes",string.Join("\n",guards));
            string isolated=Path.GetFullPath(Path.Combine(Folder,"isolated-save"));Directory.CreateDirectory(isolated);
            Require(!string.Equals(isolated.TrimEnd('\\','/'),Path.GetFullPath(Application.persistentDataPath).TrimEnd('\\','/'),StringComparison.OrdinalIgnoreCase),"Review save must be isolated");
            Environment.SetEnvironmentVariable("WEATHERHOUSE_LIFE_REVIEW_SAVE",isolated);
            File.Copy(ScenePath,Path.Combine(Folder,"scene-before-size-bake.unity"),true);
            EditorSceneManager.OpenScene(ScenePath);
            var report=new Report{scene=ScenePath,result="EDIT_STARTED",fixtureResults=AnimalScalePreviewChecks.Run()};
            WildlifeAgent[] animals=Animals();Require(animals.Length==11,"Expected exactly 11 saved squirrels/turtles");
            report.animals=animals.Select(a=>new Record{species=a.speciesId,spawnId=a.spawnId,before=Read(a)}).ToArray();
            int changed=WatersideAnimalScalePreview.ApplyToActiveScene();
            Require(changed==11,"Expected 11 fresh preview bakes, got "+changed);
            foreach(Record record in report.animals)
            {
                WildlifeAgent animal=animals.First(a=>a.spawnId==record.spawnId);record.after=Read(animal);
                Same(record.before.rootPosition,record.after.rootPosition,"Baking moved animal root");
                Same(record.before.rootScale,record.after.rootScale,"Baking changed animal root scale");
                if(record.spawnId==14 || record.spawnId==44)Frame(animal,record);
                // The renderer must observe a player-loop update between changed skinned poses.
                animal.visualRoot.localScale=record.before.visualScale;animal.visualRoot.localPosition=record.before.visualOffset;
            }
            report.result="AWAITING_EDIT_RENDER_UPDATE";Write(report);
            editStage=1;editTicks=0;SessionState.SetBool(SessionKey,true);EditorApplication.QueuePlayerLoopUpdate();
        }
        catch(Exception e){Finish("FAIL "+e);}
    }

    static void Tick()
    {
        if(!SessionState.GetBool(SessionKey,false)||SessionState.GetBool(SessionKey+".finishing",false))return;
        try
        {
            Guard();
            if(!EditorApplication.isPlaying){TickEdit();return;}
            Require(string.Equals(Environment.GetEnvironmentVariable("WEATHERHOUSE_LIFE_REVIEW_SAVE"),Path.GetFullPath(Path.Combine(Folder,"isolated-save")),StringComparison.OrdinalIgnoreCase),"Isolated save override was lost before Play");
            if(Time.frameCount<enteredFrame+2)return;
            WildlifeAgent[] animals=Animals();
            Require(animals.Length==11,"Saved animal set changed on entering Play");
            if(animals.Any(a=>!a.Population || !a.Population.Initialized))
            {Require(EditorApplication.timeSinceStartup-entered<180,"Population did not initialize");return;}
            // Freeze only after normal Start/Initialize ran. No animal is relocated for the capture.
            foreach(WildlifePopulation population in animals.Select(a=>a.Population).Distinct())population.enabled=false;
            var report=JsonUtility.FromJson<Report>(File.ReadAllText(JsonPath));
            foreach(Record record in report.animals)
            {
                WildlifeAgent animal=animals.First(a=>a.spawnId==record.spawnId);record.play=Read(animal);
                Same(record.play.visualScale,record.after.visualScale,"Play changed baked visual scale: "+record.species+record.spawnId);
                Same(record.play.visualOffset,record.after.visualOffset,"Play changed baked foot offset: "+record.species+record.spawnId);
                if(record.spawnId==14 || record.spawnId==44)
                {
                    ImageMeasurement rendered=Capture(animal,record,record.species+"-play");record.images.Add(rendered);
                    ImageMeasurement edit=record.images.First(i=>i.name.EndsWith("-edit-after",StringComparison.Ordinal));
                    float width=rendered.width/(float)edit.width,height=rendered.height/(float)edit.height;
                    Write(report);
                    Require(width>.7f && width<1.35f && height>.7f && height<1.35f,
                        "Rendered Edit/Play size mismatch: "+record.species+" widthRatio="+width+" heightRatio="+height);
                    log.Add("PASS RASTER "+record.species+" reopenedEdit="+edit.width+"x"+edit.height+" Play="+rendered.width+"x"+rendered.height+"; animation pose allowed35percent");
                }
                animal.Initialize(animal.Population);record.secondInitialize=Read(animal);
                Same(record.secondInitialize.visualScale,record.after.visualScale,"Second Initialize applied scale twice");
                Same(record.secondInitialize.visualOffset,record.after.visualOffset,"Second Initialize applied offset twice");
                Require(Mathf.Abs(record.secondInitialize.radius-record.play.radius)<.0001f,"Second Initialize changed radius");
                log.Add("PASS "+record.species+"/"+record.spawnId+" edit="+record.after.visualScale.ToString("R")+
                    " Play="+record.play.visualScale.ToString("R")+" second="+record.secondInitialize.visualScale.ToString("R")+
                    " offset="+record.play.visualOffset.ToString("R")+" runtimeRootDelta="+(record.play.rootPosition-record.after.rootPosition).ToString("R"));
            }
            report.result="PASS 11 edit/Play/second-initialize comparisons; 6 same-camera captures including saved-and-reopened Edit; raster size checked; saved clone only";
            Write(report);Finish(report.result);
        }
        catch(Exception e){Finish("FAIL "+e);}
    }

    static void TickEdit()
    {
        if(editStage==0)return;
        EditorApplication.QueuePlayerLoopUpdate();SceneView.RepaintAll();
        if(++editTicks<8)return;
        var report=JsonUtility.FromJson<Report>(File.ReadAllText(JsonPath));WildlifeAgent[] animals=Animals();
        if(editStage==1)
        {
            foreach(Record record in report.animals)
            {
                WildlifeAgent animal=animals.First(a=>a.spawnId==record.spawnId);
                if(record.spawnId==14 || record.spawnId==44)record.images.Add(Capture(animal,record,record.species+"-edit-before"));
                animal.visualRoot.localScale=record.after.visualScale;animal.visualRoot.localPosition=record.after.visualOffset;
            }
            Require(WatersideAnimalScalePreview.ApplyToActiveScene()==0,"Second bake accumulated scale");
            Guard();Require(EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene()),"Clone scene save failed");
            File.Copy(ScenePath,Path.Combine(Folder,"scene-baked.unity"),true);SessionState.SetString(SessionKey+".bakedSceneHash",Hash(ScenePath));
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            EditorSceneManager.OpenScene(ScenePath);
            report.result="BAKED_CLONE_REOPENED_AWAITING_EDIT_RENDER";Write(report);editStage=2;editTicks=0;return;
        }
        foreach(Record record in report.animals)
        {
            WildlifeAgent animal=animals.First(a=>a.spawnId==record.spawnId);Pose reopened=Read(animal);
            Same(reopened.visualScale,record.after.visualScale,"Reopened edit scale changed");
            Same(reopened.visualOffset,record.after.visualOffset,"Reopened edit offset changed");
            record.after=reopened;
            if(record.spawnId!=14 && record.spawnId!=44)continue;
            ImageMeasurement rendered=Capture(animal,record,record.species+"-edit-after");record.images.Add(rendered);
            ImageMeasurement before=record.images.First(i=>i.name.EndsWith("-edit-before",StringComparison.Ordinal));
            float expected=record.after.visualScale.magnitude/record.before.visualScale.magnitude;
            float width=rendered.width/(float)before.width,height=rendered.height/(float)before.height;
            Write(report);
            Require(Mathf.Abs(width/expected-1)<.15f && Mathf.Abs(height/expected-1)<.15f,
                "Reopened Edit renderer did not apply baked size: "+record.species+" expected="+expected+" width="+width+" height="+height);
            log.Add("PASS RASTER EDIT "+record.species+" before="+before.width+"x"+before.height+" reopenedAfter="+rendered.width+"x"+rendered.height+" expectedRatio="+expected);
        }
        report.result="REOPENED_EDIT_RENDER_VERIFIED_AWAITING_PLAY";Write(report);editStage=0;EditorApplication.isPlaying=true;
    }

    static void Frame(WildlifeAgent animal,Record record)
    {
        WildlifeBodyEnvelope body=WildlifeBodyEnvelope.Measure(animal.transform,animal.Radius);
        Vector3 center=animal.transform.position+animal.transform.rotation*body.center;
        float factor=record.before.visualScale.magnitude/Mathf.Max(.001f,record.after.visualScale.magnitude);
        float largest=Mathf.Max(1,factor),size=Mathf.Max(.6f,body.half.magnitude*largest*1.55f);
        record.cameraSize=size;record.cameraPosition=center+new Vector3(1.6f,.9f,-2.4f).normalized*size*4;
        record.cameraRotation=Quaternion.LookRotation(center-record.cameraPosition);
    }

    static ImageMeasurement Capture(WildlifeAgent animal,Record record,string name)
    {
        var savedLayers=new Dictionary<GameObject,int>();var hidden=new List<Renderer>();
        GameObject cameraRoot=null,grid=null;Material gridMaterial=null;RenderTexture target=null;Texture2D image=null;
        RenderTexture old=RenderTexture.active;
        try
        {
            foreach(Renderer other in Object.FindObjectsOfType<Renderer>())
                if(other.gameObject.layer==CaptureLayer && !other.transform.IsChildOf(animal.transform) && other.enabled)
                {hidden.Add(other);other.enabled=false;}
            foreach(Renderer renderer in animal.GetComponentsInChildren<Renderer>())
            {if(!savedLayers.ContainsKey(renderer.gameObject))savedLayers.Add(renderer.gameObject,renderer.gameObject.layer);renderer.gameObject.layer=CaptureLayer;}
            cameraRoot=new GameObject("Scale evidence camera"){hideFlags=HideFlags.HideAndDontSave};
            Camera camera=cameraRoot.AddComponent<Camera>();camera.enabled=false;
            camera.orthographic=true;camera.orthographicSize=record.cameraSize;camera.nearClipPlane=.01f;camera.farClipPlane=100;
            camera.cullingMask=1<<CaptureLayer;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.16f,.19f,.19f);
            camera.transform.SetPositionAndRotation(record.cameraPosition,record.cameraRotation);
            grid=new GameObject("Scale evidence 0.25m grid"){hideFlags=HideFlags.HideAndDontSave};
            gridMaterial=new Material(Shader.Find("Sprites/Default")){hideFlags=HideFlags.HideAndDontSave};
            int cells=Mathf.Clamp(Mathf.CeilToInt(record.cameraSize/.25f),3,12);
            Vector3 origin=record.after.rootPosition;float reach=cells*.25f;
            for(int axis=0;axis<2;axis++)for(int i=-cells;i<=cells;i++)
            {
                var lineObject=new GameObject("0.25m grid line");lineObject.transform.SetParent(grid.transform);lineObject.layer=CaptureLayer;
                var line=lineObject.AddComponent<LineRenderer>();line.sharedMaterial=gridMaterial;line.positionCount=2;line.widthMultiplier=.005f;
                line.startColor=line.endColor=new Color(.37f,.42f,.42f,.75f);
                line.SetPositions(axis==0?new[]{origin+new Vector3(i*.25f,.004f,-reach),origin+new Vector3(i*.25f,.004f,reach)}:
                    new[]{origin+new Vector3(-reach,.004f,i*.25f),origin+new Vector3(reach,.004f,i*.25f)});
            }
            target=new RenderTexture(960,640,24);camera.targetTexture=target;camera.Render();
            RenderTexture.active=target;image=new Texture2D(960,640,TextureFormat.RGB24,false);
            image.ReadPixels(new Rect(0,0,960,640),0,0);image.Apply();
            File.WriteAllBytes(Path.Combine(Folder,name+".png"),image.EncodeToPNG());
            int left=960,right=-1,bottom=640,top=-1,count=0;Color32[] pixels=image.GetPixels32();
            for(int y=0;y<640;y++)for(int x=0;x<960;x++)
            {
                Color32 color=pixels[y*960+x];
                if(color.r<=color.b+12 || color.g<=color.b+8)continue;
                left=Mathf.Min(left,x);right=Mathf.Max(right,x);bottom=Mathf.Min(bottom,y);top=Mathf.Max(top,y);count++;
            }
            Require(count>50,"Animal did not render visibly: "+name);
            return new ImageMeasurement{name=name,x=left,y=bottom,width=right-left+1,height=top-bottom+1,pixels=count};
        }
        finally
        {
            RenderTexture.active=old;
            foreach(var item in savedLayers)if(item.Key)item.Key.layer=item.Value;
            foreach(Renderer renderer in hidden)if(renderer)renderer.enabled=true;
            if(image)Object.DestroyImmediate(image);if(target){target.Release();Object.DestroyImmediate(target);}
            if(cameraRoot)Object.DestroyImmediate(cameraRoot);if(grid)Object.DestroyImmediate(grid);if(gridMaterial)Object.DestroyImmediate(gridMaterial);
        }
    }

    static void Finish(string result)
    {
        SessionState.SetString(SessionKey+".result",result);SessionState.SetBool(SessionKey+".finishing",true);
        Directory.CreateDirectory(Folder);log.Add(result);
        File.WriteAllLines(Path.Combine(Folder,"scale-checks.txt"),log);
        if(File.Exists(JsonPath)){var report=JsonUtility.FromJson<Report>(File.ReadAllText(JsonPath));report.result=result;Write(report);}
        if(EditorApplication.isPlaying){EditorApplication.isPlaying=false;return;}
        VerifyAndExit();
    }
    static string Hash(string path)
    {if(!File.Exists(path))return "MISSING";using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-","");}
    static void VerifyAndExit()
    {
        SessionState.SetBool(SessionKey,false);SessionState.SetBool(SessionKey+".finishing",false);
        string result=SessionState.GetString(SessionKey+".result","FAIL missing result");bool preserved=true;
        var checks=new List<string>();
        foreach(string entry in SessionState.GetString(SessionKey+".saveHashes","").Split('\n'))
        {
            int split=entry.LastIndexOf('|');if(split<0)continue;
            string path=entry.Substring(0,split);bool same=Hash(path)==entry.Substring(split+1);preserved&=same;
            checks.Add((same?"PASS ":"FAIL ")+"Real save unchanged after exiting Play: "+Path.GetFileName(path));
        }
        string expected=SessionState.GetString(SessionKey+".bakedSceneHash","");
        if(!string.IsNullOrEmpty(expected))
        {bool same=Hash(ScenePath)==expected;preserved&=same;checks.Add((same?"PASS ":"FAIL ")+"Baked clone scene unchanged by Play");}
        checks.Add("Play=False; main project scene was never loaded or saved by this entry");
        File.WriteAllLines(Path.Combine(Folder,"save-protection.txt"),checks);
        if(!preserved)result="FAIL save protection; "+result;
        if(File.Exists(JsonPath)){var report=JsonUtility.FromJson<Report>(File.ReadAllText(JsonPath));report.result=result;Write(report);}
        File.AppendAllLines(Path.Combine(Folder,"scale-checks.txt"),checks);
        string previous=SessionState.GetString(SessionKey+".previousSave","");Environment.SetEnvironmentVariable("WEATHERHOUSE_LIFE_REVIEW_SAVE",string.IsNullOrEmpty(previous)?null:previous);
        Debug.Log(result);if(Application.isBatchMode)EditorApplication.Exit(result.StartsWith("PASS")?0:1);
    }
}
