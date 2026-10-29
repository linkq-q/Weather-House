using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using WeatherHouse.Life;

namespace WeatherHousePrototype
{
    [DisallowMultipleComponent, DefaultExecutionOrder(200)]
    public sealed class GameplayHud : MonoBehaviour
    {
        [Header("控件样式 · 运行时修改立即预览")]
        [InspectorName("线条、字体与颜色")]
        public ProceduralUiStyle uiStyle = new ProceduralUiStyle();

        [Header("动物对话气泡")]
        [InspectorName("动物气泡尺寸")]
        [Tooltip("1600×1000 参考画布中的最大宽度和高度；单字接近方形，随字数加宽。过大时按屏幕可用空间等比缩小。")]
        public Vector2 animalBubbleSize = new Vector2(380f, 110f);
        [InspectorName("动物字语高度"), Range(16f, 120f)]
        [Tooltip("参考画布中的字形高度。气泡变小时，整句字语会继续等比缩小，避免挤出边缘。")]
        public float animalGlyphHeight = 43f;
        [InspectorName("字语笔画粗细"), Range(.5f, 14f)]
        [Tooltip("原始 100×120 字形坐标中的描边宽度；作用于气泡和照片详情。书页字符按设计稿使用独立的描边比例。")]
        public float animalGlyphStrokeWidth = 5f;

        [Header("相册符号页")]
        [InspectorName("使用 P1 示例照片与完整字形")]
        [Tooltip("用于设计稿对照；只影响符号页展示，不解锁字库或写入存档。相片页签始终显示实际拍摄记录。")]
        public bool useAlbumDesignSamples = true;

        public bool CameraMode { get; private set; }
        public AnimalActor DialogueTarget => null;
        public bool IsDialogueOpen => false;
        public bool JournalOpen { get; private set; }
        public bool IsAlbumOpening => (journal3D && journal3D.IsAnimating) || (albumOpening != null && albumOpening.IsPlaying);
        public bool BlocksMovement => JournalOpen || InputFocused();
        public LifePrimaryAction PrimaryInteraction => LifeInteractionRules.Primary(
            !session || BlocksMovement || CameraMode || IsAlbumOpening || session.Judging || session.Recorder.IsRecording || LifeOnboarding.Blocks(session) ||
            !string.IsNullOrEmpty(Input.compositionString) || Time.frameCount<=lastCompositionFrame+1 ||
            (life && life.GestureInputActive) || (gestureInput && gestureInput.IsWorldDrawing),
            session && session.Observed && (!life || life.CanTalk(session.Observed)),life && life.World && life.World.CanGather);
        public string GatheringHint => PrimaryInteraction==LifePrimaryAction.Gather?"E 采集":"";
        public void InteractPrimary()
        {
            switch(PrimaryInteraction)
            {
                case LifePrimaryAction.Talk:ListenToAnimal();break;
                case LifePrimaryAction.Gather:life.World.TryGather();break;
            }
        }

        private GameplaySession session;
        private LifeSession life;
        private LifeGestureInput gestureInput;
        private Canvas canvas;
        private RectTransform canvasRect, controlsRoot, bubbleRoot, modalRoot, cameraRoot, focusMark;
        private float focusMarkUntil;
        private const string PhotoGuide = "留下你想记住的风景与片刻。";
        private float photoControlScale = -1f;
        private int photoHintFontSize;
        private CanvasGroup cameraGroup;
        private float cameraBlend;
        private bool cameraRequested;
        private bool CanTakePhoto => CameraMode && cameraRequested && cameraBlend >= 1f;
        private Text statusLabel, captureLabel, playbackLabel;
        private RectTransform statusRoot;
        private BreathingTextGlow statusGlow;
        private string displayedStatus;
        private float statusUntil;
        private int lastCompositionFrame = -10;
        private InputField guessInput;
        private Font font, albumCountFont;
        private RectTransform albumRoot;
        private RectTransform detailRoot;
        private RectTransform detailLayer;
        private AlbumBookOpening albumOpening;
        private LifeJournal3D journal3D;
        private bool openingJournal;
        private AnimalActor bubbleAnimal;
        private CanvasGroup bubbleGroup;
        private bool bubbleWanted;
        private ProceduralUiGraphic bubbleShape;
        private readonly List<AnimalGlyphGraphic> bubbleGlyphs = new List<AnimalGlyphGraphic>();
        private readonly List<Text> bubbleMeanings = new List<Text>();
        private readonly RaycastHit[] bubbleSightHits = new RaycastHit[16];
        private string bubbleUtterance, selectedGlyph, guessGlyphId, guessDraft = "";
        private int page, currentFrame;
        private int glyphPage;
        private bool albumPhotosSelected, glyphDetailOpen, albumGesturesSelected, albumTasksSelected;
        private int photoTaskPage;
        private string publicTaskId;
        private string selectedGesture = "rain";
        private bool dirty, playingPhoto, inspectorDirty;
        private float nextFrameAt;
        private ObservationRecord expandedPhoto;
        private RawImage expandedImage;
        private Texture2D expandedTexture;
        private readonly LifePhotoCache photoCache = new LifePhotoCache();
        public bool OnboardingReady => canvas != null;
        public string OnboardingGestureSelected => selectedGesture;
        public void OnboardingHighlight(params string[] targetNames)
        {
            var focus = GetComponent<LifeOnboardingBookFocus>();
            if (!focus && (targetNames == null || targetNames.Length == 0)) return;
            if (!focus) focus = gameObject.AddComponent<LifeOnboardingBookFocus>();
            focus.Show(JournalOpen && !IsAlbumOpening ? albumRoot : null, JournalOpen && !IsAlbumOpening ? detailRoot : null,
                targetNames, journal3D, selectedGesture);
        }
        public bool OnboardingDetailOpen => glyphDetailOpen || expandedPhoto != null;
        public string OnboardingSelectedWord => glyphDetailOpen ? selectedGlyph : null;
        bool onboardingCanvasHidden, onboardingCanvasWasEnabled;
        bool ApplyOnboardingSuppression()
        {
            bool suppressed = LifeOnboarding.Blocks(session);
            if (suppressed) { if (!onboardingCanvasHidden) { onboardingCanvasWasEnabled = canvas.enabled; onboardingCanvasHidden = true; } canvas.enabled = false; }
            else if (onboardingCanvasHidden) { canvas.enabled = onboardingCanvasWasEnabled; onboardingCanvasHidden = false; }
            return suppressed;
        }
        public void OnboardingSelectBook(int tab, string focus)
        {
            if (!JournalOpen || IsAlbumOpening) return;
            albumPhotosSelected = tab == 1; albumGesturesSelected = tab == 2; albumTasksSelected = tab == 3;
            expandedPhoto = null; publicTaskId = null; glyphDetailOpen = false; selectedGlyph = focus;
            if (!string.IsNullOrEmpty(focus))
            { int index = Array.FindIndex(session.Catalog.glyphs, glyph => glyph.id == focus); if (index >= 0) glyphPage = index / AlbumBookView.GlyphsPerPage; }
            ClearFocus(); RebuildModal();
        }
        public void OnboardingSelectGesture(string id) { selectedGesture = id; OnboardingSelectBook(2, null); }
        public void OnboardingOpenWord(string id)
        {
            if (!JournalOpen || IsAlbumOpening) return;
            expandedPhoto = null; publicTaskId = null; selectedGlyph = guessGlyphId = id;
            guessDraft = session.Notebook.GetWord(id)?.guess ?? ""; glyphDetailOpen = true;
            if (albumRoot) albumRoot.gameObject.SetActive(true); ClearFocus(); ShowDetails();
        }
        public int PhotoDecodeCount => photoCache.DecodedFrames;
        public int CachedPhotoTextureCount => photoCache.TextureCount;
        private ProceduralUiStyle appliedStyle;
        private string lastStyleWarning;
        private static readonly Color Paper = new Color(.96f, .95f, .88f);
        private static Color Ink => ProceduralUiLibrary.Ink;
        private static readonly Color SoftOutline = new Color32(174, 184, 151, 255);

