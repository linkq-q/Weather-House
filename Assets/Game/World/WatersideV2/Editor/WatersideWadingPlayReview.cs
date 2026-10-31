using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ForestSpriteCharacter;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using WeatherHouse.Hillside;
using Object = UnityEngine.Object;

namespace WeatherHouse.WatersideV2.Editor
{
    /// <summary>Real controller movement and GPU wake observation in a disposable Play session.</summary>
    [InitializeOnLoad]
    public static class WatersideWadingPlayReview
    {
        const string Key = "WeatherHouse.WatersideWadingReview.";
        const string Folder = "WatersideV3Review/wading-play";
        const string ReportPath = Folder + "/checks.json";
        const float ShallowTerrainDepth = .30f;
        enum Phase { Setup, Settle, Dry, WalkIn, ShallowArrival, Stop, PushDeep, Return, DryAgain }
        static Phase phase;
        static Review report;
        static ForestSpriteDemoController player;
        static CharacterController body;
        static WatersideWaterBoundary boundary;
        static Terrain terrain;
        static HillsideWaterInteraction water;
        static Camera camera;
        static Route route;
        static Vector3 previous;
        static float phaseStart, playStart, previousTime;
        static int lastFrame = -1, stopStamps = -1, dryStamps, blockedAtStart, groundedFrames, observedFrames, phaseEntryFrame;
        static bool stopMeasured, returnedToShallow;
        static readonly Collider[] overlaps = new Collider[64];
        static SearchDiagnostics search;
        static readonly Dictionary<string, int> rejected = new Dictionary<string, int>();

        [Serializable] sealed class Review
        {
            public string status = "RUNNING", error = "", startedUtc, finishedUtc, stage = "Setup";
            public string movement = "One initial spawn placement; all subsequent motion uses ForestSpriteDemoController.SetScriptedInput.";
            public string gpuEvidence = "ReadPixels from the actual floating-point normal RT; same-frame renderer MPB strengths 0, 1, and 1.45.";
            public string waterName;
            public Vector3 dryStart, shallowTarget, deepTarget;
            public float routeMetres, shallowTerrainDepth, playSeconds, maxStepMetres, maxHorizontalStepMetres;
            public float maximumSupportedDepth, maximumFootDepth, groundedFraction;
            public int frames, spawnPlacements, dryStampCount, shallowStampCount, stoppedStampCount, returnedDryStampCount;
            public int blockedSteps, rollbackCount, teleportSkips;
            public bool reachedShallow, returnedDry, deepBoundaryBlocked, dryNoStamps, stoppedNoStamps, gpuDecayed;
            public bool returnedToEdit, sceneDirtyAfter, restoredStartScene;
            public bool originalRunInBackground, originalPause, restoredRunInBackground, restoredPause;
            public Field movingField, stopStartField, stoppedField;
            public PixelDifference originalVsOff, strongerVsOff, strongerVsOriginal;
            public float originalAppliedSlopeRms, strongerAppliedSlopeRms;
            public int comparisonFrame;
            public List<string> screenshots = new List<string>();
        }
        [Serializable] sealed class Field
        {
            public float energy, maximum, slopeRms, strongerSlopeRms;
            public Vector2 peakUV;
            public int width, height, nonNeutralPixels;
        }
        [Serializable] sealed class PixelDifference { public int changedPixels; public double meanAbsoluteRGB; }
        [Serializable] sealed class SearchDiagnostics
        {
            public string method = "Actual terrain coast transects with depth-contour bisection; 2–4 m straight or oblique dry approach; collision checked only to the reachable 0.4 m boundary.";
            public int transects, shallowCandidates, dryCandidates, accepted;
            public Vector3 selectedDry, selectedShallow, selectedDeep;
            public List<string> rejectionCounts = new List<string>();
            public List<string> examples = new List<string>();
        }
        sealed class Route { public Vector3 dry, wet, deep; public float score; }
        static bool Active => SessionState.GetBool(Key + "active", false);
        static bool Ending => SessionState.GetBool(Key + "ending", false);

        static WatersideWadingPlayReview()
        {
            EditorApplication.update += Update;
            EditorApplication.playModeStateChanged += PlayChanged;
            Application.logMessageReceived += OnLog;
        }

