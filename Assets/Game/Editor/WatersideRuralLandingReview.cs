using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using WeatherHouse.WatersideV2.Editor;

namespace WeatherHouse.Review
{
    // Coordinator review entrypoints; all actual scene changes use the guarded importer.
    public static class WatersideRuralLandingReview
    {
        const string Folder = "JourneyReview/Revision_20261024/MapIntegrationPlan";
        [Serializable] sealed class Protection { public string name; public Vector3 center, size; }
        [Serializable] sealed class ProtectionReport { public Vector3 terrainOrigin, terrainSize; public Protection[] bounds; }
        static void ExportProtection(string hash)
        {
            RuralMapIntegration.Preflight(hash);
            var all = SceneManager.GetActiveScene().GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true)).ToArray();
            var names = new System.Collections.Generic.List<string> { "天气屋 · v05 苔顶木构" };
            names.AddRange(WeatherHouse.WatersideV2.WatersideLayout.Crossings.Select(c => c.Name));
            names.AddRange(all.Where(t => (t.name == "岸边小桌" || t.name == "木椅") && t.GetComponentsInChildren<Renderer>().Length > 0).Select(t => t.name + " " + GlobalObjectId.GetGlobalObjectIdSlow(t)));
            foreach (var fox in all.Select(t => t.GetComponent<WeatherHouse.Hillside.WildlifeAgent>()).Where(a => a && a.speciesId == "fox"))
            { names.Add("fox " + fox.spawnId + " habitat"); names.Add("fox " + fox.spawnId + " current"); }
            var boxes = (System.Collections.Generic.List<Bounds>)typeof(RuralMapIntegration).GetField("protectedBounds", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
            if (boxes.Count != names.Count) throw new InvalidOperationException("Protection labels do not match prepared bounds.");
            var terrain = all.Select(t => t.GetComponent<Terrain>()).Single(t => t);
            var report = new ProtectionReport { terrainOrigin = terrain.transform.position, terrainSize = terrain.terrainData.size,
                bounds = boxes.Select((b, i) => new Protection { name = names[i], center = b.center, size = b.size }).ToArray() };
            File.WriteAllText(Folder + "/MainImport/protected-bounds.json", JsonUtility.ToJson(report, true));
        }
        public static void InstallReviewedDogContact()
        {
            WatersideSceneBuilder.RequireSaved();
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
                throw new InvalidOperationException("Install from idle Edit mode.");
            const string review = "JourneyReview/Revision_20261024/AnimationInstall/AdditionalSources/DogEating/ForageContactV1/";
            var info = new ProcessStartInfo {
                FileName = "C:/Users/31483/.cache/codex-runtimes/codex-primary-runtime/dependencies/python/python.exe",
                Arguments = "-X utf8 \"" + Path.GetFullPath(review + "install_reviewed.py") + "\" --apply",
                WorkingDirectory = Directory.GetCurrentDirectory(), UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true
            };
            AssetDatabase.DisallowAutoRefresh();
            try
            {
                using (var process = Process.Start(info))
                {
                    var output = process.StandardOutput.ReadToEndAsync();
                    var error = process.StandardError.ReadToEndAsync();
                    if (!process.WaitForExit(15000)) { process.Kill(); throw new TimeoutException("Owned dog installer timed out."); }
                    File.WriteAllText(review + "main-install.txt", output.GetAwaiter().GetResult() + error.GetAwaiter().GetResult());
                    if (process.ExitCode != 0) throw new InvalidOperationException("Dog installer failed; inspect main-install.txt.");
                }
            }
            finally { AssetDatabase.AllowAutoRefresh(); }
            AssetDatabase.Refresh();
        }
        public static void Run(string command)
        {
            string hash = File.ReadAllText(Folder + "/baseline-scene-sha.txt").Trim();
            if (command == "v3-rural-protection") { ExportProtection(hash); return; }
            if (command == "v3-rural-recover") { RecoverRestoredAttempt(hash); return; }
            if (command == "v3-rural-code")
            {
                WatersideSceneBuilder.RequireSaved();
                var info = new ProcessStartInfo {
                    FileName = "C:/Users/31483/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/bin/node.exe",
                    Arguments = "\"" + Path.GetFullPath(Folder + "/staging/install_code.cjs") + "\" --apply --scene-sha=" + hash,
                    WorkingDirectory = Directory.GetCurrentDirectory(),
                    UseShellExecute = false, CreateNoWindow = true,
                    RedirectStandardOutput = true, RedirectStandardError = true
                };
                AssetDatabase.DisallowAutoRefresh();
                try
                {
                    using (var process = Process.Start(info))
                    {
                        var stdout = process.StandardOutput.ReadToEndAsync();
                        var stderr = process.StandardError.ReadToEndAsync();
                        if (!process.WaitForExit(15000)) { process.Kill(); throw new TimeoutException("Owned code installer timed out."); }
                        string output = stdout.GetAwaiter().GetResult() + stderr.GetAwaiter().GetResult();
                        File.WriteAllText(Folder + "/MainImport/code-install.txt", output);
                        if (process.ExitCode != 0) throw new InvalidOperationException("Code installer failed; see code-install.txt.");
                    }
                }
                finally { AssetDatabase.AllowAutoRefresh(); }
                AssetDatabase.Refresh();
                return;
            }
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
                throw new InvalidOperationException("Wait for clean Edit mode and compilation.");
            string method;
            switch (command)
            {
                case "v3-rural-preflight": method = "Preflight"; break;
                case "v3-rural-apply": method = "Apply"; break;
                case "v3-rural-validate": method = "Validate"; break;
                case "v3-rural-save": method = "Save"; break;
                default: throw new ArgumentException("Unknown rural landing command.");
            }
            var type = typeof(WatersideSceneBuilder).Assembly.GetType("WeatherHouse.WatersideV2.Editor.RuralMapIntegration", true);
            string result = (string)type.GetMethod(method, BindingFlags.Static | BindingFlags.Public)
                .Invoke(null, method == "Validate" ? null : new object[] { hash });
            File.WriteAllText(Folder + "/MainImport/" + command + ".txt", result);
        }