        private void Start()
        {
            ApplyInspectorStyle();
            session = GetComponent<GameplaySession>();
            if (session == null) { Debug.LogError("GameplayHud needs GameplaySession on the same object.", this); enabled = false; return; }
            life = GetComponent<LifeSession>();
            gestureInput = GetComponent<LifeGestureInput>();
            if (life) { useAlbumDesignSamples = false; life.Changed += OnChanged; }
            font = Resources.Load<Font>("Fonts/ZCOOLXiaoWei-Regular");
            if (font == null) throw new InvalidOperationException("Missing UI font: Resources/Fonts/ZCOOLXiaoWei-Regular.ttf");
            albumCountFont = Font.CreateDynamicFontFromOSFont(new[] { "Comic Sans MS", "Segoe Print", "Arial" }, 72);
            var root = new GameObject("Weather House HUD", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            root.transform.SetParent(transform, false);
            root.layer = 5;
            canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;
            canvasRect = (RectTransform)root.transform;
            var scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1600f, 1000f);
            scaler.matchWidthOrHeight = .5f;
            if (EventSystem.current == null)
            {
                var events = new GameObject("Weather House UI Events", typeof(EventSystem), typeof(StandaloneInputModule));
                events.transform.SetParent(transform, false);
            }
            BuildChrome();
            session.Changed += OnChanged;
            dirty = true;
            inspectorDirty = false;
        }

        private void BuildChrome()
        {
            var controls = controlsRoot = Rect(canvasRect, "Controls", Vector2.zero, new Vector2(336f, 104f));
            controls.anchorMin = controls.anchorMax = Vector2.zero;
            controls.anchoredPosition = new Vector2(176f, 64f);
            Color hint = new Color(.91f, .93f, .82f, .88f);
            HudIcon(controls, "Photo icon", "Camera", new Vector2(-132f, 10f), new Vector2(44f, 34f), new Rect(.12f, .20f, .74f, .60f));
            HudIcon(controls, "Magic book icon", "MagicBook", new Vector2(-44f, 10f), new Vector2(34f, 44f), new Rect(0, 0, 1, 1));
            HudIcon(controls, "Dictionary icon", "DictionaryBook", new Vector2(44f, 10f), new Vector2(34f, 44f), new Rect(0, 0, 1, 1));
            HudIcon(controls, "Photo tasks icon", "PhotoTasksBook", new Vector2(132f, 10f), new Vector2(34f, 44f), new Rect(0, 0, 1, 1));
            Label(controls, "Photo shortcut", "C", new Vector2(-132f, -30f), new Vector2(44f, 28f), 21, hint);
            Label(controls, "Magic book shortcut", "M", new Vector2(-44f, -30f), new Vector2(44f, 28f), 21, hint);
            Label(controls, "Dictionary shortcut", "J", new Vector2(44f, -30f), new Vector2(44f, 28f), 21, hint);
            Label(controls, "Photo tasks shortcut", "L", new Vector2(132f, -30f), new Vector2(44f, 28f), 21, hint);
            var spellButton = Rect(controls, "Spell shortcut button", new Vector2(220f, -3f), new Vector2(72f, 86f));
            var spellHit = spellButton.gameObject.AddComponent<Image>(); spellHit.color = Color.clear;
            var cast = spellButton.gameObject.AddComponent<Button>(); cast.targetGraphic = spellHit;
            cast.onClick.AddListener(() => { if (!gestureInput) gestureInput = GetComponent<LifeGestureInput>(); if (gestureInput) gestureInput.ToggleWorldCasting(); });
            HudIcon(spellButton, "Spell icon", "SpellCast", new Vector2(0f, 13f), new Vector2(44f, 44f), new Rect(0, 0, 1, 1));
            Label(spellButton, "Spell shortcut", "G", new Vector2(0f, -27f), new Vector2(44f, 28f), 21, hint);
            statusRoot = Rect(canvasRect, "Bottom status", Vector2.zero, new Vector2(880f, 72f));
            statusRoot.anchorMin = statusRoot.anchorMax = new Vector2(.5f, 0f);
            statusRoot.anchoredPosition = new Vector2(0f, 140f);
            statusLabel = Label(statusRoot, "Status", "", Vector2.zero, new Vector2(1120f, 72f), 33, Color.white);
            statusGlow = statusLabel.gameObject.AddComponent<BreathingTextGlow>();
            UpdateStatus();
        }

        private void UpdateStatus()
        {
            if (session.Status != displayedStatus)
            {
                displayedStatus = session.Status;
                statusUntil = Time.unscaledTime + 10f;
            }
            bool hasMessage = !string.IsNullOrEmpty(displayedStatus) && Time.unscaledTime < statusUntil;
            if (session.Observed != null && displayedStatus != null && displayedStatus.StartsWith("走近动物，观察")) hasMessage = false;
            string text = hasMessage ? displayedStatus : "";
            if (!CameraMode && session.Observed != null && !hasMessage)
                text = session.Observed.displayName + (life && !life.CanTalk(session.Observed) ? "" : "   ·   E");
            if (captureLabel != null)
                captureLabel.text = session.Recorder != null && session.Recorder.IsRecording
                    ? "记录中  " + Mathf.RoundToInt(session.Recorder.CaptureProgress * 100f) + "%"
                    : GetComponent<LifeOnboarding>()?.State?.stage == LifeOnboardingStage.TakeSpeechPhoto ? "点下方圆形快门，或按空格拍照。左键点画面可以对焦。" :
                        hasMessage && !displayedStatus.StartsWith("走近动物，观察") ? displayedStatus : PhotoGuide;
            statusRoot.gameObject.SetActive(!JournalOpen && !CameraMode && !string.IsNullOrEmpty(text));
            statusLabel.text = text;
            float width = Mathf.Min(1120f, canvasRect.rect.width - 48f);
            float height = Mathf.Clamp(Mathf.Ceil(text.Length * statusLabel.fontSize / (width - 24f)) * statusLabel.fontSize * 1.4f + 12f, 54f, 140f);
            statusRoot.sizeDelta = new Vector2(width, height);
            statusLabel.rectTransform.sizeDelta = statusRoot.sizeDelta;
            AnimateStatusGlow(Time.unscaledTime);
        }

        private void AnimateStatusGlow(float time)
        {
            float breath = .5f + .5f * Mathf.Sin(time * Mathf.PI * .8f);
            statusLabel.color = new Color(1f, 1f, 1f, Mathf.Lerp(.78f, 1f, breath));
            statusGlow.Intensity = Mathf.Lerp(.18f, .375f, breath);
        }

        private void OnValidate() { inspectorDirty = true; }

        private bool ApplyInspectorStyle()
        {
            try
            {
                if (uiStyle == null) throw new ArgumentException("样式不能为空");
                // Keep the active style separate from the Inspector's unfinished edits.
                ProceduralUiStyle candidate = ProceduralUiStyle.Parse(JsonUtility.ToJson(uiStyle));
                ProceduralUiStyle.Use(candidate);
                appliedStyle = candidate;
                lastStyleWarning = null;
                return true;
            }
            catch (ArgumentException error)
            {
                ProceduralUiStyle.Use(appliedStyle ?? (appliedStyle = new ProceduralUiStyle()));
                if (lastStyleWarning != error.Message)
                {
                    Debug.LogWarning("UI 样式暂未更新：" + error.Message + "。保留上一份有效预览，Inspector 中的输入也已保留。", this);
                    lastStyleWarning = error.Message;
                }
                return false;
            }
        }

        [ContextMenu("从 HTML JSON 导入样式")]
        private void ImportHtmlStyle()
        {
            TextAsset asset = Resources.Load<TextAsset>("WeatherHouseUiStyle");
            if (asset == null) { Debug.LogWarning("Resources 中没有 WeatherHouseUiStyle.json，Inspector 参数已保留。", this); return; }
            try
            {
                ProceduralUiStyle imported = ProceduralUiStyle.Parse(asset.text);
#if UNITY_EDITOR
                UnityEditor.Undo.RecordObject(this, "导入 UI 样式");
#endif
                uiStyle = imported;
                inspectorDirty = true;
#if UNITY_EDITOR
                UnityEditor.EditorUtility.SetDirty(this);
                if (!Application.isPlaying && gameObject.scene.IsValid())
                    UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(gameObject.scene);
#endif
            }
            catch (ArgumentException error) { Debug.LogWarning("样式没有导入：" + error.Message + "。Inspector 参数已保留。", this); }
        }

