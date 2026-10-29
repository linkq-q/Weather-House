using System;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using WeatherHousePrototype;

namespace WeatherHouse.Life
{
    public static class LifeJournalChrome
    {
        public static void GestureTab(RectTransform book, bool selected, UnityAction click)
        {
            if (selected)
            {
                book.Find("Symbols tab").GetComponent<RawImage>().texture = Resources.Load<Texture2D>("AlbumBook/TabSymbolsIdle");
                book.Find("Photos tab").GetComponent<RawImage>().texture = Resources.Load<Texture2D>("AlbumBook/TabPhotosIdle");
            }
            // Match the two original 82 x 69 paper tabs and their shared baseline.
            var rect = LifeUi.Rect(book, "Gesture learning tab", new Vector2(465f - 836f, 470.5f - 109.5f), new Vector2(82, 69));
            rect.SetSiblingIndex(2);
            foreach (string name in new[] { "Symbols tab", "Photos tab", "Gesture learning tab" })
            {
                var tab = (RectTransform)book.Find(name);
                tab.anchoredPosition = new Vector2(tab.anchoredPosition.x, 377f);
            }
            var graphic = rect.gameObject.AddComponent<LifeJournalTabGraphic>();
            graphic.selected = selected;
            var button = rect.gameObject.AddComponent<Button>(); button.targetGraphic = graphic;
            button.onClick.AddListener(click);
            ColorBlock colors = button.colors; colors.highlightedColor = new Color(1, .98f, .92f); colors.pressedColor = new Color(.86f, .88f, .8f); colors.selectedColor = Color.white; colors.fadeDuration = .12f; button.colors = colors;
        }

        public static void Word(Button button, string id, int state, string meaning, bool selected, Font font, Action changed, bool interactive)
        {
            button.targetGraphic.color = state == 2 ? new Color32(242, 224, 188, 255) :
                state == 1 ? new Color32(195, 213, 213, 255) : new Color32(207, 215, 188, 255);
            button.transition = Selectable.Transition.None;
            if (interactive) button.gameObject.AddComponent<LifeJournalWord>().Initialize(id, state, meaning, selected, font, changed);
        }

        public static void SelectWord(Transform book, string id)
        {
            foreach (LifeJournalWord word in book.GetComponentsInChildren<LifeJournalWord>(true)) word.SetSelected(word.GlyphId == id);
        }

        public static void PhotographyTab(RectTransform book, bool selected, UnityAction click)
        {
            if (selected)
                foreach (string name in new[] { "Symbols", "Photos" })
                    book.Find(name + " tab").GetComponent<RawImage>().texture = Resources.Load<Texture2D>("AlbumBook/Tab" + name + "Idle");
            var rect = LifeUi.Rect(book, "Photography tasks tab", new Vector2(547f - 836f, 377f), new Vector2(82, 69));
            rect.SetSiblingIndex(3);
            var graphic = rect.gameObject.AddComponent<LifeJournalTabGraphic>();
            graphic.selected = selected;
            var button = rect.gameObject.AddComponent<Button>(); button.targetGraphic = graphic;
            button.onClick.AddListener(click);
            ColorBlock colors = button.colors; colors.highlightedColor = new Color(1, .98f, .92f); colors.pressedColor = new Color(.86f, .88f, .8f); colors.selectedColor = Color.white; colors.fadeDuration = .12f; button.colors = colors;
        }

        public static void BookIcons(RectTransform book, bool photos, bool gestures, bool tasks)
        {
            TabSymbol((RectTransform)book.Find("Symbols tab"), LifeJournalTabGraphic.Symbol.Dictionary, !photos && !gestures && !tasks);
            TabSymbol((RectTransform)book.Find("Photos tab"), LifeJournalTabGraphic.Symbol.Photo, photos && !gestures && !tasks);
            TabSymbol((RectTransform)book.Find("Gesture learning tab"), LifeJournalTabGraphic.Symbol.Sun, gestures);
            TabSymbol((RectTransform)book.Find("Photography tasks tab"), LifeJournalTabGraphic.Symbol.Tasks, tasks);
        }

