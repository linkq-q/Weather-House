using UnityEngine;
using UnityEngine.UI;

namespace WeatherHousePrototype
{
    public static class ProceduralUiLibrary
    {
        public static Color Cream => ProceduralUiStyle.ParseColor(ProceduralUiStyle.Current.cream);
        public static Color Grass => ProceduralUiStyle.ParseColor(ProceduralUiStyle.Current.player);
        public static Color Ink => ProceduralUiStyle.ParseColor(ProceduralUiStyle.Current.ink);

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