        public static void Begin()
        {
            if (Active) throw new InvalidOperationException("Wading review is already active.");
            WatersideSceneBuilder.RequireSaved();
            if (SceneManager.GetActiveScene().path != WatersideSceneBuilder.ScenePath)
                throw new InvalidOperationException("Open the saved Waterside V2 scene first.");
            Directory.CreateDirectory(Folder);
            report = new Review { startedUtc = DateTime.UtcNow.ToString("O"),
                originalRunInBackground = Application.runInBackground, originalPause = EditorApplication.isPaused };
            SessionState.SetBool(Key + "runInBackground", Application.runInBackground);
            SessionState.SetBool(Key + "pause", EditorApplication.isPaused);
            SessionState.SetString(Key + "startScene", AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene));
            SessionState.SetString(Key + "beginTicks", DateTime.UtcNow.Ticks.ToString());
            SessionState.SetString(Key + "runtimeError", "");
            SessionState.SetBool(Key + "active", true); SessionState.SetBool(Key + "ending", false);
            SessionState.SetBool(Key + "success", false); SessionState.SetBool(Key + "entered", false);
            try
            {
                Save();
                File.WriteAllText(Folder + "/trace.csv", "simulation_seconds,frame,phase,x,y,z,terrain_depth,supported_depth,foot_depth,step_metres,horizontal_step,grounded,in_water,total_stamps,last_stamps,blocked_steps,rollback_count\n");
                Application.runInBackground = true; EditorApplication.isPaused = false;
                EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(WatersideSceneBuilder.ScenePath);
                EditorApplication.isPlaying = true;
            }
            catch (Exception error) { Finish(error); }
        }

        public static string DiagnoseRoute()
        {
            Require(!Active, "Do not run the route diagnostic during the Play review.");
            WatersideSceneBuilder.RequireSaved();
            Require(SceneManager.GetActiveScene().path == WatersideSceneBuilder.ScenePath, "Open the saved Waterside V2 scene first.");
            try
            {
                player = Object.FindObjectOfType<ForestSpriteDemoController>();
                Require(player, "ForestSprite controller is missing.");
                body = player.GetComponent<CharacterController>(); boundary = player.GetComponent<WatersideWaterBoundary>();
                Require(body && boundary && boundary.terrain, "Controller boundary or terrain is missing.");
                terrain = boundary.terrain;
                water = Object.FindObjectsByType<HillsideWaterInteraction>(FindObjectsSortMode.None)
                    .FirstOrDefault(w => w.surface is WatersideWaterSurface s && s.section == -1 && w.isActiveAndEnabled);
                Require(water, "Sea wake component is missing.");
                Directory.CreateDirectory(Folder); Physics.SyncTransforms();
                Route candidate = FindRoute();
                return candidate == null ? "NO ROUTE; inspect " + Folder + "/route-search.json" :
                    "ROUTE FOUND; dry=" + candidate.dry.ToString("F3") + "; shallow=" + candidate.wet.ToString("F3") +
                    "; deep=" + candidate.deep.ToString("F3") + "; metres=" + HorizontalDistance(candidate.dry, candidate.wet).ToString("F2");
            }
            finally { player = null; body = null; boundary = null; terrain = null; water = null; }
        }

        static void PlayChanged(PlayModeStateChange change)
        {
            if (!Active) return;
            if (change == PlayModeStateChange.EnteredPlayMode)
            {
                report = Load(); phase = Phase.Setup; lastFrame = -1; stopStamps = -1; stopMeasured = false; returnedToShallow = false;
                groundedFrames = observedFrames = 0; playStart = Time.time;
                SessionState.SetBool(Key + "entered", true);
            }
            else if (change == PlayModeStateChange.ExitingPlayMode) Cleanup();
            else if (change == PlayModeStateChange.EnteredEditMode)
            {
                report = Load();
                if (!Ending) { report.error = "Play stopped before the wading review completed."; SessionState.SetBool(Key + "success", false); }
                Restore();
            }
        }

