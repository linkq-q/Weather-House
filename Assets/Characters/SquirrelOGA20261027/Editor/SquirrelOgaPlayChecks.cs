using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using WeatherHouse.Hillside;
using WeatherHouse.Life;
using WeatherHouse.Life.Editor;
using WeatherHousePrototype;
using Object = UnityEngine.Object;

namespace WeatherHouse.SquirrelOga.Editor
{
    // Parent UiOnly harness calls Run serially. This class never enters/exits Play or saves a scene.
    public static class SquirrelOgaPlayChecks
    {
        const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        const string Folder = "LifeGameplayReview/20261027/squirrel-oga/play";
        [Serializable] sealed class Frame
        {
            public string phase, file, animal, state, restPhase;
            public float seconds, speed, walkWeight, restWeight, drinkWeight, mouthSkinDistance;
            public bool consumed, touching;
            public Vector3 position, mouth, water;
            public Quaternion rotation;
        }
        [Serializable] sealed class Report
        {
            public string status = "RUNNING", utc, error, saveDirectory;
            public string boundary = "Existing isolated-save formal-scene Play only. Fixture commands target the real WildlifeAgent controller; real frames and Playables advance walk/turn/restDown/loop/up. Bowl F calls production Interact and production Drink contact/consumption. Initial bowl water and rest destinations are arranged, not a complete quest/AI/gesture test. Frames render the actual Play scene without SampleAnimation. Main scene and real saves are never saved/reset. Human animation/material acceptance remains separate.";
            public List<string> checks = new List<string>();
            public List<Frame> frames = new List<Frame>();
            public float walkDistance, turnDegrees, footPoseDelta, waterBefore, waterAfter;
            public bool down, sleep, up, drinkPlayed, contact, consumed, returnedToRest;
        }
        sealed class Capture : IDisposable
        {
            public Camera camera;
            readonly RenderTexture target;
            readonly Texture2D image;
            readonly Dictionary<string, int> counts = new Dictionary<string, int>();
            readonly string frameFolder = Path.Combine(Folder, "frames", DateTime.UtcNow.ToString("yyyyMMdd-HHmmssfff"));
            float next;
            public Capture()
            {
                camera = new GameObject("OGA isolated Play capture").AddComponent<Camera>();
                camera.enabled = false; camera.orthographic = true; camera.orthographicSize = .95f;
                camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.67f, .72f, .67f);
                camera.cullingMask = ~LayerMask.GetMask("UI"); camera.nearClipPlane = .02f; camera.farClipPlane = 25f;
                target = new RenderTexture(640, 480, 24); target.Create(); camera.targetTexture = target;
                image = new Texture2D(640, 480, TextureFormat.RGB24, false);
            }
            public void Observe(WildlifeAgent animal, SquirrelOgaRig rig, string phase, Report report, float began, object drink = null, bool force = false)
            {
                if (!force && Time.realtimeSinceStartup < next) return;
                next = Time.realtimeSinceStartup + .2f;
                foreach (var skin in animal.GetComponentsInChildren<SkinnedMeshRenderer>())
                {
                    var baked = new Mesh();
                    try
                    {
                        skin.BakeMesh(baked, true); var bounds = skin.bounds; bounds.Expand(.008f);
                        foreach (var v in baked.vertices) Require(bounds.Contains(skin.transform.TransformPoint(v)), "Actual Play renderer bounds contain current skinned points: " + phase + "/" + skin.name);
                    }
                    finally { Object.Destroy(baked); }
                }
                Vector3 focus = animal.transform.position + Vector3.up * .34f;
                camera.transform.position = focus + animal.transform.forward * 1.7f + animal.transform.right * 1.35f + Vector3.up * .85f;
                camera.transform.LookAt(focus);
                var previous = RenderTexture.active;
                try { camera.Render(); RenderTexture.active = target; image.ReadPixels(new Rect(0, 0, 640, 480), 0, 0); image.Apply(false); }
                finally { RenderTexture.active = previous; }
                counts.TryGetValue(phase, out int count); counts[phase] = count + 1;
                string directory = Path.Combine(frameFolder, phase); Directory.CreateDirectory(directory);
                string path = Path.Combine(directory, count.ToString("D4") + ".png"); File.WriteAllBytes(path, image.EncodeToPNG());
                var frame = new Frame { phase = phase, file = Path.GetFullPath(path), animal = animal.name,
                    seconds = Time.realtimeSinceStartup - began, state = animal.State.ToString(), restPhase = animal.RestPhase.ToString(),
                    position = animal.transform.position, rotation = animal.transform.rotation, speed = animal.ActualSpeed,
                    walkWeight = Weight(animal, rig.walk), restWeight = Weight(animal, rig.restLoop), drinkWeight = Weight(animal, rig.drink) };
                if (drink != null)
                {
                    frame.mouth = ((Transform)Public(drink, "mouthBone")).TransformPoint((Vector3)Public(drink, "mouthLocal"));
                    frame.water = ((LifeObject)Public(drink, "bowl")).waterVisual.position;
                    frame.mouthSkinDistance = (float)Public(drink, "skinDistance"); frame.touching = (bool)Public(drink, "touching"); frame.consumed = (bool)Public(drink, "consumed");
                }
                report.frames.Add(frame);
            }
            public void Dispose()
            {
                if (camera) Object.Destroy(camera.gameObject);
                target.Release(); Object.Destroy(target); Object.Destroy(image);
            }
        }