        private void RebuildInspectorPreview()
        {
            inspectorDirty = false;
            if (!ApplyInspectorStyle()) return;
            bool restoreGuessFocus = guessInput != null && guessInput.isFocused;
            if (guessInput != null) guessDraft = guessInput.text;
            ClearFocus();
            ReleasePhotoTextures();
            foreach (Transform child in canvasRect)
            {
                child.gameObject.SetActive(false);
                Destroy(child.gameObject);
            }
            bubbleRoot = modalRoot = cameraRoot = null;
            bubbleShape = null; bubbleGlyphs.Clear(); bubbleMeanings.Clear();
            captureLabel = playbackLabel = null;
            cameraGroup = null;
            bubbleAnimal = null;
            bubbleGroup = null;
            bubbleWanted = false;
            bubbleUtterance = null;
            BuildChrome();
            if (CameraMode) BuildCameraChrome();
            UpdateAnimalBubble();
            RebuildModal();
            if (restoreGuessFocus && guessInput != null) guessInput.ActivateInputField();
            dirty = false;
        }

        private void OnChanged()
        {
            dirty = true;
            foreach (Text meaning in bubbleMeanings)
            {
                if (!meaning) continue;
                string id = meaning.gameObject.name;
                meaning.text = session?.Notebook?.GetWord(id)?.confirmed == true ? FindGlyph(id)?.name ?? "" : "";
            }
        }

        private void Update()
        {
            if (session == null || canvas == null) return;
            if (ApplyOnboardingSuppression()) return;
            if (inspectorDirty && !IsAlbumOpening) RebuildInspectorPreview();
            if (!string.IsNullOrEmpty(Input.compositionString)) lastCompositionFrame = Time.frameCount;
            UpdateStatus();
            UpdateCameraTransition(ProceduralUiLibrary.FadeFrameDelta(Time.unscaledDeltaTime));
            UpdatePhotoLayout();
            if (focusMark != null) focusMark.gameObject.SetActive(Time.unscaledTime < focusMarkUntil);
            if (Input.GetKeyDown(KeyCode.Escape)) { CloseTop(); return; }
            if (dirty && !InputFocused() && !IsAlbumOpening && !(gestureInput && gestureInput.IsStrokeActive))
            {
                dirty = false;
                if (JournalOpen && expandedPhoto == null) RebuildModal();
            }
            UpdateAnimalBubble();
            UpdatePlayback();
            if (InputFocused()) return;
            if (Input.GetKeyDown(KeyCode.J)) { UseBookShortcut(KeyCode.J); return; }
            if (Input.GetKeyDown(KeyCode.M)) { UseBookShortcut(KeyCode.M); return; }
            if (Input.GetKeyDown(KeyCode.L)) { UseBookShortcut(KeyCode.L); return; }
            if (IsAlbumOpening) return;
            if (JournalOpen && expandedPhoto == null && publicTaskId == null && !glyphDetailOpen && !albumGesturesSelected)
            {
                if (Input.GetKeyDown(KeyCode.RightArrow)) { TurnJournalPage(1); return; }
                if (Input.GetKeyDown(KeyCode.LeftArrow)) { TurnJournalPage(-1); return; }
            }
            if (Input.GetKeyDown(KeyCode.C) && !JournalOpen) SetCameraMode(!cameraRequested);
            if (Input.GetKeyDown(KeyCode.E)) InteractPrimary();
            if (JournalOpen) return;
            if (CameraMode && cameraRequested && Input.GetMouseButtonDown(0) && (EventSystem.current == null || !EventSystem.current.IsPointerOverGameObject()))
            {
                session.PhotoRig.FocusAt(Input.mousePosition);
                RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, Input.mousePosition, null, out Vector2 focusPosition);
                focusMark.anchoredPosition = focusPosition;
                focusMarkUntil = Time.unscaledTime + 1.5f;
            }
            if (CanTakePhoto && Input.GetKeyDown(KeyCode.Space)) session.TakePhoto();
            if (!life)
            {
                if (Input.GetKeyDown(KeyCode.Alpha1)) session.Cast(LocalWeather.Rain);
                if (Input.GetKeyDown(KeyCode.Alpha2)) session.Cast(LocalWeather.Sun);
                if (Input.GetKeyDown(KeyCode.Alpha3)) session.Cast(LocalWeather.Wind);
            }
        }

        private void LateUpdate()
        {
            if (session == null || canvas == null) return;
            if (ApplyOnboardingSuppression()) return;
            PositionAnimalBubble();
            FitAlbum();
            FitDetails();
        }

        private void FitAlbum()
        {
            if (albumRoot == null) return;
            float scale = Mathf.Min(canvasRect.rect.width / 1672f, canvasRect.rect.height / 941f);
            albumRoot.localScale = Vector3.one * scale;
        }

        private void FitDetails()
        {
            if (!detailRoot || !canvasRect) return;
            Vector2 size = detailRoot.sizeDelta;
            if (size.x <= 0 || size.y <= 0) return;
            float scale = Mathf.Min(1f, Mathf.Max(1f, canvasRect.rect.width - 48f) / size.x,
                Mathf.Max(1f, canvasRect.rect.height - 48f) / size.y);
            detailRoot.localScale = Vector3.one * scale;
        }

        public void UseBookShortcut(KeyCode key)
        {
            int tab = key == KeyCode.J ? 0 : key == KeyCode.M ? 2 : key == KeyCode.L ? 3 : -1;
            if (tab < 0) return;
            int current = albumTasksSelected ? 3 : albumGesturesSelected ? 2 : albumPhotosSelected ? 1 : 0;
            if (JournalOpen && current == tab && !glyphDetailOpen && expandedPhoto == null && publicTaskId == null)
            { ToggleJournal(); return; }
            if (IsAlbumOpening) return;
            if (gestureInput) gestureInput.Cancel();
            albumPhotosSelected = false; albumGesturesSelected = tab == 2; albumTasksSelected = tab == 3;
            expandedPhoto = null; publicTaskId = null; glyphDetailOpen = false; selectedGlyph = null;
            ClearFocus();
            if (JournalOpen) RebuildJournalPage();
            else ToggleJournal();
        }

        public void ToggleJournal()
        {
            if (gestureInput) gestureInput.Cancel();
            if (JournalOpen)
            {
                if (journal3D && journal3D.IsClosing) journal3D.Resume();
                else if (albumOpening != null && albumOpening.IsClosing) albumOpening.Resume();
                else CloseJournal();
                return;
            }
            JournalOpen = true;
            expandedPhoto = null;
            publicTaskId = null;
            glyphDetailOpen = false;
            SetCameraMode(false, true);
            ClearFocus();
            openingJournal = true;
            try { RebuildModal(); }
            finally { openingJournal = false; }
            dirty = false;
            if (journal3D && journal3D.Available)
                journal3D.Open(BuildJournalIntro);
            else if (albumOpening != null) albumOpening.Play();
            session.RefreshInputBlocking();
        }

        public void ListenToAnimal()
        {
            if (!isActiveAndEnabled || PrimaryInteraction != LifePrimaryAction.Talk) return;
            AnimalActor animal = session.Observed;
            string utterance = animal.UtteranceId;
            if (life)
            {
                if (!life.Listen(animal, utterance)) return;
            }
            else
            {
                session.Notebook.Observe(animal.animalId, utterance, animal.CurrentGlyphIds);
                session.SaveNotebook();
            }
            session.BeginDialogue(animal);
            UpdateAnimalBubble();
        }

        // Kept for callers that previously opened/closed the reply modal.
        public void ToggleDialogue() => ListenToAnimal();
        public void CloseDialogue()
        {
            ClearFocus();
            if (session) session.RefreshInputBlocking();
        }

        private void CloseJournal()
        {
            if (journal3D && journal3D.IsClosing) return;
            if (albumOpening != null && albumOpening.IsClosing) return;
            ClearFocus();
            // Details replace or cover the book; return to its current page before folding it away.
            if (expandedPhoto != null || glyphDetailOpen || publicTaskId != null)
            {
                HideDetails();
            }
            if (journal3D && journal3D.Available) journal3D.Close(CompleteJournalClose);
            else if (albumOpening != null) albumOpening.PlayReverse(CompleteJournalClose);
            else CompleteJournalClose();
        }

