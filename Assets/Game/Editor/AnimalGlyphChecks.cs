using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace WeatherHousePrototype.Editor
{
    public static class AnimalGlyphChecks
    {
        [Serializable] private sealed class Glyphs { public Glyph[] glyphs; }
        [Serializable] private sealed class Glyph { public string id; public string body; }

        // Independent endpoints for the authored alphabet and approved revisions, in viewBox coordinates.
        private static readonly Dictionary<string, string> Endpoints = new Dictionary<string, string>
        {
            { "rain", "40 38 40 104;22 30 78 30;50 13 50 29;23 58 33 89" },
            { "sunlight", "41 15 41 15;50 54 50 103;28 69 28 87;72 69 72 87" },
            { "wind", "72 19 61 87" },
            { "clear", "24 27 52 52;76 93 48 68" },
            { "water", "40 38 40 104;26 20 52 20;26 20 40 77" },
            { "wet", "40 38 40 104;25 17 78 104" },
            { "dry", "12 43 12 78;40 38 40 104;25 17 78 104" },
            { "cold", "37 20 39 100;63 20 61 100" },
            { "warm", "28 20 26 100;72 20 74 100" },
            { "strong", "50 16 50 103;31 103 69 103;27 29 73 29;27 29 38 29;62 29 73 29;32 78 68 78" },
            { "weak", "50 16 50 103;31 103 69 103;27 53 73 53;27 53 40 83;73 53 60 83;32 29 68 29" },
            { "want", "36 19 36 103" },
            { "reject", "12 43 12 78;36 19 36 103" },
            { "tree", "50 14 50 52;32 19 68 19;50 52 50 90;32 57 68 57;50 90 50 105;32 105 68 105" },
            { "grass", "30 20 30 58;12 25 48 25;70 20 70 58;52 25 88 25;30 58 70 104;70 58 30 104" },
            { "home", "28 28 28 28" },
            { "here", "50 23 50 94;28 94 72 94" },
            { "there", "50 94 78 23;28 94 72 94" },
            { "come", "34 20 62 80;28 80 72 80" },
            { "go", "34 10 62 70;28 104 72 104" },
            { "first", "50 18 63 102;18 47 18 65;34 35 34 78" },
            { "then", "50 18 63 102;66 35 66 78;82 47 82 65" },
            { "hello", "28 23 72 103;29 42 46 42" },
            { "goodbye", "28 23 72 103;54 84 71 84" },
            { "self", "62 18 28 101;35 66 50 66" },
            { "you", "62 18 28 101;74 66 89 66" },
            { "like", "36 19 68 83" },
            { "look", "62 18 28 101;62 28 62 54;44 28 44 54" },
            { "sea", "40 28 40 94;20 106 82 106;76 78 76 106" },
            { "past", "50 18 63 102;50 38 50 76" }
        };

        [MenuItem("Weather House/Check Animal Glyph Geometry")]
        public static void Run()
        {
            var report = new List<string>();
            Directory.CreateDirectory("PrototypeReview");
            try
            {
                TextAsset asset = Resources.Load<TextAsset>("WeatherHouseGlyphs");
                Check(asset != null, "Original glyph resource exists");
                Glyph[] glyphs = JsonUtility.FromJson<Glyphs>(asset.text).glyphs;
                Check(glyphs.Length == 37, "Approved alphabet preserves 33 stable IDs and adds like, look, sea and past");
                var ids = new HashSet<string>();
                foreach (Glyph glyph in glyphs)
                {
                    Check(ids.Add(glyph.id), "Unique glyph: " + glyph.id);
                    List<List<Vector2>> paths = AnimalGlyphGraphic.ParseSvgBody(glyph.body);
                    string[] expected = Endpoints.TryGetValue(glyph.id, out string endpoint) ? endpoint.Split(';') : Array.Empty<string>();
                    if (expected.Length > 0) Check(paths.Count == expected.Length, glyph.id + " stroke count");
                    else Check(Array.IndexOf(new[] { "fox", "squirrel", "sheep", "deer", "sleep", "eat", "together" }, glyph.id) >= 0,
                        "Only the approved seven extend the alphabet");
                    for (int i = 0; i < expected.Length; i++)
                    {
                        string[] values = expected[i].Split(' ');
                        Vector2 from = new Vector2(float.Parse(values[0], CultureInfo.InvariantCulture), float.Parse(values[1], CultureInfo.InvariantCulture));
                        Vector2 to = new Vector2(float.Parse(values[2], CultureInfo.InvariantCulture), float.Parse(values[3], CultureInfo.InvariantCulture));
                        Check(Vector2.Distance(paths[i][0], from) < .001f && Vector2.Distance(paths[i][paths[i].Count - 1], to) < .001f,
                            glyph.id + " stroke " + i + " preserves authored endpoints");
                    }
                    foreach (Rect rect in new[] { new Rect(0f, 0f, 100f, 120f), new Rect(-24f, -24f, 48f, 48f), new Rect(0f, 0f, 240f, 120f), new Rect(0f, 0f, 24f, 28.8f) })
                    using (var mesh = new VertexHelper())
                    {
                        AnimalGlyphGraphic.DrawPaths(mesh, paths, rect, Color.white);
                        Check(mesh.currentVertCount > 0 && mesh.currentIndexCount > 0, glyph.id + " has geometry");
                        var vertex = new UIVertex();
                        for (int i = 0; i < mesh.currentVertCount; i++)
                        {
                            mesh.PopulateUIVertex(ref vertex, i);
                            Check(Finite(vertex.position.x) && Finite(vertex.position.y) && Finite(vertex.position.z), glyph.id + " finite vertex");
                            Check(rect.Contains(new Vector2(vertex.position.x, vertex.position.y)), glyph.id + " fits its rect");
                        }
                    }
                    report.Add("PASS " + glyph.id + ": " + (expected.Length > 0 ? "authored endpoints, " : "approved extension, ") + "four sizes including24px, finite nonempty geometry.");
                }
                var curve = AnimalGlyphGraphic.ParseSvgBody("<path d=\"M0 0 Q0 10 10 10\"/>")[0];
                Check(curve.Exists(point => Vector2.Distance(point, new Vector2(2.5f, 7.5f)) < .001f), "Quadratic curve midpoint");
                Reject("<path d=\"M0 0 C1 2 3 4 5 6\"/>");
                Reject("<path d=\"m0 0 v5\"/>");
                Reject("<path d=\"M0 0 Q2\"/>");
                Reject("<g transform=\"rotate(20)\"><path d=\"M0 0 V10\"/></g>");
                Reject("<circle cx=\"10\" cy=\"10\" r=\"5\"/>");
                report.Add("PASS quadratic midpoint; unknown commands, malformed parameters, unsupported transforms, and elements reject explicitly.");
                report.Add("PASS 37 glyphs including like, look, sea and past; sea preserves its authored translation. This verifies geometry and parsing; visual review remains separate.");
            }
            catch (Exception exception)
            {
                report.Add("FAIL " + exception);
                throw;
            }
            finally { File.WriteAllLines("PrototypeReview/animal-glyphs.txt", report); }
        }

        private static bool Finite(float value) { return !float.IsNaN(value) && !float.IsInfinity(value); }
        private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        private static void Reject(string body)
        {
            try { AnimalGlyphGraphic.ParseSvgBody(body); }
            catch (FormatException) { return; }
            throw new InvalidOperationException("Unsupported SVG was silently accepted: " + body);
        }
    }
}
