using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace WeatherHouse.Life
{
    // Paper content always follows the actual skinned page. The invisible UGUI retains pointer
    // targets; the authored page keeps the same XZ projection while its height forms the curve.
    [DisallowMultipleComponent, DefaultExecutionOrder(400)]
    public sealed class LifeJournal3D : MonoBehaviour
    {
        private const int CaptureLayer = 31, BookLayer = 30;
        // Original artwork Rect(211,108,1228,742), then the authored paper UV crops.
        private static readonly Rect LeftPage = new Rect(242.928f / 1672f, 114.744f / 941f, 579.616f / 1672f, 698.964f / 941f);
        private static readonly Rect RightPage = new Rect(853.244f / 1672f, 114.744f / 941f, 557.512f / 1672f, 698.964f / 941f);
        private GameObject studio, model;
        private Transform presentation;
        private Camera view, captureCamera;
        private Canvas captureCanvas;
        private RenderTexture display, before, after;
        private readonly Vector4[] beforePhotos = new Vector4[8], afterPhotos = new Vector4[8];
        private Renderer pageLeft, pageRight, turningPage;
        private AnimationClip openClip, turnClip;
        private RectTransform book;
        private RawImage image, artwork;
        private CanvasGroup input;
        private readonly Dictionary<Renderer, Material[]> runtime = new Dictionary<Renderer, Material[]>();
        private readonly Dictionary<Material, Material> surfaces = new Dictionary<Material, Material>();
        private readonly Dictionary<CanvasGroup, float> content = new Dictionary<CanvasGroup, float>();
        private struct ContentLayout { public Vector2 position; public Vector3 scale; }
        private readonly Dictionary<RectTransform, ContentLayout> layouts = new Dictionary<RectTransform, ContentLayout>();
        private enum Motion { None, Opening, Closing, Turning }
        private Motion motion;
        private float elapsed;
        private float lastRawStep, lastStep, maximumRawStep;
        private int presentationFrames;
        private Action onClosed;
        private bool openingFanPending;
        private bool openingTurn, openingSequence, openingTargetRevealed;
        private LifeJournalOpeningFan openingFan;
        private bool prepared, drawingFrame, reverseTurn, closeAfterTurn;
        private bool manualTurn, cancellingTurn;
        private Action restoreDraggedPage;
        private bool animatedContent;
        private bool wordFeedback, contentDirty;
        private float nextContentFrame;
        private Vector3 lastPointer;
        private Transform[] captureTransforms;
        private int[] captureLayers;
        private Selectable[] captureControls;
        private Color[] captureTints;
        private float openness = 1f;
        private const float PaperHeight = .0323f, ReadingAngle = 70f, ClosedAngle = 58f;
        private float MotionDuration => motion == Motion.Turning ? (openingTurn ? LifeJournalOpeningFan.Duration : turnClip.length) : openClip.length;
        public bool IsAnimating => motion != Motion.None;
        public bool IsClosing => motion == Motion.Closing || closeAfterTurn;
        public bool Available => prepared;
        public float AnimationProgress => !IsAnimating ? 1f : Mathf.Clamp01(elapsed / MotionDuration);
        public string MotionName => motion.ToString();
        public GameObject Model => model;
        public RenderTexture ViewTexture => display;
        public bool HasPageSnapshot => prepared && runtime.TryGetValue(pageLeft, out Material[] materials) && pageLeft.sharedMaterial == materials[0];
        public float PresentationOpacity => input ? input.alpha : 0f;
        public float ViewingAngle => view ? view.transform.localEulerAngles.x : 0f;
        public int OpeningTurnsCompleted { get; private set; }
        public bool IsOpeningBrowse => openingTurn;
        public int PageCaptureCount { get; private set; }
        public int IntroCaptureCount { get; private set; }
        public float OpeningBrowseTime { get; private set; }
        public LifeJournalOpeningFan OpeningFan => openingFan;

        public bool Bind(RectTransform newBook, bool opening = false)
        {
            if (!Prepare()) return false;
            book = newBook; artwork = book.Find("Book artwork")?.GetComponent<RawImage>();
            if (!artwork) { book = null; return false; }
            AddPaper("Left paper", LeftPage); AddPaper("Right paper", RightPage);
            content.Clear(); layouts.Clear();
            input = book.GetComponent<CanvasGroup>(); if (!input) input = book.gameObject.AddComponent<CanvasGroup>();
            if (opening) input.alpha = 0f;
            image = LifeUi.Rect(book, "Physical journal", artwork.rectTransform.anchoredPosition, artwork.rectTransform.sizeDelta).gameObject.AddComponent<RawImage>();
            image.texture = display; image.raycastTarget = false;
            image.rectTransform.sizeDelta += new Vector2(128f, 384f);
            image.transform.SetSiblingIndex(artwork.transform.GetSiblingIndex());
            artwork.gameObject.SetActive(false);
            foreach (Transform child in book)
            {
                if (child == image.transform || child == artwork.transform) continue;
                var group = child.GetComponent<CanvasGroup>(); if (!group) group = child.gameObject.AddComponent<CanvasGroup>();
                content[group] = group.alpha;
                if (IsNavigation(child.name))
                    foreach (Selectable control in child.GetComponentsInChildren<Selectable>(true))
                    {
                        // CanvasGroup still disables clicks during page motion. Keep that
                        // disabled state from applying the default half-transparent tint.
                        ColorBlock colors = control.colors; colors.disabledColor = colors.normalColor; control.colors = colors;
                    }
                var rect = child as RectTransform;
                if (rect) layouts[rect] = new ContentLayout { position = rect.anchoredPosition, scale = rect.localScale };
            }
            SetContent(!IsAnimating);
            captureTransforms = book.GetComponentsInChildren<Transform>(true); captureLayers = new int[captureTransforms.Length];
            for (int i = 0; i < captureTransforms.Length; i++) captureLayers[i] = captureTransforms[i].gameObject.layer;
            captureControls = book.GetComponentsInChildren<Selectable>(true); captureTints = new Color[captureControls.Length];
            animatedContent = book.GetComponentInChildren<LifeGestureDemo>(true);
            wordFeedback = book.GetComponentInChildren<LifeJournalWord>(true);
            contentDirty = false;
            lastPointer = Input.mousePosition;
            if (!IsAnimating) { SampleOpen(opening ? 0f : openClip.length); turningPage.enabled = false; RefreshPaper(); }
            UpdatePresentation(opening ? 0f : openness);
            if (!opening) RenderBook();
            return true;
        }

        private void AddPaper(string name, Rect crop) => AddPaper(book, name, crop);
        private void AddPaper(RectTransform parent, string name, Rect crop)
        {
            var paper = LifeUi.Rect(parent, name, new Vector2((crop.center.x - .5f) * 1672, (crop.center.y - .5f) * 941), new Vector2(crop.width * 1672, crop.height * 941)).gameObject.AddComponent<RawImage>();
            paper.texture = Resources.Load<Texture2D>("LifeAssets/JournalTextures/PaperAged");
            paper.color = paper.texture ? Color.white : new Color(.94f, .90f, .77f);
            paper.raycastTarget = false; paper.transform.SetAsFirstSibling();
        }

        public void Open(Action<RectTransform> buildIntro)
        {
            if (!prepared || !book) return;
            CaptureIntro(buildIntro);
            LifeAudio.Play(LifeAudioCue.Paper);
            turningPage.enabled = false; motion = Motion.Opening; elapsed = 0; onClosed = null; closeAfterTurn = false;
            openingSequence = true; openingFanPending = true; OpeningTurnsCompleted = 0; OpeningBrowseTime = 0; openingTurn = false;
            // The spare manual-turn target holds one stable introductory spread. Both
            // captures finish before the first visible frame; the fan never rebuilds UI.
            openingTargetRevealed = false;
            ApplyPage(pageLeft, after, LeftPage); ApplyPage(pageRight, after, RightPage);
            openingFan.SetPages(after, before, LeftPage, RightPage, afterPhotos, beforePhotos);
            lastRawStep = lastStep = maximumRawStep = 0; presentationFrames = 0;
            SetContent(false); SampleOpen(0); RenderBook();
        }
        public void Close(Action completed)
        {
            if (!prepared || !book) { completed?.Invoke(); return; }
            if (motion == Motion.Closing) return;
            // Finish the physical page's travel before folding the covers; changing clips halfway
            // would teleport the page from its raised position to a flat sheet.
            if (motion == Motion.Turning) { closeAfterTurn = true; onClosed = completed; return; }
            float reverseTime = motion == Motion.Opening ? openClip.length - elapsed : 0;
            LifeAudio.Play(LifeAudioCue.Paper);
            if (!openingSequence) { Capture(before); ApplyPage(pageLeft, before, LeftPage); ApplyPage(pageRight, before, RightPage); }
            turningPage.enabled = false; motion = Motion.Closing; elapsed = reverseTime; onClosed = completed; SetContent(false);
        }
        public void Resume()
        {
            if (closeAfterTurn) { closeAfterTurn = false; onClosed = null; return; }
            if (motion != Motion.Closing) return;
            LifeAudio.Play(LifeAudioCue.Paper);
            elapsed = openClip.length - elapsed; motion = Motion.Opening; onClosed = null;
        }
        public void Turn(Action replaceContent, int direction = 1)
        {
            if (!prepared || !book || IsAnimating) { if (!IsAnimating) replaceContent(); return; }
            LifeAudio.Play(LifeAudioCue.Paper);
            Capture(before); motion = Motion.Turning; elapsed = 0; reverseTurn = direction < 0;
            replaceContent();
            if (!book) { motion = Motion.None; return; }
            Capture(after);
            ApplyPage(pageLeft, reverseTurn ? after : before, LeftPage); ApplyPage(pageRight, reverseTurn ? before : after, RightPage);
            ApplyPage(turningPage, reverseTurn ? after : before, RightPage, reverseTurn ? before : after, LeftPage);
            turningPage.enabled = true; SetContent(false); SampleOpen(openClip.length);
            turnClip.SampleAnimation(model, reverseTurn ? turnClip.length : 0); RenderBook();
        }
        public void BeginPageDrag(Action replaceContent, Action restoreContent, int direction)
        {
            Turn(replaceContent, direction);
            if (motion != Motion.Turning) return;
            manualTurn = true; cancellingTurn = false; restoreDraggedPage = restoreContent;
        }
        public void DragPage(float progress)
        {
            if (!manualTurn) return;
            // Undo the authored rotation's cosine projection and smoothstep easing so the
            // page travels with the pointer instead of barely moving at the start.
            float rotation = Mathf.Acos(1f - 2f * Mathf.Clamp01(progress)) / Mathf.PI;
            float clipProgress = .5f - Mathf.Sin(Mathf.Asin(1f - 2f * rotation) / 3f);
            elapsed = clipProgress * MotionDuration;
            float time = elapsed / MotionDuration * turnClip.length;
            turnClip.SampleAnimation(model, reverseTurn ? turnClip.length - time : time);
            UpdatePresentation(1); RenderBook();
        }
        public void ReleasePage(bool complete)
        { if (manualTurn) { manualTurn = false; cancellingTurn = !complete; } }
        private void TurnOpeningLeaves()
        {
            LifeAudio.Play(LifeAudioCue.Paper);
            openingFanPending = false; openingTurn = true; motion = Motion.Turning; elapsed = 0; reverseTurn = false;
            turningPage.enabled = false; SetContent(false); SampleOpen(openClip.length);
            openingFan.Sample(0); RenderBook();
        }
        public void Unbind()
        {
            Cancel(); book = null; artwork = image = null; input = null; content.Clear(); layouts.Clear();
        }
        public void Cancel()
        {
            manualTurn = cancellingTurn = false; restoreDraggedPage = null;
            openingSequence = false; openingFanPending = false; openingTurn = false; openingFan?.Hide();
            motion = Motion.None; onClosed = null; closeAfterTurn = false; SetContent(true);
            if (prepared) { SampleOpen(openClip.length); turningPage.enabled = false; }
        }
        private void Update()
        {
            if (!prepared || !book || !IsAnimating || manualTurn) return;
            lastRawStep = Time.unscaledDeltaTime;
            maximumRawStep = Mathf.Max(maximumRawStep, lastRawStep);
            // A cold capture or editor hitch must not consume the entire visible fade in
            // one presented frame. This UI animation can take longer when rendering stalls.
            lastStep = Mathf.Min(lastRawStep, .05f); presentationFrames++;
            float duration = MotionDuration;
            if (openingTurn) OpeningBrowseTime += Mathf.Min(lastStep, Mathf.Max(0, duration - elapsed));
            elapsed += cancellingTurn ? -lastStep : lastStep;
            float time = Mathf.Clamp(elapsed, 0, duration);
            if (motion == Motion.Turning)
            {
                if (openingTurn)
                {
                    openingFan.Sample(time); OpeningTurnsCompleted = openingFan.Completed;
                    if (!openingTargetRevealed && openingFan.RevealingTarget)
                    { openingTargetRevealed = true; ApplyPage(pageRight, before, RightPage); }
                }
                else
                {
                    float clipTime = time / duration * turnClip.length;
                    turnClip.SampleAnimation(model, reverseTurn ? turnClip.length - clipTime : clipTime);
                }
                UpdatePresentation(1f);
            }
            else SampleOpen(motion == Motion.Closing ? duration - time : time);
            RenderBook();
            if (cancellingTurn && elapsed <= 0)
            {
                cancellingTurn = false; motion = Motion.None; turningPage.enabled = false;
                Action restore = restoreDraggedPage; restoreDraggedPage = null; restore?.Invoke();
                SampleOpen(openClip.length); RefreshPaper(); SetContent(true); RenderBook(); return;
            }
            if (elapsed < duration) return;
            restoreDraggedPage = null;
            bool closing = motion == Motion.Closing, closeNext = closeAfterTurn; Action done = onClosed;
            if (motion == Motion.Turning && openingTurn) { openingTurn = false; openingFan.Hide(); }
            motion = Motion.None; onClosed = null; closeAfterTurn = false; turningPage.enabled = false;
            if (closing)
            {
                openingSequence = false; openingFanPending = false; openingFan.Hide();
                done?.Invoke(); return;
            }
            SampleOpen(openClip.length);
            if (!openingSequence) RefreshPaper();
            else if (!openingFanPending) { ApplyPage(pageLeft, before, LeftPage); ApplyPage(pageRight, before, RightPage); }
            if (closeNext) { Close(done); RenderBook(); return; }
            if (openingFanPending)
            {
                TurnOpeningLeaves(); return;
            }
            openingSequence = false;
            SetContent(true); RenderBook();
        }

        private void LateUpdate()
        {
            // Unity updates skinned meshes after the pointer event sampled the clip.
            // Refresh while held so the render texture shows that pose even if the mouse stops.
            if (manualTurn) { RenderBook(); return; }
            if (!prepared || !book || IsAnimating || !book.gameObject.activeInHierarchy || Time.unscaledTime < nextContentFrame) return;
            // Static records need no recurring camera render. Only the live pen demonstration
            // or pointer/keyboard feedback changes their captured content.
            bool changed = animatedContent || contentDirty || (!wordFeedback &&
                (Input.mousePosition != lastPointer || Input.anyKeyDown || Input.GetMouseButton(0)));
            if (!changed) return;
            contentDirty = false;
            lastPointer = Input.mousePosition; nextContentFrame = Time.unscaledTime + 1f / 30f;
            RefreshPaper(); RenderBook();
        }

        public void RequestContentRefresh()
        {
            if (prepared && book && book.gameObject.activeInHierarchy && !IsAnimating) contentDirty = true;
        }

        private void RefreshPaper()
        {
            Capture(before); ApplyPage(pageLeft, before, LeftPage); ApplyPage(pageRight, before, RightPage);
        }

        private void SampleOpen(float time)
        {
            openClip.SampleAnimation(model, time);
            // The authored cover closes onto the right leaf; keep the held book centered.
            presentation.localPosition = new Vector3(-.114f * (1f - Mathf.Clamp01(time / openClip.length)), 0, 0);
            if (openingSequence && openingFanPending) openingFan.SampleCover(Mathf.Clamp01(time / openClip.length));
            UpdatePresentation(Mathf.Clamp01(time / openClip.length));
        }

        private void UpdatePresentation(float amount)
        {
            openness = amount;
            if (!view) return;
            float angle = Mathf.Lerp(ClosedAngle, ReadingAngle, Mathf.SmoothStep(0, 1, amount));
            Quaternion rotation = Quaternion.Euler(angle, 0, 0);
            view.transform.rotation = rotation;
            float movement = motion == Motion.Turning ? AnimationProgress : amount;
            float follow = (motion == Motion.Turning ? .025f : .055f) * Mathf.Sin(movement * Mathf.PI);
            view.transform.position = studio.transform.position + Vector3.up * .025f + Vector3.forward * follow - rotation * Vector3.forward * 2f;
            if (input) input.alpha = Mathf.SmoothStep(0, 1, Mathf.Clamp01(amount / .35f));
            ApplyScreenLayout(); SetContent(!IsAnimating);
        }

        private void ApplyScreenLayout()
        {
            if (!view || !image || !artwork) return;
            Vector2 center = artwork.rectTransform.anchoredPosition, size = artwork.rectTransform.sizeDelta, displaySize = image.rectTransform.sizeDelta;
            float scaleX = .14259935f / view.orthographicSize * displaySize.y / size.y, scaleY = scaleX * Mathf.Sin(ViewingAngle * Mathf.Deg2Rad);
            foreach (var pair in layouts) if (pair.Key)
            {
                Vector2 offset = pair.Value.position - center;
                Vector3 world = studio.transform.position + new Vector3(offset.x / size.x * .472f, PaperHeight, offset.y / size.y * .2851987f);
                Vector3 viewport = view.WorldToViewportPoint(world);
                pair.Key.anchoredPosition = image.rectTransform.anchoredPosition + new Vector2((viewport.x - .5f) * displaySize.x, (viewport.y - .5f) * displaySize.y);
                pair.Key.localScale = Vector3.Scale(pair.Value.scale, new Vector3(scaleX, scaleY, 1));
            }
        }

        private void RestoreCaptureLayout()
        {
            foreach (var pair in layouts) if (pair.Key)
            { pair.Key.anchoredPosition = pair.Value.position; pair.Key.localScale = pair.Value.scale; }
        }

        private bool Prepare()
        {
            if (prepared) return true;
            var prefab = Resources.Load<GameObject>("LifeAssets/JournalBook");
            if (!prefab) return false;
            AnimationClip[] clips = Resources.LoadAll<AnimationClip>("LifeAssets/JournalBook");
            openClip = Array.Find(clips, c => c.name.EndsWith("BookOpen", StringComparison.Ordinal));
            turnClip = Array.Find(clips, c => c.name.EndsWith("PageTurn", StringComparison.Ordinal));
            if (!openClip || !turnClip || openClip.length <= 0 || turnClip.length <= 0) { Debug.LogError("The physical journal needs its authored BookOpen and PageTurn clips.", this); return false; }
            Shader surface = Resources.Load<Shader>("LifeGesture/JournalSurface");
            if (!surface) { Debug.LogError("The physical journal needs its unlit surface shader.", this); return false; }
            // Far below the world, but close enough to retain sub-millimetre paper separation.
            // At -10000 a float's spacing exceeds the leaf thickness and causes depth stripes.
            studio = new GameObject("Journal rendering studio"); studio.transform.SetParent(transform, false); studio.transform.position = new Vector3(0, -100, 0);
            presentation = new GameObject("Journal orientation").transform; presentation.SetParent(studio.transform, false);
            // Unity imports this FBX with both in-plane axes reversed. Keep the authored rig
            // conversion intact, and correct its reading orientation outside the sampled hierarchy.
            presentation.localRotation = Quaternion.Euler(0, 180, 0);
            model = Instantiate(prefab, presentation); model.name = "JournalBook";
            foreach (Transform part in model.GetComponentsInChildren<Transform>(true)) part.gameObject.layer = BookLayer;
            var animator = model.GetComponent<Animator>(); if (animator) animator.enabled = false;
            foreach (Renderer renderer in model.GetComponentsInChildren<Renderer>(true))
            {
                // These two legacy tabs would duplicate the three interactive tabs above the book.
                if (renderer.name == "SymbolsTab" || renderer.name == "PhotosTab") renderer.enabled = false;
                if (renderer.name == "PageLeft") pageLeft = renderer;
                if (renderer.name == "PageRight") pageRight = renderer;
                if (renderer.name == "TurningPage") turningPage = renderer;
                renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
                if (renderer is SkinnedMeshRenderer skinned) skinned.updateWhenOffscreen = true;
            }
            if (!pageLeft || !pageRight || !turningPage)
            { Debug.LogError("The physical journal is missing PageLeft, PageRight or TurningPage.", this); Destroy(studio); studio = null; return false; }
            foreach (Renderer renderer in model.GetComponentsInChildren<Renderer>(true))
            {
                Material[] materials = renderer.sharedMaterials;
                for (int i = 0; i < materials.Length; i++)
                {
                    Material source = materials[i];
                    if (source.name == "JournalCloth")
                    {
                        var cover = Resources.Load<Material>("AlbumBook/CoverPaint");
                        if (cover)
                        {
                            if (!surfaces.TryGetValue(source, out Material paint))
                                surfaces[source] = paint = new Material(cover) { name = "Journal olive painted cover" };
                            materials[i] = paint;
                            continue;
                        }
                    }
                    if (!surfaces.TryGetValue(source, out Material unlit))
                    {
                        unlit = new Material(surface) { name = "Journal surface: " + source.name };
                        string textureProperty = source.HasProperty("_BaseMap") ? "_BaseMap" : "_MainTex";
                        unlit.SetTexture("_BaseMap", source.GetTexture(textureProperty));
                        unlit.SetTextureScale("_BaseMap", source.GetTextureScale(textureProperty));
                        unlit.SetTextureOffset("_BaseMap", source.GetTextureOffset(textureProperty));
                        unlit.SetColor("_BaseColor", source.HasProperty("_BaseColor") ? source.GetColor("_BaseColor") : source.color);
                        bool readingPaper = source.name == "JournalPageLeft" || source.name == "JournalPageRight";
                        unlit.SetFloat("_ShadeStrength", readingPaper ? .30f : .42f);
                        unlit.SetFloat("_MinimumShade", readingPaper ? .80f : .72f);
                        unlit.SetFloat("_FoldOcclusion", source.name == "JournalGutter" ? .18f : 0f);
                        foreach (string detail in new[] { "_BumpMap", "_OcclusionMap" })
                            if (source.HasProperty(detail))
                            {
                                unlit.SetTexture(detail, source.GetTexture(detail));
                                unlit.SetTextureScale(detail, source.GetTextureScale(detail));
                                unlit.SetTextureOffset(detail, source.GetTextureOffset(detail));
                            }
                        if (source.HasProperty("_BumpScale")) unlit.SetFloat("_BumpScale", source.GetFloat("_BumpScale"));
                        if (source.HasProperty("_OcclusionStrength")) unlit.SetFloat("_OcclusionStrength", source.GetFloat("_OcclusionStrength"));
                        surfaces[source] = unlit;
                    }
                    materials[i] = unlit;
                }
                renderer.sharedMaterials = materials;
            }
            foreach (Renderer page in new[] { pageLeft, pageRight, turningPage })
            {
                var materials = new Material[page.sharedMaterials.Length];
                for (int i = 0; i < materials.Length; i++) materials[i] = new Material(page.sharedMaterials[i]) { name = "Journal page snapshot" };
                runtime[page] = materials;
            }
            openingFan = new LifeJournalOpeningFan(prefab, presentation, turnClip, runtime[turningPage], BookLayer);
            openClip.SampleAnimation(model, openClip.length); turningPage.enabled = false;
            display = MakeTarget(1356, 1126, "Journal 3D view"); before = MakeTarget(1672, 941, "Journal current page"); after = MakeTarget(1672, 941, "Journal next page");
            view = MakeCamera("Journal view", studio.transform, BookLayer); view.targetTexture = display;
            // Enlarge both the render target and UI rectangle in the same ratio as the
            // frustum. The moving leaf gains padding without shrinking the readable book.
            view.orthographicSize = .152f * 1126f / 742f;
            captureCamera = MakeCamera("Journal content capture", studio.transform, CaptureLayer);
            captureCamera.transform.localPosition = new Vector3(0, 0, -5); captureCamera.transform.localRotation = Quaternion.identity;
            var captureObject = new GameObject("Journal capture canvas", typeof(RectTransform), typeof(Canvas)); captureObject.transform.SetParent(studio.transform, false); captureObject.layer = CaptureLayer;
            captureCanvas = captureObject.GetComponent<Canvas>(); captureCanvas.renderMode = RenderMode.ScreenSpaceCamera; captureCanvas.worldCamera = captureCamera; captureCanvas.planeDistance = 1f;
            prepared = true; return true;
        }
        private static Camera MakeCamera(string name, Transform parent, int layer)
        {
            var go = new GameObject(name, typeof(Camera)); go.transform.SetParent(parent, false);
            var camera = go.GetComponent<Camera>(); camera.enabled = false; camera.orthographic = true; camera.nearClipPlane = .01f; camera.farClipPlane = 10f;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.clear; camera.cullingMask = 1 << layer; camera.allowHDR = false; camera.allowMSAA = false;
            return camera;
        }
        private static RenderTexture MakeTarget(int width, int height, string name)
        {
            var target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32) { name = name, filterMode = FilterMode.Bilinear };
            target.Create(); return target;
        }
        private void Capture(RenderTexture target)
        {
            if (!book) return;
            Transform previousParent = book.parent; int sibling = book.GetSiblingIndex(); Vector3 scale = book.localScale; Vector2 position = book.anchoredPosition;
            bool oldImage = image.enabled; bool visible = !IsAnimating; float oldAlpha = input.alpha;
            try
            {
                RestoreCaptureLayout(); input.alpha = 1f;
                WeatherHousePrototype.AlbumPhotoPaper.Capture(book, target == before ? beforePhotos : afterPhotos);
                foreach (var pair in content) if (pair.Key) pair.Key.alpha = IsNavigation(pair.Key.name) ? 0f : pair.Value;
                for (int i = 0; i < captureControls.Length; i++)
                {
                    Selectable control = captureControls[i]; if (!control || !control.targetGraphic) continue;
                    captureTints[i] = control.targetGraphic.canvasRenderer.GetColor();
                }
                artwork.gameObject.SetActive(false); image.enabled = false;
                for (int i = 0; i < captureTransforms.Length; i++) if (captureTransforms[i]) captureTransforms[i].gameObject.layer = CaptureLayer;
                captureCamera.targetTexture = target; captureCamera.orthographicSize = target.height * .5f;
                book.SetParent(captureCanvas.transform, false); book.localScale = Vector3.one; book.anchoredPosition = Vector2.zero;
                Canvas.ForceUpdateCanvases();
                foreach (Selectable control in captureControls)
                    // Only remove the parent animation lock's tint; an intentionally locked
                    // button retains its own disabled appearance in the page snapshot.
                    if (control && control.interactable && control.targetGraphic && control.transition == Selectable.Transition.ColorTint)
                        control.targetGraphic.canvasRenderer.SetColor(control.colors.normalColor * control.colors.colorMultiplier);
                captureCamera.Render(); PageCaptureCount++;
            }
            finally
            {
                book.SetParent(previousParent, false); book.SetSiblingIndex(sibling); book.localScale = scale; book.anchoredPosition = position;
                for (int i = 0; i < captureTransforms.Length; i++) if (captureTransforms[i]) captureTransforms[i].gameObject.layer = captureLayers[i];
                artwork.gameObject.SetActive(false); image.enabled = oldImage; input.alpha = oldAlpha; SetContent(visible); ApplyScreenLayout(); captureCamera.targetTexture = null;
                for (int i = 0; i < captureControls.Length; i++)
                    if (captureControls[i] && captureControls[i].targetGraphic) captureControls[i].targetGraphic.canvasRenderer.SetColor(captureTints[i]);
                Canvas.ForceUpdateCanvases();
            }
        }
        private void CaptureIntro(Action<RectTransform> build)
        {
            var intro = LifeUi.Rect(captureCanvas.transform, "Journal opening spread", Vector2.zero, new Vector2(1672, 941));
            try
            {
                AddPaper(intro, "Left paper", LeftPage); AddPaper(intro, "Right paper", RightPage);
                build(intro);
                WeatherHousePrototype.AlbumPhotoPaper.Capture(intro, afterPhotos);
                foreach (Transform part in intro.GetComponentsInChildren<Transform>(true)) part.gameObject.layer = CaptureLayer;
                foreach (Graphic graphic in intro.GetComponentsInChildren<Graphic>(true)) graphic.raycastTarget = false;
                captureCamera.targetTexture = after; captureCamera.orthographicSize = after.height * .5f;
                Canvas.ForceUpdateCanvases(); captureCamera.Render(); PageCaptureCount++; IntroCaptureCount++;
            }
            finally
            {
                intro.gameObject.SetActive(false); Destroy(intro.gameObject); captureCamera.targetTexture = null;
            }
        }
        private void ApplyPage(Renderer page, Texture front, Rect frontUv, Texture back = null, Rect backUv = default)
        {
            Material[] materials = runtime[page];
            for (int i = 0; i < materials.Length; i++)
            {
                Texture texture = i == 0 || back == null ? front : back; Rect uv = i == 0 || back == null ? frontUv : backUv;
                WeatherHousePrototype.AlbumPhotoPaper.Apply(materials[i], texture == before ? beforePhotos : afterPhotos);
                foreach (string property in new[] { "_BaseMap", "_MainTex" })
                    if (materials[i].HasProperty(property)) { materials[i].SetTexture(property, texture); materials[i].SetTextureScale(property, uv.size); materials[i].SetTextureOffset(property, uv.position); }
                if (materials[i].HasProperty("_BaseColor")) materials[i].SetColor("_BaseColor", Color.white);
            }
            page.sharedMaterials = materials;
        }
        private void SetContent(bool visible)
        {
            foreach (var pair in content) if (pair.Key)
            {
                float navigationAlpha = motion == Motion.Turning ? 1f : Mathf.SmoothStep(0, 1, Mathf.Clamp01((openness - .72f) / .28f));
                pair.Key.alpha = IsNavigation(pair.Key.name) ? pair.Value * navigationAlpha : 0f;
                pair.Key.blocksRaycasts = visible; pair.Key.interactable = visible;
            }
            if (input) input.blocksRaycasts = input.interactable = visible;
        }
        private static bool IsNavigation(string name) => name == "Symbols tab" || name == "Photos tab" || name == "Gesture learning tab" || name == "Photography tasks tab" || name == "Close album" || name == "Next symbols" || name == "Previous symbols";
        private void RenderBook()
        {
            if (!view || drawingFrame) return;
            drawingFrame = true; try { view.Render(); } finally { drawingFrame = false; }
        }
        private void OnDisable() => Cancel();
        private void OnDestroy()
        {
            openingFan?.Dispose();
            foreach (var pair in runtime) foreach (var material in pair.Value) if (material) Destroy(material);
            foreach (var material in surfaces.Values) if (material) Destroy(material);
            foreach (var target in new[] { display, before, after }) if (target) { target.Release(); Destroy(target); }
            if (studio) Destroy(studio);
        }
    }
}
