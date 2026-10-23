using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using WeatherHouse.Vegetation;

namespace WeatherHouse.WatersideV2
{
    /// <summary>Solid near-camera foliage; retains the Painterly Grass UV, wind and pigment channels.</summary>
    public static class WatersidePlantMesh
    {
        public const int TriangleBudget = 10000;

        public static Mesh Build(GrassSpecies definition, int variation, string id)
        {
            Mesh surface = Surface(definition, variation, id);
            try
            {
                var result = Solidify(surface, Mathf.Clamp(definition.width * .075f, .003f, .008f));
                result.name = id + "_v" + (variation + 1);
                Validate(result);
                return result;
            }
            finally
            {
                if (Application.isPlaying) UnityEngine.Object.Destroy(surface);
                else UnityEngine.Object.DestroyImmediate(surface);
            }
        }

        static Mesh Surface(GrassSpecies definition, int variation, string id)
        {
            if (id.StartsWith("N", StringComparison.Ordinal)) return new Forms(definition, variation).Build(id);
            Mesh original = GrassMeshFactory.Build(definition, variation);
            if (id != "R01_Plume" && id != "R02_BrownSeed" && id != "R09_CreamFoxtail" && id != "R15_LowSeed") return original;
            try { return new Forms(definition, variation).ReedClump(original, 8 + variation % 3); }
            finally
            {
                if (Application.isPlaying) UnityEngine.Object.Destroy(original);
                else UnityEngine.Object.DestroyImmediate(original);
            }
        }

        struct Edge : IEquatable<Edge>
        {
            public readonly int a, b;
            public Edge(int x, int y) { a = Math.Min(x, y); b = Math.Max(x, y); }
            public bool Equals(Edge other) => a == other.a && b == other.b;
            public override bool Equals(object value) => value is Edge other && Equals(other);
            public override int GetHashCode() => unchecked(a * 397 ^ b);
        }
        sealed class Use { public int count, balance, from, to; }

        static Dictionary<Edge, Use> Edges(IList<int> indices)
        {
            var result = new Dictionary<Edge, Use>();
            for (int i = 0; i < indices.Count; i += 3)
                for (int j = 0; j < 3; j++)
                {
                    int a = indices[i + j], b = indices[i + (j + 1) % 3];
                    var key = new Edge(a, b);
                    if (!result.TryGetValue(key, out var use)) result[key] = use = new Use { from = a, to = b };
                    use.count++; use.balance += a < b ? 1 : -1;
                }
            return result;
        }

