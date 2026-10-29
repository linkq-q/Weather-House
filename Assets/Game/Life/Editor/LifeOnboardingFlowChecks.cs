using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using WeatherHousePrototype;

namespace WeatherHouse.Life.Editor
{
    // Run only from the dedicated review after its fresh isolated Play session starts.
    // Player viewpoints are fixtures; tutorial progress, observations, guesses and photos are real gameplay outputs.
    public static class LifeOnboardingFlowChecks
    {
        const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        const string Scope = "FirstBook -> visible greetings/farewells -> local guesses -> actual speech photo -> evidence link -> FindRainRequest";
        public static IEnumerator Run(LifeSession life, Action<string> completed)
        {
            var lines = new List<string>(); var stack = new Stack<IEnumerator>();
            stack.Push(Steps(life, lines)); Exception failure = null; float began = Time.realtimeSinceStartup;
            while (stack.Count > 0)
            {
                object current = null; bool more = false;
                try
                {
                    if (Time.realtimeSinceStartup - began > 240) throw new TimeoutException("Onboarding smoke exceeded 240 seconds.");
                    more = stack.Peek().MoveNext(); if (more) current = stack.Peek().Current;
                }
                catch (Exception error) { failure = error; break; }
                if (!more) { (stack.Pop() as IDisposable)?.Dispose(); continue; }
                if (current is IEnumerator nested) stack.Push(nested); else yield return current;
            }
            while (stack.Count > 0)
                try { (stack.Pop() as IDisposable)?.Dispose(); } catch (Exception error) { failure = failure ?? error; }
            string result = (failure == null ? "PASS" : "FAIL") + " onboarding first-loop smoke\n" + Scope + "\n" + string.Join("\n", lines) +
                "\nNOT COVERED: full opening playback/composition, keyboard hardware, wrong-guess AI/free hint, weather practice/casting/clearing and final return visit.\n" +
                (failure == null ? "" : failure.ToString());
            // The guard below is intentionally repeated before any output write.
            if (SafeReview(life)) File.WriteAllText(Path.Combine(GameplaySession.PersistenceDirectory, "onboarding-flow-checks.txt"), result);
            completed?.Invoke(result);
        }

