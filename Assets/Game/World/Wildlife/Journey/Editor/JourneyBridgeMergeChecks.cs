using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace WeatherHouse.WildlifeJourney.Editor
{
    public static class JourneyBridgeMergeChecks
    {
        [MenuItem("Weather House/Wildlife Journey/Check Bridge Approach Merge")]
        public static void RunMenu() { Debug.Log(RunLogicChecks()); }

        public static string RunLogicChecks()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Run isolated bridge merge checks outside Play mode.");
            return CheckMerge(1, false) + "\n" + CheckMerge(-1, false) + "\n" + CheckMerge(1, true) + "\n" + CheckMerge(-1, true);
        }

        private static string CheckMerge(float side, bool reserved)
        {
            using (var fixture = new Fixture(side, reserved))
            {
                JourneyHerd herd = fixture.herd;
                JourneyMember leader = herd.members[0], follower = herd.members[1];
                Require(leader.Mode == JourneyTravelMode.Walk && ((int)Field("bridge").GetValue(leader) > 0) == reserved,
                    "The merge fixture must start on dry land with the requested reservation state.");
                float room = (leader.bodyLength + follower.bodyLength) * .5f + .12f;
                Require(Flat(leader.root.position, follower.root.position) >= room &&
                    Flat(leader.root.position, follower.root.position) < room + .01f,
                    "The reproduced bridge merge must be close but not initially overlapping.");
                var crossed = new HashSet<int>();
                bool yielded = false;
                Vector3 followerStart = follower.root.position, leaderStart = leader.root.position;
                float initialGap = Flat(followerStart, leaderStart);
                for (int tick = 0; tick < 3000 && herd.Travelling; tick++)
                {
                    Vector3[] before = herd.members.Select(m => m.root.position).ToArray();
                    herd.Simulate(.02f);
                    for (int i = 0; i < herd.members.Length; i++)
                    {
                        JourneyMember member = herd.members[i];
                        if (member.Mode == JourneyTravelMode.Bridge) crossed.Add(member.id);
                        Require(Flat(member.root.position, before[i]) <= member.profile.walkSpeed * 1.12f * .02f + .0002f,
                            "Bridge merge recovery teleported an animal instead of yielding by actual movement.");
                        Require(fixture.nav.TryProject(member.profile, member.root.position, out _, out JourneyTravelMode mode) &&
                            (mode == JourneyTravelMode.Walk || mode == JourneyTravelMode.Bridge),
                            "Bridge merge recovery moved an animal off its safe land/deck surfaces.");
                        for (int j = i + 1; j < herd.members.Length; j++)
                        {
                            JourneyMember other = herd.members[j];
                            float clearance = (member.bodyLength + other.bodyLength) * .5f + .12f;
                            Require(Flat(member.root.position, other.root.position) >= clearance - .0002f,
                                "Bridge merge recovery resolved a queue by overlapping two bodies.");
                        }
                    }
                    yielded |= !crossed.Contains(leader.id) && follower.Mode == JourneyTravelMode.Walk &&
                        Flat(follower.root.position, followerStart) > .08f && Flat(follower.root.position, leaderStart) > initialGap + .04f;
                    Require(herd.TargetSite == 1, "The bridge merge abandoned its intended crossing and changed the reunion site.");
                }
                Require(yielded, "The blocked trailing animal never made room for the animal ahead at the bridge entrance.");
                Require(!herd.Travelling && herd.CompletedJourneys == 1 && herd.members.All(m => m.Arrived) && crossed.Count == 4,
                    "Four same-direction bridge arrivals remained deadlocked at the merge: " + string.Join("; ", herd.members.Select(m =>
                        m.id + " " + m.State + " " + m.Mode + " " + m.root.position + " " + m.LastDecision)));
                return "PASS " + (side > 0 ? "east" : "west") + " bridge merge: " + (reserved ? "reserved" : "not yet reserved") +
                    " leader, three followers yield and all four cross without overlap; " +
                    herd.SimulationTime.ToString("F2", System.Globalization.CultureInfo.InvariantCulture) + "s";
            }
        }

        private sealed class Fixture : IDisposable
        {
            public readonly JourneyHerd herd;
            public readonly JourneyNavigation nav;
            private readonly Scene scene;
            private readonly GameObject root;

            public Fixture(float side, bool reserved)
            {
                scene = EditorSceneManager.NewPreviewScene();
                root = new GameObject("Isolated bridge merge checks") { hideFlags = HideFlags.HideAndDontSave };
                SceneManager.MoveGameObjectToScene(root, scene);
                nav = root.AddComponent<JourneyNavigation>(); JourneyLabBuilder.ConfigureNavigation(nav);
                nav.surfaceSampler = p => new JourneySurface { valid = true, groundY = .65f,
                    bridgeId = Mathf.Abs(p.x) <= 4.6f && Mathf.Abs(p.z - 9) <= .625f ? 1 : 0 };
                herd = root.AddComponent<JourneyHerd>(); herd.navigation = nav; herd.autoAdvance = false;
                herd.sites = new[] { new JourneyActivitySite { center = Point(8 * side, 9), radius = 4.3f },
                    new JourneyActivitySite { center = Point(-11 * side, 9), radius = 4.3f } };
                Vector3[] starts = { Point(5.394f * side, 9.193f), Point(5.413f * side, 7.810f),
                    Point(5.45f * side, 6.3f), Point(5.45f * side, 4.8f) };
                Vector3[] goals = { Point(-10 * side, 10.2f), Point(-10 * side, 7.8f),
                    Point(-12 * side, 10.2f), Point(-12 * side, 7.8f) };
                int[] ids = { 5, 9, 7, 10 };
                herd.members = new JourneyMember[starts.Length];
                for (int i = 0; i < starts.Length; i++)
                {
                    var body = new GameObject("Merge sheep " + ids[i]);
                    SceneManager.MoveGameObjectToScene(body, scene); body.transform.SetParent(root.transform, false);
                    body.transform.position = starts[i];
                    herd.members[i] = new JourneyMember { id = ids[i], root = body.transform, sociability = 0,
                        bodyLength = i == 0 ? 1.2998722f : i == 1 ? 1.2162187f : 1.2f,
                        profile = new JourneyProfile { bodyRadius = .26f, canSwim = false, walkSpeed = 1 } };
                }
                herd.Initialize();
                Require(herd.BeginJourney(1), "The bridge merge fixture could not establish its intended reunion.");
                for (int i = 0; i < herd.members.Length; i++)
                {
                    JourneyMember member = herd.members[i];
                    var route = new JourneyRoute(); Vector3 cursor = starts[i];
                    if (i > 0) Add(Point(6 * side, 9), 9);
                    Add(Point(4.5f * side, 9), 7); Add(Point(-4.5f * side, 9), 6);
                    Add(Point(-6 * side, 9), 5); Add(goals[i], -1);
                    Field("route").SetValue(member, route); Field("destination").SetValue(member, goals[i]);
                    Field("segment").SetValue(member, 0); Field("nextChoice").SetValue(member, 0f); Field("pace").SetValue(member, 1f);
                    member.Mode = JourneyTravelMode.Walk;
                    if (i == 0 && reserved)
                    {
                        // A reservation precedes stepping onto the actual bridge. Reproduce that public movement state,
                        // with the leader displaced sideways by the preceding crowd detour, using the real admission method.
                        int entry = route.segments.FindIndex(s => s.bridgeId == 1);
                        Require(entry >= 0, "The leader route has no actual bridge entry.");
                        Field("segment").SetValue(member, entry);
                        var admit = typeof(JourneyHerd).GetMethod("AdmitBridge", BindingFlags.Instance | BindingFlags.NonPublic);
                        Require(admit != null && (bool)admit.Invoke(herd, new object[] { member, route.segments[entry] }),
                            "The leader could not reserve its real bridge direction.");
                    }
                    void Add(Vector3 target, int link)
                    {
                        JourneyRoute edge = nav.TraceDirectRoute(cursor, target, member.profile);
                        Require(edge != null, "A bridge merge fixture edge was not continuously traversable.");
                        foreach (JourneySegment original in edge.segments)
                        { JourneySegment part = original; part.linkIndex = link; route.segments.Add(part); }
                        route.totalCost += edge.totalCost; cursor = target;
                    }
                }
            }
            public void Dispose() { Object.DestroyImmediate(root); EditorSceneManager.ClosePreviewScene(scene); }
        }

        private static FieldInfo Field(string name)
        {
            FieldInfo field = typeof(JourneyMember).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null) throw new InvalidOperationException("Missing merge fixture state: " + name);
            return field;
        }
        private static Vector3 Point(float x, float z) => new Vector3(x, .65f, z);
        private static float Flat(Vector3 a, Vector3 b) { a.y = b.y = 0; return Vector3.Distance(a, b); }
        private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    }
}
