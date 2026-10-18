using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace WeatherHousePrototype
{
    [DisallowMultipleComponent]
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
        [Tooltip("原始 100×120 字形坐标中的描边宽度；同步作用于气泡、照片和手账里的所有原字形，并随显示尺寸等比缩放。")]
        public float animalGlyphStrokeWidth = 5f;

        [Header("人物对话气泡")]
        [InspectorName("人物气泡尺寸")]
        [Tooltip("输入和已发送的中文气泡共用这个尺寸；输入框、按钮与字体按气泡尺寸成比例排布。")]
        public Vector2 playerBubbleSize = new Vector2(900f, 190f);

        public bool CameraMode { get; private set; }
        public bool JournalOpen { get; private set; }
        public bool BlocksMovement => JournalOpen || dialogueOpen || InputFocused();

        private GameplaySession session;
        private Canvas canvas;
        private RectTransform canvasRect, bubbleRoot, modalRoot, cameraRoot, echoRoot, focusMark;
        private float focusMarkUntil;
        private Text statusLabel, captureLabel, playbackLabel;
        private InputField replyInput, guessInput;
        private Font font;
        private AnimalActor bubbleAnimal;
        private string bubbleUtterance, selectedGlyph, guessDraft = "", replyDraft = "", lastReply;
        private int page, currentFrame;
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

        private void Start()
        {
            ApplyInspectorStyle();
            session = GetComponent<GameplaySession>();
            if (session == null) { Debug.LogError("GameplayHud needs GameplaySession on the same object.", this); enabled = false; return; }
            font = Font.CreateDynamicFontFromOSFont(new[] { "Microsoft YaHei", "微软雅黑", "Arial" }, 22);
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
            var controls = Panel(canvasRect, "Controls", Vector2.zero, new Vector2(718f, 36f), new Color(.14f, .19f, .16f, .62f), false);
            controls.anchorMin = controls.anchorMax = Vector2.zero;
            controls.anchoredPosition = new Vector2(383f, 36f);
            Label(controls, "Controls text", "WASD 移动 · 右键视角 · E 交谈 · C 拍照 · J 手账 · 1 雨  2 晴  3 风", Vector2.zero, new Vector2(698f, 32f), 16, ProceduralUiLibrary.Cream);
            statusLabel = Label(canvasRect, "Status", "", new Vector2(0f, 456f), new Vector2(1250f, 54f), 22, ProceduralUiLibrary.Cream);
            statusLabel.rectTransform.anchorMin = statusLabel.rectTransform.anchorMax = new Vector2(.5f, 1f);
            statusLabel.rectTransform.anchoredPosition = new Vector2(0f, -42f);
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
            if (inspectorDirty) RebuildInspectorPreview();
            statusLabel.text = session.Status ?? "";
            if (captureLabel != null)
                captureLabel.text = session.Recorder != null && session.Recorder.IsRecording
                    ? "记录中  " + Mathf.RoundToInt(session.Recorder.CaptureProgress * 100f) + "%"
                    : session.PhotoRig.FocalLength.ToString("F0") + " mm  ·  " + session.PhotoRig.FocusLabel + "\n滚轮变焦 · 点击对焦 · 空格拍照 · ESC 返回";
            if (focusMark != null) focusMark.gameObject.SetActive(Time.unscaledTime < focusMarkUntil);
            if (Input.GetKeyDown(KeyCode.Escape)) { CloseTop(); return; }
            if (dirty && !InputFocused())
            {
                dirty = false;
                if (JournalOpen && expandedPhoto == null) RebuildModal();
            }
            UpdateAnimalBubble();
            UpdatePlayback();
            if (echoRoot != null) echoRoot.gameObject.SetActive(!JournalOpen && !dialogueOpen && Time.unscaledTime < echoUntil);
            if (InputFocused()) return;
            if (Input.GetKeyDown(KeyCode.J)) { ToggleJournal(); return; }
            if (Input.GetKeyDown(KeyCode.C) && !JournalOpen && !dialogueOpen) SetCameraMode(!CameraMode);
            if (Input.GetKeyDown(KeyCode.E) && !JournalOpen && !CameraMode && session.Observed != null)
            {
                dialogueOpen = !dialogueOpen;
                RebuildModal();
            }
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

        private void LateUpdate() { if (session != null && canvas != null) PositionAnimalBubble(); }

        public void ToggleJournal()
        {
            JournalOpen = !JournalOpen;
            dialogueOpen = false;
            expandedPhoto = null;
            SetCameraMode(false);
            ClearFocus();
            RebuildModal();
        }

        public void SetCameraMode(bool value)
        {
            if (!value && CameraMode && session.Recorder.IsRecording) session.Recorder.CancelCapture();
            session.PhotoRig.SetActive(value);
            session.walker.CameraInputBlocked = value;
            session.walker.JumpBlocked = value;
            CameraMode = value;
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
            if (JournalOpen) JournalOpen = false;
            else if (dialogueOpen) dialogueOpen = false;
            else if (CameraMode) { SetCameraMode(false); return; }
            RebuildModal();
        }

        private void RebuildModal()
        {
            ReleasePhotoTextures();
            playingPhoto = false;
            expandedImage = null;
            playbackLabel = null;
            replyInput = null;
            guessInput = null;
            if (modalRoot != null) { modalRoot.gameObject.SetActive(false); Destroy(modalRoot.gameObject); modalRoot = null; }
            if (!JournalOpen && !dialogueOpen) return;
            modalRoot = Panel(canvasRect, "Modal backdrop", Vector2.zero, new Vector2(4000f, 3000f), new Color(.13f, .19f, .16f, .48f));
            if (expandedPhoto != null) BuildPhoto();
            else if (JournalOpen) BuildJournal();
            else BuildDialogue();
        }

        private void BuildDialogue()
        {
            Vector2 size = FitBubbleSize(playerBubbleSize, new Vector2(300f, 100f));
            float scale = Mathf.Min(size.x / 900f, size.y / 190f);
            var bubble = ProceduralUiLibrary.Create(modalRoot, "Player reply bubble", UiMark.PlayerBubble,
                PlayerBubblePosition(size), size, ProceduralUiLibrary.Grass);
            Label(bubble.transform, "Reply instruction", "用中文回应它", new Vector2(-size.x * .15f, size.y * .27f),
                new Vector2(size.x * .58f, size.y * .18f), Mathf.Max(8, Mathf.RoundToInt(18f * scale)), Ink, TextAnchor.MiddleLeft);
            replyInput = InputBox(bubble.transform, "Chinese reply", replyDraft, "写下你想说的话……",
                new Vector2(-size.x * .09f, -size.y * .015f), new Vector2(size.x * .68f, size.y * .29f), scale);
            replyInput.characterLimit = 240;
            replyInput.onValueChanged.AddListener(value => replyDraft = value);
            TextButton(bubble.transform, "Send reply", "发送", new Vector2(size.x * .355f, -size.y * .015f),
                new Vector2(size.x * .13f, size.y * .29f), SubmitReply, scale);
            CloseButton(bubble.transform, new Vector2(size.x * .425f, size.y * .30f), CloseTop, null, Mathf.Min(size.y * .25f, size.x * .08f));
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
            Label(echoRoot, "Original Chinese reply", lastReply, Vector2.zero, new Vector2(size.x * .8f, size.y * .58f),
                Mathf.Max(8, Mathf.RoundToInt(23f * scale)), Ink);
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
            return new Vector2(0f, canvasRect.rect.yMin + size.y * .5f + 125f);
        }

        private void BuildJournal()
        {
            var book = Panel(modalRoot, "Notebook", Vector2.zero, new Vector2(1380f, 860f), Paper);
            Label(book, "Notebook title", "林间手账", new Vector2(-530f, 370f), new Vector2(240f, 50f), 31, Ink, TextAnchor.MiddleLeft);
            Label(book, "Photos heading", selectedGlyph == null ? "观察照片" : "与这个字有关的照片", new Vector2(-408f, 310f), new Vector2(480f, 34f), 20, Ink, TextAnchor.MiddleLeft);
            TextButton(book, "All photos", "全部照片", new Vector2(-60f, 310f), new Vector2(130f, 36f), () => { selectedGlyph = null; page = 0; RebuildModal(); });
            CloseButton(book, new Vector2(634f, 371f), CloseTop);
            Panel(book, "Page fold", new Vector2(139f, -25f), new Vector2(1.5f, 720f), new Color(.32f, .39f, .29f, .17f), false);
            List<ObservationRecord> photos = selectedGlyph == null ? session.Notebook.observations : session.Notebook.FindEvidence(selectedGlyph);
            int pages = Mathf.Max(1, Mathf.CeilToInt(photos.Count / 4f));
            page = Mathf.Clamp(page, 0, pages - 1);
            for (int i = 0; i < 4; i++)
            {
                int index = page * 4 + i;
                if (index >= photos.Count) break;
                Vector2 position = new Vector2(-468f + (i % 2) * 354f, 163f - (i / 2) * 263f);
                BuildPhotoCard(book, photos[index], position);
            }
            if (photos.Count == 0)
                Label(book, "No photos", selectedGlyph == null ? "把一次相遇拍下来，留在这里。" : "还没有这个字的照片。\n下次见到它时，拍下它所在的场景。", new Vector2(-290f, 20f), new Vector2(650f, 150f), 22, Ink);
            TextButton(book, "Previous photos", "上一页", new Vector2(-500f, -340f), new Vector2(120f, 40f), () => { page = Mathf.Max(0, page - 1); RebuildModal(); });
            Label(book, "Photo page", (page + 1) + " / " + pages, new Vector2(-295f, -340f), new Vector2(130f, 40f), 18, Ink);
            TextButton(book, "Next photos", "下一页", new Vector2(-90f, -340f), new Vector2(120f, 40f), () => { page = Mathf.Min(pages - 1, page + 1); RebuildModal(); });
            Label(book, "Photo hint", "点开照片后，可以手动播放当时的片刻。", new Vector2(-290f, -393f), new Vector2(650f, 30f), 16, Ink);
            BuildWordPage(book);
        }

        private void BuildPhotoCard(Transform parent, ObservationRecord record, Vector2 position)
        {
            var card = Panel(parent, "Photo " + record.id, position, new Vector2(330f, 238f), new Color(.88f, .90f, .82f));
            var image = Raw(card, "Cover frame", new Vector2(0f, 20f), new Vector2(310f, 174.375f));
            try { Texture2D texture = MotionPhotoRecorder.LoadFrame(record, 0); thumbnailTextures.Add(texture); image.texture = texture; }
            catch (Exception exception) { Label(card, "Missing frame", "照片暂时无法读取", new Vector2(0f, 20f), new Vector2(300f, 60f), 17, Ink); Debug.LogWarning(exception.Message, this); }
            GlyphLine(card, record.glyphIds, new Vector2(0f, -91f), 27f, 280f);
            var button = card.gameObject.AddComponent<Button>();
            button.targetGraphic = card.GetComponent<Image>();
            button.onClick.AddListener(() => { expandedPhoto = record; currentFrame = 0; ClearFocus(); RebuildModal(); });
        }

        private void BuildWordPage(Transform book)
        {
            Label(book, "Words heading", "遇见的字", new Vector2(407f, 312f), new Vector2(430f, 36f), 22, Ink);
            GlyphDefinition[] glyphs = session.Catalog.glyphs;
            for (int i = 0; i < glyphs.Length; i++)
            {
                GlyphDefinition glyph = glyphs[i];
                GlyphKnowledge knowledge = session.Notebook.GetWord(glyph.id);
                bool seen = knowledge != null && knowledge.encountered;
                Vector2 position = new Vector2(251f + (i % 4) * 102f, 244f - (i / 4) * 75f);
                var cell = Panel(book, "Word " + glyph.id, position, new Vector2(78f, 67f), selectedGlyph == glyph.id ? new Color(.74f, .81f, .63f) : new Color(.88f, .90f, .82f));
                if (!seen) ProceduralUiLibrary.Create(cell, "Unseen word", UiMark.Dot, Vector2.zero, new Vector2(15f, 15f), new Color(.48f, .54f, .46f, .48f));
                else
                {
                    Glyph(cell, glyph, Vector2.zero, new Vector2(44f, 53f));
                    var button = cell.gameObject.AddComponent<Button>();
                    button.targetGraphic = cell.GetComponent<Image>();
                    button.onClick.AddListener(() =>
                    {
                        selectedGlyph = glyph.id;
                        guessDraft = knowledge.guess ?? "";
                        page = 0;
                        ClearFocus();
                        RebuildModal();
                    });
                    if (knowledge.confirmed) ProceduralUiLibrary.Create(cell, "Confirmed", UiMark.Dot, new Vector2(28f, -23f), new Vector2(8f, 8f), Ink);
                }
            }
            if (selectedGlyph == null)
            {
                Label(book, "Choose word", "点一个遇见过的字，\n看看它出现过的场景。", new Vector2(405f, -273f), new Vector2(430f, 130f), 20, Ink);
                return;
            }
            GlyphKnowledge selected = session.Notebook.GetWord(selectedGlyph);
            if (selected != null && selected.confirmed)
            {
                GlyphDefinition definition = FindGlyph(selectedGlyph);
                Label(book, "Confirmed meaning", "已确认\n" + (definition == null ? "" : definition.meaning), new Vector2(405f, -279f), new Vector2(430f, 160f), 21, Ink);
            }
            else
            {
                Label(book, "Guess heading", "我的猜测", new Vector2(405f, -224f), new Vector2(430f, 34f), 19, Ink, TextAnchor.MiddleLeft);
                guessInput = InputBox(book, "Word guess", guessDraft, "这个字可能是什么意思？", new Vector2(405f, -277f), new Vector2(430f, 54f));
                guessInput.characterLimit = 160;
                guessInput.onValueChanged.AddListener(value => guessDraft = value);
                TextButton(book, "Save guess", "记下来", new Vector2(405f, -341f), new Vector2(144f, 43f), () => { session.SaveGuess(selectedGlyph, guessDraft); ClearFocus(); });
                Label(book, "Guess hint", "猜测可以修改，去下一次相遇中验证。", new Vector2(405f, -393f), new Vector2(450f, 30f), 16, Ink);
            }
        }

        private void BuildPhoto()
        {
            var card = Panel(modalRoot, "Expanded motion photo", Vector2.zero, new Vector2(1240f, 850f), Paper);
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
            Label(bubbleRoot, "Animal hint", animal.displayName + "  ·  E 交谈", new Vector2(0f, -size.y * .5f - 18f), new Vector2(size.x, 34f), 18, ProceduralUiLibrary.Cream);
            if (modalRoot != null) modalRoot.SetAsLastSibling();
        }

        private void PositionAnimalBubble()
        {
            if (bubbleRoot == null || bubbleAnimal == null) return;
            Camera view = session.CurrentView;
            if (view == null) { bubbleRoot.gameObject.SetActive(false); return; }
            Vector3 screen = view.WorldToScreenPoint(bubbleAnimal.BubbleAnchor);
            bool visible = screen.z > 0f && !JournalOpen;
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
            var rect = Panel(parent, name, position, size, ProceduralUiLibrary.Grass);
            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = rect.GetComponent<Image>();
            button.onClick.AddListener(clicked);
            Label(rect, "Label", text, Vector2.zero, size - new Vector2(12f, 6f) * scale, Mathf.Max(8, Mathf.RoundToInt(19f * scale)), Ink);
            return button;
        }

        private static void CloseButton(Transform parent, Vector2 position, UnityEngine.Events.UnityAction clicked, Color? color = null, float size = 49f)
        {
            var button = ProceduralUiLibrary.Button(parent, "Close", UiMark.Close, position, new Vector2(size, size), color ?? Ink, 2.8f * size / 49f);
            button.onClick.AddListener(clicked);
        }

        private InputField InputBox(Transform parent, string name, string value, string placeholder, Vector2 position, Vector2 size, float scale = 1f)
        {
            var rect = Panel(parent, name, position, size, new Color(.99f, .99f, .95f));
            var input = rect.gameObject.AddComponent<InputField>();
            input.textComponent = Label(rect, "Input text", "", Vector2.zero, size - new Vector2(24f, 10f) * scale, Mathf.Max(8, Mathf.RoundToInt(22f * scale)), Ink, TextAnchor.MiddleLeft);
            input.placeholder = Label(rect, "Placeholder", placeholder, Vector2.zero, size - new Vector2(24f, 10f) * scale, Mathf.Max(8, Mathf.RoundToInt(20f * scale)), new Color(.40f, .46f, .39f, .75f), TextAnchor.MiddleLeft);
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

        private void OnDestroy()
        {
            if (session != null) session.Changed -= OnChanged;
            ReleasePhotoTextures();
            if (font != null) Destroy(font);
        }
    }
}
