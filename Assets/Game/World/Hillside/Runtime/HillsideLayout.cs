using System;
using System.Collections.Generic;
using UnityEngine;

namespace WeatherHouse.Hillside
{
    /// <summary>The October 23 hillside plan, in metres; design coordinates run east / south.</summary>
    public static class HillsideLayout
    {
        public const float Size = 240f;
        public const float TerrainBottom = -6f;
        public const float TerrainHeight = 64f;

        public sealed class WaterBody
        {
            public readonly string Name;
            public readonly Vector2 Center;
            public readonly Vector2 Radii;
            public readonly float SurfaceY;
            public readonly float Depth;

            public WaterBody(string name, float x, float y, Vector2 radii, float surfaceY, float depth)
            {
                Name = name;
                Vector3 p = Design(x, y);
                Center = new Vector2(p.x, p.z);
                Radii = radii;
                SurfaceY = surfaceY;
                Depth = depth;
            }

            public float SignedDistance(float x, float z)
            {
                float dx = (x - Center.x) / Radii.x;
                float dz = (z - Center.y) / Radii.y;
                float angle = Mathf.Atan2(dz, dx);
                float contour = 1f + .035f * Mathf.Sin(angle * 5f) + .02f * Mathf.Cos(angle * 3f);
                return (Mathf.Sqrt(dx * dx + dz * dz) - contour) * Mathf.Min(Radii.x, Radii.y);
            }
        }

        public static readonly WaterBody[] Waters =
        {
            new WaterBody("山脚池塘", 33f, 75.5f, new Vector2(26.4f, 30f), 0f, 2.2f),
            new WaterBody("W1 老树小池", 24f, 53f, new Vector2(5.2f, 4.2f), 6.2f, .8f),
            new WaterBody("W2 白桦台地池", 75f, 47f, new Vector2(6.4f, 4.6f), 17.2f, .9f)
        };

        public static readonly Vector3 A = At(17f, 78f, 2f);
        public static readonly Vector3 B = At(18f, 49f, 7f);
        public static readonly Vector3 C = At(35f, 61f, 1f);
        public static readonly Vector3 E = At(70f, 44f, 18f);
        public static readonly Vector3 D = At(65f, 18f, 36f);
        public static readonly Vector3 PlayerStart = At(17f, 83f, 2f);

        // Each shared junction uses the same elevation. The courtyard exit skirts W1 on dry land.
        static readonly Vector3[][] RouteAnchors =
        {
            new[] { PlayerStart, At(20, 80, 2), At(19.8f, 76, 2), At(20, 71, 2), At(17, 65, 3),
                At(15.5f, 59, 4.4f), B, At(18, 54.5f, 6), At(25, 57, 5.5f), C,
                At(40, 60, 2.5f), At(45, 55, 5), At(46, 50, 8), At(51, 46, 11),
                At(55, 49.5f, 13.5f), At(59, 49, 15.5f), At(61, 40, 17), At(65, 43.5f, 18), At(67, 47, 18),
                At(70, 47, 18), At(72, 44, 18), At(76, 41, 19.5f), At(83, 40, 21.5f),
                At(86, 35, 24), At(85, 29, 28), At(82, 25, 30.7f), At(77, 22, 33), D },
            new[] { At(20, 71, 2), At(21, 66, 1.5f), At(25, 62, 1.3f), C },
            new[] { B, At(20, 42, 10), At(25, 37, 13), At(28, 33, 16.5f), At(35, 31, 22),
                At(39, 28, 25.2f), At(45, 26, 28), At(51, 26.5f, 30.5f), At(57, 23, 33), D },
            new[] { At(35, 31, 22), At(42, 35, 20), At(49, 42, 14.5f), At(51, 46, 11) },
            new[] { B, At(11, 44, 8), At(9, 39, 10) },
            new[] { C, At(42, 61, 1.5f), At(46, 65, 2) },
            new[] { At(72, 44, 18), At(80, 45, 18.8f), At(84, 52, 18.5f) }
        };

        public static readonly Vector3[][] Routes = SmoothRoutes(RouteAnchors);

