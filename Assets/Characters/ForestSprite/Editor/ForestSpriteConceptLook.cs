using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace ForestSpriteCharacter.Editor
{
    public static class ForestSpriteConceptLook
    {
        const string Root = "Assets/Characters/ForestSprite";
        const string Review = "VegetationLabReview/Concept20261022";

        public static void Apply()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Apply textures outside Play.");
            Directory.CreateDirectory(Review + "/before");
            ApplyTexture("Hat", "Hat_ConceptIvory");
            ApplyTexture("Cape", "Cape_ConceptMoon");
        }

        static void ApplyTexture(string region, string textureName)
        {
            string path = Root + "/Textures/" + textureName + ".png";
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (!importer) throw new InvalidOperationException("Import the concept texture first: " + path);
            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = true;
            importer.alphaSource = TextureImporterAlphaSource.None;
            importer.mipmapEnabled = true;
            importer.maxTextureSize = 2048;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            importer.SaveAndReimport();
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            string materialPath = Root + "/Generated/" + region + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (!material || !texture) throw new InvalidOperationException("Missing concept material or texture: " + region);
            string backup = Review + "/before/" + region + ".mat";
            if (!File.Exists(backup)) File.Copy(materialPath, backup);
            Undo.RecordObject(material, "Apply concept character palette");
            material.SetTexture("_BaseMap", texture);
            material.SetTexture("_MainTex", texture);
            material.SetColor("_BaseColor", Color.white);
            EditorUtility.SetDirty(material);
            AssetDatabase.SaveAssetIfDirty(material);
        }

        public static void CaptureViews(ForestSpriteDemoController player, Camera camera)
        {
            if (!EditorApplication.isPlaying) throw new InvalidOperationException("Capture the actual character in Play.");
            var renderers = player.visual.GetComponentsInChildren<Renderer>().Where(r => r.enabled).ToArray();
            var bounds = renderers[0].bounds;
            foreach (var renderer in renderers.Skip(1)) bounds.Encapsulate(renderer.bounds);
            var position = camera.transform.position;
            var rotation = camera.transform.rotation;
            float fov = camera.fieldOfView, aspect = camera.aspect;
            var target = camera.targetTexture;
            var active = RenderTexture.active;
            var rt = new RenderTexture(1000, 1000, 24);
            var image = new Texture2D(1000, 1000, TextureFormat.RGB24, false);
            try
            {
                Directory.CreateDirectory(Review);
                camera.fieldOfView = 32; camera.aspect = 1; camera.targetTexture = rt;
                float distance = Mathf.Max(bounds.size.y, bounds.size.x) * 2.2f;
                var directions = new[] { player.transform.forward, player.transform.right, -player.transform.forward };
                var names = new[] { "front", "side", "back" };
                for (int i = 0; i < directions.Length; i++)
                {
                    Vector3 offset = (directions[i] + Vector3.up * .12f).normalized * distance;
                    camera.transform.SetPositionAndRotation(bounds.center + offset, Quaternion.LookRotation(-offset));
                    camera.Render(); RenderTexture.active = rt;
                    image.ReadPixels(new Rect(0, 0, 1000, 1000), 0, 0); image.Apply();
                    File.WriteAllBytes(Review + "/character-" + names[i] + ".png", image.EncodeToPNG());
                }
            }
            finally
            {
                camera.transform.SetPositionAndRotation(position, rotation);
                camera.fieldOfView = fov; camera.aspect = aspect; camera.targetTexture = target;
                RenderTexture.active = active;
                rt.Release(); UnityEngine.Object.DestroyImmediate(rt); UnityEngine.Object.DestroyImmediate(image);
            }
        }
    }
}