        // Components are identified by shared indices, not spatial proximity: intersecting leaves stay independent.
        public static Mesh Solidify(Mesh source, float thickness)
        {
            Vector3[] points = source.vertices;
            int[] sourceIndices = source.triangles;
            var validFaces = new List<int>(sourceIndices.Length);
            for (int i = 0; i < sourceIndices.Length; i += 3)
            {
                int a = sourceIndices[i], b = sourceIndices[i + 1], c = sourceIndices[i + 2];
                if ((uint)a >= points.Length || (uint)b >= points.Length || (uint)c >= points.Length ||
                    !Finite(points[a]) || !Finite(points[b]) || !Finite(points[c]))
                    throw new InvalidOperationException(source.name + ": invalid source triangle " + i / 3);
                // Prune true degeneracies before connectivity, so their otherwise isolated vertices disappear too.
                if (Vector3.Cross(points[b] - points[a], points[c] - points[a]).sqrMagnitude < 1e-20f) continue;
                validFaces.Add(a); validFaces.Add(b); validFaces.Add(c);
            }
            int[] indices = validFaces.ToArray();
            var parent = Enumerable.Range(0, points.Length).ToArray();
            int Find(int a) { while (a != parent[a]) { parent[a] = parent[parent[a]]; a = parent[a]; } return a; }
            for (int i = 0; i < indices.Length; i += 3)
            {
                parent[Find(indices[i + 1])] = Find(indices[i]);
                parent[Find(indices[i + 2])] = Find(indices[i]);
            }
            var components = new Dictionary<int, List<int>>();
            for (int i = 0; i < indices.Length; i += 3)
            {
                int key = Find(indices[i]);
                if (!components.TryGetValue(key, out var list)) components[key] = list = new List<int>();
                list.Add(indices[i]); list.Add(indices[i + 1]); list.Add(indices[i + 2]);
            }
            Vector2[] uv = source.uv, wind = source.uv2;
            Color[] colors = source.colors;
            var output = new Geometry();
            foreach (var faces in components.Values)
            {
                var edges = Edges(faces);
                if (edges.Values.Any(e => e.count > 2 || e.count == 2 && e.balance != 0))
                    throw new InvalidOperationException(source.name + ": inconsistent source surface topology");
                var boundary = edges.Values.Where(e => e.count == 1).ToArray();
                int[] used = faces.Distinct().OrderBy(i => i).ToArray();
                var map = new Dictionary<int, int>();
                foreach (int old in used)
                    map[old] = output.Vertex(points[old], uv.Length == points.Length ? uv[old] : Vector2.zero,
                        wind.Length == points.Length ? wind[old] : Vector2.zero,
                        colors.Length == points.Length ? colors[old] : new Color(1, 1, 1, 0));
                var local = faces.Select(i => map[i]).ToList();
                if (boundary.Length == 0)
                {
                    // Closed stems and seed heads already have volume; never duplicate them into inner shells.
                    OrientClosed(local, output.points);
                    output.indices.AddRange(local);
                    continue;
                }
                var loops = BoundaryLoops(boundary);
                bool openStem = loops.Count == 2 && loops.All(l => l.Count >= 3 && l.Count <= 8) &&
                    used.All(i => colors.Length != points.Length || colors[i].a < .1f);
                if (openStem)
                {
                    foreach (var loop in loops)
                    {
                        Vector3 center = Vector3.zero; Vector2 tex = Vector2.zero, sway = Vector2.zero;
                        foreach (int old in loop) { center += points[old]; tex += output.uv[map[old]]; sway += output.wind[map[old]]; }
                        int cap = output.Vertex(center / loop.Count, tex / loop.Count, sway / loop.Count, output.colors[map[loop[0]]]);
                        for (int i = 0; i < loop.Count; i++)
                        { local.Add(cap); local.Add(map[loop[(i + 1) % loop.Count]]); local.Add(map[loop[i]]); }
                    }
                    OrientClosed(local, output.points);
                    output.indices.AddRange(local);
                    continue;
                }
                var normals = used.ToDictionary(i => i, i => Vector3.zero);
                for (int i = 0; i < faces.Count; i += 3)
                {
                    int a = faces[i], b = faces[i + 1], c = faces[i + 2];
                    Vector3 normal = Vector3.Cross(points[b] - points[a], points[c] - points[a]);
                    normals[a] += normal; normals[b] += normal; normals[c] += normal;
                }
                var underside = new Dictionary<int, int>();
                foreach (int old in used)
                {
                    int top = map[old];
                    // Daisy leaf tips have real sub-mm triangles. Vector3.normalized treats area vectors
                    // shorter than 1e-5 as zero; normalize against the same geometric tolerance as Validate.
                    float magnitude = normals[old].magnitude;
                    if (magnitude < 1e-10f) throw new InvalidOperationException(source.name + ": missing surface normal at vertex " + old);
                    Vector3 normal = normals[old] / magnitude;
                    Vector3 offset = normal * (Mathf.Max(.0005f, thickness) * .5f);
                    output.points[top] += offset;
                    Color color = output.colors[top];
                    color.r *= .88f; color.g *= .88f; color.b *= .88f;
                    underside[old] = output.Vertex(points[old] - offset, output.uv[top], output.wind[top], color);
                }
                output.indices.AddRange(local);
                for (int i = 0; i < faces.Count; i += 3)
                    output.Triangle(underside[faces[i]], underside[faces[i + 2]], underside[faces[i + 1]]);
                foreach (Use e in boundary)
                {
                    output.Triangle(map[e.to], map[e.from], underside[e.from]);
                    output.Triangle(map[e.to], underside[e.from], underside[e.to]);
                }
            }
            return output.Mesh(source.name + "_Solid");
        }

