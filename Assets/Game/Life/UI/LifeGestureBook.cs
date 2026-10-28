using System;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using WeatherHousePrototype;

namespace WeatherHouse.Life
{
    public static class LifeGestureBook
    {
        static readonly float[] RowPositions = { 190, 100, 6, -88, -182, -276 };

        public static void Build(RectTransform book, LifeSession life, LifeGestureInput input, Font font, string selected, UnityAction<string> select, UnityAction<string> showWord)
        {
            var definition = GestureCatalog.Find(selected) ?? GestureCatalog.All[0];
            LifeUi.Text(book, "Gesture book heading", "天气魔法施法手势", new Vector2(-288, 282), new Vector2(440, 64), font, 40, LifeUi.Ink, TextAnchor.MiddleCenter);
            for (int i = 0; i < GestureCatalog.All.Length; i++)
            {
                var gesture = GestureCatalog.All[i];
                bool unlocked = life.IsGestureUnlocked(gesture.id), chosen = definition.id == gesture.id;
                var button = LifeUi.Button(book, "Gesture " + gesture.id, "",
                    new Vector2(-288, RowPositions[i]), new Vector2(260, 62), font, () => select(gesture.id));
                button.GetComponent<Image>().color = new Color(.55f, .61f, .45f, chosen ? 0 : .11f);
                Glyph(button.transform, "Spell glyph", GlyphBody(life, gesture.id), Vector2.zero,
                    chosen ? new Vector2(76, 91) : new Vector2(39, 47), unlocked ? 1f : .35f);
            }
            bool available = life.IsGestureUnlocked(definition.id);
            Glyph(book, "Selected gesture", GlyphBody(life, definition.id), new Vector2(290, 220), new Vector2(104, 125), available ? 1f : .35f);
            if (!available) return;
            var panel = LifeUi.Panel(book, "Practice pad", new Vector2(290, -65), new Vector2(400, 380), new Color(.55f, .61f, .45f, .12f), true);
            var demoRoot = LifeUi.Rect(panel.transform, "Gesture demonstration", Vector2.zero, new Vector2(180, 150));
            var guide = LifeUi.Rect(demoRoot, "Complete gesture guide", Vector2.zero, new Vector2(180, 150)).gameObject.AddComponent<LifeStrokeGraphic>();
            guide.material = null; guide.color = new Color(.29f, .40f, .30f, .19f); guide.width = 3.2f; guide.raycastTarget = false;
            var guidePoints = new Vector2[definition.points.Length];
            for (int i = 0; i < guidePoints.Length; i++) guidePoints[i] = definition.points[i] * 65f;
            guide.SetPath(guidePoints);
            var demoInk = LifeUi.Rect(demoRoot, "Animated example", Vector2.zero, new Vector2(180, 150)).gameObject.AddComponent<LifeStrokeGraphic>();
            demoInk.color = new Color(.3f, .52f, .44f, .42f); demoInk.width = 5.5f; demoInk.raycastTarget = false;
            var marker = Glyph(demoRoot, "Moving pen tip", "<path d=\"M 47 57 Q 51 54 54 58 Q 56 62 51 64 Q 47 64 47 57\"/>", Vector2.zero, new Vector2(150, 150), .8f);
            marker.color = new Color(.63f, .42f, .21f, .55f); marker.raycastTarget = false;
            var demo = demoRoot.gameObject.AddComponent<LifeGestureDemo>(); demo.definition = definition; demo.ink = demoInk; demo.marker = marker.rectTransform;
            var stroke = LifeUi.Rect(panel.transform, "Practice ink", Vector2.zero, panel.rectTransform.sizeDelta).gameObject.AddComponent<LifeStrokeGraphic>(); stroke.width = 5.5f;
            Line(panel.transform, "Pencil corner upper", new[] { new Vector2(0, -20), Vector2.zero, new Vector2(23, 1) }, new Vector2(-188, 176));
            Line(panel.transform, "Pencil corner lower", new[] { new Vector2(-23, -1), Vector2.zero, new Vector2(0, 20) }, new Vector2(188, -176));
            var mouse = Glyph(panel.transform, "Practice mouse hint", "<path d=\"M 35 43 Q 35 25 50 25 Q 65 25 65 43 L 65 76 Q 65 93 50 93 Q 35 93 35 76 L 35 43 M 50 26 L 50 53 M 36 53 L 64 53 M 41 36 L 44 36 L 44 45 L 41 45 L 41 36\"/>", new Vector2(150, 142), new Vector2(52, 62), .5f);
            mouse.gameObject.SetActive(!input || !input.HasStartedPractice);
            var pad = panel.gameObject.AddComponent<LifeGesturePad>(); pad.input = input; pad.gestureId = definition.id; pad.ink = stroke; pad.mouseHint = mouse.gameObject;
            var eraser = LifeUi.Button(book, "Retry practice", "", new Vector2(290, -299), new Vector2(72, 72), font, pad.ResetStroke);
            eraser.GetComponent<Image>().color = Color.clear;
            Glyph(eraser.transform, "Eraser sketch", "<path d=\"M 22 75 L 57 35 Q 60 32 64 35 L 82 51 Q 85 54 81 58 L 49 94 L 38 94 L 22 75 M 36 58 L 62 81 M 38 96 L 72 97\"/>", Vector2.zero, new Vector2(53, 64), .7f);
        }

        static string GlyphBody(LifeSession life, string id)
        {
            var glyph = Array.Find(life.Gameplay.Catalog.glyphs, item => item.id == id);
            if (glyph == null) throw new InvalidOperationException("Spell glyph is missing from the production catalog: " + id);
            return glyph.body;
        }

        static AnimalGlyphGraphic Glyph(Transform parent, string name, string body, Vector2 position, Vector2 size, float opacity)
        {
            var glyph = LifeUi.Rect(parent, name, position, size).gameObject.AddComponent<AnimalGlyphGraphic>();
            glyph.SvgBody = body; glyph.lineWidth = 3.8f; glyph.color = new Color(LifeUi.Ink.r, LifeUi.Ink.g, LifeUi.Ink.b, opacity); glyph.raycastTarget = false;
            return glyph;
        }

        static void Line(Transform parent, string name, Vector2[] points, Vector2 position)
        {
            var line = LifeUi.Rect(parent, name, position, new Vector2(100, 50)).gameObject.AddComponent<LifeStrokeGraphic>();
            line.material = null; line.width = 1.4f; line.color = new Color(LifeUi.Ink.r, LifeUi.Ink.g, LifeUi.Ink.b, .2f); line.SetPath(points);
        }
    }
}
