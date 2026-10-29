using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using WeatherHousePrototype;

namespace WeatherHouse.Life.Editor
{
    public static class LifePhotoTaskTextChecks
    {
        [MenuItem("Weather House/Review 20261027/Check photo task text isolated")]
        public static void Run()
        {
            var life = UnityEngine.Object.FindObjectOfType<LifeSession>();
            if (!Application.isPlaying || !life || !Path.GetFullPath(GameplaySession.PersistenceDirectory).StartsWith(
                Path.GetFullPath("LifeGameplayReview") + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Task text checks require isolated Play.");
            var game = life.Gameplay;
            string original = JsonUtility.ToJson(game.Notebook);
            var root = new GameObject("Task text fixture", typeof(RectTransform));
            var checks = new List<string>();
            void Check(bool value, string label) => checks.Add((value ? "PASS " : "FAIL ") + label);
            try
            {
                JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(new LanguageNotebook()), game.Notebook);
                game.Notebook.Normalize(game.Catalog);
                var font = Resources.Load<Font>("Fonts/ZCOOLXiaoWei-Regular");
                if (!font) font = life.GetComponent<GameplayHud>().GetComponentInChildren<Text>(true)?.font;
                string opened = null;
                LifePhotoBook.Build(root.transform, game, 0, font, id => opened = id, _ => { });
                var rows = root.GetComponentsInChildren<RectTransform>().Where(t => t.name.StartsWith("Photo task ", StringComparison.Ordinal)).ToArray();
                Check(rows.Length == 6, "A task spread keeps its six rows");
                Check(!root.transform.Find("Previous photography page") && !root.transform.Find("Next photography page"),
                    "Task navigation uses the shared page drag without arrow buttons");
                Check(rows.SelectMany(row => row.GetComponentsInChildren<AnimalGlyphGraphic>()).All(glyph =>
                    Mathf.Abs(glyph.rectTransform.rect.height - 61.2f) < .01f), "Task glyph descriptions are enlarged by 1.2");
                Check(rows.All(row => !row.GetComponentsInChildren<Text>().Any(text => !string.IsNullOrEmpty(text.text))),
                    "Unfinished task rows contain only animal glyphs, without instruction text");
                Check(root.transform.Find("Photography heading").GetComponent<Text>().text == "留给照片的页",
                    "The approved photo page title remains");
                foreach (Transform child in root.transform.Cast<Transform>().ToArray()) UnityEngine.Object.DestroyImmediate(child.gameObject);
                game.Notebook.GetWord("fox").encountered = true; game.Notebook.GetWord("fox").confirmed = true;
                game.Notebook.completedPhotoTasks.Add("P01");
                game.Notebook.AddObservation(new ObservationRecord { id = "task-text-fixture", frameCount = 1,
                    glyphIds = new[] { "fox" }, matchedPhotoTaskIds = new[] { "P01" } });
                LifePhotoBook.Build(root.transform, game, 0, font, id => opened = id, _ => { });
                Transform rowKnown = root.transform.Find("Photo task P01");
                Check(!rowKnown.GetComponentsInChildren<Text>().Any(text => text.name.StartsWith("Meaning ", StringComparison.Ordinal) || text.name == "Task kind"),
                    "Knowing the word never adds a Chinese translation or instruction to the task row");
                Check(rowKnown.Find("Recorded seal") && rowKnown.GetComponentInChildren<AnimalGlyphGraphic>(),
                    "Completed tasks retain their check mark and animal glyph");
                rowKnown.GetComponentInChildren<Button>().onClick.Invoke();
                Check(opened == "P01", "Clicking a completed task still opens its matching photo evidence");
                var detail = new GameObject("Photo detail fixture", typeof(RectTransform)); detail.transform.SetParent(root.transform, false);
                LifePhotoBook.TokenLine(detail.transform, game, new[] { "fox" }, Vector2.zero, 51, 370, font);
                Check(detail.GetComponentsInChildren<Text>().Any(text => text.name == "Meaning fox"),
                    "Existing confirmed-word labels in photo details remain available");
            }
            finally
            {
                JsonUtility.FromJsonOverwrite(original, game.Notebook); game.Notebook.Normalize(game.Catalog);
                UnityEngine.Object.DestroyImmediate(root);
                File.WriteAllLines(Path.Combine(GameplaySession.PersistenceDirectory, "photo-task-text-checks.txt"), checks);
            }
            if (checks.Any(line => line.StartsWith("FAIL ", StringComparison.Ordinal))) throw new InvalidOperationException(string.Join("\n", checks));
        }
    }
}