        static void Update()
        {
            if (!Active) return;
            try
            {
                if (Ending)
                { if (!EditorApplication.isPlayingOrWillChangePlaymode) { report = Load(); Restore(); } return; }
                if (report == null) report = Load();
                if (Age() > 90) throw new TimeoutException("Wading review exceeded 90 wall-clock seconds including Play entry and screenshots.");
                string logged = SessionState.GetString(Key + "runtimeError", "");
                if (!string.IsNullOrEmpty(logged)) throw new InvalidOperationException("Unity runtime error: " + logged);
                if (!EditorApplication.isPlaying || !SessionState.GetBool(Key + "entered", false)) return;
                EditorApplication.QueuePlayerLoopUpdate();
                if (Time.frameCount == lastFrame) return;
                lastFrame = Time.frameCount; report.frames++; report.playSeconds = Time.time - playStart;
                if (report.playSeconds > 60) throw new TimeoutException("Wading review exceeded 60 simulated seconds.");
                if (phase == Phase.Setup) { if (report.playSeconds >= .25f) Setup(); return; }
                Observe();
                float elapsed = Time.time - phaseStart;
                switch (phase)
                {
                    case Phase.Settle:
                        Stop();
                        if (elapsed >= .8f && player.IsGrounded)
                        { water.ClearField(); dryStamps = water.TotalStampCount; Enter(Phase.Dry); }
                        else Require(elapsed < 5, "Player did not settle on the selected dry ground.");
                        break;
                    case Phase.Dry:
                        Stop();
                        if (elapsed < 1) break;
                        report.dryStampCount = water.TotalStampCount - dryStamps;
                        report.dryNoStamps = report.dryStampCount == 0 && !water.IsInWater;
                        Require(report.dryNoStamps, "Stationary dry ground generated water stamps.");
                        CaptureGame("01_dry.png"); Enter(Phase.WalkIn); break;
                    case Phase.WalkIn:
                        if (!WalkTo(route.wet)) { Require(elapsed < 15, "Unable to walk to shallow water; obstacle or boundary." ); break; }
                        Stop(); Enter(Phase.ShallowArrival); break;
                    case Phase.ShallowArrival:
                        Stop();
                        // Finish queued input and let gravity settle the controller's skin above the actual terrain.
                        if (Time.frameCount <= phaseEntryFrame || elapsed < .18f) break;
                        report.reachedShallow = water.IsInWater && FootDepth() >= .15f && FootDepth() <= .34f;
                        report.shallowStampCount = water.TotalStampCount - dryStamps;
                        Require(report.reachedShallow && report.shallowStampCount > 0, "Real shallow walking did not produce a wet footprint and stamps.");
                        report.movingField = ReadField(); Require(report.movingField.nonNeutralPixels > 0, "GPU wake normal field stayed exactly neutral while walking.");
                        CaptureGame("02_shallow_walking.png"); CaptureStrengthComparison(); Enter(Phase.Stop); break;
                    case Phase.Stop:
                        Stop();
                        // Allow the already queued final movement frame to finish before measuring stationary stamps.
                        if (!stopMeasured && elapsed >= .5f)
                        { stopStamps = water.TotalStampCount; report.stopStartField = ReadField(); stopMeasured = true; Save(); }
                        if (elapsed < 5.5f) break;
                        report.stoppedStampCount = water.TotalStampCount - stopStamps;
                        report.stoppedNoStamps = report.stoppedStampCount == 0;
                        report.stoppedField = ReadField();
                        report.gpuDecayed = report.stopStartField.energy > 0 && report.stoppedField.energy < report.stopStartField.energy;
                        CaptureGame("03_shallow_stopped.png");
                        Require(report.stoppedNoStamps, "Standing still kept stamping water.");
                        Require(report.gpuDecayed, "Actual GPU normal energy did not decrease after standing still.");
                        blockedAtStart = boundary.BlockedStepCount; Enter(Phase.PushDeep); break;
                    case Phase.PushDeep:
                        WalkTo(route.deep);
                        if (boundary.BlockedStepCount > blockedAtStart && elapsed >= 2)
                        {
                            report.deepBoundaryBlocked = true; report.blockedSteps = boundary.BlockedStepCount;
                            Require(boundary.SampleSupportedDepth(player.transform.position, body.bounds.min.y) <= boundary.maximumWadingDepth + .005f,
                                "Deep-water guard allowed a supported depth greater than its 0.4 m limit.");
                            Stop(); Enter(Phase.Return);
                        }
                        else Require(elapsed < 10, "No deep-water blocking was observed on the scanned route.");
                        break;
                    case Phase.Return:
                        if (!returnedToShallow)
                        {
                            returnedToShallow = WalkTo(route.wet);
                            if (!returnedToShallow) { Require(elapsed < 10, "Player could not return from the boundary to the shallow waypoint."); break; }
                        }
                        if (!WalkTo(route.dry)) { Require(elapsed < 18, "Player could not walk back to dry ground."); break; }
                        Stop(); report.returnedDry = !water.IsInWater && RawDepth(player.transform.position) < -.025f;
                        Require(report.returnedDry, "Return endpoint was not dry.");
                        dryStamps = water.TotalStampCount; Enter(Phase.DryAgain); break;
                    case Phase.DryAgain:
                        Stop(); if (elapsed < 1) break;
                        report.returnedDryStampCount = water.TotalStampCount - dryStamps;
                        Require(report.returnedDryStampCount <= 1, "Dry return generated more than one exit ring.");
                        CaptureGame("04_returned_dry.png");
                        report.rollbackCount = boundary.RollbackCount; report.teleportSkips = water.TeleportSkipCount;
                        Require(report.rollbackCount == 0 && report.teleportSkips == 0, "Water crossing used a rollback or produced a teleport streak skip.");
                        Require(report.groundedFraction > .9f, "More than 10% of observed traversal frames were ungrounded.");
                        Finish(null); break;
                }
            }
            catch (Exception error) { Finish(error); }
        }