        static IEnumerator Steps(LifeSession life, List<string> lines)
        {
            Require(SafeReview(life), "Dedicated opt-in, formal-scene isolated Play environment", lines);
            var game = life.Gameplay; var hud = game.GetComponent<GameplayHud>();
            var tutorial = game.GetComponent<LifeOnboarding>();
            yield return Wait(() => tutorial && tutorial.State != null, 15, "Controller bootstrap");
            Require(tutorial.TutorialActive && tutorial.State.stage <= LifeOnboardingStage.GuessBook,
                "Fresh tutorial or existing genuine observation/guess checkpoint; no checkpoint injection", lines);
            if (tutorial.State.stage == LifeOnboardingStage.Opening)
            {
                yield return Wait(() => game.GetComponent<LifeOpeningCinematic>()?.IsPlaying == true, 10, "Opening begins");
                game.GetComponent<LifeOpeningCinematic>().Skip();
            }
            if (tutorial.State.stage == LifeOnboardingStage.FirstBook)
                yield return Wait(() => !tutorial.IsBlockingInput, 5, "Opening hands off to first book");
            Require(!hud.JournalOpen && !hud.CameraMode && !hud.IsDialogueOpen, "Fresh or resumed HUD starts idle", lines);
            Require(game.Notebook.GetWord("want")?.confirmed == true && game.Notebook.GetWord("reject")?.confirmed == true,
                "Initial want/reject remain confirmed", lines);
            lines.Add("CHECKPOINT actual stage=" + tutorial.State.stage + ", distinct=" + tutorial.State.animalsSeen.Count + ", hello=" + tutorial.State.helloSeen + ", goodbye=" + tutorial.State.goodbyeSeen);
            var mover = game.forestSprite; var capsule = mover ? mover.GetComponent<CharacterController>() : null;
            Require(mover && capsule && capsule.enabled && !mover.ScriptedInput, "Live player is available for declared viewpoint fixtures", lines);
            Vector3 position = mover.transform.position; Quaternion rotation = mover.transform.rotation;
            mover.SetScriptedInput(Vector2.zero, false, false);
            try
            {
                if (tutorial.State.stage == LifeOnboardingStage.FirstBook)
                {
                    hud.ToggleJournal(); yield return WaitBook(hud);
                    while (tutorial.State.stage == LifeOnboardingStage.FirstBook)
                    {
                        yield return null;
                        yield return LifeOnboardingSmokeFrames.Capture(life, "first-book-" + (tutorial.State.firstBookPage + 1));
                        Click(FindNoteButton(game), lines, "First-book note " + (tutorial.State.firstBookPage + 1)); yield return null;
                    }
                    Require(tutorial.State.stage == LifeOnboardingStage.ObserveAnimals && tutorial.State.booksIntroduced.Contains("first"),
                        "Actual note callbacks hand off to animal observation", lines);
                    yield return Wait(() => !hud.IsAlbumOpening, 20, "First-book animation settles before the close decision");
                    if (hud.JournalOpen) hud.ToggleJournal();
                    yield return Wait(() => !hud.JournalOpen, 12, "Book closes");
                }
                else lines.Add("RESUMED existing observed checkpoint; completed first-book clicks are not repeated.");
                var friends = new[] { "dog", "squirrel", "fox", "deer", "sheep" }.Select(key => life.World.FindSpeaker(key)?.Actor)
                    .Where(a => a && a.IsAvailableForObservation).Distinct().ToArray();
                Require(friends.Length >= 3, "At least three live distinct story animals exist", lines);
                if (tutorial.State.stage == LifeOnboardingStage.ObserveAnimals)
                {
                    hud.SetCameraMode(true, true);
                    // Do not demand a particular squirrel when three other real individuals were already observed.
                    // Product gates remain three stable IDs and a genuinely visible farewell.
                    foreach (var actor in friends)
                    {
                        if (tutorial.State.animalsSeen.Count >= 3 && tutorial.State.helloSeen && tutorial.State.goodbyeSeen) break;
                        yield return ObserveFromVisibleStand(life, hud, capsule, actor, lines);
                    }
                }
                yield return Wait(() => tutorial.State.stage == LifeOnboardingStage.GuessBook, 5, "Three animals plus both utterances unlock guessing");
                Require(tutorial.State.animalsSeen.Distinct().Count() >= 3 && tutorial.State.helloSeen && tutorial.State.goodbyeSeen &&
                    game.Notebook.GetWord("hello")?.encountered == true && game.Notebook.GetWord("goodbye")?.encountered == true,
                    "Observation gate comes from distinct animals and real hello/goodbye notebook evidence", lines);
                hud.SetCameraMode(false, true); hud.ToggleJournal(); yield return WaitBook(hud);
                foreach (var pair in new[] { new[] { "hello", "你好" }, new[] { "goodbye", "再见" } })
                {
                    if (Known(game, pair[0])) { lines.Add("RESUMED previously confirmed " + pair[0]); continue; }
                    if (hud.OnboardingDetailOpen) Click(FindButton(Get<RectTransform>(hud, "detailRoot"), "Close"), lines, "Close previous word");
                    // This is the production tutorial focus operation; opening the word still uses its physical-page pointer.
                    hud.OnboardingSelectBook(0, pair[0]); yield return WaitBook(hud); yield return null;
                    yield return LifeOnboardingSmokeFrames.Capture(life, "guess-glyph-" + pair[0]);
                    Click(FindButton(Get<RectTransform>(hud, "albumRoot"), "Word " + pair[0]), lines, "Physical-page word " + pair[0]);
                    yield return null;
                    Require(hud.OnboardingSelectedWord == pair[0], "Word pointer opens the matching detail " + pair[0], lines);
                    yield return LifeOnboardingSmokeFrames.Capture(life, "guess-input-" + pair[0]);
                    var input = Get<InputField>(hud, "guessInput"); Require(input, "Real word input exists", lines);
                    input.text = pair[1]; Click(FindButton(Get<RectTransform>(hud, "detailRoot"), "Save guess"), lines, "Submit local synonym " + pair[0]);
                    yield return Wait(() => Known(game, pair[0]), 5, "Local synonym is confirmed through production judgement");
                    yield return ConfirmedLayoutFrame(hud);
                }
                yield return Wait(() => tutorial.State.stage == LifeOnboardingStage.TakeSpeechPhoto, 5, "Both known words unlock camera prompt");
                int earned = tutorial.State.earnedWords.Count;
                game.SaveGuess("hello", "你好"); yield return null;
                Require(tutorial.State.earnedWords.Count == earned && earned == 2 && tutorial.State.HintsAvailable == 0,
                    "Repeated confirmed guess and initial words never mint extra hint credits", lines);
                hud.ToggleJournal(); yield return Wait(() => !hud.JournalOpen, 12, "Close before photographing");
                var subject = friends[0];
                hud.SetCameraMode(true, true);
                MoveNear(game, capsule, subject, 5.7f); Aim(game, subject.BubbleAnchor, false); yield return Hold(8.3f);
                MoveNear(game, capsule, subject, 2.2f); Aim(game, subject.BubbleAnchor, false);
                float speechDeadline = Time.realtimeSinceStartup + 7;
                while (!(hud.IsAnimalSpeechVisible(subject) && LifeOnboardingSpeech.Current(subject)) && Time.realtimeSinceStartup < speechDeadline)
                { Aim(game, subject.BubbleAnchor, false); yield return null; }
                Require(hud.IsAnimalSpeechVisible(subject) && LifeOnboardingSpeech.Current(subject), "Actual visible speech is ready at shutter", lines);
                var snapshot = FrameSpeechPhoto(life, hud, capsule, subject);
                Require(snapshot != null && string.IsNullOrEmpty(snapshot.visibilityError) &&
                    snapshot.entities.Any(e => e.id == subject.animalId && e.bodyVisible) && hud.IsAnimalSpeechVisible(subject),
                    "Actual depth snapshot and visible speech agree at the final shutter viewpoint", lines);
                int before = game.Notebook.observations.Count; game.TakePhoto();
                Require(game.Recorder.IsRecording, "Ordinary TakePhoto starts its real recorder", lines);
                yield return Wait(() => !game.Recorder.IsRecording, 20, "Static photo finishes");
                Require(game.Notebook.observations.Count == before + 1, "Exactly one actual photograph is saved", lines);
                var photo = game.Notebook.observations.Last();
                Require(photo.frameCount == MotionPhotoRecorder.FrameCount && !photo.liveTruncated && string.IsNullOrEmpty(photo.captureWarning) &&
                    photo.glyphIds.Contains("hello") && photo.photoSnapshot.entities.Any(e => e.id == subject.animalId && e.bodyVisible),
                    "Saved complete photo contains the real speaking animal and hello", lines);
                yield return Wait(() => tutorial.State.stage == LifeOnboardingStage.PhotoBook, 5, "Actual photograph unlocks evidence lesson");
                hud.SetCameraMode(false, true); hud.ToggleJournal(); yield return WaitBook(hud); yield return null;
                Click(FindButton(Get<RectTransform>(hud, "albumRoot"), "Photo " + photo.id), lines, "Open saved photograph"); yield return null;
                yield return LifeOnboardingSmokeFrames.Capture(life, "photo-word-link");
                Click(FindButton(Get<RectTransform>(hud, "detailRoot"), "Photo word link hello"), lines, "Photo word pointer"); yield return null;
                Require(hud.OnboardingSelectedWord == "hello", "Photo word opens the matching real notebook word", lines);
                yield return LifeOnboardingSmokeFrames.Capture(life, "photo-evidence-button");
                Click(FindButton(Get<RectTransform>(hud, "detailRoot"), "Word evidence"), lines, "View related photographs"); yield return null;
                Require(tutorial.State.evidenceViewed && game.Notebook.FindEvidence("hello").Any(p => p.id == photo.id),
                    "Evidence lesson requires a saved matching photograph", lines);
                yield return LifeOnboardingSmokeFrames.Capture(life, "photo-evidence-page");
                if (hud.HasPhotoTaskBook) { yield return null; Click(FindNoteButton(game), lines, "View photography challenge"); yield return WaitBook(hud); yield return null; }
                else Require(!Get<RectTransform>(hud,"albumRoot").Find("Photography tasks tab"),"Uncollected task book stays locked throughout the opening",lines);
                Click(FindNoteButton(game), lines, "Continue to first request"); yield return null;
                Require(tutorial.State.stage == LifeOnboardingStage.FindRainRequest, "Production callbacks finish the first tutorial loop", lines);
                Require(string.IsNullOrEmpty(game.LastSaveError) && string.IsNullOrEmpty(tutorial.SaveError), "Isolated notebook and tutorial writes succeed", lines);
                var disk = JsonUtility.FromJson<LifeOnboardingState>(File.ReadAllText(tutorial.StatePath));
                Require(disk.stage == tutorial.State.stage && disk.earnedWords.Count == tutorial.State.earnedWords.Count && disk.evidenceViewed,
                    "Written checkpoint preserves actual stage, hint ledger and evidence visit", lines);
            }
            finally
            {
                game.Recorder?.FinishLiveCapture();
                if (hud.CameraMode) hud.SetCameraMode(false, true);
                if (hud.JournalOpen) { game.GetComponent<LifeJournal3D>()?.Cancel(); Call(hud, "CompleteJournalClose"); }
                if (hud.IsDialogueOpen) hud.CloseDialogue();
                life.SetGestureInput(false); mover.ReleaseScriptedInput(); mover.transform.SetPositionAndRotation(position, rotation); Physics.SyncTransforms();
            }
        }

