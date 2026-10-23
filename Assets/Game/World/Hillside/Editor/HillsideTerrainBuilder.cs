using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace WeatherHouse.Hillside.Editor
{
    public static class HillsideTerrainBuilder
    {
        const int HeightResolution = 513;
        const int SurfaceResolution = 512;

        public static Terrain Build(string outputFolder, bool regenerate = false)
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play before creating hillside terrain.");
            string folder = outputFolder.Replace('\\', '/').TrimEnd('/');
            if (!folder.StartsWith("Assets/", StringComparison.Ordinal) || folder.Contains(".."))
                throw new ArgumentException("Use a dedicated folder beneath Assets.", nameof(outputFolder));
            string dataPath = folder + "/HillsideTerrain.asset";
            if (!regenerate && File.Exists(dataPath)) throw new InvalidOperationException("Terrain already exists; preserve edits: " + dataPath);
            HillsideLayout.Validate();
            Directory.CreateDirectory(folder);
            AssetDatabase.Refresh();
            var data = AssetDatabase.LoadAssetAtPath<TerrainData>(dataPath);
            bool createData = !data;
            if (!data) data = new TerrainData();
            data.name = "山林探索 · 可编辑地形";
            data.heightmapResolution = HeightResolution;
            data.alphamapResolution = SurfaceResolution;
            data.baseMapResolution = 1024;
            data.size = new Vector3(HillsideLayout.Size, HillsideLayout.TerrainHeight, HillsideLayout.Size);
            var heights = new float[HeightResolution, HeightResolution];
            for (int z = 0; z < HeightResolution; z++)
                for (int x = 0; x < HeightResolution; x++)
                {
                    float wx = (x / (float)(HeightResolution - 1) - .5f) * HillsideLayout.Size;
                    float wz = (z / (float)(HeightResolution - 1) - .5f) * HillsideLayout.Size;
                    heights[z, x] = (HillsideLayout.Height(wx, wz) - HillsideLayout.TerrainBottom) / HillsideLayout.TerrainHeight;
                }
            data.SetHeights(0, 0, heights);
            Color[] colors =
            {
                new Color(.48f, .54f, .26f), new Color(.72f, .66f, .44f),
                new Color(.48f, .49f, .43f), new Color(.42f, .39f, .27f)
            };
            string[] names = { "Meadow Ground", "Pale Earth Path", "Rock Shoulder", "Pond Bank" };
            var layers = new TerrainLayer[names.Length];
            for (int i = 0; i < layers.Length; i++)
            {
                var texture = new Texture2D(4, 4, TextureFormat.RGB24, false) { name = names[i] + " Color" };
                var pixels = new Color[16];
                for (int p = 0; p < pixels.Length; p++) pixels[p] = colors[i];
                texture.SetPixels(pixels);
                texture.Apply();
                texture = Store(texture, folder + "/" + names[i] + ".asset");
                layers[i] = new TerrainLayer { name = names[i], diffuseTexture = texture, tileSize = Vector2.one * 8f,
                    metallic = 0, smoothness = .02f, normalScale = 0 };
                layers[i] = Store(layers[i], folder + "/" + names[i] + ".terrainlayer");
            }
            data.terrainLayers = layers;
            var alpha = new float[SurfaceResolution, SurfaceResolution, layers.Length];
            for (int z = 0; z < SurfaceResolution; z++)
                for (int x = 0; x < SurfaceResolution; x++)
                {
                    float u = x / (float)(SurfaceResolution - 1), v = z / (float)(SurfaceResolution - 1);
                    float wx = (u - .5f) * HillsideLayout.Size, wz = (v - .5f) * HillsideLayout.Size;
                    float path = 1f - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(-.15f, .6f, HillsideLayout.PathEdgeDistance(wx, wz)));
                    float bank = 0f;
                    foreach (HillsideLayout.WaterBody water in HillsideLayout.Waters)
                        bank = Mathf.Max(bank, 1f - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.5f, 3.1f, water.SignedDistance(wx, wz))));
                    float rock = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(26f, 46f, data.GetSteepness(u, v))) * (1f - path) * (1f - bank);
                    alpha[z, x, 1] = path * (1f - bank);
                    alpha[z, x, 2] = rock;
                    alpha[z, x, 3] = bank;
                    alpha[z, x, 0] = Mathf.Max(0f, 1f - alpha[z, x, 1] - rock - bank);
                }
            data.SetAlphamaps(0, 0, alpha);
            if (createData) AssetDatabase.CreateAsset(data, dataPath);
            var terrainObject = Terrain.CreateTerrainGameObject(data);
            terrainObject.name = "01 山林探索地形 · 五区与干燥路网";
            terrainObject.transform.position = new Vector3(-HillsideLayout.Size * .5f, HillsideLayout.TerrainBottom, -HillsideLayout.Size * .5f);
            var terrain = terrainObject.GetComponent<Terrain>();
            terrain.heightmapPixelError = 3f;
            terrain.basemapDistance = 500f;
            terrain.drawInstanced = true;
            var shader = Shader.Find("Universal Render Pipeline/Terrain/Lit");
            if (!shader) throw new InvalidOperationException("URP Terrain shader is missing.");
            var material = new Material(shader) { name = "Hillside Flat Terrain" };
            material = Store(material, folder + "/Hillside Terrain.mat");
            terrain.materialTemplate = material;
            terrain.Flush();
            string validation = HillsideLayout.Validate((x, z) => terrain.SampleHeight(new Vector3(x, 0, z)) + terrain.transform.position.y);
            Debug.Log(validation, terrain);
            EditorUtility.SetDirty(data);
            AssetDatabase.SaveAssets();
            return terrain;
        }

        static T Store<T>(T created, string path) where T : UnityEngine.Object
        {
            T existing = AssetDatabase.LoadAssetAtPath<T>(path);
            if (!existing) { AssetDatabase.CreateAsset(created, path); return created; }
            EditorUtility.CopySerialized(created, existing);
            UnityEngine.Object.DestroyImmediate(created);
            EditorUtility.SetDirty(existing);
            return existing;
        }
    }
}
