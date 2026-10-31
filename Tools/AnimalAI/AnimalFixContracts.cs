using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class AnimalFixContracts
{
    static bool continueIntoPlay;
    public static void BatchAndPlay() { continueIntoPlay=true;Batch(); }
    public static void Batch()
    {
        if(!Application.isBatchMode || !Application.dataPath.Contains("ValidationProject"))
            throw new InvalidOperationException("Use the isolated validation project.");
        string folder=Environment.GetEnvironmentVariable("WEATHERHOUSE_FIX_REPORT");
        if(string.IsNullOrEmpty(folder))throw new InvalidOperationException("Set WEATHERHOUSE_FIX_REPORT.");
        Directory.CreateDirectory(folder);
        Environment.SetEnvironmentVariable("WEATHERHOUSE_ANIMAL_REPORT",Path.Combine(folder,"glyph.txt"));
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
        var lines=new List<string>();int failures=0;
        void Check(string name,Func<IEnumerable<string>> check)
        {
            try{foreach(string line in check())lines.Add(line);}
            catch(Exception error){failures++;lines.Add("FAIL "+name+": "+error);}
        }
        Check("Autonomy and navigation",WeatherHouse.Hillside.Editor.AnimalAutonomyChecks.Run);
        Check("Shelter search",ShelterRegression.Run);
        Check("Baked edit sizes",AnimalScalePreviewChecks.Run);
        Check("Raised floor support",FloorSupportRegression.Run);
        Check("Work budget",()=>{
            string[] budget=AnimalWorkBudgetChecks.Run();
            File.WriteAllLines(Path.Combine(folder,"budget-checks.txt"),budget);
            return budget;
        });
        File.WriteAllLines(Path.Combine(folder,"contracts.txt"),lines);
        if(failures==0 && continueIntoPlay){AnimalBehaviorRegression.StartBatch();return;}
        EditorApplication.Exit(failures==0?0:1);
    }
}
