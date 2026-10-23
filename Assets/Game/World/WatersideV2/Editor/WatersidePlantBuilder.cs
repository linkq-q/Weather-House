using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using WeatherHouse.Vegetation;
using Object = UnityEngine.Object;

namespace WeatherHouse.WatersideV2.Editor
{
    public static class WatersidePlantBuilder
    {
        const string Source = "Assets/Experiments/Vegetation/PlantLibrary";
        const int VariantCount = 3;
        public static readonly string[] Ids = { "G1_TallBroad", "G2_Upright", "G3_OpenLow", "G4_DenseShort",
            "R01_Plume", "R02_BrownSeed", "R03_YellowSpike", "R04_PaleSpike", "R05_WhiteSpray", "R06_Daisy",
            "R07_BlueFlower", "R08_Fern", "R09_CreamFoxtail", "R10_OrangeFlower", "R11_YellowButton", "R12_LilacBell",
            "R13_LeafyHerb", "R14_RoundGroundcover", "R15_LowSeed", "N01_WaterLily", "N02_Ivy", "N03_BroadleafShrub" };
        public static readonly IReadOnlyDictionary<string, string> Labels = new Dictionary<string, string> {
            { "G1_TallBroad", "高草·厚宽叶" }, { "G2_Upright", "中高草·直立束" }, { "G3_OpenLow", "中低草·疏斜叶" }, { "G4_DenseShort", "短草·密实叶" },
            { "R01_Plume", "奶白实体长穗" }, { "R02_BrownSeed", "棕色直立穗" }, { "R03_YellowSpike", "黄色长花序" }, { "R04_PaleSpike", "浅白花穗" },
            { "R05_WhiteSpray", "分枝白花" }, { "R06_Daisy", "白色雏菊" }, { "R07_BlueFlower", "浅蓝小花" }, { "R08_Fern", "厚叶蕨丛" },
            { "R09_CreamFoxtail", "奶白短卵穗" }, { "R10_OrangeFlower", "橙色小花" }, { "R11_YellowButton", "黄色纽扣花" }, { "R12_LilacBell", "浅紫杯形花" },
            { "R13_LeafyHerb", "阔叶草本" }, { "R14_RoundGroundcover", "圆叶匍匐地被" }, { "R15_LowSeed", "低位浅穗草" },
            { "N01_WaterLily", "水面叶盘与睡莲" }, { "N02_Ivy", "常春藤叶簇" }, { "N03_BroadleafShrub", "分枝阔叶灌木" } };

