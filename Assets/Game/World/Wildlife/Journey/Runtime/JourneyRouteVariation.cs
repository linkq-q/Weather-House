using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using WeatherHouse.Hillside;

namespace WeatherHouse.WildlifeJourney
{
    /// <summary>Continuous individual lanes on open ground; surface crossings retain their verified geometry.</summary>
    internal static class JourneyRouteVariation
    {
        public static void Apply(JourneyNavigation navigation, JourneyProfile profile, JourneyRoute route, float lateral,
            float spentSwimming = 0, int noiseSeed = 0)
        {
            IEnumerator steps = ApplySteps(navigation, profile, route, lateral, spentSwimming, noiseSeed);
            while (steps.MoveNext()) { }
        }

        internal static IEnumerator ApplySteps(JourneyNavigation navigation, JourneyProfile profile, JourneyRoute route, float lateral,
            float spentSwimming = 0, int noiseSeed = 0, bool intent = false)
        {
            if (route == null || route.segments.Count < 2 || Mathf.Abs(lateral) < .02f) yield break;
            var original = route.segments;
            var varied = new List<JourneySegment>(original.Count);
            Quaternion heading = profile.bodyHeading;
            for (int first = 0; first < original.Count;)
            {
                // Intent search has already proved its initial attachment, including local detour corners.
                // Preserve those exact samples; new noise or a compressed chord can hit the avoided prop.
                if (intent && first == 0)
                {
                    while (first < original.Count && original[first].linkIndex < 0)
                    {
                        JourneySegment connector = original[first++]; varied.Add(connector);
                        heading = WildlifeBodyEnvelope.Heading(connector.to - connector.from, heading);
                        yield return null;
                    }
                    if (first == original.Count) break;
                }
                if (!OpenGround(original[first]))
                {
                    JourneySegment segment = original[first++]; varied.Add(segment);
                    heading = WildlifeBodyEnvelope.Heading(segment.to - segment.from, heading);
                    continue;
                }
                int end = first + 1;
                while (end < original.Count && OpenGround(original[end])) end++;
                List<JourneySegment> run = null;
                IEnumerator operation = VaryGround(navigation, profile, original, first, end, lateral, noiseSeed, heading, value => run = value, intent);
                while (operation.MoveNext()) yield return null;
                if (run != null) varied.AddRange(run);
                else for (int i = first; i < end; i++) varied.Add(original[i]);
                JourneySegment last = varied[varied.Count - 1];
                heading = WildlifeBodyEnvelope.Heading(last.to - last.from, heading);
                first = end;
            }
            float cost = 0;
            foreach (JourneySegment segment in varied)
            {
                yield return null;
                float speed = segment.mode == JourneyTravelMode.Swim ? profile.swimSpeed : profile.walkSpeed;
                float penalty = 1 + Mathf.Max(0, profile.waterAversion) * (segment.mode == JourneyTravelMode.Swim ? 1 : segment.mode == JourneyTravelMode.Wade ? .5f : 0);
                cost += Vector3.Distance(segment.from, segment.to) / Mathf.Max(.01f, speed) * penalty;
            }
            route.segments = varied; route.totalCost = cost;
        }

        private static bool OpenGround(JourneySegment segment)
        { return segment.mode == JourneyTravelMode.Walk && segment.bridgeId == 0 && !segment.surfaceBoundary; }

