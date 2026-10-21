using UnityEngine;

namespace WeatherHouse.Vegetation
{
    // The shared mesh is saved in the library, so a prefab also works outside the test scene.
    public sealed class PlantSpecimen : MonoBehaviour
    {
        public GrassSpecies source;
        [Range(0, 3)] public int variation;
    }
}
