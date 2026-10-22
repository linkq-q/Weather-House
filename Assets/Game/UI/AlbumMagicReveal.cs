using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace WeatherHousePrototype
{
    // Every item samples one book-space noise texture; groups only define aura shapes and particle bounds.
    internal sealed class AlbumMagicReveal : IDisposable
    {
        private readonly RectTransform root;
        private readonly List<AlbumRevealGroup> groups = new List<AlbumRevealGroup>();
        private readonly Vector3[] corners = new Vector3[4];
        private readonly Material particlesMaterial;
        private readonly Texture2D noiseTexture;
        private readonly Canvas canvas;
        private AdditionalCanvasShaderChannels addedChannels;
        private RectTransform auraRoot;
        private AlbumRevealStars stars;
        private bool assigned;
        private bool disposed;

        internal AlbumMagicReveal(RectTransform book, Transform artwork)
        {
            root = book;
            Shader shader = Resources.Load<Shader>("AlbumBook/AlbumMagicReveal");
            Shader auraShader = Resources.Load<Shader>("AlbumBook/AlbumMagicAura");
            Shader particlesShader = Resources.Load<Shader>("AlbumBook/AlbumMagicParticles");
            if (shader == null || !shader.isSupported)
                throw new InvalidOperationException("Album magic reveal shader is missing or unsupported.");
            if (auraShader == null || !auraShader.isSupported || particlesShader == null || !particlesShader.isSupported)
                throw new InvalidOperationException("Album magic aura or particles shader is missing or unsupported.");
            noiseTexture = Resources.Load<Texture2D>("AlbumBook/RevealNoise");
            if (noiseTexture == null || !noiseTexture.isReadable)
                throw new InvalidOperationException("AlbumBook/RevealNoise must be present and readable for the shared reveal field.");
            canvas = root.GetComponentInParent<Canvas>();
            particlesMaterial = new Material(particlesShader)
                { name = "Album opening particles (runtime)", hideFlags = HideFlags.DontSave };
            var byName = new Dictionary<string, AlbumRevealGroup>();
            var frames = new RectTransform[4];
            for (int slot = 0; slot < frames.Length; slot++)
                frames[slot] = root.Find("Photo frame " + (slot + 1)) as RectTransform;
            foreach (Graphic graphic in root.GetComponentsInChildren<Graphic>(true))
            {
                if (graphic.transform == artwork || graphic.transform.IsChildOf(artwork)) continue;
                Transform item = graphic.transform;
                while (item.parent != root && item.parent != null) item = item.parent;
                string key = item.name;
                bool namedPhotoPart = key.StartsWith("Photo frame ", StringComparison.Ordinal)
                    || key.StartsWith("Photo sticker ", StringComparison.Ordinal)
                    || key.StartsWith("Sample photo ", StringComparison.Ordinal);
                bool photo = namedPhotoPart || (item.GetComponent<RawImage>() != null
                    && key.StartsWith("Photo ", StringComparison.Ordinal));
                if (photo)
                {
                    int slot;
                    if (!namedPhotoPart || !int.TryParse(key.Substring(key.LastIndexOf(' ') + 1), out slot)
                        || slot < 1 || slot > 4)
                    {
                        slot = 1;
                        float nearest = float.PositiveInfinity;
                        for (int candidate = 0; candidate < frames.Length; candidate++)
                        {
                            if (frames[candidate] == null) continue;
                            float distance = (item.position - frames[candidate].position).sqrMagnitude;
                            if (distance >= nearest) continue;
                            nearest = distance;
                            slot = candidate + 1;
                        }
                    }
                    key = "Photo slot " + slot;
                }
                if (!byName.TryGetValue(key, out AlbumRevealGroup group))
                {
                    int index = groups.Count;
                    bool navigation = IsNavigation(item.name);
                    group = new AlbumRevealGroup
                    {
                        material = new Material(shader)
                        {
                            name = "Album opening reveal (runtime)", hideFlags = HideFlags.DontSave
                        },
                        particleSeed = index + 1,
                        navigation = navigation,
                        photo = photo,
                        sparkles = photo || navigation || item.name.StartsWith("Word ", StringComparison.Ordinal)
                    };
                    if (group.sparkles)
                    {
                        group.auraMaterial = new Material(auraShader)
                            { name = "Album opening aura (runtime)", hideFlags = HideFlags.DontSave };
                        group.auraMaterial.SetFloat("_Shape", photo || navigation ? 0f : 1f);
                    }
                    groups.Add(group);
                    byName.Add(key, group);
                }
                group.graphics.Add(graphic);
                group.originals.Add(graphic.material);
                group.coordinates.Add(null);
            }
        }

        internal void Sample(float progress, float seconds)
        {
            if (disposed) return;
            AlbumMagicRevealSettings settings = AlbumMagicRevealSettings.Load();
            bool dissolve = settings.enableNoiseDissolve;
            if (assigned && !dissolve) Restore();
            if (dissolve && !assigned && canvas != null)
            {
                addedChannels = AdditionalCanvasShaderChannels.TexCoord1 & ~canvas.additionalShaderChannels;
                canvas.additionalShaderChannels |= AdditionalCanvasShaderChannels.TexCoord1;
            }
            progress = Mathf.Clamp01(progress);
            Matrix4x4 worldToLocal = root.worldToLocalMatrix;
            EnsureOverlays(dissolve);
            foreach (AlbumRevealGroup group in groups)
            {
                Vector2 minimum = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
                Vector2 maximum = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
                for (int i = 0; i < group.graphics.Count; i++)
                {
                    Graphic graphic = group.graphics[i];
                    if (graphic == null) continue;
                    if (dissolve && !assigned)
                    {
                        graphic.material = group.material;
                        var effect = graphic.gameObject.AddComponent<AlbumRevealCoordinates>();
                        effect.bookRoot = root;
                        group.coordinates[i] = effect;
                    }
                    if (dissolve) graphic.SetVerticesDirty();
                    graphic.rectTransform.GetWorldCorners(corners);
                    foreach (Vector3 corner in corners)
                    {
                        Vector2 point = worldToLocal.MultiplyPoint3x4(corner);
                        minimum = Vector2.Min(minimum, point);
                        maximum = Vector2.Max(maximum, point);
                    }
                }
                if (float.IsInfinity(minimum.x)) continue;
                group.bounds = Rect.MinMaxRect(minimum.x, minimum.y, maximum.x, maximum.y);
                group.progress = progress;
                Vector4 origin = new Vector4(minimum.x, minimum.y,
                    Mathf.Max(1f, maximum.x - minimum.x), Mathf.Max(1f, maximum.y - minimum.y));
                if (dissolve) SetField(group.material, progress, origin, settings);
                if (group.aura != null)
                {
                    group.aura.rectTransform.anchoredPosition = group.bounds.center;
                    float padding = Mathf.Max(38f, settings.glowSpread * 1.6f);
                    group.aura.rectTransform.sizeDelta = group.bounds.size + Vector2.one * padding * 2f;
                    group.aura.SetVerticesDirty();
                    SetField(group.auraMaterial, progress, origin, settings);
                }
            }
            assigned = dissolve;
            stars.SetTime(seconds, groups, settings, noiseTexture);
        }

        private void EnsureOverlays(bool dissolve)
        {
            if (dissolve && auraRoot == null)
            {
                auraRoot = OverlayNode(root, "Opening magic aura");
                foreach (AlbumRevealGroup group in groups)
                {
                    if (!group.sparkles) continue;
                    var node = OverlayNode(auraRoot, (group.photo ? "Photo" : "Word") + " aura " + group.particleSeed);
                    group.aura = node.gameObject.AddComponent<AlbumRevealAura>();
                    group.aura.bookRoot = root;
                    group.aura.material = group.auraMaterial;
                    group.aura.raycastTarget = false;
                }
            }
            if (stars == null)
            {
                var rect = OverlayNode(root, "Opening magic stars");
                stars = rect.gameObject.AddComponent<AlbumRevealStars>();
                stars.material = particlesMaterial;
                stars.raycastTarget = false;
            }
            stars.transform.SetAsLastSibling();
        }

        internal static bool IsNavigation(string name)
        {
            return name == "Symbols tab" || name == "Photos tab" || name == "Close album";
        }

        private static RectTransform OverlayNode(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = parent.gameObject.layer;
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
            rect.sizeDelta = AlbumBookView.ReferenceSize;
            return rect;
        }

        private void SetField(Material material, float progress, Vector4 origin, AlbumMagicRevealSettings settings)
        {
            material.SetFloat("_Reveal", progress);
            material.SetVector("_NoiseOrigin", origin);
            material.SetTexture("_NoiseTex", noiseTexture);
            material.SetFloat("_NoiseSize", Mathf.Max(1f, settings.noiseSize));
            material.SetVector("_NoiseOffset", new Vector4(settings.noiseOffset.x, settings.noiseOffset.y, 0f, 0f));
            material.SetFloat("_EdgeWidth", settings.edgeWidth);
            material.SetFloat("_Softness", settings.softness);
            material.SetColor("_AmberColor", settings.amber);
            material.SetColor("_GoldColor", settings.gold);
            material.SetColor("_IvoryColor", settings.ivory);
            material.SetFloat("_GlowIntensity", settings.glowIntensity);
            material.SetFloat("_GlowSpread", settings.glowSpread);
        }

        internal void Restore()
        {
            if (assigned)
            {
                foreach (AlbumRevealGroup group in groups)
                {
                    for (int i = 0; i < group.graphics.Count; i++)
                    {
                        if (group.graphics[i] != null) group.graphics[i].material = group.originals[i];
                        if (group.coordinates[i] == null) continue;
                        group.coordinates[i].enabled = false;
                        UnityEngine.Object.Destroy(group.coordinates[i]);
                        group.coordinates[i] = null;
                    }
                }
                assigned = false;
            }
            // Release this playback's channel synchronously, before a replacement book can acquire it.
            if (canvas != null) canvas.additionalShaderChannels &= ~addedChannels;
            addedChannels = 0;
            if (auraRoot != null)
            {
                auraRoot.gameObject.SetActive(false);
                UnityEngine.Object.Destroy(auraRoot.gameObject);
                auraRoot = null;
                foreach (AlbumRevealGroup group in groups) group.aura = null;
            }
            if (stars == null) return;
            stars.gameObject.SetActive(false);
            UnityEngine.Object.Destroy(stars.gameObject);
            stars = null;
        }

        public void Dispose()
        {
            if (disposed) return;
            Restore();
            foreach (AlbumRevealGroup group in groups)
            {
                if (group.material != null) UnityEngine.Object.Destroy(group.material);
                if (group.auraMaterial != null) UnityEngine.Object.Destroy(group.auraMaterial);
            }
            if (particlesMaterial != null) UnityEngine.Object.Destroy(particlesMaterial);
            disposed = true;
        }

    }

    internal sealed class AlbumRevealGroup
    {
        internal readonly List<Graphic> graphics = new List<Graphic>();
        internal readonly List<Material> originals = new List<Material>();
        internal readonly List<AlbumRevealCoordinates> coordinates = new List<AlbumRevealCoordinates>();
        internal Material material;
        internal Material auraMaterial;
        internal AlbumRevealAura aura;
        internal int particleSeed;
        internal bool navigation;
        internal Rect bounds;
        internal float progress;
        internal bool photo;
        internal bool sparkles;
    }

    internal sealed class AlbumRevealCoordinates : BaseMeshEffect
    {
        internal RectTransform bookRoot;

        public override void ModifyMesh(VertexHelper mesh)
        {
            if (!IsActive() || bookRoot == null) return;
            UIVertex vertex = default;
            for (int i = 0; i < mesh.currentVertCount; i++)
            {
                mesh.PopulateUIVertex(ref vertex, i);
                Vector3 point = bookRoot.InverseTransformPoint(transform.TransformPoint(vertex.position));
                vertex.uv1 = new Vector4(point.x, point.y, 0f, 0f);
                mesh.SetUIVertex(vertex, i);
            }
        }
    }

    [RequireComponent(typeof(CanvasRenderer))]
    internal sealed class AlbumRevealAura : MaskableGraphic
    {
        internal RectTransform bookRoot;

        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            Rect rect = GetPixelAdjustedRect();
            mesh.AddVert(new Vector2(rect.xMin, rect.yMin), color, new Vector2(0f, 0f));
            mesh.AddVert(new Vector2(rect.xMin, rect.yMax), color, new Vector2(0f, 1f));
            mesh.AddVert(new Vector2(rect.xMax, rect.yMax), color, new Vector2(1f, 1f));
            mesh.AddVert(new Vector2(rect.xMax, rect.yMin), color, new Vector2(1f, 0f));
            mesh.AddTriangle(0, 1, 2);
            mesh.AddTriangle(0, 2, 3);
            if (bookRoot == null) return;
            UIVertex vertex = default;
            for (int i = 0; i < mesh.currentVertCount; i++)
            {
                mesh.PopulateUIVertex(ref vertex, i);
                Vector3 point = bookRoot.InverseTransformPoint(transform.TransformPoint(vertex.position));
                vertex.uv1 = new Vector4(point.x, point.y, 0f, 0f);
                mesh.SetUIVertex(vertex, i);
            }
        }
    }

    [RequireComponent(typeof(CanvasRenderer))]
    internal sealed class AlbumRevealStars : MaskableGraphic
    {
        private float seconds;
        private List<AlbumRevealGroup> groups;
        private AlbumMagicRevealSettings settings;
        private Texture2D noiseTexture;

        internal void SetTime(float time, List<AlbumRevealGroup> regions, AlbumMagicRevealSettings parameters,
            Texture2D noise)
        {
            seconds = time;
            groups = regions;
            settings = parameters;
            noiseTexture = noise;
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            if (groups == null || settings == null || noiseTexture == null
                || seconds <= settings.revealStart || seconds >= settings.TotalDuration) return;
            float tail = Mathf.Clamp01((settings.TotalDuration - seconds) / .10f);
            foreach (AlbumRevealGroup group in groups)
            {
                if (!group.sparkles) continue;
                int count = group.photo ? Mathf.Clamp(settings.photoDustCount, 0, 300) + 4
                    : Mathf.Clamp(settings.wordDustCount, 0, 60) + 1;
                for (int i = 0; i < count; i++)
                {
                    float seed = group.particleSeed * .173205f;
                    bool star = group.photo ? i < 4 : i == 0;
                    float fx = Fraction(i * .754878f + seed * .73f + .19f);
                    float fy = Fraction(i * .569841f + seed * 1.31f + .37f);
                    if (!group.photo && !group.navigation)
                    {
                        float direction = fx * Mathf.PI * 2f;
                        float distance = .43f * Mathf.Sqrt(fy);
                        fx = .5f + Mathf.Cos(direction) * distance;
                        fy = .5f + Mathf.Sin(direction) * distance;
                    }
                    Rect bounds = group.bounds;
                    Vector2 source = new Vector2(Mathf.Lerp(bounds.xMin, bounds.xMax, fx),
                        Mathf.Lerp(bounds.yMin, bounds.yMax, fy));
                    Vector2 noiseUv = source / Mathf.Max(1f, settings.noiseSize) + settings.noiseOffset;
                    // This is the exact texture and book-space UV used by both reveal shaders.
                    float noise = noiseTexture.GetPixelBilinear(noiseUv.x, noiseUv.y).r;
                    float birth = (noise + settings.softness) / (1f + 2f * settings.softness);
                    float age = (group.progress - birth) / (star ? .33f : .30f);
                    if (age <= 0f || age >= 1f) continue;
                    float pulse = Mathf.Pow(Mathf.Sin(age * Mathf.PI), .7f) * tail;
                    Vector2 outward = new Vector2(fx - .5f, fy - .5f).normalized;
                    if (outward.sqrMagnitude < .01f)
                        outward = new Vector2(Mathf.Cos(seed * 9f), Mathf.Sin(seed * 9f));
                    Vector2 center = source;
                    center += outward * (12f + Fraction(i * .414214f + seed) * 16f) * age;
                    Vector2 size = star ? settings.starSize : settings.dustSize;
                    float radius = Mathf.Lerp(Mathf.Max(.1f, size.x), Mathf.Max(.1f, size.y), fx) * pulse;
                    AddParticle(mesh, center, radius, pulse, star, Fraction(i * .382f + seed * .81f), settings);
                }
            }
        }

        private static void AddParticle(VertexHelper mesh, Vector2 center, float radius, float pulse, bool star,
            float warmth, AlbumMagicRevealSettings settings)
        {
            int halo = mesh.currentVertCount;
            Color haloColor = Color.Lerp(settings.gold, settings.amber, warmth);
            haloColor.a = .44f * pulse;
            mesh.AddVert(center, haloColor, Vector2.zero);
            const int ring = 8;
            for (int j = 0; j < ring; j++)
            {
                float angle = j * Mathf.PI * 2f / ring;
                Color transparent = haloColor;
                transparent.a = 0f;
                mesh.AddVert(center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius * 3f,
                    transparent, Vector2.zero);
            }
            for (int j = 0; j < ring; j++) mesh.AddTriangle(halo, halo + 1 + j, halo + 1 + (j + 1) % ring);
            int core = mesh.currentVertCount;
            Color coreColor = star ? settings.ivory : warmth < .5f
                ? Color.Lerp(settings.ivory, settings.gold, warmth * 2f)
                : Color.Lerp(settings.gold, settings.amber, (warmth - .5f) * 2f);
            coreColor.a = pulse;
            mesh.AddVert(center, coreColor, Vector2.zero);
            for (int j = 0; j < ring; j++)
            {
                float angle = j * Mathf.PI / 4f;
                float length = star && j % 2 != 0 ? radius * .19f : radius;
                Color pointColor = Color.Lerp(coreColor, settings.gold, .35f);
                pointColor.a = pulse;
                mesh.AddVert(center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * length,
                    pointColor, Vector2.zero);
            }
            for (int j = 0; j < ring; j++) mesh.AddTriangle(core, core + 1 + j, core + 1 + (j + 1) % ring);
        }

        private static float Fraction(float value) { return value - Mathf.Floor(value); }
    }
}
