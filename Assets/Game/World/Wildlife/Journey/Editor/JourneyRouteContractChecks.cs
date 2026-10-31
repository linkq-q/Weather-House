using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace WeatherHouse.WildlifeJourney.Editor
{
    // These fixtures have straight authored links; terrain sample counts are not a route contract.
    internal static class JourneyRouteContractChecks
    {
        public static void Validate(IList<JourneySegment> before, IList<JourneySegment> after,
            Func<JourneySegment, bool> walkClear)
        {
            Require(before != null && before.Count > 0 && after != null && after.Count > 0, "Variation erased the route.");
            Require(Near(before[0].from, after[0].from) && Near(before[before.Count - 1].to, after[after.Count - 1].to),
                "Variation changed the route start or goal.");
            for (int i = 0; i < after.Count; i++)
            {
                JourneySegment s = after[i];
                Require(Finite(s.from) && Finite(s.to) && (s.to - s.from).sqrMagnitude > .000001f,
                    "Variation contains a nonfinite or empty segment.");
                Require(i == 0 || Near(after[i - 1].to, s.from), "Variation disconnected or reordered the route.");
                if (Open(s)) Require(walkClear(s), "Variation accepted a chord that is not Walk throughout.");
            }
            List<List<JourneySegment>> oldPhases = Phases(before), newPhases = Phases(after);
            Require(oldPhases.Count == newPhases.Count, "Variation removed or added a link/surface phase.");
            for (int i = 0; i < oldPhases.Count; i++)
            {
                List<JourneySegment> a = oldPhases[i], b = newPhases[i];
                Require(SamePhase(a[0], b[0]), "Variation changed link or surface phase order.");
                if (!Open(a[0]))
                {
                    Require(a.Count == b.Count && a.Select((s, j) => Near(s.from, b[j].from) && Near(s.to, b[j].to)).All(v => v),
                        "Variation bent, compressed or removed a bridge/surface-boundary segment.");
                    continue;
                }
                Vector3 axis = a[a.Count - 1].to - a[0].from; axis.y = 0;
                Require(axis.sqrMagnitude > .000001f, "Straight-link fixture has no forward axis.");
                foreach (JourneySegment s in b)
                {
                    Vector3 step = s.to - s.from; step.y = 0;
                    float from = Vector3.Dot(s.from - a[0].from, axis) / axis.sqrMagnitude;
                    float to = Vector3.Dot(s.to - a[0].from, axis) / axis.sqrMagnitude;
                    Require(Vector3.Dot(step, axis) > 0 && from >= -.001f && to <= 1.001f,
                        "Variation backtracked or skipped an authored link boundary.");
                }
            }
        }

        public static void CheckMutations()
        {
            JourneySegment[] original = {
                Segment(0, .5f, 0), Segment(.5f, 1, 0), Segment(1, 1.5f, 0), Segment(1.5f, 2, 0),
                Segment(2, 3, 1, JourneyTravelMode.Bridge, 1), Segment(3, 4, 1, JourneyTravelMode.Bridge, 1),
                Segment(4, 4.25f, 2, JourneyTravelMode.Walk, 0, true), Segment(4.25f, 4.5f, 2), Segment(4.5f, 5, 2)
            };
            JourneySegment[] compressed = {
                Segment(0, 1, 0), Segment(1, 2, 0), original[4], original[5], original[6], Segment(4.25f, 5, 2)
            };
            Validate(original, original, s => true);
            Validate(original, compressed, s => true);
            Require(compressed.Length < original.Length, "The positive fixture did not exercise legal sample compression.");
            Reject(original, compressed, a => { var s = a[0]; s.from.x += .1f; a[0] = s; });
            Reject(original, compressed, a => { var s = a[a.Count - 1]; s.to.x -= .1f; a[a.Count - 1] = s; });
            Reject(original, compressed, a => { var s = a[1]; s.from.x += .1f; a[1] = s; });
            Reject(original, compressed, a => { a.RemoveRange(0, 2); a.InsertRange(0, new[] {
                Segment(0, 1.5f, 0), Segment(1.5f, 1, 0), Segment(1, 2, 0) }); });
            Reject(original, compressed, a => a.RemoveAt(2));
            Reject(original, compressed, a => { var s = a[2]; s.bridgeId = 2; a[2] = s; });
            Reject(original, compressed, a => { var s = a[2]; s.to.z = .2f; a[2] = s; s = a[3]; s.from.z = .2f; a[3] = s; });
            Reject(original, compressed, a => { var s = a[4]; s.surfaceBoundary = false; a[4] = s; });
            Reject(original, compressed, a => { var s = a[0]; s.mode = JourneyTravelMode.Swim; a[0] = s; });
            Reject(original, compressed, a => { var s = a[1]; s.linkIndex = 7; a[1] = s; });
            Reject(original, compressed, a => { var s = a[0]; s.to.x = float.NaN; a[0] = s; });
            bool rejected = false;
            try { Validate(original, compressed, s => s.from.x >= 1); }
            catch (InvalidOperationException) { rejected = true; }
            Require(rejected, "Contract mutation survived: a rejected full-chord Walk query was ignored.");
        }

        private static void Reject(JourneySegment[] original, JourneySegment[] valid, Action<List<JourneySegment>> mutate)
        {
            var changed = valid.ToList(); mutate(changed); bool rejected = false;
            try { Validate(original, changed, s => true); }
            catch (InvalidOperationException) { rejected = true; }
            Require(rejected, "A deliberately damaged route survived the contract check.");
        }
        private static JourneySegment Segment(float from, float to, int link, JourneyTravelMode mode = JourneyTravelMode.Walk,
            int bridge = 0, bool boundary = false)
        { return new JourneySegment { from = new Vector3(from, 0, 0), to = new Vector3(to, 0, 0), linkIndex = link,
            mode = mode, bridgeId = bridge, surfaceBoundary = boundary }; }
        private static List<List<JourneySegment>> Phases(IList<JourneySegment> segments)
        {
            var phases = new List<List<JourneySegment>>();
            foreach (JourneySegment s in segments)
            {
                if (phases.Count == 0 || !SamePhase(phases[phases.Count - 1][0], s)) phases.Add(new List<JourneySegment>());
                phases[phases.Count - 1].Add(s);
            }
            return phases;
        }
        private static bool SamePhase(JourneySegment a, JourneySegment b)
        { return a.linkIndex == b.linkIndex && a.mode == b.mode && a.bridgeId == b.bridgeId && a.surfaceBoundary == b.surfaceBoundary; }
        private static bool Open(JourneySegment s) { return s.mode == JourneyTravelMode.Walk && s.bridgeId == 0 && !s.surfaceBoundary; }
        private static bool Near(Vector3 a, Vector3 b) { return (a - b).sqrMagnitude < .000001f; }
        private static bool Finite(Vector3 p)
        { return !float.IsNaN(p.x) && !float.IsNaN(p.y) && !float.IsNaN(p.z) && !float.IsInfinity(p.x) && !float.IsInfinity(p.y) && !float.IsInfinity(p.z); }
        private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    }
}
