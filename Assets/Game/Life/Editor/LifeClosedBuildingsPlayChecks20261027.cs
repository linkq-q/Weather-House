using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using UnityEngine;
using UnityEngine.UI;
using WeatherHouse.WatersideV2;
using WeatherHousePrototype;

namespace WeatherHouse.Life.Editor
{
    // Run only inside the coordinator's already isolated Play lifecycle. Never starts/stops Play,
    // creates buildings, enables/disables scenery, saves progress or saves the scene.
    public static class LifeClosedBuildingsPlayChecks20261027
    {
        const string Folder = "C:/Users/31483/.codex/visualizations/2026/10/26/01a178cf-479b-7f41-af26-309b1776f6d5/closed-building-play-checks";
        const string Closed = "该房屋暂未开放";
        const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        static readonly string[] Names = { "A01 · 湖畔集雨温室", "A04 · 潮湾船坞风铃楼", "A08 · 白桦林缘树籽库" };
        [Serializable] sealed class Sample
        {
            public string building, side, result, nearestId, nearestKind, hint, primaryAction, skipReason;
            public Vector3 requested, actual;
            public float boundsDistance, nearestDistance;
            public bool safe, uiVisible;
        }
        [Serializable] sealed class BuildingResult
        {
            public string name;
            public Bounds bounds;
            public int safeSamples, shownSamples, uiShownSamples, maskedSamples, eastSamples, eastShown;
            public List<string> nearbyObjects = new List<string>();
        }
        [Serializable] sealed class Report
        {
            public string status, utc, error;
            public string boundary = "Actual public InteractionHint through existing safe player positions around real Renderer.bounds. This proves a whole-building 2.2m XZ boundary, not a measured door threshold or complete doorway traversal. No building/scenery transforms or active flags are changed. Existing action prompts retain priority; overlaps are recorded instead of hidden.";
            public bool playerRestored, heldRestored, inputRestored, cacheRestored, cameraRestored, observationRestored, saveFilesUnchanged;
            public List<string> checks = new List<string>();
            public List<string> cacheRoots = new List<string>();
            public List<Sample> samples = new List<Sample>();
            public List<BuildingResult> buildings = new List<BuildingResult>();
        }
        static Report report;

        public static IEnumerator Run(LifeSession life, Action<string> completed)
        {
            report = new Report { status = "RUNNING", utc = DateTime.UtcNow.ToString("O") };
            Exception failure = null; IEnumerator steps = Steps(life);
            while (true)
            {
                bool more; object next = null;
                try { more = steps.MoveNext(); if (more) next = steps.Current; }
                catch (Exception error) { failure = error; break; }
                if (!more) break;
                yield return next;
            }
            try { (steps as IDisposable)?.Dispose(); } catch (Exception error) { failure = failure ?? error; }
            report.status = failure == null ? "PASS_REAL_CLOSED_BUILDING_HINT_BOUNDS_AND_PRIORITY" : "FAIL_CLOSED_BUILDING_HINT";
            report.error = failure?.ToString();
            Directory.CreateDirectory(Folder);
            string json = Path.Combine(Folder, "play-result.json"), text = Path.Combine(Folder, "play-result.txt");
            File.WriteAllText(json, JsonUtility.ToJson(report, true));
            string result = (failure == null ? "PASS" : "FAIL") + " closed-building hint\n" + string.Join("\n", report.checks) +
                "\nBoundary: " + report.boundary + (failure == null ? "" : "\n" + failure);
            File.WriteAllText(text, result); completed?.Invoke(result);
        }

