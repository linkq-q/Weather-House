using System.Linq;
using UnityEngine;
using WeatherHousePrototype;

namespace WeatherHouse.Life
{
    public sealed partial class LifeWorld
    {
        // Ordinary life owns idle animals; an actual interaction keeps the existing short movement lease.
        bool TryLifeStation(Binding animal, out Vector3 target)
        {
            target = animal.target;
            if (animal.rest || animal.weatherRetreat || Flat(animal.target, animal.home) > .25f ||
                drinks.Any(drink => drink.animal == animal) || demonstrationKey == animal.speaker.key)
                return true;
            GameplayHud hud = owner.GetComponent<GameplayHud>();
            if ((hud && hud.DialogueTarget == animal.speaker.Actor) ||
                (owner.Gameplay.Judging && owner.Gameplay.Observed == animal.speaker.Actor))
            {
                target = animal.speaker.transform.position;
                return true;
            }
            return false;
        }
    }
}
