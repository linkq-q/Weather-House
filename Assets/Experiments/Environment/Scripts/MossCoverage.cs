using System;
using System.Collections.Generic;
using UnityEngine;

namespace WeatherHouse.EnvironmentLab
{
    // Preview copies only exist during Play. Saved materials remain reusable assets.
    public sealed class MossCoverage : MonoBehaviour
    {
        public Renderer[] targets = Array.Empty<Renderer>();
        [Range(0, 1)] public float coverage = .62f;
        [Range(.1f, 2)] public float edgeSharpness = .7f;
        [Range(.25f, 8)] public float textureScale = 2;
        public Color mossColor = new Color(.66f, .78f, .39f, 1);
        public bool showMoss = true;
        readonly List<Slot> slots = new List<Slot>();
        sealed class Slot { public Renderer renderer; public Material[] originals, copies; }

        void OnEnable() { if (Application.isPlaying) Bind(); }
        void OnValidate() { if (Application.isPlaying) Apply(); }
        void OnDisable() { Restore(); }
        void OnDestroy() { Restore(); }

        public void Bind()
        {
            Restore();
            var seen = new HashSet<Renderer>();
            foreach (var renderer in targets)
            {
                if (!renderer || !seen.Add(renderer)) continue;
                var originals = renderer.sharedMaterials;
                var copies = (Material[])originals.Clone();
                for (int i = 0; i < originals.Length; i++)
                    if (originals[i] && originals[i].HasProperty("_CoverageLevel"))
                        copies[i] = new Material(originals[i]) { name = originals[i].name + " (Moss Preview)", hideFlags = HideFlags.DontSave };
                renderer.sharedMaterials = copies;
                slots.Add(new Slot { renderer = renderer, originals = originals, copies = copies });
            }
            Apply();
        }

        public void Apply()
        {
            foreach (var slot in slots)
                for (int i = 0; i < slot.copies.Length; i++)
                    if (slot.copies[i] != slot.originals[i]) ApplyTo(slot.copies[i]);
        }

        public void ApplyTo(Material material)
        {
            material.SetFloat("_Coverageon", 1);
            material.EnableKeyword("_COVERAGEON_ON");
            material.SetFloat("_OverlayMethod", 0);
            material.EnableKeyword("_OVERLAYMETHOD_WORLD_NORMAL");
            material.DisableKeyword("_OVERLAYMETHOD_VERTEX_POSITION");
            material.SetFloat("_CoverageLevel", showMoss ? Mathf.Lerp(-.85f, 0, Mathf.Clamp01(coverage)) : -2);
            material.SetFloat("_CoverageFade", Mathf.Clamp(edgeSharpness, .1f, 2));
            material.SetColor("_CoverageColor", mossColor);
            foreach (string property in new[] { "_CoverageAlbedo", "_CoverageNormal", "_CoverageMask" })
                material.SetTextureScale(property, Vector2.one * Mathf.Clamp(textureScale, .25f, 8));
        }

        public void Restore()
        {
            foreach (var slot in slots)
            {
                if (slot.renderer)
                {
                    var current = slot.renderer.sharedMaterials;
                    for (int i = 0; i < current.Length && i < slot.copies.Length; i++)
                        if (current[i] == slot.copies[i]) current[i] = slot.originals[i];
                    slot.renderer.sharedMaterials = current;
                }
                for (int i = 0; i < slot.copies.Length; i++)
                    if (slot.copies[i] != slot.originals[i] && slot.copies[i])
                    {
                        if (Application.isPlaying) Destroy(slot.copies[i]);
                        else DestroyImmediate(slot.copies[i]);
                    }
            }
            slots.Clear();
        }
    }
}