        public static Dictionary<string, GameObject[]> Build(string outputFolder)
        {
            outputFolder = OutputFolder(outputFolder);
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Build waterside plant assets in Edit mode.");
            var sourceHashes = Ids.Take(19).ToDictionary(id => id, id => FileHash(Source + "/Definitions/" + id + ".asset"));
            foreach (string child in new[] { "", "/Definitions", "/Materials", "/Meshes", "/Prefabs" })
                Directory.CreateDirectory(outputFolder + child);
            AssetDatabase.Refresh();
            var shader = Shader.Find("Weather House/Painterly Grass");
            if (!shader) throw new InvalidOperationException("Painterly Grass shader is required");
            var result = new Dictionary<string, GameObject[]>();
            var preview = EditorSceneManager.NewPreviewScene();
            try
            {
                foreach (string id in Ids)
                {
                    GrassSpecies source = AssetDatabase.LoadAssetAtPath<GrassSpecies>(Source + "/Definitions/" + id + ".asset");
                    if (!source && !id.StartsWith("N", StringComparison.Ordinal)) throw new FileNotFoundException("Missing original plant definition: " + id);
                    var definition = source ? Object.Instantiate(source) : ScriptableObject.CreateInstance<GrassSpecies>();
                    definition.name = id;
                    Configure(definition, id);
                    definition = StoreDefinition(definition, outputFolder + "/Definitions/" + id + ".asset");
                    string materialPath = outputFolder + "/Materials/" + id + ".mat";
                    var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
                    if (!material) { material = new Material(shader); AssetDatabase.CreateAsset(material, materialPath); }
                    material.shader = shader; material.enableInstancing = true;
                    material.SetColor("_BaseColor", definition.baseColor); material.SetColor("_TipColor", definition.tipColor);
                    material.SetFloat("_UpNormals", .15f); material.SetFloat("_MeadowBlend", 0);
                    material.SetFloat("_WindStrength", id.StartsWith("G", StringComparison.Ordinal) || id == "R01_Plume" ? .035f : .025f);
                    material.SetFloat("_WindSpeed", .55f); material.SetFloat("_PatchStrength", .08f);
                    material.SetFloat("_BladeContrast", .8f); material.SetFloat("_ShadowStrength", .65f);
                    EditorUtility.SetDirty(material);
                    var variants = new GameObject[VariantCount]; result[id] = variants;
                    for (int variant = 0; variant < VariantCount; variant++)
                    {
                        Mesh mesh = WatersidePlantMesh.Build(definition, variant, id);
                        string name = id + "_v" + (variant + 1), meshPath = outputFolder + "/Meshes/" + name + ".asset";
                        var existing = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
                        if (existing) { GrassMeshFactory.UpdateStoredMesh(mesh, existing); Object.DestroyImmediate(mesh); mesh = existing; EditorUtility.SetDirty(mesh); }
                        else AssetDatabase.CreateAsset(mesh, meshPath);
                        var go = new GameObject(name); SceneManager.MoveGameObjectToScene(go, preview);
                        try
                        {
                            go.AddComponent<MeshFilter>().sharedMesh = mesh;
                            var renderer = go.AddComponent<MeshRenderer>(); renderer.sharedMaterial = material;
                            renderer.shadowCastingMode = ShadowCastingMode.On; renderer.receiveShadows = true;
                            variants[variant] = PrefabUtility.SaveAsPrefabAsset(go, outputFolder + "/Prefabs/" + name + ".prefab");
                        }
                        finally { Object.DestroyImmediate(go); }
                    }
                }
                AssetDatabase.SaveAssets();
                foreach (var pair in sourceHashes)
                    Require(pair.Value == FileHash(Source + "/Definitions/" + pair.Key + ".asset"), "Original library definition changed: " + pair.Key);
                return result;
            }
            finally { EditorSceneManager.ClosePreviewScene(preview); }
        }

        static GrassSpecies StoreDefinition(GrassSpecies source, string path)
        {
            var stored = AssetDatabase.LoadAssetAtPath<GrassSpecies>(path);
            if (!stored) { AssetDatabase.CreateAsset(source, path); return source; }
            EditorUtility.CopySerialized(source, stored); Object.DestroyImmediate(source); EditorUtility.SetDirty(stored); return stored;
        }

        static void Configure(GrassSpecies s, string id)
        {
            s.displayName = Labels[id]; s.seed += 20261023;
            s.width *= 1.25f; s.spread *= 1.08f;
            s.clumpLayering = Mathf.Max(s.clumpLayering, .55f);
            s.heightVariation = .23f;
            s.baseColor = Color.Lerp(s.baseColor, new Color(.20f, .29f, .09f), .55f);
            s.tipColor = Color.Lerp(s.tipColor, new Color(.64f, .75f, .31f), .35f);
            s.bladeCount = Mathf.Min(s.bladeCount, 28); s.flowerCount = Mathf.Min(s.flowerCount, 3);
            s.flowerRadius = Mathf.Min(.10f, s.flowerRadius * 1.28f);
            s.curl = Mathf.Max(.24f, s.curl);
            switch (id)
            {
                // A closed cream seed head replaces thousands of thin florets while preserving the tall pale silhouette.
                case "R01_Plume": s.kind = GrassKind.SeedHead; s.height = 1.55f; s.width = .09f; s.spread = .14f; s.flowerCount = 4; s.headLength = .34f; s.flowerRadius = .13f; s.bladeCount = 12; s.bend = .32f; break;
                case "R02_BrownSeed": s.width = .075f; s.spread = .12f; s.flowerCount = 4; s.flowerRadius = .065f; s.headLength = .24f; s.bladeCount = 12; break;
                case "R03_YellowSpike": case "R04_PaleSpike": s.width = .07f; s.flowerCount = 3; s.bladeCount = 6; s.petalCount = 4; s.flowerRadius = .065f; break;
                case "R05_WhiteSpray": s.flowerCount = 2; s.bladeCount = 6; s.petalCount = 5; s.flowerRadius = .065f; break;
                case "R08_Fern": s.bladeCount = 4; s.headLength = .20f; s.leafAspect = 1.05f; s.width = .095f; break;
                case "R09_CreamFoxtail": s.width = .065f; s.spread = .075f; s.flowerCount = 4; s.headLength = .18f; s.bladeCount = 6; break;
                case "R13_LeafyHerb": s.bladeCount = 8; break;
                case "R15_LowSeed": s.width = .045f; s.spread = .055f; s.bladeCount = 6; s.flowerCount = 4; break;
                case "N01_WaterLily": s.kind = GrassKind.RoundGroundcover; s.height = .18f; s.width = .11f; s.flowerColor = new Color(.93f, .79f, .82f, 1); s.baseColor = new Color(.19f, .31f, .15f); s.tipColor = new Color(.49f, .64f, .29f); break;
                case "N02_Ivy": s.kind = GrassKind.LeafyHerb; s.height = .28f; s.width = .10f; s.baseColor = new Color(.16f, .27f, .10f); s.tipColor = new Color(.51f, .65f, .26f); break;
                case "N03_BroadleafShrub": s.kind = GrassKind.LeafyHerb; s.height = .65f; s.width = .12f; s.baseColor = new Color(.17f, .25f, .09f); s.tipColor = new Color(.58f, .69f, .30f); break;
            }
        }

