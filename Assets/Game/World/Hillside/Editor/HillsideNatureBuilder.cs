using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using WeatherHouse.Vegetation;
using Object = UnityEngine.Object;

namespace WeatherHouse.Hillside.Editor
{
    public static class HillsideNatureBuilder
    {
        const string Source = "Assets/Experiments/Vegetation/Generated/";
        const string TreeSource = "Assets/HIGHLANDS - Stylized Environment/Prefabs/Forest/";
        const int NatureSeed = 431;

        public static GameObject Build(Terrain terrain, string outputFolder, Transform parent)
        {
            if (!terrain || !terrain.terrainData) throw new ArgumentException("Nature needs the generated terrain.");
            if (!outputFolder.StartsWith("Assets/", StringComparison.Ordinal)) throw new ArgumentException("Nature output must be an Assets folder.");
            string folder = outputFolder.TrimEnd('/') + "/Nature";
            Directory.CreateDirectory(folder);
            AssetDatabase.Refresh();
            var root = new GameObject("02 主草地 · 杉树林 · 画意水面");
            root.transform.SetParent(parent, false);
            BuildGrass(terrain, folder, root.transform);
            BuildTrees(terrain, root.transform);
            BuildWater(terrain, folder, root.transform);
            Verify(root);
            return root;
        }

        static void BuildGrass(Terrain terrain, string folder, Transform parent)
        {
            var defaults = Required<GrassMeadowDefaults>(Source + "MeadowDefaults/Region0.asset");
            if (defaults.plants == null || defaults.plants.Length != 3 || defaults.weights.Length != 3)
                throw new InvalidOperationException("The original main meadow needs its three saved species.");
            var go = new GameObject("原主草地 · 原草形与比例 · 空间分块");
            go.transform.SetParent(parent, false);
            var grass = go.AddComponent<HillsideGrassRenderer>();
            grass.terrain = terrain;
            grass.seed = defaults.seed;
            grass.speciesWeights = (float[])defaults.weights.Clone();
            grass.clumpsPerSquareMeter = defaults.totalClumps / (42f * 34f);
            grass.variants = new Mesh[12];
            grass.materials = new Material[3];
            string[] names = { "ShortGrass", "TallGrass", "WhiteDaisy" };
            for (int i = 0; i < names.Length; i++)
            {
                var species = Object.Instantiate(Required<GrassSpecies>(Source + names[i] + ".asset"));
                try
                {
                    JsonUtility.FromJsonOverwrite(defaults.plants[i], species);
                    species.name = names[i];
                    for (int variation = 0; variation < 4; variation++)
                        grass.variants[i * 4 + variation] = Store(GrassMeshFactory.Build(species, variation),
                            folder + "/" + names[i] + "_Variant" + variation + ".asset");
                    var material = new Material(Required<Material>(Source + names[i] + ".mat")) { name = names[i], enableInstancing = true };
                    material.SetColor("_BaseColor", species.baseColor);
                    material.SetColor("_TipColor", species.tipColor);
                    material.SetFloat("_WindStrength", defaults.windStrength);
                    material.SetFloat("_WindSpeed", defaults.windSpeed);
                    material.SetFloat("_UpNormals", defaults.upNormals);
                    grass.materials[i] = Store(material, folder + "/" + names[i] + ".mat");
                }
                finally { Object.DestroyImmediate(species); }
            }
            MatchMeadowGround(terrain, folder, grass.materials[0]);
        }

        static void MatchMeadowGround(Terrain terrain, string folder, Material grass)
        {
            TerrainLayer[] layers = terrain.terrainData.terrainLayers;
            if (layers.Length == 0) throw new InvalidOperationException("Meadow ground needs the generated terrain's first layer.");
            var texture = new Texture2D(4, 4, TextureFormat.RGB24, false) { name = "主草地远景底色" };
            Color color = Color.Lerp(grass.GetColor("_MeadowBaseColor"), grass.GetColor("_MeadowPatchColor"), .4f);
            texture.SetPixels(Enumerable.Repeat(color, 16).ToArray());
            texture.Apply();
            texture = Store(texture, folder + "/MeadowGroundColor.asset");
            // Clone the generated layer so rebuilding this scene cannot alter the calibrated source meadow.
            var layer = Object.Instantiate(layers[0]);
            layer.name = "主草地连续底色";
            layer.diffuseTexture = texture;
            layers[0] = Store(layer, folder + "/MeadowGround.terrainlayer");
            terrain.terrainData.terrainLayers = layers;
            EditorUtility.SetDirty(terrain.terrainData);
            terrain.Flush();
        }

