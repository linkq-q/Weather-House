using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
using WeatherHouse.Hillside;
using WeatherHouse.Vegetation;
using WeatherHouse.WatersideV2;
using WeatherHouse.WildlifeJourney;
using WeatherHousePrototype;

namespace WeatherHouse.Life.Editor
{
    // The parent owns Play mode. Only starting positions, topics and initial object states are fixtures.
    public static class LifePhotoInteractionChecks
    {
        const string Folder = "LifeGameplayReview/20261026/photo-flow";
        const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        [Serializable] sealed class Report
        {
            public string status = "RUNNING", utc = DateTime.UtcNow.ToString("o"), error;
            public string boundary = "Isolated formal-scene Play. Initial player positions, authored topic selection and wet-mat/empty-bowl states are arranged. HUD visibility drives public events; original scripted player input, CharacterController and local weather advance over real frames. No completed context or reward is injected. No AI, human keyboard, gesture recognition or composition acceptance is claimed.";
            public float seconds;
            public List<string> checks = new List<string>();
            public List<string> observations = new List<string>();
            public List<LifePublicContext> emitted = new List<LifePublicContext>();
        }

        public static IEnumerator Run(LifeSession life, Action<string> completed)
        {
            var report = new Report(); float began = Time.realtimeSinceStartup;
            IEnumerator steps = Steps(life, report); Exception failure = null;
            while (true)
            {
                bool more; object next = null;
                try
                {
                    if (Time.realtimeSinceStartup - began > 90) throw new TimeoutException("Photography interactions exceeded 90 seconds.");
                    more = steps.MoveNext(); if (more) next = steps.Current;
                }
                catch (Exception error) { failure = error; break; }
                if (!more) break;
                yield return next;
            }
            try { (steps as IDisposable)?.Dispose(); } catch (Exception error) { failure = failure ?? error; }
            report.seconds = Time.realtimeSinceStartup - began;
            report.status = failure == null ? "PASS" : "FAIL"; report.error = failure?.ToString();
            Directory.CreateDirectory(Folder);
            File.WriteAllText(Folder + "/interaction-play-checks.json", JsonUtility.ToJson(report, true));
            string text = report.status + " photography interactions\n" + string.Join("\n", report.checks.Select(c => "PASS " + c)) +
                (failure == null ? "" : "\n" + failure);
            File.WriteAllText(Folder + "/interaction-play-checks.txt", text);
            completed?.Invoke(text);
        }

        static IEnumerator Steps(LifeSession life, Report report)
        {
            string isolated = Environment.GetEnvironmentVariable("WEATHERHOUSE_LIFE_REVIEW_SAVE");
            Check(report, Application.isPlaying && life && life.World && !string.IsNullOrEmpty(isolated) &&
                string.Equals(Path.GetFullPath(isolated), Path.GetFullPath(GameplaySession.PersistenceDirectory), StringComparison.OrdinalIgnoreCase),
                "Interaction checks require the running formal scene and isolated persistence");
            var game = life.Gameplay; var hud = game.GetComponent<GameplayHud>(); var world = life.World;
            var mover = game.forestSprite; var capsule = mover ? mover.GetComponent<CharacterController>() : null;
            Check(report, mover && capsule && capsule.enabled && !mover.ScriptedInput && !game.Judging &&
                !hud.JournalOpen && !hud.IsDialogueOpen && !hud.CameraMode && !game.Recorder.IsRecording,
                "The real player controller and HUD start idle with collision enabled");
            var weather = UnityEngine.Object.FindObjectOfType<LifeLocalWeather>();
            string notebook = JsonUtility.ToJson(game.Notebook), progress = JsonUtility.ToJson(life.Progress), worldJson = world.CaptureJson(), status = game.Status;
            byte[] lifeFile = File.Exists(life.SavePath) ? File.ReadAllBytes(life.SavePath) : null;
            Vector3 position = mover.transform.position; Quaternion rotation = mover.transform.rotation;
            AnimalActor observed = game.Observed;
            var orbit = game.PlayerViewCamera.GetComponent<VegetationTopDownCamera>();
            float orbitYaw = orbit ? orbit.yawOffset : 0, orbitPitch = orbit ? orbit.pitch : 0, orbitDistance = orbit ? orbit.distance : 0;
            var topics = world.Requests.Select(r => world.FindSpeaker(r.speakerKey)).Where(s => s).Distinct().ToDictionary(s => s, s => s.topicIndex);
            var movementState = mover.GetType().GetFields(Hidden | BindingFlags.DeclaredOnly).Where(f => !f.IsInitOnly && f.FieldType.IsValueType)
                .ToDictionary(f => f, f => f.GetValue(mover));
            Action<LifePublicContext> observe = value => report.emitted.Add(value.Copy());
            world.PublicPhotoContext += observe;
            try
            {
                weather.Cast("clear", out _);
                JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(new LanguageNotebook()), game.Notebook); game.Notebook.Normalize(game.Catalog);
                JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(new LifeProgress()), life.Progress);
                world.RestorePublicPhotoContexts(Array.Empty<LifePublicContext>());
                Check(report, game.SaveNotebook(), "Fresh unknown interaction rewards are saved before exercising their sources");
                var earned = new HashSet<string>(new[] { "want", "reject" });

