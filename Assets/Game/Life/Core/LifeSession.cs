using System;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using WeatherHousePrototype;

namespace WeatherHouse.Life
{
    // WildlifePopulation (50) must finish its measured body bounds before life points are placed.
    [DefaultExecutionOrder(60)]
    [DisallowMultipleComponent]
    public sealed class LifeSession : MonoBehaviour
    {
        public const float CastRadius = 8f;
        public const float TalkRadius = 3.5f;
        public GameplaySession Gameplay { get; private set; }
        public LifeProgress Progress { get; private set; } = new LifeProgress();
        public LifeWorld World { get; private set; }
        public bool GestureInputActive { get; private set; }
#if UNITY_EDITOR
        public string LastSaveError { get; private set; }
#endif
        public event Action Changed;
        public string SavePath => Path.Combine(GameplaySession.PersistenceDirectory, "life-progress.json");
        public string InteractionHint => World ? World.InteractionHint : "";
        public string WeatherText => weather ? weather.Description : "自然天气";
        public bool LocalRainActive => weather && weather.IsRain;
        public int TaskPage { get; private set; }
        public int TaskPageCount => Mathf.Max(1, (Progress.tasks.Count(t => t.status == LifeTaskStatus.Active) + 1) / 2);
        public void ChangeTaskPage(int delta) { TaskPage = (TaskPage + delta + TaskPageCount) % TaskPageCount; Changed?.Invoke(); }
        public string TaskText
        {
            get
            {
                if (!World) return "";
                TaskPage = Mathf.Clamp(TaskPage, 0, TaskPageCount - 1);
                var tasks = Progress.tasks.Where(t => t.status == LifeTaskStatus.Active).Skip(TaskPage * 2).Take(2)
                    .Select(t => World.Requests.FirstOrDefault(r => r.id == t.requestId))
                    .Where(r => r != null).Select(r =>
                    {
                        var speaker = World.FindSpeaker(r.speakerKey);
                        string animal = speaker && speaker.Actor ? speaker.Actor.displayName : "动物";
                        string distance = speaker && Gameplay.PlayerTransform ?
                            " · " + Vector3.Distance(Gameplay.PlayerTransform.position, speaker.transform.position).ToString("F0") + "米" : "";
                        return animal + distance + "\n" + TaskReminder(r.condition);
                    });
                string text = string.Join("\n", tasks);
                return text;
            }
        }
        private static string TaskReminder(string condition)
        {
            switch (condition)
            {
                case "rain_seen": return "想看一场雨";
                case "water_retained": return "想要喝水";
                case "dry_mat": return "想睡得干爽";
                case "fed": return "想吃些草";
                case "arrive": return "想请你过去";
                case "animal_arrived": return "想去住处休息";
                case "wind_weak": return "想吹一点风";
                case "rain_weak": return "想淋一点小雨";
                case "warm_rest": return "想暖和地歇一歇";
                case "common_rest": return "想和朋友歇一歇";
                case "walk_sequence": case "walk_reverse": return "想请你走一小段";
                default: return "有件事想请你帮忙";
            }
        }

        private LifeLocalWeather weather;
        private LifeCastRange range;
        private bool saveBlocked;
        private float nextCheck;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Register()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
            Attach(SceneManager.GetActiveScene());
        }
        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => Attach(scene);
        private static void Attach(Scene scene)
        {
            if (scene.name != "WeatherHouse_WatersideV2") return;
            var session = FindObjectOfType<GameplaySession>();
            if (session && !session.GetComponent<LifeSession>()) session.gameObject.AddComponent<LifeSession>();
        }

        private void Start()
        {
            Gameplay = GetComponent<GameplaySession>();
            if (!Gameplay || !Gameplay.PlayerTransform) { enabled = false; return; }
            try
            {
                if (File.Exists(SavePath)) Progress = JsonUtility.FromJson<LifeProgress>(File.ReadAllText(SavePath)) ?? new LifeProgress();
            }
            catch (Exception e) when (e is IOException || e is ArgumentException || e is UnauthorizedAccessException)
            { saveBlocked = true; Gameplay.Notify("生活存档无法读取，原文件已保留；本次进度暂不覆盖它。"); }
            Progress.Normalize();
            foreach (string id in new[] { "want", "reject" })
            {
                Gameplay.Notebook.Observe("tutorial", "initial-vocabulary", new[] { id });
                Gameplay.Notebook.GetWord(id).confirmed = true;
            }
            World = gameObject.AddComponent<LifeWorld>();
            World.Initialize(this);
            World.RestorePublicPhotoContexts(Gameplay.Notebook.publicHistory);
            World.PublicPhotoContext += Gameplay.RecordPublicContext;
            foreach (string key in World.Requests.Select(r => r.speakerKey).Distinct())
                World.FindSpeaker(key)?.ConfigureProgress(Progress, World.Requests);
            if (!string.IsNullOrEmpty(Progress.worldJson)) World.RestoreJson(Progress.worldJson);
            Gameplay.animals = FindObjectsOfType<AnimalActor>();
            weather = new GameObject("Player local weather").AddComponent<LifeLocalWeather>();
            weather.Initialize(Gameplay.PlayerTransform, CastRadius);
            range = LifeCastRange.Create(weather.transform);
            gameObject.AddComponent<LifeGestureInput>();
            Gameplay.Changed += OnGameplayChanged;
            Gameplay.SaveNotebook();
            SaveAndNotify(string.IsNullOrEmpty(World.SetupWarning) ?
                "" :
                "生活点需要检查：" + World.SetupWarning);
        }

