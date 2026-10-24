using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using WeatherHouse.Hillside;
using Object = UnityEngine.Object;

namespace WeatherHouse.Vegetation.Editor
{
    // Uses the production matrix builders in an additive fixture; never opens or edits the user's map.
    public static class GrassLocalPaintIntegrationChecks
    {
        const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
        static readonly Vector3 Center = new Vector3(12, 1, 12), View = new Vector3(12, 12, 12);
        static readonly Vector2Int Key = new Vector2Int(1, 1);
        const float Radius = 2.8f;
        sealed class Instance { public int source, variant; public Matrix4x4 matrix; public Vector3 Position => matrix.GetColumn(3); }

        [MenuItem("Tools/Weather House/草地笔刷/验证实际渲染与保存")]
        static void Menu() { Debug.Log(Run()); }

        public static string Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("请在非 Play 状态运行独立草刷检查。");
            Scene active = SceneManager.GetActiveScene(), fixture = default;
            string folder = "Assets/__GrassBrushChecks_" + Guid.NewGuid().ToString("N"), report = "";
            GrassLocalPainter painter = null;
            try
            {
                AssetDatabase.CreateFolder("Assets", Path.GetFileName(folder));
                fixture = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
                SceneManager.SetActiveScene(fixture);
                var data = new TerrainData { heightmapResolution = 33, size = new Vector3(32, 4, 32) };
                AssetDatabase.CreateAsset(data, folder + "/terrain.asset");
                GameObject terrainObject = Terrain.CreateTerrainGameObject(data); terrainObject.transform.position = Vector3.up;
                var map = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
                map.SetPixels(new[] { Color.red, Color.red, Color.red, Color.red }); map.Apply();
                AssetDatabase.CreateAsset(map, folder + "/coverage.asset");
                var mesh = new Mesh { name = "Brush fixture blade" };
                mesh.vertices = new[] { new Vector3(-.04f, 0, 0), new Vector3(.04f, 0, 0), new Vector3(0, .5f, 0) };
                mesh.triangles = new[] { 0, 1, 2 }; mesh.RecalculateNormals(); mesh.RecalculateBounds();
                AssetDatabase.CreateAsset(mesh, folder + "/blade.asset");
                Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                Require(shader, "Fixture instancing shader is available");
                var material = new Material(shader) { enableInstancing = true };
                AssetDatabase.CreateAsset(material, folder + "/blade.mat");
                var grass = new GameObject("Local grass fixture").AddComponent<HillsideGrassRenderer>();
                grass.terrain = terrainObject.GetComponent<Terrain>(); grass.placementMap = map;
                grass.variants = Enumerable.Repeat(mesh, 12).ToArray(); grass.materials = Enumerable.Repeat(material, 3).ToArray();
                grass.speciesWeights = new[] { 1f, 1f, 1f }; grass.clumpsPerSquareMeter = 12; grass.cellSize = 8;
                grass.visibleRadius = 42; grass.manualCaptureOnly = true;
                var camera = new GameObject("Brush test observer").AddComponent<Camera>(); camera.enabled = false;
                camera.transform.position = View; camera.transform.rotation = Quaternion.Euler(90, 0, 0);
                camera.orthographic = true; camera.orthographicSize = 10; camera.farClipPlane = 100;

                List<Instance> baseline = Base(grass);
                painter = grass.gameObject.AddComponent<GrassLocalPainter>();
                Same(baseline, Base(grass), "Adding a painter preserves existing matrices");
                var settings = new GrassBrushSettings { mode = GrassPaintMode.Adjust, sourceId = 0, density = 2,
                    radius = Radius, hardness = 1, affectHeight = false, affectWidth = false };
                int stroke = painter.BeginStroke(settings);
                painter.AppendStrokePoint(stroke, new Vector3(28, 1, 28));
                Call(painter, "OnValidate");
                painter.AppendStrokePoint(stroke, new Vector3(30, 1, 28));
                painter.CancelStroke(stroke);
                Require(painter.StrokeCount == 0, "OnValidate must not break an active mouse drag");

                painter.BrushAt(Center, settings);
                // Reproduce the former production candidate cap, with the real candidate/filter/matrix code.
                object cell = Call(grass, "GetOrCreateCell", Key, 8f);
                Call(grass, "BeginBuild", cell, View);
                Field(cell, "attempts").SetValue(cell, (int)Field(cell, "maximum").GetValue(cell));
                Call(grass, "AdvanceBuild", cell, long.MaxValue);
                List<Instance> oldCap = FromCell(cell, false);
                int initialCount = baseline.Count(i => i.source == 0 && Inside(i.Position));
                int oldCapCount = oldCap.Count(i => i.source == 0 && Inside(i.Position));
                Require(oldCapCount == initialCount, "RED control must reproduce the original density ceiling");
                grass.InvalidateLocalRegion(new Rect(8, 8, 8, 8));
                List<Instance> increased = Base(grass);
                int increasedCount = increased.Count(i => i.source == 0 && Inside(i.Position));
                Require(initialCount > 30 && increasedCount > initialCount * 1.6f,
                    "Density 2 must add actual base instances: " + initialCount + " -> " + increasedCount);
                Same(baseline.Where(i => i.source != 0 || !Inside(i.Position)), increased.Where(i => i.source != 0 || !Inside(i.Position)),
                    "Density must preserve every other species and every exterior matrix");
                foreach (Instance item in baseline) Require(increased.Any(i => i.variant == item.variant && i.matrix.Equals(item.matrix)), "Original candidate positions remain intact");
                long queries = (long)Field(grass, "staticCandidateQueries").GetValue(grass);
                Same(increased, Base(grass), "Repeated base draw uses identical matrices");
                Require((long)Field(grass, "staticCandidateQueries").GetValue(grass) == queries, "Unchanged cells must reuse terrain samples");

                painter.BrushAt(Center, new GrassBrushSettings { mode = GrassPaintMode.Adjust, sourceId = 1, radius = Radius,
                    hardness = 1, affectDensity = false, heightScale = 1.8f, widthScale = .65f });
                List<Instance> reshaped = Base(grass);
                Same(increased.Where(i => i.source != 1 || !Inside(i.Position)), reshaped.Where(i => i.source != 1 || !Inside(i.Position)), "Shape edit remains local and species-specific");
                foreach (Instance before in increased.Where(i => i.source == 1 && Inside(i.Position)))
                {
                    Instance after = reshaped.Single(i => i.variant == before.variant && i.Position == before.Position);
                    Near(after.matrix.GetColumn(1).magnitude / before.matrix.GetColumn(1).magnitude, 1.8f, "Local height");
                    Near(after.matrix.GetColumn(0).magnitude / before.matrix.GetColumn(0).magnitude, .65f, "Local width");
                }
                var species = ScriptableObject.CreateInstance<GrassSpecies>(); species.kind = GrassKind.Fern;
                AssetDatabase.CreateAsset(species, folder + "/species.asset");
                int source = painter.AddSource(species, material, new[] { mesh });
                painter.BrushAt(Center, new GrassBrushSettings { mode = GrassPaintMode.Paint, sourceId = source, radius = Radius,
                    hardness = 1, density = 8, heightScale = 1.3f, widthScale = .7f });
                List<Instance> overlay = Overlay(painter, camera);
                Require(overlay.Count > 100 && overlay.All(i => i.source == source && Inside(i.Position)), "New species is actually rendered only inside the brush");
                object overlayCell = OverlayCell(painter);
                Same(overlay, Overlay(painter, camera), "Stable overlay matrices");
                Require(ReferenceEquals(overlayCell, OverlayCell(painter)), "Overlay cache survives unchanged draws");

                Undo.IncrementCurrentGroup();
                Undo.RegisterCompleteObjectUndo(painter, "Grass integration erase");
                painter.BrushAt(Center, new GrassBrushSettings { mode = GrassPaintMode.Erase, sourceId = source, radius = Radius, hardness = 1, density = 0 });
                EditorUtility.SetDirty(painter); Undo.FlushUndoRecordObjects();
                Require(Overlay(painter, camera).Count == 0, "Erasing the added species removes its real instances");
                Undo.PerformUndo(); painter.NotifyEdited();
                Same(overlay, Overlay(painter, camera), "Undo restores overlay caches and geometry");
                Undo.ClearUndo(painter);
                painter.enabled = false;
                Require(painter.VisibleOverlayCount == 0 && painter.CachedOverlayCells == 0 && grass.localPainter == null, "Disable releases overlay resources and detaches edits");
                Same(baseline, Base(grass), "Disabling restores original base instances");
                painter.enabled = true;
                Same(reshaped, Base(grass), "Re-enable restores base edits");
                Same(overlay, Overlay(painter, camera), "Re-enable restores overlay instances");

                string scenePath = folder + "/fixture.unity";
                EditorSceneManager.MarkSceneDirty(fixture); AssetDatabase.SaveAssets();
                Require(EditorSceneManager.SaveScene(fixture, scenePath), "Fixture save succeeds");
                SceneManager.SetActiveScene(active); EditorSceneManager.CloseScene(fixture, true);
                fixture = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
                grass = fixture.GetRootGameObjects().SelectMany(o => o.GetComponentsInChildren<HillsideGrassRenderer>()).Single();
                grass.manualCaptureOnly = true; painter = grass.GetComponent<GrassLocalPainter>();
                camera = fixture.GetRootGameObjects().SelectMany(o => o.GetComponentsInChildren<Camera>()).Single();
                Same(reshaped, Base(grass), "Saved scene reload restores base matrices exactly");
                Same(overlay, Overlay(painter, camera), "Saved scene reload restores overlay matrices exactly");
                report = "PASS actual production matrix builders; legacy-cap RED " + initialCount + " -> " + oldCapCount +
                    "; fixed density GREEN " + initialCount + " -> " + increasedCount + ".\n" +
                    "PASS unchanged original candidate prefix, all exterior matrices, other species, local height/width, active drag validation, cached terrain samples, " +
                    overlay.Count + " added-species instances, real Unity Undo, disable/re-enable, and save/close/reopen exact matrices.\n" +
                    "Only an additive disposable fixture was modified. Main scene and source grass assets were untouched. Visual brush UI is checked separately.";
                return report;
            }
            catch (Exception error) { report = "FAIL " + error; throw; }
            finally
            {
                if (painter) Undo.ClearUndo(painter);
                if (active.IsValid() && active.isLoaded) SceneManager.SetActiveScene(active);
                if (fixture.IsValid() && fixture.isLoaded) EditorSceneManager.CloseScene(fixture, true);
                AssetDatabase.DeleteAsset(folder);
                Directory.CreateDirectory("WatersideV3Review/grass-brush");
                File.WriteAllText("WatersideV3Review/grass-brush/integration-checks.txt", DateTime.UtcNow.ToString("O") + "\n" + report);
            }
        }

