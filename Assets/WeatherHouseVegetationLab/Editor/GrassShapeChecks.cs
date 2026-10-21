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

                foreach (GrassKind kind in Enum.GetValues(typeof(GrassKind)))
                {
                    species.kind = kind;
                    foreach (float tip in new[] { 0f, 0.0001f, 0.002f, 0.11f, 0.4f })
                    {
                        species.tipWidthRatio = tip;
                        CheckTriangles(Build(species, owned));
                    }
                }
                Debug.Log("Grass shape checks PASS: editable width, tip width, unchanged body, root spread, full-range bend, nondegenerate geometry including zero tip width.");
            }
            finally { foreach (Object item in owned) if (item) Object.DestroyImmediate(item); }
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