        static IEnumerator ObserveFromVisibleStand(LifeSession life, GameplayHud hud, CharacterController capsule, AnimalActor actor, List<string> lines)
        {
            var game = life.Gameplay; var tutorial = game.GetComponent<LifeOnboarding>(); bool framed = false;
            foreach (float distance in new[] { 2.2f, 2.7f })
            {
                foreach (float angle in new[] { 90f, 0f, 180f, 270f, 45f, 135f, 225f, 315f })
                {
                    if (!LifeFreshPhotos.TryStand(game, capsule, actor.transform.position, distance, angle, out Vector3 point)) continue;
                    game.PlayerTransform.position = point; Physics.SyncTransforms();
                    float end = Time.realtimeSinceStartup + 1.7f;
                    while (Time.realtimeSinceStartup < end)
                    {
                        Aim(game, Vector3.Lerp(LifeFreshPhotos.BoundsOf(actor).center, actor.BubbleAnchor, .4f), false);
                        var speech = LifeOnboardingSpeech.Current(actor);
                        if (game.Observed == actor && hud.IsAnimalSpeechVisible(actor) && speech && speech.Request.glyphIds.Contains("hello")) { framed = true; break; }
                        yield return null;
                    }
                    if (framed) break;
                    lines.Add("VIEW candidate " + actor.animalId + " distance=" + distance + " angle=" + angle + " observed=" + game.Observed?.animalId + " ambient=" + actor.HasAmbientPresentation + " ownSpeech=" + (LifeOnboardingSpeech.Current(actor)?.Utterance ?? "none") + " visible=" + hud.IsAnimalSpeechVisible(actor));
                }
                if (framed) break;
            }
            if (!framed) { lines.Add("NOT_OBSERVED no genuinely visible hello found for " + actor.animalId + "; no progress injected"); yield break; }
            // Give the production coordinator one Update to consume the actual visible utterance.
            yield return null;
            Require(tutorial.State.animalsSeen.Contains(actor.animalId), "Actual visible greeting is recorded for " + actor.animalId, lines);
            if (tutorial.State.goodbyeSeen) yield break;
            foreach (float angle in new[] { 90f, 0f, 180f, 270f, 45f, 135f, 225f, 315f })
            {
                if (!LifeFreshPhotos.TryStand(game, capsule, actor.transform.position, 5.7f, angle, out Vector3 point)) continue;
                game.PlayerTransform.position = point; Physics.SyncTransforms();
                float end = Time.realtimeSinceStartup + .8f;
                while (Time.realtimeSinceStartup < end && !tutorial.State.goodbyeSeen)
                { Aim(game, Vector3.Lerp(LifeFreshPhotos.BoundsOf(actor).center, actor.BubbleAnchor, .4f), false); yield return null; }
                if (tutorial.State.goodbyeSeen) { lines.Add("PASS Actual visible farewell is recorded for " + actor.animalId); yield break; }
            }
            lines.Add("NOT_OBSERVED no genuinely visible farewell at tested supported viewpoints for " + actor.animalId);
        }