        static void Setup()
        {
            player = Object.FindObjectOfType<ForestSpriteDemoController>();
            Require(player && player.isActiveAndEnabled, "Active ForestSprite controller is missing.");
            body = player.GetComponent<CharacterController>(); boundary = player.GetComponent<WatersideWaterBoundary>();
            Require(body && boundary && boundary.terrain && boundary.surface, "Character water boundary or terrain is missing.");
            terrain = boundary.terrain;
            water = Object.FindObjectsByType<HillsideWaterInteraction>(FindObjectsSortMode.None)
                .FirstOrDefault(w => w.surface is WatersideWaterSurface s && s.section == -1 && w.isActiveAndEnabled);
            Require(water && water.player == player, "Active sea wake bound to the actual player is missing.");
            if (!water.CurrentTexture)
            {
                water.ClearField(); // This review reads a neutral GPU field before its first movement; runtime water allocates lazily.
                Require(water.CurrentTexture, "Sea wake could not initialize its GPU field for the review.");
            }
            Require(Mathf.Abs(boundary.maximumWadingDepth - .4f) < .001f, "The authored 0.4 m wading limit changed.");
            Physics.SyncTransforms();
            route = FindRoute(); Require(route != null, "No unobstructed dry-to-shallow coast route of 2–4 m with deeper water beyond was found. See route-search.json for actual rejected candidates.");
            report.waterName = water.name; report.dryStart = route.dry; report.shallowTarget = route.wet; report.deepTarget = route.deep;
            report.routeMetres = HorizontalDistance(route.dry, route.wet); report.shallowTerrainDepth = RawDepth(route.wet);
            Stop(); body.enabled = false;
            player.transform.position = route.dry + Vector3.up * .04f;
            body.enabled = true; Physics.SyncTransforms(); report.spawnPlacements++;
            if (player.secondaryMotion) player.secondaryMotion.ResetSimulation();
            if (player.capeCloth) player.capeCloth.ResetSimulation();
            var go = new GameObject("Wading review camera · Play only") { hideFlags = HideFlags.DontSave };
            camera = go.AddComponent<Camera>(); camera.enabled = false;
            previous = player.transform.position; previousTime = Time.time;
            Enter(Phase.Settle);
        }

        static Route FindRoute()
        {
            Route best = null; search = new SearchDiagnostics(); rejected.Clear();
            try
            {
                for (int segment = 1; segment < WatersideLayout.Coastline.Length; segment++)
                {
                    Vector3 a = WatersideLayout.Coastline[segment - 1], b = WatersideLayout.Coastline[segment];
                    int samples = Mathf.Max(2, Mathf.CeilToInt(HorizontalDistance(a, b) / 1.5f));
                    Vector3 outward = Vector3.Cross(Vector3.up, (b - a).normalized);
                    for (int sample = 1; sample < samples; sample++)
                    {
                        search.transects++;
                        Vector3 coast = Vector3.Lerp(a, b, sample / (float)samples);
                        if (WatersideLayout.SeaSignedDistance(coast.x + outward.x, coast.z + outward.z) > 0) outward = -outward;
                        Vector3 prior = coast - outward * 2;
                        for (float offset = -1.85f; offset <= 4; offset += .15f)
                        {
                            Vector3 point = coast + outward * offset;
                            if (InsideTerrain(prior) && InsideTerrain(point) && RawDepth(prior) < ShallowTerrainDepth && RawDepth(point) >= ShallowTerrainDepth)
                            {
                                // A depth contour can be much narrower than the previous 1 m grid cell.
                                Vector3 low = prior, high = point;
                                for (int iteration = 0; iteration < 12; iteration++)
                                { Vector3 middle = (low + high) * .5f; if (RawDepth(middle) < ShallowTerrainDepth) low = middle; else high = middle; }
                                Vector3 wet = Ground((low + high) * .5f);
                                if (water.surface.Contains(wet)) EvaluateCandidate(wet, outward, ref best);
                                else Reject("shallow contour outside sea surface", wet, point);
                                break;
                            }
                            prior = point;
                        }
                    }
                }
                return best;
            }
            finally
            {
                if (best != null) { search.selectedDry = best.dry; search.selectedShallow = best.wet; search.selectedDeep = best.deep; }
                search.rejectionCounts = rejected.OrderByDescending(p => p.Value).Select(p => p.Key + ": " + p.Value).ToList();
                File.WriteAllText(Folder + "/route-search.json", JsonUtility.ToJson(search, true));
            }
        }

