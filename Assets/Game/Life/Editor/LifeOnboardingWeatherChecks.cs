using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using WeatherHousePrototype;

namespace WeatherHouse.Life.Editor
{
    // Continuation of the first-loop smoke. Existing dedicated Play only; never starts/stops or saves a scene.
    public static class LifeOnboardingWeatherChecks
    {
        const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        [MenuItem("Weather House/Review 20261027/Check onboarding weather in current isolated Play")]
        public static void Begin()
        {
            var life = UnityEngine.Object.FindObjectOfType<LifeSession>(); Guard(life);
            life.StartCoroutine(Run(life));
        }
        static IEnumerator Run(LifeSession life)
        {
            var lines = new List<string>(); var stack = new Stack<IEnumerator>(); stack.Push(Steps(life, lines));
            Exception failure = null; float began = Time.realtimeSinceStartup;
            while (stack.Count > 0)
            {
                object next = null; bool more = false;
                try
                {
                    if (Time.realtimeSinceStartup - began > 150) throw new TimeoutException("Weather continuation exceeded 150 seconds.");
                    more = stack.Peek().MoveNext(); if (more) next = stack.Peek().Current;
                }
                catch (Exception error) { failure = error; break; }
                if (!more) { (stack.Pop() as IDisposable)?.Dispose(); continue; }
                if (next is IEnumerator nested) stack.Push(nested); else yield return next;
            }
            while (stack.Count > 0) try { (stack.Pop() as IDisposable)?.Dispose(); } catch (Exception error) { failure = failure ?? error; }
            string result = (failure == null ? "PASS" : "FAIL") + " onboarding weather continuation\n" + string.Join("\n", lines) +
                "\nBOUNDARY: actual formal-scene gameplay and isolated checkpoint; player/camera pose are declared fixtures. Book practice uses real raycast/pointer handlers. World ink samples enter the existing SubmitStroke recognition/TryCast path through reflection; OS G/mouse and visual pacing remain manual checks. No tutorial stage/word/task completion was assigned.\n" + failure;
            Guard(life); File.WriteAllText(Path.Combine(GameplaySession.PersistenceDirectory, "onboarding-weather-checks.txt"), result);
            Directory.CreateDirectory("LifeGameplayReview/20261027/onboarding"); File.WriteAllText("LifeGameplayReview/20261027/onboarding/weather-checks.txt", result);
        }
        static IEnumerator Steps(LifeSession life, List<string> checks)
        {
            Guard(life); var game = life.Gameplay; var hud = game.GetComponent<GameplayHud>(); var tutorial = game.GetComponent<LifeOnboarding>();
            Require(tutorial && tutorial.State?.stage == LifeOnboardingStage.FindRainRequest, "Starts only after the real first-loop smoke checkpoint", checks);
            Require(!hud.JournalOpen && !hud.CameraMode && !hud.IsDialogueOpen && !game.Judging, "HUD is idle before the weather continuation", checks);
            var mover = game.forestSprite; var capsule = mover ? mover.GetComponent<CharacterController>() : null;
            Require(mover && capsule && capsule.enabled && !mover.ScriptedInput, "Live player supports declared viewpoint fixtures", checks);
            var dog = life.World.FindSpeaker("dog"); Require(dog && dog.Actor && dog.Actor.IsAvailableForObservation, "Authored dog is live", checks);
            var camera = game.PlayerViewCamera; var orbit = camera.GetComponents<MonoBehaviour>().FirstOrDefault(b => b.GetType().Name == "VegetationTopDownCamera");
            Vector3 player = mover.transform.position, cameraPosition = camera.transform.position; Quaternion rotation = mover.transform.rotation, cameraRotation = camera.transform.rotation;
            bool orbitEnabled = orbit && orbit.enabled; mover.SetScriptedInput(Vector2.zero, false, false); if (orbit) orbit.enabled = false;
            try
            {
                Near(game, capsule, dog.Actor, 2.2f); Aim(camera, dog.Actor);
                yield return Wait(() => game.Observed == dog.Actor && life.CurrentRequest(dog.Actor)?.id == "dog.hello", 10, "Actual authored greeting is selected");
                Reply(hud, "打个招呼", checks);
                yield return Wait(() => life.Progress.understoodRequests.Contains("dog.hello"), 5, "Actual local greeting response satisfies the authored prerequisite");
                yield return Wait(() => tutorial.State.stage == LifeOnboardingStage.WeatherBook, 10, "Visible authored rain glyph reaches the fourth book cue");
                Require(life.CurrentRequest(dog.Actor)?.id == "dog.rain.lesson" && hud.IsAnimalSpeechVisible(dog.Actor), "Actual want/rain request is visible before the weather book", checks);
                hud.ToggleJournal(); yield return WaitBook(hud); yield return null;
                var pad = hud.GetComponentsInChildren<LifeGesturePad>().Single();
                yield return LifeOnboardingSmokeFrames.Capture(life, "weather-practice-rain");
                DrawPractice(pad, "rain", checks);
                Require(life.IsGestureLearned("rain") && !life.IsGestureLearned("clear") && !life.LocalRainActive, "Paper rain trace learns the gesture without making world rain", checks);
                yield return null; Click(tutorial.GetComponentInChildren<LifeOnboardingView>().transform, "Continue note", checks); yield return null;
                pad = hud.GetComponentsInChildren<LifeGesturePad>().Single();
                yield return LifeOnboardingSmokeFrames.Capture(life, "weather-practice-clear");
                DrawPractice(pad, "clear", checks);
                Require(life.IsGestureLearned("clear") && tutorial.State.booksIntroduced.Distinct().Count() == 4, "Rain and clear are learned in the fourth distinct contextual book visit", checks);
                hud.ToggleJournal(); yield return Wait(() => !hud.JournalOpen, 14, "Weather book closes");
                yield return Wait(() => tutorial.State.stage == LifeOnboardingStage.UnderstandRain && game.Observed == dog.Actor, 5, "Returns to the live request");
                Reply(hud, "你希望这里下雨", checks);
                yield return Wait(() => tutorial.State.stage == LifeOnboardingStage.CastRain && life.Progress.FindTask("dog.rain.lesson")?.status == LifeTaskStatus.Active, 5, "Real request response creates the active rain task");
                var input = game.GetComponent<LifeGestureInput>(); SubmitWorldInk(input, "rain", checks);
                Require(life.LocalRainActive && tutorial.State.rainCast, "Production recognition triggers real local rain and its successful-cast hook", checks);
                yield return Wait(() => life.Progress.FindTask("dog.rain.lesson")?.status == LifeTaskStatus.Complete && tutorial.State.stage == LifeOnboardingStage.ClearWeather, 12, "Actual world rainfall satisfies the post-acceptance rain task");
                input.Cancel(); SubmitWorldInk(input, "clear", checks); input.Cancel();
                yield return Wait(() => tutorial.State.stage == LifeOnboardingStage.ReturnVisit && !life.LocalRainActive, 5, "Real clear gesture stops the local weather");
                var bowl = life.World.Objects.FirstOrDefault(o => o && o.State.id == "dog.bowl");
                float water = bowl ? bowl.State.water : 0; yield return null;
                Require(!bowl || Mathf.Approximately(water, bowl.State.water), "Stopping rain does not immediately erase collected bowl water", checks);
                Near(game, capsule, dog.Actor, 5.8f); Aim(camera, dog.Actor);
                yield return Wait(() => tutorial.State.returnAway && LifeOnboardingSpeech.Current(dog.Actor)?.Request.glyphIds.Contains("goodbye") == true && hud.IsAnimalSpeechVisible(dog.Actor), 8, "Walking away produces an actually visible goodbye");
                yield return Hold(1.1f); Near(game, capsule, dog.Actor, 2.2f); Aim(camera, dog.Actor);
                yield return Wait(() => tutorial.State.stage == LifeOnboardingStage.Complete, 8, "Returning produces an actually visible hello and finishes the tutorial");
                Require(!tutorial.TutorialActive && tutorial.SocialGreetingsEnabled && tutorial.State.rainCast && tutorial.State.clearCast, "Completion releases teaching while preserving future social greetings", checks);
                var disk = JsonUtility.FromJson<LifeOnboardingState>(File.ReadAllText(tutorial.StatePath));
                Require(disk.stage == LifeOnboardingStage.Complete && disk.returnAway && disk.rainCast && disk.clearCast, "Final sidecar persists the real rain/clear/return checkpoint", checks);
                Require(string.IsNullOrEmpty(game.LastSaveError) && string.IsNullOrEmpty(life.LastSaveError) && string.IsNullOrEmpty(tutorial.SaveError), "All isolated writes report success", checks);
            }
            finally
            {
                game.GetComponent<LifeGestureInput>()?.Cancel(); if (hud.IsDialogueOpen) hud.CloseDialogue();
                if (hud.JournalOpen) { game.GetComponent<LifeJournal3D>()?.Cancel(); Call(hud, "CompleteJournalClose"); }
                mover.ReleaseScriptedInput(); mover.transform.SetPositionAndRotation(player, rotation); camera.transform.SetPositionAndRotation(cameraPosition, cameraRotation);
                if (orbit) orbit.enabled = orbitEnabled; Physics.SyncTransforms();
            }
        }
        static void Guard(LifeSession life)
        {
            string path = Environment.GetEnvironmentVariable("WEATHERHOUSE_LIFE_REVIEW_SAVE"), root = Path.GetFullPath("LifeGameplayReview") + Path.DirectorySeparatorChar;
            if (!EditorApplication.isPlaying || !life || !life.World || !life.Gameplay || Environment.GetEnvironmentVariable(LifeOnboarding.ReviewOptIn) != "1" || string.IsNullOrEmpty(path) ||
                !Path.GetFullPath(path).StartsWith(root, StringComparison.OrdinalIgnoreCase) || Path.GetFullPath(path) != Path.GetFullPath(GameplaySession.PersistenceDirectory) ||
                SceneManager.GetActiveScene().path != "Assets/Scenes/WeatherHouse_WatersideV2.unity") throw new InvalidOperationException("Dedicated opted-in isolated formal-scene Play required.");
        }
        static void Reply(GameplayHud hud, string text, List<string> checks)
        {
            hud.InteractPrimary(); Require(hud.IsDialogueOpen, "Existing E interaction opens the actual dialogue", checks);
            var field = Field<InputField>(hud, "replyInput"); field.text = text; field.onSubmit.Invoke(text);
            Require(!hud.IsDialogueOpen, "Existing reply submit callback returns to the world", checks);
        }
        static void DrawPractice(LifeGesturePad pad, string id, List<string> checks)
        {
            Require(pad && pad.gestureId == id && pad.input.CanPractice(id), "Actual practice pad is bound to " + id, checks);
            typeof(LifeGesturePointerChecks).GetMethod("Draw", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { pad, GestureCatalog.Find(id).points, checks });
        }
        static void SubmitWorldInk(LifeGestureInput input, string id, List<string> checks)
        {
            var samples = Field<List<Vector2>>(input, "points"); samples.Clear(); var controls = GestureCatalog.Find(id).points;
            for (int segment = 1; segment < controls.Length; segment++) for (int i = 0; i < 12; i++) samples.Add(Vector2.Lerp(controls[segment - 1], controls[segment], i / 11f) * 100f);
            Require(LifeGestureInput.ValidStroke(samples) && GestureCatalog.TryRecognize(samples, out string found, out _) && found == id, "Declared world ink samples are recognized as " + id, checks);
            Call(input, "SubmitStroke");
        }
        static void Near(GameplaySession game, CharacterController capsule, AnimalActor animal, float distance)
        {
            foreach (float angle in new[] { 90f, 0f, 180f, 270f, 45f, 135f, 225f, 315f }) if (LifeFreshPhotos.TryStand(game, capsule, animal.transform.position, distance, angle, out Vector3 point))
            { game.PlayerTransform.position = point; Physics.SyncTransforms(); return; }
            throw new InvalidOperationException("No supported fixture viewpoint near the dog.");
        }
        static void Aim(Camera camera, AnimalActor animal)
        { camera.transform.position = animal.BubbleAnchor + new Vector3(4f, 3.2f, -4f); camera.transform.LookAt(animal.BubbleAnchor); }
        static void Click(Transform root, string name, List<string> checks)
        {
            var button = root.GetComponentsInChildren<Button>().Single(b => b.name == name); Canvas.ForceUpdateCanvases();
            var e = new PointerEventData(EventSystem.current) { position = RectTransformUtility.WorldToScreenPoint(null, button.transform.position), button = PointerEventData.InputButton.Left };
            var hits = new List<RaycastResult>(); EventSystem.current.RaycastAll(e, hits);
            Require(button.IsInteractable() && hits.Count > 0 && hits[0].gameObject.GetComponentInParent<Button>() == button, "Actual pointer raycast reaches " + name, checks);
            ExecuteEvents.Execute(button.gameObject, e, ExecuteEvents.pointerClickHandler);
        }
        static IEnumerator Wait(Func<bool> done, float timeout, string label)
        { float end = Time.realtimeSinceStartup + timeout; while (!done() && Time.realtimeSinceStartup < end) yield return null; if (!done()) throw new TimeoutException(label); }
        static IEnumerator WaitBook(GameplayHud hud) => Wait(() => hud.JournalOpen && !hud.IsAlbumOpening, 18, "Physical weather book settles");
        static IEnumerator Hold(float seconds) { float end = Time.realtimeSinceStartup + seconds; while (Time.realtimeSinceStartup < end) yield return null; }
        static T Field<T>(object value, string name) => (T)value.GetType().GetField(name, Hidden).GetValue(value);
        static void Call(object value, string name) => value.GetType().GetMethod(name, Hidden).Invoke(value, null);
        static void Require(bool ok, string label, List<string> checks) { checks.Add((ok ? "PASS " : "FAIL ") + label); if (!ok) throw new InvalidOperationException(label); }
    }
}
