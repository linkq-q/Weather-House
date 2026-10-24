using System;
using System.Collections.Generic;
using UnityEngine;

namespace WeatherHouse.Vegetation
{
    public enum GrassPaintMode { Erase, Paint, Adjust }

    [Serializable]
    public sealed class GrassBrushSettings
    {
        public GrassPaintMode mode;
        public int sourceId = -1;
        public float radius = 2, hardness = .5f, strength = 1, density = 0;
        public float heightScale = 1, widthScale = 1;
        public bool affectDensity = true, affectHeight = true, affectWidth = true;

        public GrassBrushSettings Copy()
        {
            var copy = (GrassBrushSettings)MemberwiseClone();
            copy.radius = Mathf.Clamp(Finite(radius, 2), .15f, 40);
            copy.hardness = Mathf.Clamp01(Finite(hardness, .5f)); copy.strength = Mathf.Clamp01(Finite(strength, 1));
            copy.density = Mathf.Clamp(Finite(density, 0), 0, mode == GrassPaintMode.Paint ? 40 : mode == GrassPaintMode.Adjust ? 2 : 1);
            copy.heightScale = Mathf.Clamp(Finite(heightScale, 1), .1f, 4);
            copy.widthScale = Mathf.Clamp(Finite(widthScale, 1), .1f, 4);
            return copy;
        }
        static float Finite(float value, float fallback) => float.IsNaN(value) || float.IsInfinity(value) ? fallback : value;
    }

    [Serializable]
    public sealed class GrassPaintStroke
    {
        public GrassBrushSettings settings = new GrassBrushSettings();
        public List<Vector2> points = new List<Vector2>();
        public Rect bounds;
        public const int MaximumPoints = 2048;

        public bool Append(Vector2 point)
        {
            if (points.Count > 0 && Vector2.Distance(points[points.Count - 1], point) < Mathf.Max(.04f, settings.radius * .12f)) return false;
            if (points.Count > 1)
            {
                Vector2 a = points[points.Count - 2], b = points[points.Count - 1];
                if (Vector2.Dot((b - a).normalized, (point - b).normalized) > .997f &&
                    SegmentDistance(b, a, point) < settings.radius * .025f)
                {
                    points[points.Count - 1] = point; ExpandBounds(point); return true;
                }
            }
            if (points.Count >= MaximumPoints) throw new InvalidOperationException("这一笔已达 2048 个转折点，请松开鼠标后继续下一笔。");
            points.Add(point); ExpandBounds(point); return true;
        }

        void ExpandBounds(Vector2 p)
        {
            float r = settings.radius;
            Rect next = Rect.MinMaxRect(p.x - r, p.y - r, p.x + r, p.y + r);
            bounds = points.Count == 1 ? next : Union(bounds, next);
        }

        public float Weight(Vector2 p)
        {
            if (points.Count == 0 || !bounds.Contains(p)) return 0;
            float distance = Vector2.Distance(p, points[0]);
            for (int i = 1; i < points.Count; i++) distance = Mathf.Min(distance, SegmentDistance(p, points[i - 1], points[i]));
            float t = distance / settings.radius;
            if (t >= 1) return 0;
            if (settings.hardness >= .999f || t <= settings.hardness) return settings.strength;
            return (1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(settings.hardness, 1, t))) * settings.strength;
        }

        public static Rect Union(Rect a, Rect b) => Rect.MinMaxRect(Mathf.Min(a.xMin, b.xMin), Mathf.Min(a.yMin, b.yMin), Mathf.Max(a.xMax, b.xMax), Mathf.Max(a.yMax, b.yMax));
        static float SegmentDistance(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 delta = b - a;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, delta) / Mathf.Max(.000001f, delta.sqrMagnitude));
            return Vector2.Distance(p, a + delta * t);
        }
    }

    public struct GrassLocalSample
    {
        public float density, heightScale, widthScale;
        public static GrassLocalSample Base => new GrassLocalSample { density = 1, heightScale = 1, widthScale = 1 };
        public static GrassLocalSample Overlay => new GrassLocalSample { density = 0, heightScale = 1, widthScale = 1 };

        public void Apply(GrassPaintStroke stroke, Vector2 point, int sourceId, bool isBase)
        {
            var s = stroke.settings;
            if (s.sourceId != -1 && s.sourceId != sourceId) return;
            if (s.mode == GrassPaintMode.Paint && isBase) return;
            float weight = stroke.Weight(point); if (weight <= 0) return;
            if (s.mode == GrassPaintMode.Erase) density *= Mathf.Lerp(1, s.density, weight);
            else if (s.mode == GrassPaintMode.Paint)
            {
                density = Mathf.Lerp(density, s.density, weight);
                heightScale = Mathf.Lerp(heightScale, s.heightScale, weight);
                widthScale = Mathf.Lerp(widthScale, s.widthScale, weight);
            }
            else
            {
                if (s.affectDensity) density *= Mathf.Lerp(1, s.density, weight);
                if (s.affectHeight) heightScale = Mathf.Lerp(heightScale, s.heightScale, weight);
                if (s.affectWidth) widthScale = Mathf.Lerp(widthScale, s.widthScale, weight);
            }
            density = Mathf.Clamp(density, 0, isBase ? 2 : 40);
        }
    }

    [Serializable]
    public sealed class GrassPaintSource
    {
        public GrassSpecies definition;
        public Material material;
        public Mesh[] variants;
    }

    public struct GrassPaintSourceInfo
    {
        public int id;
        public string label;
        public GrassSpecies definition;
        public Material material;
        public bool isBase;
    }
}