        static readonly Vector3[] Platforms = { A, B, C, E, D };
        static readonly float[] PlatformRadii = { 8f, 15f, 6f, 18f, 16f };
        static readonly Vector3[] LandformAnchors =
        {
            A, B, C, E, D, At(5, 95, 1), At(45, 95, 2), At(90, 90, 6), At(9, 60, 5),
            At(45, 55, 5), At(51, 46, 11), At(59, 49, 15.5f), At(85, 29, 28),
            At(49, 42, 14.5f), At(42, 35, 20), At(76, 41, 19.5f), At(83, 40, 21.5f), At(25, 57, 5.5f),
            At(25, 37, 13), At(35, 31, 22), At(45, 26, 28), At(57, 23, 33),
            At(77, 22, 33), At(82, 25, 30.7f),
            At(56, 30, 26), At(64, 30, 28.5f), At(73, 31, 26.5f), At(79, 34, 25),
            At(58, 35, 21), At(66, 35, 24), At(74, 37, 22), At(60, 39, 19),
            At(80, 10, 35), At(35, 8, 32), At(8, 15, 21), At(92, 60, 14)
        };

        static Vector3[][] SmoothRoutes(Vector3[][] source)
        {
            var result = new Vector3[source.Length][];
            for (int r = 0; r < source.Length; r++)
            {
                var points = new List<Vector3> { source[r][0] };
                for (int i = 0; i < source[r].Length - 1; i++)
                {
                    Vector3 a = source[r][i], b = source[r][i + 1];
                    Vector3 previous = source[r][Mathf.Max(0, i - 1)];
                    Vector3 next = source[r][Mathf.Min(source[r].Length - 1, i + 2)];
                    float distance = HorizontalDistance(a, b);
                    Vector3 tangentA = (b - previous) * .38f;
                    Vector3 tangentB = (next - a) * .38f;
                    tangentA = Vector3.ClampMagnitude(tangentA, distance * .85f);
                    tangentB = Vector3.ClampMagnitude(tangentB, distance * .85f);
                    int steps = Mathf.Max(3, Mathf.CeilToInt(distance / 2.5f));
                    for (int step = 1; step <= steps; step++)
                    {
                        float t = step / (float)steps, t2 = t * t, t3 = t2 * t;
                        points.Add((2 * t3 - 3 * t2 + 1) * a + (t3 - 2 * t2 + t) * tangentA +
                            (-2 * t3 + 3 * t2) * b + (t3 - t2) * tangentB);
                    }
                }
                result[r] = points.ToArray();
            }
            return result;
        }

        public static Vector3 Design(float x, float y)
        {
            return new Vector3((x / 100f - .5f) * Size, 0f, (.5f - y / 100f) * Size);
        }

        static Vector3 At(float x, float y, float height)
        {
            Vector3 p = Design(x, y);
            p.y = height;
            return p;
        }

        static float Ease(float from, float to, float value)
        {
            float t = Mathf.Clamp01((value - from) / (to - from));
            return t * t * (3f - 2f * t);
        }

