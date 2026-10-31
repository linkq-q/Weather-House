using System;
using UnityEngine;

namespace WeatherHouse.Hillside
{
    [Serializable] public sealed class AnimalOption
    {
        public string id, action, target;
        public float score;
    }
    [Serializable] public sealed class AnimalObservation
    {
        public string animal, species, current, previous;
        public int revision;
        public bool outgoing, raining, night, inHerd;
        public float fatigue, playerDistance;
        public AnimalOption[] options;
        public AnimalStoryState character;
        public AnimalStoryWord[] vocabulary;
        public bool canShareStory;
        public string[] allowedWishes;
    }
    [Serializable] public sealed class AnimalPlan
    {
        public int revision;
        public string[] choices;
        public AnimalStorySpeech speech;
        public AnimalStoryReflection reflection;

        public static bool TryRead(string json, AnimalObservation observation, out AnimalPlan plan)
        {
            plan = null;
            if (observation == null || observation.options == null || string.IsNullOrWhiteSpace(json) || json.Length > 8192) return false;
            try { plan = JsonUtility.FromJson<AnimalPlan>(json); }
            catch (ArgumentException) { return false; }
            // Unity's inline class serializer may restore omitted/null optional objects as empty instances.
            if(plan?.speech!=null && string.IsNullOrEmpty(plan.speech.intent) && string.IsNullOrEmpty(plan.speech.subject) &&
                string.IsNullOrEmpty(plan.speech.evidenceId) && string.IsNullOrEmpty(plan.speech.emotion) &&
                (plan.speech.glyphs==null || plan.speech.glyphs.Length==0))plan.speech=null;
            if(plan?.reflection!=null && string.IsNullOrEmpty(plan.reflection.preferenceId) && string.IsNullOrEmpty(plan.reflection.wish) &&
                plan.reflection.preferenceDelta==0 && (plan.reflection.evidenceIds==null || plan.reflection.evidenceIds.Length==0))plan.reflection=null;
            if (plan == null || plan.revision != observation.revision || plan.choices == null ||
                plan.choices.Length < 1 || plan.choices.Length > 3) return false;
            for (int i = 0; i < plan.choices.Length; i++)
            {
                string id = plan.choices[i];
                if (string.IsNullOrEmpty(id) || Array.Find(observation.options, o => o.id == id) == null) return false;
                for (int j = 0; j < i; j++) if (id == plan.choices[j]) return false;
            }
            return AnimalStoryRules.Validate(plan, observation);
        }

        public static bool Outgoing(int seed, int individual, string species)
        {
            // A separate seed stream never changes animation size, movement RNG or personality on encounters.
            uint hash = unchecked((uint)seed * 16777619u ^ (uint)individual);
            foreach (char c in species ?? "") hash = unchecked((hash ^ c) * 16777619u);
            hash ^= hash >> 16; hash = unchecked(hash * 0x7feb352du); hash ^= hash >> 15;
            return hash % 5 == 0;
        }
    }

    // Context revisions change on meaningful events, never on every position update.
    public sealed class AnimalPlanGate
    {
        public int Revision { get; private set; } = 1;
        public bool Pending { get; private set; }
        float requested;
        int ticket;
        public int Begin(float now) { Pending = true; requested = now; return ticket = Revision; }
        public void Invalidate() { Revision++; Pending = false; }
        public bool Complete(int request, float now, bool available)
        {
            if (!Pending || request != ticket || request != Revision) return false;
            Pending = false;
            return available && now >= requested && now - requested <= 15f;
        }
    }
}