        static List<List<int>> BoundaryLoops(Use[] boundary)
        {
            var next = new Dictionary<int, int>();
            foreach (var edge in boundary)
            {
                if (next.ContainsKey(edge.from)) throw new InvalidOperationException("Branched foliage boundary");
                next[edge.from] = edge.to;
            }
            var loops = new List<List<int>>();
            while (next.Count > 0)
            {
                int start = next.Keys.Min(), at = start;
                var loop = new List<int>();
                do
                {
                    loop.Add(at);
                    if (!next.TryGetValue(at, out int target)) throw new InvalidOperationException("Broken foliage boundary");
                    next.Remove(at); at = target;
                } while (at != start);
                loops.Add(loop);
            }
            return loops;
        }

        static void OrientClosed(List<int> faces, IList<Vector3> vertices)
        {
            double volume = 0;
            Vector3 origin = vertices[faces[0]];
            for (int i = 0; i < faces.Count; i += 3)
                volume += Vector3.Dot(vertices[faces[i]] - origin, Vector3.Cross(vertices[faces[i + 1]] - origin, vertices[faces[i + 2]] - origin));
            if (volume < 0)
                for (int i = 0; i < faces.Count; i += 3) { int swap = faces[i + 1]; faces[i + 1] = faces[i + 2]; faces[i + 2] = swap; }
        }

        public static string Validate(Mesh mesh)
        {
            Vector3[] v = mesh.vertices, normals = mesh.normals; int[] t = mesh.triangles;
            if (v.Length == 0 || t.Length == 0 || t.Length / 3 > TriangleBudget) throw new InvalidOperationException(mesh.name + ": empty mesh or triangle budget exceeded");
            if (normals.Length != v.Length || mesh.uv.Length != v.Length || mesh.uv2.Length != v.Length || mesh.colors.Length != v.Length)
                throw new InvalidOperationException(mesh.name + ": incomplete shading channels");
            for (int i = 0; i < v.Length; i++)
                if (!Finite(v[i]) || !Finite(normals[i]) || normals[i].sqrMagnitude < .5f)
                    throw new InvalidOperationException(mesh.name + ": invalid vertex or normal " + i);
            for (int i = 0; i < t.Length; i += 3)
                if ((uint)t[i] >= v.Length || (uint)t[i + 1] >= v.Length || (uint)t[i + 2] >= v.Length ||
                    Vector3.Cross(v[t[i + 1]] - v[t[i]], v[t[i + 2]] - v[t[i]]).sqrMagnitude < 1e-20f)
                    throw new InvalidOperationException(mesh.name + ": invalid triangle " + i / 3);
            if (Edges(t).Values.Any(e => e.count != 2 || e.balance != 0))
                throw new InvalidOperationException(mesh.name + ": unclosed or inconsistently wound surface");
            return v.Length + " vertices, " + t.Length / 3 + " triangles; all edges closed";
        }
        static bool Finite(Vector3 p) => !(float.IsNaN(p.x) || float.IsNaN(p.y) || float.IsNaN(p.z) || float.IsInfinity(p.x) || float.IsInfinity(p.y) || float.IsInfinity(p.z));

