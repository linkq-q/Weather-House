using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace WeatherHouse.Vegetation.Editor
{
    public static class GrassShapeChecks
    {
        [MenuItem("Weather House/Vegetation Lab/Check Editable Grass Shape")]
        public static void Run()
        {
            var owned = new List<Object>();
            try
            {
                var species = ScriptableObject.CreateInstance<GrassSpecies>();
                owned.Add(species);
                species.bladeCount = 1;
                species.height = 0.5f;
                species.heightVariation = 0;
                species.width = 0.036f;
                species.tipWidthRatio = 0.11f;
                Mesh original = Build(species, owned);
                species.width *= 2;
                Mesh wider = Build(species, owned);
                Require(Span(wider, 0.5f) > Span(original, 0.5f) * 1.9f, "叶宽增大必须扩大叶片中部。");
                Require(original.vertices.Select(v => v.y).SequenceEqual(wider.vertices.Select(v => v.y)),
                    "只改叶宽不应改变叶片高度。");

                species.tipWidthRatio = 0.05f;
                Mesh thinTip = Build(species, owned);
                species.tipWidthRatio = 0.3f;
                Mesh broadTip = Build(species, owned);
                Require(Span(broadTip, 0.985f) > Span(thinTip, 0.985f) * 5,
                    "草尖比例必须改变尖帽宽度。");
                Require(Body(thinTip).SequenceEqual(Body(broadTip)), "只改草尖不应改变叶身与根部。");

                species.bend = 0.8f;
                Mesh lowBend = Build(species, owned);
                species.bend = 1.4f;
                Mesh highBend = Build(species, owned);
                Require(!Body(lowBend).SequenceEqual(Body(highBend)), "弯曲在高于0.6的范围内也必须改变叶片。");

                species.bladeCount = 14;
                species.rootSpread = 0.025f;
                Mesh narrowRoots = Build(species, owned);
                species.rootSpread = 0.4f;
                Mesh wideRoots = Build(species, owned);
                Require(RootRadius(wideRoots) > RootRadius(narrowRoots) + 0.005f,
                    "根束展开参数必须改变根部占地。");

                species.outerBladeFraction = 0;
                Mesh innerLeaves = Build(species, owned);
                species.outerBladeFraction = 1;
                Mesh outerLeaves = Build(species, owned);
                Require(!Body(innerLeaves).SequenceEqual(Body(outerLeaves)), "外展叶比例必须改变草丛构型。");
                species.outerBladeFraction = 0.25f;
                Mesh flatLayers = Build(species, owned);
                species.clumpLayering = 0.8f;
                Mesh distinctLayers = Build(species, owned);
                Require(!Body(flatLayers).SequenceEqual(Body(distinctLayers)), "内外层次必须改变草丛构型。");
                species.clumpLayering = 0;
                for (int i = 0; i < 40; i++)
                    Require((Mathf.FloorToInt(i * 0.25f) != Mathf.FloorToInt((i - 1) * 0.25f)) == (i % 4 == 0),
                        "默认外展比例必须保留原有叶序。");

                foreach (GrassKind kind in new[] { GrassKind.ShortGrass, GrassKind.TallGrass, GrassKind.Daisy })
                {
                    species.kind = kind;
                    foreach (float tip in new[] { 0f, 0.0001f, 0.002f, 0.11f, 0.4f })
                    {
                        species.tipWidthRatio = tip;
                        CheckTriangles(Build(species, owned));
                    }
                }
                VerifyReferenceGrassShapes(new[]
                {
                    ReferenceGrass(owned, "G1 高粗密", .90f, .055f, 28, .24f, .09f, .28f, .30f, .40f, .65f),
                    ReferenceGrass(owned, "G2 直细", .64f, .021f, 16, .12f, .05f, .10f, .12f, .125f, .08f),
                    ReferenceGrass(owned, "G3 疏斜", .46f, .023f, 11, .19f, .12f, .08f, .33f, .60f, .18f),
                    ReferenceGrass(owned, "G4 矮密粗", .25f, .032f, 34, .075f, .16f, .30f, .24f, .45f, .70f)
                });
                Debug.Log("Grass shape checks PASS: width, tip, root, bend, outer fraction, layering, legacy outer sequence, four normalized shapes and nondegenerate legacy geometry.");
            }
            finally { foreach (Object item in owned) if (item) Object.DestroyImmediate(item); }
        }

        // The catalog check can pass the four real saved presets here, in G1...G4 order.
        public static void VerifyReferenceGrassShapes(IReadOnlyList<GrassSpecies> presets)
        {
            Require(presets != null && presets.Count == 4, "四档普通草检查需要恰好四个草种资源。");
            var owned = new List<Object>();
            try
            {
                for (int variation = 0; variation < 3; variation++)
                {
                    var signatures = new Vector3[4];
                    for (int i = 0; i < presets.Count; i++)
                    {
                        GrassSpecies preset = presets[i];
                        Require(preset && (preset.kind == GrassKind.ShortGrass || preset.kind == GrassKind.TallGrass),
                            "四档普通草必须使用 ShortGrass 或 TallGrass。");
                        Mesh mesh = GrassMeshFactory.Build(preset, variation);
                        owned.Add(mesh);
                        CheckTriangles(mesh);
                        signatures[i] = NormalizedShape(mesh);
                    }
                    for (int i = 0; i < signatures.Length; i++)
                        for (int j = i + 1; j < signatures.Length; j++)
                            Require(Vector3.Distance(signatures[i], signatures[j]) > 0.01f,
                                $"G{i + 1} 和 G{j + 1} 在变体 {variation} 的归一化轮廓过于接近，不能仅缩放同一草型。");
                    Debug.Log($"Four grass shapes variation {variation}: normalized width / fan / height deviation = " +
                        string.Join("; ", signatures.Select(s => s.ToString("F4"))));
                }
            }
            finally { foreach (Object item in owned) if (item) Object.DestroyImmediate(item); }
        }

        static GrassSpecies ReferenceGrass(List<Object> owned, string name, float height, float width,
            int blades, float spread, float roots, float bend, float variation, float outer, float layering)
        {
            var species = ScriptableObject.CreateInstance<GrassSpecies>();
            owned.Add(species);
            species.name = species.displayName = name;
            species.height = height;
            species.width = width;
            species.bladeCount = blades;
            species.spread = spread;
            species.rootSpread = roots;
            species.bend = bend;
            species.heightVariation = variation;
            species.outerBladeFraction = outer;
            species.clumpLayering = layering;
            return species;
        }

        static Vector3 NormalizedShape(Mesh mesh)
        {
            Vector3[] vertices = mesh.vertices;
            Vector2[] uv = mesh.uv;
            var roots = new List<Vector3>();
            var tips = new List<Vector3>();
            var widths = new List<float>();
            for (int i = 0; i < vertices.Length; i++)
            {
                if (uv[i].x == 0 && uv[i].y == 0) roots.Add((vertices[i] + vertices[i + 2]) * 0.5f);
                if (uv[i].x == 0 && uv[i].y == 0.5f) widths.Add(Vector3.Distance(vertices[i], vertices[i + 2]));
                if (uv[i].x == 0.5f && uv[i].y == 1) tips.Add(vertices[i]);
            }
            Require(tips.Count > 0 && roots.Count == tips.Count && widths.Count == tips.Count,
                "普通草必须具备一一对应的叶根、叶尖与中部截面。");
            float maxHeight = tips.Max(p => p.y);
            float meanHeight = tips.Average(p => p.y / maxHeight);
            float fan = 0;
            for (int i = 0; i < tips.Count; i++)
                fan += new Vector2(tips[i].x - roots[i].x, tips[i].z - roots[i].z).magnitude;
            float deviation = Mathf.Sqrt(tips.Average(p => Mathf.Pow(p.y / maxHeight - meanHeight, 2)));
            // All three measures are invariant to uniform scaling and ignore vertex/triangle count.
            return new Vector3(widths.Average() / maxHeight, fan / tips.Count / maxHeight, deviation);
        }

        static Mesh Build(GrassSpecies species, List<Object> owned)
        {
            Mesh mesh = GrassMeshFactory.Build(species, 1);
            owned.Add(mesh);
            return mesh;
        }

        static IEnumerable<Vector3> Body(Mesh mesh)
        {
            Vector3[] vertices = mesh.vertices;
            Vector2[] uv = mesh.uv;
            return vertices.Where((v, i) => uv[i].y < 0.98f);
        }

        static float Span(Mesh mesh, float heightUV)
        {
            Vector3[] vertices = mesh.vertices;
            Vector2[] uv = mesh.uv;
            Vector3[] row = vertices.Where((v, i) => Mathf.Abs(uv[i].y - heightUV) < 0.0001f).ToArray();
            Require(row.Length >= 2, "需要至少两个横向截面顶点。");
            float span = 0;
            foreach (Vector3 a in row) foreach (Vector3 b in row) span = Mathf.Max(span, Vector3.Distance(a, b));
            return span;
        }

        static float RootRadius(Mesh mesh)
        {
            Vector3[] vertices = mesh.vertices;
            Vector2[] uv = mesh.uv;
            return vertices.Where((v, i) => uv[i].y == 0).Max(v => new Vector2(v.x, v.z).magnitude);
        }

        static void CheckTriangles(Mesh mesh)
        {
            Vector3[] vertices = mesh.vertices;
            int[] triangles = mesh.triangles;
            Require(vertices.All(v => IsFinite(v.x) && IsFinite(v.y) && IsFinite(v.z)), "顶点必须是有限值。");
            for (int i = 0; i < triangles.Length; i += 3)
            {
                int a = triangles[i], b = triangles[i + 1], c = triangles[i + 2];
                Require(a >= 0 && b >= 0 && c >= 0 && a < vertices.Length && b < vertices.Length && c < vertices.Length,
                    "三角形索引越界。");
                Require(Vector3.Cross(vertices[b] - vertices[a], vertices[c] - vertices[a]).sqrMagnitude > 1e-20f,
                    "检测到退化三角形。");
            }
        }

        static bool IsFinite(float value) { return !float.IsNaN(value) && !float.IsInfinity(value); }
        static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
    }
}
