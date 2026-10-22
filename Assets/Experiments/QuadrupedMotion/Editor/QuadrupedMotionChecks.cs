using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace WeatherHouseQuadrupedMotion.Editor
{
    public static class QuadrupedMotionChecks
    {
        public const string Review = "QuadrupedMotionReview";
        [Serializable] public sealed class ClipResult
        {
            public string species, action, source, error, sourceHash, preview;
            public bool passed, finite, materialsValid, uvsValid, sourceUnchanged;
            public int bones, vertices, samples, textureWidth, textureHeight;
            public float seconds, maxVertexMovement, articulatedMovement, minGroundHeight, normalizedHeight;
        }
        [Serializable] public sealed class Report
        {
            public bool passed, playStateUnchanged, activeSceneUnchanged;
            public string capturedAt;
            public List<ClipResult> clips = new List<ClipResult>();
        }
        [Serializable] public sealed class CaptureStatus
        {
            public bool running, complete;
            public string species, action, error;
            public int clip, clips = 12, frame, frames;
            public int fps = 15, width = 512, height = 512;
        }
        static readonly Dictionary<string, Bounds> CaptureBounds = new Dictionary<string, Bounds>();
        static readonly Dictionary<string, Vector3> CaptureScales = new Dictionary<string, Vector3>();
        static readonly Dictionary<string, Vector3> CapturePositions = new Dictionary<string, Vector3>();
        static readonly Dictionary<string, float> CaptureFrontYaws = new Dictionary<string, float>();
        static QuadrupedPreviewScene captureScene;
        static CaptureStatus captureStatus;
        public static bool IsCapturing => captureStatus != null && captureStatus.running;
        [Serializable] sealed class GeometryStage
        {
            public string species;
            public Vector3 parentScale, rendererLocalScale, rendererLossyScale, sharedBoundsSize, bakedFalseSize, bakedTrueSize, falseWorldSize, rendererBoundsSize;
        }
        [Serializable] sealed class GeometryReport { public List<GeometryStage> stages = new List<GeometryStage>(); }

        public static void DiagnoseGeometry()
        {
            var report = new GeometryReport();
            foreach (string animal in QuadrupedMotionPreview.Species)
            {
                var preview = new PreviewRenderUtility();
                var root = new GameObject("Geometry check") { hideFlags = HideFlags.HideAndDontSave };
                preview.AddSingleGO(root);
                var mesh = new Mesh();
                try
                {
                    string path = QuadrupedMotionPreview.ModelPath(animal, "walk");
                    var model = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(path), root.transform);
                    foreach (Behaviour behaviour in model.GetComponentsInChildren<Behaviour>()) behaviour.enabled = false;
                    QuadrupedMotionPreview.FindClip(path).SampleAnimation(model, 0);
                    var skin = model.GetComponentInChildren<SkinnedMeshRenderer>();
                    foreach (float scale in new[] { 1f, .01f, 2f })
                    {
                        root.transform.localScale = Vector3.one * scale;
                        var stage = new GeometryStage { species = animal, parentScale = root.transform.localScale,
                            rendererLocalScale = skin.transform.localScale, rendererLossyScale = skin.transform.lossyScale,
                            sharedBoundsSize = skin.sharedMesh.bounds.size, rendererBoundsSize = skin.bounds.size };
                        skin.BakeMesh(mesh, false); stage.bakedFalseSize = mesh.bounds.size;
                        Vector3[] vertices = mesh.vertices;
                        var bounds = new Bounds(skin.transform.TransformPoint(vertices[0]), Vector3.zero);
                        foreach (Vector3 vertex in vertices) bounds.Encapsulate(skin.transform.TransformPoint(vertex));
                        stage.falseWorldSize = bounds.size;
                        skin.BakeMesh(mesh, true); stage.bakedTrueSize = mesh.bounds.size;
                        report.stages.Add(stage);
                    }
                }
                finally { Object.DestroyImmediate(mesh); preview.Cleanup(); }
            }
            File.WriteAllText(Review + "/geometry_diagnosis.json", JsonUtility.ToJson(report, true));
        }

        [MenuItem("Weather House/四足动作/导出十二动作预览序列")]
        public static void StartSequenceCapture()
        {
            if (IsCapturing) throw new InvalidOperationException("正在导出序列，请等待完成。");
            captureStatus = new CaptureStatus { running = true };
            Directory.CreateDirectory(Review);
            CaptureBounds.Clear(); CaptureScales.Clear(); CapturePositions.Clear(); CaptureFrontYaws.Clear();
            try
            {
                foreach (string animal in QuadrupedMotionPreview.Species)
                {
                    using (var walk = new QuadrupedPreviewScene(animal, "walk"))
                    {
                        CaptureScales[animal] = walk.DisplayScale;
                        CapturePositions[animal] = walk.DisplayPosition;
                        CaptureFrontYaws[animal] = walk.FrontYaw;
                        CaptureBounds[animal] = walk.FrameBounds;
                    }
                    foreach (string action in QuadrupedMotionPreview.Actions)
                        using (var scene = new QuadrupedPreviewScene(animal, action, CaptureScales[animal], CapturePositions[animal]))
                        {
                            Bounds combined = CaptureBounds[animal]; combined.Encapsulate(scene.FrameBounds); CaptureBounds[animal] = combined;
                        }
                }
                SaveCaptureStatus();
                EditorApplication.update += CaptureNextFrame;
            }
            catch (Exception exception) { FinishCapture(exception); throw; }
        }

        static void CaptureNextFrame()
        {
            if (EditorApplication.isCompiling) { FinishCapture(new InvalidOperationException("编译中断了序列导出；请重新执行导出。")); return; }
            try
            {
                int animalIndex = captureStatus.clip / QuadrupedMotionPreview.Actions.Length;
                int actionIndex = captureStatus.clip % QuadrupedMotionPreview.Actions.Length;
                string animal = QuadrupedMotionPreview.Species[animalIndex], action = QuadrupedMotionPreview.Actions[actionIndex];
                if (captureScene == null)
                {
                    captureScene = new QuadrupedPreviewScene(animal, action, CaptureScales[animal], CapturePositions[animal]);
                    captureScene.SharedCameraBounds = CaptureBounds[animal];
                    captureScene.SharedFrontYaw = CaptureFrontYaws[animal];
                    captureStatus.species = animal; captureStatus.action = action;
                    captureStatus.frame = 0;
                    captureStatus.frames = Mathf.Max(1, Mathf.CeilToInt(captureScene.Clip.length * captureStatus.fps));
                    Directory.CreateDirectory(Review + "/frames/" + animal + "/" + action);
                    SaveCaptureStatus();
                }
                captureScene.Sample((float)captureStatus.frame / captureStatus.fps);
                var texture = captureScene.Capture(captureStatus.width, captureStatus.height);
                try { File.WriteAllBytes(Review + "/frames/" + animal + "/" + action + "/frame_" + captureStatus.frame.ToString("000") + ".png", texture.EncodeToPNG()); }
                finally { Object.DestroyImmediate(texture); }
                captureStatus.frame++;
                if (captureStatus.frame >= captureStatus.frames)
                {
                    captureScene.Dispose(); captureScene = null;
                    captureStatus.clip++; SaveCaptureStatus();
                    if (captureStatus.clip == captureStatus.clips) FinishCapture(null);
                }
            }
            catch (Exception exception) { FinishCapture(exception); Debug.LogException(exception); }
        }

        static void FinishCapture(Exception error)
        {
            EditorApplication.update -= CaptureNextFrame;
            captureScene?.Dispose(); captureScene = null;
            captureStatus.running = false; captureStatus.complete = error == null;
            captureStatus.error = error?.ToString(); SaveCaptureStatus();
        }

        static void SaveCaptureStatus() => File.WriteAllText(Review + "/capture_status.json", JsonUtility.ToJson(captureStatus, true));

        [MenuItem("Weather House/四足动作/检查全部十二动作并截图")]
        public static void ValidateAndCapture()
        {
            Directory.CreateDirectory(Review);
            bool play = EditorApplication.isPlaying;
            var activeScene = EditorSceneManager.GetActiveScene();
            bool dirty = activeScene.isDirty;
            var report = new Report { capturedAt = DateTimeOffset.Now.ToString("o") };
            foreach (string species in QuadrupedMotionPreview.Species)
                foreach (string action in QuadrupedMotionPreview.Actions)
                    report.clips.Add(Check(species, action));
            report.playStateUnchanged = play == EditorApplication.isPlaying;
            report.activeSceneUnchanged = activeScene == EditorSceneManager.GetActiveScene() && dirty == activeScene.isDirty;
            report.passed = report.clips.Count == 12 && report.clips.All(c => c.passed) && report.playStateUnchanged && report.activeSceneUnchanged;
            File.WriteAllText(Review + "/verification.json", JsonUtility.ToJson(report, true));
            if (!report.passed) throw new InvalidOperationException("十二动作检查未全部通过，请查看 " + Review + "/verification.json");
            Debug.Log("QuadrupedMotion: 12 animation clips validated and captured. " + Review + "/verification.json");
        }

        static ClipResult Check(string species, string action)
        {
            var result = new ClipResult { species = species, action = action, source = QuadrupedMotionPreview.ModelPath(species, action), finite = true, minGroundHeight = float.PositiveInfinity };
            try
            {
                result.sourceHash = Hash(result.source);
                using (var scene = new QuadrupedPreviewScene(species, action))
                {
                    result.seconds = scene.Clip.length;
                    result.bones = scene.Skins.SelectMany(s => s.bones).Distinct().Count();
                    result.vertices = scene.Skins.Sum(s => s.sharedMesh.vertexCount);
                    result.uvsValid = scene.Skins.All(s => s.sharedMesh.uv.Length == s.sharedMesh.vertexCount);
                    result.textureWidth = scene.Material.mainTexture.width; result.textureHeight = scene.Material.mainTexture.height;
                    result.materialsValid = scene.Material != null && scene.Material.shader != null && scene.Material.shader.isSupported
                        && scene.Material.mainTexture != null && scene.Skins.All(s => s.sharedMaterials.All(m => m == scene.Material))
                        && AssetDatabase.GetAssetPath(scene.Material.mainTexture) == QuadrupedMotionPreview.Root + "/Textures/" + species + ".png"
                        && result.textureWidth <= 2048 && result.textureHeight <= 2048;
                    scene.Sample(0);
                    result.normalizedHeight = scene.BoundsNow().size.y;
                    Vector3[] first = scene.VerticesNow();
                    Vector3 firstCenter = first.Aggregate(Vector3.zero, (sum, v) => sum + v) / first.Length;
                    for (int i = 0; i <= 24; i++)
                    {
                        scene.Sample(result.seconds * i / 24);
                        Vector3[] points = scene.VerticesNow();
                        Vector3 center = points.Aggregate(Vector3.zero, (sum, v) => sum + v) / points.Length;
                        result.samples++;
                        for (int j = 0; j < points.Length; j++)
                        {
                            Vector3 p = points[j];
                            result.finite &= IsFinite(p.x) && IsFinite(p.y) && IsFinite(p.z);
                            result.minGroundHeight = Mathf.Min(result.minGroundHeight, p.y);
                            result.maxVertexMovement = Mathf.Max(result.maxVertexMovement, Vector3.Distance(first[j], p));
                            result.articulatedMovement = Mathf.Max(result.articulatedMovement, Vector3.Distance(first[j] - firstCenter, p - center));
                        }
                    }
                    scene.Sample(result.seconds * .35f);
                    var texture = scene.Capture(960, 720);
                    result.preview = Review + "/" + species + "_" + action + ".png";
                    File.WriteAllBytes(result.preview, texture.EncodeToPNG());
                    Object.DestroyImmediate(texture);
                }
                result.sourceUnchanged = result.sourceHash == Hash(result.source);
                result.passed = result.finite && result.materialsValid && result.uvsValid && result.sourceUnchanged && result.bones >= 8
                    && result.vertices > 0 && result.seconds > .1f && result.articulatedMovement > .015f
                    && Mathf.Abs(result.normalizedHeight - 2f) < .005f;
                if (!result.passed) result.error = "动画、蒙皮运动、材质或原始文件检查失败。";
            }
            catch (Exception exception) { result.error = exception.ToString(); result.passed = false; }
            return result;
        }

        static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        static string Hash(string path)
        {
            using (var stream = File.OpenRead(path))
            using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "");
        }
    }
}
