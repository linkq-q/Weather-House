using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace WeatherHouse.WatersideV2.Editor
{
    // Only switches V2's existing rock/sand layer references. No terrain generation or scene saving.
    public static class WatersideTerrainMaterialAB
    {
        const string DataPath = "Assets/Game/World/WatersideV2/Generated/WatersideTerrain.asset";
        const string Folder = "Assets/Game/World/WatersideV2/Generated/TerrainMaterialAB";
        const string Library = "Assets/Environment/DDriveLibrary/TerrainTextures/Terrain Layers/";
        const string Review = "AssetIntakeReview/terrain-candidates";
        const string BaselinePath = Review + "/baseline.json";
        const float MatteAlpha = 20f / 255f;

        [Serializable] public sealed class Baseline
        {
            public string terrainGuid;
            public string[] layerGuids;
        }

        [Serializable] public sealed class Report
        {
            public string operation, utc, terrain, heightHash, alphamapHash;
            public bool geometryAndCoverageUnchanged, meadowAndPathUnchanged, sceneStateUnchanged;
            public string[] layerPaths;
            public string note;
        }

        [MenuItem("Tools/Weather House/Waterside V2/Terrain materials/Apply rock and sand candidate")]
        static void ApplyMenu() { Debug.Log(Apply()); }
        [MenuItem("Tools/Weather House/Waterside V2/Terrain materials/Restore baseline")]
        static void RestoreMenu() { Debug.Log(Restore()); }

        public static string Apply() { return Change(true); }
        public static string Restore() { return Change(false); }

        static string Change(bool candidate)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
                throw new InvalidOperationException("Stop Play and finish compilation before terrain material A/B.");
            var data = AssetDatabase.LoadAssetAtPath<TerrainData>(DataPath);
            if (!data || data.terrainLayers.Length != 4)
                throw new InvalidOperationException("The existing four-layer Waterside V2 terrain is required.");
            var original = data.terrainLayers;
            string height = HeightHash(data), alpha = AlphaHash(data);
            var scenes = Enumerable.Range(0, SceneManager.sceneCount)
                .Select(SceneManager.GetSceneAt).Select(s => (s.handle, s.isDirty)).ToArray();
            int activeScene = SceneManager.GetActiveScene().handle;
            Directory.CreateDirectory(Review);
            Baseline baseline;
            if (File.Exists(BaselinePath))
                baseline = JsonUtility.FromJson<Baseline>(File.ReadAllText(BaselinePath));
            else
            {
                if (!candidate) throw new InvalidOperationException("No A/B baseline has been captured.");
                if (original.Any(l => AssetDatabase.GetAssetPath(l).StartsWith(Folder + "/", StringComparison.Ordinal)))
                    throw new InvalidOperationException("Candidate layers are already assigned but their baseline is missing.");
                baseline = new Baseline { terrainGuid = AssetDatabase.AssetPathToGUID(DataPath),
                    layerGuids = original.Select(Guid).ToArray() };
                File.WriteAllText(BaselinePath, JsonUtility.ToJson(baseline, true));
            }
            if (baseline == null || baseline.layerGuids == null || baseline.layerGuids.Length != 4 ||
                baseline.terrainGuid != AssetDatabase.AssetPathToGUID(DataPath))
                throw new InvalidOperationException("The A/B baseline belongs to different terrain data.");
            var restored = baseline.layerGuids.Select(g =>
                AssetDatabase.LoadAssetAtPath<TerrainLayer>(AssetDatabase.GUIDToAssetPath(g))).ToArray();
            if (restored.Any(l => !l))
                throw new InvalidOperationException("An original terrain layer is missing; the baseline cannot be restored.");
            for (int i = 0; i < original.Length; i++)
            {
                string expectedCandidate = Folder + (i == 2 ? "/Rock_3.terrainlayer" : "/Sand_1.terrainlayer");
                if (original[i] != restored[i] && (i < 2 || AssetDatabase.GetAssetPath(original[i]) != expectedCandidate))
                    throw new InvalidOperationException("Terrain layer " + i + " was changed outside this A/B. Preserve it and review the baseline.");
            }
            if (candidate && original.Skip(2).Any(l => AssetDatabase.GetAssetPath(l).StartsWith(Folder + "/", StringComparison.Ordinal)))
                throw new InvalidOperationException("Restore the baseline before regenerating candidates; do not overwrite layers currently in use.");
            var next = (TerrainLayer[])restored.Clone();
            if (candidate)
            {
                EnsureFolder();
                next[2] = MakeLayer("Rock_3", 4.5f, .18f, new Vector4(1.8f, 1.8f, 1.65f, 1));
                next[3] = MakeLayer("Sand_1", 3.5f, .12f, new Vector4(.85f, .95f, 1, 1));
            }
            bool assigned = false;
            try
            {
                data.terrainLayers = next;
                assigned = true;
                data.SetBaseMapDirty();
                Flush(data);
                if (height != HeightHash(data) || alpha != AlphaHash(data))
                    throw new InvalidOperationException("Height or alphamap changed during the material-only operation.");
                if (data.terrainLayers[0] != original[0] || data.terrainLayers[1] != original[1])
                    throw new InvalidOperationException("Meadow or Path layer reference changed.");
                CheckScenes(scenes, activeScene);
                EditorUtility.SetDirty(data);
                AssetDatabase.SaveAssetIfDirty(data);
                var report = new Report
                {
                    operation = candidate ? "apply" : "restore", utc = DateTime.UtcNow.ToString("o"),
                    terrain = DataPath, heightHash = height, alphamapHash = alpha,
                    geometryAndCoverageUnchanged = true, meadowAndPathUnchanged = true,
                    sceneStateUnchanged = true, layerPaths = data.terrainLayers.Select(AssetDatabase.GetAssetPath).ToArray(),
                    note = candidate ? "Candidate only; visual acceptance requires same-camera screenshots. Rock_3: tile 4.5 m, normal .18, RGB multiplier 1.8/1.8/1.65. Sand_1: tile 3.5 m, normal .12, RGB multiplier .85/.95/1. Alpha is 20/255 in private copies; source RGB/normal assets and Path are unchanged."
                        : "Original four layer references restored. Private candidate assets retained for repeatable A/B."
                };
                string json = JsonUtility.ToJson(report, true);
                File.WriteAllText(Review + "/" + report.operation + "-report.json", json);
                return json;
            }
            catch
            {
                if (assigned)
                {
                    data.terrainLayers = original;
                    data.SetBaseMapDirty();
                    EditorUtility.SetDirty(data);
                    AssetDatabase.SaveAssetIfDirty(data);
                    Flush(data);
                }
                throw;
            }
        }

        static TerrainLayer MakeLayer(string name, float tile, float normal, Vector4 tint)
        {
            string sourcePath = Library + name + ".terrainlayer";
            var source = AssetDatabase.LoadAssetAtPath<TerrainLayer>(sourcePath);
            if (!source || !source.diffuseTexture || !source.normalMapTexture)
                throw new InvalidOperationException("Missing source diffuse or normal: " + sourcePath);
            string normalPath = AssetDatabase.GetAssetPath(source.normalMapTexture);
            if (!(AssetImporter.GetAtPath(normalPath) is TextureImporter ni) ||
                ni.textureType != TextureImporterType.NormalMap)
                throw new InvalidOperationException("The candidate normal must already be imported as a normal map.");
            var layer = UnityEngine.Object.Instantiate(source);
            layer.name = "Waterside A-B " + name;
            layer.diffuseTexture = MatteCopy(source.diffuseTexture, name);
            layer.maskMapTexture = null;
            layer.tileSize = Vector2.one * tile;
            layer.tileOffset = Vector2.zero;
            layer.normalScale = normal;
            layer.metallic = 0;
            layer.smoothness = MatteAlpha;
            // w controls opacity-as-density in URP, not smoothness; keep the existing splat weights authoritative.
            layer.diffuseRemapMin = Vector4.zero;
            layer.diffuseRemapMax = tint;
            string path = Folder + "/" + name + ".terrainlayer";
            var saved = AssetDatabase.LoadAssetAtPath<TerrainLayer>(path);
            if (saved)
            {
                EditorUtility.CopySerialized(layer, saved);
                UnityEngine.Object.DestroyImmediate(layer);
                EditorUtility.SetDirty(saved);
            }
            else { saved = layer; AssetDatabase.CreateAsset(saved, path); }
            AssetDatabase.SaveAssetIfDirty(saved);
            return saved;
        }

        static Texture2D MatteCopy(Texture2D source, string name)
        {
            string sourcePath = AssetDatabase.GetAssetPath(source);
            if (!sourcePath.StartsWith("Assets/Environment/DDriveLibrary/TerrainTextures/Textures/", StringComparison.Ordinal) ||
                !sourcePath.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Only the newly imported PNG sources may be used.");
            byte[] sourceBytes = File.ReadAllBytes(sourcePath);
            string path = Folder + "/" + name + "_Matte.png";
            var pixels = new Texture2D(2, 2, TextureFormat.RGBA32, false, false);
            var check = new Texture2D(2, 2, TextureFormat.RGBA32, false, false);
            try
            {
                if (!ImageConversion.LoadImage(pixels, sourceBytes, false))
                    throw new InvalidOperationException("Cannot decode " + sourcePath);
                Color32[] original = pixels.GetPixels32(), output = (Color32[])original.Clone();
                for (int i = 0; i < output.Length; i++) output[i].a = 20;
                pixels.SetPixels32(output); pixels.Apply(false, false);
                byte[] encoded = pixels.EncodeToPNG();
                if (!ImageConversion.LoadImage(check, encoded, false))
                    throw new InvalidOperationException("Cannot decode the generated matte image.");
                Color32[] roundtrip = check.GetPixels32();
                if (roundtrip.Length != original.Length) throw new InvalidOperationException("Image dimensions changed.");
                for (int i = 0; i < original.Length; i++)
                    if (roundtrip[i].r != original[i].r || roundtrip[i].g != original[i].g ||
                        roundtrip[i].b != original[i].b || roundtrip[i].a != 20)
                        throw new InvalidOperationException("Matte conversion changed RGB pixels.");
                File.WriteAllBytes(path, encoded);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(pixels);
                UnityEngine.Object.DestroyImmediate(check);
            }
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = true;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.alphaIsTransparency = false;
            importer.mipmapEnabled = true;
            importer.mipMapBias = 0;
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.filterMode = FilterMode.Trilinear;
            importer.anisoLevel = 4;
            importer.isReadable = false;
            importer.maxTextureSize = 2048;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            importer.SaveAndReimport();
            if (!sourceBytes.SequenceEqual(File.ReadAllBytes(sourcePath)))
                throw new InvalidOperationException("The source texture unexpectedly changed.");
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        static void EnsureFolder()
        {
            const string parent = "Assets/Game/World/WatersideV2/Generated";
            if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder(parent, "TerrainMaterialAB");
        }

        static string Guid(TerrainLayer layer)
        {
            string guid = layer ? AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(layer)) : "";
            if (string.IsNullOrEmpty(guid)) throw new InvalidOperationException("Baseline layers must be persistent assets.");
            return guid;
        }

        static string HeightHash(TerrainData data)
        { return Hash(data.GetHeights(0, 0, data.heightmapResolution, data.heightmapResolution)); }
        static string AlphaHash(TerrainData data)
        { return Hash(data.GetAlphamaps(0, 0, data.alphamapWidth, data.alphamapHeight)); }
        static string Hash(Array values)
        {
            var bytes = new byte[Buffer.ByteLength(values)];
            Buffer.BlockCopy(values, 0, bytes, 0, bytes.Length);
            using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
        }

        static void Flush(TerrainData data)
        {
            foreach (var terrain in Terrain.activeTerrains)
                if (terrain.terrainData == data) terrain.Flush();
        }

        static void CheckScenes((int handle, bool isDirty)[] before, int active)
        {
            if (SceneManager.sceneCount != before.Length || SceneManager.GetActiveScene().handle != active)
                throw new InvalidOperationException("Loaded or active scenes changed.");
            for (int i = 0; i < before.Length; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (scene.handle != before[i].handle || scene.isDirty != before[i].isDirty)
                    throw new InvalidOperationException("Scene state changed during material A/B.");
            }
        }
    }
}
