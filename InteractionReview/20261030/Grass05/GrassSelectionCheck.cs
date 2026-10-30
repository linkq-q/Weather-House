using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using WeatherHouse.Life;

public static class GrassSelectionCheck
{
    public static void Run()
    {
        WeatherHouse.Life.Editor.LifeAudioChecks.Run();
        var catalog = Resources.Load<LifeAudioCatalog>("LifeAudio/Catalog");
        var clip = catalog.Find(LifeAudioCue.GrassStep).clips.Single();
        if (clip.name != "step_grass_a" || Math.Abs(clip.length - .5f) > .01f || !clip.LoadAudioData())
            throw new Exception("Selected grass 05 failed to import or bind");
        var samples = new float[clip.samples * clip.channels];
        if (!clip.GetData(samples, 0) || samples.Max(x => Math.Abs(x)) < .1f)
            throw new Exception("Selected grass 05 is silent or cannot decode");
        File.WriteAllText("../../../InteractionReview/20261030/Grass05/unity-checks.txt",
            File.ReadAllText("LifeGameplayReview/20261026/audio-checks.txt") +
            "\nPASS selected 05 is the sole grass cue, length 0.5 seconds, decoded non-silent PCM\n");
    }
}
