using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using WeatherHouse.Hillside;
using Object = UnityEngine.Object;

namespace WeatherHouse.WildlifeJourney.Editor
{
    public static class JourneyHerdChecks
    {
        [MenuItem("Weather House/Wildlife Journey/Check Herd Decisions")]
        public static void RunMenu() { Debug.Log(RunLogicChecks()); }

        public static void BatchTraceHerd()
        {
            string project = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            if (!Application.isBatchMode || !File.Exists(Path.Combine(project, ".wildlife-journey-validation")))
                throw new InvalidOperationException("Herd tracing is only for the marked independent batch validation project.");
            string folder = Path.Combine(project, "WildlifeJourneyReview");
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, "herd-diagnostic-" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss") + ".csv");
            var csv = new StringBuilder("time,id,state,mode,arrived,x,y,z,yaw,speed,distance_travelled,nearest_companion,segment,segment_count,from_x,from_z,to_x,to_z,waypoint_distance,link,bridge,goal_x,goal_z,target_site,travelling,completed,route_changes,decision\n");
            FieldInfo routeField = typeof(JourneyMember).GetField("route", BindingFlags.Instance | BindingFlags.NonPublic);
            FieldInfo segmentField = typeof(JourneyMember).GetField("segment", BindingFlags.Instance | BindingFlags.NonPublic);
            FieldInfo goalField = typeof(JourneyMember).GetField("destination", BindingFlags.Instance | BindingFlags.NonPublic);
            Require(routeField != null && segmentField != null && goalField != null, "Diagnostic route fields could not be inspected.");
            using (var fixture = new Fixture())
            {
                JourneyHerd herd = fixture.herd;
                Require(herd.BeginJourney(1), "The diagnostic herd could not start its east-bank journey.");
                if (Environment.GetCommandLineArgs().Contains("-journeyTraceLandBlocked"))
                {
                    fixture.landClosed = true;
                    herd.navigation.Rebuild();
                }
                for (int tick = 0; tick <= 1250; tick++)
                {
                    if (tick > 0) herd.Simulate(.1f);
                    if (tick % 50 != 0) continue;
                    foreach (JourneyMember member in herd.members)
                    {
                        JourneyRoute route = (JourneyRoute)routeField.GetValue(member);
                        int index = (int)segmentField.GetValue(member);
                        Vector3 goal = (Vector3)goalField.GetValue(member);
                        bool hasSegment = route != null && index >= 0 && index < route.segments.Count;
                        JourneySegment segment = hasSegment ? route.segments[index] : default;
                        Vector3 position = member.root.position;
                        float neighbourDistance = herd.members.Where(other => other != member && other.root && other.root.gameObject.activeInHierarchy)
                            .Select(other => Vector2.Distance(new Vector2(position.x, position.z), new Vector2(other.root.position.x, other.root.position.z)))
                            .DefaultIfEmpty(float.PositiveInfinity).Min();
                        csv.AppendLine(string.Join(",", new[] {
                            F(herd.SimulationTime), member.id.ToString(), member.State.ToString(), member.Mode.ToString(), member.Arrived ? "1" : "0",
                            F(position.x), F(position.y), F(position.z), F(member.root.eulerAngles.y), F(member.ActualSpeed), F(member.DistanceTravelled), F(neighbourDistance),
                            index.ToString(), (route == null ? 0 : route.segments.Count).ToString(),
                            hasSegment ? F(segment.from.x) : "", hasSegment ? F(segment.from.z) : "", hasSegment ? F(segment.to.x) : "", hasSegment ? F(segment.to.z) : "",
                            hasSegment ? F(Vector2.Distance(new Vector2(position.x, position.z), new Vector2(segment.to.x, segment.to.z))) : "",
                            hasSegment ? segment.linkIndex.ToString() : "", hasSegment ? segment.bridgeId.ToString() : "", F(goal.x), F(goal.z),
                            herd.TargetSite.ToString(), herd.Travelling ? "1" : "0", herd.CompletedJourneys.ToString(), herd.RouteChanges.ToString(),
                            "\"" + (member.LastDecision ?? "").Replace("\"", "\"\"") + "\""
                        }));
                    }
                    File.WriteAllText(path, csv.ToString());
                }
                File.WriteAllText(Path.Combine(folder, "latest-herd-diagnostic.txt"), path);
                Debug.Log("Herd diagnostic written: " + path + "; target=" + herd.TargetSite + "; completed=" + herd.CompletedJourneys);
            }
        }

        private static string F(float value) { return value.ToString("F3", CultureInfo.InvariantCulture); }

