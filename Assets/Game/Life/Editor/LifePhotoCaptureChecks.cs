using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
using WeatherHousePrototype;
using Object = UnityEngine.Object;

namespace WeatherHouse.Life.Editor
{
    public static class LifePhotoCaptureChecks
    {
        public static IEnumerator RunPlayChecks()
        {
            if (!Application.isPlaying) throw new InvalidOperationException("Photo capture checks require Play mode.");
            var fixture = new GameObject("Isolated photography revision checks");
            var lines = new List<string>();
            var ownedIds = new List<string>();
            void Check(bool condition, string label) { if (!condition) throw new InvalidOperationException(label); lines.Add("PASS " + label); }
            try
            {
                fixture.transform.position = new Vector3(30000, 30000, 30000);
                var camera = fixture.AddComponent<Camera>(); camera.enabled = false;
                camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.2f, .45f, .7f);
                camera.cullingMask = 0;
                var recorder = fixture.AddComponent<MotionPhotoRecorder>();
                var book = new LanguageNotebook();
                string error = null;
                string id = Guid.NewGuid().ToString("N"); ownedIds.Add(id);
                Check(recorder.Capture(camera, new ObservationRecord { id = id, photoSnapshot = new LifePhotoSnapshot() },
                    record => book.AddObservation(record), message => error = message), "Animal-free shutter starts capture");
                Check(File.Exists(Path.Combine(Folder(id), "frame_000.png")), "Ordinary still is rendered synchronously at shutter time");
                float began = Time.realtimeSinceStartup;
                while (recorder.IsRecording && Time.realtimeSinceStartup - began < 12f) yield return null;
                Check(!recorder.IsRecording && error == null && book.observations.Count == 1 && book.observations[0].frameCount == 1,
                    "Unmatched landscape saves a real single-frame photo without an animal");
                Check(book.ApplyPhotoTasks() == 0 && book.observations.Count == 1, "No matching task never deletes the landscape");
                id = Guid.NewGuid().ToString("N"); ownedIds.Add(id);
                bool sameTurn = true;
                var context = new LifeContextSnapshot { speakerId = "a", listenerId = "player", targetId = "original", anchorPosition = new Vector3(1, 2, 3) };
                var shot = new ObservationRecord { id = id, animalId = "a", utteranceId = "original-turn", glyphIds = new[] { "come", "here" },
                    context = context, photoSnapshot = new LifePhotoSnapshot { entities = new[] { new LifePhotoEntity { id = "fox", kind = "animal", speciesId = "fox", bodyVisible = true, sleeping = true } } } };
                Check(recorder.Capture(camera, shot, record => book.AddObservation(record), message => error = message, () => sameTurn), "Contextual shutter starts capture");
                sameTurn = false; context.targetId = "later"; shot.glyphIds[0] = "go"; shot.photoSnapshot.entities[0].sleeping = false;
                began = Time.realtimeSinceStartup;
                while (recorder.IsRecording && Time.realtimeSinceStartup - began < 3f) yield return null;
                Check(!recorder.IsRecording && error == null && book.observations.Count == 2,
                    "An immediate dialogue change retains the ordinary photo");
                var truncated = book.observations[1];
                Check(truncated.frameCount == 1 && !truncated.liveTruncated && truncated.utteranceId == "original-turn" &&
                    truncated.glyphIds[0] == "come" && truncated.context.targetId == "original" && truncated.photoSnapshot.entities[0].sleeping,
                    "The still contains only the original turn and preserves shutter context, anchors and sleeping state");
                Check(Directory.GetFiles(Folder(id), "frame_*.png").Length == 1 && File.Exists(Path.Combine(Folder(id), "metadata.json")),
                    "Truncation leaves a complete one-frame photo and metadata on disk");
                var reread = JsonUtility.FromJson<ObservationRecord>(File.ReadAllText(Path.Combine(Folder(id), "metadata.json")));
                Check(!reread.liveTruncated && reread.context.anchorPosition == new Vector3(1, 2, 3) && reread.photoSnapshot.entities[0].sleeping,
                    "Saved replay context survives metadata reload");
                Check(book.ApplyPhotoTasks() == 2 && book.GetWord("fox").encountered && book.GetWord("sleep").encountered &&
                    !book.GetWord("fox").confirmed && !book.GetWord("sleep").confirmed,
                    "A static photo completes portrait and sleep tasks without confirming their meanings");
                var texture = MotionPhotoRecorder.LoadFrame(truncated, 0);
                Check(texture.width == 2560 && texture.height == 1440, "High-resolution still loads through the real album decoder");
                Object.Destroy(texture);
                IEnumerator integration = CheckGameplayEntry(lines, ownedIds);
                try { while (integration.MoveNext()) yield return integration.Current; }
                finally { (integration as IDisposable)?.Dispose(); }
            }
            finally
            {
                fixture.GetComponent<MotionPhotoRecorder>()?.CancelCapture();
                Object.Destroy(fixture);
                foreach (string id in ownedIds)
                {
                    string path = Folder(id);
                    if (Directory.Exists(path)) Directory.Delete(path, true);
                }
                Directory.CreateDirectory("LifeGameplayReview/20261026/photo-flow");
                File.WriteAllLines("LifeGameplayReview/20261026/photo-flow/capture-play-checks.txt", lines);
            }
        }

