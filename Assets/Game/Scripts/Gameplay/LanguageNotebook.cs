using System;
using System.Collections.Generic;
using System.Linq;
using WeatherHouse.Life;

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
        public WeatherHouse.Life.LifeContextSnapshot context;
        public LifePhotoSnapshot photoSnapshot;
        public string[] matchedPhotoTaskIds = Array.Empty<string>();
        public bool liveTruncated;
        public string captureWarning;
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
        public int version = 2;
        public List<GlyphKnowledge> words = new List<GlyphKnowledge>();
        public List<ObservationRecord> observations = new List<ObservationRecord>();
        public List<ObservationEncounter> encounters = new List<ObservationEncounter>();
        public List<string> completedPhotoTasks = new List<string>();
        public List<LifePublicContext> publicHistory = new List<LifePublicContext>();

        public void Normalize(GlyphCatalog catalog)
        {
            words = words ?? new List<GlyphKnowledge>();
            observations = observations ?? new List<ObservationRecord>();
            encounters = encounters ?? new List<ObservationEncounter>();
            completedPhotoTasks = completedPhotoTasks ?? new List<string>();
            publicHistory = publicHistory ?? new List<LifePublicContext>();
            foreach (var glyph in catalog?.glyphs ?? Array.Empty<GlyphDefinition>())
                if (glyph != null && !string.IsNullOrWhiteSpace(glyph.id)) EnsureWord(glyph.id);
            foreach (string id in new[] { "want", "reject" })
            {
                var word = EnsureWord(id);
                word.encountered = word.confirmed = true;
            }
            version = 2;
        }

        private GlyphKnowledge EnsureWord(string id)
        {
            if (words == null) words = new List<GlyphKnowledge>();
            var word = GetWord(id);
            if (word != null) return word;
            word = new GlyphKnowledge { glyphId = id, guess = string.Empty };
            words.Add(word);
            return word;
        }

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
                capturedUtcTicks = record.capturedUtcTicks, context = record.context?.Copy(),
                photoSnapshot = record.photoSnapshot?.Copy(), liveTruncated = record.liveTruncated, captureWarning = record.captureWarning,
                matchedPhotoTaskIds = record.matchedPhotoTaskIds == null ? Array.Empty<string>() : (string[])record.matchedPhotoTaskIds.Clone()
            };
            observations.Add(snapshot);
            Observe(snapshot.animalId, snapshot.utteranceId, snapshot.glyphIds);
        }

        public bool AddPublicContext(LifePublicContext context)
        {
            if (context == null || string.IsNullOrWhiteSpace(context.id)) return false;
            if (publicHistory == null) publicHistory = new List<LifePublicContext>();
            int prior = publicHistory.FindIndex(c => c != null && c.id == context.id);
            if (prior >= 0)
            {
                var existing = publicHistory[prior];
                if ((existing.kind == "interaction" && existing.completed) ||
                    UnityEngine.JsonUtility.ToJson(existing) == UnityEngine.JsonUtility.ToJson(context)) return false;
                publicHistory[prior] = context.Copy();
                return true;
            }
            publicHistory.Add(context.Copy());
            return true;
        }

        public bool IsPhotoTaskComplete(string id) => completedPhotoTasks != null && completedPhotoTasks.Contains(id);

        public List<ObservationRecord> FindTaskPhotos(string taskId) => (observations ?? new List<ObservationRecord>())
            .Where(photo => photo != null && Array.IndexOf(photo.matchedPhotoTaskIds ?? Array.Empty<string>(), taskId) >= 0).ToList();

        // Re-evaluate stored shutter facts, never today's world. Only the matcher allows later public context.
        public int ApplyPhotoTasks()
        {
            if (completedPhotoTasks == null) completedPhotoTasks = new List<string>();
            if (publicHistory == null) publicHistory = new List<LifePublicContext>();
            int count = 0;
            foreach (var photo in observations ?? new List<ObservationRecord>())
            {
                if (photo?.photoSnapshot == null) continue;
                var matched = LifePhotoRules.Match(photo.photoSnapshot, publicHistory)
                    .Where(id => LifePhotoRules.Tasks.Any(task => task.id == id && !task.IsInteraction)).ToArray();
                photo.matchedPhotoTaskIds = matched;
                foreach (string id in matched) if (CompletePhotoTask(id)) count++;
            }
            foreach (string id in LifePhotoRules.Match(null, publicHistory)) if (CompletePhotoTask(id)) count++;
            return count;
        }

        private bool CompletePhotoTask(string id)
        {
            var task = LifePhotoRules.Tasks.FirstOrDefault(item => item.id == id);
            if (task == null) return false;
            // Restore encounters from old task records without revealing meanings or undoing guesses.
            foreach (string glyphId in task.rewards)
            {
                var word = EnsureWord(glyphId);
                word.encountered = true;
            }
            if (IsPhotoTaskComplete(id)) return false;
            completedPhotoTasks.Add(id);
            return true;
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
                if (record != null && ((record.glyphIds != null && Array.IndexOf(record.glyphIds, glyphId) >= 0) ||
                    LifePhotoRules.Tasks.Any(task => Array.IndexOf(record.matchedPhotoTaskIds ?? Array.Empty<string>(), task.id) >= 0 &&
                        Array.IndexOf(task.tokens, glyphId) >= 0)))
                    result.Add(record);
            return result;
        }

        public bool HasPhotoEvidence(string glyphId) => FindEvidence(glyphId).Any(photo => photo.frameCount > 0);

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
