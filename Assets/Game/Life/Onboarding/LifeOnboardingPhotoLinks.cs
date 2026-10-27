using System;
using UnityEngine;
using UnityEngine.UI;

namespace WeatherHouse.Life
{
    public static class LifeOnboardingPhotoLinks
    {
        public static void Add(Transform parent, string[] ids, Vector2 center, float height, float width, Action<string> open)
        {
            if (ids == null || ids.Length == 0) return;
            float step = Mathf.Min(height * 1.5f, width / ids.Length); height = Mathf.Min(height, step * .8f);
            for (int i = 0; i < ids.Length; i++)
            {
                string id = ids[i];
                var image = LifeUi.Panel(parent, "Photo word link " + id, center + Vector2.right * ((i - (ids.Length - 1) * .5f) * step),
                    new Vector2(step, height + 36), Color.clear, true);
                var button = image.gameObject.AddComponent<Button>(); button.targetGraphic = image;
                var colors = button.colors; colors.highlightedColor = colors.pressedColor = Color.white; button.colors = colors;
                button.onClick.AddListener(() => open(id));
            }
        }
    }
}
