using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using WeatherHouse.MaterialLab;

namespace WeatherHouse.DrawerPrototype.Editor
{
    public static class DrawerLookAssets
    {
        public const string Folder = DrawerPrototypeEditor.Root+"/Look";

        public static void Bind(DrawerPrototypeController controller)
        {
            Directory.CreateDirectory(Folder);
            string settingsPath = Folder+"/DrawerLook.asset";
            var settings = AssetDatabase.LoadAssetAtPath<DrawerLookSettings>(settingsPath);
            if (!settings)
            {
                settings = ScriptableObject.CreateInstance<DrawerLookSettings>(); settings.name = "DrawerLook";
                settings.grass = new Material[3];
                for (int i = 0; i < 3; i++) settings.grass[i] = CopyMaterial(controller.meadowMaterials[i],new[] { "ShortGrass","TallGrass","Daisies" }[i]);
                settings.ground = CopyMaterial(controller.meadowGround,"Ground");
                settings.orangeFlowers = CopyMaterial(controller.meadowPlants[0].GetComponent<MeshRenderer>().sharedMaterial,"OrangeFlowers");
                settings.paper = OilMaterial("Paper",controller.paperGrain,new Color(.76f,.76f,.71f),false);
                settings.paperFold = OilMaterial("PaperFold",controller.paperGrain,new Color(.73f,.73f,.68f),false);
                Texture2D cloth = Resources.Load<Texture2D>("DrawerPrototype/Velvet");
                settings.magicBook = OilMaterial("MagicBook",cloth,new Color(.48f,.53f,.35f),true);
                settings.dictionary = OilMaterial("Dictionary",cloth,new Color(.44f,.49f,.34f),true);
                AssetDatabase.CreateAsset(settings,settingsPath);
            }
            controller.lookDefaults = settings;
            controller.lookPipeline = Pipeline();
        }

        static Material CopyMaterial(Material source, string name)
        {
            string path = Folder+"/"+name+".mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path); if (existing) return existing;
            if (!source) throw new InvalidOperationException("Missing source material: "+name);
            var material = new Material(source) { name = name }; AssetDatabase.CreateAsset(material,path); return material;
        }

        static Material OilMaterial(string name, Texture texture, Color pigment, bool cloth)
        {
            string path = Folder+"/"+name+".mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path); if (existing) return existing;
            var source = AssetDatabase.LoadAssetAtPath<Material>("Assets/Experiments/Materials/Generated/Materials/Oil_0_1.mat");
            if (!source) throw new InvalidOperationException("The existing Oil Paint Surface preset is missing.");
            var material = new Material(source) { name = name };
            material.SetTexture("_BaseMap",texture); material.SetFloat("_TextureInfluence",1);
            material.SetColor("_BaseColor",pigment);
            material.SetColor("_HighlightColor",Color.Lerp(pigment,new Color(1,.97f,.86f),.18f));
            material.SetColor("_ShadowColor",pigment*.72f);
            material.SetColor("_StrokeColor",Color.Lerp(pigment,new Color(.72f,.66f,.49f),.18f));
            material.SetColor("_ShadowEdgeColor",Color.Lerp(pigment,new Color(.46f,.40f,.29f),.35f));
            material.SetFloat("_BrushStrength",cloth ? .38f : .24f); material.SetFloat("_BrushScale",cloth ? 12 : 9);
            material.SetFloat("_Impasto",cloth ? .16f : .08f); material.SetFloat("_Smoothness",cloth ? .14f : .12f);
            material.SetFloat("_ShadowEdgeStrength",.12f); material.SetFloat("_ShadowFill",.18f);
            AssetDatabase.CreateAsset(material,path); return material;
        }

        static UniversalRenderPipelineAsset Pipeline()
        {
            string rendererPath = Folder+"/DrawerRenderer.asset", pipelinePath = Folder+"/DrawerPipeline.asset";
            if (!File.Exists(rendererPath))
            {
                if (!AssetDatabase.CopyAsset("Assets/Settings/URP-HighFidelity-Renderer.asset",rendererPath)) throw new IOException("Could not copy the drawer renderer.");
                var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(rendererPath);
                var feature = ScriptableObject.CreateInstance<OilPaintPostFeature>(); feature.name = "Drawer oil paint preview";
                feature.shader = Shader.Find("Hidden/Weather House/Oil Paint Post");
                AssetDatabase.AddObjectToAsset(feature,renderer); renderer.rendererFeatures.Add(feature);
                renderer.intermediateTextureMode = IntermediateTextureMode.Always;
                renderer.SetDirty(); EditorUtility.SetDirty(renderer); AssetDatabase.SaveAssetIfDirty(renderer);
            }
            if (!File.Exists(pipelinePath))
            {
                if (!AssetDatabase.CopyAsset("Assets/Settings/URP-HighFidelity.asset",pipelinePath)) throw new IOException("Could not copy the drawer pipeline.");
                var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(pipelinePath);
                var serialized = new SerializedObject(pipeline);
                var renderers = serialized.FindProperty("m_RendererDataList"); renderers.arraySize = 1;
                renderers.GetArrayElementAtIndex(0).objectReferenceValue = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(rendererPath);
                serialized.FindProperty("m_DefaultRendererIndex").intValue = 0; serialized.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(pipeline); AssetDatabase.SaveAssetIfDirty(pipeline);
            }
            return AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(pipelinePath);
        }

        public static void CopyLook(DrawerLookSettings source, DrawerLookSettings destination)
        {
            Material[] targets = destination.Materials, originals = source.Materials;
            string label = destination.name;
            EditorUtility.CopySerialized(source,destination); destination.SetMaterials(targets); destination.name = label;
            for (int i = 0; i < targets.Length; i++) targets[i].CopyPropertiesFromMaterial(originals[i]);
        }

        public static void Save(DrawerPrototypeController controller)
        {
            var target = controller.lookDefaults;
            if (!AssetDatabase.GetAssetPath(target).StartsWith(Folder+"/",StringComparison.Ordinal)) throw new InvalidOperationException("Only drawer appearance assets may be saved here.");
            foreach (Material material in target.Materials)
                if (!AssetDatabase.GetAssetPath(material).StartsWith(Folder+"/",StringComparison.Ordinal)) throw new InvalidOperationException("Preserve non-drawer material: "+material.name);
            Undo.RecordObject(target,"保存抽屉画面参数"); Undo.RecordObjects(target.Materials,"保存抽屉材质参数");
            CopyLook(controller.RuntimeLook,target);
            foreach (Material material in target.Materials) { EditorUtility.SetDirty(material); AssetDatabase.SaveAssetIfDirty(material); }
            EditorUtility.SetDirty(target); AssetDatabase.SaveAssetIfDirty(target);
        }
    }
}