        static void BuildTrees(Terrain terrain, Transform parent)
        {
            var group = new GameObject("杉树丛组 · 复用主草地最终树叶材质");
            group.transform.SetParent(parent, false);
            group.AddComponent<HillsideTreeOcclusion>();
            var leaves = Required<Material>(Source + "Backdrop_Tall_Fir_Branch.mat");
            GameObject[] prefabs = Enumerable.Range(1, 3).Select(i => Required<GameObject>(TreeSource + "Fir_Tall_" + i + ".prefab")).ToArray();
            var random = new System.Random(NatureSeed + 7001);
            var positions = new List<Vector3>();
            Vector2[] groves = {
                new Vector2(9, 69), new Vector2(8, 55), new Vector2(8, 43), new Vector2(9, 29), new Vector2(20, 27),
                new Vector2(36, 19), new Vector2(49, 14), new Vector2(35, 43), new Vector2(47, 38),
                new Vector2(54, 65), new Vector2(67, 64), new Vector2(83, 64), new Vector2(91, 50),
                new Vector2(93, 30), new Vector2(85, 13), new Vector2(59, 83), new Vector2(14, 91)
            };
            for (int grove = 0; grove < groves.Length; grove++)
            {
                Vector3 center = HillsideLayout.Design(groves[grove].x, groves[grove].y);
                bool westernForest = groves[grove].x < 50;
                int target = westernForest ? 24 : 8;
                int placed = 0;
                for (int attempt = 0; attempt < target * 30 && placed < target; attempt++)
                {
                    float angle = (float)random.NextDouble() * Mathf.PI * 2;
                    float radius = Mathf.Sqrt((float)random.NextDouble()) * 19;
                    Vector3 point = center + new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * radius;
                    if (!TreeSite(terrain, point) || positions.Any(p => FlatDistance(p, point) < (westernForest ? 4.2f : 6.5f))) continue;
                    point.y = terrain.SampleHeight(point) + terrain.transform.position.y;
                    float yaw = (float)random.NextDouble() * 360;
                    float height = Mathf.Lerp(westernForest ? 13 : 11, westernForest ? 19 : 16, (float)random.NextDouble());
                    float width = Mathf.Lerp(westernForest ? 1.55f : 1.35f, westernForest ? 1.9f : 1.6f, (float)random.NextDouble());
                    PlaceTree(prefabs[(grove + placed) % 3], point, height, width, yaw, group.transform, leaves,
                        "杉树 " + (positions.Count + 1).ToString("D3"));
                    positions.Add(point); placed++;
                }
            }
            if (positions.Count < 60) throw new InvalidOperationException("The hillside needs at least 60 valid grove trees; found " + positions.Count + ".");
            Vector3 oldTree = HillsideLayout.Design(16, 47);
            oldTree.y = terrain.SampleHeight(oldTree) + terrain.transform.position.y;
            PlaceTree(prefabs[0], oldTree, 12, 1.65f, 32, group.transform, leaves, "B 老树地标 · 主草地杉树");
            Vector3 entranceTree = HillsideLayout.Design(12, 76);
            entranceTree.y = terrain.SampleHeight(entranceTree) + terrain.transform.position.y;
            PlaceTree(prefabs[2], entranceTree, 11, 1.55f, 148, group.transform, leaves, "A 入口框景树 · 主草地杉树");
        }