        private void CompleteJournalClose()
        {
            JournalOpen = false;
            RebuildModal();
            dirty = false;
            session.RefreshInputBlocking();
        }

        public void SetCameraMode(bool value, bool immediate = false)
        {
            if (value && gestureInput) gestureInput.Cancel();
            if (value && !cameraRequested) session.BeginPhotoSpeech();
            if (!value) session.EndPhotoSpeech();
            if (!value && CameraMode && session.Recorder.IsRecording) session.Recorder.FinishLiveCapture();
            cameraRequested = value;
            if (!value && !CameraMode)
            {
                session.PhotoRig.SetActive(false);
                session.RefreshInputBlocking();
                return;
            }
            if (value && !CameraMode)
            {
                session.PhotoRig.SetActive(true);
                CameraMode = true;
                cameraBlend = 0f;
                if (controlsRoot) controlsRoot.gameObject.SetActive(false);
                session.RefreshInputBlocking();
                BuildCameraChrome();
            }
            if (immediate) cameraBlend = value ? 1f : 0f;
            UpdateCameraTransition(0f);
        }

        private void UpdateCameraTransition(float deltaTime)
        {
            if (!CameraMode) return;
            cameraBlend = ProceduralUiLibrary.FadeAlpha(cameraBlend, cameraRequested, deltaTime, .2f);
            float visibility = Mathf.SmoothStep(0f, 1f, cameraBlend);
            if (cameraGroup)
            {
                cameraGroup.alpha = visibility;
                cameraGroup.interactable = cameraGroup.blocksRaycasts = CanTakePhoto;
            }
            session.PhotoRig.SetVignetteVisibility(visibility);
            if (cameraRequested || cameraBlend > 0f) return;
            session.PhotoRig.SetActive(false);
            CameraMode = false;
            if (cameraRoot) { cameraRoot.gameObject.SetActive(false); Destroy(cameraRoot.gameObject); }
            cameraRoot = null; cameraGroup = null; captureLabel = null; focusMark = null;
            if (controlsRoot) controlsRoot.gameObject.SetActive(!JournalOpen);
            session.RefreshInputBlocking();
            UpdateStatus();
        }

        private void BuildCameraChrome()
        {
            if (canvasRect == null) return;
            cameraRoot = Rect(canvasRect, "Camera mode", Vector2.zero, Vector2.zero);
            cameraGroup = cameraRoot.gameObject.AddComponent<CanvasGroup>();
            cameraGroup.alpha = Mathf.SmoothStep(0f, 1f, cameraBlend);
            cameraGroup.interactable = cameraGroup.blocksRaycasts = CanTakePhoto;
            cameraRoot.anchorMin = Vector2.zero; cameraRoot.anchorMax = Vector2.one;
            cameraRoot.offsetMin = cameraRoot.offsetMax = Vector2.zero;
            var frame = ProceduralUiLibrary.Create(cameraRoot, "Viewfinder", UiMark.Viewfinder, Vector2.zero, Vector2.zero, ProceduralUiLibrary.Cream, 10f);
            frame.rectTransform.anchorMin = new Vector2(.02f, .025f); frame.rectTransform.anchorMax = new Vector2(.98f, .975f);
            frame.rectTransform.offsetMin = frame.rectTransform.offsetMax = Vector2.zero;
            frame.cornerRadius = 40f; frame.viewfinderLegRatios = new Vector2(.23f, .19f);
            var dot = ProceduralUiLibrary.Create(cameraRoot, "Camera dot", UiMark.Dot, Vector2.zero, Vector2.one * 10f, ProceduralUiLibrary.Cream).rectTransform;
            dot.anchorMin = dot.anchorMax = new Vector2(.5f, .142f);
            var shutter = ProceduralUiLibrary.Button(cameraRoot, "Shutter", UiMark.Shutter, Vector2.zero, Vector2.one * 96f, ProceduralUiLibrary.Cream, 8f);
            var shutterRect = (RectTransform)shutter.transform;
            shutterRect.anchorMin = shutterRect.anchorMax = new Vector2(.5f, .077f);
            shutter.onClick.AddListener(() => { if (CanTakePhoto) session.TakePhoto(); });
            captureLabel = Label(cameraRoot, "Capture hint", PhotoGuide, Vector2.zero, Vector2.zero, 28, ProceduralUiLibrary.Cream);
            captureLabel.rectTransform.anchorMin = new Vector2(.08f, .17f); captureLabel.rectTransform.anchorMax = new Vector2(.92f, .23f);
            captureLabel.rectTransform.offsetMin = captureLabel.rectTransform.offsetMax = Vector2.zero;
            captureLabel.resizeTextForBestFit = true;
            captureLabel.resizeTextMinSize = 18; captureLabel.resizeTextMaxSize = captureLabel.fontSize;
            photoHintFontSize = captureLabel.fontSize;
            photoControlScale = -1f;
            focusMark = ProceduralUiLibrary.Create(cameraRoot, "Focus indicator", UiMark.Viewfinder, Vector2.zero, new Vector2(60f, 60f), ProceduralUiLibrary.Cream, 2f).rectTransform;
            focusMark.gameObject.SetActive(false);
            UpdatePhotoLayout();
            UpdateStatus();
        }

        private void UpdatePhotoLayout()
        {
            if (!cameraRoot || !captureLabel || cameraRoot.rect.height <= 0f) return;
            float scale = Mathf.Min(1f, cameraRoot.rect.height / 1000f);
            if (Mathf.Approximately(photoControlScale, scale)) return;
            photoControlScale = scale;
            var shutter = cameraRoot.Find("Shutter").GetComponent<ProceduralUiGraphic>();
            shutter.rectTransform.sizeDelta = Vector2.one * (96f * scale);
            shutter.lineWidth = 8f * ProceduralUiStyle.Current.lineScale * scale;
            shutter.SetVerticesDirty();
            ((RectTransform)cameraRoot.Find("Camera dot")).sizeDelta = Vector2.one * (10f * scale);
            captureLabel.fontSize = Mathf.Max(1, Mathf.RoundToInt(photoHintFontSize * scale));
            captureLabel.resizeTextMaxSize = captureLabel.fontSize;
            captureLabel.resizeTextMinSize = Mathf.Clamp(Mathf.RoundToInt(18f * scale), 1, captureLabel.fontSize);
        }

        private void CloseTop()
        {
            if (gestureInput && gestureInput.IsWorldDrawing) { gestureInput.Cancel(); return; }
            ClearFocus();
            if (expandedPhoto != null || glyphDetailOpen || publicTaskId != null) { HideDetails(); return; }
            if (JournalOpen) { CloseJournal(); return; }
            else if (CameraMode) { SetCameraMode(false); return; }
            RebuildModal();
            session.RefreshInputBlocking();
        }

        private void RebuildModal()
        {
            if (journal3D && !JournalOpen) journal3D.Unbind();
            controlsRoot.gameObject.SetActive(!JournalOpen && !CameraMode);
            statusRoot.gameObject.SetActive(!JournalOpen && !CameraMode);
            if (!JournalOpen) ReleasePhotoTextures();
            playingPhoto = false;
            expandedImage = null;
            playbackLabel = null;
            guessInput = null;
            albumRoot = null;
            detailRoot = null;
            detailLayer = null;
            albumOpening = null;
            if (modalRoot != null) { modalRoot.gameObject.SetActive(false); Destroy(modalRoot.gameObject); modalRoot = null; }
            if (!JournalOpen) return;
            modalRoot = Panel(canvasRect, "Modal backdrop", Vector2.zero, new Vector2(4000f, 3000f), new Color(.13f, .19f, .16f, .48f));
            BuildJournal();
            if (JournalOpen && (expandedPhoto != null || glyphDetailOpen || publicTaskId != null)) ShowDetails();
        }