        private static void TabSymbol(RectTransform tab, LifeJournalTabGraphic.Symbol symbol, bool selected)
        {
            if (!tab) return;
            var button = tab.GetComponent<Button>();
            var original = tab.GetComponent<RawImage>();
            var paper = button.targetGraphic as LifeJournalTabGraphic;
            if (original)
            {
                // Keep the original texture reference for page selection; draw one shared paper style.
                original.enabled = false;
                paper = LifeUi.Rect(tab, "Tab paper", Vector2.zero, tab.sizeDelta).gameObject.AddComponent<LifeJournalTabGraphic>();
                button.targetGraphic = paper;
            }
            paper.selected = selected;
            paper.symbol = symbol;
            paper.SetVerticesDirty();
        }
    }

    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class LifeJournalTabGraphic : MaskableGraphic
    {
        public enum Symbol { Sun, Dictionary, Photo, Tasks }
        public bool selected;
        public bool iconOnly;
        public Symbol symbol;
        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            Rect rect = GetPixelAdjustedRect();
            Color paper = selected ? new Color32(180, 140, 108, 255) : new Color32(246, 241, 216, 255);
            paper *= color;
            const int steps = 10;
            if (!iconOnly)
            {
            mesh.AddVert(rect.center, paper, Vector2.zero);
            mesh.AddVert(new Vector2(rect.xMin, rect.yMin), paper, Vector2.zero);
            for (int i = 0; i <= steps; i++)
            {
                float a = Mathf.Lerp(Mathf.PI, Mathf.PI * .5f, i / (float)steps);
                mesh.AddVert(new Vector2(rect.xMin + 16 + Mathf.Cos(a) * 16, rect.yMax - 16 + Mathf.Sin(a) * 16), paper, Vector2.zero);
            }
            for (int i = 0; i <= steps; i++)
            {
                float a = Mathf.Lerp(Mathf.PI * .5f, 0, i / (float)steps);
                mesh.AddVert(new Vector2(rect.xMax - 10 + Mathf.Cos(a) * 10, rect.yMax - 10 + Mathf.Sin(a) * 10), paper, Vector2.zero);
            }
            mesh.AddVert(new Vector2(rect.xMax, rect.yMin), paper, Vector2.zero);
            int count = mesh.currentVertCount - 1;
            for (int i = 1; i <= count; i++) mesh.AddTriangle(0, i, i == count ? 1 : i + 1);
            }

            Color ink = iconOnly ? color : (selected ? new Color32(246, 241, 216, 255) : new Color32(180, 140, 108, 255)) * color;
            // Reuse the approved SVG's internal paths, centered on the existing tab symbol position.
            Vector2 Point(float x, float y) => new Vector2((x - 20f) * 2f, 8f + (19f - y) * 2f);
            void Stroke(float x1, float y1, float x2, float y2) => Line(mesh, Point(x1, y1), Point(x2, y2), 1.9f, ink);
            if (symbol == Symbol.Dictionary)
            {
                for (int y = 13; y <= 25; y += 6) { Stroke(13, y, 16, y); Stroke(20, y, y == 25 ? 24 : 27, y); }
            }
            else if (symbol == Symbol.Tasks)
            {
                Stroke(12, 13, 14, 15); Stroke(14, 15, 18, 10); Stroke(21, 13, 28, 13);
                for (int y = 19; y <= 26; y += 7)
                { Stroke(12, y-2, 16, y-2); Stroke(16, y-2, 16, y+2); Stroke(16, y+2, 12, y+2); Stroke(12, y+2, 12, y-2); Stroke(21, y, 28, y); }
            }
            else if (symbol == Symbol.Photo)
            {
                Stroke(12.5f, 12, 28.5f, 12); Stroke(28.5f, 12, 28.5f, 28);
                Stroke(28.5f, 28, 12.5f, 28); Stroke(12.5f, 28, 12.5f, 12);
                Stroke(13, 25, 18, 19); Stroke(18, 19, 21, 22); Stroke(21, 22, 24, 18); Stroke(24, 18, 28, 23);
                int at = mesh.currentVertCount; Vector2 center = Point(17, 16);
                mesh.AddVert(center, ink, Vector2.zero);
                for (int i = 0; i <= 12; i++)
                {
                    float a = i * Mathf.PI * 2f / 12f;
                    mesh.AddVert(center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 1.4f, ink, Vector2.zero);
                    if (i > 0) mesh.AddTriangle(at, at + i, at + i + 1);
                }
            }
            else
            {
                for (int i = 0; i < 32; i++)
                {
                    float a = i * Mathf.PI * 2f / 32f, b = (i + 1) * Mathf.PI * 2f / 32f;
                    Stroke(20 + Mathf.Cos(a) * 3.5f, 19 + Mathf.Sin(a) * 3.5f,
                        20 + Mathf.Cos(b) * 3.5f, 19 + Mathf.Sin(b) * 3.5f);
                }
                Stroke(20, 10, 20, 12); Stroke(20, 26, 20, 28); Stroke(11, 19, 13, 19); Stroke(27, 19, 29, 19);
                Stroke(13.6f, 12.6f, 15, 14); Stroke(25, 24, 26.4f, 25.4f);
                Stroke(13.6f, 25.4f, 15, 24); Stroke(25, 14, 26.4f, 12.6f);
            }
        }