        public static string RunLogicChecks()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Run isolated herd checks outside Play mode.");
            var results = new List<string>();
            CheckBridgeApproachVariation();
            results.Add("PASS the entire bridge approach keeps its center line while open ground retains individual route offsets");
            CheckHiddenWaterVariation();
            results.Add("PASS route variation rejects hidden mid-segment swimming beyond endurance while preserving a safe land offset");
            CheckBridgeSideEntranceAdmission();
            results.Add("PASS lateral bridge entrances admit the same crossing direction and reject opposing crossings");
            using (var fixture = new Fixture())
            {
                JourneyHerd herd = fixture.herd;
                FieldInfo phase = typeof(JourneyMember).GetField("phase", BindingFlags.Instance | BindingFlags.NonPublic);
                Require(phase != null, "Individual animation phases cannot be inspected.");
                float[] phases = herd.members.Select(m => (float)phase.GetValue(m)).ToArray();
                Require(phases.Distinct().Count() == herd.members.Length, "All sheep must have independent seeded animation phases.");
                herd.Initialize();
                Require(herd.members.Select(m => (float)phase.GetValue(m)).SequenceEqual(phases), "Reinitialization changed individual animation phases.");
                results.Add("PASS independent stable individual animation phases");

                Require(herd.BeginJourney(1), "A mixed-ability herd could not start its east-bank journey.");
                TravelEvidence outward = Advance(herd, 3000);
                RequireArrived(herd, 1);
                Require(outward.swimmers.Count == 4 && outward.bridgeWalkers.Count >= 6,
                    "The journey did not visibly split into four swimmers and six bridge walkers.");
                Require(outward.earlyArrival, "No early arrival waited for companions while the herd was still travelling.");
                Require(outward.maximumBridgeOccupancy >= 2, "The natural crossing never exercised multiple sheep sharing the narrow bridge in single file.");
                Require(outward.firstMovement.Values.Distinct().Count() >= 4, "Departure timing stayed synchronized.");
                results.Add("PASS mixed-ability split, staggered departure, surface swimming, bridge queue and full east-bank reunion");

                Require(herd.BeginJourney(0), "The reunited herd could not begin its return journey.");
                TravelEvidence homeward = Advance(herd, 3000);
                RequireArrived(herd, 0);
                Require(herd.CompletedJourneys == 2 && homeward.swimmers.Count == 4 && homeward.bridgeWalkers.Count >= 6,
                    "Return crossing lost individual route abilities or left members behind.");
                results.Add("PASS bidirectional crossing and second whole-herd reunion");
            }
            using (var fixture = new Fixture())
            {
                JourneyHerd herd = fixture.herd;
                herd.members = herd.members.Take(2).ToArray();
                herd.members[0].root.position = JourneyLabBuilder.Position(-5.1f, 0);
                herd.members[1].root.position = JourneyLabBuilder.Position(-6.7f, 0);
                herd.members[0].profile.swimSpeed = .3f;
                herd.members[1].profile.swimSpeed = 1.4f;
                herd.members[0].profile.walkSpeed = herd.members[1].profile.walkSpeed = .55f;
                herd.Initialize();
                int pairedSwimmingTicks = 0;
                float closestWetGap = float.PositiveInfinity;
                for (int journey = 0; journey < 4; journey++)
                {
                    int destination = journey % 2 == 0 ? 1 : 0;
                    Require(herd.BeginJourney(destination), "Different-speed swimmers could not start journey " + (journey + 1));
                    TravelEvidence evidence = Advance(herd, 3000, true);
                    RequireArrived(herd, destination);
                    Require(evidence.swimmers.Count == 2, "Both animals must swim to exercise the following-distance check.");
                    pairedSwimmingTicks += evidence.pairedSwimmingTicks;
                    closestWetGap = Mathf.Min(closestWetGap, evidence.closestWetGap);
                }
                Require(herd.CompletedJourneys == 4 && pairedSwimmingTicks >= 10 && closestWetGap < 2.2f,
                    "The fast follower never approached the slow swimmer closely enough to exercise collision avoidance.");
                results.Add("PASS faster swimmers do not persistently overlap slower companions; both reunite after two round trips");
            }
            using (var fixture = new Fixture())
            {
                JourneyHerd herd = fixture.herd;
                Vector3[] before = herd.members.Select(m => m.root.position).ToArray();
                fixture.bridgeClosed = true;
                Require(!herd.BeginJourney(1), "A mixed herd committed to a destination unreachable by its non-swimmers.");
                Require(!herd.Travelling && herd.TargetSite == 0 && herd.members.Select(m => m.root.position).SequenceEqual(before),
                    "Rejected planning partially committed the herd or teleported an animal.");
                fixture.bridgeClosed = false;
                herd.navigation.Rebuild();
                Require(herd.BeginJourney(1), "Restoring the bridge did not restore a reachable journey.");
                Advance(herd, 70);
                fixture.bridgeClosed = true;
                herd.navigation.Rebuild();
                Advance(herd, 250);
                Require(herd.members.Any(m => !m.profile.canSwim && !m.Arrived), "Bridge closure did not delay any bridge-dependent member.");
                JourneyMember stopped = herd.members.FirstOrDefault(m => m.Mode == JourneyTravelMode.Bridge && m.ActualSpeed < .02f);
                Require(stopped != null, "The deck obstruction did not exercise waiting after an animal had stepped onto the bridge.");
                Vector3 waitingPosition = stopped.root.position;
                Require(!herd.BeginJourney(0) && herd.TargetSite == 1, "A sheep waiting on a blocked bridge accepted an unsafe reversal.");
                Advance(herd, 40);
                Require(stopped.root.position == waitingPosition && stopped.Mode == JourneyTravelMode.Bridge,
                    "A sheep waiting for a blocked bridge deck moved off its safe position.");
                fixture.bridgeClosed = false;
                herd.navigation.Rebuild();
                Advance(herd, 3000);
                RequireArrived(herd, 1);
                results.Add("PASS unreachable-group rejection, safe on-deck waiting without reversal, and reunion after reopening");
            }
            using (var fixture = new Fixture())
            {
                JourneyHerd herd = fixture.herd;
                Require(herd.BeginJourney(1), "Land-obstruction fixture could not start.");
                fixture.landClosed = true;
                herd.navigation.Rebuild();
                AdvanceUntil(herd, () => herd.RouteChanges > 0, "A newly blocked land route was never reconsidered while the detour remained available.");
                Require(herd.TargetSite == 1 && herd.Travelling, "A land obstruction abandoned the reachable destination instead of selecting a detour.");
                Advance(herd, 3000);
                RequireArrived(herd, 1);
                Require(fixture.landClosed && herd.RouteChanges > 0, "Land replanning only recovered because the obstacle disappeared.");
                results.Add("PASS a dynamic land obstruction causes replanning and reunion through an available detour while still blocked");
            }
            using (var fixture = new Fixture())
            {
                JourneyHerd herd = fixture.herd;
                Require(herd.BeginJourney(1), "Pause/resume fixture could not start.");
                Advance(herd, 60);
                float time = herd.SimulationTime;
                Vector3[] positions = herd.members.Select(m => m.root.position).ToArray();
                float[] distances = herd.members.Select(m => m.DistanceTravelled).ToArray();
                herd.SetPaused(true);
                Advance(herd, 100);
                Require(herd.Paused && herd.SimulationTime == time && herd.members.Select(m => m.root.position).SequenceEqual(positions) &&
                    herd.members.Select(m => m.DistanceTravelled).SequenceEqual(distances), "Paused simulation advanced its clock or moved an animal.");
                herd.SetPaused(false);
                Advance(herd, 20);
                Require(!herd.Paused && herd.SimulationTime > time && !herd.members.Select(m => m.root.position).SequenceEqual(positions),
                    "Resuming did not continue the existing journey.");
                results.Add("PASS pause freezes simulation and positions; resume continues the same journey");

                AdvanceUntil(herd, () => herd.members.Any(m => m.Mode == JourneyTravelMode.Swim), "No animal entered the water for the restart check.");
                JourneyMember swimming = herd.members.First(m => m.Mode == JourneyTravelMode.Swim);
                Vector3 basePosition = swimming.visualRoot.localPosition;
                Quaternion baseRotation = swimming.visualRoot.localRotation;
                herd.ApplyBodyLanguage();
                Require(swimming.visualRoot.localRotation != baseRotation, "Swimming overlay was not exercised before reinitialization.");
                herd.Initialize();
                Require(swimming.visualRoot.localPosition == basePosition && swimming.visualRoot.localRotation == baseRotation,
                    "Reinitializing captured the temporary swimming pose as the permanent model offset.");
                herd.Initialize();
                Require(swimming.visualRoot.localPosition == basePosition && swimming.visualRoot.localRotation == baseRotation,
                    "Repeated initialization accumulated a body-language offset.");
                results.Add("PASS restarting restores the original visual pose without accumulating a swimming offset");
            }
            using (var fixture = new Fixture())
            {
                JourneyHerd herd = fixture.herd;
                Require(herd.BeginJourney(1), "Bridge safety fixture could not start.");
                AdvanceUntil(herd, () => herd.members.Any(m => m.Mode == JourneyTravelMode.Bridge), "No sheep entered the bridge for the reversal check.");
                Vector3[] before = herd.members.Select(m => m.root.position).ToArray();
                Require(!herd.BeginJourney(0) && herd.TargetSite == 1 && herd.Travelling &&
                    herd.members.Select(m => m.root.position).SequenceEqual(before), "Changing destination reversed or reset a member already on a narrow bridge.");
                Advance(herd, 3000);
                RequireArrived(herd, 1);
                results.Add("PASS bridge crossing rejects a mid-crossing destination reversal and still reunites");
            }
            using (var fixture = new Fixture())
            {
                StartSlowBridgeQueue(fixture);
                JourneyHerd herd = fixture.herd;
                AdvanceUntil(herd, () => herd.members.Any(m => m.State == JourneyMemberState.WaitingForBridge),
                    "A fast bridge follower never waited behind the deliberately slow leader.");
                Advance(herd, 3000);
                RequireArrived(herd, 1);
                results.Add("PASS a faster bridge follower waits behind a slow leader and all queued companions eventually reunite");
            }
            using (var fixture = new Fixture())
            {
                JourneyHerd herd = fixture.herd;
                StartSlowBridgeQueue(fixture);
                AdvanceUntil(herd, () => herd.members.Any(m => m.State == JourneyMemberState.WaitingForBridge),
                    "No queued sheep was available for the inactive reservation check.");
                JourneyMember queued = herd.members.First(m => m.State == JourneyMemberState.WaitingForBridge);
                queued.root.gameObject.SetActive(false);
                Advance(herd, 3000);
                Require(!herd.Travelling && herd.TargetSite == 1 && herd.CompletedJourneys == 1 &&
                    herd.members.Where(m => m.root.gameObject.activeInHierarchy).All(m => m.Arrived),
                    "An inactive queued member permanently held a bridge reservation or blocked the remaining herd's reunion.");
                results.Add("PASS deactivating a queued sheep releases its reservation and lets active companions reunite");
            }
            using (var fixture = new Fixture())
            {
                JourneyHerd herd = fixture.herd;
                StartSlowBridgeQueue(fixture);
                for (int i = 1; i < herd.members.Length; i++) herd.members[i].profile.walkSpeed = .4f;
                FieldInfo bridge = typeof(JourneyMember).GetField("bridge", BindingFlags.Instance | BindingFlags.NonPublic);
                Require(bridge != null, "The actual on-shore bridge reservation could not be inspected.");
                Func<JourneyMember> reservedOnShore = () => herd.members.FirstOrDefault(m => !m.Arrived &&
                    m.Mode != JourneyTravelMode.Bridge && (int)bridge.GetValue(m) > 0 &&
                    herd.navigation.SampleSurface(m.root.position).bridgeId == 0);
                AdvanceUntil(herd, () => reservedOnShore() != null, "No bridge reservation was observed before its holder stepped onto the deck.");
                JourneyMember returning = reservedOnShore();
                Vector3 waitingPosition = returning.root.position;
                returning.root.gameObject.SetActive(false);
                Advance(herd, 4);
                Require(returning.root.position == waitingPosition && herd.Travelling && herd.TargetSite == 1,
                    "Deactivating the on-shore reservation holder moved it or reset the current journey.");
                returning.root.gameObject.SetActive(true);
                TravelEvidence resumed = Advance(herd, 3000);
                Require(resumed.bridgeWalkers.Contains(returning.id), "The re-enabled reservation holder never actually crossed the bridge.");
                RequireArrived(herd, 1);
                Require(herd.CompletedJourneys == 1, "Re-enabling an on-shore reservation holder restarted the group's journey.");
                results.Add("PASS an on-shore reservation holder can deactivate, clear for 0.4 seconds, re-enable and cross before full reunion");
            }
            return string.Join("\n", results);
        }

