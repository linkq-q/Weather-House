using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace WeatherHouse.Vegetation.Editor
{
    public static class PlantBiomeSamples
    {
        const string Root = PlantLibraryBuilder.Root + "/Biomes";
        const string GroundPath = Root + "/ExpandedGround.asset";
        static readonly string[] Ids = { "LowMeadow", "TallFlowers", "FernEdge", "Wetland" };
        static readonly string[] Names = { "低矮草坪", "高草花甸", "蕨类林缘", "羽穗湿地草" };
        static readonly string[] Signs = { "Low meadow", "Tall flowers", "Fern edge", "Wetland" };
        static readonly int[] Totals = { 18000, 16000, 14000, 16000 };
        static readonly Vector2[] Positions = {
            new Vector2(-50, -44), new Vector2(50, -44), new Vector2(-50, 44), new Vector2(50, 44)
        };
        static readonly string[] PlantIds = {
            "G1_TallBroad", "G2_Upright", "G3_OpenLow", "G4_DenseShort", "R01_Plume", "R02_BrownSeed",
            "R03_YellowSpike", "R04_PaleSpike", "R05_WhiteSpray", "R06_Daisy", "R07_BlueFlower", "R08_Fern",
            "R09_CreamFoxtail", "R10_OrangeFlower", "R11_YellowButton", "R12_LilacBell", "R13_LeafyHerb",
            "R14_RoundGroundcover", "R15_LowSeed"
        };
        // Catalog order: G1..G4, followed by R1..R15. Every recipe totals 100.
        static readonly float[][] Recipes = {
            new[] { 0f, 5, 15, 67, 0, 0, 0, .5f, 1, 4, 1, 0, .5f, .5f, 1, .5f, 1, 3, 0 },
            new[] { 34f, 26, 18, 10, .5f, .5f, 1, 1, 1, 3, 1, 0, 1, .75f, .75f, 1, .25f, 0, .25f },
            new[] { 4f, 16, 30, 36.8f, 0, 0, 0, .5f, .5f, 1, .5f, 1.2f, .5f, 0, .5f, 1, 3, 4.5f, 0 },
            new[] { 26f, 27, 16, 17, 1, 3, .25f, 1.5f, .25f, .5f, .25f, 0, 4, 0, .25f, 0, 1, 0, 2 }
        };

        [MenuItem("Weather House/Vegetation Lab/四种植物样区 · 安装到旧试验场")]
        public static void Install()
        {
            RequireEditMode();
            for (int i = 0; i < SceneManager.sceneCount; i++)
                Require(!SceneManager.GetSceneAt(i).isDirty, "请保存所有已打开场景后安装样区，避免覆盖未保存修改。");
            Require(File.Exists(VegetationLabBuilder.ScenePath), "原草地与水体试验场尚不存在，请先打开原场景。");
            Scene scene = SceneManager.GetActiveScene();
            if (scene.path != VegetationLabBuilder.ScenePath)
                scene = EditorSceneManager.OpenScene(VegetationLabBuilder.ScenePath, OpenSceneMode.Single);
            GrassMeadow main = FindMain(scene);
            GrassLabView view = FindView(scene);
            PlantLibraryCatalog catalog = ReadCatalog();
            string mainBefore = EditorJsonUtility.ToJson(main);
            Mesh[] mainMeshes = main.GetComponentsInChildren<MeshFilter>(true).Select(f => f.sharedMesh).ToArray();
            var samples = new GrassMeadow[4];
            for (int area = 0; area < samples.Length; area++)
            {
                string folder = Root + "/" + Ids[area];
                EnsureFolder(folder + "/Definitions");
                EnsureFolder(folder + "/Materials");
                GrassMeadow sample = FindSample(scene, area);
                bool created = !sample;
                if (created)
                {
                    sample = new GameObject(Names[area]).AddComponent<GrassMeadow>();
                    sample.seed = 5101 + area * 719;
                    sample.windStrength = main.windStrength;
                    sample.windSpeed = main.windSpeed;
                    sample.upNormals = main.upNormals;
                }
                if (created || sample.size == new Vector2(8, 7))
                {
                    // Migrate only the named small samples; keep all independently edited plant assets.
                    sample.transform.position = new Vector3(Positions[area].x, 0, Positions[area].y);
                    sample.size = main.size;
                    sample.totalClumps = Totals[area];
                    sample.weights = (float[])Recipes[area].Clone();
                }
                Require(sample.transform.rotation == Quaternion.identity && sample.transform.lossyScale == Vector3.one,
                    Names[area] + " 的根对象需要保持零旋转与单位缩放。");
                Require(Mathf.Abs(sample.transform.position.y) < .0001f, Names[area] + " 的根对象 Y 需要为 0。");
                samples[area] = sample;
                sample.groundOffset = new Vector2(sample.transform.position.x, sample.transform.position.z);
                sample.pondCenter = main.pondCenter - sample.groundOffset;
                sample.pondRadius = main.pondRadius;
                sample.waterLevel = main.waterLevel;
                sample.species = new GrassSpecies[PlantIds.Length];
                sample.materials = new Material[PlantIds.Length];
                for (int plant = 0; plant < PlantIds.Length; plant++)
                {
                    var entry = catalog.entries[plant];
                    sample.species[plant] = CopyDefinition(entry, folder);
                    sample.materials[plant] = CopyMaterial(entry, folder);
                }
                Require(sample.ConfigurationError == null, sample.ConfigurationError);
                AddSign(sample, Signs[area]);
                EditorUtility.SetDirty(sample);
            }
            view.regions = new[] { main, samples[0], samples[1], samples[2], samples[3] };
            view.meadow = main;
            EditorUtility.SetDirty(view);
            ExpandGround(scene, main);
            var navigation = view.GetComponent<GrassLabCamera>();
            Require(navigation, "原试验场缺少浏览相机控制器。");
            navigation.groundHalfExtents = new Vector2(79.5f, 69.5f);
            view.GetComponent<Camera>().farClipPlane = 350;
            // Keep the enlarged regions visible at the longer comparison-camera distances.
            RenderSettings.fogStartDistance = Mathf.Max(RenderSettings.fogStartDistance, 150);
            RenderSettings.fogEndDistance = Mathf.Max(RenderSettings.fogEndDistance, 400);
            EditorUtility.SetDirty(navigation);
            EditorUtility.SetDirty(view.GetComponent<Camera>());
            RequireOriginalUnchanged(main, mainBefore, mainMeshes);
            Require(EditorSceneManager.SaveScene(scene), "新增样区对象保存失败，尚未生成网格。");
            var ids = samples.Concat(new[] { main }).Select(s => GlobalObjectId.GetGlobalObjectIdSlow(s)).ToArray();
            Require(ids.All(id => id.targetObjectId != 0) && ids.Select(id => id.ToString()).Distinct().Count() == ids.Length,
                "样区需要互不重复且非零的持久对象 ID，已停止生成网格。");
            // Save the new objects first so the production mesh writer uses stable, separate folders.
            foreach (GrassMeadow sample in samples) sample.Rebuild();
            RequireOriginalUnchanged(main, mainBefore, mainMeshes);
            Verify();
            AssetDatabase.SaveAssets();
            Require(EditorSceneManager.SaveScene(scene), "样区已生成，但场景保存失败。");
            Selection.activeGameObject = samples[0].gameObject;
            Debug.Log("Plant biome samples installed: LowMeadow / TallFlowers / FernEdge / Wetland. Original meadow was not rebuilt.");
        }

        [MenuItem("Weather House/Vegetation Lab/四种植物样区 · 验证")]
        public static void Verify()
        {
            RequireEditMode();
            Scene scene = SceneManager.GetActiveScene();
            Require(scene.path == VegetationLabBuilder.ScenePath, "请在原 VegetationLab 场景中验证样区。");
            GrassMeadow main = FindMain(scene);
            var catalog = ReadCatalog();
            var definitions = new HashSet<GrassSpecies>(catalog.entries.Select(e => e.species).Concat(main.species));
            var materials = new HashSet<Material>(catalog.entries.Select(e => e.variants[0].GetComponent<MeshRenderer>().sharedMaterial).Concat(main.materials));
            var meshes = new HashSet<Mesh>(main.GetComponentsInChildren<MeshFilter>(true).Select(f => f.sharedMesh));
            MeshFilter ground = FindGround(scene);
            Mesh groundMesh = ground.sharedMesh;
            Require(groundMesh && AssetDatabase.GetAssetPath(groundMesh) == GroundPath &&
                groundMesh.indexFormat == IndexFormat.UInt32 && groundMesh.vertexCount == 321 * 281 &&
                ground.GetComponent<MeshCollider>().sharedMesh == groundMesh, "扩展地面网格或碰撞引用不完整。");
            Require(groundMesh.bounds.size.x >= 159.99f && groundMesh.bounds.size.z >= 139.99f,
                "扩展地面需要覆盖 160 × 140 米。");
            foreach (Vector3 vertex in groundMesh.vertices)
            {
                Vector3 world = ground.transform.TransformPoint(vertex);
                Require(Mathf.Abs(world.y - WorldGroundHeight(main, world.x, world.z)) < .0001f,
                    "扩展地面顶点与原草地世界地高不一致。");
            }
            var samples = new GrassMeadow[4];
            for (int area = 0; area < samples.Length; area++)
            {
                GrassMeadow sample = FindSample(scene, area);
                Require(sample, "缺少样区：" + Names[area]);
                samples[area] = sample;
                Require(sample.size == main.size, Names[area] + " 的范围需要与原池塘草地相同。");
                Require(sample.SpeciesCount == 19 && sample.ConfigurationError == null, Names[area] + " 需要完整的 19 个植物槽位。");
                Require(sample.Counts.SequenceEqual(GrassMeadow.Allocate(sample.totalClumps, sample.weights)),
                    Names[area] + " 的实际草丛数量与当前配方不一致，请先重新生成该样区。");
                string folder = Root + "/" + Ids[area] + "/";
                for (int plant = 0; plant < PlantIds.Length; plant++)
                {
                    Require(AssetDatabase.Contains(sample.species[plant]) &&
                        AssetDatabase.GetAssetPath(sample.species[plant]) == folder + "Definitions/" + PlantIds[plant] + ".asset" &&
                        definitions.Add(sample.species[plant]), Names[area] + " 的参数资产未独立保存。");
                    Require(AssetDatabase.Contains(sample.materials[plant]) &&
                        AssetDatabase.GetAssetPath(sample.materials[plant]) == folder + "Materials/" + PlantIds[plant] + ".mat" &&
                        materials.Add(sample.materials[plant]), Names[area] + " 的材质资产未独立保存。");
                }
                foreach (MeshFilter filter in sample.GetComponentsInChildren<MeshFilter>(true))
                    Require(filter.sharedMesh && AssetDatabase.Contains(filter.sharedMesh) && meshes.Add(filter.sharedMesh),
                        Names[area] + " 的生成网格没有独立保存。");
                foreach (float x in new[] { -sample.size.x * .5f, 0f, sample.size.x * .5f })
                    foreach (float z in new[] { -sample.size.y * .5f, 0f, sample.size.y * .5f })
                    {
                        Vector3 world = sample.transform.TransformPoint(new Vector3(x, 0, z));
                        float expected = WorldGroundHeight(main, world.x, world.z);
                        float actual = sample.GetGroundHeight(x, z) + sample.transform.position.y;
                        Require(Mathf.Abs(expected - actual) < .0001f, Names[area] + " 的世界地高与原地形不一致。");
                        Vector3 groundPoint = ground.transform.InverseTransformPoint(world);
                        Require(groundPoint.x >= groundMesh.bounds.min.x && groundPoint.x <= groundMesh.bounds.max.x &&
                            groundPoint.z >= groundMesh.bounds.min.z && groundPoint.z <= groundMesh.bounds.max.z,
                            Names[area] + " 超出扩展地面边界。");
                    }
            }
            for (int a = 0; a < samples.Length; a++)
                for (int b = a + 1; b < samples.Length; b++)
                {
                    float sumA = samples[a].weights.Sum(), sumB = samples[b].weights.Sum();
                    Require(sumA > 0 && sumB > 0 && Enumerable.Range(0, 19).Any(i =>
                        Mathf.Abs(samples[a].weights[i] / sumA - samples[b].weights[i] / sumB) > .01f),
                        Names[a] + " 与 " + Names[b] + " 的归一化配方没有明显区别。");
                }
            GrassLabView view = FindView(scene);
            Require(view.regions != null && view.regions.SequenceEqual(new[] { main, samples[0], samples[1], samples[2], samples[3] }),
                "运行面板没有连接原草地和四个独立样区。");
            Debug.Log("Plant biome checks PASS: four full-size regions, different recipes, exact counts, 76 independent definitions/materials, 90201 terrain vertices, 36 region ground/coverage samples and five panel regions.");
        }

        static void ExpandGround(Scene scene, GrassMeadow main)
        {
            MeshFilter ground = FindGround(scene);
            Require(ground.transform.position == Vector3.zero && ground.transform.rotation == Quaternion.identity &&
                ground.transform.lossyScale == Vector3.one, "原地面根对象需要保持原点、零旋转与单位缩放。");
            const int columns = 320, rows = 280;
            var vertices = new Vector3[(columns + 1) * (rows + 1)];
            var uv = new Vector2[vertices.Length];
            var triangles = new int[columns * rows * 6];
            for (int z = 0; z <= rows; z++)
                for (int x = 0; x <= columns; x++)
                {
                    int i = z * (columns + 1) + x;
                    float px = (x / (float)columns - .5f) * 160, pz = (z / (float)rows - .5f) * 140;
                    vertices[i] = new Vector3(px, WorldGroundHeight(main, px, pz), pz);
                    uv[i] = new Vector2(px / 80 + .5f, pz / 70 + .5f);
                }
            int index = 0;
            for (int z = 0; z < rows; z++)
                for (int x = 0; x < columns; x++)
                {
                    int i = z * (columns + 1) + x;
                    triangles[index++] = i; triangles[index++] = i + columns + 1; triangles[index++] = i + 1;
                    triangles[index++] = i + 1; triangles[index++] = i + columns + 1; triangles[index++] = i + columns + 2;
                }
            var mesh = new Mesh { name = "ExpandedGround", indexFormat = IndexFormat.UInt32,
                vertices = vertices, uv = uv, triangles = triangles };
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            var stored = AssetDatabase.LoadAssetAtPath<Mesh>(GroundPath);
            if (stored)
            {
                GrassMeshFactory.UpdateStoredMesh(mesh, stored);
                Object.DestroyImmediate(mesh);
                EditorUtility.SetDirty(stored);
            }
            else
            {
                Require(!File.Exists(GroundPath), "扩展地面路径已被其他资产占用。");
                stored = mesh; AssetDatabase.CreateAsset(stored, GroundPath);
            }
            ground.sharedMesh = stored;
            MeshCollider collider = ground.GetComponent<MeshCollider>();
            collider.sharedMesh = null; collider.sharedMesh = stored;
            EditorUtility.SetDirty(ground); EditorUtility.SetDirty(collider);
        }

        static float WorldGroundHeight(GrassMeadow main, float x, float z)
        {
            Vector3 local = main.transform.InverseTransformPoint(new Vector3(x, 0, z));
            return main.GetGroundHeight(local.x, local.z) + main.transform.position.y;
        }

        static MeshFilter FindGround(Scene scene)
        {
            var matches = scene.GetRootGameObjects().Where(g => g.name.StartsWith("02 ", StringComparison.Ordinal) &&
                g.GetComponent<MeshFilter>() && g.GetComponent<MeshCollider>()).ToArray();
            Require(matches.Length == 1 && matches[0].GetComponent<MeshFilter>() &&
                matches[0].GetComponent<MeshCollider>() && matches[0].GetComponent<MeshRenderer>(),
                "需要唯一的原始 02 地面对象及其网格、碰撞和材质组件。");
            return matches[0].GetComponent<MeshFilter>();
        }

        static GrassSpecies CopyDefinition(PlantLibraryCatalog.Entry entry, string folder)
        {
            string path = folder + "/Definitions/" + entry.id + ".asset";
            var asset = AssetDatabase.LoadAssetAtPath<GrassSpecies>(path);
            if (asset) return asset;
            Require(!File.Exists(path), "参数资产路径被其他类型占用：" + path);
            asset = Object.Instantiate(entry.species); asset.name = entry.id;
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }

        static Material CopyMaterial(PlantLibraryCatalog.Entry entry, string folder)
        {
            string path = folder + "/Materials/" + entry.id + ".mat";
            var asset = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (asset) return asset;
            Require(!File.Exists(path), "材质资产路径被其他类型占用：" + path);
            asset = new Material(entry.variants[0].GetComponent<MeshRenderer>().sharedMaterial) { name = entry.id };
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }

        static void AddSign(GrassMeadow sample, string text)
        {
            Transform existing = sample.transform.Find("样区标牌");
            var label = existing ? existing.GetComponent<TextMesh>() : new GameObject("样区标牌").AddComponent<TextMesh>();
            Require(label, "样区标牌名称已被其他对象占用。");
            label.transform.SetParent(sample.transform, false);
            float front = -sample.size.y * .5f - .5f;
            label.transform.localPosition = new Vector3(0, sample.GetGroundHeight(0, front) + .035f, front);
            label.transform.localRotation = Quaternion.Euler(90, 0, 0);
            label.text = text; label.fontSize = 52; label.characterSize = .015f;
            label.anchor = TextAnchor.MiddleCenter; label.alignment = TextAlignment.Center;
            label.color = new Color(.88f, .91f, .72f);
        }

        static PlantLibraryCatalog ReadCatalog()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<PlantLibraryCatalog>(PlantLibraryBuilder.CatalogPath);
            Require(catalog && catalog.entries != null && catalog.entries.Select(e => e.id).SequenceEqual(PlantIds),
                "请先生成具有完整 G1–G4、R1–R15 顺序的植物资源库。");
            Require(catalog.entries.All(e => e.species && e.variants != null && e.variants.Length > 0 && e.variants[0] &&
                e.variants[0].GetComponent<MeshRenderer>() && e.variants[0].GetComponent<MeshRenderer>().sharedMaterial),
                "植物资源库存在缺失参数、预制体或材质。");
            return catalog;
        }

        static GrassMeadow FindMain(Scene scene)
        {
            var matches = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<GrassMeadow>(true))
                .Where(m => m.SpeciesCount == 3 && m.species[0] && m.species[0].kind == GrassKind.ShortGrass &&
                    m.species[1] && m.species[1].kind == GrassKind.TallGrass && m.species[2] && m.species[2].kind == GrassKind.Daisy).ToArray();
            Require(matches.Length == 1, "需要且只能有一个原始三草种草地控制器。");
            return matches[0];
        }

        static GrassLabView FindView(Scene scene)
        {
            var views = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<GrassLabView>(true)).ToArray();
            Require(views.Length == 1, "原试验场需要且只能有一个参数面板。");
            return views[0];
        }

        static GrassMeadow FindSample(Scene scene, int area)
        {
            var roots = scene.GetRootGameObjects().Where(g => g.name == Names[area]).ToArray();
            Require(roots.Length <= 1, "存在重名样区：" + Names[area]);
            if (roots.Length == 0) return null;
            GrassMeadow sample = roots[0].GetComponent<GrassMeadow>();
            Require(sample, "样区名称已被其他对象占用：" + Names[area]);
            return sample;
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = path.Substring(0, path.LastIndexOf('/'));
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, path.Substring(path.LastIndexOf('/') + 1));
        }

        static void RequireOriginalUnchanged(GrassMeadow main, string json, Mesh[] meshes)
        {
            Require(EditorJsonUtility.ToJson(main) == json &&
                main.GetComponentsInChildren<MeshFilter>(true).Select(f => f.sharedMesh).SequenceEqual(meshes),
                "原草地参数或网格发生变化，已停止保存场景。");
        }

        static void RequireEditMode() { Require(!EditorApplication.isPlayingOrWillChangePlaymode, "请退出 Play 后安装或验证样区。"); }
        static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    }
}
