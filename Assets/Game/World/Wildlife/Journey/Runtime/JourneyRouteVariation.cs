using System.Collections.Generic;
using UnityEngine;

namespace WeatherHouse.WildlifeJourney
{
    /// <summary>Continuous individual lanes on open ground; surface crossings retain their verified geometry.</summary>
    internal static class JourneyRouteVariation
    {
        public static void Apply(JourneyNavigation navigation, JourneyProfile profile, JourneyRoute route, float lateral,
            float spentSwimming = 0, int noiseSeed = 0)
        {
            if (route == null || route.segments.Count < 2 || Mathf.Abs(lateral) < .02f) return;
            var varied = new List<JourneySegment>(route.segments);
            for (int first = 0; first < varied.Count;)
            {
                if (!OpenGround(varied[first])) { first++; continue; }
                int end = first + 1;
                while (end < varied.Count && OpenGround(varied[end])) end++;
                VaryGround(navigation, profile, varied, first, end, lateral, noiseSeed);
                first = end;
            }
            float cost = 0;
            foreach (JourneySegment segment in varied)
            {
                float speed = segment.mode == JourneyTravelMode.Swim ? profile.swimSpeed : profile.walkSpeed;
                float penalty = 1 + Mathf.Max(0, profile.waterAversion) * (segment.mode == JourneyTravelMode.Swim ? 1 : segment.mode == JourneyTravelMode.Wade ? .5f : 0);
                cost += Vector3.Distance(segment.from, segment.to) / Mathf.Max(.01f, speed) * penalty;
            }
            route.segments = varied; route.totalCost = cost;
        }

        private static bool OpenGround(JourneySegment segment)
        {
            return segment.mode == JourneyTravelMode.Walk && segment.bridgeId == 0 && !segment.surfaceBoundary;
        }

        private static void VaryGround(JourneyNavigation nav, JourneyProfile profile, List<JourneySegment> segments,
            int first, int end, float lateral, int seed)
        {
            int count = end - first + 1;
            var original = new Vector3[count];
            var offsets = new Vector3[count];
            var points = new Vector3[count];
            var distance = new float[count];
            var scale = new float[count];
            original[0] = segments[first].from;
            for (int i = 1; i < count; i++)
            {
                original[i] = segments[first + i - 1].to;
                distance[i] = distance[i - 1] + FlatDistance(original[i - 1], original[i]);
            }
            float length = distance[count - 1];
            if (length < .1f) return;
            float startPin = first > 0 && segments[first - 1].bridgeId != 0 ? 1.2f : 0;
            float endPin = end < segments.Count && segments[end].bridgeId != 0 ? 1.2f : 0;
            float ramp = Mathf.Min(3f, length * .25f);
            float noiseX = (uint)seed % 8191 * .17f, noiseY = ((uint)seed >> 13) * .013f;
            for (int i = 1; i < count - 1; i++)
            {
                Vector3 heading = original[i + 1] - original[i - 1]; heading.y = 0;
                float taper = Mathf.SmoothStep(0, 1, (distance[i] - startPin) / ramp) *
                    Mathf.SmoothStep(0, 1, (length - distance[i] - endPin) / ramp);
                float noise = .5f + .5f * Mathf.PerlinNoise(noiseX, noiseY + distance[i] * .11f);
                offsets[i] = Vector3.Cross(Vector3.up, heading.normalized) * (lateral * taper * noise);
                scale[i] = 1;
                // Verify the lateral corridor too, so an offset cannot jump across a thin water strip or obstacle.
                while (scale[i] > .1f && !Walk(nav, profile, original[i], original[i] + offsets[i] * scale[i], out _)) scale[i] *= .5f;
                if (scale[i] <= .1f) scale[i] = 0;
            }
            // Narrow only around rejected points, spreading the contraction over nearby route distance.
            for (int attempt = 0; attempt < 3; attempt++)
            {
                SmoothCaps(scale, distance);
                points[0] = original[0]; points[count - 1] = original[count - 1];
                for (int i = 1; i < count - 1; i++)
                    if (!nav.TryProject(profile, original[i] + offsets[i] * scale[i], out points[i], out JourneyTravelMode mode) || mode != JourneyTravelMode.Walk)
                    { scale[i] = 0; points[i] = original[i]; }
                bool valid = true;
                for (int i = 1; i < count; i++)
                    if (!Forward(original[i - 1], original[i], points[i - 1], points[i]) ||
                        !Walk(nav, profile, points[i - 1], points[i], out _))
                    { scale[i - 1] = scale[i] = 0; valid = false; }
                if (valid) { Commit(segments, first, points); return; }
            }
            // Each accepted fallback keeps a verified escape to the next original point; failure stays local.
            points[0] = original[0];
            for (int i = 1; i < count; i++)
            {
                float cap = scale[i]; bool accepted = false;
                for (int attempt = 0; attempt < 5; attempt++)
                {
                    Vector3 candidate = original[i] + offsets[i] * cap;
                    if (Forward(original[i - 1], original[i], points[i - 1], candidate) &&
                        Walk(nav, profile, points[i - 1], candidate, out Vector3 projected) &&
                        (i == count - 1 || (Forward(original[i], original[i + 1], projected, original[i + 1]) &&
                            Walk(nav, profile, projected, original[i + 1], out _))))
                    { points[i] = projected; accepted = true; break; }
                    cap = attempt < 3 ? cap * .5f : 0;
                }
                if (!accepted) return;
            }
            Commit(segments, first, points);
        }

        private static void SmoothCaps(float[] scale, float[] distance)
        {
            // Endpoints already taper geometrically; they must not suppress a short walkable run a second time.
            for (int i = 2; i < scale.Length - 1; i++)
                scale[i] = Mathf.Min(scale[i], scale[i - 1] + (distance[i] - distance[i - 1]) * .5f);
            for (int i = scale.Length - 3; i > 0; i--)
                scale[i] = Mathf.Min(scale[i], scale[i + 1] + (distance[i + 1] - distance[i]) * .5f);
        }

        private static bool Walk(JourneyNavigation nav, JourneyProfile profile, Vector3 from, Vector3 to, out Vector3 point)
        {
            return nav.TryStep(profile, from, to, out point, out _, JourneyTravelMode.Walk);
        }

        private static bool Forward(Vector3 originalFrom, Vector3 originalTo, Vector3 from, Vector3 to)
        {
            Vector3 heading = originalTo - originalFrom, movement = to - from;
            heading.y = movement.y = 0;
            // Inside a sharp bend a wide offset can fold back behind an earlier waypoint.
            return heading.sqrMagnitude < .000001f || Vector3.Dot(heading, movement) >= heading.sqrMagnitude * .1f;
        }

        private static void Commit(List<JourneySegment> segments, int first, Vector3[] points)
        {
            for (int i = 1; i < points.Length; i++)
            {
                JourneySegment segment = segments[first + i - 1];
                segment.from = points[i - 1]; segment.to = points[i];
                segments[first + i - 1] = segment;
            }
        }

        private static float FlatDistance(Vector3 a, Vector3 b)
        {
            a.y = b.y = 0; return Vector3.Distance(a, b);
        }
    }
}