        private static void CheckBridgeSideEntranceAdmission()
        {
            FieldInfo routeField = typeof(JourneyMember).GetField("route", BindingFlags.Instance | BindingFlags.NonPublic);
            FieldInfo segmentField = typeof(JourneyMember).GetField("segment", BindingFlags.Instance | BindingFlags.NonPublic);
            MethodInfo admit = typeof(JourneyHerd).GetMethod("AdmitBridge", BindingFlags.Instance | BindingFlags.NonPublic);
            Require(routeField != null && segmentField != null && admit != null, "The controlled bridge admission fixture could not bind its runtime entrypoint.");
            foreach (bool opposing in new[] { false, true })
                using (var fixture = new Fixture())
                {
                    JourneyHerd herd = fixture.herd;
                    herd.members = herd.members.Take(2).ToArray();
                    herd.Initialize();
                    JourneyRoute leading = SideEntranceRoute(false, -1), following = SideEntranceRoute(opposing, opposing ? -1 : 1);
                    routeField.SetValue(herd.members[0], leading); segmentField.SetValue(herd.members[0], 0);
                    routeField.SetValue(herd.members[1], following); segmentField.SetValue(herd.members[1], 0);
                    Require((bool)admit.Invoke(herd, new object[] { herd.members[0], leading.segments[0] }),
                        "The first bridge entrant could not reserve an empty bridge.");
                    // Let the normal admission interval expire while keeping the first live occupant's reservation.
                    Advance(herd, 30);
                    bool allowed = (bool)admit.Invoke(herd, new object[] { herd.members[1], following.segments[0] });
                    Require(allowed != opposing, opposing
                        ? "An opposing crossing was admitted because its short lateral entrance pointed the same way."
                        : "A same-direction crossing was refused because its short lateral entrance pointed the other way.");
                }
        }

