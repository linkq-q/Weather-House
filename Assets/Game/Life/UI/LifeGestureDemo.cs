using System.Collections.Generic;
using UnityEngine;

namespace WeatherHouse.Life
{
    public sealed class LifeGestureDemo : MonoBehaviour
    {
        public GestureDefinition definition;
        public LifeStrokeGraphic ink;
        public RectTransform marker;
        private readonly List<Vector2> path = new List<Vector2>();
        private void Update()
        {
            if (definition == null || !ink) return;
            float phase = Mathf.Clamp01((Time.unscaledTime % 4f) / 3.2f);
            float total = GestureCatalog.Length(definition.points), target = phase * total, walked = 0;
            Vector2 offset = new Vector2(0, 0), end = definition.points[0];
            path.Clear(); path.Add(end * 65f + offset);
            for (int i = 1; i < definition.points.Length; i++)
            {
                float segment = Vector2.Distance(definition.points[i - 1], definition.points[i]);
                end = Vector2.Lerp(definition.points[i - 1], definition.points[i], segment < .001f ? 1f : (target - walked) / segment);
                path.Add(end * 65f + offset);
                if (walked + segment >= target) break;
                walked += segment;
            }
            ink.SetPath(path); if (marker) marker.anchoredPosition = end * 65f + offset;
        }
    }
}