        static void PlaceTree(GameObject prefab, Vector3 point, float height, float width, float yaw, Transform parent, Material leaves, string name)
        {
            var tree = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent.gameObject.scene);
            tree.name = name;
            tree.transform.SetParent(parent, false);
            tree.transform.SetPositionAndRotation(point, Quaternion.Euler(0, yaw, 0));
            Renderer[] renderers = tree.GetComponentsInChildren<Renderer>();
            Bounds bounds = renderers[0].bounds;
            foreach (Renderer renderer in renderers) bounds.Encapsulate(renderer.bounds);
            tree.transform.localScale *= height / Mathf.Max(.1f, bounds.size.y);
            tree.transform.localScale = Vector3.Scale(tree.transform.localScale, new Vector3(width, 1, width));
            bounds = renderers[0].bounds;
            foreach (Renderer renderer in renderers)
            {
                bounds.Encapsulate(renderer.bounds);
                Material[] slots = renderer.sharedMaterials;
                for (int i = 0; i < slots.Length; i++)
                    if (slots[i] && slots[i].name == "Tall_Fir_Branch") slots[i] = leaves;
                renderer.sharedMaterials = slots;
                PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
            }
            tree.transform.position += Vector3.up * (point.y - bounds.min.y - .08f);
            foreach (LODGroup lod in tree.GetComponentsInChildren<LODGroup>())
            {
                LOD[] levels = lod.GetLODs();
                if (levels.Length > 0) { levels[levels.Length - 1].screenRelativeTransitionHeight = .001f; lod.SetLODs(levels); }
                PrefabUtility.RecordPrefabInstancePropertyModifications(lod);
            }
            PrefabUtility.RecordPrefabInstancePropertyModifications(tree);
            PrefabUtility.RecordPrefabInstancePropertyModifications(tree.transform);
        }

        static bool TreeSite(Terrain terrain, Vector3 point)
        {
            Vector3 origin = terrain.transform.position, size = terrain.terrainData.size;
            float u = (point.x - origin.x) / size.x, v = (point.z - origin.z) / size.z;
            if (u < .025f || u > .975f || v < .025f || v > .975f ||
                HillsideLayout.IsWater(point.x, point.z, 3) || HillsideLayout.PathEdgeDistance(point.x, point.z) < 3.5f ||
                InEntranceViewCorridor(point) ||
                terrain.terrainData.GetSteepness(u, v) > 32) return false;
            return FlatDistance(point, HillsideLayout.A) > 12 && FlatDistance(point, HillsideLayout.B) > 14 &&
                FlatDistance(point, HillsideLayout.C) > 13 && FlatDistance(point, HillsideLayout.E) > 18 &&
                FlatDistance(point, HillsideLayout.D) > 23;
        }

        static bool InEntranceViewCorridor(Vector3 point)
        {
            // The reused orbit camera starts behind spawn at yaw 42. Keep large crowns beside that view.
            Vector3 spawn = HillsideLayout.Design(17, 83);
            Vector3 forward = Quaternion.Euler(0, 42, 0) * Vector3.forward;
            Vector3 delta = point - spawn;
            float along = Vector3.Dot(delta, forward);
            Vector3 closest = spawn + forward * Mathf.Clamp(along, -30, 5);
            return FlatDistance(point, closest) < 13;
        }

        static float FlatDistance(Vector3 a, Vector3 b)
        {
            return Vector2.Distance(new Vector2(a.x, a.z), new Vector2(b.x, b.z));
        }

