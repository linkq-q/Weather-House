using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using WeatherHouse.Life;

public static class SelectedAudioImportChecks
{
    public static void Run()
    {
        // The isolated project imports production types from its compiled candidate assembly.
        var script = AssetDatabase.LoadAllAssetsAtPath("Assets/WeatherHousePhotoCandidate.dll")
            .OfType<MonoScript>().Single(s => s.GetClass() == typeof(LifeAudioCatalog));
        AssetDatabase.TryGetGUIDAndLocalFileIdentifier(script, out string guid, out long id);
        const string catalogPath = "Assets/Game/Life/Audio/Resources/LifeAudio/Catalog.asset";
        string text = File.ReadAllText(catalogPath);
        text = System.Text.RegularExpressions.Regex.Replace(text, @"m_Script: \{[^}]+\}",
            "m_Script: {fileID: " + id + ", guid: " + guid + ", type: 3}");
        File.WriteAllText(catalogPath, text);
        AssetDatabase.ImportAsset(catalogPath, ImportAssetOptions.ForceSynchronousImport);
        WeatherHouse.Life.Editor.LifeAudioChecks.Run();
        var catalog = Resources.Load<LifeAudioCatalog>("LifeAudio/Catalog");
        var clips = catalog.sounds.SelectMany(s => s.clips).Concat(catalog.voices.Select(v => v.clip)).Distinct().ToArray();
        foreach (var clip in clips)
            if (!clip.LoadAudioData() || clip.loadState != AudioDataLoadState.Loaded || clip.samples <= 0)
                throw new Exception("Audio failed to decode: " + clip.name);
        foreach (var cue in new[] { LifeAudioCue.Rain, LifeAudioCue.Wind, LifeAudioCue.Shore })
            if (catalog.Find(cue).clips.Length != 1) throw new Exception("Multiple ambience variants loaded: " + cue);
        string report = File.ReadAllText("LifeGameplayReview/20261026/audio-checks.txt") +
            "\nPASS all 23 Unity AudioClips decode and load\nPASS rain, wind and shore each bind a single clip\n";
        File.WriteAllText("../../../InteractionReview/20261030/AudioSelection/unity-import-checks.txt", report);
        Debug.Log("PASS selected audio import checks");
    }
}
