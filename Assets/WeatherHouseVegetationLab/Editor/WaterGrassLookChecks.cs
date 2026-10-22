using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace WeatherHouse.Vegetation.Editor
{
    [InitializeOnLoad]
    public static class WaterGrassLookChecks
    {
        const string Folder = "VegetationLabReview/WaterGrassRevision";
        const string Request = Folder + "/request.txt";
        const string Stage = "WeatherHouse.WaterGrassCheck.Stage";
        const string ReturnScene = "WeatherHouse.WaterGrassCheck.ReturnScene";
        static double next;
        static WaterGrassLookWindow window;
        static Camera camera;
        static Color32[] firstFrame;
        static double dynamicStart;
        static double playReadyAt;
        static readonly StringBuilder report = new StringBuilder();

        static WaterGrassLookChecks() { EditorApplication.update += Poll; }
        [MenuItem("Weather House/Vegetation Lab/水草画风 · 验证")]
        public static void Verify() { Run("verify"); }
        static void Poll()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.timeSinceStartup < next) return;
            next = EditorApplication.timeSinceStartup + .25;
            try
            {
                string stage = SessionState.GetString(Stage, "");
                if (stage == "play" && EditorApplication.isPlaying)
                {
                    if (playReadyAt == 0) { playReadyAt = EditorApplication.timeSinceStartup + 1; return; }
                    if (EditorApplication.timeSinceStartup < playReadyAt) return;
                    if (window == null)
                    {
                        window = WaterGrassLookWindow.Open(); window.BeginPreview();
                        WaterGrassLookWindow.ApplyIllustratedStyle(window.PreviewMaterials);
                        var motionWater = window.PreviewMaterials.Single(m => m.shader.name == "Weather House/Painterly Water");
                        // Isolate the new painted motion from existing ripples, caustics and moving reflections.
                        motionWater.SetFloat("_RippleSpeed", 0); motionWater.SetFloat("_CausticSpeed", 0);
                        motionWater.SetFloat("_ReflectionStrength", 0);
                        camera = Object.FindObjectOfType<GrassLabView>().GetComponent<Camera>();
                        var view = camera.GetComponent<GrassLabView>(); view.Shore(); view.showPanel = false;
                        Require(view.meadow.ConfigurationError == null, "Preview broke runtime meadow material ownership.");
                        view.meadow.windStrength = 0; view.meadow.ApplyMaterials();
                        camera.GetComponent<GrassLabCamera>().enabled = false;
                        view.enabled = false;
                        foreach (var m in window.PreviewMaterials.Where(m => m.shader.name.Contains("Grass"))) m.SetFloat("_WindStrength", 0);
                        dynamicStart = EditorApplication.timeSinceStartup;
                        return;
                    }
                    if (EditorApplication.timeSinceStartup - dynamicStart < 2) return;
                    if (firstFrame == null) { firstFrame = Render(camera, "play_01"); dynamicStart = EditorApplication.timeSinceStartup; return; }
                    var second = Render(camera, "play_02");
                    int changed = Difference(firstFrame, second, true);
                    Require(changed > 100, "Play animation did not change pixels: " + changed);
                    File.AppendAllText(Folder + "/checks.txt", "PASS actual Play, new painted motion only (old ripple/caustics/reflections disabled), same camera, pond center ROI, 2 seconds apart: " + changed + " changed pixels.\n");
                    window.CancelPreview(); window.Close(); window = null; firstFrame = null;
                    playReadyAt = 0;
                    SessionState.SetString(Stage, "restore");
                    EditorApplication.isPlaying = false;
                    return;
                }
                if (stage == "restore" && !EditorApplication.isPlayingOrWillChangePlaymode)
                {
                    string original = SessionState.GetString(ReturnScene, "");
                    if (!string.IsNullOrEmpty(original)) EditorSceneManager.OpenScene(original);
                    SessionState.EraseString(Stage); SessionState.EraseString(ReturnScene);
                    File.AppendAllText(Folder + "/checks.txt", "Restored original scene outside Play.\n");
                    return;
                }
                if (!File.Exists(Request) || !string.IsNullOrEmpty(stage)) return;
                string command = File.ReadAllText(Request).Trim(); File.Delete(Request);
                Run(command);
            }
            catch (Exception error)
            {
                File.AppendAllText(Folder + "/checks.txt", "FAIL " + error + "\n");
                if (window) { window.CancelPreview(); window.Close(); window = null; }
                if (EditorApplication.isPlaying) { SessionState.SetString(Stage, "restore"); EditorApplication.isPlaying = false; }
                else SessionState.EraseString(Stage);
                Debug.LogException(error);
            }
        }
        static void Run(string command)
        {
            Require(command == "baseline" || command == "verify" || command == "motion", "Unknown check command.");
            Require(!EditorApplication.isPlayingOrWillChangePlaymode && SceneManager.sceneCount == 1, "Need one saved scene outside Play.");
            var original = SceneManager.GetActiveScene();
            Require(!original.isDirty && !string.IsNullOrEmpty(original.path), "Unsaved scene was preserved; save it before testing.");
            string originalPath = original.path;
            if (command == "motion")
            {
                SessionState.SetString(ReturnScene, originalPath);
                EditorSceneManager.OpenScene(VegetationLabBuilder.ScenePath);
                SessionState.SetString(Stage, "play"); EditorApplication.isPlaying = true;
                return;
            }
            byte[] sceneBytes = File.ReadAllBytes(VegetationLabBuilder.ScenePath);
            Directory.CreateDirectory(Folder); report.Clear();
            report.AppendLine("GPU: " + SystemInfo.graphicsDeviceName + "; Unity " + Application.unityVersion);
            bool startPlay = false;
            try
            {
                EditorSceneManager.OpenScene(VegetationLabBuilder.ScenePath);
                var view = Object.FindObjectOfType<GrassLabView>();
                camera = view.GetComponent<Camera>();
                camera.GetUniversalAdditionalCameraData().requiresColorOption = CameraOverrideOption.On;
                camera.GetUniversalAdditionalCameraData().requiresDepthOption = CameraOverrideOption.On;
                view.Shore(); camera.aspect = 1.6f;
                // Unity materializes new Shader defaults on first use. Warm all review views
                // before taking the preservation snapshot, so schema initialization isn't mistaken for an edit.
                view.Overview(); Render(camera, "warmup");
                view.CloseGrass(); Render(camera, "warmup");
                view.Shore(); Render(camera, "warmup");
                var originalMaterials = Object.FindObjectsOfType<Renderer>().SelectMany(r => r.sharedMaterials).Where(m => m).Distinct().ToArray();
                var json = originalMaterials.ToDictionary(m => m, m => EditorJsonUtility.ToJson(m));
                window = WaterGrassLookWindow.Open(); window.BeginPreview();
                var materials = window.PreviewMaterials;
                Freeze(materials);
                Render(camera, command == "baseline" ? "before_shore" : "original_shore", 1200, 750);
                var water = materials.Single(m => m.shader.name == "Weather House/Painterly Water");
                var grass = materials.First(m => m.shader.name == "Weather House/Painterly Grass");
                string[] waterProperties = { "_ReflectionNear", "_ReflectionFar", "_ReflectionFalloff", "_FoamTravel", "_FoamSpeed", "_WaveletStrength", "_WaterSaturation" };
                if (command == "baseline")
                {
                    Require(waterProperties.Any(p => !water.HasProperty(p)) && !grass.HasProperty("_MeadowBlend"), "Baseline unexpectedly already has new controls.");
                    report.AppendLine("EXPECTED RED: existing water/grass shaders lack requested independent controls.");
                    view.Overview(); Render(camera, "before_overview", 1200, 750);
                    view.CloseGrass(); Render(camera, "before_grass", 1200, 750);
                }
                else
                {
                    foreach (string p in waterProperties) Require(water.HasProperty(p), "Missing water control " + p);
                    Require(grass.HasProperty("_MeadowBlend"), "Missing grass color-field control.");
                    WaterGrassLookWindow.ApplyIllustratedStyle(materials); Freeze(materials);
                    Render(camera, "after_shore", 1200, 750);
                    Compare(water, "_Opacity", .4f, 1);
                    Compare(water, "_ReflectionNear", 0, 1);
                    Compare(water, "_ReflectionFar", 0, 1);
                    Compare(water, "_FoamTravel", 0, 1);
                    Compare(water, "_WaveletStrength", 0, .8f);
                    Compare(water, "_WaterSaturation", 0, 1.4f);
                    view.Overview(); Render(camera, "after_overview", 1200, 750);
                    view.CloseGrass(); Render(camera, "after_grass", 1200, 750);
                    Compare(grass, "_MeadowBlend", 0, 1);
                    Compare(grass, "_BladeContrast", 0, 1);
                    foreach (var shader in materials.Select(m => m.shader).Distinct())
                        Require(shader.isSupported && !ShaderUtil.ShaderHasError(shader), "Shader compile failure: " + shader.name);
                    startPlay = true;
                }
                window.CancelPreview(); window.Close(); window = null;
                foreach (var entry in json)
                {
                    string after = EditorJsonUtility.ToJson(entry.Key);
                    if (after != entry.Value)
                    {
                        File.WriteAllText(Folder + "/source-before.json", entry.Value);
                        File.WriteAllText(Folder + "/source-after.json", after);
                    }
                    Require(after == entry.Value, "Preview changed source material " + entry.Key.name);
                }
                Require(File.ReadAllBytes(VegetationLabBuilder.ScenePath).SequenceEqual(sceneBytes), "Scene file changed.");
                report.AppendLine("PASS preview restored original material properties and left saved scene bytes intact.");
            }
            finally
            {
                if (window) { window.CancelPreview(); window.Close(); window = null; }
                EditorSceneManager.OpenScene(originalPath);
                File.WriteAllText(Folder + (command == "baseline" ? "/baseline.txt" : "/checks.txt"), report.ToString());
            }
            if (startPlay)
            {
                SessionState.SetString(ReturnScene, originalPath);
                EditorSceneManager.OpenScene(VegetationLabBuilder.ScenePath);
                SessionState.SetString(Stage, "play");
                EditorApplication.isPlaying = true;
            }
        }
        static void Freeze(Material[] materials)
        {
            foreach (var m in materials)
            foreach (string property in new[] { "_WindStrength", "_RippleSpeed", "_CausticSpeed", "_FoamSpeed", "_WaveletSpeed" })
                if (m.HasProperty(property)) m.SetFloat(property, 0);
        }
        static void Compare(Material material, string property, float a, float b)
        {
            float saved = material.GetFloat(property);
            try
            {
                material.SetFloat(property, a); var left = Render(camera, property + "_A");
                material.SetFloat(property, b); var right = Render(camera, property + "_B");
                int changed = Difference(left, right);
                Require(changed > 30, property + " ineffective: " + changed + " pixels.");
                report.AppendLine("PASS " + property + ": " + changed + " changed pixels / " + left.Length);
            }
            finally { material.SetFloat(property, saved); }
        }
        static int Difference(Color32[] a, Color32[] b, bool pondOnly = false)
        {
            int count = 0;
            for (int i = 0; i < a.Length; i++)
            {
                if (pondOnly && (i % 640 < 220 || i % 640 > 430 || i / 640 < 150 || i / 640 > 220)) continue;
                if (Math.Abs(a[i].r-b[i].r)+Math.Abs(a[i].g-b[i].g)+Math.Abs(a[i].b-b[i].b) >= 9) count++;
            }
            return count;
        }
        static Color32[] Render(Camera camera, string name, int width = 640, int height = 400)
        {
            var rt = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGB32);
            var old = camera.targetTexture; var active = RenderTexture.active;
            var image = new Texture2D(width, height, TextureFormat.RGB24, false);
            try
            {
                camera.targetTexture = rt; camera.Render(); RenderTexture.active = rt;
                image.ReadPixels(new Rect(0, 0, width, height), 0, 0); image.Apply();
                var pixels = image.GetPixels32();
                Require(pixels.Count(p => p.r+p.g+p.b > 12) > pixels.Length / 10, "Black render " + name);
                File.WriteAllBytes(Folder + "/" + name + ".png", image.EncodeToPNG());
                return pixels;
            }
            finally { camera.targetTexture = old; RenderTexture.active = active; RenderTexture.ReleaseTemporary(rt); Object.DestroyImmediate(image); }
        }
        static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    }
}
