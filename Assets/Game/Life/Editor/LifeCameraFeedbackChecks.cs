using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using WeatherHousePrototype;
using WeatherHouse.WildlifeJourney;
using Object = UnityEngine.Object;

namespace WeatherHouse.Life.Editor
{
    public static class LifeCameraFeedbackChecks
    {
        const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        [MenuItem("Weather House/Review 20261027/Check camera feedback isolated")]
        public static void Begin()
        {
            var life = Object.FindObjectOfType<LifeSession>();
            string directory = Environment.GetEnvironmentVariable("WEATHERHOUSE_LIFE_REVIEW_SAVE");
            if (!Application.isBatchMode || !Application.isPlaying || !SessionState.GetBool("WeatherHouse.FeedbackReview20261028", false) ||
                !life || !life.World || !life.Gameplay || string.IsNullOrEmpty(directory) ||
                !Path.GetFullPath(directory).StartsWith(Path.GetFullPath("LifeGameplayReview/20261028/feedback") + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                Path.GetFullPath(directory) != Path.GetFullPath(GameplaySession.PersistenceDirectory) ||
                SceneManager.GetActiveScene().path != "Assets/Scenes/WeatherHouse_WatersideV2.unity")
                throw new InvalidOperationException("Camera checks require the owned feedback isolated Play.");
            life.StartCoroutine(Run(life, directory));
        }
        static IEnumerator Run(LifeSession life, string directory)
        {
            var lines = new List<string>(); IEnumerator steps = Steps(life, lines); Exception failure = null;
            try
            {
                while (true)
                {
                    bool more; object next = null;
                    try { more = steps.MoveNext(); if (more) next = steps.Current; }
                    catch (Exception error) { failure = error; break; }
                    if (!more) break; yield return next;
                }
            }
            finally
            {
                try { (steps as IDisposable)?.Dispose(); } catch (Exception error) { failure = failure ?? error; }
                string result = (failure == null ? "PASS" : "FAIL") + " camera feedback\n" + string.Join("\n", lines) + "\n" + failure +
                    "\nBOUNDARY real session/HUD/recorder and social schedule; targets, expired greeting deadline and viewpoint are declared fixtures. Captures remain in this isolated run. No OS keyboard/mouse or framing acceptance claim.";
                File.WriteAllText(Path.Combine(directory, "camera-feedback-checks.txt"), result); Debug.Log(result);
            }
        }
        static IEnumerator Steps(LifeSession life, List<string> lines)
        {
            var game = life.Gameplay; var hud = game.GetComponent<GameplayHud>(); var rig = game.PhotoRig;
            Check(hud && !hud.CameraMode && !hud.JournalOpen && !game.Recorder.IsRecording && !game.forestSprite.ScriptedInput,
                "Camera checks start from idle isolated gameplay", lines);
            string notebook = JsonUtility.ToJson(game.Notebook), progress = JsonUtility.ToJson(life.Progress), status = game.Status;
            var observed = game.Observed; float next = Field<float>(game, "nextObservation");
            float yaw = Field<float>(rig, "photoYaw"), pitch = Field<float>(rig, "photoPitch"), focal = rig.FocalLength;
            var files = new[] { game.SavePath, life.SavePath }.ToDictionary(path => path, path => File.Exists(path) ? File.ReadAllBytes(path) : null);
            var fixture = new GameObject("Camera feedback isolated targets") { hideFlags = HideFlags.DontSave };
            var mover = game.forestSprite; mover.SetScriptedInput(Vector2.zero, false, false);
            Set(game, "nextObservation", float.PositiveInfinity);
            try
            {
                Vector3 forward = Quaternion.Euler(0, game.PlayerViewCamera.transform.eulerAngles.y, 0) * Vector3.forward;
                Vector3 position = game.PlayerTransform.position + Vector3.up * Mathf.Max(.2f, mover.cameraHeight) + forward * 2f - Vector3.up * 1.35f;
                var a = MakeActor(fixture.transform, "camera-feedback-a", position);
                var b = MakeActor(fixture.transform, "camera-feedback-b", position + Vector3.right * 2f);
                CheckTemporaryGreeting(game, a, fixture.transform, lines);
                CheckSocial(game, fixture.transform, lines);
                CheckPublicContext(life, lines);
                Observed(game, a); hud.SetCameraMode(true, true); Aim(game, a); Show(hud);
                Check(Mathf.Approximately(rig.FocalLength, 24f) && !mover.InputBlocked && mover.JumpBlocked && mover.MovementCameraOverride == rig.CaptureCamera,
                    "The initial 24 mm photo view permits movement, reserves jump and uses its own movement direction", lines);
                string held = game.DisplayedUtteranceId(a); string[] glyphs = game.DisplayedGlyphIds(a).ToArray();
                a.GetComponent<LifeSpeaker>().topicIndex = 1; Show(hud);
                Check(a.UtteranceId != held && game.DisplayedUtteranceId(a) == held && hud.IsAnimalSpeechVisible(a) &&
                    Field<List<Text>>(hud, "bubbleMeanings").Where(label => label.transform.parent.gameObject.activeSelf).Select(label => label.name).SequenceEqual(glyphs),
                    "Actual HUD keeps the original words when the target's live request changes", lines);
                hud.SetCameraMode(false); Check(hud.CameraMode && !game.IsPhotoSpeechHeld(a), "Closing releases speech while the existing camera fade continues", lines);
                hud.SetCameraMode(true); Show(hud);
                Check(game.IsPhotoSpeechHeld(a) && game.DisplayedUtteranceId(a) == a.UtteranceId,
                    "Reopening during the closing fade captures the current turn again", lines);
                Observed(game, b); Call(game, "RefreshPhotoSpeechTarget");
                Check(!game.IsPhotoSpeechHeld(a) && game.IsPhotoSpeechHeld(b), "Changing A to B replaces only the held target", lines);
                Observed(game, null); Call(game, "RefreshPhotoSpeechTarget");
                Check(!game.IsPhotoSpeechHeld(a) && !game.IsPhotoSpeechHeld(b), "Looking away releases both prior targets", lines);

                Observed(game, a); Call(game, "RefreshPhotoSpeechTarget"); Aim(game, a); Show(hud);
                held = game.DisplayedUtteranceId(a); glyphs = game.DisplayedGlyphIds(a).ToArray();
                a.GetComponent<LifeSpeaker>().topicIndex = 0;
                var herd = Object.FindObjectsOfType<JourneyHerd>().FirstOrDefault(value => value.isActiveAndEnabled && !value.Paused);
                float simulationTime = herd ? herd.SimulationTime : 0; Vector3 animalPosition = a.transform.position;
                game.TakePhoto(); var pending = Field<ObservationRecord>(game.Recorder, "pending");
                Check(pending != null && pending.utteranceId == held && pending.glyphIds.SequenceEqual(glyphs) && pending.context?.sequenceId.EndsWith("sun", StringComparison.Ordinal) == true,
                    "Real shutter metadata and semantic context match the retained visible turn", lines);
                string photoId = pending.id; float deadline = Time.realtimeSinceStartup + 15;
                while (game.Recorder.IsRecording && Time.realtimeSinceStartup < deadline) yield return null;
                Check(!game.Recorder.IsRecording && game.Notebook.observations.Any(photo => photo.id == photoId && photo.frameCount == MotionPhotoRecorder.FrameCount) && !game.IsPhotoSpeechHeld(a),
                    "A complete real Live capture releases the target even though its live request changed", lines);
                Call(game, "RefreshPhotoSpeechTarget");
                Check(!game.IsPhotoSpeechHeld(a) && game.DisplayedUtteranceId(a) == a.UtteranceId, "The unchanged target resumes live updates after capture", lines);
                Check(Vector3.Distance(animalPosition, a.transform.position) > .0001f && (!herd || herd.SimulationTime > simulationTime),
                    "Native animal movement and the live herd clock continue during capture", lines);

                game.BeginPhotoSpeech(); game.Recorder.enabled = false; game.TakePhoto(); game.Recorder.enabled = true;
                Check(!game.IsPhotoSpeechHeld(a) && !game.Recorder.IsRecording, "A real disabled-recorder failure releases the retained turn", lines);
                game.BeginPhotoSpeech(); Show(hud); game.TakePhoto();
                Check(game.Recorder.IsRecording, "The early-exit check starts a real recording", lines);
                hud.SetCameraMode(false, true);
                Check(!game.IsPhotoSpeechHeld(a) && !game.Recorder.IsRecording && !hud.CameraMode, "Exiting during recording preserves the still and releases all camera speech", lines);
                Observed(game, a); hud.SetCameraMode(true, true); Aim(game, a); Show(hud); game.TakePhoto();
                Check(game.Recorder.IsRecording, "The target-switch callback check starts a real recording", lines);
                Observed(game, b); Call(game, "RefreshPhotoSpeechTarget"); game.Recorder.FinishLiveCapture();
                Check(game.IsPhotoSpeechHeld(b) && !game.IsPhotoSpeechHeld(a), "Finishing A's recording cannot clear B's newer held turn", lines);
            }
            finally
            {
                hud.SetCameraMode(false, true); game.EndPhotoSpeech();
                game.Recorder.enabled = true; Observed(game, observed); Set(game, "nextObservation", next);
                Set(rig, "photoYaw", yaw); Set(rig, "photoPitch", pitch);
                typeof(PhotoCameraRig).GetProperty("FocalLength").SetValue(rig, focal); rig.CaptureCamera.focalLength = focal;
                mover.ReleaseScriptedInput(); Object.DestroyImmediate(fixture);
                JsonUtility.FromJsonOverwrite(notebook, game.Notebook); JsonUtility.FromJsonOverwrite(progress, life.Progress);
                foreach (var file in files)
                    if (file.Value != null) File.WriteAllBytes(file.Key, file.Value);
                    else if (File.Exists(file.Key)) File.Delete(file.Key);
                game.Notify(status); game.RefreshInputBlocking();
            }
        }
        static AnimalActor MakeActor(Transform parent, string id, Vector3 position)
        {
            var root = new GameObject(id); root.transform.SetParent(parent, false); root.transform.position = position;
            var animal = root.AddComponent<AnimalActor>(); animal.animalId = id; animal.displayName = id; Set(animal, "terrain", null);
            var speaker = root.AddComponent<LifeSpeaker>(); speaker.Actor = animal;
            speaker.requests = new[] { new LifeRequest { id = id + "-rain", glyphIds = new[] { "want", "rain" }, condition = "conversation" },
                new LifeRequest { id = id + "-sun", glyphIds = new[] { "sunlight" }, condition = "conversation" } };
            return animal;
        }
        static void CheckTemporaryGreeting(GameplaySession game, AnimalActor animal, Transform parent, List<string> lines)
        {
            var ownerObject = new GameObject("Inactive greeting owner"); ownerObject.transform.SetParent(parent, false); ownerObject.SetActive(false);
            var owner = ownerObject.AddComponent<LifeOnboarding>(); typeof(LifeOnboarding).GetProperty("State").SetValue(owner, new LifeOnboardingState { started = true, stage = LifeOnboardingStage.Complete });
            var suppression = typeof(LifeOnboarding).GetField("reviewSuppressed", BindingFlags.Static | BindingFlags.NonPublic); object previous = suppression.GetValue(null);
            var speech = animal.gameObject.AddComponent<LifeOnboardingSpeech>(); speech.owner = owner;
            try
            {
                suppression.SetValue(null, false); speech.Speak("hello", 10); Observed(game, animal); game.BeginPhotoSpeech();
                string utterance = animal.UtteranceId; Set(speech, "until", Time.unscaledTime - 1f);
                Check(!LifeOnboardingSpeech.Current(animal) && game.DisplayedUtteranceId(animal) == utterance && game.DisplayedGlyphIds(animal).SequenceEqual(new[] { "you", "hello" }),
                    "An actually expired temporary greeting remains the retained photo turn", lines);
                game.EndPhotoSpeech(); Check(game.DisplayedUtteranceId(animal) == animal.UtteranceId && animal.UtteranceId != utterance,
                    "Releasing the greeting reveals the current authored request", lines);
            }
            finally { game.EndPhotoSpeech(); suppression.SetValue(null, previous); Object.DestroyImmediate(speech); }
        }
        static void CheckSocial(GameplaySession game, Transform parent, List<string> lines)
        {
            var root = new GameObject("Isolated camera social"); root.transform.SetParent(parent, false);
            var factory = typeof(WeatherHouse.WildlifeJourney.Editor.JourneySocialChecks); const BindingFlags flags = BindingFlags.Static | BindingFlags.NonPublic;
            RenderTexture target = null;
            try
            {
                var participants = (JourneySocialParticipant[])factory.GetMethod("Participants", flags).Invoke(null, new object[] { root.transform, 2 });
                var social = (JourneySocial)factory.GetMethod("Social", flags).Invoke(null, new object[] { root.transform });
                var view = (Camera)factory.GetMethod("View", flags).Invoke(null, new object[] { root.transform });
                target = new RenderTexture(320, 200, 16); view.targetTexture = target; social.viewCamera = view;
                social.Tick(0, participants); social.Tick(1.2f, participants); social.Tick(1.21f, participants); factory.GetMethod("ShowFully", flags).Invoke(null, new object[] { social });
                Check(social.ActiveConversation != null && social.VisibleBubbleCount == 1, "The sheep fixture has an actual visible social turn", lines);
                var herd = root.AddComponent<JourneyHerd>(); herd.social = social;
                herd.members = participants.Select(p => new JourneyMember { id = p.id, root = p.root, headBone = p.head, visualRoot = p.root }).ToArray();
                var animals = new Dictionary<int, AnimalActor>();
                foreach (var participant in participants)
                {
                    var ecology = participant.root.gameObject.AddComponent<WildlifeInteractionTarget>(); Set(ecology, "herd", herd); Set(ecology, "memberId", participant.id); Set(ecology, "stableId", "camera-sheep-" + participant.id);
                    var actor = participant.root.gameObject.AddComponent<AnimalActor>(); actor.animalId = "camera-sheep-" + participant.id; animals.Add(participant.id, actor);
                }
                var first = social.ActiveConversation; var animal = animals[first.speakerId]; Observed(game, animal); game.BeginPhotoSpeech();
                string id = game.DisplayedUtteranceId(animal); social.Tick(first.endsAt + .01f, participants); social.Tick(first.endsAt + social.turnGap + .02f, participants);
                factory.GetMethod("ShowFully", flags).Invoke(null, new object[] { social });
                Check(social.ActiveConversation != null && social.ActiveConversation.speakerId != first.speakerId && social.PresentationConversation.speakerId == first.speakerId && social.VisibleBubbleCount == 1 && game.DisplayedUtteranceId(animal) == id,
                    "The real social schedule changes speakers while its photo presentation keeps the first turn", lines);
                var second = animals[social.ActiveConversation.speakerId]; Observed(game, second); Call(game, "RefreshPhotoSpeechTarget"); factory.GetMethod("ShowFully", flags).Invoke(null, new object[] { social });
                factory.GetMethod("ShowFully", flags).Invoke(null, new object[] { social });
                Check(game.IsPhotoSpeechHeld(second) && social.PresentationConversation.speakerId == social.ActiveConversation.speakerId &&
                    Field<JourneyConversation>(social, "presentedConversation")?.speakerId == second.GetComponent<WildlifeInteractionTarget>().SocialMemberId && social.VisibleBubbleCount == 1 &&
                    game.DisplayedGlyphIds(second).SequenceEqual(social.PresentationConversation.glyphIds), "Switching to the second sheep aligns its visible social bubble and photo glyphs", lines);
            }
            finally { game.EndPhotoSpeech(); Object.DestroyImmediate(root); if (target) Object.DestroyImmediate(target); }
        }
        static void CheckPublicContext(LifeSession life, List<string> lines)
        {
            var game = life.Gameplay; var dog = life.World.FindSpeaker("dog"); var requests = dog.requests; int topic = dog.topicIndex;
            var current = Field<LifeRequest>(dog, "lastCurrent"); int revision = dog.Revision;
            Vector3 playerPosition = game.PlayerTransform.position;
            var contexts = Field<List<LifePublicContext>>(life.World, "photoContexts"); var original = contexts.ToArray();
            var windows = Field<Dictionary<string, float>>(life.World, "photoContextUntil"); var oldWindows = windows.ToArray();
            try
            {
                game.PlayerTransform.position = dog.transform.position;
                foreach (string word in new[] { "hello", "goodbye", "self" })
                {
                    var request = new LifeRequest { id = "camera-context-" + word, speakerKey = "dog", listenerKey = "player", condition = "conversation", glyphIds = word == "self" ? new[] { "self", "you" } : new[] { word } };
                    dog.requests = new[] { request }; dog.topicIndex = 0; Observed(game, dog.Actor); game.BeginPhotoSpeech();
                    request.glyphIds[0] = "rain"; Call(game, "ObserveDisplayedConversation", dog.Actor);
                    string kind = word == "self" ? "roles" : word;
                    Check(contexts.Any(value => value.kind == kind && value.id.Contains("camera-context-" + word)),
                        "First visible retained " + kind + " publishes its original public context despite later live mutation", lines);
                    if (word == "hello")
                    {
                        string key = contexts.Last(value => value.kind == kind).id; windows[key] = Time.time - 1; Call(game, "ObserveDisplayedConversation", dog.Actor);
                        Check(windows[key] > Time.time, "The shutter's same context-refresh path renews an expired retained greeting window", lines);
                    }
                    game.EndPhotoSpeech();
                }
            }
            finally
            {
                game.EndPhotoSpeech(); dog.requests = requests; dog.topicIndex = topic; Set(dog, "lastCurrent", current);
                game.PlayerTransform.position = playerPosition;
                typeof(LifeSpeaker).GetProperty("Revision").SetValue(dog, revision); contexts.Clear(); contexts.AddRange(original); windows.Clear(); foreach (var pair in oldWindows) windows.Add(pair.Key, pair.Value);
            }
        }
        static void Aim(GameplaySession game, AnimalActor animal)
        {
            Vector3 eye = game.PlayerTransform.position + Vector3.up * Mathf.Max(.2f, game.forestSprite.cameraHeight);
            Vector3 angle = Quaternion.LookRotation(animal.BubbleAnchor - eye).eulerAngles;
            Set(game.PhotoRig, "photoYaw", angle.y); Set(game.PhotoRig, "photoPitch", angle.x > 180 ? angle.x - 360 : angle.x); Call(game.PhotoRig, "UpdatePose");
        }
        static void Show(GameplayHud hud)
        {
            Call(hud, "ClearAnimalBubble"); Call(hud, "UpdateAnimalBubble"); Call(hud, "UpdateAnimalPresentation", .3f); Canvas.ForceUpdateCanvases();
        }
        static void Observed(GameplaySession game, AnimalActor animal) => typeof(GameplaySession).GetProperty("Observed").SetValue(game, animal);
        static T Field<T>(object value, string name) => (T)value.GetType().GetField(name, Hidden).GetValue(value);
        static void Set(object value, string name, object data) => value.GetType().GetField(name, Hidden).SetValue(value, data);
        static void Call(object value, string name, params object[] args) => value.GetType().GetMethod(name, Hidden).Invoke(value, args);
        static void Check(bool ok, string label, List<string> lines) { lines.Add((ok ? "PASS " : "FAIL ") + label); if (!ok) throw new InvalidOperationException(label); }
    }
}
