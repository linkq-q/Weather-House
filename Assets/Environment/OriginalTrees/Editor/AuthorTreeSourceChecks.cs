using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using UnityEngine;

namespace WeatherHouse.OriginalTrees.Editor
{
    public static class AuthorTreeSourceChecks
    {
        const float PositionTolerance = .00002f;
        const float UvTolerance = .00002f;
        const float UnitTolerance = .0002f;
        const float MinimumDot = .9999f;

        public static string Verify(GameObject tree, string species)
        {
            Require(tree, "Missing tree");
            Require(species == "Beech" || species == "Birch" || species == "FirTree", "Unknown species: " + species);
            string path = Path.Combine(Application.dataPath, "WeatherHouseOriginalTrees/Models/SM_" + species + ".fbx");
            var source = ReadSource(File.ReadAllText(path));
            int leafSlot = species == "FirTree" ? 0 : 1;
            var leaves = new List<int>();
            for (int i = 0; i < source.Materials.Length; i++)
                if (source.Materials[i] == leafSlot) leaves.Add(i);
            Require(leaves.Count >= 32, species + ": fewer than 32 source leaf triangles");

            var filters = tree.GetComponentsInChildren<MeshFilter>();
            Require(filters.Length == 1 && filters[0].sharedMesh, species + ": expected one imported mesh");
            var filter = filters[0];
            var mesh = filter.sharedMesh;
            Require(mesh.isReadable && mesh.subMeshCount > leafSlot, species + ": unreadable mesh or missing leaf submesh");
            Vector3[] positions = mesh.vertices, normals = mesh.normals;
            Vector4[] tangents = mesh.tangents;
            Vector2[] uvs = mesh.uv;
            Require(normals.Length == positions.Length && tangents.Length == positions.Length && uvs.Length == positions.Length,
                species + ": incomplete Unity vertex attributes");
            var leafVertices = new HashSet<int>(mesh.GetTriangles(leafSlot));
            Matrix4x4 world = filter.transform.localToWorldMatrix;
            Matrix4x4 worldNormals = world.inverse.transpose;
            float worstDot = 1, worstPositionError = 0, worstUvError = 0;

            for (int sample = 0; sample < 32; sample++)
            {
                int triangle = leaves[(int)((long)sample * (leaves.Count - 1) / 31)];
                for (int corner = 0; corner < 3; corner++)
                {
                    int sourceCorner = triangle * 3 + corner;
                    int rawIndex = source.Indices[sourceCorner];
                    int positionIndex = rawIndex < 0 ? -rawIndex - 1 : rawIndex;
                    Vector3 expectedPosition = ToUnity(source.Positions[positionIndex]) * .01f;
                    Vector2 expectedUv = source.Uvs[source.UvIndices[sourceCorner]];
                    Vector3 expectedNormal = ToUnity(source.Normals[sourceCorner]);
                    Vector3 expectedTangent = ToUnity(source.Tangents[sourceCorner]);
                    Vector3 expectedBinormal = ToUnity(source.Binormals[sourceCorner]);
                    string label = species + " source triangle " + triangle + ", corner " + corner;

                    // FBX import can split and reorder vertices; match attributes inside the leaf submesh.
                    int match = -1;
                    float bestScore = float.NegativeInfinity;
                    foreach (int vertex in leafVertices)
                    {
                        if ((positions[vertex] - expectedPosition).sqrMagnitude > PositionTolerance * PositionTolerance ||
                            (uvs[vertex] - expectedUv).sqrMagnitude > UvTolerance * UvTolerance) continue;
                        Vector3 tangent = tangents[vertex];
                        Vector3 binormal = Vector3.Cross(normals[vertex], tangent) * tangents[vertex].w;
                        float score = Mathf.Min(Vector3.Dot(normals[vertex].normalized, expectedNormal.normalized),
                            Mathf.Min(Vector3.Dot(tangent.normalized, expectedTangent.normalized),
                                Vector3.Dot(binormal.normalized, expectedBinormal.normalized)));
                        if (score > bestScore) { bestScore = score; match = vertex; }
                    }
                    Require(match >= 0, label + ": no Unity leaf vertex at the source position and UV0");
                    Vector3 actualTangent = tangents[match];
                    Vector3 actualBinormal = Vector3.Cross(normals[match], actualTangent) * tangents[match].w;
                    Require(Mathf.Abs(Mathf.Abs(tangents[match].w) - 1) <= UnitTolerance, label + ": tangent.w is not +/-1");
                    worstDot = Mathf.Min(worstDot, CheckDirection(expectedNormal, normals[match], label + " normal"));
                    worstDot = Mathf.Min(worstDot, CheckDirection(expectedTangent, actualTangent, label + " tangent"));
                    worstDot = Mathf.Min(worstDot, CheckDirection(expectedBinormal, actualBinormal, label + " binormal"));
                    worstPositionError = Mathf.Max(worstPositionError, Vector3.Distance(positions[match], expectedPosition));
                    worstUvError = Mathf.Max(worstUvError, Vector2.Distance(uvs[match], expectedUv));

                    // Export mirrors UE Y. With the study's -90 degree yaw, UE XYZ becomes Unity YZX.
                    Vector3 expectedWorldPosition = tree.transform.position + ToStudyWorld(source.Positions[positionIndex]) * .01f;
                    Require(Vector3.Distance(world.MultiplyPoint3x4(positions[match]), expectedWorldPosition) <= PositionTolerance,
                        label + ": world position does not use UE->Unity (y,z,x) metres");
                    CheckDirection(ToStudyWorld(source.Normals[sourceCorner]), worldNormals.MultiplyVector(normals[match]), label + " world normal");
                    CheckDirection(ToStudyWorld(source.Tangents[sourceCorner]), world.MultiplyVector(actualTangent), label + " world tangent");
                    CheckDirection(ToStudyWorld(source.Binormals[sourceCorner]), world.MultiplyVector(actualBinormal), label + " world FBX binormal");
                }
            }
            return string.Format(CultureInfo.InvariantCulture,
                "PASS {0}: original FBX vs Unity, 32 leaf triangles / 96 corners; position max {1:G4} m; UV0 max {2:G4}; minimum N/T/B dot {3:F7}; unit basis, tangent sign and UE->Unity world axes verified.",
                species, worstPositionError, worstUvError, worstDot);
        }

