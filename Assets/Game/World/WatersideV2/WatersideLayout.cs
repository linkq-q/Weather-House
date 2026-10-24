using System;
using System.Collections.Generic;
using UnityEngine;

namespace WeatherHouse.WatersideV2
{
    /// <summary>Authored 180 m coastal valley. Design coordinates run east / south.</summary>
    public static class WatersideLayout
    {
        public const float Size = 180f;
        public const float TerrainBottom = -8f;
        public const float TerrainHeight = 52f;
        public const float SeaLevel = 0f;

        public sealed class WaterNode
        {
            public readonly Vector3 Position;
            public readonly float HalfWidth, Depth;
            internal readonly int SourceSection = -1;
            public WaterNode(float x, float y, float surface, float halfWidth, float depth)
            { Position = At(x, y, surface); HalfWidth = halfWidth; Depth = depth; }
            public WaterNode(Vector3 position, float halfWidth, float depth, int sourceSection)
            { Position = position; HalfWidth = halfWidth; Depth = depth; SourceSection = sourceSection; }
        }

        public sealed class WaterChannel
        {
            public readonly string Name;
            public readonly WaterNode[] Nodes;
            public WaterChannel(string name, params WaterNode[] nodes) { Name = name; Nodes = nodes; }
        }

        public sealed class Crossing
        {
            public readonly string Name, Kind;
            public readonly Vector3 Start, End;
            public readonly float Width;
            public Crossing(string name, string kind, Vector3 start, Vector3 end, float width)
            { Name = name; Kind = kind; Start = start; End = end; Width = width; }
        }

        public sealed class Terrace
        {
            public readonly string Name;
            public readonly Vector3 Center;
            public readonly Vector2 Radii;
            public readonly float Feather;
            public Terrace(string name, Vector3 center, float rx, float rz, float feather)
            { Name = name; Center = center; Radii = new Vector2(rx, rz); Feather = feather; }
        }

        public sealed class TidePool
        {
            public readonly Vector3 Center;
            public readonly Vector2 Radii;
            public readonly float Depth;
            public TidePool(float x, float z, float rx, float rz, float depth)
            { Center = new Vector3(x, SeaLevel, z); Radii = new Vector2(rx, rz); Depth = depth; }
            public float SignedDistance(float x, float z)
            {
                float dx = (x - Center.x) / Radii.x, dz = (z - Center.z) / Radii.y;
                return (Mathf.Sqrt(dx * dx + dz * dz) - 1) * Mathf.Min(Radii.x, Radii.y);
            }
        }

        public static readonly TidePool[] TidePools =
        {
            new TidePool(55.2f, -27.4f, 1.6f, 1.4f, .45f),
            new TidePool(61, -33, 2, 1.75f, .55f),
            new TidePool(54.6f, -36, 1.9f, 1.6f, .4f)
        };

        public static readonly Vector3 Entry = At(24, 75, 3);
        public static readonly Vector3 OldTree = At(48, 56, 4.5f);
        public static readonly Vector3 LakeTerrace = At(63, 36, 10);
        public static readonly Vector3 RoundTerrace = At(65, 23, 17);
        public static readonly Vector3 Cape = At(81, 54, 4);
        public static readonly Vector3 Harbor = At(77, 73, 1.5f);
        public static readonly Vector3 MoonBay = At(45, 84, 1.2f);
        public static readonly Vector3 WesternGrove = At(29, 44, 8);
        public static readonly Vector3 PlayerStart = At(24, 78, 3);
        public static readonly Vector3[] CourtyardSoil =
        {
            new Vector3(-34.5f,0,-30), new Vector3(1.8f,0,-7.2f), new Vector3(-23,0,32.4f),
            new Vector3(0,0,32.4f), new Vector3(57.3f,0,-3.5f), new Vector3(-9,0,-61.2f)
        };

        public static float CourtyardWear(float x, float z)
        {
            float wear = 0;
            foreach (Vector3 center in CourtyardSoil)
            {
                float distance = Mathf.Sqrt((x-center.x)*(x-center.x)+(z-center.z)*(z-center.z));
                wear = Mathf.Max(wear, 1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(1.5f,3.4f,distance)));
            }
            return wear;
        }

        public static readonly IReadOnlyDictionary<string, Vector3> Points = new Dictionary<string, Vector3>
        {
            { "Entry", Entry }, { "OldTree", OldTree }, { "LakeTerrace", LakeTerrace },
            { "RoundTerrace", RoundTerrace }, { "Cape", Cape }, { "Harbor", Harbor },
            { "MoonBay", MoonBay }, { "WesternGrove", WesternGrove }
        };