        private static void Line(VertexHelper mesh, Vector2 a, Vector2 b, float width, Color tint)
        {
            Vector2 delta = b - a, n = new Vector2(-delta.y, delta.x).normalized * width;
            int at = mesh.currentVertCount;
            mesh.AddVert(a - n, tint, Vector2.zero); mesh.AddVert(a + n, tint, Vector2.zero);
            mesh.AddVert(b + n, tint, Vector2.zero); mesh.AddVert(b - n, tint, Vector2.zero);
            mesh.AddTriangle(at, at + 1, at + 2); mesh.AddTriangle(at, at + 2, at + 3);
        }
    }

    public sealed class LifeJournalWord : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, ISelectHandler, IDeselectHandler
    {
        public string GlyphId { get; private set; }
        public int State { get; private set; }
        public bool OutlineVisible => outline && outline.gameObject.activeSelf;
        public bool TooltipVisible => tooltip && tooltip.gameObject.activeSelf;
        private LifeJournalWordOutlineGraphic outline;
        private RectTransform tooltip;
        private Button button;
        private Action changed;
        private bool hovered, selected, focused;
        private int sibling;

        internal void Initialize(string id, int state, string meaning, bool isSelected, Font font, Action onChanged)
        {
            GlyphId = id; State = state; selected = isSelected; changed = onChanged; button = GetComponent<Button>();
            outline = LifeUi.Rect(transform, "Word outline", Vector2.zero, new Vector2(88, 90)).gameObject.AddComponent<LifeJournalWordOutlineGraphic>();
            outline.raycastTarget = false; outline.color = new Color32(86, 126, 64, 255);
            outline.gameObject.SetActive(selected);
            if (string.IsNullOrEmpty(meaning)) return;
            Vector2 size = new Vector2(Mathf.Clamp(meaning.Length * 18 + 28, 76, 148), 48);
            var word = (RectTransform)transform;
            float right = Mathf.Min(64f, 570f - word.anchoredPosition.x - size.x * .5f);
            var bubble = ProceduralUiLibrary.Create(transform, "Word meaning tooltip", UiMark.AnimalBubble,
                new Vector2(right, 60), size, ProceduralUiLibrary.Cream);
            tooltip = bubble.rectTransform;
            float inset = 2f + Mathf.Min(size.x, size.y) * bubble.waviness;
            float center = (size.y - 2f * inset) * bubble.tailSize * .5f;
            LifeUi.Text(tooltip, "Confirmed word meaning", meaning, Vector2.up * center, new Vector2(size.x - 18, 28), font, 18,
                ProceduralUiLibrary.Ink, TextAnchor.MiddleCenter);
            tooltip.gameObject.SetActive(false);
        }

        public void SetSelected(bool value) { if (selected == value) return; selected = value; Refresh(); }
        public void OnPointerEnter(PointerEventData eventData)
        {
            if (!button || !button.IsInteractable() || hovered) return;
            hovered = true; sibling = transform.GetSiblingIndex(); transform.SetAsLastSibling(); Refresh();
        }
        public void OnPointerExit(PointerEventData eventData)
        {
            if (!hovered) return;
            hovered = false; transform.SetSiblingIndex(sibling); Refresh();
        }
        public void OnSelect(BaseEventData eventData) { focused = true; Refresh(); }
        public void OnDeselect(BaseEventData eventData) { focused = false; Refresh(); }
        private void OnDisable() { hovered = focused = false; Refresh(); }
        private void Refresh()
        {
            if (!outline) return;
            outline.gameObject.SetActive(hovered || selected || focused);
            if (tooltip) tooltip.gameObject.SetActive(hovered);
            changed?.Invoke();
        }
    }

    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class LifeJournalWordOutlineGraphic : MaskableGraphic
    {
        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            Color tint = color;
            const float width = 1.2f;
            Vector2 radius = GetPixelAdjustedRect().size * .5f - Vector2.one * 5;
            for (int i = 0; i <= 72; i++)
            {
                float angle = i * Mathf.PI * 2 / 72; Vector2 direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                Vector2 point = Vector2.Scale(direction, radius) * (1 + .008f * Mathf.Sin(angle * 5));
                Color transparent = tint; transparent.a = 0;
                mesh.AddVert(point - direction * (width + 1), transparent, Vector2.zero);
                mesh.AddVert(point - direction * width, tint, Vector2.zero);
                mesh.AddVert(point + direction * width, tint, Vector2.zero);
                mesh.AddVert(point + direction * (width + 1), transparent, Vector2.zero);
                if (i == 0) continue;
                int at = i * 4;
                for (int band = 0; band < 3; band++) { mesh.AddTriangle(at + band - 4, at + band, at + band + 1); mesh.AddTriangle(at + band - 4, at + band + 1, at + band - 3); }
            }
        }
    }
}