        static void EvaluateCandidate(Vector3 wet, Vector3 outward, ref Route best)
        {
            search.shallowCandidates++;
            Vector3 deep = wet;
            for (float d = .15f; d <= 3.5f; d += .15f)
            {
                Vector3 p = Ground(wet + outward * d);
                if (!InsideTerrain(p)) break;
                if (RawDepth(p) >= .56f && water.surface.Contains(p)) { deep = p; break; }
            }
            if (deep == wet) { Reject("no deeper water beyond contour", wet, deep); return; }
            if (!ClearRoute(wet, deep, true)) return;
            // A diagonal approach includes a useful length of shallow walking without demanding
            // that the character climb the steep inland bank several metres behind the beach.
            foreach (float angle in new[] { 0f, 35f, -35f, 60f, -60f, 75f, -75f })
            {
                Vector3 towardDry = Quaternion.AngleAxis(angle, Vector3.up) * -outward;
                foreach (float distance in new[] { 2.5f, 2f, 3f, 3.5f, 4f })
                {
                    Vector3 dry = Ground(wet + towardDry * distance);
                    if (!InsideTerrain(dry)) { Reject("dry endpoint outside terrain", dry, wet); continue; }
                    if (RawDepth(dry) > -.07f) { Reject("dry endpoint is wet", dry, wet); continue; }
                    search.dryCandidates++;
                    if (ClearRoute(dry, wet, false))
                    {
                        search.accepted++;
                        float score = Mathf.Abs(distance - 2.5f) + Mathf.Abs(dry.y - wet.y) + HorizontalDistance(wet, deep) * .15f;
                        if (best == null || score < best.score) best = new Route { dry = dry, wet = wet, deep = deep, score = score };
                        break; // Other coast samples still compete for a gentler and clearer approach.
                    }
                }
            }
        }

