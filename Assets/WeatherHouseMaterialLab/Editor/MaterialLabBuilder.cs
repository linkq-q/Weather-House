using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace WeatherHouse.MaterialLab.Editor
{
    public static class MaterialLabBuilder
    {
        public const string Root = "Assets/WeatherHouseMaterialLab";
        public const string Generated = Root + "/Generated";
        public const string ScenePath = Root + "/OilPaintMaterialLab.unity";
        public const string AtlasPath = Root + "/Textures/OilBrushStamps.png";

        [MenuItem("Weather House/Material Lab/打开油画材质与微像素实验")]
        public static void OpenOrCreate()
        {
            Require(!EditorApplication.isPlayingOrWillChangePlaymode && SceneManager.sceneCount == 1 && !SceneManager.GetActiveScene().isDirty,
                "请先保存当前场景并退出 Play。");
            if (File.Exists(ScenePath)) { EditorSceneManager.OpenScene(ScenePath); return; }
            Directory.CreateDirectory(Generated + "/Materials");
            AssetDatabase.Refresh();
            var oilShader = Shader.Find("Weather House/Oil Paint Surface");
            var postShader = Shader.Find("Hidden/Weather House/Oil Paint Post");
            Require(oilShader && postShader, "需要先完成两个油画 Shader 的导入。");
            UniversalRenderPipelineAsset pipeline = MakePipeline(postShader);
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            RenderSettings.skybox = null;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(.72f, .79f, .81f);
            RenderSettings.ambientEquatorColor = new Color(.43f, .48f, .43f);
            RenderSettings.ambientGroundColor = new Color(.29f, .30f, .23f);
            RenderSettings.fog = false;
            var scope = new GameObject("00 实验专用渲染管线 · Play 后自动恢复").AddComponent<MaterialLabPipelineScope>();
            scope.pipeline = pipeline;
            var sun = new GameObject("01 暖日光").AddComponent<Light>();
            sun.type = LightType.Directional; sun.color = new Color(1, .96f, .84f); sun.intensity = 1.05f;
            sun.shadows = LightShadows.Soft; sun.transform.rotation = Quaternion.Euler(48, -32, 0);
            RenderSettings.sun = sun;
            var camera = new GameObject("02 实验相机").AddComponent<Camera>();
            camera.tag = "MainCamera"; camera.fieldOfView = 48; camera.nearClipPlane = .1f; camera.farClipPlane = 100;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.68f, .81f, .82f);
            camera.allowHDR = true; camera.allowMSAA = false;
            camera.gameObject.AddComponent<AudioListener>();
            var data = camera.gameObject.AddComponent<UniversalAdditionalCameraData>();
            data.SetRenderer(0); data.renderPostProcessing = false; data.antialiasing = AntialiasingMode.None;
            var post = camera.gameObject.AddComponent<OilPaintPostSettings>();
            post.splitScreen = true; post.oilStrength = .65f; post.radius = 3; post.pixelSize = 2; post.pixelStrength = .55f;
            var controls = camera.gameObject.AddComponent<MaterialLabControls>();
            controls.viewCamera = camera; controls.post = post; controls.keyLight = sun; controls.distance = 18.5f; controls.pitch = 27;
            var floor = Lit("Floor", new Color(.58f, .62f, .48f), .08f);
            Primitive("03 平台", PrimitiveType.Cube, new Vector3(0, -.18f, 1.5f), new Vector3(18, .3f, 13), floor);
            var plinth = Lit("Plinth", new Color(.73f, .71f, .60f), .15f);
            var samples = new List<Renderer>();
            string[] titles = { "LIT / REFERENCE", "SOFT COLOR", "DIRECTIONAL", "IMPASTO" };
            Color[] bases = { new Color(.72f, .60f, .35f), new Color(.22f, .39f, .34f) };
            for (int row = 0; row < 2; row++)
            for (int column = 0; column < 4; column++)
            {
                float x = -5.25f + 3.5f * column, z = row == 0 ? 3.6f : -.7f;
                Material material;
                if (column == 0) material = Lit("Lit_" + row, bases[row], .28f);
                else
                {
                    string path = Generated + "/Materials/Oil_" + row + "_" + column + ".mat";
                    material = new Material(oilShader) { name = "Oil_" + row + "_" + column };
                    material.SetColor("_BaseColor", bases[row]);
                    material.SetColor("_ShadowColor", Color.Lerp(bases[row], new Color(.17f, .29f, .27f), .55f));
                    material.SetColor("_HighlightColor", Color.Lerp(bases[row], new Color(1, .90f, .66f), .45f));
                    SetBrushPreset(material, column);
                    AssetDatabase.CreateAsset(material, path);
                }
                Primitive("底座 " + row + "-" + column, PrimitiveType.Cylinder, new Vector3(x, .22f, z), new Vector3(2.65f, .22f, 2.65f), plinth);
                var sphere = Primitive("材质球 " + row + "-" + titles[column], PrimitiveType.Sphere, new Vector3(x, 1.6f, z), Vector3.one * 2.25f, material);
                if (column > 0) samples.Add(sphere);
                if (row == 1) Label(titles[column], new Vector3(x, .50f, z - 1.45f));
            }
            controls.paintedSamples = samples.ToArray();
            controls.PlaceCamera();
            // Thin geometry and alternating colors make the subtle pixel-size control observable.
            for (int i = 0; i < 18; i++)
                Primitive("像素边缘标尺 " + i, PrimitiveType.Cube, new Vector3(-6.4f + i * .75f, .05f, -3.1f),
                    new Vector3(.42f, .16f, .42f), i % 2 == 0 ? floor : plinth).transform.rotation = Quaternion.Euler(0, 25, 0);
            AssetDatabase.SaveAssets();
            Require(EditorSceneManager.SaveScene(scene, ScenePath), "实验场景保存失败。");
            Selection.activeGameObject = camera.gameObject;
        }

        [MenuItem("Weather House/Material Lab/应用破边油画笔触预设")]
        public static void ApplyBrushRevision()
        {
            OpenOrCreate();
            var importer = AssetImporter.GetAtPath(AtlasPath) as TextureImporter;
            Require(importer, "笔触图集尚未导入。");
            importer.sRGBTexture = false; importer.mipmapEnabled = true; importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Trilinear; importer.maxTextureSize = 2048;
            importer.npotScale = TextureImporterNPOTScale.ToNearest;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            importer.SaveAndReimport();
            var controls = Object.FindObjectOfType<MaterialLabControls>();
            for (int i = 0; i < controls.paintedSamples.Length; i++)
            {
                Material material = controls.paintedSamples[i].sharedMaterial;
                Require(AssetDatabase.GetAssetPath(material).StartsWith(Generated + "/Materials/Oil_"), "保留不属于本实验的材质。");
                SetBrushPreset(material, i % 3 + 1); EditorUtility.SetDirty(material);
            }
            foreach (var label in Object.FindObjectsOfType<TextMesh>())
                if (label.transform.position.z > 1) Object.DestroyImmediate(label.gameObject);
            AssetDatabase.SaveAssets();
            EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
        }

        [MenuItem("Weather House/Material Lab/补齐笔触颜色与明暗过渡参数")]
        public static void ApplySurfaceRevision()
        {
            Require(!EditorApplication.isPlayingOrWillChangePlaymode, "请先退出 Play，再补齐持久材质参数。");
            var materials = new List<Material>();
            for (int row = 0; row < 2; row++)
            for (int column = 1; column <= 3; column++)
            {
                string path = Generated + "/Materials/Oil_" + row + "_" + column + ".mat";
                Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
                Require(material && material.shader.name == "Weather House/Oil Paint Surface", "需要原实验材质：" + path);
                foreach (string property in new[] { "_StrokeColor", "_StrokeDensity", "_ShadowEdgeColor", "_ShadowEdgeWidth", "_ShadowEdgeStrength", "_ShadowFill" })
                    Require(material.HasProperty(property), "请先导入新的油画 shader：" + property);
                materials.Add(material);
            }
            foreach (Material material in materials)
            {
                if (!AddMissingSurfaceProperties(material)) continue;
                EditorUtility.SetDirty(material);
                AssetDatabase.SaveAssetIfDirty(material);
            }
        }

        static bool AddMissingSurfaceProperties(Material material)
        {
            Color stroke = Color.Lerp(material.GetColor("_ShadowColor"), material.GetColor("_HighlightColor"), .72f);
            bool changed = AddColor(material, "_StrokeColor", stroke);
            changed |= AddFloat(material, "_StrokeDensity", 1.25f);
            changed |= AddColor(material, "_ShadowEdgeColor", new Color(.18f, .42f, .46f, 1));
            changed |= AddFloat(material, "_ShadowEdgeWidth", .14f);
            changed |= AddFloat(material, "_ShadowEdgeStrength", .55f);
            changed |= AddFloat(material, "_ShadowFill", .22f);
            return changed;
        }
        static bool AddFloat(Material material, string property, float value)
        {
            if (!material.HasProperty(property) || HasSavedProperty(material, "m_Floats", property)) return false;
            material.SetFloat(property, value); return true;
        }
        static bool AddColor(Material material, string property, Color value)
        {
            if (!material.HasProperty(property) || HasSavedProperty(material, "m_Colors", property)) return false;
            material.SetColor(property, value); return true;
        }
        static bool HasSavedProperty(Material material, string array, string property)
        {
            // HasProperty also sees new shader defaults. Inspect serialized values to retain user overrides.
            using (var serialized = new SerializedObject(material))
            {
                SerializedProperty values = serialized.FindProperty("m_SavedProperties." + array);
                for (int i = 0; i < values.arraySize; i++)
                    if (values.GetArrayElementAtIndex(i).FindPropertyRelative("first").stringValue == property) return true;
                return false;
            }
        }

        static void SetBrushPreset(Material material, int column)
        {
            material.SetFloat("_BrushScale", column == 1 ? 7 : column == 2 ? 9 : 6.5f);
            material.SetFloat("_BrushStrength", column == 1 ? .42f : column == 2 ? .68f : .72f);
            material.SetFloat("_StrokeAspect", column == 1 ? 1.15f : column == 2 ? 1.35f : 1.2f);
            material.SetFloat("_StrokeAngle", column == 1 ? 10 : column == 2 ? 25 : -15);
            material.SetFloat("_StrokeScatter", column == 2 ? 55 : 70);
            material.SetFloat("_ColorSteps", column == 1 ? 12 : 10);
            material.SetFloat("_Impasto", column == 1 ? .18f : column == 2 ? .35f : .65f);
            material.SetFloat("_Smoothness", column == 1 ? .22f : column == 2 ? .26f : .38f);
            var atlas = AssetDatabase.LoadAssetAtPath<Texture2D>(AtlasPath);
            material.SetTexture("_BrushAtlas", atlas); material.SetFloat("_UseBrushAtlas", atlas ? 1 : 0);
            material.DisableKeyword("_USEBRUSHATLAS_ON");
            AddMissingSurfaceProperties(material);
        }

        static UniversalRenderPipelineAsset MakePipeline(Shader shader)
        {
            string rendererPath = Generated + "/MaterialLabRenderer.asset", pipelinePath = Generated + "/MaterialLabPipeline.asset";
            if (!File.Exists(rendererPath)) Require(AssetDatabase.CopyAsset("Assets/Settings/URP-HighFidelity-Renderer.asset", rendererPath), "复制 renderer 失败。");
            var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(rendererPath);
            renderer.rendererFeatures.Clear();
            var feature = ScriptableObject.CreateInstance<OilPaintPostFeature>();
            feature.name = "油画与微像素实验"; feature.shader = shader; feature.SetActive(true);
            AssetDatabase.AddObjectToAsset(feature, renderer); renderer.rendererFeatures.Add(feature);
            renderer.intermediateTextureMode = IntermediateTextureMode.Always;
            renderer.SetDirty(); EditorUtility.SetDirty(renderer);
            if (!File.Exists(pipelinePath)) Require(AssetDatabase.CopyAsset("Assets/Settings/URP-HighFidelity.asset", pipelinePath), "复制 URP 失败。");
            var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(pipelinePath);
            var serialized = new SerializedObject(pipeline);
            var list = serialized.FindProperty("m_RendererDataList"); list.arraySize = 1; list.GetArrayElementAtIndex(0).objectReferenceValue = renderer;
            serialized.FindProperty("m_DefaultRendererIndex").intValue = 0;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            pipeline.msaaSampleCount = 1; pipeline.renderScale = 1; pipeline.shadowDistance = 45;
            EditorUtility.SetDirty(pipeline);
            return pipeline;
        }
        static Material Lit(string name, Color color, float smoothness)
        {
            var material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name };
            material.SetColor("_BaseColor", color); material.SetFloat("_Smoothness", smoothness);
            AssetDatabase.CreateAsset(material, Generated + "/Materials/" + name + ".mat");
            return material;
        }
        static Renderer Primitive(string name, PrimitiveType type, Vector3 position, Vector3 scale, Material material)
        {
            var go = GameObject.CreatePrimitive(type); go.name = name; go.transform.position = position; go.transform.localScale = scale;
            var collider = go.GetComponent<Collider>(); if (collider) Object.DestroyImmediate(collider);
            var renderer = go.GetComponent<Renderer>(); renderer.sharedMaterial = material;
            return renderer;
        }
        static void Label(string title, Vector3 position)
        {
            var label = new GameObject(title).AddComponent<TextMesh>();
            label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            label.GetComponent<MeshRenderer>().sharedMaterial = label.font.material;
            label.text = title; label.fontSize = 64; label.characterSize = .055f; label.anchor = TextAnchor.MiddleCenter;
            label.color = new Color(.16f, .23f, .20f); label.transform.position = position;
            label.transform.rotation = Quaternion.Euler(65, 0, 0);
        }
        static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    }
}
