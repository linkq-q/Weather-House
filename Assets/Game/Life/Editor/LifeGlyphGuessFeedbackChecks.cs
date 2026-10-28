using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using WeatherHousePrototype;

namespace WeatherHouse.Life.Editor
{
    // Real submission entry and callbacks; synthetic vocabulary is confined to an isolated save and restored.
    public static class LifeGlyphGuessFeedbackChecks
    {
        const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;

        [MenuItem("Weather House/Review 20261027/Check guess feedback isolated")]
        public static void Begin()
        {
            var life = UnityEngine.Object.FindObjectOfType<LifeSession>();
            if (!Application.isPlaying || !life) throw new InvalidOperationException("Start isolated Play before checking guess feedback.");
            life.StartCoroutine(Run(life, result => Debug.Log(result)));
        }

        public static IEnumerator Run(LifeSession life, Action<string> completed)
        {
            string directory = Path.GetFullPath(GameplaySession.PersistenceDirectory);
            string reviewRoot = Path.GetFullPath("LifeGameplayReview") + Path.DirectorySeparatorChar;
            if (!Application.isPlaying || !life || string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WEATHERHOUSE_LIFE_REVIEW_SAVE")) ||
                !directory.StartsWith(reviewRoot, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Guess feedback checks require isolated Play saves.");
            var game = life.Gameplay;
            var hud = game.GetComponent<GameplayHud>();
            var controller = game.GetComponent<LifeOnboarding>();
            if (!controller || !hud || hud.JournalOpen || hud.CameraMode)
                throw new InvalidOperationException("Guess feedback checks require an idle HUD and onboarding component.");
            var checks = new List<string>();
            void Check(bool condition, string label) => checks.Add((condition ? "PASS " : "FAIL ") + label);
            string notebook = JsonUtility.ToJson(game.Notebook), status = game.Status;
            string[] fields = { "<State>k__BackingField", "ready", "guessJudge", "revision", "judging", "feedback", "feedbackUntil" };
            var prior = fields.ToDictionary(name => name, name => Get(controller, name));
            string[] paths = { game.SavePath, controller.StatePath };
            var files = paths.ToDictionary(path => path, path => File.Exists(path) ? File.ReadAllBytes(path) : null);
            var oldSuppressed = typeof(LifeOnboarding).GetField("reviewSuppressed", BindingFlags.Static | BindingFlags.NonPublic);
            bool suppressed = (bool)oldSuppressed.GetValue(null);
            try
            {
                oldSuppressed.SetValue(null, false);
                JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(new LanguageNotebook()), game.Notebook);
                game.Notebook.Normalize(game.Catalog);
                game.Notebook.Observe("guess-fixture", "word", new[] { "hello" });
                var state = new LifeOnboardingState { started = true, stage = LifeOnboardingStage.Complete };
                Set(controller, "<State>k__BackingField", state); Set(controller, "ready", true);
                Set(controller, "judging", false); Set(controller, "feedback", ""); Set(controller, "feedbackUntil", 0f);
                Set(controller, "guessJudge", Judge(game.Catalog, new JudgementResult { success = true, understood = false }));
                hud.ToggleJournal();
                float deadline = Time.realtimeSinceStartup + 15;
                while (hud.IsAlbumOpening && Time.realtimeSinceStartup < deadline) yield return null;
                if (hud.IsAlbumOpening) throw new TimeoutException("Book did not open for guess feedback checks.");
                hud.OnboardingOpenWord("hello");
                game.Notify("before rejected guess");
                game.SaveGuess("hello", "石头");
                yield return WaitForJudgement(controller);
                Check(game.Notebook.GetWord("hello").guess == "石头" && !game.Notebook.GetWord("hello").confirmed,
                    "Rejected guess remains editable and does not confirm the word");
                Check(game.Status == "before rejected guess" && (string)Get(controller, "feedback") == "",
                    "Rejected guess produces no automatic failure feedback");
                Check(state.HintsAvailable == 0 && state.hintsSpent == 0, "Rejected guess never consumes hint credits");

                Set(controller, "guessJudge", Judge(game.Catalog, new JudgementResult { success = false, error = "fixture unavailable" }));
                game.SaveGuess("hello", "朋友之间表示关心");
                yield return WaitForJudgement(controller);
                Check(game.Notebook.GetWord("hello").guess == "朋友之间表示关心" && !game.Notebook.GetWord("hello").confirmed && game.Status.Contains("重试"),
                    "Unavailable judgement preserves the guess and explains that retry is possible");

                game.SaveGuess("hello", "你好");
                yield return WaitForJudgement(controller);
                Check(game.Notebook.GetWord("hello").confirmed && game.Status.Contains("猜对"), "Correct local guess confirms through the production submission entry");
                int earned = state.earnedWords.Count;
                game.SaveGuess("hello", "你好"); yield return null;
                Check(state.earnedWords.Count == earned && state.hintsSpent == 0, "Repeated confirmed submission creates no extra hint credit");

                game.Notebook.Observe("guess-fixture", "legacy-word", new[] { "goodbye" });
                Set(controller, "<State>k__BackingField", null); Set(controller, "ready", false);
                hud.OnboardingOpenWord("goodbye");
                game.SaveGuess("goodbye", "再见");
                yield return WaitForJudgement(controller);
                Check(game.Notebook.GetWord("goodbye").confirmed && controller.State == null,
                    "A legacy save without tutorial state can confirm a dictionary guess without starting onboarding");
            }
            finally
            {
                if (hud.JournalOpen) hud.ToggleJournal();
                Set(controller, "revision", (int)Get(controller, "revision") + 1);
                JsonUtility.FromJsonOverwrite(notebook, game.Notebook);
                foreach (string name in fields) Set(controller, name, prior[name]);
                oldSuppressed.SetValue(null, suppressed);
                foreach (var file in files)
                    if (file.Value != null) File.WriteAllBytes(file.Key, file.Value);
                    else if (File.Exists(file.Key)) File.Delete(file.Key);
                game.Notify(status);
            }
            string result = (checks.Any(line => line.StartsWith("FAIL ", StringComparison.Ordinal)) ? "FAIL" : "PASS") +
                " guess feedback\n" + string.Join("\n", checks);
            File.WriteAllText(Path.Combine(directory, "guess-feedback-checks.txt"), result);
            completed?.Invoke(result);
        }

        static IEnumerator WaitForJudgement(LifeOnboarding controller)
        {
            yield return null;
            float deadline = Time.realtimeSinceStartup + 3;
            while ((bool)Get(controller, "judging") && Time.realtimeSinceStartup < deadline) yield return null;
            if ((bool)Get(controller, "judging")) throw new TimeoutException("Guess judgement did not finish.");
        }
        static LifeGlyphGuessJudge Judge(GlyphCatalog catalog, JudgementResult result)
        {
            IEnumerator Reply(JudgementInput input, Action<JudgementResult> complete, Func<bool> cancelled)
            { yield return null; complete(result); }
            var callback = new Func<JudgementInput, Action<JudgementResult>, Func<bool>, IEnumerator>(Reply);
            var constructor = typeof(LifeGlyphGuessJudge).GetConstructor(Hidden, null, new[] { typeof(GlyphCatalog), callback.GetType() }, null);
            return (LifeGlyphGuessJudge)constructor.Invoke(new object[] { catalog, callback });
        }
        static object Get(object target, string name) => target.GetType().GetField(name, Hidden).GetValue(target);
        static void Set(object target, string name, object value) => target.GetType().GetField(name, Hidden).SetValue(target, value);
    }
}