        public static readonly Terrace[] Terraces =
        {
            new Terrace("入口草台", Entry, 10, 9, 8),
            new Terrace("老树分汊岛", OldTree, 8, 8, 7),
            new Terrace("西坡林间台", WesternGrove, 10, 8, 9),
            new Terrace("湖阶", LakeTerrace, 10, 7, 9),
            new Terrace("湖畔二级台", At(62, 30, 13), 8, 4.5f, 4),
            new Terrace("望海圆台", RoundTerrace, 9, 7, 7),
            new Terrace("海岬草帽", Cape, 6, 8, 6)
        };

        static readonly WaterNode Fork = new WaterNode(46, 41, 3, 3.3f, .75f);
        static readonly WaterNode Join = new WaterNode(48, 72, .3f, 4.2f, 1.1f);
        // Authoring/reflection sections stay coarse; curve samples must not create extra reflection cameras.
        public static readonly WaterChannel[] WaterChannels =
        {
            new WaterChannel("源潭与上游湖", new WaterNode(40, 24, 9, 3, .8f),
                new WaterNode(42, 28, 7.5f, 4.5f, 1), new WaterNode(44, 32, 6, 8, 1.6f),
                new WaterNode(45, 36, 4.5f, 3.2f, .8f), Fork),
            new WaterChannel("西汊", Fork, new WaterNode(38, 48, 2, 3.3f, .7f),
                new WaterNode(36, 58, 1.3f, 3, .65f), new WaterNode(40, 66, .7f, 3.2f, .7f), Join),
            new WaterChannel("东汊", Fork, new WaterNode(55, 46, 2.5f, 3, .7f),
                new WaterNode(59, 55, 1.7f, 2.8f, .55f), new WaterNode(58, 65, .9f, 3.3f, .8f), Join),
            new WaterChannel("入海溪", Join, new WaterNode(53, 80, .1f, 4.5f, 1),
                new WaterNode(59, 87, 0, 5.5f, 1.5f), new WaterNode(63, 94, 0, 6, 2))
        };
        static readonly WaterChannel[] SampledWaterChannels = SmoothWaterChannels();
        // These private authoring samples are immutable for the lifetime of the static layout.
        // Cache only conservative bounds; exact distance and variable width still use the source nodes.
        struct WaterQuerySegment
        {
            public WaterNode a, b;
            public float minX, maxX, minZ, maxZ;
        }
        static readonly WaterQuerySegment[] WaterQuerySegments = MakeWaterQuerySegments();
        static WaterQuerySegment[] MakeWaterQuerySegments()
        {
            var result = new List<WaterQuerySegment>();
            foreach (WaterChannel channel in SampledWaterChannels)
                for (int i = 1; i < channel.Nodes.Length; i++)
                {
                    WaterNode a = channel.Nodes[i - 1], b = channel.Nodes[i];
                    float reach = Mathf.Max(a.HalfWidth, b.HalfWidth);
                    float guard = (1 + Mathf.Abs(a.Position.x) + Mathf.Abs(a.Position.z) + Mathf.Abs(b.Position.x) + Mathf.Abs(b.Position.z) + reach) * .000001f;
                    result.Add(new WaterQuerySegment { a = a, b = b,
                        minX = Mathf.Min(a.Position.x, b.Position.x) - reach - guard, maxX = Mathf.Max(a.Position.x, b.Position.x) + reach + guard,
                        minZ = Mathf.Min(a.Position.z, b.Position.z) - reach - guard, maxZ = Mathf.Max(a.Position.z, b.Position.z) + reach + guard });
                }
            return result.ToArray();
        }

        // A single shoreline, open toward the east and south; the remaining edges are mountains.
        public static readonly Vector3[] Coastline =
        {
            Design(85, 0), Design(88, 18), Design(84, 31), Design(88, 43), Design(86, 53),
            Design(82, 65), Design(83, 71), Design(80, 79), Design(70, 84), Design(61, 91),
            Design(47, 87), Design(34, 90), Design(22, 85), Design(0, 90)
        };

        public static readonly Crossing[] Crossings =
        {
            new Crossing("西汊拱桥", "ArchBridge", At(35, 65, 2), At(44, 65, 2), 2.8f),
            new Crossing("上游木桥", "UpperBridge", At(39, 37, 6.2f), At(49, 37, 6.2f), 2.5f),
            new Crossing("河口栈桥", "MouthBridge", At(48, 81, 1), At(61, 81, 1), 2.8f),
            new Crossing("东汊踏石", "SteppingStones", At(53, 57, 2.2f), At(65, 57, 2.2f), 2.4f)
        };