        static Vector3 ToUnity(Vector3 fbx) => new Vector3(fbx.x, fbx.z, fbx.y);
        static Vector3 ToStudyWorld(Vector3 fbx) => new Vector3(-fbx.y, fbx.z, fbx.x);

        static float CheckDirection(Vector3 expected, Vector3 actual, string label)
        {
            Require(Mathf.Abs(expected.magnitude - 1) <= UnitTolerance, label + ": source basis is not unit length");
            Require(Mathf.Abs(actual.magnitude - 1) <= UnitTolerance, label + ": Unity basis is not unit length");
            float dot = Vector3.Dot(expected.normalized, actual.normalized);
            Require(dot > MinimumDot, label + ": direction dot=" + dot.ToString("G9", CultureInfo.InvariantCulture));
            return dot;
        }

        sealed class Source
        {
            public Vector3[] Positions, Normals, Tangents, Binormals;
            public Vector2[] Uvs;
            public int[] Indices, UvIndices, Materials;
        }

        static Source ReadSource(string text)
        {
            Require(text.StartsWith("; FBX", StringComparison.Ordinal), "Expected ASCII FBX; binary FBX is unsupported");
            string geometry = Block(text, "Geometry");
            string normal = Layer(geometry, "LayerElementNormal", "ByPolygonVertex", "Direct");
            string tangent = Layer(geometry, "LayerElementTangent", "ByPolygonVertex", "Direct");
            string binormal = Layer(geometry, "LayerElementBinormal", "ByPolygonVertex", "Direct");
            string uv = Layer(geometry, "LayerElementUV", "ByPolygonVertex", "IndexToDirect");
            string material = Layer(geometry, "LayerElementMaterial", "ByPolygon", "IndexToDirect");
            var result = new Source
            {
                Positions = Vectors(geometry, "Vertices"), Indices = Integers(geometry, "PolygonVertexIndex"),
                Normals = Vectors(normal, "Normals"), Tangents = Vectors(tangent, "Tangents"),
                Binormals = Vectors(binormal, "Binormals"), UvIndices = Integers(uv, "UVIndex"),
                Materials = Integers(material, "Materials")
            };
            float[] values = Floats(uv, "UV");
            Require(values.Length % 2 == 0, "UV array has an incomplete pair");
            result.Uvs = new Vector2[values.Length / 2];
            for (int i = 0; i < result.Uvs.Length; i++) result.Uvs[i] = new Vector2(values[i * 2], values[i * 2 + 1]);
            int corners = result.Indices.Length;
            Require(corners % 3 == 0 && result.Materials.Length == corners / 3, "Expected one material index per triangle");
            Require(result.Normals.Length == corners && result.Tangents.Length == corners && result.Binormals.Length == corners &&
                result.UvIndices.Length == corners, "Expected one N/T/B and UV index per polygon corner");
            for (int i = 0; i < corners; i++)
            {
                int index = result.Indices[i];
                Require((index < 0) == (i % 3 == 2), "Non-triangle polygon at corner " + i);
                int vertex = index < 0 ? -index - 1 : index;
                Require(vertex >= 0 && vertex < result.Positions.Length, "FBX position index out of range");
                Require(result.UvIndices[i] >= 0 && result.UvIndices[i] < result.Uvs.Length, "FBX UV index out of range");
            }
            return result;
        }