        private static JourneyRoute SideEntranceRoute(bool fromEast, float side)
        {
            float entrance = fromEast ? 1 : -1;
            Vector3[] points = {
                new Vector3(entrance * 4.75f, JourneyLabBuilder.BridgeHeight, JourneyLabBuilder.BridgeZ + side * .6f),
                new Vector3(entrance * 4.5f, JourneyLabBuilder.BridgeHeight, JourneyLabBuilder.BridgeZ),
                new Vector3(-entrance * 4.5f, JourneyLabBuilder.BridgeHeight, JourneyLabBuilder.BridgeZ),
                new Vector3(-entrance * 4.75f, JourneyLabBuilder.BridgeHeight, JourneyLabBuilder.BridgeZ - side * .6f)
            };
            var route = new JourneyRoute();
            for (int i = 0; i < points.Length - 1; i++)
                route.segments.Add(new JourneySegment { from = points[i], to = points[i + 1],
                    mode = JourneyTravelMode.Bridge, bridgeId = 1, linkIndex = i });
            return route;
        }

        private static void CheckBridgeApproachVariation()
        {
            using (var fixture = new Fixture())
            {
                JourneyNavigation nav = fixture.herd.navigation;
                nav.endpointConnectionRadius = .4f;
                nav.surfaceSampler = p => new JourneySurface { valid = true, groundY = 0,
                    bridgeId = p.x >= -4 && p.x <= 4 && Mathf.Abs(p.z) <= .6f ? 1 : 0 };
                float[] x = { -20, -14, -6, -4, 4, 10 };
                nav.nodes = x.Select((position, i) => new JourneyNode { id = "Approach " + i, position = new Vector3(position, 0, 0) }).ToArray();
                nav.links = Enumerable.Range(0, x.Length - 1).Select(i => new JourneyLink {
                    from = i, to = i + 1, bridgeId = i == 3 ? 1 : 0 }).ToArray();
                nav.Rebuild();
                var profile = new JourneyProfile { canSwim = false, bodyRadius = .25f };
                Type variation = typeof(JourneyHerd).Assembly.GetType("WeatherHouse.WildlifeJourney.JourneyRouteVariation");
                MethodInfo apply = variation?.GetMethod("Apply", BindingFlags.Static | BindingFlags.Public);
                Require(apply != null, "The route variation entrypoint could not be inspected from the Editor assembly.");
                foreach (float lateral in new[] { -.9f, .9f })
                {
                    JourneyRoute route = nav.FindRoute(nav.nodes[0].position, nav.nodes[5].position, profile);
                    Require(route != null, "The open-ground and bridge-approach fixture has no traversable route.");
                    JourneySegment[] original = route.segments.ToArray();
                    Require(original.Count(s => s.linkIndex == 0) >= 4 && original.Count(s => s.linkIndex == 1) >= 8 &&
                        original.Any(s => s.bridgeId == 1), "The fixture did not include the full open-ground, approach and bridge sections.");
                    // The implementation is internal to the runtime assembly; compare only its observable route output.
                    apply.Invoke(null, new object[] { nav, profile, route, lateral, 0f });
                    Require(route.segments.Count == original.Length, "Individual route variation changed the route's segment count.");
                    float openOffset = 0;
                    for (int i = 0; i < original.Length; i++)
                    {
                        JourneySegment before = original[i], after = route.segments[i];
                        if (before.linkIndex == 1 || before.linkIndex == 2 || before.bridgeId == 1)
                            Require((after.from - before.from).sqrMagnitude < .000001f && (after.to - before.to).sqrMagnitude < .000001f,
                                "An individual offset bent the bridge approach or deck away from its shared center line.");
                        if (before.linkIndex == 0) openOffset = Mathf.Max(openOffset, Mathf.Abs(after.to.z - before.to.z));
                    }
                    Require(openOffset > .3f, "Keeping the bridge approach centered also erased individual movement on open ground.");
                }
            }
        }