        public static readonly Crossing[] Steps =
        {
            new Crossing("湖阶上圆台", "StoneSteps", At(62, 31, 12.5f), At(64, 26, 16), 2.2f)
        };

        static readonly Vector3[][] RouteAnchors =
        {
            new[] { Entry, At(27,70,3), At(31,67,2.4f), Crossings[0].Start, Crossings[0].End,
                At(47,61,3.5f), OldTree, At(51,57,3.5f), Crossings[3].Start, Crossings[3].End,
                At(67,51,4), At(64,44,7), LakeTerrace, At(69,39,9), At(73,46,6), Cape,
                At(79,62,2.5f), Harbor, At(68,79,1.4f), Crossings[2].End, Crossings[2].Start,
                MoonBay, At(35,82,2), Entry },
            new[] { LakeTerrace, At(56,38,8), Crossings[1].End, Crossings[1].Start,
                WesternGrove, At(25,54,6), At(22,64,4), Entry },
            new[] { LakeTerrace, Steps[0].Start, Steps[0].End, RoundTerrace,
                At(72,25,16), At(75,31,13), At(71,35,11), LakeTerrace }
        };
        public static readonly Vector3[][] Routes = SmoothRoutes();
        public static readonly Vector3[] ShoreSamples = MakeShoreSamples();

        static readonly Vector3[] LandformAnchors =
        {
            Entry, OldTree, WesternGrove, LakeTerrace, RoundTerrace, Cape, Harbor, MoonBay,
            At(20,84,2), At(35,78,2), At(25,54,6), At(22,64,4), At(31,67,2.4f),
            At(47,61,3.5f), At(51,57,3.5f), At(67,51,4), At(64,44,7), At(69,39,9),
            At(73,46,6), At(79,62,2.5f), At(68,79,1.4f), At(49,49,4.3f), At(51,43,5), At(56,38,8),
            At(37,39,7), At(50,36,6.2f), At(62,31,12.5f), At(64,26,16), At(72,25,16),
            At(75,31,13), At(71,35,11), At(28,25,14), At(48,20,13), At(77,15,17),
            At(15,73,5), At(70,63,3), At(65,73,2), At(35,65,2), At(44,65,2),
            At(48,81,1), At(61,81,1), At(53,57,2.2f), At(65,57,2.2f)
        };

        public static Vector3 Design(float x, float y)
        { return new Vector3((x / 100f - .5f) * Size, 0, (.5f - y / 100f) * Size); }

        static Vector3 At(float x, float y, float height)
        { Vector3 p = Design(x, y); p.y = height; return p; }

        static float Ease(float from, float to, float value)
        { float t = Mathf.Clamp01((value - from) / (to - from)); return t * t * (3 - 2 * t); }

        static float Distance(Vector3 a, Vector3 b)
        { float dx = a.x - b.x, dz = a.z - b.z; return Mathf.Sqrt(dx * dx + dz * dz); }

        static float Segment(float x, float z, Vector3 a, Vector3 b, out float t)
        {
            float dx = b.x - a.x, dz = b.z - a.z;
            t = Mathf.Clamp01(((x - a.x) * dx + (z - a.z) * dz) / Mathf.Max(.0001f, dx * dx + dz * dz));
            float px = x - Mathf.Lerp(a.x, b.x, t), pz = z - Mathf.Lerp(a.z, b.z, t);
            return Mathf.Sqrt(px * px + pz * pz);
        }

        static bool LandContains(float x, float z)
        {
            // Extend the two mountain sides beyond the heightmap; exact border samples stay dry.
            x = Mathf.Max(x, -Size * .5f + .001f);
            z = Mathf.Min(z, Size * .5f - .001f);
            bool inside = false;
            Vector3 previous = Design(0, 0);
            for (int i = 0; i <= Coastline.Length; i++)
            {
                Vector3 current = i == Coastline.Length ? Design(0, 0) : Coastline[i];
                if ((current.z > z) != (previous.z > z) &&
                    x < (previous.x - current.x) * (z - current.z) / (previous.z - current.z) + current.x)
                    inside = !inside;
                previous = current;
            }
            return inside;
        }

        public static float SeaSignedDistance(float x, float z)
        {
            float distance = float.MaxValue;
            for (int i = 1; i < Coastline.Length; i++)
            { float t; distance = Mathf.Min(distance, Segment(x, z, Coastline[i - 1], Coastline[i], out t)); }
            return LandContains(x, z) ? distance : -distance;
        }