        static bool ClearRoute(Vector3 from, Vector3 to, bool deeper)
        {
            int steps = Mathf.CeilToInt(HorizontalDistance(from, to) / .15f);
            Vector3 last = from;
            for (int i = 0; i <= steps; i++)
            {
                Vector3 p = Ground(Vector3.Lerp(from, to, i / (float)Mathf.Max(1, steps)));
                float depth = RawDepth(p);
                bool atBoundary = deeper && depth > boundary.maximumWadingDepth;
                if (atBoundary)
                {
                    Vector3 low = last, high = p;
                    for (int iteration = 0; iteration < 12; iteration++)
                    { Vector3 middle = (low + high) * .5f; if (RawDepth(middle) <= boundary.maximumWadingDepth) low = middle; else high = middle; }
                    p = Ground(low); depth = RawDepth(p);
                }
                float horizontal = HorizontalDistance(p, last);
                if (horizontal > .001f && Mathf.Abs(p.y - last.y) > horizontal * Mathf.Tan(Mathf.Min(40, body.slopeLimit) * Mathf.Deg2Rad) + .015f)
                { Reject("slope exceeds controller approach", from, p); return false; }
                if (!deeper && depth > .31f) { Reject("approach crosses deep water", from, p); return false; }
                if (depth > .015f && !water.surface.Contains(p)) { Reject("submerged ground outside sea footprint", from, p); return false; }
                float radius = Mathf.Max(body.bounds.size.x, body.bounds.size.z) * .5f + .03f;
                float height = Mathf.Max(radius * 2, body.bounds.size.y);
                int count = Physics.OverlapCapsuleNonAlloc(p + Vector3.up * (radius + .06f),
                    p + Vector3.up * (height - radius + .06f), radius, overlaps, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
                if (count == overlaps.Length) { Reject("overlap buffer full", from, p); return false; }
                for (int c = 0; c < count; c++)
                    if (!(overlaps[c] is TerrainCollider) && overlaps[c] != body && !overlaps[c].transform.IsChildOf(player.transform))
                    { Reject("obstacle " + overlaps[c].name, from, p); return false; }
                if (atBoundary) return true; // The controller must stop here; seabed beyond is deliberately unreachable.
                last = p;
            }
            return true;
        }

        static bool InsideTerrain(Vector3 p)
        {
            Vector3 origin = terrain.transform.position, size = terrain.terrainData.size;
            return p.x > origin.x + .5f && p.x < origin.x + size.x - .5f && p.z > origin.z + .5f && p.z < origin.z + size.z - .5f;
        }
        static void Reject(string reason, Vector3 from, Vector3 point)
        {
            int count; rejected.TryGetValue(reason, out count); rejected[reason] = count + 1;
            if (count < 3 && search.examples.Count < 80)
                search.examples.Add(reason + "; from=" + from.ToString("F3") + "; at=" + point.ToString("F3") + "; depth=" + RawDepth(point).ToString("F4", System.Globalization.CultureInfo.InvariantCulture));
        }

        static Vector3 Ground(Vector3 p) { p.y = terrain.SampleHeight(p) + terrain.transform.position.y; return p; }
        static float RawDepth(Vector3 p) => water.surface.Height(p) - Ground(p).y;
        static float FootDepth() => water.surface.Contains(body.bounds.center) ? Mathf.Max(0, water.surface.Height(body.bounds.center) - body.bounds.min.y) : 0;
        static float HorizontalDistance(Vector3 a, Vector3 b) => Vector3.ProjectOnPlane(a - b, Vector3.up).magnitude;
        static void Stop() { if (player) player.SetScriptedInput(Vector2.zero, false, false); }
        static bool WalkTo(Vector3 target)
        {
            Vector3 delta = Vector3.ProjectOnPlane(target - player.transform.position, Vector3.up);
            if (delta.magnitude < .075f) return true;
            Vector3 forward = player.demoCamera ? Vector3.ProjectOnPlane(player.demoCamera.transform.forward, Vector3.up).normalized : Vector3.forward;
            float amount = Mathf.Clamp01(delta.magnitude / Mathf.Max(.08f, player.walkSpeed * Mathf.Max(Time.deltaTime, .03f) * 1.3f));
            Vector3 direction = delta.normalized * amount;
            player.SetScriptedInput(new Vector2(Vector3.Dot(direction, Vector3.Cross(Vector3.up, forward)), Vector3.Dot(direction, forward)), false, false);
            return false;
        }
        static void Enter(Phase next) { phase = next; phaseStart = Time.time; phaseEntryFrame = Time.frameCount; report.stage = next.ToString(); Save(); }

        static void Observe()
        {
            Vector3 p = player.transform.position;
            float step = Vector3.Distance(p, previous), horizontal = HorizontalDistance(p, previous), dt = Time.time - previousTime;
            float support = boundary.SampleSupportedDepth(p, body.bounds.min.y), foot = FootDepth();
            if (phase != Phase.Settle)
            {
                report.maxStepMetres = Mathf.Max(report.maxStepMetres, step); report.maxHorizontalStepMetres = Mathf.Max(report.maxHorizontalStepMetres, horizontal);
                report.maximumSupportedDepth = Mathf.Max(report.maximumSupportedDepth, support); report.maximumFootDepth = Mathf.Max(report.maximumFootDepth, foot);
                observedFrames++; if (player.IsGrounded) groundedFrames++;
                report.groundedFraction = groundedFrames / (float)observedFrames;
                Require(horizontal <= player.walkSpeed * dt + .16f, "Actual horizontal displacement jumped beyond the controller's continuous movement budget.");
                Require(support <= boundary.maximumWadingDepth + .005f, "Actual traversal exceeded the supported wading-depth limit.");
            }
            File.AppendAllText(Folder + "/trace.csv", FormattableString.Invariant($"{Time.time - playStart:F4},{Time.frameCount},{phase},{p.x:F5},{p.y:F5},{p.z:F5},{RawDepth(p):F5},{support:F5},{foot:F5},{step:F5},{horizontal:F5},{player.IsGrounded},{water.IsInWater},{water.TotalStampCount},{water.LastStampCount},{boundary.BlockedStepCount},{boundary.RollbackCount}\n"));
            previous = p; previousTime = Time.time;
        }

        static Field ReadField()
        {
            RenderTexture source = water.CurrentTexture, prior = RenderTexture.active;
            Require(source && source.IsCreated(), "GPU wake normal render texture is unavailable.");
            var read = new Texture2D(source.width, source.height, TextureFormat.RGBAFloat, false, true);
            try
            {
                RenderTexture.active = source; read.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0); read.Apply();
                Color[] pixels = read.GetPixels(); var field = new Field { width = source.width, height = source.height };
                double sum = 0, slopeSquare = 0, strongerSquare = 0;
                for (int i = 0; i < pixels.Length; i++)
                {
                    Color p = pixels[i];
                    Require(!float.IsNaN(p.r) && !float.IsInfinity(p.r) && !float.IsNaN(p.g) && !float.IsInfinity(p.g) &&
                        !float.IsNaN(p.b) && !float.IsInfinity(p.b), "GPU normals contain non-finite values.");
                    float x = p.r * 2 - 1, z = p.g * 2 - 1, up = Mathf.Max(.35f, p.b * 2 - 1);
                    float energy = Mathf.Abs(x) + Mathf.Abs(z); sum += energy;
                    if (energy > 0) field.nonNeutralPixels++;
                    if (energy > field.maximum)
                    { field.maximum = energy; field.peakUV = new Vector2((i % source.width + .5f) / source.width, (i / source.width + .5f) / source.height); }
                    float sx = x / up, sz = z / up;
                    slopeSquare += Mathf.Pow(Mathf.Clamp(sx, -.65f, .65f), 2) + Mathf.Pow(Mathf.Clamp(sz, -.65f, .65f), 2);
                    strongerSquare += Mathf.Pow(Mathf.Clamp(sx * 1.45f, -.65f, .65f), 2) + Mathf.Pow(Mathf.Clamp(sz * 1.45f, -.65f, .65f), 2);
                }
                field.energy = (float)(sum / pixels.Length); field.slopeRms = (float)Math.Sqrt(slopeSquare / pixels.Length);
                field.strongerSlopeRms = (float)Math.Sqrt(strongerSquare / pixels.Length); return field;
            }
            finally { RenderTexture.active = prior; Object.DestroyImmediate(read); }
        }

