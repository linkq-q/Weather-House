#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using WeatherHousePrototype;
using Object = UnityEngine.Object;

namespace WeatherHouse.Life.Editor
{
    // Small, synchronous checks for the actual photo token renderer; no scene or save is written.
    public static class PhotoGlyphDetailChecks
    {
        public static string RunModel(GlyphCatalog catalog)
        {
            var checks = new List<string>();
            void Check(bool value, string message) { Require(value, message); checks.Add("PASS " + message); }
            string[] Show(ObservationRecord photo) => LifePhotoBook.PhotoGlyphs(photo, catalog);

            var grass = new ObservationRecord { id = "old-grass", matchedPhotoTaskIds = new[] { "P03" } };
            Check(Show(grass).SequenceEqual(new[] { "grass" }), "A legacy task-only grass photo displays grass without speech or inscription metadata");
            var reloaded = JsonUtility.FromJson<ObservationRecord>(JsonUtility.ToJson(grass));
            Check(Show(reloaded).SequenceEqual(new[] { "grass" }), "Persisted matching IDs are sufficient when reopening an old photo");

            var allSources = new ObservationRecord { id = "three-sources", glyphIds = new[] { "hello", "self", "like", "sea", "look" },
                matchedPhotoTaskIds = new[] { "P03", "P04", "P03" } };
            string unchanged = JsonUtility.ToJson(allSources);
            Check(Show(allSources).SequenceEqual(new[] { "hello", "self", "like", "sea", "look", "grass", "wet" }),
                "Animal speech and stone letters retain their order; only matching photo rewards are appended once");
            Check(JsonUtility.ToJson(allSources) == unchanged, "Displaying a photo does not rewrite its stored speech, IDs or shutter facts");
            Check(Show(new ObservationRecord { glyphIds = new[] { "hello", "hello", "grass" }, matchedPhotoTaskIds = new[] { "P03", "P03" } })
                .SequenceEqual(new[] { "hello", "hello", "grass" }), "Intentional speech repetition remains while appended task duplicates are removed");
            Check(Show(new ObservationRecord { matchedPhotoTaskIds = new[] { "P04" } }).SequenceEqual(new[] { "wet" }),
                "A task's full token sentence is not substituted for its actual rewards");
            Check(Show(new ObservationRecord { matchedPhotoTaskIds = new[] { "P16" } }).SequenceEqual(new[] { "strong" }),
                "A strong-rain photo does not invent wind from P16's task-description tokens");
            Check(Show(new ObservationRecord { matchedPhotoTaskIds = new[] { "P22" } }).SequenceEqual(new[] { "together" }),
                "A shared activity photo does not invent the sheep/deer/water tokens from its task description");
            Check(Show(new ObservationRecord { glyphIds = new[] { "hello" } }).SequenceEqual(new[] { "hello" }),
                "Unmatched tasks are never added to an ordinary speech photo");
            Check(Show(new ObservationRecord { glyphIds = new[] { "sea", null, "unknown-glyph", "" }, matchedPhotoTaskIds = new[] { "not-a-task", "I01", "P03" } })
                .SequenceEqual(new[] { "sea", "grass" }), "Unknown glyph/task IDs and non-photo interaction matches are conservatively ignored");
            Check(Show(null).Length == 0 && Show(new ObservationRecord()).Length == 0 &&
                LifePhotoBook.PhotoGlyphs(grass, null).Length == 0, "Missing metadata and catalogs produce no invented letters");
            var withoutGrass = new GlyphCatalog { glyphs = catalog.glyphs.Where(g => g.id != "grass").ToArray() };
            Check(LifePhotoBook.PhotoGlyphs(grass, withoutGrass).Length == 0, "A task reward absent from the active catalog cannot be displayed");

            var book = new LanguageNotebook(); book.Normalize(catalog);
            book.AddObservation(new ObservationRecord { id = "actual-grass-shutter", glyphIds = Array.Empty<string>(), photoSnapshot = new LifePhotoSnapshot
                { entities = new[] { new LifePhotoEntity { id = "grass.fixture", kind = "grass", bodyVisible = true } } } });
            Check(book.ApplyPhotoTasks() == 1 && Show(book.observations[0]).SequenceEqual(new[] { "grass" }),
                "The real P03 matcher feeds the photo-detail display even when the original glyphIds array is empty");
            Check(book.observations[0].glyphIds.Length == 0 && book.GetWord("grass").encountered && !book.GetWord("grass").confirmed && book.encounters.Count == 0,
                "Displaying task evidence preserves its original empty speech and never creates animal encounters or confirmed words");
            return string.Join("\n", checks);
        }