        static void WaterSample(float x, float z, out float distance, out float surface, out float depth)
        { int section; WaterSample(x, z, out distance, out surface, out depth, out section); }

        static void WaterSample(float x, float z, out float distance, out float surface, out float depth, out int section, bool includeTidePools = true)
        {
            distance = SeaSignedDistance(x, z); surface = SeaLevel; depth = 2.5f; section = -1;
            foreach (WaterChannel channel in SampledWaterChannels)
                for (int i = 1; i < channel.Nodes.Length; i++)
                {
                    WaterNode a = channel.Nodes[i - 1], b = channel.Nodes[i];
                    float t;
                    float d = Segment(x, z, a.Position, b.Position, out t) - Mathf.Lerp(a.HalfWidth, b.HalfWidth, t);
                    if (d < distance)
                    {
                        distance = d; surface = Mathf.Lerp(a.Position.y, b.Position.y, t);
                        depth = Mathf.Lerp(a.Depth, b.Depth, t);
                        section = b.SourceSection;
                    }
                }
            if (includeTidePools)
                foreach (TidePool pool in TidePools)
                {
                    float d = pool.SignedDistance(x, z);
                    if (d < distance)
                    { distance = d; surface = SeaLevel; depth = pool.Depth; section = -1; }
                }
        }

        /// <summary>Negative inside the union of connected streams, lake widening and sea.</summary>
        public static float WaterSignedDistance(float x, float z)
        { float distance, surface, depth; WaterSample(x, z, out distance, out surface, out depth); return distance; }

        /// <summary>Original authoring segment index for the nearest smooth sample; -1 denotes sea.</summary>
        public static int WaterSection(float x, float z)
        { float distance, surface, depth; int section; WaterSample(x, z, out distance, out surface, out depth, out section); return section; }

        public static bool IsWater(float x, float z, float margin = 0)
        {
            if (float.IsNaN(x) || float.IsInfinity(x) || float.IsNaN(z) || float.IsInfinity(z) ||
                float.IsNaN(margin) || float.IsInfinity(margin)) return WaterSignedDistance(x, z) < margin;
            float sea = SeaSignedDistance(x, z);
            if (float.IsNaN(sea)) return WaterSignedDistance(x, z) < margin;
            if (sea < margin) return true;
            float expansion = margin + (1 + Mathf.Abs(x) + Mathf.Abs(z) + Mathf.Abs(margin)) * .000001f;
            foreach (WaterQuerySegment segment in WaterQuerySegments)
            {
                if (x < segment.minX - expansion || x > segment.maxX + expansion ||
                    z < segment.minZ - expansion || z > segment.maxZ + expansion) continue;
                float t;
                if (Segment(x, z, segment.a.Position, segment.b.Position, out t) -
                    Mathf.Lerp(segment.a.HalfWidth, segment.b.HalfWidth, t) < margin) return true;
            }
            foreach (TidePool pool in TidePools) if (pool.SignedDistance(x, z) < margin) return true;
            return false;
        }

        /// <summary>Nearest water surface, including outside its footprint. Test IsWater before use.</summary>
        public static float WaterHeight(float x, float z)
        { float distance, surface, depth; WaterSample(x, z, out distance, out surface, out depth); return surface; }

        public static float WaterDepth(float x, float z)
        { return IsWater(x, z) ? Mathf.Max(0, WaterHeight(x, z) - Height(x, z)) : 0; }

        public static float GroundHeight(Terrain terrain, float x, float z)
        { return terrain ? terrain.SampleHeight(new Vector3(x, 0, z)) + terrain.transform.position.y : Height(x, z); }

        public static Crossing CrossingAt(float x, float z, float margin = 0)
        {
            foreach (Crossing crossing in Crossings)
            {
                float t;
                if (Segment(x, z, crossing.Start, crossing.End, out t) < crossing.Width * .5f + margin)
                    return crossing;
            }
            return null;
        }

        public static float PathDistance(float x, float z)
        {
            float distance = float.MaxValue;
            foreach (Vector3[] route in Routes)
                for (int i = 1; i < route.Length; i++)
                { float t; distance = Mathf.Min(distance, Segment(x, z, route[i - 1], route[i], out t)); }
            return distance;
        }

