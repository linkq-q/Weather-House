using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace WeatherHouse.Life
{
    // Marks belong to the target's UI rect, so the physical journal captures the same page-space ink.
    [DefaultExecutionOrder(300)]
    public sealed class LifeOnboardingBookFocus : MonoBehaviour
    {
        readonly List<RectTransform> targets = new List<RectTransform>();
        readonly List<LifeOnboardingFocusGraphic> marks = new List<LifeOnboardingFocusGraphic>();
        LifeJournal3D journal; string signature; float nextPulse;
        public void Show(RectTransform book, RectTransform detail, string[] names, LifeJournal3D physicalBook, string gesture)
        {
            names = names ?? Array.Empty<string>();
            var found = new List<RectTransform>();
            var roots = new[] { detail, book }.Where(root => root && root.gameObject.activeInHierarchy).ToArray();
            foreach (string name in names)
            {
                var all = roots.SelectMany(root => root.GetComponentsInChildren<RectTransform>()).Where(t => t.gameObject.activeInHierarchy);
                RectTransform target = name == "First photo" ? all.FirstOrDefault(t => t.name.StartsWith("Photo ", StringComparison.Ordinal) && t.GetComponent<Button>()) :
                    name == "First photo word" ? all.FirstOrDefault(t => t.name.StartsWith("Photo word link ", StringComparison.Ordinal)) :
                    all.FirstOrDefault(t => t.name == (name == "Practice start" ? "Practice pad" : name));
                if (target) found.Add(target);
            }
            string key = string.Join("|", names) + "/" + gesture;
            if (key == signature && targets.SequenceEqual(found) && marks.All(mark => mark)) return;
            Clear(); signature = key; journal = physicalBook; targets.AddRange(found);
            int index = 0;
            foreach (string name in names)
            {
                var all = roots.SelectMany(root => root.GetComponentsInChildren<RectTransform>()).Where(t => t.gameObject.activeInHierarchy);
                var target = name == "First photo" ? all.FirstOrDefault(t => t.name.StartsWith("Photo ", StringComparison.Ordinal) && t.GetComponent<Button>()) :
                    name == "First photo word" ? all.FirstOrDefault(t => t.name.StartsWith("Photo word link ", StringComparison.Ordinal)) :
                    all.FirstOrDefault(t => t.name == (name == "Practice start" ? "Practice pad" : name));
                if (!target) continue;
                bool paper = book && target.IsChildOf(book);
                // New marks are added after Bind's cached layer list. Keep page marks on the existing
                // capture layer so they reach its RenderTexture; parent alpha hides the screen duplicate.
                var go = new GameObject("Tutorial gold focus " + index++, typeof(RectTransform)); go.layer = paper ? 31 : target.gameObject.layer;
                var rect = (RectTransform)go.transform; rect.SetParent(target, false);
                if (name == "Practice start")
                {
                    rect.anchorMin = rect.anchorMax = Vector2.one * .5f; rect.sizeDelta = Vector2.one * 25f;
                    rect.anchoredPosition = (GestureCatalog.Find(gesture)?.points[0] ?? Vector2.zero) * 55f + Vector2.down * 10f;
                }
                else
                { rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = Vector2.one * -5f; rect.offsetMax = Vector2.one * 5f; }
                var mark = go.AddComponent<LifeOnboardingFocusGraphic>(); mark.raycastTarget = false; mark.color = new Color(.71f, .53f, .20f, .68f); marks.Add(mark);
            }
            journal?.RequestContentRefresh(); nextPulse = 0;
        }
        void Update()
        {
            if (marks.Count == 0 || Time.unscaledTime < nextPulse) return;
            nextPulse = Time.unscaledTime + .2f;
            float alpha = .58f + .18f * Mathf.Sin(Time.unscaledTime * 2.1f);
            foreach (var mark in marks) if (mark) mark.color = new Color(.71f, .53f, .20f, alpha);
            journal?.RequestContentRefresh();
        }
        void Clear()
        {
            foreach (var mark in marks) if (mark) { mark.gameObject.SetActive(false); Destroy(mark.gameObject); }
            marks.Clear(); targets.Clear(); signature = null; journal?.RequestContentRefresh();
        }
        void OnDisable() => Clear();
        void OnDestroy() => Clear();
    }

    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class LifeOnboardingFocusGraphic : MaskableGraphic
    {
        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear(); Rect rect = GetPixelAdjustedRect(); Vector2 radius = rect.size * .5f - Vector2.one * 2;
            for (int i = 0; i <= 80; i++)
            {
                float angle = i * Mathf.PI * 2f / 80f; Vector2 direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                Vector2 point = rect.center + Vector2.Scale(direction, radius) * (1f + .012f * Mathf.Sin(angle * 5f + .6f));
                Color edge = color; edge.a = 0;
                mesh.AddVert(point - direction * 3f, edge, Vector2.zero); mesh.AddVert(point - direction * 1.7f, color, Vector2.zero);
                mesh.AddVert(point + direction * 1.7f, color, Vector2.zero); mesh.AddVert(point + direction * 3f, edge, Vector2.zero);
                if (i == 0) continue; int at = i * 4;
                for (int band = 0; band < 3; band++)
                { mesh.AddTriangle(at + band - 4, at + band, at + band + 1); mesh.AddTriangle(at + band - 4, at + band + 1, at + band - 3); }
            }
        }
    }
}
