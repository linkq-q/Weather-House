using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using WeatherHouse.WildlifeJourney;
using WeatherHousePrototype;
using Object = UnityEngine.Object;

namespace WeatherHouse.Review
{
    // Observation only. The existing review owns Play, the saved scene and notebook restoration.
    public static class JourneyHerdMainProbe
    {
        [Serializable] public sealed class MemberResult
        {
            public int id, routeReplacements, bridgeFrames, swimFrames, movingFrames;
            public Vector3 start, committedGoal, finalGoal, arrivalPosition, finalPosition, bodyCenter, bodyHalf;
            public float arrivalTime = -1, distanceTravelled, originalWalkSpeed, bodyLength;
        }
        [Serializable] public sealed class Report
        {
            public string result = "RUNNING", error = "", configuration;
            public string evidence = "Actual main-map Play, first natural journey only. No initialization, forced plan, simulation calls, parameter changes, actor/obstacle movement, camera changes or synthetic input. Full measured-body world/deck sweeps are rechecked for observed frame-to-frame root motion; this is not animated-mesh contact or visual acceptance. Existing RuralMainPlayReview restores notebook and scene lifecycle.";
            public bool restored, configurationUnchanged, departed, completedOriginalJourney;
            public int frames, physicsChecks, physicsFailures, startSite, targetSite = -1, initialCompleted, finalCompleted;
            public int planningRequests, planningCompletions, planningCancellations, routeChanges, navigationVersion;
            public float seconds, simulationStart, simulationTime, departureTime = -1, completionTime = -1;
            public Vector3 siteCenter; public float siteRadius;
            public JourneyActivitySite[] sites;
            public List<MemberResult> members = new List<MemberResult>();
        }
        [Serializable] sealed class MemberFrame
        {
            public int id, segment, bridge, bodyStepPhase; public string state, mode, decision;
            public bool arrived, hasRoute; public Vector3 position, goal, next; public Quaternion rotation;
            public float distanceTravelled, speed, swimming;
        }
        [Serializable] sealed class Frame
        {
            public int frame, targetSite, completed, requests, completions, cancellations, routeChanges;
            public float simulationTime, seconds; public bool travelling, planning;
            public string decision; public MemberFrame[] members;
        }
        [Serializable] sealed class Event
        {
            public string kind, detail; public int id, frame; public float simulationTime;
            public Vector3 position, goal, previousPosition; public Quaternion rotation, previousRotation;
            public JourneySegment[] route;
        }
        [Serializable] sealed class PlanSnapshot
        {
            public string kind, iteratorType, operationType, searchStatus, stageEvidence;
            public int frame, site, member, iteratorState, firstMissingRouteMember = -1;
            public float simulationTime, capturedTime;
            public bool success, finished, recovery;
            public string[] locals; public Vector3[] scatteredGoals, destinations;
            public int[] routeSegments;
        }
        const float SimulationLimit = 1050f, WallLimit = 1150f;
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        static readonly FieldInfo Destination = typeof(JourneyMember).GetField("destination", Private);
        static readonly FieldInfo Route = typeof(JourneyMember).GetField("route", Private);
        static readonly FieldInfo Segment = typeof(JourneyMember).GetField("segment", Private);
        static readonly FieldInfo Bridge = typeof(JourneyMember).GetField("bridge", Private);
        static readonly FieldInfo BodyPhase = typeof(JourneyMember).GetField("bodyStepPhase", Private);
        static readonly FieldInfo Pending = typeof(JourneyHerd).GetField("pending", Private), PlanningSteps = typeof(JourneyHerd).GetField("planningSteps", Private);
        static JourneyHerd herd; static JourneyMember[] members; static JourneyNavigation navigation;
        static GameplaySession session; static Report report; static string directory;
        static Vector3[] previousPositions, lastGoals; static Quaternion[] previousRotations;
        static JourneyRoute[] lastRoutes; static string[] lastModes;
        static float started, nextSample, timeScale; static int lastFrame; static bool running;
        static StreamWriter trajectory, events;
        static StreamWriter plans; static object observedPlan, observedSteps; static string lastPlanStage;
        public static bool IsComplete { get; private set; }
        public static string Failure => IsComplete && report != null && report.result != "COMPLETE" ? report.error : null;