        /// <summary>Visible worn trail only; the walkable, graded corridor stays wider.</summary>
        public static float PathHalfWidth(float x, float z)
        {
            float broad = .63f + .18f * Mathf.Sin(x * .11f + z * .047f) + .12f * Mathf.Cos(z * .077f - x * .025f);
            float scallop = .065f * Mathf.Sin(x * .43f - z * .31f) * Mathf.Sin(x * .21f + z * .27f);
            return Mathf.Min(Mathf.Clamp(broad + scallop, .4f, .95f), WalkableHalfWidth(x, z) - .18f);
        }

        public static float WalkableHalfWidth(float x, float z)
        { return Mathf.Clamp(1.05f + .19f * Mathf.Sin(x * .075f + z * .033f) + .12f * Mathf.Cos(z * .09f), .85f, 1.35f); }

        public static float PathEdgeDistance(float x, float z)
        { return PathDistance(x, z) - PathHalfWidth(x, z); }

        public static float WalkablePathEdgeDistance(float x, float z)
        { return PathDistance(x, z) - WalkableHalfWidth(x, z); }

        static void NearestPath(float x, float z, out float distance, out float height)
        {
            distance = float.MaxValue; height = 0;
            float sum = 0, weighted = 0;
            foreach (Vector3[] route in Routes)
                for (int i = 1; i < route.Length; i++)
                {
                    float t, d = Segment(x, z, route[i - 1], route[i], out t);
                    float target = Mathf.Lerp(route[i - 1].y, route[i].y, t);
                    if (d < distance) { distance = d; height = target; }
                    float w = Mathf.Clamp01(1 - d / 3.5f); w = w * w * w;
                    sum += w; weighted += w * target;
                }
            if (sum > .00001f) height = weighted / sum;
        }

        static float NaturalHeight(float x, float z)
        {
            double sum = 0, weighted = 0;
            foreach (Vector3 anchor in LandformAnchors)
            {
                double dx = x - anchor.x, dz = z - anchor.z, d = dx * dx + dz * dz + 36;
                double w = 1 / (d * d); sum += w; weighted += anchor.y * w;
            }
            float h = (float)(weighted / sum) + .25f * Mathf.Sin(x * .08f) * Mathf.Cos(z * .06f);
            foreach (Terrace terrace in Terraces)
            {
                float dx = (x - terrace.Center.x) / terrace.Radii.x;
                float dz = (z - terrace.Center.z) / terrace.Radii.y;
                float angle = Mathf.Atan2(dz, dx);
                float contour = 1 + .09f * Mathf.Sin(angle * 3 + terrace.Center.x);
                float edge = (Mathf.Sqrt(dx * dx + dz * dz) - contour) * Mathf.Min(terrace.Radii.x, terrace.Radii.y);
                h = Mathf.Lerp(h, terrace.Center.y, 1 - Ease(0, terrace.Feather, edge));
            }
            // Curving crests, separate foothills and valleys replace a uniform raised border shelf.
            float westSpine = -79 + 3.5f * Mathf.Sin(z * .047f) + 1.8f * Mathf.Cos(z * .103f);
            float northSpine = 79 + 3 * Mathf.Sin(x * .052f + 1) + 1.5f * Mathf.Cos(x * .094f);
            float westPeak = 29 + 5 * Mathf.Sin(z * .075f + .8f) + 2 * Mathf.Cos(z * .16f);
            float northPeak = 30 + 5 * Mathf.Sin(x * .068f - .6f) + 2 * Mathf.Cos(x * .14f);
            float west = 1 - Ease(0, 21, Mathf.Abs(x - westSpine));
            float north = 1 - Ease(0, 21, Mathf.Abs(z - northSpine));
            float westFoot = 1 - Ease(0, 14, Mathf.Abs(x - (westSpine + 13)));
            float northFoot = 1 - Ease(0, 13, Mathf.Abs(z - (northSpine - 13)));
            h = Mathf.Max(h, Mathf.Max(westPeak * west, northPeak * north));
            h = Mathf.Max(h, Mathf.Max((9 + 2 * Mathf.Sin(z * .11f + 2)) * westFoot,
                (10 + 2 * Mathf.Sin(x * .10f)) * northFoot));
            return h;
        }