        private void ShowDetails()
        {
            ClearDetailLayer();
            if (expandedPhoto != null)
            {
                if (albumRoot) albumRoot.gameObject.SetActive(false);
                BuildPhoto();
            }
            else if (glyphDetailOpen) BuildGlyphDetail();
            else if (publicTaskId != null) BuildPublicRecord();
        }

        private void HideDetails()
        {
            ClearFocus(); expandedPhoto = null; glyphDetailOpen = false; publicTaskId = null;
            ClearDetailLayer();
            if (albumRoot) albumRoot.gameObject.SetActive(true);
        }

        private void ClearDetailLayer()
        {
            if (detailLayer) { detailLayer.gameObject.SetActive(false); Destroy(detailLayer.gameObject); }
            detailLayer = detailRoot = null; expandedImage = null; expandedTexture = null;
            playbackLabel = null; playingPhoto = false; guessInput = null;
        }

        private void BuildJournal()
        {
            // LifeSession adds this component during startup, after the HUD may cache it.
            if (life && !gestureInput) gestureInput = GetComponent<LifeGestureInput>();
            albumRoot = AlbumBookView.Create(modalRoot, albumPhotosSelected,
                () => { albumPhotosSelected = false; albumGesturesSelected = false; albumTasksSelected = false; glyphDetailOpen = false; ClearFocus(); RebuildJournalPage(); },
                () => { albumPhotosSelected = true; albumGesturesSelected = false; albumTasksSelected = false; selectedGlyph = null; glyphDetailOpen = false; ClearFocus(); RebuildJournalPage(); },
                CloseTop, () => TurnJournalPage(1));
            if (life)
            {
                LifeJournalChrome.GestureTab(albumRoot, albumGesturesSelected, () =>
                { albumGesturesSelected = true; albumTasksSelected = false; glyphDetailOpen = false; ClearFocus(); RebuildJournalPage(); });
                LifeJournalChrome.PhotographyTab(albumRoot, albumTasksSelected, () =>
                { albumTasksSelected = true; albumGesturesSelected = false; glyphDetailOpen = false; ClearFocus(); RebuildJournalPage(); });
                LifeJournalChrome.BookIcons(albumRoot, albumPhotosSelected, albumGesturesSelected, albumTasksSelected);
                if (albumTasksSelected)
                {
                    LifePhotoBook.Build(albumRoot, session, photoTaskPage, font, id =>
                    {
                        var records = session.Notebook.FindTaskPhotos(id);
                        if (records.Count == 0) { publicTaskId = id; ClearFocus(); ShowDetails(); return; }
                        expandedPhoto = records[records.Count - 1]; currentFrame = 0; ClearFocus(); ShowDetails();
                    }, direction => TurnJournalPage(direction));
                    FinishJournal(); return;
                }
                if (albumGesturesSelected)
                {
                    LifeGestureBook.Build(albumRoot, life, gestureInput, font, selectedGesture,
                        id => { selectedGesture = id; RebuildJournalPage(); },
                        id => { albumGesturesSelected = false; selectedGlyph = guessGlyphId = id; guessDraft = session.Notebook.GetWord(id)?.guess ?? ""; glyphDetailOpen = true; RebuildModal(); });
                    FinishJournal(); return;
                }
            }
            bool samples = !life && useAlbumDesignSamples && !albumPhotosSelected;
            if (samples)
            {
                string[] names = { "SampleHouse", "SampleFrog", "SampleSquirrel", "SampleTraveler" };
                for (int i = 0; i < names.Length; i++)
                    AlbumBookView.Photo(albumRoot, i, Resources.Load<Texture2D>("AlbumBook/" + names[i]), "Sample photo " + (i + 1));
            }
            else BuildAlbumPhotos(albumRoot);
            AlbumBookView.AddStickers(albumRoot);
            BuildWordPage(albumRoot);
            int collected = samples ? session.Catalog.glyphs.Length :
                Array.FindAll(session.Catalog.glyphs, glyph => session.Notebook.GetWord(glyph.id)?.encountered == true).Length;
            AlbumBookView.Count(albumRoot, collected, session.Catalog.glyphs.Length, albumCountFont);
            FinishJournal();
        }

        private void FinishJournal()
        {
            FitAlbum();
            if (life)
            {
                if (!journal3D) journal3D = GetComponent<LifeJournal3D>() ?? gameObject.AddComponent<LifeJournal3D>();
                if (journal3D.Bind(albumRoot, openingJournal)) return;
            }
            albumOpening = albumRoot.gameObject.AddComponent<AlbumBookOpening>();
        }

        private void RebuildJournalPage(int direction = 1)
        {
            if (IsAlbumOpening) return;
            if (journal3D && journal3D.Available) journal3D.Turn(RebuildModal, direction);
            else RebuildModal();
        }

        private void TurnGlyphPage(int direction)
        {
            if (IsAlbumOpening || albumGesturesSelected || albumTasksSelected) return;
            int pages = Mathf.Max(1, Mathf.CeilToInt(session.Catalog.glyphs.Length / (float)AlbumBookView.GlyphsPerPage));
            glyphPage = (glyphPage + direction + pages) % pages;
            ClearFocus();
            RebuildJournalPage(direction);
        }

        private void TurnJournalPage(int direction)
        {
            if (IsAlbumOpening) return;
            if (albumTasksSelected)
            {
                photoTaskPage = (photoTaskPage + direction + LifePhotoBook.PageCount) % LifePhotoBook.PageCount;
                ClearFocus(); RebuildJournalPage(direction); return;
            }
            if (!albumPhotosSelected) { TurnGlyphPage(direction); return; }
            page += direction;
            ClearFocus();
            RebuildJournalPage(direction);
        }

        private void BuildAlbumPhotos(Transform book)
        {
            List<ObservationRecord> photos = selectedGlyph == null ? session.Notebook.observations : session.Notebook.FindEvidence(selectedGlyph);
            int pages = Mathf.Max(1, Mathf.CeilToInt(photos.Count / 4f));
            page = Mathf.Clamp(page, 0, pages - 1);
            for (int i = 0; i < 4; i++)
            {
                int index = page * 4 + i;
                if (index < photos.Count) BuildPhotoCard(book, photos[index], i);
                else AlbumBookView.Photo(book, i, null, "Empty photo " + (i + 1));
            }
        }

        private void BuildPhotoCard(Transform parent, ObservationRecord record, int slot)
        {
            Texture2D texture = null;
            try { texture = photoCache.Cover(record); }
            catch (Exception exception) { Debug.LogWarning(exception.Message, this); }
            var image = AlbumBookView.Photo(parent, slot, texture, "Photo " + record.id,
                () => { expandedPhoto = record; glyphDetailOpen = false; currentFrame = 0; ClearFocus(); ShowDetails(); });
            if (texture == null)
                Label(image.transform, "Missing frame", "照片暂时无法读取", new Vector2(0f, -image.rectTransform.rect.height * .35f), new Vector2(image.rectTransform.rect.width - 12f, 46f), 17, Ink);
        }

        private void BuildJournalIntro(RectTransform book)
        {
            for (int i = 0; i < 4; i++) AlbumBookView.Photo(book, i, null, "Intro empty photo " + (i + 1));
            AlbumBookView.AddStickers(book);
            BuildWordPage(book, true);
            int found = Array.FindAll(session.Catalog.glyphs, glyph => session.Notebook.GetWord(glyph.id)?.encountered == true).Length;
            AlbumBookView.Count(book, found, session.Catalog.glyphs.Length, albumCountFont);
        }

