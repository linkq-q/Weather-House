using System;
using System.IO;
using System.Linq;
using ForestSpriteCharacter;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using WeatherHouse.Vegetation;
using Object = UnityEngine.Object;

namespace WeatherHouse.Hillside.Editor
{
    public static class HillsideSceneBuilder
    {
        public const string ScenePath = "Assets/Scenes/WeatherHouse_Hillside.unity";
        public const string Output = "Assets/Game/World/Hillside/Generated";
        public const string Review = "HillsideReview";
        const string Source = "Assets/Game/World/Hillside/SourceProps";
        static Material wood, roof, stone;

        [MenuItem("Weather House/Hillside/Build Hillside Map")]
        public static void BuildMenu() { Build(); }

        public static void Build(bool regenerate = false)
        {
            RequireSavedEditor();
            Directory.CreateDirectory(Output);
            Directory.CreateDirectory(Review);
            HillsideAnimalBuilder.PrepareAssets();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Terrain terrain = HillsideTerrainBuilder.Build(Output, regenerate);
            HillsideNatureBuilder.Build(terrain, Output, Group("02 草地林木与水面"));
            Landmarks();
            Lighting();
            var player = Player(terrain);
            HillsideAnimalBuilder.Build(terrain, player.transform, Output);
            AssetDatabase.SaveAssets();
            EditorSceneManager.SaveScene(SceneManager.GetActiveScene(), ScenePath);
            // Validate the persisted prefab overrides and references, not only the construction objects.
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            HillsideReviewChecks.Validate();
            Debug.Log("Hillside map saved: " + ScenePath);
        }