        static IEnumerator Steps(LifeSession life)
        {
            Require(Application.isPlaying && life && life.World && life.Gameplay && !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WEATHERHOUSE_LIFE_REVIEW_SAVE")), "Existing isolated Play/save is required");
            var game = life.Gameplay; var world = life.World; var hud = life.GetComponent<GameplayHud>(); var mover = game.forestSprite;
            Transform player = game.PlayerTransform;
            var capsule = player ? player.GetComponent<CharacterController>() : null;
            Require(hud && mover && capsule && capsule.enabled && !hud.JournalOpen && !hud.IsDialogueOpen && !hud.CameraMode && !hud.IsAlbumOpening && !game.Judging && !life.GestureInputActive,
                "The prior review has returned to idle gameplay before this independent check");
            Require(Get<object>(world, "feeding") == null && Get<string>(world, "craftingId") == null, "An actual feeding/crafting action will not be interrupted by the fixture");
            Require(!life.Progress.tasks.Any(task => task != null && task.status == LifeTaskStatus.Active), "No active help completion can write progress during the position-only fixture");
            Vector3 position = player.position; Quaternion rotation = player.rotation;
            Camera view = game.PlayerViewCamera; Vector3 cameraPosition = view ? view.transform.position : Vector3.zero; Quaternion cameraRotation = view ? view.transform.rotation : Quaternion.identity;
            LifeObject originalHeld = Get<LifeObject>(world, "held");
            string heldJson = originalHeld ? JsonUtility.ToJson(originalHeld.State) : "";
            Vector3 heldPosition = originalHeld ? originalHeld.transform.position : Vector3.zero; Quaternion heldRotation = originalHeld ? originalHeld.transform.rotation : Quaternion.identity;
            bool scripted = mover.ScriptedInput; Vector2 scriptedMovement = Get<Vector2>(mover, "scriptedMovement");
            bool scriptedRun = Get<bool>(mover, "scriptedRun"), scriptedJump = Get<bool>(mover, "scriptedJump");
            var movementState = new Dictionary<string, object>();
            foreach (string field in new[] { "verticalSpeed", "groundSpeed", "stopElapsed", "stopSpeed", "turnSeconds", "groundDirection", "previousIntent", "modeStarted", "nextJump", "<MovementIntent>k__BackingField", "<ActualVelocity>k__BackingField" })
            { var info = mover.GetType().GetField(field, Hidden); if (info != null) movementState[field] = info.GetValue(mover); }
            var demo = mover.demonstration;
            float nextObservation = Get<float>(game, "nextObservation");
            string notebookBefore = FileHash(game.SavePath), progressBefore = FileHash(life.SavePath);
            bool cacheFound = Get<bool>(world, "closedBuildingsFound"); IList cache = Get<IList>(world, "closedBuildings"); object[] cacheEntries = cache.Cast<object>().ToArray();
            try
            {
                mover.SetScriptedInput(Vector2.zero, false, false);
                // Suppress only automatic observation while the player-position fixture runs,
                // so incidental nearby speech cannot write even the isolated notebook.
                Set(game, "nextObservation", float.PositiveInfinity);
                // Empty hands are a declared temporary player fixture. No held item state or scene object is fabricated.
                Set(world, "held", null);
                Transform[] all = game.gameObject.scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Transform>(true)).ToArray();
                Transform[] roots = Names.Select(name => all.SingleOrDefault(t => t.name == name && t.gameObject.activeInHierarchy)).Where(t => t).ToArray();
                Require(roots.Any(t => t.name == Names[0]) && roots.Any(t => t.name == Names[1]), "The real existing A01 and A04 roots are present");
                Require(!all.Any(t => t.name == Names[2]), "A08 is absent in this scene; no fake building or fallback position is used");
                Require(!all.Any(t => t.name == "A05 · 溪岸水车磨坊"), "Deleted A05 is not restored or invented");
                Bounds[] allTargetBounds = roots.Select(BoundsOf).ToArray();
                foreach (Transform root in roots.Where(t => t.name == Names[0] || t.name == Names[1]))
                {
                    Bounds bounds = BoundsOf(root);
                    var building = new BuildingResult { name = root.name, bounds = bounds }; report.buildings.Add(building);
                    building.nearbyObjects.AddRange(world.Objects.Where(o => o && FlatBounds(o.transform.position, bounds) <= 4.4f)
                        .Select(o => o.State.id + " | " + o.State.kind + " | " + o.transform.position.ToString("F3") + " | flat bounds distance " + FlatBounds(o.transform.position, bounds).ToString("F3")));
                    Vector3? shown = null;
                    foreach (var candidate in Around(bounds, false))
                    {
                        var sample = new Sample { building = root.name, side = candidate.Item1, requested = candidate.Item2 };
                        Vector3 stand; string why;
                        if (!Supported(life, capsule, candidate.Item2, out stand, out why)) { sample.result = "SKIP_UNSUPPORTED_POINT"; sample.skipReason = why; report.samples.Add(sample); continue; }
                        sample.safe = true; player.position = stand; Set(mover, "verticalSpeed", 0f); Physics.SyncTransforms();
                        yield return null; yield return new WaitForEndOfFrame();
                        Read(sample, life, bounds);
                        building.safeSamples++; if (sample.side == "east-bounds-edge") building.eastSamples++;
                        if (sample.hint == Closed)
                        {
                            Require(string.IsNullOrEmpty(sample.nearestId), "Closure fallback never replaces an actual nearby LifeObject action");
                            building.shownSamples++; if (sample.side == "east-bounds-edge") building.eastShown++;
                            if (sample.uiVisible) building.uiShownSamples++;
                            sample.result = "PUBLIC_HINT_SHOWN"; if (!shown.HasValue) shown = player.position;
                        }
                        else if (!string.IsNullOrEmpty(sample.nearestId)) { building.maskedSamples++; sample.result = "EXISTING_ACTION_PROMPT_HAS_PRIORITY"; }
                        else sample.result = "NO_HINT_WITH_EMPTY_HANDS_NO_OBJECT";
                        report.samples.Add(sample);
                    }
                    report.checks.Add("OBSERVED " + root.name + " safe=" + building.safeSamples + " shown=" + building.shownSamples + " maskedByLifeObject=" + building.maskedSamples + " eastSafe=" + building.eastSamples + " eastShown=" + building.eastShown);
                    Require(building.shownSamples > 0, root.name + " exposes the public closure hint at at least one real supported bounds-edge point");
                    Require(building.uiShownSamples > 0, root.name + " also presents that closure text in the active existing bottom hint UI after a real frame");
                    if (building.eastSamples > 0 && building.eastShown == 0) report.checks.Add("REVIEW " + root.name + " all tested east-edge points were masked or blank; this is not proof of a usable doorway prompt");
                    bool left = false;
                    foreach (var candidate in Around(bounds, true))
                    {
                        Vector3 stand; string why;
                        if (!Supported(life, capsule, candidate.Item2, out stand, out why) || allTargetBounds.Any(b => FlatBounds(stand, b) <= 2.3f)) continue;
                        player.position = stand; Set(mover, "verticalSpeed", 0f); Physics.SyncTransforms(); yield return null;
                        Require(world.InteractionHint != Closed, root.name + " closure text disappears outside all real target bounds"); left = true; break;
                    }
                    Require(left, root.name + " has a supported leave point; no water teleport is substituted");
                    player.position = shown.Value; Set(mover, "verticalSpeed", 0f); Physics.SyncTransforms(); yield return null;
                    Require(world.InteractionHint == Closed, root.name + " closure text returns on approaching again");
                    LifeObject heldMaterial = originalHeld ? originalHeld : world.Objects.FirstOrDefault(o => o && (o.State.kind == LifeObjectKind.Mat || o.State.kind == LifeObjectKind.Bundle));
                    if (heldMaterial)
                    {
                        // Getter-only priority probe: no frame advances while an existing material is assigned.
                        Set(world, "held", heldMaterial);
                        try { Require(world.InteractionHint != Closed, "An existing-material held-reference getter fixture preserves its action/drop prompt instead of closure text"); }
                        finally { Set(world, "held", null); }
                    }
                    else report.checks.Add("NOT_EXERCISED carried-material priority: this fresh save has no actual Mat/Bundle; no object was created");
                }
                string[] registered = cache.Cast<object>().Select(entry => (Transform)entry.GetType().GetField("root").GetValue(entry)).Where(t => t).Select(t => t.name).ToArray();
                report.cacheRoots.AddRange(registered);
                Require(registered.All(name => Names.Contains(name)), "Closure cache contains only the three exact building root names, never copied A01 furniture/flowers");
                Transform control = all.SingleOrDefault(t => t.name == "A02 · 望湖风信亭" && t.gameObject.activeInHierarchy);
                Require(control, "An existing A02 provides a real non-target control"); bool controlDone = false;
                foreach (var candidate in Around(BoundsOf(control), false))
                {
                    Vector3 stand; string why;
                    if (!Supported(life, capsule, candidate.Item2, out stand, out why) || allTargetBounds.Any(b => FlatBounds(stand, b) <= 2.3f)) continue;
                    player.position = stand; Set(mover, "verticalSpeed", 0f); Physics.SyncTransforms(); yield return null;
                    Require(world.InteractionHint != Closed, "A02 does not get a fabricated closed-building hint"); controlDone = true; break;
                }
                Require(controlDone, "A supported non-target A02 check point exists");
                Require(report.samples.Where(s => s.safe && !string.IsNullOrEmpty(s.nearestId)).All(s => s.hint != Closed), "All observed overlaps preserve the existing LifeObject prompt priority");
                if (!report.samples.Any(s => s.safe && !string.IsNullOrEmpty(s.nearestId))) report.checks.Add("NOT_EXERCISED perimeter overlap: no actual LifeObject occupied the sampled supported boundary; nearby-object table remains available");
                report.checks.Add("LIMIT No precise door-anchor, all-side traversal, feeding/crafting interruption, or absent-A08-location claim; whole-building XZ bounds is the current production contract");
            }
            finally
            {
                Set(world, "held", originalHeld);
                if (originalHeld) { JsonUtility.FromJsonOverwrite(heldJson, originalHeld.State); originalHeld.transform.SetPositionAndRotation(heldPosition, heldRotation); }
                player.SetPositionAndRotation(position, rotation);
                if (scripted) mover.SetScriptedInput(scriptedMovement, scriptedRun, scriptedJump); else mover.ReleaseScriptedInput();
                Set(mover, "scriptedMovement", scriptedMovement); Set(mover, "scriptedRun", scriptedRun); Set(mover, "scriptedJump", scriptedJump); mover.demonstration = demo;
                foreach (var entry in movementState) Set(mover, entry.Key, entry.Value);
                if (view) view.transform.SetPositionAndRotation(cameraPosition, cameraRotation);
                Set(game, "nextObservation", nextObservation);
                cache.Clear(); foreach (object entry in cacheEntries) cache.Add(entry); Set(world, "closedBuildingsFound", cacheFound);
                Physics.SyncTransforms();
                report.playerRestored = Vector3.Distance(player.position, position) < .0001f && Quaternion.Angle(player.rotation, rotation) < .001f;
                report.heldRestored = Get<LifeObject>(world, "held") == originalHeld && (!originalHeld || JsonUtility.ToJson(originalHeld.State) == heldJson);
                report.inputRestored = mover.ScriptedInput == scripted; report.cacheRestored = cache.Count == cacheEntries.Length && Get<bool>(world, "closedBuildingsFound") == cacheFound;
                report.cameraRestored = !view || Vector3.Distance(view.transform.position, cameraPosition) < .0001f && Quaternion.Angle(view.transform.rotation, cameraRotation) < .001f;
                report.observationRestored = Get<float>(game, "nextObservation") == nextObservation;
                report.saveFilesUnchanged = FileHash(game.SavePath) == notebookBefore && FileHash(life.SavePath) == progressBefore;
            }
            Require(report.playerRestored && report.heldRestored && report.inputRestored && report.cacheRestored && report.cameraRestored && report.observationRestored && report.saveFilesUnchanged,
                "Player, carried state, input, camera, observation timer and initial closure cache restored; isolated notebook/progress files unchanged");
        }

