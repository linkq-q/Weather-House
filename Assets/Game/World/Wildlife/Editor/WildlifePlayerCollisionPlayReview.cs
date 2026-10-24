using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace WeatherHouse.Hillside.Editor
{
    // Dedicated disposable validation project only. Never opens or saves a production map.
    [InitializeOnLoad]
    public static class WildlifePlayerCollisionPlayReview
    {
        const string Key = "WeatherHouse.PlayerCollisionReview.";
        const string Report = "PlayerCollisionReview/results.txt";
        static WildlifePlayerCollisionPlayReview() { EditorApplication.playModeStateChanged += Changed; }

        public static void Begin()
        {
            if (!File.Exists(".wildlife-journey-validation") || EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Run only in an isolated Edit-mode project containing the .wildlife-journey-validation marker.");
            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Validation cannot discard a dirty scene.");
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            if (!EditorSceneManager.SaveScene(scene, "Assets/PlayerCollisionValidation.unity"))
                throw new InvalidOperationException("Could not save the isolated validation scene.");
            Directory.CreateDirectory(Path.GetDirectoryName(Report));
            File.WriteAllText(Report, "RUNNING actual Play; native default-physics CharacterController and production controller entrypoint.\n");
            SessionState.SetBool(Key + "running", true); SessionState.SetBool(Key + "done", false);
            EditorApplication.isPlaying = true;
        }

        static void Changed(PlayModeStateChange state)
        {
            if (!SessionState.GetBool(Key + "running", false)) return;
            if (state == PlayModeStateChange.EnteredPlayMode) EditorApplication.delayCall += Run;
            if (state == PlayModeStateChange.EnteredEditMode)
            {
                bool passed = SessionState.GetBool(Key + "done", false) && SessionState.GetBool(Key + "passed", false);
                File.AppendAllText(Report, "HANDOFF Play=False dirty=" + SceneManager.GetActiveScene().isDirty + "\n");
                SessionState.SetBool(Key + "running", false);
                EditorApplication.Exit(passed ? 0 : 1);
            }
        }

        static void Run()
        {
            bool passed = true;
            // Keep the controller entrypoint independent: a missing production hook cannot hide behind an earlier failure.
            foreach (Func<string> check in new Func<string>[] { WildlifePlayerCollisionChecks.RunControllerChecks,
                WildlifePlayerCollisionChecks.RunVerticalChecks, WildlifePlayerCollisionChecks.RunLandingChecks,
                WildlifePlayerCollisionChecks.RunPlayChecks, WildlifePlayerCollisionChecks.RunShapeChecks,
                WildlifePlayerCollisionChecks.RunNativeWallChecks })
            {
                try { File.AppendAllText(Report, check() + "\n"); }
                catch (Exception error) { passed = false; File.AppendAllText(Report, "FAIL " + error + "\n"); Debug.LogException(error); }
            }
            SessionState.SetBool(Key + "passed", passed); SessionState.SetBool(Key + "done", true);
            EditorApplication.isPlaying = false;
        }
    }
}
