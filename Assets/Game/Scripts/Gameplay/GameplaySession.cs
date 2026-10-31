using System;
using System.Collections;
using System.IO;
using System.Linq;
using UnityEngine;
using ForestSpriteCharacter;
using WeatherHouse.Vegetation;
using WeatherHouse.WatersideV2;
using WeatherHouse.Life;
using WeatherHouse.WildlifeJourney;

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
        private GameplayHud hud;
        private float nextObservation;
        private string lastUtterance;
        private VegetationTopDownCamera orbit;
        private LifeSession Life => GetComponent<LifeSession>();
        private int conversationRevision;
        private AnimalActor listenedAnimal;
        private float listeningUntil;
        public void BeginDialogue(AnimalActor animal)
        {
            conversationRevision++; lastUtterance = null;
            listenedAnimal = animal; listeningUntil = Time.unscaledTime + Mathf.Max(.2f, automaticGreetingDuration);
        }

        private void Awake()
        {
            Recorder = GetComponent<MotionPhotoRecorder>();
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
            RefreshPhotoSpeechTarget();
            if (Observed != null && hud && hud.IsAnimalSpeechVisible(Observed) && lastUtterance != DisplayedUtteranceId(Observed))
            {
                lastUtterance = DisplayedUtteranceId(Observed);
                Notebook.Observe(Observed.animalId, lastUtterance, DisplayedGlyphIds(Observed));
                ObserveDisplayedConversation(Observed);
                Save();
                Changed?.Invoke();
            }
        }

        public bool ShouldShowAnimalUtterance(AnimalActor animal) => animal && animal.IsAvailableForObservation &&
            (IsPhotoSpeechHeld(animal) ? photoSpeech.glyphs.Length > 0 :
             !animal.UsesAutomaticGreeting || animal.HasAmbientSpeech || animal.HasAutomaticGreetingAt(Time.unscaledTime) ||
             (hud && hud.isActiveAndEnabled && listenedAnimal == animal && Time.unscaledTime < listeningUntil));

        sealed class PhotoSpeech
        {
            public AnimalActor animal;
            public string utterance;
            public string[] glyphs;
            public LifeContextSnapshot context;
            public LifeRequest request;
            public JourneySocial social;
            public bool usesSocial;
        }
        PhotoSpeech photoSpeech;
        AnimalActor photoSpeechTarget;
        bool photoSpeechTracking;

        public bool IsPhotoSpeechHeld(AnimalActor animal) => animal && animal.IsAvailableForObservation && photoSpeech != null && photoSpeech.animal == animal;
        public string DisplayedUtteranceId(AnimalActor animal) => IsPhotoSpeechHeld(animal) ? photoSpeech.utterance : animal ? animal.UtteranceId : "";
        public string[] DisplayedGlyphIds(AnimalActor animal) => IsPhotoSpeechHeld(animal) ? photoSpeech.glyphs : animal ? animal.CurrentGlyphIds : Array.Empty<string>();
        public bool UsesSocialPresentation(AnimalActor animal) => IsPhotoSpeechHeld(animal) ? photoSpeech.usesSocial : animal && animal.HasAmbientPresentation;

        public void BeginPhotoSpeech()
        {
            photoSpeechTracking = true;
            HoldPhotoSpeech(Observed);
        }
        void RefreshPhotoSpeechTarget()
        {
            if (photoSpeechTracking && photoSpeechTarget != Observed) HoldPhotoSpeech(Observed);
        }
        void HoldPhotoSpeech(AnimalActor animal)
        {
            ReleasePhotoSpeech(photoSpeech);
            photoSpeechTarget = animal;
            if (!animal || !animal.IsAvailableForObservation) return;
            var ecology = animal.GetComponent<WildlifeInteractionTarget>();
            var socialTurn = ecology ? ecology.CurrentSocialTurn : null;
            bool shown = socialTurn != null || ShouldShowAnimalUtterance(animal);
            var request = shown && Life && socialTurn == null ? Life.CurrentRequest(animal) : null;
            photoSpeech = new PhotoSpeech { animal = animal,
                utterance = shown ? (socialTurn != null ? ecology.SocialUtteranceId(socialTurn) : animal.UtteranceId) : "",
                glyphs = shown ? (string[])((socialTurn != null ? socialTurn.glyphIds : animal.CurrentGlyphIds) ?? Array.Empty<string>()).Clone() : Array.Empty<string>(),
                context = shown && Life && socialTurn == null ? Life.CaptureContext(animal)?.Copy() : null,
                request = request == null ? null : JsonUtility.FromJson<LifeRequest>(JsonUtility.ToJson(request)),
                social = ecology ? ecology.Social : null, usesSocial = socialTurn != null };
            if (photoSpeech.social) photoSpeech.social.SetPhotoTarget(ecology.SocialMemberId, socialTurn != null);
        }
        void ReleasePhotoSpeech(PhotoSpeech captured)
        {
            // An older recording may finish after a different animal becomes the photo target.
            if (captured == null || !ReferenceEquals(photoSpeech, captured)) return;
            if (captured.social) captured.social.ClearPhotoTarget();
            photoSpeech = null;
        }
        public void EndPhotoSpeech()
        {
            photoSpeechTracking = false;
            ReleasePhotoSpeech(photoSpeech);
            photoSpeechTarget = null;
        }
        void ObserveDisplayedConversation(AnimalActor animal)
        {
            if (Life && Life.World) Life.World.ObservePhotoConversation(IsPhotoSpeechHeld(animal) ? photoSpeech.request : Life.CurrentRequest(animal));
        }

        public void RefreshInputBlocking()
        {
            bool movement = isActiveAndEnabled && hud && hud.isActiveAndEnabled && hud.BlocksMovement;
            movement |= Life && Life.GestureInputActive;
            movement |= LifeOnboarding.Blocks(this);
            bool photo = isActiveAndEnabled && hud && hud.isActiveAndEnabled && hud.CameraMode;
            if (walker) { walker.InputBlocked = movement; walker.JumpBlocked = movement; walker.CameraInputBlocked = photo; }
            if (forestSprite)
            {
                forestSprite.InputBlocked = movement; forestSprite.JumpBlocked = movement;
                forestSprite.CameraInputBlocked = movement || photo;
            }
            if (orbit) orbit.InputBlocked = movement || photo;
        }

        public void SaveGuess(string id, string guess)
        {
            if (LifeOnboarding.TrySubmitGuess(this, id, guess)) return;
            Notebook.SetGuess(id, guess);
            Notify(Save() ? "语义判断暂不可用。猜测已保留，可以稍后重试。" : "猜测保留在本次游戏中，手账暂时无法写入磁盘。");
        }

        public void TakePhoto()
        {
            if (Recorder.IsRecording) return;
            if (!PhotoRig.Active) { Notify("按 C 举起相机，再取景拍照。"); return; }
            if (photoSpeechTracking && photoSpeech == null && Observed) HoldPhotoSpeech(Observed);
            var animal = Observed;
            bool speechVisible = animal && hud && hud.IsAnimalSpeechVisible(animal);
            PhotoSpeech capturedSpeech = IsPhotoSpeechHeld(animal) ? photoSpeech : null;
            string capturedUtterance = DisplayedUtteranceId(animal);
            int capturedRevision = conversationRevision;
            if (speechVisible) ObserveDisplayedConversation(animal);
            string[] capturedAnimalGlyphs = speechVisible ? (string[])DisplayedGlyphIds(animal).Clone() : Array.Empty<string>();
            LifeGlyphInscription.Reading[] inscriptions = LifeGlyphInscription.Capture(PhotoRig.CaptureCamera, Catalog);
            Recorder.Capture(PhotoRig.CaptureCamera, new ObservationRecord
            {
                animalId = animal ? animal.animalId : "", utteranceId = speechVisible ? capturedUtterance : "",
                glyphIds = LifeGlyphInscription.PhotoGlyphs(capturedAnimalGlyphs, inscriptions),
                weather = Life ? Life.WeatherText : !animal || animal.region == null ? "" : animal.region.Current.ToString(),
                context = speechVisible ? (capturedSpeech != null ? capturedSpeech.context?.Copy() : Life ? Life.CaptureContext(animal) : null) : null,
                photoSnapshot = LifePhotoCapture.Capture(Life, PhotoRig.CaptureCamera)
            }, record =>
            {
                ReleasePhotoSpeech(capturedSpeech);
                Notebook.AddObservation(record, capturedAnimalGlyphs);
                LifeGlyphInscription.Observe(Notebook, inscriptions);
                int completed = Notebook.ApplyPhotoTasks();
                bool saved = Save();
                if (saved) LifeAudio.Play(LifeAudioCue.Ui);
                if (completed > 0) LifeAudio.Play(LifeAudioCue.Progress);
                bool recognitionFailed = !string.IsNullOrEmpty(record.photoSnapshot?.visibilityError);
                string message = recognitionFailed ? "照片已保留，画面识别暂时失败，本次没有新增摄影记录。" : "";
                if (!saved) message = "照片画面已保存，手账暂时无法写入磁盘，本次游戏仍可查看。" +
                    (recognitionFailed ? " 画面识别暂时失败，本次没有新增摄影记录。" : "");
                if (!string.IsNullOrEmpty(record.captureWarning)) message += " 照片附加信息未完整保存，画面已保留。";
                Notify(message);
            }, error =>
            {
                ReleasePhotoSpeech(capturedSpeech);
                Debug.LogWarning("Photo capture failed: " + error, this);
                Notify("这次照片没能保存，请再试一次。");
            },
                speechVisible ? (Func<bool>)(() => animal && animal.IsAvailableForObservation && Observed == animal &&
                    DisplayedUtteranceId(animal) == capturedUtterance && conversationRevision == capturedRevision) : null);
        }

        public void RecordPublicContext(LifePublicContext context)
        {
            if (!Notebook.AddPublicContext(context)) return;
            int completed = Notebook.ApplyPhotoTasks();
            bool saved = Save();
            if (completed > 0) LifeAudio.Play(LifeAudioCue.Progress);
            string message = "这次相遇记在手账里了。";
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
                Notify("四处生活的小请求都得到了帮助。你可以继续探索森林，把新发现留进手账。");
                return;
            }
            Notify(weather == LocalWeather.Rain ? "雨落在这片地方。再看看动物的动作。" :
                weather == LocalWeather.Sun ? "雨云散去。湿润的东西会慢慢晒干。" : "风吹过这片地方，叶子开始摇动。");
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
            EndPhotoSpeech();
            listenedAnimal = null; listeningUntil = 0;
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