        public static string Verify(string outputFolder)
        {
            outputFolder = OutputFolder(outputFolder);
            var report = new List<string> { VerifySolidifier() };
            foreach (string id in Ids)
            {
                var definition = AssetDatabase.LoadAssetAtPath<GrassSpecies>(outputFolder + "/Definitions/" + id + ".asset");
                Require(definition != null, "Missing definition " + id);
                var hashes = new HashSet<string>();
                int maximum = 0;
                for (int v = 0; v < VariantCount; v++)
                {
                    string path = outputFolder + "/Prefabs/" + id + "_v" + (v + 1) + ".prefab";
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    Require(prefab != null, "Missing saved prefab " + path);
                    var filter = prefab.GetComponent<MeshFilter>(); var renderer = prefab.GetComponent<MeshRenderer>();
                    Require(filter && filter.sharedMesh && renderer && renderer.sharedMaterial, "Missing mesh/material " + path);
                    var material = renderer.sharedMaterial;
                    Require(material.shader.name == "Weather House/Painterly Grass" && Mathf.Abs(material.GetFloat("_UpNormals") - .15f) < .001f &&
                        material.GetFloat("_MeadowBlend") == 0 && material.GetFloat("_WindStrength") >= .025f && material.GetFloat("_WindStrength") <= .04f,
                        "Incorrect near-camera material " + path);
                    WatersidePlantMesh.Validate(filter.sharedMesh);
                    maximum = Mathf.Max(maximum, filter.sharedMesh.triangles.Length / 3);
                    string hash = MeshHash(filter.sharedMesh); hashes.Add(hash);
                    Mesh again = WatersidePlantMesh.Build(definition, v, id);
                    try { Require(hash == MeshHash(again), "Regenerated seed differs from saved geometry: " + path); }
                    finally { Object.DestroyImmediate(again); }
                    Require(prefab.GetComponentsInChildren<Collider>().Length == 0, "Plant must not block the player corridor: " + path);
                }
                Require(hashes.Count == VariantCount, "Variants are duplicates: " + id);
                report.Add("PASS " + id + ": 3 distinct deterministic saved variants; closed edges, finite normals; maximum " + maximum + " triangles.");
            }
            return string.Join("\n", report);
        }

