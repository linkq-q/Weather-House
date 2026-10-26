using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace WeatherHouse.Life
{
    // Four independent skins share the authored leaf mesh and textures. Their bones are
    // sampled at different times, so opening reveals a real fan rather than a repeated leaf.
    public sealed class LifeJournalOpeningFan : IDisposable
    {
        public const int Count = 4;
        public const float Duration = .74f;
        private const float Travel = .50f, Stagger = .08f, Separation = .0012f;
        private readonly GameObject[] rigs = new GameObject[Count];
        private readonly Transform[] holders = new Transform[Count];
        private readonly SkinnedMeshRenderer[] pages = new SkinnedMeshRenderer[Count];
        private readonly Material[][] materials = new Material[Count][];
        private readonly float[] progress = new float[Count];
        private readonly AnimationClip clip;
        public SkinnedMeshRenderer[] Pages => pages;
        public float Progress(int index) => progress[index];
        public int Completed { get; private set; }
        public bool RevealingTarget => progress[Count - 1] > Initial(Count - 1) + .0001f;

        public LifeJournalOpeningFan(GameObject prefab, Transform parent, AnimationClip turn, Material[] paper, int layer)
        {
            clip = turn;
            for (int i = 0; i < Count; i++)
            {
                holders[i] = new GameObject("Opening leaf " + (i + 1)).transform;
                holders[i].SetParent(parent, false);
                rigs[i] = UnityEngine.Object.Instantiate(prefab, holders[i]);
                foreach (Transform part in rigs[i].GetComponentsInChildren<Transform>(true)) part.gameObject.layer = layer;
                foreach (Animator animator in rigs[i].GetComponentsInChildren<Animator>(true)) animator.enabled = false;
                foreach (Renderer renderer in rigs[i].GetComponentsInChildren<Renderer>(true))
                {
                    renderer.enabled = false;
                    if (renderer.name == "TurningPage") pages[i] = renderer as SkinnedMeshRenderer;
                }
                pages[i].updateWhenOffscreen = true;
                pages[i].shadowCastingMode = ShadowCastingMode.Off; pages[i].receiveShadows = false;
                materials[i] = new[] { new Material(paper[0]), new Material(paper[1]) };
                foreach (Material material in materials[i]) material.name = "Journal opening leaf";
                pages[i].sharedMaterials = materials[i];
            }
        }

        public void SetPages(Texture intro, Texture target, Rect leftPage, Rect rightPage)
        {
            for (int i = 0; i < Count; i++)
                for (int face = 0; face < 2; face++)
                {
                    bool finalReverse = i == Count - 1 && face == 1;
                    Material material = materials[i][face];
                    Rect crop = face == 0 ? rightPage : leftPage;
                    material.SetTexture("_BaseMap", finalReverse ? target : intro);
                    material.SetTextureScale("_BaseMap", crop.size);
                    material.SetTextureOffset("_BaseMap", crop.position);
                    material.SetColor("_BaseColor", Color.white);
                }
        }

        public void SampleCover(float openness)
        {
            float spread = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.5f, 1f, openness));
            Completed = 0;
            for (int i = 0; i < Count; i++) SampleLeaf(i, Initial(i) * spread, spread > 0);
        }

        public void Sample(float seconds)
        {
            Completed = 0;
            for (int i = 0; i < Count; i++)
            {
                float t = Mathf.Clamp01((seconds - i * Stagger) / Travel);
                float value = Mathf.Lerp(Initial(i), 1, t);
                if (t >= 1) Completed++;
                // A fully landed preliminary leaf joins the existing paper stack. Keeping
                // another coplanar skin there would create a second depth/texture surface.
                SampleLeaf(i, value, t < 1);
            }
        }

        private static float Initial(int index) => .24f - index * .07f;
        private void SampleLeaf(int index, float amount, bool visible)
        {
            progress[index] = amount;
            holders[index].localPosition = Vector3.up * ((Count - 1 - index) * Separation * (1 - amount));
            clip.SampleAnimation(rigs[index], amount * clip.length);
            pages[index].enabled = visible;
        }
        public void Hide() { foreach (SkinnedMeshRenderer page in pages) if (page) page.enabled = false; }
        public void Dispose()
        {
            foreach (Material[] pair in materials) foreach (Material material in pair) if (material) UnityEngine.Object.Destroy(material);
            // The studio owns the holders/rigs and destroys them together with the book.
        }
    }
}
