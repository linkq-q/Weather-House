// Consolidated isolated regression entry. Does not start network requests or save any scene.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class AnimalHealthReview
{
    static string Hash(string path)
    {if(!File.Exists(path))return "MISSING";using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-","");}
    static IEnumerable<Type> Types(Assembly assembly)
    {try{return assembly.GetTypes();}catch(ReflectionTypeLoadException error){return error.Types.Where(t=>t!=null);}}
    static Type Find(string name)=>AppDomain.CurrentDomain.GetAssemblies().SelectMany(Types).Single(t=>t.Name==name);
    static Exception Unwrap(Exception error)=>error is TargetInvocationException target && target.InnerException!=null?target.InnerException:error;
    public static void Batch()
    {
        if(!Application.isBatchMode || Application.isPlaying || !Application.dataPath.Contains("ValidationProject"))
            throw new InvalidOperationException("Isolated ValidationProject Edit mode only");
        string folder=Environment.GetEnvironmentVariable("WEATHERHOUSE_ANIMAL_HEALTH_REPORT");
        if(string.IsNullOrWhiteSpace(folder) || Directory.Exists(folder))throw new InvalidOperationException("Use a fresh report folder");
        Directory.CreateDirectory(folder);
        var paths=new[]{Path.GetFullPath("Assets/Scenes/WeatherHouse_WatersideV2.unity"),"C:/unity project/Weather House/Assets/Scenes/WeatherHouse_WatersideV2.unity"}
            .Concat(new[]{"language-notebook.json","life-progress.json","onboarding.json"}.Select(name=>Path.Combine(Application.persistentDataPath,name))).ToArray();
        var before=paths.ToDictionary(path=>path,Hash);
        Environment.SetEnvironmentVariable("WEATHERHOUSE_LIFE_REVIEW_SAVE",Path.Combine(folder,"isolated-save"));
        Directory.CreateDirectory(Path.Combine(folder,"isolated-save"));
        Environment.SetEnvironmentVariable("WEATHERHOUSE_ANIMAL_REPORT",Path.Combine(folder,"sleep-glyph-checks.txt"));
        var lines=new List<string>();int failures=0;
        string[] cases={
            "AnimalAutonomyChecks.Run", "ShelterRegression.Run", "LongShelterRouteChecks.Run", "ShelterCandidateSchedulingChecks.Run",
            "ShelterStartupChecks.Run", "ShelterPersistenceChecks.Run", "RainHerdLeaseChecks.Run", "JourneyMovementHealthChecks.Run",
            "AnimalTurnChecks.Run", "FloorSupportRegression.Run", "AnimalWorkBudgetChecks.Run", "PopulationDecisionFairnessChecks.Run", "MovingRainReview.Contracts", "UrgentShelterChecks.Run",
            "AnimalExpressionPolicyChecks.Run", "AnimalExpressionIntegrationChecks.Run", "AnimalExpressionIntegrationChecks.RunSleepClips",
            "LifeQuietWorldChecks.Run", "LifeArrivalRegionChecks.Run", "LifeGuidanceChecks.Run",
            "AnimalStoryStateChecks.Run", "AnimalStoryPlanChecks.Run", "AnimalStoryIntegrationChecks.Run", "AnimalStorySightlineChecks.Run", "DemoTeachingChecks.Run"
        };
        foreach(string name in cases)
        {
            try
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
                string[] split=name.Split('.');
                var output=(IEnumerable<string>)Find(split[0]).GetMethod(split[1],BindingFlags.Public|BindingFlags.Static).Invoke(null,null);
                foreach(string line in output)
                {lines.Add(name+": "+line);if(line.Contains("FAIL"))throw new InvalidOperationException("Check reported a failure");}
            }
            catch(Exception error){failures++;lines.Add("FAIL "+name+": "+Unwrap(error));}
            File.WriteAllLines(Path.Combine(folder,"contracts.txt"),lines);
        }
        try
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            string glyphFolder=Path.Combine(folder,"emotion-rendering");Directory.CreateDirectory(glyphFolder);
            var glyphLines=new List<string>();
            Find("EmotionGlyphRegression").GetMethod("Run",BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,new object[]{glyphFolder,glyphLines});
            lines.AddRange(glyphLines.Select(line=>"Emotion rendering: "+line));
            File.WriteAllLines(Path.Combine(glyphFolder,"checks.txt"),glyphLines);
        }
        catch(Exception error){failures++;lines.Add("FAIL Emotion rendering: "+Unwrap(error));}
        foreach(string path in paths)
        {
            bool same=Hash(path)==before[path];if(!same)failures++;
            lines.Add((same?"PASS ":"FAIL ")+"Protected file unchanged: "+path);
        }
        lines.Add("RESULT failedGroups="+failures);
        File.WriteAllLines(Path.Combine(folder,"contracts.txt"),lines);
        File.WriteAllText(Path.Combine(folder,"result.txt"),"exit="+(failures==0?0:1));
        if(failures==0 && !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("WEATHERHOUSE_SHELTER_HOLD_REPORT")))
        {RainShelterHoldReview.Batch();return;}
        EditorApplication.Exit(failures==0?0:1);
    }
}
