using System;
using UnityEngine;
using UnityEngine.UI;
using WeatherHousePrototype;

namespace WeatherHouse.Life
{
    // One short, non-modal margin note. No full-screen image, input capture or world mask.
    public sealed class LifeOnboardingView : MonoBehaviour
    {
        RectTransform root; Text label; Button action; Text actionLabel; Action clicked;
        public void Build(Transform parent)
        {
            transform.SetParent(parent, false); gameObject.layer = 5;
            var canvas = gameObject.AddComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 145;
            var scaler = gameObject.AddComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1600, 1000); scaler.matchWidthOrHeight = .5f; gameObject.AddComponent<GraphicRaycaster>();
            var font = Resources.Load<Font>("Fonts/ZCOOLXiaoWei-Regular");
            root = LifeUi.Rect(transform, "Grandmother margin note", new Vector2(0, 56), new Vector2(0, 70));
            root.anchorMin = new Vector2(.04f, 0); root.anchorMax = new Vector2(.96f, 0);
            label = LifeUi.Text(root, "One contextual instruction", "", Vector2.zero, Vector2.zero, font, 23, Color.white, TextAnchor.MiddleCenter);
            label.rectTransform.anchorMin = Vector2.zero; label.rectTransform.anchorMax = Vector2.one;
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            AddShadow(label);
            var hit = LifeUi.Panel(root, "Continue note", new Vector2(-90, 0), new Vector2(170, 52), Color.clear, true);
            hit.rectTransform.anchorMin = hit.rectTransform.anchorMax = new Vector2(1, .5f);
            action = hit.gameObject.AddComponent<Button>(); action.targetGraphic = hit;
            action.onClick.AddListener(() => clicked?.Invoke());
            actionLabel = LifeUi.Text(hit.transform, "Label", "继续 →", Vector2.zero, new Vector2(164, 44), font, 22, Color.white, TextAnchor.MiddleCenter);
            action.targetGraphic = actionLabel; AddShadow(actionLabel);
        }
        public void Show(string text, string button, Action onClick, bool hidden)
        {
            if (!root) return; root.gameObject.SetActive(!hidden && !string.IsNullOrEmpty(text));
            bool hasAction = !string.IsNullOrEmpty(button);
            label.text = text; clicked = onClick; action.gameObject.SetActive(hasAction); actionLabel.text = hasAction ? button + " →" : "";
            label.rectTransform.offsetMin = new Vector2(12, 8);
            label.rectTransform.offsetMax = new Vector2(hasAction ? -200 : -12, -8);
        }
        static void AddShadow(Text text)
        {
            var shadow = text.gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(0, 0, 0, .65f); shadow.effectDistance = new Vector2(1, -1);
        }
    }
}
