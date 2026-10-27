using System;
using System.Collections;
using System.IO;
using System.Linq;
using UnityEngine;
using ForestSpriteCharacter;
using WeatherHouse.Vegetation;
using WeatherHouse.WatersideV2;
using WeatherHouse.Life;

namespace WeatherHousePrototype
{
    [DefaultExecutionOrder(-20)]
    [RequireComponent(typeof(MotionPhotoRecorder), typeof(LanguageJudgementClient), typeof(GameplayHud))]
    public sealed class GameplaySession : MonoBehaviour
    {
        public PrototypeWalker walker;
        public ForestSpriteDemoController forestSprite;
        public AnimalActor[] animals;
        public WeatherRegion[] regions;
        [Min(.2f)] public float automaticGreetingDuration = 3.2f;
        [Min(0f)] public float automaticGreetingCooldown = 30f;
        public LanguageNotebook Notebook { get; private set; }
        public GlyphCatalog Catalog { get; private set; }
        public AnimalActor Observed { get; private set; }
        public MotionPhotoRecorder Recorder { get; private set; }
        public PhotoCameraRig PhotoRig { get; private set; }
        public Transform PlayerTransform => forestSprite ? forestSprite.transform : walker ? walker.transform : null;
        public Camera PlayerViewCamera => forestSprite ? forestSprite.demoCamera : walker ? walker.viewCamera : null;
        public Transform PlayerVisualRoot => forestSprite ? forestSprite.visual : walker ? walker.visualRoot : null;
        public Camera CurrentView => PhotoRig != null && PhotoRig.Active ? PhotoRig.CaptureCamera : PlayerViewCamera;
        public bool Judging { get; private set; }
        public string Status { get; private set; } = "走近动物，观察它的动作与字语。C 拍照留证，J 打开手账。";
        public event Action Changed;
        public static string PersistenceDirectory
        {
            get
            {
#if UNITY_EDITOR
                string review = Environment.GetEnvironmentVariable("WEATHERHOUSE_LIFE_REVIEW_SAVE");
                if (!string.IsNullOrWhiteSpace(review)) return review;
#endif
                return Application.persistentDataPath;
            }
        }
        public string SavePath => Path.Combine(PersistenceDirectory, "language-notebook.json");
#if UNITY_EDITOR
        public string LastSaveError { get; private set; }
#endif
        private LanguageJudgementClient judge;
        private GameplayHud hud;
        private float nextObservation;
        private string lastUtterance;
        private VegetationTopDownCamera orbit;
        private LifeSession Life => GetComponent<LifeSession>();
        private int conversationRevision;
        public void BeginDialogue(AnimalActor animal) { conversationRevision++; lastUtterance = null; }

        private void Awake()
        {
            Recorder = GetComponent<MotionPhotoRecorder>();
            judge = GetComponent<LanguageJudgementClient>();
            hud = GetComponent<GameplayHud>();
            Catalog = JsonUtility.FromJson<GlyphCatalog>(Resources.Load<TextAsset>("WeatherHouseGlyphs").text);
            Notebook = new LanguageNotebook();
            try
            {
                if (File.Exists(SavePath)) Notebook = JsonUtility.FromJson<LanguageNotebook>(File.ReadAllText(SavePath)) ?? new LanguageNotebook();
            }
            catch (Exception error) when (error is IOException || error is ArgumentException || error is UnauthorizedAccessException)
            {
                Status = "旧手账暂时无法读取，原文件已保留。";
                // Keep an unreadable save intact while the player uses a new session.
                saveBlocked = true;
            }
            Notebook.Normalize(Catalog);
            Notebook.ApplyPhotoTasks();
            if (!walker && !forestSprite) forestSprite = FindObjectOfType<ForestSpriteDemoController>();
            if (!walker && !forestSprite) walker = FindObjectOfType<PrototypeWalker>();
            if (!PlayerTransform || !PlayerViewCamera) throw new InvalidOperationException("GameplaySession needs the scene's player and view camera.");
            if (forestSprite)
            {
                forestSprite.GameplayInputManaged = true;
                orbit = PlayerViewCamera.GetComponent<VegetationTopDownCamera>();
            }
            PhotoRig = GetComponent<PhotoCameraRig>();
            if (PhotoRig == null) PhotoRig = gameObject.AddComponent<PhotoCameraRig>();
            if (forestSprite) PhotoRig.Initialize(forestSprite); else PhotoRig.Initialize(walker);
            if (walker) walker.ShowLegacyHint = false;
        }