        private static void CheckHiddenWaterVariation()
        {
            using (var fixture = new Fixture())
            {
                JourneyNavigation nav = fixture.herd.navigation;
                nav.surfaceSampler = p => new JourneySurface { valid = true };
                nav.sampleSpacing = .3f;
                var profile = new JourneyProfile { bodyRadius = .3f, canSwim = true, wadeDepth = .28f, maxSwimDistance = .05f };
                Type variation = typeof(JourneyHerd).Assembly.GetType("WeatherHouse.WildlifeJourney.JourneyRouteVariation");
                MethodInfo apply = variation?.GetMethod("Apply", BindingFlags.Static | BindingFlags.Public);
                Require(apply != null, "The hidden-water fixture cannot inspect the route variation entrypoint.");
                Vector3 start = Vector3.zero, goal = new Vector3(3, 0, 0);
                JourneyRoute dryOffset = nav.TraceDirectRoute(start, goal, profile);
                Require(dryOffset != null, "The hidden-water fixture has no initial dry route.");
                apply.Invoke(null, new object[] { nav, profile, dryOffset, -1f, 0f });
                Require(dryOffset.segments.Max(s => s.to.z) > .5f, "The fixture did not exercise an actual lateral offset.");
                JourneySegment crossing = dryOffset.segments.First(s => s.from.x < .7f && s.to.x > .8f);

                nav.surfaceSampler = p => {
                    bool wet = p.x > .7f && p.x < .8f && p.z > .49f;
                    return new JourneySurface { valid = true, hasWater = wet, waterY = 0, groundY = wet ? -.35f : 0 };
                };
                Require(nav.TryProject(profile, crossing.from, out _, out JourneyTravelMode fromMode) && fromMode == JourneyTravelMode.Walk &&
                    nav.TryStep(profile, crossing.from, crossing.to, out _, out JourneyTravelMode endMode) && endMode == JourneyTravelMode.Walk &&
                    nav.TryProject(profile, Vector3.Lerp(crossing.from, crossing.to, .5f), out _, out JourneyTravelMode middleMode) && middleMode == JourneyTravelMode.Swim &&
                    nav.TraceDirectRoute(crossing.from, crossing.to, profile) == null,
                    "The regression must have legal walking endpoints but an intervening swim beyond the .05m allowance.");

                JourneyRoute rejected = nav.TraceDirectRoute(start, goal, profile);
                Require(rejected != null, "Adding water away from the original route invalidated its dry baseline.");
                JourneySegment[] original = rejected.segments.ToArray();
                float originalCost = rejected.totalCost;
                apply.Invoke(null, new object[] { nav, profile, rejected, -1f, 0f });
                Require(rejected.segments.Count == original.Length && Mathf.Abs(rejected.totalCost - originalCost) < .0001f &&
                    rejected.segments.Select((s, i) => (s.from - original[i].from).sqrMagnitude < .000001f &&
                        (s.to - original[i].to).sqrMagnitude < .000001f && s.mode == original[i].mode &&
                        s.surfaceBoundary == original[i].surfaceBoundary).All(unchanged => unchanged),
                    "A lateral variation accepted hidden Walk-Swim-Walk travel or partly committed an unsafe route.");

                JourneyRoute safe = nav.TraceDirectRoute(start, goal, profile);
                apply.Invoke(null, new object[] { nav, profile, safe, 1f, 0f });
                Require(safe.segments.Min(s => s.to.z) < -.5f && safe.segments.All(s =>
                    nav.TryStep(profile, s.from, s.to, out _, out _, JourneyTravelMode.Walk)),
                    "Rejecting the hidden-water variation also suppressed a safe offset on the opposite side.");
            }
        }

