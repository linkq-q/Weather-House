using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace WeatherHouse.WatersideV2.Editor
{
    public static class WatersideTerrainBuilder
    {
        const int HeightResolution = 513;
        const int SurfaceResolution = 512;

        public static Terrain Build(string outputFolder)
        { return Build(outputFolder, false); }

        public static Terrain Build(string outputFolder, bool rebuild)
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play before building Waterside V2.");
            string folder = outputFolder.Replace('\\', '/').TrimEnd('/');
            if (!folder.StartsWith("Assets/", StringComparison.Ordinal) || folder.Contains(".."))
                throw new ArgumentException("Use a dedicated Assets folder.", nameof(outputFolder));
            string path = folder + "/WatersideTerrain.asset";
            if (File.Exists(path) && (!rebuild || !folder.StartsWith("Assets/Game/World/WatersideV2/", StringComparison.Ordinal)))
                throw new InvalidOperationException("Use an explicit rebuild only for this task's WatersideV2 assets: " + path);
            WatersideLayout.Validate();
            Directory.CreateDirectory(folder);
            AssetDatabase.Refresh();
            var data = AssetDatabase.LoadAssetAtPath<TerrainData>(path);
            bool createData = !data;
            if (!data) data = new TerrainData();
            data.name = "水岸探索 · 连通溪谷与台地";
            data.heightmapResolution = HeightResolution;
            data.alphamapResolution = SurfaceResolution;
            data.baseMapResolution = 512;
            data.size = new Vector3(WatersideLayout.Size, WatersideLayout.TerrainHeight, WatersideLayout.Size);
            var heights = new float[HeightResolution, HeightResolution];
            for (int z = 0; z < HeightResolution; z++)
                for (int x = 0; x < HeightResolution; x++)
                {
                    float wx = (x / (float)(HeightResolution - 1) - .5f) * WatersideLayout.Size;
                    float wz = (z / (float)(HeightResolution - 1) - .5f) * WatersideLayout.Size;
                    heights[z, x] = (WatersideLayout.Height(wx, wz) - WatersideLayout.TerrainBottom) / WatersideLayout.TerrainHeight;
                }
            data.SetHeights(0, 0, heights);
            string[] names = { "Meadow", "Path", "Warm Rock", "Sand" };
            Color[] colors =
            {
                new Color(.48f,.54f,.26f), new Color(.72f,.67f,.46f),
                new Color(.59f,.59f,.49f), new Color(.75f,.72f,.53f)
            };
            Material meadow = AssetDatabase.LoadAssetAtPath<Material>("Assets/Experiments/Vegetation/Generated/ShortGrass.mat");
            if (meadow && meadow.HasProperty("_MeadowBaseColor") && meadow.HasProperty("_MeadowPatchColor"))
                colors[0] = Color.Lerp(meadow.GetColor("_MeadowBaseColor"), meadow.GetColor("_MeadowPatchColor"), .4f);
            var layers = new TerrainLayer[names.Length];
            for (int i = 0; i < layers.Length; i++)
            {
                if (i == 1)
                {
                    const string mudPath = "Assets/HIGHLANDS - Stylized Environment/Terrain Layers/Mud_1.terrainlayer";
                    TerrainLayer source = AssetDatabase.LoadAssetAtPath<TerrainLayer>(mudPath);
                    if (!source || !source.diffuseTexture || !source.normalMapTexture)
                        throw new FileNotFoundException("The original HIGHLANDS mud and normal are required.", mudPath);
                    var mud = UnityEngine.Object.Instantiate(source);
                    mud.name = "Waterside Path · Original HIGHLANDS Mud";
                    mud.tileSize = Vector2.one * 3;
                    mud.normalScale = .35f;
                    mud.metallic = 0;
                    layers[i] = Store(mud, folder + "/Path.terrainlayer");
                    continue;
                }
                // Terrain/Lit uses diffuse alpha as smoothness, so pure colors must have no alpha.
                var texture = new Texture2D(4, 4, TextureFormat.RGB24, false) { name = "Waterside " + names[i] };
                var pixels = new Color[16];
                for (int p = 0; p < pixels.Length; p++) pixels[p] = colors[i];
                texture.SetPixels(pixels); texture.Apply();
                texture = Store(texture, folder + "/" + names[i] + " Color.asset");
                layers[i] = Store(new TerrainLayer
                {
                    name = "Waterside " + names[i], diffuseTexture = texture,
                    tileSize = Vector2.one * 8, metallic = 0, smoothness = .015f, normalScale = 0
                }, folder + "/" + names[i] + ".terrainlayer");
            }
            data.terrainLayers = layers;
            var alpha = new float[SurfaceResolution, SurfaceResolution, layers.Length];
            for (int z = 0; z < SurfaceResolution; z++)
                for (int x = 0; x < SurfaceResolution; x++)
                {
                    float u = x / (float)(SurfaceResolution - 1), v = z / (float)(SurfaceResolution - 1);
                    float wx = (u - .5f) * WatersideLayout.Size, wz = (v - .5f) * WatersideLayout.Size;
                    float shore = WatersideLayout.WaterSignedDistance(wx, wz);
                    // A thin, uneven foot-worn trace with meadow showing through even at its center.
                    // The graded walkable corridor is independent, so this does not narrow traversal.
                    float patch = .5f + .27f * Mathf.Sin(wx * .71f + Mathf.Sin(wz * .37f)) +
                        .20f * Mathf.Cos(wz * .91f - wx * .23f);
                    float edgeBreak = .16f * Mathf.Sin(wx * 1.17f + wz * .73f) +
                        .10f * Mathf.Cos(wz * 1.43f - wx * .53f);
                    float edge = WatersideLayout.PathEdgeDistance(wx, wz) + edgeBreak;
                    float wear = Mathf.Lerp(.75f, .92f, Smooth(.1f, .9f, patch));
                    float pathWeight = shore > .1f ? (1 - Smooth(-.2f, .65f, edge)) * wear : 0;
                    if (shore > .1f)
                    {
                        float courtyard = WatersideLayout.CourtyardWear(wx, wz) * Mathf.Lerp(.65f,.82f,Smooth(.1f,.9f,patch));
                        pathWeight = Mathf.Max(pathWeight, courtyard);
                    }
                    float sand = (1 - Smooth(1.3f, 4.5f, Mathf.Abs(shore))) * (1 - pathWeight) * .65f;
                    float slope = data.GetSteepness(u, v);
                    float rock = Smooth(34, 58, slope) * (1 - pathWeight - sand) * .7f;
                    alpha[z, x, 1] = pathWeight;
                    alpha[z, x, 2] = rock;
                    alpha[z, x, 3] = sand;
                    alpha[z, x, 0] = Mathf.Max(0, 1 - pathWeight - rock - sand);
                }
            data.SetAlphamaps(0, 0, alpha);
            if (createData) AssetDatabase.CreateAsset(data, path);
            GameObject go = Terrain.CreateTerrainGameObject(data);
            go.name = "01 水岸地形 · 双山界与连通溪谷";
            go.transform.position = new Vector3(-WatersideLayout.Size * .5f, WatersideLayout.TerrainBottom, -WatersideLayout.Size * .5f);
            var terrain = go.GetComponent<Terrain>();
            terrain.heightmapPixelError = 3;
            terrain.basemapDistance = 300;
            terrain.drawInstanced = true;
            Shader shader = Shader.Find("Universal Render Pipeline/Terrain/Lit");
            if (!shader) throw new InvalidOperationException("URP Terrain/Lit is missing.");
            terrain.materialTemplate = Store(new Material(shader) { name = "Waterside Flat Terrain" }, folder + "/Waterside Terrain.mat");
            terrain.Flush();
            Debug.Log(Verify(terrain), terrain);
            EditorUtility.SetDirty(data);
            AssetDatabase.SaveAssets();
            return terrain;
        }

        public static string Verify(Terrain terrain)
        {
            if (!terrain || !terrain.terrainData) throw new ArgumentException("Waterside Terrain is required.");
            Vector3 expected = new Vector3(-WatersideLayout.Size * .5f, WatersideLayout.TerrainBottom, -WatersideLayout.Size * .5f);
            if ((terrain.transform.position - expected).sqrMagnitude > .0001f)
                throw new InvalidOperationException("Waterside Terrain origin differs from its shared layout.");
            return WatersideLayout.Validate((x, z) => WatersideLayout.GroundHeight(terrain, x, z));
        }

        static float Smooth(float from, float to, float value)
        { return Mathf.SmoothStep(0, 1, Mathf.InverseLerp(from, to, value)); }

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
