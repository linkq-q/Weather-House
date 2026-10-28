using System;
using UnityEngine;
using WeatherHousePrototype;

namespace WeatherHouse.Life
{
    public sealed class LifeOnboardingSpeech : MonoBehaviour
    {
        public LifeOnboarding owner;
        public LifeRequest Request { get; private set; }
        public string Utterance { get; private set; }
        public bool Active => owner && owner.SocialGreetingsEnabled && Request != null && Time.unscaledTime < until;
        float until; int serial; string heardUtterance;
        public void Speak(string word, float seconds)
        {
            var actor = GetComponent<AnimalActor>(); serial++;
            Request = new LifeRequest { id = "onboarding." + word + "." + serial, speakerKey = GetComponent<LifeSpeaker>()?.key,
                listenerKey = "player", condition = "conversation", glyphIds = new[] { word },
                meaning = word == "hello" ? "见面时向朋友问好" : "分别时向朋友道别", context = word == "hello" ? "它看向刚刚走近的朋友。" : "你走远时，它仍留在原地，向你道别。" };
            Utterance = actor.animalId + "." + Request.id; until = Time.unscaledTime + seconds; heardUtterance = null;
        }
        public void Listen(string utteranceId)
        {
            if (!Active || Utterance != utteranceId) return;
            if (heardUtterance == utteranceId) Silence();
            else heardUtterance = utteranceId;
        }
        public void Silence() { until = 0; }
        public static LifeOnboardingSpeech Current(AnimalActor actor)
        { var speech = actor ? actor.GetComponent<LifeOnboardingSpeech>() : null; return speech && speech.Active ? speech : null; }
    }
    public sealed partial class LifeWorld
    {
        public void ObserveOnboardingUtterance(AnimalActor actor, LifeOnboardingSpeech speech)
        {
            var hud = owner.GetComponent<GameplayHud>();
            if (!actor || !speech || !speech.Active || !hud || !hud.IsAnimalSpeechVisible(actor)) return;
            string word = speech.Request.glyphIds[0];
            PublishPhotoContext(new LifePublicContext { id = speech.Utterance, kind = word, actorId = actor.animalId,
                listenerId = "player", glyphIds = (string[])speech.Request.glyphIds.Clone(), occurredUtc = DateTime.UtcNow.ToString("O"),
                anchor = actor.transform.position, origin = Player.position, description = speech.Request.context }, 10);
        }
    }
}
