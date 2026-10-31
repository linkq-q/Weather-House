using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using WeatherHouse.WatersideV2;

namespace WeatherHouse.NightReview20261027
{
    // Coordinator-installed observer. No original camera, object, asset or scene is written.
    [InitializeOnLoad]
    public static class NightMapViewCapture
    {
        const string Stage = NightMapSnapshot.Staging;
        static double nextPoll;
        [Serializable] public sealed class Request { public string expectedSceneSha256; public string label = "before"; public string[] assets; }
        [Serializable] public sealed class View { public string key, file, buildingId, status; public Vector3 position, target; public Bounds bounds; public bool orthographic; }
        [Serializable] public sealed class Line { public Vector3[] points; }
        [Serializable] public sealed class Result
        {
            public string status, utc, sceneSha256, terrainSha256, originalStateHashBefore, originalStateHashAfter, error;
            public bool dirtyBefore, dirtyAfter;
            public int transformCountBefore, transformCountAfter;
            public List<View> views = new List<View>();
            public List<Line> routes = new List<Line>(), corridors = new List<Line>();
        }
        static NightMapViewCapture() { EditorApplication.update += Poll; }
        static void Poll()
        {
            if (EditorApplication.timeSinceStartup < nextPoll || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            nextPoll = EditorApplication.timeSinceStartup + 1;
            string path = Path.Combine(Stage, "views-request.json"); if (!File.Exists(path)) return;
            var request = JsonUtility.FromJson<Request>(File.ReadAllText(path)); File.Delete(path);
            try { Capture(request); } catch (Exception e) { File.WriteAllText(Path.Combine(Stage, "views-error.txt"), e.ToString()); }
        }
        public static string Capture(Request request)
        {
            Scene scene = SceneManager.GetActiveScene();
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating || scene.path != NightMapSnapshot.ScenePath || SceneManager.sceneCount != 1)
                throw new InvalidOperationException("Stable, already-open WatersideV2 Edit mode is required; no editor state is changed.");
            string hash = HashFile(scene.path);
            if (request == null || !string.Equals(hash, request.expectedSceneSha256, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Coordinator scene SHA baseline does not match.");
            Transform[] original = All(scene);
            Camera source = original.Select(t => t.GetComponent<Camera>()).FirstOrDefault(c => c && c.gameObject.activeInHierarchy);
            if (!source) throw new InvalidOperationException("No existing source camera for render and volume settings.");
            Terrain terrain = original.Select(t => t.GetComponent<Terrain>()).FirstOrDefault(t => t && t.terrainData);
            string terrainPath = terrain ? AssetDatabase.GetAssetPath(terrain.terrainData) : "";
            string terrainHash = File.Exists(terrainPath) ? HashFile(terrainPath) : "";
            string folder = Path.Combine(Stage, "views", DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + (request.label == "after" ? "after" : "before"));
            Directory.CreateDirectory(folder);
            var result = new Result { status = "CAPTURING", utc = DateTime.UtcNow.ToString("O"), sceneSha256 = hash, terrainSha256 = terrainHash,
                dirtyBefore = scene.isDirty, originalStateHashBefore = StateHash(original), transformCountBefore = original.Length };
            result.routes = WatersideLayout.Routes.Select(r => new Line { points = r }).ToList();
            result.corridors = RuralMapConfiguration.NewCorridors.Select(r => new Line { points = r }).ToList();
            Add(result, "map-top", new Vector3(0, 195, 0), Vector3.zero, true);
            Add(result, "map-oblique", new Vector3(-155, 150, -170), new Vector3(0, 4, 0), false);
            foreach (string asset in request.assets != null && request.assets.Length > 0 ? request.assets : new[] { "A01", "A02", "A04", "A07" })
            {
                Transform building = original.FirstOrDefault(t => t.gameObject.activeInHierarchy &&
                    PrefabUtility.GetNearestPrefabInstanceRoot(t.gameObject) == t.gameObject &&
                    PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(t.gameObject) == "Assets/Environment/RuralRebuild20261025/Prefabs/" + asset + ".prefab");
                if (!building) continue;
                Renderer[] rs = building.GetComponentsInChildren<Renderer>(true).Where(r => r.enabled && r.gameObject.activeInHierarchy).ToArray();
                if (rs.Length == 0) continue;
                Bounds bounds = rs[0].bounds; foreach (Renderer r in rs.Skip(1)) bounds.Encapsulate(r.bounds);
                float radius = Mathf.Max(bounds.extents.x, bounds.extents.z) * 3.8f + 8;
                Vector3 target = bounds.center - Vector3.up * Mathf.Min(bounds.extents.y * .2f, 1);
                Vector3[] directions = { Vector3.forward, Vector3.right, Vector3.back, Vector3.left };
                string[] names = { "north", "east", "south", "west" };
                for (int i = 0; i < directions.Length; i++)
                {
                    View view = Add(result, asset + "-" + names[i], target + directions[i] * radius + Vector3.up * (radius * .48f), target, false);
                    view.bounds = bounds; view.buildingId = GlobalObjectId.GetGlobalObjectIdSlow(building).ToString();
                }
            }
            try
            {
                foreach (View view in result.views)
                {
                    view.file = Path.Combine(folder, view.key + ".png");
                    Render(source, view); view.status = "RENDERED_TEMPORARY_CAMERA";
                }
            }
            catch (Exception e) { result.error = e.ToString(); }
            finally
            {
                Transform[] after = All(scene); result.transformCountAfter = after.Length;
                result.originalStateHashAfter = StateHash(after); result.dirtyAfter = scene.isDirty;
                bool same = result.originalStateHashBefore == result.originalStateHashAfter && result.dirtyBefore == result.dirtyAfter &&
                    HashFile(scene.path) == hash && (string.IsNullOrEmpty(terrainPath) || HashFile(terrainPath) == terrainHash);
                result.status = same && string.IsNullOrEmpty(result.error) ? "PASS_READ_ONLY_VIEWS" : same ? "FAIL_RENDER_STATE_UNCHANGED" : "FAIL_STATE_GUARD";
                File.WriteAllText(Path.Combine(folder, "views.json"), JsonUtility.ToJson(result, true));
                File.WriteAllText(Path.Combine(Stage, "latest-views.txt"), Path.Combine(folder, "views.json"));
            }
            return Path.Combine(folder, "views.json");
        }
        static View Add(Result r, string key, Vector3 position, Vector3 target, bool orthographic)
        { var v = new View { key = key, position = position, target = target, orthographic = orthographic, status = "NOT_RENDERED" }; r.views.Add(v); return v; }
        static void Render(Camera source, View view)
        {
            GameObject go = null; Texture2D image = null; RenderTexture target = null; RenderTexture oldActive = RenderTexture.active;
            try
            {
                go = new GameObject("NightMapTemporaryView") { hideFlags = HideFlags.HideAndDontSave };
                Camera camera = go.AddComponent<Camera>(); camera.hideFlags = HideFlags.HideAndDontSave; camera.CopyFrom(source); camera.enabled = false;
                var sourceData = source.GetComponent<UniversalAdditionalCameraData>(); var data = go.AddComponent<UniversalAdditionalCameraData>(); data.hideFlags = HideFlags.HideAndDontSave;
                if (sourceData) EditorUtility.CopySerialized(sourceData, data);
                data.renderType = CameraRenderType.Base; data.cameraStack.Clear();
                camera.transform.position = view.position; camera.transform.rotation = Quaternion.LookRotation(view.target - view.position, view.orthographic ? Vector3.forward : Vector3.up);
                camera.orthographic = view.orthographic; camera.orthographicSize = 102; camera.fieldOfView = view.key == "map-oblique" ? 47 : 48;
                camera.aspect = 16f / 9; camera.nearClipPlane = .2f; camera.farClipPlane = 600;
                target = RenderTexture.GetTemporary(1600, 900, 24, RenderTextureFormat.ARGB32);
                var request = new UniversalRenderPipeline.SingleCameraRequest { destination = target };
                if (!RenderPipeline.SupportsRenderRequest(camera, request)) throw new NotSupportedException("URP SingleCameraRequest is unavailable.");
                RenderPipeline.SubmitRenderRequest(camera, request); RenderTexture.active = target;
                image = new Texture2D(target.width, target.height, TextureFormat.RGB24, false) { hideFlags = HideFlags.HideAndDontSave };
                image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); image.Apply(false, false); File.WriteAllBytes(view.file, image.EncodeToPNG());
            }
            finally { RenderTexture.active = oldActive; if (image) UnityEngine.Object.DestroyImmediate(image); if (target) RenderTexture.ReleaseTemporary(target); if (go) UnityEngine.Object.DestroyImmediate(go); }
        }
        static Transform[] All(Scene scene) => scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true)).Where(t => (t.gameObject.hideFlags & HideFlags.DontSave) == 0).OrderBy(t => t.GetInstanceID()).ToArray();
        static string StateHash(Transform[] all)
        {
            string[] rows = all.Select(t => t.GetInstanceID() + "|" + (t.parent ? t.parent.GetInstanceID() : 0) + "|" + t.GetSiblingIndex() + "|" + t.gameObject.activeSelf + "|" +
                EditorJsonUtility.ToJson(t) + "|" + string.Join("|", t.GetComponents<Camera>().Select(c => EditorJsonUtility.ToJson(c))) + "|" +
                string.Join("|", t.GetComponents<Renderer>().Select(r => EditorJsonUtility.ToJson(r)))).ToArray();
            using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(string.Join("\n", rows)))).Replace("-", "");
        }
        static string HashFile(string path) { using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-", ""); }
    }
}
