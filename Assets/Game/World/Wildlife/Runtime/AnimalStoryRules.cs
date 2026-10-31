using System;
using System.Linq;

namespace WeatherHouse.Hillside
{
    [Serializable] public sealed class AnimalStoryWord { public string id, meaning; }
    [Serializable] public sealed class AnimalStorySpeech
    {
        public string intent, subject, evidenceId, emotion;
        public string[] glyphs;
    }

    // The model can arrange words, but facts and participants come from local execution.
    public static class AnimalStoryRules
    {
        public static readonly string[] PreferenceIds = { "sea", "player" };
        public static readonly string[] WishIds = { "watch_sea", "rest", "none" };
        public static readonly string[] OrdinaryWishIds = { "rest", "none" };
        public static bool Validate(AnimalPlan plan, AnimalObservation observation)
        {
            if (plan.reflection != null)
            {
                if (observation.character == null) return false;
                var copy = UnityEngine.JsonUtility.FromJson<AnimalStoryState>(UnityEngine.JsonUtility.ToJson(observation.character));
                if (!copy.TryApplyReflection(plan.reflection, PreferenceIds, observation.allowedWishes ?? OrdinaryWishIds)) return false;
            }
            return plan.speech == null || ValidateSpeech(plan.speech, observation);
        }

        public static bool ValidateSpeech(AnimalStorySpeech speech, AnimalObservation observation)
        {
            if (speech == null || !observation.canShareStory || observation.character == null ||
                speech.glyphs == null || speech.glyphs.Length < 1 || speech.glyphs.Length > 9 ||
                observation.vocabulary == null) return false;
            string allowed = "self you hello goodbye here there sheep";
            bool sea = speech.subject == "sea", person = speech.subject == "player";
            if (!sea && !person) return false;
            var card = observation.character;
            switch (speech.intent)
            {
                case "preference":
                    if (!card.preferences.Any(p => p.id == speech.subject && p.value > .5f)) return false;
                    allowed += " like";
                    if (sea) allowed += " look sea";
                    if (!speech.glyphs.Contains("like") || !speech.glyphs.Contains("self") ||
                        sea && speech.glyphs.Contains("you") || person && !speech.glyphs.Contains("you")) return false;
                    break;
                case "affection":
                    if (!person || card.playerRelationship < .05f) return false;
                    allowed += " like";
                    if (!speech.glyphs.Contains("like") || !speech.glyphs.Contains("you") || !speech.glyphs.Contains("self")) return false;
                    break;
                case "wish":
                    if (!sea || observation.raining || !observation.options.Any(o => o.action == "watch_sea")) return false;
                    allowed += " want reject go come together look sea";
                    if (!speech.glyphs.Contains("sea")) return false;
                    break;
                case "memory":
                    var evidence = card.FindEvent(speech.evidenceId);
                    if (!sea || evidence == null || evidence.outcome != "completed" || evidence.activity != "watch_sea") return false;
                    allowed += " past go look sea";
                    if (evidence.playerParticipated) allowed += " together";
                    else if (speech.glyphs.Contains("you")) return false;
                    if (!speech.glyphs.Contains("past") || !speech.glyphs.Contains("sea")) return false;
                    break;
                default: return false;
            }
            if (speech.intent != "memory" && !string.IsNullOrEmpty(speech.evidenceId)) return false;
            if (sea && !speech.glyphs.Contains("sea")) return false;
            string[] valid = allowed.Split(' ');
            foreach (string id in speech.glyphs)
                if (string.IsNullOrEmpty(id) || !valid.Contains(id) || !observation.vocabulary.Any(w => w.id == id)) return false;
            return string.IsNullOrEmpty(speech.emotion) || speech.emotion == "heart" &&
                (speech.intent == "affection" || speech.intent == "preference") && speech.glyphs.Contains("like");
        }
    }
}