        private static void StartSlowBridgeQueue(Fixture fixture)
        {
            JourneyHerd herd = fixture.herd;
            herd.members = herd.members.Take(3).ToArray();
            for (int i = 0; i < herd.members.Length; i++)
            {
                JourneyMember member = herd.members[i];
                member.root.position = JourneyLabBuilder.Position(-4.85f - i * 1.65f, JourneyLabBuilder.BridgeZ);
                member.profile.canSwim = false;
                member.profile.walkSpeed = i == 0 ? .3f : .1f;
            }
            herd.Initialize();
            Require(herd.BeginJourney(1), "The controlled slow-leader bridge fixture could not start.");
            AdvanceUntil(herd, () => herd.members[0].Mode == JourneyTravelMode.Bridge,
                "The slow leader never entered the bridge before its followers accelerated.");
            for (int i = 1; i < herd.members.Length; i++) herd.members[i].profile.walkSpeed = 1.2f;
        }

        public static string RunSceneChecks(JourneyHerd herd)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Run saved-scene checks outside Play mode.");
            Require(herd && herd.navigation, "The saved lab has no connected herd/navigation.");
            Require(!EditorSceneManager.IsPreviewScene(herd.gameObject.scene), "Collider checks require a normal additive scene.");
            Require(herd.members.Length == 10 && herd.members.Count(m => m.profile.canSwim) == 4,
                "The saved lab must contain ten sheep, including four swimmers.");
            Require(herd.GetComponentsInChildren<WildlifeAgent>().Length == 0, "Old WildlifeAgent still competes with the new herd controller.");
            Require(herd.members.All(m => m.animator && m.visualRoot && m.headBone && m.paddleBones.Length == 4 &&
                m.clips != null && m.clips.idle && m.clips.walk && m.clips.forage && m.swimClip), "The saved sheep lost real animation/rig bindings or its authored swimming clip.");
            Require(herd.members.Select(m => m.visualRoot.localScale).Distinct().Count() > 5, "Saved sheep sizes are synchronized.");
            string bodySizes = DescribeBodySizes(herd);
            Physics.SyncTransforms();
            JourneyNavigation nav = herd.navigation;
            JourneySurface deck = nav.SampleSurface(new Vector3(0, 0, JourneyLabBuilder.BridgeZ));
            Require(deck.valid && deck.bridgeId == 1 && Mathf.Abs(deck.groundY - JourneyLabBuilder.BridgeHeight) < .02f,
                "Bridge navigation sampled the river bed instead of the real deck collider.");
            JourneySurface river = nav.SampleSurface(Vector3.zero);
            Require(river.valid && river.hasWater && river.bridgeId == 0 && river.groundY < -.8f,
                "The river lacks actual depth or the water plane behaves like solid ground.");
            JourneyMember swimmer = herd.members.First(m => m.profile.canSwim);
            JourneyRoute crossing = nav.FindRoute(JourneyLabBuilder.Position(-4.8f, 0), JourneyLabBuilder.Position(4.8f, 0), swimmer.profile);
            Require(crossing != null && crossing.segments.Any(s => s.mode == JourneyTravelMode.Swim), "Real colliders rejected the intended shallow-bank crossing.");
            JourneyLink[] links = nav.links;
            try
            {
                nav.links = new[] { new JourneyLink { from = 8, to = 9, bidirectional = true } };
                Require(nav.FindRoute(JourneyLabBuilder.Position(-4.8f, -10), JourneyLabBuilder.Position(4.8f, -10), swimmer.profile) == null,
                    "The real steep riverbank accepted an unsafe swimming exit.");
            }
            finally { nav.links = links; }
            herd.autoAdvance = false;
            herd.Initialize();
            Require(herd.BeginJourney(1), "Saved scene could not start a mixed-ability journey through its actual geometry.");
            TravelEvidence evidence = Advance(herd, 3000);
            RequireArrived(herd, 1);
            Require(evidence.swimmers.Count == 4 && evidence.bridgeWalkers.Count >= 6,
                "Saved-scene geometry did not produce both swimming and bridge routes.");
            Require(evidence.measuredWaterlines.Count == 4 && evidence.minimumHeadClearance > .03f,
                "The real swimming rigs did not keep their heads above the water.");
            return "PASS saved scene: ten independent sheep, real rig bindings, real deck and river colliders, unsafe-bank rejection and 300-second whole-herd reunion\n" + bodySizes +
                "\nINFO minimum measured swimming head clearance=" + evidence.minimumHeadClearance.ToString("F3", CultureInfo.InvariantCulture) + " m (saved rig pose; animated pose also needs Play review)";
        }

        public static string DescribeBodySizes(JourneyHerd herd)
        {
            var lines = new List<string>();
            foreach (JourneyMember member in herd.members)
            {
                Bounds bounds = RenderedBounds(member);
                lines.Add(string.Format(CultureInfo.InvariantCulture,
                    "INFO sheep {0:00} rendered metres width={1:F3} height={2:F3} length={3:F3} footOffset={4:F3}; configured bodyLength={5:F3} width={6:F3} waterline={7:F3}",
                    member.id, bounds.size.x, bounds.size.y, bounds.size.z, bounds.min.y, member.bodyLength, member.profile.bodyRadius * 2, member.profile.waterlineHeight));
            }
            return string.Join("\n", lines);
        }

        private static Bounds RenderedBounds(JourneyMember member)
        {
            bool measured = false;
            Bounds bounds = default;
            Quaternion unrotate = Quaternion.Inverse(member.root.rotation);
            foreach (Renderer renderer in member.root.GetComponentsInChildren<Renderer>())
            {
                if (!renderer.enabled) continue;
                Mesh baked = null;
                try
                {
                    Mesh mesh;
                    if (renderer is SkinnedMeshRenderer skin)
                    // The imported FBX renderer carries scale 100; true yields local vertices for one TransformPoint.
                    { baked = new Mesh(); skin.BakeMesh(baked, true); mesh = baked; }
                    else
                    { MeshFilter filter = renderer.GetComponent<MeshFilter>(); mesh = filter ? filter.sharedMesh : null; }
                    if (!mesh) continue;
                    foreach (Vector3 vertex in mesh.vertices)
                    {
                        // Remove heading only, so dimensions retain world metres without inflating a world-axis AABB.
                        Vector3 aligned = unrotate * (renderer.transform.TransformPoint(vertex) - member.root.position);
                        if (!measured) { bounds = new Bounds(aligned, Vector3.zero); measured = true; }
                        else bounds.Encapsulate(aligned);
                    }
                }
                finally { if (baked) Object.DestroyImmediate(baked); }
            }
            Require(measured && bounds.size.sqrMagnitude > .01f, "No visible sheep mesh could be measured for member " + member.id);
            Require(bounds.size.x >= .1f && bounds.size.x <= 5 && bounds.size.y >= .1f && bounds.size.y <= 5 &&
                bounds.size.z >= .1f && bounds.size.z <= 5, "Sheep mesh dimensions are outside the plausible 0.1..5 metre range: " + bounds.size);
            return bounds;
        }

        private sealed class TravelEvidence
        {
            public readonly HashSet<int> swimmers = new HashSet<int>(), bridgeWalkers = new HashSet<int>();
            public readonly Dictionary<int, int> firstMovement = new Dictionary<int, int>();
            public readonly HashSet<int> measuredWaterlines = new HashSet<int>();
            public bool waited, earlyArrival;
            public int pairedSwimmingTicks, maximumBridgeOccupancy;
            public float closestWetGap = float.PositiveInfinity;
            public float minimumHeadClearance = float.PositiveInfinity;
        }

        private static TravelEvidence Advance(JourneyHerd herd, int ticks, bool checkSwimSpacing = false)
        {
            var evidence = new TravelEvidence();
            var closeTicks = checkSwimSpacing ? new int[herd.members.Length, herd.members.Length] : null;
            for (int tick = 0; tick < ticks; tick++)
            {
                herd.Simulate(.1f);
                evidence.maximumBridgeOccupancy = Mathf.Max(evidence.maximumBridgeOccupancy,
                    herd.members.Count(m => m.root.gameObject.activeInHierarchy && m.Mode == JourneyTravelMode.Bridge));
                foreach (JourneyMember m in herd.members)
                {
                    Require(!float.IsNaN(m.root.position.sqrMagnitude) && !float.IsInfinity(m.root.position.sqrMagnitude), "An animal position became non-finite.");
                    if (m.ActualSpeed > .02f && !evidence.firstMovement.ContainsKey(m.id)) evidence.firstMovement.Add(m.id, tick);
                    evidence.waited |= m.State == JourneyMemberState.WaitingForBridge;
                    evidence.earlyArrival |= herd.Travelling && m.Arrived;
                    if (m.Mode == JourneyTravelMode.Swim)
                    {
                        evidence.swimmers.Add(m.id);
                        JourneySurface surface = herd.navigation.SampleSurface(m.root.position);
                        float floating = m.profile.waterlineHeight >= 0 ? surface.waterY - m.profile.waterlineHeight : surface.waterY + herd.navigation.floatOffset;
                        Require(m.profile.canSwim && surface.hasWater && m.root.position.y >= surface.groundY - .02f &&
                            Mathf.Abs(m.root.position.y - Mathf.Max(surface.groundY, floating)) < .025f,
                            "A swimming sheep sank below its support or left its water-relative floating height.");
                        if (m.headBone)
                        {
                            float headClearance = m.headBone.position.y - surface.waterY;
                            evidence.minimumHeadClearance = Mathf.Min(evidence.minimumHeadClearance, headClearance);
                            Require(headClearance > .03f, "A real sheep head bone went below the waterline.");
                            if (evidence.measuredWaterlines.Add(m.id))
                            {
                                Bounds body = RenderedBounds(m);
                                Require(m.root.position.y + body.min.y < surface.waterY - .03f && m.root.position.y + body.max.y > surface.waterY + .1f,
                                    "The actual rendered sheep was not partly submerged with its upper body above water.");
                            }
                        }
                    }
                    if (m.Mode == JourneyTravelMode.Bridge)
                    {
                        evidence.bridgeWalkers.Add(m.id);
                        Require(Mathf.Abs(m.root.position.z - JourneyLabBuilder.BridgeZ) < JourneyLabBuilder.BridgeWidth * .5f &&
                            Mathf.Abs(m.root.position.y - JourneyLabBuilder.BridgeHeight) < .025f,
                            "A sheep left the narrow bridge deck while crossing.");
                    }
                }
                if (checkSwimSpacing)
                    for (int i = 0; i < herd.members.Length; i++)
                        for (int j = i + 1; j < herd.members.Length; j++)
                        {
                            JourneyMember a = herd.members[i], b = herd.members[j];
                            if (a.Mode != JourneyTravelMode.Swim || b.Mode != JourneyTravelMode.Swim)
                            { closeTicks[i, j] = 0; continue; }
                            evidence.pairedSwimmingTicks++;
                            float gap = Vector2.Distance(new Vector2(a.root.position.x, a.root.position.z), new Vector2(b.root.position.x, b.root.position.z));
                            evidence.closestWetGap = Mathf.Min(evidence.closestWetGap, gap);
                            float minimumGap = a.profile.bodyRadius + b.profile.bodyRadius + .05f;
                            closeTicks[i, j] = gap < minimumGap ? closeTicks[i, j] + 1 : 0;
                            Require(closeTicks[i, j] < 3, "Swimming companions " + a.id + " and " + b.id +
                                " persistently overlap: centre gap=" + gap.ToString("F3", CultureInfo.InvariantCulture));
                        }
            }
            return evidence;
        }

        private static void RequireArrived(JourneyHerd herd, int site)
        {
            string state = string.Join("; ", herd.members.Select(m => m.id + " " + m.State + " @ " + m.root.position + " " + m.LastDecision));
            Require(!herd.Travelling && herd.TargetSite == site && herd.members.All(m => m.Arrived), "The herd failed to reunite: " + state);
            Require(herd.members.All(m => Vector2.Distance(new Vector2(m.root.position.x, m.root.position.z),
                new Vector2(herd.sites[site].center.x, herd.sites[site].center.z)) <= herd.sites[site].radius + .6f),
                "An arrived sheep wandered away from the rendezvous: " + state);
        }

        private static void AdvanceUntil(JourneyHerd herd, Func<bool> reached, string failure)
        {
            for (int i = 0; i < 1200 && !reached(); i++) herd.Simulate(.1f);
            Require(reached(), failure);
        }

        private sealed class Fixture : IDisposable
        {
            public readonly JourneyHerd herd;
            public bool bridgeClosed, landClosed;
            private readonly Scene scene;
            private readonly GameObject root;

            public Fixture()
            {
                scene = EditorSceneManager.NewPreviewScene();
                root = new GameObject("Isolated herd fixture") { hideFlags = HideFlags.HideAndDontSave };
                SceneManager.MoveGameObjectToScene(root, scene);
                var nav = root.AddComponent<JourneyNavigation>();
                JourneyLabBuilder.ConfigureNavigation(nav);
                nav.surfaceSampler = Surface;
                herd = root.AddComponent<JourneyHerd>();
                herd.navigation = nav; herd.sites = JourneyLabBuilder.ActivitySites(); herd.autoAdvance = false;
                herd.members = new JourneyMember[10];
                for (int i = 0; i < herd.members.Length; i++)
                {
                    var animal = new GameObject("Sheep fixture " + (i + 1));
                    SceneManager.MoveGameObjectToScene(animal, scene);
                    animal.transform.SetParent(root.transform, false);
                    animal.transform.position = JourneyLabBuilder.Position(-13 + (i % 4) * 1.25f, -1.5f + (i / 4) * 1.5f);
                    var visual = new GameObject("Visual fixture");
                    SceneManager.MoveGameObjectToScene(visual, scene);
                    visual.transform.SetParent(animal.transform, false);
                    visual.transform.localPosition = new Vector3(.03f, .08f, -.04f);
                    visual.transform.localRotation = Quaternion.Euler(0, 5, 0);
                    herd.members[i] = new JourneyMember { id = i + 1, root = animal.transform, visualRoot = visual.transform, sociability = 0, bodyLength = 1.1f,
                        profile = new JourneyProfile { bodyRadius = .25f, canSwim = i < 4, maxSwimDistance = 8,
                            wadeDepth = .28f, walkSpeed = .9f + i * .012f, swimSpeed = .8f, waterAversion = .1f } };
                }
                herd.Initialize();
            }

            private JourneySurface Surface(Vector3 p)
            {
                bool bridge = Mathf.Abs(p.x) <= JourneyLabBuilder.BridgeHalfLength &&
                    Mathf.Abs(p.z - JourneyLabBuilder.BridgeZ) <= JourneyLabBuilder.BridgeWidth * .5f;
                return new JourneySurface { valid = Mathf.Abs(p.x) <= 20 && Mathf.Abs(p.z) <= 16,
                    groundY = bridge ? JourneyLabBuilder.BridgeHeight : JourneyLabBuilder.GroundHeight(p.x, p.z),
                    hasWater = Mathf.Abs(p.x) <= 4.2f, waterY = 0, bridgeId = bridge ? 1 : 0,
                    blocked = (bridgeClosed && p.x > -.25f && p.x < .55f && Mathf.Abs(p.z - JourneyLabBuilder.BridgeZ) < .9f) ||
                        (landClosed && p.x > -8.1f && p.x < -7.3f && Mathf.Abs(p.z) < 1.2f) };
            }

            public void Dispose()
            {
                if (root) Object.DestroyImmediate(root);
                if (scene.IsValid()) EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        private static void Require(bool condition, string message)
        { if (!condition) throw new InvalidOperationException(message); }
    }
}
