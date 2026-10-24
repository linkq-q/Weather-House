using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using WeatherHouse.WatersideV2;
using Debug = UnityEngine.Debug;

public static class WatersideWaterBooleanChecks
{
    private static readonly float[] Margins = { -10, -2, -.3f, -.00001f, 0, .00001f, .3f, 2, 10 };
    public static void Run()
    {
        string project = Path.GetDirectoryName(Application.dataPath);
        if (!File.Exists(Path.Combine(project, ".wildlife-journey-validation"))) throw new InvalidOperationException("marker required");
        int exit = 1, comparisons = 0, boundaries = 0; var report = new StringBuilder(); var points = new List<Vector2>();
        try
        {
            for (int x = -100; x <= 100; x += 2)
                for (int z = -100; z <= 100; z += 2)
                {
                    var point = new Vector2(x, z); points.Add(point); Compare(point, ref comparisons);
                    foreach (Vector2 direction in new[] { Vector2.right, Vector2.up })
                    {
                        Vector2 next = point + direction * 2;
                        bool inside = WatersideLayout.WaterSignedDistance(point.x, point.y) < 0;
                        if (inside == (WatersideLayout.WaterSignedDistance(next.x, next.y) < 0)) continue;
                        Vector2 a = point, b = next;
                        for (int i = 0; i < 22; i++)
                        {
                            Vector2 mid = (a + b) * .5f;
                            if ((WatersideLayout.WaterSignedDistance(mid.x, mid.y) < 0) == inside) a = mid; else b = mid;
                        }
                        Vector2 boundary = (a + b) * .5f; boundaries++;
                        foreach (float epsilon in new[] { -.001f, -.00001f, 0, .00001f, .001f })
                            Compare(boundary + direction * epsilon, ref comparisons);
                    }
                }
            var channels = (WatersideLayout.WaterChannel[])typeof(WatersideLayout).GetField("SampledWaterChannels", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic).GetValue(null);
            foreach (var channel in channels)
                for (int i = 1; i < channel.Nodes.Length; i++)
                {
                    var a = channel.Nodes[i - 1]; var b = channel.Nodes[i]; Vector3 delta = b.Position - a.Position; delta.y = 0;
                    Vector3 normal = Vector3.Cross(Vector3.up, delta.normalized);
                    foreach (float t in new[] { 0, .25f, .5f, .75f, 1 })
                        foreach (float side in new[] { -1, 1 })
                        {
                            Vector3 point = Vector3.Lerp(a.Position, b.Position, t) + normal * (Mathf.Lerp(a.HalfWidth, b.HalfWidth, t) * side);
                            foreach (float epsilon in new[] { -.001f, -.00001f, 0, .00001f, .001f })
                                Compare(new Vector2(point.x + normal.x * epsilon, point.z + normal.z * epsilon), ref comparisons);
                        }
                }
            float[] unusual = { float.NaN, float.NegativeInfinity, float.PositiveInfinity, float.MinValue, float.MaxValue, -1e20f, 1e20f, -float.Epsilon, 0, float.Epsilon };
            foreach (float x in unusual) foreach (float z in unusual) foreach (float margin in unusual) Check(x, z, margin, ref comparisons);
            report.AppendLine("PASS exact IsWater == WaterSignedDistance < margin comparisons=" + comparisons + " bisectionBoundaries=" + boundaries);
            report.AppendLine("Grid includes sea, pools, confluences; original channel sides and true union boundaries tested at +/-1e-5 and +/-1e-3; negative/zero/positive margins and non-finite/extreme inputs.");
            double before = 0, after = 0; int oldWet = 0, newWet = 0;
            for (int round = 0; round < 4; round++)
            {
                var clock = Stopwatch.StartNew(); foreach (Vector2 point in points) if (WatersideLayout.WaterSignedDistance(point.x, point.y) < 0) oldWet++; clock.Stop();
                if (round > 0) before += clock.Elapsed.TotalMilliseconds;
                clock.Restart(); foreach (Vector2 point in points) if (WatersideLayout.IsWater(point.x, point.y)) newWet++; clock.Stop();
                if (round > 0) after += clock.Elapsed.TotalMilliseconds;
            }
            if (oldWet != newWet) throw new InvalidOperationException("benchmark results differ");
            report.AppendLine("BENCH same " + points.Count * 3 + " queries baseline_ms=" + before.ToString("F3") + " optimized_ms=" + after.ToString("F3") + " ratio=" + (after / before).ToString("F4"));
            exit = 0;
        }
        catch (Exception e) { report.AppendLine("FAIL " + e); }
        finally
        {
            string path = Path.Combine(project, "JourneyReview", "water-boolean-results.txt"); Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, report.ToString()); Debug.Log(report.ToString()); EditorApplication.Exit(exit);
        }
    }
    private static void Compare(Vector2 p, ref int count) { foreach (float margin in Margins) Check(p.x, p.y, margin, ref count); }
    private static void Check(float x, float z, float margin, ref int count)
    {
        bool expected = WatersideLayout.WaterSignedDistance(x, z) < margin, actual = WatersideLayout.IsWater(x, z, margin); count++;
        if (expected != actual) throw new InvalidOperationException("mismatch at (" + x.ToString("R") + "," + z.ToString("R") + ") margin=" + margin.ToString("R") + " old=" + expected + " new=" + actual);
    }
}
