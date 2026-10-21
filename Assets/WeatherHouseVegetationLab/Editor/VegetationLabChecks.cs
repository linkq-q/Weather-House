using System;
using System.Collections;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace WeatherHouse.Vegetation.Editor
{
    public static class VegetationLabChecks
    {
        const string Output = "VegetationLabReview";
        static double startTime;
        static int phase;
        static string errors = "";
        static IEnumerator liveChecks;
        const string RunningKey = "WeatherHouse.VegetationLab.Checking";
        const string PhaseKey = "WeatherHouse.VegetationLab.CheckPhase";

        public static void BuildAndVerify()
        {
            Directory.CreateDirectory(Output);
            if (File.Exists(Output + "/FAILED.txt")) File.Delete(Output + "/FAILED.txt");
            try
            {
                VegetationLabBuilder.Build();
                var meadow = Object.FindObjectOfType<GrassMeadow>();
                var view = Object.FindObjectOfType<GrassLabView>();
                view.Navigation.meadow = meadow;
                view.gameObject.name = "06 主相机 · 环绕 / 行走 / 飞行";
                meadow.Rebuild();
                EditorSceneManager.SaveScene(meadow.gameObject.scene);
                VerifyEditor();
                errors = "";
                SessionState.SetBool(RunningKey, true);
                SessionState.SetInt(PhaseKey, 0);
                Application.logMessageReceived -= Log;
                Application.logMessageReceived += Log;
                EditorApplication.playModeStateChanged -= PlayChanged;
                EditorApplication.playModeStateChanged += PlayChanged;
                EditorApplication.isPlaying = true;
            }
            catch (Exception ex) { Fail(ex); }
        }

        [MenuItem("Weather House/Vegetation Lab/Run Checks")]
        public static void VerifyEditor()
        {
            Assert(GrassMeadow.Allocate(100, new float[] { 70, 24, 6 }).SequenceEqual(new[] { 70, 24, 6 }), "70/24/6 counts");
            Assert(GrassMeadow.Allocate(10, new float[] { 1, 1, 1 }).Sum() == 10, "Rounding conserves total");
            Assert(GrassMeadow.Allocate(100, new float[] { 0, 1, 0 }).SequenceEqual(new[] { 0, 100, 0 }), "Single species only");
            Assert(GrassMeadow.Allocate(100, new float[] { 0, 0, 0 }).Sum() == 0, "Zero weights empty");
            Assert(GrassMeadow.Allocate(0, new float[] { 70, 24, 6 }).Sum() == 0, "Zero density empty");
            foreach (string name in new[] { "Weather House/Painterly Grass", "Weather House/Painterly Water", "Weather House/Painterly Ground", "Weather House/Painterly Tree" })
            {
                Shader shader = Shader.Find(name);
                Assert(shader != null, "Shader exists " + name);
                var messages = ShaderUtil.GetShaderMessages(shader);
                Assert(!messages.Any(m => m.severity == UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error), "Shader compiler: " + string.Join("; ", messages.Select(m => m.message)));
            }
            var meadow = Object.FindObjectOfType<GrassMeadow>();
            Assert(meadow != null && meadow.species.Length == 3, "Three editable species exist");
            GrassShapeChecks.Run();
            PainterlyTreeChecks.Run();
            GrassCameraChecks.Verify(Object.FindObjectOfType<GrassLabView>().Navigation);
            foreach (var species in meadow.species)
            {
                Mesh a = GrassMeshFactory.Build(species, 0), b = GrassMeshFactory.Build(species, 0);
                Assert(a.vertexCount > 20 && a.triangles.Length > 20, "Real modeled plant geometry");
                Assert(a.vertices.SequenceEqual(b.vertices), "Seed reproducibility");
                Assert(a.normals.Length == a.vertexCount && a.normals.Any(n => Mathf.Abs(n.y) < .9f), "Geometry normals available for A/B");
                Assert(a.uv2.Length == a.vertexCount && a.colors.Length == a.vertexCount, "Wind/color vertex channels");
                if (species.kind == GrassKind.Daisy)
                    Assert(a.colors.Any(c => c.a > .99f && c.r > .9f && c.g > .9f && c.b > .7f), "White daisy petals");
                var editable = Object.Instantiate(species);
                editable.height *= 2;
                Mesh taller = GrassMeshFactory.Build(editable, 0);
                Assert(taller.vertices.Max(v => v.y) > a.vertices.Max(v => v.y) * 1.7f, "Editable height changes mesh");
                Object.DestroyImmediate(a); Object.DestroyImmediate(b); Object.DestroyImmediate(taller); Object.DestroyImmediate(editable);
            }
            Assert(meadow.Counts.Sum() == meadow.totalClumps, "Saved scene population");
            Assert(meadow.materials.All(m => m.GetFloat("_UpNormals") == 1), "Up-facing shading enabled by default");
            Assert(meadow.GetComponentsInChildren<MeshFilter>().All(f => f.sharedMesh != null), "Mesh references persist");
            Assert(meadow.GetGroundHeight(meadow.pondCenter.x, meadow.pondCenter.y) < meadow.waterLevel, "Pond basin under water");
            Assert(!meadow.IsPlantable(meadow.pondCenter), "No grass under water");
            File.WriteAllText(Output + "/editor-checks.txt", "PASS: exact ratios; rounding; single species; all-zero; zero density; 4 shaders; deterministic grass and tree geometry; tree parameter checks; upward normals; wind channels; white petals; editable heights; saved meshes; shoreline exclusion.\n");
            Debug.Log("Vegetation lab editor checks PASS");
        }

        static void PlayChanged(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.EnteredEditMode) RestorePreviousScene();
            if (!SessionState.GetBool(RunningKey, false)) return;
            if (change == PlayModeStateChange.EnteredPlayMode)
            {
                phase = 0; startTime = EditorApplication.timeSinceStartup;
                EditorApplication.update += Tick;
            }
        }

        static void RestorePreviousScene()
        {
            string path = SessionState.GetString("WeatherHouse.VegetationLab.ReturnScene", "");
            SessionState.EraseString("WeatherHouse.VegetationLab.ReturnScene");
            if (!string.IsNullOrEmpty(path) && File.Exists(path)) EditorSceneManager.OpenScene(path);
        }

        [InitializeOnLoadMethod]
        static void ResumeAfterReload()
        {
            // Batch play testing survives the domain reload caused by entering Play.
            if (SessionState.GetBool(RunningKey, false) || !string.IsNullOrEmpty(SessionState.GetString("WeatherHouse.VegetationLab.ReturnScene", "")) || (Application.isBatchMode && Environment.GetCommandLineArgs().Any(arg => arg.EndsWith("VegetationLabChecks.BuildAndVerify") || arg.EndsWith("VegetationLabPalette.ApplyAndVerify"))))
            {
                EditorApplication.playModeStateChanged -= PlayChanged;
                EditorApplication.playModeStateChanged += PlayChanged;
                Application.logMessageReceived -= Log;
                Application.logMessageReceived += Log;
                if (EditorApplication.isPlaying && SessionState.GetBool(RunningKey, false))
                {
                    phase = SessionState.GetInt(PhaseKey, 0);
                    startTime = EditorApplication.timeSinceStartup;
                    EditorApplication.update -= Tick;
                    EditorApplication.update += Tick;
                }
            }
        }
        static void Tick()
        {
            if (phase != 12 && EditorApplication.timeSinceStartup - startTime < 3) return;
            startTime = EditorApplication.timeSinceStartup;
            try
            {
                var meadow = Object.FindObjectOfType<GrassMeadow>();
                var view = Object.FindObjectOfType<GrassLabView>();
                if (phase == 0)
                {
                    Assert(Application.isPlaying, "Play mode active");
                    RestoreDepthMaterials(meadow);
                    GrassCameraChecks.Verify(view.Navigation);
                    int savedTotal=meadow.totalClumps; float[] savedWeights=(float[])meadow.weights.Clone();
                    meadow.totalClumps = 120; meadow.weights = new float[] { 0, 1, 0 }; meadow.Rebuild(false);
                    Assert(meadow.Counts.SequenceEqual(new[] { 0, 120, 0 }), "Runtime single-species control");
                    meadow.weights = new float[] { 0, 0, 0 }; meadow.Rebuild(false);
                    Assert(meadow.Counts.Sum() == 0 && meadow.transform.Find("Generated Grass").childCount == 0, "Runtime clearing");
                    meadow.totalClumps = savedTotal; meadow.weights = savedWeights; meadow.Rebuild(false);
                    Assert(meadow.Counts.SequenceEqual(GrassMeadow.Allocate(savedTotal,savedWeights)), "Runtime restore exact ratios");
                    view.Overview(); view.showPanel = false;
                }
                else if (phase == 1) { Capture("01_Overview", view.GetComponent<Camera>()); view.CloseGrass(); }
                else if (phase == 2) { Capture("02_Grass_Close", view.GetComponent<Camera>()); view.Shore(); }
                else if (phase == 3) { Capture("03_Shore", view.GetComponent<Camera>()); }
                else if (phase == 4)
                {
                    Assert(meadow.transform.Cast<Transform>().Count(t => t.name == "Generated Grass") == 1, "Rebuild does not accumulate objects");
                    Capture("04_Wind_Later", view.GetComponent<Camera>());
                    view.CloseGrass(); meadow.windSpeed=0; meadow.upNormals=0; meadow.ApplyMaterials();
                }
                else if (phase == 5)
                {
                    Capture("05_Geometry_Normals", view.GetComponent<Camera>());
                    meadow.upNormals=1; meadow.ApplyMaterials();
                }
                else if (phase == 6)
                {
                    Capture("06_Upward_Normals", view.GetComponent<Camera>());
                    view.Navigation.SetMode(GrassCameraMode.Walk);
                }
                else if (phase == 7)
                {
                    Capture("08_First_Person", view.GetComponent<Camera>());
                    view.Navigation.SetMode(GrassCameraMode.Fly);
                    view.Navigation.StepNavigation(Vector3.up, Vector2.zero, false, 1);
                }
                else if (phase == 8)
                {
                    Capture("09_Fly", view.GetComponent<Camera>());
                    view.Shore();
                    SetWaterDepthDebug(meadow, false);
                }
                else if (phase == 9)
                {
                    Capture("10_Depth_Before", view.GetComponent<Camera>());
                    SetWaterDepthDebug(meadow, true);
                }
                else if (phase == 10)
                {
                    Capture("11_Depth_After", view.GetComponent<Camera>());
                    RestoreDepthMaterials(meadow);
                }
                else if (phase == 11)
                {
                    Capture("12_Water_Fixed", view.GetComponent<Camera>());
                    WaterStyleChecks.Verify(view.GetComponent<Camera>());
                }
                else if (phase == 12)
                {
                    if (liveChecks == null) liveChecks = GrassLiveChecks.Verify(meadow);
                    if (liveChecks.MoveNext()) return;
                    (liveChecks as IDisposable)?.Dispose(); liveChecks = null;
                    view.Overview(); view.showPanel = true;
                }
                else if (phase == 13)
                {
                    ScreenCapture.CaptureScreenshot(Output + "/18_Live_Controls.png");
                }
                else if (phase == 14)
                {
                    var tree = Object.FindObjectsOfType<PainterlyTree>().OrderByDescending(t => t.height).FirstOrDefault();
                    if (tree)
                    {
                        view.showPanel = false;
                        var nav = view.Navigation;
                        nav.mode = GrassCameraMode.Orbit;
                        nav.target = tree.transform.position + Vector3.up * tree.height * .48f;
                        nav.yaw = 22; nav.pitch = 10; nav.distance = tree.height * 1.7f;
                        nav.PlaceCamera();
                        Capture("19_Tree_Sample", view.GetComponent<Camera>());
                    }
                    Assert(errors.Length == 0, "Runtime errors: " + errors);
                    File.WriteAllText(Output + "/runtime-checks.txt", "PASS: actual Play mode; allocation/clearing; restore saved ratios; no accumulating generated roots; editable grass geometry; walking/flying/shore exclusion/mode transitions; automatic live grass updates; GPU A/B for opacity, shore, surface strokes, caustics and reflection brush; no logged runtime errors. See live-grass-checks.txt and water-style-checks.txt for details.\nGPU: " + SystemInfo.graphicsDeviceName + "\n");
                    EditorApplication.update -= Tick;
                    SessionState.SetBool(RunningKey, false);
                    Application.logMessageReceived -= Log;
                    if (Application.isBatchMode) EditorApplication.Exit(0);
                    else EditorApplication.isPlaying = false;
                }
                phase++;
                SessionState.SetInt(PhaseKey, phase);
            }
            catch (Exception ex) { Fail(ex); }
        }

        static Material Water { get { return AssetDatabase.LoadAssetAtPath<Material>(VegetationLabBuilder.Generated + "/PainterlyWater.mat"); } }
        static Material Ground { get { return AssetDatabase.LoadAssetAtPath<Material>(VegetationLabBuilder.Generated + "/PainterlyGround.mat"); } }
        static void SetWaterDepthDebug(GrassMeadow meadow, bool enabled)
        {
            Water.SetFloat("_DebugDepth", 1);
            Ground.SetShaderPassEnabled("DepthNormals", enabled);
            foreach (var material in meadow.materials) material.SetShaderPassEnabled("DepthNormals", enabled);
        }
        static void RestoreDepthMaterials(GrassMeadow meadow)
        {
            Water.SetFloat("_DebugDepth", 0);
            Ground.SetShaderPassEnabled("DepthNormals", true);
            if (meadow) foreach (var material in meadow.materials) material.SetShaderPassEnabled("DepthNormals", true);
        }

        public static void Capture(string name, Camera camera)
        {
            var texture = RenderTexture.GetTemporary(1600, 1000, 24, RenderTextureFormat.ARGB32);
            var prior = camera.targetTexture; var active = RenderTexture.active;
            try
            {
                camera.targetTexture = texture; camera.Render(); RenderTexture.active = texture;
                var image = new Texture2D(1600, 1000, TextureFormat.RGB24, false);
                image.ReadPixels(new Rect(0, 0, 1600, 1000), 0, 0); image.Apply();
                File.WriteAllBytes(Output + "/" + name + ".png", image.EncodeToPNG());
                Object.DestroyImmediate(image);
            }
            finally { camera.targetTexture = prior; RenderTexture.active = active; RenderTexture.ReleaseTemporary(texture); }
        }
        static void Log(string message, string stack, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors += message + "\n";
        }
        static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
        static void Fail(Exception error)
        {
            (liveChecks as IDisposable)?.Dispose(); liveChecks = null;
            EditorApplication.update -= Tick;
            SessionState.SetBool(RunningKey, false);
            Application.logMessageReceived -= Log;
            RestoreDepthMaterials(Object.FindObjectOfType<GrassMeadow>());
            Directory.CreateDirectory(Output);
            File.WriteAllText(Output + "/FAILED.txt", error.ToString());
            Debug.LogException(error);
            if (Application.isBatchMode) EditorApplication.Exit(1);
            else if (EditorApplication.isPlaying) EditorApplication.isPlaying = false;
            else RestorePreviousScene();
        }
    }
}
