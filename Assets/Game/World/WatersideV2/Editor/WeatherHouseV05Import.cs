using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace WeatherHouse.WatersideV2.Editor
{
    public static class WeatherHouseV05Import
    {
        public const string Folder = "Assets/Environment/WeatherHouseV05";
        public const string Prefab = Folder + "/WeatherHouse_v05.prefab";
        public static void ForceLod(int level)
        {
            GameObject house = GameObject.Find("天气屋 · v05 苔顶木构");
            if (!house) throw new InvalidOperationException("The placed house is missing.");
            LODGroup[] groups = house.GetComponentsInChildren<LODGroup>(true);
            File.WriteAllText("WeatherHouseIntegrationReview/lod-groups.txt", "Groups=" + groups.Length + "\n" + string.Join("\n", groups.Select(g => g.name + " enabled=" + g.enabled + " LODs=" + g.GetLODs().Length)));
            house.GetComponent<LODGroup>().ForceLOD(level);
        }
        public static void SetNormalScale(float scale)
        {
            foreach (string guid in AssetDatabase.FindAssets("t:Material", new[] { Folder + "/Materials" }))
            {
                Material material = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
                material.SetFloat("_BumpScale", scale); EditorUtility.SetDirty(material);
            }
            AssetDatabase.SaveAssets();
        }
        [Serializable] sealed class MaterialEntry
        {
            public string name, baseColor, normal, metallicSmoothness, emission;
            public bool doubleSided;
            public float roughness;
        }
        [Serializable] sealed class Manifest { public MaterialEntry[] materials; }

        // Source files are copied only after the export chat supplies its final verified package.
        public static void BuildPrefab()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Import the house in edit mode.");
            Manifest manifest = JsonUtility.FromJson<Manifest>(File.ReadAllText(Folder + "/material_manifest.json"));
            if (manifest.materials == null || manifest.materials.Length == 0) throw new InvalidOperationException("Empty material manifest.");
            Directory.CreateDirectory(Folder + "/Materials");
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            foreach (string file in Directory.GetFiles(Folder, "*", SearchOption.AllDirectories)
                .Where(p => p.EndsWith(".png", StringComparison.OrdinalIgnoreCase) || p.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase)))
                AssetDatabase.ImportAsset(file.Replace('\\', '/'), ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
            Material[] materials = manifest.materials.Select(ImportMaterial).ToArray();
            var preview = EditorSceneManager.NewPreviewScene();
            var root = new GameObject("天气屋 · v05 苔顶木构");
            SceneManager.MoveGameObjectToScene(root, preview);
            try
            {
                var lods = new LOD[3];
                float[] heights = { .50f, .18f, .005f };
                for (int i = 0; i < lods.Length; i++)
                {
                    string path = Folder + "/WeatherHouse_v05_LOD" + i + ".fbx";
                    var importer = AssetImporter.GetAtPath(path) as ModelImporter;
                    if (!importer) throw new InvalidOperationException("Missing house model: " + path);
                    importer.importAnimation = false;
                    importer.importCameras = false;
                    importer.importLights = false;
                    importer.addCollider = false;
                    importer.importNormals = ModelImporterNormals.Import;
                    importer.importTangents = ModelImporterTangents.CalculateMikk;
                    importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
                    for (int m = 0; m < materials.Length; m++)
                        importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), manifest.materials[m].name), materials[m]);
                    importer.SaveAndReimport();
                    GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    var instance = (GameObject)PrefabUtility.InstantiatePrefab(model, preview);
                    instance.name = "LOD " + i;
                    instance.transform.SetParent(root.transform, false);
                    Renderer[] renderers = instance.GetComponentsInChildren<Renderer>(true);
                    if (renderers.Length == 0 || renderers.Any(r => r.sharedMaterials.Any(m => !m || !materials.Contains(m))))
                        throw new InvalidOperationException("Unmapped material in house LOD " + i);
                    if (instance.GetComponentsInChildren<MeshFilter>(true).Any(f => !f.sharedMesh || f.sharedMesh.vertexCount == 0))
                        throw new InvalidOperationException("Empty house mesh in LOD " + i);
                    lods[i] = new LOD(heights[i], renderers);
                }
                string[] collisions = Directory.GetFiles(Folder, "*.fbx").Where(p => p.IndexOf("collision", StringComparison.OrdinalIgnoreCase) >= 0).ToArray();
                if (collisions.Length != 1) throw new InvalidOperationException("Expected one verified low-poly collision FBX.");
                GameObject collisionAsset = AssetDatabase.LoadAssetAtPath<GameObject>(collisions[0].Replace('\\', '/'));
                GameObject proxy = Object.Instantiate(collisionAsset, root.transform);
                proxy.name = "实体墙身基座台阶碰撞";
                foreach (Renderer r in proxy.GetComponentsInChildren<Renderer>(true)) Object.DestroyImmediate(r);
                foreach (MeshFilter filter in proxy.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (!filter.sharedMesh || filter.sharedMesh.vertexCount == 0) throw new InvalidOperationException("Empty collision proxy.");
                    BoxCollider box = filter.gameObject.AddComponent<BoxCollider>();
                    box.center = filter.sharedMesh.bounds.center;
                    box.size = filter.sharedMesh.bounds.size;
                }
                if (proxy.GetComponentsInChildren<BoxCollider>().Length == 0) throw new InvalidOperationException("No house colliders.");
                LODGroup group = root.AddComponent<LODGroup>();
                group.SetLODs(lods); group.RecalculateBounds();
                PrefabUtility.SaveAsPrefabAsset(root, Prefab);
                AssetDatabase.SaveAssets();
                Directory.CreateDirectory("WeatherHouseIntegrationReview");
                File.WriteAllText("WeatherHouseIntegrationReview/import-checks.txt", "PASS\n" + Prefab + "\nMaterials=" + materials.Length + "\nLOD renderers=" + string.Join(",", lods.Select(l => l.renderers.Length)) + "\nColliders=" + proxy.GetComponentsInChildren<Collider>().Length);
            }
            finally { Object.DestroyImmediate(root); EditorSceneManager.ClosePreviewScene(preview); }
        }

        static Material ImportMaterial(MaterialEntry entry)
        {
            string path = Folder + "/Materials/" + entry.name + ".mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!material)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(material, path);
            }
            material.SetColor("_BaseColor", Color.white);
            material.SetTexture("_BaseMap", Texture(entry.baseColor, false, true));
            material.SetTexture("_BumpMap", Texture(entry.normal, true, false));
            material.SetTexture("_MetallicGlossMap", Texture(entry.metallicSmoothness, false, false));
            material.SetTexture("_EmissionMap", Texture(entry.emission, false, true));
            material.SetFloat("_Smoothness", string.IsNullOrEmpty(entry.metallicSmoothness) ? 1 - entry.roughness : 1);
            material.SetFloat("_Metallic", string.IsNullOrEmpty(entry.metallicSmoothness) ? 0 : 1);
            material.SetFloat("_Cull", entry.doubleSided ? 0 : 2);
            material.SetFloat("_BumpScale", 1);
            Keyword(material, "_NORMALMAP", !string.IsNullOrEmpty(entry.normal));
            Keyword(material, "_METALLICSPECGLOSSMAP", !string.IsNullOrEmpty(entry.metallicSmoothness));
            Keyword(material, "_EMISSION", !string.IsNullOrEmpty(entry.emission));
            material.SetColor("_EmissionColor", string.IsNullOrEmpty(entry.emission) ? Color.black : Color.white);
            material.enableInstancing = true;
            EditorUtility.SetDirty(material);
            return material;
        }

        static Texture2D Texture(string name, bool normal, bool srgb)
        {
            if (string.IsNullOrEmpty(name)) return null;
            string path = Folder + "/Textures/" + name;
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (!importer) throw new InvalidOperationException("Missing baked texture: " + path);
            importer.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            importer.sRGBTexture = srgb;
            importer.maxTextureSize = 4096;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.mipmapEnabled = true;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }
        static void Keyword(Material material, string keyword, bool enabled)
        { if (enabled) material.EnableKeyword(keyword); else material.DisableKeyword(keyword); }
    }
}
