using System;
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
        public static readonly Vector3[][] Routes =
        {
            new[] { PlayerStart, At(20, 80, 2), At(20, 71, 2), At(17, 65, 3), B,
                At(18, 54.5f, 6), At(25, 57, 3.5f), C, At(45, 55, 5), At(51, 46, 11),
                At(59, 49, 13), At(61, 40, 17), At(65, 43.5f, 18), At(67, 47, 18),
                At(70, 47, 18), At(72, 44, 18), At(76, 41, 20), At(83, 40, 22),
                At(85, 29, 28), At(77, 22, 33), D },
            new[] { At(20, 71, 2), At(25, 62, 1.3f), C },
            new[] { B, At(25, 37, 13), At(35, 31, 22), At(45, 26, 28), At(57, 23, 33), D },
            new[] { At(35, 31, 22), At(42, 35, 20), At(49, 42, 14.5f), At(51, 46, 11) },
            new[] { B, At(11, 44, 8), At(9, 39, 10) },
            new[] { C, At(42, 61, 1.5f), At(46, 65, 2) },
            new[] { At(72, 44, 18), At(80, 45, 18.8f), At(84, 52, 18.5f) }
        };

        static readonly Vector3[] Platforms = { A, B, C, E, D };
        static readonly float[] PlatformRadii = { 8f, 10f, 6f, 12f, 12f };

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

        static void NearestPath(Vector3 p, out float distance, out float height)
        {
            distance = float.MaxValue;
            height = 0f;
            float sum = 0f, weightedHeight = 0f;
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
                }
            // Blend intersecting corridors so the nearest segment cannot create a step at a fork.
            if (sum > .0001f) height = weightedHeight / sum;
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

        static float Ridge(Vector3 p, Vector3 a, Vector3 b, float width)
        {
            float t;
            float distance = Segment(p, a, b, out t);
            return Mathf.Lerp(a.y, b.y, t) * Mathf.Pow(Mathf.Clamp01(1f - distance / width), 1.6f);
        }

        public static float Height(float x, float z)
        {
            Vector3 p = new Vector3(x, 0, z);
            float designX = (x / Size + .5f) * 100f;
            float designY = (.5f - z / Size) * 100f;
            float uphill = Mathf.Clamp01((85f - designY) / 70f);
            float h = .8f + 27f * uphill * uphill * Mathf.Lerp(.66f, 1f, Ease(12, 74, designX));
            h = Mathf.Max(h, Ridge(p, At(38, 39, 15), At(52, 27, 25), 31f));
            h = Mathf.Max(h, Ridge(p, At(46, 59, 9), At(54, 56, 12), 24f));
            h += 7f * (1f - Ease(5, 47, HorizontalDistance(p, E)));
            h += 9f * (1f - Ease(7, 40, HorizontalDistance(p, D)));
            h += .24f * Mathf.Sin(x * .075f) * Mathf.Cos(z * .061f);

            for (int i = 0; i < Platforms.Length; i++)
            {
                float weight = 1f - Ease(PlatformRadii[i], PlatformRadii[i] + 13f, HorizontalDistance(p, Platforms[i]));
                h = Mathf.Lerp(h, Platforms[i].y, weight);
            }

            foreach (WaterBody water in Waters)
            {
                float shore = water.SignedDistance(x, z);
                float bankWidth = water == Waters[0] ? 8f : 4f;
                if (shore < bankWidth)
                {
                    float bottom = water.SurfaceY - water.Depth * Ease(0f, water == Waters[0] ? 6f : 2.4f, -shore);
                    h = shore < 0 ? bottom : Mathf.Lerp(water.SurfaceY, h, Ease(0, bankWidth, shore));
                }
            }

            float pathDistance, pathHeight;
            NearestPath(p, out pathDistance, out pathHeight);
            // Keep a flat cross-section across the walking corridor, then feather into terrain.
            if (!IsWater(x, z, .3f)) h = Mathf.Lerp(h, pathHeight, 1f - Ease(2f, 7f, pathDistance));
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
                        Vector3 offset = new Vector3(direction.z, 0, -direction.x) * (side * .65f);
                        Vector3 start = route[i - 1] + offset;
                        float previous = sample(start.x, start.z);
                        for (int step = 1; step <= steps; step++)
                        {
                            Vector3 point = Vector3.Lerp(route[i - 1], route[i], step / (float)steps) + offset;
                            float h = sample(point.x, point.z);
                            if (float.IsNaN(h) || float.IsInfinity(h)) throw new InvalidOperationException("Non-finite hillside height.");
                            if (IsWater(point.x, point.z, .32f)) throw new InvalidOperationException("Hillside route " + routeIndex + " enters a water margin at " + point);
                            float slope = Mathf.Atan2(Mathf.Abs(h - previous), distance / steps) * Mathf.Rad2Deg;
                            maxSlope = Mathf.Max(maxSlope, slope);
                            previous = h;
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
                if (sample(water.Center.x, water.Center.y) >= water.SurfaceY - .3f)
                    throw new InvalidOperationException("Water basin is missing: " + water.Name);
            return "PASS: " + Routes.Length + " connected routes; " + samples + " dry samples; " + length.ToString("F1") +
                " m of paths; 1.3 m walking corridor; maximum slope " + maxSlope.ToString("F2") + " degrees; five plateaus and three water basins.";
        }
    }
}
