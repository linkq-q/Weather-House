using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace WeatherHouse.Vegetation
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public sealed class PainterlyTree : MonoBehaviour
    {
        [Header("树形 · 修改后使用 Inspector 中的更新按钮")]
        [Tooltip("整体高度的参考尺寸，树冠边缘有少量自然起伏。单位为米。")]
        [Range(2f, 12f)] public float height = 6.2f;
        [Tooltip("主干根部半径。不是树干直径。")]
        [Range(.12f, .9f)] public float trunkRadius = .38f;
        [Tooltip("主干弯曲量相对高度的比例，保持根部在原点。")]
        [Range(0f, .4f)] public float trunkBend = .22f;
        [Tooltip("主要树冠展开半径，边缘笔触会略微伸出。")]
        [Range(1f, 5f)] public float crownRadius = 2.5f;
        [Tooltip("只改变固定朝向的实体叶片数量；不改变枝干和树冠簇的位置。")]
        [Range(.15f, 1f)] public float crownDensity = .78f;
        [Range(3, 8)] public int branchCount = 6;
        public int seed = 913;

        // Caller owns the returned mesh. The Editor button updates a persistent asset;
        // no hidden OnValidate generation, runtime polling, or scene-wide mutation.
        public Mesh CreateMesh()
        {
            return new Builder(this).Build();
        }

        public static Material CreateMaterial(bool foliage)
        {
            Shader shader = Shader.Find("Weather House/Painterly Tree");
            if (!shader) throw new InvalidOperationException("Painterly Tree shader is not imported.");
            var material = new Material(shader) { name = foliage ? "Painterly Tree Leaves" : "Painterly Tree Bark" };
            material.SetFloat("_Foliage", foliage ? 1 : 0);
            material.SetColor("_BaseColor", foliage ? new Color(.48f, .57f, .29f) : new Color(.36f, .31f, .23f));
            material.SetColor("_LitColor", foliage ? new Color(.69f, .72f, .40f) : new Color(.61f, .48f, .29f));
            material.SetColor("_ShadowColor", foliage ? new Color(.29f, .42f, .33f) : new Color(.22f, .28f, .25f));
            material.SetFloat("_FoliageShadowStrength", .38f);
            return material;
        }

        void OnValidate()
        {
            height = Mathf.Clamp(height, 2f, 12f);
            trunkRadius = Mathf.Clamp(trunkRadius, .12f, .9f);
            trunkBend = Mathf.Clamp(trunkBend, 0f, .4f);
            crownRadius = Mathf.Clamp(crownRadius, 1f, 5f);
            crownDensity = Mathf.Clamp(crownDensity, .15f, 1f);
            branchCount = Mathf.Clamp(branchCount, 3, 8);
        }

        sealed class Builder
        {
            readonly PainterlyTree source;
            readonly System.Random random;
            readonly List<Vector3> vertices = new List<Vector3>();
            readonly List<Vector3> normals = new List<Vector3>();
            readonly List<Vector2> uv = new List<Vector2>();
            readonly List<Color> colors = new List<Color>();
            readonly List<int> wood = new List<int>();
            readonly List<int> leaves = new List<int>();
            readonly List<Cloud> clouds = new List<Cloud>();
            readonly float h, radius, bend, crown, phase;

            struct Cloud
            {
                public Vector3 center, size;
                public float angle;
                public int seed;
            }

            public Builder(PainterlyTree source)
            {
                this.source = source;
                random = new System.Random(source.seed);
                h = Mathf.Clamp(source.height, 2, 12);
                radius = Mathf.Clamp(source.trunkRadius, .12f, .9f);
                bend = Mathf.Clamp(source.trunkBend, 0, .4f);
                crown = Mathf.Clamp(source.crownRadius, 1, 5);
                phase = Range(0, Mathf.PI * 2);
            }

            public Mesh Build()
            {
                var trunk = new Vector3[10];
                for (int i = 0; i < trunk.Length; i++) trunk[i] = Trunk(i / (float)(trunk.Length - 1));
                Tube(trunk, radius, radius * .13f, 11);

                // Roots flare into the soil; their narrow ends stay slightly below it.
                for (int i = 0; i < 5; i++)
                {
                    Vector3 direction = Direction(phase + i * Mathf.PI * 2 / 5);
                    Tube(new[] { Trunk(.05f), direction * radius * 1.3f + Vector3.up * .08f,
                        direction * radius * Range(2f, 2.8f) - Vector3.up * .035f },
                        radius * .47f, radius * .045f, 6);
                }

                int count = Mathf.Clamp(source.branchCount, 3, 8);
                for (int i = 0; i < count; i++)
                {
                    float t = (i + .5f) / count;
                    float angle = phase + i * 2.399963f + Range(-.18f, .18f);
                    Vector3 direction = Direction(angle);
                    Vector3 start = Trunk(Mathf.Lerp(.36f, .84f, t));
                    Vector3 tip = Trunk(.79f) + direction * crown * Range(.46f, .68f);
                    tip.y = h * (.59f + .17f * t + Range(-.025f, .025f));
                    Vector3 control = Vector3.Lerp(start, tip, .47f) - Vector3.up * h * .065f;
                    Vector3[] branch = Curve(start, control, tip, 7);
                    Tube(branch, radius * Mathf.Lerp(.57f, .25f, t), radius * .045f, 8);
                    AddCloud(tip + Vector3.up * h * .055f,
                        new Vector3(crown * .43f, h * .115f, crown * .36f), angle);

                    for (int j = 0; j < 2; j++)
                    {
                        float split = .56f + j * .18f;
                        Vector3 fork = Bezier(start, control, tip, split);
                        Vector3 twigTip = Vector3.Lerp(fork, tip, .65f)
                            + Direction(angle + (j == 0 ? 1.1f : -1.15f)) * crown * .27f
                            + Vector3.up * h * Range(.065f, .105f);
                        Tube(Curve(fork, Vector3.Lerp(fork, twigTip, .48f) - Vector3.up * h * .025f, twigTip, 5),
                            radius * .14f, radius * .022f, 6);
                        AddCloud(twigTip + Vector3.up * h * .025f,
                            new Vector3(crown * .29f, h * .085f, crown * .25f), angle + j * .8f);
                    }
                }
                AddCloud(Trunk(.98f) + Vector3.up * h * .045f,
                    new Vector3(crown * .39f, h * .12f, crown * .34f), phase + .4f);
                foreach (Cloud cloud in clouds) Foliage(cloud);

                var mesh = new Mesh { name = "Painterly broadleaf " + source.seed, indexFormat = IndexFormat.UInt32 };
                mesh.SetVertices(vertices);
                mesh.SetNormals(normals);
                mesh.SetUVs(0, uv);
                mesh.SetColors(colors);
                mesh.subMeshCount = 2;
                mesh.SetTriangles(wood, 0);
                mesh.SetTriangles(leaves, 1);
                mesh.RecalculateBounds();
                Bounds bounds = mesh.bounds;
                bounds.Expand(.45f);
                mesh.bounds = bounds;
                return mesh;
            }

            Vector3 Trunk(float t)
            {
                float x = h * bend * (Mathf.Sin(t * Mathf.PI * 1.32f) * .51f + t * .34f);
                float z = h * .038f * (Mathf.Sin(t * Mathf.PI * 1.7f + phase) - Mathf.Sin(phase));
                return new Vector3(x, h * .76f * t, z);
            }

            void AddCloud(Vector3 center, Vector3 size, float angle)
            {
                clouds.Add(new Cloud { center = center, size = size * Range(.88f, 1.12f), angle = angle, seed = random.Next() });
            }

            void Foliage(Cloud cloud)
            {
                // Each cluster has its own random stream. Density never moves the skeleton.
                var rng = new System.Random(cloud.seed);
                int count = Mathf.RoundToInt(Mathf.Lerp(75, 230, Mathf.Clamp01(source.crownDensity)));
                Quaternion rotation = Quaternion.Euler(0, cloud.angle * Mathf.Rad2Deg, 0);
                float offset = (float)rng.NextDouble() * Mathf.PI * 2;
                for (int i = 0; i < count; i++)
                {
                    float y = 1 - 2 * (i + .5f) / count;
                    float ring = Mathf.Sqrt(1 - y * y);
                    float angle = i * 2.399963f + offset;
                    Vector3 radial = new Vector3(Mathf.Cos(angle) * ring, y, Mathf.Sin(angle) * ring);
                    // Uneven, flattened overlapping masses; no solid sphere under the leaves.
                    float lobe = 1 + .14f * Mathf.Sin(angle * 3 + y * 4 + offset)
                        + .08f * Mathf.Cos(angle * 5 - y * 3);
                    float shell = Mathf.Lerp(.72f, 1.03f, (float)rng.NextDouble()) * lobe;
                    Vector3 point = cloud.center + rotation * Vector3.Scale(radial, cloud.size) * shell;
                    Vector3 outward = rotation * new Vector3(radial.x, radial.y * 1.3f, radial.z);
                    Vector3 facing = (outward + Vector3.up * .5f).normalized;
                    // Broad rounded brush marks, fixed in object space, rather than radial stars.
                    float size = Mathf.Min(cloud.size.x, cloud.size.z) * Mathf.Lerp(.13f, .20f, (float)rng.NextDouble());
                    float twist = (float)rng.NextDouble() * Mathf.PI * 2;
                    Vector3 side = Vector3.Cross(facing, Mathf.Abs(facing.y) < .95f ? Vector3.up : Vector3.forward).normalized;
                    Vector3 along = Vector3.Cross(side, facing).normalized;
                    Vector3 across = side * Mathf.Cos(twist) + along * Mathf.Sin(twist);
                    along = Vector3.Cross(across, facing).normalized;
                    float elongation = Mathf.Lerp(1.02f, 1.22f, (float)rng.NextDouble());
                    float tint = Mathf.Lerp(.9f, 1f, (float)rng.NextDouble());
                    // Shade leaves as a coherent canopy envelope; geometry remains fully 3D.
                    Vector3 shadeNormal = (outward * .55f + Vector3.up * 1.1f).normalized;
                    int first = vertices.Count;
                    float wind = Mathf.Pow(Mathf.Clamp01(point.y / h), 2);
                    Vertex(point + facing * size * .18f, shadeNormal, new Vector2(.5f, point.y / h), tint, wind);
                    const int edges = 8;
                    for (int edge = 0; edge < edges; edge++)
                    {
                        float a = edge * Mathf.PI * 2 / edges;
                        float irregular = 1 + .045f * Mathf.Sin(edge * 2.7f + offset);
                        Vector3 direction = across * Mathf.Cos(a) + along * Mathf.Sin(a) * elongation;
                        Vector3 leaf = point + direction * size * irregular;
                        Vertex(leaf, (shadeNormal + direction.normalized * .12f).normalized,
                            new Vector2(edge / (float)(edges - 1), leaf.y / h), tint, wind);
                    }
                    for (int edge = 0; edge < edges; edge++) Triangle(leaves, first, first + 1 + (edge + 1) % edges, first + 1 + edge);
                }
            }

            void Tube(Vector3[] path, float bottom, float top, int sides)
            {
                int first = vertices.Count;
                Vector3 initialTangent = (path[1] - path[0]).normalized;
                Vector3 previousRight = Vector3.Cross(initialTangent,
                    Mathf.Abs(initialTangent.y) < .95f ? Vector3.up : Vector3.forward).normalized;
                for (int ring = 0; ring < path.Length; ring++)
                {
                    float t = ring / (float)(path.Length - 1);
                    Vector3 tangent = (path[Mathf.Min(ring + 1, path.Length - 1)] - path[Mathf.Max(0, ring - 1)]).normalized;
                    Vector3 right = Vector3.ProjectOnPlane(previousRight, tangent).normalized;
                    if (right.sqrMagnitude < .5f) right = Vector3.Cross(tangent, Vector3.right).normalized;
                    Vector3 forward = Vector3.Cross(tangent, right).normalized;
                    previousRight = right;
                    float r = Mathf.Lerp(bottom, top, Mathf.Pow(t, .8f));
                    for (int side = 0; side < sides; side++)
                    {
                        float a = side * Mathf.PI * 2 / sides;
                        Vector3 normal = right * Mathf.Cos(a) + forward * Mathf.Sin(a);
                        float ridge = 1 + .06f * Mathf.Sin(side * 2.6f + t * 2 + phase);
                        Vertex(path[ring] + normal * r * ridge, normal,
                            new Vector2(side / (float)sides, path[ring].y / h), .94f + .04f * Mathf.Sin(a * 3 + phase),
                            Mathf.Pow(Mathf.Clamp01(path[ring].y / h), 3) * .32f);
                    }
                }
                for (int ring = 0; ring < path.Length - 1; ring++)
                    for (int side = 0; side < sides; side++)
                    {
                        int next = (side + 1) % sides;
                        int a = first + ring * sides + side, b = first + ring * sides + next;
                        Triangle(wood, a, b, b + sides);
                        Triangle(wood, a, b + sides, a + sides);
                    }
                int cap = vertices.Count;
                Vertex(path[0], (path[0] - path[1]).normalized, Vector2.zero, .94f, 0);
                Vertex(path[path.Length - 1], (path[path.Length - 1] - path[path.Length - 2]).normalized, Vector2.one, .94f, .1f);
                int last = first + (path.Length - 1) * sides;
                for (int side = 0; side < sides; side++)
                {
                    int next = (side + 1) % sides;
                    Triangle(wood, cap, first + next, first + side);
                    Triangle(wood, cap + 1, last + side, last + next);
                }
            }

            void Vertex(Vector3 position, Vector3 normal, Vector2 texcoord, float tint, float wind)
            {
                vertices.Add(position); normals.Add(normal); uv.Add(texcoord);
                colors.Add(new Color(tint, tint, tint, wind));
            }
            static void Triangle(List<int> target, int a, int b, int c) { target.Add(a); target.Add(b); target.Add(c); }
            static Vector3 Direction(float angle) { return new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)); }
            float Range(float min, float max) { return Mathf.Lerp(min, max, (float)random.NextDouble()); }
            static Vector3 Bezier(Vector3 a, Vector3 b, Vector3 c, float t)
            { return (1 - t) * (1 - t) * a + 2 * (1 - t) * t * b + t * t * c; }
            static Vector3[] Curve(Vector3 a, Vector3 b, Vector3 c, int count)
            {
                var points = new Vector3[count];
                for (int i = 0; i < count; i++) points[i] = Bezier(a, b, c, i / (float)(count - 1));
                return points;
            }
        }
    }
}