        private void BuildWordPage(Transform book, bool intro = false)
        {
            GlyphDefinition[] glyphs = session.Catalog.glyphs;
            var photographed = new HashSet<string>();
            foreach (ObservationRecord photo in session.Notebook.observations)
                foreach (string id in photo?.glyphIds ?? Array.Empty<string>()) photographed.Add(id);
            if (!intro) glyphPage = Mathf.Clamp(glyphPage, 0, Mathf.Max(0, (glyphs.Length - 1) / AlbumBookView.GlyphsPerPage));
            int first = intro ? 0 : glyphPage * AlbumBookView.GlyphsPerPage;
            for (int i = first; i < Mathf.Min(first + AlbumBookView.GlyphsPerPage, glyphs.Length); i++)
            {
                GlyphDefinition glyph = glyphs[i];
                GlyphKnowledge knowledge = session.Notebook.GetWord(glyph.id);
                bool seen = knowledge != null && knowledge.encountered;
                bool visible = seen || (!life && useAlbumDesignSamples && !albumPhotosSelected);
                var wordButton = AlbumBookView.Word(book, i - first, glyph, visible, !intro && selectedGlyph == glyph.id, () =>
                {
                    if (guessGlyphId != glyph.id) guessDraft = knowledge?.guess ?? "";
                    guessGlyphId = glyph.id;
                    selectedGlyph = glyph.id;
                    if (life) LifeJournalChrome.SelectWord(book, glyph.id);
                    glyphDetailOpen = true;
                    ClearFocus();
                    ShowDetails();
                });
                if (life) LifeJournalChrome.Word(wordButton, glyph.id, knowledge?.confirmed == true ? 2 : photographed.Contains(glyph.id) ? 1 : 0,
                    knowledge?.confirmed == true ? glyph.name : "", !intro && selectedGlyph == glyph.id, font,
                    () => { if (journal3D) journal3D.RequestContentRefresh(); }, !intro);
            }
        }

        private void BuildGlyphDetail()
        {
            GlyphDefinition definition = FindGlyph(selectedGlyph);
            GlyphKnowledge selected = session.Notebook.GetWord(selectedGlyph);
            bool encountered = selected != null && selected.encountered;
            bool confirmed = encountered && selected.confirmed;
            Vector2 size = confirmed ? new Vector2(320f, 278f) :
                encountered ? new Vector2(390f, 350f) : new Vector2(320f, 190f);
            var shade = detailLayer = Panel(modalRoot, "Word detail backdrop", Vector2.zero, new Vector2(4000f, 3000f), new Color(.13f, .19f, .16f, .38f));
            var card = detailRoot = PaperPanel(shade, "Word detail", Vector2.zero, size, ProceduralUiLibrary.Cream);
            float center = 0f;
            FitDetails();
            CloseButton(card, new Vector2(size.x * .5f - 30f, size.y * .5f - 30f), CloseTop, null, 35f);
            if (definition != null && (!life || encountered))
                Glyph(card, definition, new Vector2(0f, center + (confirmed ? 64f : 104f)), new Vector2(50f, 60f));
            if (!encountered)
            {
                Label(card, "Unseen meaning", "还没有认识这个字。\n带着相机去看看吧。", new Vector2(0f, center), new Vector2(240f, 70f), 21, Ink);
                return;
            }
            if (confirmed)
                Label(card, "Confirmed meaning", definition == null ? "" : definition.name, new Vector2(0f, center - 4f), new Vector2(250f, 34f), 26, Ink);
            else
            {
                Label(card, "Guess heading", "我的猜测", new Vector2(0f, center + 34f), new Vector2(300f, 24f), 19, Ink, TextAnchor.MiddleLeft);
                guessInput = InputBox(card, "Word guess", guessDraft, "", new Vector2(0f, center - 9f), new Vector2(300f, 54f));
                guessInput.targetGraphic.color = Color.Lerp(ProceduralUiLibrary.Cream, Color.white, .35f);
                guessInput.transition = Selectable.Transition.ColorTint;
                var guessColors = guessInput.colors;
                guessColors.highlightedColor = new Color(1f, .98f, .91f);
                guessColors.selectedColor = new Color(1f, .96f, .82f);
                guessColors.pressedColor = guessColors.selectedColor;
                guessInput.colors = guessColors;
                guessInput.characterLimit = 160;
                guessInput.onValueChanged.AddListener(value => guessDraft = value);
                guessInput.ActivateInputField();
                TextButton(card, "Save guess", "提交猜测", new Vector2(0f, center - 64f), new Vector2(144f, 32f), () => { session.SaveGuess(selectedGlyph, guessDraft); ClearFocus(); }, neutral: true);
            }
            TextButton(card, "Word evidence", "查看相关照片", new Vector2(0f, center - (confirmed ? 77f : 116f)), new Vector2(190f, 32f), () =>
            { LifeOnboarding.EvidenceOpened(session, selectedGlyph); albumPhotosSelected = true; glyphDetailOpen = false; page = 0; ClearFocus(); RebuildModal(); }, neutral: true);
            LifeOnboarding.DecorateWord(session, card, font, selectedGlyph, confirmed);
        }

        private void BuildPhoto()
        {
            detailLayer = Rect(modalRoot, "Photo detail layer", Vector2.zero, new Vector2(4000, 3000));
            var card = detailRoot = PaperPanel(detailLayer, "Expanded motion photo", Vector2.zero, new Vector2(1120f, 830f), Color.white);
            FitDetails();
            Label(card, "Photo title", "留下的片刻", new Vector2(-272f, 355f), new Vector2(480f, 62f), 44, Ink, TextAnchor.MiddleLeft);
            CloseButton(card, new Vector2(510f, 355f), CloseTop);
            expandedImage = Raw(card, "Motion photo frame", new Vector2(0f, 4f), new Vector2(1024f, 576f));
            currentFrame = Mathf.Clamp(currentFrame, 0, Mathf.Max(0, expandedPhoto.frameCount - 1));
            LoadExpandedFrame();
            GlyphLine(card, expandedPhoto.glyphIds, new Vector2(0f, -317f), 38f, 950f);
            var play = TextButton(card, "Play motion photo", "播放", new Vector2(0f, -370f), new Vector2(140f, 40f), () =>
            {
                playingPhoto = !playingPhoto;
                if (playingPhoto && currentFrame >= expandedPhoto.frameCount - 1) { currentFrame = 0; LoadExpandedFrame(); }
                nextFrameAt = Time.unscaledTime + 1f / Mathf.Max(1f, expandedPhoto.framesPerSecond);
                UpdatePlaybackLabel();
            }, neutral: true);
            playbackLabel = play.GetComponentInChildren<Text>();

        }

        private void BuildPublicRecord()
        {
            detailLayer = Panel(modalRoot, "Encounter record backdrop", Vector2.zero, new Vector2(4000, 3000), new Color(.13f, .19f, .16f, .38f));
            var card = detailRoot = PaperPanel(detailLayer, "Encounter record", Vector2.zero, new Vector2(660, 440), Paper);
            FitDetails(); CloseButton(card, new Vector2(282, 172), CloseTop);
            Label(card, "Encounter record title", "相遇记录", new Vector2(0, 160), new Vector2(500, 40), 27, Ink);
            var records = session.Notebook.publicHistory.FindAll(c => c != null && c.targetId == publicTaskId && c.completed);
            Label(card, "Public encounter context", string.Join("\n\n", records.ConvertAll(c => c.description)), new Vector2(0, -5), new Vector2(550, 250), 21, Ink, TextAnchor.UpperLeft);
        }

        private string PhotoParticipant(string id)
        {
            if (id == "player") return "玩家";
            AnimalActor animal = Array.Find(session.animals ?? Array.Empty<AnimalActor>(), a => a && a.animalId == id);
            return animal ? animal.displayName : string.IsNullOrEmpty(id) ? "未记录" : "当时的动物";
        }

        private static string PhotoVisibleState(string state)
        {
            if (string.IsNullOrWhiteSpace(state)) return "物件状态未记录";
            // Old snapshots included the AI-only Describe payload. Keep its physical-state field only.
            int anchor = state.IndexOf("；指示位置=", StringComparison.Ordinal);
            if (anchor >= 0)
            {
                int start = state.IndexOf('；', anchor + 1);
                if (start < 0) return "物件状态未记录";
                int end = state.IndexOf('；', start + 1);
                return end < 0 ? state.Substring(start + 1) : state.Substring(start + 1, end - start - 1);
            }
            return state;
        }

        private void UpdatePlayback()
        {
            if (!playingPhoto || expandedPhoto == null || expandedImage == null || Time.unscaledTime < nextFrameAt) return;
            if (currentFrame >= expandedPhoto.frameCount - 1) { playingPhoto = false; UpdatePlaybackLabel(); return; }
            currentFrame++;
            LoadExpandedFrame();
            nextFrameAt = Time.unscaledTime + 1f / Mathf.Max(1f, expandedPhoto.framesPerSecond);
            UpdatePlaybackLabel();
        }

