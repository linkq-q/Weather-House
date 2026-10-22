using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace WeatherHouse.Vegetation.Editor
{
    [InitializeOnLoad]
    public static class GrassParameterDefaultChecks
    {
        const string Folder = "VegetationLabReview/GrassDefaultsRevision";
        const string Stage = "WeatherHouse.GrassParameterDefaults.Stage";
        static double next, started;
        static int phase;
        static GrassParameterDefaultChecks() { EditorApplication.update += Poll; }
        static void Require(bool value, string text) { if (!value) throw new InvalidOperationException(text); }
        static void Log(string text) { File.AppendAllText(Folder + "/checks.txt", text + "\n"); }
        static GrassLabView View() { return Object.FindObjectOfType<GrassLabView>(true); }
        static void CheckScreenshots(GrassMeadow meadow)
        {
            Require(meadow.totalClumps == 40000 && meadow.seed == 431 && meadow.weights.SequenceEqual(new[] {78f,6f,.8f}), "Population defaults mismatch.");
            Require(Mathf.Abs(meadow.windStrength-.20f)<.00001f && Mathf.Abs(meadow.windSpeed-.80f)<.00001f && Mathf.Abs(meadow.upNormals-.82f)<.00001f, "Wind defaults mismatch.");
            var shortGrass = meadow.species[0]; var tallGrass = meadow.species[1];
            Require(Mathf.Abs(shortGrass.height-.30f)<.00001f && Mathf.Abs(shortGrass.width-.094f)<.00001f && shortGrass.bladeCount==13 && shortGrass.bend==0, "Short grass shape mismatch.");
            Require(Mathf.Abs(tallGrass.height-.51f)<.00001f && Mathf.Abs(tallGrass.width-.053f)<.00001f && tallGrass.bladeCount==11 && tallGrass.bend==0, "Tall grass shape mismatch.");
            Require(Mathf.Abs(shortGrass.tipWidthRatio-.37f)<.00001f && Mathf.Abs(shortGrass.spread-.30f)<.00001f && Mathf.Abs(shortGrass.rootSpread-.190f)<.00001f
                && Mathf.Abs(shortGrass.heightVariation-.35f)<.00001f && Mathf.Abs(shortGrass.outerBladeFraction-.30f)<.00001f && Mathf.Abs(shortGrass.clumpLayering-.55f)<.00001f && shortGrass.seed==137, "Short grass detail mismatch.");
            Require(Mathf.Abs(tallGrass.tipWidthRatio-.40f)<.00001f && Mathf.Abs(tallGrass.spread-.21f)<.00001f && Mathf.Abs(tallGrass.rootSpread-.141f)<.00001f
                && Mathf.Abs(tallGrass.heightVariation-.52f)<.00001f && Mathf.Abs(tallGrass.outerBladeFraction-.25f)<.00001f && Mathf.Abs(tallGrass.clumpLayering-.59f)<.00001f && tallGrass.seed==269, "Tall grass detail mismatch.");
            Require(meadow.ConfigurationError == null, "Grass material/species ownership broken.");
        }
        static void ApplyScreenshots()
        {
            GrassMeadowDefaultsSetup.Install();
            var meadow = View().regions[0]; var defaults = meadow.defaults;
            defaults.totalClumps=40000; defaults.seed=431; defaults.weights=new[] {78f,6f,.8f};
            defaults.windStrength=.20f; defaults.windSpeed=.80f; defaults.upNormals=.82f;
            for (int i=0;i<2;i++)
            {
                var plant=Object.Instantiate(meadow.species[i]);
                if (i==0) { plant.height=.30f; plant.width=.094f; plant.tipWidthRatio=.37f; plant.spread=.30f; plant.bend=0; plant.bladeCount=13; plant.rootSpread=.190f; plant.heightVariation=.35f; plant.outerBladeFraction=.30f; plant.clumpLayering=.55f; plant.seed=137; }
                else { plant.height=.51f; plant.width=.053f; plant.tipWidthRatio=.40f; plant.spread=.21f; plant.bend=0; plant.bladeCount=11; plant.rootSpread=.141f; plant.heightVariation=.52f; plant.outerBladeFraction=.25f; plant.clumpLayering=.59f; plant.seed=269; }
                defaults.plants[i]=JsonUtility.ToJson(plant); Object.DestroyImmediate(plant);
            }
            meadow.totalClumps=defaults.totalClumps; meadow.weights=(float[])defaults.weights.Clone(); meadow.seed=defaults.seed;
            meadow.windStrength=defaults.windStrength; meadow.windSpeed=defaults.windSpeed; meadow.upNormals=defaults.upNormals;
            EditorUtility.SetDirty(defaults); AssetDatabase.SaveAssetIfDirty(defaults);
            EditorSceneManager.MarkSceneDirty(meadow.gameObject.scene); EditorSceneManager.SaveScene(meadow.gameObject.scene);
            Log("Applied screenshot defaults to Region0; five independent region profiles connected. Existing species and meshes retained.");
        }
        static void Poll()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.timeSinceStartup<next) return;
            next=EditorApplication.timeSinceStartup+.25;
            string stage=SessionState.GetString(Stage,"");
            try
            {
                if ((stage=="play" || stage=="replay") && EditorApplication.isPlaying)
                {
                    Application.runInBackground=true;
                    if (started==0) { started=EditorApplication.timeSinceStartup; return; }
                    Require(EditorApplication.timeSinceStartup-started<120,"Runtime grass update timed out.");
                    var view=View(); var meadow=view.regions[0];
                    Require(string.IsNullOrEmpty(meadow.LiveError),"Live grass update error: "+meadow.LiveError);
                    if (meadow.IsLiveUpdating || EditorApplication.timeSinceStartup-started<2) return;
                    CheckScreenshots(meadow);
                    Require(meadow.Counts.Sum()==40000 && meadow.Counts.SequenceEqual(GrassMeadow.Allocate(40000,new[] {78f,6f,.8f})),"Saved population was not rebuilt on GPU meshes.");
                    var character=Object.FindObjectOfType<VegetationCharacterPreview>(); if(character) character.SetCharacterMode(false);
                    view.Shore(); view.showPanel=true;
                    if (stage=="play" && phase==0)
                    {
                        var others=view.regions.Skip(1).Select(m=>JsonUtility.ToJson(m.defaults)).ToArray();
                        meadow.windStrength=.21f; meadow.species[0].height=.31f;
                        view.SaveRegionDefaults();
                        Require(Mathf.Abs(meadow.defaults.windStrength-.21f)<.00001f && meadow.defaults.plants[0].Contains("0.31"),"Runtime save missed parameter profile.");
                        Require(File.ReadAllText(AssetDatabase.GetAssetPath(meadow.defaults)).Contains("windStrength: 0.21"),"Runtime save did not reach disk.");
                        Require(others.SequenceEqual(view.regions.Skip(1).Select(m=>JsonUtility.ToJson(m.defaults))),"Saving main region changed another region profile.");
                        JsonUtility.FromJsonOverwrite(File.ReadAllText(Folder+"/profile-before.json"),meadow.defaults); meadow.defaults.Apply(meadow);
                        view.SaveRegionDefaults();
                        Log("PASS actual Play: 40000 clumps rebuilt; all screenshot shapes/wind loaded; top-level save reached disk and preserved other region profiles.");
                        phase=1; return;
                    }
                    if (stage=="play")
                    {
                        ScreenCapture.CaptureScreenshot(Folder+"/runtime_panel.png");
                        SessionState.SetString(Stage,"capture"); started=EditorApplication.timeSinceStartup; return;
                    }
                    Log("PASS second Play after scene reopen: shape, weights, wind and 40000 generated clumps restored automatically.");
                    SessionState.SetString(Stage,"finish"); EditorApplication.isPlaying=false; return;
                }
                if (stage=="capture" && EditorApplication.isPlaying && EditorApplication.timeSinceStartup-started>1)
                { SessionState.SetString(Stage,"restore"); EditorApplication.isPlaying=false; return; }
                if ((stage=="restore" || stage=="finish" || stage=="failed") && !EditorApplication.isPlayingOrWillChangePlaymode)
                {
                    foreach(string file in Directory.GetFiles(Folder+"/MaterialsBefore", "*.mat"))
                    {
                        string asset=VegetationLabBuilder.Generated+"/"+Path.GetFileName(file);
                        File.WriteAllBytes(asset,File.ReadAllBytes(file)); AssetDatabase.ImportAsset(asset,ImportAssetOptions.ForceUpdate);
                    }
                    if(stage=="failed")
                    {
                        var mainDefaults=AssetDatabase.LoadAssetAtPath<GrassMeadowDefaults>(VegetationLabBuilder.Generated+"/MeadowDefaults/Region0.asset");
                        JsonUtility.FromJsonOverwrite(File.ReadAllText(Folder+"/profile-before.json"),mainDefaults);
                        EditorUtility.SetDirty(mainDefaults);AssetDatabase.SaveAssetIfDirty(mainDefaults);
                    }
                    EditorSceneManager.OpenScene(VegetationLabBuilder.ScenePath);
                    Require(File.ReadAllBytes(VegetationLabBuilder.ScenePath).SequenceEqual(File.ReadAllBytes(Folder+"/scene-before.bytes")),"Runtime save modified scene file.");
                    if (stage=="restore") { SessionState.SetString(Stage,"replay"); started=0; EditorApplication.isPlaying=true; }
                    else { Application.runInBackground=SessionState.GetBool(Stage+"Background",false); SessionState.EraseString(Stage); started=0; phase=0; Log((stage=="failed"?"RECOVERED":"PASS")+" returned to saved VegetationLab outside Play; unrelated scene fields and materials preserved."); }
                    return;
                }
                if(stage!="" || !File.Exists(Folder+"/request.txt"))return;
                string command=File.ReadAllText(Folder+"/request.txt").Trim();File.Delete(Folder+"/request.txt");
                var scene=SceneManager.GetActiveScene();
                Require(!EditorApplication.isPlayingOrWillChangePlaymode && !scene.isDirty && scene.path==VegetationLabBuilder.ScenePath && SceneManager.sceneCount==1,"Need saved VegetationLab outside Play; existing work preserved.");
                if(command=="apply-screenshots") { ApplyScreenshots();return; }
                Require(command=="verify","Unknown grass defaults check command.");
                var main=View().regions[0];Require(main.defaults,"No saved region profile.");
                File.WriteAllText(Folder+"/checks.txt","Unity "+Application.unityVersion+"; GPU "+SystemInfo.graphicsDeviceName+"\n");
                File.WriteAllText(Folder+"/profile-before.json",JsonUtility.ToJson(main.defaults));
                File.Copy(VegetationLabBuilder.ScenePath,Folder+"/scene-before.bytes",true);
                Directory.CreateDirectory(Folder+"/MaterialsBefore");
                foreach(string name in new[] {"ShortGrass","TallGrass","WhiteDaisy","PainterlyWater","PainterlyGround","QuietDaySky"})File.Copy(VegetationLabBuilder.Generated+"/"+name+".mat",Folder+"/MaterialsBefore/"+name+".mat",true);
                Require(View().regions.Select(m=>m.defaults).Distinct().Count()==5,"Region profiles not independent.");
                SessionState.SetBool(Stage+"Background",Application.runInBackground);Application.runInBackground=true;
                phase=0;started=0;SessionState.SetString(Stage,"play");EditorApplication.isPlaying=true;
            }
            catch(Exception error)
            {
                Log("FAIL "+error); Debug.LogException(error);
                SessionState.SetString(Stage,stage!="" && File.Exists(Folder+"/profile-before.json")?"failed":"");started=0;phase=0;
                if(EditorApplication.isPlaying)EditorApplication.isPlaying=false;
            }
        }
    }
}
