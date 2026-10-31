using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
using WeatherHousePrototype;

namespace WeatherHouse.Life.Editor
{
    // The parent review owns Play and its isolated save; these fixtures never save a scene.
    public static class LifeAudioPlayChecks
    {
        const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        public static IEnumerator Run(LifeSession life, Action<string> completed)
        {
            var checks = new List<string>(); IEnumerator steps = Steps(life, checks); Exception failure = null;
            while (true)
            {
                bool more; object next = null;
                try { more = steps.MoveNext(); if (more) next = steps.Current; }
                catch (Exception error) { failure = error; break; }
                if (!more) break; yield return next;
            }
            try { (steps as IDisposable)?.Dispose(); } catch (Exception error) { failure = failure ?? error; }
            string result = (failure == null ? "PASS" : "FAIL") + " actual audio playback\n" + string.Join("\n", checks) +
                "\nBoundary: real controller movement, actual ground and AudioSource state; player position, focus and idle local weather are isolated fixtures. Human loudness/timbre acceptance remains separate." + (failure == null ? "" : "\n" + failure);
            File.WriteAllText("LifeGameplayReview/20261026/play/audio-play-result.txt", result); completed?.Invoke(result);
        }
        static IEnumerator Steps(LifeSession life, List<string> checks)
        {
            Check(Application.isPlaying && life && !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WEATHERHOUSE_LIFE_REVIEW_SAVE")), "Isolated Play save required", checks);
            var audio = life.GetComponent<LifeAudio>(); var hud = life.GetComponent<GameplayHud>(); var game = life.Gameplay;
            var controller = game.forestSprite; var player = game.PlayerTransform; var character = player.GetComponent<CharacterController>();
            Check(audio && audio.Catalog && controller && !hud.JournalOpen && !hud.CameraMode && !hud.IsDialogueOpen && !Get<LifeObject>(life.World, "held"), "Audio/controller ready and UI/hands idle", checks);
            AudioSource[] sources = Get<AudioSource[]>(audio, "sources");
            var localWeather = Get<LifeLocalWeather>(life, "weather");
            Check(!localWeather.Active, "Weather fixture starts idle", checks);
            Vector3 originalPosition = player.position; Quaternion rotation = player.rotation;
            string world = life.World.CaptureJson(), progress = JsonUtility.ToJson(life.Progress), notebook = JsonUtility.ToJson(game.Notebook);
            bool focus = Get<bool>(audio, "focused"); var demonstration = controller.demonstration;
            var weatherState = new Dictionary<string, object>();
            foreach (string field in new[] { "skyKind", "skyActive", "windActive", "windSelected", "skyStrength", "windStrength" }) weatherState[field] = Field(localWeather, field).GetValue(localWeather);
            Call(audio, "OnApplicationFocus", true); controller.SetScriptedInput(Vector2.zero, false, false);
            try
            {
                Check(sources.Length == 12 && sources.All(s => s), "12 preallocated AudioSources", checks);
                foreach (LifeAudioSurface surface in new[] { LifeAudioSurface.Grass, LifeAudioSurface.Dirt, LifeAudioSurface.Stone })
                {
                    FindLane(player, audio.Catalog, surface, out Vector3 point, out Vector3 direction);
                    Place(character, player, point + Vector3.up * .05f);
                    float until = Time.time + .5f; while (Time.time < until) yield return null;
                    int stationary = audio.FootstepCount;
                    until = Time.time + .4f; while (Time.time < until) yield return null;
                    Check(controller.IsGrounded && audio.FootstepCount == stationary, surface + " stationary is silent", checks);
                    var heard = new HashSet<AudioClip>(); Vector3 start = player.position; int before = audio.FootstepCount;
                    LifeAudioCue cue = surface == LifeAudioSurface.Grass ? LifeAudioCue.GrassStep : surface == LifeAudioSurface.Dirt ? LifeAudioCue.DirtStep : LifeAudioCue.StoneStep;
                    until = Time.time + 1.05f;
                    while (Time.time < until)
                    {
                        Walk(controller, direction);
                        if (sources[3].isPlaying && audio.Catalog.Find(cue).clips.Contains(sources[3].clip)) heard.Add(sources[3].clip);
                        yield return null;
                    }
                    controller.SetScriptedInput(Vector2.zero, false, false);
                    Check(LifeWorld.Flat(start, player.position) > .8f && audio.FootstepCount >= before + 2 && heard.SetEquals(audio.Catalog.Find(cue).clips),
                        surface + " actual controller travel uses the selected surface clip; metres=" + LifeWorld.Flat(start, player.position).ToString("F2") + "; clips=" + heard.Count, checks);
                }
                int groundedCount = audio.FootstepCount;
                Place(character, player, player.position + Vector3.up * 3);
                yield return null;
                float airborneUntil = Time.time + .3f;
                bool stayedAirborne = true;
                while (Time.time < airborneUntil) { stayedAirborne &= !controller.IsGrounded; yield return null; }
                Check(stayedAirborne, "Airborne fixture remains off ground", checks);
                Check(audio.FootstepCount == groundedCount, "Airborne displacement emits no repeated dry steps", checks);

                localWeather.Cast("rain", out _); localWeather.Cast("wind", out _);
                float deadline = Time.time + .9f; while (Time.time < deadline) yield return null;
                Check(sources[8].isPlaying && sources[9].isPlaying && sources[8].volume > 0 && sources[9].volume > 0, "Rain and wind run together on independent loops", checks);
                localWeather.Cast("sunlight", out _); deadline = Time.time + .7f; while (Time.time < deadline) yield return null;
                Check(!sources[8].isPlaying && sources[9].isPlaying, "Sunlight stops rain and preserves wind", checks);
                localWeather.Cast("clear", out _); deadline = Time.time + .7f; while (Time.time < deadline) yield return null;
                Check(!sources[8].isPlaying && !sources[9].isPlaying, "Clear stops both weather channels", checks);

                LifeObject plant = life.World.GatheringSources.First(p => p && p.gameObject.activeInHierarchy);
                Place(character, player, plant.transform.position + Vector3.up * .08f); deadline = Time.time + .4f; while (Time.time < deadline) yield return null;
                int fiber = audio.Count(LifeAudioCue.Fiber);
                Check(life.World.TryGather() && audio.Count(LifeAudioCue.Fiber) == fiber + 1, "Real registered plant gathering emits one fiber sound", checks);
                Call(life.World, "StartCraft"); deadline = Time.time + .25f; while (Time.time < deadline) yield return null;
                Check(life.World.IsCrafting && sources[7].isPlaying, "Crafting starts its one fiber loop", checks);
                Place(character, player, player.position + Vector3.right * 1.5f); yield return null; yield return null;
                Check(!life.World.IsCrafting && !sources[7].isPlaying && Get<LifeObject>(life.World, "held").State.kind == LifeObjectKind.Bundle, "Moving away stops craft loop and preserves grass", checks);
                int contact = audio.Count(LifeAudioCue.Contact); Call(life.World, "StartCraft");
                deadline = Time.time + 1.9f; while (Time.time < deadline) yield return null;
                Check(!life.World.IsCrafting && !sources[7].isPlaying && Get<LifeObject>(life.World, "held").State.kind == LifeObjectKind.Mat && audio.Count(LifeAudioCue.Contact) == contact + 1, "Real elapsed crafting ends with one contact sound", checks);
                Call(life.World, "RemoveHeld");

                int paper = audio.Count(LifeAudioCue.Paper); hud.ToggleJournal();
                deadline = Time.time + 12;
                while (hud.IsAlbumOpening && Time.time < deadline) yield return null;
                var book = life.GetComponent<LifeJournal3D>();
                Check(book && !hud.IsAlbumOpening && book.OpeningTurnsCompleted == LifeJournalOpeningFan.Count && audio.Count(LifeAudioCue.Paper) - paper >= 2 && audio.Count(LifeAudioCue.Paper) - paper <= 4,
                    "J completes the independent opening leaves with bounded paper sounds, no queued replay; sounds=" + (audio.Count(LifeAudioCue.Paper) - paper), checks);
                paper = audio.Count(LifeAudioCue.Paper); deadline = Time.time + .6f; while (Time.time < deadline) yield return null;
                Check(audio.Count(LifeAudioCue.Paper) == paper, "Idle journal redraw does not replay paper", checks);
                var gestureInput = life.GetComponent<LifeGestureInput>();
                gestureInput.SetPracticeDrawing(true);
                Check(gestureInput.IsStrokeActive && life.GestureInputActive && !gestureInput.IsWorldDrawing, "Journal practice enters the shared input state before a movement pulse", checks);
                LifeAudio.StrokeDiagnostic = checks.Add;
                checks.Add("TRACE stroke fixture instance=" + audio.GetInstanceID() + " source=" + sources[2].GetInstanceID() + " frame=" + Time.frameCount);
                float strokeSignalAt = Time.unscaledTime; LifeAudio.StrokeMoved();
                // The original 90 ms timestamp lost an unconsumed movement event after a slow frame.
                System.Threading.Thread.Sleep(150); yield return null;
                checks.Add("TRACE stroke waited=" + (Time.unscaledTime - strokeSignalAt).ToString("F3") + "s active=" + life.GetComponent<LifeGestureInput>().IsStrokeActive + " focus=" + Get<bool>(audio, "focused"));
                LifeAudio.StrokeDiagnostic = null;
                Check(sources[2].isPlaying, "Actual stroke signal survives a 150 ms frame and starts shared drawing loop", checks);
                deadline = Time.unscaledTime + .6f; while (Time.unscaledTime < deadline) yield return null;
                Check(!sources[2].isPlaying, "Holding drawing mode without new movement lets the stroke become silent", checks);
                LifeAudio.StrokeMoved(); yield return null;
                Check(sources[2].isPlaying, "New movement resumes the stroke after silence", checks);
                gestureInput.SetPracticeDrawing(false); yield return null;
                Check(!sources[2].isPlaying, "Ending journal practice stops its shared stroke source", checks);
                hud.ToggleJournal(); deadline = Time.time + 5; while (hud.JournalOpen && Time.time < deadline) yield return null;
                Check(!hud.JournalOpen, "Journal closes after audio fixture", checks);
                // Isolate the world's armed/dragging state, then use its actual AddPoint and submit/cancel hooks.
                Set(gestureInput, "worldMode", true); life.SetGestureInput(true);
                yield return null;
                Check(!sources[2].isPlaying, "Armed world gesture without movement is silent", checks);
                Set(gestureInput, "drawing", true);
                var points = Get<List<Vector2>>(gestureInput, "points"); points.Clear(); points.Add(new Vector2(-10000, -10000));
                Call(gestureInput, "AddPoint"); yield return null;
                Check(gestureInput.IsWorldDrawing && sources[2].isPlaying && audio.Catalog.Find(LifeAudioCue.Casting).clips.Contains(sources[2].clip), "World AddPoint movement uses a selected magic clip on the shared drawing source", checks);
                Set(gestureInput, "drawing", false); Call(gestureInput, "SubmitStroke");
                Check(!sources[2].isPlaying, "World pointer release stops its stroke immediately", checks);
                yield return null;
                Check(!sources[2].isPlaying, "Submitted world stroke is not queued for replay", checks);
                Set(gestureInput, "drawing", true); points.Clear(); points.Add(new Vector2(-10000, -10000));
                Call(gestureInput, "AddPoint"); yield return null;
                Check(sources[2].isPlaying, "A new world stroke resumes after release", checks);
                gestureInput.Cancel();
                Check(!sources[2].isPlaying && !life.GestureInputActive && !gestureInput.IsWorldDrawing, "World cancellation clears input and stops immediately", checks);
                LifeAudio.PlayAt(LifeAudioCue.Contact, player.position);
                Call(audio, "OnApplicationFocus", false);
                Check(sources.All(s => !s.isPlaying), "Losing focus stops every source immediately", checks);
                int rejectedFiber = audio.Count(LifeAudioCue.Fiber), rejectedProgress = audio.Count(LifeAudioCue.Progress), rejectedCast = audio.Count(LifeAudioCue.Cast);
                int beforeBackground = audio.PlayedCount;
                LifeAudio.PlayAt(LifeAudioCue.Fiber, player.position); LifeAudio.Play(LifeAudioCue.Progress);
                life.SetGestureInput(true); LifeAudio.StrokeMoved(); localWeather.Cast("rain", out _);
                yield return null;
                Check(audio.PlayedCount == beforeBackground && sources.All(s => !s.isPlaying), "Background world/results/stroke requests cannot restart stopped sources", checks);
                life.SetGestureInput(false); Call(audio, "OnApplicationFocus", true);
                deadline = Time.time + .7f; while (Time.time < deadline) yield return null;
                Check(audio.Count(LifeAudioCue.Fiber) == rejectedFiber && audio.Count(LifeAudioCue.Progress) == rejectedProgress && audio.Count(LifeAudioCue.Cast) == rejectedCast && !sources[2].isPlaying,
                    "Regaining focus does not replay rejected one-shots or stale stroke pulses", checks);
                Check(sources[8].isPlaying, "Still-active weather resumes as a current loop after focus returns", checks);
                localWeather.Cast("clear", out _); deadline = Time.time + .7f; while (Time.time < deadline) yield return null;
                audio.enabled = false; Check(sources.All(s => !s.isPlaying), "Disabling audio releases all loop and one-shot playback", checks); audio.enabled = true;
            }
            finally
            {
                LifeAudio.StrokeDiagnostic = null;
                controller.ReleaseScriptedInput(); controller.SetDemonstration(demonstration);
                life.GetComponent<LifeGestureInput>()?.Cancel();
                if (hud.JournalOpen) { life.GetComponent<LifeJournal3D>()?.Cancel(); Call(hud, "CompleteJournalClose"); }
                Call(life.World, "RemoveHeld"); Set(life.World, "craftingId", null); life.World.RestoreJson(world);
                JsonUtility.FromJsonOverwrite(progress, life.Progress); JsonUtility.FromJsonOverwrite(notebook, game.Notebook);
                localWeather.Cast("clear", out _);
                foreach (var pair in weatherState) Field(localWeather, pair.Key).SetValue(localWeather, pair.Value);
                Get<WeatherRegion>(localWeather, "sky").SetLocalWeather(Get<LocalWeather>(localWeather, "skyKind"), Get<int>(localWeather, "skyStrength"), Get<bool>(localWeather, "skyActive"));
                Get<WeatherRegion>(localWeather, "wind").SetLocalWeather(LocalWeather.Wind, Get<int>(localWeather, "windStrength"), Get<bool>(localWeather, "windActive"));
                Place(character, player, originalPosition); player.rotation = rotation; audio.enabled = true; Call(audio, "OnApplicationFocus", focus); game.RefreshInputBlocking();
            }
        }
        static void FindLane(Transform player, LifeAudioCatalog catalog, LifeAudioSurface surface, out Vector3 point, out Vector3 direction)
        {
            var ground = new LifeAudioGround(); var candidates = new List<Vector3>();
            var water = UnityEngine.Object.FindObjectsOfType<WorldWaterSurface>();
            foreach (Terrain terrain in Terrain.activeTerrains)
            {
                TerrainData data = terrain.terrainData;
                for (float x = 2; x < data.size.x - 2; x += 4) for (float z = 2; z < data.size.z - 2; z += 4)
                {
                    Vector3 p = terrain.transform.position + new Vector3(x, 0, z); p.y = terrain.SampleHeight(p) + terrain.transform.position.y;
                    if (data.GetSteepness(x / data.size.x, z / data.size.z) < 20 && LifeAudioGround.TerrainAt(terrain, p, catalog) == surface) candidates.Add(p);
                }
            }
            if (surface == LifeAudioSurface.Stone)
                foreach (Collider collider in UnityEngine.Object.FindObjectsOfType<Collider>())
                {
                    Renderer renderer = collider.GetComponent<Renderer>();
                    if (!collider.isTrigger && renderer && renderer.sharedMaterials.Length == 1 && catalog.Surface(renderer.sharedMaterial) == surface)
                        candidates.Add(new Vector3(collider.bounds.center.x, collider.bounds.max.y + .02f, collider.bounds.center.z));
                }
            foreach (Vector3 p in candidates.OrderBy(p => (p - player.position).sqrMagnitude))
            {
                if (ground.Resolve(p + Vector3.up * .05f, player, catalog, out Vector3 contact) != surface) continue;
                if (water.Any(w => w.TryGetHeight(contact, out float height) && height > contact.y + .025f)) continue;
                for (int angle = 0; angle < 8; angle++)
                {
                    Vector3 forward = new Vector3(Mathf.Cos(angle * Mathf.PI / 4), 0, Mathf.Sin(angle * Mathf.PI / 4)); bool valid = true;
                    for (int step = 1; step <= 4; step++)
                    {
                        Vector3 next = contact + forward * (step * .85f);
                        if (ground.Resolve(next + Vector3.up * .08f, player, catalog, out Vector3 at) != surface || Mathf.Abs(at.y - contact.y) > .18f) { valid = false; break; }
                    }
                    if (valid) { point = contact; direction = forward; return; }
                }
            }
            throw new Exception("No actual walkable " + surface + " lane found");
        }
        static void Walk(ForestSpriteCharacter.ForestSpriteDemoController controller, Vector3 direction)
        {
            Camera camera = controller.MovementCameraOverride ? controller.MovementCameraOverride : controller.demoCamera;
            Vector3 forward = camera ? Vector3.ProjectOnPlane(camera.transform.forward, Vector3.up).normalized : Vector3.forward;
            controller.SetScriptedInput(new Vector2(Vector3.Dot(direction, Vector3.Cross(Vector3.up, forward)), Vector3.Dot(direction, forward)), false, false);
        }
        static void Place(CharacterController controller, Transform player, Vector3 position) { bool enabled = controller.enabled; controller.enabled = false; player.position = position; controller.enabled = enabled; Physics.SyncTransforms(); }
        static FieldInfo Field(object target, string name) => target.GetType().GetField(name, Hidden) ?? throw new MissingFieldException(target.GetType().Name, name);
        static T Get<T>(object target, string name) => (T)Field(target, name).GetValue(target);
        static void Set(object target, string name, object value) => Field(target, name).SetValue(target, value);
        static void Call(object target, string name, params object[] args) => target.GetType().GetMethod(name, Hidden).Invoke(target, args);
        static void Check(bool value, string label, List<string> checks) { if (!value) throw new Exception(label); checks.Add("PASS " + label); }
    }
}