        public static string Begin(string folder)
        {
            if (running || !SessionState.GetBool("WeatherHouse.RuralMainPlay.active", false) || !EditorApplication.isPlaying || EditorApplication.isPaused)
                throw new InvalidOperationException("Requires protected unpaused RuralMainPlayReview Play.");
            session = Object.FindObjectOfType<GameplaySession>();
            herd = Object.FindObjectsOfType<JourneyHerd>().SingleOrDefault(h => h.isActiveAndEnabled);
            if (!session || !session.forestSprite || !session.forestSprite.enabled || session.forestSprite.ScriptedInput ||
                !herd || !herd.navigation || herd.Paused || !herd.autoAdvance || herd.Travelling || herd.CompletedJourneys != 0 ||
                herd.members.Length != 10 || herd.members.Any(m => m == null || !m.root || !m.root.gameObject.activeInHierarchy || !m.profile.bodyEnvelope.Valid) ||
                herd.members.Select(m => m.id).Distinct().Count() != 10)
                throw new InvalidOperationException("Need the unchanged original player and ten initialized live sheep before their first natural departure.");
            if (new[] { Destination, Route, Segment, Bridge, BodyPhase, Pending, PlanningSteps }.Any(f => f == null))
                throw new InvalidOperationException("Read-only diagnostic fields changed.");
            navigation = herd.navigation; members = (JourneyMember[])herd.members.Clone(); directory = Path.GetFullPath(folder);
            Directory.CreateDirectory(directory); started = Time.realtimeSinceStartup; timeScale = Time.timeScale;
            report = new Report { configuration = Configuration(), startSite = herd.TargetSite, initialCompleted = herd.CompletedJourneys,
                navigationVersion = navigation.Version, simulationStart = herd.SimulationTime,
                sites = JsonUtility.FromJson<SiteSnapshot>(JsonUtility.ToJson(new SiteSnapshot { sites = herd.sites })).sites };
            previousPositions = members.Select(m => m.root.position).ToArray(); previousRotations = members.Select(m => m.root.rotation).ToArray();
            lastGoals = members.Select(Goal).ToArray(); lastRoutes = new JourneyRoute[10]; lastModes = new string[10];
            foreach (JourneyMember m in members) report.members.Add(new MemberResult { id = m.id, start = m.root.position, originalWalkSpeed = m.profile.walkSpeed,
                bodyCenter = m.profile.bodyEnvelope.center, bodyHalf = m.profile.bodyEnvelope.half, bodyLength = m.bodyLength });
            nextSample = herd.SimulationTime; lastFrame = -1; IsComplete = false; observedPlan = observedSteps = null; lastPlanStage = null; running = true;
            try
            {
                trajectory = new StreamWriter(Path.Combine(directory, "trajectory.jsonl")); events = new StreamWriter(Path.Combine(directory, "events.jsonl"));
                plans = new StreamWriter(Path.Combine(directory, "planning.jsonl"));
                EditorApplication.update += Tick; RenderPipelineManager.endFrameRendering += Rendered;
                File.WriteAllText(Path.Combine(directory, "planned.json"), JsonUtility.ToJson(report, true));
            }
            catch (Exception e) { report.error = e.ToString(); Finish("FAILED_TO_START"); throw; }
            return directory;
        }
        [Serializable] sealed class SiteSnapshot { public JourneyActivitySite[] sites; }
        static string Configuration() => JsonUtility.ToJson(herd) + "\n" + JsonUtility.ToJson(navigation);
        static Vector3 Goal(JourneyMember m) => (Vector3)Destination.GetValue(m);
        static float Flat(Vector3 a, Vector3 b) { a.y = b.y = 0; return Vector3.Distance(a, b); }
        static bool ValidArrival(Vector3 position, Vector3 goal, Vector3 center, float radius, bool arrived, JourneyTravelMode mode)
            => arrived && mode == JourneyTravelMode.Walk && Flat(position, goal) <= .18001f && Flat(goal, center) <= radius + .00001f;
        static bool CompleteContract(bool departed, bool sameSite, bool allArrived, bool allRecorded, int completed, int initial)
            => departed && sameSite && allArrived && allRecorded && completed == initial + 1;

