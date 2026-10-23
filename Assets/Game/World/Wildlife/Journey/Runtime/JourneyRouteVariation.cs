using System.Collections.Generic;
using UnityEngine;

namespace WeatherHouse.WildlifeJourney
{
    /// <summary>Gives an individual a gentle line within a verified corridor; narrow bridges keep their center line.</summary>
    internal static class JourneyRouteVariation
    {
        public static void Apply(JourneyNavigation navigation, JourneyProfile profile, JourneyRoute route, float lateral, float spentSwimming = 0)
        {
            if (route == null || route.segments.Count < 2 || Mathf.Abs(lateral) < .02f) return;
            var varied = new List<JourneySegment>(route.segments);
            for (int first = 0; first < varied.Count;)
            {
                int end = first + 1;
                while (end < varied.Count && varied[end].linkIndex == varied[first].linkIndex) end++;
                Vector3 origin = varied[first].from, finish = varied[end - 1].to;
                Vector3 heading = finish - origin; heading.y = 0;
                float length = heading.magnitude;
                bool narrow = false;
                for (int i = first; i < end; i++) narrow |= varied[i].bridgeId != 0;
                // Merge onto the shared approach before the bottleneck, while there is still room to queue.
                for (int i = end; !narrow && i < varied.Count; i++)
                {
                    Vector3 ahead = varied[i].from - finish; ahead.y = 0;
                    if (ahead.sqrMagnitude > 9f) break;
                    narrow = varied[i].bridgeId != 0;
                }
                if (!narrow && length > 2f)
                {
                    Vector3 side = Vector3.Cross(Vector3.up, heading.normalized);
                    for (int i = first; i < end; i++)
                    {
                        JourneySegment segment = varied[i];
                        segment.from += Offset(segment.from, origin, heading, side, lateral);
                        segment.to += Offset(segment.to, origin, heading, side, lateral);
                        varied[i] = segment;
                    }
                }
                first = end;
            }
            // Reject the entire variation if even one point changes the crossing ability or exceeds the swim budget.
            Vector3 previous = route.segments[0].from;
            float swimming = spentSwimming, cost = 0;
            JourneyTravelMode previousMode = navigation.TryProject(profile, previous, out _, out JourneyTravelMode startMode) ? startMode : JourneyTravelMode.Walk;
            for (int i = 0; i < varied.Count; i++)
            {
                JourneySegment segment = varied[i];
                if (!navigation.TryStep(profile, previous, segment.to, out Vector3 point, out JourneyTravelMode mode)) return;
                JourneySegment original = route.segments[i];
                if ((mode == JourneyTravelMode.Bridge) != (original.bridgeId != 0) && original.mode != JourneyTravelMode.Bridge) return;
                if (mode == JourneyTravelMode.Swim || previousMode == JourneyTravelMode.Swim)
                {
                    swimming += Vector2.Distance(new Vector2(previous.x, previous.z), new Vector2(point.x, point.z));
                    if (swimming > profile.maxSwimDistance) return;
                }
                if (mode != JourneyTravelMode.Swim) swimming = 0;
                segment.from = previous; segment.to = point;
                segment.mode = mode == JourneyTravelMode.Swim || previousMode == JourneyTravelMode.Swim ? JourneyTravelMode.Swim :
                    mode == JourneyTravelMode.Bridge || previousMode == JourneyTravelMode.Bridge ? JourneyTravelMode.Bridge : mode;
                float speed = segment.mode == JourneyTravelMode.Swim ? profile.swimSpeed : profile.walkSpeed;
                float penalty = 1 + Mathf.Max(0, profile.waterAversion) * (segment.mode == JourneyTravelMode.Swim ? 1 : segment.mode == JourneyTravelMode.Wade ? .5f : 0);
                cost += Vector3.Distance(previous, point) / Mathf.Max(.01f, speed) * penalty;
                varied[i] = segment; previous = point; previousMode = mode;
            }
            route.segments = varied; route.totalCost = cost;
        }

        private static Vector3 Offset(Vector3 point, Vector3 origin, Vector3 heading, Vector3 side, float lateral)
        {
            float along = Mathf.Clamp01(Vector3.Dot(point - origin, heading) / heading.sqrMagnitude);
            float taper = Mathf.Sin(along * Mathf.PI);
            return side * (lateral * taper * taper);
        }
    }
}
