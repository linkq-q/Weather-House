using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace WeatherHouse.Vegetation
{
    /// <summary>Deterministic texture-free plant meshes sharing the grass material's vertex contract.</summary>
    public static class PlantMeshFactory
    {
        public static Mesh Build(GrassSpecies species, int variation = 0)
        {
            if (!species) throw new ArgumentNullException(nameof(species));
            return new Builder(species, variation).Build();
        }

        sealed class Builder
        {
            readonly GrassSpecies source;
            readonly System.Random random;
            readonly List<Vector3> vertices = new List<Vector3>();
            readonly List<Vector2> uv = new List<Vector2>(), wind = new List<Vector2>();
            readonly List<Color> colors = new List<Color>();
            readonly List<int> indices = new List<int>();
            readonly Color green = new Color(1, 1, 1, 0);
            readonly float height, width, spread, bend, curl, aspect, headLength, radius, variation;
            readonly int blades, flowers, petals;

            public Builder(GrassSpecies species, int variant)
            {
                source = species;
                random = new System.Random(unchecked(species.seed + variant * 7919));
                float size = new[] { .86f, 1f, 1.12f, .96f }[((variant % 4) + 4) % 4];
                height = Safe(species.height, .03f, 4, .4f) * size;
                width = Safe(species.width, .005f, .3f, .025f);
                spread = Safe(species.spread, .01f, 2, .14f) * size;
                bend = Safe(species.bend, 0, 1.5f, .3f);
                curl = Safe(species.curl, 0, 1, .2f);
                aspect = Safe(species.leafAspect, .1f, 2, 1);
                headLength = Safe(species.headLength, .02f, 2, .25f);
                radius = Safe(species.flowerRadius, .01f, .3f, .07f);
                variation = Safe(species.heightVariation, 0, .7f, .2f);
                blades = Mathf.Clamp(species.bladeCount, 1, 40);
                flowers = Mathf.Clamp(species.flowerCount, 1, 7);
                petals = Mathf.Clamp(species.petalCount, 3, 12);
            }

            static float Safe(float value, float low, float high, float fallback)
            { return float.IsNaN(value) || float.IsInfinity(value) ? fallback : Mathf.Clamp(value, low, high); }
            float R(float low, float high) { return Mathf.Lerp(low, high, (float)random.NextDouble()); }
            Vector3 Radial(float angle) { return new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)); }
            Color Pigment(Color color, float value = 1)
            { return new Color(Safe(color.r, 0, 4, 1) * value, Safe(color.g, 0, 4, 1) * value, Safe(color.b, 0, 4, 1) * value, 1); }
            Vector3 Root()
            {
                float rootSpread = Safe(source.rootSpread, .025f, .4f, .12f);
                return Radial(R(0, Mathf.PI * 2)) * Mathf.Sqrt(R(0, 1)) * spread * (.3f + rootSpread);
            }
            Vector3 StemPoint(Vector3 root, Vector3 lean, float h, float t)
            { return root + Vector3.up * (h * t) + lean * (t * t); }
            int Vertex(Vector3 position, Color color, float across = .5f)
            {
                int index = vertices.Count;
                vertices.Add(position);
                float gradient = Mathf.Clamp01(position.y / Mathf.Max(height, .03f));
                uv.Add(new Vector2(across, gradient));
                wind.Add(new Vector2(Mathf.Pow(gradient, 1.6f), 0));
                colors.Add(color);
                return index;
            }
            void Triangle(int a, int b, int c)
            {
                // Ring builders may reference the next vertex before that ring is complete.
                indices.Add(a); indices.Add(b); indices.Add(c);
            }
            void Quad(int a, int b, int c, int d) { Triangle(a, b, c); Triangle(c, b, d); }
            void ValidateAndFilterTriangles()
            {
                // Resolve indices and reject degenerate faces only after every builder has finished.
                int kept = 0;
                for (int i = 0; i < indices.Count; i += 3)
                {
                    int a = indices[i], b = indices[i + 1], c = indices[i + 2];
                    if ((uint)a >= (uint)vertices.Count || (uint)b >= (uint)vertices.Count || (uint)c >= (uint)vertices.Count)
                        throw new InvalidOperationException(source.kind + " triangle " + (i / 3) + " references a missing vertex.");
                    float areaSquared = Vector3.Cross(vertices[b] - vertices[a], vertices[c] - vertices[a]).sqrMagnitude;
                    if (float.IsNaN(areaSquared) || float.IsInfinity(areaSquared))
                        throw new InvalidOperationException(source.kind + " triangle " + (i / 3) + " has nonfinite geometry.");
                    if (areaSquared <= 1e-18f) continue;
                    indices[kept++] = a; indices[kept++] = b; indices[kept++] = c;
                }
                if (kept < indices.Count) indices.RemoveRange(kept, indices.Count - kept);
            }
            static void Frame(Vector3 direction, out Vector3 side, out Vector3 normal)
            {
                direction = direction.sqrMagnitude < 1e-10f ? Vector3.up : direction.normalized;
                side = Vector3.Cross(Mathf.Abs(direction.y) > .94f ? Vector3.forward : Vector3.up, direction).normalized;
                normal = Vector3.Cross(direction, side).normalized;
            }

            public Mesh Build()
            {
                switch (source.kind)
                {
                    case GrassKind.Plume: Plumes(); break;
                    case GrassKind.SeedHead: SeedHeads(false); break;
                    case GrassKind.FlowerSpike: FlowerSpikes(); break;
                    case GrassKind.FlowerSpray: FlowerSprays(); break;
                    case GrassKind.Fern: Ferns(); break;
                    case GrassKind.Foxtail: SeedHeads(true); break;
                    case GrassKind.Wildflower: Wildflowers(); break;
                    case GrassKind.Bellflower: Bellflowers(); break;
                    case GrassKind.LeafyHerb: Herbs(); break;
                    case GrassKind.RoundGroundcover: Groundcover(); break;
                    default: throw new ArgumentOutOfRangeException(nameof(source.kind), "PlantMeshFactory handles the additional plant kinds.");
                }
                ValidateAndFilterTriangles();
                if (indices.Count / 3 >= 15000) throw new InvalidOperationException("Plant mesh exceeded its 15000 triangle budget.");
                var mesh = new Mesh { name = source.name + "_Plant", indexFormat = IndexFormat.UInt32 };
                mesh.SetVertices(vertices); mesh.SetUVs(0, uv); mesh.SetUVs(1, wind); mesh.SetColors(colors);
                mesh.SetTriangles(indices, 0); mesh.RecalculateNormals(); mesh.RecalculateBounds();
                Bounds bounds = mesh.bounds; bounds.Expand(new Vector3(1.6f, .25f, 1.6f)); mesh.bounds = bounds;
                return mesh;
            }

            // Tapered, closed curved stems; parallel rings have nonzero radii at both endpoints.
            void Tube(Func<float, Vector3> path, float thickness, Color color, int segments = 5, int sides = 5)
            {
                thickness = Mathf.Max(.0007f, thickness);
                int first = vertices.Count;
                for (int row = 0; row <= segments; row++)
                {
                    float t = row / (float)segments;
                    Vector3 center = path(t);
                    Frame(path(Mathf.Min(1, t + .01f)) - path(Mathf.Max(0, t - .01f)), out Vector3 side, out Vector3 normal);
                    float r = thickness * Mathf.Lerp(1, .62f, t);
                    for (int col = 0; col < sides; col++)
                    {
                        float angle = col * Mathf.PI * 2 / sides;
                        Vertex(center + (side * Mathf.Cos(angle) + normal * Mathf.Sin(angle)) * r, color, col / (float)sides);
                        if (row == 0) continue;
                        int a = first + (row - 1) * sides + col, b = first + (row - 1) * sides + (col + 1) % sides;
                        Quad(a, b, a + sides, b + sides);
                    }
                }
                int bottom = Vertex(path(0), color), top = Vertex(path(1), color);
                for (int col = 0; col < sides; col++)
                {
                    Triangle(bottom, first + (col + 1) % sides, first + col);
                    Triangle(top, first + segments * sides + col, first + segments * sides + (col + 1) % sides);
                }
            }

            // A cambered leaf or petal with an actual midrib and curved silhouette (16 faces).
            void Leaf(Vector3 root, Vector3 end, float fullWidth, float arch, Color color, bool rounded = false, float roll = 0)
            {
                Vector3 axis = end - root;
                if (axis.sqrMagnitude < 1e-8f) return;
                Frame(axis, out Vector3 side, out Vector3 normal);
                if (normal.y < 0) { side = -side; normal = -normal; }
                Quaternion rotation = Quaternion.AngleAxis(roll, axis.normalized);
                side = rotation * side; normal = rotation * normal;
                fullWidth = Mathf.Max(.001f, fullWidth);
                int start = Vertex(root, color), first = vertices.Count;
                const int rows = 4;
                for (int row = 0; row < rows; row++)
                {
                    float t = (row + 1f) / (rows + 1f);
                    float profile = Mathf.Pow(Mathf.Sin(t * Mathf.PI), rounded ? .38f : .85f);
                    if (!rounded) profile *= 1 + .06f * Mathf.Sin(t * Mathf.PI * 7);
                    float half = fullWidth * .5f * profile;
                    Vector3 center = Vector3.Lerp(root, end, t) + normal * (Mathf.Sin(t * Mathf.PI) * arch);
                    float edgeCurl = half * (.12f + curl * .3f);
                    Vertex(center - side * half - normal * edgeCurl, color, 0);
                    Vertex(center + normal * fullWidth * .045f, color, .5f);
                    Vertex(center + side * half - normal * edgeCurl, color, 1);
                    if (row == 0) { Triangle(start, first, first + 1); Triangle(start, first + 1, first + 2); }
                    else
                    {
                        int a = first + (row - 1) * 3, b = a + 3;
                        Quad(a, b, a + 1, b + 1); Quad(a + 1, b + 1, a + 2, b + 2);
                    }
                }
                int tip = Vertex(end, color);
                int last = first + (rows - 1) * 3;
                Triangle(last, tip, last + 1); Triangle(last + 1, tip, last + 2);
            }

            void Spindle(Vector3 start, Vector3 end, float r, Color color, float profile = 1, int sides = 8, int rows = 5)
            {
                Vector3 axis = end - start;
                Frame(axis, out Vector3 side, out Vector3 normal);
                r = Mathf.Max(.001f, r);
                int bottom = Vertex(start, color), first = vertices.Count;
                for (int row = 1; row <= rows; row++)
                {
                    float t = row / (rows + 1f);
                    float ring = Mathf.Pow(Mathf.Sin(t * Mathf.PI), profile) * r;
                    for (int col = 0; col < sides; col++)
                    {
                        float angle = col * Mathf.PI * 2 / sides;
                        Vertex(Vector3.Lerp(start, end, t) + (side * Mathf.Cos(angle) + normal * Mathf.Sin(angle)) * ring,
                            color, col / (float)sides);
                        int current = first + (row - 1) * sides + col, next = first + (row - 1) * sides + (col + 1) % sides;
                        if (row == 1) Triangle(bottom, next, current);
                        else Quad(current - sides, next - sides, current, next);
                    }
                }
                int top = Vertex(end, color);
                for (int col = 0; col < sides; col++)
                    Triangle(first + (rows - 1) * sides + col, first + (rows - 1) * sides + (col + 1) % sides, top);
            }

            Vector3 Stem(Vector3 root, float h, float angle, int leaves = 2)
            {
                Vector3 lean = Radial(angle) * h * (.035f + bend * .16f);
                Tube(t => StemPoint(root, lean, h, t), Mathf.Clamp(width * .10f, .0012f, .008f), green);
                for (int i = 0; i < leaves; i++)
                {
                    float t = .18f + i * .55f / Mathf.Max(1, leaves - 1);
                    Vector3 point = StemPoint(root, lean, h, t);
                    Vector3 outward = Radial(angle + i * 2.39996f);
                    float length = h * R(.13f, .24f) * aspect;
                    Leaf(point, point + outward * length + Vector3.up * length * .32f,
                        width * R(.65f, 1.2f), length * curl * .25f, green);
                }
                return root + Vector3.up * h + lean;
            }

            void Flower(Vector3 center, Vector3 up, float r, int count, Color petalColor, bool tiny = false)
            {
                Frame(up, out Vector3 x, out Vector3 z);
                float phase = R(0, Mathf.PI * 2);
                for (int p = 0; p < count; p++)
                {
                    float angle = phase + p * Mathf.PI * 2 / count + R(-.04f, .04f);
                    Vector3 radial = x * Mathf.Cos(angle) + z * Mathf.Sin(angle);
                    float length = r * R(.9f, 1.08f);
                    Leaf(center + radial * r * .13f, center + radial * length + up * (r * (.02f + curl * .22f)),
                        r * Mathf.Min(.90f, 3.5f / count), r * (.04f + curl * .12f), Pigment(petalColor, R(.92f, 1.04f)), true);
                }
                Spindle(center - up * r * .025f, center + up * r * .23f, r * .22f,
                    Pigment(source.centerColor), .6f, tiny ? 6 : 10, tiny ? 2 : 3);
            }

            void Plumes()
            {
                for (int stem = 0; stem < Mathf.Min(flowers, 5); stem++)
                {
                    Vector3 root = Root(); float angle = R(0, Mathf.PI * 2), h = height * R(1 - variation, 1);
                    float length = Mathf.Clamp(headLength, h * .15f, h * .62f);
                    Vector3 head = Stem(root, h - length * .78f, angle, Mathf.Clamp(blades / 7, 1, 3));
                    Vector3 outward = Radial(angle), across = Radial(angle + Mathf.PI * .5f);
                    float startAngle = .12f + bend * .15f, arc = .30f + bend * .45f + curl * 1.30f;
                    // A continuous arc lets the plume tip droop while its base meets the stem.
                    Func<float, Vector3> axis = t => head
                        + Vector3.up * (length * (Mathf.Sin(startAngle + arc * t) - Mathf.Sin(startAngle)) / arc)
                        + outward * (length * (Mathf.Cos(startAngle) - Mathf.Cos(startAngle + arc * t)) / arc);
                    Tube(axis, Mathf.Max(.0013f, width * .065f), Pigment(source.flowerColor, .82f), 7, 4);
                    int levels = Mathf.Clamp(blades, 6, 14);
                    for (int level = 0; level < levels; level++)
                    {
                        float t = .05f + level * .89f / Mathf.Max(1, levels - 1);
                        float envelope = Mathf.Pow(Mathf.Sin(t * Mathf.PI), .60f);
                        Vector3 tangent = Vector3.up * Mathf.Cos(startAngle + arc * t) + outward * Mathf.Sin(startAngle + arc * t);
                        Vector3 depth = Vector3.Cross(across, tangent).normalized;
                        for (int side = -1; side <= 1; side += 2)
                        {
                            Vector3 branchDirection = (across * side + depth * R(-.35f, .35f)).normalized;
                            Vector3 from = axis(t);
                            float arm = length * (.18f + radius * 1.4f) * envelope;
                            Vector3 tip = from + branchDirection * arm + tangent * length * .10f;
                            Tube(u => Vector3.Lerp(from, tip, u) + tangent * Mathf.Sin(u * Mathf.PI) * arm * .1f,
                                Mathf.Max(.0007f, width * .025f), Pigment(source.flowerColor, .88f), 2, 3);
                            for (int floret = 0; floret < 4; floret++)
                            {
                                Vector3 at = Vector3.Lerp(from, tip, .10f + floret * .19f);
                                Vector3 end = tip + tangent * length * R(.06f, .16f) + depth * R(-arm * .18f, arm * .18f);
                                Leaf(at, end, Mathf.Max(.008f, arm * .42f), arm * .07f,
                                    Pigment(source.flowerColor, R(.88f, 1.04f)), true, floret * 40 - 60);
                            }
                        }
                    }
                }
            }

            void SeedHeads(bool fox)
            {
                for (int stem = 0; stem < flowers; stem++)
                {
                    float h = height * R(1 - variation, 1), angle = R(0, Mathf.PI * 2);
                    float length = Mathf.Clamp(headLength, h * .08f, h * (fox ? .48f : .42f));
                    Vector3 head = Stem(Root(), h - length, angle, Mathf.Clamp(blades / 6, 1, 4));
                    Vector3 axis = (Vector3.up + Radial(angle) * bend * .35f).normalized;
                    float thick = fox ? Mathf.Clamp(radius * .55f, .012f, length * .36f) : Mathf.Clamp(radius * .32f, .009f, length * .2f);
                    Spindle(head, head + axis * length, thick, Pigment(source.flowerColor), fox ? .8f : .28f, 10, 6);
                    if (!fox)
                    {
                        Tube(t => head + axis * (length + t * length * .22f), .0009f, Pigment(source.flowerColor, .75f), 2, 4);
                        continue;
                    }
                    Frame(axis, out Vector3 x, out Vector3 z);
                    int tufts = Mathf.Clamp(blades + 6, 10, 28);
                    for (int i = 0; i < tufts; i++)
                    {
                        float t = R(.12f, .85f), a = i * 2.399963f;
                        Vector3 radial = x * Mathf.Cos(a) + z * Mathf.Sin(a);
                        Vector3 start = head + axis * length * t + radial * thick * Mathf.Sin(t * Mathf.PI) * .82f;
                        Leaf(start, start + axis * length * R(.1f, .2f) + radial * thick * .9f,
                            thick * .32f, thick * .12f, Pigment(source.flowerColor, R(.88f, 1.06f)), false, i * 33);
                    }
                }
            }

            void FlowerSpikes()
            {
                for (int stem = 0; stem < flowers; stem++)
                {
                    float h = height * R(1 - variation, 1), angle = R(0, Mathf.PI * 2);
                    float length = Mathf.Clamp(headLength, h * .18f, h * .65f);
                    Vector3 head = Stem(Root(), h - length, angle, Mathf.Clamp(blades / 5, 2, 5));
                    Vector3 axis = (Vector3.up + Radial(angle) * bend * .15f).normalized;
                    Tube(t => head + axis * length * t, Mathf.Max(.001f, width * .07f), green, 4, 4);
                    int levels = Mathf.Clamp(blades / 2, 3, 7);
                    for (int i = 0; i < levels; i++)
                        for (int side = 0; side < 2; side++)
                        {
                            float t = .08f + i * .84f / Mathf.Max(1, levels - 1);
                            Vector3 outward = Radial(angle + i * 2.39996f + side * Mathf.PI);
                            float r = radius * .35f * Mathf.Lerp(1, .55f, t);
                            Vector3 center = head + axis * length * t + outward * r * .65f;
                            Flower(center, (outward + Vector3.up * .7f).normalized, r, Mathf.Min(petals, 5), source.flowerColor, true);
                        }
                }
            }

            void FlowerSprays()
            {
                for (int stem = 0; stem < flowers; stem++)
                {
                    float h = height * R(1 - variation, 1), angle = R(0, Mathf.PI * 2);
                    Vector3 head = Stem(Root(), h * .65f, angle, 2);
                    for (int branch = 0; branch < 3; branch++)
                    {
                        Vector3 outwards = Radial(angle + branch * 2.399963f);
                        Vector3 split = head + outwards * (spread * .65f + h * .12f) + Vector3.up * h * .18f;
                        Tube(t => Vector3.Lerp(head, split, t) + Vector3.up * Mathf.Sin(t * Mathf.PI) * h * .025f,
                            .0015f, green, 3, 4);
                        int tips = Mathf.Clamp(blades / 5, 2, 4);
                        for (int tip = 0; tip < tips; tip++)
                        {
                            Vector3 flower = split + Radial(angle + tip * 2.4f + branch) * h * .08f + Vector3.up * h * R(.03f, .14f);
                            Tube(t => Vector3.Lerp(split, flower, t), .0008f, green, 2, 3);
                            Flower(flower, (Vector3.up + outwards * .2f).normalized, radius * .32f,
                                Mathf.Min(petals, 5), source.flowerColor, true);
                        }
                    }
                }
            }

            void Ferns()
            {
                int fronds = Mathf.Clamp(Mathf.RoundToInt(Mathf.Sqrt(blades) * 2.05f), 4, 12);
                float phase = R(0, Mathf.PI * 2);
                for (int f = 0; f < fronds; f++)
                {
                    float h = height * R(1 - variation, 1);
                    Vector3 root = Root() * .4f, direction = Radial(phase + f * 2.399963f);
                    Vector3 side = Vector3.Cross(Vector3.up, direction);
                    float reach = spread + h * (.45f + bend * .22f);
                    Func<float, Vector3> axis = t => root + direction * reach * t + Vector3.up * (h * Mathf.Sin(t * Mathf.PI * (.65f + curl * .2f)));
                    Tube(axis, Mathf.Clamp(width * .07f, .0012f, .004f), green, 8, 4);
                    int pairs = Mathf.Clamp(12 + Mathf.RoundToInt(headLength * 10), 12, 18);
                    for (int i = 0; i < pairs; i++)
                    {
                        float t = .12f + i * .82f / (pairs - 1f);
                        float pinna = h * .34f * Mathf.Pow(Mathf.Sin(t * Mathf.PI), .55f) * (1 - t * .5f) * aspect;
                        for (int s = -1; s <= 1; s += 2)
                        {
                            Vector3 point = axis(t + (s > 0 ? .009f : 0));
                            Leaf(point, point + side * (s * pinna) + direction * pinna * .28f + Vector3.up * pinna * .03f,
                                Mathf.Max(width * .95f * Mathf.Pow(1 - t, .35f), pinna * .48f / Mathf.Sqrt(aspect)),
                                pinna * (.08f + curl * .08f), green);
                        }
                    }
                    Leaf(axis(.88f), axis(1), width * .8f, h * .015f, green);
                }
            }

            void Wildflowers()
            {
                for (int stem = 0; stem < flowers; stem++)
                {
                    float angle = R(0, Mathf.PI * 2), h = height * R(1 - variation, 1);
                    Vector3 head = Stem(Root(), h, angle, Mathf.Clamp(blades / 5, 2, 5));
                    Vector3 up = Quaternion.Euler(R(-18, 18), 0, R(-18, 18)) * Vector3.up;
                    Flower(head, up, radius * R(.85f, 1.08f), petals, source.flowerColor);
                }
            }

            void Bellflowers()
            {
                for (int stem = 0; stem < flowers; stem++)
                {
                    float angle = R(0, Mathf.PI * 2), h = height * R(1 - variation, 1);
                    Vector3 head = Stem(Root(), h, angle, Mathf.Clamp(blades / 5, 2, 5));
                    Vector3 radial = Radial(angle);
                    Vector3 side = Vector3.Cross(Vector3.up, radial);
                    Vector3 axis = Quaternion.AngleAxis(Mathf.Lerp(12, 155, curl), side) * Vector3.up;
                    float length = Mathf.Clamp(headLength, radius * .7f, Mathf.Max(radius, h * .5f));
                    Vector3 neck = head + radial * length * .23f;
                    Tube(t => Vector3.Lerp(head, neck, t) + Vector3.up * Mathf.Sin(t * Mathf.PI) * length * .16f, .0015f, green, 3, 4);
                    Frame(axis, out Vector3 x, out Vector3 z);
                    int sides = petals * 3, first = vertices.Count;
                    for (int row = 0; row <= 5; row++)
                    {
                        float t = row / 5f;
                        float r = radius * (.24f + .76f * Mathf.Pow(Mathf.Sin(t * Mathf.PI * .5f), .7f));
                        for (int col = 0; col < sides; col++)
                        {
                            float a = col * Mathf.PI * 2 / sides;
                            float lobe = Mathf.Cos(a * petals) * Mathf.Pow(t, 5);
                            Vector3 point = neck + axis * (length * t + radius * .15f * lobe)
                                + (x * Mathf.Cos(a) + z * Mathf.Sin(a)) * r * (1 + .12f * lobe);
                            Vertex(point, Pigment(source.flowerColor, .82f + t * .18f), col / (float)sides);
                            if (row == 0) continue;
                            int a0 = first + (row - 1) * sides + col, b0 = first + (row - 1) * sides + (col + 1) % sides;
                            Quad(a0, b0, a0 + sides, b0 + sides);
                        }
                    }
                    int bottom = Vertex(neck, Pigment(source.centerColor));
                    for (int i = 0; i < sides; i++) Triangle(bottom, first + (i + 1) % sides, first + i);
                    Spindle(neck + axis * length * .12f, neck + axis * length * .55f, radius * .10f, Pigment(source.centerColor), .7f, 6, 3);
                }
            }

            void Herbs()
            {
                for (int stem = 0; stem < Mathf.Min(flowers, 5); stem++)
                {
                    float angle = R(0, Mathf.PI * 2), h = height * R(1 - variation, 1);
                    Vector3 root = Root(), lean = Radial(angle) * h * bend * .12f;
                    Tube(t => StemPoint(root, lean, h, t), Mathf.Clamp(width * .09f, .0015f, .006f), green, 4, 5);
                    int layers = Mathf.Clamp(blades / 2, 3, 9);
                    for (int i = 0; i < layers; i++)
                    {
                        float t = .15f + i * .78f / (layers - 1f);
                        for (int side = 0; side < 2; side++)
                        {
                            Vector3 start = StemPoint(root, lean, h, t), outward = Radial(angle + i * 1.2f + side * Mathf.PI);
                            float length = (spread * .55f + h * .18f) * aspect * Mathf.Lerp(1, .5f, t);
                            Leaf(start, start + outward * length + Vector3.up * length * (.2f + curl * .3f),
                                width * 2.3f * Mathf.Lerp(1, .55f, t), length * (.08f + curl * .15f), green);
                        }
                    }
                }
            }

            void RoundLeaf(Vector3 center, Vector3 direction, float r)
            {
                Vector3 x = Vector3.Cross(Vector3.up, direction).normalized;
                int middle = Vertex(center + Vector3.up * r * .09f, green), first = vertices.Count;
                const int sides = 12;
                for (int ring = 1; ring <= 2; ring++)
                    for (int i = 0; i < sides; i++)
                    {
                        float a = i * Mathf.PI * 2 / sides, scale = ring * .5f;
                        Vector3 p = center + (x * Mathf.Cos(a) + direction * Mathf.Sin(a) * aspect) * r * scale;
                        p.y += r * (.12f * Mathf.Sin(a) - curl * .28f * scale * scale + .05f * Mathf.Cos(a * 3));
                        Vertex(p, green, .5f + Mathf.Cos(a) * .5f);
                        int at = first + (ring - 1) * sides + i, next = first + (ring - 1) * sides + (i + 1) % sides;
                        if (ring == 1) Triangle(middle, next, at); else Quad(at - sides, next - sides, at, next);
                    }
            }

            void Groundcover()
            {
                int groups = Mathf.Clamp((blades + 2) / 3, 1, 10);
                for (int group = 0; group < groups; group++)
                {
                    float angle = R(0, Mathf.PI * 2), h = height * R(.35f, 1 - variation * .3f);
                    Vector3 root = Radial(angle) * spread * Mathf.Sqrt(R(0, 1));
                    Vector3 top = root + Vector3.up * h + Radial(angle) * h * bend * .18f;
                    Tube(t => Vector3.Lerp(root, top, t), Mathf.Clamp(width * .07f, .001f, .0035f), green, 3, 4);
                    float r = Mathf.Clamp(radius * .65f + width * .6f, .012f, .14f);
                    for (int leaflet = 0; leaflet < 3; leaflet++)
                    {
                        Vector3 direction = Radial(angle + leaflet * Mathf.PI * 2 / 3);
                        RoundLeaf(top + direction * r * .65f, direction, r);
                    }
                }
            }
        }
    }
}