        private void OnGameplayChanged() => Changed?.Invoke();
        private void Update()
        {
            if (!World || !Gameplay || !weather || LifeOnboarding.Blocks(Gameplay)) return;
            weather.Apply(World, Time.deltaTime);
            if (range)
            {
                if (weather.Active || GestureInputActive) range.Show(Gameplay.PlayerTransform.position, CastRadius,
                    weather.Active ? .9f : .45f, weather.Active);
                else range.Hide();
            }
            var hud = Gameplay.GetComponent<GameplayHud>();
            if (Input.GetKeyDown(KeyCode.F) && !GestureInputActive && !Gameplay.Judging && !(hud && (hud.BlocksMovement || hud.CameraMode))) World.Interact();
            if (Time.unscaledTime < nextCheck) return;
            nextCheck = Time.unscaledTime + .2f;
            bool completed = false;
            foreach (var task in Progress.tasks.Where(t => t.status == LifeTaskStatus.Active).ToArray())
            {
                var request = World.Requests.FirstOrDefault(r => r.id == task.requestId);
                var speaker = request == null ? null : World.FindSpeaker(request.speakerKey);
                bool near = speaker && InsideCircle(Gameplay.PlayerTransform.position, speaker.transform.position, CastRadius);
                if (request != null && Progress.Complete(task.requestId, near, World.IsSatisfied(request))) completed = true;
            }
            if (completed) { SaveAndNotify(""); LifeAudio.Play(LifeAudioCue.Progress); }
        }

