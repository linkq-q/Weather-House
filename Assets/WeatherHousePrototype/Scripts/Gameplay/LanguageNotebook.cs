using System;
using System.Collections.Generic;

namespace WeatherHousePrototype
{
    [Serializable]
    public class GlyphDefinition
    {
        public string id, name, meaning, boundary, body;
    }

    [Serializable]
    public class GlyphCatalog
    {
        public GlyphDefinition[] glyphs;
    }

    [Serializable]
    public class ObservationRecord
    {
        public string id, animalId, utteranceId;
        public string[] glyphIds;
        public string weather, clipFolder;
        public int frameCount;
        public float framesPerSecond;
        public long capturedUtcTicks;
    }

    [Serializable]
    public class GlyphKnowledge
    {
        public string glyphId, guess;
        public bool encountered, confirmed;
    }

    [Serializable]
    public class ObservationEncounter
    {
        public string animalId, utteranceId, glyphId;
    }

    [Serializable]
    public class LanguageNotebook
    {
        public List<GlyphKnowledge> words = new List<GlyphKnowledge>();
        public List<ObservationRecord> observations = new List<ObservationRecord>();
        public List<ObservationEncounter> encounters = new List<ObservationEncounter>();

        public void Observe(string animalId, string utteranceId, string[] glyphIds)
        {
            if (string.IsNullOrWhiteSpace(animalId) || string.IsNullOrWhiteSpace(utteranceId) || glyphIds == null)
                return;

            if (words == null) words = new List<GlyphKnowledge>();
            if (encounters == null) encounters = new List<ObservationEncounter>();
            foreach (var glyphId in glyphIds)
            {
                if (string.IsNullOrWhiteSpace(glyphId)) continue;
                var word = GetWord(glyphId);
                if (word == null)
                {
                    word = new GlyphKnowledge { glyphId = glyphId, guess = string.Empty };
                    words.Add(word);
                }
                word.encountered = true;
                if (!encounters.Exists(e => e != null && e.glyphId == glyphId &&
                    e.animalId == animalId && e.utteranceId == utteranceId))
                    encounters.Add(new ObservationEncounter
                    {
                        animalId = animalId, utteranceId = utteranceId, glyphId = glyphId
                    });
            }
        }

        public void AddObservation(ObservationRecord record)
        {
            if (record == null) throw new ArgumentNullException(nameof(record));
            if (string.IsNullOrWhiteSpace(record.id))
                throw new ArgumentException("An observation needs a stable id.", nameof(record));
            if (observations == null) observations = new List<ObservationRecord>();
            if (observations.Exists(o => o != null && o.id == record.id)) return;

            // Keep the photographed utterance even if the caller reuses its capture buffer.
            var snapshot = new ObservationRecord
            {
                id = record.id, animalId = record.animalId, utteranceId = record.utteranceId,
                glyphIds = record.glyphIds == null ? new string[0] : (string[])record.glyphIds.Clone(),
                weather = record.weather, clipFolder = record.clipFolder,
                frameCount = record.frameCount, framesPerSecond = record.framesPerSecond,
                capturedUtcTicks = record.capturedUtcTicks
            };
            observations.Add(snapshot);
            Observe(snapshot.animalId, snapshot.utteranceId, snapshot.glyphIds);
        }

        public void SetGuess(string glyphId, string guess)
        {
            var word = GetWord(glyphId);
            if (word != null && word.encountered && !word.confirmed)
                word.guess = guess ?? string.Empty;
        }

        public GlyphKnowledge GetWord(string id)
        {
            if (string.IsNullOrWhiteSpace(id) || words == null) return null;
            return words.Find(w => w != null && w.glyphId == id);
        }

        public List<ObservationRecord> FindEvidence(string glyphId)
        {
            var result = new List<ObservationRecord>();
            if (string.IsNullOrWhiteSpace(glyphId) || observations == null) return result;
            foreach (var record in observations)
                if (record != null && record.glyphIds != null && Array.IndexOf(record.glyphIds, glyphId) >= 0)
                    result.Add(record);
            return result;
        }

        public bool CanVerify(string glyphId, string currentAnimalId)
        {
            var word = GetWord(glyphId);
            if (word == null || !word.encountered || word.confirmed ||
                string.IsNullOrWhiteSpace(currentAnimalId) || encounters == null)
                return false;
            return encounters.Exists(e => e != null && e.glyphId == glyphId &&
                !string.IsNullOrWhiteSpace(e.animalId) && e.animalId != currentAnimalId);
        }

        // True means this call newly confirmed the word, so repeated clicks do not repeat rewards.
        public bool ConfirmFromJudgement(string glyphId, string currentAnimalId,
            string[] currentUtteranceGlyphIds, bool understood)
        {
            if (!understood || currentUtteranceGlyphIds == null ||
                Array.IndexOf(currentUtteranceGlyphIds, glyphId) < 0 || !CanVerify(glyphId, currentAnimalId))
                return false;
            GetWord(glyphId).confirmed = true;
            return true;
        }
    }
}