        // One-line parent bridge replaces LifeUiPlayChecks.Run with RunWithUi in the existing UiOnly branch.
        public static IEnumerator RunWithUi(LifeSession life, Action<string> completed)
        {
            string ui = null, squirrel = null;
            yield return LifeUiPlayChecks.Run(life, result => ui = result);
            if (ui == null || !ui.StartsWith("PASS", StringComparison.Ordinal)) { completed?.Invoke(ui ?? "FAIL UI returned no result."); yield break; }
            yield return Run(life, result => squirrel = result);
            bool passed = squirrel != null && squirrel.StartsWith("PASS", StringComparison.Ordinal);
            completed?.Invoke((passed ? "PASS" : "FAIL") + " serial UI and squirrel OGA Play\n" + ui + "\n" + (squirrel ?? "FAIL OGA returned no result."));
        }

        public static IEnumerator Run(LifeSession life, Action<string> completed)
        {
            var report = new Report { utc = DateTime.UtcNow.ToString("O") };
            IEnumerator steps = Steps(life, report); Exception failure = null;
            while (true)
            {
                bool more; object next = null;
                try { more = steps.MoveNext(); if (more) next = steps.Current; }
                catch (Exception error) { failure = error; break; }
                if (!more) break;
                yield return next;
            }
            try { (steps as IDisposable)?.Dispose(); } catch (Exception error) { failure = failure == null ? error : new AggregateException(failure, error); }
            report.status = failure == null ? "PASS_ACTUAL_PLAY_FUNCTIONAL_VISUAL_ACCEPTANCE_PENDING" : "FAIL";
            report.error = failure?.ToString(); Directory.CreateDirectory(Folder);
            File.WriteAllText(Folder + "/play-latest.json", JsonUtility.ToJson(report, true));
            completed?.Invoke((failure == null ? "PASS" : "FAIL") + " squirrel OGA Play\n" + string.Join("\n", report.checks) + (failure == null ? "" : "\n" + failure));
        }

