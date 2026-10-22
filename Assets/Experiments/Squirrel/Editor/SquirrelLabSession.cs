using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace WeatherHouseSquirrelLab.Editor
{
    [InitializeOnLoad]
    public static class SquirrelLabSession
    {
        const string Folder = "SquirrelLabReview";
        const string Request = Folder + "/request.txt";
        static double nextStatus, nextCapture;
        static int frame;
        static bool recording;
        static string captureFolder;
        static readonly List<float> captureTimes = new List<float>();
        [Serializable] sealed class CaptureInfo { public string project; public bool actualPlay; public float[] times; }
        [Serializable] sealed class TailCheck
        {
            public string actor;
            public bool initialized;
            public float rootPositionError, repeatRotationErrorDegrees, localPositionError, localScaleError, tipSideSpan;
        }
        [Serializable] sealed class TailChecks { public bool actualPlay; public TailCheck[] actors; }

        [Serializable] sealed class SceneInfo { public string path; public bool dirty; }
        [Serializable] sealed class State
        {
            public string project, error, lastCommand;
            public bool playing, compiling, updating;
            public int captureFrames;
            public SceneInfo[] scenes;
        }

        static SquirrelLabSession() { EditorApplication.update += Poll; }

        static void Poll()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            try
            {
                if (recording && EditorApplication.isPlaying && EditorApplication.timeSinceStartup >= nextCapture)
                {
                    captureTimes.Add(Time.time);
                    Capture(captureFolder + "/frame_" + frame.ToString("0000") + ".png");
                    frame++;
                    nextCapture = EditorApplication.timeSinceStartup + .1;
                    if (frame >= 60)
                    {
                        recording = false;
                        SquirrelLabBuilder.ExportReport();
                        File.WriteAllText(captureFolder + "/timing.json", JsonUtility.ToJson(new CaptureInfo
                        { project = Application.dataPath, actualPlay = EditorApplication.isPlaying, times = captureTimes.ToArray() }, true));
                        File.WriteAllText(Folder + "/capture_complete.txt", captureFolder + "\nActual Play camera renders, 60 frames, interval at least 0.1 seconds.");
                    }
                }
                if (EditorApplication.timeSinceStartup < nextStatus) return;
                nextStatus = EditorApplication.timeSinceStartup + .5;
                Directory.CreateDirectory(Folder);
                WriteState(null);
                if (!File.Exists(Request)) return;
                string request = File.ReadAllText(Request).Trim();
                if (request == SessionState.GetString("SquirrelLab.LastRequest", "")) return;
                SessionState.SetString("SquirrelLab.LastRequest", request);
                string command = request.Split('|').Last();
                if (command == "build") SquirrelLabBuilder.Build();
                else if (command == "refresh") AssetDatabase.Refresh();
                else if (command == "bake-expressive") SquirrelExpressiveBaker.Build();
                else if (command == "play")
                {
                    if (Enumerable.Range(0, SceneManager.sceneCount).Any(i => SceneManager.GetSceneAt(i).isDirty))
                        throw new InvalidOperationException("Unsaved scene preserved; cannot replace the current scene setup.");
                    EditorSceneManager.OpenScene(SquirrelLabBuilder.ScenePath, OpenSceneMode.Single);
                    EditorApplication.ExecuteMenuItem("Window/General/Game");
                    EditorApplication.isPlaying = true;
                }
                else if (command == "stop") EditorApplication.isPlaying = false;
                else if (command.StartsWith("capture"))
                {
                    if (!EditorApplication.isPlaying) throw new InvalidOperationException("Capture requires actual Play.");
                    var gallery = UnityEngine.Object.FindObjectOfType<SquirrelMotionGallery>();
                    gallery.SetExpressiveMotion(!command.EndsWith("original"));
                    gallery.Restart();
                    gallery.SetPaused(false);
                    gallery.showPanel = false;
                    int focused = command.Contains("wave") ? 2 : command.Contains("walk") || command == "capture-close" ? 0 : -1;
                    gallery.FocusActor(focused);
                    foreach (var actor in gallery.actors)
                        actor.animator.transform.parent.gameObject.SetActive(focused < 0 || actor == gallery.actors[focused]);
                    gallery.cameraYaw = -18f;
                    gallery.PlaceCamera();
                    captureFolder = Folder + "/PlayCapture_" + DateTime.Now.ToString("HHmmss");
                    Directory.CreateDirectory(captureFolder);
                    frame = 0;
                    captureTimes.Clear();
                    nextCapture = EditorApplication.timeSinceStartup + .3;
                    recording = true;
                }
                else if (command == "report") SquirrelLabBuilder.ExportReport();
                else if (command == "check") CheckTails();
                else if (command == "check-expressive") SquirrelExpressiveChecks.Run();
                else if (command == "show-panel")
                {
                    var gallery = UnityEngine.Object.FindObjectOfType<SquirrelMotionGallery>();
                    foreach (var actor in gallery.actors) actor.animator.transform.parent.gameObject.SetActive(true);
                    gallery.showPanel = true;
                    gallery.SetExpressiveMotion(true);
                }
                else if (command != "status") throw new InvalidOperationException("Unknown squirrel preview command: " + command);
                WriteState(null);
            }
            catch (Exception error)
            {
                recording = false;
                File.WriteAllText(Folder + "/last_error.txt", error.ToString());
                WriteState(error.ToString());
                Debug.LogException(error);
            }
        }

        static void WriteState(string error)
        {
            var state = new State
            {
                project = Application.dataPath, playing = EditorApplication.isPlaying,
                compiling = EditorApplication.isCompiling, updating = EditorApplication.isUpdating,
                error = error, lastCommand = SessionState.GetString("SquirrelLab.LastRequest", ""),
                captureFrames = frame,
                scenes = Enumerable.Range(0, SceneManager.sceneCount).Select(i => new SceneInfo
                { path = SceneManager.GetSceneAt(i).path, dirty = SceneManager.GetSceneAt(i).isDirty }).ToArray()
            };
            File.WriteAllText(Folder + "/editor_state.json", JsonUtility.ToJson(state, true));
        }

        static void Capture(string path)
        {
            var gallery = UnityEngine.Object.FindObjectOfType<SquirrelMotionGallery>();
            if (gallery == null || gallery.galleryCamera == null) throw new InvalidOperationException("Gallery camera missing.");
            Camera camera = gallery.galleryCamera;
            var target = new RenderTexture(1280, 720, 24);
            var previous = RenderTexture.active;
            var previousTarget = camera.targetTexture;
            float previousAspect = camera.aspect;
            var pixels = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            try
            {
                camera.targetTexture = target;
                camera.aspect = 1280f / 720f;
                camera.Render();
                RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
                pixels.Apply();
                File.WriteAllBytes(path, pixels.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture = previousTarget;
                camera.aspect = previousAspect;
                RenderTexture.active = previous;
                UnityEngine.Object.DestroyImmediate(pixels);
                target.Release();
                UnityEngine.Object.DestroyImmediate(target);
            }
        }

        static void CheckTails()
        {
            if (!EditorApplication.isPlaying) throw new InvalidOperationException("Tail validation requires Play.");
            var gallery = UnityEngine.Object.FindObjectOfType<SquirrelMotionGallery>();
            var results = new List<TailCheck>();
            string[] names = { "TailRoot", "Tail02", "Tail03", "Tail04", "Tail05", "Tail06", "Tail07", "Tail08" };
            foreach (var actor in gallery.actors)
            {
                var all = actor.animator.GetComponentsInChildren<Transform>(true);
                var chain = names.Select(n => all.Single(t => t.name == n)).ToArray();
                var positions = chain.Select(t => t.localPosition).ToArray();
                var scales = chain.Select(t => t.localScale).ToArray();
                var check = new TailCheck { actor = actor.id, initialized = actor.tail.Initialize(actor.animator.transform) };
                if (!check.initialized) throw new InvalidOperationException("Tail initialization failed: " + actor.id);
                actor.tail.EvaluateAt(.23f);
                var first = chain.Select(t => t.rotation).ToArray();
                actor.tail.EvaluateAt(.23f);
                for (int i = 0; i < chain.Length; i++)
                    check.repeatRotationErrorDegrees = Mathf.Max(check.repeatRotationErrorDegrees, Quaternion.Angle(first[i], chain[i].rotation));
                Vector3 root = chain[0].position;
                Vector3 side = (all.Single(t => t.name == "Thigh.L").position - all.Single(t => t.name == "Thigh.R").position).normalized;
                float min = float.PositiveInfinity, max = float.NegativeInfinity;
                for (int sample = 0; sample <= 40; sample++)
                {
                    actor.tail.EvaluateAt(sample / (40f * actor.tail.frequency));
                    check.rootPositionError = Mathf.Max(check.rootPositionError, Vector3.Distance(root, chain[0].position));
                    float x = Vector3.Dot(chain[7].position - root, side);
                    min = Mathf.Min(min, x); max = Mathf.Max(max, x);
                    for (int i = 0; i < chain.Length; i++)
                    {
                        check.localPositionError = Mathf.Max(check.localPositionError, Vector3.Distance(positions[i], chain[i].localPosition));
                        check.localScaleError = Mathf.Max(check.localScaleError, Vector3.Distance(scales[i], chain[i].localScale));
                    }
                }
                check.tipSideSpan = max - min;
                if (check.rootPositionError > .0001f || check.repeatRotationErrorDegrees > .1f ||
                    check.localPositionError > .0001f || check.localScaleError > .0001f || check.tipSideSpan < .1f)
                    throw new InvalidOperationException("Tail runtime check failed: " + actor.id + " " + JsonUtility.ToJson(check));
                results.Add(check);
            }
            File.WriteAllText(Folder + "/tail_runtime_checks.json", JsonUtility.ToJson(new TailChecks
            { actualPlay = true, actors = results.ToArray() }, true));
        }
    }
}