        private bool saveBlocked;
        private void OnEnable()
        {
            if (walker != null) walker.ShowLegacyHint = false;
            if (forestSprite) forestSprite.GameplayInputManaged = true;
        }
        private void Update()
        {
            RefreshInputBlocking();
            if (LifeOnboarding.Blocks(this)) return;
            if (Time.unscaledTime < nextObservation) return;
            nextObservation = Time.unscaledTime + .2f;
            AnimalActor next = null;
            float best = 10f;
            Camera view = CurrentView;
            AnimalActor dialogueTarget = hud && hud.isActiveAndEnabled ? hud.DialogueTarget : null;
            if (hud && hud.isActiveAndEnabled && hud.IsDialogueOpen)
            {
                if (dialogueTarget && dialogueTarget.IsAvailableForObservation &&
                    (Life ? Life.CanTalk(dialogueTarget) : Vector3.Distance(PlayerTransform.position, dialogueTarget.transform.position) < 12f)) next = dialogueTarget;
                else hud.CloseDialogue();
            }
            if (next == null && view && view.enabled)
                foreach (var animal in animals ?? Array.Empty<AnimalActor>())
                {
                    if (animal == null || !animal.IsAvailableForObservation) continue;
                    float distance = Vector3.Distance(PlayerTransform.position, animal.transform.position);
                    Vector3 sightPoint = PhotoRig.Active && PhotoCameraRig.TryGetAnimalBounds(animal, out Bounds bounds)
                        ? bounds.center : animal.BubbleAnchor;
                    Vector3 screen = view.WorldToViewportPoint(sightPoint);
                    if (distance >= best || screen.z <= 0f || screen.x < .03f || screen.x > .97f || screen.y < .04f || screen.y > .95f) continue;
                    Vector3 origin = view.transform.position;
                    if (Physics.Linecast(origin, sightPoint, out RaycastHit hit, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore) &&
                        !hit.transform.IsChildOf(animal.transform) && !hit.transform.IsChildOf(PlayerTransform)) continue;
                    next = animal;
                    best = distance;
                }
            if (Observed != next)
            {
                if (Observed != null) Observed.SetObserved(false);
                Observed = next;
                if (Observed != null) Observed.SetObserved(true);
                lastUtterance = null;
                Changed?.Invoke();
            }
            if (Observed && hud && hud.isActiveAndEnabled && !hud.JournalOpen && !hud.IsDialogueOpen)
                Observed.TryBeginAutomaticGreeting(Time.unscaledTime, automaticGreetingDuration, automaticGreetingCooldown);
            if (Observed != null && hud && hud.IsAnimalSpeechVisible(Observed) && lastUtterance != Observed.UtteranceId)
            {
                lastUtterance = Observed.UtteranceId;
                Notebook.Observe(Observed.animalId, Observed.UtteranceId, Observed.CurrentGlyphIds);
                if (Life && Life.World) Life.World.ObservePhotoConversation(Life.CurrentRequest(Observed));
                Save();
                Changed?.Invoke();
            }
        }

        public bool ShouldShowAnimalUtterance(AnimalActor animal) => animal && animal.IsAvailableForObservation &&
            (!animal.UsesAutomaticGreeting || animal.HasAmbientSpeech || animal.HasAutomaticGreetingAt(Time.unscaledTime) ||
             (hud && hud.isActiveAndEnabled && hud.DialogueTarget == animal));

        public void RefreshInputBlocking()
        {
            bool movement = isActiveAndEnabled && hud && hud.isActiveAndEnabled && hud.BlocksMovement;
            movement |= Life && Life.GestureInputActive;
            movement |= LifeOnboarding.Blocks(this);
            bool photo = isActiveAndEnabled && hud && hud.isActiveAndEnabled && hud.CameraMode;
            if (walker) { walker.InputBlocked = movement; walker.JumpBlocked = photo; walker.CameraInputBlocked = photo; }
            if (forestSprite)
            {
                forestSprite.InputBlocked = movement; forestSprite.JumpBlocked = photo;
                forestSprite.CameraInputBlocked = movement || photo;
            }
            if (orbit) orbit.InputBlocked = movement || photo;
        }

        public void SaveGuess(string id, string guess)
        {
            if (LifeOnboarding.TrySubmitGuess(this, id, guess)) return;
            Notebook.SetGuess(id, guess);
            bool saved = Save();
            if (saved) LifeAudio.Play(LifeAudioCue.Ui);
            Notify(saved ? "已记下你的猜测。可以继续寻找别处的线索。" : "猜测保留在本次游戏中，手账暂时无法写入磁盘。");
        }