        public static float Height(float x, float z)
        {
            float h = NaturalHeight(x, z), pathDistance, pathHeight;
            NearestPath(x, z, out pathDistance, out pathHeight);
            float shore, water, depth; int section;
            // Small tide pools have their own narrow bank below; they must not flatten the main road's 7 m river bank.
            WaterSample(x, z, out shore, out water, out depth, out section, false);
            if (shore > 0)
            {
                float lowBank = water + (.3f + .05f * Mathf.Sin(x * .3f + z * .2f)) * Ease(0, 1.2f, shore);
                h = Mathf.Lerp(lowBank, h, Ease(1.2f, 7, shore));
                float correction = Mathf.Clamp(pathHeight - h, -1.25f, 1.25f);
                float walkableWidth = WalkableHalfWidth(x, z);
                h += correction * (1 - Ease(walkableWidth + .25f, walkableWidth + 2.2f, pathDistance));
                foreach (Crossing crossing in Crossings)
                {
                    Vector3 p = new Vector3(x, 0, z);
                    // Spread the old-tree bank's descent before the east-fork stepping stones.
                    // A 6 m shoulder compounded the path grade into a 33-degree collider slope.
                    float startShoulder = crossing == Crossings[3] ? 10.5f : 6;
                    h = Mathf.Lerp(h, crossing.Start.y, 1 - Ease(1.8f, startShoulder, Distance(p, crossing.Start)));
                    h = Mathf.Lerp(h, crossing.End.y, 1 - Ease(1.8f, 6, Distance(p, crossing.End)));
                }
                foreach (Crossing steps in Steps)
                {
                    float t, d = Segment(x, z, steps.Start, steps.End, out t);
                    h = Mathf.Lerp(h, Mathf.Lerp(steps.Start.y, steps.End.y, t),
                        1 - Ease(steps.Width * .5f + .3f, steps.Width * .5f + 3, d));
                }
            }
            else
            {
                h = water - depth * Ease(0, 2.4f, -shore);
                if (SeaSignedDistance(x, z) < -3) h -= Mathf.Min(3, (-SeaSignedDistance(x, z) - 3) * .1f);
            }
            foreach (TidePool pool in TidePools)
            {
                float edge = pool.SignedDistance(x, z);
                if (edge < 0) h = SeaLevel - pool.Depth * Ease(0, .75f, -edge);
                else if (edge < .85f)
                {
                    float rim = shore < 0 ? SeaLevel - .06f : SeaLevel + .12f;
                    h = Mathf.Lerp(rim, h, Ease(0, .85f, edge));
                }
            }
            return Mathf.Max(TerrainBottom + .4f, h);
        }

        static bool IsStraightConnection(Vector3 a, Vector3 b)
        {
            foreach (Crossing crossing in Crossings)
                if ((a == crossing.Start && b == crossing.End) || (b == crossing.Start && a == crossing.End)) return true;
            foreach (Crossing steps in Steps)
                if ((a == steps.Start && b == steps.End) || (b == steps.Start && a == steps.End)) return true;
            return false;
        }

        static WaterChannel[] SmoothWaterChannels()
        {
            var channels = new WaterChannel[WaterChannels.Length];
            int section = 0;
            for (int c = 0; c < channels.Length; c++)
            {
                WaterNode[] nodes = WaterChannels[c].Nodes;
                var samples = new List<WaterNode> { nodes[0] };
                for (int i = 1; i < nodes.Length; i++, section++)
                {
                    WaterNode a = nodes[i - 1], b = nodes[i];
                    float length = Distance(a.Position, b.Position);
                    Vector3 ta = (b.Position - nodes[Mathf.Max(0, i - 2)].Position) * .38f;
                    Vector3 tb = (nodes[Mathf.Min(nodes.Length - 1, i + 1)].Position - a.Position) * .38f;
                    ta = Vector3.ClampMagnitude(ta, length * .85f);
                    tb = Vector3.ClampMagnitude(tb, length * .85f);
                    int count = Mathf.Max(3, Mathf.CeilToInt(length / 1.5f));
                    for (int j = 1; j <= count; j++)
                    {
                        float t = j / (float)count, t2 = t * t, t3 = t2 * t;
                        Vector3 p = (2*t3-3*t2+1)*a.Position + (t3-2*t2+t)*ta +
                            (-2*t3+3*t2)*b.Position + (t3-t2)*tb;
                        // The shared water query and bed use these same samples; water level stays monotonic.
                        p.y = Mathf.Lerp(a.Position.y, b.Position.y, t);
                        samples.Add(new WaterNode(p, Mathf.Lerp(a.HalfWidth, b.HalfWidth, t), Mathf.Lerp(a.Depth, b.Depth, t), section));
                    }
                }
                channels[c] = new WaterChannel(WaterChannels[c].Name, samples.ToArray());
            }
            return channels;
        }

