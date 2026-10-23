using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace WeatherHouse.WildlifeJourney.Editor
{
    public static class JourneyScatterChecks
    {
        [MenuItem("Weather House/Wildlife Journey/Check Scatter")]
        public static void RunMenu() { Debug.Log(RunLogicChecks()); }

        public static string RunLogicChecks()
        {
            var results = new List<string>();
            Vector3 center = new Vector3(-11, 7, 0);
            var globalState = UnityEngine.Random.state;
            try
            {
                float expected = UnityEngine.Random.value;
                UnityEngine.Random.state = globalState;
                Require(JourneyScatter.TryCreate(center, 3.8f, 1.45f, 10, 20261023, out Vector3[] first) &&
                    JourneyScatter.TryCreate(center, 3.8f, 1.45f, 10, 20261023, out Vector3[] repeat) &&
                    first.SequenceEqual(repeat) && UnityEngine.Random.value == expected,
                    "The same scatter seed changed the points or consumed Unity's global random stream.");
                Require(JourneyScatter.TryCreate(center, 3.8f, 1.45f, 10, 20261024, out Vector3[] different) &&
                    !first.SequenceEqual(different), "Different seeds did not change the herd's layout.");
                results.Add("PASS deterministic scatter, varying seeds and independent global random stream");

                foreach (int count in new[] { 1, 4, 10, 24 })
                {
                    float radius = count > 10 ? 8 : 3.8f;
                    Require(JourneyScatter.TryCreate(center, radius, 1.45f, count, 20261023, out Vector3[] points),
                        "A sufficiently spacious herd layout could not be generated for " + count + " animals.");
                    Require(points.Length == count && points.All(p => p.y == center.y && (p - center).magnitude <= radius),
                        "Scatter changed the supplied height or left its outer radius.");
                    for (int a = 0; a < points.Length; a++)
                        for (int b = a + 1; b < points.Length; b++)
                            Require(Vector3.Distance(points[a], points[b]) >= 1.45f, "Scatter violated its minimum spacing.");
                }
                results.Add("PASS requested counts, plane, outer boundary and minimum pair spacing");

                Require(JourneyScatter.TryCreate(center, 4, .1f, 512, 20261023, out Vector3[] dense),
                    "A dense outline sample failed unexpectedly.");
                var reach = new float[16];
                foreach (Vector3 p in dense)
                {
                    Vector3 delta = p - center;
                    int sector = Mathf.Clamp((int)((Mathf.Atan2(delta.z, delta.x) + Mathf.PI) / (2 * Mathf.PI) * reach.Length), 0, reach.Length - 1);
                    reach[sector] = Mathf.Max(reach[sector], delta.magnitude);
                }
                Require(reach.Min() > 2 && reach.Max() - reach.Min() > .35f,
                    "Dense points did not reveal a filled group with a visibly uneven outer edge.");
                results.Add("PASS filled, uneven herd outline");

                Require(JourneyScatter.TryCreate(center, 5, .7f, 12, 51, out Vector3[] filtered, p => p.x > center.x + .5f) &&
                    filtered.All(p => p.x > center.x + .5f), "A supplied point filter was ignored.");
                int rejectedCalls = 0;
                Require(!JourneyScatter.TryCreate(center, 4, 1, 10, 1, out Vector3[] denied, p => { rejectedCalls++; return false; }) &&
                    denied.Length == 0 && rejectedCalls > 0 && rejectedCalls <= 640,
                    "A rejecting site filter did not fail atomically within its finite attempt budget.");
                Require(!JourneyScatter.TryCreate(center, .1f, 1.45f, 10, 1, out Vector3[] cramped) && cramped.Length == 0,
                    "An overcrowded site silently reduced spacing or exposed a partial herd.");
                results.Add("PASS optional site filter and bounded, atomic failure in blocked or tiny regions");

                Require(JourneyScatter.TryCreate(center, 1, 1, 0, 1, out Vector3[] empty) && empty.Length == 0 &&
                    !JourneyScatter.TryCreate(center, 1, 1, int.MaxValue, 1, out _) &&
                    !JourneyScatter.TryCreate(center, float.NaN, 1, 1, 1, out _) &&
                    !JourneyScatter.TryCreate(center, 1, 0, 1, 1, out _),
                    "Scatter did not handle empty or invalid requests safely.");
                results.Add("PASS empty requests and invalid-input rejection");
                return string.Join("\n", results);
            }
            finally { UnityEngine.Random.state = globalState; }
        }

        private static void Require(bool condition, string message)
        { if (!condition) throw new InvalidOperationException(message); }
    }
}