                foreach (object step in Show("sheep.self.deer", "roles")) yield return step;
                Check(report, !game.Notebook.IsPhotoTaskComplete("I01"), "One visible speaker-listener relation cannot complete I01");
                foreach (object step in Show("deer.self.sheep", "roles")) yield return step;
                foreach (object step in Await(() => game.Notebook.IsPhotoTaskComplete("I01"), 3, "I01 reciprocal public roles")) yield return step;
                var roles = game.Notebook.publicHistory.Where(c => c.kind == "roles").ToArray();
                Check(report, roles.Length == 2 && roles[0].actorId == roles[1].listenerId && roles[0].listenerId == roles[1].actorId &&
                    roles[0].actorId != roles[1].actorId, "Two real coastal actors exchange the authored speaker and listener roles");
                Verify("I01", "self", "you");
                foreach (object step in Show("sheep.self.deer", "roles")) yield return step;
                foreach (object step in Show("deer.self.sheep", "roles")) yield return step;
                Check(report, report.emitted.Count(c => c.kind == "interaction" && c.targetId == "I01") == 1,
                    "Replaying the same real role exchange cannot emit a second I01 reward");

                LifeRequest invitation = world.Requests.Single(r => r.id == "sheep.here");
                Vector3 here = world.AnchorPosition(invitation.targetId), tide = world.AnchorPosition("harbor.tide");
                Vector3 approach = here + (tide - here).normalized * 2.2f;
                hud.CloseDialogue(); Place(approach);
                foreach (object step in Walk(here, "Uninvited approach")) yield return step;
                Check(report, !game.Notebook.IsPhotoTaskComplete("I02"), "Walking to the coastal point without its public invitation gives no I02 reward");
                Place(approach);
                foreach (object step in Show(invitation.id, "invitation", false)) yield return step;
                LifePublicContext firstInvitation = game.Notebook.publicHistory.Single(c => c.kind == "invitation").Copy();
                Check(report, !game.Notebook.IsPhotoTaskComplete("I02"), "The visible invitation alone does not grant come or here");
                hud.CloseDialogue();
                foreach (object step in Walk(here, "I02 invited approach")) yield return step;
                Check(report, report.emitted.TakeWhile(c => c.kind != "interaction" || c.targetId != "I02")
                    .Where(c => c.id == firstInvitation.id).All(c => c.origin == firstInvitation.origin && c.anchor == firstInvitation.anchor),
                    "Looking between animals during an unfinished invitation preserves its original departure point and anchor");
                foreach (object step in Await(() => game.Notebook.IsPhotoTaskComplete("I02"), 3, "I02 arrival event")) yield return step;
                Verify("I02", "come", "here");

                LifeRequest route = world.Requests.Single(r => r.id == "fox.go"); LifeSpeaker fox = world.FindSpeaker(route.speakerKey);
                Vector3 tree = world.AnchorPosition(route.targetId), toward = tree - fox.transform.position; toward.y = 0; toward.Normalize();
                Place(fox.transform.position - toward * .75f + Vector3.Cross(Vector3.up, toward) * 1.25f);
                foreach (object step in Show(route.id, "tree-route", false)) yield return step;
                Check(report, !game.Notebook.IsPhotoTaskComplete("I03") && LifeWorld.Flat(mover.transform.position, tree) > 1.15f,
                    "The visible tree direction is distinct from arrival and cannot grant go early");
                hud.CloseDialogue();
                foreach (object step in Walk(tree, "I03 tree arrival", true)) yield return step;
                foreach (object step in Await(() => game.Notebook.IsPhotoTaskComplete("I03"), 3, "I03 arrival event")) yield return step;
                Verify("I03", "go");

