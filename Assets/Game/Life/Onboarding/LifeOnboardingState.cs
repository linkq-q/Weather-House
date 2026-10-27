using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using WeatherHousePrototype;

namespace WeatherHouse.Life
{
    public enum LifeOnboardingStage { Opening, FirstBook, ObserveAnimals, GuessBook, TakeSpeechPhoto, PhotoBook, FindRainRequest, WeatherBook, UnderstandRain, CastRain, ClearWeather, ReturnVisit, Complete }
    [Serializable] public sealed class LifeOnboardingState
    {
        public int version = 1;
        public bool started, helloSeen, goodbyeSeen, teachingHintAvailable, teachingHintRead, rainCast, clearCast, returnAway, evidenceViewed;
        public LifeOnboardingStage stage;
        public int firstBookPage, photoBookPage, hintsSpent;
        public List<string> animalsSeen = new List<string>(), earnedWords = new List<string>(), hintsRead = new List<string>(), booksIntroduced = new List<string>();
        public string feedback = "";
        public int HintsAvailable => Mathf.Max(0, earnedWords.Count / 3 - hintsSpent);
        public void Normalize()
        {
            animalsSeen = (animalsSeen ?? new List<string>()).Where(x => !string.IsNullOrEmpty(x)).Distinct().ToList();
            earnedWords = (earnedWords ?? new List<string>()).Where(x => !string.IsNullOrEmpty(x) && x != "want" && x != "reject").Distinct().ToList();
            hintsRead = (hintsRead ?? new List<string>()).Distinct().ToList(); booksIntroduced = (booksIntroduced ?? new List<string>()).Distinct().ToList();
            hintsSpent = Mathf.Clamp(hintsSpent, 0, earnedWords.Count / 3);
        }
        public bool Observe(string animalId, string[] glyphs)
        {
            bool change = false;
            if (!string.IsNullOrEmpty(animalId) && !animalsSeen.Contains(animalId)) { animalsSeen.Add(animalId); change = true; }
            if (!helloSeen && Array.IndexOf(glyphs ?? Array.Empty<string>(), "hello") >= 0) { helloSeen = true; change = true; }
            if (!goodbyeSeen && Array.IndexOf(glyphs ?? Array.Empty<string>(), "goodbye") >= 0) { goodbyeSeen = true; change = true; }
            return change;
        }
        public bool TrackConfirmed(LanguageNotebook notebook)
        {
            bool change = false;
            foreach (var word in notebook.words.Where(w => w != null && w.confirmed && w.glyphId != "want" && w.glyphId != "reject"))
                if (!earnedWords.Contains(word.glyphId)) { earnedWords.Add(word.glyphId); change = true; }
            return change;
        }
        public bool TryReadHint(string id, bool teaching)
        {
            if (hintsRead.Contains(id)) return true;
            if (teaching && teachingHintAvailable && !teachingHintRead) teachingHintRead = true;
            else { if (HintsAvailable <= 0) return false; hintsSpent++; }
            hintsRead.Add(id); return true;
        }
        public static bool ShouldStart(bool priorNotebook, bool priorLife, bool review, bool explicitReview, string directory)
        {
            if (review)
            {
                string root = Path.GetFullPath("LifeGameplayReview") + Path.DirectorySeparatorChar;
                if (!explicitReview || !Path.GetFullPath(directory).StartsWith(root, StringComparison.OrdinalIgnoreCase)) return false;
            }
            return !priorNotebook && !priorLife;
        }
    }
}