        sealed class Geometry
        {
            public readonly List<Vector3> points = new List<Vector3>();
            public readonly List<Vector2> uv = new List<Vector2>(), wind = new List<Vector2>();
            public readonly List<Color> colors = new List<Color>();
            public readonly List<int> indices = new List<int>();
            public int Vertex(Vector3 p, Vector2 tex, Vector2 sway, Color color)
            { int i = points.Count; points.Add(p); uv.Add(tex); wind.Add(sway); colors.Add(color); return i; }
            public void Triangle(int a, int b, int c) { indices.Add(a); indices.Add(b); indices.Add(c); }
            public void Append(Mesh mesh)
            {
                int offset = points.Count;
                points.AddRange(mesh.vertices); uv.AddRange(mesh.uv); wind.AddRange(mesh.uv2); colors.AddRange(mesh.colors);
                foreach (int index in mesh.triangles) indices.Add(offset + index);
            }
            public Mesh Mesh(string name)
            {
                var mesh = new Mesh { name = name, indexFormat = IndexFormat.UInt32 };
                mesh.SetVertices(points); mesh.SetUVs(0, uv); mesh.SetUVs(1, wind); mesh.SetColors(colors); mesh.SetTriangles(indices, 0);
                mesh.RecalculateNormals(); mesh.RecalculateBounds();
                Bounds bounds = mesh.bounds; bounds.Expand(new Vector3(.12f, .04f, .12f)); mesh.bounds = bounds;
                return mesh;
            }
        }

