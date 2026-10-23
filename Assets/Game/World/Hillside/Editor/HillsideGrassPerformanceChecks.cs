using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace WeatherHouse.Hillside.Editor
{
    // Keeps the pre-repair placement algorithm as an independent visual equivalence reference.
    public static class HillsideGrassPerformanceChecks
    {
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

        [MenuItem("Weather House/Checks/Grass Streaming Performance")]
        public static void RunMenu() { UnityEngine.Debug.Log(Run()); }

        public static string Run()
        {
            var source = Object.FindObjectOfType<HillsideGrassRenderer>();
            Require(source && source.terrain, "Open the hillside scene with generated grass first.");
            var advance = typeof(HillsideGrassRenderer).GetMethod("AdvanceBuild", Private);
            Require(advance != null, "REGRESSION: grass still builds an entire cell synchronously without a resumable budget.");
            Scene preview = EditorSceneManager.NewPreviewScene();
            GameObject holder = null;
            try
            {
                holder = new GameObject("Grass performance check") { hideFlags = HideFlags.HideAndDontSave };
                SceneManager.MoveGameObjectToScene(holder, preview);
                var grass = holder.AddComponent<HillsideGrassRenderer>();
                grass.manualCaptureOnly = true;
                grass.terrain = source.terrain; grass.variants = source.variants; grass.materials = source.materials;
                grass.speciesWeights = (float[])source.speciesWeights.Clone(); grass.seed = source.seed;
                grass.clumpsPerSquareMeter = source.clumpsPerSquareMeter; grass.cellSize = source.cellSize;
                grass.visibleRadius = source.visibleRadius; grass.maximumSlope = source.maximumSlope;
                var report = new StringBuilder("Grass streaming repair checks\n");
                var keys = new HashSet<Vector2Int>();
                foreach (Vector3 point in new[] { HillsideLayout.A, HillsideLayout.B, HillsideLayout.E, HillsideLayout.PlayerStart })
                    keys.Add(Key(point, grass.cellSize));
                foreach (var water in HillsideLayout.Waters)
                {
                    keys.Add(Key(new Vector3(water.Center.x, 0, water.Center.y), grass.cellSize));
                    keys.Add(Key(new Vector3(water.Center.x + water.Radii.x, 0, water.Center.y), grass.cellSize));
                }
                Vector3 origin = grass.terrain.transform.position, size = grass.terrain.terrainData.size;
                keys.Add(Key(origin, grass.cellSize)); keys.Add(Key(origin + new Vector3(size.x - .01f, 0, size.z - .01f), grass.cellSize));
                var build = typeof(HillsideGrassRenderer).GetMethod("BuildCell", Private);
                var queries = typeof(HillsideGrassRenderer).GetField("staticCandidateQueries", Private);
                Require(queries != null, "Static candidate query counter is unavailable.");
                int comparisons = 0, matrices = 0;
                foreach (Vector2Int key in keys)
                {
                    Vector3 center = new Vector3((key.x + .5f) * grass.cellSize, origin.y + 14, (key.y + .5f) * grass.cellSize);
                    foreach (float altitude in new[] { 0f, 245f })
                    {
                        Vector3 view = center + Vector3.up * altitude;
                        var expected = Legacy(grass, key, grass.cellSize, view);
                        var timer = Stopwatch.StartNew();
                        object actualCell = build.Invoke(grass, new object[] { key, grass.cellSize, view });
                        timer.Stop(); double cold = timer.Elapsed.TotalMilliseconds;
                        matrices += Compare(expected, Snapshot(actualCell), key + " altitude=" + altitude);
                        long before = (long)queries.GetValue(grass);
                        timer.Restart(); object repeated = build.Invoke(grass, new object[] { key, grass.cellSize, view }); timer.Stop();
                        Compare(expected, Snapshot(repeated), "repeat " + key);
                        Require((long)queries.GetValue(grass) == before, "Warm grass cell repeated static terrain/path queries.");
                        report.AppendLine(key + " altitude=" + altitude + " cold=" + cold.ToString("F3") + "ms warm=" + timer.Elapsed.TotalMilliseconds.ToString("F3") + "ms");
                        comparisons++;
                    }
                }
                CheckBudget(grass, advance, report);
                CheckTurnBack(grass, report);
                report.AppendLine("PASS: " + comparisons + " legacy matrix comparisons, " + matrices + " matrices, near/far/water/shore/boundary cells; warm queries unchanged; resumable work; turn-back cache retained.");
                Directory.CreateDirectory("HillsidePerformanceReview");
                File.WriteAllText("HillsidePerformanceReview/grass-repair-checks.txt", report.ToString());
                return report.ToString();
            }
            finally
            {
                if (holder) Object.DestroyImmediate(holder);
                EditorSceneManager.ClosePreviewScene(preview);
            }
        }

        static Vector2Int Key(Vector3 point, float edge) => new Vector2Int(Mathf.FloorToInt(point.x / edge), Mathf.FloorToInt(point.z / edge));

        static List<Matrix4x4>[] Snapshot(object cell)
        {
            var groups = Groups();
            var batches = (System.Collections.IEnumerable)cell.GetType().GetField("batches").GetValue(cell);
            foreach (object batch in batches)
            {
                Type type = batch.GetType(); int variant = (int)type.GetField("variant").GetValue(batch);
                var values = (Matrix4x4[])type.GetField("matrices").GetValue(batch);
                FieldInfo countField = type.GetField("count"); int count = countField == null ? values.Length : (int)countField.GetValue(batch);
                for (int i = 0; i < count; i++) groups[variant].Add(values[i]);
            }
            return groups;
        }

        static int Compare(List<Matrix4x4>[] expected, List<Matrix4x4>[] actual, string context)
        {
            int count = 0;
            for (int variant = 0; variant < expected.Length; variant++)
            {
                Require(expected[variant].Count == actual[variant].Count, context + " changed variant " + variant + " count: " + expected[variant].Count + " vs " + actual[variant].Count);
                for (int i = 0; i < expected[variant].Count; i++)
                    for (int component = 0; component < 16; component++)
                        Require(Mathf.Abs(expected[variant][i][component] - actual[variant][i][component]) <= .00001f,
                            context + " changed grass matrix " + variant + "/" + i + "/" + component);
                count += expected[variant].Count;
            }
            return count;
        }

        static void CheckBudget(HillsideGrassRenderer grass, MethodInfo advance, StringBuilder report)
        {
            var key = new Vector2Int(4, 4);
            var get = typeof(HillsideGrassRenderer).GetMethod("GetOrCreateCell", Private);
            var begin = typeof(HillsideGrassRenderer).GetMethod("BeginBuild", Private);
            object cell = get.Invoke(grass, new object[] { key, grass.cellSize });
            Vector3 view = new Vector3((key.x + .5f) * grass.cellSize, 14, (key.y + .5f) * grass.cellSize);
            begin.Invoke(grass, new[] { cell, (object)view });
            FieldInfo cursor = cell.GetType().GetField("generated");
            advance.Invoke(grass, new[] { cell, (object)0L });
            int generated = (int)cursor.GetValue(cell);
            Require(generated < Mathf.CeilToInt(grass.cellSize * grass.cellSize * grass.clumpsPerSquareMeter), "Expired budget still generated an entire grass cell.");
            int slices = 0; var timer = Stopwatch.StartNew();
            while (!(bool)advance.Invoke(grass, new[] { cell, (object)(Stopwatch.GetTimestamp() + Stopwatch.Frequency / 1000) }))
                Require(++slices < 10000, "Grass build did not resume to completion.");
            timer.Stop();
            Compare(Legacy(grass, key, grass.cellSize, view), Snapshot(cell), "sliced build");
            report.AppendLine("Resumed in " + (slices + 1) + " 1ms slices, total=" + timer.Elapsed.TotalMilliseconds.ToString("F3") + "ms.");
        }

        static void CheckTurnBack(HillsideGrassRenderer grass, StringBuilder report)
        {
            var get = typeof(HillsideGrassRenderer).GetMethod("GetOrCreateCell", Private);
            var retire = typeof(HillsideGrassRenderer).GetMethod("RetireCells", Private);
            var requested = (HashSet<Vector2Int>)typeof(HillsideGrassRenderer).GetField("requestedKeys", Private).GetValue(grass);
            var key = Key(HillsideLayout.PlayerStart, grass.cellSize);
            object before = get.Invoke(grass, new object[] { key, grass.cellSize });
            requested.Clear(); retire.Invoke(grass, null);
            object after = get.Invoke(grass, new object[] { key, grass.cellSize });
            Require(ReferenceEquals(before, after), "Looking away discarded the grass cell cache.");
            report.AppendLine("Turn-away/return retained the same cell and candidate cache.");
        }

        static List<Matrix4x4>[] Groups()
        {
            var result = new List<Matrix4x4>[12];
            for (int i = 0; i < result.Length; i++) result[i] = new List<Matrix4x4>();
            return result;
        }

        static float Density(HillsideGrassRenderer grass, float distance)
        {
            float near = Mathf.Max(12, grass.visibleRadius);
            if (distance <= near) return 1;
            if (distance < near * 2.3f) return Mathf.Lerp(1, .18f, Mathf.SmoothStep(0, 1, Mathf.InverseLerp(near, near * 2.3f, distance)));
            if (distance < near * 5) return Mathf.Lerp(.18f, .018f, Mathf.SmoothStep(0, 1, Mathf.InverseLerp(near * 2.3f, near * 5, distance)));
            return Mathf.Lerp(.018f, .006f, Mathf.SmoothStep(0, 1, Mathf.InverseLerp(near * 5, near * 11, distance)));
        }

        static List<Matrix4x4>[] Legacy(HillsideGrassRenderer grass, Vector2Int key, float edge, Vector3 view)
        {
            var groups = Groups();
            var random = new System.Random(unchecked(grass.seed ^ key.x * 73856093 ^ key.y * 19349663));
            Vector3 origin = grass.terrain.transform.position, size = grass.terrain.terrainData.size;
            float totalWeight = Mathf.Max(0, grass.speciesWeights[0]) + Mathf.Max(0, grass.speciesWeights[1]) + Mathf.Max(0, grass.speciesWeights[2]);
            int maximum = Mathf.CeilToInt(edge * edge * Mathf.Clamp(grass.clumpsPerSquareMeter, 0, 40));
            var bounds = new Bounds(new Vector3((key.x + .5f) * edge, origin.y + size.y * .5f, (key.y + .5f) * edge), new Vector3(edge + 2, size.y + 2, edge + 2));
            int attempts = Mathf.CeilToInt(maximum * Density(grass, Mathf.Sqrt(bounds.SqrDistance(view))));
            if (totalWeight <= 0) attempts = 0;
            for (int i = 0; i < attempts; i++)
            {
                float x = (key.x + (float)random.NextDouble()) * edge, z = (key.y + (float)random.NextDouble()) * edge;
                float pick = (float)random.NextDouble() * totalWeight;
                int species = pick < grass.speciesWeights[0] ? 0 : pick < grass.speciesWeights[0] + grass.speciesWeights[1] ? 1 : 2;
                int variant = species * 4 + random.Next(4);
                float yaw = (float)random.NextDouble() * 360, scale = .8f + (float)random.NextDouble() * .4f;
                float rank = (i + (float)random.NextDouble()) / Mathf.Max(1, maximum);
                float u = (x - origin.x) / size.x, v = (z - origin.z) / size.z;
                if (u <= .004f || u >= .996f || v <= .004f || v >= .996f) continue;
                float height = grass.terrain.SampleHeight(new Vector3(x, 0, z)) + origin.y - .025f;
                float distance = Vector3.Distance(view, new Vector3(x, height, z)), depth = 0;
                foreach (var water in HillsideLayout.Waters)
                    if (water.SignedDistance(x, z) <= 0) depth = Mathf.Max(depth, water.SurfaceY - (height + .025f));
                float coverage = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(0, 1.8f, HillsideLayout.PathEdgeDistance(x, z))) *
                    (1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(grass.maximumSlope - 8, grass.maximumSlope, grass.terrain.terrainData.GetSteepness(u, v))));
                if (depth > 0)
                {
                    float patch = Mathf.PerlinNoise((x + grass.seed * .13f) * .12f, (z - grass.seed * .17f) * .12f);
                    float bedDensity = Mathf.Lerp(.06f, .55f, Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.3f, .7f, patch)));
                    coverage *= Mathf.Lerp(1, bedDensity, Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.15f, 1.2f, depth)));
                    float tallWeight = grass.speciesWeights[1] * (1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(0, .45f, depth)));
                    species = pick >= grass.speciesWeights[0] && pick < grass.speciesWeights[0] + tallWeight ? 1 : 0;
                    variant = species * 4 + variant % 4;
                }
                if (rank >= Density(grass, distance) * coverage) continue;
                scale *= Mathf.Lerp(1, .65f, Mathf.SmoothStep(0, 1, Mathf.InverseLerp(75, 260, distance)));
                groups[variant].Add(Matrix4x4.TRS(new Vector3(x, height, z), Quaternion.Euler(0, yaw, 0), Vector3.one * scale));
            }
            return groups;
        }
    }
}