                LifeObject bowl = world.Objects.Single(o => o.State.id == "squirrel.common.bowl");
                LifeObject mat = world.Objects.Single(o => o.State.id == "squirrel.wet");
                foreach (LifeObject item in world.Objects)
                {
                    if (item.State.kind == LifeObjectKind.Bowl) item.State.water = 0;
                    if (item.State.kind == LifeObjectKind.Mat) item.State.wetness = 1;
                }
                mat.State.held = false; mat.State.slot = ""; mat.State.sheltered = false;
                mat.State.position = mat.transform.position = bowl.transform.position + Vector3.right * .8f;
                mat.RefreshVisual(Time.time); bowl.RefreshVisual(Time.time);
                foreach (object step in Show("squirrel.sequence", "water-plan")) yield return step;
                Check(report, !game.Notebook.IsPhotoTaskComplete("I04"), "A public two-step plan with a wet mat and empty bowl does not grant first or then");
                hud.CloseDialogue(); Place(bowl.transform.position + Vector3.forward * 1.8f);
                Check(report, weather.Cast("rain", out _), "The production local weather controller starts rain over the arranged objects");
                foreach (object step in Await(() => bowl.State.water >= .35f, 9, "I04 real rainfall filling the bowl")) yield return step;
                Check(report, !mat.State.IsDry && !game.Notebook.IsPhotoTaskComplete("I04"), "Retained water with a still-wet mat cannot complete I04");
                Check(report, weather.Cast("sunlight", out _), "The same local controller replaces rain with sunlight");
                foreach (object step in Await(() => mat.State.IsDry && game.Notebook.IsPhotoTaskComplete("I04"), 25, "I04 real drying and public result")) yield return step;
                Check(report, bowl.State.water >= .3f && bowl.State.rainStrength == 0 && !mat.State.held,
                    "Actual elapsed sunlight dries the mat while the bowl retains its rainwater");
                Verify("I04", "first", "then");
                Check(report, life.Progress.tasks.Count == 0 && life.Progress.understoodRequests.Count == 0 && game.Notebook.observations.Count == 0,
                    "All four interaction rewards use public participation without invented AI acceptance or photographs");
                Check(report, game.Notebook.ApplyPhotoTasks() == 0 && game.Notebook.completedPhotoTasks.Count == 4,
                    "Rechecking completed public events cannot duplicate rewards");

