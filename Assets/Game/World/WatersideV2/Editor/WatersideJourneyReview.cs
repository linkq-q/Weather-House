using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using WeatherHouse.Hillside;
using WeatherHouse.WildlifeJourney;
using Object = UnityEngine.Object;

namespace WeatherHouse.WatersideV2.Editor
{
    /// <summary>Observes the saved Waterside herd through its real Update; never drives or accelerates it.</summary>
    [InitializeOnLoad]
    public static class WatersideJourneyReview
    {
        const string Folder = WatersideSceneBuilder.Review;
        const string Key = "WeatherHouse.WatersideV2.JourneyReview.";
        const float SimulationLimit = 360;
        static double nextPoll, started, nextTrace;
        static JourneyHerd herd;
        static HillsideGrassRenderer grass;
        static Camera mainCamera;
        static bool initialized, departed, travelShot, bridgeShot, queueShot;
        static int departureSite = -1;
        static float departureTime;
        static readonly HashSet<int> bridgeUsers = new HashSet<int>(), queuedMembers = new HashSet<int>();
        static readonly HashSet<int> observedBridges = new HashSet<int>();
        static readonly Dictionary<string, float> mixedMargins = new Dictionary<string, float>();
        static int mixedSamples, initialMixedOverlaps, newMixedOverlaps;
        static float minimumMixedMargin, finalMixedMargin;
        static string minimumMixedPair, finalMixedPair;
        static bool Running => SessionState.GetBool(Key + "running", false);

        static WatersideJourneyReview()
        {
            EditorApplication.update += Poll;
            EditorApplication.playModeStateChanged += PlayChanged;
        }

