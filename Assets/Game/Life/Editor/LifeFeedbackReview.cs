using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using WeatherHousePrototype;

namespace WeatherHouse.Life.Editor
{
    // Explicit isolated review only. Never saves the scene or uses the player's save directory.
    [InitializeOnLoad]
    public static class LifeFeedbackReview
    {
        const string Key = "WeatherHouse.FeedbackReview20261028";
        const string Folder = "LifeGameplayReview/20261028/feedback";
        static LifeFeedbackReview()
        {
            EditorApplication.update += ObserveReady;
            EditorApplication.playModeStateChanged += state =>
            {
                if (state != PlayModeStateChange.EnteredEditMode || !SessionState.GetBool(Key, false)) return;
                Environment.SetEnvironmentVariable("WEATHERHOUSE_LIFE_REVIEW_SAVE", EmptyNull(SessionState.GetString(Key + ".save", "")));
                Environment.SetEnvironmentVariable(LifeOnboarding.ReviewOptIn, EmptyNull(SessionState.GetString(Key + ".opt", "")));
                SessionState.SetBool(Key, false);
                File.WriteAllText(Folder + "/handoff.txt", "Play=False\nDirty=" + SceneManager.GetActiveScene().isDirty + "\n" + DateTime.UtcNow.ToString("O"));
            };
        }
        static string EmptyNull(string value) => string.IsNullOrEmpty(value) ? null : value;

        [MenuItem("Weather House/Review 20261027/Begin feedback isolated")]
        public static void Begin()
        {
            if (!Application.isBatchMode || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || SceneManager.GetActiveScene().isDirty)
                throw new InvalidOperationException("This driver requires an idle, clean batch editor owned by the feedback review.");
            Directory.CreateDirectory(Folder);
            var catalog = JsonUtility.FromJson<GlyphCatalog>(Resources.Load<TextAsset>("WeatherHouseGlyphs").text);
            File.WriteAllLines(Folder + "/unity-photo-model.txt", LifePhotoFlowChecks.CheckModel(catalog));
            EditorSceneManager.OpenScene("Assets/Scenes/WeatherHouse_WatersideV2.unity", OpenSceneMode.Single);
            SessionState.SetString(Key + ".save", Environment.GetEnvironmentVariable("WEATHERHOUSE_LIFE_REVIEW_SAVE") ?? "");
            SessionState.SetString(Key + ".opt", Environment.GetEnvironmentVariable(LifeOnboarding.ReviewOptIn) ?? "");
            string directory = Path.GetFullPath(Folder + "/run-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmssfff"));
            Directory.CreateDirectory(directory);
            Environment.SetEnvironmentVariable("WEATHERHOUSE_LIFE_REVIEW_SAVE", directory);
            Environment.SetEnvironmentVariable(LifeOnboarding.ReviewOptIn, null);
            SessionState.SetBool(Key, true); SessionState.SetBool(Key + ".ready", false);
            File.WriteAllText(Folder + "/current-run.txt", directory);
            EditorApplication.isPaused = false; EditorApplication.isPlaying = true;
        }
        static void ObserveReady()
        {
            if (!SessionState.GetBool(Key, false) || SessionState.GetBool(Key + ".ready", false) || !EditorApplication.isPlaying || EditorApplication.isCompiling) return;
            var life = UnityEngine.Object.FindObjectOfType<LifeSession>();
            if (!life || !life.World || !life.Gameplay || !life.Gameplay.GetComponent<GameplayHud>().OnboardingReady) return;
            SessionState.SetBool(Key + ".ready", true);
            File.WriteAllText(Folder + "/ready.txt", "READY isolated formal-scene Play\n" + GameplaySession.PersistenceDirectory + "\n" + DateTime.UtcNow.ToString("O"));
        }
        [MenuItem("Weather House/Review 20261027/Check feedback spell access")]
        public static void CheckSpellAccess()
        {
            if (!SessionState.GetBool(Key, false) || !EditorApplication.isPlaying) throw new InvalidOperationException("Only the owned isolated Play may run this check.");
            var life = UnityEngine.Object.FindObjectOfType<LifeSession>();
            var game = life.Gameplay;
            var checks = new List<string>();
            void Check(bool value, string label) { if (!value) throw new InvalidOperationException(label); checks.Add("PASS " + label); }
            Check(!life.IsGestureLearned("rain") && life.IsGestureUnlocked("rain"), "Fresh rain is open without practice");
            Check(life.TryCast("rain", out _) && life.LocalRainActive, "Unpractised rain casts through the real LifeSession and weather components");
            Check(life.TryCast("clear", out _) && !life.LocalRainActive, "Unpractised clear ends the active weather");
            Check(!life.TryCast("wind", out _), "Wind without a photo remains unavailable");
            game.Notebook.AddObservation(new ObservationRecord { id = "feedback-spell-photo", glyphIds = new[] { "wind", "weak", "strong" }, frameCount = 1 });
            Check(life.IsGestureUnlocked("wind") && !game.Notebook.GetWord("wind").confirmed && !life.IsGestureLearned("wind"), "Photo evidence opens wind without claiming understanding or practice");
            Check(life.TryCast("wind", out _) && life.TryCast("strong", out _) && life.TryCast("weak", out _), "Photographed wind and modifiers cast before practice");
            Check(life.TryCast("clear", out _), "Clear remains available after wind and strength changes");
            File.WriteAllLines(Path.Combine(GameplaySession.PersistenceDirectory, "spell-access-checks.txt"), checks);
        }
        [MenuItem("Weather House/Review 20261027/Finish feedback isolated")]
        public static void Finish()
        {
            if (!SessionState.GetBool(Key, false)) throw new InvalidOperationException("No feedback-owned review is running.");
            EditorApplication.isPlaying = false;
        }
        [MenuItem("Weather House/Review 20261027/Exit feedback batch editor")]
        public static void ExitBatch()
        {
            if (!Application.isBatchMode || EditorApplication.isPlayingOrWillChangePlaymode || SessionState.GetBool(Key, false))
                throw new InvalidOperationException("Finish the isolated batch review before exiting.");
            EditorApplication.Exit(0);
        }
        [MenuItem("Weather House/Review 20261027/Refresh feedback sources")]
        public static void RefreshSources()
        {
            if (!Application.isBatchMode || EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Refresh feedback code only between isolated Play runs.");
            AssetDatabase.Refresh();
        }
        [MenuItem("Weather House/Review 20261027/Check feedback social greetings")]
        public static void CheckSocialGreetings()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Run social checks after finishing feedback Play.");
            Directory.CreateDirectory(Folder);
            File.WriteAllText(Folder + "/social-greeting-checks.txt", WeatherHouse.WildlifeJourney.Editor.JourneySocialChecks.RunLogicChecks());
        }
    }
}
