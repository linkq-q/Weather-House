using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Copy into the independent validation project only; never saves the open scene.
public static class RainExpressionReview
{
    public static void Batch()
    {
        if(!Application.isBatchMode || !Application.dataPath.Contains("ValidationProject"))
            throw new InvalidOperationException("Independent ValidationProject batch only.");
        string folder=Environment.GetEnvironmentVariable("WEATHERHOUSE_MOVING_RAIN_REPORT");
        if(string.IsNullOrEmpty(folder))throw new InvalidOperationException("Set WEATHERHOUSE_MOVING_RAIN_REPORT.");
        Directory.CreateDirectory(folder);
        Environment.SetEnvironmentVariable("WEATHERHOUSE_ANIMAL_REPORT",Path.Combine(folder,"sleep-glyph-checks.txt"));
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
        var lines=new List<string>();int failures=0;
        void Check(string name,Func<IEnumerable<string>> run)
        {
            try { foreach(string line in run())lines.Add(line); }
            catch(Exception error){failures++;lines.Add("FAIL "+name+": "+error);}
        }
        Check("Autonomy",WeatherHouse.Hillside.Editor.AnimalAutonomyChecks.Run);
        Check("Shelter",ShelterRegression.Run);
        Check("Long shelter route",LongShelterRouteChecks.Run);
        Check("Shelter candidate scheduling",ShelterCandidateSchedulingChecks.Run);
        Check("Floor support",FloorSupportRegression.Run);
        Check("Work budget",AnimalWorkBudgetChecks.Run);
        Check("Moving rain",MovingRainReview.Contracts);
        Check("Urgent shelter",UrgentShelterChecks.Run);
        Check("Expression policy",AnimalExpressionPolicyChecks.Run);
        Check("Expression integration",AnimalExpressionIntegrationChecks.Run);
        Check("Existing sleep clips",AnimalExpressionIntegrationChecks.RunSleepClips);
        lines.Add("RESULT contract groups failed="+failures);
        File.WriteAllLines(Path.Combine(folder,"contracts.txt"),lines);
        if(failures>0){EditorApplication.Exit(1);return;}
        MovingRainReview.Batch();
    }
}
