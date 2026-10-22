using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace WeatherHousePrototype
{
    public static class ProceduralUiStyleChecks
    {
        private const string AllFieldsJson = "{\"version\":1,\"lineScale\":1.8,\"fontScale\":1.25,\"fontWeight\":700," +
            "\"waviness\":0.041,\"bubbleRadius\":0.29,\"tailSize\":0.21,\"cornerRadius\":31," +
            "\"shutterInnerRatio\":0.36,\"shutterOuterLineScale\":1.6,\"shutterInnerLineScale\":0.72,\"closeGap\":67," +
            "\"ink\":\"#123456\",\"cream\":\"#abcdef\",\"player\":\"#987654\"}";

        [MenuItem("Weather House/Validate UI Style")]
        public static void Run()
        {
            var report = new List<string>();
            try
            {
                var asset = Resources.Load<TextAsset>("WeatherHouseUiStyle");
                Check(asset != null, "The shared Resources configuration exists", report);
                Check(ProceduralUiStyle.Parse(asset.text) != null, "The resource satisfies the style schema", report);

                var defaults = new ProceduralUiStyle();
                SameStyle(defaults, ProceduralUiStyle.Parse(JsonUtility.ToJson(defaults)), "Parse the complete default configuration", report);
                var expected = new ProceduralUiStyle
                {
                    version = 1, lineScale = 1.8f, fontScale = 1.25f, fontWeight = 700,
                    waviness = .041f, bubbleRadius = .29f, tailSize = .21f, cornerRadius = 31f,
                    shutterInnerRatio = .36f, shutterOuterLineScale = 1.6f, shutterInnerLineScale = .72f, closeGap = 67f,
                    ink = "#123456", cream = "#abcdef", player = "#987654"
                };
                var parsed = ProceduralUiStyle.Parse(AllFieldsJson);
                SameStyle(expected, parsed, "Parse all explicitly supplied fields", report);
                SameStyle(expected, ProceduralUiStyle.Parse(JsonUtility.ToJson(parsed)), "JSON roundtrip preserves every custom field", report);

                Reject(null, "Null configuration", report);
                Reject("  ", "Empty configuration", report);
                RejectChange(s => s.version = 0, "Old version", report);
                RejectChange(s => s.version = 2, "Unsupported future version", report);
                RejectChange(s => s.fontWeight = 500, "Unsupported font weight", report);
                Range((s, v) => s.lineScale = v, .4f, 2.5f, "lineScale", report);
                Range((s, v) => s.fontScale = v, .7f, 1.6f, "fontScale", report);
                Range((s, v) => s.waviness = v, 0f, .05f, "waviness", report);
                Range((s, v) => s.bubbleRadius = v, .15f, .5f, "bubbleRadius", report);
                Range((s, v) => s.tailSize = v, 0f, .24f, "tailSize", report);
                Range((s, v) => s.cornerRadius = v, 0f, 40f, "cornerRadius", report);
                Range((s, v) => s.shutterInnerRatio = v, .22f, .38f, "shutterInnerRatio", report);
                Range((s, v) => s.shutterOuterLineScale = v, .5f, 2f, "shutterOuterLineScale", report);
                Range((s, v) => s.shutterInnerLineScale = v, .2f, 1f, "shutterInnerLineScale", report);
                Range((s, v) => s.closeGap = v, 10f, 80f, "closeGap", report);
                foreach (string color in new[] { null, "red", "123456", "#123", "#12345678", "#gg1234" })
                    RejectChange(s => s.ink = color, "Invalid ink color: " + (color ?? "null"), report);
                RejectChange(s => s.cream = "#zzzzzz", "Invalid cream color", report);
                RejectChange(s => s.player = "#zzzzzz", "Invalid player color", report);
                Check(ProceduralUiStyle.ParseColor("#123456") == new Color32(0x12, 0x34, 0x56, 0xff),
                    "Hex colors map to the requested RGB values", report);
                CheckBindings(parsed, report);

                report.Add("Result: PASS (configuration and bindings; visual comparison is separate)");
                WriteReport(report, false);
                Debug.Log("UI style checks passed: PrototypeReview/ui-style.txt");
            }
            catch (Exception exception)
            {
                report.Add("Result: FAIL\n" + exception);
                WriteReport(report, false);
                throw;
            }
        }

        public static void CheckExportedFile(string path)
        {
            var report = new List<string> { "Browser export: " + path };
            try
            {
                var style = ProceduralUiStyle.Parse(File.ReadAllText(path));
                Check(Near(style.lineScale, 1.8f) && style.fontWeight == 700,
                    "The browser test export retains lineScale 1.8 and fontWeight 700", report);
                SameStyle(style, ProceduralUiStyle.Parse(JsonUtility.ToJson(style)), "Browser export roundtrip preserves all fields", report);
                CheckBindings(style, report);
                report.Add("Browser export result: PASS");
                WriteReport(report, true);
            }
            catch (Exception exception)
            {
                report.Add("Browser export result: FAIL\n" + exception);
                WriteReport(report, true);
                throw;
            }
        }

        private static void CheckBindings(ProceduralUiStyle style, List<string> report)
        {
            var scene = EditorSceneManager.NewPreviewScene();
            GameObject graphicObject = null, textObject = null;
            try
            {
                graphicObject = new GameObject("UI style test graphic", typeof(RectTransform), typeof(CanvasRenderer), typeof(ProceduralUiGraphic));
                SceneManager.MoveGameObjectToScene(graphicObject, scene);
                var graphic = graphicObject.GetComponent<ProceduralUiGraphic>();
                style.Apply(graphic, 3f);
                Check(Near(graphic.lineWidth, 3f * style.lineScale), "Graphic line width applies the scale to base width 3", report);
                Check(Near(graphic.waviness, style.waviness) && Near(graphic.bubbleRadius, style.bubbleRadius) &&
                    Near(graphic.tailSize, style.tailSize) && Near(graphic.cornerRadius, style.cornerRadius),
                    "Waviness, bubble radius, tail and corner radius reach the graphic", report);
                Check(Near(graphic.shutterInnerRatio, style.shutterInnerRatio) &&
                    Near(graphic.shutterOuterLineScale, style.shutterOuterLineScale) &&
                    Near(graphic.shutterInnerLineScale, style.shutterInnerLineScale) && Near(graphic.closeGap, style.closeGap),
                    "Shutter proportions, both shutter strokes and close gap reach the graphic", report);

                textObject = new GameObject("UI style test text", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
                SceneManager.MoveGameObjectToScene(textObject, scene);
                var text = textObject.GetComponent<Text>();
                style.ApplyText(text, 20);
                Check(text.fontSize == Mathf.RoundToInt(20 * style.fontScale) &&
                    text.fontStyle == (style.fontWeight == 700 ? FontStyle.Bold : FontStyle.Normal),
                    "Text applies the configured font scale and weight", report);
                new ProceduralUiStyle().ApplyText(text, 20);
                Check(text.fontSize == 20 && text.fontStyle == FontStyle.Normal,
                    "Applying default text style restores size and normal weight", report);
            }
            finally
            {
                if (textObject != null) UnityEngine.Object.DestroyImmediate(textObject);
                if (graphicObject != null) UnityEngine.Object.DestroyImmediate(graphicObject);
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        private static void Range(Action<ProceduralUiStyle, float> set, float min, float max, string name, List<string> report)
        {
            var style = new ProceduralUiStyle();
            set(style, min);
            Check(ProceduralUiStyle.Parse(JsonUtility.ToJson(style)) != null, "Accept " + name + " minimum", report);
            set(style, max);
            Check(ProceduralUiStyle.Parse(JsonUtility.ToJson(style)) != null, "Accept " + name + " maximum", report);
            RejectChange(s => set(s, min - .01f), name + " below range", report);
            RejectChange(s => set(s, max + .01f), name + " above range", report);
        }

        private static void RejectChange(Action<ProceduralUiStyle> change, string description, List<string> report)
        {
            var style = new ProceduralUiStyle();
            change(style);
            Reject(JsonUtility.ToJson(style), description, report);
        }

        private static void Reject(string json, string description, List<string> report)
        {
            bool rejected = false;
            try { ProceduralUiStyle.Parse(json); }
            catch (ArgumentException) { rejected = true; }
            Check(rejected, "Reject: " + description, report);
        }

        private static void SameStyle(ProceduralUiStyle a, ProceduralUiStyle b, string description, List<string> report)
        {
            Check(a.version == b.version && Near(a.lineScale, b.lineScale) && Near(a.fontScale, b.fontScale) &&
                a.fontWeight == b.fontWeight && Near(a.waviness, b.waviness) && Near(a.bubbleRadius, b.bubbleRadius) &&
                Near(a.tailSize, b.tailSize) && Near(a.cornerRadius, b.cornerRadius) && Near(a.shutterInnerRatio, b.shutterInnerRatio) &&
                Near(a.shutterOuterLineScale, b.shutterOuterLineScale) && Near(a.shutterInnerLineScale, b.shutterInnerLineScale) &&
                Near(a.closeGap, b.closeGap) && a.ink == b.ink && a.cream == b.cream && a.player == b.player, description, report);
        }

        private static bool Near(float a, float b) { return Mathf.Abs(a - b) < .00001f; }

        private static void Check(bool condition, string description, List<string> report)
        {
            if (!condition) throw new InvalidOperationException(description);
            report.Add("PASS: " + description);
        }

        private static void WriteReport(List<string> report, bool append)
        {
            Directory.CreateDirectory("PrototypeReview");
            string path = Path.Combine("PrototypeReview", "ui-style.txt");
            if (append) File.AppendAllLines(path, report);
            else File.WriteAllLines(path, report);
        }
    }
}
