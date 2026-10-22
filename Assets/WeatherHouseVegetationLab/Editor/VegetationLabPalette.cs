using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace WeatherHouse.Vegetation.Editor
{
    public static class VegetationLabPalette
    {
        [MenuItem("Weather House/Vegetation Lab/Apply Day Concept Style")]
        public static void Apply()
        {
            var meadow = Object.FindObjectOfType<GrassMeadow>();
            if (!meadow || meadow.gameObject.scene.path != VegetationLabBuilder.ScenePath)
                throw new System.InvalidOperationException("请先打开独立草地试验场。");
            VegetationLabBuilder.PolishBackdrop(meadow);
            Color[] roots = { new Color(.34f,.44f,.14f), new Color(.32f,.42f,.13f), new Color(.30f,.41f,.22f) };
            Color[] tips = { new Color(.86f,.83f,.38f), new Color(.81f,.81f,.33f), new Color(.65f,.73f,.36f) };
            meadow.species[0].height=.30f; meadow.species[0].width=.036f; meadow.species[0].bladeCount=12;
            meadow.species[0].spread=.16f; meadow.species[0].bend=.50f; meadow.species[0].heightVariation=.35f;
            meadow.species[1].height=.64f; meadow.species[1].width=.052f; meadow.species[1].bladeCount=11;
            meadow.species[1].spread=.21f; meadow.species[1].bend=.50f; meadow.species[1].heightVariation=.40f;
            meadow.totalClumps=28000; meadow.weights=new[]{78f,20f,2f}; meadow.upNormals=1;
            for (int i = 0; i < 3; i++)
            {
                meadow.species[i].baseColor = roots[i]; meadow.species[i].tipColor = tips[i];
                EditorUtility.SetDirty(meadow.species[i]);
                meadow.materials[i].SetColor("_ShadowTint", new Color(.28f,.43f,.40f));
                meadow.materials[i].SetFloat("_ShadowStrength", .85f);
                meadow.materials[i].SetFloat("_PatchStrength", .16f);
                EditorUtility.SetDirty(meadow.materials[i]);
            }
            meadow.ApplyMaterials();
            var ground = AssetDatabase.LoadAssetAtPath<Material>(VegetationLabBuilder.Generated + "/PainterlyGround.mat");
            ground.SetColor("_BaseColor", new Color(.49f,.55f,.24f));
            ground.SetColor("_PatchColor", new Color(.81f,.75f,.38f));
            ground.SetColor("_ShoreColor", new Color(.71f,.67f,.55f));
            EditorUtility.SetDirty(ground);
            var water = AssetDatabase.LoadAssetAtPath<Material>(VegetationLabBuilder.Generated + "/PainterlyWater.mat");
            water.SetColor("_ShallowColor", new Color(.66f,.79f,.75f));
            water.SetColor("_DeepColor", new Color(.55f,.75f,.74f));
            water.SetColor("_ReflectionTint", new Color(.75f,.86f,.82f));
            water.SetColor("_RippleColor", new Color(.96f,.94f,.82f));
            water.SetFloat("_ReflectionStrength", .82f);
            water.SetFloat("_ReflectionDistortion", .005f);
            water.SetVector("_BrushDirection", new Vector4(.829f,-.559f,0,0));
            water.SetFloat("_DepthDistance", 1.2f);
            water.SetFloat("_RippleStrength", .36f);
            water.SetFloat("_ViewAlignedStrokes", 0);
            var waterBrushAtlas = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/WeatherHouseMaterialLab/Textures/OilBrushStamps.png");
            water.SetTexture("_WaterBrushAtlas", waterBrushAtlas);
            water.SetFloat("_BrushViewResponse", .85f);
            water.SetFloat("_ReflectionBrushSize", 1.35f);
            water.SetFloat("_SurfaceStrokeStrength", .84f);
            water.SetFloat("_SmallStrokeScale", 3.8f);
            water.SetFloat("_SmallStrokeWeight", .75f);
            water.SetFloat("_BrushGradientStrength", .65f);
            water.SetColor("_ShadowPigmentColor", new Color(.035f, .40f, .43f, 1));
            water.SetFloat("_ShadowPigmentBlend", .75f);
            water.SetFloat("_UseWaterBrushAtlas", waterBrushAtlas ? 1 : 0);
            EditorUtility.SetDirty(water);
            foreach (var renderer in Object.FindObjectsOfType<MeshRenderer>())
                if (renderer.sharedMaterial == water && !renderer.GetComponent<PainterlyWaterReflection>())
                    renderer.gameObject.AddComponent<PainterlyWaterReflection>();
            foreach (var reflection in Object.FindObjectsOfType<PainterlyWaterReflection>()) reflection.resolution=1024;
            RenderSettings.sun.color = new Color(1,.96f,.86f);
            RenderSettings.sun.intensity = 1.15f;
            RenderSettings.ambientSkyColor = new Color(.63f,.72f,.75f);
            RenderSettings.ambientEquatorColor = new Color(.48f,.54f,.49f);
            RenderSettings.ambientGroundColor = new Color(.28f,.32f,.26f);
            RenderSettings.fogColor = Hex("#9BB7B7");
            RenderSettings.fogStartDistance = 52;
            RenderSettings.fogEndDistance = 110;
            string skyPath=VegetationLabBuilder.Generated+"/PainterlyDaySky.mat";
            var sky = AssetDatabase.LoadAssetAtPath<Material>(skyPath);
            if (!sky) { sky=new Material(Shader.Find("Weather House/Painterly Day Sky")); AssetDatabase.CreateAsset(sky,skyPath); }
            sky.SetVector("_SunDirection", -RenderSettings.sun.transform.forward);
            sky.SetFloat("_CloudCoverage", .44f);
            RenderSettings.skybox=sky;
            EditorUtility.SetDirty(sky);
            meadow.Rebuild();
            Object.FindObjectOfType<GrassLabView>().Overview();
            AssetDatabase.SaveAssets();
            EditorSceneManager.SaveScene(meadow.gameObject.scene);
        }
        static Color Hex(string value) { ColorUtility.TryParseHtmlString(value, out var color); return color; }
        public static void ApplyAndVerify()
        {
            VegetationLabBuilder.Build();
            Apply();
            VegetationLabChecks.BuildAndVerify();
        }
    }
}
