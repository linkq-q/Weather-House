using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace WeatherHousePrototype
{
    /// <summary>One reversible timeline; the fully open book remains the existing, interactive UI.</summary>
    public sealed class AlbumBookOpening : MonoBehaviour
    {
        public static float Duration { get { return AlbumMagicRevealSettings.Load().PlaybackDuration; } }
        public bool IsPlaying { get; private set; }
        public bool IsClosing { get; private set; }

        private const float BodyHandoff = .80f;
        private readonly List<ChildState> children = new List<ChildState>();
        private RectTransform root;
        private Vector2 restingPosition;
        private CanvasGroup inputGroup;
        private bool originalBlocksRaycasts;
        private bool originalInteractable;
        private bool initialized;
        private bool manualPlayback;
        private float elapsed;
        private System.Action closed;
        private RectTransform visual;
        private AlbumOpeningSurfaceGraphic rightPage;
        private AlbumOpeningSurfaceGraphic cover;
        private AlbumOpeningLeafGraphic leaf;
        private AlbumOpeningLeafGraphic leafShadow;
        private AlbumOpeningThicknessGraphic coverEdge;
        private AlbumOpeningThicknessGraphic pageStack;
        private AlbumOpeningThicknessGraphic hingeShadow;
        private Texture2D bookTexture;
        private Texture2D coverTexture;
        private AlbumMagicReveal reveal;

        private sealed class ChildState
        {
            public RectTransform rect;
            public CanvasGroup group;
            public Vector2 position;
            public Vector3 scale;
            public float alpha;
            public bool isBook;
        }

        public void Play()
        {
            if (!Initialize()) return;
            IsClosing = false;
            closed = null;
            manualPlayback = false;
            elapsed = 0f;
            enabled = true;
            ApplySample(0f);
        }

        public void PlayReverse(System.Action onClosed)
        {
            if (IsClosing) return;
            if (!Initialize()) { onClosed?.Invoke(); return; }
            if (!IsPlaying) elapsed = Duration;
            IsClosing = true;
            closed = onClosed;
            manualPlayback = false;
            enabled = true;
            ApplySample(elapsed);
            if (elapsed <= 0f) CompleteClose();
        }

        public void Resume()
        {
            if (!Initialize()) return;
            IsClosing = false;
            closed = null;
            manualPlayback = false;
            enabled = true;
            ApplySample(elapsed);
        }

        /// <summary>Pose the animation without advancing it automatically; Play resumes normal playback.</summary>
        public void Sample(float seconds)
        {
            if (!Initialize()) return;
            IsClosing = false;
            closed = null;
            manualPlayback = true;
            elapsed = Mathf.Clamp(seconds, 0f, Duration);
            ApplySample(elapsed);
        }

        public void FinishImmediately()
        {
            IsPlaying = false;
            IsClosing = false;
            closed = null;
            elapsed = Duration;
            RestoreOriginals();
            RemoveVisual();
        }

        private void Update()
        {
            if (!IsPlaying || manualPlayback) return;
            elapsed = Mathf.Clamp(elapsed + (IsClosing ? -1f : 1f) * Time.unscaledDeltaTime, 0f, Duration);
            ApplySample(elapsed);
            if (IsClosing && elapsed <= 0f) CompleteClose();
        }

        private void CompleteClose()
        {
            IsPlaying = false;
            IsClosing = false;
            System.Action completed = closed;
            closed = null;
            completed?.Invoke();
        }

        private bool Initialize()
        {
            if (initialized) return true;
            root = transform as RectTransform;
            Transform artwork = transform.Find("Book artwork");
            bookTexture = Resources.Load<Texture2D>("AlbumBook/Book");
            coverTexture = Resources.Load<Texture2D>("AlbumBook/CoverFlower");
            if (root == null || artwork == null || bookTexture == null || coverTexture == null)
            {
                Debug.LogWarning("Album opening requires the existing book and AlbumBook/CoverFlower texture.", this);
                return false;
            }

            restingPosition = root.anchoredPosition;
            inputGroup = GetComponent<CanvasGroup>();
            if (inputGroup == null) inputGroup = gameObject.AddComponent<CanvasGroup>();
            originalBlocksRaycasts = inputGroup.blocksRaycasts;
            originalInteractable = inputGroup.interactable;
            for (int i = 0; i < transform.childCount; i++)
            {
                var child = transform.GetChild(i) as RectTransform;
                if (child == null) continue;
                var group = child.GetComponent<CanvasGroup>();
                if (group == null) group = child.gameObject.AddComponent<CanvasGroup>();
                children.Add(new ChildState
                {
                    rect = child,
                    group = group,
                    position = child.anchoredPosition,
                    scale = child.localScale,
                    alpha = group.alpha,
                    isBook = child == artwork
                });
            }
            reveal = new AlbumMagicReveal(root, artwork);
            initialized = true;
            return true;
        }

        private void ApplySample(float playbackSeconds)
        {
            AlbumMagicRevealSettings revealSettings = AlbumMagicRevealSettings.Load();
            if (!IsClosing && playbackSeconds >= revealSettings.PlaybackDuration)
            {
                FinishImmediately();
                return;
            }
            float seconds = revealSettings.MapPlaybackTime(playbackSeconds);

            IsPlaying = true;
            inputGroup.blocksRaycasts = false;
            // Raycasts and GameplayHud's opening guard block input without Button's disabled tint.
            inputGroup.interactable = originalInteractable;
            float angle = OpeningAngle(seconds);
            float impact = Mathf.Sin(Mathf.PI * Mathf.Clamp01((seconds - .46f) / .34f));
            float settle = impact * impact;
            Vector3 scale = new Vector3(1f + .02f * settle, 1f - .015f * settle, 1f);
            // FitAlbum owns root.localScale. Move this root, and scale only its existing children/visual.
            float centerOffset = -301.5f * (1f + Mathf.Min(0f, Mathf.Cos(angle * Mathf.Deg2Rad)));
            root.anchoredPosition = restingPosition + new Vector2(centerOffset, VerticalOffset(seconds));
            float progress = revealSettings.ProgressAt(seconds);
            float contentAlpha = revealSettings.enableNoiseDissolve
                ? (seconds >= revealSettings.revealStart ? 1f : 0f) : Mathf.SmoothStep(0f, 1f, progress);
            foreach (ChildState child in children)
            {
                if (child.rect == null || child.group == null) continue;
                child.group.alpha = child.alpha * (child.isBook
                    ? (seconds >= BodyHandoff ? 1f : 0f) : contentAlpha);
                child.rect.anchoredPosition = Vector2.Scale(child.position, new Vector2(scale.x, scale.y));
                child.rect.localScale = Vector3.Scale(child.scale, scale);
            }
            reveal.Sample(progress, seconds);

            if (seconds >= BodyHandoff)
            {
                RemoveVisual();
                return;
            }
            if (visual == null) CreateVisual();
            visual.localScale = scale;
            rightPage.SetPose(bookTexture, 180f, false);
            cover.SetPose(angle < 90f ? coverTexture : bookTexture, angle, true);
            coverEdge.SetPose(angle);
            pageStack.SetPose(angle);
            hingeShadow.SetPose(angle);
            float leafAngle = 180f * Ease((seconds - .04f) / .74f);
            float curl = Mathf.Sin(Mathf.PI * Mathf.Clamp01((seconds - .20f) / .58f));
            float leafOpacity = Ease((seconds - .07f) / .07f);
            leaf.SetPose(bookTexture, leafAngle, curl, leafOpacity, false);
            leafShadow.SetPose(bookTexture, leafAngle, curl, leafOpacity, true);
            // The carried sheet lies beneath the front cover until that cover passes the hinge.
            // Afterwards it settles above the now open inside cover, with its own delayed motion.
            if (angle < 90f)
            {
                cover.transform.SetAsLastSibling();
                coverEdge.transform.SetAsLastSibling();
            }
            else
            {
                leafShadow.transform.SetAsLastSibling();
                leaf.transform.SetAsLastSibling();
            }
        }

        private void CreateVisual()
        {
            visual = Node(root, "Opening visual");
            visual.SetAsFirstSibling();
            pageStack = Graphic<AlbumOpeningThicknessGraphic>("Opening page stack");
            pageStack.kind = AlbumOpeningThicknessGraphic.Kind.PageStack;
            rightPage = Graphic<AlbumOpeningSurfaceGraphic>("Opening right page");
            hingeShadow = Graphic<AlbumOpeningThicknessGraphic>("Opening hinge shadow");
            hingeShadow.kind = AlbumOpeningThicknessGraphic.Kind.HingeShadow;
            cover = Graphic<AlbumOpeningSurfaceGraphic>("Opening cover");
            coverEdge = Graphic<AlbumOpeningThicknessGraphic>("Opening cover edge");
            coverEdge.kind = AlbumOpeningThicknessGraphic.Kind.CoverEdge;
            leafShadow = Graphic<AlbumOpeningLeafGraphic>("Opening loose leaf shadow");
            leaf = Graphic<AlbumOpeningLeafGraphic>("Opening loose left leaf");
        }

        private T Graphic<T>(string objectName) where T : Graphic
        {
            T graphic = Node(visual, objectName).gameObject.AddComponent<T>();
            graphic.raycastTarget = false;
            return graphic;
        }

        private static RectTransform Node(Transform parent, string objectName)
        {
            var go = new GameObject(objectName, typeof(RectTransform));
            go.layer = parent.gameObject.layer;
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
            rect.sizeDelta = AlbumBookView.ReferenceSize;
            return rect;
        }

        private void RestoreOriginals()
        {
            if (!initialized) return;
            reveal.Restore();
            if (root != null) root.anchoredPosition = restingPosition;
            if (inputGroup != null)
            {
                inputGroup.blocksRaycasts = originalBlocksRaycasts;
                inputGroup.interactable = originalInteractable;
            }
            foreach (ChildState child in children)
            {
                if (child.rect != null)
                {
                    child.rect.anchoredPosition = child.position;
                    child.rect.localScale = child.scale;
                }
                if (child.group != null) child.group.alpha = child.alpha;
            }
        }

        private void RemoveVisual()
        {
            if (visual == null) return;
            // Hide immediately, even when a deterministic capture occurs before deferred Destroy runs.
            visual.gameObject.SetActive(false);
            Destroy(visual.gameObject);
            visual = null;
        }

        private void OnDestroy()
        {
            RestoreOriginals();
            RemoveVisual();
            if (reveal != null) reveal.Dispose();
        }

        private void OnDisable()
        {
            FinishImmediately();
        }

        private static float OpeningAngle(float seconds)
        {
            // A single continuous turn avoids the pauses introduced by several eased keyframes.
            return 180f * Ease((seconds - .03f) / .53f);
        }

        private static float VerticalOffset(float seconds)
        {
            if (seconds < .28f)
            {
                float remaining = 1f - Mathf.Clamp01(seconds / .28f);
                return Mathf.Lerp(-1050f, 8f, 1f - remaining * remaining * remaining);
            }
            return Mathf.Lerp(8f, 0f, Ease((seconds - .28f) / .30f));
        }

        private static float Ease(float value)
        {
            value = Mathf.Clamp01(value);
            return value * value * (3f - 2f * value);
        }
    }

    [RequireComponent(typeof(CanvasRenderer))]
    internal sealed class AlbumOpeningSurfaceGraphic : MaskableGraphic
    {
        internal const float Top = 362.5f;
        internal const float Bottom = -379.5f;
        internal const float RightWidth = 603f;
        internal const float LeftWidth = 625f;
        internal const float HingeU = LeftWidth / 1228f;
        private static readonly Rect CoverUv = new Rect(91f / 1122f, 71f / 1402f, 943f / 1122f, 1256f / 1402f);
        private Texture2D source;
        private float angle;
        private bool turning;

        public override Texture mainTexture { get { return source != null ? source : Texture2D.whiteTexture; } }

        internal void SetPose(Texture2D texture, float degrees, bool isTurning)
        {
            bool changedTexture = source != texture;
            source = texture;
            angle = degrees;
            turning = isTurning;
            if (changedTexture) SetMaterialDirty();
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            const int segments = 20;
            float radians = angle * Mathf.Deg2Rad;
            float cosine = Mathf.Cos(radians);
            float width = turning ? Mathf.Lerp(RightWidth, LeftWidth, angle / 180f) : RightWidth;
            bool outside = turning && angle < 90f;
            for (int i = 0; i <= segments; i++)
            {
                float across = i / (float)segments;
                // Orthographic front view: horizontal page edges never become diagonal trapezoids.
                float x = turning ? width * across * cosine : width * across;
                if (turning && !outside)
                    x += 8f * Mathf.Sin(Mathf.PI * across) * Mathf.Sin(radians)
                        * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(90f, 105f, angle));
                float shade = turning ? 1f - .09f * Mathf.Sin(radians) * (1f - .35f * across) : 1f;
                Color tint = new Color(shade, shade, shade, 1f) * color;
                float u = outside ? Mathf.Lerp(CoverUv.xMin, CoverUv.xMax, across)
                    : turning ? Mathf.Lerp(HingeU, 0f, across) : Mathf.Lerp(HingeU, 1f, across);
                float bottomV = outside ? CoverUv.yMin : 0f;
                float topV = outside ? CoverUv.yMax : 1f;
                mesh.AddVert(new Vector3(x, Bottom), tint, new Vector2(u, bottomV));
                mesh.AddVert(new Vector3(x, Top), tint, new Vector2(u, topV));
                if (i == 0) continue;
                int n = i * 2;
                mesh.AddTriangle(n - 2, n - 1, n);
                mesh.AddTriangle(n - 1, n + 1, n);
            }
        }
    }

    [RequireComponent(typeof(CanvasRenderer))]
    internal sealed class AlbumOpeningLeafGraphic : MaskableGraphic
    {
        // This paper-only crop ends before the leather strap. In the resting pose every UV is
        // at its original Book-artwork pixel, including the slightly raised outer page border.
        private const float PaperLeft = -592f;
        private const float PaperRight = -28f;
        private const float PaperBottom = -354f;
        private const float PaperTop = 347f;
        private Texture2D source;
        private float angle;
        private float curl;
        private float opacity;
        private bool shadow;

        public override Texture mainTexture
        {
            get { return !shadow && source != null ? source : Texture2D.whiteTexture; }
        }

        internal void SetPose(Texture2D texture, float degrees, float bend, float alpha, bool isShadow)
        {
            bool changedTexture = source != texture || shadow != isShadow;
            source = texture;
            angle = degrees;
            curl = bend;
            opacity = alpha;
            shadow = isShadow;
            if (changedTexture) SetMaterialDirty();
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            const int columns = 28;
            float lift = Mathf.Sin(angle * Mathf.Deg2Rad);
            if (opacity <= 0f || (shadow && lift < .001f)) return;
            // Each column spans the complete page height. Its Y displacement is linear,
            // so subdividing it into rows adds no curvature and makes overlap seams visible.
            for (int column = 0; column <= columns; column++)
            {
                float across = column / (float)columns;
                for (int row = 0; row < 2; row++)
                {
                    float down = row;
                    Vector2 position = Position(across, down);
                    float pixelX = Mathf.Lerp(PaperRight, PaperLeft, across);
                    float pixelY = Mathf.Lerp(PaperBottom, PaperTop, down);
                    Vector2 uv = new Vector2((pixelX + AlbumOpeningSurfaceGraphic.LeftWidth) / 1228f,
                        (pixelY - AlbumOpeningSurfaceGraphic.Bottom) / 742f);
                    float shade = 1f - .075f * lift * (1f - across);
                    Color tint = shadow ? new Color(.18f, .20f, .12f, .16f * lift * opacity)
                        : new Color(shade, shade, shade, opacity);
                    mesh.AddVert(position, tint * color, uv);
                }
                if (column == 0) continue;
                int current = column * 2;
                mesh.AddTriangle(current - 2, current - 1, current);
                mesh.AddTriangle(current - 1, current + 1, current);
            }
            if (shadow || lift < .001f) return;

            // The loose page has its own thin edge, distinct from the 18-pixel hard-cover rim.
            Vector2 lower = Position(1f, 0f);
            Vector2 upper = Position(1f, 1f);
            Vector2 edge = new Vector2(2.2f * lift, -1.4f * lift);
            int start = mesh.currentVertCount;
            Color edgeTint = new Color(.80f, .77f, .66f, opacity * lift) * color;
            Vector2 paperUv = new Vector2(.25f, .50f);
            mesh.AddVert(lower, edgeTint, paperUv);
            mesh.AddVert(upper, edgeTint, paperUv);
            mesh.AddVert(upper + edge, edgeTint, paperUv);
            mesh.AddVert(lower + edge, edgeTint, paperUv);
            mesh.AddTriangle(start, start + 1, start + 2);
            mesh.AddTriangle(start, start + 2, start + 3);
        }

        private Vector2 Position(float across, float down)
        {
            float lift = Mathf.Sin(angle * Mathf.Deg2Rad);
            float projection = Mathf.Cos(angle * Mathf.Deg2Rad);
            // This remapping stays monotonic (derivative >= 1 - .1*pi), so the paper
            // cannot fold back over itself near 90 degrees. The hinge inset turns with it.
            float bentAcross = across + .10f * curl * lift * Mathf.Sin(Mathf.PI * across);
            float x = (-PaperRight + (PaperRight - PaperLeft) * bentAcross) * projection;
            float y = Mathf.Lerp(PaperBottom, PaperTop, down)
                + 24f * curl * Mathf.Pow(across, 1.4f) * lift;
            if (shadow) return new Vector2(x + 7f * lift, y - 11f * lift);
            return new Vector2(x, y);
        }
    }

    [RequireComponent(typeof(CanvasRenderer))]
    internal sealed class AlbumOpeningThicknessGraphic : MaskableGraphic
    {
        internal enum Kind { PageStack, CoverEdge, HingeShadow }
        internal Kind kind;
        private float angle;

        internal void SetPose(float degrees)
        {
            angle = degrees;
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            float radians = angle * Mathf.Deg2Rad;
            float reveal = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(150f, 180f, angle));
            const float top = AlbumOpeningSurfaceGraphic.Top;
            const float bottom = AlbumOpeningSurfaceGraphic.Bottom;
            const float width = AlbumOpeningSurfaceGraphic.RightWidth;
            if (kind == Kind.PageStack)
            {
                // Three calm, round-edged bands make the closed book a block of pages, not a sheet.
                float stackReveal = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(35f, 90f, angle));
                RoundedRect(mesh, new Rect(1f, bottom - 13f, width + 5f, top - bottom), 17f,
                    Tint(new Color32(73, 88, 61, 255), stackReveal));
                RoundedRect(mesh, new Rect(1f, bottom - 9f, width + 3f, top - bottom), 16f,
                    Tint(new Color32(205, 193, 157, 255), stackReveal));
                RoundedRect(mesh, new Rect(1f, bottom - 6f, width + 2f, top - bottom), 15f,
                    Tint(new Color32(241, 229, 192, 255), stackReveal));
                RoundedRect(mesh, new Rect(1f, bottom - 3f, width + 1f, top - bottom), 15f,
                    Tint(new Color32(217, 206, 169, 255), stackReveal));
                return;
            }
            if (kind == Kind.HingeShadow)
            {
                float shadow = Mathf.Sin(radians) * reveal;
                Quad(mesh, new Vector2(0f, bottom + 15f), new Vector2(0f, top - 15f),
                    new Vector2(40f + 28f * shadow, top - 15f), new Vector2(40f + 28f * shadow, bottom + 15f),
                    new Color(0.24f, .26f, .15f, .19f * shadow), new Color(.24f, .26f, .15f, 0f));
                return;
            }

            float faceWidth = Mathf.Lerp(width, AlbumOpeningSurfaceGraphic.LeftWidth, angle / 180f);
            float outerX = faceWidth * Mathf.Cos(radians);
            float thickness = (5f + 13f * Mathf.Sin(radians)) * reveal;
            if (thickness <= .01f) return;
            float drop = 7f * reveal;
            Color edge = Tint(new Color32(105, 119, 78, 255), reveal);
            // An 18-pixel rounded edge remains visible at 90 degrees, even when the cover face is edge-on.
            RoundedRect(mesh, new Rect(outerX, bottom + 14f - drop, thickness, top - bottom - 28f),
                thickness * .45f, edge);
            RoundedRect(mesh, new Rect(outerX + thickness * .22f, bottom + 19f - drop,
                Mathf.Max(1f, thickness * .15f), top - bottom - 38f), thickness * .07f,
                Tint(new Color32(152, 162, 117, 255), reveal * .75f));
            Quad(mesh, new Vector2(0f, bottom), new Vector2(outerX, bottom),
                new Vector2(outerX + thickness, bottom - drop), new Vector2(0f, bottom - drop),
                edge, edge);
        }

        private Color Tint(Color tint, float alpha)
        {
            tint.a *= alpha;
            return tint * color;
        }

        private static void Quad(VertexHelper mesh, Vector2 a, Vector2 b, Vector2 c, Vector2 d,
            Color near, Color far)
        {
            int start = mesh.currentVertCount;
            mesh.AddVert(a, near, Vector2.zero);
            mesh.AddVert(b, near, Vector2.zero);
            mesh.AddVert(c, far, Vector2.zero);
            mesh.AddVert(d, far, Vector2.zero);
            mesh.AddTriangle(start, start + 1, start + 2);
            mesh.AddTriangle(start, start + 2, start + 3);
        }

        private static void RoundedRect(VertexHelper mesh, Rect rect, float radius, Color tint)
        {
            if (tint.a <= 0f || rect.width <= 0f || rect.height <= 0f) return;
            radius = Mathf.Min(radius, Mathf.Min(rect.width, rect.height) * .5f);
            const int cornerSegments = 6;
            int start = mesh.currentVertCount;
            mesh.AddVert(rect.center, tint, Vector2.zero);
            for (int corner = 0; corner < 4; corner++)
            {
                Vector2 center = corner == 0 ? new Vector2(rect.xMax - radius, rect.yMax - radius)
                    : corner == 1 ? new Vector2(rect.xMin + radius, rect.yMax - radius)
                    : corner == 2 ? new Vector2(rect.xMin + radius, rect.yMin + radius)
                    : new Vector2(rect.xMax - radius, rect.yMin + radius);
                for (int i = 0; i <= cornerSegments; i++)
                {
                    float direction = (corner * 90f + i * 90f / cornerSegments) * Mathf.Deg2Rad;
                    mesh.AddVert(center + new Vector2(Mathf.Cos(direction), Mathf.Sin(direction)) * radius,
                        tint, Vector2.zero);
                }
            }
            int perimeter = 4 * (cornerSegments + 1);
            for (int i = 0; i < perimeter; i++)
                mesh.AddTriangle(start, start + 1 + i, start + 1 + (i + 1) % perimeter);
        }
    }
}
