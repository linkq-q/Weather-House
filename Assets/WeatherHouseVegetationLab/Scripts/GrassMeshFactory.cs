using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace WeatherHouse.Vegetation
{
    public static class GrassMeshFactory
    {
        public static Mesh Build(GrassSpecies species, int variation = 0)
        {
            if (species == null) throw new ArgumentNullException(nameof(species));
            if (species.kind >= GrassKind.Plume) return PlantMeshFactory.Build(species, variation);
            var builder = new Builder(species, unchecked(species.seed + variation * 7919), variation);
            return builder.Build();
        }

        private sealed class Builder
        {
            private readonly GrassSpecies species;
            private readonly System.Random random;
            private readonly float tuftScale;
            private readonly List<Vector3> positions = new List<Vector3>();
            private readonly List<Vector2> uv = new List<Vector2>();
            private readonly List<Vector2> wind = new List<Vector2>();
            private readonly List<Color> colors = new List<Color>();
            private readonly List<int> triangles = new List<int>();
            private readonly Color grass = new Color(1, 1, 1, 0);

            public Builder(GrassSpecies source, int seed, int variation)
            {
                species = source;
                random = new System.Random(seed);
                int size = (variation % 3 + 3) % 3;
                tuftScale = source.kind == GrassKind.Daisy ? 1f : size == 0 ? 0.8f : size == 1 ? 1f : 1.16f;
            }

            private float Range(float low, float high)
            {
                return Mathf.Lerp(low, high, (float)random.NextDouble());
            }

            private Vector3 GroundPoint()
            {
                float angle = Range(0f, Mathf.PI * 2f);
                float radius = Mathf.Sqrt(Range(0f, 1f)) * Mathf.Max(0.01f, species.spread);
                return new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * radius;
            }

            public Mesh Build()
            {
                bool daisy = species.kind == GrassKind.Daisy;
                int blades = daisy ? 0 : Mathf.Clamp(species.bladeCount, 1, 40);
                float rotation = Range(0f, Mathf.PI * 2f);
                for (int i = 0; i < blades; i++)
                {
                    float height = Mathf.Max(0.03f, species.height) * tuftScale * Range(1f - species.heightVariation, 1f);
                    // Most blades stay in the upright bundle; a few shorter outer blades open gently.
                    float angle = rotation + i * 2.399963f + Range(-0.15f, 0.15f);
                    Vector3 outward = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle));
                    Vector3 root = outward * species.spread * tuftScale * Range(0.025f, Mathf.Clamp(species.rootSpread, 0.025f, 0.4f));
                    float outerFraction = Mathf.Clamp01(species.outerBladeFraction);
                    // At the legacy default 0.25 this still selects 0, 4, 8... without consuming random values.
                    bool outer = Mathf.FloorToInt(i * outerFraction) != Mathf.FloorToInt((i - 1) * outerFraction);
                    float fan = species.spread * tuftScale * (outer ? Range(.55f,.9f) : Range(.08f,.38f));
                    if (outer) height *= Range(.76f,.93f);
                    float layering = Mathf.Clamp01(species.clumpLayering);
                    if (layering > 0f)
                    {
                        height *= Mathf.Lerp(1f, outer ? .72f : (i % 3 == 1 ? .88f : 1f), layering);
                        fan *= Mathf.Lerp(1f, outer ? 1.22f : .72f, layering);
                    }
                    AddBlade(root, height, species.width * tuftScale * Range(0.65f, 1f),
                        angle, species.bend, fan);
                }
                if (daisy)
                    for (int i = 0; i < Mathf.Clamp(species.flowerCount, 1, 7); i++) AddDaisy();

                var mesh = new Mesh { name = species.name + "_Clump", indexFormat = IndexFormat.UInt32 };
                mesh.SetVertices(positions);
                mesh.SetUVs(0, uv);
                mesh.SetUVs(1, wind);
                mesh.SetColors(colors);
                mesh.SetTriangles(triangles, 0);
                mesh.RecalculateNormals();
                mesh.RecalculateBounds();
                var bounds = mesh.bounds;
                bounds.Expand(new Vector3(1.6f, 0.2f, 1.6f));
                mesh.bounds = bounds;
                return mesh;
            }

            private int Vertex(Vector3 p, Vector2 texcoord, Color color)
            {
                int index = positions.Count;
                positions.Add(p);
                uv.Add(texcoord);
                wind.Add(new Vector2(Mathf.Pow(Mathf.Clamp01(p.y / Mathf.Max(0.03f, species.height)), 1.6f), 0));
                colors.Add(color);
                return index;
            }

            private void Triangle(int a, int b, int c)
            {
                triangles.Add(a); triangles.Add(b); triangles.Add(c);
            }

            private void AddBlade(Vector3 root, float height, float width, float angle, float bend, float tipSpread = -1f)
            {
                Vector3 forward = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle));
                Vector3 side = new Vector3(-forward.z, 0, forward.x);
                bool uprightTuft = tipSpread >= 0;
                const int rows = 4;
                int start = positions.Count;
                for (int row = 0; row < rows; row++)
                {
                    float t = row / (float)rows;
                    float profile = uprightTuft
                        ? (0.3f + 0.7f * Mathf.Sin(Mathf.PI * Mathf.Pow(t, 0.72f))) * Mathf.Pow(1f - t, 0.35f)
                        : Mathf.Pow(1f - t, 0.65f);
                    float halfWidth = Mathf.Max(0.005f, width) * 0.5f * profile;
                    float lean = uprightTuft
                        ? tipSpread * Mathf.Lerp(t, Mathf.Pow(t, 1f + 2f * Mathf.Clamp(bend, 0, 1.5f)), 0.9f)
                        : height * bend * t * t;
                    Vector3 center = root + Vector3.up * height * t + forward * lean;
                    Vertex(center - side * halfWidth, new Vector2(0, t), grass);
                    Vertex(center + forward * halfWidth * 0.12f, new Vector2(0.5f, t), grass);
                    Vertex(center + side * halfWidth, new Vector2(1, t), grass);
                    if (row == 0) continue;
                    int a = start + (row - 1) * 3;
                    int b = a + 3;
                    for (int column = 0; column < 2; column++)
                    {
                        Triangle(a + column, b + column, a + column + 1);
                        Triangle(a + column + 1, b + column, b + column + 1);
                    }
                }
                // A tiny rounded cap keeps the upper blade full instead of tapering to a long needle.
                Vector3 tipCenter = root + Vector3.up * height + forward * (uprightTuft ? tipSpread : height * bend);
                int last = start + (rows - 1) * 3;
                float tipRatio = Mathf.Clamp(species.tipWidthRatio, 0f, 0.4f);
                if (tipRatio <= 0.001f)
                {
                    int point = Vertex(tipCenter, new Vector2(0.5f, 1), grass);
                    Triangle(last, point, last + 1);
                    Triangle(last + 1, point, last + 2);
                    return;
                }
                Vector3 tipShoulder = tipCenter - Vector3.up * height * 0.015f;
                float tipHalfWidth = Mathf.Max(0.005f, width) * tipRatio * 0.5f;
                int tip = Vertex(tipShoulder - side * tipHalfWidth, new Vector2(0, 0.985f), grass);
                Vertex(tipCenter, new Vector2(0.5f, 1), grass);
                Vertex(tipShoulder + side * tipHalfWidth, new Vector2(1, 0.985f), grass);
                Triangle(last, tip, last + 1);
                Triangle(last + 1, tip, tip + 1);
                Triangle(last + 1, tip + 1, tip + 2);
                Triangle(last + 1, tip + 2, last + 2);
            }

            private void AddDaisy()
            {
                Vector3 root = GroundPoint();
                float height = Mathf.Max(0.03f, species.height) * Range(0.72f, 1.08f);
                float angle = Range(0f, Mathf.PI * 2f);
                Vector3 lean = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * height * species.bend * 0.25f;
                Vector3 head = root + Vector3.up * height + lean;
                float stemRadius = Mathf.Clamp(species.width * 0.07f, 0.003f, 0.009f);
                int stemStart = positions.Count;
                const int sides = 5;
                for (int row = 0; row <= 3; row++)
                {
                    float t = row / 3f;
                    Vector3 center = root + Vector3.up * height * t + lean * t * t;
                    for (int i = 0; i < sides; i++)
                    {
                        float theta = i * Mathf.PI * 2f / sides;
                        Vertex(center + new Vector3(Mathf.Cos(theta), 0, Mathf.Sin(theta)) * stemRadius,
                            new Vector2(i / (float)sides, t), grass);
                        if (row == 0) continue;
                        int a = stemStart + (row - 1) * sides + i;
                        int b = stemStart + (row - 1) * sides + (i + 1) % sides;
                        Triangle(a, a + sides, b);
                        Triangle(b, a + sides, b + sides);
                    }
                }
                AddBlade(root + Vector3.up * height * 0.25f + lean * 0.0625f,
                    height * 0.21f, species.width * 0.20f, angle + 1.7f, 1.5f, height * 0.18f);
                AddBlade(root + Vector3.up * height * 0.47f + lean * 0.2209f,
                    height * 0.15f, species.width * 0.16f, angle - 1.4f, 1.5f, height * 0.14f);

                float radius = Mathf.Clamp(species.flowerRadius, 0.025f, 0.22f) * Range(0.82f, 1.18f);
                Quaternion tilt = Quaternion.Euler(Range(-22, 22), Range(0, 360), Range(-22, 22));
                int petals = random.Next(9, 13);
                for (int p = 0; p < petals; p++)
                {
                    float theta = p * Mathf.PI * 2f / petals + Range(-0.025f, 0.025f);
                    Vector3 radial = new Vector3(Mathf.Cos(theta), 0, Mathf.Sin(theta));
                    Vector3 tangent = new Vector3(-radial.z, 0, radial.x);
                    float length = radius * Range(0.85f, 1.1f);
                    float width = length * 0.20f;
                    Vector3[] outline =
                    {
                        radial * (length * 0.18f) - tangent * width * 0.25f,
                        radial * (length * 0.55f) - tangent * width,
                        radial * (length * 0.88f) - tangent * width * 0.82f,
                        radial * length,
                        radial * (length * 0.88f) + tangent * width * 0.82f,
                        radial * (length * 0.55f) + tangent * width,
                        radial * (length * 0.18f) + tangent * width * 0.25f
                    };
                    Color white = species.flowerColor * Range(0.95f, 1f);
                    white.a = 1;
                    int center = Vertex(head + tilt * (radial * length * 0.58f + Vector3.up * radius * 0.06f),
                        new Vector2(0.5f, 1), white);
                    int first = positions.Count;
                    for (int i = 0; i < outline.Length; i++)
                    {
                        Vector3 point = outline[i];
                        point.y = radius * (Mathf.Pow(point.magnitude / length, 2f) * 0.16f - 0.035f);
                        Vertex(head + tilt * point, new Vector2(0.5f, 1), white);
                    }
                    for (int i = 0; i < outline.Length; i++) Triangle(center, first + i, first + (i + 1) % outline.Length);
                }

                Color yellow = species.centerColor;
                yellow.a = 1;
                int dome = Vertex(head + tilt * Vector3.up * radius * 0.2f, new Vector2(0.5f, 1), yellow);
                int rim = positions.Count;
                for (int i = 0; i < 12; i++)
                {
                    float theta = i * Mathf.PI * 2f / 12;
                    Vertex(head + tilt * (new Vector3(Mathf.Cos(theta), 0.1f, Mathf.Sin(theta)) * radius * 0.24f),
                        new Vector2(0.5f, 1), yellow);
                }
                for (int i = 0; i < 12; i++) Triangle(dome, rim + (i + 1) % 12, rim + i);
            }
        }
    }
}