        static void CaptureGame(string name)
        {
            Camera main = Camera.main; Require(main, "Main Game camera is unavailable.");
            camera.CopyFrom(main); camera.transform.SetPositionAndRotation(main.transform.position, main.transform.rotation);
            camera.enabled = false; camera.targetTexture = null;
            var sourceData = main.GetUniversalAdditionalCameraData();
            var reviewData = camera.GetUniversalAdditionalCameraData();
            reviewData.requiresDepthTexture = true; reviewData.renderPostProcessing = sourceData.renderPostProcessing;
            reviewData.renderShadows = sourceData.renderShadows; reviewData.volumeLayerMask = sourceData.volumeLayerMask;
            reviewData.volumeTrigger = sourceData.volumeTrigger; reviewData.antialiasing = sourceData.antialiasing;
            reviewData.antialiasingQuality = sourceData.antialiasingQuality;
            Capture(name);
        }

        static void CaptureStrengthComparison()
        {
            var saved = new MaterialPropertyBlock(); water.waterRenderer.GetPropertyBlock(saved);
            Require(saved.GetTexture("_InteractionNormalTex") == water.CurrentTexture, "Renderer is not bound to the actual GPU wake field.");
            Require(Mathf.Abs(saved.GetFloat("_InteractionStrength") - 1.45f) < .001f, "Actual authored wake strength is not 1.45.");
            Field field = report.movingField;
            Vector4 rect = water.WorldRect;
            Vector3 center = new Vector3(rect.x + field.peakUV.x / rect.z, 0, rect.y + field.peakUV.y / rect.w);
            center.y = water.surface.Height(center);
            camera.orthographic = false; camera.fieldOfView = 40; camera.nearClipPlane = .03f; camera.farClipPlane = 100;
            Vector3 offset = new Vector3(2.8f, 4.4f, -4.6f);
            camera.transform.SetPositionAndRotation(center + offset, Quaternion.LookRotation(-offset, Vector3.up));
            report.comparisonFrame = Time.frameCount;
            try
            {
                var block = new MaterialPropertyBlock(); water.waterRenderer.GetPropertyBlock(block);
                block.SetFloat("_InteractionStrength", 0); water.waterRenderer.SetPropertyBlock(block); Color32[] off = Capture("05_wake_strength_0.png");
                block.SetFloat("_InteractionStrength", 1); water.waterRenderer.SetPropertyBlock(block); Color32[] original = Capture("06_wake_strength_1.png");
                block.SetFloat("_InteractionStrength", 1.45f); water.waterRenderer.SetPropertyBlock(block); Color32[] stronger = Capture("07_wake_strength_1_45.png");
                Require(Time.frameCount == report.comparisonFrame, "Wake strength comparison advanced a simulation frame.");
                report.originalVsOff = Difference(original, off); report.strongerVsOff = Difference(stronger, off); report.strongerVsOriginal = Difference(stronger, original);
                report.originalAppliedSlopeRms = field.slopeRms; report.strongerAppliedSlopeRms = field.strongerSlopeRms;
                Require(field.strongerSlopeRms > field.slopeRms, "Increasing normal strength did not increase the measured GPU field's applied slope magnitude.");
                Require(report.originalVsOff.changedPixels > 0 && report.strongerVsOriginal.changedPixels > 0, "Same-frame actual water renders did not respond to wake strength.");
            }
            finally { water.waterRenderer.SetPropertyBlock(saved); }
        }

