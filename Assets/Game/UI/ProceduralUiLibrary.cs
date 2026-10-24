using UnityEngine;
using UnityEngine.UI;

namespace WeatherHousePrototype
{
    public static class ProceduralUiLibrary
    {
        public static Color Cream => ProceduralUiStyle.ParseColor(ProceduralUiStyle.Current.cream);
        public static Color Grass => ProceduralUiStyle.ParseColor(ProceduralUiStyle.Current.player);
        public static Color Ink => ProceduralUiStyle.ParseColor(ProceduralUiStyle.Current.ink);

        // Authored X is the width limit; Y remains the bubble height. One word starts square.
        public static Vector2 AnimalBubbleSize(Vector2 configured, int glyphCount, float glyphHeight, Vector2 canvasSize)
        {
            float height = Mathf.Max(64f, configured.y);
            float step = Mathf.Min(Mathf.Max(0f, glyphHeight), height * .52f) * .95f;
            float width = Mathf.Min(Mathf.Max(64f, configured.x), height + Mathf.Max(0, glyphCount - 1) * step);
            Vector2 size = new Vector2(width, height);
            if (canvasSize.x <= 0f || canvasSize.y <= 0f) return size;
            float fit = Mathf.Min(1f, Mathf.Max(1f, canvasSize.x - 48f) / width, canvasSize.y * .65f / height);
            return size * fit;
        }

        public static ProceduralUiGraphic Create(Transform parent, string name, UiMark mark,
            Vector2 position, Vector2 size, Color color, float lineWidth = 3f, bool raycastTarget = false)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(ProceduralUiGraphic));
            go.transform.SetParent(parent, false);
            go.layer = parent.gameObject.layer;
            var rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            var graphic = go.GetComponent<ProceduralUiGraphic>();
            graphic.mark = mark;
            graphic.color = color;
            ProceduralUiStyle.Current.Apply(graphic, lineWidth);
            graphic.raycastTarget = raycastTarget;
            return graphic;
        }

        public static Button Button(Transform parent, string name, UiMark mark, Vector2 position,
            Vector2 size, Color color, float lineWidth = 3f)
        {
            var graphic = Create(parent, name, mark, position, size, color, lineWidth, true);
            var button = graphic.gameObject.AddComponent<Button>();
            button.targetGraphic = graphic;
            var colors = button.colors;
            colors.highlightedColor = new Color(1f, 1f, 1f, 1f);
            colors.pressedColor = new Color(.78f, .83f, .72f, 1f);
            button.colors = colors;
            return button;
        }
    }
}