        static IEnumerator Steps(LifeSession life, Report report)
        {
            string reviewSave = Environment.GetEnvironmentVariable("WEATHERHOUSE_LIFE_REVIEW_SAVE");
            Require(Application.isPlaying && life && life.World && !string.IsNullOrEmpty(reviewSave), "Use the existing isolated-save UiOnly Play harness.");
            string save = Path.GetFullPath(reviewSave), reviewRoot = Path.GetFullPath("LifeGameplayReview") + Path.DirectorySeparatorChar;
            Require(save.StartsWith(reviewRoot, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(save, Path.GetFullPath(GameplaySession.PersistenceDirectory), StringComparison.OrdinalIgnoreCase), "Refuse real or mismatched save directories.");
            report.saveDirectory = save;
            var hud = life.GetComponent<GameplayHud>();
            Require(hud && !hud.JournalOpen && !hud.CameraMode && !hud.IsDialogueOpen && !life.GestureInputActive, "Run serially after UI returns to idle.");
            LifeWorld world = life.World; var game = life.Gameplay;
            var weather = Get<LifeLocalWeather>(life, "weather"); Require(weather && !weather.Active, "The shared UI fixture must finish with local weather cleared.");
            Require(Get<IList>(world, "drinks").Count == 0 && Get<LifeObject>(world, "held") == null && Get<string>(world, "craftingId") == null, "Start without pending drinking/crafting.");
            var animals = Object.FindObjectsOfType<WildlifeAgent>().Where(a => a.speciesId == "squirrel").ToArray();
            Require(animals.Length > 0, "Loaded scene contains squirrels.");
            foreach (var animal in animals)
            {
                var profile = animal.GetComponentInChildren<SquirrelOgaRig>();
                Require(profile && profile.HasLifeClips && profile.HasAnimationBounds, "Every loaded squirrel has the complete OGA profile.");
                foreach (var clip in new[] { animal.clips.idle, animal.clips.walk, animal.clips.run, animal.clips.restDown, animal.clips.restSleep, animal.clips.restUp, profile.drink })
                    Require(clip && AssetDatabase.GetAssetPath(clip) == SquirrelOgaInstaller.ModelPath, "No former-skeleton clip remains in Play.");
            }
            report.checks.Add("All loaded squirrels bind complete same-FBX OGA clips and runtime animation bounds.");
            var main = world.FindSpeaker("squirrel").GetComponent<WildlifeAgent>();
            var companion = world.FindSpeaker("companion").GetComponent<WildlifeAgent>();
            var rig = main.GetComponentInChildren<SquirrelOgaRig>();
            var bindings = Get<IDictionary>(world, "bindings"); object binding = bindings["squirrel"], other = bindings["companion"];
            var poses = animals.ToDictionary(a => a, a => new Pose(a.transform.position, a.transform.rotation));
            Vector3 player = game.PlayerTransform.position; Quaternion playerRotation = game.PlayerTransform.rotation;
            string worldBefore = world.CaptureJson(), progressBefore = JsonUtility.ToJson(life.Progress), notebookBefore = JsonUtility.ToJson(game.Notebook), statusBefore = game.Status;
            var disk = new[] { life.SavePath, game.SavePath }.Distinct().ToDictionary(path => path, path => File.Exists(path) ? File.ReadAllBytes(path) : null);
            float began = Time.realtimeSinceStartup;
            using (var capture = new Capture())
            try
            {
                // A short controller target is a declared fixture, not a teleport or a new quest acceptance.
                Vector3 start = main.transform.position, goal = default; bool found = false;
                for (int i = 0; i < 24 && !found; i++)
                {
                    Vector3 point = start + Quaternion.Euler(0, 90 + i * 15, 0) * main.transform.forward * 1.35f;
                    if (main.RequestLifeTarget(point, 1.5f)) { goal = Get<Vector3>(main, "lifeTarget"); found = LifeWorld.Flat(goal, start) > .9f; }
                }
                Require(found, "Find a real short collision-checked walking route near the current squirrel.");
                Target(binding, main, goal, false); float until = Time.realtimeSinceStartup + 10;
                Quaternion initialRotation = main.transform.rotation;
                Transform foot = main.GetComponentsInChildren<Transform>().First(t => t.name == "Arm_L.002");
                Vector3 footBefore = main.transform.InverseTransformPoint(foot.position); bool walked = false;
                while (LifeWorld.Flat(main.transform.position, goal) > .25f || main.RestPhase != WildlifeRestPhase.Standing)
                {
                    Deadline(until, "Actual walking controller did not arrive.");
                    walked |= main.ActualSpeed > .1f && Weight(main, rig.walk) > .6f;
                    report.footPoseDelta = Mathf.Max(report.footPoseDelta, Vector3.Distance(footBefore, main.transform.InverseTransformPoint(foot.position)));
                    report.turnDegrees = Mathf.Max(report.turnDegrees, Quaternion.Angle(initialRotation, main.transform.rotation));
                    capture.Observe(main, rig, "walk-turn", report, began); yield return null;
                }
                report.walkDistance = LifeWorld.Flat(start, main.transform.position);
                Require(walked && report.walkDistance > .7f && report.turnDegrees > 12f && report.footPoseDelta > .002f, "Real travel, heading change, walk mixer weight and limb-pose changes are all observed.");
                report.checks.Add("Actual controller walked and turned with RunSlow_Derived mixer weight and changing limb pose; no SampleAnimation.");

                RestHere(world, binding, main); until = Time.realtimeSinceStartup + 7;
                while (main.RestPhase != WildlifeRestPhase.Sleeping || Weight(main, rig.restLoop) < .8f)
                {
                    report.down |= main.RestPhase == WildlifeRestPhase.LyingDown;
                    Deadline(until, "RestDown did not reach the real resting loop."); capture.Observe(main, rig, "rest-down", report, began); yield return null;
                }
                report.sleep = true; until = Time.realtimeSinceStartup + 1.8f;
                while (Time.realtimeSinceStartup < until) { capture.Observe(main, rig, "rest-loop", report, began); yield return null; }
                int eye = rig.headSkin.sharedMesh.GetBlendShapeIndex("EyesClosed");
                Require(report.down && rig.headSkin.GetBlendShapeWeight(eye) > 70f, "RestDown, weighted RestLoop and actual closed eyes are observed.");
                report.checks.Add("Same-rig restDown/loop and runtime closed-eye control execute over real frames.");
                Target(binding, main, main.transform.position, false); until = Time.realtimeSinceStartup + 6;
                do
                {
                    report.up |= main.RestPhase == WildlifeRestPhase.GettingUp;
                    Deadline(until, "RestUp did not return to standing."); capture.Observe(main, rig, "rest-up", report, began); yield return null;
                } while (main.RestPhase != WildlifeRestPhase.Standing);
                Require(report.up, "The real rest controller used RestUp before standing.");
                report.checks.Add("Same-rig restUp returns to standing without replacing clips or posing bones manually.");

                LifeObject bowl = world.ObjectAt("squirrel.common.bowl"); Require(bowl && bowl.waterVisual, "Existing communal bowl is present.");
                Require(LifeWorld.Flat(main.transform.position, bowl.transform.position) < 3.5f || LifeWorld.Flat(companion.transform.position, bowl.transform.position) < 3.5f, "A real squirrel is close enough for normal bowl selection.");
                RestHere(world, binding, main); RestHere(world, other, companion);
                until = Time.realtimeSinceStartup + 7;
                while (main.RestPhase != WildlifeRestPhase.Sleeping || companion.RestPhase != WildlifeRestPhase.Sleeping) { Deadline(until, "Both fixture squirrels did not settle before drinking."); yield return null; }
                bowl.State.water = .8f; bowl.RefreshVisual(Time.time); report.waterBefore = bowl.State.water;
                Require(LifeWorldPlayChecks.PlaceForInteraction(life, bowl), "The player can stand at a real selectable bowl interaction.");
                world.Interact(); IList drinks = Get<IList>(world, "drinks"); Require(drinks.Count == 1, "Production bowl interaction starts one drink.");
                object drink = drinks[0], drinkBinding = Public(drink, "animal");
                var drinking = (WildlifeAgent)Public(drinkBinding, "wildlife"); var drinkRig = drinking.GetComponentInChildren<SquirrelOgaRig>();
                Require((AnimationClip)Public(drink, "clip") == drinkRig.drink && (SkinnedMeshRenderer)Public(drink, "skin") == drinkRig.headSkin, "Production Drink uses the new clip and visible head skin.");
                Require(Mathf.Approximately(bowl.State.water, report.waterBefore), "Approach does not consume water.");
                until = Time.realtimeSinceStartup + 26;
                while (drinks.Count > 0)
                {
                    Deadline(until, "Actual approach/drink did not finish.");
                    report.drinkPlayed |= Weight(drinking, drinkRig.drink) > .6f;
                    report.contact |= (bool)Public(drink, "touching") && (float)Public(drink, "skinDistance") < .035f;
                    report.consumed |= (bool)Public(drink, "consumed");
                    capture.Observe(drinking, drinkRig, "drink", report, began, drink); yield return null;
                }
                report.waterAfter = bowl.State.water;
                Require(report.drinkPlayed && report.contact && report.consumed && Mathf.Abs(report.waterBefore - report.waterAfter - .12f) < .005f,
                    "Real Drink mixer weight and production mouth contact precede exactly one water consumption.");
                until = Time.realtimeSinceStartup + 10;
                while (drinking.RestPhase != WildlifeRestPhase.Sleeping)
                { Deadline(until, "Drinking squirrel did not return to its preserved rest destination."); capture.Observe(drinking, drinkRig, "return-rest", report, began); yield return null; }
                report.returnedToRest = true; capture.Observe(drinking, drinkRig, "return-rest", report, began, null, true);
                report.checks.Add("Actual bowl interaction uses OGA Drink, actual visible-mouth contact, one 0.12 water consumption and returns through the normal rest controller.");
            }
            finally
            {
                foreach (var animal in animals) if (animal) { animal.CancelLifeAction(); animal.ReleaseLifeTarget(); }
                world.RestoreJson(worldBefore); JsonUtility.FromJsonOverwrite(progressBefore, life.Progress); JsonUtility.FromJsonOverwrite(notebookBefore, game.Notebook);
                world.RestorePublicPhotoContexts(game.Notebook.publicHistory);
                foreach (var pair in poses) if (pair.Key) pair.Key.transform.SetPositionAndRotation(pair.Value.position, pair.Value.rotation);
                foreach (var obj in world.Objects) obj.RefreshVisual(Time.time);
                game.PlayerTransform.SetPositionAndRotation(player, playerRotation); Physics.SyncTransforms(); game.Notify(statusBefore);
                foreach (var pair in disk) { if (pair.Value != null) File.WriteAllBytes(pair.Key, pair.Value); else if (File.Exists(pair.Key)) File.Delete(pair.Key); }
            }
        }

        static void RestHere(LifeWorld world, object binding, WildlifeAgent animal)
        {
            // Test the controller's rest sequence without borrowing an unprepared bed's separate dry/warm gate.
            for (int i = 0; i < 9; i++)
            {
                Vector3 point = animal.transform.position;
                if (i > 0) point += Quaternion.Euler(0, (i - 1) * 45, 0) * Vector3.right * .42f;
                if (world.Objects.Any(o => o.State.kind == LifeObjectKind.Rest && LifeWorld.Flat(o.transform.position, point) < .15f)) continue;
                if (!animal.RequestLifeTarget(point, 1.5f, true)) continue;
                Target(binding, animal, point, true); return;
            }
            throw new InvalidOperationException("No nearby collision-checked rest-controller fixture outside an unprepared station.");
        }

        static void Target(object binding, WildlifeAgent animal, Vector3 point, bool rest)
        {
            Require(animal.RequestLifeTarget(point, 1.5f, rest), "Production controller accepts the fixture target.");
            binding.GetType().GetField("target").SetValue(binding, Get<Vector3>(animal, "lifeTarget")); binding.GetType().GetField("rest").SetValue(binding, rest);
        }
        static float Weight(WildlifeAgent animal, AnimationClip clip)
        { var player = Get<object>(animal, "animationPlayer"); return (float)player.GetType().GetMethod("WeightOf").Invoke(player, new object[] { clip }); }
        static object Public(object item, string name) => item.GetType().GetField(name).GetValue(item);
        static T Get<T>(object item, string name) => (T)item.GetType().GetField(name, Hidden).GetValue(item);
        static void Deadline(float until, string label) => Require(Time.realtimeSinceStartup < until, label);
        static void Require(bool ok, string label) { if (!ok) throw new InvalidOperationException(label); }
    }
}
