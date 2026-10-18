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
        public bool CameraMode { get; private set; }
        public bool JournalOpen { get; private set; }
        public bool BlocksMovement => JournalOpen || dialogueOpen || InputFocused();

        private GameplaySession session;
        private Canvas canvas;
        private RectTransform canvasRect, bubbleRoot, modalRoot, cameraRoot, echoRoot;
        private Text statusLabel, captureLabel, playbackLabel;
        private InputField replyInput, guessInput;
        private Font font;
        private AnimalActor bubbleAnimal;
        private string bubbleUtterance, selectedGlyph, guessDraft = "", replyDraft = "", lastReply;
        private int page, currentFrame;
        private bool dialogueOpen, dirty, playingPhoto;
        private float nextFrameAt, echoUntil;
        private ObservationRecord expandedPhoto;
        private RawImage expandedImage;
        private Texture2D expandedTexture;
        private readonly List<Texture2D> thumbnailTextures = new List<Texture2D>();
        private static readonly Color Paper = new Color(.96f, .95f, .88f);
        private static readonly Color Ink = ProceduralUiLibrary.Ink;

        private void Start()
        {
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
            var controls = Panel(canvasRect, "Controls", Vector2.zero, new Vector2(718f, 36f), new Color(.14f, .19f, .16f, .62f), false);
            controls.anchorMin = controls.anchorMax = Vector2.zero;
            controls.anchoredPosition = new Vector2(383f, 36f);
            Label(controls, "Controls text", "WASD 移动 · 右键视角 · E 交谈 · C 拍照 · J 手账 · 1 雨  2 晴  3 风", Vector2.zero, new Vector2(698f, 32f), 16, ProceduralUiLibrary.Cream);
            statusLabel = Label(canvasRect, "Status", "", new Vector2(0f, 456f), new Vector2(1250f, 54f), 22, ProceduralUiLibrary.Cream);
            statusLabel.rectTransform.anchorMin = statusLabel.rectTransform.anchorMax = new Vector2(.5f, 1f);
            statusLabel.rectTransform.anchoredPosition = new Vector2(0f, -42f);
            session.Changed += OnChanged;
            dirty = true;
        }

        private void OnChanged() { dirty = true; }

        private void Update()
        {
            if (session == null || canvas == null) return;
            statusLabel.text = session.Status ?? "";
            if (captureLabel != null)
                captureLabel.text = session.Recorder != null && session.Recorder.IsRecording
                    ? "记录中  " + Mathf.RoundToInt(session.Recorder.CaptureProgress * 100f) + "%"
                    : "空格或点击拍照  ·  ESC 返回";
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
            CameraMode = value;
            if (cameraRoot != null) { cameraRoot.gameObject.SetActive(false); Destroy(cameraRoot.gameObject); cameraRoot = null; }
            if (!value || canvasRect == null) return;
            cameraRoot = Rect(canvasRect, "Camera mode", Vector2.zero, new Vector2(1600f, 1000f));
            ProceduralUiLibrary.Create(cameraRoot, "Viewfinder", UiMark.Viewfinder, new Vector2(0f, 45f), new Vector2(1240f, 720f), ProceduralUiLibrary.Cream, 3.5f);
            ProceduralUiLibrary.Create(cameraRoot, "Camera dot", UiMark.Dot, new Vector2(0f, -334f), new Vector2(9f, 9f), ProceduralUiLibrary.Cream);
            var shutter = ProceduralUiLibrary.Button(cameraRoot, "Shutter", UiMark.Shutter, new Vector2(0f, -395f), new Vector2(88f, 88f), ProceduralUiLibrary.Cream, 4f);
            shutter.onClick.AddListener(() => session.TakePhoto());
            captureLabel = Label(cameraRoot, "Capture hint", "", new Vector2(390f, -404f), new Vector2(500f, 38f), 18, ProceduralUiLibrary.Cream);
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
            var bubble = ProceduralUiLibrary.Create(modalRoot, "Player reply bubble", UiMark.PlayerBubble,
                new Vector2(0f, -280f), new Vector2(970f, 190f), ProceduralUiLibrary.Cream);
            Label(bubble.transform, "Reply instruction", "用中文回应它", new Vector2(-160f, 52f), new Vector2(500f, 32f), 18, Ink, TextAnchor.MiddleLeft);
            replyInput = InputBox(bubble.transform, "Chinese reply", replyDraft, "写下你想说的话……", new Vector2(-80f, -3f), new Vector2(660f, 55f));
            replyInput.characterLimit = 240;
            replyInput.onValueChanged.AddListener(value => replyDraft = value);
            TextButton(bubble.transform, "Send reply", "发送", new Vector2(344f, -3f), new Vector2(125f, 55f), SubmitReply);
            CloseButton(bubble.transform, new Vector2(413f, 58f), CloseTop);
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
            if (echoRoot != null) Destroy(echoRoot.gameObject);
            var bubble = ProceduralUiLibrary.Create(canvasRect, "Player spoken reply", UiMark.PlayerBubble,
                new Vector2(0f, -330f), new Vector2(820f, 144f), ProceduralUiLibrary.Cream);
            echoRoot = bubble.rectTransform;
            Label(echoRoot, "Original Chinese reply", lastReply, Vector2.zero, new Vector2(720f, 90f), 23, Ink);
            echoUntil = Time.unscaledTime + 8f;
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
            currentFrame = 0;
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
            float width = Mathf.Clamp(glyphs.Length * 49f + 90f, 250f, 560f);
            bubbleRoot = Rect(canvasRect, "Animal utterance", Vector2.zero, new Vector2(width, 160f));
            ProceduralUiLibrary.Create(bubbleRoot, "Animal bubble", UiMark.AnimalBubble, Vector2.zero, new Vector2(width, 106f), ProceduralUiLibrary.Cream);
            GlyphLine(bubbleRoot, glyphs, new Vector2(0f, 7f), 43f, width - 70f);
            Label(bubbleRoot, "Animal hint", animal.displayName + "  ·  E 交谈", new Vector2(0f, -70f), new Vector2(width, 34f), 18, ProceduralUiLibrary.Cream);
            if (modalRoot != null) modalRoot.SetAsLastSibling();
        }

        private void PositionAnimalBubble()
        {
            if (bubbleRoot == null || bubbleAnimal == null) return;
            Camera view = session.walker == null ? Camera.main : session.walker.viewCamera;
            if (view == null) { bubbleRoot.gameObject.SetActive(false); return; }
            Vector3 screen = view.WorldToScreenPoint(bubbleAnimal.BubbleAnchor);
            bool visible = screen.z > 0f && !JournalOpen;
            bubbleRoot.gameObject.SetActive(visible);
            if (!visible) return;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screen, null, out Vector2 local);
            local += Vector2.up * 60f;
            float half = bubbleRoot.sizeDelta.x * .5f;
            local.x = Mathf.Clamp(local.x, canvasRect.rect.xMin + half + 16f, canvasRect.rect.xMax - half - 16f);
            local.y = Mathf.Clamp(local.y, canvasRect.rect.yMin + 150f, canvasRect.rect.yMax - 110f);
            bubbleRoot.anchoredPosition = local;
        }

        private void GlyphLine(Transform parent, string[] ids, Vector2 center, float height, float maxWidth)
        {
            if (ids == null || ids.Length == 0) return;
            float step = Mathf.Min(height * .95f, maxWidth / ids.Length);
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

        private static void Glyph(Transform parent, GlyphDefinition glyph, Vector2 position, Vector2 size)
        {
            var rect = Rect(parent, "Glyph " + glyph.id, position, size);
            rect.gameObject.AddComponent<CanvasRenderer>();
            var graphic = rect.gameObject.AddComponent<AnimalGlyphGraphic>();
            graphic.SvgBody = glyph.body;
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
            text.fontSize = fontSize;
            text.text = content;
            text.color = color;
            text.alignment = alignment;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.raycastTarget = false;
            return text;
        }

        private Button TextButton(Transform parent, string name, string text, Vector2 position, Vector2 size, UnityEngine.Events.UnityAction clicked)
        {
            var rect = Panel(parent, name, position, size, ProceduralUiLibrary.Grass);
            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = rect.GetComponent<Image>();
            button.onClick.AddListener(clicked);
            Label(rect, "Label", text, Vector2.zero, size - new Vector2(12f, 6f), 19, Ink);
            return button;
        }

        private static void CloseButton(Transform parent, Vector2 position, UnityEngine.Events.UnityAction clicked, Color? color = null)
        {
            var button = ProceduralUiLibrary.Button(parent, "Close", UiMark.Close, position, new Vector2(49f, 49f), color ?? Ink, 2.8f);
            button.onClick.AddListener(clicked);
        }

        private InputField InputBox(Transform parent, string name, string value, string placeholder, Vector2 position, Vector2 size)
        {
            var rect = Panel(parent, name, position, size, new Color(.99f, .99f, .95f));
            var input = rect.gameObject.AddComponent<InputField>();
            input.textComponent = Label(rect, "Input text", "", Vector2.zero, size - new Vector2(24f, 10f), 22, Ink, TextAnchor.MiddleLeft);
            input.placeholder = Label(rect, "Placeholder", placeholder, Vector2.zero, size - new Vector2(24f, 10f), 20, new Color(.40f, .46f, .39f, .75f), TextAnchor.MiddleLeft);
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
