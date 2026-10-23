using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace WeatherHouse.WildlifeJourney.Editor
{
    public static class JourneyAvoidanceChecks
    {
        [MenuItem("Weather House/Wildlife Journey/Check Local Avoidance")]
        public static void RunMenu() { Debug.Log(RunLogicChecks()); }

        public static string RunLogicChecks()
        {
            return string.Join("\n", RunFixedArrivalGap(), RunEnclosedArrival(), RunLoiterDetour(false), RunLoiterDetour(true));
        }

        private static string RunFixedArrivalGap()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Run isolated avoidance checks outside Play mode.");
            Scene scene = EditorSceneManager.NewPreviewScene();
            var root = new GameObject("Isolated arrival-gap regression") { hideFlags = HideFlags.HideAndDontSave };
            SceneManager.MoveGameObjectToScene(root, scene);
            try
            {
                JourneyNavigation nav = root.AddComponent<JourneyNavigation>();
                nav.surfaceSampler = p => new JourneySurface { valid = true, groundY = .65f };
                Vector3 start = Point(8.160f, 1.742f), routeAnchor = Point(8.853f, 2.453f), goal = Point(13.780f, -.861f);
                nav.nodes = new[] { new JourneyNode { id = "arrival approach", position = routeAnchor },
                    new JourneyNode { id = "east destination", position = goal } };
                nav.links = new[] { new JourneyLink { from = 0, to = 1, bidirectional = true } };
                nav.Rebuild();
                JourneyHerd herd = root.AddComponent<JourneyHerd>();
                herd.navigation = nav; herd.autoAdvance = false;
                herd.sites = new[] { new JourneyActivitySite { name = "approach", center = start, radius = 4.3f },
                    new JourneyActivitySite { name = "east arrival", center = goal, radius = 4.3f } };
                herd.members = new[] { Member(root.transform, scene, 1, start),
                    Member(root.transform, scene, 2, Point(9.039f, 2.588f)), Member(root.transform, scene, 5, Point(9.075f, .878f)) };
                herd.Initialize();
                Require(herd.BeginJourney(1), "The arrival-gap fixture could not begin a real herd journey.");

                JourneyMember traveller = herd.members[0];
                JourneyRoute route = nav.FindRoute(routeAnchor, goal, traveller.profile);
                Require(route != null, "The fixture's unobstructed terrain approach is unreachable.");
                // Replay the trace's legitimate off-path position, retaining the route cursor ahead of it.
                Set(traveller, "route", route); Set(traveller, "segment", 0);
                Set(traveller, "destination", goal); Set(traveller, "nextChoice", 0f);
                Vector3[] stationary = { herd.members[1].root.position, herd.members[2].root.position };
                for (int i = 1; i < herd.members.Length; i++)
                {
                    JourneyMember observer = herd.members[i];
                    observer.Arrived = true; observer.State = JourneyMemberState.Idle;
                    Set(observer, "route", null); Set(observer, "localTarget", observer.root.position);
                    Set(observer, "nextChoice", 1000f);
                }

                float minimumGap = float.PositiveInfinity;
                for (int tick = 0; tick < 1200 && !traveller.Arrived; tick++)
                {
                    Vector3 previous = traveller.root.position;
                    herd.Simulate(.05f);
                    Require(Vector3.Distance(previous, traveller.root.position) <= .06f,
                        "Local avoidance teleported or exceeded the configured walking step.");
                    for (int i = 1; i < herd.members.Length; i++)
                    {
                        JourneyMember observer = herd.members[i];
                        Require(Vector3.Distance(observer.root.position, stationary[i - 1]) < .0001f,
                            "An arrived observer moved, so the fixed arrival-gap regression no longer applies.");
                        float gap = Vector3.Distance(traveller.root.position, observer.root.position);
                        minimumGap = Mathf.Min(minimumGap, gap);
                        Require(gap >= 1.218f, "Avoidance escaped by overlapping an observer's body clearance: " + gap);
                    }
                    Require(traveller.DistanceTravelled <= 30f,
                        "Avoidance exceeded 30 metres around a six-metre arrival approach without completing it.");
                }
                Require(traveller.Arrived && Vector3.Distance(traveller.root.position, goal) < .2f,
                    "The traveller did not escape the fixed arrival gap within 60 seconds: position=" + traveller.root.position +
                    "; distance=" + traveller.DistanceTravelled + "; decision=" + traveller.LastDecision);
                return string.Format(CultureInfo.InvariantCulture,
                    "PASS fixed three-sheep arrival gap: traveller arrives in {0:F2}s, walks {1:F2}m and retains {2:F3}m clearance",
                    herd.SimulationTime, traveller.DistanceTravelled, minimumGap);
            }
            finally
            {
                if (root) Object.DestroyImmediate(root);
                if (scene.IsValid()) EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        private static string RunEnclosedArrival()
        {
            return Isolated("Observed arrival pocket", (root, scene) => {
                // Play_20261024_015648, trajectory.csv at 154.691s. The goal is sheep 6's first observed arrived position.
                Vector3 start = Point(-9.844f, 3.230f), goal = Point(-10.888f, -3.689f), center = Point(-11, 0);
                JourneyMember traveller = Member(root.transform, scene, 6, start);
                JourneyMember first = Member(root.transform, scene, 1, Point(-11.137f, 2.846f));
                JourneyMember second = Member(root.transform, scene, 2, Point(-8.864f, 2.089f));
                JourneyMember third = Member(root.transform, scene, 7, Point(-9.578f, 4.540f));
                traveller.bodyLength = 1.2706457f; first.bodyLength = 1.1760403f;
                second.bodyLength = 1.2517221f; third.bodyLength = 1.148837f;
                JourneyHerd herd = Setup(root, start, goal, center, 4.3f, traveller, first, second, third);
                Require(herd.BeginJourney(1), "The observed arrival pocket could not begin a journey.");
                Set(traveller, "route", herd.navigation.FindRoute(start, goal, traveller.profile));
                Set(traveller, "segment", 0); Set(traveller, "destination", goal); Set(traveller, "nextChoice", 0f);
                var obstacles = new List<JourneyCrowdObstacle>();
                for (int i = 1; i < herd.members.Length; i++)
                {
                    Rest(herd.members[i]);
                    obstacles.Add(new JourneyCrowdObstacle { center = herd.members[i].root.position, radius = herd.members[i].bodyLength * .5f });
                }
                Require(!JourneyCrowdPath.TryFindPath(start, goal, traveller.bodyLength * .5f, obstacles, (a, b) => true, out _),
                    "The observed three-circle pocket is no longer enclosed; this test would not require a neighbour to yield.");
                Vector3[] initial = Positions(herd);
                bool yielded = false;
                for (int tick = 0; tick < 400 && !traveller.Arrived; tick++)
                {
                    Vector3[] before = Positions(herd);
                    herd.Simulate(.05f);
                    CheckSteps(herd, before, initial, center, 4.3f);
                    for (int i = 1; i < herd.members.Length; i++)
                        yielded |= Vector3.Distance(initial[i], herd.members[i].root.position) > .2f;
                    Require(traveller.DistanceTravelled < 16f, "The enclosed traveller circles instead of using the opened exit.");
                }
                Require(yielded && traveller.Arrived && Vector3.Distance(traveller.root.position, goal) < .2f,
                    "The observed pocket did not open and finish within 20s: yielded=" + yielded + "; position=" + traveller.root.position +
                    "; distance=" + traveller.DistanceTravelled + "; decision=" + traveller.LastDecision);
                return string.Format(CultureInfo.InvariantCulture,
                    "PASS observed three-neighbour pocket: an arrived neighbour yields; traveller arrives in {0:F2}s after {1:F2}m",
                    herd.SimulationTime, traveller.DistanceTravelled);
            });
        }

        private static string RunLoiterDetour(bool afterSocial)
        {
            return Isolated(afterSocial ? "Social contact exit" : "Arrived wander detour", (root, scene) => {
                Vector3 start = Point(afterSocial ? -1.24f : -2f, 0), goal = Point(2.5f, 0), center = Point(0, 0);
                JourneyMember walker = Member(root.transform, scene, 1, start);
                JourneyMember neighbour = Member(root.transform, scene, 2, center);
                if (afterSocial) walker.bodyLength = neighbour.bodyLength = 1.2f;
                JourneyHerd herd = Setup(root, start, goal, center, 5f, walker, neighbour);
                Rest(walker); Rest(neighbour);
                Set(walker, "localTarget", goal);
                Vector3[] initial = Positions(herd);
                bool usedPath = false, detoured = false;
                for (int tick = 0; tick < 440; tick++)
                {
                    Vector3[] before = Positions(herd);
                    herd.Simulate(.05f);
                    CheckSteps(herd, before, initial, center, 5f);
                    Require(Vector3.Distance(neighbour.root.position, initial[1]) < .0001f, "The static wandering neighbour moved.");
                    usedPath |= Get(walker, "crowdPath") != null;
                    detoured |= Mathf.Abs(walker.root.position.z) > .3f;
                    if (Vector3.Distance(walker.root.position, goal) < .12f && Get(walker, "crowdPath") == null) break;
                }
                Require(usedPath && detoured && Vector3.Distance(walker.root.position, goal) < .12f && Get(walker, "crowdPath") == null,
                    (afterSocial ? "Post-social walking" : "Arrived wandering") + " failed to execute and clear its planned detour: position=" +
                    walker.root.position + "; distance=" + walker.DistanceTravelled + "; heldPath=" + (Get(walker, "crowdPath") != null));
                Require(walker.DistanceTravelled < 10f, "The wandering detour exceeded its short local path.");
                Set(walker, "nextChoice", 0f);
                herd.Simulate(.05f);
                Require((float)Get(walker, "nextChoice") > herd.SimulationTime, "A completed local path prevented the next activity choice.");
                return string.Format(CultureInfo.InvariantCulture, "PASS {0}: held detour executes and clears in {1:F2}s, {2:F2}m; next activity remains available",
                    afterSocial ? ".04m social gap opens to normal walking clearance" : "arrived wandering", herd.SimulationTime, walker.DistanceTravelled);
            });
        }

        private static JourneyHerd Setup(GameObject root, Vector3 start, Vector3 goal, Vector3 center, float radius, params JourneyMember[] members)
        {
            JourneyNavigation nav = root.AddComponent<JourneyNavigation>();
            nav.surfaceSampler = p => new JourneySurface { valid = true, groundY = .65f };
            nav.nodes = new[] { new JourneyNode { id = "start", position = start }, new JourneyNode { id = "goal", position = goal } };
            nav.links = new[] { new JourneyLink { from = 0, to = 1, bidirectional = true } }; nav.Rebuild();
            JourneyHerd herd = root.AddComponent<JourneyHerd>();
            herd.navigation = nav; herd.autoAdvance = false; herd.members = members;
            herd.sites = new[] { new JourneyActivitySite { name = "activity ground", center = center, radius = radius },
                new JourneyActivitySite { name = "rendezvous", center = center, radius = radius } };
            herd.Initialize(); return herd;
        }

        private static void Rest(JourneyMember member)
        {
            member.Arrived = true; member.State = JourneyMemberState.Idle;
            Set(member, "route", null); Set(member, "localTarget", member.root.position); Set(member, "nextChoice", 1000f);
        }

        private static Vector3[] Positions(JourneyHerd herd)
        {
            var positions = new Vector3[herd.members.Length];
            for (int i = 0; i < positions.Length; i++) positions[i] = herd.members[i].root.position;
            return positions;
        }

        private static void CheckSteps(JourneyHerd herd, Vector3[] before, Vector3[] initial, Vector3 center, float siteRadius)
        {
            for (int i = 0; i < herd.members.Length; i++)
            {
                JourneyMember member = herd.members[i];
                Require(member.Mode == JourneyTravelMode.Walk && Mathf.Abs(member.root.position.y - .65f) < .001f,
                    "A local crowd escape left its walking surface.");
                Require(Vector3.Distance(before[i], member.root.position) <= member.profile.walkSpeed * 1.12f * .05f + .0001f,
                    "A local crowd escape teleported or exceeded its walking speed.");
                // Observed sheep 7 starts outside the site. It may stay or move inward, but the fixture cannot enlarge the site.
                Require(Vector3.Distance(center, member.root.position) <= Mathf.Max(siteRadius, Vector3.Distance(center, initial[i])) + .002f,
                    "A local crowd escape left the existing activity boundary.");
                for (int j = 0; j < i; j++)
                {
                    JourneyMember other = herd.members[j];
                    float normal = (member.bodyLength + other.bodyLength) * .5f + .12f;
                    float previous = Vector3.Distance(before[i], before[j]);
                    Require(Vector3.Distance(member.root.position, other.root.position) >= Mathf.Min(normal, previous) - .0001f,
                        "A local crowd escape crossed a body clearance or deepened an existing social contact.");
                }
            }
        }

        private static string Isolated(string name, Func<GameObject, Scene, string> run)
        {
            Scene scene = EditorSceneManager.NewPreviewScene();
            var root = new GameObject(name) { hideFlags = HideFlags.HideAndDontSave };
            SceneManager.MoveGameObjectToScene(root, scene);
            try { return run(root, scene); }
            finally { Object.DestroyImmediate(root); EditorSceneManager.ClosePreviewScene(scene); }
        }

        private static object Get(JourneyMember member, string name)
        {
            FieldInfo field = typeof(JourneyMember).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Require(field != null, "The avoidance fixture cannot access member state: " + name);
            return field.GetValue(member);
        }

        private static JourneyMember Member(Transform parent, Scene scene, int id, Vector3 position)
        {
            var animal = new GameObject("Arrival-gap sheep " + id) { hideFlags = HideFlags.HideAndDontSave };
            SceneManager.MoveGameObjectToScene(animal, scene);
            animal.transform.SetParent(parent, false); animal.transform.position = position;
            return new JourneyMember { id = id, root = animal.transform, sociability = 0, bodyLength = 1.1f,
                profile = new JourneyProfile { bodyRadius = .25f, canSwim = false, walkSpeed = .9f } };
        }
        private static Vector3 Point(float x, float z) => new Vector3(x, .65f, z);
        private static void Set(JourneyMember member, string name, object value)
        {
            FieldInfo field = typeof(JourneyMember).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Require(field != null, "The avoidance fixture cannot access member state: " + name);
            field.SetValue(member, value);
        }
        private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    }
}
