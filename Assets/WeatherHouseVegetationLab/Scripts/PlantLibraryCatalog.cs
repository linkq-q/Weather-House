using System;
using UnityEngine;

namespace WeatherHouse.Vegetation
{
    [CreateAssetMenu(menuName = "Weather House/Plant Library Catalog")]
    public sealed class PlantLibraryCatalog : ScriptableObject
    {
        [Serializable]
        public sealed class Entry
        {
            public string id;
            public string reference;
            [TextArea] public string silhouette;
            [TextArea] public string uncertainty;
            public GrassSpecies species;
            public GameObject[] variants;
        }
        public Entry[] entries;
    }
}