        static string VerifySolidifier()
        {
            var quad = new Mesh(); var tetra = new Mesh(); var tube = new Mesh(); var tiny = new Mesh();
            Mesh solidQuad = null, solidTetra = null, solidTube = null, solidTiny = null;
            try
            {
                quad.vertices = new[] { new Vector3(0, 0, 0), new Vector3(1, 0, 0), new Vector3(0, 0, 1), new Vector3(1, 0, 1) };
                quad.triangles = new[] { 0, 2, 1, 1, 2, 3 };
                solidQuad = WatersidePlantMesh.Solidify(quad, .01f); WatersidePlantMesh.Validate(solidQuad);
                Require(solidQuad.triangles.Length == 36 && solidQuad.vertices.Max(p => p.y) - solidQuad.vertices.Min(p => p.y) > .0099f,
                    "Open leaf did not receive physical thickness and four closed sides");
                tetra.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up, Vector3.forward };
                tetra.triangles = new[] { 0, 2, 1, 0, 1, 3, 0, 3, 2, 1, 2, 3 };
                solidTetra = WatersidePlantMesh.Solidify(tetra, .01f); WatersidePlantMesh.Validate(solidTetra);
                Require(solidTetra.vertexCount == 4 && solidTetra.triangles.Length == 12, "Closed stem was duplicated");
                var points = new List<Vector3>(); var triangles = new List<int>();
                for (int ring = 0; ring < 2; ring++)
                    for (int i = 0; i < 5; i++) points.Add(new Vector3(Mathf.Cos(i * Mathf.PI * .4f) * .02f, ring, Mathf.Sin(i * Mathf.PI * .4f) * .02f));
                for (int i = 0; i < 5; i++) { int next = (i + 1) % 5; triangles.AddRange(new[] { i, next, i + 5, i + 5, next, next + 5 }); }
                tube.SetVertices(points); tube.SetTriangles(triangles, 0);
                solidTube = WatersidePlantMesh.Solidify(tube, .01f); WatersidePlantMesh.Validate(solidTube);
                Require(solidTube.vertexCount == 12 && solidTube.triangles.Length == 60, "Uncapped stem should receive end caps, not a second wall");
                // Reproduces the real daisy-tip area below Vector3.normalized's epsilon, plus a true degenerate face and orphan.
                tiny.vertices = new[] { Vector3.zero, new Vector3(.001f, 0, 0), new Vector3(0, 0, .001f), new Vector3(.001f, 0, .001f), Vector3.one, Vector3.one * 2 };
                tiny.triangles = new[] { 0, 2, 1, 1, 2, 3, 0, 0, 4 };
                solidTiny = WatersidePlantMesh.Solidify(tiny, .003f); WatersidePlantMesh.Validate(solidTiny);
                Require(solidTiny.vertexCount == 8 && solidTiny.triangles.Length == 36,
                    "Tiny valid leaf lost its normals or degenerate/orphan vertices survived cleanup");
                return "PASS solidifier: open leaf gains thickness; closed stem is not duplicated; uncapped stem gains only two caps; tiny daisy-scale faces remain valid while degenerate/orphan vertices are removed.";
            }
            finally { foreach (Mesh mesh in new[] { quad, tetra, tube, tiny, solidQuad, solidTetra, solidTube, solidTiny }) if (mesh) Object.DestroyImmediate(mesh); }
        }

        static string MeshHash(Mesh mesh)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                foreach (Vector3 p in mesh.vertices) { writer.Write(p.x); writer.Write(p.y); writer.Write(p.z); }
                foreach (Vector3 p in mesh.normals) { writer.Write(p.x); writer.Write(p.y); writer.Write(p.z); }
                foreach (Vector2 p in mesh.uv) { writer.Write(p.x); writer.Write(p.y); }
                foreach (Vector2 p in mesh.uv2) { writer.Write(p.x); writer.Write(p.y); }
                foreach (Color c in mesh.colors) { writer.Write(c.r); writer.Write(c.g); writer.Write(c.b); writer.Write(c.a); }
                foreach (int i in mesh.triangles) writer.Write(i);
                writer.Flush(); using (var sha = SHA256.Create()) return Convert.ToBase64String(sha.ComputeHash(stream.ToArray()));
            }
        }
        static string FileHash(string path) { using (var sha = SHA256.Create()) return Convert.ToBase64String(sha.ComputeHash(File.ReadAllBytes(path))); }
        static string OutputFolder(string path)
        {
            path = path.Replace('\\', '/').TrimEnd('/');
            if (!path.StartsWith("Assets/Game/World/WatersideV2/", StringComparison.Ordinal) || path.Split('/').Any(p => p == ".."))
                throw new ArgumentException("Use an output folder inside Assets/Game/World/WatersideV2.", nameof(path));
            return path;
        }
        static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    }
}
