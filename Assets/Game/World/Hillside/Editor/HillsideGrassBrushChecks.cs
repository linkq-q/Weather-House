using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using WeatherHouse.Vegetation;

namespace WeatherHouse.Hillside.Editor
{
    public static class HillsideGrassBrushChecks
    {
        [Serializable] sealed class Report
        {
            public string generatedUtc, scope = "Editor 参数/资产及预览场景真实 Undo/Redo、草种切换与圆选区应用检查；SceneView 鼠标和实际画面需独立验收。";
            public bool passed;
            public int presets, uniqueKinds, reusedMeshVariants;
            public string[] checks;
        }

        [MenuItem("天气屋/地形与植被/检查草笔刷（独立预览场景）")]
        static void RunFromMenu() { Run(); }

        public static string Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("请在 Edit 模式检查草笔刷。");
            var report = new Report { generatedUtc = DateTime.UtcNow.ToString("o") };
            var checks = new List<string>();
            string path = Path.GetFullPath("WatersideV3Review/grass-brush/editor-checks.json");
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            try
            {
                var catalog = HillsideGrassBrushWindow.ReadCatalog();
                report.presets = catalog.Count;
                report.uniqueKinds = catalog.Select(c => c.definition.kind).Distinct().Count();
                report.reusedMeshVariants = catalog.Sum(c => c.variants == null ? 0 : c.variants.Length);
                Require(catalog.Count >= 20, "20 个草种预设可用，包含四档长短草与独立羽穗原型", checks);
                var missing = Enum.GetValues(typeof(GrassKind)).Cast<GrassKind>().Except(catalog.Select(c => c.definition.kind)).ToArray();
                Require(missing.Length == 0, "13 种植物形态完整；缺失=" + string.Join(",", missing), checks);
                Require(catalog.All(c => c.material && c.definition && c.variants != null && c.variants.Length >= 3 && c.variants.All(v => v)),
                    "每个预设复用现有材质、定义及至少3个网格变体", checks);
                Require(catalog.All(c => EditorUtility.IsPersistent(c.material) && EditorUtility.IsPersistent(c.definition) && c.variants.All(EditorUtility.IsPersistent)),
                    "草种选择引用项目资产，不建立临时全局定义或改写原网格", checks);
                var originalPlume = AssetDatabase.LoadAssetAtPath<GrassSpecies>("Assets/Experiments/Vegetation/PlantLibrary/Definitions/R01_Plume.asset");
                var solidSeed = AssetDatabase.LoadAssetAtPath<GrassSpecies>("Assets/Game/World/WatersideV2/Generated/Nature/Plants/Definitions/R01_Plume.asset");
                Require(originalPlume && solidSeed && originalPlume.kind == GrassKind.Plume && solidSeed.kind == GrassKind.SeedHead &&
                    catalog.Any(c => c.definition == originalPlume) && catalog.Any(c => c.definition == solidSeed),
                    "同名 R01 的羽穗原型与水岸实体长穗均保留为独立可选形态", checks);
                Require(catalog.GroupBy(c => new { c.definition, c.material }).All(g => g.Count() == 1),
                    "补回缺失形态不重复已有预设", checks);

                var erase = HillsideGrassBrushWindow.MakeSettings(GrassPaintMode.Erase, -1, 2, .3f, .8f, .35f, 1, 1, true, true, true);
                Require(erase.mode == GrassPaintMode.Erase && erase.sourceId == -1 && Near(erase.density, .35f) && Near(erase.hardness, .3f),
                    "擦除保留密度与软边独立传递，支持全部草种", checks);
                var paint = HillsideGrassBrushWindow.MakeSettings(GrassPaintMode.Paint, 2, 3, .5f, 1, 12.5f, 1.3f, .7f, true, true, true);
                Require(paint.sourceId == 2 && Near(paint.density, 12.5f) && Near(paint.heightScale, 1.3f) && Near(paint.widthScale, .7f),
                    "铺草密度按丛/㎡传递，高宽独立且不修改源草种", checks);
                var adjust = HillsideGrassBrushWindow.MakeSettings(GrassPaintMode.Adjust, 0, 4, .4f, 1, 1.5f, .7f, 1.8f, false, true, false);
                Require(adjust.mode == GrassPaintMode.Adjust && !adjust.affectDensity && adjust.affectHeight && !adjust.affectWidth && Near(adjust.heightScale, .7f),
                    "局部调整只作用勾选参数，保留草种筛选", checks);
                var clamped = HillsideGrassBrushWindow.MakeSettings(GrassPaintMode.Adjust, 0, -1, 3, 2, 40, 0, 9, true, true, true);
                Require(Near(clamped.radius, .2f) && Near(clamped.hardness, 1) && Near(clamped.strength, 1) && Near(clamped.density, 2)
                    && Near(clamped.heightScale, .1f) && Near(clamped.widthScale, 4), "窗口参数范围与模式密度上限一致", checks);
                CheckUndoAndGuard(checks);
                report.passed = true;
            }
            finally
            {
                report.checks = checks.ToArray();
                File.WriteAllText(path, JsonUtility.ToJson(report, true));
            }
            Debug.Log("Grass brush editor checks passed: " + path);
            return path;
        }

