using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace WeatherHouse.Vegetation.Editor
{
    public static class VegetationLabBuilder
    {
        public const string Root = "Assets/WeatherHouseVegetationLab";
        public const string Generated = Root + "/Generated";
        public const string ScenePath = Root + "/VegetationLab.unity";
        const string Pack = "Assets/HIGHLANDS - Stylized Environment/Prefabs/Forest/";

        [MenuItem("Weather House/Vegetation Lab/Open or Create Grass and Water Scene")]
        public static void Build()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play before creating the lab scene.");
            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i).isDirty)
                    throw new InvalidOperationException("Save the open scenes before creating the vegetation lab.");
            if (File.Exists(ScenePath)) { EditorSceneManager.OpenScene(ScenePath); return; }
            Directory.CreateDirectory(Generated);
            AssetDatabase.Refresh();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var meadow = new GameObject("01 草地控制器 · 选中调整占比与密度").AddComponent<GrassMeadow>();
            meadow.species = new[] {
                Species("ShortGrass", GrassKind.ShortGrass, .34f, .065f, 12, .24f, .35f, 137),
                Species("TallGrass", GrassKind.TallGrass, .75f, .075f, 8, .25f, .45f, 269),
                Species("WhiteDaisy", GrassKind.Daisy, .50f, .055f, 5, .20f, .25f, 421)
            };
            meadow.materials = new Material[3];
            for (int i = 0; i < 3; i++)
            {
                var species = meadow.species[i];
                meadow.materials[i] = MaterialAsset(species.name, "Weather House/Painterly Grass", material => {
                    material.SetColor("_BaseColor", species.baseColor);
                    material.SetColor("_TipColor", species.tipColor);
                    material.enableInstancing = true;
                });
            }
            meadow.weights = new[] { 70f, 24f, 6f };
            meadow.totalClumps = 7000;
            meadow.seed = 431;
            meadow.size = new Vector2(42, 34);
            meadow.pondCenter = new Vector2(6, 4);
            meadow.pondRadius = 9;
            meadow.waterLevel = 0;

            Ground(meadow);
            Water(meadow);
            Backdrop(meadow);
            Lighting();
            Camera(meadow);
            EditorSceneManager.SaveScene(SceneManager.GetActiveScene(), ScenePath);
            meadow.Rebuild();

            AssetDatabase.SaveAssets();
            EditorSceneManager.SaveScene(SceneManager.GetActiveScene(), ScenePath);
            Selection.activeGameObject = meadow.gameObject;
            if (SceneView.lastActiveSceneView != null)
                SceneView.lastActiveSceneView.LookAt(new Vector3(3, 1, 3), Quaternion.Euler(30, 30, 0), 28);
            Debug.Log("Vegetation lab saved: " + ScenePath);
        }

        static GrassSpecies Species(string name, GrassKind kind, float height, float width, int blades, float spread, float bend, int seed)
        {
            string path = Generated + "/" + name + ".asset";
            var existing = AssetDatabase.LoadAssetAtPath<GrassSpecies>(path);
            if (existing != null) return existing;
            var species = ScriptableObject.CreateInstance<GrassSpecies>();
            species.name = name;
            species.kind = kind;
            species.height = height;
            species.width = width;
            species.bladeCount = blades;
            species.spread = spread;
            species.bend = bend;
            species.seed = seed;
            if (kind == GrassKind.TallGrass)
            {
                species.baseColor = new Color(.25f, .36f, .16f);
                species.tipColor = new Color(.65f, .72f, .31f);
            }
            if (kind == GrassKind.Daisy)
            {
                species.baseColor = new Color(.25f, .39f, .19f);
                species.tipColor = new Color(.53f, .65f, .31f);
                species.flowerCount = 3;
                species.flowerRadius = .085f;
                species.flowerColor = new Color(1, .985f, .92f);
            }
            AssetDatabase.CreateAsset(species, path);
            return species;
        }

        static Material MaterialAsset(string name, string shaderName, Action<Material> initialize)
        {
            string path = Generated + "/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null) return material;
            var shader = Shader.Find(shaderName);
            if (shader == null) throw new InvalidOperationException("Missing lab shader: " + shaderName);
            material = new Material(shader) { name = name };
            initialize?.Invoke(material);
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        static Mesh StoreMesh(Mesh mesh, string name)
        {
            string path = Generated + "/" + name + ".asset";
            mesh.name = name;
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing == null) { AssetDatabase.CreateAsset(mesh, path); return mesh; }
            EditorUtility.CopySerialized(mesh, existing);
            Object.DestroyImmediate(mesh);
            return existing;
        }

        static void Ground(GrassMeadow meadow)
        {
            const int columns = 180, rows = 160;
            const float width = 80, length = 70;
            var vertices = new Vector3[(columns + 1) * (rows + 1)];
            var uv = new Vector2[vertices.Length];
            var triangles = new int[columns * rows * 6];
            for (int z = 0; z <= rows; z++)
                for (int x = 0; x <= columns; x++)
                {
                    int i = z * (columns + 1) + x;
                    float px = ((float)x / columns - .5f) * width;
                    float pz = ((float)z / rows - .5f) * length;
                    vertices[i] = new Vector3(px, meadow.GetGroundHeight(px, pz), pz);
                    uv[i] = new Vector2((float)x / columns, (float)z / rows);
                }
            int index = 0;
            for (int z = 0; z < rows; z++)
                for (int x = 0; x < columns; x++)
                {
                    int i = z * (columns + 1) + x;
                    triangles[index++] = i; triangles[index++] = i + columns + 1; triangles[index++] = i + 1;
                    triangles[index++] = i + 1; triangles[index++] = i + columns + 1; triangles[index++] = i + columns + 2;
                }
            var mesh = new Mesh { vertices = vertices, uv = uv, triangles = triangles };
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            mesh = StoreMesh(mesh, "MeadowAndPondBowl");
            var ground = new GameObject("02 起伏草甸与浅滩 · 独立测试地形");
            ground.AddComponent<MeshFilter>().sharedMesh = mesh;
            ground.AddComponent<MeshCollider>().sharedMesh = mesh;
            ground.AddComponent<MeshRenderer>().sharedMaterial = MaterialAsset("PainterlyGround", "Weather House/Painterly Ground", null);
        }

        static void Water(GrassMeadow meadow)
        {
            float radius = meadow.pondRadius + 3;
            var mesh = new Mesh {
                vertices = new[] { new Vector3(-radius, 0, -radius), new Vector3(-radius, 0, radius), new Vector3(radius, 0, radius), new Vector3(radius, 0, -radius) },
                uv = new[] { Vector2.zero, Vector2.up, Vector2.one, Vector2.right },
                triangles = new[] { 0, 1, 2, 0, 2, 3 }
            };
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            var water = new GameObject("03 浅青色池水 · 材质可调");
            water.layer = 4;
            water.transform.position = new Vector3(meadow.pondCenter.x, meadow.waterLevel, meadow.pondCenter.y);
            water.AddComponent<MeshFilter>().sharedMesh = StoreMesh(mesh, "WaterSurface");
            var renderer = water.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = MaterialAsset("PainterlyWater", "Weather House/Painterly Water", null);
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = true;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Simple;

            var probe = new GameObject("池塘倒影探针").AddComponent<ReflectionProbe>();
            probe.transform.position = new Vector3(meadow.pondCenter.x, meadow.waterLevel + 1, meadow.pondCenter.y);
            probe.mode = ReflectionProbeMode.Realtime;
            probe.refreshMode = ReflectionProbeRefreshMode.OnAwake;
            probe.timeSlicingMode = ReflectionProbeTimeSlicingMode.NoTimeSlicing;
            probe.resolution = 256;
            probe.size = new Vector3(65, 35, 60);
            probe.center = new Vector3(0, 8, 0);
            probe.boxProjection = true;
            probe.cullingMask = ~(1 << 4);
            probe.farClipPlane = 100;
            renderer.probeAnchor = probe.transform;
        }

        static void Backdrop(GrassMeadow meadow)
        {
            var parent = new GameObject("04 背景参照 · HIGHLANDS 现有树石").transform;
            var palette = new Dictionary<Material, Material>();
            Vector3[] trees = {
                new Vector3(-22,12,14), new Vector3(-17,10,21), new Vector3(-9,11,25),
                new Vector3(1,10,27), new Vector3(14,13,23), new Vector3(22,10,18),
                new Vector3(28,12,11), new Vector3(27,9,0), new Vector3(-27,14,3),
                new Vector3(-20,11,-8)
            };
            for (int i = 0; i < trees.Length; i++)
                Place("Fir_Tall_" + (i % 3 + 1), new Vector2(trees[i].x, trees[i].z), trees[i].y, i * 137, parent, meadow, palette);
            Vector3[] stones = {
                new Vector3(-3.8f,1.5f,3), new Vector3(-1.8f,.7f,-1.7f), new Vector3(3.5f,.8f,-3.4f),
                new Vector3(11.5f,1.5f,-1.4f), new Vector3(15.5f,1.2f,5.2f),
                new Vector3(11.8f,1,9), new Vector3(4,1.1f,10.5f), new Vector3(-2.7f,.8f,8),
                new Vector3(-8,1.8f,-7), new Vector3(-9.4f,.7f,-6.5f)
            };
            for (int i = 0; i < stones.Length; i++)
                Place("Rock_" + (i % 2 + 1), new Vector2(stones[i].x, stones[i].z), stones[i].y, i * 73, parent, meadow, palette);
        }

        public static void PolishBackdrop(GrassMeadow meadow)
        {
            if (meadow == null || meadow.gameObject.scene.path != ScenePath)
                throw new InvalidOperationException("Backdrop polish is restricted to the vegetation lab scene.");
            const string groupName = "背景灌木";
            foreach (var root in meadow.gameObject.scene.GetRootGameObjects())
                if (root.name == groupName) Object.DestroyImmediate(root);
            var group = new GameObject(groupName);
            SceneManager.MoveGameObjectToScene(group, meadow.gameObject.scene);
            var palette = new Dictionary<Material, Material>();
            // Uneven overlapping groups cover the far bank without a repeated hedge silhouette.
            Vector3[] bushes = {
                new Vector3(-25,3.7f,18), new Vector3(-21,3.1f,16), new Vector3(-18,4,19),
                new Vector3(-14,2.8f,17), new Vector3(-11,3.7f,19), new Vector3(-8,2.3f,16),
                new Vector3(-5,3.8f,20), new Vector3(-1,3,18), new Vector3(2,4,22),
                new Vector3(6,3.6f,20), new Vector3(9,2.7f,18), new Vector3(12,3.9f,20),
                new Vector3(15,3.2f,17), new Vector3(18,2.4f,15), new Vector3(21,4,18),
                new Vector3(24,3.1f,16), new Vector3(27,3.8f,19), new Vector3(-23,2.3f,15)
            };
            for (int i = 0; i < bushes.Length; i++)
                Place("Bush_" + (i % 3 + 1), new Vector2(bushes[i].x, bushes[i].z), bushes[i].y,
                    i * 137.5f, group.transform, meadow, palette);
            EditorSceneManager.MarkSceneDirty(meadow.gameObject.scene);
        }

        static void Place(string asset, Vector2 position, float height, float yaw, Transform parent, GrassMeadow meadow, Dictionary<Material, Material> palette)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Pack + asset + ".prefab");
            if (prefab == null) return;
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            go.transform.SetParent(parent, false);
            go.transform.rotation = Quaternion.Euler(0, yaw, 0);
            var renderers = go.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return;
            var bounds = renderers[0].bounds;
            foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
            go.transform.localScale = Vector3.one * height / Mathf.Max(.1f, bounds.size.y);
            bounds = renderers[0].bounds;
            foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
            go.transform.position = new Vector3(position.x, meadow.GetGroundHeight(position.x, position.y) - bounds.min.y - .08f, position.y);
            foreach (var lod in go.GetComponentsInChildren<LODGroup>())
            {
                var levels = lod.GetLODs();
                if (levels.Length > 0) { levels[levels.Length - 1].screenRelativeTransitionHeight = .001f; lod.SetLODs(levels); }
            }
            foreach (var renderer in renderers)
            {
                var materials = renderer.sharedMaterials;
                for (int i = 0; i < materials.Length; i++)
                {
                    var source = materials[i];
                    if (source == null || !source.HasProperty("_MainColor")) continue;
                    if (!palette.TryGetValue(source, out var tinted))
                    {
                        string path = Generated + "/Backdrop_" + source.name + ".mat";
                        tinted = AssetDatabase.LoadAssetAtPath<Material>(path);
                        if (tinted == null)
                        {
                            tinted = new Material(source);
                            bool foliage = asset.StartsWith("Fir", StringComparison.Ordinal) || asset.StartsWith("Bush", StringComparison.Ordinal);
                            tinted.SetColor("_MainColor", foliage ? new Color(.30f,.38f,.34f) : new Color(.46f,.47f,.38f));
                            if (tinted.HasProperty("_SecondColor")) tinted.SetColor("_SecondColor", foliage ? new Color(.50f,.54f,.36f) : new Color(.65f,.63f,.48f));
                            tinted.enableInstancing = true;
                            AssetDatabase.CreateAsset(tinted, path);
                        }
                        palette.Add(source, tinted);
                    }
                    materials[i] = tinted;
                }
                renderer.sharedMaterials = materials;
            }
        }

        static void Lighting()
        {
            var sun = new GameObject("05 柔和午后日光").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.transform.rotation = Quaternion.Euler(48, -34, 0);
            sun.color = new Color(1, .95f, .81f);
            sun.intensity = 1.04f;
            sun.shadows = LightShadows.Soft;
            sun.shadowBias = .035f;
            sun.shadowNormalBias = .35f;
            RenderSettings.sun = sun;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(.57f,.69f,.72f);
            RenderSettings.ambientEquatorColor = new Color(.42f,.51f,.42f);
            RenderSettings.ambientGroundColor = new Color(.22f,.28f,.18f);
            RenderSettings.ambientIntensity = 1;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = new Color(.64f,.76f,.75f);
            RenderSettings.fogStartDistance = 32;
            RenderSettings.fogEndDistance = 100;
            RenderSettings.skybox = MaterialAsset("QuietDaySky", "Skybox/Procedural", sky => {
                sky.SetColor("_SkyTint", new Color(.66f,.75f,.79f));
                sky.SetColor("_GroundColor", new Color(.58f,.65f,.50f));
                sky.SetFloat("_AtmosphereThickness", .75f);
                sky.SetFloat("_Exposure", 1.1f);
                sky.SetFloat("_SunSize", .035f);
            });
        }

        static void Camera(GrassMeadow meadow)
        {
            var camera = new GameObject("06 主相机 · 环绕 / 行走 / 飞行").AddComponent<Camera>();
            camera.tag = "MainCamera";
            camera.fieldOfView = 48;
            camera.nearClipPlane = .08f;
            camera.farClipPlane = 180;
            camera.clearFlags = CameraClearFlags.Skybox;
            camera.allowHDR = true;
            camera.gameObject.AddComponent<AudioListener>();
            var data = camera.GetUniversalAdditionalCameraData();
            data.requiresDepthTexture = true;
            data.renderPostProcessing = true;
            data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            data.antialiasingQuality = AntialiasingQuality.High;
            var view = camera.gameObject.AddComponent<GrassLabView>();
            view.meadow = meadow;
            view.target = new Vector3(3, 1, 3);
            view.distance = 30;
            view.yaw = 34;
            view.pitch = 14;
            view.PlaceCamera();
        }
    }
}
