using UnityEngine;

namespace WeatherHouse.AnimalToon
{
    [RequireComponent(typeof(Camera))]
    public sealed class AnimalToonView : MonoBehaviour
    {
        [Range(0, 24)] public float mergeRadius = 10;
        public Color edgeColor = new Color(1, .94f, .78f, 1);
    }
}
