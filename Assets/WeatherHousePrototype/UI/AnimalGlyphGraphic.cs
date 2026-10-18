using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace WeatherHousePrototype
{
    [AddComponentMenu("Weather House/Animal Glyph Graphic")]
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class AnimalGlyphGraphic : MaskableGraphic
    {
        [TextArea(3, 8)] [SerializeField] private string svgBody;
        [Tooltip("Stroke width in the original 100 x 120 SVG viewBox units.")]
        [Min(.1f)] public float lineWidth = 3.8f;
        private string cachedBody;
        private List<List<Vector2>> cachedPaths;
        private const string Number = @"[-+]?(?:\d+(?:\.\d*)?|\.\d+)(?:[eE][-+]?\d+)?";
        private static readonly Regex NumberAt = new Regex(@"\G" + Number, RegexOptions.CultureInvariant);
        private static readonly Regex Translation = new Regex(@"^\s*translate\(\s*(" + Number + @")(?:[\s,]+(" + Number + @"))?\s*\)\s*$", RegexOptions.CultureInvariant);

        public string SvgBody
        {
            get { return svgBody; }
            set { if (svgBody == value) return; svgBody = value; cachedPaths = null; SetVerticesDirty(); }
        }

        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            if (string.IsNullOrWhiteSpace(svgBody)) return;
            if (cachedPaths == null || cachedBody != svgBody)
            {
                cachedPaths = ParseSvgBody(svgBody);
                cachedBody = svgBody;
            }
            DrawPaths(mesh, cachedPaths, GetPixelAdjustedRect(), color, lineWidth);
        }

        // The authored v03 alphabet uses absolute M/H/V/Q and translated groups.
        // Unsupported SVG syntax is reported so an asset revision cannot silently lose strokes.
        public static List<List<Vector2>> ParseSvgBody(string body)
        {
            if (string.IsNullOrWhiteSpace(body)) throw new FormatException("SVG body is empty.");
            var paths = new List<List<Vector2>>();
            var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
            using (var reader = XmlReader.Create(new StringReader("<body>" + body + "</body>"), settings))
                ReadElements(XElement.Load(reader), Vector2.zero, paths);
            if (paths.Count == 0) throw new FormatException("SVG body contains no drawable paths.");
            return paths;
        }

        private static void ReadElements(XElement parent, Vector2 offset, List<List<Vector2>> paths)
        {
            foreach (XNode node in parent.Nodes())
            {
                if (node is XText text && string.IsNullOrWhiteSpace(text.Value)) continue;
                var element = node as XElement;
                if (element == null || element.Name.NamespaceName.Length != 0)
                    throw new FormatException("Unsupported SVG node.");
                if (element.Name.LocalName == "g")
                {
                    foreach (var attribute in element.Attributes())
                        if (attribute.Name != "transform") throw new FormatException("Unsupported SVG group attribute: " + attribute.Name);
                    Vector2 translated = offset;
                    string transform = (string)element.Attribute("transform");
                    if (transform != null)
                    {
                        Match match = Translation.Match(transform);
                        if (!match.Success) throw new FormatException("Unsupported SVG transform: " + transform);
                        translated += new Vector2(ParseNumber(match.Groups[1].Value),
                            match.Groups[2].Success ? ParseNumber(match.Groups[2].Value) : 0f);
                    }
                    ReadElements(element, translated, paths);
                }
                else if (element.Name.LocalName == "path")
                {
                    foreach (var attribute in element.Attributes())
                        if (attribute.Name != "d") throw new FormatException("Unsupported SVG path attribute: " + attribute.Name);
                    if (element.HasElements) throw new FormatException("SVG path cannot contain elements.");
                    ReadPath((string)element.Attribute("d"), offset, paths);
                }
                else throw new FormatException("Unsupported SVG element: " + element.Name);
            }
        }

        private static void ReadPath(string data, Vector2 offset, List<List<Vector2>> paths)
        {
            if (string.IsNullOrWhiteSpace(data)) throw new FormatException("SVG path has no d attribute.");
            int index = 0;
            char command = '\0';
            Vector2 current = Vector2.zero;
            List<Vector2> stroke = null;
            while (SkipSeparators(data, ref index))
            {
                if (char.IsLetter(data[index]))
                {
                    command = data[index++];
                    if ("MLHVQ".IndexOf(command) < 0) throw new FormatException("Unsupported SVG command: " + command);
                }
                if (command == '\0') throw new FormatException("SVG path must begin with M.");
                if (command != 'M' && stroke == null) throw new FormatException("SVG path must begin with M.");
                if (command == 'M')
                {
                    current = new Vector2(ReadNumber(data, ref index), ReadNumber(data, ref index));
                    stroke = new List<Vector2> { current + offset };
                    paths.Add(stroke);
                    command = 'L';
                }
                else if (command == 'Q')
                {
                    Vector2 control = new Vector2(ReadNumber(data, ref index), ReadNumber(data, ref index));
                    Vector2 end = new Vector2(ReadNumber(data, ref index), ReadNumber(data, ref index));
                    int steps = Mathf.Clamp(Mathf.CeilToInt((Vector2.Distance(current, control) + Vector2.Distance(control, end)) / 4f), 4, 24);
                    if (steps % 2 != 0) steps++;
                    for (int i = 1; i <= steps; i++)
                    {
                        float t = i / (float)steps;
                        AddDistinct(stroke, (1f - t) * (1f - t) * current + 2f * (1f - t) * t * control + t * t * end + offset);
                    }
                    current = end;
                }
                else
                {
                    if (command == 'L') current = new Vector2(ReadNumber(data, ref index), ReadNumber(data, ref index));
                    else if (command == 'H') current.x = ReadNumber(data, ref index);
                    else if (command == 'V') current.y = ReadNumber(data, ref index);
                    AddDistinct(stroke, current + offset);
                }
            }
            foreach (var parsed in paths)
                if (parsed.Count < 2) throw new FormatException("SVG path contains an empty stroke.");
        }

        private static void AddDistinct(List<Vector2> points, Vector2 point)
        {
            if (points.Count == 0 || (points[points.Count - 1] - point).sqrMagnitude > .000001f) points.Add(point);
        }

        private static bool SkipSeparators(string data, ref int index)
        {
            while (index < data.Length && (char.IsWhiteSpace(data[index]) || data[index] == ',')) index++;
            return index < data.Length;
        }

        private static float ReadNumber(string data, ref int index)
        {
            SkipSeparators(data, ref index);
            Match match = NumberAt.Match(data, index);
            if (!match.Success) throw new FormatException("Expected SVG number at position " + index + ".");
            index += match.Length;
            return ParseNumber(match.Value);
        }

        private static float ParseNumber(string value)
        {
            if (!float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float number) ||
                float.IsNaN(number) || float.IsInfinity(number)) throw new FormatException("Invalid SVG number: " + value);
            return number;
        }

        public static void DrawPaths(VertexHelper mesh, List<List<Vector2>> paths, Rect rect, Color tint, float strokeWidth = 3.8f)
        {
            mesh.Clear();
            if (rect.width <= 0f || rect.height <= 0f) return;
            if (strokeWidth <= 0f || float.IsNaN(strokeWidth) || float.IsInfinity(strokeWidth))
                throw new ArgumentOutOfRangeException(nameof(strokeWidth));
            float scale = Mathf.Min(rect.width / 100f, rect.height / 120f);
            float half = strokeWidth * scale * .5f;
            Color transparent = tint;
            transparent.a = 0f;
            foreach (var path in paths)
            {
                var points = new List<Vector2>(path.Count);
                foreach (Vector2 point in path)
                    points.Add(rect.center + new Vector2(point.x - 50f, 60f - point.y) * scale);
                int start = mesh.currentVertCount;
                for (int i = 0; i < points.Count; i++)
                {
                    Vector2 before = points[Mathf.Max(0, i - 1)];
                    Vector2 after = points[Mathf.Min(points.Count - 1, i + 1)];
                    Vector2 direction = (after - before).normalized;
                    Vector2 normal = new Vector2(-direction.y, direction.x);
                    mesh.AddVert(points[i] - normal * (half + .75f), transparent, Vector2.zero);
                    mesh.AddVert(points[i] - normal * half, tint, Vector2.zero);
                    mesh.AddVert(points[i] + normal * half, tint, Vector2.zero);
                    mesh.AddVert(points[i] + normal * (half + .75f), transparent, Vector2.zero);
                }
                for (int i = 0; i < points.Count - 1; i++)
                for (int lane = 0; lane < 3; lane++)
                {
                    int a = start + i * 4 + lane;
                    int b = a + 4;
                    mesh.AddTriangle(a, b, a + 1);
                    mesh.AddTriangle(a + 1, b, b + 1);
                }
                for (int i = 0; i < points.Count; i++)
                {
                    if (i == 0 || i == points.Count - 1 ||
                        Vector2.Dot((points[i] - points[i - 1]).normalized, (points[i + 1] - points[i]).normalized) < .995f)
                        RoundJoin(mesh, points[i], half, tint, transparent);
                }
            }
        }

        private static void RoundJoin(VertexHelper mesh, Vector2 center, float radius, Color tint, Color transparent)
        {
            const int segments = 18;
            int start = mesh.currentVertCount;
            mesh.AddVert(center, tint, Vector2.zero);
            for (int i = 0; i < segments; i++)
            {
                float angle = i * Mathf.PI * 2f / segments;
                Vector2 direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                mesh.AddVert(center + direction * radius, tint, Vector2.zero);
                mesh.AddVert(center + direction * (radius + .75f), transparent, Vector2.zero);
            }
            for (int i = 0; i < segments; i++)
            {
                int a = start + 1 + i * 2;
                int b = start + 1 + ((i + 1) % segments) * 2;
                mesh.AddTriangle(start, a, b);
                mesh.AddTriangle(a, a + 1, b);
                mesh.AddTriangle(a + 1, b + 1, b);
            }
        }
    }
}
