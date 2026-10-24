using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace WeatherHouse.WatersideV2.Editor
{
    public static class WatersideWaterContactReview
    {
        const string MaterialPath = "Assets/Game/World/WatersideV2/Generated/Nature/Water.mat";
        const string Review = "WatersideV3Review/water-contact";
        static readonly int Shallow = Shader.PropertyToID("_ShallowColor");
        static readonly Color Candidate = new Color(.42f, .64f, .62f, 1);

        // Refresh only the V2 water palette; existing reflection and interaction property blocks survive.
        public static string Apply()
        {
            var mood = Mood();
            if (mood.Current != WatersideMood.Mode.Day)
                throw new InvalidOperationException("Restore the current capture to Day before changing its daylight water palette.");
            var cache = typeof(WatersideMood).GetField("daylight", BindingFlags.Instance | BindingFlags.NonPublic);
            if (cache == null) throw new InvalidOperationException("The existing WatersideMood daylight cache has changed; inspect its API before applying.");
            var material = Material();
            var renderers = WaterRenderers(material);
            if (renderers.Length == 0) throw new InvalidOperationException("No V2 water renderers use the expected material.");
            material.SetColor(Shallow, Candidate);
            EditorUtility.SetDirty(material);
            AssetDatabase.SaveAssetIfDirty(material);
            var block = new MaterialPropertyBlock();
            foreach (var renderer in renderers)
            {
                renderer.GetPropertyBlock(block);
                block.SetColor(Shallow, Candidate);
                renderer.SetPropertyBlock(block);
            }
            // This one-off editor trial invalidates the existing transient snapshot, not scene lighting.
            cache.SetValue(mood, null);
            mood.CaptureState();
            return Check();
        }

        // Exercises the same Apply/Restore palette path used by repeated reference captures.
        public static string Check()
        {
            var mood = Mood();
            var material = Material();
            var renderers = WaterRenderers(material);
            if (renderers.Length == 0 || !Equal(material.GetColor(Shallow), Candidate))
                throw new InvalidOperationException("The V2 shallow-color candidate has not been applied.");
            var scene = SceneManager.GetActiveScene();
            bool dirty = scene.isDirty;
            var original = mood.CaptureState();
            var block = new MaterialPropertyBlock();
            var modes = new[] { WatersideMood.Mode.Day, WatersideMood.Mode.Dawn, WatersideMood.Mode.Night,
                WatersideMood.Mode.Overcast, WatersideMood.Mode.Day, WatersideMood.Mode.Dusk, WatersideMood.Mode.Day };
            int samples = 0;
            try
            {
                foreach (var mode in modes)
                {
                    mood.Apply(mode);
                    Color tint = mode == WatersideMood.Mode.Night ? new Color(.4f, .45f, .6f) :
                        mode == WatersideMood.Mode.Overcast ? new Color(.7f, .8f, .85f) : Color.white;
                    foreach (var renderer in renderers)
                    {
                        renderer.GetPropertyBlock(block);
                        if (!block.HasColor(Shallow) || !Equal(block.GetColor(Shallow), Candidate * tint))
                            throw new InvalidOperationException("A stale water palette survived the " + mode + " capture transition: " + renderer.name);
                        samples++;
                    }
                }
            }
            finally { mood.RestoreState(original); }
            if (scene.isDirty != dirty) throw new InvalidOperationException("Water palette checks changed the scene dirty state.");
            string report = "PASS: V2 shallow-water palette and repeated capture-state transitions.\n" +
                "UTC=" + DateTime.UtcNow.ToString("o") + "\nMaterial=" + MaterialPath +
                "\nShallowColor=" + Candidate + "\nRenderers=" + renderers.Length + "\nPaletteChecks=" + samples +
                "\nSceneDirtyStateUnchanged=True\nVisualAcceptance=Pending same-camera A/B; this check does not prove image quality.\n";
            Directory.CreateDirectory(Review);
            File.WriteAllText(Review + "/palette-checks.txt", report);
            return report;
        }

        static WatersideMood Mood()
        {
            var scene = SceneManager.GetActiveScene();
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling ||
                scene.path != WatersideSceneBuilder.ScenePath)
                throw new InvalidOperationException("Run the water trial in the open Waterside V2 scene, in edit mode after compilation.");
            var mood = Object.FindObjectsOfType<WatersideMood>(true).SingleOrDefault(m => m.gameObject.scene == scene);
            if (!mood) throw new InvalidOperationException("The V2 scene's existing mood component is required.");
            return mood;
        }

        static Material Material()
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (!material || !material.HasProperty(Shallow)) throw new InvalidOperationException("V2 water material is missing.");
            return material;
        }

        static Renderer[] WaterRenderers(Material material) => Object.FindObjectsOfType<WatersideWaterSurface>(true)
            .Where(s => s.section != -2 && s.gameObject.scene == SceneManager.GetActiveScene())
            .Select(s => s.GetComponent<Renderer>()).Where(r => r && r.sharedMaterial == material).Distinct().ToArray();

        static bool Equal(Color a, Color b) => Mathf.Abs(a.r - b.r) < .0001f && Mathf.Abs(a.g - b.g) < .0001f &&
            Mathf.Abs(a.b - b.b) < .0001f && Mathf.Abs(a.a - b.a) < .0001f;
    }
}
