using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using WeatherHousePrototype;

namespace WeatherHouse.Life
{
    public static class LifePhotoBook
    {
        public const int EntriesPerSpread = 6;
        public static int PageCount => Mathf.Max(1, Mathf.CeilToInt(LifePhotoRules.VisibleTasks.Count() / (float)EntriesPerSpread));

        public static string[] PhotoGlyphs(ObservationRecord photo, GlyphCatalog catalog)
        {
            if (photo == null || catalog?.glyphs == null) return Array.Empty<string>();
            var known = new HashSet<string>(catalog.glyphs.Where(g => g != null && !string.IsNullOrWhiteSpace(g.id)).Select(g => g.id), StringComparer.Ordinal);
            var result = new List<string>((photo.glyphIds ?? Array.Empty<string>()).Where(id => id != null && known.Contains(id)));
            var present = new HashSet<string>(result, StringComparer.Ordinal);
            // Display stored evidence without rewriting the original utterance or granting discoveries.
            foreach (string taskId in photo.matchedPhotoTaskIds ?? Array.Empty<string>())
            {
                var task = LifePhotoRules.Tasks.FirstOrDefault(item => item.id == taskId && !item.IsInteraction);
                foreach (string id in task?.rewards ?? Array.Empty<string>())
                    if (id != null && known.Contains(id) && present.Add(id)) result.Add(id);
            }
            return result.ToArray();
        }

        public static void Build(Transform book, GameplaySession session, int page, Font font, Action<string> openEvidence, Action<int> turn)
        {
            var tasks = LifePhotoRules.VisibleTasks.ToArray();
            page = Mathf.Clamp(page, 0, PageCount - 1);
            LifeUi.Text(book, "Photography heading", "留给照片的页", new Vector2(-290, 287), new Vector2(490, 44), font, 30, LifeUi.Ink);
            LifeUi.Text(book, "Photography page", (page + 1) + " / " + PageCount, new Vector2(290, 287), new Vector2(490, 44), font, 24, LifeUi.Ink, TextAnchor.MiddleRight);
            for (int slot = 0; slot < EntriesPerSpread; slot++)
            {
                int index = page * EntriesPerSpread + slot;
                if (index >= tasks.Length) break;
                var task = tasks[index];
                Vector2 position = new Vector2(slot < 3 ? -290 : 290, 175 - (slot % 3) * 180);
                var row = LifeUi.Rect(book, "Photo task " + task.id, position, new Vector2(485, 155));
                bool complete = session.Notebook.IsPhotoTaskComplete(task.id);
                var paper = LifeUi.Panel(row, "Task paper", Vector2.zero, new Vector2(485, 154), new Color(.80f, .83f, .68f, .16f), complete);
                TokenLine(row, session, task.tokens, new Vector2(-8, 5), 61.2f, 444, font, false);
                if (complete)
                {
                    var mark = LifeUi.Rect(row, "Recorded seal", new Vector2(209, 42), new Vector2(33, 33));
                    LifeUi.Text(mark, "Seal", "✓", Vector2.zero, new Vector2(33, 33), font, 27, new Color(.45f, .57f, .37f), TextAnchor.MiddleCenter);
                    if (task.IsInteraction || session.Notebook.FindTaskPhotos(task.id).Count > 0)
                    {
                        string id = task.id;
                        var button = paper.gameObject.AddComponent<Button>(); button.targetGraphic = paper;
                        button.onClick.AddListener(() => { LifeAudio.Play(LifeAudioCue.Ui); openEvidence(id); });
                    }
                }
            }
        }

        public static void TokenLine(Transform parent, GameplaySession session, string[] ids, Vector2 center, float height, float width, Font font, bool showMeanings = true)
        {
            if (ids == null || ids.Length == 0) return;
            float step = Mathf.Min(height * 1.5f, width / ids.Length);
            height = Mathf.Min(height, step * .8f);
            for (int i = 0; i < ids.Length; i++)
            {
                GlyphDefinition glyph = Array.Find(session.Catalog.glyphs, g => g.id == ids[i]);
                if (glyph == null) continue;
                Vector2 at = center + Vector2.right * ((i - (ids.Length - 1) * .5f) * step);
                var graphic = LifeUi.Rect(parent, "Token " + glyph.id, at, new Vector2(height * 5 / 6, height)).gameObject.AddComponent<AnimalGlyphGraphic>();
                graphic.SvgBody = glyph.body; graphic.lineWidth = 3.8f; graphic.color = LifeUi.Ink; graphic.raycastTarget = false;
                if (showMeanings && session.Notebook.GetWord(glyph.id)?.confirmed == true)
                    LifeUi.Text(parent, "Meaning " + glyph.id, glyph.name, at + Vector2.down * (height * .5f + 15), new Vector2(step + 5, 27), font, 20, LifeUi.Ink, TextAnchor.MiddleCenter);
            }
        }
    }
}