        static IEnumerable<Tuple<string, Vector3>> Around(Bounds b, bool far)
        {
            foreach (float distance in far ? new[] { 6f, 9f } : new[] { .6f, 1.25f, 2f })
            foreach (float fraction in new[] { .25f, .5f, .75f })
                yield return Tuple.Create("east-bounds-edge", new Vector3(b.max.x + distance, b.min.y, Mathf.Lerp(b.min.z, b.max.z, fraction)));
            foreach (Vector3 side in new[] { Vector3.forward, Vector3.back, Vector3.left })
            foreach (float fraction in new[] { .25f, .5f, .75f })
            {
                float distance = far ? 6f : 1.25f;
                Vector3 point = side.x < 0 ? new Vector3(b.min.x - distance, b.min.y, Mathf.Lerp(b.min.z, b.max.z, fraction)) :
                    new Vector3(Mathf.Lerp(b.min.x, b.max.x, fraction), b.min.y, side.z > 0 ? b.max.z + distance : b.min.z - distance);
                yield return Tuple.Create(side.x < 0 ? "west-bounds-edge" : side.z > 0 ? "north-bounds-edge" : "south-bounds-edge", point);
            }
        }
        static bool Supported(LifeSession life, CharacterController capsule, Vector3 desired, out Vector3 point, out string why)
        {
            point = desired; why = ""; Terrain ground = Terrain.activeTerrain; Transform player = life.Gameplay.PlayerTransform;
            if (!ground || !ground.terrainData) { why = "NO_ACTUAL_TERRAIN"; return false; }
            Vector3 local = desired - ground.transform.position, size = ground.terrainData.size;
            if (local.x < 0 || local.z < 0 || local.x > size.x || local.z > size.z || ground.terrainData.GetSteepness(local.x / size.x, local.z / size.z) > 38) { why = "OUTSIDE_OR_STEEP_TERRAIN"; return false; }
            float height = ground.SampleHeight(desired) + ground.transform.position.y;
            var water = player.GetComponent<WatersideWaterBoundary>();
            if (water && water.surface && water.terrain && water.SampleSupportedDepth(desired, height) > water.maximumWadingDepth) { why = "WATER_TOO_DEEP"; return false; }
            float radius = capsule.radius * Mathf.Max(Mathf.Abs(player.lossyScale.x), Mathf.Abs(player.lossyScale.z));
            float capsuleHeight = capsule.height * Mathf.Abs(player.lossyScale.y), half = Mathf.Max(0, capsuleHeight * .5f - radius);
            Vector3 offset = player.TransformVector(capsule.center); point.y = height - offset.y + capsuleHeight * .5f + .06f;
            Vector3 center = point + offset;
            if (Physics.OverlapCapsule(center + Vector3.up * half, center - Vector3.up * half, radius * .95f,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore).Any(c => !(c is TerrainCollider) && !c.transform.IsChildOf(player))) { why = "CAPSULE_OBSTRUCTED"; return false; }
            return true;
        }
        static void Read(Sample sample, LifeSession life, Bounds bounds)
        {
            sample.actual = life.Gameplay.PlayerTransform.position; sample.boundsDistance = FlatBounds(sample.actual, bounds);
            LifeObject nearest = (LifeObject)Call(life.World, "Nearest"); sample.nearestId = nearest ? nearest.State.id : "";
            sample.nearestKind = nearest ? nearest.State.kind.ToString() : ""; sample.nearestDistance = nearest ? LifeWorld.Flat(sample.actual, nearest.transform.position) : -1;
            sample.hint = life.World.InteractionHint; sample.primaryAction = life.GetComponent<GameplayHud>().PrimaryInteraction.ToString();
            var input = life.GetComponent<LifeGestureInput>(); var ui = Get<RectTransform>(input, "hintRoot"); var text = Get<Text>(input, "interactionText");
            sample.uiVisible = ui && ui.gameObject.activeInHierarchy && text && text.text.Contains(Closed);
        }
        static Bounds BoundsOf(Transform t)
        {
            var rs = t.GetComponentsInChildren<Renderer>(true).Where(r => r.enabled && r.gameObject.activeInHierarchy).ToArray();
            if (rs.Length == 0) throw new InvalidOperationException("No current visible building Renderer: " + t.name);
            Bounds bounds = rs[0].bounds; foreach (Renderer r in rs.Skip(1)) bounds.Encapsulate(r.bounds); return bounds;
        }
        static float FlatBounds(Vector3 point, Bounds bounds) { point.y = bounds.center.y; return Vector3.Distance(point, bounds.ClosestPoint(point)); }
        static T Get<T>(object target, string field) => (T)target.GetType().GetField(field, Hidden).GetValue(target);
        static void Set(object target, string field, object value) => target.GetType().GetField(field, Hidden).SetValue(target, value);
        static object Call(object target, string method) => target.GetType().GetMethod(method, Hidden).Invoke(target, null);
        static string FileHash(string path) { if (!File.Exists(path)) return "MISSING"; using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-", ""); }
        static void Require(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); report.checks.Add("PASS " + message); }
    }
}
