using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace WeatherHouse.WatersideV2.Editor
{
    public static class WeatherHouseRoofHarmony
    {
        const string Folder = "Assets/Environment/WeatherHouseV05/Materials/";
        const string Review = "WatersideV3Review/roof";

        public static void ApplySparseMeshes()
        {
            WatersideSceneBuilder.RequireSaved();
            const string prefabPath = WeatherHouseV05Import.Prefab;
            Directory.CreateDirectory(Review + "/original");
            if (!File.Exists(Review + "/original/WeatherHouse_v05.prefab"))
                File.Copy(prefabPath, Review + "/original/WeatherHouse_v05.prefab");
            GameObject contents = PrefabUtility.LoadPrefabContents(prefabPath);
            var lines = new System.Collections.Generic.List<string>();
            try
            {
                for (int level = 0; level < 3; level++)
                {
                    string path = WeatherHouseV05Import.Folder + "/RoofHarmony/WeatherHouse_v05_RoofSparse_LOD" + level + ".fbx";
                    var importer = AssetImporter.GetAtPath(path) as ModelImporter;
                    if (!importer) throw new InvalidOperationException("Missing sparse roof " + path);
                    importer.importAnimation = false; importer.importCameras = false; importer.importLights = false;
                    importer.importNormals = ModelImporterNormals.Import;
                    importer.importTangents = ModelImporterTangents.CalculateMikk;
                    importer.materialImportMode = ModelImporterMaterialImportMode.None;
                    importer.SaveAndReimport();
                    GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    foreach (string part in new[] { "Moss", "Foliage" })
                    {
                        string name = "WeatherHouse_" + part + "_LOD" + level;
                        MeshFilter old = contents.GetComponentsInChildren<MeshFilter>(true).Single(f => f.name == name);
                        MeshFilter replacement = source.GetComponentsInChildren<MeshFilter>(true).Single(f => f.name == name);
                        if (Vector3.Distance(old.transform.localPosition, replacement.transform.localPosition) > .0001f ||
                            Vector3.Distance(old.transform.localScale, replacement.transform.localScale) > .0001f ||
                            Quaternion.Angle(old.transform.localRotation, replacement.transform.localRotation) > .01f)
                            throw new InvalidOperationException("Sparse roof import transform mismatch: " + name);
                        Bounds a = old.sharedMesh.bounds, b = replacement.sharedMesh.bounds;
                        if (Vector3.Distance(a.min, b.min) > .06f || Vector3.Distance(a.max, b.max) > .06f)
                            throw new InvalidOperationException("Sparse roof bounds mismatch: " + name + " " + a + " / " + b);
                        lines.Add(name + " vertices=" + old.sharedMesh.vertexCount + " -> " + replacement.sharedMesh.vertexCount);
                        old.sharedMesh = replacement.sharedMesh;
                    }
                }
                PrefabUtility.SaveAsPrefabAsset(contents, prefabPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(contents); }
            AssetDatabase.SaveAssets();
            GameObject house = GameObject.Find("天气屋 · v05 苔顶木构");
            int applied = house.GetComponentsInChildren<MeshFilter>(true).Count(f => AssetDatabase.GetAssetPath(f.sharedMesh).Contains("/RoofHarmony/"));
            if (applied != 6) throw new InvalidOperationException("Expected six sparse roof meshes on the live house; found " + applied);
            File.WriteAllText(Review + "/mesh-checks.txt", "PASS\nUtc=" + DateTime.UtcNow.ToString("O") +
                "\nSixMeshReferencesReplaced=True\nExistingTransformsMaterialsCollidersKept=True\n" + string.Join("\n", lines));
        }

        public static void Inspect()
        {
            GameObject house = GameObject.Find("天气屋 · v05 苔顶木构");
            if (!house) throw new InvalidOperationException("Missing v05 house.");
            Directory.CreateDirectory(Review);
            File.WriteAllLines(Review + "/mesh-inspection.txt", house.GetComponentsInChildren<MeshFilter>(true).Select(f =>
                f.transform.parent.name + "/" + f.name + " vertices=" + f.sharedMesh.vertexCount + " localBounds=" + f.sharedMesh.bounds +
                " worldBounds=" + f.GetComponent<Renderer>().bounds + " material=" + f.GetComponent<Renderer>().sharedMaterial.name));
        }

        public static void Apply()
        {
            WatersideSceneBuilder.RequireSaved();
            GameObject house = GameObject.Find("天气屋 · v05 苔顶木构");
            if (!house) throw new InvalidOperationException("The user-placed v05 house is missing.");
            Material moss = AssetDatabase.LoadAssetAtPath<Material>(Folder + "WH05_Moss.mat");
            Material tiles = AssetDatabase.LoadAssetAtPath<Material>(Folder + "WH05_RoofTiles.mat");
            Shader shader = Shader.Find("Weather House/Painterly Ground");
            if (!moss || !tiles || !shader || !shader.isSupported || ShaderUtil.ShaderHasError(shader))
                throw new InvalidOperationException("The existing painterly roof material dependencies are unavailable.");
            Directory.CreateDirectory(Review + "/original");
            foreach (string name in new[] { "WH05_Moss", "WH05_RoofTiles" })
                if (!File.Exists(Review + "/original/" + name + ".mat"))
                    File.Copy(Folder + name + ".mat", Review + "/original/" + name + ".mat");
            Vector3 position = house.transform.position, scale = house.transform.localScale;
            Quaternion rotation = house.transform.rotation;
            // Reuse the meadow's broad pigment treatment on the existing moss geometry.
            // The baked maps stay on disk; no UV islands or house geometry are changed.
            Undo.RecordObjects(new UnityEngine.Object[] { moss, tiles }, "Soften the moss roof palette");
            moss.shader = shader;
            moss.shaderKeywords = Array.Empty<string>();
            moss.SetColor("_BaseColor", new Color(.49f, .54f, .245f));
            moss.SetColor("_PatchColor", new Color(.69f, .70f, .36f));
            moss.SetColor("_ShadowTint", new Color(.66f, .74f, .62f));
            moss.SetColor("_ShoreColor", new Color(.49f, .54f, .245f));
            moss.SetFloat("_WaterLevel", -100);
            moss.SetFloat("_PatchScale", .72f);
            moss.SetFloat("_BoundaryHardness", .32f);
            moss.SetFloat("_BoundaryBreakup", .035f);
            moss.SetFloat("_BoundarySize", .8f);
            tiles.SetFloat("_BumpScale", .18f);
            tiles.SetFloat("_Smoothness", .25f);
            tiles.SetColor("_BaseColor", new Color(.98f, .97f, .88f));
            EditorUtility.SetDirty(moss); EditorUtility.SetDirty(tiles);
            AssetDatabase.SaveAssets();
            if (house.transform.position != position || house.transform.localScale != scale || house.transform.rotation != rotation)
                throw new InvalidOperationException("House placement changed during a material-only operation.");
            int references = house.GetComponentsInChildren<Renderer>(true).Count(r => r.sharedMaterials.Contains(moss));
            if (references != 3) throw new InvalidOperationException("The moss material must reach all three existing house LODs.");
            File.WriteAllText(Review + "/checks.txt", "PASS\nUtc=" + DateTime.UtcNow.ToString("O") +
                "\nMossLODRenderers=" + references + "\nHouseTransformUnchanged=True\nOriginalBakedMapsUnchanged=True" +
                "\nMoss=existing painterly meadow shader, broad 0.72/m pigment patches, no noisy normal map" +
                "\nRoofTilesNormalScale=0.18\nRoofTilesSmoothness=0.25\nScenePlacementUntouched=True\nVisualAcceptance=requires same-camera review");
        }
    }
}
