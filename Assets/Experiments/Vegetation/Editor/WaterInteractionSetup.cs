using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace WeatherHouse.Vegetation.Editor
{
    public static class WaterInteractionSetup
    {
        public const string SimulationShaderName = "Hidden/WeatherHouse/WaterInteractionNormals";

        [MenuItem("Weather House/Vegetation Lab/角色涉水 · 接入法线尾迹")]
        public static void Install()
        {
            Scene scene = SceneManager.GetActiveScene();
            Require(!EditorApplication.isPlayingOrWillChangePlaymode && SceneManager.sceneCount == 1 &&
                !scene.isDirty && !string.IsNullOrEmpty(scene.path) && File.Exists(scene.path), "先保存单一场景并退出 Play。");
            Require(File.Exists(VegetationLabBuilder.ScenePath), "需要现有植被试验场；此入口不重建场景。");
            Shader simulation = Shader.Find(SimulationShaderName);
            Require(simulation && simulation.isSupported && !ShaderUtil.ShaderHasError(simulation), "法线场 shader 尚未正确导入。");
            if (scene.path != VegetationLabBuilder.ScenePath)
                scene = EditorSceneManager.OpenScene(VegetationLabBuilder.ScenePath, OpenSceneMode.Single);
            var adapter = Components<VegetationCharacterPreview>(scene).Single();
            Require(adapter.player && adapter.labView && adapter.labView.regions != null && adapter.labView.regions.Length > 0,
                "需要原角色与主草地引用。");
            var reflection = Components<PainterlyWaterReflection>(scene).Single();
            Renderer renderer = reflection.GetComponent<Renderer>();
            Require(renderer && renderer.sharedMaterial && renderer.sharedMaterial.HasProperty("_InteractionStrength"), "水材质尚未接入法线场。");
            var meadows = Components<GrassMeadow>(scene);
            string[] meadowStates = meadows.Select(EditorJsonUtility.ToJson).ToArray();
            Mesh[][] meshes = meadows.Select(m => m.GetComponentsInChildren<MeshFilter>(true).Select(f => f.sharedMesh).ToArray()).ToArray();
            string playerState = EditorJsonUtility.ToJson(adapter.player);
            string materialState = EditorJsonUtility.ToJson(renderer.sharedMaterial);
            var existing = Components<PainterlyWaterInteraction>(scene);
            Require(existing.Length <= 1 && (existing.Length == 0 || existing[0].gameObject == renderer.gameObject), "另一处已有涉水组件，先核对接线。");
            var interaction = renderer.GetComponent<PainterlyWaterInteraction>();
            if (!interaction) interaction = renderer.gameObject.AddComponent<PainterlyWaterInteraction>();
            interaction.waterRenderer = renderer;
            interaction.meadow = adapter.labView.regions[0];
            interaction.player = adapter.player;
            interaction.simulationShader = simulation;
            interaction.enabled = true;
            for (int i=0;i<meadows.Length;i++)
            {
                Require(EditorJsonUtility.ToJson(meadows[i]) == meadowStates[i], "接线不得改变草地参数。");
                Require(meshes[i].SequenceEqual(meadows[i].GetComponentsInChildren<MeshFilter>(true).Select(f => f.sharedMesh)), "接线不得重建草地网格。");
            }
            Require(EditorJsonUtility.ToJson(adapter.player) == playerState, "接线不得改变角色移动、动画或相机参数。");
            Require(EditorJsonUtility.ToJson(renderer.sharedMaterial) == materialState, "接线不得修改持久水材质。");
            EditorUtility.SetDirty(interaction);
            EditorSceneManager.MarkSceneDirty(scene);
            Require(EditorSceneManager.SaveScene(scene), "无法保存涉水组件接线。");
            Debug.Log("Water interaction installed: existing water, main meadow and character linked; meadow meshes and material assets retained.");
        }

        internal static T[] Components<T>(Scene scene) where T : Component =>
            scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<T>(true)).ToArray();
        static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException("WaterInteractionSetup: " + message); }
    }
}