        public void TakePhoto()
        {
            if (Recorder.IsRecording) return;
            if (!PhotoRig.Active) { Notify("按 C 举起相机，再取景拍照。"); return; }
            var animal = Observed;
            bool speechVisible = animal && hud && hud.IsAnimalSpeechVisible(animal);
            string capturedUtterance = animal ? animal.UtteranceId : "";
            int capturedRevision = conversationRevision;
            Recorder.Capture(PhotoRig.CaptureCamera, new ObservationRecord
            {
                animalId = animal ? animal.animalId : "", utteranceId = speechVisible ? capturedUtterance : "",
                glyphIds = speechVisible ? (string[])animal.CurrentGlyphIds.Clone() : Array.Empty<string>(),
                weather = Life ? Life.WeatherText : !animal || animal.region == null ? "" : animal.region.Current.ToString(),
                context = Life && speechVisible ? Life.CaptureContext(animal) : null,
                photoSnapshot = LifePhotoCapture.Capture(Life, PhotoRig.CaptureCamera)
            }, record =>
            {
                Notebook.AddObservation(record);
                int completed = Notebook.ApplyPhotoTasks();
                bool saved = Save();
                if (saved) LifeAudio.Play(LifeAudioCue.Ui);
                if (completed > 0) LifeAudio.Play(LifeAudioCue.Progress);
                bool recognitionFailed = !string.IsNullOrEmpty(record.photoSnapshot?.visibilityError);
                string message = recognitionFailed ? "照片已保留，画面识别暂时失败，本次没有新增摄影字义。" : "照片已放进手账。";
                if (!saved) message = "照片画面已保存，手账暂时无法写入磁盘，本次游戏仍可查看。" +
                    (recognitionFailed ? " 画面识别暂时失败，本次没有新增摄影字义。" : "");
                if (!string.IsNullOrEmpty(record.captureWarning)) message += " 动态片段未完整保存，已保留拍下的照片。";
                if (completed > 0) message += " 有新的字义记下来了。";
                Notify(message);
            }, error =>
            {
                Debug.LogWarning("Photo capture failed: " + error, this);
                Notify("这次照片没能保存，请再试一次。");
            },
                speechVisible ? (Func<bool>)(() => animal && animal.IsAvailableForObservation &&
                    animal.UtteranceId == capturedUtterance && conversationRevision == capturedRevision) : null);
        }

        public void RecordPublicContext(LifePublicContext context)
        {
            if (!Notebook.AddPublicContext(context)) return;
            int completed = Notebook.ApplyPhotoTasks();
            bool saved = Save();
            if (completed > 0) LifeAudio.Play(LifeAudioCue.Progress);
            string message = "这次相遇记在手账里了。";
            if (completed > 0) message += " 有新的字义记下来了。";
            Notify(message + (saved ? "" : " 手账暂未写入磁盘，本次游戏仍可查看。"));
        }

        public void Cast(LocalWeather weather)
        {
            if (Life) { Notify("按 G 画出在手账中学会的天气手势。"); return; }
            WeatherRegion target = Observed != null ? Observed.region : null;
            if (target == null)
                target = (regions ?? Array.Empty<WeatherRegion>()).Where(r => r != null && r.Contains(PlayerTransform.position))
                    .OrderBy(r => (r.transform.position - PlayerTransform.position).sqrMagnitude).FirstOrDefault();
            if (target == null)
            {
                Notify(FindObjectOfType<WatersideMood>() ? "这张地图使用 F8 切换白天、晨雾、黄昏、阴天与夜晚氛围。" : "走近一处动物生活区域，再试着改变天气。");
                return;
            }
            target.SetWeather(weather);
            if (animals.Where(a => !a.background).All(a => a.RequestSatisfied))
            {
                Notify("四处生活的小请求都得到回应了。你可以继续探索森林，把新发现留进手账。");
                return;
            }
            Notify(weather == LocalWeather.Rain ? "雨落在这片地方。再看看动物的动作。" :
                weather == LocalWeather.Sun ? "雨云散去。湿润的东西会慢慢晒干。" : "风吹过这片地方，叶子开始摇动。");
        }

        public void Reply(string chinese)
        {
            if (Judging) return;
            if (Life && (!hud || !hud.IsDialogueOpen)) { Notify("先走近动物，按 E 听它说话。"); return; }
            var animal = hud && hud.DialogueTarget ? hud.DialogueTarget : Observed;
            if (!animal || !animal.IsAvailableForObservation ||
                (Life ? !Life.CanTalk(animal) : Vector3.Distance(PlayerTransform.position, animal.transform.position) >= 12f))
            { if (hud) hud.CloseDialogue(); Notify("它已经走远了，靠近后再继续交谈。"); return; }
            if (string.IsNullOrWhiteSpace(chinese)) { Notify("写下你想对它说的话。"); return; }
            var lifeRequest = Life ? Life.CurrentRequest(animal) : null;
            if (LifeOnboarding.TryReplyRain(this, animal, chinese)) return;
            if (animal.background && lifeRequest == null) { Notify("你的话已经说出。它正在分享眼前的生活，可以继续观察它的动作和字语。"); return; }
            if (!Life && animal.RequestSatisfied) { Notify("它的请求已经得到回应。可以继续观察它的生活，把新发现留进手账。"); return; }
            var ids = (string[])animal.CurrentGlyphIds.Clone();
            string context = animal.BehaviourDescription + (Life ? "。玩家手势天气：" : "。当前天气：") + (Life ? Life.WeatherText : animal.region ? animal.region.Current.ToString() : "自然天气") +
                (lifeRequest != null ? "。本轮语境：" + Life.World.Describe(lifeRequest) : "") + "。本句字义边界：" +
                string.Join("；", Catalog.glyphs.Where(g => Array.IndexOf(ids, g.id) >= 0).Select(g => g.id + "=" + g.meaning + " " + g.boundary));
            StartCoroutine(JudgeReply(new JudgementInput
            {
                animalId = animal.animalId, utteranceId = animal.UtteranceId,
                requestMeaning = lifeRequest != null ? lifeRequest.meaning : animal.requestMeaning, glyphIds = ids, playerReply = chinese, context = context
            }));
        }