        static Vector3[][] SmoothRoutes()
        {
            var routes = new Vector3[RouteAnchors.Length][];
            for (int r = 0; r < RouteAnchors.Length; r++)
            {
                Vector3[] anchors = RouteAnchors[r];
                var output = new List<Vector3> { anchors[0] };
                for (int i = 1; i < anchors.Length; i++)
                {
                    Vector3 a = anchors[i - 1], b = anchors[i];
                    float distance = Distance(a, b);
                    bool straight = IsStraightConnection(a, b);
                    Vector3 ta = (b - anchors[Mathf.Max(0, i - 2)]) * .3f;
                    Vector3 tb = (anchors[Mathf.Min(anchors.Length - 1, i + 1)] - a) * .3f;
                    ta = Vector3.ClampMagnitude(ta, distance * .7f); tb = Vector3.ClampMagnitude(tb, distance * .7f);
                    int count = Mathf.Max(3, Mathf.CeilToInt(distance / 2));
                    for (int j = 1; j <= count; j++)
                    {
                        float t = j / (float)count, t2 = t * t, t3 = t2 * t;
                        output.Add(straight ? Vector3.Lerp(a, b, t) :
                            (2*t3-3*t2+1)*a + (t3-2*t2+t)*ta + (-2*t3+3*t2)*b + (t3-t2)*tb);
                    }
                }
                routes[r] = output.ToArray();
            }
            return routes;
        }

        static Vector3[] MakeShoreSamples()
        {
            var samples = new List<Vector3>();
            for (int i = 1; i < Coastline.Length; i++)
            {
                int steps = Mathf.CeilToInt(Distance(Coastline[i - 1], Coastline[i]) / 3);
                for (int j = 0; j < steps; j++) samples.Add(Vector3.Lerp(Coastline[i - 1], Coastline[i], j / (float)steps));
            }
            foreach (WaterChannel channel in SampledWaterChannels)
                for (int i = 1; i < channel.Nodes.Length; i++)
                {
                    WaterNode a = channel.Nodes[i - 1], b = channel.Nodes[i];
                    Vector3 tangent = (b.Position - a.Position); tangent.y = 0; tangent.Normalize();
                    Vector3 side = new Vector3(tangent.z, 0, -tangent.x);
                    int steps = Mathf.CeilToInt(Distance(a.Position, b.Position) / 2);
                    for (int j = 0; j <= steps; j++)
                    {
                        float t = j / (float)steps;
                        Vector3 center = Vector3.Lerp(a.Position, b.Position, t);
                        float width = Mathf.Lerp(a.HalfWidth, b.HalfWidth, t);
                        foreach (float sign in new[] { -1f, 1f })
                        {
                            Vector3 p = center + side * (sign * width);
                            if (WaterSignedDistance(p.x, p.z) > -.25f) samples.Add(p);
                        }
                    }
                }
            foreach (TidePool pool in TidePools)
                for (int i = 0; i < 20; i++)
                {
                    float angle = i * Mathf.PI * 2 / 20;
                    samples.Add(pool.Center + new Vector3(Mathf.Cos(angle) * pool.Radii.x, 0, Mathf.Sin(angle) * pool.Radii.y));
                }
            return samples.ToArray();
        }