        static List<Instance> Base(HillsideGrassRenderer grass) => FromCell(Call(grass, "BuildVerificationCell", Key, 8f, View), false);
        static List<Instance> Overlay(GrassLocalPainter painter, Camera camera)
        {
            painter.DrawForCamera(camera); var result = new List<Instance>();
            foreach (DictionaryEntry entry in (IDictionary)Field(painter, "cells").GetValue(painter)) result.AddRange(FromCell(entry.Value, true));
            return result;
        }
        static object OverlayCell(GrassLocalPainter painter) => ((IDictionary)Field(painter, "cells").GetValue(painter))[Key];
        static List<Instance> FromCell(object cell, bool overlay)
        {
            var result = new List<Instance>();
            foreach (object batch in (IEnumerable)Field(cell, "batches").GetValue(cell))
            {
                int variant = (int)Field(batch, "variant").GetValue(batch), count = (int)Field(batch, "count").GetValue(batch);
                int source = overlay ? (int)Field(batch, "sourceId").GetValue(batch) : variant / 4;
                var matrices = (Matrix4x4[])Field(batch, "matrices").GetValue(batch);
                for (int i = 0; i < count; i++) result.Add(new Instance { source = source, variant = variant, matrix = matrices[i] });
            }
            return result;
        }
        static FieldInfo Field(object target, string name) => target.GetType().GetField(name, Private | BindingFlags.Public);
        static object Call(object target, string name, params object[] args) => target.GetType().GetMethod(name, Private).Invoke(target, args);
        static bool Inside(Vector3 p) => new Vector2(p.x - Center.x, p.z - Center.z).magnitude < Radius;
        static void Same(IEnumerable<Instance> first, IEnumerable<Instance> second, string label)
        {
            var a = first.ToList(); var b = second.ToList(); Require(a.Count == b.Count, label + " (count)");
            var byPosition = b.ToDictionary(i => new Vector4(i.Position.x, i.Position.y, i.Position.z, i.variant + i.source * 16));
            foreach (Instance item in a)
                Require(byPosition.TryGetValue(new Vector4(item.Position.x, item.Position.y, item.Position.z, item.variant + item.source * 16), out Instance other)
                    && item.matrix.Equals(other.matrix), label + " (matrix)");
        }
        static void Near(float actual, float expected, string label) => Require(Mathf.Abs(actual - expected) < .0001f, label);
        static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    }
}
