using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using WeatherHousePrototype;

namespace WeatherHouse.Life.Editor
{
    public static class LifePhotoFlowChecks
    {
        [MenuItem("Weather House/Life/Validate Photo Flow")]
        public static void Run()
        {
            var catalog = JsonUtility.FromJson<GlyphCatalog>(Resources.Load<TextAsset>("WeatherHouseGlyphs").text);
            var lines = CheckModel(catalog);
            Directory.CreateDirectory("LifeGameplayReview/20261026/photo-flow");
            File.WriteAllLines("LifeGameplayReview/20261026/photo-flow/model-checks.txt", lines);
        }

        public static List<string> CheckModel(GlyphCatalog catalog)
        {
            var lines = new List<string>();
            void Check(bool condition, string label) { if (!condition) throw new InvalidOperationException(label); lines.Add("PASS " + label); }
            var book = new LanguageNotebook(); book.Normalize(catalog);
            Check(catalog.glyphs.Length == 33 && catalog.glyphs.Select(g => g.id).Distinct().Count() == 33, "33 unique stable glyph IDs");
            Check(LifePhotoRules.Tasks.Count == 26 && LifePhotoRules.Tasks.Select(t => t.id).Distinct().Count() == 26, "26 unique approved task IDs");
            Check(LifePhotoRules.Tasks.All(t => t.rewards.Length >= 1 && t.rewards.Length <= 2 &&
                t.tokens.Concat(t.rewards).All(id => catalog.glyphs.Any(g => g.id == id))), "Every token and 1-2 word reward exists in the catalog");
            var photoWords = catalog.glyphs.Where(g => g.id != "want" && g.id != "reject" && g.id != "clear" && g.id != "there").ToArray();
            Check(photoWords.Length == 29 && photoWords.All(g => LifePhotoRules.Tasks.Any(t => t.rewards.Contains(g.id))),
                "All 29 existing photo words retain task sources; clear and there are not granted by legacy photo tasks");
            Check(new[] { "clear", "there" }.All(id => book.GetWord(id) != null && !book.GetWord(id).encountered && !book.GetWord(id).confirmed),
                "New clear and there words stay unknown until explicitly encountered");
            book.AddObservation(Photo("landscape"));
            Check(book.ApplyPhotoTasks() == 0 && book.observations.Count == 1, "An animal-free, unmatched landscape is retained");
            var fox = new LifePhotoEntity { id = "fox-one", kind = "animal", speciesId = "fox", bodyVisible = true, sleeping = true };
            var snapshot = new LifePhotoSnapshot { entities = new[] { fox } };
            book.AddObservation(new ObservationRecord { id = "sleeping-fox", photoSnapshot = snapshot });
            fox.sleeping = false; fox.speciesId = "deer";
            Check(book.ApplyPhotoTasks() == 2 && book.IsPhotoTaskComplete("P01") && book.IsPhotoTaskComplete("P09"),
                "One shutter snapshot completes fox and sleep tasks before reading or understanding them");
            Check(book.GetWord("fox").encountered && book.GetWord("sleep").encountered && !book.GetWord("deer").encountered &&
                !book.GetWord("fox").confirmed && !book.GetWord("sleep").confirmed,
                "A photo records encountered glyphs without confirming meanings or rewriting its shutter facts");
            book.SetGuess("sleep", "玩家保留的猜测");
            Check(book.FindTaskPhotos("P09").Single().id == "sleeping-fox" && book.FindEvidence("sleep").Count == 1,
                "Task and word lookups return the actual stored photo");
            Check(!book.HasPhotoEvidence("sleep"), "A matched task without a stored photo frame is not spell evidence");
            var spellBook = new LanguageNotebook(); spellBook.Normalize(catalog);
            spellBook.GetWord("wind").confirmed = true;
            Check(!spellBook.HasPhotoEvidence("wind"), "A confirmed meaning alone is not photographic spell evidence");
            spellBook.AddObservation(new ObservationRecord { id = "unfinished-wind", animalId = "fox", utteranceId = "wind-request", glyphIds = new[] { "wind" } });
            Check(!spellBook.HasPhotoEvidence("wind"), "An utterance record with zero stored frames is not spell evidence");
            spellBook.AddObservation(new ObservationRecord { id = "wind-speech", animalId = "fox", utteranceId = "wind-request", glyphIds = new[] { "wind" }, frameCount = 1 });
            Check(spellBook.HasPhotoEvidence("wind"), "A stored speech photo provides photographic spell evidence");
            var sunlightPhoto = Photo("sunlight-task", new LifePhotoEntity { id = "grass", bodyVisible = true, sunContrastVisible = true });
            sunlightPhoto.frameCount = 1; spellBook.AddObservation(sunlightPhoto); spellBook.ApplyPhotoTasks();
            Check(spellBook.HasPhotoEvidence("sunlight") && !spellBook.GetWord("sunlight").confirmed,
                "A stored task photo provides photographic spell evidence before the word is understood");
            Check(!spellBook.HasPhotoEvidence("strong") && !spellBook.HasPhotoEvidence(null),
                "Unrelated words and empty glyph IDs have no photographic spell evidence");
            book.AddObservation(new ObservationRecord { id = "duplicate-shot", photoSnapshot = book.observations[1].photoSnapshot });
            book.AddObservation(new ObservationRecord { id = "duplicate-shot", photoSnapshot = snapshot });
            Check(book.ApplyPhotoTasks() == 0 && book.FindTaskPhotos("P09").Count == 2 && book.observations.Count == 3,
                "Repeated shots keep their photos; duplicate IDs and rewards are idempotent");
            Check(!book.GetWord("sleep").confirmed && book.GetWord("sleep").guess == "玩家保留的猜测",
                "Repeated task matching preserves the player's unconfirmed guess");
            book.AddObservation(Photo("rain-water", new LifePhotoEntity { id = "bowl", rainVisible = true, rainStrength = 3,
                waterSurfaceVisible = true, water = .7f }));
            Check(book.ApplyPhotoTasks() == 2 && book.IsPhotoTaskComplete("P05") && book.IsPhotoTaskComplete("P06"),
                "Rain and standing water reward independently in the same photograph");
            var wetBook = new LanguageNotebook(); wetBook.Normalize(catalog);
            wetBook.AddObservation(Photo("sunny-wet", new LifePhotoEntity { id = "grass", kind = "grass", bodyVisible = true,
                wetness = 1f, wetSurfaceVisible = true })); wetBook.ApplyPhotoTasks();
            Check(wetBook.GetWord("wet").encountered && !wetBook.GetWord("wet").confirmed && !wetBook.GetWord("rain").encountered,
                "Wet grass records wet without revealing its meaning or implying rain");
            var homeBook = new LanguageNotebook(); homeBook.Normalize(catalog);
            homeBook.AddObservation(Photo("old-home", new LifePhotoEntity { id = "den", kind = "home", bodyVisible = true }));
            Check(homeBook.ApplyPhotoTasks() == 0, "An unattributed home photograph is retained without guessing ownership");
            var home = new LifePublicContext { id = "public-home", kind = "home", actorId = "fox", targetId = "den", glyphIds = new[] { "home" } };
            Check(homeBook.AddPublicContext(home), "A public ownership demonstration is recorded");
            home.actorId = "changed"; home.glyphIds[0] = "changed";
            Check(homeBook.ApplyPhotoTasks() == 1 && homeBook.IsPhotoTaskComplete("P11") &&
                homeBook.publicHistory[0].actorId == "fox" && homeBook.publicHistory[0].glyphIds[0] == "home", "Later public home attribution pays the old photo and is copied independently");
            homeBook.AddObservation(Photo("old-cold", new LifePhotoEntity { id = "fox", kind = "animal", speciesId = "fox", bodyVisible = true, environmentVisible = true }));
            homeBook.AddPublicContext(new LifePublicContext { id = "later-cold", kind = "cold", actorId = "fox" }); homeBook.ApplyPhotoTasks();
            Check(!homeBook.IsPhotoTaskComplete("P18"), "Later cold speech cannot rewrite a past photograph");
            homeBook.AddPublicContext(new LifePublicContext { id = "self-you-event", kind = "interaction", targetId = "I01", completed = true });
            Check(homeBook.ApplyPhotoTasks() == 1 && homeBook.GetWord("self").encountered && homeBook.GetWord("you").encountered &&
                !homeBook.GetWord("self").confirmed && !homeBook.GetWord("you").confirmed,
                "An explicit interaction records its preset pair without confirming meanings");
            Check(homeBook.FindTaskPhotos("I01").Count == 0, "A later interaction is never attached to unrelated old photos");
            var interactionBook = new LanguageNotebook(); interactionBook.Normalize(catalog);
            foreach (var task in LifePhotoRules.Tasks.Where(task => task.IsInteraction))
                interactionBook.AddPublicContext(new LifePublicContext { id = task.id, kind = "interaction", targetId = task.id, completed = true });
            Check(interactionBook.ApplyPhotoTasks() == 4 && LifePhotoRules.Tasks.Where(task => task.IsInteraction)
                .SelectMany(task => task.rewards).All(id => interactionBook.GetWord(id).encountered && !interactionBook.GetWord(id).confirmed),
                "All four action tasks complete independently of word understanding");
            var historyBook = new LanguageNotebook();
            var greeting = new LifePublicContext { id = "hello:request:fox", kind = "hello", actorId = "fox", occurredUtc = "first", glyphIds = new[] { "hello" } };
            historyBook.AddPublicContext(greeting);
            historyBook.AddObservation(new ObservationRecord { id = "old-greeting", photoSnapshot = new LifePhotoSnapshot { contexts = new[] { greeting } } });
            greeting.occurredUtc = "second"; greeting.anchor = new Vector3(2, 3, 4);
            Check(historyBook.AddPublicContext(greeting) && historyBook.publicHistory.Count == 1 && historyBook.publicHistory[0].occurredUtc == "second" &&
                historyBook.observations[0].photoSnapshot.contexts[0].occurredUtc == "first", "A stable public-context ID refreshes current history without rewriting old shutter context");
            Check(!historyBook.AddPublicContext(greeting), "Identical public-context replay causes no redundant history update");
            Check(!homeBook.AddPublicContext(new LifePublicContext { id = "self-you-event", kind = "interaction", targetId = "I01", completed = true, occurredUtc = "later" }) &&
                homeBook.ApplyPhotoTasks() == 0, "Completed interaction replay neither replaces its record nor repeats its reward");
            var restored = JsonUtility.FromJson<LanguageNotebook>(JsonUtility.ToJson(book)); restored.Normalize(catalog);
            Check(restored.ApplyPhotoTasks() == 0 && restored.GetWord("fox").encountered && !restored.GetWord("fox").confirmed &&
                !restored.GetWord("sleep").confirmed && restored.GetWord("sleep").guess == "玩家保留的猜测" &&
                restored.observations[1].photoSnapshot.entities[0].sleeping,
                "Reload preserves snapshots, guesses and task completion without confirming meanings");
            var completedLegacy = JsonUtility.FromJson<LanguageNotebook>(JsonUtility.ToJson(book));
            completedLegacy.GetWord("fox").confirmed = true;
            completedLegacy.GetWord("fox").guess = "旧存档已确认的猜测";
            completedLegacy.GetWord("sleep").encountered = false;
            completedLegacy.Normalize(catalog);
            Check(completedLegacy.ApplyPhotoTasks() == 0 && completedLegacy.GetWord("fox").confirmed &&
                completedLegacy.GetWord("fox").guess == "旧存档已确认的猜测" && completedLegacy.GetWord("sleep").encountered &&
                !completedLegacy.GetWord("sleep").confirmed && completedLegacy.GetWord("sleep").guess == "玩家保留的猜测",
                "Replaying completed legacy tasks preserves existing confirmations and only repairs missing encounters");
            var legacy = JsonUtility.FromJson<LanguageNotebook>("{\"words\":[{\"glyphId\":\"rain\",\"guess\":\"旧猜测\",\"encountered\":true,\"confirmed\":true}],\"observations\":[{\"id\":\"legacy\",\"animalId\":\"old\",\"glyphIds\":[\"rain\"]}]}");
            legacy.Normalize(catalog);
            Check(legacy.words.Count == 33 && legacy.GetWord("rain").confirmed && legacy.GetWord("rain").guess == "旧猜测" &&
                new[] { "fox", "squirrel", "sheep", "deer", "sleep", "eat", "together", "clear", "there" }.All(id => !legacy.GetWord(id).confirmed),
                "Legacy knowledge and guesses survive while added words start unknown");
            Check(legacy.ApplyPhotoTasks() == 0 && legacy.observations.Count == 1, "Legacy photos remain visible without inventing missing shutter evidence");
            return lines;
        }

        static ObservationRecord Photo(string id, params LifePhotoEntity[] entities) => new ObservationRecord
        { id = id, photoSnapshot = new LifePhotoSnapshot { entities = entities } };
    }
}