                void Verify(string id, params string[] rewards)
                {
                    foreach (string reward in rewards) earned.Add(reward);
                    Check(report, game.Notebook.IsPhotoTaskComplete(id) && rewards.All(word => game.Notebook.GetWord(word).confirmed) &&
                        report.emitted.Count(c => c.kind == "interaction" && c.targetId == id && c.completed) == 1,
                        id + " reaches the notebook through exactly one real completed public event");
                    Check(report, new HashSet<string>(game.Notebook.words.Where(w => w.confirmed).Select(w => w.glyphId)).SetEquals(earned),
                        id + " grants only its preset meanings and preserves earlier rewards");
                    var saved = JsonUtility.FromJson<LanguageNotebook>(File.ReadAllText(game.SavePath));
                    Check(report, string.IsNullOrEmpty(game.LastSaveError) && saved.IsPhotoTaskComplete(id) &&
                        rewards.All(word => saved.GetWord(word).confirmed) && saved.publicHistory.Any(c => c.kind == "interaction" && c.targetId == id && c.completed),
                        id + " public evidence and meanings survive the actual notebook disk round trip");
                }
                IEnumerable<object> Show(string requestId, string kind, bool arrange = true)
                {
                    hud.CloseDialogue(); LifeRequest request = world.Requests.Single(r => r.id == requestId); LifeSpeaker speaker = world.FindSpeaker(request.speakerKey);
                    if (arrange) Place(speaker.transform.position + Vector3.right * 1.8f);
                    if (orbit)
                    {
                        Vector3 direction = speaker.Actor.BubbleAnchor - mover.transform.position;
                        orbit.StepOrbit(Mathf.DeltaAngle(orbit.Yaw, Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg), 0, 0);
                        orbit.SnapToPlayer();
                    }
                    speaker.topicIndex = Array.FindIndex(speaker.requests, r => r.id == requestId);
                    Check(report, speaker.Current?.id == requestId && life.CanTalk(speaker.Actor), "Authored topic is reachable from fresh progress: " + requestId);
                    typeof(GameplaySession).GetProperty("Observed").SetValue(game, speaker.Actor); hud.ToggleDialogue();
                    string id = kind + ":" + requestId + ":" + speaker.Actor.animalId;
                    string prior = game.Notebook.publicHistory.FirstOrDefault(c => c.id == id)?.occurredUtc;
                    foreach (object step in Await(() => hud.IsAnimalSpeechVisible(speaker.Actor), 4,
                        "Actual visible HUD bubble " + requestId)) yield return step;
                    foreach (object step in Await(() => game.Notebook.publicHistory.FirstOrDefault(c => c.id == id)?.occurredUtc != prior,
                        4, "Visible dialogue source " + requestId)) yield return step;
                    Check(report, hud.IsDialogueOpen && hud.DialogueTarget == speaker.Actor && hud.IsAnimalSpeechVisible(speaker.Actor) &&
                        game.Notebook.publicHistory.Single(c => c.id == id).glyphIds.SequenceEqual(request.glyphIds),
                        "Actual HUD dialogue publishes its authored public context: " + requestId);
                }
                void Place(Vector3 at)
                {
                    mover.SetScriptedInput(Vector2.zero, false, false);
                    Terrain terrain = Terrain.activeTerrain; if (terrain) at.y = terrain.SampleHeight(at) + terrain.transform.position.y;
                    float bottom = mover.transform.TransformVector(capsule.center).y - capsule.height * Mathf.Abs(mover.transform.lossyScale.y) * .5f;
                    mover.transform.position = at + Vector3.up * (-bottom + .08f); Physics.SyncTransforms();
                }
                IEnumerable<object> Walk(Vector3 destination, string label, bool allowDetour = false)
                {
                    Vector3 start = mover.transform.position; float deadline = Time.realtimeSinceStartup + 15;
                    Vector3 sampled = start; float nextSample = Time.realtimeSinceStartup + .7f;
                    List<Vector3> path = null; int waypoint = 0, detours = 0;
                    while (LifeWorld.Flat(mover.transform.position, destination) > .95f)
                    {
                        if (Time.realtimeSinceStartup > deadline) throw new TimeoutException(label + " blocked at " + mover.transform.position + " for " + destination);
                        if (Time.realtimeSinceStartup >= nextSample)
                        {
                            if (allowDetour && LifeWorld.Flat(sampled, mover.transform.position) < .06f)
                            {
                                if (++detours > 2) throw new InvalidOperationException(label + " remained obstructed after two collision-checked detours.");
                                path = Detour(destination, label); waypoint = 1;
                            }
                            sampled = mover.transform.position; nextSample = Time.realtimeSinceStartup + .7f;
                        }
                        if (path != null)
                            while (waypoint < path.Count - 1 && LifeWorld.Flat(mover.transform.position, path[waypoint]) < .08f) waypoint++;
                        Vector3 aim = path == null ? destination : path[waypoint];
                        Camera camera = mover.MovementCameraOverride ? mover.MovementCameraOverride : mover.demoCamera;
                        Vector3 forward = Vector3.ProjectOnPlane(camera.transform.forward, Vector3.up).normalized;
                        Vector3 direction = aim - mover.transform.position; direction.y = 0; direction.Normalize();
                        mover.SetScriptedInput(new Vector2(Vector3.Dot(direction, Vector3.Cross(Vector3.up, forward)), Vector3.Dot(direction, forward)), false, false);
                        yield return null;
                    }
                    mover.SetScriptedInput(Vector2.zero, false, false); yield return null;
                    report.observations.Add(label + ": from=" + start.ToString("F3") + ", to=" + mover.transform.position.ToString("F3") +
                        ", target=" + destination.ToString("F3") + ", distance=" + LifeWorld.Flat(mover.transform.position, destination).ToString("F3"));
                    Check(report, capsule.enabled && LifeWorld.Flat(start, mover.transform.position) > .5f &&
                        LifeWorld.Flat(mover.transform.position, destination) < 1.15f && Mathf.Abs(mover.transform.position.y - destination.y) < 1.5f,
                        label + " uses real scripted input, collision and elapsed controller movement");
                }
                List<Vector3> Detour(Vector3 destination, string label)
                {
                    // The test steers around real bodies. It never moves a transform or relaxes the production arrival gate.
                    Vector3 start = mover.transform.position;
                    float radius = capsule.radius * Mathf.Max(Mathf.Abs(mover.transform.lossyScale.x), Mathf.Abs(mover.transform.lossyScale.z));
                    float height = capsule.height * Mathf.Abs(mover.transform.lossyScale.y);
                    Terrain terrain = Terrain.activeTerrain; var boundary = mover.GetComponent<WatersideWaterBoundary>();
                    var obstacles = new List<JourneyCrowdObstacle>();
                    var animals = UnityEngine.Object.FindObjectsOfType<WildlifeAgent>().Where(a => a.isActiveAndEnabled &&
                        a.locomotion == WildlifeLocomotion.Ground && LifeWorld.Flat(a.transform.position, start) < 8).ToArray();
                    foreach (WildlifeAgent animal in animals)
                    {
                        float bodyRadius = animal.BodyEnvelope.ContactRadius(animal.Radius);
                        obstacles.Add(new JourneyCrowdObstacle { center = animal.transform.position, radius = bodyRadius });
                        report.observations.Add(label + " nearby body: " + animal.name + " at=" + animal.transform.position.ToString("F3") +
                            ", radius=" + bodyRadius.ToString("F3") + ", player clearance=" +
                            (LifeWorld.Flat(start, animal.transform.position) - radius - bodyRadius - .12f).ToString("F3"));
                    }
                    foreach (Collider hit in Physics.OverlapSphere(start + Vector3.up * height * .5f, 4, ~0, QueryTriggerInteraction.Ignore).Where(Blocks))
                    {
                        Bounds bounds = hit.bounds;
                        if (bounds.max.y <= start.y + .05f || bounds.min.y >= start.y + height) continue;
                        report.observations.Add(label + " nearby collider: " + hit.name + " center=" + bounds.center.ToString("F3") +
                            ", size=" + bounds.size.ToString("F3"));
                        float bodyRadius = new Vector2(bounds.extents.x, bounds.extents.z).magnitude;
                        if (bodyRadius < 3) obstacles.Add(new JourneyCrowdObstacle { center = bounds.center, radius = bodyRadius });
                    }
                    Vector3 center = mover.transform.TransformPoint(capsule.center), direction = Vector3.ProjectOnPlane(destination - start, Vector3.up).normalized;
                    float half = Mathf.Max(radius, height * .5f);
                    string[] ahead = Physics.CapsuleCastAll(center + Vector3.up * (half - radius), center - Vector3.up * (half - radius),
                        radius, direction, .5f, ~0, QueryTriggerInteraction.Ignore).Where(h => Blocks(h.collider))
                        .Select(h => h.collider.name + "@" + h.distance.ToString("F3")).ToArray();
                    CollisionFlags flags = mover.AnimalCollisionFlags;
                    Vector3 probe = direction * .2f, allowed = mover.ConstrainAnimalDisplacement?.Invoke(probe) ?? probe;
                    mover.AnimalCollisionFlags = flags;
                    report.observations.Add(label + " stalled: player=" + start.ToString("F3") + ", native=" + capsule.collisionFlags +
                        ", animal=" + flags + ", animal probe=" + probe.ToString("F3") + " -> " + allowed.ToString("F3") +
                        ", ahead=" + string.Join(";", ahead) + ", grounded=" + mover.IsGrounded);
                    if (!terrain) throw new InvalidOperationException(label + " has no terrain for a checked detour.");
                    for (int candidate = 0; candidate < 9; candidate++)
                    {
                        Vector3 goal = destination;
                        if (candidate > 0) goal += Quaternion.Euler(0, (candidate - 1) * 45, 0) * -direction * .75f;
                        if (!JourneyCrowdPath.TryFindPath(start, goal, radius, obstacles, ClearGround, out List<Vector3> route)) continue;
                        report.observations.Add(label + " controller detour: " + string.Join(" -> ", route.Select(p => p.ToString("F3"))));
                        return route;
                    }
                    throw new InvalidOperationException(label + " found no local collision-checked detour; diagnostics are in observations.");

                    bool Blocks(Collider hit) => hit && !(hit is TerrainCollider) && hit != capsule && !hit.transform.IsChildOf(mover.transform) &&
                        !Physics.GetIgnoreLayerCollision(capsule.gameObject.layer, hit.gameObject.layer) && !Physics.GetIgnoreCollision(capsule, hit);
                    bool ClearGround(Vector3 from, Vector3 to)
                    {
                        float distance = LifeWorld.Flat(from, to); int samples = Mathf.Max(1, Mathf.CeilToInt(distance / .25f));
                        float prior = terrain.SampleHeight(from) + terrain.transform.position.y;
                        for (int index = 1; index <= samples; index++)
                        {
                            Vector3 point = Vector3.Lerp(from, to, (float)index / samples), local = point - terrain.transform.position;
                            if (local.x < 0 || local.z < 0 || local.x > terrain.terrainData.size.x || local.z > terrain.terrainData.size.z) return false;
                            point.y = terrain.SampleHeight(point) + terrain.transform.position.y;
                            if (Mathf.Abs(point.y - prior) > Mathf.Tan(capsule.slopeLimit * Mathf.Deg2Rad) * distance / samples + .02f ||
                                boundary && boundary.SampleSupportedDepth(point, point.y + .02f) > boundary.maximumWadingDepth) return false;
                            if (Physics.OverlapCapsule(point + Vector3.up * (radius + .04f), point + Vector3.up * Mathf.Max(radius + .04f, height - radius + .04f),
                                radius, ~0, QueryTriggerInteraction.Ignore).Any(Blocks)) return false;
                            prior = point.y;
                        }
                        return true;
                    }
                }
            }
            finally
            {
                var pending = (Dictionary<string, LifePublicContext>)typeof(LifeWorld).GetField("pendingPhotoInteractions", Hidden).GetValue(world);
                var invalid = (HashSet<string>)typeof(LifeWorld).GetField("invalidTargets", Hidden).GetValue(world);
                foreach (LifePublicContext context in pending.Values)
                    report.observations.Add("Pending " + context.id + ": player=" + mover.transform.position.ToString("F3") + ", origin=" + context.origin.ToString("F3") +
                        ", anchor=" + context.anchor.ToString("F3") + ", moved=" + LifeWorld.Flat(mover.transform.position, context.origin).ToString("F3") +
                        ", distance=" + LifeWorld.Flat(mover.transform.position, context.anchor).ToString("F3") + ", invalid=" + invalid.Contains(context.targetId ?? ""));
                world.PublicPhotoContext -= observe; hud.CloseDialogue(); weather.Cast("clear", out _); mover.ReleaseScriptedInput();
                foreach (var field in movementState) field.Key.SetValue(mover, field.Value);
                JsonUtility.FromJsonOverwrite(progress, life.Progress); world.RestoreJson(worldJson);
                JsonUtility.FromJsonOverwrite(notebook, game.Notebook); world.RestorePublicPhotoContexts(game.Notebook.publicHistory);
                foreach (var topic in topics) if (topic.Key) topic.Key.topicIndex = topic.Value;
                foreach (LifeObject item in world.Objects) item.RefreshVisual(Time.time);
                mover.transform.SetPositionAndRotation(position, rotation); Physics.SyncTransforms();
                if (orbit) { orbit.yawOffset = orbitYaw; orbit.pitch = orbitPitch; orbit.distance = orbitDistance; orbit.SnapToPlayer(); }
                typeof(GameplaySession).GetProperty("Observed").SetValue(game, observed); game.SaveNotebook();
                if (lifeFile != null) File.WriteAllBytes(life.SavePath, lifeFile); else if (File.Exists(life.SavePath)) File.Delete(life.SavePath);
                game.Notify(status);
            }
        }

        static IEnumerable<object> Await(Func<bool> ready, float seconds, string label)
        {
            float deadline = Time.realtimeSinceStartup + seconds;
            while (!ready()) { if (Time.realtimeSinceStartup > deadline) throw new TimeoutException(label); yield return null; }
        }
        static void Check(Report report, bool condition, string label)
        { if (!condition) throw new InvalidOperationException(label); report.checks.Add(label); }
    }
}