        static float HorizontalDistance(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x, dz = a.z - b.z;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        static float Segment(Vector3 p, Vector3 a, Vector3 b, out float t)
        {
            float dx = b.x - a.x, dz = b.z - a.z;
            t = Mathf.Clamp01(((p.x - a.x) * dx + (p.z - a.z) * dz) / Mathf.Max(.0001f, dx * dx + dz * dz));
            float x = p.x - Mathf.Lerp(a.x, b.x, t), z = p.z - Mathf.Lerp(a.z, b.z, t);
            return Mathf.Sqrt(x * x + z * z);
        }

        public static float PathDistance(float x, float z)
        {
            float distance = float.MaxValue;
            Vector3 p = new Vector3(x, 0, z);
            foreach (Vector3[] route in Routes)
                for (int i = 1; i < route.Length; i++)
                {
                    float t;
                    distance = Mathf.Min(distance, Segment(p, route[i - 1], route[i], out t));
                }
            return distance;
        }

        public static float PathHalfWidth(float x, float z)
        {
            float width = 1.18f + .27f * Mathf.Sin(x * .047f + z * .028f) + .17f * Mathf.Cos(z * .065f - x * .023f);
            float clearing = 1f - Ease(5f, 17f, LandmarkDistance(x, z));
            return Mathf.Lerp(Mathf.Clamp(width, .95f, 1.6f), 1.6f, clearing);
        }

        public static float PathEdgeDistance(float x, float z)
        {
            return PathDistance(x, z) - PathHalfWidth(x, z);
        }

        static void NearestPath(Vector3 p, out float distance, out float height, out float shoulderHeight)
        {
            distance = float.MaxValue;
            height = 0f;
            float sum = 0f, weightedHeight = 0f;
            float shoulderSum = 0f, shoulderWeighted = 0f;
            foreach (Vector3[] route in Routes)
                for (int i = 1; i < route.Length; i++)
                {
                    float t;
                    float d = Segment(p, route[i - 1], route[i], out t);
                    float target = Mathf.Lerp(route[i - 1].y, route[i].y, t);
                    if (d < distance) { distance = d; height = target; }
                    float influence = Mathf.Clamp01(1f - d / 5f);
                    float weight = influence * influence * influence;
                    sum += weight;
                    weightedHeight += target * weight;
                    float broad = Mathf.Clamp01(1f - d / 25f);
                    float broadWeight = broad * broad * broad;
                    shoulderSum += broadWeight;
                    shoulderWeighted += target * broadWeight;
                }
            // Blend intersecting corridors so the nearest segment cannot create a step at a fork.
            if (sum > .0001f) height = weightedHeight / sum;
            shoulderHeight = shoulderSum > .0001f ? shoulderWeighted / shoulderSum : height;
        }

        public static bool IsWater(float x, float z, float margin = 0f)
        {
            foreach (WaterBody water in Waters)
                if (water.SignedDistance(x, z) < margin) return true;
            return false;
        }

        public static float LandmarkDistance(float x, float z)
        {
            float nearest = float.MaxValue;
            Vector3 p = new Vector3(x, 0, z);
            foreach (Vector3 landmark in Platforms) nearest = Mathf.Min(nearest, HorizontalDistance(p, landmark));
            return nearest;
        }

        static float NaturalHeight(float x, float z)
        {
            Vector3 p = new Vector3(x, 0, z);
            double sum = 0, weightedHeight = 0;
            foreach (Vector3 anchor in LandformAnchors)
            {
                double dx = x - anchor.x, dz = z - anchor.z;
                double distance = dx * dx + dz * dz + 81;
                double weight = 1 / (distance * distance);
                sum += weight;
                weightedHeight += anchor.y * weight;
            }
            float h = (float)(weightedHeight / sum) + .3f * Mathf.Sin(x * .065f) * Mathf.Cos(z * .051f);

            for (int i = 0; i < Platforms.Length; i++)
            {
                float dx = x - Platforms[i].x, dz = z - Platforms[i].z;
                // The upper shelves are wider east-west, leaving room for separate south-facing spurs.
                if (i >= 3) dx *= .8f;
                float distance = Mathf.Sqrt(dx * dx + dz * dz);
                float angle = Mathf.Atan2(dz, dx);
                float feather = i >= 3 ? 11f + 3f * Mathf.Sin(angle * 3f + i) : 24f;
                float weight = 1f - Ease(PlatformRadii[i], PlatformRadii[i] + feather, distance);
                h = Mathf.Lerp(h, Platforms[i].y, weight);
            }
            return h;
        }

        public static float Height(float x, float z)
        {
            Vector3 p = new Vector3(x, 0, z);
            float h = NaturalHeight(x, z);
            float pathDistance, pathHeight, shoulderHeight;
            NearestPath(p, out pathDistance, out pathHeight, out shoulderHeight);
            // The broad shelves carry the route; only the walking strip is levelled precisely.
            if (!IsWater(x, z, .3f))
            {
                h = Mathf.Lerp(h, shoulderHeight, (1f - Ease(3f, 25f, pathDistance)) * .65f);
                h = Mathf.Lerp(h, pathHeight, 1f - Ease(PathHalfWidth(x, z) + .5f, 8f, pathDistance));
            }
            // Carve last: route shoulders must not lift a steep rim immediately above the water.
            foreach (WaterBody water in Waters)
            {
                float shore = water.SignedDistance(x, z);
                if (shore < 0)
                    h = water.SurfaceY - water.Depth * Ease(0f, water == Waters[0] ? 6f : 2.4f, -shore);
                else if (shore < (water == Waters[0] ? 12f : 9f))
                {
                    float lowBank = water.SurfaceY + .4f * Ease(0, 3f, shore);
                    float outerBank = Ease(3f, water == Waters[0] ? 12f : 9f, shore);
                    float roadClearance = Ease(.5f, 1.8f, pathDistance - PathHalfWidth(x, z));
                    h = Mathf.Lerp(h, Mathf.Lerp(lowBank, h, outerBank), roadClearance);
                }
            }
            return Mathf.Max(TerrainBottom + .5f, h);
        }

        /// <summary>Checks generated height samples, dry routes and shared route junctions.</summary>
        public static string Validate(Func<float, float, float> sample = null)
        {
            if (sample == null) sample = Height;
            float maxSlope = 0, length = 0;
            int samples = 0;
            var reached = new bool[Routes.Length];
            reached[0] = true;
            for (int pass = 0; pass < Routes.Length; pass++)
                for (int i = 0; i < Routes.Length; i++)
                    if (reached[i])
                        for (int j = 0; j < Routes.Length; j++)
                            if (!reached[j])
                                foreach (Vector3 a in Routes[i])
                                    foreach (Vector3 b in Routes[j])
                                        if ((a - b).sqrMagnitude < .01f) reached[j] = true;
            if (Array.Exists(reached, value => !value)) throw new InvalidOperationException("Hillside route graph has a disconnected path.");

            for (int routeIndex = 0; routeIndex < Routes.Length; routeIndex++)
            {
                Vector3[] route = Routes[routeIndex];
                for (int i = 1; i < route.Length; i++)
                {
                    float distance = HorizontalDistance(route[i - 1], route[i]);
                    length += distance;
                    int steps = Mathf.CeilToInt(distance / .4f);
                    Vector3 direction = (route[i] - route[i - 1]) / distance;
                    for (int side = -1; side <= 1; side++)
                    {
                        Vector3 sideways = new Vector3(direction.z, 0, -direction.x);
                        Vector3 offset = sideways * (side * (PathHalfWidth(route[i - 1].x, route[i - 1].z) - .28f));
                        Vector3 start = route[i - 1] + offset;
                        float previous = sample(start.x, start.z);
                        Vector3 previousPoint = start;
                        for (int step = 1; step <= steps; step++)
                        {
                            Vector3 center = Vector3.Lerp(route[i - 1], route[i], step / (float)steps);
                            Vector3 point = center + sideways * (side * (PathHalfWidth(center.x, center.z) - .28f));
                            float h = sample(point.x, point.z);
                            if (float.IsNaN(h) || float.IsInfinity(h)) throw new InvalidOperationException("Non-finite hillside height.");
                            if (IsWater(point.x, point.z, .32f)) throw new InvalidOperationException("Hillside route " + routeIndex + " enters a water margin at " + point);
                            float slope = Mathf.Atan2(Mathf.Abs(h - previous), HorizontalDistance(point, previousPoint)) * Mathf.Rad2Deg;
                            maxSlope = Mathf.Max(maxSlope, slope);
                            previous = h;
                            previousPoint = point;
                            samples++;
                        }
                    }
                }
            }
            if (maxSlope > 32f) throw new InvalidOperationException("Hillside path exceeds 32 degrees: " + maxSlope.ToString("F2"));
            foreach (Vector3 platform in Platforms)
                if (Mathf.Abs(sample(platform.x, platform.z) - platform.y) > .5f)
                    throw new InvalidOperationException("Landmark plateau height differs from the plan at " + platform);
            foreach (WaterBody water in Waters)
            {
                if (sample(water.Center.x, water.Center.y) >= water.SurfaceY - .3f)
                    throw new InvalidOperationException("Water basin is missing: " + water.Name);
                for (int i = 0; i < 32; i++)
                {
                    float angle = i * Mathf.PI / 16f;
                    float contour = 1f + .035f * Mathf.Sin(angle * 5f) + .02f * Mathf.Cos(angle * 3f);
                    float radius = contour + 2f / Mathf.Min(water.Radii.x, water.Radii.y);
                    float x = water.Center.x + Mathf.Cos(angle) * water.Radii.x * radius;
                    float z = water.Center.y + Mathf.Sin(angle) * water.Radii.y * radius;
                    if (PathEdgeDistance(x, z) > 2f && sample(x, z) > water.SurfaceY + .6f)
                        throw new InvalidOperationException("Water bank rises too sharply: " + water.Name);
                }
            }
            return "PASS: " + Routes.Length + " connected routes; " + samples + " dry samples; " + length.ToString("F1") +
                " m of paths; variable 1.9-3.2 m path width; maximum slope " + maxSlope.ToString("F2") + " degrees; five plateaus and three water basins.";
        }
    }
}