        static void Poll()
        {
            if (EditorApplication.timeSinceStartup < nextPoll || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            nextPoll = EditorApplication.timeSinceStartup + .2;
            string pendingCommand = null;
            try
            {
                if (Running)
                {
                    if (EditorApplication.isPlaying) Observe();
                    return;
                }
                // Leave a queued request untouched while another Play review owns the editor.
                if (EditorApplication.isPlayingOrWillChangePlaymode || SessionState.GetBool(Key + "settings", false)) return;
                string request = Folder + "/journey-request.txt";
                if (!File.Exists(request)) return;
                string command = File.ReadAllText(request).Trim(); File.Delete(request);
                pendingCommand = command;
                if (command == "diagnose") File.WriteAllText(Folder + "/herd-stranding-diagnostic.txt", DiagnoseStranding());
                else if (command == "playcheck") Begin();
                else throw new InvalidOperationException("Unknown Journey review command: " + command);
            }
            catch (Exception error)
            {
                if (Running) Finish("FAIL", error.ToString());
                else { Directory.CreateDirectory(Folder); File.WriteAllText(Folder + (pendingCommand == "diagnose" ? "/herd-stranding-diagnostic.txt" : "/herd-runtime.txt"), "FAIL\n" + error); }
                Debug.LogException(error);
            }
        }

        public static string DiagnoseStranding()
        {
            WatersideSceneBuilder.RequireSaved();
            Scene scene = SceneManager.GetActiveScene();
            if (scene.path != WatersideSceneBuilder.ScenePath) throw new InvalidOperationException("Diagnose the saved Waterside map in Edit mode.");
            JourneyHerd source = Object.FindObjectOfType<JourneyHerd>();
            JourneyMember member = source ? source.members.SingleOrDefault(m => m != null && m.id == 25) : null;
            if (member == null || !source.navigation || source.sites.Length < 2) throw new InvalidOperationException("Actual Waterside sheep 25 and its navigation are required.");
            Transform[] transforms = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true)).ToArray();
            Vector3[] positions = transforms.Select(t => t.position).ToArray();
            Quaternion[] rotations = transforms.Select(t => t.rotation).ToArray();
            var report = new StringBuilder("READ-ONLY STRANDING REPLAN\n");
            try
            {
                JourneyNavigation nav = source.navigation; JourneyProfile profile = member.profile;
                Vector3 from = new Vector3(25.680f, 9.834f, 22.390f), goal = source.sites[1].center;
                // Initialize uses this first random draw for phase; reading the nonserialized Edit field is not a replay.
                var individual = new WildlifeRandom(source.seed, member.id, "sheep");
                float phase = individual.Value(), pace = individual.Range(.88f, 1.12f), lateral = (phase - .5f) * 2.2f;
                FieldInfo phaseField = typeof(JourneyMember).GetField("phase", BindingFlags.Instance | BindingFlags.NonPublic);
                report.AppendLine("Evidence: from is the failed trace rounded to 3 decimals; original dispersed destination and temporary excluded links were lost on exiting Play.");
                report.AppendLine("This is a NEW FindRoute to the old-tree site center, no blocked links and initialSwimDistance=0; it is not the exact former runtime route.");
                report.AppendLine("No Initialize, Simulate, movement, member writes or scene-object creation is performed. Pre-fix PassedWaypoint and unchecked 1.3m lookahead are evaluated as a diagnostic baseline, not as an assertion about the current Herd implementation; Mode=Walk follows the failed trace.");
                report.AppendLine("seed=" + source.seed + " id=" + member.id + " phaseReplay=" + phase.ToString("R") + " paceReplay=" + pace.ToString("R") +
                    " lateral=" + lateral.ToString("R") + " editFieldPhase=" + (phaseField == null ? "unavailable" : phaseField.GetValue(member).ToString()));
                report.AppendLine("Profile: radius=" + profile.bodyRadius + " walk=" + profile.walkSpeed + " canSwim=" + profile.canSwim + " swim=" + profile.swimSpeed +
                    " wade=" + profile.wadeDepth + " swimLimit=" + profile.maxSwimDistance + " waterline=" + profile.waterlineHeight + " aversion=" + profile.waterAversion +
                    "; nav maxSlope=" + nav.maxSlope + " stepHeight=" + nav.maxStepHeight + " spacing=" + nav.sampleSpacing + " attachmentRadius=" + nav.endpointConnectionRadius);
                Physics.SyncTransforms();
                Point("FROM", from); Point("GOAL CENTER (substitute)", goal);
                JourneyRoute raw = nav.FindRoute(from, goal, profile);
                if (raw == null) { report.AppendLine("FindRoute returned null."); return report.ToString(); }
                Describe("RAW", raw);
                var varied = new JourneyRoute { segments = new List<JourneySegment>(raw.segments), totalCost = raw.totalCost, navigationVersion = raw.navigationVersion };
                Type variation = typeof(JourneyHerd).Assembly.GetType("WeatherHouse.WildlifeJourney.JourneyRouteVariation", true);
                MethodInfo apply = variation.GetMethod("Apply", BindingFlags.Public | BindingFlags.Static);
                if (apply == null) throw new MissingMethodException(variation.FullName, "Apply");
                apply.Invoke(null, new object[] { nav, profile, varied, lateral, 0f });
                bool changed = raw.segments.Where((s, i) => s.from != varied.segments[i].from || s.to != varied.segments[i].to).Any();
                report.AppendLine("Variation invoked on a fresh route copy using the exact replayed phase; geometryChanged=" + changed + ". A false value may mean the variation rejected its candidate or made no eligible offset.");
                Describe("VARIED", varied);
                Compare("RAW", raw); Compare("VARIED", varied);
                return report.ToString();

                void Describe(string label, JourneyRoute route)
                {
                    report.AppendLine(label + " segmentCount=" + route.segments.Count + " totalCost=" + route.totalCost.ToString("F6"));
                    for (int i = 0; i < Math.Min(20, route.segments.Count); i++)
                    {
                        JourneySegment s = route.segments[i];
                        report.AppendLine(label + "[" + i + "] from=" + s.from.ToString("F6") + " to=" + s.to.ToString("F6") + " link=" + s.linkIndex + " bridge=" + s.bridgeId + " mode=" + s.mode);
                    }
                }
                void Compare(string label, JourneyRoute route)
                {
                    if (route.segments.Count == 0) return;
                    int selected = 0;
                    while (selected < route.segments.Count)
                    {
                        JourneySegment s = route.segments[selected]; Vector3 along = s.to - s.from, offset = from - s.to; along.y = offset.y = 0;
                        bool passed = selected == route.segments.Count - 1 ? offset.magnitude < .18f :
                            offset.magnitude < .12f || s.bridgeId == 0 && s.mode == JourneyTravelMode.Walk && offset.magnitude < 1.6f && Vector3.Dot(offset, along) >= 0;
                        report.AppendLine(label + " PassedWaypoint index=" + selected + " passed=" + passed + " flatDistance=" + offset.magnitude.ToString("F6") + " dot=" + Vector3.Dot(offset, along).ToString("F6"));
                        if (!passed) break;
                        selected++;
                    }
                    Step(label + " root->first.to FULL", route.segments[0].to);
                    Step(label + " root->first.to 0.02m", Vector3.MoveTowards(from, route.segments[0].to, .02f));
                    if (selected == route.segments.Count) { report.AppendLine(label + " would immediately mark Arrived under the pre-fix waypoint test."); return; }
                    JourneySegment first = route.segments[selected]; Vector3 lookahead = first.to; int last = selected;
                    for (int ahead = selected + 1; ahead < route.segments.Count; ahead++)
                    {
                        JourneySegment candidate = route.segments[ahead];
                        if (candidate.mode != first.mode || candidate.bridgeId != first.bridgeId || candidate.linkIndex != first.linkIndex || Flat(from, candidate.to) > 1.3f) break;
                        lookahead = candidate.to; last = ahead;
                    }
                    report.AppendLine(label + " selected=" + selected + " lookaheadIndex=" + last + " target=" + lookahead.ToString("F6"));
                    Point(label + " SELECTED.to", first.to); Point(label + " LOOKAHEAD", lookahead);
                    Step(label + " root->selected.to FULL", first.to);
                    Step(label + " root->selected.to 0.02m", Vector3.MoveTowards(from, first.to, .02f));
                    Step(label + " root->lookahead 0.02m", Vector3.MoveTowards(from, lookahead, .02f));
                }
                void Step(string label, Vector3 requested)
                {
                    bool legal = nav.TryStep(profile, from, requested, out Vector3 projected, out JourneyTravelMode mode);
                    nav.TryProject(profile, from, out Vector3 startFeet, out _);
                    nav.TryProject(profile, requested, out Vector3 endFeet, out _);
                    float horizontal = Flat(startFeet, endFeet), rise = endFeet.y - startFeet.y;
                    report.AppendLine(label + " TryStep=" + legal + " requested=" + requested.ToString("F6") + " result=" + projected.ToString("F6") + " mode=" + mode +
                        " feetDeltaY=" + rise.ToString("F6") + " horizontal=" + horizontal.ToString("F6") + " absRisePerM=" + (Mathf.Abs(rise) / Mathf.Max(.000001f, horizontal)).ToString("F6") + " maxRisePerM=" + Mathf.Tan(nav.maxSlope * Mathf.Deg2Rad).ToString("F6"));
                    Point(label + " POINT", requested);
                }
                void Point(string label, Vector3 point)
                {
                    JourneySurface sample = nav.SampleSurface(point);
                    bool projected = nav.TryProject(profile, point, out Vector3 feet, out JourneyTravelMode mode);
                    float immersion = profile.waterlineHeight >= 0 ? -profile.waterlineHeight : nav.floatOffset;
                    float probeY = mode == JourneyTravelMode.Swim ? Mathf.Max(sample.groundY, sample.waterY + immersion) : sample.groundY;
                    float radius = Mathf.Max(.05f, profile.bodyRadius);
                    Vector3 probe = new Vector3(point.x, probeY + radius + .04f, point.z);
                    Collider[] overlap = Physics.OverlapSphere(probe, radius * .85f, nav.obstacleMask, QueryTriggerInteraction.Ignore);
                    report.AppendLine(label + " p=" + point.ToString("F6") + " TryProject=" + projected + " feet=" + feet.ToString("F6") + " feetMinusInputY=" + (feet.y - point.y).ToString("F6") +
                        " mode=" + mode + " slope=" + sample.slope.ToString("F6") + " ground=" + sample.groundY.ToString("F6") + " valid=" + sample.valid + " blocked=" + sample.blocked +
                        " water=" + sample.hasWater + ":" + sample.waterY.ToString("F6") + " bridge=" + sample.bridgeId + " support=" + (sample.supportingCollider ? sample.supportingCollider.name : "none") +
                        " OverlapSphere(center=" + probe.ToString("F6") + ",r=" + (radius * .85f).ToString("F6") + ")=[" + string.Join(" | ", overlap.Select(c => c.name + "(" + c.GetType().Name + ") center=" + c.bounds.center.ToString("F6") +
                        " blocksProject=" + (!(c is TerrainCollider) && c != sample.supportingCollider))) + "]");
                }
            }
            finally
            {
                if (SceneManager.GetActiveScene() != scene || scene.isDirty || transforms.Where((t, i) => !t || t.position != positions[i] || t.rotation != rotations[i]).Any())
                    throw new InvalidOperationException("The read-only diagnostic unexpectedly changed scene state or transforms.");
            }
        }

