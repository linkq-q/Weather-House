using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ForestSpriteCharacter.Editor
{
    // Explicit, scene-local parameter application. Scene/prefab saving belongs to the caller.
    public static class ForestSpriteCapeMotionReview
    {
        public const float WaveAmplitude = .02f, WingSpread = .95f;
        const string BaselineKey = "ForestSprite.CapeMotionReview.baseline";
        const string Folder = "WatersideV3Review/cape-motion-review";
        [Serializable] sealed class Entry
        {
            public int instanceId;
            public string name, settingsPath, before, expected;
        }
        [Serializable] sealed class Baseline
        {
            public string scene;
            public bool playing;
            public List<Entry> entries = new List<Entry>();
        }
        [Serializable] sealed class Inspection
        {
            public string scene;
            public bool playing;
            public List<Item> capes = new List<Item>();
        }
        [Serializable] sealed class Item
        {
            public string name, settingsPath, persistedSettings;
            public ForestSpriteCapeCloth.Settings settings;
            public bool rendererBoundsFinite, meshBoundsFinite, usesGuides;
            public Bounds rendererBounds, meshBounds;
        }

        internal static ForestSpriteCapeCloth[] CurrentCapes()
        {
            var scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || !scene.isLoaded) throw new InvalidOperationException("No active loaded scene.");
            var capes = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<ForestSpriteCapeCloth>(true)).ToArray();
            if (capes.Length == 0) throw new InvalidOperationException("Active scene contains no ForestSprite cape.");
            return capes;
        }

        public static string Inspect()
        {
            var inspection = new Inspection { scene = SceneManager.GetActiveScene().path, playing = Application.isPlaying };
            foreach (var cape in CurrentCapes())
            {
                var renderer = cape.GetComponent<SkinnedMeshRenderer>();
                inspection.capes.Add(new Item { name = cape.name, settings = cape.settings, settingsPath = cape.SettingsPath,
                    persistedSettings = File.Exists(cape.SettingsPath) ? File.ReadAllText(cape.SettingsPath) : "<missing>", usesGuides = cape.UsesGuides,
                    rendererBoundsFinite = renderer && Finite(renderer.localBounds), meshBoundsFinite = renderer && renderer.sharedMesh && Finite(renderer.sharedMesh.bounds),
                    rendererBounds = renderer ? renderer.localBounds : default, meshBounds = renderer && renderer.sharedMesh ? renderer.sharedMesh.bounds : default });
            }
            string json = JsonUtility.ToJson(inspection, true);
            Directory.CreateDirectory(Folder); File.WriteAllText(Folder + "/inspection.json", json);
            return json;
        }

        public static string Apply()
        {
            var capes = CurrentCapes();
            var baseline = new Baseline { scene = SceneManager.GetActiveScene().path, playing = Application.isPlaying };
            foreach (var cape in capes)
            {
                if (cape.settings == null) throw new InvalidOperationException("Missing cape settings; no changes applied.");
                string before = JsonUtility.ToJson(cape.settings);
                var expected = JsonUtility.FromJson<ForestSpriteCapeCloth.Settings>(before);
                expected.waveAmplitude = WaveAmplitude; expected.wingSpread = WingSpread;
                string expectedJson = JsonUtility.ToJson(expected); expected.Clamp();
                if (expectedJson != JsonUtility.ToJson(expected))
                    throw new InvalidOperationException("An unrelated setting would be clamped; inspect it before applying.");
                baseline.entries.Add(new Entry { instanceId = cape.GetInstanceID(), name = cape.name,
                    settingsPath = cape.SettingsPath, before = before, expected = expectedJson });
            }
            SessionState.SetString(BaselineKey, JsonUtility.ToJson(baseline));
            Directory.CreateDirectory(Folder); File.WriteAllText(Folder + "/apply-baseline.json", JsonUtility.ToJson(baseline, true));
            foreach (var cape in capes)
            {
                if (!Application.isPlaying) Undo.RecordObject(cape, "Adjust cape vertical and lateral motion");
                cape.settings.waveAmplitude = WaveAmplitude; cape.settings.wingSpread = WingSpread;
                cape.ApplySettings();
                if (!Application.isPlaying)
                {
                    PrefabUtility.RecordPrefabInstancePropertyModifications(cape); EditorUtility.SetDirty(cape);
                    EditorSceneManager.MarkSceneDirty(cape.gameObject.scene);
                }
            }
            return Check();
        }

        public static string Check()
        {
            string json = SessionState.GetString(BaselineKey, "");
            if (string.IsNullOrEmpty(json)) throw new InvalidOperationException("Apply first to establish the preservation baseline.");
            var baseline = JsonUtility.FromJson<Baseline>(json);
            if (baseline.scene != SceneManager.GetActiveScene().path || baseline.playing != Application.isPlaying)
                throw new InvalidOperationException("Baseline belongs to a different scene or Play session; Apply again.");
            var capes = CurrentCapes();
            if (capes.Length != baseline.entries.Count) throw new InvalidOperationException("Cape instance count changed.");
            foreach (var entry in baseline.entries)
            {
                var cape = capes.SingleOrDefault(c => c.GetInstanceID() == entry.instanceId);
                if (!cape || JsonUtility.ToJson(cape.settings) != entry.expected)
                    throw new InvalidOperationException("Cape settings differ from the two-field-only expectation: " + entry.name);
            }
            string result = "PASS: " + capes.Length + " cape(s), waveAmplitude=" + WaveAmplitude + ", wingSpread=" + WingSpread + "; all other settings unchanged. Scene and prefab are not saved.";
            File.WriteAllText(Folder + "/apply-check.txt", result); return result;
        }

        // Caller backs up Inspect's SettingsPath before invoking this explicit persistence operation.
        public static string SavePersisted()
        {
            Check();
            var groups = CurrentCapes().GroupBy(c => c.SettingsPath).ToArray();
            foreach (var group in groups)
            {
                if (group.Select(c => JsonUtility.ToJson(c.settings)).Distinct().Count() != 1)
                    throw new InvalidOperationException("Capes sharing a profile have different settings: " + group.Key);
                if (!Application.isPlaying && File.Exists(group.Key))
                {
                    var saved = ForestSpriteCapeCloth.ReadSettings(File.ReadAllText(group.Key));
                    saved.waveAmplitude = WaveAmplitude; saved.wingSpread = WingSpread;
                    if (JsonUtility.ToJson(saved) != JsonUtility.ToJson(group.First().settings))
                        throw new InvalidOperationException("Saved profile has different unrelated settings. Apply and save after Start loads it in Play; no profile written.");
                }
            }
            foreach (var group in groups)
            {
                var cape = group.First(); cape.SaveSettings();
                string actual = JsonUtility.ToJson(ForestSpriteCapeCloth.ReadSettings(File.ReadAllText(group.Key)));
                if (actual != JsonUtility.ToJson(cape.settings)) throw new IOException("Saved profile verification failed: " + group.Key);
            }
            return "PASS: saved through ForestSpriteCapeCloth.SaveSettings(): " + string.Join("; ", groups.Select(g => g.Key));
        }

        public static string RepairNonFiniteBounds()
        {
            int count = 0;
            foreach (var cape in CurrentCapes())
            {
                var renderer = cape.GetComponent<SkinnedMeshRenderer>();
                if (!renderer || Finite(renderer.localBounds)) continue;
                if (!Application.isPlaying) Undo.RecordObject(renderer, "Restore invalid cape render bounds");
                RestoreKnownBounds(renderer); count++;
                if (!Application.isPlaying)
                {
                    PrefabUtility.RecordPrefabInstancePropertyModifications(renderer); EditorUtility.SetDirty(renderer);
                    EditorSceneManager.MarkSceneDirty(renderer.gameObject.scene);
                }
            }
            return "Repaired " + count + " non-finite cape renderer bounds; scene/prefab not saved.";
        }

        internal static void RestoreKnownBounds(SkinnedMeshRenderer renderer)
        {
            // Last finite bounds in this prefab's tracked baseline, before its isolated NaN diff.
            renderer.localBounds = new Bounds(new Vector3(.015848666f, .44542745f, -.16785541f),
                new Vector3(1.1278195f, .967198f, .91327393f) * 2f);
        }
        internal static bool Finite(Bounds bounds) => Finite(bounds.center) && Finite(bounds.size);
        internal static bool Finite(Vector3 v) => float.IsFinite(v.x) && float.IsFinite(v.y) && float.IsFinite(v.z);
    }
}