        public static void RequireSavedEditor()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
                throw new InvalidOperationException("Hillside build requires Edit mode after compilation.");
            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i).isDirty)
                    throw new InvalidOperationException("Unsaved scene preserved; save it before building the hillside.");
        }

        public static void RebuildAnimals()
        {
            RequireSavedEditor();
            if (SceneManager.GetActiveScene().path != ScenePath) throw new InvalidOperationException("Open the hillside before rebuilding its wildlife.");
            var terrain = Object.FindObjectOfType<Terrain>();
            var player = Object.FindObjectOfType<ForestSpriteDemoController>();
            if (!terrain || !player) throw new InvalidOperationException("Saved hillside terrain and player are required.");
            HillsideAnimalBuilder.PrepareAssets();
            var previous = Object.FindObjectOfType<WildlifePopulation>();
            if (previous) Object.DestroyImmediate(previous.gameObject);
            HillsideAnimalBuilder.Build(terrain, player.transform, Output);
            AssetDatabase.SaveAssets();
            EditorSceneManager.SaveScene(SceneManager.GetActiveScene(), ScenePath);
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            HillsideReviewChecks.Validate();
        }

        static ForestSpriteDemoController Player(Terrain terrain)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Characters/ForestSprite/Generated/ForestSprite.prefab");
            if (!prefab) throw new FileNotFoundException("ForestSprite.prefab is required.");
            var root = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            root.name = "05 森林精灵";
            var player = root.GetComponent<ForestSpriteDemoController>();
            root.transform.position = At(17, 83) + Vector3.up * .1f;
            player.externalCameraControl = true;
            player.showControls = false;
            player.demonstration = ForestSpriteDemoController.Demonstration.Manual;
            var panel = root.GetComponent<ForestSpriteCapePanel>();
            if (panel) panel.enabled = false;
            var camera = new GameObject("06 俯视跟随相机", typeof(Camera), typeof(AudioListener)).GetComponent<Camera>();
            camera.tag = "MainCamera";
            camera.nearClipPlane = .1f;
            camera.farClipPlane = 450;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.65f, .76f, .77f);
            camera.allowHDR = true;
            var data = camera.gameObject.AddComponent<UniversalAdditionalCameraData>();
            data.requiresDepthTexture = true;
            data.requiresColorTexture = true;
            data.renderPostProcessing = true;
            player.demoCamera = camera;
            var orbit = camera.gameObject.AddComponent<VegetationTopDownCamera>();
            orbit.player = player;
            orbit.baseYaw = 42; orbit.pitch = 28; orbit.distance = 14;
            orbit.yawLimit = 45; orbit.fieldOfView = 48; orbit.lookAhead = 4.5f;
            orbit.SnapToPlayer();
            var context = root.AddComponent<HillsidePlayerGround>();
            context.terrain = terrain; context.player = player;
            if (player.capeCloth && player.capeCloth.cloth)
            {
                var floor = new GameObject("披风地面接触");
                floor.layer = 2;
                var sphere = floor.AddComponent<SphereCollider>();
                sphere.radius = 1000; sphere.enabled = false;
                context.clothFloor = sphere;
                player.capeCloth.cloth.sphereColliders = player.capeCloth.cloth.sphereColliders
                    .Concat(new[] { new ClothSphereColliderPair(sphere) }).ToArray();
                context.PlaceFloor();
            }
            return player;
        }

        static Vector3 At(float x, float y) { var p = HillsideLayout.Design(x, y); p.y = HillsideLayout.Height(p.x, p.z); return p; }

        static void Lighting()
        {
            var sun = new GameObject("04 午后柔光", typeof(Light)).GetComponent<Light>();
            sun.type = LightType.Directional; sun.color = new Color(1, .94f, .81f);
            sun.intensity = 1.12f; sun.shadows = LightShadows.Soft;
            sun.shadowStrength = .65f; sun.shadowBias = .025f; sun.shadowNormalBias = .3f;
            sun.transform.rotation = Quaternion.Euler(48, -34, 0);
            RenderSettings.sun = sun;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(.65f, .73f, .76f);
            RenderSettings.ambientEquatorColor = new Color(.48f, .53f, .40f);
            RenderSettings.ambientGroundColor = new Color(.30f, .34f, .22f);
            RenderSettings.fog = true; RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = new Color(.65f, .74f, .73f);
            RenderSettings.fogStartDistance = 110; RenderSettings.fogEndDistance = 330;
        }

        static void Landmarks()
        {
            var parent = Group("03 五个生活区与地标");
            wood = Material("ShelterWood", new Color(.37f, .28f, .17f));
            roof = Material("ShelterRoof", new Color(.45f, .48f, .28f));
            stone = Material("StandingStone", new Color(.60f, .61f, .52f));
            var home = Group("A 天气屋山脚入口", parent);
            Prop("cottage", "天气屋", 17, 78, 5.2f, 180, home);
            Prop("fence", "花园短篱笆", 13, 80, 1.1f, 0, home);
            Prop("fence", "南侧短篱笆", 14, 83, 1.1f, 90, home);
            var yard = Group("B 老树小院", parent);
            Prop("drying_rack", "树叶晾晒架", 13, 51, 2.4f, 0, yard);
            Prop("stone", "苔藓床旁岸石", 22, 50, .8f, 45, yard);
            var moss = Material("MossBed", new Color(.38f, .47f, .23f));
            Box("青蛙苔藓床", At(21, 50) + Vector3.up * .15f, new Vector3(2.2f, .3f, 1.4f), moss, yard);
            var bank = Group("C 北岸浅滩", parent);
            Vector3 dock = At(36, 62.8f); dock.y = .6f;
            for (int i = 0; i < 10; i++) Box("短木栈台", dock + new Vector3(0, 0, -i * .44f), new Vector3(2.8f, .18f, .40f), wood, bank);
            var meadow = Group("E 半山花坡", parent);
            Vector3 origin = At(70, 44);
            for (int side = -1; side <= 1; side += 2)
                for (int end = -1; end <= 1; end += 2)
                    Box("草棚立柱", origin + new Vector3(side * 2.5f, 1.55f, end * 1.8f), new Vector3(.21f, 3.1f, .21f), wood, meadow);
            for (int side = -1; side <= 1; side += 2)
            {
                var panel = Box("开放式草棚屋顶", origin + new Vector3(side * 1.45f, 3.35f, 0), new Vector3(3.4f, .20f, 4.6f), roof, meadow);
                panel.transform.rotation = Quaternion.Euler(0, 0, side * -25);
            }
            Box("草棚固定长凳", origin + new Vector3(0, .55f, 1.5f), new Vector3(3.7f, .2f, .55f), wood, meadow);
            Prop("fence", "台地短篱笆", 67, 49.5f, 1, -20, meadow);
            var summit = Group("D 石阵山顶", parent);
            Vector3 peak = At(65, 18);
            var standingPositions = new System.Collections.Generic.List<Vector3>();
            for (int i = 0; i < 5; i++)
            {
                Vector3 p = peak;
                bool clear = false;
                for (int attempt = 0; attempt < 30; attempt++)
                {
                    float angle = (25 + i * 65 + attempt * 11) * Mathf.Deg2Rad;
                    float radius = 6 + attempt / 12 * 2;
                    p = peak + new Vector3(Mathf.Cos(angle) * radius, 0, Mathf.Sin(angle) * radius);
                    if (HillsideLayout.PathEdgeDistance(p.x, p.z) < 1.6f || standingPositions.Any(other => Vector3.Distance(other, p) < 3)) continue;
                    clear = true; break;
                }
                if (!clear) throw new InvalidOperationException("No clear summit standing-stone site.");
                p.y = HillsideLayout.Height(p.x, p.z);
                StandingStone(i, p, summit);
                standingPositions.Add(p);
            }
            Prop("stone", "石阵倒伏残石", 68, 21, .7f, 35, summit);
            Prop("chime", "石阵木风铃", 62, 17, 3.7f, 0, summit);
        }

        static void Prop(string id, string label, float x, float y, float height, float yaw, Transform parent)
        {
            string path = Source + "/" + id + "/" + id + ".fbx";
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (!asset) throw new FileNotFoundException(path);
            var go = (GameObject)PrefabUtility.InstantiatePrefab(asset);
            go.name = label; go.transform.SetParent(parent, false);
            // FBX roots carry their source-axis correction; yaw is additional world rotation.
            go.transform.rotation = Quaternion.Euler(0, yaw, 0) * go.transform.rotation;
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(Source + "/" + id + "/" + id + ".jpg");
            Material material = Material(id, Color.white);
            material.SetTexture("_BaseMap", texture);
            foreach (var renderer in go.GetComponentsInChildren<Renderer>())
                renderer.sharedMaterials = Enumerable.Repeat(material, renderer.sharedMaterials.Length).ToArray();
            Bounds b = Bounds(go);
            go.transform.localScale *= height / Mathf.Max(.01f, b.size.y);
            b = Bounds(go);
            Vector3 p = At(x, y);
            go.transform.position += new Vector3(p.x - b.center.x, p.y - b.min.y, p.z - b.center.z);
            // Static source props have no gameplay scripts; use their actual mesh for collision.
            foreach (var filter in go.GetComponentsInChildren<MeshFilter>())
                if (!filter.GetComponent<Collider>() && filter.sharedMesh)
                    filter.gameObject.AddComponent<MeshCollider>().sharedMesh = filter.sharedMesh;
        }

        public static Bounds Bounds(GameObject go)
        {
            var renderers = go.GetComponentsInChildren<Renderer>();
            var result = new Bounds(go.transform.position, Vector3.zero);
            if (renderers.Length > 0) result = renderers[0].bounds;
            foreach (var renderer in renderers) result.Encapsulate(renderer.bounds);
            return result;
        }

        static void StandingStone(int index, Vector3 position, Transform parent)
        {
            const int sides = 7;
            float height = 3.5f + index * .29f;
            var vertices = new Vector3[sides * 4 + 2];
            var triangles = new System.Collections.Generic.List<int>();
            float[] levels = { 0, .18f, .84f, 1 };
            float[] widths = { .84f, 1, .82f, .50f };
            for (int ring = 0; ring < 4; ring++)
                for (int side = 0; side < sides; side++)
                {
                    float angle = side * Mathf.PI * 2 / sides;
                    float irregular = 1 + .17f * Mathf.Sin(side * 2.7f + index * 1.9f);
                    float y = levels[ring] * height + (ring > 1 ? .13f * Mathf.Sin(side * 1.8f + index) : 0);
                    vertices[ring * sides + side] = new Vector3(Mathf.Cos(angle) * .86f * widths[ring] * irregular + levels[ring] * .22f,
                        y, Mathf.Sin(angle) * .62f * widths[ring] * irregular);
                    if (ring < 3)
                    {
                        int a = ring * sides + side, b = ring * sides + (side + 1) % sides;
                        triangles.AddRange(new[] { a, a + sides, b, b, a + sides, b + sides });
                    }
                }
            vertices[sides * 4] = Vector3.zero;
            vertices[sides * 4 + 1] = new Vector3(.22f, height, 0);
            for (int side = 0; side < sides; side++)
            {
                int next = (side + 1) % sides;
                triangles.AddRange(new[] { sides * 4, side, next, sides * 4 + 1, 3 * sides + next, 3 * sides + side });
            }
            // Separate triangle vertices retain the broad, uneven facets of weathered stone.
            var flat = triangles.Select(v => vertices[v]).ToArray();
            string path = Output + "/StandingStone" + index + ".asset";
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (!mesh) { mesh = new Mesh(); AssetDatabase.CreateAsset(mesh, path); }
            mesh.Clear(); mesh.name = "风化立石 " + (index + 1);
            mesh.vertices = flat; mesh.triangles = Enumerable.Range(0, flat.Length).ToArray();
            mesh.RecalculateNormals(); mesh.RecalculateBounds(); EditorUtility.SetDirty(mesh);
            var go = new GameObject(mesh.name, typeof(MeshFilter), typeof(MeshRenderer), typeof(MeshCollider));
            go.transform.SetParent(parent, false); go.transform.position = position - Vector3.up * .08f;
            go.transform.rotation = Quaternion.Euler(index % 2 == 0 ? 4 : -3, index * 67, 3);
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            go.GetComponent<MeshRenderer>().sharedMaterial = stone;
            go.GetComponent<MeshCollider>().sharedMesh = mesh;
        }

        static GameObject Box(string label, Vector3 position, Vector3 size, Material material, Transform parent)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = label; go.transform.SetParent(parent); go.transform.position = position;
            go.transform.localScale = size; go.GetComponent<Renderer>().sharedMaterial = material;
            return go;
        }

        public static Transform Group(string label, Transform parent = null)
        {
            var t = new GameObject(label).transform; t.SetParent(parent, false); return t;
        }

        public static Material Material(string label, Color color)
        {
            string path = Output + "/" + label + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!m) { m = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(m, path); }
            m.SetColor("_BaseColor", color); m.SetFloat("_Smoothness", .05f);
            EditorUtility.SetDirty(m); return m;
        }
    }
}
