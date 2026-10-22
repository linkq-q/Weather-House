using System;
using System.IO;
using System.Linq;
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
                    Capture(captureFolder + "/frame_" + frame.ToString("0000") + ".png");
                    frame++;
                    nextCapture = EditorApplication.timeSinceStartup + .1;
                    if (frame >= 60)
                    {
                        recording = false;
                        SquirrelLabBuilder.ExportReport();
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
                else if (command == "play")
                {
                    if (Enumerable.Range(0, SceneManager.sceneCount).Any(i => SceneManager.GetSceneAt(i).isDirty))
                        throw new InvalidOperationException("Unsaved scene preserved; cannot replace the current scene setup.");
                    EditorSceneManager.OpenScene(SquirrelLabBuilder.ScenePath, OpenSceneMode.Single);
                    EditorApplication.ExecuteMenuItem("Window/General/Game");
                    EditorApplication.isPlaying = true;
                }
                else if (command == "stop") EditorApplication.isPlaying = false;
                else if (command == "capture")
                {
                    if (!EditorApplication.isPlaying) throw new InvalidOperationException("Capture requires actual Play.");
                    var gallery = UnityEngine.Object.FindObjectOfType<SquirrelMotionGallery>();
                    gallery.Restart();
                    gallery.SetPaused(false);
                    gallery.showPanel = false;
                    gallery.cameraYaw = -18f;
                    gallery.PlaceCamera();
                    captureFolder = Folder + "/PlayCapture_" + DateTime.Now.ToString("HHmmss");
                    Directory.CreateDirectory(captureFolder);
                    frame = 0;
                    nextCapture = EditorApplication.timeSinceStartup + .3;
                    recording = true;
                }
                else if (command == "report") SquirrelLabBuilder.ExportReport();
                else if (command == "show-panel") UnityEngine.Object.FindObjectOfType<SquirrelMotionGallery>().showPanel = true;
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
            var pixels = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            try
            {
                camera.targetTexture = target;
                camera.Render();
                RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
                pixels.Apply();
                File.WriteAllBytes(path, pixels.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture = previousTarget;
                RenderTexture.active = previous;
                UnityEngine.Object.DestroyImmediate(pixels);
                target.Release();
                UnityEngine.Object.DestroyImmediate(target);
            }
        }
    }
}