        private void UpdatePlaybackLabel()
        {
            if (playbackLabel != null) playbackLabel.text = playingPhoto ? "暂停" : "播放";
        }

        private void LoadExpandedFrame()
        {
            expandedTexture = null;
            expandedImage.texture = null;
            try { expandedTexture = photoCache.Frame(expandedPhoto, currentFrame); expandedImage.texture = expandedTexture; }
            catch (Exception exception) { playingPhoto = false; Debug.LogWarning(exception.Message, this); }
            if (expandedImage.texture == null)
            {
                expandedImage.texture = Resources.Load<Texture2D>("AlbumBook/PhotoMissing");
                expandedImage.GetComponent<RectTransform>().sizeDelta = new Vector2(432f, 576f);
            }
            else expandedImage.rectTransform.sizeDelta = new Vector2(1024f, 576f);
        }

        private void UpdateAnimalBubble()
        {
            AnimalActor animal = session.Observed;
            if (!session.ShouldShowAnimalUtterance(animal)) animal = null;
            string utterance = session.DisplayedUtteranceId(animal);
            bubbleWanted = animal != null && animal == bubbleAnimal && utterance == bubbleUtterance;
            // Finish the old word on its original speaker before reusing the presentation slot.
            if (bubbleAnimal != null) return;
            if (animal == null) return;
            string[] glyphs = session.DisplayedGlyphIds(animal) ?? Array.Empty<string>();
            if (glyphs.Length == 0) return;
            bubbleAnimal = animal;
            bubbleUtterance = utterance;
            Vector2 size = ProceduralUiLibrary.AnimalBubbleSize(animalBubbleSize, glyphs.Length, animalGlyphHeight, canvasRect.rect.size);
            if (!bubbleRoot)
            {
                bubbleRoot = Rect(canvasRect, "Animal utterance", Vector2.zero, size);
                bubbleGroup = bubbleRoot.gameObject.AddComponent<CanvasGroup>();
                bubbleShape = ProceduralUiLibrary.Create(bubbleRoot, "Animal bubble", UiMark.AnimalBubble, Vector2.zero, size, ProceduralUiLibrary.Cream);
            }
            bubbleRoot.sizeDelta = bubbleShape.rectTransform.sizeDelta = size;
            bubbleRoot.gameObject.SetActive(true);
            bubbleGroup.alpha = 0f; bubbleGroup.interactable = bubbleGroup.blocksRaycasts = false;
            bubbleWanted = true;
            float height = Mathf.Min(animalGlyphHeight, size.y * .48f, size.x * .78f / (glyphs.Length * .95f));
            float inset = 2f + Mathf.Min(size.x, size.y) * bubbleShape.waviness;
            float bodyCenter = (size.y - 2f * inset) * bubbleShape.tailSize * .5f;
            for (int i = 0; i < glyphs.Length; i++)
            {
                if (i == bubbleGlyphs.Count)
                {
                    var slot = Rect(bubbleRoot, "Animal glyph slot " + i, Vector2.zero, Vector2.zero);
                    bubbleGlyphs.Add(slot.gameObject.AddComponent<AnimalGlyphGraphic>());
                    bubbleMeanings.Add(Label(slot, "Meaning", "", Vector2.zero, Vector2.zero, 16, Color.white));
                }
                AnimalGlyphGraphic graphic = bubbleGlyphs[i]; GlyphDefinition definition = FindGlyph(glyphs[i]);
                graphic.gameObject.SetActive(definition != null);
                if (definition == null) continue;
                graphic.SvgBody = definition.body; graphic.lineWidth = animalGlyphStrokeWidth; graphic.color = Ink; graphic.raycastTarget = false;
                graphic.rectTransform.sizeDelta = new Vector2(height * 5f / 6f, height);
                graphic.rectTransform.anchoredPosition = new Vector2((i - (glyphs.Length - 1) * .5f) * height * .95f, bodyCenter);
                Text meaning = bubbleMeanings[i]; meaning.gameObject.name = definition.id;
                meaning.text = session.Notebook.GetWord(definition.id)?.confirmed == true ? definition.name : "";
                meaning.fontSize = Mathf.Clamp(Mathf.RoundToInt(height * .36f), 12, 18);
                float meaningHeight = Mathf.Max(height * .45f, meaning.fontSize * 1.4f);
                meaning.rectTransform.sizeDelta = new Vector2(height * 1.1f, meaningHeight);
                meaning.rectTransform.anchoredPosition = Vector2.up * (size.y * .5f + 6f + meaningHeight * .5f - bodyCenter);
            }
            for (int i = glyphs.Length; i < bubbleGlyphs.Count; i++) bubbleGlyphs[i].gameObject.SetActive(false);
            if (modalRoot != null) modalRoot.SetAsLastSibling();
        }

        private void PositionAnimalBubble() => UpdateAnimalPresentation(ProceduralUiLibrary.FadeFrameDelta(Time.unscaledDeltaTime));