        public static string Validate(Func<float, float, float> sample = null)
        {
            if (sample == null) sample = Height;
            int cycles = ValidateRouteGraph();
            float maxSlope = 0, routeLength = 0; int samples = 0, wetCrossings = 0;
            for (int r = 0; r < Routes.Length; r++)
            {
                Vector3[] route = Routes[r];
                for (int i = 1; i < route.Length; i++)
                {
                    Vector3 a = route[i - 1], b = route[i];
                    float length = Distance(a, b); routeLength += length;
                    Vector3 direction = (b - a) / length, side = new Vector3(direction.z, 0, -direction.x);
                    int count = Mathf.Max(1, Mathf.CeilToInt(length / .35f));
                    for (int j = 0; j <= count; j++)
                        for (int s = -1; s <= 1; s++)
                        {
                            Vector3 p = Vector3.Lerp(a, b, j / (float)count);
                            p += side * (s * (WalkableHalfWidth(p.x, p.z) - .25f));
                            Crossing crossing = CrossingAt(p.x, p.z, .1f);
                            if (IsWater(p.x, p.z, .1f))
                            {
                                if (crossing == null) throw new InvalidOperationException("Unplanned wet route " + r + " at " + p);
                                wetCrossings++; continue;
                            }
                            if (crossing == null)
                            {
                                float ahead = sample(p.x + direction.x * .3f, p.z + direction.z * .3f);
                                float behind = sample(p.x - direction.x * .3f, p.z - direction.z * .3f);
                                float slope = Mathf.Atan2(Mathf.Abs(ahead - behind), .6f) * Mathf.Rad2Deg;
                                if (slope > maxSlope) maxSlope = slope;
                                if (slope > 34) throw new InvalidOperationException("Path exceeds 34 degrees at " + p + ": " + slope);
                            }
                            samples++;
                        }
                }
            }
            float approachSlope = ValidateEastForkApproach(sample);
            foreach (KeyValuePair<string, Vector3> point in Points)
                if (Mathf.Abs(sample(point.Value.x, point.Value.z) - point.Value.y) > .65f)
                    throw new InvalidOperationException("Landmark height mismatch: " + point.Key);
            foreach (Crossing crossing in Crossings)
            {
                if (!IsWater(Mathf.Lerp(crossing.Start.x, crossing.End.x, .5f), Mathf.Lerp(crossing.Start.z, crossing.End.z, .5f)))
                    throw new InvalidOperationException("Crossing does not cross water: " + crossing.Name);
                if (Mathf.Abs(sample(crossing.Start.x, crossing.Start.z) - crossing.Start.y) > .2f ||
                    Mathf.Abs(sample(crossing.End.x, crossing.End.z) - crossing.End.y) > .2f)
                    throw new InvalidOperationException("Crossing landing is not level with its deck: " + crossing.Name);
            }
            return "PASS: connected coast/island/high-terrace routes; " + cycles + " independent loops; " + samples +
                " dry samples; " + wetCrossings + " planned crossing samples; " + routeLength.ToString("F1") +
                " m routes; maximum walking slope " + maxSlope.ToString("F2") + " degrees; east-fork approach full gradient " +
                approachSlope.ToString("F2") + " degrees.";
        }

        static float ValidateEastForkApproach(Func<float, float, float> sample)
        {
            float maximum = 0; bool approach = false;
            Vector3[] route = Routes[0];
            for (int i = 1; i < route.Length; i++)
            {
                Vector3 a = route[i - 1], b = route[i];
                if (a == OldTree) approach = true;
                if (!approach) continue;
                float length = Distance(a, b);
                Vector3 side = new Vector3(b.z - a.z, 0, a.x - b.x) / length;
                int count = Mathf.CeilToInt(length / .15f);
                for (int j = 0; j <= count; j++)
                    for (int s = -2; s <= 2; s++)
                    {
                        Vector3 p = Vector3.Lerp(a, b, j / (float)count);
                        p += side * (s * .5f * WalkableHalfWidth(p.x, p.z));
                        float dx = (sample(p.x + .18f, p.z) - sample(p.x - .18f, p.z)) / .36f;
                        float dz = (sample(p.x, p.z + .18f) - sample(p.x, p.z - .18f)) / .36f;
                        maximum = Mathf.Max(maximum, Mathf.Atan2(Mathf.Sqrt(dx * dx + dz * dz), 1) * Mathf.Rad2Deg);
                    }
                if (b == Crossings[3].Start) break;
            }
            if (!approach || maximum > 28)
                throw new InvalidOperationException("Old-tree to east-fork approach exceeds the herd's 28-degree terrain target: " + maximum);
            return maximum;
        }

        static int ValidateRouteGraph()
        {
            var nodes = new Dictionary<Vector3, int>();
            var edges = new HashSet<string>();
            var neighbors = new List<List<int>>();
            foreach (Vector3[] route in RouteAnchors)
            {
                int previous = -1;
                foreach (Vector3 p in route)
                {
                    int id;
                    if (!nodes.TryGetValue(p, out id))
                    { id = nodes.Count; nodes.Add(p, id); neighbors.Add(new List<int>()); }
                    if (previous >= 0)
                    {
                        string edge = Mathf.Min(previous, id) + ":" + Mathf.Max(previous, id);
                        if (edges.Add(edge)) { neighbors[previous].Add(id); neighbors[id].Add(previous); }
                    }
                    previous = id;
                }
            }
            var visited = new HashSet<int>(); var queue = new Queue<int>(); queue.Enqueue(0);
            while (queue.Count > 0)
            { int id = queue.Dequeue(); if (visited.Add(id)) foreach (int next in neighbors[id]) queue.Enqueue(next); }
            int cycles = edges.Count - nodes.Count + 1;
            if (visited.Count != nodes.Count || cycles < 2)
                throw new InvalidOperationException("Waterside route graph must be connected with at least two independent loops.");
            return cycles;
        }
    }
}
