using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace WeatherHousePrototype
{
    public enum UiMark { PlayerBubble, AnimalBubble, Ellipsis, Close, Viewfinder, Shutter, Dot }

    [AddComponentMenu("Weather House/Procedural UI Graphic")]
    public sealed class ProceduralUiGraphic : MaskableGraphic
    {
        public UiMark mark;
        [Min(.5f)] public float lineWidth = 3f;
        [Range(0f, .05f)] public float waviness = .025f;
        public int seed = 7;
        [Range(.15f, .5f)] public float bubbleRadius = .47f;
        [Range(0f, .24f)] public float tailSize = .13f;
        [Range(0f, 40f)] public float cornerRadius = 18f;
        [Range(.22f, .38f)] public float shutterInnerRatio = .318f;
        [Range(.5f, 2f)] public float shutterOuterLineScale = 1f;
        [Range(.2f, 1f)] public float shutterInnerLineScale = .44f;
        [Range(10f, 80f)] public float closeGap = 35f;

        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            Rect rect = GetPixelAdjustedRect();
            if (rect.width < 4f || rect.height < 4f) return;
            Vector2 center = rect.center;
            float size = Mathf.Min(rect.width, rect.height);
            switch (mark)
            {
                case UiMark.PlayerBubble:
                case UiMark.AnimalBubble:
                    Bubble(mesh, rect, mark == UiMark.AnimalBubble);
                    break;
                case UiMark.Ellipsis:
                    for (int i = -1; i <= 1; i++)
                        Disc(mesh, center + Vector2.right * (i * rect.width * .145f),
                            Mathf.Min(rect.height * .13f, rect.width * .035f));
                    break;
                case UiMark.Dot:
                    Disc(mesh, center, size * .35f);
                    break;
                case UiMark.Close:
                    Stroke(mesh, Arc(center, size * .40f, closeGap * .5f, 180f - closeGap * .5f, 40), lineWidth);
                    Stroke(mesh, Arc(center, size * .40f, 180f + closeGap * .5f, 360f - closeGap * .5f, 44), lineWidth);
                    float cross = size * .125f;
                    Stroke(mesh, new List<Vector2> { center + new Vector2(-cross, -cross), center + new Vector2(cross, cross) }, lineWidth * .85f);
                    Stroke(mesh, new List<Vector2> { center + new Vector2(-cross, cross), center + new Vector2(cross, -cross) }, lineWidth * .85f);
                    break;
                case UiMark.Shutter:
                    Stroke(mesh, Arc(center, size * .405f, 0f, 360f, 96), lineWidth * shutterOuterLineScale, true);
                    Stroke(mesh, Arc(center, shutterInnerRatio * size, 0f, 360f, 96), lineWidth * shutterInnerLineScale, true);
                    break;
                case UiMark.Viewfinder:
                    Viewfinder(mesh, rect);
                    break;
            }
        }

        private void Bubble(VertexHelper mesh, Rect rect, bool tail)
        {
            float inset = 2f + Mathf.Min(rect.width, rect.height) * waviness;
            rect = Rect.MinMaxRect(rect.xMin + inset, rect.yMin + inset,
                rect.xMax - inset, rect.yMax - inset);
            float tailHeight = tail ? rect.height * tailSize : 0f;
            rect.yMin += tailHeight;
            float radius = Mathf.Min(rect.height * bubbleRadius, rect.width * .2f);
            var path = new List<Vector2>(160);
            int straightSteps = Mathf.Clamp(Mathf.CeilToInt(rect.width / 9f), 24, 70);
            for (int i = 0; i <= straightSteps; i++)
            {
                float t = i / (float)straightSteps;
                float x = Mathf.Lerp(rect.xMin + radius, rect.xMax - radius, t);
                // The small animal tail belongs to the same silhouette as the bubble.
                float tailT = Mathf.Clamp01(1f - Mathf.Abs((t - .70f) / .075f));
                path.Add(new Vector2(x, rect.yMin - tailHeight * tailT * tailT));
            }
            path.AddRange(Arc(new Vector2(rect.xMax - radius, rect.yMin + radius), radius, -90f, 0f, 16));
            AddLine(path, new Vector2(rect.xMax, rect.yMin + radius), new Vector2(rect.xMax, rect.yMax - radius), 8);
            path.AddRange(Arc(new Vector2(rect.xMax - radius, rect.yMax - radius), radius, 0f, 90f, 16));
            AddLine(path, new Vector2(rect.xMax - radius, rect.yMax), new Vector2(rect.xMin + radius, rect.yMax), straightSteps);
            path.AddRange(Arc(new Vector2(rect.xMin + radius, rect.yMax - radius), radius, 90f, 180f, 16));
            AddLine(path, new Vector2(rect.xMin, rect.yMax - radius), new Vector2(rect.xMin, rect.yMin + radius), 8);
            path.AddRange(Arc(new Vector2(rect.xMin + radius, rect.yMin + radius), radius, 180f, 270f, 16));
            RemoveRepeatedPoints(path);
            for (int i = 0; i < path.Count; i++)
            {
                Vector2 delta = path[i] - rect.center;
                float angle = Mathf.Atan2(delta.y / rect.height, delta.x / rect.width);
                float wave = Mathf.Sin(angle * 5f + seed * .73f) + .3f * Mathf.Sin(angle * 9f - seed * .41f);
                path[i] += delta.normalized * (rect.height * waviness * wave);
            }
            Fill(mesh, path, rect.center);
        }

        private void Viewfinder(VertexHelper mesh, Rect rect)
        {
            float pad = lineWidth + 2f;
            float halfW = rect.width * .5f - pad;
            float halfH = rect.height * .5f - pad;
            float leg = Mathf.Min(rect.width, rect.height) * .16f;
            float radius = Mathf.Min(cornerRadius, leg);
            foreach (float xSign in new[] { -1f, 1f })
            foreach (float ySign in new[] { -1f, 1f })
            {
                var path = new List<Vector2> { new Vector2(halfW - leg, halfH) };
                path.AddRange(Arc(new Vector2(halfW - radius, halfH - radius), radius, 90f, 0f, 14));
                path.Add(new Vector2(halfW, halfH - leg));
                for (int i = 0; i < path.Count; i++)
                    path[i] = rect.center + new Vector2(path[i].x * xSign, path[i].y * ySign);
                Stroke(mesh, path, lineWidth);
            }
        }

        private static List<Vector2> Arc(Vector2 center, float radius, float from, float to, int segments)
        {
            var points = new List<Vector2>(segments + 1);
            for (int i = 0; i <= segments; i++)
            {
                float angle = Mathf.Lerp(from, to, i / (float)segments) * Mathf.Deg2Rad;
                points.Add(center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius);
            }
            return points;
        }

        private static void AddLine(List<Vector2> points, Vector2 from, Vector2 to, int segments)
        {
            for (int i = 1; i <= segments; i++) points.Add(Vector2.Lerp(from, to, i / (float)segments));
        }

        private static void RemoveRepeatedPoints(List<Vector2> points)
        {
            for (int i = points.Count - 1; i > 0; i--)
                if ((points[i] - points[i - 1]).sqrMagnitude < .001f) points.RemoveAt(i);
            if ((points[0] - points[points.Count - 1]).sqrMagnitude < .001f)
                points.RemoveAt(points.Count - 1);
        }

        private void Disc(VertexHelper mesh, Vector2 center, float radius)
        {
            var points = Arc(center, radius, 0f, 360f, 32);
            points.RemoveAt(points.Count - 1);
            Fill(mesh, points, center);
        }

        private void Fill(VertexHelper mesh, List<Vector2> points, Vector2 center)
        {
            int start = mesh.currentVertCount;
            mesh.AddVert(center, color, Vector2.zero);
            Color transparent = color;
            transparent.a = 0f;
            for (int i = 0; i < points.Count; i++)
            {
                Vector2 tangent = (points[(i + 1) % points.Count] - points[(i + points.Count - 1) % points.Count]).normalized;
                Vector2 outward = new Vector2(tangent.y, -tangent.x);
                mesh.AddVert(points[i], color, Vector2.zero);
                mesh.AddVert(points[i] + outward, transparent, Vector2.zero);
            }
            for (int i = 0; i < points.Count; i++)
            {
                int a = start + 1 + i * 2;
                int b = start + 1 + ((i + 1) % points.Count) * 2;
                mesh.AddTriangle(start, a, b);
                mesh.AddTriangle(a, a + 1, b);
                mesh.AddTriangle(a + 1, b + 1, b);
            }
        }

        private void Stroke(VertexHelper mesh, List<Vector2> points, float width, bool closed = false)
        {
            if (closed) points.RemoveAt(points.Count - 1);
            int start = mesh.currentVertCount;
            Color transparent = color;
            transparent.a = 0f;
            float half = Mathf.Max(.25f, width * .5f);
            for (int i = 0; i < points.Count; i++)
            {
                Vector2 before = points[closed ? (i + points.Count - 1) % points.Count : Mathf.Max(0, i - 1)];
                Vector2 after = points[closed ? (i + 1) % points.Count : Mathf.Min(points.Count - 1, i + 1)];
                Vector2 tangent = (after - before).normalized;
                Vector2 normal = new Vector2(-tangent.y, tangent.x);
                mesh.AddVert(points[i] - normal * (half + 1f), transparent, Vector2.zero);
                mesh.AddVert(points[i] - normal * half, color, Vector2.zero);
                mesh.AddVert(points[i] + normal * half, color, Vector2.zero);
                mesh.AddVert(points[i] + normal * (half + 1f), transparent, Vector2.zero);
            }
            int edgeCount = closed ? points.Count : points.Count - 1;
            for (int i = 0; i < edgeCount; i++)
            {
                int a = start + i * 4;
                int b = start + ((i + 1) % points.Count) * 4;
                for (int lane = 0; lane < 3; lane++)
                {
                    mesh.AddTriangle(a + lane, b + lane, a + lane + 1);
                    mesh.AddTriangle(a + lane + 1, b + lane, b + lane + 1);
                }
            }
            if (!closed)
            {
                Disc(mesh, points[0], half);
                Disc(mesh, points[points.Count - 1], half);
            }
        }
    }
}
