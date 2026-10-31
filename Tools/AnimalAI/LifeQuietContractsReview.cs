#if UNITY_EDITOR
using System;
using System.IO;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public static class LifeQuietContractsReview
{
    public static void Batch()
    {
        if (!Application.isBatchMode || !Application.dataPath.Contains("ValidationProject"))
            throw new InvalidOperationException("Independent validation project only");
        string folder = Environment.GetEnvironmentVariable("WEATHERHOUSE_LIFE_GUIDANCE_REPORT");
        if (string.IsNullOrEmpty(folder) || Directory.Exists(folder))
            throw new InvalidOperationException("Use a fresh evidence directory");
        Directory.CreateDirectory(folder);
        var lines = new List<string>();
        int exit = 0;
        try
        {
            lines.AddRange(LifeQuietWorldChecks.Run());
            lines.AddRange(LifeArrivalRegionChecks.Run());
            lines.AddRange(LifeGuidanceChecks.Run());
        }
        catch (Exception error) { exit = 1; lines.Add("FAIL " + error); }
        File.WriteAllLines(Path.Combine(folder, "contracts.txt"), lines);
        EditorApplication.Exit(exit);
    }
}
#endif
