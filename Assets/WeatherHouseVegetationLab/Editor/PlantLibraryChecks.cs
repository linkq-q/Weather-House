using System;
using System.Collections;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace WeatherHouse.Vegetation.Editor
{
    public static class PlantLibraryChecks
    {
        const string Output = "VegetationLabReview";
        const string Running = "WeatherHouse.PlantLibrary.Checking";
        const string ReturnScene = "WeatherHouse.PlantLibrary.ReturnScene";
        static IEnumerator steps;
        static double started;
        static string errors;

        [MenuItem("Weather House/Vegetation Lab/植物资源库 · 验证并截图")]
        public static void BuildAndVerify()
        {
            var scene = SceneManager.GetActiveScene();
            if (EditorApplication.isPlaying || scene.isDirty || SceneManager.sceneCount != 1)
                throw new InvalidOperationException("请先保存当前单一场景并退出 Play，检查后会恢复它。");
            Directory.CreateDirectory(Output);
            File.WriteAllText(Output + "/plant-library-result.txt", "RUNNING\n");
            SessionState.SetString(ReturnScene, scene.path);
            try
            {
                PlantLibraryBuilder.OpenOrCreate();
                VerifyEditor();
                SessionState.SetBool(Running, true);
                Register();
                EditorApplication.isPlaying = true;
            }
            catch (Exception error) { Finish(error); }
        }

        public static void VerifyEditor()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<PlantLibraryCatalog>(PlantLibraryBuilder.CatalogPath);
            Assert(catalog && catalog.entries.Length == 19, "19 reference-mapped presets");
            Assert(catalog.entries.Select(e => e.species).Distinct().Count() == 19, "19 independently editable definitions");
            GrassShapeChecks.Run();
            GrassShapeChecks.VerifyReferenceGrassShapes(catalog.entries.Take(4).Select(e => e.species).ToArray());
            var report = new System.Text.StringBuilder("PASS four structurally distinct grass tiers; original grass shape checks\n");
            foreach (var entry in catalog.entries)
            {
                Assert(entry.variants.Length == 4, entry.id + " has four variants");
                for (int v = 0; v < 4; v++)
                {
                    var prefab = entry.variants[v]; Assert(prefab, entry.id + " saved prefab");
                    var specimen = prefab.GetComponent<PlantSpecimen>();
                    Assert(specimen.source == entry.species && specimen.variation == v, entry.id + " source and variation references");
                    var mesh = prefab.GetComponent<MeshFilter>().sharedMesh;
                    Assert(mesh && AssetDatabase.Contains(mesh), entry.id + " persistent shared mesh");
                    Vector3[] points = mesh.vertices; int[] triangles = mesh.triangles;
                    Assert(points.Length > 20 && triangles.Length > 20 && triangles.Length / 3 < 15000, entry.id + " modeled geometry within budget");
                    Assert(points.All(p => Finite(p.x) && Finite(p.y) && Finite(p.z)), entry.id + " finite vertices");
                    Assert(mesh.colors.Length == points.Length && mesh.uv2.Length == points.Length && mesh.normals.Length == points.Length, entry.id + " render channels");
                    for (int k = 0; k < triangles.Length; k += 3)
                        Assert(Vector3.Cross(points[triangles[k + 1]] - points[triangles[k]], points[triangles[k + 2]] - points[triangles[k]]).sqrMagnitude > 1e-18f, entry.id + " nondegenerate triangles");
                    var repeat = GrassMeshFactory.Build(entry.species, v);
                    try { Assert(points.SequenceEqual(repeat.vertices) && triangles.SequenceEqual(repeat.triangles), entry.id + " deterministic saved mesh"); }
                    finally { Object.DestroyImmediate(repeat); }
                    Assert(prefab.GetComponent<MeshRenderer>().sharedMaterial.shader.name == "Weather House/Painterly Grass", entry.id + " painterly grass shader");
                    report.AppendLine(entry.id + " v" + (v + 1) + " triangles=" + triangles.Length / 3 + " height=" + mesh.bounds.size.y.ToString("F3") + "m");
                }
            }
            var meadow = Object.FindObjectOfType<GrassMeadow>();
            Assert(meadow && meadow.ConfigurationError == null && meadow.SpeciesCount == 19, "19 runtime controls wired");
            Assert(meadow.Counts.Sum() == meadow.totalClumps && meadow.Counts.All(n => n > 0), "all 19 present in saved mixed field");
            Assert(Object.FindObjectOfType<GrassLabView>().Navigation.libraryLayout, "library camera presets wired");
            Assert(meadow.GetComponentsInChildren<MeshFilter>().All(f => f.sharedMesh && AssetDatabase.Contains(f.sharedMesh)), "mixed field persists outside Play");
            var shader = Shader.Find("Weather House/Painterly Grass");
            Assert(!ShaderUtil.GetShaderMessages(shader).Any(m => m.severity == UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error), "grass shader compiles");
            File.WriteAllText(Output + "/plant-library-editor-checks.txt", report.ToString());
        }

        [InitializeOnLoadMethod]
        static void Resume() { if (SessionState.GetBool(Running, false) || !string.IsNullOrEmpty(SessionState.GetString(ReturnScene, ""))) Register(); }
        static void Register()
        {
            EditorApplication.playModeStateChanged -= Changed; EditorApplication.playModeStateChanged += Changed;
            Application.logMessageReceived -= Log; Application.logMessageReceived += Log;
        }
        static void Changed(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Running, false))
            {
                errors = ""; started = EditorApplication.timeSinceStartup; steps = Run();
                EditorApplication.update -= Tick; EditorApplication.update += Tick;
            }
            if (state == PlayModeStateChange.EnteredEditMode) Restore();
        }
        static void Tick()
        {
            if (EditorApplication.timeSinceStartup - started < 3) return;
            try
            {
                if (EditorApplication.timeSinceStartup - started > 240) throw new Exception("Plant library Play verification timed out.");
                if (!steps.MoveNext()) Finish(string.IsNullOrEmpty(errors) ? null : new Exception(errors));
            }
            catch (Exception error) { Finish(error); }
        }
        static IEnumerator Run()
        {
            var view = Object.FindObjectOfType<GrassLabView>(); var camera = view.GetComponent<Camera>(); var nav = view.Navigation;
            nav.Overview(); Capture("Plants_Overview", camera, 1600, 1000);
            ScreenCapture.CaptureScreenshot(Output + "/Plants_Controls.png");
            yield return null;
            nav.CloseGrass(); Capture("Plants_FourGrass", camera, 1600, 900);
            nav.Shore(); Capture("Plants_Accents", camera, 1600, 1000);
            var catalog = AssetDatabase.LoadAssetAtPath<PlantLibraryCatalog>(PlantLibraryBuilder.CatalogPath);
            nav.enabled = false;
            for (int i = 0; i < 19; i++)
            {
                float x = -19 + (i % 4) * 3.7f, z = -5.5f + (i / 4) * 3;
                var target = new Vector3(x, 3.1f + catalog.entries[i].species.height * .35f, z);
                camera.transform.position = target + new Vector3(1.5f, 1.8f, -3.2f);
                camera.transform.LookAt(target); Capture("Plant_" + catalog.entries[i].id, camera, 960, 720);
                yield return null;
            }
            camera.transform.position = new Vector3(5.8f, 7.2f, -7.5f); camera.transform.LookAt(new Vector3(0, 3, 0));
            Capture("Plants_Mixed", camera, 1600, 1000);
            nav.enabled = true; nav.Overview();
            IEnumerator check = PlantLibraryLiveChecks.Verify(view.meadow);
            try { while (check.MoveNext()) yield return null; }
            finally { (check as IDisposable)?.Dispose(); }

            var legacy = new GameObject("Legacy three-species check source"); legacy.SetActive(false);
            var legacyMeadow = legacy.AddComponent<GrassMeadow>();
            string root = VegetationLabBuilder.Generated + "/";
            legacyMeadow.species = new[] { "ShortGrass", "TallGrass", "WhiteDaisy" }.Select(name => AssetDatabase.LoadAssetAtPath<GrassSpecies>(root + name + ".asset")).ToArray();
            legacyMeadow.materials = new[] { "ShortGrass", "TallGrass", "WhiteDaisy" }.Select(name => AssetDatabase.LoadAssetAtPath<Material>(root + name + ".mat")).ToArray();
            try
            {
                check = GrassLiveChecks.Verify(legacyMeadow);
                while (check.MoveNext()) yield return null;
            }
            finally { (check as IDisposable)?.Dispose(); Object.Destroy(legacy); }
            nav.SetMode(GrassCameraMode.Fly); var before = nav.transform.position;
            nav.StepNavigation(Vector3.up, Vector2.zero, false, .2f);
            Assert(nav.transform.position.y > before.y, "library fly ascends");
            nav.CloseGrass();
        }
        static void Capture(string name, Camera camera, int width, int height)
        {
            var target = RenderTexture.GetTemporary(width, height, 24); var previous = camera.targetTexture; var active = RenderTexture.active;
            var texture = new Texture2D(width, height, TextureFormat.RGB24, false);
            try { camera.targetTexture = target; camera.Render(); RenderTexture.active = target; texture.ReadPixels(new Rect(0, 0, width, height), 0, 0); texture.Apply(); File.WriteAllBytes(Output + "/" + name + ".png", texture.EncodeToPNG()); }
            finally { camera.targetTexture = previous; RenderTexture.active = active; RenderTexture.ReleaseTemporary(target); Object.DestroyImmediate(texture); }
        }
        static void Finish(Exception error)
        {
            (steps as IDisposable)?.Dispose(); steps = null;
            EditorApplication.update -= Tick; SessionState.SetBool(Running, false);
            Application.logMessageReceived -= Log;
            File.WriteAllText(Output + "/plant-library-result.txt", error == null ? "PASS: editor assets, 76 prefab variants, GPU captures, 19-slot live controls, legacy three-slot live controls, fly camera.\n" : "FAIL: " + error + "\n");
            if (EditorApplication.isPlaying) EditorApplication.isPlaying = false; else Restore();
        }
        static void Restore()
        {
            string path = SessionState.GetString(ReturnScene, ""); SessionState.EraseString(ReturnScene);
            if (!string.IsNullOrEmpty(path) && File.Exists(path) && SceneManager.GetActiveScene().path != path) EditorSceneManager.OpenScene(path);
        }
        static void Log(string message, string stack, LogType type) { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors += message + "\n"; }
        static bool Finite(float value) { return !float.IsNaN(value) && !float.IsInfinity(value); }
        static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException("Plant library: " + message); }
    }
}