        static void BuildWater(Terrain terrain, string folder, Transform parent)
        {
            var material = Store(new Material(Required<Material>(Source + "PainterlyWater.mat")) { name = "PainterlyWater" }, folder + "/PainterlyWater.mat");
            Shader simulation = Required<Shader>("Assets/Experiments/Vegetation/Shaders/WaterInteractionNormals.shader");
            for (int waterIndex = 0; waterIndex < HillsideLayout.Waters.Length; waterIndex++)
            {
                HillsideLayout.WaterBody body = HillsideLayout.Waters[waterIndex];
                const int segments = 96;
                var vertices = new Vector3[segments + 1];
                var uv = new Vector2[vertices.Length];
                var triangles = new int[segments * 3];
                uv[0] = Vector2.one * .5f;
                for (int i = 0; i < segments; i++)
                {
                    float angle = i * Mathf.PI * 2 / segments;
                    Vector2 direction = new Vector2(Mathf.Cos(angle) * body.Radii.x, Mathf.Sin(angle) * body.Radii.y);
                    float low = .8f, high = 1.2f;
                    for (int n = 0; n < 16; n++)
                    {
                        float middle = (low + high) * .5f;
                        Vector2 p = body.Center + direction * middle;
                        if (body.SignedDistance(p.x, p.y) <= 0) low = middle; else high = middle;
                    }
                    Vector2 edge = direction * ((low + high) * .5f);
                    vertices[i + 1] = new Vector3(edge.x, 0, edge.y);
                    uv[i + 1] = new Vector2(.5f + edge.x / (body.Radii.x * 2.2f), .5f + edge.y / (body.Radii.y * 2.2f));
                    triangles[i * 3] = 0; triangles[i * 3 + 1] = (i + 1) % segments + 1; triangles[i * 3 + 2] = i + 1;
                }
                var mesh = new Mesh { name = body.Name + " 水面", vertices = vertices, uv = uv, triangles = triangles };
                mesh.RecalculateNormals(); mesh.RecalculateBounds();
                mesh = Store(mesh, folder + "/Water" + waterIndex + ".asset");
                var water = new GameObject(body.Name + " · 原实验场景水体");
                water.transform.SetParent(parent, false);
                water.transform.position = new Vector3(body.Center.x, body.SurfaceY, body.Center.y);
                water.layer = 4;
                water.AddComponent<MeshFilter>().sharedMesh = mesh;
                var renderer = water.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = true;
                var reflection = water.AddComponent<PainterlyWaterReflection>();
                reflection.resolution = 1024;
                reflection.clipOffset = .04f;
                var interaction = water.AddComponent<HillsideWaterInteraction>();
                interaction.waterRenderer = renderer;
                interaction.terrain = terrain;
                interaction.simulationShader = simulation;
            }
        }

        public static void Verify(GameObject root)
        {
            var grass = root.GetComponentInChildren<HillsideGrassRenderer>();
            if (!grass || grass.variants.Length != 12 || grass.variants.Any(m => !m || !EditorUtility.IsPersistent(m) || m.vertexCount < 30) ||
                grass.materials.Any(m => !m || !EditorUtility.IsPersistent(m) || !m.enableInstancing))
                throw new InvalidOperationException("Main meadow mesh variants and instanced materials must be persistent.");
            if (root.GetComponentsInChildren<PainterlyWaterReflection>().Length != HillsideLayout.Waters.Length ||
                root.GetComponentsInChildren<HillsideWaterInteraction>().Length != HillsideLayout.Waters.Length)
                throw new InvalidOperationException("Every planned water body needs reflection and character interaction.");
            if (root.GetComponentsInChildren<MeshFilter>().Any(f => !f.sharedMesh || !EditorUtility.IsPersistent(f.sharedMesh)))
                throw new InvalidOperationException("Nature contains an unsaved mesh.");
            if (grass.terrain.terrainData.terrainLayers[0].diffuseTexture.format != TextureFormat.RGB24)
                throw new InvalidOperationException("The meadow's terrain texture must have no alpha-driven smoothness.");
            if (root.GetComponentsInChildren<HillsideTreeOcclusion>().Length != 1)
                throw new InvalidOperationException("The generated tree group needs one runtime view-occlusion controller.");
            var leaves = Required<Material>(Source + "Backdrop_Tall_Fir_Branch.mat");
            foreach (LODGroup tree in root.GetComponentsInChildren<LODGroup>())
                foreach (Renderer renderer in tree.GetComponentsInChildren<Renderer>())
                    if (!renderer.sharedMaterials.Contains(leaves)) throw new InvalidOperationException("A tree lost the calibrated main-meadow foliage material.");
            Debug.Log(grass.VerifyPlacement());
        }

        static T Required<T>(string path) where T : Object
        {
            T asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (!asset) throw new InvalidOperationException("Missing vegetation source: " + path);
            return asset;
        }

        static T Store<T>(T created, string path) where T : Object
        {
            T existing = AssetDatabase.LoadAssetAtPath<T>(path);
            if (!existing) { AssetDatabase.CreateAsset(created, path); return created; }
            EditorUtility.CopySerialized(created, existing);
            Object.DestroyImmediate(created);
            EditorUtility.SetDirty(existing);
            return existing;
        }
    }
}
