using System;
using System.Globalization;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace WeatherHouse.WildlifeJourney.Editor
{
    public static class JourneySteeringChecks
    {
        [MenuItem("Weather House/Wildlife Journey/Check Safe Steering")]
        public static void RunMenu() { Debug.Log(RunLogicChecks()); }

        public static string RunLogicChecks()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Run isolated steering checks outside Play mode.");
            return CheckRoundedBendProgression() + "\n" + CheckLookaheadChord() + "\n" + CheckNearCornerProgression() + "\n" + CheckBlockedNextLeg() + "\n" +
                CheckShortBankRest() + "\n" + CheckShortFinalBoundary();
        }

        private static string CheckRoundedBendProgression()
        {
            Vector3[] line = { Point(0, 0), Point(.4f, 0), Point(.4f, .4f), Point(0, .4f), Point(0, 2.2f) };
            using (var fixture = new Fixture(line, line[0], .7f, true))
            {
                Require(fixture.navigation.TryStep(fixture.member.profile, line[0], line[3], out _, out _),
                    "The rounded bend fixture must allow its safe lookahead shortcut.");
                return "PASS safe lookahead advances its route cursor through a rounded bend without zero-speed stalling; " + AdvanceToArrival(fixture);
            }
        }

        private static string CheckLookaheadChord()
        {
            Vector3[] line = { Point(0, 0), Point(.8f, 0), Point(.8f, .8f), Point(.8f, 1.8f) };
            using (var fixture = new Fixture(line, line[0], .08f, true))
            {
                Require(Vector3.Distance(line[0], line[2]) < 1.3f,
                    "The fixture no longer exercises the herd's normal lookahead distance.");
                Require(!fixture.navigation.TryStep(fixture.member.profile, line[0], line[2], out _, out _),
                    "The lookahead chord must leave the safe corridor while both individual legs remain valid.");
                string evidence = AdvanceToArrival(fixture);
                return "PASS same-link curved corridor rejects an unsafe lookahead chord and arrives without replanning; " + evidence;
            }
        }

        private static string CheckNearCornerProgression()
        {
            Vector3[] line = { Point(0, 0), Point(.3f, 0), Point(.3f, .7f), Point(.3f, 1.4f) };
            Vector3 start = Point(.2f, 0);
            using (var fixture = new Fixture(line, start, .025f, false))
            {
                Require(Vector3.Distance(start, line[1]) < .12f,
                    "The fixture must start inside the old waypoint completion tolerance.");
                Require(!fixture.navigation.TryStep(fixture.member.profile, start, line[2], out _, out _),
                    "The next segment must not be directly reachable before approaching this corner.");
                fixture.herd.Simulate(Step);
                Vector3 first = fixture.member.root.position;
                Require(first.x > start.x && Mathf.Abs(first.z - start.z) < .00001f,
                    "A nearby corner was skipped before the next segment became reachable: first position=" + first);
                Require((int)Field("segment").GetValue(fixture.member) == 0,
                    "The herd advanced the waypoint index while its next segment was still unreachable.");
                string evidence = AdvanceToArrival(fixture);
                return "PASS a nearby corner remains active until the next leg is reachable, then the member arrives without replanning; " + evidence;
            }
        }

        private const float Step = .02f;

        private static string CheckShortFinalBoundary()
        {
            Vector3[] line = { Point(0, 0), Point(.1f, 0) };
            var evidence = new string[2];
            for (int variant = 0; variant < 2; variant++)
            {
                JourneyTravelMode crossing = variant == 0 ? JourneyTravelMode.Swim : JourneyTravelMode.Bridge;
                using (var fixture = new Fixture(line, line[0], .5f, true))
                {
                    JourneyMember member = fixture.member;
                    JourneyNavigation nav = fixture.navigation;
                    member.profile.canSwim = crossing == JourneyTravelMode.Swim;
                    member.profile.swimSpeed = .6f; member.profile.waterlineHeight = .42f;
                    member.profile.bodyRadius = .3f; nav.sampleSpacing = .3f;
                    nav.surfaceSampler = point => {
                        bool dry = point.x >= .095f, swimming = !dry && crossing == JourneyTravelMode.Swim;
                        return new JourneySurface { valid = true, blocked = !fixture.Contains(point),
                            groundY = swimming ? -.5f : 0, hasWater = swimming, waterY = .42f,
                            bridgeId = !dry && crossing == JourneyTravelMode.Bridge ? 7 : 0 };
                    };
                    JourneyRoute route = nav.TraceDirectRoute(line[0], line[1], member.profile);
                    Require(route != null && route.segments.Count == 1 && route.segments[0].surfaceBoundary &&
                        route.segments[0].mode == crossing, "The short final " + crossing + " fixture lost its real traced surface transition.");
                    Field("route").SetValue(member, route);
                    Field("segment").SetValue(member, 0);
                    member.Mode = crossing;
                    member.ContinuousSwimDistance = crossing == JourneyTravelMode.Swim ? .4f : 0;
                    fixture.herd.Simulate(Step);
                    Require(!member.Arrived && fixture.herd.CompletedJourneys == 0 && member.Mode == crossing,
                        "The .10 m final " + crossing + " segment completed inside the .18 m arrival tolerance before landing.");
                    bool observedLanding = false;
                    for (int frame = 0; frame < 400 && fixture.herd.CompletedJourneys == 0; frame++)
                    {
                        Vector3 before = member.root.position;
                        fixture.herd.Simulate(Step);
                        Require(Vector3.Distance(before, member.root.position) <= .6f * 1.12f * Step + .0001f,
                            "The short final " + crossing + " segment teleported to its dry endpoint.");
                        observedLanding |= member.Mode == JourneyTravelMode.Walk && member.root.position.x >= .095f;
                        Require(!member.Arrived || observedLanding, "The member arrived before physically leaving " + crossing + ".");
                        Require(fixture.herd.RouteChanges == 0, "The short safe final " + crossing + " segment caused a replan.");
                    }
                    Require(observedLanding && member.Arrived && member.Mode == JourneyTravelMode.Walk &&
                        member.ContinuousSwimDistance == 0 && fixture.herd.CompletedJourneys == 1 && !fixture.herd.Travelling &&
                        Vector3.Distance(member.root.position, fixture.goal) < .0011f,
                        "The short final " + crossing + " segment did not finish at its actual dry endpoint.");
                    evidence[variant] = crossing + "=" + Number(fixture.herd.SimulationTime) + "s";
                }
            }
            return "PASS .10 m final Swim-to-Walk and Bridge-to-Walk segments physically land before arrival; " + string.Join(", ", evidence);
        }

        private static string CheckShortBankRest()
        {
            Vector3[] line = { Point(0, 0), Point(.25f, 0), Point(.25f, .25f), Point(.25f, 1.4f) };
            using (var fixture = new Fixture(line, line[0], .5f, true))
            {
                JourneyMember member = fixture.member;
                JourneyNavigation nav = fixture.navigation;
                member.profile.canSwim = true; member.profile.swimSpeed = .6f;
                member.profile.bodyRadius = .3f; member.profile.waterlineHeight = .42f;
                member.profile.maxSwimDistance = 1.15f; nav.sampleSpacing = .3f;
                Vector3 bank = line[1];
                nav.surfaceSampler = point => {
                    bool dry = Vector3.Distance(point, bank) <= .045f || point.z >= 1f;
                    return new JourneySurface { valid = true, blocked = !fixture.Contains(point),
                        groundY = dry ? 0 : -.5f, hasWater = !dry, waterY = .42f };
                };
                var route = new JourneyRoute { navigationVersion = nav.Version };
                // The first incoming swim already spent .4 m; the bank resets it before the next .25 m leg.
                float[] spentBeforeLeg = { .4f, 0, .25f };
                for (int i = 1; i < line.Length; i++)
                {
                    JourneyRoute leg = nav.TraceDirectRoute(line[i - 1], line[i], member.profile, spentBeforeLeg[i - 1]);
                    Require(leg != null, "The real navigation trace rejected bank-rest fixture leg " + i);
                    route.segments.AddRange(leg.segments);
                }
                Require(route.segments[0].mode == JourneyTravelMode.Swim && route.segments[1].mode == JourneyTravelMode.Swim,
                    "The fixture must retain Trace's two adjacent Swim-labelled transitions across a real dry waypoint.");
                Require(nav.TryStep(member.profile, line[0], line[2], out _, out _, JourneyTravelMode.Swim),
                    "The shortcut must be physically swimmable while bypassing the required bank rest.");
                Field("route").SetValue(member, route);
                Field("segment").SetValue(member, 0);
                member.Mode = JourneyTravelMode.Swim; member.ContinuousSwimDistance = .4f;
                bool observedRest = false, resumedSwimming = false;
                float maximumSwimming = member.ContinuousSwimDistance;
                for (int frame = 0; frame < 1600 && fixture.herd.CompletedJourneys == 0; frame++)
                {
                    Vector3 before = member.root.position;
                    fixture.herd.Simulate(Step);
                    Require(Vector3.Distance(before, member.root.position) <= .6f * 1.12f * Step + .0001f,
                        "The bank-rest fixture moved farther than its genuine simulation step.");
                    Require(fixture.Contains(member.root.position), "The swimmer left its validated bank-rest corridor.");
                    Require(fixture.herd.RouteChanges == 0, "The valid bank-rest route caused an unnecessary replan.");
                    maximumSwimming = Mathf.Max(maximumSwimming, member.ContinuousSwimDistance);
                    if (member.Mode == JourneyTravelMode.Walk && Vector3.Distance(member.root.position, bank) <= .0451f)
                    {
                        Require(member.ContinuousSwimDistance == 0, "Reaching the dry bank did not reset the actual swimming budget.");
                        observedRest = true;
                    }
                    if (observedRest && member.Mode == JourneyTravelMode.Swim && member.ContinuousSwimDistance > 0)
                        resumedSwimming = true;
                }
                Require(observedRest && resumedSwimming,
                    "The member shortcut past the short dry bank instead of visibly walking, resetting its budget and swimming again.");
                Require(fixture.herd.CompletedJourneys == 1 && member.Arrived && !fixture.herd.Travelling &&
                    member.Mode == JourneyTravelMode.Walk && Vector3.Distance(member.root.position, fixture.goal) < .19f,
                    "The bank-rest journey did not finish on its real dry destination.");
                Require(maximumSwimming <= member.profile.maxSwimDistance + .0001f,
                    "Following the bank-rest route exceeded the member's continuous swimming limit.");
                return "PASS real traced Swim-to-bank-to-Swim transitions physically rest, reset and resume before arrival; time=" +
                    Number(fixture.herd.SimulationTime) + "s, maximumContinuousSwim=" + Number(maximumSwimming) + "m, routeChanges=0";
            }
        }

        private static string CheckBlockedNextLeg()
        {
            Vector3[] line = { Point(0, 0), Point(.5f, 0), Point(1.1f, 0), Point(1.8f, 0) };
            using (var fixture = new Fixture(line, line[0], .08f, false))
            {
                // Close the next leg after validation. Its current corner remains reachable throughout.
                fixture.additionalBlock = point => point.x > .65f && point.x < .95f;
                bool reachedCorner = false, reconsidered = false;
                for (int frame = 0; frame < 500 && !reconsidered; frame++)
                {
                    fixture.herd.Simulate(Step);
                    Vector3 position = fixture.member.root.position;
                    reachedCorner |= Vector3.Distance(position, line[1]) < .002f;
                    Require(!fixture.additionalBlock(position), "The member walked through the newly blocked next leg.");
                    Require(!fixture.member.Arrived && fixture.herd.TargetSite == 1,
                        "A blocked next leg incorrectly completed or abandoned the intended journey.");
                    reconsidered = Field("route").GetValue(fixture.member) == null &&
                        fixture.member.State == JourneyMemberState.Regrouping;
                }
                Require(reachedCorner, "The member never reached its still-valid current waypoint.");
                Require(reconsidered,
                    "At the corner, a blocked next leg kept succeeding as a zero-length move instead of entering replanning.");
                fixture.additionalBlock = null;
                string evidence = AdvanceToArrival(fixture, 1, .8f);
                Require(fixture.herd.RouteChanges == 1, "Reopening the next leg did not produce exactly one successful recovery plan.");
                return "PASS a newly blocked next leg enters replanning after reaching its corner and arrives after reopening; " + evidence;
            }
        }

        private static string AdvanceToArrival(Fixture fixture, int maximumReplans = 0, float maximumStop = .3f)
        {
            JourneyHerd herd = fixture.herd;
            JourneyMember member = fixture.member;
            int initialChanges = herd.RouteChanges;
            float longestStop = 0, stopped = 0, largestStep = 0;
            for (int frame = 0; frame < 1600 && herd.CompletedJourneys == 0; frame++)
            {
                Vector3 before = member.root.position;
                herd.Simulate(Step);
                float moved = Vector3.Distance(before, member.root.position);
                largestStep = Mathf.Max(largestStep, moved);
                Require(moved <= member.profile.walkSpeed * 1.12f * Step + .0001f,
                    "Steering escaped the fixture by moving farther than the actual frame's walking distance.");
                Require(fixture.Contains(member.root.position), "Steering left the validated corridor at " + member.root.position);
                if (!member.Arrived && moved < .00001f) stopped += Step; else stopped = 0;
                longestStop = Mathf.Max(longestStop, stopped);
                Require(herd.RouteChanges <= initialChanges + maximumReplans,
                    "A valid corridor caused an unnecessary replan at " + member.root.position + ": " + member.LastDecision);
            }
            Require(herd.CompletedJourneys == 1 && member.Arrived && !herd.Travelling && herd.TargetSite == 1,
                "The member did not continuously reach the intended rendezvous: position=" + member.root.position + ", " + member.LastDecision);
            Require(Vector3.Distance(member.root.position, fixture.goal) < .19f,
                "Arrival was declared before the member reached the real end of the fixture route.");
            Require(longestStop < maximumStop, "The safe corridor produced a persistent steering stall of " + longestStop + " seconds.");
            return "time=" + Number(herd.SimulationTime) + "s, distance=" + Number(member.DistanceTravelled) +
                "m, longestStop=" + Number(longestStop) + "s, largestStep=" + Number(largestStep) + "m, routeChanges=" + herd.RouteChanges;
        }

        private sealed class Fixture : IDisposable
        {
            private readonly Scene scene;
            private readonly GameObject root;
            private readonly Vector3[] line;
            private readonly float halfWidth;
            public readonly JourneyNavigation navigation;
            public readonly JourneyHerd herd;
            public readonly JourneyMember member;
            public readonly Vector3 goal;
            public Func<Vector3, bool> additionalBlock;

            public Fixture(Vector3[] line, Vector3 start, float halfWidth, bool sameLink)
            {
                this.line = line; this.halfWidth = halfWidth; goal = line[line.Length - 1];
                scene = EditorSceneManager.NewPreviewScene();
                root = new GameObject("Isolated Journey steering fixture");
                SceneManager.MoveGameObjectToScene(root, scene);
                try
                {
                    navigation = root.AddComponent<JourneyNavigation>();
                    navigation.surfaceSampler = point => new JourneySurface { valid = true, groundY = 0 };
                    navigation.sampleSpacing = .04f;
                    navigation.endpointConnectionRadius = .5f;
                    navigation.nodes = new JourneyNode[line.Length];
                    navigation.links = new JourneyLink[line.Length - 1];
                    for (int i = 0; i < line.Length; i++)
                    {
                        navigation.nodes[i] = new JourneyNode { id = "Corner " + i, position = line[i] };
                        if (i > 0) navigation.links[i - 1] = new JourneyLink { from = i - 1, to = i };
                    }
                    navigation.Rebuild();
                    var animal = new GameObject("Steering sheep");
                    SceneManager.MoveGameObjectToScene(animal, scene);
                    animal.transform.SetParent(root.transform, false);
                    animal.transform.position = start;
                    member = new JourneyMember { id = 1, root = animal.transform, sociability = 0, bodyLength = .2f,
                        profile = new JourneyProfile { bodyRadius = .05f, canSwim = false, walkSpeed = .6f } };
                    herd = root.AddComponent<JourneyHerd>();
                    herd.navigation = navigation; herd.autoAdvance = false; herd.rendezvousTimeout = 60;
                    herd.members = new[] { member };
                    herd.sites = new[] {
                        new JourneyActivitySite { name = "Start", center = start, radius = .2f },
                        new JourneyActivitySite { name = "Goal", center = goal, radius = .2f } };
                    herd.Initialize();
                    Require(herd.BeginJourney(1), "The flat baseline could not initialize a legitimate journey.");

                    // Replay a previously validated polyline without moving the member or replacing its simulation.
                    navigation.surfaceSampler = point => new JourneySurface { valid = true, groundY = 0,
                        blocked = !Contains(point) || (additionalBlock != null && additionalBlock(point)) };
                    var route = new JourneyRoute { navigationVersion = navigation.Version };
                    for (int i = 1; i < line.Length; i++)
                    {
                        Require(navigation.TryStep(member.profile, line[i - 1], line[i], out _, out JourneyTravelMode mode) && mode == JourneyTravelMode.Walk,
                            "The fixture route itself contains an unsafe leg " + i);
                        route.segments.Add(new JourneySegment { from = line[i - 1], to = line[i], mode = JourneyTravelMode.Walk,
                            linkIndex = sameLink ? 0 : i - 1 });
                    }
                    Field("route").SetValue(member, route);
                    Field("segment").SetValue(member, 0);
                    Field("destination").SetValue(member, goal);
                    Field("nextChoice").SetValue(member, 0f);
                    Require(member.root.position == start, "Preparing the fixture moved the member.");
                }
                catch { Dispose(); throw; }
            }

            public bool Contains(Vector3 point)
            {
                for (int i = 1; i < line.Length; i++)
                {
                    Vector3 a = line[i - 1], direction = line[i] - a;
                    float along = Mathf.Clamp01(Vector3.Dot(point - a, direction) / direction.sqrMagnitude);
                    Vector3 offset = point - (a + direction * along); offset.y = 0;
                    if (offset.sqrMagnitude <= halfWidth * halfWidth + .0000001f) return true;
                }
                return false;
            }

            public void Dispose()
            {
                if (root) Object.DestroyImmediate(root);
                if (scene.IsValid()) EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        private static Vector3 Point(float x, float z) => new Vector3(x, 0, z);
        private static string Number(float value) => value.ToString("F3", CultureInfo.InvariantCulture);
        private static FieldInfo Field(string name)
        {
            FieldInfo field = typeof(JourneyMember).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null) throw new InvalidOperationException("Steering fixture could not inspect JourneyMember." + name);
            return field;
        }
        private static void Require(bool condition, string message)
        { if (!condition) throw new InvalidOperationException(message); }
    }
}
