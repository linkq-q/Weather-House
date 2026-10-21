using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace WeatherHouse.Vegetation.Editor
{
    // Explicit local requests only; opening the project never starts a test or changes a scene.
    [InitializeOnLoad]
    public static class VegetationLabSession
    {
        const string Folder = "VegetationLabReview";
        static double nextPoll;
        static VegetationLabSession() { EditorApplication.update += Poll; }
        static void Poll()
        {
            if (EditorApplication.timeSinceStartup < nextPoll || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            nextPoll = EditorApplication.timeSinceStartup + 1;
            string request = Folder + "/request.txt";
            if (!File.Exists(request)) return;
            string command = File.ReadAllText(request).Trim();
            File.Delete(request);
            try
            {
                var scene = SceneManager.GetActiveScene();
                if (command == "refresh") { AssetDatabase.Refresh(); return; }
                if (command == "inspect")
                {
                    File.WriteAllText(Folder + "/editor-state.txt", "project=" + Application.dataPath + "\nplaying=" + EditorApplication.isPlaying + "\nscene=" + scene.path + "\ndirty=" + scene.isDirty);
                    return;
                }
                if (command != "verify" && command != "upgrade-verify" && command != "style-verify") throw new InvalidOperationException("Unsupported vegetation request: " + command);
                if (EditorApplication.isPlaying || scene.isDirty || SceneManager.sceneCount != 1)
                    throw new InvalidOperationException("Checks need one saved scene outside Play; user edits were preserved.");
                File.WriteAllText(Folder + "/request-error.txt", "");
                SessionState.SetString("WeatherHouse.VegetationLab.ReturnScene", scene.path == VegetationLabBuilder.ScenePath ? "" : scene.path);
                if (command == "style-verify")
                {
                    VegetationLabUpgrade.Apply();
                    PainterlyTreeEditor.RefreshLabSamples();
                    UnityEditor.SceneManagement.EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
                    VegetationLabChecks.BuildAndVerify();
                }
                else if (command == "upgrade-verify") VegetationLabUpgrade.ApplyAndVerify();
                else VegetationLabChecks.BuildAndVerify();
            }
            catch (Exception error) { File.WriteAllText(Folder + "/request-error.txt", error.ToString()); Debug.LogException(error); }
        }
    }
}
