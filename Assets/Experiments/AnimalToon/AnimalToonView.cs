using UnityEngine;

namespace WeatherHouse.AnimalToon
{
    [RequireComponent(typeof(Camera))]
    public sealed class AnimalToonView : MonoBehaviour
    {
        [Range(0, 24)] public float mergeRadius = 10;
        public Color edgeColor = new Color(1, .94f, .78f, 1);
        [Tooltip("Camera-space depth in metres before the light edge starts thinning.")]
        [Min(0)] public float outlineLodStart = 20;
        [Tooltip("Camera-space depth in metres where the light edge reaches its minimum width.")]
        [Min(0)] public float outlineLodEnd = 55;
        [Range(0, 1)] public float outlineFarWidthScale = .4f;
    }
}
