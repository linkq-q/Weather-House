using System;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace WeatherHouse.Hillside
{
    // This reduction accepts only the rounded-tip blade topology generated for the hillside meadow.
    internal sealed class HillsideGrassMeshLod : IDisposable
    {
        readonly Mesh[] sources = new Mesh[8], reduced = new Mesh[8];
        readonly bool[] attempted = new bool[8];
        bool invalidated;
        static readonly int[] KeptVertices = { 0, 1, 2, 6, 7, 8, 12, 13, 14 };
        static readonly int[] OriginalTriangles = {
            0, 3, 1, 1, 3, 4, 1, 4, 2, 2, 4, 5,
            3, 6, 4, 4, 6, 7, 4, 7, 5, 5, 7, 8,
            6, 9, 7, 7, 9, 10, 7, 10, 8, 8, 10, 11,
            9, 12, 10, 10, 12, 13, 10, 13, 14, 10, 14, 11 };
        static readonly int[] ReducedTriangles = {
            0, 3, 1, 1, 3, 4, 1, 4, 2, 2, 4, 5,
            3, 6, 4, 4, 6, 7, 4, 7, 8, 4, 8, 5 };

        internal Mesh Get(Mesh source, int variant, bool lowDetail)
        {
            if (invalidated) Dispose();
            if (variant < 0 || variant >= reduced.Length) return source;
            if (sources[variant] != source)
            {
                Release(variant); sources[variant] = source;
            }
            if (!lowDetail || !source) return source;
            if (!attempted[variant])
            {
                attempted[variant] = true;
                reduced[variant] = Build(source);
            }
            return reduced[variant] ? reduced[variant] : source;
        }

        internal static bool UseLowDetail(Bounds bounds, Vector3 observer, bool previous, float distance, float hysteresis)
        {
            if (!(distance > 0)) return false;
            float threshold = distance + (previous ? 0 : Mathf.Max(0, hysteresis));
            return bounds.SqrDistance(observer) > threshold * threshold;
        }

        static Mesh Build(Mesh source)
        {
            int count = source.vertexCount;
            if (!source.isReadable || source.subMeshCount != 1 || source.blendShapeCount != 0 || count == 0 || count % 15 != 0 ||
                source.GetTopology(0) != MeshTopology.Triangles ||
                source.GetVertexAttributeDimension(VertexAttribute.TexCoord0) != 2 ||
                source.GetVertexAttributeDimension(VertexAttribute.TexCoord1) != 2) return null;
            int[] triangles = source.triangles;
            Vector3[] positions = source.vertices, normals = source.normals;
            Vector2[] uv = source.uv, wind = source.uv2;
            Color[] colors = source.colors;
            Vector4[] tangents = source.tangents;
            if (triangles.Length != count / 15 * 48 || normals.Length != count || uv.Length != count ||
                wind.Length != count || colors.Length != count || tangents.Length != 0 && tangents.Length != count) return null;
            for (int blade = 0; blade < count / 15; blade++)
            {
                for (int i = 0; i < OriginalTriangles.Length; i++)
                    if (triangles[blade * 48 + i] != blade * 15 + OriginalTriangles[i]) return null;
                for (int vertex = 0; vertex < 15; vertex++)
                {
                    Vector2 expected = new Vector2(vertex % 3 * .5f,
                        vertex < 12 ? vertex / 3 * .25f : vertex == 13 ? 1f : .985f);
                    int index = blade * 15 + vertex;
                    if (!uv[index].Equals(expected) || wind[index].y != 0 || !(wind[index].x >= 0 && wind[index].x <= 1)) return null;
                }
            }
            int blades = count / 15;
            var indices = new int[blades * ReducedTriangles.Length];
            for (int blade = 0; blade < blades; blade++)
                for (int i = 0; i < ReducedTriangles.Length; i++)
                    indices[blade * ReducedTriangles.Length + i] = blade * KeptVertices.Length + ReducedTriangles[i];
            var mesh = new Mesh { name = source.name + "_FarLOD", hideFlags = HideFlags.HideAndDontSave,
                indexFormat = source.indexFormat };
            mesh.vertices = Reduce(positions, blades); mesh.normals = Reduce(normals, blades);
            mesh.uv = Reduce(uv, blades); mesh.uv2 = Reduce(wind, blades); mesh.colors = Reduce(colors, blades);
            if (tangents.Length != 0) mesh.tangents = Reduce(tangents, blades);
            mesh.triangles = indices; mesh.bounds = source.bounds;
            mesh.UploadMeshData(false);
            return mesh;
        }

        static T[] Reduce<T>(T[] source, int blades)
        {
            var result = new T[blades * KeptVertices.Length];
            for (int blade = 0; blade < blades; blade++)
                for (int vertex = 0; vertex < KeptVertices.Length; vertex++)
                    result[blade * KeptVertices.Length + vertex] = source[blade * 15 + KeptVertices[vertex]];
            return result;
        }

        internal void Invalidate() { invalidated = true; }

        void Release(int variant)
        {
            if (reduced[variant])
            {
                if (Application.isPlaying) Object.Destroy(reduced[variant]);
                else Object.DestroyImmediate(reduced[variant]);
            }
            reduced[variant] = sources[variant] = null; attempted[variant] = false;
        }

        public void Dispose()
        {
            for (int i = 0; i < reduced.Length; i++) Release(i);
            invalidated = false;
        }
    }
}
