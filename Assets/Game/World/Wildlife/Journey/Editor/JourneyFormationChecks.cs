using System;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace WeatherHouse.WildlifeJourney.Editor
{
    public static class JourneyFormationChecks
    {
        [MenuItem("Weather House/Wildlife Journey/Check Open Ground Formation")]
        public static void RunMenu() { Debug.Log(RunLogicChecks()); }

        public static string RunLogicChecks()
        {
            var root = new GameObject("Isolated formation checks");
            try
            {
                JourneyNavigation nav = root.AddComponent<JourneyNavigation>();
                nav.surfaceSampler = p => new JourneySurface { valid = true };
                var profile = new JourneyProfile { canSwim = true, maxSwimDistance = .05f };
                JourneyRoute a = ShortLinks(nav, profile), repeat = ShortLinks(nav, profile), other = ShortLinks(nav, profile);
                Apply(nav, profile, a, -2.4f, 17); Apply(nav, profile, repeat, -2.4f, 17); Apply(nav, profile, other, -2.4f, 928);
                Require(a.segments.Where(s => s.to.x >= 5 && s.to.x <= 27 && Mathf.Abs(s.to.x - Mathf.Round(s.to.x)) < .001f)
                    .All(s => s.to.z > 1f), "Short authored links repeatedly squeezed open-ground travel back to their shared center line.");
                Require(a.segments.All(s => s.to.z >= -.0001f && s.to.z <= 2.4001f), "Variation exceeded its signed lateral width in metres.");
                Require(a.segments.Select((s, i) => (s.to - repeat.segments[i].to).sqrMagnitude < .0000001f).All(equal => equal) &&
                    Mathf.Abs(a.totalCost - repeat.totalCost) < .000001f, "The same seed and route produced different travel geometry.");
                Require(a.segments.Select((s, i) => Vector3.Distance(s.to, other.segments[i].to)).Max() > .08f,
                    "Different member seeds did not produce visibly different paths.");
                float[] interior = a.segments.Where(s => s.to.x >= 5 && s.to.x <= 27).Select(s => s.to.z).ToArray();
                Require(interior.Max() - interior.Min() > .08f && a.segments.Where(s => s.from.x >= 5 && s.to.x <= 27)
                    .All(s => Mathf.Abs(s.to.z - s.from.z) <= (s.to.x - s.from.x) * .8f + .001f),
                    "Individual lanes are rigid parallel lines or change sideways too abruptly.");
                Validate(nav, profile, a);
                CheckCorners(nav, profile);

                nav.surfaceSampler = p => new JourneySurface { valid = true, blocked = p.x > 14 && p.x < 16 && p.z > .5f };
                JourneyRoute obstacle = ShortLinks(nav, profile);
                Apply(nav, profile, obstacle, -2.4f, 17);
                Validate(nav, profile, obstacle);
                Require(obstacle.segments.Where(s => s.to.x >= 14.25f && s.to.x <= 15.75f).All(s => s.to.z <= .5001f),
                    "Individual variation entered the locally blocked corridor.");
                Require(obstacle.segments.Where(s => s.to.x >= 5 && s.to.x <= 9).Max(s => s.to.z) > 1f &&
                    obstacle.segments.Where(s => s.to.x >= 23 && s.to.x <= 27).Max(s => s.to.z) > 1f,
                    "One blocked stretch erased the safe lateral spread along the rest of the journey.");

                nav.surfaceSampler = p => {
                    bool wet = p.x > 14 && p.x < 16 && p.z > .5f;
                    return new JourneySurface { valid = true, hasWater = wet, waterY = 0, groundY = wet ? -1 : 0 };
                };
                JourneyRoute water = ShortLinks(nav, profile);
                Apply(nav, profile, water, -2.4f, 17); Validate(nav, profile, water);
                Require(water.segments.Where(s => s.to.x >= 23 && s.to.x <= 27).Max(s => s.to.z) > 1f,
                    "Preserving a dry route suppressed safe spreading beyond the water strip.");
                JourneyRoute zero = ShortLinks(nav, profile); Apply(nav, profile, zero, 0, 17);
                Require(zero.segments.All(s => Mathf.Abs(s.to.z) < .000001f), "Zero travel spread still introduced noise.");
                return "PASS continuous spread across 32 short authored links with stable endpoints\n" +
                    "PASS repeatable, distinct, slowly changing individual noise within the configured width\n" +
                    "PASS wide individual lanes narrow at corners without folding behind earlier waypoints\n" +
                    "PASS local obstacle narrowing retains safe spread before and after the obstruction\n" +
                    "PASS water cannot be introduced by a land offset; zero width preserves the original route";
            }
            finally { Object.DestroyImmediate(root); }
        }

        private static JourneyRoute ShortLinks(JourneyNavigation nav, JourneyProfile profile)
        {
            var route = new JourneyRoute();
            for (int i = 0; i < 32; i++)
            {
                JourneyRoute edge = nav.TraceDirectRoute(new Vector3(i, 0, 0), new Vector3(i + 1, 0, 0), profile);
                Require(edge != null, "The authored center route is invalid in the formation fixture.");
                foreach (JourneySegment original in edge.segments)
                { JourneySegment segment = original; segment.linkIndex = i; route.segments.Add(segment); }
                route.totalCost += edge.totalCost;
            }
            return route;
        }

        private static void CheckCorners(JourneyNavigation nav, JourneyProfile profile)
        {
            foreach (float lateral in new[] { -2.8f, 2.8f })
            {
                var route = new JourneyRoute();
                Vector3[] corners = { Vector3.zero, new Vector3(8, 0, 0), new Vector3(8, 0, 8), new Vector3(16, 0, 8) };
                for (int leg = 0; leg < corners.Length - 1; leg++)
                {
                    JourneyRoute part = nav.TraceDirectRoute(corners[leg], corners[leg + 1], profile);
                    foreach (JourneySegment original in part.segments)
                    { JourneySegment segment = original; segment.linkIndex = leg; route.segments.Add(segment); }
                }
                JourneySegment[] baseline = route.segments.ToArray();
                Apply(nav, profile, route, lateral, 17);
                Require(route.segments.Select((s, i) => {
                    Vector3 original = baseline[i].to - baseline[i].from, varied = s.to - s.from;
                    original.y = varied.y = 0;
                    return Vector3.Dot(original, varied) > 0 && nav.TryStep(profile, s.from, s.to, out _, out _, JourneyTravelMode.Walk);
                }).All(forward => forward), "A wide corner offset folded backwards within an authored link and can trap waypoint lookahead.");
                Require(route.segments.Where(s => s.to.x > 3 && s.to.x < 5).Any(s => Mathf.Abs(s.to.z) > 1f),
                    "Preventing a folded corner erased individual width on its open approach.");
            }
        }

        private static void Apply(JourneyNavigation nav, JourneyProfile profile, JourneyRoute route, float lateral, int seed)
        {
            Type type = typeof(JourneyNavigation).Assembly.GetType("WeatherHouse.WildlifeJourney.JourneyRouteVariation", true);
            MethodInfo apply = type.GetMethod("Apply", BindingFlags.Static | BindingFlags.Public);
            Require(apply != null, "Individual route variation cannot be inspected.");
            // The previous five-argument entrypoint remains inspectable to prove this regression fails before the fix.
            apply.Invoke(null, apply.GetParameters().Length == 6 ? new object[] { nav, profile, route, lateral, 0f, seed } :
                new object[] { nav, profile, route, lateral, 0f });
        }

        private static void Validate(JourneyNavigation nav, JourneyProfile profile, JourneyRoute route)
        {
            Require((route.segments[0].from - Vector3.zero).sqrMagnitude < .000001f &&
                (route.segments.Last().to - new Vector3(32, 0, 0)).sqrMagnitude < .000001f,
                "Formation variation moved the journey endpoints.");
            Require(route.segments.All(s => s.mode == JourneyTravelMode.Walk && !s.surfaceBoundary && s.bridgeId == 0 &&
                nav.TryStep(profile, s.from, s.to, out _, out _, JourneyTravelMode.Walk)),
                "A varied land segment is not continuously traversable on its original surface mode.");
            Require(route.segments.Select((s, i) => i == 0 || (s.from - route.segments[i - 1].to).sqrMagnitude < .000001f).All(joined => joined),
                "Variation disconnected two consecutive route segments.");
        }

        private static void Require(bool condition, string message)
        { if (!condition) throw new InvalidOperationException(message); }
    }
}