        [Serializable] sealed class TransformList { public RuralMapIntegration.TransformRecord[] items; }
        static void RecoverRestoredAttempt(string expectedHash)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
                throw new InvalidOperationException("Recovery requires Edit mode.");
            var run = JsonUtility.FromJson<RuralMapIntegration.Run>(File.ReadAllText(Folder + "/latest-execution.json"));
            var scene = SceneManager.GetActiveScene();
            using (var sha = SHA256.Create())
                if (BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(scene.path))).Replace("-", "") != expectedHash
                    || run.sceneBeforeHash != expectedHash || !run.status.StartsWith("failed-restored-in-memory:"))
                    throw new InvalidOperationException("Only the unchanged saved baseline of a restored failed attempt may be reopened.");
            var all = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true)).ToArray();
            var before = JsonUtility.FromJson<TransformList>(File.ReadAllText(run.folder + "/existing-transforms.json")).items;
            var current = all.ToDictionary(t => GlobalObjectId.GetGlobalObjectIdSlow(t).ToString());
            if (current.Count != before.Length) throw new InvalidOperationException("Scene object count differs after recovery.");
            foreach (var item in before)
            {
                Transform t;
                if (!current.TryGetValue(item.id, out t) || Vector3.Distance(t.position,item.position) > .00001f
                    || Quaternion.Angle(t.rotation,item.rotation) > .001f || t.localScale != item.scale || t.gameObject.activeSelf != item.active)
                    throw new InvalidOperationException("Recovered transform differs: " + item.name);
            }
            var terrain = all.Select(t => t.GetComponent<Terrain>()).Single(t => t);
            var grass = all.Select(t => t.GetComponent<WeatherHouse.Hillside.HillsideGrassRenderer>()).Single(g => g && g.terrain == terrain);
            var sea = all.Single(t => t.name == "海湾").GetComponent<MeshFilter>();
            if (AssetDatabase.GetAssetPath(terrain.terrainData) != run.originalTerrain
                || terrain.GetComponent<TerrainCollider>().terrainData != terrain.terrainData
                || AssetDatabase.GetAssetPath(grass.placementMap) != run.originalGrass
                || AssetDatabase.GetAssetPath(sea.sharedMesh) != run.originalSea)
                throw new InvalidOperationException("Recovered terrain/grass/sea references differ.");
            if (!EditorSceneManager.SaveScene(scene, run.folder + "/restored-memory.unity", true))
                throw new IOException("Recovered memory backup failed.");
            EditorSceneManager.OpenScene(scene.path);
            File.WriteAllText(run.folder + "/recovery-checked.txt", "PASS all original transforms and terrain/collider/grass/sea references restored; memory backed up; unchanged disk baseline reopened. New Layout remains installed: no Play until the layout application succeeds.");
        }
    }
}
