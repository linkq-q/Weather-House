using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using WeatherHouse.Hillside;

namespace WeatherHouse.WildlifeJourney
{
    /// <summary>Deterministic XZ scatter with a smooth, uneven outline and a fixed minimum spacing.</summary>
    public static class JourneyScatter
    {
        // A failed placement is atomic. The caller can choose another site without moving half the herd.
        public static bool TryCreate(Vector3 center, float radius, float minSpacing, int count, int seed,
            out Vector3[] points, Func<Vector3, bool> acceptPoint = null)
        {
            Vector3[] result = null;
            IEnumerator steps = CreateSteps(center, radius, minSpacing, count, seed, value => result = value, acceptPoint);
            while (steps.MoveNext()) { }
            points = result ?? Array.Empty<Vector3>();
            return result != null;
        }

        internal static IEnumerator CreateSteps(Vector3 center, float radius, float minSpacing, int count, int seed,
            Action<Vector3[]> complete, Func<Vector3, bool> acceptPoint = null)
        {
            if (count < 0 || count > 1024 || !Finite(center.x) || !Finite(center.y) || !Finite(center.z) ||
                !Finite(radius) || !Finite(minSpacing) || radius <= 0 || minSpacing <= 0) yield break;
            if (count == 0) { complete(Array.Empty<Vector3>()); yield break; }

            var random = new WildlifeRandom(seed, 0, "journey-scatter");
            float noiseX = random.Range(20, 1000), noiseZ = random.Range(20, 1000);
            var accepted = new List<Vector3>(count);
            float spacingSquared = minSpacing * minSpacing;
            int budget = Mathf.Max(128, count * 64);
            for (int attempt = 0; attempt < budget && accepted.Count < count; attempt++)
            {
                yield return null;
                Vector2 offset = random.InCircle(radius);
                Vector2 direction = offset.sqrMagnitude > 0 ? offset.normalized : Vector2.right;
                // Sampling around a circle makes the noisy edge continuous across the angular seam.
                float edge = radius * (.65f + .35f * Mathf.Clamp01(Mathf.PerlinNoise(
                    noiseX + direction.x * 1.5f, noiseZ + direction.y * 1.5f)));
                if (offset.sqrMagnitude > edge * edge) continue;
                Vector3 point = center + new Vector3(offset.x, 0, offset.y);
                bool clear = true;
                for (int i = 0; i < accepted.Count; i++)
                    if ((point - accepted[i]).sqrMagnitude < spacingSquared) { clear = false; break; }
                if (!clear || (acceptPoint != null && !acceptPoint(point))) continue;
                accepted.Add(point);
            }
            if (accepted.Count == count) complete(accepted.ToArray());
        }

        private static bool Finite(float value) { return !float.IsNaN(value) && !float.IsInfinity(value); }
    }
}
