using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace WeatherHouseToon.Editor
{
    // Local, explicit requests only; no automatic scene changes on project open.
    [InitializeOnLoad]
    public static class ToonPreviewSession
    {
        const string Review = "ToonPreviewReview";
        static double nextPoll;
        static ToonPreviewSession() { EditorApplication.update += Poll; }
        static void Poll()
        {
            if (EditorApplication.timeSinceStartup < nextPoll || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            nextPoll = EditorApplication.timeSinceStartup + 1;
            string request = Review + "/request.txt";
            if (!File.Exists(request)) return;
            string command = File.ReadAllText(request).Trim();
            File.Delete(request);
            try
            {
                if (command == "build") { MagicalGirlToonBuilder.Build(); return; }
                if (command == "capture") { MagicalGirlToonBuilder.Validate(); MagicalGirlToonBuilder.Capture(); return; }
                if (command == "warm") { MagicalGirlToonBuilder.WarmLight(); return; }
                if (command == "runtime") { MagicalGirlToonBuilder.RuntimeCheck(); return; }
                if (command == "play")
                {
                    if (SceneManager.GetActiveScene().path != MagicalGirlToonBuilder.ScenePath)
                        throw new InvalidOperationException("Only enter Play in the new preview scene.");
                    EditorApplication.isPlaying = true;
                    var gameView = typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.GameView");
                    EditorWindow.GetWindow(gameView).Focus();
                    return;
                }
                if (command == "stop") { EditorApplication.isPlaying = false; return; }
                if (command != "inspect") throw new InvalidOperationException("Unsupported request: " + command);
                var scenes = Enumerable.Range(0, SceneManager.sceneCount).Select(i => SceneManager.GetSceneAt(i)).ToArray();
                string state = "project=" + Application.dataPath + "\nplaying=" + EditorApplication.isPlaying + "\n";
                state += string.Join("\n", scenes.Select(s => "scene=" + s.path + " dirty=" + s.isDirty));
                state += "\npipeline=" + UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline.name;
                File.WriteAllText(Review + "/editor-state.txt", state);
            }
            catch (Exception e) { File.WriteAllText(Review + "/request-error.txt", e.ToString()); Debug.LogException(e); }
        }
    }
}
