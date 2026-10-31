using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
using WeatherHousePrototype;

namespace WeatherHouse.Life.Editor
{
    public static class LifePersistencePlayChecks
    {
        // Uses the same callback as a live AI response, but isolates the filesystem failure itself.
        public static string Run(LifeSession life)
        {
            string originalRoot = Environment.GetEnvironmentVariable("WEATHERHOUSE_LIFE_REVIEW_SAVE");
            if (!Application.isPlaying || !life || string.IsNullOrWhiteSpace(originalRoot))
                throw new InvalidOperationException("Persistence failure checks require the isolated Play review.");
            var game = life.Gameplay; var speaker = life.World.FindSpeaker("dog");
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            if (!speaker || (bool)typeof(LifeSession).GetField("saveBlocked", flags).GetValue(life))
                throw new InvalidOperationException("The fixture needs an initialized speaker and working life-save state.");
            int topic = speaker.topicIndex;
            Vector3 position = game.PlayerTransform.position;
            string progress = JsonUtility.ToJson(life.Progress), notebook = JsonUtility.ToJson(game.Notebook), world = life.World.CaptureJson(), status = game.Status;
            string root = Path.GetFullPath(originalRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), fixture = Path.Combine(root, "ui-save-failure-" + Guid.NewGuid().ToString("N"));
            if (!Path.GetFullPath(fixture).StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                throw new InvalidOperationException("Persistence fixture must stay in the isolated save.");
            string blocker = Path.Combine(fixture, "life-progress.json.tmp");
            try
            {
                Directory.CreateDirectory(fixture);
                Environment.SetEnvironmentVariable("WEATHERHOUSE_LIFE_REVIEW_SAVE", fixture);
                for (int i = 0; i < 12; i++)
                {
                    life.Progress.rainTutorialSeen = i % 2 == 0;
                    game.Notebook.GetWord("want").guess = "persistence roundtrip " + i;
                    if (!(bool)typeof(LifeSession).GetMethod("SaveProgress", flags).Invoke(life, null))
                        throw new InvalidOperationException("Life save roundtrip " + i + ": " + life.LastSaveError);
                    if (!game.SaveNotebook()) throw new InvalidOperationException("Notebook save roundtrip " + i + ": " + game.LastSaveError);
                    var readLife = JsonUtility.FromJson<LifeProgress>(File.ReadAllText(life.SavePath));
                    var readNotebook = JsonUtility.FromJson<LanguageNotebook>(File.ReadAllText(game.SavePath));
                    if (readLife.rainTutorialSeen != (i % 2 == 0) || readNotebook.GetWord("want").guess != "persistence roundtrip " + i)
                        throw new InvalidOperationException("Saving over an existing file must preserve the latest life and notebook changes.");
                }
                byte[] lastGoodLife = File.ReadAllBytes(life.SavePath), lastGoodNotebook = File.ReadAllBytes(game.SavePath);
                using (var lockedLife = new FileStream(life.SavePath, FileMode.Open, FileAccess.Read, FileShare.None))
                using (var lockedNotebook = new FileStream(game.SavePath, FileMode.Open, FileAccess.Read, FileShare.None))
                {
                    life.Progress.rainTutorialSeen = !life.Progress.rainTutorialSeen;
                    game.Notebook.GetWord("want").guess = "must not replace a locked save";
                    if ((bool)typeof(LifeSession).GetMethod("SaveProgress", flags).Invoke(life, null) || game.SaveNotebook())
                        throw new InvalidOperationException("Locked destinations must report save failure.");
                }
                if (!lastGoodLife.SequenceEqual(File.ReadAllBytes(life.SavePath)) || !lastGoodNotebook.SequenceEqual(File.ReadAllBytes(game.SavePath)))
                    throw new InvalidOperationException("A failed write must retain both last good save files byte for byte.");
                File.Delete(life.SavePath + ".tmp"); File.Delete(game.SavePath + ".tmp");
#if UNITY_EDITOR_WIN
                string replacement = Path.Combine(fixture, "替换检查.json"), stagedReplacement = replacement + ".tmp";
                File.WriteAllText(replacement, "old"); File.WriteAllText(stagedReplacement, "new");
                typeof(LifeSession).Assembly.GetType("WeatherHouse.Life.LifeSaveFile")
                    .GetMethod("MoveReplacementOnWindows", BindingFlags.Static | BindingFlags.NonPublic)
                    .Invoke(null, new object[] { stagedReplacement, replacement });
                if (File.ReadAllText(replacement) != "new" || File.Exists(stagedReplacement))
                    throw new InvalidOperationException("Windows staged replacement must install the new file at its Unicode path.");
                File.Delete(replacement);
#endif
                File.Delete(life.SavePath); File.Delete(game.SavePath);
                Directory.CreateDirectory(blocker);
                if (!life.Progress.HasHeard("dog.hello")) life.Progress.heardRequests.Add("dog.hello");
                speaker.topicIndex = Array.FindIndex(speaker.requests, r => r.id == "dog.rain.lesson");
                if (speaker.topicIndex < 0) throw new InvalidOperationException("Rain request fixture missing.");
                LifeRequest request = speaker.Current;
                life.Progress.tasks.RemoveAll(t => t != null && t.requestId == request.id);
                game.PlayerTransform.position = speaker.transform.position + Vector3.right;
                var input = new JudgementInput { animalId = speaker.Actor.animalId, utteranceId = speaker.Actor.UtteranceId, glyphIds = request.glyphIds };
                int revision = (int)typeof(GameplaySession).GetField("conversationRevision", flags).GetValue(game);
                typeof(GameplaySession).GetMethod("ApplyJudgement", flags).Invoke(game, new object[] { input,
                    new JudgementResult { success = true, understood = true, evidenceGlyphIds = Array.Empty<string>() }, revision });
                if (life.Progress.FindTask(request.id) == null || !game.Status.Contains("生活进度暂未写入磁盘") || File.Exists(life.SavePath))
                    throw new InvalidOperationException("Accepted help must remain usable and visibly report a failed life save, even when no word was newly confirmed.");
                return "PASS twelve successive saves roundtrip actual life and notebook files.\nPASS locked destinations retain the last good saves byte for byte.\nPASS platform replacement and Unicode save paths preserve the staged contents.\nPASS actual AI callback surfaces a real blocked-file write while retaining the accepted task in memory; no provider request or config access.";
            }
            finally
            {
                Environment.SetEnvironmentVariable("WEATHERHOUSE_LIFE_REVIEW_SAVE", originalRoot);
                JsonUtility.FromJsonOverwrite(progress, life.Progress); life.World.RestoreJson(world);
                JsonUtility.FromJsonOverwrite(notebook, game.Notebook);
                speaker.topicIndex = topic; game.PlayerTransform.position = position;
                game.Notify(status);
                if (Directory.Exists(blocker) && !Directory.EnumerateFileSystemEntries(blocker).Any()) Directory.Delete(blocker);
                if (Directory.Exists(fixture) && !Directory.EnumerateFileSystemEntries(fixture).Any()) Directory.Delete(fixture);
            }
        }
    }
}
