using System;
using UnityEngine;

namespace WeatherHouse.EnvironmentLab
{
    [DisallowMultipleComponent]
    public sealed class FogCard : MonoBehaviour
    {
        public Renderer targetRenderer;
        [Tooltip("RGB tint; opacity below controls alpha.")]
        public Color tint = new Color(.65f, .73f, .75f, 1);
        [Range(0, 1)] public float opacity = .22f;
        [Min(0)] public float nearFadeStart = 4;
        [Min(.01f)] public float nearFadeEnd = 12;
        [Min(.01f)] public float intersectionFadeDistance = 2;

        Renderer boundRenderer;
        Material previousMaterial;
        Material ownedMaterial;
        bool settingsDirty;

        void OnEnable() { ApplySettings(); }
        void OnDisable() { ReleaseMaterial(); }
        void OnValidate() { settingsDirty = true; }

        void Update()
        {
            if (!settingsDirty) return;
            settingsDirty = false;
            ApplySettings();
        }

        // Builders may call this only on their own persistent material copy.
        public void ApplyTo(Material material)
        {
            if (!material || !material.shader || material.shader.name != "Universal Render Pipeline/Particles/Unlit")
                throw new ArgumentException("Fog cards require a copy of the URP Particles/Unlit fog material.", nameof(material));

            float near = Mathf.Max(0, nearFadeStart);
            float far = Mathf.Max(near + .01f, nearFadeEnd);
            float intersection = Mathf.Max(.01f, intersectionFadeDistance);
            material.SetColor("_BaseColor", new Color(tint.r, tint.g, tint.b, Mathf.Clamp01(opacity)));
            material.SetFloat("_CameraFadingEnabled", 1);
            material.SetFloat("_CameraNearFadeDistance", near);
            material.SetFloat("_CameraFarFadeDistance", far);
            material.SetVector("_CameraFadeParams", new Vector4(near, 1 / (far - near), 0, 0));
            material.SetFloat("_SoftParticlesEnabled", 1);
            material.SetFloat("_SoftParticlesNearFadeDistance", 0);
            material.SetFloat("_SoftParticlesFarFadeDistance", intersection);
            material.SetVector("_SoftParticleFadeParams", new Vector4(0, 1 / intersection, 0, 0));
            material.EnableKeyword("_SOFTPARTICLES_ON");
            material.EnableKeyword("_FADING_ON");
            // shortcut: opaque scene depth cannot soften against transparent water; keep cards beyond navigable water and review sorting.
        }

        public void ApplySettings()
        {
            if (!Application.isPlaying || !isActiveAndEnabled) return;
            if (boundRenderer != targetRenderer || (boundRenderer && ownedMaterial && boundRenderer.sharedMaterial != ownedMaterial))
                ReleaseMaterial();
            if (!targetRenderer || !targetRenderer.sharedMaterial) return;
            if (!ownedMaterial)
            {
                previousMaterial = targetRenderer.sharedMaterial;
                ownedMaterial = new Material(previousMaterial) { name = previousMaterial.name + " (Fog Play)", hideFlags = HideFlags.DontSave };
                boundRenderer = targetRenderer;
                boundRenderer.sharedMaterial = ownedMaterial;
            }
            ApplyTo(ownedMaterial);
        }

        void ReleaseMaterial()
        {
            if (boundRenderer && boundRenderer.sharedMaterial == ownedMaterial)
                boundRenderer.sharedMaterial = previousMaterial;
            if (ownedMaterial) Destroy(ownedMaterial);
            ownedMaterial = null;
            previousMaterial = null;
            boundRenderer = null;
        }
    }
}
