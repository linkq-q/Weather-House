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

namespace WeatherHouse.NightReview20261027
{
    // The coordinator installs this observer. A request captures data only; it never applies a plan or saves a scene.
    [InitializeOnLoad]
    public static class NightMapSnapshot
    {
        public const string Staging = "C:/Users/31483/.codex/visualizations/2026/10/26/01a178cf-479b-7f41-af26-309b1776f6d5/map-staging";
        public const string ScenePath = "Assets/Scenes/WeatherHouse_WatersideV2.unity";
        static double nextPoll;
        [Serializable] public sealed class Request { public string expectedSceneSha256; public bool captureCamera = true; }
        [Serializable] public sealed class TransformRow
        {
            public string id, parentId, path, name, prefabPath;
            public int sibling, instanceId;
            public Vector3 position, localPosition, localScale;
            public Quaternion rotation, localRotation;
            public bool activeSelf, activeInHierarchy;
        }
        [Serializable] public sealed class RendererRow
        {
            public string id, transformId, path, meshPath, prefabPath;
            public string[] materials;
            public Bounds bounds;
            public bool enabled, active;
        }
        [Serializable] public sealed class RootRow { public string id, path; public int descendants; public Bounds bounds; }
        [Serializable] public sealed class TerrainRow
        {
            public string id, path, dataPath, dataSha256;
            public Vector3 position, size;
            public int heightResolution, alphaResolution, treeInstances, detailPrototypes;
        }
        [Serializable] public sealed class CameraRow
        {
            public string id, path, image, status;
            public Vector3 position;
            public Quaternion rotation;
            public float fieldOfView, aspect, near, far;
            public bool orthographic;
        }
        [Serializable] public sealed class Snapshot
        {
            public string status, utc, scene, sceneSha256, error;
            public bool playing, dirtyBefore, dirtyAfter, compiling;
            public List<TransformRow> transforms = new List<TransformRow>();
            public List<RendererRow> renderers = new List<RendererRow>();
            public List<RootRow> protectionRootCandidates = new List<RootRow>();
            public List<TerrainRow> terrains = new List<TerrainRow>();
            public List<CameraRow> cameras = new List<CameraRow>();
        }
        static NightMapSnapshot() { EditorApplication.update += Poll; }
        static void Poll()
        {
            if (EditorApplication.timeSinceStartup < nextPoll || EditorApplication.isPlayingOrWillChangePlaymode ||
                EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            nextPoll = EditorApplication.timeSinceStartup + 1;
            string path = Path.Combine(Staging, "snapshot-request.json");
            if (!File.Exists(path)) return;
            Request request = JsonUtility.FromJson<Request>(File.ReadAllText(path));
            File.Delete(path);
            try { Capture(request); }
            catch (Exception e) { File.WriteAllText(Path.Combine(Staging, "snapshot-error.txt"), e.ToString()); }
        }
        public static string Capture(Request request)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
                throw new InvalidOperationException("Snapshot needs stable Edit mode; no editor state was changed.");
            Scene scene = SceneManager.GetActiveScene();
            if (scene.path != ScenePath || SceneManager.sceneCount != 1)
                throw new InvalidOperationException("Snapshot expects the already open WatersideV2 scene alone.");
            string hash = Hash(ScenePath);
            if (request == null || string.IsNullOrEmpty(request.expectedSceneSha256) ||
                !hash.Equals(request.expectedSceneSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Scene disk hash changed; coordinator must provide the current baseline.");
            string folder = Path.Combine(Staging, "snapshots", DateTime.UtcNow.ToString("yyyyMMdd-HHmmss"));
            Directory.CreateDirectory(folder);
            var result = new Snapshot { status = "CAPTURING", utc = DateTime.UtcNow.ToString("O"), scene = scene.path,
                sceneSha256 = hash, dirtyBefore = scene.isDirty, playing = EditorApplication.isPlaying, compiling = EditorApplication.isCompiling };
            Transform[] all = All(scene);
            foreach (Transform t in all)
            {
                result.transforms.Add(CaptureTransform(t));
                foreach (Renderer renderer in t.GetComponents<Renderer>())
                {
                    MeshFilter filter = t.GetComponent<MeshFilter>();
                    Mesh mesh = renderer is SkinnedMeshRenderer skin ? skin.sharedMesh : filter ? filter.sharedMesh : null;
                    result.renderers.Add(new RendererRow { id = Id(renderer), transformId = Id(t), path = PathOf(t),
                        prefabPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(t.gameObject), meshPath = mesh ? AssetDatabase.GetAssetPath(mesh) : "",
                        materials = renderer.sharedMaterials.Select(m => m ? AssetDatabase.GetAssetPath(m) : "").ToArray(), bounds = renderer.bounds,
                        enabled = renderer.enabled, active = renderer.gameObject.activeInHierarchy });
                }
                if (t.name.Contains("天气屋") || t.name.Contains("归家石桥小院") || t.name.Contains("归家院"))
                {
                    Renderer[] renderers = t.GetComponentsInChildren<Renderer>(true);
                    Bounds bounds = new Bounds(t.position, Vector3.zero);
                    if (renderers.Length > 0) { bounds = renderers[0].bounds; foreach (Renderer r in renderers.Skip(1)) bounds.Encapsulate(r.bounds); }
                    result.protectionRootCandidates.Add(new RootRow { id = Id(t), path = PathOf(t), descendants = t.GetComponentsInChildren<Transform>(true).Length, bounds = bounds });
                }
                Terrain terrain = t.GetComponent<Terrain>();
                if (terrain && terrain.terrainData)
                {
                    TerrainData d = terrain.terrainData; string dataPath = AssetDatabase.GetAssetPath(d);
                    result.terrains.Add(new TerrainRow { id = Id(t), path = PathOf(t), dataPath = dataPath, dataSha256 = File.Exists(dataPath) ? Hash(dataPath) : "",
                        position = t.position, size = d.size, heightResolution = d.heightmapResolution, alphaResolution = d.alphamapResolution,
                        treeInstances = d.treeInstanceCount, detailPrototypes = d.detailPrototypes.Length });
                }
            }
            foreach (Camera camera in all.Select(t => t.GetComponent<Camera>()).Where(c => c))
            {
                var row = new CameraRow { id = Id(camera), path = PathOf(camera.transform), position = camera.transform.position,
                    rotation = camera.transform.rotation, fieldOfView = camera.fieldOfView, aspect = camera.aspect,
                    near = camera.nearClipPlane, far = camera.farClipPlane, orthographic = camera.orthographic, status = "metadata only" };
                result.cameras.Add(row);
                if (request.captureCamera)
                {
                    try { row.image = CaptureCamera(camera, Path.Combine(folder, "before-camera-" + result.cameras.Count + ".png")); row.status = "rendered current camera without moving it"; }
                    catch (Exception e) { row.status = "image unavailable: " + e.GetType().Name + ": " + e.Message; }
                }
            }
            result.dirtyAfter = scene.isDirty;
            TransformRow[] after = All(scene).Select(CaptureTransform).ToArray();
            bool same = result.transforms.Count == after.Length && result.transforms.Zip(after, Same).All(x => x);
            result.status = same && result.dirtyBefore == result.dirtyAfter && Hash(ScenePath) == hash ? "PASS_READ_ONLY_SNAPSHOT" : "FAIL_STATE_GUARD";
            if (result.status != "PASS_READ_ONLY_SNAPSHOT") result.error = "Transform, parent, active state, dirty flag or scene disk hash changed during capture.";
            string output = Path.Combine(folder, "snapshot.json"); File.WriteAllText(output, JsonUtility.ToJson(result, true));
            File.WriteAllText(Path.Combine(Staging, "latest-snapshot.txt"), output);
            return output;
        }
        static string CaptureCamera(Camera source, string path)
        {
            var request = new UniversalRenderPipeline.SingleCameraRequest();
            if (!RenderPipeline.SupportsRenderRequest(source, request)) throw new NotSupportedException("Active pipeline does not support SingleCameraRequest.");
            RenderTexture target = RenderTexture.GetTemporary(1280, 720, 24, RenderTextureFormat.ARGB32);
            RenderTexture oldActive = RenderTexture.active, oldTarget = source.targetTexture;
            Texture2D image = null;
            try
            {
                request.destination = target;
                RenderPipeline.SubmitRenderRequest(source, request);
                RenderTexture.active = target;
                image = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
                image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); image.Apply(false, false);
                File.WriteAllBytes(path, image.EncodeToPNG()); return path;
            }
            finally
            {
                source.targetTexture = oldTarget; RenderTexture.active = oldActive;
                if (image) UnityEngine.Object.DestroyImmediate(image); RenderTexture.ReleaseTemporary(target);
            }
        }
        static Transform[] All(Scene s) => s.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true)).OrderBy(Id).ToArray();
        static TransformRow CaptureTransform(Transform t) => new TransformRow { id = Id(t), parentId = t.parent ? Id(t.parent) : "", path = PathOf(t), name = t.name,
            prefabPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(t.gameObject), instanceId = t.GetInstanceID(), sibling = t.GetSiblingIndex(),
            position = t.position, rotation = t.rotation, localPosition = t.localPosition, localRotation = t.localRotation, localScale = t.localScale,
            activeSelf = t.gameObject.activeSelf, activeInHierarchy = t.gameObject.activeInHierarchy };
        static bool Same(TransformRow a, TransformRow b) => JsonUtility.ToJson(a) == JsonUtility.ToJson(b);
        static string PathOf(Transform t) => t.parent ? PathOf(t.parent) + "/" + t.name + "[" + t.GetSiblingIndex() + "]" : t.name + "[" + t.GetSiblingIndex() + "]";
        static string Id(UnityEngine.Object o) => GlobalObjectId.GetGlobalObjectIdSlow(o).ToString();
        static string Hash(string path) { using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-", ""); }
    }
}
