using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace WeatherHouse.Life
{
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class LifeStrokeGraphic : MaskableGraphic
    {
        public float width = 11f;
        private readonly List<Vector2> points = new List<Vector2>();
        private Material ink;
        public void SetPath(IList<Vector2> path)
        { points.Clear(); if (path != null) for (int i = 0; i < path.Count; i++) points.Add(path[i]); SetVerticesDirty(); }
        protected override void Awake()
        {
            base.Awake(); raycastTarget = false;
            Shader shader = Resources.Load<Shader>("LifeGesture/GestureInk");
            if (shader) { ink = new Material(shader) { name = "Gesture ink (runtime)" }; material = ink; }
        }
        protected override void OnPopulateMesh(VertexHelper vh)
        { PopulateStroke(vh, points, width, color); }

        public static void PopulateStroke(VertexHelper vh, IList<Vector2> path, float strokeWidth, Color tint)
        {
            vh.Clear();
            for (int i = 1; i < path.Count; i++)
            {
                Vector2 a = path[i - 1], b = path[i], delta = b - a;
                if (delta.sqrMagnitude < .001f) continue;
                Vector2 normal = new Vector2(-delta.y, delta.x).normalized * strokeWidth;
                int index = vh.currentVertCount;
                vh.AddVert(a - normal, tint, new Vector2(0, 0));
                vh.AddVert(a + normal, tint, new Vector2(0, 1));
                vh.AddVert(b + normal, tint, new Vector2(1, 1));
                vh.AddVert(b - normal, tint, new Vector2(1, 0));
                vh.AddTriangle(index, index + 1, index + 2); vh.AddTriangle(index + 2, index + 3, index);
            }
            // Round ends and corners keep a broad, even brush stroke at direction changes.
            if (path.Count < 2) return;
            const int sides = 12;
            foreach (Vector2 point in path)
            {
                int center = vh.currentVertCount;
                vh.AddVert(point, tint, new Vector2(.5f, .5f));
                for (int i = 0; i < sides; i++)
                {
                    float angle = i * Mathf.PI * 2f / sides;
                    vh.AddVert(point + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * strokeWidth,
                        tint, new Vector2(.5f, 1f));
                }
                for (int i = 0; i < sides; i++)
                    vh.AddTriangle(center, center + 1 + i, center + 1 + (i + 1) % sides);
            }
        }
        protected override void OnDestroy() { if (ink) Destroy(ink); base.OnDestroy(); }
    }
}
