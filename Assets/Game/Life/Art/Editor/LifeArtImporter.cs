using UnityEditor;
using UnityEngine;
using System.IO;

namespace WeatherHouse.Life.Editor
{
    // This importer only owns original life props and clips in this resource folder.
    public sealed class LifeArtImporter : AssetPostprocessor
    {
        private bool IsLifeModel => assetPath.StartsWith("Assets/Game/Life/Art/Resources/LifeAssets/") && assetPath.EndsWith(".fbx");
        private bool IsLifeAnimation => IsLifeModel && assetPath.Contains("/Animations/");
        private bool IsJournal => assetPath.EndsWith("/LifeAssets/JournalBook.fbx");

        private void OnPreprocessModel()
        {
            if (!IsLifeModel) return;
            var importer = (ModelImporter)assetImporter;
            importer.globalScale = 1f;
            importer.useFileScale = true;
            importer.importAnimation = IsLifeAnimation || IsJournal;
            importer.addCollider = false;
            importer.materialImportMode = IsLifeAnimation ? ModelImporterMaterialImportMode.None : ModelImporterMaterialImportMode.ImportStandard;
            if (IsJournal) importer.importTangents = ModelImporterTangents.CalculateMikk;
            if (IsLifeAnimation || IsJournal)
            {
                importer.animationType = ModelImporterAnimationType.Generic;
                importer.animationCompression = ModelImporterAnimationCompression.Off;
                importer.optimizeGameObjects = false;
            }
        }

        private void OnPreprocessAnimation()
        {
            if (!IsLifeAnimation && !IsJournal) return;
            var importer = (ModelImporter)assetImporter;
            var clips = importer.defaultClipAnimations;
            foreach (var clip in clips)
            {
                clip.name = IsJournal ? clip.takeName.Substring(clip.takeName.LastIndexOf('|') + 1) : Path.GetFileNameWithoutExtension(assetPath);
                clip.loopTime = assetPath.EndsWith("RestLoop.fbx") || assetPath.EndsWith("Idle.fbx");
                clip.keepOriginalPositionY = true;
                clip.keepOriginalPositionXZ = true;
                clip.keepOriginalOrientation = true;
            }
            importer.clipAnimations = clips;
        }

        private void OnPostprocessMaterial(Material material)
        {
            if (!IsLifeModel) return;
            Color color = material.color;
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (!shader) return;
            material.shader = shader;
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Smoothness", material.name == "Water" ? .75f : .12f);
            material.SetFloat("_Metallic", 0f);
            if (IsJournal) ApplyJournalTexture(material);
        }

        private static void ApplyJournalTexture(Material material)
        {
            string texture = null;
            Vector2 offset = Vector2.zero;
            Vector2 scale = Vector2.one;
            switch (material.name)
            {
                case "JournalCloth": texture = "CoverAged"; offset = new Vector2(.20f, .20f); scale = new Vector2(.15f, .15f); break;
                case "JournalCover": texture = "CoverAged"; break;
                case "JournalPageLeft": texture = "PaperAged"; break;
                case "JournalPageRight": texture = "PaperAged"; break;
                case "JournalGutter": texture = "PaperAged"; offset = new Vector2(.35f, .10f); scale = new Vector2(.1f, .8f); break;
            }
            if (texture == null) return;
            var source = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Game/Life/Art/Resources/LifeAssets/JournalTextures/" + texture + ".png");
            material.SetColor("_BaseColor", Color.white);
            material.SetTexture("_BaseMap", source);
            material.SetTextureOffset("_BaseMap", offset);
            material.SetTextureScale("_BaseMap", scale);
            if (texture == "PaperAged" || texture == "CoverAged")
            {
                string folder = "Assets/Game/Life/Art/Resources/LifeAssets/JournalTextures/";
                material.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(folder + texture + "_Normal.png"));
                material.SetTexture("_OcclusionMap", AssetDatabase.LoadAssetAtPath<Texture2D>(folder + texture + "_AO.png"));
                material.SetTextureOffset("_BumpMap", offset); material.SetTextureScale("_BumpMap", scale);
                material.SetTextureOffset("_OcclusionMap", offset); material.SetTextureScale("_OcclusionMap", scale);
                material.SetFloat("_BumpScale", texture == "PaperAged" ? .35f : .65f);
                material.SetFloat("_OcclusionStrength", texture == "PaperAged" ? .28f : .45f);
                material.EnableKeyword("_NORMALMAP"); material.EnableKeyword("_OCCLUSIONMAP");
            }
        }

        private void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith("Assets/Game/Life/Art/Resources/LifeAssets/JournalTextures/")) return;
            var importer = (TextureImporter)assetImporter;
            bool normal = assetPath.EndsWith("_Normal.png"), ao = assetPath.EndsWith("_AO.png");
            importer.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            importer.convertToNormalmap = false;
            importer.sRGBTexture = !normal && !ao;
            importer.maxTextureSize = 4096;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.alphaIsTransparency = !normal && !ao;
            if (normal || ao) { importer.mipmapEnabled = true; importer.filterMode = FilterMode.Trilinear; importer.anisoLevel = 4; }
        }
    }
}
