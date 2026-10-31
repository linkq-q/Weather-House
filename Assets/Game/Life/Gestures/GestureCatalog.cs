using System;
using System.Collections.Generic;
using UnityEngine;

namespace WeatherHouse.Life
{
    public sealed class GestureDefinition
    {
        public readonly string id, title, instruction;
        public readonly Vector2[] points;
        public GestureDefinition(string id, string title, string instruction, Vector2[] points)
        { this.id = id; this.title = title; this.instruction = instruction; this.points = points; }
    }

    // One continuous stroke, fixed start/direction, free translation and uniform scale.
    // Timing does not enter the matcher; rotation and reflection deliberately remain meaningful.
    public static class GestureCatalog
    {
        private const int Samples = 64;
        public const float MinimumScore = .79f;
        public const float MinimumMargin = .055f;
        public static readonly GestureDefinition[] All =
        {
            new GestureDefinition("rain", "降雨", "从顶端向下画直线，折向左上，沿原线向下，最后向右上收笔。按住左键一笔画完。",
                new[] { new Vector2(0, 1), new Vector2(0, -1), new Vector2(-.6f, -.35f), new Vector2(0, -1), new Vector2(.6f, -.35f) }),
            new GestureDefinition("clear", "收起天气", "从左上开始，向右画横线，斜向左下，再向右画横线。",
                new[] { new Vector2(-1, .7f), new Vector2(1, .7f), new Vector2(-1, -.7f), new Vector2(1, -.7f) }),
            new GestureDefinition("sunlight", "日照", "从圆顶起笔，顺时针画完整一圈，接上起点。", Circle()),
            new GestureDefinition("wind", "起风", "从左向右画两道连续的波浪，先上后下，再上再下。", Wave()),
            new GestureDefinition("weak", "减弱", "从左上向中下画，再向右上提起，像一个向下的尖角。",
                new[] { new Vector2(-1, .7f), new Vector2(0, -.7f), new Vector2(1, .7f) }),
            new GestureDefinition("strong", "增强", "从左下向中上画，再向右下落笔，像一个向上的尖角。",
                new[] { new Vector2(-1, -.7f), new Vector2(0, .7f), new Vector2(1, -.7f) })
        };
        private static readonly Vector2[][] Templates = BuildTemplates();
        public static GestureDefinition Find(string id) => Array.Find(All, g => g.id == id);

        public static bool IsUsableStroke(IList<Vector2> path)
        {
            if (path == null || path.Count < 5 || path.Count >= 512 || Length(path) < 75f) return false;
            Vector2 min = path[0], max = path[0];
            foreach (Vector2 p in path)
            {
                if (!Finite(p.x) || !Finite(p.y)) return false;
                min = Vector2.Min(min, p); max = Vector2.Max(max, p);
            }
            return Mathf.Max(max.x - min.x, max.y - min.y) >= 40f;
        }

        public static bool TryRecognize(IList<Vector2> stroke, out string id, out float score)
        {
            id = null; score = 0f;
            if (stroke == null || stroke.Count < 3 || stroke.Count > 2048) return false;
            float length = Length(stroke);
            if (!Finite(length) || length <= .001f) return false;
            Vector2[] candidate = Normalize(stroke);
            if (candidate == null) return false;
            float best = float.PositiveInfinity, runner = float.PositiveInfinity;
            int chosen = -1;
            for (int i = 0; i < Templates.Length; i++)
            {
                float distance = 0f;
                for (int j = 0; j < Samples; j++) distance += Vector2.Distance(candidate[j], Templates[i][j]);
                distance /= Samples;
                if (distance < best) { runner = best; best = distance; chosen = i; }
                else if (distance < runner) runner = distance;
            }
            score = Mathf.Clamp01(1f - best / .65f);
            if (chosen < 0 || score < MinimumScore || (runner - best) / .65f < MinimumMargin) return false;
            // Extra loops and backtracking must not normalize into an otherwise valid shape.
            float ratio = Length(candidate) / Length(Templates[chosen]);
            if (ratio < .72f || ratio > 1.35f) return false;
            id = All[chosen].id;
            return true;
        }

        public static float Length(IList<Vector2> points)
        {
            float length = 0f;
            for (int i = 1; i < points.Count; i++) length += Vector2.Distance(points[i - 1], points[i]);
            return length;
        }

        private static Vector2[] Normalize(IList<Vector2> points)
        {
            Vector2 min = points[0], max = points[0];
            for (int i = 0; i < points.Count; i++)
            {
                if (!Finite(points[i].x) || !Finite(points[i].y)) return null;
                min = Vector2.Min(min, points[i]); max = Vector2.Max(max, points[i]);
            }
            float extent = Mathf.Max(max.x - min.x, max.y - min.y);
            if (extent < .001f) return null;
            Vector2 middle = (min + max) * .5f;
            var result = new Vector2[Samples];
            float step = Length(points) / (Samples - 1), traversed = 0f;
            int segment = 1;
            for (int i = 0; i < Samples; i++)
            {
                float target = step * i;
                while (segment < points.Count - 1 && traversed + Vector2.Distance(points[segment - 1], points[segment]) < target)
                { traversed += Vector2.Distance(points[segment - 1], points[segment]); segment++; }
                float distance = Vector2.Distance(points[segment - 1], points[segment]);
                Vector2 point = Vector2.Lerp(points[segment - 1], points[segment], distance < .000001f ? 0f : (target - traversed) / distance);
                result[i] = (point - middle) / extent;
            }
            return result;
        }

        private static Vector2[][] BuildTemplates()
        {
            var result = new Vector2[All.Length][];
            for (int i = 0; i < All.Length; i++) result[i] = Normalize(All[i].points);
            return result;
        }
        private static Vector2[] Circle()
        {
            var points = new Vector2[49];
            for (int i = 0; i < points.Length; i++)
            { float angle = i * Mathf.PI * 2f / (points.Length - 1); points[i] = new Vector2(Mathf.Sin(angle), Mathf.Cos(angle)); }
            return points;
        }
        private static Vector2[] Wave()
        {
            var points = new Vector2[49];
            for (int i = 0; i < points.Length; i++)
            { float t = i / (float)(points.Length - 1); points[i] = new Vector2(t * 2f - 1f, Mathf.Sin(t * Mathf.PI * 4f) * .38f); }
            return points;
        }
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