        static IEnumerator ConfirmedLayoutFrame(GameplayHud hud)
        {
            // Notify rebuilds the smaller confirmed card and moves its Close button.
            yield return null;
            yield return Wait(() => !hud.IsAlbumOpening && !Get<bool>(hud, "dirty"), 8, "Confirmed word layout settles");
            yield return new WaitForEndOfFrame();
        }

        static bool SafeReview(LifeSession life)
        {
            string isolated = Environment.GetEnvironmentVariable("WEATHERHOUSE_LIFE_REVIEW_SAVE");
            string root = Path.GetFullPath("LifeGameplayReview") + Path.DirectorySeparatorChar;
            return Application.isPlaying && life && life.World && life.Gameplay &&
                Environment.GetEnvironmentVariable(LifeOnboarding.ReviewOptIn) == "1" && !string.IsNullOrEmpty(isolated) &&
                Path.GetFullPath(isolated).StartsWith(root, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(Path.GetFullPath(isolated), Path.GetFullPath(GameplaySession.PersistenceDirectory), StringComparison.OrdinalIgnoreCase) &&
                SceneManager.GetActiveScene().path == "Assets/Scenes/WeatherHouse_WatersideV2.unity";
        }
        static bool Known(GameplaySession game, string id) => game.Notebook.GetWord(id)?.confirmed == true;
        static IEnumerator Wait(Func<bool> done, float seconds, string label)
        { float end = Time.realtimeSinceStartup + seconds; while (!done() && Time.realtimeSinceStartup < end) yield return null; if (!done()) throw new TimeoutException(label); }
        static IEnumerator WaitBook(GameplayHud hud) => Wait(() => hud.JournalOpen && !hud.IsAlbumOpening, 18, "Physical book settles");
        static IEnumerator Hold(float seconds) { float end = Time.realtimeSinceStartup + seconds; while (Time.realtimeSinceStartup < end) yield return null; }
        static Button FindNoteButton(GameplaySession game) => FindButton(game.GetComponentInChildren<LifeOnboardingView>().transform, "Continue note");
        static Button FindButton(Transform root, string name)
        {
            if (!root) throw new InvalidOperationException("Missing UI root for " + name);
            var button = root.GetComponentsInChildren<Button>(true).FirstOrDefault(b => b.name == name && b.gameObject.activeInHierarchy);
            if (!button) throw new InvalidOperationException("Missing active button " + name); return button;
        }
        static void Click(Button button, List<string> lines, string label)
        {
            Require(EventSystem.current && button && button.IsInteractable(), "Interactive " + label, lines);
            Canvas.ForceUpdateCanvases();
            var pointer = new PointerEventData(EventSystem.current) { position = RectTransformUtility.WorldToScreenPoint(null, button.transform.position), button = PointerEventData.InputButton.Left };
            var hits = new List<RaycastResult>(); EventSystem.current.RaycastAll(pointer, hits);
            Require(hits.Count > 0 && hits[0].gameObject.GetComponentInParent<Button>() == button,
                "Actual UI raycast reaches " + label + (hits.Count > 0 ? "; top=" + hits[0].gameObject.name : "; no hit"), lines);
            ExecuteEvents.Execute(button.gameObject, pointer, ExecuteEvents.pointerClickHandler);
        }
        static void MoveNear(GameplaySession game, CharacterController capsule, AnimalActor animal, float distance)
        {
            foreach (float angle in new[] { 90f, 0f, 180f, 270f, 45f, 135f, 225f, 315f })
                if (LifeFreshPhotos.TryStand(game, capsule, animal.transform.position, distance, angle, out Vector3 point))
                { game.PlayerTransform.position = point; Physics.SyncTransforms(); return; }
            throw new InvalidOperationException("No supported player viewpoint near " + animal.animalId + " at " + distance);
        }
        static void Aim(GameplaySession game, Vector3 target, bool behind)
        {
            Vector3 eye = game.PlayerTransform.position + Vector3.up * Mathf.Max(.2f, game.forestSprite.cameraHeight);
            Vector3 angles = Quaternion.LookRotation(target - eye).eulerAngles; if (behind) angles.y += 180;
            typeof(PhotoCameraRig).GetProperty("Yaw", Hidden).SetValue(game.PhotoRig, angles.y);
            typeof(PhotoCameraRig).GetProperty("Pitch", Hidden).SetValue(game.PhotoRig, Mathf.DeltaAngle(0, angles.x));
            Call(game.PhotoRig, "UpdatePose");
        }
        static LifePhotoSnapshot FrameSpeechPhoto(LifeSession life, GameplayHud hud, CharacterController capsule, AnimalActor animal)
        {
            var game = life.Gameplay;
            foreach (float distance in new[] { 3.4f, 2.7f })
                foreach (float angle in new[] { 90f, 0f, 180f, 270f, 45f, 135f, 225f, 315f })
                {
                    if (!LifeFreshPhotos.TryStand(game, capsule, animal.transform.position, distance, angle, out Vector3 point)) continue;
                    game.PlayerTransform.position = point; Physics.SyncTransforms();
                    Aim(game, Vector3.Lerp(LifeFreshPhotos.BoundsOf(animal).center, animal.BubbleAnchor, .35f), false);
                    if (!hud.IsAnimalSpeechVisible(animal)) continue;
                    var view = LifePhotoCapture.Capture(life, game.PhotoRig.CaptureCamera);
                    if (!string.IsNullOrEmpty(view.visibilityError)) throw new InvalidOperationException("Real depth preflight failed: " + view.visibilityError);
                    if (view.entities.Any(e => e.id == animal.animalId && e.bodyVisible)) return view;
                }
            return null;
        }
        static T Get<T>(object owner, string field) => (T)owner.GetType().GetField(field, Hidden).GetValue(owner);
        static void Call(object owner, string method) => owner.GetType().GetMethod(method, Hidden).Invoke(owner, null);
        static void Require(bool value, string message, List<string> lines)
        { if (!value) throw new InvalidOperationException(message); lines.Add("PASS " + message); }
    }
}

