using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace WeatherHouse.Vegetation.Editor
{
    public static class PlantLibraryBuilder
    {
        public const string Root = "Assets/WeatherHouseVegetationLab/PlantLibrary";
        public const string ScenePath = Root + "/PlantLibrary.unity";
        public const string CatalogPath = Root + "/PlantLibraryCatalog.asset";
        static readonly string[] Ids = { "G1_TallBroad", "G2_Upright", "G3_OpenLow", "G4_DenseShort", "R01_Plume", "R02_BrownSeed", "R03_YellowSpike", "R04_PaleSpike", "R05_WhiteSpray", "R06_Daisy", "R07_BlueFlower", "R08_Fern", "R09_CreamFoxtail", "R10_OrangeFlower", "R11_YellowButton", "R12_LilacBell", "R13_LeafyHerb", "R14_RoundGroundcover", "R15_LowSeed" };
        static readonly string[] Labels = { "高草 · 宽叶分层", "中高草 · 直立细束", "中低草 · 疏落斜叶", "短草 · 密实宽叶", "奶白羽穗草", "棕色直立穗草", "黄色长花序", "疏生浅白花穗", "分枝白色碎花", "白色雏菊", "浅蓝小花", "羽状蕨叶", "奶白短卵穗", "橙色小花", "黄色纽扣花", "浅紫杯形花", "低矮披针叶草本", "圆叶匍匐地被", "低位稀疏浅穗草" };
        static readonly string[] Forms = { "长短层叠，宽叶、束根，较密", "狭窄根部与细长直立叶，轮廓收束", "疏叶与斜叶，分布不对称", "高度最低，叶数最多，紧凑且偏宽", "高挑细秆与弯垂羽毛状乳白穗", "细长直立秆与深棕圆柱穗", "黄色小花沿高轴间隔排列", "中等高度的疏生浅白花序", "分枝茎上的细碎白花", "白色圆瓣与黄色花心", "细茎浅蓝小花", "弯曲叶轴与成对小叶", "密集奶白卵形短穗", "橙色圆瓣小花", "黄色细茎纽扣状花头", "浅紫杯形花冠", "低矮多层披针叶", "圆阔叶成组的低位地被", "低位浅色小穗，较疏" };
        static readonly GrassKind[] Kinds = { GrassKind.TallGrass, GrassKind.TallGrass, GrassKind.ShortGrass, GrassKind.ShortGrass, GrassKind.Plume, GrassKind.SeedHead, GrassKind.FlowerSpike, GrassKind.FlowerSpike, GrassKind.FlowerSpray, GrassKind.Daisy, GrassKind.Wildflower, GrassKind.Fern, GrassKind.Foxtail, GrassKind.Wildflower, GrassKind.Wildflower, GrassKind.Bellflower, GrassKind.LeafyHerb, GrassKind.RoundGroundcover, GrassKind.Foxtail };
        static readonly float[] Heights = { .90f, .64f, .46f, .25f, 1.85f, 1.05f, 1.10f, .80f, .52f, .38f, .34f, .65f, .68f, .36f, .30f, .35f, .22f, .16f, .38f };
        static readonly float[] Mix = { 18, 24, 23, 26, .25f, .35f, .3f, .5f, .8f, 1.4f, .6f, .35f, .7f, .6f, .7f, .7f, .3f, 1, .45f };

        [MenuItem("Weather House/Vegetation Lab/植物资源库 · 创建或打开")]
        public static void OpenOrCreate()
        {
            RequireSavedScene();
            if (File.Exists(ScenePath)) { EditorSceneManager.OpenScene(ScenePath); return; }
            BuildResources();
            CreateScene();
        }

        static void RequireSavedScene()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("请退出 Play 后打开植物资源库。");
            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("请保存当前场景后继续，避免覆盖未保存修改。");
        }

        [MenuItem("Weather House/Vegetation Lab/植物资源库 · 刷新资源保留参数")]
        public static void BuildResources()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("请退出 Play 后刷新资源。");
            foreach (string folder in new[] { Root, Root + "/Definitions", Root + "/Materials", Root + "/Meshes", Root + "/Prefabs" }) Directory.CreateDirectory(folder);
            AssetDatabase.Refresh();
            var catalog = AssetDatabase.LoadAssetAtPath<PlantLibraryCatalog>(CatalogPath);
            if (!catalog) { catalog = ScriptableObject.CreateInstance<PlantLibraryCatalog>(); AssetDatabase.CreateAsset(catalog, CatalogPath); }
            catalog.entries = new PlantLibraryCatalog.Entry[Ids.Length];
            for (int i = 0; i < Ids.Length; i++)
            {
                var species = AssetDatabase.LoadAssetAtPath<GrassSpecies>(Root + "/Definitions/" + Ids[i] + ".asset");
                if (!species)
                {
                    species = ScriptableObject.CreateInstance<GrassSpecies>(); species.name = Ids[i];
                    Defaults(species, i);
                    AssetDatabase.CreateAsset(species, Root + "/Definitions/" + Ids[i] + ".asset");
                }
                var entry = new PlantLibraryCatalog.Entry { id = Ids[i], species = species, reference = i < 4 ? "四档草图，从左至右 G" + (i + 1) : "资源总览红框 R" + (i - 3), silhouette = Forms[i], variants = new GameObject[4], uncertainty = i == 7 ? "R4 与 R9 可能属于同类植物，按疏密和花序差异分别制作。" : i >= 16 ? "原图分辨率不足以确认物种，按可见轮廓制作；不作生物学命名。" : "按参考外形重建，米制高度与数量为本项目制作设定。" };
                catalog.entries[i] = entry;
                RefreshEntry(entry);
            }
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();
        }

        static Color C(string hex) { ColorUtility.TryParseHtmlString("#" + hex, out var color); return color; }
        static void Defaults(GrassSpecies s, int i)
        {
            s.displayName = Labels[i]; s.kind = Kinds[i]; s.height = Heights[i]; s.seed = 1301 + i * 157;
            s.baseColor = C(i == 11 || i == 17 ? "728A3D" : "899540");
            s.tipColor = C(i == 11 || i == 17 ? "AFBF67" : "D7DC78");
            s.tipWidthRatio = .11f; s.width = .025f; s.bladeCount = 9; s.rootSpread = .10f; s.spread = .16f;
            s.bend = .18f; s.heightVariation = .2f; s.outerBladeFraction = .25f; s.clumpLayering = .25f;
            s.flowerColor = C("F2EDCA"); s.centerColor = C("D8B65B"); s.flowerCount = 4; s.flowerRadius = .047f;
            s.petalCount = 6; s.headLength = .22f; s.leafAspect = 1; s.curl = .2f;
            if (i < 4)
            {
                s.width = new[] { .055f, .021f, .023f, .032f }[i]; s.bladeCount = new[] { 28, 16, 11, 34 }[i];
                s.spread = new[] { .24f, .12f, .19f, .075f }[i]; s.rootSpread = new[] { .09f, .05f, .12f, .16f }[i];
                s.bend = new[] { .28f, .10f, .08f, .30f }[i]; s.heightVariation = new[] { .30f, .12f, .33f, .24f }[i];
                s.outerBladeFraction = new[] { .40f, .125f, .60f, .45f }[i]; s.clumpLayering = new[] { .65f, .08f, .18f, .70f }[i];
                return;
            }
            switch (i)
            {
                case 4: s.flowerCount = 6; s.bladeCount = 12; s.headLength = .60f; s.width = .035f; s.spread = .25f; s.bend = .65f; s.curl = .8f; break;
                case 5: s.flowerColor = C("866E40"); s.flowerCount = 5; s.headLength = .18f; s.flowerRadius = .026f; s.curl = .05f; break;
                case 6: s.flowerColor = C("E9DA66"); s.flowerCount = 3; s.headLength = .48f; s.flowerRadius = .038f; break;
                case 7: s.flowerCount = 3; s.headLength = .35f; s.flowerRadius = .036f; s.spread = .15f; break;
                case 8: s.flowerCount = 5; s.flowerRadius = .029f; s.spread = .19f; break;
                case 9: s.flowerColor = C("FFF9DC"); s.flowerCount = 4; s.flowerRadius = .065f; s.bladeCount = 6; break;
                case 10: s.flowerColor = C("BBCEDC"); s.petalCount = 5; s.flowerCount = 4; break;
                case 11: s.bladeCount = 22; s.width = .09f; s.spread = .28f; s.leafAspect = 1.35f; s.curl = .4f; s.bend = .5f; break;
                case 12: s.flowerCount = 7; s.headLength = .15f; s.flowerRadius = .042f; s.spread = .20f; break;
                case 13: s.flowerColor = C("EFBB7A"); s.flowerCount = 5; s.petalCount = 7; s.flowerRadius = .052f; break;
                case 14: s.flowerColor = C("ECDD69"); s.centerColor = C("D4AD4A"); s.petalCount = 9; s.flowerCount = 5; s.flowerRadius = .029f; break;
                case 15: s.flowerColor = C("CFBBD8"); s.petalCount = 5; s.flowerCount = 4; s.flowerRadius = .06f; s.curl = .25f; break;
                case 16: s.bladeCount = 12; s.width = .045f; s.spread = .17f; s.leafAspect = .65f; break;
                case 17: s.bladeCount = 14; s.width = .068f; s.spread = .29f; s.leafAspect = 1.25f; s.bend = .32f; break;
                case 18: s.flowerCount = 3; s.headLength = .085f; s.flowerRadius = .025f; s.bladeCount = 7; s.spread = .15f; break;
            }
        }

        public static void RefreshSpecies(GrassSpecies species)
        {
            var catalog = AssetDatabase.LoadAssetAtPath<PlantLibraryCatalog>(CatalogPath);
            var entry = catalog ? catalog.entries.FirstOrDefault(e => e.species == species) : null;
            if (entry == null) throw new InvalidOperationException("这个 Source 不在植物资源库中。");
            RefreshEntry(entry); EditorUtility.SetDirty(catalog); AssetDatabase.SaveAssets();
        }

        static void RefreshEntry(PlantLibraryCatalog.Entry entry)
        {
            string path = Root + "/Materials/" + entry.id + ".mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!mat) { mat = new Material(Shader.Find("Weather House/Painterly Grass")); AssetDatabase.CreateAsset(mat, path); }
            mat.enableInstancing = true;
            mat.SetColor("_BaseColor", entry.species.baseColor); mat.SetColor("_TipColor", entry.species.tipColor);
            mat.SetFloat("_UpNormals", 1); mat.SetFloat("_WindStrength", .08f); mat.SetFloat("_WindSpeed", .65f);
            EditorUtility.SetDirty(mat);
            var staging = EditorSceneManager.NewPreviewScene();
            try
            {
                for (int v = 0; v < 4; v++)
                {
                    string name = entry.id + "_v" + (v + 1);
                    var mesh = StoreMesh(GrassMeshFactory.Build(entry.species, v), name);
                    var go = new GameObject(name);
                    SceneManager.MoveGameObjectToScene(go, staging);
                    try
                    {
                        go.AddComponent<MeshFilter>().sharedMesh = mesh;
                        var renderer = go.AddComponent<MeshRenderer>(); renderer.sharedMaterial = mat; renderer.shadowCastingMode = ShadowCastingMode.TwoSided;
                        var specimen = go.AddComponent<PlantSpecimen>(); specimen.source = entry.species; specimen.variation = v;
                        entry.variants[v] = PrefabUtility.SaveAsPrefabAsset(go, Root + "/Prefabs/" + name + ".prefab");
                    }
                    finally { Object.DestroyImmediate(go); }
                }
            }
            finally { EditorSceneManager.ClosePreviewScene(staging); }
        }

        static Mesh StoreMesh(Mesh mesh, string name)
        {
            mesh.name = name;
            string path = Root + "/Meshes/" + name + ".asset";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (!existing) { AssetDatabase.CreateAsset(mesh, path); return mesh; }
            GrassMeshFactory.UpdateStoredMesh(mesh, existing); Object.DestroyImmediate(mesh); EditorUtility.SetDirty(existing); return existing;
        }

        static Material Solid(string name, Color color)
        {
            string path = Root + "/Materials/" + name + ".mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!mat) { mat = new Material(Shader.Find("Universal Render Pipeline/Lit")); mat.SetColor("_BaseColor", color); mat.SetFloat("_Smoothness", .1f); AssetDatabase.CreateAsset(mat, path); }
            return mat;
        }

        static void CreateScene()
        {
            RequireSavedScene();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var catalog = AssetDatabase.LoadAssetAtPath<PlantLibraryCatalog>(CatalogPath);
            var meadow = new GameObject("01 混合草地 · 19种实时参数").AddComponent<GrassMeadow>();
            meadow.species = catalog.entries.Select(e => e.species).ToArray();
            meadow.materials = catalog.entries.Select(e => e.variants[0].GetComponent<MeshRenderer>().sharedMaterial).ToArray();
            meadow.weights = (float[])Mix.Clone(); meadow.totalClumps = 1400; meadow.size = new Vector2(8, 9); meadow.pondCenter = new Vector2(100, 100); meadow.pondRadius = 1;
            meadow.windStrength = .08f; meadow.windSpeed = .65f;
            Ground(meadow);
            var tile = Solid("SpecimenSoil", C("ACB29E"));
            var group = new GameObject("02 19种样本 · 每种四个种子变体 · 原尺寸").transform;
            for (int i = 0; i < catalog.entries.Length; i++)
            {
                float x = -19 + (i % 4) * 3.7f, z = -5.5f + (i / 4) * 3;
                var cell = new GameObject(catalog.entries[i].id + " " + Labels[i]).transform; cell.SetParent(group);
                cell.position = new Vector3(x, 3.1f, z);
                Box(cell, "展示台", new Vector3(0, -.1f, 0), new Vector3(3.4f, .15f, 2.7f), tile);
                Label(cell, catalog.entries[i].id.Replace('_', ' '), new Vector3(-1.45f, .02f, -1.17f), .12f);
                for (int v = 0; v < 4; v++)
                {
                    var plant = (GameObject)PrefabUtility.InstantiatePrefab(catalog.entries[i].variants[v]);
                    plant.transform.SetParent(cell); plant.transform.localPosition = new Vector3((v % 2 - .5f) * 1.1f, 0, (v / 2 - .5f) * 1.0f + .15f);
                    plant.transform.localRotation = Quaternion.Euler(0, v * 73, 0);
                }
            }
            var lineup = new GameObject("03 四档草同尺度对比 · G1至G4").transform;
            lineup.position = new Vector3(-10.5f, 3.1f, -10);
            Box(lineup, "四档同尺度台", new Vector3(0, -.1f, 0), new Vector3(5.2f, .15f, 2.4f), tile);
            for (int i = 0; i < 4; i++)
            {
                var go = (GameObject)PrefabUtility.InstantiatePrefab(catalog.entries[i].variants[0]); go.transform.SetParent(lineup);
                go.transform.localPosition = new Vector3(-1.8f + i * 1.2f, 0, .2f);
                Label(lineup, "G" + (i + 1) + "  " + Heights[i].ToString("0.00") + "m", new Vector3(-2.25f + i * 1.2f, .02f, -.65f), .12f);
            }
            RenderSettings.fog = false; RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = C("D8E7DE"); RenderSettings.ambientEquatorColor = C("B8C2A7"); RenderSettings.ambientGroundColor = C("797A5C"); RenderSettings.ambientIntensity = 1;
            var sun = new GameObject("明亮柔和日光").AddComponent<Light>(); sun.type = LightType.Directional; sun.intensity = 1.05f; sun.color = C("FFF4DC"); sun.transform.rotation = Quaternion.Euler(47, -35, 0); sun.shadows = LightShadows.Soft; RenderSettings.sun = sun;
            var camera = new GameObject("04 摄像机 · 按1总览 2四档草 3植物近景 4步行 5飞行").AddComponent<Camera>();
            camera.tag = "MainCamera"; camera.nearClipPlane = .03f; camera.farClipPlane = 120; camera.fieldOfView = 48; camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = C("D6E3D7");
            camera.GetUniversalAdditionalCameraData().renderPostProcessing = false;
            var nav = camera.gameObject.AddComponent<GrassLabCamera>(); nav.meadow = meadow; nav.libraryLayout = true; nav.Overview();
            var view = camera.gameObject.AddComponent<GrassLabView>(); view.meadow = meadow;
            EditorSceneManager.SaveScene(SceneManager.GetActiveScene(), ScenePath);
            meadow.Rebuild();
            AssetDatabase.SaveAssets(); EditorSceneManager.SaveScene(SceneManager.GetActiveScene(), ScenePath);
            Selection.activeGameObject = meadow.gameObject;
        }

        static void Box(Transform parent, string name, Vector3 position, Vector3 scale, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube); go.name = name; go.transform.SetParent(parent, false); go.transform.localPosition = position; go.transform.localScale = scale; go.GetComponent<MeshRenderer>().sharedMaterial = mat;
        }
        static void Label(Transform parent, string text, Vector3 position, float size)
        {
            var go = new GameObject(text); go.transform.SetParent(parent, false); go.transform.localPosition = position; go.transform.localRotation = Quaternion.Euler(90, 0, 0);
            var label = go.AddComponent<TextMesh>(); label.text = text; label.fontSize = 52; label.characterSize = size / 8; label.color = C("435046"); label.anchor = TextAnchor.MiddleLeft;
        }
        public static void RefreshScenePresentation()
        {
            RequireSavedScene();
            BuildResources();
            EditorSceneManager.OpenScene(ScenePath);
            foreach (var label in Object.FindObjectsOfType<TextMesh>()) label.characterSize = .015f;
            var lineup = GameObject.Find("03 四档草同尺度对比 · G1至G4").transform;
            lineup.Find("四档同尺度台").localScale = new Vector3(5.2f, .15f, 2.4f);
            var specimens = lineup.GetComponentsInChildren<PlantSpecimen>();
            for (int i = 0; i < specimens.Length; i++) specimens[i].transform.localPosition = new Vector3(-1.8f + i * 1.2f, 0, .2f);
            var labels = lineup.GetComponentsInChildren<TextMesh>();
            for (int i = 0; i < labels.Length; i++) { labels[i].text = "G" + (i + 1) + "  " + Heights[i].ToString("0.00") + "m"; labels[i].transform.localPosition = new Vector3(-2.25f + i * 1.2f, .02f, -.65f); }
            Object.FindObjectOfType<GrassMeadow>().Rebuild();
            Object.FindObjectOfType<GrassLabCamera>().Overview();
            EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
        }
        static void Ground(GrassMeadow meadow)
        {
            const int nx = 64, nz = 48;
            var vertices = new Vector3[(nx + 1) * (nz + 1)]; var triangles = new int[nx * nz * 6]; var uv = new Vector2[vertices.Length];
            for (int z = 0; z <= nz; z++) for (int x = 0; x <= nx; x++)
            {
                int k = z * (nx + 1) + x; float px = -23 + 31f * x / nx, pz = -13 + 25f * z / nz;
                vertices[k] = new Vector3(px, meadow.GetGroundHeight(px, pz) - .015f, pz); uv[k] = new Vector2(px * .1f, pz * .1f);
            }
            int t = 0;
            for (int z = 0; z < nz; z++) for (int x = 0; x < nx; x++) { int k = z * (nx + 1) + x; triangles[t++] = k; triangles[t++] = k + nx + 1; triangles[t++] = k + 1; triangles[t++] = k + 1; triangles[t++] = k + nx + 1; triangles[t++] = k + nx + 2; }
            var mesh = new Mesh { vertices = vertices, triangles = triangles, uv = uv }; mesh.RecalculateNormals(); mesh.RecalculateBounds();
            var go = new GameObject("00 中性地面"); go.AddComponent<MeshFilter>().sharedMesh = StoreMesh(mesh, "LibraryGround"); go.AddComponent<MeshRenderer>().sharedMaterial = Solid("LibraryGround", C("879083"));
        }
    }
}
