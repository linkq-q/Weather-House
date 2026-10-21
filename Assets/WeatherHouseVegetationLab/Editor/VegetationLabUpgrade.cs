using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace WeatherHouse.Vegetation.Editor
{
    public static class VegetationLabUpgrade
    {
        [MenuItem("Weather House/Vegetation Lab/Apply Painted Water and Tree Samples")]
        public static void Apply()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("请退出 Play 后更新试验场。");
            VegetationLabBuilder.Build();
            var meadow = Object.FindObjectOfType<GrassMeadow>();
            var water = AssetDatabase.LoadAssetAtPath<Material>(VegetationLabBuilder.Generated + "/PainterlyWater.mat");
            ApplyWater(water);
            foreach (var reflection in Object.FindObjectsOfType<PainterlyWaterReflection>()) reflection.resolution = 1024;
            var camera = Object.FindObjectOfType<GrassLabView>().GetComponent<Camera>();
            camera.GetUniversalAdditionalCameraData().requiresColorOption = CameraOverrideOption.On;
            camera.GetUniversalAdditionalCameraData().requiresDepthOption = CameraOverrideOption.On;
            AddTree(meadow, "07 阔叶主样 · 弯干与暖色叶簇", new Vector2(12.6f, 13.8f), 6.2f, 2.5f, 913, 25);
            AddTree(meadow, "08 阔叶副样 · 树冠疏密对照", new Vector2(-7.3f, 10.5f), 4.8f, 2.1f, 521, -40);
            AssetDatabase.SaveAssets();
            EditorSceneManager.SaveScene(meadow.gameObject.scene);
        }

        public static void ApplyWater(Material water)
        {
            if (!water) throw new InvalidOperationException("缺少试验场水材质。");
            water.SetFloat("_Opacity", .70f);
            water.SetFloat("_RefractionStrength", .012f);
            water.SetFloat("_CausticStrength", .22f);
            water.SetFloat("_CausticScale", 1.25f);
            water.SetFloat("_CausticSpeed", .28f);
            water.SetFloat("_CausticDepth", 1.1f);
            water.SetFloat("_ShoreStrength", .72f);
            water.SetFloat("_ShoreWidth", .16f);
            water.SetFloat("_SurfaceStrokeStrength", .58f);
            water.SetFloat("_SurfaceStrokeDensity", .24f);
            water.SetFloat("_ReflectionBrushStrength", .50f);
            water.SetFloat("_ReflectionBrushSize", .75f);
            water.SetFloat("_ReflectionColorSteps", 9);
            water.SetFloat("_ReflectionPosterize", .35f);
            water.SetFloat("_ReflectionStrength", .82f);
            water.SetFloat("_ReflectionDistortion", .009f);
            water.SetFloat("_RippleSpeed", .16f);
            water.SetFloat("_RippleStrength", .55f);
            water.SetFloat("_ViewAlignedStrokes", 1);
            water.SetColor("_RippleColor", new Color(.933f, .941f, .824f, 1));
            water.SetFloat("_DebugDepth", 0);
            EditorUtility.SetDirty(water);
        }

        static void AddTree(GrassMeadow meadow, string name, Vector2 position, float height, float radius, int seed, float yaw)
        {
            // Reapplying the water preset preserves hand-edited tree samples.
            foreach (var root in meadow.gameObject.scene.GetRootGameObjects()) if (root.name == name) return;
            var tree = new GameObject(name).AddComponent<PainterlyTree>();
            tree.height = height; tree.crownRadius = radius; tree.seed = seed;
            tree.trunkRadius = height * .061f;
            tree.transform.SetPositionAndRotation(new Vector3(position.x, meadow.GetGroundHeight(position.x, position.y) - .035f, position.y), Quaternion.Euler(0, yaw, 0));
            string folder = VegetationLabBuilder.Generated + "/TreeSamples";
            Directory.CreateDirectory(folder);
            var mesh = tree.CreateMesh();
            string path = folder + "/Tree_" + seed + ".asset";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing) { EditorUtility.CopySerialized(mesh, existing); Object.DestroyImmediate(mesh); mesh = existing; EditorUtility.SetDirty(mesh); }
            else AssetDatabase.CreateAsset(mesh, path);
            tree.GetComponent<MeshFilter>().sharedMesh = mesh;
            tree.GetComponent<MeshRenderer>().sharedMaterials = new[] { TreeMaterial(folder, false), TreeMaterial(folder, true) };
        }

        static Material TreeMaterial(string folder, bool foliage)
        {
            string path = folder + (foliage ? "/Canopy.mat" : "/Bark.mat");
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material) return material;
            var shader = Shader.Find("Weather House/Painterly Tree");
            if (!shader) throw new InvalidOperationException("请等待树木 Shader 导入完成。");
            material = PainterlyTree.CreateMaterial(foliage);
            material.name = foliage ? "Canopy" : "Bark";
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        public static void ApplyAndVerify() { Apply(); VegetationLabChecks.BuildAndVerify(); }
    }
}