        static string Layer(string geometry, string name, string mapping, string reference)
        {
            string layer = Block(geometry, name);
            Require(Regex.IsMatch(layer, @"MappingInformationType:\s*""" + Regex.Escape(mapping) + @""""), name + ": unsupported mapping");
            Require(Regex.IsMatch(layer, @"ReferenceInformationType:\s*""" + Regex.Escape(reference) + @""""), name + ": unsupported reference mode");
            return layer;
        }

        static string Block(string text, string name)
        {
            Match start = Regex.Match(text, @"(?m)^[ \t]*" + Regex.Escape(name) + @":[^\r\n{]*\{");
            Require(start.Success, "Missing FBX block: " + name);
            int opening = start.Index + start.Length - 1, depth = 1;
            for (int i = opening + 1; i < text.Length; i++)
            {
                if (text[i] == '{') depth++;
                else if (text[i] == '}' && --depth == 0) return text.Substring(opening + 1, i - opening - 1);
            }
            throw new InvalidOperationException("Unclosed FBX block: " + name);
        }

        static string[] Tokens(string text, string name)
        {
            Match match = Regex.Match(text, @"(?m)^[ \t]*" + Regex.Escape(name) + @":\s*\*(\d+)\s*\{\s*a:\s*([^}]*)\}");
            Require(match.Success, "Missing FBX numeric array: " + name);
            string[] values = match.Groups[2].Value.Split(new[] { ',', ' ', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            Require(values.Length == int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture), name + ": declared array length mismatch");
            return values;
        }

        static int[] Integers(string text, string name) => Array.ConvertAll(Tokens(text, name), v => int.Parse(v, CultureInfo.InvariantCulture));
        static float[] Floats(string text, string name)
        {
            float[] values = Array.ConvertAll(Tokens(text, name), v => float.Parse(v, NumberStyles.Float, CultureInfo.InvariantCulture));
            foreach (float value in values) Require(!float.IsNaN(value) && !float.IsInfinity(value), name + ": non-finite value");
            return values;
        }
        static Vector3[] Vectors(string text, string name)
        {
            float[] values = Floats(text, name);
            Require(values.Length % 3 == 0, name + ": incomplete vector");
            var result = new Vector3[values.Length / 3];
            for (int i = 0; i < result.Length; i++) result[i] = new Vector3(values[i * 3], values[i * 3 + 1], values[i * 3 + 2]);
            return result;
        }
        static void Require(bool condition, string message)
        { if (!condition) throw new InvalidOperationException("Author tree source check: " + message); }
    }
}
