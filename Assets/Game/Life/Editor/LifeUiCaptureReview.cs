using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using WeatherHousePrototype;
using WeatherHouse.WatersideV2;

namespace WeatherHouse.Life.Editor
{
    // Only GameView pixels are evidence. Every arranged or reused state is declared separately.
    public static class LifeUiCaptureReview
    {
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        [Serializable] sealed class Shot
        {
            public string id, state, file, capturedUtc, capturedHongKong, entry, condition, source, sourceWrittenUtc, sha256;
            public int width, height, unityFrame;
            public bool fixture, reused;
        }
        [Serializable] sealed class Report
        {
            public string status = "RUNNING", startedUtc = DateTime.UtcNow.ToString("o"), finishedUtc, scene, saveDirectory, folder;
            public string boundary = "Actual Unity GameView PNGs; arranged states and earlier images have explicit provenance. No provider call, deliberate provider failure, human keyboard/IME proof or visual acceptance is claimed.";
            public string[] coveredUiIds, missingUiIds;
            public List<Shot> shots = new List<Shot>(); public List<string> gaps = new List<string>(), notes = new List<string>();
        }
        sealed class Job
        {
            public string id, state, entry, condition; public Action setup; public Func<bool> ready; public float settle = .15f;
        }
        [Serializable] sealed class FreshShot { public ObservationRecord record = null; }
        [Serializable] sealed class FreshReport { public string status = null, saveDirectory = null; public List<FreshShot> shots = null; }

        public static IEnumerator Run(LifeSession life, Action<string> completed)
        {
            IEnumerator steps = Steps(life, completed); Exception failure = null;
            while (true)
            {
                bool next; object value = null;
                try { next = steps.MoveNext(); if (next) value = steps.Current; }
                catch (Exception error) { failure = error; break; }
                if (!next) break;
                yield return value;
            }
            try { (steps as IDisposable)?.Dispose(); } catch (Exception error) { failure = failure ?? error; }
            if (failure != null) completed?.Invoke("FAIL UI capture: " + failure);
        }