        static bool Near(float a, float b) => Mathf.Abs(a - b) < .0001f;

        static void CheckUndoAndGuard(List<string> checks)
        {
            Scene active = SceneManager.GetActiveScene();
            var originalScenes = Enumerable.Range(0, SceneManager.sceneCount).Select(SceneManager.GetSceneAt).ToArray();
            bool[] originalDirty = originalScenes.Select(s => s.isDirty).ToArray();
            Scene preview = EditorSceneManager.NewPreviewScene();
            TerrainData data = null;
            GameObject go = null, unrelated = null;
            HillsideGrassBrushWindow window = null;
            HillsideGrassRenderer grass = null;
            GrassLocalPainter painter = null;
            try
            {
                go = EditorUtility.CreateGameObjectWithHideFlags("Grass brush editor regression", HideFlags.HideAndDontSave);
                SceneManager.MoveGameObjectToScene(go, preview);
                unrelated = EditorUtility.CreateGameObjectWithHideFlags("Unrelated edit regression", HideFlags.HideAndDontSave);
                SceneManager.MoveGameObjectToScene(unrelated, preview);
                data = new TerrainData { heightmapResolution = 33, size = new Vector3(16, 2, 16), hideFlags = HideFlags.HideAndDontSave };
                var terrain = go.AddComponent<Terrain>(); terrain.terrainData = data;
                go.AddComponent<TerrainCollider>().terrainData = data;
                grass = go.AddComponent<HillsideGrassRenderer>(); grass.terrain = terrain;
                window = ScriptableObject.CreateInstance<HillsideGrassBrushWindow>();
                window.SetTarget(grass);
                window.InitializePainter();
                painter = go.GetComponent<GrassLocalPainter>();
                Require(painter && painter.StrokeCount == 0 && grass.localPainter == painter, "首次启用仅加空 Painter，不放置示例笔触", checks);
                Require(HillsideGrassBrushWindow.EditBlockReason(grass, false, false) == null, "有效场景草地允许 Edit 编辑", checks);
                Require(HillsideGrassBrushWindow.EditBlockReason(grass, true, false).Contains("Play"), "Play 及进入 Play 分支阻止编辑（不切换实际 Play）", checks);
                Require(HillsideGrassBrushWindow.EditBlockReason(grass, false, true) != null, "Prefab 编辑阶段阻止笔触", checks);
                window.StartStroke(new Vector3(4, 0, 4), GrassPaintMode.Paint);
                painter.AppendStrokePoint(0, new Vector3(7, 0, 4));
                window.FinishStroke();
                Undo.FlushUndoRecordObjects();
                Require(painter.StrokeCount == 1 && painter.SampleOverlay(new Vector3(5, 0, 4), 0).density > 0, "窗口真实连续笔触只生成一条可见数据", checks);
                // Deliberately record another tool's edit immediately, without Unity mouse-event grouping.
                Undo.RegisterCompleteObjectUndo(unrelated.transform, "无关物体移动");
                unrelated.transform.position = new Vector3(12, 3, 9);
                Undo.FlushUndoRecordObjects();
                Undo.PerformUndo();
                Require(unrelated.transform.position == Vector3.zero && painter.StrokeCount == 1,
                    "笔触结束后无关物体的编辑独立撤销，不连带删除草笔触", checks);
                Undo.PerformUndo();
                Require(painter && painter.StrokeCount == 0 && Near(painter.SampleOverlay(new Vector3(5, 0, 4), 0).density, 0), "一次 Undo 恢复整笔及局部采样", checks);
                Undo.PerformRedo();
                Require(painter.StrokeCount == 1 && painter.SampleOverlay(new Vector3(5, 0, 4), 0).density > 0 && unrelated.transform.position == Vector3.zero,
                    "一次 Redo 只恢复草笔触及局部采样，无关物体仍保持撤销状态", checks);
                Undo.PerformRedo();
                Require(unrelated.transform.position == new Vector3(12, 3, 9) && painter.StrokeCount == 1,
                    "后续 Redo 单独恢复无关物体", checks);

                SetWindowSettings(window, 2, 2, .55f, 1.8f, false, true, false);
                window.SelectRegion(new Vector3(5, 0, 4), 2);
                Require(painter.StrokeCount == 1 && Near(painter.SampleBase(new Vector3(5, 0, 4), 1).heightScale, 1),
                    "圈定范围和改窗口参数本身不修改草地", checks);
                window.ApplyRegion();
                Require(painter.StrokeCount == 2 && Near(painter.SampleBase(new Vector3(5, 0, 4), 1).heightScale, .55f),
                    "点击应用才修改圆选区内目标草种高度", checks);
                var selected = painter.SampleBase(new Vector3(5, 0, 4), 1);
                var other = painter.SampleBase(new Vector3(5, 0, 4), 0);
                var exterior = painter.SampleBase(new Vector3(9, 0, 4), 1);
                Require(Near(selected.density, 1) && Near(selected.widthScale, 1) && Near(other.heightScale, 1) && Near(exterior.heightScale, 1),
                    "未勾选的密度/宽度、其它草种及选区外高度保留", checks);
                Undo.PerformUndo();
                Require(painter.StrokeCount == 1 && Near(painter.SampleBase(new Vector3(5, 0, 4), 1).heightScale, 1),
                    "圆选区调整作为独立一笔撤销", checks);
                Undo.PerformRedo();
                Require(painter.StrokeCount == 2 && Near(painter.SampleBase(new Vector3(5, 0, 4), 1).heightScale, .55f),
                    "圆选区调整可重做", checks);

                SetWindowSettings(window, 1, 4, 1.2f, .7f, true, true, true);
                window.StartStroke(new Vector3(8, 0, 8), GrassPaintMode.Paint);
                window.FinishStroke();
                Require(painter.StrokeCount == 3 && painter.GetSources().Length == 4 && painter.SampleOverlay(new Vector3(8, 0, 8), 3).density > 0,
                    "切换植物库预设后注册独立草种并铺草", checks);
                Undo.PerformUndo();
                Require(painter.StrokeCount == 2 && painter.GetSources().Length == 3,
                    "植物库铺草的一次 Undo 同时还原新增草种引用，不残留无效来源", checks);
                Undo.PerformRedo();
                Require(painter.StrokeCount == 3 && painter.GetSources().Length == 4 && painter.SampleOverlay(new Vector3(8, 0, 8), 3).density > 0,
                    "植物库铺草 Redo 恢复来源与笔触", checks);
            }
            finally
            {
                if (window) UnityEngine.Object.DestroyImmediate(window);
                if (painter) Undo.ClearUndo(painter);
                if (grass) Undo.ClearUndo(grass);
                if (go) { Undo.ClearUndo(go); UnityEngine.Object.DestroyImmediate(go); }
                if (unrelated) { Undo.ClearUndo(unrelated.transform); UnityEngine.Object.DestroyImmediate(unrelated); }
                EditorSceneManager.ClosePreviewScene(preview);
                if (data) UnityEngine.Object.DestroyImmediate(data);
            }
            Require(SceneManager.GetActiveScene() == active && SceneManager.sceneCount == originalScenes.Length &&
                originalScenes.Select((s, i) => s.IsValid() && s.isLoaded && s.isDirty == originalDirty[i]).All(v => v),
                "当前场景、已加载场景和 Dirty 状态保留；未保存或重开主场景", checks);
        }

        static void SetWindowSettings(HillsideGrassBrushWindow window, int mode, int sourceIndex, float height, float width,
            bool density, bool changeHeight, bool changeWidth)
        {
            var serialized = new SerializedObject(window);
            serialized.FindProperty("mode").intValue = mode;
            serialized.FindProperty("selectedSource").intValue = sourceIndex;
            serialized.FindProperty("heightScale").floatValue = height;
            serialized.FindProperty("widthScale").floatValue = width;
            serialized.FindProperty("affectDensity").boolValue = density;
            serialized.FindProperty("affectHeight").boolValue = changeHeight;
            serialized.FindProperty("affectWidth").boolValue = changeWidth;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        static void Require(bool condition, string description, List<string> checks)
        {
            checks.Add((condition ? "PASS " : "FAIL ") + description);
            if (!condition) throw new InvalidOperationException(description);
        }
    }
}
