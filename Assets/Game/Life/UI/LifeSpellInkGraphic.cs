using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace WeatherHouse.Life
{
    // Shared by world casting and handwriting input; reference diagrams retain paper ink.
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class LifeSpellInkGraphic : MaskableGraphic
    {
        public const float WidthMultiplier = 3f;
        public float Daylight { get; set; } = 1f;
        public bool Released => released >= 0f;
        public float ReleaseEndTime => releaseEnd;
        public int SampleCount => samples.Count;
        public override Texture mainTexture => surface ? surface : Texture2D.blackTexture;
        struct Sample { public Vector2 p; public float s, born, width; }
        struct Dust
        {
            public Vector2 p, velocity;
            public float born, life, size, phase, gold, gain, bend, rate, frequency, rise, hold, decay;
            public int tier;
        }
        readonly List<Sample> samples = new List<Sample>();
        readonly List<Dust> motes = new List<Dust>();
        readonly List<Vector3> vertices = new List<Vector3>();
        readonly List<Vector4> local = new List<Vector4>(), pathData = new List<Vector4>();
        readonly List<Vector2> widths = new List<Vector2>();
        readonly List<int> triangles = new List<int>();
        Mesh strokeMesh, dustMesh;
        Material effect, display;
        RenderTexture surface;
        CommandBuffer commands;
        float released = -1f, releaseEnd, length, carried, strokeSeed;
        uint randomState = 2761;
        bool geometryDirty;
        bool success = true;

        protected override void Awake()
        {
            base.Awake(); raycastTarget = false;
            effect = new Material(Resources.Load<Shader>("LifeGesture/SpellInk")) { name = "Spell ink effects" };
            display = new Material(Resources.Load<Shader>("LifeGesture/SpellInkComposite")) { name = "Spell ink display" };
            material = display;
            strokeMesh = new Mesh { name = "Round spell segments" }; strokeMesh.MarkDynamic();
            dustMesh = new Mesh { name = "Spell stardust" }; dustMesh.MarkDynamic();
            commands = new CommandBuffer { name = "Spell ink" };
        }

        public void ClearStroke()
        {
            samples.Clear(); motes.Clear(); released = -1f; releaseEnd = length = carried = 0f;
            geometryDirty = true; success = true;
            if (surface && commands != null)
            {
                commands.Clear(); commands.SetRenderTarget(surface); commands.ClearRenderTarget(false, true, Color.clear);
                Graphics.ExecuteCommandBuffer(commands);
            }
            SetVerticesDirty();
        }

        public void SetPath(IList<Vector2> path)
        {
            if (path == null || path.Count == 0) { ClearStroke(); return; }
            if (path.Count < samples.Count || (samples.Count > 0 && path[0] != samples[0].p)) ClearStroke();
            if (samples.Count == 0) strokeSeed = RandomValue() * 100f;
            for (int i = samples.Count; i < path.Count; i++)
            {
                Vector2 p = path[i];
                float radius = 2.4f;
                if (samples.Count > 0)
                {
                    Sample previous = samples[samples.Count - 1];
                    float distance = Vector2.Distance(p, previous.p);
                    float speed = distance / Mathf.Max(1f / 180f, Time.unscaledTime - previous.born);
                    radius = Mathf.Lerp(previous.width, Mathf.Clamp(3f - speed * .0019f, 1.25f, 3f), .2f);
                    length += distance; carried += distance;
                    if (carried >= 11f) { carried %= 11f; Spawn(p, i); }
                }
                samples.Add(new Sample { p = p, s = length, born = Time.unscaledTime, width = radius });
            }
            geometryDirty = true; SetVerticesDirty();
        }

        public void ReleaseStroke(bool accepted)
        {
            if (Released || samples.Count == 0) return;
            released = Time.unscaledTime; success = accepted; releaseEnd = released + 1.35f;
            SeedFireflies();
            foreach (Dust mote in motes)
                if (mote.tier >= 0) releaseEnd = Mathf.Max(releaseEnd, mote.born + mote.life);
        }

        float RandomValue()
        {
            randomState = unchecked(randomState * 1664525u + 1013904223u);
            return (randomState >> 8) / 16777216f;
        }
        static float Smooth(float a, float b, float value) => Mathf.SmoothStep(0f, 1f, (value - a) / (b - a));
        float MoteHash(float cell) { float n = Mathf.Sin(cell * 17.13f + strokeSeed) * 13.137f; return n - Mathf.Floor(n); }
        Vector2 PathPoint(float distance)
        {
            for (int i = 1; i < samples.Count; i++)
                if (samples[i].s >= distance)
                    return Vector2.Lerp(samples[i - 1].p, samples[i].p,
                        (distance - samples[i - 1].s) / Mathf.Max(.001f, samples[i].s - samples[i - 1].s));
            return samples[samples.Count - 1].p;
        }
        void SeedFireflies()
        {
            int leadSlot = Mathf.FloorToInt(RandomValue() * 7f);
            for (int cell = 0; cell < Mathf.Max(1, Mathf.CeilToInt(length / 30f)); cell++)
            {
                float arc = Mathf.Min(length, (cell + .2f + .6f * MoteHash(cell)) * 30f);
                float delay = .12f + .55f * MoteHash(cell + 4.7f);
                int tier = cell % 7 == leadSlot ? 0 : RandomValue() < .58f ? 1 : 2;
                SpawnFirefly(arc, tier, delay + .14f);
                if (RandomValue() > .18f)
                    SpawnFirefly(Mathf.Clamp(arc + (RandomValue() - .5f) * 24f, 0f, length), 2, delay + .08f + RandomValue() * .3f);
            }
        }
        void SpawnFirefly(float arc, int tier, float delay)
        {
            if (motes.Count >= 420) return;
            float angle = RandomValue() * Mathf.PI * 2f;
            motes.Add(new Dust { p = PathPoint(arc), born = released + delay, tier = tier,
                life = tier == 0 ? 2.05f + RandomValue() * .85f : tier == 1 ? 1.2f + RandomValue() * .95f : .65f + RandomValue() * .75f,
                size = tier == 0 ? 32f + RandomValue() * 12f : tier == 1 ? 15f + RandomValue() * 9f : 3f + RandomValue() * 5f,
                gain = tier == 0 ? .9f + RandomValue() * .1f : tier == 1 ? .45f + RandomValue() * .2f : .28f + RandomValue() * .22f,
                gold = RandomValue(), phase = RandomValue() * Mathf.PI * 2f,
                // Unity UI local Y points upward; the approved browser preview points downward.
                velocity = new Vector2(Mathf.Cos(angle) * (24f + RandomValue() * 44f), -Mathf.Sin(angle) * (12f + RandomValue() * 25f) + 20f + RandomValue() * 25f),
                bend = 3f + RandomValue() * 10f, rate = 3.8f + RandomValue() * 2.4f, frequency = 1.4f + RandomValue() * 2.8f,
                rise = .09f + RandomValue() * .15f, hold = .17f + RandomValue() * .17f, decay = 1.05f + RandomValue() * 1.35f });
        }
        Vector4 FireflySample(Dust p, float age)
        {
            float f = Mathf.Clamp01(age / p.life), travel = Smooth(0f, 1f, f);
            float move = 1f - (1f + p.rate * travel) * Mathf.Exp(-p.rate * travel);
            float bend = Mathf.Pow(Mathf.Sin(Mathf.PI * travel), 2f) * p.bend, angle = p.phase + p.frequency * travel;
            Vector2 position = p.p + p.velocity * move + new Vector2(Mathf.Sin(angle), -Mathf.Cos(angle)) * bend;
            float envelope = Smooth(0f, p.rise, age) * Mathf.Pow(1f - Smooth(p.hold, 1f, f), p.decay);
            float alpha = age < 0f ? 0f : p.gain * envelope * (1f + .07f * Mathf.Sin(p.phase + age * p.frequency) * (1f - Smooth(.35f, .7f, f)));
            return new Vector4(position.x, position.y, p.size * (1f - .36f * Smooth(.1f, 1f, f)) * Mathf.Lerp(1f, 1.24f, Daylight), alpha);
        }

        void Spawn(Vector2 p, int seed)
        {
            // Leave space for the release layers even after a long, fast handwritten stroke.
            if (motes.Count >= 160) return;
            float phase = seed * 2.39996f;
            motes.Add(new Dust { p = p + new Vector2(Mathf.Cos(phase), Mathf.Sin(phase)) * 7f,
                velocity = new Vector2(Mathf.Sin(phase * 2f) * 13f, 10f + 9f * Mathf.Cos(phase)),
                born = Time.unscaledTime, life = 1.5f + .8f * Mathf.Abs(Mathf.Sin(phase)),
                size = seed % 4 == 0 ? 16f : 7f, phase = phase, gold = Mathf.Abs(Mathf.Sin(phase)), tier = -1 });
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear(); if (samples.Count == 0 || !surface) return;
            Rect r = rectTransform.rect;
            vh.AddVert(new Vector2(r.xMin, r.yMin), Color.white, Vector2.zero);
            vh.AddVert(new Vector2(r.xMin, r.yMax), Color.white, Vector2.up);
            vh.AddVert(new Vector2(r.xMax, r.yMax), Color.white, Vector2.one);
            vh.AddVert(new Vector2(r.xMax, r.yMin), Color.white, Vector2.right);
            vh.AddTriangle(0, 1, 2); vh.AddTriangle(2, 3, 0);
        }

        void ResetGeometry() { vertices.Clear(); local.Clear(); pathData.Clear(); widths.Clear(); triangles.Clear(); }
        void Quad(Vector2 a, Vector2 b, float padding, Vector4 data, Vector2 radius)
        {
            Vector2 axis = (b - a).normalized, normal = new Vector2(-axis.y, axis.x);
            float distance = Vector2.Distance(a, b);
            int start = vertices.Count;
            foreach (Vector2 p in new[] { new Vector2(-padding, -padding), new Vector2(-padding, padding),
                new Vector2(distance + padding, padding), new Vector2(distance + padding, -padding) })
            {
                vertices.Add(a + axis * p.x + normal * p.y);
                local.Add(new Vector4(p.x, p.y, 0, 0)); pathData.Add(data); widths.Add(radius);
            }
            triangles.Add(start); triangles.Add(start + 1); triangles.Add(start + 2);
            triangles.Add(start + 2); triangles.Add(start + 3); triangles.Add(start);
        }
        void Upload(Mesh mesh)
        {
            mesh.Clear(); mesh.SetVertices(vertices); mesh.SetUVs(0, local); mesh.SetUVs(1, pathData);
            mesh.SetUVs(2, widths); mesh.SetTriangles(triangles, 0); mesh.RecalculateBounds();
        }
        void BuildStroke()
        {
            ResetGeometry();
            for (int i = 1; i < samples.Count; i++)
            {
                Sample a = samples[i - 1], b = samples[i];
                float distance = Vector2.Distance(a.p, b.p);
                if (distance < .001f) continue;
                Quad(a.p, b.p, 66f, new Vector4(a.s, distance, a.born, b.born), new Vector2(a.width, b.width));
            }
            Upload(strokeMesh); geometryDirty = false;
        }
        void DustQuad(Vector2 p, float size, float alpha, float star, float gold)
        {
            int start = vertices.Count;
            foreach (Vector2 uv in new[] { new Vector2(-1, -1), new Vector2(-1, 1), new Vector2(1, 1), new Vector2(1, -1) })
            {
                vertices.Add(p + uv * size * .5f); local.Add(new Vector4(uv.x, uv.y, 0, 0));
                pathData.Add(new Vector4(alpha, star, gold, 0)); widths.Add(Vector2.zero);
            }
            triangles.Add(start); triangles.Add(start + 1); triangles.Add(start + 2);
            triangles.Add(start + 2); triangles.Add(start + 3); triangles.Add(start);
        }
        void BuildDust(float now, float releaseAge)
        {
            ResetGeometry();
            motes.RemoveAll(p => now - p.born >= p.life || (p.tier < 0 && releaseAge > .35f));
            foreach (Dust p in motes)
            {
                float age = now - p.born, f = age / p.life;
                if (age < 0f) continue;
                if (p.tier >= 0)
                {
                    Vector4 q = FireflySample(p, age);
                    if (p.tier == 0)
                        for (int i = 3; i > 0; i--)
                        {
                            float tailAge = age - i * .035f; if (tailAge < 0f) continue;
                            Vector4 tail = FireflySample(p, tailAge);
                            DustQuad(new Vector2(tail.x, tail.y), q.z * .3f, q.w * (.13f - i * .025f), 0f, p.gold);
                        }
                    DustQuad(new Vector2(q.x, q.y), q.z, q.w, p.tier == 0 ? 4f : p.tier == 1 ? 5f : 0f, p.gold);
                    continue;
                }
                float alpha = Mathf.Min(1f, age * 16f) * Mathf.Pow(1f - f, 1.4f);
                if (releaseAge >= 0f) alpha *= 1f - Smooth(0f, .35f, releaseAge);
                Vector2 offset = p.velocity * age + new Vector2(Mathf.Sin(age * 3f + p.phase) * age * 4f, age * age * 3f);
                DustQuad(p.p + offset, p.size * (1f - f * .45f) * Mathf.Lerp(1f, 1.24f, Daylight), alpha, p.size > 10f ? 1f : 0f, p.gold);
            }
            if (!Released && samples.Count > 0) DustQuad(samples[samples.Count - 1].p, Mathf.Lerp(38f, 54f, Daylight), 1f, 2f, .5f);
            Upload(dustMesh);
        }

        void LateUpdate() { RenderInk(Time.unscaledTime); }
        public void RenderInk(float now)
        {
            if (!effect || samples.Count == 0 || !isActiveAndEnabled) return;
            float releaseAge = Released ? now - released : -1f;
            if (Released && now >= releaseEnd) { ClearStroke(); return; }
            Rect r = rectTransform.rect;
            float scale = canvas ? canvas.scaleFactor : 1f;
            int w = Mathf.Max(1, Mathf.CeilToInt(r.width * scale)), h = Mathf.Max(1, Mathf.CeilToInt(r.height * scale));
            if (!surface || surface.width != w || surface.height != h)
            {
                ReleaseSurface();
                surface = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32) { name = "Spell ink surface", filterMode = FilterMode.Bilinear };
                surface.Create(); SetMaterialDirty(); SetVerticesDirty();
            }
            if (geometryDirty) BuildStroke();
            BuildDust(now, releaseAge);
            effect.SetFloat("_InkTime", now); effect.SetFloat("_Length", length); effect.SetFloat("_Release", releaseAge);
            effect.SetFloat("_StrokeSeed", strokeSeed);
            effect.SetFloat("_Daylight", Mathf.Clamp01(Daylight)); effect.SetFloat("_Failure", success ? 0f : 1f);
            effect.SetVector("_Style", new Vector4(Mathf.Lerp(1f, 1.36f, Daylight) * WidthMultiplier,
                Mathf.Lerp(18f, 30f, Daylight), Mathf.Lerp(.23f, .46f, Daylight), Mathf.Lerp(.32f, .66f, Daylight)));
            commands.Clear(); commands.SetRenderTarget(surface); commands.ClearRenderTarget(false, true, Color.clear);
            commands.SetViewProjectionMatrices(Matrix4x4.identity, Matrix4x4.Ortho(r.xMin, r.xMax, r.yMin, r.yMax, -1f, 1f));
            if (strokeMesh.vertexCount > 0) commands.DrawMesh(strokeMesh, Matrix4x4.identity, effect, 0, 0);
            if (dustMesh.vertexCount > 0) commands.DrawMesh(dustMesh, Matrix4x4.identity, effect, 0, 1);
            Graphics.ExecuteCommandBuffer(commands);
        }

        static void DisposeObject(Object value) { if (!value) return; if (Application.isPlaying) Destroy(value); else DestroyImmediate(value); }
        void ReleaseSurface() { if (surface) { surface.Release(); DisposeObject(surface); surface = null; } }
        protected override void OnDisable() { ClearStroke(); ReleaseSurface(); base.OnDisable(); }
        protected override void OnDestroy()
        {
            ReleaseSurface(); commands?.Release();
            DisposeObject(strokeMesh); DisposeObject(dustMesh);
            DisposeObject(effect); DisposeObject(display); base.OnDestroy();
        }
    }
}
