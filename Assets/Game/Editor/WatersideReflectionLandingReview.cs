using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using WeatherHouse.Vegetation;

namespace WeatherHouse.Review
{
    // One-shot owned migration; never saves an already dirty scene.
    public static class WatersideReflectionLandingReview
    {
        const string ScenePath = "Assets/Scenes/WeatherHouse_WatersideV2.unity";
        const string Expected = "E4F00D0A826B9E81E0406A067366E4CC98082186788AA54FDBA958A6A184AD96";
        const string Folder = "PerformanceAuditReview/20261024/Optimization/ReflectionRenderer/MainLanding";
        static string Hash(string path)
        {
            using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-", "");
        }
        [Serializable] sealed class Result
        {
            public string status, error, sceneBefore, sceneAfter;
            public int waters;
            public bool transformsUnchanged, clean;
        }
        public static void Apply()
        {
            var scene = SceneManager.GetActiveScene();
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating ||
                SceneManager.sceneCount != 1 || scene.path != ScenePath || scene.isDirty || Hash(ScenePath) != Expected)
                throw new InvalidOperationException("Reflection landing requires the expected clean saved scene in idle Edit mode.");
            var waters = UnityEngine.Object.FindObjectsOfType<PainterlyWaterReflection>(true).Where(w => w.gameObject.scene == scene).ToArray();
            if (waters.Length != 16 || waters.Any(w => w.reflectionRendererIndex != -1))
                throw new InvalidOperationException("Expected 16 untouched reflection components.");
            var poses = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true))
                .ToDictionary(t => t, t => (t.localPosition, t.localRotation, t.localScale));
            bool Same() => poses.All(p => p.Key && p.Key.localPosition.Equals(p.Value.localPosition) &&
                p.Key.localRotation.Equals(p.Value.localRotation) && p.Key.localScale.Equals(p.Value.localScale));
            string folder = Path.Combine(Folder, DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff"));
            Directory.CreateDirectory(folder);
            var result = new Result { status = "STARTED", sceneBefore = Hash(ScenePath), waters = waters.Length };
            try
            {
                File.Copy(ScenePath, Path.Combine(folder, "scene-disk-before.unity"));
                if (!EditorSceneManager.SaveScene(scene, Path.Combine(folder, "scene-memory-before.unity"), true) ||
                    scene.isDirty || Hash(ScenePath) != Expected || !Same()) throw new IOException("Backup/state guard failed.");
                Undo.RecordObjects(waters, "Use dedicated water reflection renderer");
                foreach (var water in waters) { water.reflectionRendererIndex = 2; EditorUtility.SetDirty(water); }
                EditorSceneManager.MarkSceneDirty(scene);
                if (!Same() || Hash(ScenePath) != Expected) throw new IOException("Scene changed externally; owned edits left unsaved with backup.");
                if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Scene save failed.");
                result.transformsUnchanged = Same(); result.clean = !scene.isDirty;
                if (!result.transformsUnchanged || !result.clean) throw new InvalidOperationException("Post-save verification failed.");
                result.status = "PASS";
            }
            catch (Exception error) { result.status = "FAIL"; result.error = error.ToString(); throw; }
            finally
            {
                result.sceneAfter = Hash(ScenePath);
                File.WriteAllText(Path.Combine(folder, "result.json"), JsonUtility.ToJson(result, true));
            }
        }
    }
}
