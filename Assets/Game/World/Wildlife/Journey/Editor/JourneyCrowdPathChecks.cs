using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace WeatherHouse.WildlifeJourney.Editor
{
    public static class JourneyCrowdPathChecks
    {
        [MenuItem("Weather House/Wildlife Journey/Check Crowd Paths")]
        public static void RunMenu() { Debug.Log(RunLogicChecks()); }

        public static string RunLogicChecks()
        {
            var results = new List<string>();
            Vector3 start = Point(8.160f, 1.742f), goal = Point(13.780f, -.861f);
            var neighbours = new[] { Circle(9.039f, 2.588f, .55f), Circle(9.075f, .878f, .55f) };
            Require(JourneyCrowdPath.TryFindPath(start, goal, .55f, neighbours, (a, b) => true, out List<Vector3> escape),
                "The fixed two-body arrival gap has no local path around its outside.");
            Require(escape.Count > 2 && Vector3.Distance(escape[0], start) < .0001f &&
                Vector3.Distance(escape[escape.Count - 1], goal) < .0001f && Length(escape) < 14f,
                "The crowd path lost its endpoints, cut directly through the gap, or took an excessive detour.");
            CheckClearance(escape, .55f, neighbours);
            Require(JourneyCrowdPath.TryFindPath(start, goal, .55f, neighbours, (a, b) => true, out List<Vector3> repeated) &&
                repeated.Count == escape.Count && Math.Abs(Length(repeated) - Length(escape)) < .00001f,
                "An unchanged local crowd produced a different path.");
            results.Add("PASS adjacent body circles have a deterministic short exterior path without penetration");

            Require(!JourneyCrowdPath.TryFindPath(start, neighbours[0].center, .55f, neighbours, (a, b) => true, out _),
                "A destination occupied by another body was accepted.");
            int terrainChecks = 0;
            Require(!JourneyCrowdPath.TryFindPath(start, goal, .55f, neighbours, (a, b) => {
                terrainChecks++;
                return !(Math.Min(a.x, b.x) < 10f && Math.Max(a.x, b.x) >= 10f);
            }, out _) && terrainChecks > 0,
                "The local visibility graph crossed a terrain barrier or skipped terrain validation.");
            results.Add("PASS occupied goals and terrain-blocked paths are rejected");

            var single = new[] { Circle(0, 0, .6f) };
            Require(JourneyCrowdPath.TryFindPath(Point(-3, 0), Point(3, 0), .4f, single, (a, b) => true, out List<Vector3> around),
                "A single round obstacle could not be circumnavigated.");
            CheckClearance(around, .4f, single);
            Require(JourneyCrowdPath.TryFindPath(Point(-1.115f, 0), Point(-3, 0), .4f, single, (a, b) => true, out List<Vector3> contact) &&
                contact.Count == 2, "A slightly touching start could not move monotonically out of contact.");
            CheckClearance(contact, .4f, single);
            var socialPartner = new[] { Circle(0, 0, .6f) };
            Require(JourneyCrowdPath.TryFindPath(Point(-1.24f, 0), Point(3, 0), .6f, socialPartner,
                (a, b) => true, out List<Vector3> socialExit),
                "The legal social gap of .04m could not open back to the normal .12m walking gap.");
            CheckClearance(socialExit, .6f, socialPartner);
            var sharedContact = new[] { Circle(-.5f, 0, .6f), Circle(.5f, 0, .6f) };
            Require(JourneyCrowdPath.TryFindPath(Point(0, 1.15f), Point(0, 3), .6f, sharedContact,
                (a, b) => true, out List<Vector3> sharedExit), "Existing contacts could not separate together.");
            CheckClearance(sharedExit, .6f, sharedContact);
            var enclosed = new[] { Circle(1.24f, 0, .6f), Circle(-1.24f, 0, .6f),
                Circle(0, 1.24f, .6f), Circle(0, -1.24f, .6f) };
            Require(!JourneyCrowdPath.TryFindPath(Point(0, 0), Point(3, 3), .6f, enclosed, (a, b) => true, out _),
                "A start touching four sides escaped by first deepening another existing contact.");
            results.Add("PASS exact clearance and outward-only escape from every existing social contact");

            int directChecks = 0;
            Require(JourneyCrowdPath.TryFindPath(Point(0, 0), Point(3, 0), .4f, Array.Empty<JourneyCrowdObstacle>(),
                (a, b) => { directChecks++; return true; }, out List<Vector3> direct) && direct.Count == 2 && directChecks == 1,
                "An unobstructed path unnecessarily built a crowd graph or skipped its terrain check.");
            results.Add("PASS unobstructed paths retain the direct checked segment");

            var capped = new List<JourneyCrowdObstacle>();
            for (int i = 0; i < 10; i++) capped.Add(Circle(-2 - i * .05f, i * .04f, .05f));
            capped.Add(Circle(4, 0, .6f));
            if (JourneyCrowdPath.TryFindPath(Point(0, 0), Point(6, 0), .1f, capped, (a, b) => true, out List<Vector3> bounded))
                CheckClearance(bounded, .1f, capped);
            results.Add("PASS the ten-obstacle node cap never permits crossing another supplied body");
            return string.Join("\n", results);
        }

        private static void CheckClearance(List<Vector3> path, float radius, IReadOnlyList<JourneyCrowdObstacle> obstacles)
        {
            for (int edge = 1; edge < path.Count; edge++)
                foreach (JourneyCrowdObstacle obstacle in obstacles)
                {
                    float required = radius + obstacle.radius + .12f;
                    float initial = Vector3.Distance(path[0], obstacle.center), previous = initial;
                    for (int sample = 0; sample <= 500; sample++)
                    {
                        float distance = Vector3.Distance(Vector3.Lerp(path[edge - 1], path[edge], sample / 500f), obstacle.center);
                        float minimum = edge == 1 ? Math.Min(required, initial) : required;
                        Require(distance >= minimum - .00005f, "A returned crowd edge penetrates a body clearance circle.");
                        if (edge == 1 && previous < required)
                            Require(distance >= previous - .00005f, "A contact escape first deepened the existing overlap.");
                        previous = distance;
                    }
                }
        }
        private static float Length(List<Vector3> path)
        { float length = 0; for (int i = 1; i < path.Count; i++) length += Vector3.Distance(path[i - 1], path[i]); return length; }
        private static Vector3 Point(float x, float z) => new Vector3(x, 0, z);
        private static JourneyCrowdObstacle Circle(float x, float z, float radius) => new JourneyCrowdObstacle { center = Point(x, z), radius = radius };
        private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    }
}
