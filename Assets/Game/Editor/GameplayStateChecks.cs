using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace WeatherHousePrototype
{
    public static class GameplayStateChecks
    {
        [MenuItem("Weather House/Validate Gameplay State")]
        public static void Run()
        {
            var report = new List<string>();
            string path = Path.Combine("PrototypeReview", "gameplay-state.txt");
            Directory.CreateDirectory("PrototypeReview");
            try
            {
                var notebook = new LanguageNotebook();
                notebook.Observe("squirrel", "wet_leaves", new[] { "rain", "want", "rain" });
                notebook.Observe("squirrel", "wet_leaves", new[] { "rain", "want" });
                Check(notebook.words.Count == 2 && notebook.encounters.Count == 2,
                    "Repeated observation does not duplicate words or sources", report);

                notebook.SetGuess("rain", "rain");
                Check(notebook.GetWord("rain").guess == "rain" && !notebook.GetWord("rain").confirmed,
                    "Even an exact guess remains unconfirmed", report);
                notebook.SetGuess("rain", "也许是水？");
                Check(notebook.GetWord("rain").guess == "也许是水？" && !notebook.GetWord("rain").confirmed,
                    "Free guesses remain editable without validation", report);
                Check(!notebook.CanVerify("rain", "squirrel") &&
                    !notebook.ConfirmFromJudgement("rain", "squirrel", new[] { "rain" }, true),
                    "The original animal cannot confirm its own evidence", report);

                notebook.SetGuess("unseen", "晴");
                Check(notebook.GetWord("unseen") == null &&
                    !notebook.ConfirmFromJudgement("unseen", "frog", new[] { "unseen" }, true),
                    "An unseen glyph cannot be created or confirmed by a guess or judgement", report);
                Check(notebook.CanVerify("rain", "frog") &&
                    !notebook.ConfirmFromJudgement("rain", "frog", new[] { "rain" }, false) &&
                    !notebook.ConfirmFromJudgement("rain", "frog", new[] { "sun" }, true) &&
                    !notebook.ConfirmFromJudgement("rain", "frog", null, true) &&
                    !notebook.ConfirmFromJudgement("rain", "", new[] { "rain" }, true) &&
                    !notebook.GetWord("rain").confirmed,
                    "Wrong, unrelated, and incomplete judgements never confirm", report);

                var first = Photo("photo_1", "squirrel", "wet_leaves", new[] { "rain", "want" }, "rain");
                notebook.AddObservation(first);
                first.glyphIds[0] = "changed_by_caller";
                first.weather = "sun";
                notebook.AddObservation(Photo("photo_1", "frog", "duplicate_id", new[] { "sun" }, "sun"));
                notebook.AddObservation(Photo("photo_2", "frog", "pond_request", new[] { "rain" }, "rain"));
                notebook.AddObservation(Photo("photo_3", "bird", "warm_feathers", new[] { "sun" }, "sun"));
                var evidence = notebook.FindEvidence("rain");
                Check(notebook.observations.Count == 3 && evidence.Count == 2 &&
                    evidence[0].id == "photo_1" && evidence[1].id == "photo_2" &&
                    evidence[0].weather == "rain" && notebook.FindEvidence("unseen").Count == 0,
                    "Evidence filters by glyph, preserves captures, and deduplicates photo ids", report);
                Check(!notebook.GetWord("rain").confirmed && !notebook.GetWord("sun").confirmed,
                    "Photographing rain or sunshine does not confirm vocabulary", report);

                string json = JsonUtility.ToJson(notebook);
                var restored = JsonUtility.FromJson<LanguageNotebook>(json);
                var restoredPhoto = restored.FindEvidence("rain")[0];
                Check(restored.GetWord("rain").guess == "也许是水？" &&
                    restored.GetWord("rain").encountered && !restored.GetWord("rain").confirmed &&
                    restored.observations.Count == 3 && restored.encounters.Count == notebook.encounters.Count &&
                    restored.CanVerify("want", "frog") && !restored.CanVerify("want", "squirrel") &&
                    restoredPhoto.animalId == "squirrel" && restoredPhoto.utteranceId == "wet_leaves" &&
                    restoredPhoto.clipFolder == "clips/photo_1" && restoredPhoto.frameCount == 18 &&
                    restoredPhoto.framesPerSecond == 6f && restoredPhoto.capturedUtcTicks == 638990000000000000L,
                    "Unity JSON roundtrip preserves guesses, sources, evidence, and clip metadata", report);

                Check(restored.ConfirmFromJudgement("rain", "frog", new[] { "want", "rain" }, true) &&
                    restored.GetWord("rain").confirmed && !restored.GetWord("want").confirmed,
                    "A correct new animal response confirms only the targeted glyph", report);
                restored.SetGuess("rain", "overwrite confirmed");
                Check(!restored.ConfirmFromJudgement("rain", "frog", new[] { "rain" }, true) &&
                    restored.GetWord("rain").confirmed && restored.GetWord("rain").guess == "也许是水？" &&
                    restored.words.Count == 3 && restored.observations.Count == 3,
                    "Repeated confirmation is idempotent and preserves the original guess", report);
                var confirmedSave = JsonUtility.FromJson<LanguageNotebook>(JsonUtility.ToJson(restored));
                Check(confirmedSave.GetWord("rain").confirmed &&
                    !confirmedSave.ConfirmFromJudgement("rain", "frog", new[] { "rain" }, true),
                    "Confirmed state stays confirmed and idempotent after reloading", report);

                var catalog = JsonUtility.FromJson<GlyphCatalog>(JsonUtility.ToJson(new GlyphCatalog
                {
                    glyphs = new[] { new GlyphDefinition
                    {
                        id = "rain", name = "雨字", meaning = "雨", boundary = "环", body = "三点"
                    } }
                }));
                Check(catalog.glyphs.Length == 1 && catalog.glyphs[0].meaning == "雨" &&
                    catalog.glyphs[0].boundary == "环" && catalog.glyphs[0].body == "三点",
                    "Glyph catalog keeps the authored meaning and visual parts", report);

                report.Add("Result: PASS");
                File.WriteAllLines(path, report);
                Debug.Log("Gameplay state checks passed: " + path);
            }
            catch (Exception exception)
            {
                report.Add("Result: FAIL");
                report.Add(exception.ToString());
                File.WriteAllLines(path, report);
                throw;
            }
        }

        private static ObservationRecord Photo(string id, string animalId, string utteranceId,
            string[] glyphIds, string weather)
        {
            return new ObservationRecord
            {
                id = id, animalId = animalId, utteranceId = utteranceId, glyphIds = glyphIds,
                weather = weather, clipFolder = "clips/" + id, frameCount = 18,
                framesPerSecond = 6f, capturedUtcTicks = 638990000000000000L
            };
        }

        private static void Check(bool condition, string description, List<string> report)
        {
            if (!condition) throw new InvalidOperationException(description);
            report.Add("PASS: " + description);
        }
    }
}
