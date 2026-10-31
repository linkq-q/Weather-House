using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace WeatherHouse.Life.Editor
{
    public static class LifeWeatherTaskTextureImport
    {
        [MenuItem("Weather House/Review 20261027/Prepare weather task textures")]
        static void Prepare()
        {
            foreach (string name in new[] { "Paper", "Bookmark", "Meadow", "Day", "Night" })
            {
                string path = "Assets/Game/Life/Resources/HudIcons/WeatherTask/" + name + ".png";
                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (!importer) throw new InvalidOperationException("Import the task UI texture first: " + path);
                importer.textureType = TextureImporterType.Default;
                importer.alphaSource = TextureImporterAlphaSource.FromInput;
                importer.alphaIsTransparency = true;
                importer.sRGBTexture = true;
                importer.isReadable = false;
                importer.mipmapEnabled = false;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.filterMode = FilterMode.Bilinear;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.maxTextureSize = name == "Paper" ? 1024 : 512;
                importer.SaveAndReimport();
                if (!AssetDatabase.LoadAssetAtPath<Texture2D>(path)) throw new InvalidOperationException("Texture import failed: " + path);
            }
            const string result = "LifeGameplayReview/20261027/ui-texture-import.txt";
            Directory.CreateDirectory(Path.GetDirectoryName(result));
            File.WriteAllText(result, "PASS Five transparent task layers imported; no mipmaps, clamp, bilinear, no compression.\n" + DateTime.UtcNow.ToString("O"));
        }
    }
}
