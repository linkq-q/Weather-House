using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace WeatherHouse.Vegetation.Editor
{
    public static class PainterlyTreeChecks
    {
        [MenuItem("Weather House/Vegetation Lab/Check Painterly Tree")]
        public static void Run()
        {
            var objects = new List<Object>();
            var go = new GameObject("Temporary tree geometry checks") { hideFlags = HideFlags.HideAndDontSave };
            objects.Add(go);
            try
            {
                var tree = go.AddComponent<PainterlyTree>();
                Mesh original = Build(tree, objects);
                Mesh repeated = Build(tree, objects);
                Require(original.vertices.SequenceEqual(repeated.vertices), "相同参数必须生成相同树形。");
                Require(original.triangles.SequenceEqual(repeated.triangles), "重复生成的索引必须一致。");
                tree.crownDensity = .2f;
                Mesh sparse = Build(tree, objects);
                Require(sparse.GetIndexCount(1) < original.GetIndexCount(1) * .6f, "树冠疏密必须改变叶量。");
                Require(Wood(original).SequenceEqual(Wood(sparse)), "调叶量不能改变枝干轮廓。");
                tree.crownDensity = .78f;
                tree.seed++;
                Mesh differentSeed = Build(tree, objects);
                Require(!differentSeed.vertices.SequenceEqual(original.vertices), "种子必须产生不同树形。");
                tree.seed--;
                tree.height = 8f;
                Mesh taller = Build(tree, objects);
                Require(taller.bounds.size.y > original.bounds.size.y * 1.2f, "高度必须改变竖直轮廓。");
                tree.height = 6.2f;
                tree.crownRadius = 4f;
                Mesh broader = Build(tree, objects);
                Require(broader.bounds.size.x + broader.bounds.size.z > (original.bounds.size.x + original.bounds.size.z) * 1.3f,
                    "冠幅必须改变水平轮廓。");
                tree.height = 2f; tree.trunkRadius = .12f; tree.crownRadius = 1f; tree.branchCount = 3; tree.crownDensity = .15f;
                Build(tree, objects);
                tree.height = 12f; tree.trunkRadius = .9f; tree.crownRadius = 5f; tree.branchCount = 8; tree.crownDensity = 1f;
                Build(tree, objects);
                var shader = Shader.Find("Weather House/Painterly Tree");
                Require(shader && !ShaderUtil.ShaderHasError(shader), "树材质必须编译通过。");
                Debug.Log("Painterly tree checks PASS: finite nondegenerate geometry, two material groups, deterministic seed, density preserving branches, size controls, parameter endpoints, shader compilation. Visual quality requires GPU review.");
            }
            finally { foreach (Object item in objects) if (item) Object.DestroyImmediate(item); }
        }

        public static void Run(PainterlyTree tree) { Verify(tree); }
        public static void Verify(PainterlyTree tree)
        {
            Mesh mesh = tree.CreateMesh();
            try { VerifyMesh(mesh); }
            finally { Object.DestroyImmediate(mesh); }
        }

        static Mesh Build(PainterlyTree tree, List<Object> owned)
        {
            Mesh mesh = tree.CreateMesh(); owned.Add(mesh); VerifyMesh(mesh); return mesh;
        }
        static IEnumerable<Vector3> Wood(Mesh mesh)
        {
            Vector3[] vertices = mesh.vertices;
            return mesh.GetIndices(0).Select(index => vertices[index]);
        }
        static void VerifyMesh(Mesh mesh)
        {
            Vector3[] v = mesh.vertices, n = mesh.normals;
            Color[] color = mesh.colors;
            Require(mesh.subMeshCount == 2 && mesh.GetIndexCount(0) > 0 && mesh.GetIndexCount(1) > 0, "必须包含枝干和树冠两组网格。");
            Require(v.Length == n.Length && v.Length == color.Length && v.Length == mesh.uv.Length, "顶点通道必须完整。");
            Require(mesh.triangles.Length / 3 < 50000, "单棵样本应控制在 50000 三角形内。");
            for (int i = 0; i < v.Length; i++)
            {
                Require(Finite(v[i].x) && Finite(v[i].y) && Finite(v[i].z), "顶点不能包含非有限值。");
                Require(Finite(n[i].sqrMagnitude) && n[i].sqrMagnitude > .9f, "法线必须有效。");
                Require(mesh.bounds.Contains(v[i]), "包围盒必须包含所有静态顶点。");
                Require(color[i].a >= 0 && color[i].a <= 1, "风动权重必须有效。");
            }
            int[] indices = mesh.triangles;
            for (int i = 0; i < indices.Length; i += 3)
            {
                int a = indices[i], b = indices[i + 1], c = indices[i + 2];
                Require(a >= 0 && b >= 0 && c >= 0 && a < v.Length && b < v.Length && c < v.Length, "索引必须在顶点范围内。");
                Require(Vector3.Cross(v[b] - v[a], v[c] - v[a]).sqrMagnitude > 1e-15f, "不得出现退化三角形。");
            }
        }
        static bool Finite(float x) { return !float.IsNaN(x) && !float.IsInfinity(x); }
        static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    }
}