        private void UpdateAnimalPresentation(float deltaTime)
        {
            if (bubbleRoot == null) return;
            if (!bubbleAnimal) { ClearAnimalBubble(); return; }
            Camera view = session.CurrentView;
            // Social's LateUpdate has already placed its world conversation; avoid a second bubble over it.
            bool visible = BubbleAnchorVisible(view, bubbleAnimal) && !JournalOpen && !session.UsesSocialPresentation(bubbleAnimal);
            bubbleRoot.gameObject.SetActive(visible);
            if (!visible)
            {
                bubbleGroup.alpha = 0f;
                if (!bubbleWanted || !bubbleAnimal) ClearAnimalBubble();
                return;
            }
            bubbleGroup.alpha = ProceduralUiLibrary.FadeAlpha(bubbleGroup.alpha, bubbleWanted, deltaTime);
            if (!bubbleWanted && bubbleGroup.alpha <= 0f) { ClearAnimalBubble(); return; }
            Vector3 screen = view.WorldToScreenPoint(bubbleAnimal.BubbleAnchor);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screen, null, out Vector2 local);
            local += Vector2.up * (bubbleRoot.sizeDelta.y * .5f + 34f);
            float half = bubbleRoot.sizeDelta.x * .5f;
            local.x = Mathf.Clamp(local.x, canvasRect.rect.xMin + half + 16f, canvasRect.rect.xMax - half - 16f);
            local.y = Mathf.Clamp(local.y, canvasRect.rect.yMin + bubbleRoot.sizeDelta.y * .5f + 42f,
                canvasRect.rect.yMax - bubbleRoot.sizeDelta.y * .5f - 48f);
            bubbleRoot.anchoredPosition = local;
        }

        public bool IsAnimalSpeechVisible(AnimalActor animal)
        {
            if (!isActiveAndEnabled || !canvas || !canvas.enabled || JournalOpen || !animal) return false;
            if (session.IsPhotoSpeechHeld(animal))
            {
                if (session.UsesSocialPresentation(animal)) return animal.HasAmbientSpeech && animal.HasAmbientPresentation;
            }
            else
            {
                bool lifeUtterance = animal.GetComponent<LifeSpeaker>()?.Current != null;
                bool onboardingUtterance = LifeOnboardingSpeech.Current(animal);
                if (!lifeUtterance && !onboardingUtterance && animal.HasAmbientSpeech) return true;
                if ((lifeUtterance || onboardingUtterance) && animal.HasAmbientPresentation) return false;
            }
            return bubbleAnimal == animal &&
                bubbleUtterance == session.DisplayedUtteranceId(animal) && bubbleRoot && bubbleRoot.gameObject.activeInHierarchy &&
                bubbleGroup && bubbleGroup.alpha >= ProceduralUiLibrary.SpeechVisibleAlpha && BubbleAnchorVisible(session.CurrentView, animal);
        }

        private bool BubbleAnchorVisible(Camera view, AnimalActor animal)
        {
            if (!view || !view.isActiveAndEnabled || !animal || !animal.IsAvailableForObservation) return false;
            Vector3 anchor = animal.BubbleAnchor;
            Vector3 screen = view.WorldToViewportPoint(anchor);
            if (screen.z <= view.nearClipPlane || screen.x <= 0f || screen.x >= 1f || screen.y <= 0f || screen.y >= 1f) return false;
            Vector3 ray = anchor - view.transform.position;
            int count = Physics.RaycastNonAlloc(view.transform.position, ray.normalized, bubbleSightHits, ray.magnitude,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            if (count == bubbleSightHits.Length) return false;
            for (int i = 0; i < count; i++)
                if (!bubbleSightHits[i].transform.IsChildOf(animal.transform) && !bubbleSightHits[i].transform.IsChildOf(session.PlayerTransform)) return false;
            return true;
        }

        private void ClearAnimalBubble()
        {
            if (bubbleRoot) bubbleRoot.gameObject.SetActive(false);
            if (bubbleGroup) bubbleGroup.alpha = 0;
            bubbleAnimal = null; bubbleUtterance = null; bubbleWanted = false;
        }

        private void GlyphLine(Transform parent, string[] ids, Vector2 center, float height, float maxWidth)
        {
            LifePhotoBook.TokenLine(parent, session, ids, center, height, maxWidth, font);
            if (life) LifeOnboardingPhotoLinks.Add(parent, ids, center, height, maxWidth, OnboardingOpenWord);
        }

        private GlyphDefinition FindGlyph(string id)
        {
            if (session.Catalog == null || session.Catalog.glyphs == null) return null;
            return Array.Find(session.Catalog.glyphs, glyph => glyph.id == id);
        }

        private void Glyph(Transform parent, GlyphDefinition glyph, Vector2 position, Vector2 size)
        {
            var rect = Rect(parent, "Glyph " + glyph.id, position, size);
            rect.gameObject.AddComponent<CanvasRenderer>();
            var graphic = rect.gameObject.AddComponent<AnimalGlyphGraphic>();
            graphic.SvgBody = glyph.body;
            graphic.lineWidth = animalGlyphStrokeWidth;
            graphic.color = Ink;
            graphic.raycastTarget = false;
            graphic.SetAllDirty();
        }

        private static RectTransform Rect(Transform parent, string name, Vector2 position, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = 5;
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            return rect;
        }

        private static RectTransform Panel(Transform parent, string name, Vector2 position, Vector2 size, Color color, bool raycast = true)
        {
            var rect = Rect(parent, name, position, size);
            var image = rect.gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = raycast;
            return rect;
        }

        private static RectTransform PaperPanel(Transform parent, string name, Vector2 position, Vector2 size, Color color, bool raycast = true)
        {
            var edge = ProceduralUiLibrary.Create(parent, name, UiMark.PlayerBubble, position, size, SoftOutline, 3f, raycast);
            edge.bubbleRadius = .10f;
            edge.waviness = .006f;
            var paper = ProceduralUiLibrary.Create(edge.transform, "Paper fill", UiMark.PlayerBubble, Vector2.zero, size - Vector2.one * 5f, color);
            paper.bubbleRadius = .10f;
            paper.waviness = .006f;
            edge.SetVerticesDirty(); paper.SetVerticesDirty();
            return edge.rectTransform;
        }

        private Text Label(Transform parent, string name, string content, Vector2 position, Vector2 size, int fontSize, Color color, TextAnchor alignment = TextAnchor.MiddleCenter)
        {
            var text = Rect(parent, name, position, size).gameObject.AddComponent<Text>();
            text.font = font;
            ProceduralUiStyle.Current.ApplyText(text, fontSize);
            text.text = content;
            text.color = color;
            text.alignment = alignment;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.raycastTarget = false;
            return text;
        }

        private Button TextButton(Transform parent, string name, string text, Vector2 position, Vector2 size, UnityEngine.Events.UnityAction clicked, float scale = 1f, bool neutral = false)
        {
            var rect = PaperPanel(parent, name, position, size, neutral ? Color.white : ProceduralUiLibrary.Grass);
            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = rect.GetComponent<Graphic>();
            var colors = button.colors;
            colors.highlightedColor = neutral ? new Color(.96f, .96f, .96f) : new Color(1f, .99f, .91f);
            colors.pressedColor = neutral ? new Color(.86f, .86f, .86f) : new Color(.80f, .85f, .70f);
            button.colors = colors;
            button.onClick.AddListener(clicked);
            button.onClick.AddListener(() => LifeAudio.Play(LifeAudioCue.Ui));
            Label(rect, "Label", text, Vector2.zero, size - new Vector2(12f, 6f) * scale, Mathf.Max(8, Mathf.RoundToInt(19f * scale)), Ink);
            return button;
        }

        private static void CloseButton(Transform parent, Vector2 position, UnityEngine.Events.UnityAction clicked, Color? color = null, float size = 49f)
        {
            var button = ProceduralUiLibrary.Button(parent, "Close", UiMark.Close, position, new Vector2(size, size), color ?? SoftOutline, 2.8f * size / 49f);
            button.onClick.AddListener(clicked);
            button.onClick.AddListener(() => LifeAudio.Play(LifeAudioCue.Ui));
        }

        private InputField InputBox(Transform parent, string name, string value, string placeholder, Vector2 position, Vector2 size, float scale = 1f)
        {
            var rect = ProceduralUiLibrary.WhitePanel(parent, name, position, size, true).rectTransform;
            var input = rect.gameObject.AddComponent<InputField>();
            int textSize = Mathf.Max(12, Mathf.RoundToInt(24f * scale));
            Vector2 center = Vector2.zero;
            input.textComponent = Label(rect, "Input text", "", center, size - new Vector2(24f, 10f) * scale, textSize, Ink, TextAnchor.MiddleLeft);
            input.placeholder = Label(rect, "Placeholder", placeholder, center, size - new Vector2(24f, 10f) * scale, textSize, new Color(.40f, .46f, .39f, .85f), TextAnchor.MiddleLeft);
            input.customCaretColor = true;
            input.caretColor = Ink;
            input.selectionColor = new Color(.40f, .40f, .40f, .20f);
            input.targetGraphic = rect.GetComponent<Graphic>();
            input.transition = Selectable.Transition.None;
            input.lineType = InputField.LineType.SingleLine;
            input.contentType = InputField.ContentType.Standard;
            input.text = value ?? "";
            return input;
        }

        private static RawImage Raw(Transform parent, string name, Vector2 position, Vector2 size)
        {
            var image = Rect(parent, name, position, size).gameObject.AddComponent<RawImage>();
            image.color = Color.white;
            image.raycastTarget = false;
            return image;
        }

        private void HudIcon(Transform parent, string name, string resource, Vector2 position, Vector2 size, Rect uv)
        {
            var icon = Raw(parent, name, position, size);
            icon.texture = Resources.Load<Texture2D>("HudIcons/" + resource);
            icon.color = new Color32(231, 226, 198, 240);
            icon.uvRect = uv;
        }

        private static bool InputFocused()
        {
            GameObject selected = EventSystem.current == null ? null : EventSystem.current.currentSelectedGameObject;
            var input = selected == null ? null : selected.GetComponent<InputField>();
            return input != null && input.isFocused;
        }

        private static void ClearFocus()
        {
            if (EventSystem.current == null) return;
            GameObject selected = EventSystem.current.currentSelectedGameObject;
            var input = selected == null ? null : selected.GetComponent<InputField>();
            if (input != null) input.DeactivateInputField();
            EventSystem.current.SetSelectedGameObject(null);
        }

        private void ReleasePhotoTextures()
        {
            photoCache.Clear();
            expandedTexture = null;
        }

        private void OnEnable()
        {
            if (canvas) canvas.enabled = true;
            if (session) session.RefreshInputBlocking();
            dirty = true;
        }

        private void OnDisable()
        {
            if (gestureInput) gestureInput.Cancel();
            ClearFocus();
            ClearAnimalBubble();
            JournalOpen = false;
            if (session && session.PhotoRig) SetCameraMode(false, true);
            if (canvasRect) RebuildModal();
            if (canvas) canvas.enabled = false;
            if (session) session.RefreshInputBlocking();
        }

        private void OnDestroy()
        {
            if (session != null) session.Changed -= OnChanged;
            if (life != null) life.Changed -= OnChanged;
            ReleasePhotoTextures();
            if (albumCountFont != null) Destroy(albumCountFont);
        }
    }
}