        static IEnumerator Steps(LifeSession life, Action<string> completed)
        {
            var game = life ? life.Gameplay : null; var hud = life ? life.GetComponent<GameplayHud>() : null;
            Require(Application.isPlaying && life && life.World && game && hud && !game.Judging && !hud.JournalOpen && !hud.CameraMode && !hud.IsDialogueOpen &&
                !life.Progress.tasks.Any(task => task != null && task.status == LifeTaskStatus.Active) &&
                !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WEATHERHOUSE_LIFE_REVIEW_SAVE")) &&
                Path.GetFullPath(GameplaySession.PersistenceDirectory).StartsWith(Path.GetFullPath("LifeGameplayReview") + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase), "Use root's idle isolated formal-scene Play lifecycle");
            var mover = game.forestSprite; Require(mover && !mover.ScriptedInput, "Real idle player controller is required");
            var input = life.GetComponent<LifeGestureInput>(); var weather = UnityEngine.Object.FindObjectOfType<LifeLocalWeather>();
            Require(input && weather && !weather.Active, "Capture starts with initialized input and no active local weather");
            var report = new Report { folder = Path.GetFullPath("LifeGameplayReview/20261027/ui-capture/" + DateTime.UtcNow.ToString("HHmmssfff")), scene = SceneManager.GetActiveScene().path, saveDirectory = Path.GetFullPath(GameplaySession.PersistenceDirectory) };
            Directory.CreateDirectory(report.folder);
            string notebook = JsonUtility.ToJson(game.Notebook), progress = JsonUtility.ToJson(life.Progress), world = life.World.CaptureJson(), status = game.Status;
            Vector3 position = mover.transform.position; Quaternion rotation = mover.transform.rotation; AnimalActor observed = game.Observed; int taskPage = life.TaskPage;
            var objectIds = new HashSet<string>(life.World.Objects.Select(item => item.State.id));
            string[] fields = { "albumPhotosSelected", "albumGesturesSelected", "albumTasksSelected", "glyphPage", "photoTaskPage", "page", "selectedGlyph", "selectedGesture", "guessDraft", "guessGlyphId" };
            var ui = fields.ToDictionary(name => name, name => Get<object>(hud, name)); int tip = Get<int>(input, "tipLevel");
            var speakers = life.World.Requests.Select(request => request.speakerKey).Distinct().Select(key => life.World.FindSpeaker(key)).Where(speaker => speaker).ToArray(); var topics = speakers.Select(speaker => speaker.topicIndex).ToArray();
            bool finished = false;
            try
            {
                mover.SetScriptedInput(Vector2.zero, false, false); ReuseImages(report);
                var jobs = Jobs(life, report).GetEnumerator();
                while (true)
                {
                    Job job = null; bool next = false; Exception failure = null;
                    try { next = jobs.MoveNext(); if (next) job = jobs.Current; } catch (Exception error) { failure = error; }
                    if (failure != null) { report.gaps.Add("Capture sequence interrupted: " + failure); break; }
                    if (!next) { finished = true; break; }
                    // A previous actual recording completes before a later UI action closes the camera.
                    float deadline = Time.realtimeSinceStartup + 16;
                    while (game.Recorder.IsRecording && Time.realtimeSinceStartup < deadline) yield return null;
                    try { Require(!game.Recorder.IsRecording, "Prior recording timed out"); job.setup?.Invoke(); } catch (Exception error) { failure = error; }
                    if (failure != null) { report.gaps.Add(job.id + "/" + job.state + ": " + failure); Write(report); continue; }
                    deadline = Time.realtimeSinceStartup + 16;
                    bool ready = job.ready == null;
                    while (!ready && Time.realtimeSinceStartup < deadline)
                    {
                        try { ready = job.ready(); } catch (Exception error) { failure = error; break; }
                        if (!ready) yield return null;
                    }
                    if (failure != null || !ready) { report.gaps.Add(job.id + "/" + job.state + ": " + (failure?.ToString() ?? "presentation did not settle")); continue; }
                    yield return new WaitForSecondsRealtime(job.settle);
                    yield return new WaitForEndOfFrame();
                    try { Capture(report, job); } catch (Exception error) { report.gaps.Add(job.id + "/" + job.state + ": " + error); }
                }
                report.gaps.Add("U22: standalone onboarding UI is not implemented; planning HTML is not a game screenshot.");
                report.gaps.Add("U05 reply echo was removed from the game and is intentionally excluded; U04 checks listening without reply UI.");
                report.gaps.Add("U03 ecology bubble variant; U17 feed/invite-rest variants; U20 night/overcast and alternate resolutions need root's separate actual setups.");
                report.gaps.Add("U18 post-hello/post-rain guidance and U19 unlearned/cancel variants are not separately captured in this batch. Timed book images cannot establish smooth animation; root's visual review remains.");
            }
            finally
            {
                try
                {
                Close(life); if (weather) weather.Cast("clear", out _); Set(life.World, "held", null); Set(life.World, "craftingId", null);
                var objects = Get<IDictionary>(life.World, "objects"); var interactive = Get<ICollection<LifeObject>>(life.World, "interactables");
                foreach (LifeObject item in life.World.Objects.Where(item => item && !objectIds.Contains(item.State.id)).ToArray())
                { objects.Remove(item.State.id); interactive.Remove(item); UnityEngine.Object.Destroy(item.gameObject); }
                JsonUtility.FromJsonOverwrite(notebook, game.Notebook); JsonUtility.FromJsonOverwrite(progress, life.Progress); life.World.RestoreJson(world);
                for (int i = 0; i < speakers.Length; i++) if (speakers[i]) speakers[i].topicIndex = topics[i];
                foreach (var field in ui) Set(hud, field.Key, field.Value); Set(input, "tipLevel", tip);
                Property(game, "Observed", observed); Property(life, "TaskPage", taskPage);
                }
                catch (Exception error) { finished = false; report.gaps.Add("State restoration failed: " + error); }
                finally { mover.transform.SetPositionAndRotation(position, rotation); mover.ReleaseScriptedInput(); Physics.SyncTransforms(); }
                try { game.SaveNotebook(); life.SaveAndNotify(status); } catch (Exception error) { finished = false; report.gaps.Add("Isolated save restoration failed: " + error); }
                report.finishedUtc = DateTime.UtcNow.ToString("o"); report.status = !finished ? "FAIL" : report.gaps.Count == 0 ? "PASS" : "PASS_WITH_GAPS"; Write(report); Gallery(report);
            }
            completed?.Invoke((finished ? "PASS" : "FAIL") + " UI capture: " + report.shots.Count + " GameView images; declared gaps=" + report.gaps.Count + ". " + report.folder + "/manifest.json");
        }

