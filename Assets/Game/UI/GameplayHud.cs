using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

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
        [Tooltip("1600×1000 参考画布中的宽、高；与人物气泡独立。过大时会按屏幕可用空间等比缩小。")]
        public Vector2 animalBubbleSize = new Vector2(380f, 110f);
        [InspectorName("动物字语高度"), Range(16f, 120f)]
        [Tooltip("参考画布中的字形高度。气泡变小时，整句字语会继续等比缩小，避免挤出边缘。")]
        public float animalGlyphHeight = 43f;
        [InspectorName("字语笔画粗细"), Range(.5f, 14f)]
        [Tooltip("原始 100×120 字形坐标中的描边宽度；作用于气泡和照片详情。书页字符按设计稿使用独立的描边比例。")]
        public float animalGlyphStrokeWidth = 5f;

        [Header("人物对话气泡")]
        [InspectorName("人物气泡尺寸")]
        [Tooltip("输入和已发送的中文气泡共用这个尺寸；文字按气泡尺寸成比例排布，回车发送，Esc 收起。")]
        public Vector2 playerBubbleSize = new Vector2(900f, 190f);

        [Header("相册符号页")]
        [InspectorName("使用 P1 示例照片与完整字形")]
        [Tooltip("用于设计稿对照；只影响符号页展示，不解锁字库或写入存档。相片页签始终显示实际拍摄记录。")]
        public bool useAlbumDesignSamples = true;

        public bool CameraMode { get; private set; }
        public AnimalActor DialogueTarget => dialogueOpen ? dialogueTarget : null;
        public bool IsDialogueOpen => dialogueOpen;
        private AnimalActor dialogueTarget;
        public bool JournalOpen { get; private set; }
        public bool IsAlbumOpening => albumOpening != null && albumOpening.IsPlaying;
        public bool BlocksMovement => JournalOpen || dialogueOpen || InputFocused();

        private GameplaySession session;
        private Canvas canvas;
        private RectTransform canvasRect, bubbleRoot, modalRoot, cameraRoot, echoRoot, focusMark;
        private float focusMarkUntil;
        private Text statusLabel, captureLabel, playbackLabel;
        private RectTransform statusRoot;
        private BreathingTextGlow statusGlow;
        private string displayedStatus;
        private float statusUntil;
        private int lastCompositionFrame = -10;
        private InputField replyInput, guessInput;
        private Font font, albumCountFont;
        private RectTransform albumRoot;
        private AlbumBookOpening albumOpening;
        private AnimalActor bubbleAnimal;
        private string bubbleUtterance, selectedGlyph, guessGlyphId, guessDraft = "", replyDraft = "", lastReply;
        private int page, currentFrame;
        private int glyphPage;
        private bool albumPhotosSelected, glyphDetailOpen;
        private bool dialogueOpen, dirty, playingPhoto, inspectorDirty;
        private float nextFrameAt, echoUntil;
        private ObservationRecord expandedPhoto;
        private RawImage expandedImage;
        private Texture2D expandedTexture;
        private readonly List<Texture2D> thumbnailTextures = new List<Texture2D>();
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
            var controls = Rect(canvasRect, "Controls", Vector2.zero, new Vector2(560f, 64f));
            controls.anchorMin = controls.anchorMax = Vector2.zero;
            controls.anchoredPosition = new Vector2(304f, 48f);
            Color hint = new Color(.91f, .93f, .82f, .88f);
            HudIcon(controls, "Movement keyboard", "Keyboard", new Vector2(-236f, 0f), new Vector2(76f, 52f), new Rect(.02f, .18f, .94f, .66f));
            Label(controls, "Movement label", "移动", new Vector2(-166f, -4f), new Vector2(60f, 32f), 21, hint);
            HudIcon(controls, "Photo icon", "Camera", new Vector2(-80f, 0f), new Vector2(44f, 34f), new Rect(.12f, .20f, .74f, .60f));
            Label(controls, "Photo shortcut", "C 拍照", new Vector2(-14f, -4f), new Vector2(90f, 32f), 21, hint);
            HudIcon(controls, "Journal icon", "Journal", new Vector2(80f, 0f), new Vector2(34f, 44f), new Rect(.18f, .08f, .63f, .82f));
            Label(controls, "Journal shortcut", "J 手账", new Vector2(146f, -4f), new Vector2(90f, 32f), 21, hint);
            statusRoot = Rect(canvasRect, "Bottom status", Vector2.zero, new Vector2(880f, 72f));
            statusRoot.anchorMin = statusRoot.anchorMax = new Vector2(.5f, .2f);
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
            string text = session.Judging ? "动物在听你说……" : hasMessage ? displayedStatus : "";
            if (!session.Judging && !dialogueOpen && !CameraMode && session.Observed != null && !hasMessage)
                text = session.Observed.displayName + "   ·   E 交谈";
            bool echoVisible = echoRoot != null && Time.unscaledTime < echoUntil && !hasMessage && !session.Judging;
            statusRoot.gameObject.SetActive(!JournalOpen && !dialogueOpen && !echoVisible && !string.IsNullOrEmpty(text));
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
            statusGlow.Intensity = Mathf.Lerp(.18f, .50f, breath);
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
            bool restoreReplyFocus = replyInput != null && replyInput.isFocused;
            bool restoreGuessFocus = guessInput != null && guessInput.isFocused;
            if (replyInput != null) replyDraft = replyInput.text;
            if (guessInput != null) guessDraft = guessInput.text;
            ClearFocus();
            ReleasePhotoTextures();
            foreach (Transform child in canvasRect)
            {
                child.gameObject.SetActive(false);
                Destroy(child.gameObject);
            }
            bubbleRoot = modalRoot = cameraRoot = echoRoot = null;
            captureLabel = playbackLabel = null;
            bubbleAnimal = null;
            bubbleUtterance = null;
            BuildChrome();
            SetCameraMode(CameraMode);
            UpdateAnimalBubble();
            RebuildModal();
            if (!string.IsNullOrEmpty(lastReply) && Time.unscaledTime < echoUntil) BuildReplyEcho();
            if (restoreReplyFocus && replyInput != null) replyInput.ActivateInputField();
            if (restoreGuessFocus && guessInput != null) guessInput.ActivateInputField();
            dirty = false;
        }

        private void OnChanged() { dirty = true; }

        private void Update()
        {
            if (session == null || canvas == null) return;
            if (inspectorDirty && !IsAlbumOpening) RebuildInspectorPreview();
            if (!string.IsNullOrEmpty(Input.compositionString)) lastCompositionFrame = Time.frameCount;
            UpdateStatus();
            if (captureLabel != null)
                captureLabel.text = session.Recorder != null && session.Recorder.IsRecording
                    ? "记录中  " + Mathf.RoundToInt(session.Recorder.CaptureProgress * 100f) + "%"
                    : session.PhotoRig.FocalLength.ToString("F0") + " mm  ·  " + session.PhotoRig.FocusLabel + "\n滚轮变焦 · 点击对焦 · 空格拍照 · Esc 关闭";
            if (focusMark != null) focusMark.gameObject.SetActive(Time.unscaledTime < focusMarkUntil);
            if (Input.GetKeyDown(KeyCode.Escape)) { CloseTop(); return; }
            if (dirty && !InputFocused() && !IsAlbumOpening)
            {
                dirty = false;
                if (JournalOpen && expandedPhoto == null) RebuildModal();
            }
            UpdateAnimalBubble();
            UpdatePlayback();
            if (echoRoot != null) echoRoot.gameObject.SetActive(!JournalOpen && !dialogueOpen && !statusRoot.gameObject.activeSelf && Time.unscaledTime < echoUntil);
            if (InputFocused()) return;
            if (Input.GetKeyDown(KeyCode.J)) { ToggleJournal(); return; }
            if (IsAlbumOpening) return;
            if (JournalOpen && expandedPhoto == null && !glyphDetailOpen)
            {
                if (Input.GetKeyDown(KeyCode.RightArrow)) { TurnJournalPage(1); return; }
                if (Input.GetKeyDown(KeyCode.LeftArrow)) { TurnJournalPage(-1); return; }
            }
            if (Input.GetKeyDown(KeyCode.C) && !JournalOpen && !dialogueOpen) SetCameraMode(!CameraMode);
            if (Input.GetKeyDown(KeyCode.E) && !JournalOpen && !CameraMode && session.Observed != null)
                ToggleDialogue();
            if (JournalOpen || dialogueOpen) return;
            if (CameraMode && Input.GetMouseButtonDown(0) && (EventSystem.current == null || !EventSystem.current.IsPointerOverGameObject()))
            {
                session.PhotoRig.FocusAt(Input.mousePosition);
                RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, Input.mousePosition, null, out Vector2 focusPosition);
                focusMark.anchoredPosition = focusPosition;
                focusMarkUntil = Time.unscaledTime + 1.5f;
            }
            if (CameraMode && Input.GetKeyDown(KeyCode.Space)) session.TakePhoto();
            if (Input.GetKeyDown(KeyCode.Alpha1)) session.Cast(LocalWeather.Rain);
            if (Input.GetKeyDown(KeyCode.Alpha2)) session.Cast(LocalWeather.Sun);
            if (Input.GetKeyDown(KeyCode.Alpha3)) session.Cast(LocalWeather.Wind);
        }

        private void LateUpdate()
        {
            if (session == null || canvas == null) return;
            PositionAnimalBubble();
            FitAlbum();
        }

        private void FitAlbum()
        {
            if (albumRoot == null) return;
            float scale = Mathf.Min(canvasRect.rect.width / 1672f, canvasRect.rect.height / 941f);
            albumRoot.localScale = Vector3.one * scale;
        }

        public void ToggleJournal()
        {
            if (JournalOpen)
            {
                if (albumOpening != null && albumOpening.IsClosing) albumOpening.Resume();
                else CloseJournal();
                return;
            }
            JournalOpen = true;
            dialogueOpen = false;
            expandedPhoto = null;
            glyphDetailOpen = false;
            SetCameraMode(false);
            ClearFocus();
            RebuildModal();
            dirty = false;
            if (albumOpening != null) albumOpening.Play();
            session.RefreshInputBlocking();
        }

        public void ToggleDialogue()
        {
            if (JournalOpen || CameraMode || session == null || session.Observed == null || !session.Observed.IsAvailableForObservation) return;
            dialogueOpen = !dialogueOpen;
            dialogueTarget = dialogueOpen ? session.Observed : null;
            RebuildModal();
            session.RefreshInputBlocking();
        }

        public void CloseDialogue()
        {
            dialogueOpen = false; dialogueTarget = null; ClearFocus();
            if (canvasRect) RebuildModal();
            if (session) session.RefreshInputBlocking();
        }

        private void CloseJournal()
        {
            if (albumOpening != null && albumOpening.IsClosing) return;
            ClearFocus();
            // Details replace or cover the book; return to its current page before folding it away.
            if (expandedPhoto != null || glyphDetailOpen)
            {
                expandedPhoto = null;
                glyphDetailOpen = false;
                RebuildModal();
            }
            if (albumOpening != null) albumOpening.PlayReverse(CompleteJournalClose);
            else CompleteJournalClose();
        }

        private void CompleteJournalClose()
        {
            JournalOpen = false;
            RebuildModal();
            dirty = false;
            session.RefreshInputBlocking();
        }

        public void SetCameraMode(bool value)
        {
            if (!value && CameraMode && session.Recorder.IsRecording) session.Recorder.CancelCapture();
            session.PhotoRig.SetActive(value);
            CameraMode = value;
            session.RefreshInputBlocking();
            if (cameraRoot != null) { cameraRoot.gameObject.SetActive(false); Destroy(cameraRoot.gameObject); cameraRoot = null; }
            if (!value || canvasRect == null) return;
            cameraRoot = Rect(canvasRect, "Camera mode", Vector2.zero, new Vector2(1600f, 1000f));
            ProceduralUiLibrary.Create(cameraRoot, "Viewfinder", UiMark.Viewfinder, new Vector2(0f, 45f), new Vector2(1240f, 720f), ProceduralUiLibrary.Cream, 3.5f);
            ProceduralUiLibrary.Create(cameraRoot, "Camera dot", UiMark.Dot, new Vector2(0f, -334f), new Vector2(9f, 9f), ProceduralUiLibrary.Cream);
            var shutter = ProceduralUiLibrary.Button(cameraRoot, "Shutter", UiMark.Shutter, new Vector2(0f, -395f), new Vector2(88f, 88f), ProceduralUiLibrary.Cream, 4f);
            shutter.onClick.AddListener(() => session.TakePhoto());
            captureLabel = Label(cameraRoot, "Capture hint", "", new Vector2(390f, -404f), new Vector2(530f, 76f), 18, ProceduralUiLibrary.Cream);
            focusMark = ProceduralUiLibrary.Create(cameraRoot, "Focus indicator", UiMark.Viewfinder, Vector2.zero, new Vector2(60f, 60f), ProceduralUiLibrary.Cream, 2f).rectTransform;
            focusMark.gameObject.SetActive(false);
            CloseButton(cameraRoot, new Vector2(696f, 406f), () => SetCameraMode(false), ProceduralUiLibrary.Cream);
        }

        private void CloseTop()
        {
            ClearFocus();
            if (expandedPhoto != null) { expandedPhoto = null; RebuildModal(); return; }
            if (glyphDetailOpen) { glyphDetailOpen = false; RebuildModal(); return; }
            if (JournalOpen) { CloseJournal(); return; }
            else if (dialogueOpen) dialogueOpen = false;
            else if (CameraMode) { SetCameraMode(false); return; }
            RebuildModal();
            session.RefreshInputBlocking();
        }

        private void RebuildModal()
        {
            canvasRect.Find("Controls").gameObject.SetActive(!JournalOpen);
            statusRoot.gameObject.SetActive(!JournalOpen);
            ReleasePhotoTextures();
            playingPhoto = false;
            expandedImage = null;
            playbackLabel = null;
            replyInput = null;
            guessInput = null;
            albumRoot = null;
            albumOpening = null;
            if (modalRoot != null) { modalRoot.gameObject.SetActive(false); Destroy(modalRoot.gameObject); modalRoot = null; }
            if (!JournalOpen && !dialogueOpen) return;
            modalRoot = Panel(canvasRect, "Modal backdrop", Vector2.zero, new Vector2(4000f, 3000f), new Color(.13f, .19f, .16f, .48f));
            if (expandedPhoto != null) BuildPhoto();
            else if (JournalOpen) BuildJournal();
            else BuildDialogue();
            if (JournalOpen && expandedPhoto == null && glyphDetailOpen) BuildGlyphDetail();
        }

        private void BuildDialogue()
        {
            Vector2 size = FitBubbleSize(playerBubbleSize, new Vector2(300f, 100f));
            float scale = Mathf.Min(size.x / 900f, size.y / 190f);
            var bubble = ProceduralUiLibrary.Create(modalRoot, "Player reply bubble", UiMark.PlayerBubble,
                PlayerBubblePosition(size), size, ProceduralUiLibrary.Grass);
            replyInput = InputBox(bubble.transform, "Chinese reply", replyDraft, "写下你想说的话，Enter 发送…",
                Vector2.zero, new Vector2(size.x * .82f, size.y * .68f), scale, true);
            replyInput.lineType = InputField.LineType.SingleLine;
            replyInput.characterLimit = 240;
            replyInput.onValueChanged.AddListener(value => { replyDraft = value; FitReplyInput(bubble.rectTransform, size); });
            FitReplyInput(bubble.rectTransform, size);
            replyInput.onSubmit.AddListener(_ =>
            {
                // Enter used to commit an IME candidate must not send the reply as well.
                if (!string.IsNullOrEmpty(Input.compositionString) || Time.frameCount <= lastCompositionFrame + 1)
                { replyInput.ActivateInputField(); return; }
                SubmitReply();
                if (dialogueOpen && replyInput != null) replyInput.ActivateInputField();
            });
            replyInput.ActivateInputField();
        }

        private void SubmitReply()
        {
            if (session.Judging || string.IsNullOrWhiteSpace(replyDraft)) return;
            lastReply = replyDraft;
            session.Reply(replyDraft);
            dialogueOpen = false;
            ClearFocus();
            RebuildModal();
            session.RefreshInputBlocking();
            echoUntil = Time.unscaledTime + 8f;
            BuildReplyEcho();
        }

        private void BuildReplyEcho()
        {
            if (echoRoot != null) Destroy(echoRoot.gameObject);
            Vector2 size = FitBubbleSize(playerBubbleSize, new Vector2(300f, 100f));
            float scale = Mathf.Min(size.x / 900f, size.y / 190f);
            var bubble = ProceduralUiLibrary.Create(canvasRect, "Player spoken reply", UiMark.PlayerBubble,
                PlayerBubblePosition(size), size, ProceduralUiLibrary.Grass);
            echoRoot = bubble.rectTransform;
            var viewport = Rect(echoRoot, "Reply viewport", Vector2.zero, new Vector2(size.x - 64f, size.y * .68f));
            viewport.gameObject.AddComponent<RectMask2D>();
            var text = Label(viewport, "Original Chinese reply", lastReply, Vector2.zero, viewport.sizeDelta,
                Mathf.RoundToInt(Mathf.Max(12, Mathf.RoundToInt(42f * scale)) * 1.3f), Ink);
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            float width = Mathf.Clamp(text.preferredWidth + 64f, size.x, canvasRect.rect.width - 48f);
            echoRoot.sizeDelta = new Vector2(width, size.y);
            viewport.sizeDelta = new Vector2(width - 64f, size.y * .68f);
            text.rectTransform.sizeDelta = new Vector2(Mathf.Max(text.preferredWidth, viewport.sizeDelta.x), viewport.sizeDelta.y);
            if (text.preferredWidth > viewport.sizeDelta.x)
            {
                var scroll = viewport.gameObject.AddComponent<ScrollRect>();
                viewport.gameObject.AddComponent<Image>().color = Color.clear;
                scroll.viewport = viewport; scroll.content = text.rectTransform; scroll.vertical = false;
                scroll.movementType = ScrollRect.MovementType.Clamped;
                text.rectTransform.pivot = new Vector2(0f, .5f);
                text.rectTransform.anchorMin = text.rectTransform.anchorMax = new Vector2(0f, .5f);
                text.rectTransform.anchoredPosition = Vector2.zero;
            }
            if (modalRoot != null) modalRoot.SetAsLastSibling();
        }

        private Vector2 FitBubbleSize(Vector2 requested, Vector2 minimum)
        {
            Vector2 size = new Vector2(Mathf.Max(minimum.x, requested.x), Mathf.Max(minimum.y, requested.y));
            float fit = Mathf.Min(1f, (canvasRect.rect.width - 48f) / size.x, canvasRect.rect.height * .65f / size.y);
            return size * Mathf.Max(.1f, fit);
        }

        private Vector2 PlayerBubblePosition(Vector2 size)
        {
            return new Vector2(0f, canvasRect.rect.yMin + canvasRect.rect.height * .2f);
        }

        private void FitReplyInput(RectTransform bubble, Vector2 minimum)
        {
            var text = replyInput.textComponent;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            var placeholder = (Text)replyInput.placeholder;
            placeholder.horizontalOverflow = HorizontalWrapMode.Overflow;
            var settings = text.GetGenerationSettings(Vector2.zero);
            string content = string.IsNullOrEmpty(replyDraft) ? placeholder.text : replyDraft;
            float preferred = text.cachedTextGeneratorForLayout.GetPreferredWidth(content, settings) / text.pixelsPerUnit;
            float width = Mathf.Clamp(preferred + 64f, minimum.x, canvasRect.rect.width - 48f);
            bubble.sizeDelta = new Vector2(width, minimum.y);
            var inputRect = (RectTransform)replyInput.transform;
            inputRect.sizeDelta = new Vector2(width - 64f, minimum.y * .68f);
            text.rectTransform.sizeDelta = placeholder.rectTransform.sizeDelta = inputRect.sizeDelta;
        }

        private void BuildJournal()
        {
            albumRoot = AlbumBookView.Create(modalRoot, albumPhotosSelected,
                () => { albumPhotosSelected = false; glyphDetailOpen = false; ClearFocus(); RebuildModal(); },
                () => { albumPhotosSelected = true; selectedGlyph = null; glyphDetailOpen = false; ClearFocus(); RebuildModal(); },
                CloseTop, () => TurnGlyphPage(1));
            bool samples = useAlbumDesignSamples && !albumPhotosSelected;
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
            FitAlbum();
            albumOpening = albumRoot.gameObject.AddComponent<AlbumBookOpening>();
        }

        private void TurnGlyphPage(int direction)
        {
            if (IsAlbumOpening) return;
            int pages = Mathf.Max(1, Mathf.CeilToInt(session.Catalog.glyphs.Length / 22f));
            glyphPage = (glyphPage + direction + pages) % pages;
            ClearFocus();
            RebuildModal();
        }

        private void TurnJournalPage(int direction)
        {
            if (!albumPhotosSelected) { TurnGlyphPage(direction); return; }
            page += direction;
            ClearFocus();
            RebuildModal();
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
            try { texture = MotionPhotoRecorder.LoadFrame(record, 0); thumbnailTextures.Add(texture); }
            catch (Exception exception) { Debug.LogWarning(exception.Message, this); }
            var image = AlbumBookView.Photo(parent, slot, texture, "Photo " + record.id,
                () => { expandedPhoto = record; glyphDetailOpen = false; currentFrame = 0; ClearFocus(); RebuildModal(); });
            if (texture == null)
                Label(image.transform, "Missing frame", "照片暂时无法读取", new Vector2(0f, -image.rectTransform.rect.height * .35f), new Vector2(image.rectTransform.rect.width - 12f, 46f), 17, Ink);
        }

        private void BuildWordPage(Transform book)
        {
            GlyphDefinition[] glyphs = session.Catalog.glyphs;
            glyphPage = Mathf.Clamp(glyphPage, 0, Mathf.Max(0, (glyphs.Length - 1) / 22));
            int first = glyphPage * 22;
            for (int i = first; i < Mathf.Min(first + 22, glyphs.Length); i++)
            {
                GlyphDefinition glyph = glyphs[i];
                GlyphKnowledge knowledge = session.Notebook.GetWord(glyph.id);
                bool seen = knowledge != null && knowledge.encountered;
                bool visible = seen || (useAlbumDesignSamples && !albumPhotosSelected);
                AlbumBookView.Word(book, i - first, glyph, visible, selectedGlyph == glyph.id, () =>
                {
                    if (guessGlyphId != glyph.id) guessDraft = knowledge?.guess ?? "";
                    guessGlyphId = glyph.id;
                    selectedGlyph = glyph.id;
                    glyphDetailOpen = true;
                    ClearFocus();
                    RebuildModal();
                });
            }
        }

        private void BuildGlyphDetail()
        {
            var shade = Panel(modalRoot, "Word detail backdrop", Vector2.zero, new Vector2(4000f, 3000f), new Color(.13f, .19f, .16f, .38f));
            var card = PaperPanel(shade, "Word detail", Vector2.zero, new Vector2(620f, 400f), Paper);
            CloseButton(card, new Vector2(263f, 153f), CloseTop);
            GlyphDefinition definition = FindGlyph(selectedGlyph);
            if (definition != null) Glyph(card, definition, new Vector2(0f, 113f), new Vector2(50f, 60f));
            GlyphKnowledge selected = session.Notebook.GetWord(selectedGlyph);
            if (selected == null || !selected.encountered)
            {
                Label(card, "Unseen meaning", "还没有在相遇中记录这个字。\n听听动物的字语，再留下你的猜测。", new Vector2(0f, -35f), new Vector2(500f, 100f), 21, Ink);
                return;
            }
            if (selected.confirmed)
                Label(card, "Confirmed meaning", "已确认\n" + (definition == null ? "" : definition.meaning), new Vector2(0f, 5f), new Vector2(500f, 100f), 21, Ink);
            else
            {
                Label(card, "Guess heading", "我的猜测", new Vector2(0f, 40f), new Vector2(480f, 34f), 19, Ink, TextAnchor.MiddleLeft);
                guessInput = InputBox(card, "Word guess", guessDraft, "这个字可能是什么意思？", new Vector2(0f, -15f), new Vector2(480f, 54f));
                guessInput.characterLimit = 160;
                guessInput.onValueChanged.AddListener(value => guessDraft = value);
                TextButton(card, "Save guess", "记下来", new Vector2(0f, -84f), new Vector2(144f, 43f), () => { session.SaveGuess(selectedGlyph, guessDraft); ClearFocus(); });
            }
            TextButton(card, "Word evidence", "查看相关照片", new Vector2(0f, -151f), new Vector2(190f, 38f), () =>
            { albumPhotosSelected = true; glyphDetailOpen = false; page = 0; ClearFocus(); RebuildModal(); });
        }

        private void BuildPhoto()
        {
            var card = PaperPanel(modalRoot, "Expanded motion photo", Vector2.zero, new Vector2(1240f, 850f), Paper);
            Label(card, "Photo title", "观察记录", new Vector2(-470f, 378f), new Vector2(260f, 40f), 25, Ink, TextAnchor.MiddleLeft);
            CloseButton(card, new Vector2(559f, 380f), CloseTop);
            expandedImage = Raw(card, "Motion photo frame", new Vector2(0f, 38f), new Vector2(1024f, 576f));
            currentFrame = Mathf.Clamp(currentFrame, 0, Mathf.Max(0, expandedPhoto.frameCount - 1));
            LoadExpandedFrame();
            GlyphLine(card, expandedPhoto.glyphIds, new Vector2(0f, -293f), 45f, 950f);
            var play = TextButton(card, "Play motion photo", "播放这段片刻", new Vector2(0f, -372f), new Vector2(220f, 48f), () =>
            {
                playingPhoto = !playingPhoto;
                if (playingPhoto && currentFrame >= expandedPhoto.frameCount - 1) { currentFrame = 0; LoadExpandedFrame(); }
                nextFrameAt = Time.unscaledTime + 1f / Mathf.Max(1f, expandedPhoto.framesPerSecond);
                UpdatePlaybackLabel();
            });
            playbackLabel = play.GetComponentInChildren<Text>();
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
            if (playbackLabel != null) playbackLabel.text = playingPhoto ? "暂停  " + (currentFrame + 1) + " / " + expandedPhoto.frameCount : "播放这段片刻";
        }

        private void LoadExpandedFrame()
        {
            if (expandedTexture != null) { Destroy(expandedTexture); expandedTexture = null; }
            expandedImage.texture = null;
            try { expandedTexture = MotionPhotoRecorder.LoadFrame(expandedPhoto, currentFrame); expandedImage.texture = expandedTexture; }
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
            string utterance = animal == null ? "" : animal.UtteranceId;
            if (animal == bubbleAnimal && utterance == bubbleUtterance) return;
            bubbleAnimal = animal;
            bubbleUtterance = utterance;
            if (bubbleRoot != null) { Destroy(bubbleRoot.gameObject); bubbleRoot = null; }
            if (animal == null) return;
            string[] glyphs = animal.CurrentGlyphIds ?? Array.Empty<string>();
            Vector2 size = FitBubbleSize(animalBubbleSize, new Vector2(120f, 64f));
            bubbleRoot = Rect(canvasRect, "Animal utterance", Vector2.zero, size);
            ProceduralUiLibrary.Create(bubbleRoot, "Animal bubble", UiMark.AnimalBubble, Vector2.zero, size, ProceduralUiLibrary.Cream);
            GlyphLine(bubbleRoot, glyphs, new Vector2(0f, size.y * ProceduralUiStyle.Current.tailSize * .5f),
                Mathf.Min(animalGlyphHeight, size.y * .48f), size.x * .78f);
            if (modalRoot != null) modalRoot.SetAsLastSibling();
        }

        private void PositionAnimalBubble()
        {
            if (bubbleRoot == null || bubbleAnimal == null) return;
            Camera view = session.CurrentView;
            if (view == null) { bubbleRoot.gameObject.SetActive(false); return; }
            Vector3 screen = view.WorldToScreenPoint(bubbleAnimal.BubbleAnchor);
            // Social's LateUpdate has already placed its world conversation; avoid a second bubble over it.
            bool visible = screen.z > 0f && !JournalOpen && bubbleAnimal.IsAvailableForObservation && !bubbleAnimal.HasAmbientSpeech;
            bubbleRoot.gameObject.SetActive(visible);
            if (!visible) return;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screen, null, out Vector2 local);
            local += Vector2.up * (bubbleRoot.sizeDelta.y * .5f + 34f);
            float half = bubbleRoot.sizeDelta.x * .5f;
            local.x = Mathf.Clamp(local.x, canvasRect.rect.xMin + half + 16f, canvasRect.rect.xMax - half - 16f);
            local.y = Mathf.Clamp(local.y, canvasRect.rect.yMin + bubbleRoot.sizeDelta.y * .5f + 42f,
                canvasRect.rect.yMax - bubbleRoot.sizeDelta.y * .5f - 24f);
            bubbleRoot.anchoredPosition = local;
        }

        private void GlyphLine(Transform parent, string[] ids, Vector2 center, float height, float maxWidth)
        {
            if (ids == null || ids.Length == 0) return;
            height = Mathf.Min(height, maxWidth / (ids.Length * .95f));
            float step = height * .95f;
            for (int i = 0; i < ids.Length; i++)
            {
                GlyphDefinition definition = FindGlyph(ids[i]);
                if (definition != null) Glyph(parent, definition, center + Vector2.right * ((i - (ids.Length - 1) * .5f) * step), new Vector2(height * 5f / 6f, height));
            }
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

        private Button TextButton(Transform parent, string name, string text, Vector2 position, Vector2 size, UnityEngine.Events.UnityAction clicked, float scale = 1f)
        {
            var rect = PaperPanel(parent, name, position, size, ProceduralUiLibrary.Grass);
            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = rect.GetComponent<Graphic>();
            var colors = button.colors;
            colors.highlightedColor = new Color(1f, .99f, .91f);
            colors.pressedColor = new Color(.80f, .85f, .70f);
            button.colors = colors;
            button.onClick.AddListener(clicked);
            Label(rect, "Label", text, Vector2.zero, size - new Vector2(12f, 6f) * scale, Mathf.Max(8, Mathf.RoundToInt(19f * scale)), Ink);
            return button;
        }

        private static void CloseButton(Transform parent, Vector2 position, UnityEngine.Events.UnityAction clicked, Color? color = null, float size = 49f)
        {
            var button = ProceduralUiLibrary.Button(parent, "Close", UiMark.Close, position, new Vector2(size, size), color ?? SoftOutline, 2.8f * size / 49f);
            button.onClick.AddListener(clicked);
        }

        private InputField InputBox(Transform parent, string name, string value, string placeholder, Vector2 position, Vector2 size, float scale = 1f, bool reply = false)
        {
            var rect = Panel(parent, name, position, size, reply ? Color.clear : new Color(.90f, .92f, .81f));
            var input = rect.gameObject.AddComponent<InputField>();
            int textSize = Mathf.Max(12, Mathf.RoundToInt((reply ? 46f : 24f) * scale));
            if (reply) textSize = Mathf.RoundToInt(textSize * 1.3f);
            input.textComponent = Label(rect, "Input text", "", Vector2.zero, size - new Vector2(24f, 10f) * scale, textSize, Ink, TextAnchor.MiddleLeft);
            input.placeholder = Label(rect, "Placeholder", placeholder, Vector2.zero, size - new Vector2(24f, 10f) * scale, textSize, new Color(.40f, .46f, .39f, .85f), TextAnchor.MiddleLeft);
            input.customCaretColor = true;
            input.caretColor = Ink;
            input.selectionColor = new Color(.55f, .65f, .43f, .35f);
            input.targetGraphic = rect.GetComponent<Image>();
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

        private static void HudIcon(Transform parent, string name, string resource, Vector2 position, Vector2 size, Rect uv)
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
            foreach (Texture2D texture in thumbnailTextures) if (texture != null) Destroy(texture);
            thumbnailTextures.Clear();
            if (expandedTexture != null) Destroy(expandedTexture);
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
            ClearFocus();
            JournalOpen = dialogueOpen = false; dialogueTarget = null;
            if (session && session.PhotoRig) SetCameraMode(false);
            if (canvasRect) RebuildModal();
            if (canvas) canvas.enabled = false;
            if (session) session.RefreshInputBlocking();
        }

        private void OnDestroy()
        {
            if (session != null) session.Changed -= OnChanged;
            ReleasePhotoTextures();
            if (albumCountFont != null) Destroy(albumCountFont);
        }
    }
}