        private IEnumerator JudgeReply(JudgementInput input)
        {
            int submittedRevision = conversationRevision;
            Judging = true;
            LifeAudio.Play(LifeAudioCue.Ui);
            Notify("动物在听你说……");
            yield return judge.Judge(input, result => ApplyJudgement(input, result, submittedRevision));
            Judging = false;
            Changed?.Invoke();
        }

        private void ApplyJudgement(JudgementInput input, JudgementResult result, int submittedRevision)
        {
            var originalAnimal = Array.Find(animals, a => a != null && a.animalId == input.animalId);
            if (submittedRevision != conversationRevision || originalAnimal == null || originalAnimal.UtteranceId != input.utteranceId ||
                (Life && (!Life.CanTalk(originalAnimal) || (hud && hud.DialogueTarget && hud.DialogueTarget != originalAnimal))))
            {
                Status = "动物眼前的情况已经变化。这次判断未确认词义，等它提出新请求再试试。";
                return;
            }
            int count = 0;
            bool newGesture = false;
            bool progressSaved = true;
            bool taskAccepted = Life && Life.AcceptUnderstanding(originalAnimal, input.utteranceId, result, out progressSaved);
            if (!Life && result.success && result.understood)
                foreach (string id in result.evidenceGlyphIds)
                    if (Notebook.ConfirmFromJudgement(id, input.animalId, input.glyphIds, true))
                    {
                        count++;
                        newGesture |= Life && (id == "sunlight" || id == "wind" || id == "weak" || id == "strong");
                    }
            bool saved = count == 0 || Save();
            if (taskAccepted || count > 0) LifeAudio.Play(LifeAudioCue.Progress);
            string feedback = Life ? (result.understood ? "你听懂了这次话语。" : "还需要说清它指的是谁、什么变化，以及肯定或拒绝。") : result.feedback;
            Status = result.success ? feedback + (count > 0 ? " 已确认 " + count + " 个字，可在手账中查看。" : "") : result.error;
            if (newGesture) Status += " 手账里有新手势可学。";
            if (taskAccepted) Status += " 已记下它的请求，看看右上角的任务。";
            if (!saved) Status += " 这些确认只保留在本次游戏中，尚未写入磁盘。";
            if (!progressSaved) Status += " 生活进度暂未写入磁盘，本次游戏中仍可继续。";
        }

        private bool Save()
        {
            if (saveBlocked) return false;
            try
            {
                Directory.CreateDirectory(PersistenceDirectory);
                LifeSaveFile.Write(SavePath, JsonUtility.ToJson(Notebook, true));
#if UNITY_EDITOR
                LastSaveError = string.Empty;
#endif
                return true;
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
            {
#if UNITY_EDITOR
                LastSaveError = error.GetType().Name + " 0x" + error.HResult.ToString("X8") + ": " + error.Message;
#endif
                Status = "手账保留在本次游戏中，但写入磁盘失败，请检查目录权限。"; return false;
            }
        }

        public bool SaveNotebook() => Save();
        private void OnApplicationQuit() { if (Recorder) Recorder.FinishLiveCapture(); }
        public void Notify(string message) { Status = message; Changed?.Invoke(); }
        private void OnDisable()
        {
            if (Recorder) Recorder.FinishLiveCapture();
            if (Judging) StopAllCoroutines();
            if (PhotoRig != null) PhotoRig.SetActive(false);
            if (walker != null) { walker.InputBlocked = false; walker.JumpBlocked = false; walker.CameraInputBlocked = false; walker.ShowLegacyHint = true; }
            if (forestSprite) { forestSprite.InputBlocked = false; forestSprite.JumpBlocked = false; forestSprite.CameraInputBlocked = false; forestSprite.GameplayInputManaged = false; }
            if (orbit) orbit.InputBlocked = false;
            Judging = false;
        }
    }
}
