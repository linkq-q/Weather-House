using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace WeatherHouse.Life.Editor
{
    public static class LifeAudioChecks
    {
        [MenuItem("Weather House/Life/Validate Audio")]
        public static void Run()
        {
            var lines = new List<string>();
            void Check(bool ok, string label) { if (!ok) throw new Exception(label); lines.Add("PASS " + label); }
            LifeAudioCatalog catalog = Resources.Load<LifeAudioCatalog>("LifeAudio/Catalog");
            Check(catalog, "Runtime catalog imports");
            Check(catalog.sounds.Length == Enum.GetValues(typeof(LifeAudioCue)).Length && catalog.sounds.Select(s => s.cue).Distinct().Count() == catalog.sounds.Length, "One binding per semantic cue");
            Check(catalog.sounds.All(s => s.clips.Length > 0 && s.clips.All(c => c && c.length > 0)), "All effects have playable imported clips");
            foreach (LifeAudioCue cue in new[] { LifeAudioCue.GrassStep, LifeAudioCue.DirtStep, LifeAudioCue.StoneStep })
                Check(catalog.Find(cue).clips.Length == 1, cue + " uses the selected single take");
            Check(catalog.Find(LifeAudioCue.Water).clips.Length == 2 && catalog.Find(LifeAudioCue.Water).clips.Distinct().Count() == 2, "Water keeps two selected short variations");
            Check(catalog.voices.Length == 5 && new[] { "dog", "squirrel", "fox", "sheep", "deer" }.All(id => catalog.VoiceFor(id)), "Five species use authored syllable voices");
            Check(catalog.sounds.SelectMany(s => s.clips).Concat(catalog.voices.Select(v => v.clip)).Distinct().Count() == 23, "23 distinct final clips");
            Check(catalog.Surface((Material)null) == LifeAudioSurface.Unknown && catalog.Surface((TerrainLayer)null) == LifeAudioSurface.Unknown, "Unknown surface is not guessed as dirt");
            Check(catalog.terrainSurfaces.Any(s => s.surface == LifeAudioSurface.Grass) && catalog.terrainSurfaces.Any(s => s.surface == LifeAudioSurface.Dirt) && catalog.terrainSurfaces.Any(s => s.surface == LifeAudioSurface.Stone), "Actual terrain has grass dirt stone reference mappings");
            Check(catalog.materialSurfaces.Length > 0 && catalog.materialSurfaces.All(s => s.material), "Actual mesh materials have explicit reference mappings");
            var data = new TerrainData { heightmapResolution = 33, alphamapResolution = 16, size = new Vector3(16, 1, 16) };
            var layers = new[] { new TerrainLayer(), new TerrainLayer(), new TerrainLayer() };
            var fixture = ScriptableObject.CreateInstance<LifeAudioCatalog>();
            try
            {
                data.terrainLayers = layers;
                fixture.terrainSurfaces = layers.Select((layer, i) => new LifeAudioCatalog.TerrainSurface { layer = layer, surface = (LifeAudioSurface)(i + 1) }).ToArray();
                var alpha = new float[16, 16, 3];
                for (int z = 0; z < 16; z++) for (int x = 0; x < 16; x++) alpha[z, x, x < 5 ? 0 : x < 10 ? 1 : 2] = 1;
                data.SetAlphamaps(0, 0, alpha);
                Check(LifeAudioGround.TerrainAt(data, Vector3.zero, new Vector3(2, 0, 8), fixture) == LifeAudioSurface.Grass, "Grass alphamap patch selects grass");
                Check(LifeAudioGround.TerrainAt(data, Vector3.zero, new Vector3(7, 0, 8), fixture) == LifeAudioSurface.Dirt, "Dirt alphamap patch selects dirt");
                Check(LifeAudioGround.TerrainAt(data, Vector3.zero, new Vector3(13, 0, 8), fixture) == LifeAudioSurface.Stone, "Stone alphamap patch selects stone");
                fixture.terrainSurfaces = Array.Empty<LifeAudioCatalog.TerrainSurface>();
                Check(LifeAudioGround.TerrainAt(data, Vector3.zero, new Vector3(7, 0, 8), fixture) == LifeAudioSurface.Unknown, "Unregistered actual layer stays unknown");
            }
            finally
            {
                Object.DestroyImmediate(data); Object.DestroyImmediate(fixture);
                foreach (TerrainLayer layer in layers) Object.DestroyImmediate(layer);
            }
            Directory.CreateDirectory("LifeGameplayReview/20261026");
            File.WriteAllLines("LifeGameplayReview/20261026/audio-checks.txt", lines);
        }
        public static string Runtime()
        {
            LifeAudio audio = Object.FindObjectOfType<LifeAudio>();
            if (!Application.isPlaying || !audio || !audio.isActiveAndEnabled || !audio.Catalog) throw new Exception("Audio runtime is not ready");
            AudioSource[] sources = audio.GetComponentsInChildren<AudioSource>();
            if (sources.Length != LifeAudio.SourceBudget) throw new Exception("AudioSource budget changed: " + sources.Length);
            if (Object.FindObjectsOfType<AudioListener>().Count(l => l.enabled && l.gameObject.activeInHierarchy) != 1) throw new Exception("Expected the existing unique active listener");
            int before = audio.PlayedCount;
            LifeAudio.PlayAt(LifeAudioCue.Drink, audio.transform.position); LifeAudio.PlayAt(LifeAudioCue.Drink, audio.transform.position);
            if (audio.PlayedCount != before + 1) throw new Exception("Repeated same-event sound is not throttled");
            string result = "PASS runtime active; 12 fixed sources; existing single listener; repeated event coalesced; played=" + audio.PlayedCount;
            File.WriteAllText("LifeGameplayReview/20261026/audio-runtime-checks.txt", result); return result;
        }
    }
}
