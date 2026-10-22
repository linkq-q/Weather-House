using UnityEngine;

namespace WeatherHouse.MaterialLab
{
    [DisallowMultipleComponent, RequireComponent(typeof(Camera))]
    public sealed class OilPaintPostSettings : MonoBehaviour
    {
        public bool oilEnabled = true;
        [Range(0, 1)] public float oilStrength = .65f;
        [Range(0, 8)] public float radius = 3f;
        [Tooltip("0 disables color grouping. Higher values retain more tonal detail.")]
        [Range(0, 32)] public int colorSteps = 12;
        [Range(1, 3)] public float brushAspect = 1.5f;
        [Range(-180, 180)] public float brushAngle;

        public bool pixelEnabled = true;
        [Range(1, 8)] public float pixelSize = 2f;
        [Range(0, 1)] public float pixelStrength = .5f;

        [Tooltip("Left: original image. Right: enabled effects.")]
        public bool splitScreen;
        [Range(0, 1)] public float splitPosition = .5f;

        public float EffectiveOilStrength => oilEnabled && radius > 0 ? Mathf.Clamp01(oilStrength) : 0;
        public float EffectivePixelStrength => pixelEnabled && pixelSize > 1 ? Mathf.Clamp01(pixelStrength) : 0;
        public bool HasEffect => isActiveAndEnabled && (EffectiveOilStrength > 0 || EffectivePixelStrength > 0)
            && (!splitScreen || splitPosition < 1);
    }
}
