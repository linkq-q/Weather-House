using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace WeatherHouse.Life
{
    // World casting only. The journal continues to use its existing paper ink.
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class LifeSpellInkGraphic : MaskableGraphic
    {
        public const float WidthMultiplier = 3f;
        public float Daylight { get; set; } = 1f;
        public bool Released => released >= 0f;
        public int SampleCount => samples.Count;
        public override Texture mainTexture => surface ? surface : Texture2D.blackTexture;
        struct Sample { public Vector2 p; public float s, born, width; }
        struct Dust { public Vector2 p, velocity; public float born, life, size, phase, gold; }
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
        float released = -1f, length, carried;
        int echoIndex;
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
            samples.Clear(); motes.Clear(); released = -1f; length = carried = 0f; echoIndex = 0;
            geometryDirty = true; success = true;
            SetVerticesDirty();
        }

        public void SetPath(IList<Vector2> path)
        {
            if (path == null || path.Count == 0) { ClearStroke(); return; }
            if (path.Count < samples.Count || (samples.Count > 0 && path[0] != samples[0].p)) ClearStroke();
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
        { released = Time.unscaledTime; success = accepted; echoIndex = 0; }

        void Spawn(Vector2 p, int seed)
        {
            if (motes.Count >= 420) return;
            float phase = seed * 2.39996f;
            motes.Add(new Dust { p = p + new Vector2(Mathf.Cos(phase), Mathf.Sin(phase)) * 7f,
                velocity = new Vector2(Mathf.Sin(phase * 2f) * 13f, 10f + 9f * Mathf.Cos(phase)),
                born = Time.unscaledTime, life = 1.5f + .8f * Mathf.Abs(Mathf.Sin(phase)),
                size = seed % 4 == 0 ? 16f : 7f, phase = phase, gold = Mathf.Abs(Mathf.Sin(phase)) });
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
            motes.RemoveAll(p => now - p.born >= p.life);
            foreach (Dust p in motes)
            {
                float age = now - p.born, f = age / p.life;
                float alpha = Mathf.Min(1f, age * 16f) * Mathf.Pow(1f - f, 1.4f);
                if (releaseAge >= 0f) alpha *= 1f - Mathf.SmoothStep(0f, 1f, (releaseAge - .8f) / 1.35f);
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
            if (releaseAge > 2.2f) { ClearStroke(); return; }
            Rect r = rectTransform.rect;
            float scale = canvas ? canvas.scaleFactor : 1f;
            int w = Mathf.Max(1, Mathf.CeilToInt(r.width * scale)), h = Mathf.Max(1, Mathf.CeilToInt(r.height * scale));
            if (!surface || surface.width != w || surface.height != h)
            {
                ReleaseSurface();
                surface = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32) { name = "Spell ink surface", filterMode = FilterMode.Bilinear };
                surface.Create(); SetMaterialDirty(); SetVerticesDirty();
            }
            if (Released)
                while (echoIndex < samples.Count && samples[echoIndex].s < releaseAge / .75f * (length + 90f))
                { if (echoIndex % 8 == 0) Spawn(samples[echoIndex].p, echoIndex + 73); echoIndex++; }
            if (geometryDirty) BuildStroke();
            BuildDust(now, releaseAge);
            effect.SetFloat("_InkTime", now); effect.SetFloat("_Length", length); effect.SetFloat("_Release", releaseAge);
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
