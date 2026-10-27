using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using WeatherHousePrototype;

namespace WeatherHouse.Life.Editor
{
    [InitializeOnLoad]
    public static partial class LifeOnboardingReview
    {
        const string Key = "WeatherHouse.Onboarding.IsolatedReview";
        const string Folder = "LifeGameplayReview/20261027/onboarding";
        static LifeOnboardingReview()
        {
            EditorApplication.playModeStateChanged += change =>
            {
                ObserveSupplementalReviewState(change);
                if (!SessionState.GetBool(Key, false)) return;
                if (change == PlayModeStateChange.EnteredPlayMode)
                {
                    Application.runInBackground = true;
                    var gameViewType = typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView");
                    if (gameViewType != null) EditorWindow.GetWindow(gameViewType).Focus();
                }
                if (change == PlayModeStateChange.EnteredEditMode)
                {
                    try { VerifyRealFiles(); }
                    finally
                    {
                        Environment.SetEnvironmentVariable("WEATHERHOUSE_LIFE_REVIEW_SAVE", EmptyNull(SessionState.GetString(Key + ".PreviousSave", "")));
                        Environment.SetEnvironmentVariable(LifeOnboarding.ReviewOptIn, EmptyNull(SessionState.GetString(Key + ".PreviousOpt", "")));
                        Application.runInBackground = SessionState.GetBool(Key + ".PreviousBackground", false);
                        SessionState.SetBool(Key, false);
                        Directory.CreateDirectory(Folder); File.WriteAllText(Folder + "/handoff.txt", "Play=False\nDirty=" + SceneManager.GetActiveScene().isDirty + "\n" + DateTime.UtcNow.ToString("O"));
                    }
                }
            };
        }
        [MenuItem("Weather House/Review 20261027/Check onboarding state")]
        public static void CheckState()
        {
            var checks = new List<string>(); Action<bool, string> test = (ok, label) => { if (!ok) throw new InvalidOperationException(label); checks.Add(label); };
            string review = Path.GetFullPath(Folder + "/guard-fixture");
            test(LifeOnboardingState.ShouldStart(false, false, false, false, review), "Fresh ordinary save starts");
            test(!LifeOnboardingState.ShouldStart(true, false, false, false, review), "Existing notebook never automatically restarts");
            test(!LifeOnboardingState.ShouldStart(false, true, false, false, review), "Existing life progress never automatically restarts");
            test(!LifeOnboardingState.ShouldStart(false, false, true, false, review), "Existing UI/world isolated reviews suppress fresh onboarding");
            test(LifeOnboardingState.ShouldStart(false, false, true, true, review), "Dedicated isolated review explicitly opts in");
            test(!LifeOnboardingState.ShouldStart(false, false, true, true, Application.persistentDataPath), "Opt-in refuses real persistent path");
            var state = new LifeOnboardingState { started = true, stage = LifeOnboardingStage.GuessBook };
            state.Observe("animal-a", new[] { "hello" }); state.Observe("animal-a", new[] { "hello" });
            state.Observe("animal-a", new[] { "goodbye" });
            test(state.animalsSeen.Count == 1 && state.helloSeen && state.goodbyeSeen, "Repeat greetings never manufacture three animals");
            state.Observe("animal-b", new[] { "hello" }); state.Observe("animal-c", new[] { "hello" });
            test(state.animalsSeen.Count == 3, "Three stable distinct animal IDs count");
            var book = new LanguageNotebook { words = new List<GlyphKnowledge> { new GlyphKnowledge { glyphId = "want", confirmed = true }, new GlyphKnowledge { glyphId = "reject", confirmed = true } } };
            state.TrackConfirmed(book); test(state.HintsAvailable == 0, "Initial want/reject never earn hint credit");
            foreach (string id in new[] { "hello", "goodbye", "rain" }) book.words.Add(new GlyphKnowledge { glyphId = id, confirmed = true });
            state.TrackConfirmed(book); state.TrackConfirmed(book);
            test(state.HintsAvailable == 1 && state.earnedWords.Count == 3, "Three first confirmations grant exactly one repeat-safe hint");
            state.teachingHintAvailable = true;
            test(state.TryReadHint("hello", true) && state.HintsAvailable == 1, "Free failed-guess teaching hint is separate");
            test(state.TryReadHint("hello", true) && state.HintsAvailable == 1, "Rereading the same hint is free");
            test(state.TryReadHint("rain", false) && state.HintsAvailable == 0, "New paid hint consumes once");
            test(!state.TryReadHint("water", false) && state.HintsAvailable == 0, "Empty hint inventory does not become negative");
            string json = JsonUtility.ToJson(state); var restored = JsonUtility.FromJson<LifeOnboardingState>(json); restored.Normalize();
            test(restored.stage == state.stage && restored.earnedWords.Count == 3 && restored.HintsAvailable == 0 && restored.teachingHintRead, "Sidecar state roundtrips stage, inventory and free-hint usage");
            Directory.CreateDirectory(Folder); File.WriteAllText(Folder + "/state-checks.txt", "PASS\n" + string.Join("\n", checks));
            Debug.Log("PASS onboarding state: " + checks.Count + " assertions; no real saves or scenes modified.");
        }
        [MenuItem("Weather House/试玩/新手引导（独立存档）")]
        [MenuItem("Weather House/Review 20261027/Review onboarding isolated")]
        public static void Begin()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) throw new InvalidOperationException("Start only after root returns to Edit mode.");
            var scene = SceneManager.GetActiveScene();
            if (scene.path != "Assets/Scenes/WeatherHouse_WatersideV2.unity") throw new InvalidOperationException("Open the existing WatersideV2 scene first; this entry never opens scenes.");
            CheckState(); Directory.CreateDirectory(Folder);
            if (scene.isDirty)
            {
                string backup = Folder + "/before-onboarding-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmssfff") + ".unity";
                if (!EditorSceneManager.SaveScene(scene, backup, true) || !scene.isDirty) throw new InvalidOperationException("Could not preserve a copy of the dirty scene without saving the original.");
            }
            SessionState.SetString(Key + ".PreviousSave", Environment.GetEnvironmentVariable("WEATHERHOUSE_LIFE_REVIEW_SAVE") ?? "");
            SessionState.SetString(Key + ".PreviousOpt", Environment.GetEnvironmentVariable(LifeOnboarding.ReviewOptIn) ?? "");
            var real = RealPaths().Select(path => path + "|" + Hash(path)).ToArray(); SessionState.SetString(Key + ".RealHashes", string.Join("\n", real));
            string directory = Path.GetFullPath(Folder + "/run-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmssfff")); Directory.CreateDirectory(directory);
            SessionState.SetBool(Key + ".PreviousBackground", Application.runInBackground);
            Environment.SetEnvironmentVariable("WEATHERHOUSE_LIFE_REVIEW_SAVE", directory);
            Environment.SetEnvironmentVariable(LifeOnboarding.ReviewOptIn, "1"); SessionState.SetBool(Key, true);
            File.WriteAllText(Folder + "/interactive-status.txt", "RUNNING_INTERACTIVE\n" + directory + "\nFollow the real tutorial. This is not a claimed automated end-to-end pass.\n");
            EditorApplication.isPaused = false; EditorApplication.isPlaying = true;
        }
        [MenuItem("Weather House/试玩/结束新手引导试玩")]
        [MenuItem("Weather House/Review 20261027/Finish onboarding isolated")]
        public static void Finish()
        {
            if (!SessionState.GetBool(Key, false) || !EditorApplication.isPlaying) throw new InvalidOperationException("No dedicated onboarding review is running.");
            var tutorial = UnityEngine.Object.FindObjectOfType<LifeOnboarding>();
            bool complete = tutorial && tutorial.State?.stage == LifeOnboardingStage.Complete;
            File.WriteAllText(Folder + "/interactive-status.txt", (complete ? "COMPLETE_REQUIRES_IMAGE_AND_FLOW_REVIEW" : "INCOMPLETE_INTERACTIVE") + "\n" +
                (tutorial ? JsonUtility.ToJson(tutorial.State, true) : "No controller") + "\nNo synthetic tutorial progress was injected.\n");
            EditorApplication.isPlaying = false;
        }
        [MenuItem("Weather House/Review 20261027/Resume onboarding flow in current isolated Play")]
        [MenuItem("Weather House/Review 20261027/Check onboarding flow in current isolated Play")]
        public static void CheckCurrentFlow()
        {
            if (!SessionState.GetBool(Key, false) || !EditorApplication.isPlaying)
                throw new InvalidOperationException("Only the dedicated onboarding isolated Play entry may run this check.");
            var life = UnityEngine.Object.FindObjectOfType<LifeSession>();
            if (!life) throw new InvalidOperationException("The current isolated Play has no LifeSession.");
            life.StartCoroutine(LifeOnboardingFlowChecks.Run(life, result =>
            { Directory.CreateDirectory(Folder); File.WriteAllText(Folder + "/flow-checks.txt", result + "\n" + DateTime.UtcNow.ToString("O")); }));
        }
        [MenuItem("Weather House/Review 20261027/Resume saved onboarding isolated")]
        public static void ResumeSaved()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) throw new InvalidOperationException("Resume only in Edit mode.");
            var scene = SceneManager.GetActiveScene();
            if (scene.path != "Assets/Scenes/WeatherHouse_WatersideV2.unity") throw new InvalidOperationException("Keep the existing formal scene open; this entry never opens scenes.");
            string directory = Directory.GetDirectories(Folder, "run-*").Where(d => File.Exists(Path.Combine(d, "onboarding.json")))
                .OrderByDescending(d => Directory.GetLastWriteTimeUtc(d)).FirstOrDefault();
            if (directory == null) throw new InvalidOperationException("No prior dedicated onboarding checkpoint exists.");
            directory = Path.GetFullPath(directory);
            if (!directory.StartsWith(Path.GetFullPath(Folder) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Resume path must stay in dedicated Review.");
            if (scene.isDirty && !EditorSceneManager.SaveScene(scene, Folder + "/before-resume-" + DateTime.UtcNow.ToString("HHmmssfff") + ".unity", true))
                throw new InvalidOperationException("Could not preserve a copy of the dirty scene.");
            SessionState.SetString(Key + ".PreviousSave", Environment.GetEnvironmentVariable("WEATHERHOUSE_LIFE_REVIEW_SAVE") ?? "");
            SessionState.SetString(Key + ".PreviousOpt", Environment.GetEnvironmentVariable(LifeOnboarding.ReviewOptIn) ?? "");
            SessionState.SetString(Key + ".RealHashes", string.Join("\n", RealPaths().Select(path => path + "|" + Hash(path))));
            Environment.SetEnvironmentVariable("WEATHERHOUSE_LIFE_REVIEW_SAVE", directory); Environment.SetEnvironmentVariable(LifeOnboarding.ReviewOptIn, "1"); SessionState.SetBool(Key, true);
            File.WriteAllText(Folder + "/resume-receipt.txt", "RESUMING_EXISTING_CHECKPOINT\n" + directory + "\nNo save file reset or tutorial stage assignment.\n");
            EditorApplication.isPaused = false; EditorApplication.isPlaying = true;
        }
        static IEnumerable<string> RealPaths() => new[] { "language-notebook.json", "life-progress.json", "onboarding.json" }.Select(file => Path.Combine(Application.persistentDataPath, file));
        static void VerifyRealFiles()
        {
            foreach (string line in SessionState.GetString(Key + ".RealHashes", "").Split('\n'))
            { if (string.IsNullOrEmpty(line)) continue; int split = line.LastIndexOf('|'); string path = line.Substring(0, split); if (Hash(path) != line.Substring(split + 1)) throw new InvalidOperationException("Real save changed during isolated onboarding: " + path); }
            File.WriteAllText(Folder + "/real-save-protection.txt", "PASS all three real save file hashes unchanged\n" + DateTime.UtcNow.ToString("O"));
        }
        static string Hash(string path)
        { if (!File.Exists(path)) return "MISSING"; using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-", ""); }
        static string EmptyNull(string value) => string.IsNullOrEmpty(value) ? null : value;
    }
}
