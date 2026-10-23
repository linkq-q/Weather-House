using System;
using System.Collections.Generic;
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
    public static class HillsideGrassBatchingChecks
    {
        sealed class Capture
        {
            public readonly List<Matrix4x4>[] matrices = Groups();
            public readonly Matrix4x4[][] buffers = new Matrix4x4[HillsideGrassInstanceBatcher.GroupCount][];
            public int submissions;

            public void Submit(int group, Matrix4x4[] values, int count)
            {
                Require(count > 0 && count <= 1023, "Draw count exceeded the instance limit or submitted an empty batch.");
                Require(values.Length == 1023, "Instance buffer capacity changed.");
                if (buffers[group] == null) buffers[group] = values;
                else Require(ReferenceEquals(buffers[group], values), "A group allocated a replacement instance buffer.");
                for (int i = 0; i < count; i++) matrices[group].Add(values[i]);
                submissions++;
            }

            public void Clear()
            {
                foreach (List<Matrix4x4> group in matrices) group.Clear();
                submissions = 0;
            }
        }

        [MenuItem("Weather House/Checks/Grass Instance Batching")]
        public static void RunMenu() { Debug.Log(Run()); }

        public static string Run()
        {
            var capture = new Capture();
            var batcher = new HillsideGrassInstanceBatcher(capture.Submit);
            var report = new StringBuilder("Grass instance batching checks\n");
            int serial = 0, compared = 0;
            foreach (int count in new[] { 0, 1, 1022, 1023, 1024 })
            {
                batcher.Begin(); capture.Clear(); var expected = Groups();
                for (int group = 0; group < 24; group++) Append(batcher, expected, group, count, ref serial);
                batcher.Flush();
                int submissions = capture.submissions;
                batcher.Flush();
                Require(capture.submissions == submissions, "Flush resubmitted an already consumed tail.");
                Require(submissions == 24 * ((count + 1022) / 1023), "Boundary count emitted the wrong number of batches: " + count);
                compared += Compare(expected, capture.matrices);
                report.AppendLine("Boundary " + count + " x 24 groups: " + submissions + " submissions.");
            }

            batcher.Begin(); capture.Clear();
            var discarded = Groups();
            for (int group = 0; group < 24; group++) Append(batcher, discarded, group, 17, ref serial);
            batcher.Begin();
            var resetExpected = Groups();
            for (int group = 0; group < 24; group++) Append(batcher, resetExpected, group, 1, ref serial);
            batcher.Flush();
            compared += Compare(resetExpected, capture.matrices);
            Require(capture.submissions == 24, "Begin did not discard the previous frame's unfinished tails.");

            batcher.Begin(); capture.Clear();
            var expectedCells = Groups(); int oldSubmissions = 0;
            for (int cell = 0; cell < 16; cell++)
                for (int group = 0; group < 24; group++)
                {
                    Append(batcher, expectedCells, group, 89, ref serial);
                    oldSubmissions++;
                }
            batcher.Flush();
            compared += Compare(expectedCells, capture.matrices);
            Require(capture.submissions == 48, "Small cell batches were not combined across cell boundaries.");
            report.AppendLine("Synthetic 16-cell fixture: old submissions=" + oldSubmissions + ", combined=" + capture.submissions + ", matrices=" + (16 * 24 * 89) + ".");
            var distinctBuffers = new HashSet<Matrix4x4[]>();
            foreach (Matrix4x4[] buffer in capture.buffers) Require(distinctBuffers.Add(buffer), "Different variant/LOD groups shared a mutable instance buffer.");
            CheckRenderer(report);
            report.AppendLine("PASS: " + compared + " exact matrices in original per-group order; 24 variant/LOD groups isolated; 1023 boundaries; repeated Flush; frame reset; all buffers reused.");
            Directory.CreateDirectory("HillsidePerformanceReview");
            HillsidePerformanceDiagnostics.WriteText("HillsidePerformanceReview/grass-batching-checks.txt", report.ToString());
            return report.ToString();
        }

        static void CheckRenderer(StringBuilder report)
        {
            const BindingFlags fields = BindingFlags.Instance | BindingFlags.NonPublic;
            var source = Object.FindObjectOfType<HillsideGrassRenderer>();
            Camera main = source && source.viewCamera ? source.viewCamera : Camera.main;
            Require(source && source.terrain && main, "Open the hillside scene with grass and its main camera for the renderer wiring check.");
            PropertyInfo submitted = typeof(HillsideGrassRenderer).GetProperty("SubmittedDrawCount");
            Require(submitted != null, "REGRESSION: renderer does not expose its actual merged submission count yet.");
            Scene active = SceneManager.GetActiveScene();
            int sceneCount = SceneManager.sceneCount;
            var dirty = new Dictionary<int, bool>();
            for (int i = 0; i < sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i); dirty.Add(scene.handle, scene.isDirty);
            }
            Scene preview = EditorSceneManager.NewPreviewScene();
            GameObject grassObject = null, cameraObject = null;
            try
            {
                grassObject = new GameObject("Grass batching renderer check") { hideFlags = HideFlags.HideAndDontSave };
                SceneManager.MoveGameObjectToScene(grassObject, preview);
                grassObject.layer = source.gameObject.layer;
                var grass = grassObject.AddComponent<HillsideGrassRenderer>();
                grass.manualCaptureOnly = true;
                grass.terrain = source.terrain; grass.variants = source.variants; grass.materials = source.materials;
                grass.speciesWeights = (float[])source.speciesWeights.Clone(); grass.seed = source.seed;
                grass.clumpsPerSquareMeter = source.clumpsPerSquareMeter; grass.cellSize = source.cellSize;
                grass.visibleRadius = source.visibleRadius; grass.maximumSlope = source.maximumSlope;
                grass.meshLodDistance = source.meshLodDistance; grass.meshLodHysteresis = source.meshLodHysteresis;
                cameraObject = new GameObject("Grass batching camera check") { hideFlags = HideFlags.HideAndDontSave };
                SceneManager.MoveGameObjectToScene(cameraObject, preview);
                var camera = cameraObject.AddComponent<Camera>();
                camera.CopyFrom(main); camera.enabled = false;
                camera.transform.SetPositionAndRotation(main.transform.position, main.transform.rotation);
                grass.viewCamera = camera;
                grass.DrawForCamera(camera, true);
                Plane[] planes = GeometryUtility.CalculateFrustumPlanes(camera);
                var cells = (System.Collections.IDictionary)typeof(HillsideGrassRenderer).GetField("cells", fields).GetValue(grass);
                MethodInfo boundsForKey = typeof(HillsideGrassRenderer).GetMethod("CellBounds", fields);
                int oldSubmissions = 0, total = 0;
                foreach (System.Collections.DictionaryEntry pair in cells)
                {
                    object cell = pair.Value; Type type = cell.GetType();
                    Bounds bounds = (Bounds)type.GetField("bounds").GetValue(cell);
                    Bounds initial = (Bounds)boundsForKey.Invoke(grass, new object[] { pair.Key, Mathf.Max(4, grass.cellSize) });
                    if (!(bool)type.GetField("ready").GetValue(cell) || !GeometryUtility.TestPlanesAABB(planes, initial) || !GeometryUtility.TestPlanesAABB(planes, bounds)) continue;
                    foreach (object batch in (System.Collections.IEnumerable)type.GetField("batches").GetValue(cell))
                    {
                        Type batchType = batch.GetType();
                        int count = (int)batchType.GetField("count").GetValue(batch);
                        int variant = (int)batchType.GetField("variant").GetValue(batch);
                        if (count == 0 || !grass.variants[variant] || !grass.materials[variant / 4]) continue;
                        total += count; oldSubmissions++;
                    }
                }
                int actualSubmissions = (int)submitted.GetValue(grass);
                Require(total > 0 && grass.VisibleClumpCount == total, "Renderer wiring lost or duplicated visible grass matrices.");
                Require(actualSubmissions > 0 && actualSubmissions < oldSubmissions, "Renderer did not reduce actual submissions below nonempty visible cell batches.");
                FieldInfo queryCounter = typeof(HillsideGrassRenderer).GetField("staticCandidateQueries", fields);
                long before = (long)queryCounter.GetValue(grass);
                grass.DrawForCamera(camera, true);
                Require((long)queryCounter.GetValue(grass) == before, "The same camera view repeated static grass generation.");
                Require(grass.VisibleClumpCount == total && (int)submitted.GetValue(grass) == actualSubmissions, "A repeated view changed matrix or submission counts.");
                report.AppendLine("Actual renderer view: old cell submissions=" + oldSubmissions + ", merged=" + actualSubmissions + ", matrices=" + total + "; second view repeated no static queries.");
            }
            finally
            {
                if (cameraObject) Object.DestroyImmediate(cameraObject);
                if (grassObject) Object.DestroyImmediate(grassObject);
                EditorSceneManager.ClosePreviewScene(preview);
                Require(SceneManager.GetActiveScene().handle == active.handle && SceneManager.sceneCount == sceneCount, "Renderer check changed the loaded or active scenes.");
                for (int i = 0; i < sceneCount; i++)
                {
                    Scene scene = SceneManager.GetSceneAt(i);
                    Require(dirty.TryGetValue(scene.handle, out bool wasDirty) && scene.isDirty == wasDirty, "Renderer check changed scene dirty state.");
                }
            }
        }

        static void Append(HillsideGrassInstanceBatcher batcher, List<Matrix4x4>[] expected, int group, int count, ref int serial)
        {
            var source = new Matrix4x4[count + 3];
            for (int i = 0; i < count; i++)
            {
                Matrix4x4 matrix = Matrix4x4.identity;
                matrix.m03 = ++serial; matrix.m13 = group; matrix.m23 = serial * .25f;
                source[i] = matrix; expected[group].Add(matrix);
            }
            // The unused array suffix must never be copied or submitted.
            for (int i = count; i < source.Length; i++) source[i] = Matrix4x4.zero;
            batcher.Append(group, source, count);
        }

        static int Compare(List<Matrix4x4>[] expected, List<Matrix4x4>[] actual)
        {
            int compared = 0;
            for (int group = 0; group < expected.Length; group++)
            {
                Require(expected[group].Count == actual[group].Count, "Lost, duplicated or cross-group matrices in group " + group);
                for (int i = 0; i < expected[group].Count; i++)
                    for (int component = 0; component < 16; component++)
                        Require(expected[group][i][component] == actual[group][i][component], "Changed matrix/order in group " + group + " at " + i);
                compared += expected[group].Count;
            }
            return compared;
        }

        static List<Matrix4x4>[] Groups()
        {
            var result = new List<Matrix4x4>[24];
            for (int i = 0; i < result.Length; i++) result[i] = new List<Matrix4x4>();
            return result;
        }

        static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    }
}
