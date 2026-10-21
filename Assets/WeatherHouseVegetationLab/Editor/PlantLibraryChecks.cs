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
        const string Biomes = "WeatherHouse.PlantLibrary.BiomeChecks";
        const string Character = "WeatherHouse.PlantLibrary.CharacterChecks";
        const string SettingsSaved = "WeatherHouse.PlantLibrary.PlaySettingsSaved";
        const string BackgroundSetting = "WeatherHouse.PlantLibrary.BackgroundSetting";
        const string PauseSetting = "WeatherHouse.PlantLibrary.PauseSetting";
        static string ReportPrefix { get { return SessionState.GetBool(Character, false) ? "character-in-vegetation" : SessionState.GetBool(Biomes, false) ? "plant-biomes" : "plant-library"; } }
        static string ResultPath { get { return Output + "/" + ReportPrefix + "-result.txt"; } }
        static string DriverReport { get { return Output + "/" + ReportPrefix + "-driver-checks.txt"; } }
        static IEnumerator steps;
        static double started, lastFrameAt;
        static int lastFrame, firstFrame, editorTicks;
        static string errors;
        internal static string ActiveStage { get; set; }

        [MenuItem("Weather House/Vegetation Lab/植物资源库 · 验证并截图")]
        public static void BuildAndVerify()
        {
            BuildAndVerify(false);
        }
        public static void BuildAndVerify(bool refreshPresentation)
        {
            Begin(refreshPresentation, false);
        }
        public static void BuildBiomesAndVerify() { Begin(false, true); }
        public static void VerifyExistingBiomes() { Begin(false, true, false); }
        public static void BuildCharacterAndVerify() { Begin(false, true, false, true); }
        static void Begin(bool refreshPresentation, bool biomeSamples, bool installBiomes = true, bool character = false)
        {
            var scene = SceneManager.GetActiveScene();
            if (EditorApplication.isPlayingOrWillChangePlaymode || SessionState.GetBool(Running, false) || scene.isDirty || SceneManager.sceneCount != 1)
                throw new InvalidOperationException("请先保存当前单一场景并退出 Play，检查后会恢复它。");
            Directory.CreateDirectory(Output);
            SessionState.SetBool(Biomes, biomeSamples);
            SessionState.SetBool(Character, character);
            File.WriteAllText(ResultPath, "RUNNING\n");
            SessionState.SetString(ReturnScene, scene.path);
            try
            {
                if (biomeSamples)
                {
                    if (character) VegetationCharacterIntegration.Install();
                    else if (installBiomes) PlantBiomeSamples.Install();
                    else EditorSceneManager.OpenScene(VegetationLabBuilder.ScenePath);
                    PlantBiomeSamples.Verify();
                }
                else
                {
                    if (refreshPresentation) PlantLibraryBuilder.RefreshScenePresentation();
                    else PlantLibraryBuilder.OpenOrCreate();
                    VerifyEditor();
                }
                SessionState.SetBool(BackgroundSetting, Application.runInBackground);
                SessionState.SetBool(PauseSetting, EditorApplication.isPaused);
                SessionState.SetBool(SettingsSaved, true);
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
                var edited = Object.Instantiate(entry.species); edited.height *= 1.25f;
                var editedMesh = GrassMeshFactory.Build(edited, 0);
                Assert(!editedMesh.vertices.SequenceEqual(entry.variants[0].GetComponent<MeshFilter>().sharedMesh.vertices), entry.id + " independent editable appearance");
                Object.DestroyImmediate(editedMesh); Object.DestroyImmediate(edited);
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
                    report.AppendLine(entry.id + " v" + (v + 1) + " triangles=" + triangles.Length / 3 + " height=" + (points.Max(p => p.y) - points.Min(p => p.y)).ToString("F3") + "m");
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
                errors = ""; started = EditorApplication.timeSinceStartup;
                steps = SessionState.GetBool(Character, false) ? VegetationCharacterIntegration.VerifyPlay() : SessionState.GetBool(Biomes, false) ? RunBiomes() : Run();
                firstFrame = lastFrame = Time.frameCount; lastFrameAt = started; editorTicks = 0;
                ActiveStage = "starting Play frames";
                File.WriteAllText(DriverReport, "START " + DescribePlayState() + "\n");
                Application.runInBackground = true;
                EditorApplication.isPaused = false;
                EditorApplication.update -= Tick; EditorApplication.update += Tick;
                EditorApplication.QueuePlayerLoopUpdate();
            }
            if (state == PlayModeStateChange.ExitingPlayMode && SessionState.GetBool(Running, false))
                Finish(new InvalidOperationException("Play stopped before checks completed. " + DescribePlayState()));
            if (state == PlayModeStateChange.EnteredEditMode) Restore();
        }
        static void Tick()
        {
            if (steps == null) return;
            try
            {
                double now = EditorApplication.timeSinceStartup;
                editorTicks++;
                if (now - started > 240) throw new Exception("Plant library Play verification timed out. " + DescribePlayState());
                Assert(EditorApplication.isPlaying && !EditorApplication.isPaused, "Play must remain running. " + DescribePlayState());
                EditorApplication.QueuePlayerLoopUpdate();
                // Editor callbacks can run without a PlayerLoop frame, especially in background.
                // Each yielded test step must let real MonoBehaviour.Update/Destroy/rendering run.
                if (Time.frameCount == lastFrame)
                {
                    Assert(now - lastFrameAt < 20, "No Play frame for 20 seconds. " + DescribePlayState());
                    return;
                }
                lastFrame = Time.frameCount; lastFrameAt = now;
                if (now - started < 3) return;
                if (!steps.MoveNext()) Finish(string.IsNullOrEmpty(errors) ? null : new Exception(errors));
            }
            catch (Exception error) { Finish(error); }
        }
        static IEnumerator Run()
        {
            var view = Object.FindObjectOfType<GrassLabView>(); var camera = view.GetComponent<Camera>(); var nav = view.Navigation;
            ActiveStage = "library gallery captures";
            nav.Overview();
            yield return null;
            Capture("Plants_Overview", camera, 1600, 1000);
            IEnumerator controls = CaptureControls("Plants_Controls");
            while (controls.MoveNext()) yield return null;
            nav.CloseGrass(); Capture("Plants_FourGrass", camera, 1600, 900);
            yield return null;
            nav.Shore(); Capture("Plants_Accents", camera, 1600, 1000);
            yield return null;
            ActiveStage = "individual plant captures";
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
                ActiveStage = "legacy three-species live checks";
                check = GrassLiveChecks.Verify(legacyMeadow);
                while (check.MoveNext()) yield return null;
            }
            finally { (check as IDisposable)?.Dispose(); Object.Destroy(legacy); }
            nav.SetMode(GrassCameraMode.Fly); var before = nav.transform.position;
            nav.StepNavigation(Vector3.up, Vector2.zero, false, .2f);
            Assert(nav.transform.position.y > before.y, "library fly ascends");
            nav.CloseGrass();
        }
        static IEnumerator RunBiomes()
        {
            var character = Object.FindObjectOfType<VegetationCharacterPreview>();
            if (character) character.SetCharacterMode(false);
            ActiveStage = "biome region checks";
            var view = Object.FindObjectOfType<GrassLabView>(); var camera = view.GetComponent<Camera>(); var nav = view.Navigation;
            Assert(view.regions != null && view.regions.Length == 5, "original meadow plus four selectable regions");
            PlantRegionChecks.Verify(view);
            string[] ids = { "LowMeadow", "TallFlowers", "FernEdge", "Wetland" };
            for (int i = 1; i < view.regions.Length; i++)
            {
                view.SelectRegion(i); yield return null;
                Assert(view.meadow == view.regions[i] && nav.meadow == view.regions[i], "region selection drives controls and camera");
                Assert(view.meadow.Counts.Sum() == view.meadow.totalClumps, "region has its saved population");
                Capture("Biome_" + ids[i - 1], camera, 1600, 1000);
                string[][] before = view.regions.Select(r => r.species.Select(s => JsonUtility.ToJson(s)).ToArray()).ToArray();
                int revision = view.meadow.RequestedRevision;
                int shapeIndex = Array.FindIndex(view.meadow.Counts, count => count > 0);
                var visibleMesh = view.meadow.GetComponentsInChildren<MeshFilter>().First(f => f.GetComponent<MeshRenderer>().sharedMaterial == view.meadow.materials[shapeIndex]);
                Vector3[] verticesBefore = visibleMesh.sharedMesh.vertices;
                float initial = view.meadow.species[shapeIndex].height; view.meadow.species[shapeIndex].height = initial * 1.2f;
                int frame = Time.frameCount; float deadline = Time.realtimeSinceStartup + 30;
                do { Assert(Time.realtimeSinceStartup < deadline, "region shape update timed out"); yield return null; }
                while (Time.frameCount - frame < 3 || view.meadow.IsLiveUpdating);
                Assert(view.meadow.CompletedRevision > revision && string.IsNullOrEmpty(view.meadow.LiveError), "selected region responds without a rebuild button");
                Assert(!visibleMesh.sharedMesh.vertices.SequenceEqual(verticesBefore), "selected region changes its displayed geometry");
                for (int other = 0; other < view.regions.Length; other++)
                    if (other != i) Assert(before[other].SequenceEqual(view.regions[other].species.Select(s => JsonUtility.ToJson(s))), "region edits preserve other regions");
                view.meadow.species[shapeIndex].height = initial;
                frame = Time.frameCount; deadline = Time.realtimeSinceStartup + 30;
                do { Assert(Time.realtimeSinceStartup < deadline, "region restore timed out"); yield return null; }
                while (Time.frameCount - frame < 3 || view.meadow.IsLiveUpdating);
            }
            IEnumerator controls = CaptureControls("Biome_Controls");
            while (controls.MoveNext()) yield return null;
            nav.enabled = false;
            camera.farClipPlane = 350;
            camera.transform.position = new Vector3(0, 116, -144); camera.transform.LookAt(new Vector3(0, 3, 0));
            Capture("Biomes_Overview", camera, 1600, 1000);
            nav.enabled = true; view.SelectRegion(0); nav.Overview();
            File.WriteAllText(Output + "/plant-biomes-live-checks.txt", "PASS: four saved regions; region selection; real Play shape updates; independent parameter clones; restores; all region GPU captures.\n");
        }
        static IEnumerator CaptureControls(string name)
        {
            ActiveStage = name + " rendered UI capture";
            string path = Output + "/" + name + ".png";
            DateTime previous = File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;
            int frame = Time.frameCount;
            double deadline = EditorApplication.timeSinceStartup + 20;
            ScreenCapture.CaptureScreenshot(path);
            do
            {
                Assert(EditorApplication.timeSinceStartup < deadline, "Fresh UI screenshot was not written: " + path + ". " + DescribePlayState());
                yield return null;
            }
            while (Time.frameCount - frame < 2 || !File.Exists(path) || File.GetLastWriteTimeUtc(path) <= previous || new FileInfo(path).Length == 0);
            File.AppendAllText(DriverReport, "PASS fresh UI screenshot " + path + "; " + DescribePlayState() + "\n");
        }
        internal static string DescribePlayState()
        {
            return "stage=" + ActiveStage + "; frame=" + Time.frameCount + "; framesAdvanced=" + (Time.frameCount - firstFrame)
                + "; editorTicks=" + editorTicks + "; playing=" + EditorApplication.isPlaying + "; paused=" + EditorApplication.isPaused
                + "; focused=" + Application.isFocused + "; runInBackground=" + Application.runInBackground + "; timeScale=" + Time.timeScale;
        }
        public static void Capture(string name, Camera camera, int width, int height)
        {
            var target = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default, 4); var previous = camera.targetTexture; var active = RenderTexture.active;
            var texture = new Texture2D(width, height, TextureFormat.RGB24, false);
            try { camera.targetTexture = target; camera.Render(); RenderTexture.active = target; texture.ReadPixels(new Rect(0, 0, width, height), 0, 0); texture.Apply(); File.WriteAllBytes(Output + "/" + name + ".png", texture.EncodeToPNG()); }
            finally { camera.targetTexture = previous; RenderTexture.active = active; RenderTexture.ReleaseTemporary(target); Object.DestroyImmediate(texture); }
        }
        static void Finish(Exception error)
        {
            var pending = steps; steps = null;
            EditorApplication.update -= Tick; SessionState.SetBool(Running, false);
            try { (pending as IDisposable)?.Dispose(); }
            catch (Exception cleanup) { error = error == null ? cleanup : new AggregateException(error, cleanup); }
            Application.logMessageReceived -= Log;
            File.AppendAllText(DriverReport, (error == null ? "PASS " : "FAIL ") + DescribePlayState() + "\n");
            File.WriteAllText(ResultPath, error == null ? (SessionState.GetBool(Character, false) ? "PASS: integrated animated character, real movement and bone poses, walk/run/jump/landing, five-region ground contact, exclusive camera and input modes, GPU captures.\n" : SessionState.GetBool(Biomes, false) ? "PASS: four biome regions in original lab, independent sources, world ground alignment, Play region switching and live shape edits, GPU captures.\n" : "PASS: editor assets, 76 prefab variants, GPU captures, 19-slot live controls, legacy three-slot live controls, fly camera.\n") : "FAIL: " + error + "\n");
            if (EditorApplication.isPlaying) EditorApplication.isPlaying = false; else Restore();
        }
        static void Restore()
        {
            if (SessionState.GetBool(SettingsSaved, false))
            {
                Application.runInBackground = SessionState.GetBool(BackgroundSetting, false);
                EditorApplication.isPaused = SessionState.GetBool(PauseSetting, false);
                SessionState.EraseBool(SettingsSaved);
                SessionState.EraseBool(BackgroundSetting); SessionState.EraseBool(PauseSetting);
            }
            string path = SessionState.GetString(ReturnScene, ""); SessionState.EraseString(ReturnScene);
            if (!string.IsNullOrEmpty(path) && File.Exists(path) && SceneManager.GetActiveScene().path != path) EditorSceneManager.OpenScene(path);
        }
        static void Log(string message, string stack, LogType type) { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors += message + "\n"; }
        static bool Finite(float value) { return !float.IsNaN(value) && !float.IsInfinity(value); }
        static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException("Plant library: " + message); }
    }
}