        sealed class Forms
        {
            readonly GrassSpecies species;
            readonly System.Random random;
            readonly Geometry g = new Geometry();
            readonly float scale;
            static readonly Color Green = new Color(1, 1, 1, 0);
            public Forms(GrassSpecies source, int variation)
            { species = source; random = new System.Random(source.seed + variation * 7919); scale = new[] { .88f, 1f, 1.12f }[variation % 3]; }
            float R(float a, float b) => Mathf.Lerp(a, b, (float)random.NextDouble());
            static Vector3 Radial(float a) => new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
            int V(Vector3 p, Color c, float across = .5f)
            {
                p *= scale;
                float height = Mathf.Clamp01(p.y / Mathf.Max(.08f, species.height * scale));
                return g.Vertex(p, new Vector2(across, height), new Vector2(Mathf.Pow(height, 1.6f), 0), c);
            }
            public Mesh Build(string id)
            {
                if (id == "N01_WaterLily") WaterLily();
                else if (id == "N02_Ivy") Ivy();
                else if (id == "N03_BroadleafShrub") Shrub();
                else throw new ArgumentOutOfRangeException(nameof(id), id, "Unknown waterside plant");
                return g.Mesh(id);
            }
            public Mesh ReedClump(Mesh stalks, int count)
            {
                g.Append(stalks);
                float phase = R(0, Mathf.PI * 2);
                for (int leaf = 0; leaf < count; leaf++)
                {
                    float angle = phase + leaf * 2.399963f + R(-.12f, .12f);
                    Vector3 outward = Radial(angle), side = Radial(angle + Mathf.PI * .5f);
                    Vector3 root = outward * species.height * R(.015f, .055f);
                    float rise = species.height * R(.83f, 1.02f), reach = species.height * R(.31f, .52f);
                    float width = species.height * R(.095f, .135f), roll = R(-.22f, .22f);
                    int first = g.points.Count;
                    const int rows = 6;
                    // A narrow base opens into a broad arch, then droops and twists slightly at the tip.
                    // The center ridge and curved outline remain a surface until Solidify closes its thickness.
                    int bottom = V(root, Green);
                    for (int row = 1; row < rows; row++)
                    {
                        float t = row / (float)rows;
                        Vector3 center = root + outward * (reach * t * t) + Vector3.up * (rise * (1.42f * t - .68f * t * t * t));
                        float half = width * .5f * Mathf.Pow(Mathf.Sin(t * Mathf.PI), .7f);
                        Vector3 across = (side + Vector3.up * (roll * t)).normalized;
                        V(center - across * half, Green, 0);
                        V(center + Vector3.up * half * .18f, Green);
                        V(center + across * half, Green, 1);
                        int at = first + 1 + (row - 1) * 3;
                        if (row == 1)
                        { g.Triangle(bottom, at, at + 1); g.Triangle(bottom, at + 1, at + 2); }
                        else
                            for (int col = 0; col < 2; col++)
                            { g.Triangle(at - 3 + col, at + col, at - 2 + col); g.Triangle(at - 2 + col, at + col, at + 1 + col); }
                    }
                    int tip = V(root + outward * reach + Vector3.up * (rise * .74f), Green);
                    int last = first + 1 + (rows - 2) * 3;
                    g.Triangle(last, tip, last + 1); g.Triangle(last + 1, tip, last + 2);
                }
                return g.Mesh(stalks.name + "_LeafyClump");
            }
            static void Frame(Vector3 axis, out Vector3 side, out Vector3 normal)
            {
                side = Vector3.Cross(Mathf.Abs(axis.normalized.y) > .94f ? Vector3.forward : Vector3.up, axis).normalized;
                normal = Vector3.Cross(axis.normalized, side).normalized;
                if (normal.y < 0) { side = -side; normal = -normal; }
            }
            void Stem(Vector3 start, Vector3 end, float radius, Color color)
            {
                Frame(end - start, out Vector3 side, out Vector3 normal);
                const int sides = 5, rows = 3;
                int first = g.points.Count;
                for (int row = 0; row <= rows; row++)
                {
                    float t = row / (float)rows;
                    Vector3 center = Vector3.Lerp(start, end, t) + Vector3.up * Mathf.Sin(t * Mathf.PI) * (end - start).magnitude * .035f;
                    for (int i = 0; i < sides; i++)
                    {
                        float a = i * Mathf.PI * 2 / sides;
                        V(center + (side * Mathf.Cos(a) + normal * Mathf.Sin(a)) * radius * Mathf.Lerp(1, .55f, t), color);
                        if (row == 0) continue;
                        int lo = first + (row - 1) * sides + i, next = first + (row - 1) * sides + (i + 1) % sides;
                        g.Triangle(lo, next, lo + sides); g.Triangle(lo + sides, next, next + sides);
                    }
                }
                int bottom = V(start, color), top = V(end, color);
                for (int i = 0; i < sides; i++)
                { g.Triangle(bottom, first + (i + 1) % sides, first + i); g.Triangle(top, first + rows * sides + i, first + rows * sides + (i + 1) % sides); }
            }
            void Leaf(Vector3 start, Vector3 end, float width, float arch, Color color, bool ivy = false)
            {
                Vector3 axis = end - start;
                Frame(axis, out Vector3 side, out Vector3 normal);
                int center = V((start + end) * .5f + normal * arch, color);
                Vector2[] outline = ivy ? new[] { new Vector2(0, 0), new Vector2(-.28f, .13f), new Vector2(-.54f, .05f),
                    new Vector2(-.38f, .40f), new Vector2(-.62f, .67f), new Vector2(-.23f, .65f), new Vector2(0, 1),
                    new Vector2(.23f, .65f), new Vector2(.62f, .67f), new Vector2(.38f, .40f), new Vector2(.54f, .05f), new Vector2(.28f, .13f) }
                    : new[] { new Vector2(0, 0), new Vector2(-.35f, .18f), new Vector2(-.50f, .45f), new Vector2(-.34f, .76f),
                        new Vector2(0, 1), new Vector2(.34f, .76f), new Vector2(.50f, .45f), new Vector2(.35f, .18f) };
                int rim = g.points.Count;
                foreach (Vector2 p in outline) V(start + axis * p.y + side * width * p.x + normal * (Mathf.Sin(p.y * Mathf.PI) * arch * .25f), color, p.x + .5f);
                for (int i = 0; i < outline.Length; i++) g.Triangle(center, rim + i, rim + (i + 1) % outline.Length);
            }
            void WaterLily()
            {
                for (int pad = 0; pad < 3; pad++)
                {
                    float angle = pad * 2.399963f + R(-.3f, .3f), radius = R(.15f, .22f);
                    Vector3 center = Radial(angle) * R(.10f, .27f) + Vector3.up * (.025f + pad * .006f);
                    int middle = V(center + Vector3.up * .012f, Green), rim = g.points.Count;
                    const int segments = 18;
                    for (int i = 0; i <= segments; i++)
                    {
                        float a = angle + .25f + (Mathf.PI * 2 - .5f) * i / segments;
                        V(center + Radial(a) * radius + Vector3.up * Mathf.Sin(a * 3) * .004f, Green, .5f + Mathf.Cos(a) * .5f);
                        if (i > 0) g.Triangle(middle, rim + i, rim + i - 1);
                    }
                }
                Vector3 flower = new Vector3(.02f, .07f, -.015f);
                Stem(Vector3.zero, flower, .009f, Green);
                for (int tier = 0; tier < 2; tier++)
                {
                    int count = tier == 0 ? 8 : 6;
                    for (int i = 0; i < count; i++)
                    {
                        Vector3 radial = Radial(i * Mathf.PI * 2 / count + tier * .45f);
                        float reach = tier == 0 ? .12f : .075f;
                        Color c = Color.Lerp(species.flowerColor, Color.white, R(0, .3f)); c.a = 1;
                        Leaf(flower + radial * .012f, flower + radial * reach + Vector3.up * (tier == 0 ? .035f : .065f),
                            tier == 0 ? .060f : .042f, .018f, c);
                    }
                }
                Stem(flower, flower + Vector3.up * .032f, .025f, new Color(1f, .73f, .20f, 1));
            }
            void Ivy()
            {
                float phase = R(0, Mathf.PI * 2);
                Vector3 axis = Radial(phase), sideways = Radial(phase + Mathf.PI * .5f);
                Vector3 end = axis * .78f + Vector3.up * .05f;
                Stem(Vector3.up * .015f, end, .007f, Green);
                for (int branch = 0; branch < 6; branch++)
                {
                    Vector3 from = Vector3.Lerp(Vector3.zero, end, .12f + branch * .14f);
                    Vector3 tip = from + sideways * (branch % 2 == 0 ? 1 : -1) * R(.13f, .23f) + Vector3.up * R(.06f, .12f);
                    Stem(from, tip, .004f, Green);
                    for (int leaf = 0; leaf < 3; leaf++)
                    {
                        Vector3 start = Vector3.Lerp(from, tip, .30f + leaf * .32f);
                        Vector3 direction = Radial(phase + (branch % 2 == 0 ? 1 : -1) * R(.5f, 2.3f));
                        Leaf(start, start + direction * R(.12f, .19f) + Vector3.up * .022f, R(.12f, .18f), .018f, Green, true);
                    }
                }
            }
            void Shrub()
            {
                Color bark = new Color(.30f, .28f, .13f, 1);
                float phase = R(0, Mathf.PI * 2);
                for (int branch = 0; branch < 10; branch++)
                {
                    Vector3 direction = Radial(phase + branch * 2.399963f);
                    // Inner shoots make the top of the dome; outer shoots fill the lower silhouette.
                    float radius = branch < 3 ? R(.10f, .20f) : R(.30f, .43f);
                    Vector3 tip = direction * radius + Vector3.up * (branch < 3 ? R(.47f, .56f) : R(.30f, .42f));
                    Stem(direction * .045f, tip, .014f, bark);
                    for (int fork = 0; fork < 4; fork++)
                    {
                        Vector3 from = tip * (.22f + fork * .24f);
                        Vector3 leafDirection = Radial(phase + branch * 2.399963f + (fork % 2 == 0 ? .85f : -.85f));
                        Vector3 end = from + leafDirection * R(.09f, .15f) + Vector3.up * .045f;
                        Stem(from, end, .006f, Green);
                        for (int leaf = 0; leaf < 3; leaf++)
                        {
                            Vector3 fan = Radial(phase + branch * 2.399963f + (leaf - 1) * .95f + fork * .42f);
                            Vector3 start = Vector3.Lerp(from, end, .30f + leaf * .3f);
                            Leaf(start, start + fan * R(.19f, .29f) + Vector3.up * R(.025f, .10f), R(.21f, .29f), .035f, Green);
                        }
                    }
                }
            }
        }
    }
}