        static void Tick()
        {
            if (!running) return;
            try
            {
                report.seconds = Time.realtimeSinceStartup - started;
                if (!EditorApplication.isPlaying) { Fail("Play exited before completion."); return; }
                if (report.seconds >= WallLimit) { Fail("Exceeded 1150 wall seconds; stopping before the protected lifecycle's 1200-second limit."); return; }
                if (!herd || !herd.isActiveAndEnabled || herd.Paused || !session || !session.forestSprite ||
                    !session.forestSprite.enabled || session.forestSprite.ScriptedInput || Time.timeScale != timeScale)
                    Fail("Observation ownership, active herd/player or time scale changed.");
            }
            catch (Exception e) { Fail(e.ToString()); }
        }
        static void Rendered(ScriptableRenderContext context, Camera[] cameras)
        {
            if (!running || EditorApplication.isPaused || lastFrame == Time.frameCount) return;
            lastFrame = Time.frameCount;
            try { Sample(); } catch (Exception e) { Fail(e.ToString()); }
        }
        static void Sample()
        {
            report.frames++; report.simulationTime = herd.SimulationTime; report.seconds = Time.realtimeSinceStartup - started;
            SamplePlanning();
            if (herd.members.Length != 10 || !members.SequenceEqual(herd.members) || navigation != herd.navigation || navigation.Version != report.navigationVersion ||
                members.Any(m => !m.root || !m.root.gameObject.activeInHierarchy))
                throw new InvalidOperationException("Membership, navigation/version or active roots changed during first-journey observation.");
            if (!report.departed && herd.Travelling)
            {
                report.departed = true; report.departureTime = herd.SimulationTime; report.targetSite = herd.TargetSite;
                if (report.targetSite == report.startSite || report.targetSite < 0 || report.targetSite >= report.sites.Length)
                    throw new InvalidOperationException("First committed journey did not select a new original activity site.");
                report.siteCenter = report.sites[report.targetSite].center; report.siteRadius = report.sites[report.targetSite].radius;
                for (int i = 0; i < 10; i++)
                {
                    if (members[i].Arrived) throw new InvalidOperationException("A member was already Arrived at first commit; departure was not fully observed.");
                    report.members[i].committedGoal = Goal(members[i]);
                    WriteEvent("natural-commit", members[i], "original site=" + report.targetSite);
                }
            }
            if (report.departed && herd.TargetSite != report.targetSite)
                throw new InvalidOperationException("The original destination changed before all ten arrived (including recovery/fallback); not a successful trip.");
            for (int i = 0; i < 10; i++)
            {
                JourneyMember m = members[i]; MemberResult result = report.members[i];
                Vector3 p = m.root.position; Quaternion q = m.root.rotation;
                bool moved = p != previousPositions[i] || q != previousRotations[i];
                if (moved)
                {
                    report.physicsChecks++;
                    if (!navigation.CanMoveSupportedBody(m.profile, previousPositions[i], previousRotations[i], p, q))
                    {
                        report.physicsFailures++; WriteEvent("body-sweep-failed", m, "Observed whole frame motion; current world colliders.", previousPositions[i], previousRotations[i]);
                        throw new InvalidOperationException("Observed full-body world/deck sweep failed for member " + m.id + ".");
                    }
                    if (Flat(p, previousPositions[i]) > .00001f) result.movingFrames++;
                }
                previousPositions[i] = p; previousRotations[i] = q;
                result.finalPosition = p; result.finalGoal = Goal(m); result.distanceTravelled = m.DistanceTravelled;
                if (m.Mode == JourneyTravelMode.Bridge) result.bridgeFrames++;
                if (m.Mode == JourneyTravelMode.Swim) result.swimFrames++;
                JourneyRoute route = (JourneyRoute)Route.GetValue(m);
                if (!ReferenceEquals(route, lastRoutes[i]))
                {
                    if (report.departed && route != null) result.routeReplacements++;
                    WriteEvent(route == null ? "route-cleared" : "route-assigned", m, "segments=" + (route == null ? 0 : route.segments.Count), route: route);
                    lastRoutes[i] = route;
                }
                string mode = m.State + "/" + m.Mode + "/" + Bridge.GetValue(m) + "/step" + BodyPhase.GetValue(m);
                if (mode != lastModes[i]) { WriteEvent("mode", m, mode); lastModes[i] = mode; }
                if (result.finalGoal != lastGoals[i]) { WriteEvent("goal-adjusted", m, "previous=" + lastGoals[i].ToString("R")); lastGoals[i] = result.finalGoal; }
                if (report.departed && m.Arrived && result.arrivalTime < 0)
                {
                    if (!ValidArrival(p, result.finalGoal, report.siteCenter, report.siteRadius, m.Arrived, m.Mode))
                        throw new InvalidOperationException("Arrived flag without reaching its actual goal in the original site: " + m.id);
                    result.arrivalTime = herd.SimulationTime; result.arrivalPosition = p; WriteEvent("arrived-original-site", m, "Actual goal reached within the runtime .18 m threshold.");
                }
            }
            report.finalCompleted = herd.CompletedJourneys; report.routeChanges = herd.RouteChanges;
            report.planningRequests = herd.PlanningRequests; report.planningCompletions = herd.PlanningCompletions; report.planningCancellations = herd.PlanningCancellations;
            if (herd.SimulationTime >= nextSample)
            {
                nextSample = herd.SimulationTime + 1f;
                if (Configuration() != report.configuration) throw new InvalidOperationException("Original herd/navigation serialized configuration changed.");
                WriteFrame(); trajectory.Flush(); events.Flush(); plans.Flush();
            }
            if (CompleteContract(report.departed, herd.TargetSite == report.targetSite, members.All(m => m.Arrived),
                report.members.All(m => m.arrivalTime >= 0), herd.CompletedJourneys, report.initialCompleted))
            {
                if (members.Any(m => Flat(m.root.position, report.siteCenter) > report.siteRadius + .18001f))
                    throw new InvalidOperationException("An arrived member left the original site before full regrouping.");
                report.completedOriginalJourney = true; report.completionTime = herd.SimulationTime; WriteFrame(); Finish("COMPLETE");
            }
            else if (herd.CompletedJourneys > report.initialCompleted)
                throw new InvalidOperationException("Journey counter increased without ten verified arrivals at the original site.");
            else if (herd.SimulationTime - report.simulationStart >= SimulationLimit)
                Fail(report.departed ? "1050 simulated seconds elapsed without all ten reaching the original site." : "No natural departure within 1050 simulated seconds.");
        }
        static void WriteFrame()
        {
            trajectory.WriteLine(JsonUtility.ToJson(new Frame { frame = Time.frameCount, simulationTime = herd.SimulationTime, seconds = report.seconds,
                targetSite = herd.TargetSite, completed = herd.CompletedJourneys, requests = herd.PlanningRequests, completions = herd.PlanningCompletions,
                cancellations = herd.PlanningCancellations, routeChanges = herd.RouteChanges, travelling = herd.Travelling, planning = herd.IsPlanning, decision = herd.GroupDecision,
                members = members.Select(m => { var r = (JourneyRoute)Route.GetValue(m); int s = (int)Segment.GetValue(m);
                    return new MemberFrame { id = m.id, position = m.root.position, rotation = m.root.rotation, goal = Goal(m), segment = s, bridge = (int)Bridge.GetValue(m),
                        bodyStepPhase = (int)BodyPhase.GetValue(m), hasRoute = r != null, next = r != null && s < r.segments.Count ? r.segments[s].to : m.root.position,
                        state = m.State.ToString(), mode = m.Mode.ToString(), arrived = m.Arrived, decision = m.LastDecision, distanceTravelled = m.DistanceTravelled,
                        speed = m.ActualSpeed, swimming = m.ContinuousSwimDistance }; }).ToArray() }));
        }
        static void WriteEvent(string kind, JourneyMember m, string detail, Vector3 previous = default, Quaternion rotation = default, JourneyRoute route = null)
        {
            events.WriteLine(JsonUtility.ToJson(new Event { kind = kind, detail = detail, id = m.id, frame = Time.frameCount, simulationTime = herd.SimulationTime,
                position = m.root.position, rotation = m.root.rotation, goal = Goal(m), previousPosition = previous, previousRotation = rotation,
                route = route == null ? null : route.segments.ToArray() }));
        }
        static object Field(object value, string name) => value?.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(value);
        static PlanSnapshot PlanData(string kind)
        {
            // Compiler-generated locals are evidence, not a new scheduler or an inferred failure code.
            var fields = observedSteps.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            object Local(string name)
            {
                var direct = fields.FirstOrDefault(f => f.Name.StartsWith("<" + name + ">", StringComparison.Ordinal));
                if (direct != null) return direct.GetValue(observedSteps);
                // Callback-assigned locals (notably scatter goals) live in the iterator's display class.
                foreach (var field in fields.Where(f => f.Name.StartsWith("<>8__", StringComparison.Ordinal)))
                {
                    object closure = field.GetValue(observedSteps);
                    var captured = closure?.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (captured != null) return captured.GetValue(closure);
                }
                return null;
            }
            var routes = Local("routes") as JourneyRoute[];
            object operation = Local("operation"), search = Local("search");
            var snapshot = new PlanSnapshot { kind = kind, frame = Time.frameCount, simulationTime = herd.SimulationTime,
                site = (int)Field(observedPlan, "site"), member = (int)Field(observedPlan, "member"), capturedTime = (float)Field(observedPlan, "time"),
                success = (bool)Field(observedPlan, "success"), finished = (bool)Field(observedPlan, "finished"), recovery = (bool)Field(observedPlan, "recovery"),
                iteratorType = observedSteps.GetType().FullName, iteratorState = (int)(Field(observedSteps, "<>1__state") ?? -999),
                operationType = operation?.GetType().FullName, searchStatus = search?.GetType().GetProperty("Status")?.GetValue(search)?.ToString(),
                scatteredGoals = Local("goals") as Vector3[], destinations = Local("destinations") as Vector3[],
                routeSegments = routes?.Select(r => r == null ? -1 : r.segments.Count).ToArray(),
                locals = fields.Where(f => f.FieldType.IsPrimitive || f.FieldType.IsEnum).Select(f => f.Name + "=" + Convert.ToString(f.GetValue(observedSteps), System.Globalization.CultureInfo.InvariantCulture)).ToArray(),
                stageEvidence = "Actual native iterator locals only. A missing route can also mean an earlier placement failure. No MoveNext or diagnostic replanning was invoked." };
            if (routes != null)
                for (int i = 0; i < routes.Length; i++) if (routes[i] == null) { snapshot.firstMissingRouteMember = members[i].id; break; }
            return snapshot;
        }
        static void SamplePlanning()
        {
            object current = Pending.GetValue(herd);
            if (!ReferenceEquals(current, observedPlan))
            {
                if (observedPlan != null && observedSteps != null)
                    plans.WriteLine(JsonUtility.ToJson(PlanData((bool)Field(observedPlan, "finished") ? "native-plan-finished" : "native-plan-cancelled")));
                observedPlan = current; observedSteps = PlanningSteps.GetValue(herd); lastPlanStage = null;
            }
            if (observedPlan == null || observedSteps == null) return;
            PlanSnapshot snapshot = PlanData("native-plan-stage");
            string stage = snapshot.iteratorState + "/" + snapshot.operationType + "/" + snapshot.searchStatus + "/" + string.Join(";", snapshot.locals);
            if (stage != lastPlanStage) { plans.WriteLine(JsonUtility.ToJson(snapshot)); lastPlanStage = stage; }
        }
        static void Fail(string error) { if (!running) return; report.error += error + "\n"; Finish("FAIL"); }
        public static void Finish(string status)
        {
            if (!running) return;
            running = false; EditorApplication.update -= Tick; RenderPipelineManager.endFrameRendering -= Rendered;
            try
            {
                trajectory?.Dispose(); events?.Dispose(); plans?.Dispose();
                report.configurationUnchanged = herd && navigation && Configuration() == report.configuration && Time.timeScale == timeScale;
                report.restored = true; // This observer never changes scene objects, settings, input or camera.
                if (status == "COMPLETE" && (!report.completedOriginalJourney || report.physicsFailures != 0 || !report.configurationUnchanged))
                { status = "FAIL"; report.error += "Completion was requested without a verified original journey and unchanged configuration.\n"; }
                if (status != "COMPLETE" && string.IsNullOrEmpty(report.error)) report.error = "Observation ended before a verified complete journey: " + status;
                report.result = status;
            }
            finally { IsComplete = true; File.WriteAllText(Path.Combine(directory, "sample.json"), JsonUtility.ToJson(report, true)); }
        }
    }
}