        public static string RunUi(GameplaySession game, Font font = null)
        {
            Require(game && game.Catalog != null && game.Notebook != null, "Photo UI checks require an initialized isolated GameplaySession");
            if (!font) font = Resources.Load<Font>("Fonts/ZCOOLXiaoWei-Regular");
            Require(font, "The real UI font is available");
            var checks = new List<string>();
            void Check(bool value, string message) { Require(value, message); checks.Add("PASS " + message); }
            string original = JsonUtility.ToJson(game.Notebook);
            GameObject fixture = null;
            try
            {
                JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(new LanguageNotebook()), game.Notebook);
                game.Notebook.Normalize(game.Catalog);
                var photo = new ObservationRecord { glyphIds = new[] { "hello", "sea" }, matchedPhotoTaskIds = new[] { "P03", "P04" } };
                string[] shown = LifePhotoBook.PhotoGlyphs(photo, game.Catalog);
                foreach (string id in shown) game.Notebook.GetWord(id).encountered = true;
                fixture = new GameObject("Photo glyph detail regression", typeof(RectTransform), typeof(Canvas));
                Canvas canvas = fixture.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                var unknown = new GameObject("Unconfirmed photograph row", typeof(RectTransform));
                unknown.transform.SetParent(fixture.transform, false);
                LifePhotoBook.TokenLine(unknown.transform, game, shown, Vector2.zero, 38, 950, font);
                Canvas.ForceUpdateCanvases();
                AnimalGlyphGraphic[] glyphs = unknown.GetComponentsInChildren<AnimalGlyphGraphic>();
                Check(glyphs.Select(g => g.name).SequenceEqual(new[] { "Token hello", "Token sea", "Token grass", "Token wet" }),
                    "The actual photo token UI creates speech, stone and task glyphs in the expected order");
                Check(glyphs.All(g => g.SvgBody == game.Catalog.glyphs.Single(d => "Token " + d.id == g.name).body && HasCanvasMesh(g)),
                    "Every unconfirmed letter uses the authored SVG and has real CanvasRenderer geometry");
                Check(!unknown.GetComponentsInChildren<Text>().Any(), "Unconfirmed photo letters expose glyphs without any Chinese meaning label");

                game.Notebook.GetWord("grass").confirmed = true;
                var confirmed = new GameObject("Mixed known photograph row", typeof(RectTransform));
                confirmed.transform.SetParent(fixture.transform, false);
                LifePhotoBook.TokenLine(confirmed.transform, game, shown, new Vector2(0, -120), 38, 950, font);
                Canvas.ForceUpdateCanvases();
                Text[] labels = confirmed.GetComponentsInChildren<Text>();
                Check(labels.Length == 1 && labels[0].name == "Meaning grass" && labels[0].text == game.Catalog.glyphs.Single(g => g.id == "grass").name,
                    "Only an already-confirmed word receives its Chinese meaning under the photo");
                Check(confirmed.GetComponentsInChildren<AnimalGlyphGraphic>().All(HasCanvasMesh), "Confirming one word retains every other glyph's UI geometry");
                Check(!game.Notebook.GetWord("hello").confirmed && !game.Notebook.GetWord("sea").confirmed && !game.Notebook.GetWord("wet").confirmed,
                    "Rendering the row never confirms neighboring speech, stone or reward letters");
            }
            finally
            {
                if (fixture) Object.DestroyImmediate(fixture);
                JsonUtility.FromJsonOverwrite(original, game.Notebook);
            }
            return string.Join("\n", checks);
        }

        static bool HasCanvasMesh(AnimalGlyphGraphic graphic)
        {
            Mesh mesh = graphic.canvasRenderer.GetMesh();
            return mesh && mesh.vertexCount > 0 && mesh.triangles.Length > 0 && mesh.vertices.All(v =>
                !float.IsNaN(v.x) && !float.IsInfinity(v.x) && !float.IsNaN(v.y) && !float.IsInfinity(v.y));
        }

        static void Require(bool condition, string message)
        { if (!condition) throw new InvalidOperationException(message); }
    }
}
#endif