        static IEnumerable<Job> Jobs(LifeSession life, Report report)
        {
            var game = life.Gameplay; var hud = life.GetComponent<GameplayHud>(); var input = life.GetComponent<LifeGestureInput>(); var world = life.World;
            Job Make(string id, string state, string entry, string condition, Action setup, Func<bool> ready = null) => new Job { id = id, state = state, entry = entry, condition = condition, setup = setup, ready = ready };
            bool BookReady() => !hud.IsAlbumOpening;
            bool TaskCardReady()
            {
                var card = Get<RectTransform>(input, "taskRoot");
                if (!card.gameObject.activeSelf) return false;
                foreach (string part in new[] { "Paper", "Bookmark", "Meadow", "Day", "Night" })
                {
                    var image = card.Find("Weather task " + part).GetComponent<RawImage>();
                    Require(image && image.texture && image.texture == Resources.Load<Texture2D>("HudIcons/WeatherTask/" + part), "Task card keeps the approved separate " + part + " texture");
                }
                var mood = UnityEngine.Object.FindObjectOfType<WatersideMood>();
                bool night = mood && mood.Current == WatersideMood.Mode.Night;
                Require(Get<RawImage>(input, "taskNight").gameObject.activeSelf == night && Get<RawImage>(input, "taskDay").gameObject.activeSelf != night, "Task icon follows the current scene mood; no automatic clock is claimed");
                Require(!card.GetComponent<ProceduralUiGraphic>(), "Task card must not retain the old tailed bubble");
                int expectedRows = Mathf.Min(2, (life.TaskText.Split('\n').Length + 1) / 2);
                var marks = Get<LifeTaskMarkGraphic[]>(input, "taskMarks");
                if (marks.Count(mark => mark.gameObject.activeSelf) != expectedRows) return false;
                float removed = (2 - expectedRows) * (48f + 22f * .85f);
                Require(Mathf.Abs(card.sizeDelta.y - (256f - removed)) < .01f && card.sizeDelta.x == 430f && card.localScale == Vector3.one,
                    "Single-task page removes the empty row without scaling the widget");
                foreach (var mark in marks) Require(mark.transform.parent == card && mark.rectTransform.sizeDelta == new Vector2(24f, 24f), "Markers keep their authored parent and dimensions");
                foreach (var line in Get<Text[]>(input, "taskLines")) Require(line.transform.parent == card && line.fontSize == 21 && line.rectTransform.sizeDelta == new Vector2(286f, 24f), "Task text keeps 21-point type and horizontal dimensions");
                var paperRect = (RectTransform)card.Find("Weather task Paper");
                Require(Mathf.Abs(paperRect.sizeDelta.y - (220f - removed)) < .01f, "Paper height follows the visible task rows");
                foreach (var button in new[] { Get<Button>(input, "previousTaskPage"), Get<Button>(input, "nextTaskPage") })
                    Require(((RectTransform)button.transform).sizeDelta.x >= 36 && ((RectTransform)button.transform).sizeDelta.y >= 36, "Small leaf arrows retain usable hit areas");
                Text label = Get<Text[]>(input, "taskLines")[0];
                string longLine = "这是用于检查很长中文名字及任务内容不会越过天气便笺边缘的文字";
                string fitted = (string)typeof(LifeGestureInput).GetMethod("FitTaskLine", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { label, longLine });
                Require(fitted.EndsWith("…") && label.fontSize == 21 && label.cachedTextGeneratorForLayout.GetPreferredWidth(fitted, label.GetGenerationSettings(Vector2.zero)) / label.pixelsPerUnit <= label.rectTransform.sizeDelta.x, "Long Chinese is ellipsized without shrinking the selected font");
                return true;
            }
            void Rebuild() => Call(hud, "RebuildModal");
            yield return Make("U01", "idle", "Production idle HUD", "Fresh isolated world", () => game.Notify(""));
            yield return Make("U02", "success", "GameplaySession.Notify", "Existing success text fixture; not a new photo-save claim", () => game.Notify("照片已放进手账。"));
            yield return Make("U02", "cast-error", "LifeSession.TryCast / Notify", "Actual unlearned gesture rejection; no provider or weather failure induced", () => { life.Progress.learnedGestures.Remove("wind"); Require(!life.TryCast("wind", out string message), "Expected unlearned gesture rejection"); game.Notify(message); });
            yield return Make("U02", "save-error", "GameplaySession.Notify", "Existing recoverable save-error UI fixture; no actual I/O failure induced", () => game.Notify("生活进度暂未写入磁盘，当前游戏仍可继续。"));
            yield return Make("U02", "provider-error", "GameplaySession.Notify / existing status presenter", "Existing HTTP-error wording fixture (503); no request sent and no real provider failure claimed", () => game.Notify("AI服务返回HTTP 503，本次未确认任何词义。"));
            var tasks = world.Requests.Where(request => request.HasTask).GroupBy(request => request.speakerKey).Select(group => group.First()).Take(3).ToArray();
            void AddTask(LifeRequest request) => life.Progress.tasks.Add(new LifeTask { requestId = request.id, animalId = world.FindSpeaker(request.speakerKey).Actor.animalId, eventId = request.eventId, status = LifeTaskStatus.Active });
            string originalProgress = JsonUtility.ToJson(life.Progress);
            yield return Make("U16", "task-icon", "LifeGestureInput / folded task toggle", "One authored Active task, default folded icon; no AI acceptance claimed", () => { game.Notify(""); life.Progress.tasks.Clear(); Set(input, "taskExpanded", false); AddTask(tasks[0]); }, () => Get<RectTransform>(input, "taskToggleRoot").gameObject.activeSelf && !Get<RectTransform>(input, "taskRoot").gameObject.activeSelf);
            yield return Make("U16", "one-task", "Actual task icon button", "Click unfolds the approved weather note", () => Get<RectTransform>(input, "taskToggleRoot").GetComponent<Button>().onClick.Invoke(), TaskCardReady);
            yield return Make("U16", "paged-tasks", "Actual task paging controls", "Three authored Active tasks arranged in isolated Progress", () => { foreach (var task in tasks.Skip(1)) AddTask(task); }, () => TaskCardReady() && life.TaskPageCount == 2 && Get<Button>(input, "nextTaskPage").gameObject.activeSelf);
            yield return Make("U16", "page-two", "Actual next-page leaf button", "Actual second page of arranged tasks", () => Get<Button>(input, "nextTaskPage").onClick.Invoke(), () => life.TaskPage==1 && TaskCardReady());
            var taskMood = UnityEngine.Object.FindObjectOfType<WatersideMood>();
            WatersideMood.Snapshot taskMoodSnapshot = null;
            try
            {
                yield return Make("U16", "night-task", "WatersideMood.Apply(Night) / layered task icon", "Actual scene Night mode and moon layer; current game has F8 mood switching, no advancing game clock", () =>
                {
                    Require(taskMood, "Night capture requires the actual scene mood");
                    taskMoodSnapshot = taskMood.CaptureState(); taskMood.Apply(WatersideMood.Mode.Night);
                }, () => Get<RawImage>(input, "taskNight").gameObject.activeSelf && TaskCardReady());
            }
            finally
            {
                if (taskMood && taskMoodSnapshot != null) taskMood.RestoreState(taskMoodSnapshot);
            }
            report.notes.Add("U16 night-task uses actual WatersideMood.Apply(Night) and restores its captured state in finally. There is no advancing in-game clock; F8 currently changes the scene mood.");
            yield return Make("U18", "expanded-hint", "LifeGestureInput.Guidance", "H expansion state set; current production sentence", () => { JsonUtility.FromJsonOverwrite(originalProgress, life.Progress); Set(input, "tipLevel", 1); });
            var dog = world.FindSpeaker("dog");
            yield return Make("U04", "listening", "GameplayHud.ListenToAnimal", "Actual E listening at an arranged nearby speaker; no player reply or semantic judgement", () => { Set(input, "tipLevel", 0); PlaceNear(life, dog.transform.position, 2.4f); dog.topicIndex = Array.FindIndex(dog.requests, request => request.id == "dog.hello"); Property(game, "Observed", dog.Actor); hud.ListenToAnimal(); Require(!hud.IsDialogueOpen && !hud.BlocksMovement && life.Progress.HasHeard("dog.hello"), "Listening must record the request without a reply modal"); Require(!hud.GetComponentsInChildren<Transform>(true).Any(t => t.name == "Chinese reply" || t.name == "Player spoken reply"), "Retired reply UI must remain absent"); });
            yield return Make("U06", "camera", "GameplayHud.SetCameraMode", "Actual viewfinder UI at arranged viewpoint", () => { hud.SetCameraMode(true, true); });
            yield return Make("U06", "focus", "PhotoCameraRig.FocusAt", "Actual focus entry at screen center", () => game.PhotoRig.FocusAt(new Vector2(Screen.width * .5f, Screen.height * .5f)));
            yield return Make("U06", "recording", "GameplaySession.TakePhoto / actual Recorder", "Actual isolated capture in progress, no fake IsRecording state", () => { game.TakePhoto(); Require(game.Recorder.IsRecording, "TakePhoto did not start"); });
            var bowl = world.Objects.FirstOrDefault(item => item && item.State.kind == LifeObjectKind.Bowl);
            yield return Make("U21", "world-label", "LifeLabel.Refresh / InteractionHint", "Real nearby bowl; player support and capsule clearance checked", () => { hud.SetCameraMode(false, true); Require(bowl, "No actual bowl"); PlaceNear(life, bowl.transform.position, 1.4f); });
            yield return Make("U21", "camera-hides-label", "LifeLabel.Refresh(photographing=true)", "Actual camera mode hides world labels", () => hud.SetCameraMode(true, true));
            ObservationRecord[] photos = null;
            yield return Make("U08", "empty-spread", "ToggleJournal / BuildAlbumPhotos", "Empty observations fixture and real placeholder resources", () => { Close(life); ImportPhotos(game, report); photos = game.Notebook.observations.ToArray(); game.Notebook.observations.Clear(); Set(hud, "albumPhotosSelected", false); Set(hud, "albumGesturesSelected", false); Set(hud, "albumTasksSelected", false); Set(hud, "selectedGlyph", null); Set(hud, "page", 0); hud.ToggleJournal(); }, BookReady);
            yield return Make("U08", "real-photo-spread", "BuildAlbumPhotos", "Actual photographs copied from the PASS fresh-photo report into isolated save", () => { game.Notebook.observations.AddRange(photos ?? Array.Empty<ObservationRecord>()); Rebuild(); }, BookReady);
            var word = game.Notebook.GetWord("rain");
            foreach (string state in new[] { "unseen", "guess", "confirmed" })
                yield return Make("U10", "word-" + state, "BuildGlyphDetail", "Isolated rain knowledge state: " + state, () => { word.encountered = state != "unseen"; word.confirmed = state == "confirmed"; Set(hud, "selectedGlyph", "rain"); Set(hud, "guessGlyphId", "rain"); Set(hud, "glyphDetailOpen", true); Rebuild(); });
            yield return Make("U11", "live-photo", "BuildPhoto / actual recorded PNGs", "Source record ID and shutter facts preserved; arranged detail selection", () => { Call(hud, "HideDetails"); var record = game.Notebook.observations.FirstOrDefault(photo => photo != null && photo.frameCount >= 2 && File.Exists(Path.Combine(GameplaySession.PersistenceDirectory, photo.clipFolder, "frame_000.png"))); Require(record != null, "No real clip available"); Set(hud, "expandedPhoto", record); Set(hud, "currentFrame", 0); Rebuild(); });
            yield return Make("U11", "playing", "Actual Play motion photo button", "Production timed playback of actual local clip", () => Get<RectTransform>(hud, "detailRoot").GetComponentsInChildren<Button>().First(button => button.name == "Play motion photo").onClick.Invoke());
            yield return Make("U11", "ended", "GameplayHud.UpdatePlayback", "Actual timed playback ended with replay available", () => { }, () => !Get<bool>(hud, "playingPhoto"));
            yield return Make("U11", "missing", "BuildPhoto / PhotoMissing", "Missing GUID directory fixture; actual built-in missing presentation", () => { Call(hud, "HideDetails"); string id = Guid.NewGuid().ToString("N"); Set(hud, "expandedPhoto", new ObservationRecord { id = id, clipFolder = "MotionPhotos/" + id, frameCount = 2, framesPerSecond = 6, glyphIds = Array.Empty<string>() }); Rebuild(); });
            yield return Make("U12", "locked-gesture", "LifeGestureBook.Build", "Wind unconfirmed in isolated notebook", () => { Call(hud, "HideDetails"); Set(hud, "albumGesturesSelected", true); Set(hud, "albumTasksSelected", false); Set(hud, "selectedGesture", "wind"); game.Notebook.GetWord("wind").confirmed = false; Rebuild(); }, BookReady);
            yield return Make("U13", "rain-practice", "LifeGestureBook / actual animated demonstration", "Rain unlocked but unlearned state fixture", () => { life.Progress.learnedGestures.Remove("rain"); Set(hud, "selectedGesture", "rain"); Rebuild(); }, BookReady);
            yield return Make("U13", "practice-error", "LifeGestureInput.CompletePractice", "Injected reverse trajectory; actual recognizer and error presentation", () => { var pad = Get<RectTransform>(hud, "albumRoot").GetComponentInChildren<LifeGesturePad>(); Require(pad, "No real practice pad"); var path = Dense(GestureCatalog.Find("rain").points, 65); path.Reverse(); pad.ink.SetPath(path); input.CompletePractice("rain", path, pad.feedback, pad.ink); Require(!life.IsGestureLearned("rain"), "Reverse trajectory was accepted; this is not a failure-state screenshot"); });
            yield return Make("U13", "learned", "CompletePractice / LearnGesture", "Injected valid path; actual recognition and isolated learning", () => { var pad = Get<RectTransform>(hud, "albumRoot").GetComponentInChildren<LifeGesturePad>(); var path = Dense(GestureCatalog.Find("rain").points, 65); pad.ink.SetPath(path); input.CompletePractice("rain", path, pad.feedback, pad.ink); });
            yield return Make("U14", "photo-tasks", "LifePhotoBook.Build", "Current tasks and actual imported captured records", () => { Set(hud, "albumGesturesSelected", false); Set(hud, "albumTasksSelected", true); Set(hud, "photoTaskPage", 0); Rebuild(); }, BookReady);            LifeObject source = null;
            yield return Make("U17", "gather-prompt", "LifeGestureInput.Update / GatheringHint", "Player by an actual registered gathering plant, observed animal cleared", () => { Close(life); source = world.GatheringSources.Where(item => item && item.gameObject.activeInHierarchy).OrderByDescending(item => LifeWorld.Flat(item.transform.position, dog.transform.position)).FirstOrDefault(); Require(source, "No registered gathering plant"); PlaceNear(life, source.transform.position, .8f); Property(game, "Observed", null); Set(hud, "lastCompositionFrame", -10); });
            yield return Make("U17", "carried-grass", "GameplayHud.InteractPrimary / actual E gather", "Real material created through production action in isolated world", () => { Property(game, "Observed", null); hud.InteractPrimary(); Require(Get<LifeObject>(world, "held"), "E did not produce carried grass"); });
            yield return Make("U17", "crafting", "LifeWorld.Interact / StartCraft", "Actual F crafting timer, no forced completion", () => { world.Interact(); Require(Get<string>(world, "craftingId") != null, "F did not start crafting"); });
            yield return Make("U17", "carried-mat", "LifeWorld.Update / FinishCraft", "Actual runtime timer produces carried mat", () => { }, () => Get<string>(world, "craftingId") == null);
            yield return Make("U17", "placed-mat", "LifeWorld.Interact / material placement", "Actual drop or rest placement; no animal-help completion claimed", () => { Require(Get<LifeObject>(world, "held")?.State.kind == LifeObjectKind.Mat, "No crafted mat"); world.Interact(); });
            void Draw(List<Vector2> path, bool active)
            { Set(input, "worldMode", true); Set(input, "drawing", active); Get<RectTransform>(input, "drawRoot").gameObject.SetActive(true); life.SetGestureInput(true); var points = Get<List<Vector2>>(input, "points"); points.Clear(); points.AddRange(path); Get<LifeStrokeGraphic>(input, "stroke").SetPath(points); }
            yield return Make("U19", "stroke", "LifeGestureInput / actual LifeStrokeGraphic", "Valid trajectory injected into actual drawing state; not human mouse proof", () => { Close(life); life.LearnGesture("rain"); Draw(Dense(GestureCatalog.Find("rain").points, 180), true); Get<Text>(input, "drawHint").text = "按住左键描画 · Esc 收起"; });
            yield return Make("U19", "success", "SubmitStroke / actual recognizer and TryCast", "Injected trajectory, real weather application", () => { Set(input, "drawing", false); Call(input, "SubmitStroke"); });
            yield return Make("U20", "rain-and-range", "LifeGestureInput.Cancel / actual local rain and LifeCastRange", "Actual rain and lower-opacity world boundary after leaving G; no drawing overlay", () => { input.Cancel(); game.Notify(""); Require(!input.IsWorldDrawing && !life.GestureInputActive, "U20 did not leave drawing mode"); });
            yield return Make("U19", "error", "LifeGestureInput.SubmitStroke", "Injected tiny invalid path, actual error presentation", () => { Draw(new List<Vector2> { Vector2.zero, new Vector2(10, 3), new Vector2(5, 5), new Vector2(8, 4), Vector2.zero }, false); Call(input, "SubmitStroke"); });
        }
        static List<Vector2> Dense(Vector2[] controls, float scale)
        { var points = new List<Vector2>(); for (int edge = 1; edge < controls.Length; edge++) for (int step = 0; step < 10; step++) points.Add(Vector2.Lerp(controls[edge - 1], controls[edge], step / 10f) * scale); points.Add(controls[controls.Length - 1] * scale); return points; }
        static void Close(LifeSession life)
        {
            var hud = life.GetComponent<GameplayHud>(); life.GetComponent<LifeGestureInput>()?.Cancel();
            if (life.Gameplay.Recorder.IsRecording) life.Gameplay.Recorder.FinishLiveCapture();
            hud.CloseDialogue(); hud.SetCameraMode(false, true); life.GetComponent<LifeJournal3D>()?.Cancel(); Call(hud, "CompleteJournalClose");
        }
        static void PlaceNear(LifeSession life, Vector3 center, float distance)
        {
            Transform player = life.Gameplay.PlayerTransform; var capsule = player.GetComponent<CharacterController>(); Terrain terrain = Terrain.activeTerrain;
            Require(capsule && capsule.enabled && terrain && terrain.terrainData, "Player needs actual supported terrain and controller");
            var water = player.GetComponent<WatersideWaterBoundary>(); Vector3 size = terrain.terrainData.size, offset = player.TransformVector(capsule.center);
            float height = capsule.height * Mathf.Abs(player.lossyScale.y), radius = capsule.radius * Mathf.Max(Mathf.Abs(player.lossyScale.x), Mathf.Abs(player.lossyScale.z)), half = Mathf.Max(0, height * .5f - radius);
            for (int step = 0; step < 24; step++)
            {
                Vector3 point = center + Quaternion.Euler(0, step * 15, 0) * Vector3.forward * distance, local = point - terrain.transform.position;
                if (local.x < 0 || local.z < 0 || local.x > size.x || local.z > size.z || terrain.terrainData.GetSteepness(local.x / size.x, local.z / size.z) > 30) continue;
                float ground = terrain.SampleHeight(point) + terrain.transform.position.y;
                if (water && water.surface && water.terrain && water.SampleSupportedDepth(point, ground) > water.maximumWadingDepth) continue;
                point.y = ground - offset.y + height * .5f + .08f; Vector3 at = point + offset;
                if (Physics.OverlapCapsule(at + Vector3.up * half, at - Vector3.up * half, radius * .95f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore).Any(collider => !(collider is TerrainCollider) && !collider.transform.IsChildOf(player))) continue;
                player.position = point; Physics.SyncTransforms(); return;
            }
            throw new InvalidOperationException("No supported capsule-clear viewpoint near " + center);
        }
        static void ImportPhotos(GameplaySession game, Report report)
        {
            string path = "LifeGameplayReview/20261026/photo-replacement-staging/fresh-photos.json";
            if (!File.Exists(path)) { report.gaps.Add("U08/U11: no fresh-photo source report"); return; }
            var fresh = JsonUtility.FromJson<FreshReport>(File.ReadAllText(path));
            if (fresh?.status != "PASS") { report.gaps.Add("U08/U11: source report is not PASS"); return; }
            string isolated = Path.GetFullPath(GameplaySession.PersistenceDirectory) + Path.DirectorySeparatorChar;
            foreach (var shot in fresh.shots ?? new List<FreshShot>())
            {
                var photo = shot.record; if (photo == null || !Guid.TryParseExact(photo.id, "N", out _) || photo.clipFolder != "MotionPhotos/" + photo.id) continue;
                string source = Path.GetFullPath(Path.Combine(fresh.saveDirectory, photo.clipFolder)), target = Path.GetFullPath(Path.Combine(GameplaySession.PersistenceDirectory, photo.clipFolder));
                Require(source.StartsWith(Path.GetFullPath("LifeGameplayReview") + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) && target.StartsWith(isolated, StringComparison.OrdinalIgnoreCase), "Photo import must remain inside Review and current isolated save");
                if (!Directory.Exists(target)) { Directory.CreateDirectory(target); foreach (string file in Directory.GetFiles(source)) File.Copy(file, Path.Combine(target, Path.GetFileName(file)), false); }
                if (!game.Notebook.observations.Any(record => record.id == photo.id)) game.Notebook.AddObservation(photo);
            }
            game.Notebook.ApplyPhotoTasks(); report.notes.Add("Album source provenance: real photographs reused from " + Path.GetFullPath(path) + "; UI is captured now, the four photographs are not retaken in this runner.");
        }
        static void ReuseImages(Report report)
        {
            string[] sources = { "U03|known-unknown|play/ui-animal-bubble-known-unknown.png", "U03|confirmed|play/ui-animal-bubble-confirmed.png", "U07|opening-fan|play/ui-book-opening-cover-fan.png", "U07|opening-leaf-fan|play/ui-book-opening-fan-1.png", "U07|reading|play/ui-book-oblique-reading.png", "U09|paper-colors|play/ui-word-state-colors.png", "U09|known-hover|play/ui-word-known-hover.png", "U09|unknown-hover|play/ui-word-unknown-hover.png", "U09|selected|play/ui-word-selected.png", "U09|glyph-page-two|photo-flow/photography-glyphs-2.png", "U14|unlearned|photo-flow/photography-unlearned.png", "U14|known-page-two|photo-flow/photography-sleep-known.png", "U15|public-record|photo-flow/photography-public-record.png" };
            foreach (string value in sources)
            {
                string[] parts = value.Split('|'); string source = Path.GetFullPath("LifeGameplayReview/20261026/" + parts[2]);
                if (!File.Exists(source)) { report.gaps.Add(parts[0] + " previous actual screenshot missing: " + source); continue; }
                byte[] bytes = File.ReadAllBytes(source); string file = Path.Combine(report.folder, parts[0] + "_" + parts[1] + "_reused.png"); File.WriteAllBytes(file, bytes);
                string condition = parts[0] == "U15" ? "Persisted I01 public-record fixture; no actual social-event completion is claimed." : parts[2].StartsWith("photo-flow/", StringComparison.Ordinal) ? "Actual Unity UI with arranged notebook state and synthetic PNG record; not a real shutter photograph." : "Knowledge, pointer events or book camera state were test fixtures.";
                report.shots.Add(new Shot { id = parts[0], state = parts[1], file = file, source = source, sourceWrittenUtc = File.GetLastWriteTimeUtc(source).ToString("o"), width = PngInt(bytes, 16), height = PngInt(bytes, 20), fixture = true, reused = true, entry = "Earlier actual Unity UI/photography review", condition = "Copied PNG unchanged. " + condition + " Source write time is known; exact old camera frame time is unavailable.", sha256 = Hash(bytes) });
            }
            Write(report);
        }
        static void Capture(Report report, Job job)
        {
            Canvas.ForceUpdateCanvases();
            if (job.id == "U20")
            {
                var life = UnityEngine.Object.FindObjectOfType<LifeSession>();
                var input = life ? life.GetComponent<LifeGestureInput>() : null;
                var weather = UnityEngine.Object.FindObjectOfType<LifeLocalWeather>();
                var range = UnityEngine.Object.FindObjectOfType<LifeCastRange>();
                Require(life && input && !input.IsWorldDrawing && !life.GestureInputActive &&
                    !Get<RectTransform>(input, "drawRoot").gameObject.activeInHierarchy && weather && weather.IsRain &&
                    range && range.GetComponent<MeshRenderer>().enabled,
                    "U20 must show actual rain and the production world boundary after closing G");
            }
            Texture2D frame = ScreenCapture.CaptureScreenshotAsTexture();
            try
            {
                Require(frame && frame.width > 0 && frame.height > 0, "GameView image is empty"); byte[] bytes = frame.EncodeToPNG(); string path = Path.Combine(report.folder, job.id + "_" + job.state + ".png"); File.WriteAllBytes(path, bytes); DateTime now = DateTime.UtcNow;
                report.shots.Add(new Shot { id = job.id, state = job.state, file = path, capturedUtc = now.ToString("o"), capturedHongKong = now.AddHours(8).ToString("yyyy-MM-dd HH:mm:ss.fff") + " +08:00", width = frame.width, height = frame.height, unityFrame = Time.frameCount, fixture = job.id != "U01", entry = job.entry, condition = job.condition, sha256 = Hash(bytes) }); Write(report);
            }
            finally { if (frame) UnityEngine.Object.Destroy(frame); }
        }
        static void Write(Report report)
        {
            report.coveredUiIds = report.shots.Select(shot => shot.id).Distinct().OrderBy(id => id).ToArray();
            report.missingUiIds = Enumerable.Range(1, 22).Select(number => "U" + number.ToString("00")).Where(id => id != "U05").Except(report.coveredUiIds).ToArray();
            File.WriteAllText(Path.Combine(report.folder, "manifest.json"), JsonUtility.ToJson(report, true));
        }
        static void Gallery(Report report)
        {
            string E(string value) => System.Net.WebUtility.HtmlEncode(value ?? "");
            string html = "<!doctype html><html lang=zh-CN><meta charset=utf-8><title>Unity UI actual captures</title><style>body{background:#f3f0e5;color:#243d34;font:16px/1.7 Microsoft YaHei;margin:35px auto;max-width:1200px}img{width:100%;height:auto}figure{margin:30px 0 50px}small{color:#64756a}code{word-break:break-all}</style><h1>Unity 实际 UI 截图</h1><p>" + E(report.boundary) + "</p><p>" + E(report.startedUtc) + " / " + E(report.scene) + "</p>";
            foreach (var shot in report.shots) html += "<figure><h2>" + E(shot.id + " · " + shot.state) + "</h2><img src=\"" + E(Path.GetFileName(shot.file)) + "\"><figcaption><small>" + E(shot.reused ? "复用旧图 · 源文件UTC " + shot.sourceWrittenUtc : "本轮实际运行 · 香港时间 " + shot.capturedHongKong) + "</small><p>" + E(shot.entry) + "</p><p>" + E(shot.condition) + "</p></figcaption></figure>";
            html += "<h2>来源说明</h2><ul>" + string.Join("", report.notes.Select(note => "<li>" + E(note) + "</li>")) + "</ul><h2>未覆盖</h2><ul>" + string.Join("", report.gaps.Select(gap => "<li>" + E(gap) + "</li>")) + "</ul></html>"; File.WriteAllText(Path.Combine(report.folder, "index.html"), html);
        }
        static int PngInt(byte[] bytes, int at) => bytes.Length < at + 4 ? 0 : bytes[at] << 24 | bytes[at + 1] << 16 | bytes[at + 2] << 8 | bytes[at + 3];
        static string Hash(byte[] bytes) { using (var hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-", ""); }
        static T Get<T>(object owner, string field) => (T)(owner.GetType().GetField(field, Private) ?? throw new MissingFieldException(owner.GetType().Name, field)).GetValue(owner);
        static void Set(object owner, string field, object value) => (owner.GetType().GetField(field, Private) ?? throw new MissingFieldException(owner.GetType().Name, field)).SetValue(owner, value);
        static void Property(object owner, string name, object value) => owner.GetType().GetProperty(name).GetSetMethod(true).Invoke(owner, new[] { value });
        static object Call(object owner, string name, params object[] args) => (owner.GetType().GetMethod(name, Private) ?? throw new MissingMethodException(owner.GetType().Name, name)).Invoke(owner, args);
        static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    }
}