        static Color32[] Capture(string file)
        {
            var target = RenderTexture.GetTemporary(1600, 1000, 24, RenderTextureFormat.ARGB32);
            var pixels = new Texture2D(1600, 1000, TextureFormat.RGB24, false);
            RenderTexture active = RenderTexture.active, previousTarget = camera.targetTexture;
            try
            {
                camera.targetTexture = target; camera.aspect = 1.6f;
                foreach (var grass in Object.FindObjectsByType<HillsideGrassRenderer>(FindObjectsSortMode.None)) grass.DrawForCamera(camera, true);
                camera.Render(); RenderTexture.active = target; pixels.ReadPixels(new Rect(0, 0, 1600, 1000), 0, 0); pixels.Apply();
                File.WriteAllBytes(Folder + "/" + file, pixels.EncodeToPNG()); report.screenshots.Add(file); return pixels.GetPixels32();
            }
            finally { camera.targetTexture = previousTarget; RenderTexture.active = active; RenderTexture.ReleaseTemporary(target); Object.DestroyImmediate(pixels); }
        }

        static PixelDifference Difference(Color32[] a, Color32[] b)
        {
            var result = new PixelDifference(); double sum = 0;
            for (int i = 0; i < a.Length; i++)
            { int d = Math.Abs(a[i].r - b[i].r) + Math.Abs(a[i].g - b[i].g) + Math.Abs(a[i].b - b[i].b); if (d != 0) result.changedPixels++; sum += d; }
            result.meanAbsoluteRGB = sum / (a.Length * 765.0); return result;
        }

        static void OnLog(string message, string trace, LogType type)
        {
            if (Active && !Ending && EditorApplication.isPlaying && (type == LogType.Error || type == LogType.Exception || type == LogType.Assert))
                SessionState.SetString(Key + "runtimeError", message + "\n" + trace);
        }
        static double Age() => long.TryParse(SessionState.GetString(Key + "beginTicks", "0"), out long ticks) ?
            (DateTime.UtcNow.Ticks - ticks) / (double)TimeSpan.TicksPerSecond : double.MaxValue;
        static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        static Review Load() => File.Exists(ReportPath) ? JsonUtility.FromJson<Review>(File.ReadAllText(ReportPath)) : new Review();
        static void Save() => File.WriteAllText(ReportPath, JsonUtility.ToJson(report, true));

        static void Finish(Exception error)
        {
            if (!Active || Ending) return;
            if (report == null) report = Load();
            SessionState.SetBool(Key + "ending", true); SessionState.SetBool(Key + "success", error == null);
            report.error = error == null ? "" : error.ToString(); report.status = "RETURNING_TO_EDIT";
            try { Cleanup(); Save(); }
            finally
            {
                EditorApplication.isPaused = false;
                if (EditorApplication.isPlayingOrWillChangePlaymode) EditorApplication.isPlaying = false;
                else Restore();
            }
        }

        static void Cleanup()
        {
            if (player) { player.SetScriptedInput(Vector2.zero, false, false); player.ReleaseScriptedInput(); }
            if (camera) Object.DestroyImmediate(camera.gameObject);
            camera = null; player = null; body = null; boundary = null; water = null; terrain = null; route = null;
        }

        static void Restore()
        {
            try
            {
                Application.runInBackground = SessionState.GetBool(Key + "runInBackground", false);
                EditorApplication.isPaused = SessionState.GetBool(Key + "pause", false);
                string original = SessionState.GetString(Key + "startScene", "");
                EditorSceneManager.playModeStartScene = string.IsNullOrEmpty(original) ? null : AssetDatabase.LoadAssetAtPath<SceneAsset>(original);
                Cleanup(); report.returnedToEdit = !EditorApplication.isPlaying; report.sceneDirtyAfter = SceneManager.GetActiveScene().isDirty;
                report.restoredRunInBackground = Application.runInBackground; report.restoredPause = EditorApplication.isPaused;
                report.restoredStartScene = AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene) == original;
                report.status = SessionState.GetBool(Key + "success", false) && report.returnedToEdit && !report.sceneDirtyAfter && report.restoredStartScene &&
                    report.restoredRunInBackground == report.originalRunInBackground && report.restoredPause == report.originalPause ? "PASS" : "FAIL";
                report.finishedUtc = DateTime.UtcNow.ToString("O"); Save();
            }
            finally { SessionState.SetBool(Key + "active", false); SessionState.SetBool(Key + "ending", false); }
        }
    }
}