        private static IEnumerator VaryGround(JourneyNavigation nav, JourneyProfile profile, List<JourneySegment> source,
            int first, int end, float lateral, int seed, Quaternion heading, Action<List<JourneySegment>> complete, bool intent)
        {
            // Terrain triangle cuts are validation samples, not independent steering decisions. Keep authored
            // link corners and choose steering controls by distance; TryStep still checks every terrain cut.
            var controls = new List<JourneySegment>();
            Vector3 previous = source[first].from;
            float accumulated = 0;
            for (int i = first; i < end; i++)
            {
                yield return null;
                accumulated += FlatDistance(source[i].from, source[i].to);
                if (accumulated < .7f && i + 1 < end && source[i].linkIndex == source[i + 1].linkIndex) continue;
                JourneySegment segment = source[i]; segment.from = previous;
                controls.Add(segment); previous = segment.to; accumulated = 0;
            }
            int count = controls.Count + 1;
            var original = new Vector3[count];
            var distance = new float[count];
            original[0] = source[first].from;
            for (int i = 1; i < count; i++)
            {
                yield return null;
                original[i] = controls[i - 1].to;
                distance[i] = distance[i - 1] + FlatDistance(original[i - 1], original[i]);
            }
            float length = distance[count - 1];
            if (length < .1f) yield break;
            float startPin = intent && first == 0 ? 2f : first > 0 && source[first - 1].bridgeId != 0 ? 1.2f : 0;
            float endPin = end < source.Count && source[end].bridgeId != 0 ? 1.2f : 0;
            float ramp = Mathf.Min(3f, length * .25f);
            float noiseX = (uint)seed % 8191 * .17f, noiseY = ((uint)seed >> 13) * .013f;
            var result = new List<JourneySegment>(controls.Count);
            float nearDistance = Mathf.Max(1.5f, profile.bodyEnvelope.half.z * 2f + 1f);
            previous = original[0];
            for (int i = 1; i < count; i++)
            {
                yield return null;
                Vector3 offset = Vector3.zero;
                if (i < count - 1)
                {
                    Vector3 tangent = original[i + 1] - original[i - 1]; tangent.y = 0;
                    float taper = Mathf.SmoothStep(0, 1, (distance[i] - startPin) / ramp) *
                        Mathf.SmoothStep(0, 1, (length - distance[i] - endPin) / ramp);
                    float noise = .5f + .5f * Mathf.PerlinNoise(noiseX, noiseY + distance[i] * .11f);
                    offset = Vector3.Cross(Vector3.up, tangent.normalized) * (lateral * taper * noise);
                }
                bool accepted = false; float cap = 1;
                for (int attempt = 0; attempt < 5; attempt++)
                {
                    yield return null;
                    Vector3 candidate = original[i] + offset * cap;
                    // Replanning must not recreate a known nearby collision with the same lane noise.
                    // Contract local controls and their exit using the existing full-body walking test.
                    bool distant = intent && FlatDistance(source[0].from, previous) > nearDistance &&
                        FlatDistance(source[0].from, candidate) > nearDistance &&
                        (i == count - 1 || FlatDistance(source[0].from, original[i + 1]) > nearDistance);
                    if (Forward(original[i - 1], original[i], previous, candidate) &&
                        Walk(nav, profile, previous, candidate, heading, out Vector3 projected, distant))
                    {
                        Quaternion arrival = WildlifeBodyEnvelope.Heading(projected - previous, heading);
                        // Keep an escape to the next original control. Rejected width contracts locally,
                        // without discarding safe individual movement on the rest of the journey.
                        if (i == count - 1 || (Forward(original[i], original[i + 1], projected, original[i + 1]) &&
                            Walk(nav, profile, projected, original[i + 1], arrival, out _, distant)))
                        {
                            JourneySegment segment = controls[i - 1]; segment.from = previous; segment.to = projected;
                            result.Add(segment); previous = projected; heading = arrival; accepted = true; break;
                        }
                    }
                    cap = attempt < 3 ? cap * .5f : 0;
                }
                if (!accepted) yield break;
            }
            complete(result);
        }

        private static bool Walk(JourneyNavigation nav, JourneyProfile profile, Vector3 from, Vector3 to,
            Quaternion heading, out Vector3 point, bool intent)
        {
            if (intent) return nav.TryProjectIntent(profile, to, out point, out JourneyTravelMode mode) && mode == JourneyTravelMode.Walk;
            Quaternion saved = profile.bodyHeading;
            try
            {
                profile.bodyHeading = heading;
                if (!nav.TryStep(profile, from, to, out point, out _, JourneyTravelMode.Walk)) return false;
                Quaternion next = WildlifeBodyEnvelope.Heading(point - from, heading);
                // The executed route turns at its control before travelling. Validate that same pose,
                // rather than only a simultaneous turn-and-translation inside the surface sampler.
                return (nav.CanMoveSupportedBody(profile, from, heading, from, next) ||
                    nav.TryPlanBodyStep(profile, from, heading, from, next, out _)) &&
                    (nav.CanMoveSupportedBody(profile, from, next, point, next) ||
                    nav.TryPlanBodyStep(profile, from, next, point, next, out _));
            }
            finally { profile.bodyHeading = saved; }
        }

        private static bool Forward(Vector3 originalFrom, Vector3 originalTo, Vector3 from, Vector3 to)
        {
            Vector3 heading = originalTo - originalFrom, movement = to - from;
            heading.y = movement.y = 0;
            return heading.sqrMagnitude < .000001f || Vector3.Dot(heading, movement) >= heading.sqrMagnitude * .1f;
        }

        private static float FlatDistance(Vector3 a, Vector3 b)
        { a.y = b.y = 0; return Vector3.Distance(a, b); }
    }
}