        public static void Begin()
        {
            WatersideSceneBuilder.RequireSaved();
            if (SceneManager.GetActiveScene().path != WatersideSceneBuilder.ScenePath)
                throw new InvalidOperationException("Open the saved Waterside V2 scene before its Journey review.");
            Directory.CreateDirectory(Folder);
            File.WriteAllText(Folder + "/herd-runtime.txt", "RUNNING\nReal Update only; no Simulate calls, goal changes, speed changes or time-scale changes.\n");
            File.WriteAllText(Folder + "/herd-trace.txt", "Started UTC " + DateTime.UtcNow.ToString("O") + "\n");
            SessionState.SetBool(Key + "background", Application.runInBackground);
            SessionState.SetBool(Key + "paused", EditorApplication.isPaused);
            SessionState.SetBool(Key + "settings", true);
            SessionState.SetBool(Key + "running", true);
            SessionState.SetString(Key + "status", "RUNNING");
            SessionState.SetString(Key + "reason", "");
            EditorApplication.isPlaying = true;
        }

        static void PlayChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredPlayMode && Running)
            {
                started = EditorApplication.timeSinceStartup; nextTrace = 0;
                herd = null; initialized = departed = travelShot = bridgeShot = queueShot = false;
                departureSite = -1; bridgeUsers.Clear(); queuedMembers.Clear(); observedBridges.Clear();
                mixedMargins.Clear(); mixedSamples = initialMixedOverlaps = newMixedOverlaps = 0;
                minimumMixedMargin = finalMixedMargin = float.PositiveInfinity;
                minimumMixedPair = finalMixedPair = "none";
                Application.runInBackground = true; EditorApplication.isPaused = false;
            }
            if (state == PlayModeStateChange.ExitingPlayMode && SessionState.GetBool(Key + "settings", false))
            {
                if (Running) RecordResult("ABORTED", "Play ended before the natural journey completed.");
                RestoreSettings(false);
            }
            if (state == PlayModeStateChange.EnteredEditMode && SessionState.GetBool(Key + "settings", false))
            {
                if (Running) RecordResult("ABORTED", "Play did not complete.");
                RestoreSettings(true);
                bool clean = SceneManager.GetActiveScene().path == WatersideSceneBuilder.ScenePath;
                for (int i = 0; i < SceneManager.sceneCount; i++) clean &= !SceneManager.GetSceneAt(i).isDirty;
                string status = SessionState.GetString(Key + "status", "FAIL"), reason = SessionState.GetString(Key + "reason", "");
                if (!clean) { status = "FAIL"; reason += "\nEditor did not return to the unchanged clean Waterside scene; unsaved state was preserved."; }
                File.WriteAllText(Folder + "/herd-runtime.txt", status + "\n" + reason +
                    "\nFinal editor: playing=" + EditorApplication.isPlaying + " clean=" + clean + " scene=" + SceneManager.GetActiveScene().path +
                    "\nrunInBackground=" + Application.runInBackground + " isPaused=" + EditorApplication.isPaused + "\n\n" + File.ReadAllText(Folder + "/herd-trace.txt"));
            }
        }

        static void Observe()
        {
            double wall = EditorApplication.timeSinceStartup - started;
            if (!herd) herd = Object.FindObjectOfType<JourneyHerd>();
            if (!initialized)
            {
                if (!herd || herd.SimulationTime <= 0 || string.IsNullOrEmpty(herd.GroupDecision))
                {
                    if (wall > 20) throw new TimeoutException("Waterside JourneyHerd did not initialize through Start/Update within 20 seconds.");
                    return;
                }
                ValidateMap(); initialized = true;
                mainCamera = Camera.main; grass = Object.FindObjectOfType<HillsideGrassRenderer>();
                if (!mainCamera || !grass) throw new InvalidOperationException("The saved map camera and calibrated grass renderer are required.");
                Append("Initialized at simulation=" + herd.SimulationTime.ToString("F3") + "; autoAdvance=" + herd.autoAdvance +
                    "; timeScale=" + Time.timeScale + "; terrain=" + herd.navigation.terrain.name + "; real bridge surfaces=" + herd.navigation.bridges.Length + "\n");
            }
            if (herd.members.Length != 10 || herd.members.Any(m => m == null || !m.root || !m.root.gameObject.activeInHierarchy || !m.animator || !m.animator.enabled))
                throw new InvalidOperationException("All ten original herd members must remain active with their animators throughout the journey.");
            if (wall > 390 || herd.SimulationTime > SimulationLimit)
                throw new TimeoutException("Natural rendezvous did not finish within 360 simulation seconds / 390 wall seconds. " + Summary());
            ObserveMixedBodies();
            if (!departed && herd.Travelling)
            {
                departed = true; departureSite = herd.TargetSite; departureTime = herd.SimulationTime;
                Append("DEPARTURE " + Summary() + "\n");
            }
            foreach (JourneyMember member in herd.members)
            {
                if (member.Mode == JourneyTravelMode.Bridge && !member.Arrived)
                {
                    int id = herd.navigation.SampleSurface(member.root.position).bridgeId;
                    if (id <= 0 || id > WatersideLayout.Crossings.Length)
                        throw new InvalidOperationException("Bridge mode lacks a real Waterside bridge collider for sheep " + member.id);
                    bridgeUsers.Add(member.id); observedBridges.Add(id);
                    if (!bridgeShot) { Capture("herd-01_新地图首次过桥", BridgeBounds(id)); bridgeShot = true; }
                }
                if (member.State == JourneyMemberState.WaitingForBridge && !member.Arrived)
                {
                    int id = NearestBridge(member.root.position);
                    if (id > 0)
                    {
                        queuedMembers.Add(member.id);
                        if (!queueShot) { Capture("herd-02_实际桥外队列", BridgeBounds(id)); queueShot = true; }
                    }
                }
            }
            if (departed && herd.Travelling && !travelShot && herd.SimulationTime - departureTime >= 8)
            { Capture("herd-03_原速旅途中", MemberBounds()); travelShot = true; }
            if (EditorApplication.timeSinceStartup >= nextTrace)
            { nextTrace = EditorApplication.timeSinceStartup + 5; Trace(wall); }
            // Initialize sets Arrived=true: completion additionally requires a natural departure and an incremented journey counter.
            if (departed && herd.CompletedJourneys >= 1 && !herd.Travelling && herd.members.All(m => m.Arrived))
            {
                if (herd.TargetSite != departureSite || !bridgeShot || !travelShot || herd.members.Any(m => m.DistanceTravelled < 5))
                    throw new InvalidOperationException("Rendezvous counter advanced without the expected full map journey and bridge/travel evidence. " + Summary());
                JourneyActivitySite destination = herd.sites[herd.TargetSite];
                if (herd.members.Any(m => !(Flat(m.root.position, destination.center) <= destination.radius + .25f)))
                    throw new InvalidOperationException("Arrived flags do not match all ten actual positions inside the destination clearing. " + Summary());
                Trace(wall); Capture("herd-04_十只原速会合", MemberBounds());
                string spacing = "Mixed body footprint samples=" + mixedSamples + "; initial overlaps=" + initialMixedOverlaps +
                    "; new overlap entries=" + newMixedOverlaps + "; minimum physical gap=" + minimumMixedMargin.ToString("F4") +
                    "m pair=" + minimumMixedPair + "; final minimum physical gap=" + finalMixedMargin.ToString("F4") + "m pair=" + finalMixedPair +
                    ". Samples are every editor observation (~0.2s), not every rendered frame; radii are controller footprints, not exact animated mesh outlines.";
                Append(spacing + "\n");
                if (newMixedOverlaps > 0) throw new InvalidOperationException("New mixed-controller body overlap was observed. " + spacing);
                Finish("PASS", "A natural journey completed through real Update; all 10 active members arrived. " + Summary() +
                    "\nBridge users=" + string.Join(",", bridgeUsers.OrderBy(x => x)) + "; queued members=" + string.Join(",", queuedMembers.OrderBy(x => x)) +
                    "; actual map bridges=" + string.Join(",", observedBridges.OrderBy(x => x).Select(id => WatersideLayout.Crossings[id - 1].Name)) + "\n" + spacing);
            }
        }

        static void ObserveMixedBodies()
        {
            var population = herd.surroundingPopulation;
            if (!population || !population.isActiveAndEnabled || population.agents == null)
                throw new InvalidOperationException("The Journey herd is not linked to the active original animal population.");
            finalMixedMargin = float.PositiveInfinity; finalMixedPair = "none";
            foreach (var member in herd.members)
                foreach (var other in population.agents)
                {
                    if (!other || !other.isActiveAndEnabled || other.transform == member.root || member.Mode == JourneyTravelMode.Swim ||
                        Mathf.Abs(other.transform.position.y - member.root.position.y) > .65f ||
                        !(other.locomotion == WildlifeLocomotion.Ground || other.locomotion == WildlifeLocomotion.Flying && other.AirHeight < .01f)) continue;
                    string pair = "sheep " + member.id + "/" + other.speciesId + " " + other.spawnId + "#" + other.GetInstanceID();
                    float gap = Flat(member.root.position, other.transform.position) - Mathf.Max(member.profile.bodyRadius, member.bodyLength * .5f) - other.Radius;
                    bool known = mixedMargins.TryGetValue(pair, out float previous);
                    if (gap < -.0001f && (!known || previous >= -.0001f))
                    {
                        if (mixedSamples == 0) initialMixedOverlaps++;
                        else { newMixedOverlaps++; Append("NEW MIXED OVERLAP " + pair + " gap=" + gap.ToString("F6") + " simulation=" + herd.SimulationTime + "\n"); }
                    }
                    mixedMargins[pair] = gap;
                    if (gap < minimumMixedMargin) { minimumMixedMargin = gap; minimumMixedPair = pair; }
                    if (gap < finalMixedMargin) { finalMixedMargin = gap; finalMixedPair = pair; }
                }
            mixedSamples++;
        }

        static void ValidateMap()
        {
            if (herd.gameObject.scene.path != WatersideSceneBuilder.ScenePath || !herd.navigation || !herd.navigation.terrain ||
                !(herd.navigation.worldWaterSurface is WatersideWaterSurface) || herd.sites.Length != 2 ||
                Flat(herd.sites[0].center, WatersideLayout.LakeTerrace) > .5f || Flat(herd.sites[1].center, WatersideLayout.OldTree) > .5f)
                throw new InvalidOperationException("Journey review requires the actual lake-terrace to old-tree Waterside integration, not the lab.");
            if (herd.members.Length != 10 || herd.members.Select(m => m == null ? -1 : m.id).Distinct().Count() != 10)
                throw new InvalidOperationException("Expected ten distinct integrated sheep.");
            if (herd.navigation.bridges.Length == 0 || herd.navigation.bridges.Any(b => b == null || !b.surface || b.id <= 0 || b.id > WatersideLayout.Crossings.Length ||
                !b.surface.transform.parent || b.surface.transform.parent.name != WatersideLayout.Crossings[b.id - 1].Name))
                throw new InvalidOperationException("Journey bridge IDs are not mapped to the actual Waterside deck colliders.");
        }

        static string Summary() => "simulation=" + herd.SimulationTime.ToString("F3") + "; frame=" + Time.frameCount +
            "; completed=" + herd.CompletedJourneys + "; target=" + herd.TargetSite + "; travelling=" + herd.Travelling +
            "; arrived=" + herd.members.Count(m => m != null && m.Arrived) + "/10; GroupDecision=" + herd.GroupDecision;
        static void Trace(double wall)
        {
            var report = new StringBuilder("wall=" + wall.ToString("F1") + "; " + Summary() + "\n");
            foreach (JourneyMember m in herd.members)
                report.AppendLine("  sheep=" + m.id + " position=" + m.root.position.ToString("F3") + " state=" + m.State + " mode=" + m.Mode +
                    " arrived=" + m.Arrived + " speed=" + m.ActualSpeed.ToString("F3") + " distance=" + m.DistanceTravelled.ToString("F2") +
                    " nearBridge=" + NearestBridge(m.root.position) + " LastDecision=" + m.LastDecision);
            Append(report.ToString());
        }
        static void Append(string text)
        { File.AppendAllText(Folder + "/herd-trace.txt", text); File.AppendAllText(Folder + "/herd-runtime.txt", text); }
        static float Flat(Vector3 a, Vector3 b) => Vector3.ProjectOnPlane(a - b, Vector3.up).magnitude;
        static int NearestBridge(Vector3 position)
        {
            int id = 0; float nearest = 6;
            foreach (JourneyBridgeSurface bridge in herd.navigation.bridges)
            {
                float distance = Flat(position, bridge.surface.bounds.ClosestPoint(position));
                if (distance < nearest) { nearest = distance; id = bridge.id; }
            }
            return id;
        }
        static Bounds MemberBounds()
        {
            var bounds = new Bounds(herd.members[0].root.position + Vector3.up * .7f, new Vector3(2, 1.8f, 2));
            foreach (JourneyMember member in herd.members) bounds.Encapsulate(new Bounds(member.root.position + Vector3.up * .7f, new Vector3(2, 1.8f, 2)));
            return bounds;
        }
        static Bounds BridgeBounds(int id)
        {
            var crossing = WatersideLayout.Crossings[id - 1];
            var bounds = new Bounds((crossing.Start + crossing.End) * .5f + Vector3.up, Vector3.one * 3);
            bounds.Encapsulate(crossing.Start); bounds.Encapsulate(crossing.End); bounds.Expand(new Vector3(6, 1, 6));
            foreach (JourneyMember member in herd.members)
                if (NearestBridge(member.root.position) == id) bounds.Encapsulate(member.root.position + Vector3.up * 1.5f);
            return bounds;
        }

        static void Capture(string name, Bounds bounds)
        {
            var camera = new GameObject("Temporary Waterside herd review camera").AddComponent<Camera>();
            var target = new RenderTexture(1600, 900, 24, RenderTextureFormat.ARGB32);
            var pixels = new Texture2D(1600, 900, TextureFormat.RGB24, false);
            RenderTexture active = RenderTexture.active;
            try
            {
                camera.CopyFrom(mainCamera); camera.enabled = false;
                camera.gameObject.AddComponent<UniversalAdditionalCameraData>();
                camera.GetUniversalAdditionalCameraData().requiresColorTexture = true;
                camera.GetUniversalAdditionalCameraData().requiresDepthTexture = true;
                Quaternion view = Quaternion.Euler(48, 35, 0); float halfWidth = 0, halfHeight = 0;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 corner = Vector3.Scale(bounds.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    Vector3 local = Quaternion.Inverse(view) * corner;
                    halfWidth = Mathf.Max(halfWidth, Mathf.Abs(local.x)); halfHeight = Mathf.Max(halfHeight, Mathf.Abs(local.y));
                }
                camera.orthographic = true; camera.aspect = 16f / 9f;
                camera.orthographicSize = Mathf.Max(3, halfHeight, halfWidth / camera.aspect) * 1.08f;
                camera.nearClipPlane = .1f; camera.farClipPlane = 240;
                camera.transform.SetPositionAndRotation(bounds.center + view * Vector3.back * (bounds.extents.magnitude + 18), view);
                camera.targetTexture = target; grass.DrawForCamera(camera, true); camera.Render();
                RenderTexture.active = target; pixels.ReadPixels(new Rect(0, 0, 1600, 900), 0, 0); pixels.Apply();
                File.WriteAllBytes(Folder + "/" + name + ".png", pixels.EncodeToPNG());
                Append("CAPTURE " + name + "; " + Summary() + "; camera=" + camera.transform.position.ToString("F3") + "; focus=" + bounds.center.ToString("F3") + "\n");
            }
            finally
            {
                camera.targetTexture = null; RenderTexture.active = active; target.Release();
                Object.DestroyImmediate(pixels); Object.DestroyImmediate(target); Object.DestroyImmediate(camera.gameObject);
            }
        }

        static void Finish(string status, string reason)
        {
            RecordResult(status, reason); RestoreSettings(false); EditorApplication.isPlaying = false;
        }
        static void RecordResult(string status, string reason)
        {
            SessionState.SetBool(Key + "running", false); SessionState.SetString(Key + "status", status); SessionState.SetString(Key + "reason", reason);
            Append(status + " — " + reason + "\nAwaiting Edit-mode cleanup.\n");
        }
        static void RestoreSettings(bool complete)
        {
            Application.runInBackground = SessionState.GetBool(Key + "background", false);
            EditorApplication.isPaused = SessionState.GetBool(Key + "paused", false);
            if (complete) SessionState.SetBool(Key + "settings", false);
        }
    }
}