        static IEnumerator CheckGameplayEntry(List<string> lines, List<string> ownedIds)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            void Check(bool condition, string label) { if (!condition) throw new InvalidOperationException(label); lines.Add("PASS " + label); }
            var game = Object.FindObjectOfType<GameplaySession>();
            Check(game && !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WEATHERHOUSE_LIFE_REVIEW_SAVE")), "Actual gameplay photography checks use isolated persistence");
            var hud = game.GetComponent<GameplayHud>();
            Check(hud && !hud.JournalOpen && !hud.IsDialogueOpen && !hud.CameraMode && !game.Recorder.IsRecording,
                "Actual gameplay entry starts idle");
            string original = JsonUtility.ToJson(game.Notebook), status = game.Status;
            var observed = game.Observed;
            var property = typeof(GameplaySession).GetProperty("Observed");
            var pitch = typeof(PhotoCameraRig).GetProperty("Pitch", flags);
            var updatePose = typeof(PhotoCameraRig).GetMethod("UpdatePose", flags);
            var pendingField = typeof(MotionPhotoRecorder).GetField("pending", flags);
            try
            {
                hud.SetCameraMode(true, true);
                pitch.SetValue(game.PhotoRig, -85f); updatePose.Invoke(game.PhotoRig, null);
                property.SetValue(game, null);
                int before = game.Notebook.observations.Count;
                game.TakePhoto();
                Check(game.Recorder.IsRecording && pendingField.GetValue(game.Recorder) is ObservationRecord, "Real TakePhoto accepts no Observed animal and starts its recorder");
                var pending = (ObservationRecord)pendingField.GetValue(game.Recorder); ownedIds.Add(pending.id);
                float deadline = Time.realtimeSinceStartup + 12f;
                while (game.Recorder.IsRecording && Time.realtimeSinceStartup < deadline) yield return null;
                Check(!game.Recorder.IsRecording && game.Notebook.observations.Count == before + 1, "No-Observed sky photo reaches the actual notebook index");
                var saved = game.Notebook.observations.Last();
                Check(string.IsNullOrEmpty(saved.animalId) && saved.glyphIds.Length == 0 && saved.frameCount == 1 && saved.photoSnapshot != null &&
                    File.Exists(Path.Combine(Folder(saved.id), "frame_000.png")), "Actual free photo stores image and shutter snapshot with no fabricated speaker");
                Check(JsonUtility.FromJson<LanguageNotebook>(File.ReadAllText(game.SavePath)).observations.Any(photo => photo.id == saved.id),
                    "Actual free photo persists through the notebook disk path");
                var beforeDepth = typeof(LifePhotoCapture).GetField("BeforeDepthQuery", BindingFlags.Static | BindingFlags.NonPublic);
                Check(beforeDepth != null, "Recognition failure checks use the capture-only depth injection point");
                Action originalDepth = (Action)beforeDepth.GetValue(null);
                string rewardsBeforeFailure = string.Join(",", game.Notebook.completedPhotoTasks.OrderBy(task => task));
                string wordsBeforeFailure = string.Join(",", game.Notebook.words.Where(word => word.confirmed).Select(word => word.glyphId).OrderBy(word => word));
                string historyBeforeFailure = JsonUtility.ToJson(new LifePhotoSnapshot { contexts = game.Notebook.publicHistory.ToArray() });
                try
                {
                    foreach (bool readback in new[] { false, true })
                    {
                        string injected = readback ? "Injected depth readback failure at C:/photo-test/private-readback" : "Injected missing depth shader at C:/photo-test/private-shader";
                        beforeDepth.SetValue(null, (Action)(() => { if (readback) throw new UnityException(injected); throw new InvalidOperationException(injected); }));
                        property.SetValue(game, null); before = game.Notebook.observations.Count;
                        game.TakePhoto();
                        Check(game.Recorder.IsRecording && pendingField.GetValue(game.Recorder) is ObservationRecord,
                            "A real " + (readback ? "readback" : "shader") + " sampling exception still starts the ordinary photograph");
                        pending = (ObservationRecord)pendingField.GetValue(game.Recorder); ownedIds.Add(pending.id);
                        beforeDepth.SetValue(null, originalDepth);
                        if (readback) hud.SetCameraMode(false, true);
                        else
                        {
                            deadline = Time.realtimeSinceStartup + 12f;
                            while (game.Recorder.IsRecording && Time.realtimeSinceStartup < deadline) yield return null;
                        }
                        saved = game.Notebook.observations.Last();
                        Check(!game.Recorder.IsRecording && game.Notebook.observations.Count == before + 1 && saved.id == pending.id &&
                            saved.frameCount == (readback ? 1 : MotionPhotoRecorder.FrameCount) && File.Exists(Path.Combine(Folder(saved.id), "frame_000.png")),
                            "Recognition failure preserves " + (readback ? "the lowered-camera still" : "the static photograph") + " and the real notebook entry");
                        Check(saved.photoSnapshot != null && saved.photoSnapshot.visibilityError.Contains(injected) &&
                            saved.photoSnapshot.entities.Length == 0 && saved.photoSnapshot.contexts.Length == 0 && saved.matchedPhotoTaskIds.Length == 0 &&
                            string.Join(",", game.Notebook.completedPhotoTasks.OrderBy(task => task)) == rewardsBeforeFailure &&
                            string.Join(",", game.Notebook.words.Where(word => word.confirmed).Select(word => word.glyphId).OrderBy(word => word)) == wordsBeforeFailure,
                            "Failed scene recognition keeps empty shutter facts and grants no photographic task or word");
                        LifePhotoSnapshot fallback = saved.photoSnapshot.Copy();
                        fallback.entities = new[] { new LifePhotoEntity { id = "untrusted-fallback", kind = "animal", speciesId = "fox", bodyVisible = true, sleeping = true } };
                        Check(LifePhotoRules.Match(fallback, null).Length == 0,
                            "A recognition-error snapshot cannot award fallback portrait or sleep facts");
                        var reloaded = JsonUtility.FromJson<LanguageNotebook>(File.ReadAllText(game.SavePath));
                        var persisted = reloaded.observations.Single(photo => photo.id == saved.id);
                        Check(string.IsNullOrEmpty(game.LastSaveError) && persisted.photoSnapshot.visibilityError.Contains(injected) &&
                            persisted.photoSnapshot.entities.Length == 0 && persisted.photoSnapshot.contexts.Length == 0 &&
                            !LifePhotoRules.Match(persisted.photoSnapshot, reloaded.publicHistory).Any(task => task.StartsWith("P", StringComparison.Ordinal)) &&
                            reloaded.ApplyPhotoTasks() == 0 && JsonUtility.ToJson(new LifePhotoSnapshot { contexts = game.Notebook.publicHistory.ToArray() }) == historyBeforeFailure,
                            "Recognition failure and zero photo rewards survive actual disk reload without rewriting public history");
                        var image = MotionPhotoRecorder.LoadFrame(saved, 0);
                        Check(image.width == MotionPhotoRecorder.Width && image.height == MotionPhotoRecorder.Height &&
                            game.Status.Contains("画面识别暂时失败") && !game.Status.Contains(injected) && !game.Status.Contains("C:/photo-test"),
                            "The retained image decodes and player feedback reports recognition failure without technical details");
                        Object.Destroy(image); hud.SetCameraMode(true, true);
                    }
                }
                finally { beforeDepth.SetValue(null, originalDepth); }
                property.SetValue(game, null); before = game.Notebook.observations.Count;
                game.TakePhoto();
                pending = (ObservationRecord)pendingField.GetValue(game.Recorder); ownedIds.Add(pending.id);
                hud.SetCameraMode(false, true);
                Check(!game.Recorder.IsRecording && !hud.CameraMode && game.Notebook.observations.Count == before + 1,
                    "Normal UI camera lowering finishes the still and retains the pressed shutter");
                saved = game.Notebook.observations.Last();
                Check(saved.frameCount == 1 && !saved.liveTruncated && string.IsNullOrEmpty(saved.photoSnapshot?.visibilityError) &&
                    File.Exists(Path.Combine(Folder(saved.id), "frame_000.png")) &&
                    JsonUtility.FromJson<LanguageNotebook>(File.ReadAllText(game.SavePath)).observations.Any(photo => photo.id == saved.id),
                    "Immediate camera lowering persists an actual one-frame photo and index");

                foreach (string exit in new[] { "disable", "quit-callback" })
                {
                    hud.SetCameraMode(true, true); property.SetValue(game, null); before = game.Notebook.observations.Count;
                    game.TakePhoto(); pending = (ObservationRecord)pendingField.GetValue(game.Recorder); ownedIds.Add(pending.id);
                    if (exit == "disable") game.enabled = false;
                    else typeof(GameplaySession).GetMethod("OnApplicationQuit", flags).Invoke(game, null);
                    Check(!game.Recorder.IsRecording && game.Notebook.observations.Count == before + 1 &&
                        JsonUtility.FromJson<LanguageNotebook>(File.ReadAllText(game.SavePath)).observations.Any(photo => photo.id == pending.id && photo.frameCount == 1),
                        "Actual gameplay " + exit + " lifecycle preserves the shutter and persisted index");
                    game.enabled = true; hud.SetCameraMode(false, true);
                }

                hud.SetCameraMode(true, true); property.SetValue(game, null); before = game.Notebook.observations.Count;
                game.TakePhoto(); pending = (ObservationRecord)pendingField.GetValue(game.Recorder); ownedIds.Add(pending.id);
                Directory.CreateDirectory(Path.Combine(Folder(pending.id), "frame_001.png"));
                deadline = Time.realtimeSinceStartup + 5f;
                while (game.Recorder.IsRecording && Time.realtimeSinceStartup < deadline) yield return null;
                saved = game.Notebook.observations.Last();
                Check(!game.Recorder.IsRecording && game.Notebook.observations.Count == before + 1 && saved.frameCount == 1 && !saved.liveTruncated &&
                    string.IsNullOrEmpty(saved.captureWarning) && File.Exists(Path.Combine(Folder(saved.id), "frame_000.png")),
                    "Static capture never attempts a second frame, even when that path is blocked");
                Check(JsonUtility.FromJson<LanguageNotebook>(File.ReadAllText(game.SavePath)).observations.Any(photo => photo.id == saved.id && string.IsNullOrEmpty(photo.captureWarning)),
                    "The complete static photo survives actual index persistence");
                hud.SetCameraMode(false, true);

                hud.SetCameraMode(true, true); property.SetValue(game, null); before = game.Notebook.observations.Count;
                game.TakePhoto(); pending = (ObservationRecord)pendingField.GetValue(game.Recorder); ownedIds.Add(pending.id);
                Directory.CreateDirectory(Path.Combine(Folder(pending.id), "metadata.json"));
                hud.SetCameraMode(false, true); saved = game.Notebook.observations.Last();
                Check(saved.id == pending.id && saved.frameCount == 1 && !string.IsNullOrEmpty(saved.captureWarning) &&
                    JsonUtility.FromJson<LanguageNotebook>(File.ReadAllText(game.SavePath)).observations.Any(photo => photo.id == saved.id && photo.photoSnapshot != null),
                    "A real blocked metadata sidecar preserves the image and full replay snapshot in the notebook index");

                byte[] lastGoodIndex = File.ReadAllBytes(game.SavePath);
                using (var lockedIndex = new FileStream(game.SavePath, FileMode.Open, FileAccess.Read, FileShare.None))
                {
                    hud.SetCameraMode(true, true); property.SetValue(game, null); before = game.Notebook.observations.Count;
                    game.TakePhoto(); pending = (ObservationRecord)pendingField.GetValue(game.Recorder); ownedIds.Add(pending.id);
                    hud.SetCameraMode(false, true);
                    Check(game.Notebook.observations.Count == before + 1 && !string.IsNullOrEmpty(game.LastSaveError) &&
                        !game.Status.StartsWith("照片已放进手账", StringComparison.Ordinal), "A locked real notebook reports failure without claiming the new index was saved");
                }
                Check(lastGoodIndex.SequenceEqual(File.ReadAllBytes(game.SavePath)) && File.Exists(Path.Combine(Folder(pending.id), "frame_000.png")),
                    "Failed photo indexing keeps the previous index byte-for-byte and preserves the new image");

                var life = game.GetComponent<LifeSession>(); var speaker = life.World.FindSpeaker("dog");
                int oldTopic = speaker.topicIndex; var oldRequests = speaker.requests; Vector3 oldPosition = game.PlayerTransform.position;
                string world = life.World.CaptureJson(), progress = JsonUtility.ToJson(life.Progress);
                try
                {
                    speaker.topicIndex = Array.FindIndex(speaker.requests, request => request.glyphIds.Contains("hello"));
                    Check(speaker.topicIndex >= 0, "The first-dialogue regression uses an authored greeting");
                    // Isolate re-presenting the same greeting from E's normal automatic topic progression.
                    speaker.requests = new[] { speaker.requests[speaker.topicIndex] }; speaker.topicIndex = 0;
                    life.Progress.heardRequests.Remove(speaker.Current.id);
                    game.PlayerTransform.position = speaker.transform.position + Vector3.right * 1.5f; Physics.SyncTransforms();
                    property.SetValue(game, speaker.Actor);
                    string greetingId = "hello:" + speaker.Current.id + ":" + speaker.Actor.animalId;
                    var priorGreeting = game.Notebook.publicHistory.Find(c => c.id == greetingId);
                    string priorUtc = priorGreeting?.occurredUtc;
                    int historyCount = game.Notebook.publicHistory.Count;
                    hud.ToggleDialogue();
                    deadline = Time.realtimeSinceStartup + 3f;
                    while (Time.realtimeSinceStartup < deadline && game.Notebook.publicHistory.Find(c => c.id == greetingId)?.occurredUtc == priorUtc) yield return null;
                    var greeting = game.Notebook.publicHistory.Find(c => c.id == greetingId);
                    Check(!hud.IsDialogueOpen && life.Progress.HasHeard(speaker.Current.id) && greeting != null && greeting.occurredUtc != priorUtc && greeting.actorId == speaker.Actor.animalId &&
                        greeting.glyphIds.SequenceEqual(speaker.Current.glyphIds) && game.Notebook.publicHistory.Count == historyCount + (priorGreeting == null ? 1 : 0),
                        "First actual HUD dialogue records its visible greeting without requiring NextTopic");
                    historyCount = game.Notebook.publicHistory.Count; priorUtc = greeting.occurredUtc;
                    var windows = (Dictionary<string, float>)typeof(LifeWorld).GetField("photoContextUntil", flags).GetValue(life.World);
                    float firstWindow = windows[greetingId];
                    // Reopening is a separate player input; Time.time cannot advance within the same Unity frame.
                    yield return null;
                    hud.CloseDialogue(); property.SetValue(game, speaker.Actor); hud.ToggleDialogue();
                    deadline = Time.realtimeSinceStartup + 3f;
                    while (Time.realtimeSinceStartup < deadline && game.Notebook.publicHistory.Find(c => c.id == greetingId)?.occurredUtc == priorUtc) yield return null;
                    Check(game.Notebook.publicHistory.Find(c => c.id == greetingId)?.occurredUtc != priorUtc && windows[greetingId] > firstWindow &&
                        game.Notebook.publicHistory.Count == historyCount, "Reopening the same displayed greeting refreshes its photo window without growing public history");
                }
                finally
                {
                    hud.CloseDialogue(); speaker.requests = oldRequests; speaker.topicIndex = oldTopic; game.PlayerTransform.position = oldPosition;
                    JsonUtility.FromJsonOverwrite(progress, life.Progress); life.World.RestoreJson(world); life.SaveAndNotify(""); life.World.RestorePublicPhotoContexts(JsonUtility.FromJson<LanguageNotebook>(original).publicHistory);
                }
            }
            finally
            {
                game.Recorder.CancelCapture(); game.enabled = true; hud.SetCameraMode(false, true);
                property.SetValue(game, observed);
                JsonUtility.FromJsonOverwrite(original, game.Notebook);
                game.SaveNotebook(); game.Notify(status);
            }
        }

        static string Folder(string id)
        {
            if (!Guid.TryParseExact(id, "N", out _)) throw new ArgumentException("Invalid owned test photo ID");
            string root = Path.GetFullPath(Path.Combine(GameplaySession.PersistenceDirectory, "MotionPhotos"));
            string path = Path.GetFullPath(Path.Combine(root, id));
            if (!path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal)) throw new InvalidOperationException("Test folder escaped persistence root");
            return path;
        }
    }
}