        public static bool InsideCircle(Vector3 center, Vector3 point, float radius)
        {
            Vector3 d = point - center;
            return d.x * d.x + d.z * d.z <= radius * radius && Mathf.Abs(d.y) <= 4f;
        }
        public bool CanTalk(AnimalActor animal) => animal && animal.IsAvailableForObservation && Gameplay &&
            Vector3.Distance(Gameplay.PlayerTransform.position, animal.transform.position) <= TalkRadius;
        public LifeRequest CurrentRequest(AnimalActor animal) => LifeOnboardingSpeech.Current(animal) ? LifeOnboardingSpeech.Current(animal).Request : animal ? animal.GetComponent<LifeSpeaker>()?.Current : null;
        public void NextTopic(AnimalActor animal)
        {
            if (Gameplay.Judging || !CanTalk(animal)) return;
            var speaker = animal.GetComponent<LifeSpeaker>();
            if (!speaker) return;
            speaker.SelectNext();
            Gameplay.Notebook.Observe(animal.animalId, animal.UtteranceId, animal.CurrentGlyphIds);
            Gameplay.SaveNotebook();
            Gameplay.Notify("");
        }
        public bool Listen(AnimalActor animal, string utteranceId)
        {
            var hud = Gameplay ? Gameplay.GetComponent<GameplayHud>() : null;
            if (!Gameplay || !World || !hud || !hud.isActiveAndEnabled || hud.PrimaryInteraction != LifePrimaryAction.Talk ||
                !CanTalk(animal) || Gameplay.Observed != animal || animal.UtteranceId != utteranceId) return false;
            var request = CurrentRequest(animal);
            if (request != null && !Progress.CanPresent(request, World.Requests)) return false;
            World.RecordAnimalEncounter(animal.animalId);
            var speech = LifeOnboardingSpeech.Current(animal);
            if (speech)
            {
                speech.Listen(utteranceId);
                request = CurrentRequest(animal);
            }
            var speaker = animal.GetComponent<LifeSpeaker>();
            if (speaker && request == speaker.Current)
            {
                speaker.AdvanceAfterHeard();
                request = CurrentRequest(animal);
            }
            string heardAnimalId = animal.animalId, heardUtteranceId = animal.UtteranceId;
            string[] heardGlyphs = (string[])animal.CurrentGlyphIds.Clone();
            bool accepted = request != null && !(speaker && request == speaker.Story) && Progress.Hear(request, animal.animalId, true, true);
            if (accepted) World.OnTaskAccepted(request);
            if (request != null) World.BeginGuidance(request);
            Gameplay.Notebook.Observe(heardAnimalId, heardUtteranceId, heardGlyphs);
            bool notebookSaved = Gameplay.SaveNotebook();
            bool progressSaved = SaveProgress();
            Gameplay.Notify(!notebookSaved || !progressSaved ? "这次记录暂未写入磁盘。" : "");
            GetComponent<LifeAudio>()?.Listen(animal);
            Changed?.Invoke();
            return true;
        }
        public bool IsGestureUnlocked(string id)
        {
            if (id == "rain" || id == "clear") return true;
            return new[] { "sunlight", "wind", "weak", "strong" }.Contains(id) &&
                (Progress.weatherMagicTestUnlocked || Gameplay?.Notebook?.HasPhotoEvidence(id) == true);
        }
        public bool IsGestureLearned(string id) => Progress.learnedGestures.Contains(id) && IsGestureUnlocked(id);
        public bool LearnGesture(string id)
        {
            if (!IsGestureUnlocked(id)) return false;
            bool newlyLearned = !Progress.learnedGestures.Contains(id);
            if (newlyLearned) Progress.learnedGestures.Add(id);
            if (id == "rain") Progress.rainTutorialSeen = true;
            SaveAndNotify("");
            if (newlyLearned) LifeAudio.Play(LifeAudioCue.Progress);
            return true;
        }
        public bool TryCast(string id, out string message)
        {
            if (!IsGestureUnlocked(id)) { message = ""; return false; }
            if (!weather) { message = "天气还在准备中。"; return false; }
            bool success = weather.Cast(id, out message);
            if (success) { World.RecordCast(id, weather.LastCastWeather); LifeOnboarding.SuccessfulCast(this, id); SaveAndNotify(message); }
            return success;
        }
        public void SetGestureInput(bool active) { GestureInputActive = active; Gameplay?.RefreshInputBlocking(); }
        public void SaveAndNotify(string message)
        {
            bool saved = SaveProgress();
            Gameplay?.Notify(message + (saved ? "" : "（生活进度暂未写入磁盘）"));
            Changed?.Invoke();
        }
        public bool AcquirePhotoTaskBook()
        {
            if (Progress.photoTaskBookCollected) return true;
            Progress.photoTaskBookCollected = true;
            if (!SaveProgress())
            { Progress.photoTaskBookCollected = false; Gameplay.Notify("任务书暂时未能保存，请再试一次。"); return false; }
            Gameplay.Notify(""); Changed?.Invoke(); LifeAudio.Play(LifeAudioCue.Progress); return true;
        }
        public bool SaveAnimalStories() => SaveProgress();
        private bool SaveProgress()
        {
            if (saveBlocked) return false;
            try
            {
                if (World) Progress.worldJson = World.CaptureJson();
                Directory.CreateDirectory(GameplaySession.PersistenceDirectory);
                LifeSaveFile.Write(SavePath, JsonUtility.ToJson(Progress, true));
#if UNITY_EDITOR
                LastSaveError = string.Empty;
#endif
                return true;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
#if UNITY_EDITOR
                LastSaveError = e.GetType().Name + " 0x" + e.HResult.ToString("X8") + ": " + e.Message;
#endif
                return false;
            }
        }
        public LifeContextSnapshot CaptureContext(AnimalActor animal)
        {
            var request = CurrentRequest(animal);
            var listener = request != null ? World.FindSpeaker(request.listenerKey) : null;
            var location = request == null ? null : World.Objects.FirstOrDefault(item => item && item.State.id == request.targetId);
            return new LifeContextSnapshot { speakerId = animal.animalId, speakerPosition = animal.transform.position,
                speakerName = animal.displayName, listenerName = listener && listener.Actor ? listener.Actor.displayName : "玩家",
                locationName = location ? location.label : "",
                listenerId = listener && listener.Actor ? listener.Actor.animalId : "player",
                listenerPosition = listener ? listener.transform.position : Gameplay.PlayerTransform.position,
                targetId = request?.targetId ?? "", anchorPosition = request == null ? animal.transform.position : World.AnchorPosition(request.targetId),
                sequenceId = request?.id ?? animal.UtteranceId, objectState = request == null ? animal.BehaviourDescription : World.VisibleObjectState(request), weatherState = "手势唤起的天气：" + WeatherText };
        }
        private void OnApplicationPause(bool paused) { if (paused && World) SaveProgress(); }
        private void OnApplicationQuit() { if (World) SaveProgress(); }
        private void OnDestroy()
        {
            if (World && Gameplay) World.PublicPhotoContext -= Gameplay.RecordPublicContext;
            if (Gameplay) Gameplay.Changed -= OnGameplayChanged;
            if (weather) Destroy(weather.gameObject);
        }
    }
}
