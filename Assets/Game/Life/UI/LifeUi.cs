using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace WeatherHouse.Life
{
    internal static class LifeUi
    {
        internal static readonly Color Ink = new Color(.19f, .28f, .23f);
        internal static RectTransform Rect(Transform parent, string name, Vector2 position, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform)); go.layer = 5; go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform; rect.anchoredPosition = position; rect.sizeDelta = size; return rect;
        }
        internal static Text Text(Transform parent, string name, string content, Vector2 position, Vector2 size, Font font, int fontSize, Color color, TextAnchor alignment = TextAnchor.MiddleLeft)
        {
            var text = Rect(parent, name, position, size).gameObject.AddComponent<Text>(); text.font = font;
            text.fontSize = fontSize; text.color = color; text.text = content; text.alignment = alignment;
            text.raycastTarget = false; text.horizontalOverflow = HorizontalWrapMode.Wrap; text.verticalOverflow = VerticalWrapMode.Truncate;
            return text;
        }
        internal static Image Panel(Transform parent, string name, Vector2 position, Vector2 size, Color color, bool raycast = false)
        {
            var image = Rect(parent, name, position, size).gameObject.AddComponent<Image>(); image.color = color; image.raycastTarget = raycast; return image;
        }
        internal static Button Button(Transform parent, string name, string title, Vector2 position, Vector2 size, Font font, UnityAction clicked)
        {
            var image = Panel(parent, name, position, size, new Color(.82f, .86f, .73f), true);
            var button = image.gameObject.AddComponent<Button>(); button.targetGraphic = image; button.onClick.AddListener(clicked);
            button.onClick.AddListener(() => LifeAudio.Play(LifeAudioCue.Ui));
            Text(image.transform, "Label", title, Vector2.zero, size - Vector2.one * 10, font, 20, Ink, TextAnchor.MiddleCenter); return button;
        }
    }
}
