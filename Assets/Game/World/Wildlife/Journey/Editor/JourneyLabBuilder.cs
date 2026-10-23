using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using WeatherHouse.Hillside;
using Object = UnityEngine.Object;

namespace WeatherHouse.WildlifeJourney.Editor
{
    public static class JourneyLabBuilder
    {
        public const string ScenePath = "Assets/Scenes/Experimental/WildlifeJourneyLab.unity";
        public const string Generated = "Assets/Game/World/Wildlife/Journey/Generated";
        public const float BridgeZ = 9f, BridgeHeight = .65f, BridgeHalfLength = 4.6f, BridgeWidth = 1.25f;
        public const int GroundLayer = 8, ObstacleLayer = 9;
        private const string SheepPrefab = "Assets/Game/World/Hillside/Generated/sheep.prefab";

        public sealed class LabContents
        {
            public GameObject root, bridgeBlocker;
            public JourneyHerd herd;
            public JourneySocial social;
            public JourneyNavigation navigation;
            public Camera viewCamera;
        }

        [MenuItem("Weather House/Wildlife Journey/Build Independent Sheep Lab")]
        public static void BuildMenu() { Debug.Log("Saved independent sheep lab: " + Build()); }

        public static string Build()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Build the sheep lab outside Play mode.");
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                if (string.IsNullOrEmpty(SceneManager.GetSceneAt(i).path))
                    throw new InvalidOperationException("Unity needs the untitled scene saved or closed before creating the independent sheep lab. Your open scene has not been changed.");
                if (SceneManager.GetSceneAt(i).path == ScenePath)
                    throw new InvalidOperationException("Close the existing sheep lab before rebuilding it; its current edits are preserved.");
            }
            EnsureFolders();
            Scene original = SceneManager.GetActiveScene();
            Scene preview = EditorSceneManager.NewPreviewScene(), saved = default;
            try
            {
                LabContents lab = CreateContents(preview);
                // A normal additive scene is used only for serialization; preview scenes cannot be saved consistently across Unity versions.
                saved = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
                if (original.IsValid() && original.isLoaded && SceneManager.GetActiveScene() != original)
                    SceneManager.SetActiveScene(original);
                SceneManager.MoveGameObjectToScene(lab.root, saved);
                if (!EditorSceneManager.SaveScene(saved, ScenePath))
                    throw new InvalidOperationException("Failed to save " + ScenePath);
                return ScenePath;
            }
            finally
            {
                if (saved.IsValid() && saved.isLoaded) EditorSceneManager.CloseScene(saved, true);
                if (preview.IsValid()) EditorSceneManager.ClosePreviewScene(preview);
                if (original.IsValid() && original.isLoaded && SceneManager.GetActiveScene() != original)
                    SceneManager.SetActiveScene(original);
            }
        }

        public static LabContents CreateContents(Scene scene)
        {
            EnsureFolders();
            var lab = new LabContents { root = NewObject("Wildlife Journey Lab", scene) };
            Transform environment = NewChild("01 Meadow, river and real bridge", lab.root.transform);
            Material grass = MaterialAsset("Meadow", new Color(.39f, .53f, .24f));
            Material sand = MaterialAsset("ShallowBanks", new Color(.65f, .61f, .43f));
            Material bed = MaterialAsset("RiverBed", new Color(.28f, .38f, .32f));
            Material stone = MaterialAsset("BridgeStone", new Color(.53f, .51f, .40f));
            Material wood = MaterialAsset("BridgeRail", new Color(.33f, .25f, .16f));
            Material water = MaterialAsset("ClearWater", new Color(.22f, .58f, .63f, .48f), true);
            Mesh groundMesh = GroundMesh();
            var ground = NewChild("Continuous grass banks and river bed", environment).gameObject;
            ground.layer = GroundLayer;
            ground.AddComponent<MeshFilter>().sharedMesh = groundMesh;
            ground.AddComponent<MeshRenderer>().sharedMaterials = new[] { grass, sand, bed };
            ground.AddComponent<MeshCollider>().sharedMesh = groundMesh;
            var waterObject = NewChild("Water surface - no solid collider", environment).gameObject;
            waterObject.layer = 4;
            waterObject.AddComponent<MeshFilter>().sharedMesh = WaterMesh();
            waterObject.AddComponent<MeshRenderer>().sharedMaterial = water;
            var bridge = Box("Narrow bridge deck", environment, new Vector3(0, BridgeHeight - .14f, BridgeZ),
                new Vector3(BridgeHalfLength * 2, .28f, BridgeWidth), stone, GroundLayer);
            for (int side = -1; side <= 1; side += 2)
            {
                Box("Bridge handrail", environment, new Vector3(0, BridgeHeight + .75f, BridgeZ + side * .76f),
                    new Vector3(9.4f, .09f, .08f), wood, ObstacleLayer);
                for (int i = 0; i < 7; i++)
                    Box("Bridge post", environment, new Vector3(-4.5f + i * 1.5f, BridgeHeight + .4f, BridgeZ + side * .76f),
                        new Vector3(.1f, .8f, .1f), wood, ObstacleLayer);
            }
            lab.bridgeBlocker = Box("Bridge closure - toggle with B", environment, new Vector3(-4.2f, 1.15f, BridgeZ),
                new Vector3(.45f, 1.4f, BridgeWidth), MaterialAsset("Closure", new Color(.65f, .34f, .20f)), ObstacleLayer);
            lab.bridgeBlocker.SetActive(false);

            lab.navigation = NewChild("02 Surface-aware navigation", lab.root.transform).gameObject.AddComponent<JourneyNavigation>();
            ConfigureNavigation(lab.navigation);
            lab.navigation.bridges = new[] { new JourneyBridgeSurface { id = 1, surface = bridge.GetComponent<Collider>() } };
            lab.herd = NewChild("03 Sheep herd", lab.root.transform).gameObject.AddComponent<JourneyHerd>();
            lab.herd.navigation = lab.navigation;
            lab.herd.sites = ActivitySites();
            lab.herd.seed = 20261023;
            lab.herd.members = Sheep(lab.herd.transform, scene);
            lab.social = lab.herd.gameObject.AddComponent<JourneySocial>();
            lab.herd.social = lab.social;
            lab.social.occlusionMask = (1 << GroundLayer) | (1 << ObstacleLayer);
            lab.viewCamera = CameraAndLights(lab.root.transform);
            lab.social.viewCamera = lab.viewCamera;
            var controls = lab.root.AddComponent<JourneyLabControls>();
            controls.herd = lab.herd; controls.social = lab.social; controls.navigation = lab.navigation;
            controls.bridgeBlocker = lab.bridgeBlocker; controls.viewCamera = lab.viewCamera;
            return lab;
        }

        public static void ConfigureNavigation(JourneyNavigation navigation)
        {
            navigation.groundMask = 1 << GroundLayer;
            navigation.obstacleMask = 1 << ObstacleLayer;
            navigation.surfaceProbeHeight = 25f;
            navigation.waterAreas = new[] { new JourneyWaterArea {
                bounds = new Bounds(Vector3.zero, new Vector3(8.4f, 6f, 32f)), surfaceHeight = 0 } };
            navigation.nodes = new[] {
                Node("West meadow", -11, 0), Node("West shallow bank", -4.8f, 0),
                Node("East shallow bank", 4.8f, 0), Node("East meadow", 11, 0),
                Node("West bridge approach", -6, BridgeZ), Node("Bridge west end", -4.5f, BridgeZ),
                Node("Bridge east end", 4.5f, BridgeZ), Node("East bridge approach", 6, BridgeZ),
                Node("South west bank", -4.8f, -10), Node("South cliff top", 4.8f, -10)
            };
            navigation.links = new[] {
                Link(0, 1), Link(1, 2), Link(2, 3), Link(0, 4), Link(1, 4),
                Link(4, 5), Link(5, 6, 1), Link(6, 7), Link(7, 3), Link(7, 2),
                Link(1, 8), Link(8, 9), Link(9, 2)
            };
            navigation.Rebuild();
        }

        public static JourneyActivitySite[] ActivitySites()
        {
            return new[] {
                new JourneyActivitySite { name = "西岸草坡", center = Position(-11, 0), radius = 4.3f, forage = .85f, rest = .9f },
                new JourneyActivitySite { name = "东岸嫩草地", center = Position(11, 0), radius = 4.3f, forage = 1f, rest = .7f }
            };
        }

        public static float GroundHeight(float x, float z)
        {
            float ax = Mathf.Abs(x);
            float height = ax >= 4.2f ? .65f : Mathf.Lerp(-1.15f, .65f, Mathf.SmoothStep(0, 1, ax / 4.2f));
            if (x > 2.2f)
            {
                float cliff = x < 2.65f ? Mathf.Lerp(-.19f, 1.55f, (x - 2.2f) / .45f) :
                    Mathf.Lerp(1.55f, .65f, Mathf.Clamp01((x - 2.65f) / 5.35f));
                height = Mathf.Lerp(height, cliff, Mathf.SmoothStep(0, 1, (-z - 6f) / 2f));
            }
            float hill = Mathf.Clamp01((ax - 6f) / 9f) * .12f * Mathf.Sin(z * .35f) * Mathf.Sin(x * .28f);
            return height + hill;
        }

        public static Vector3 Position(float x, float z) { return new Vector3(x, GroundHeight(x, z), z); }

        private static JourneyMember[] Sheep(Transform parent, Scene scene)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(SheepPrefab);
            if (!prefab) throw new InvalidOperationException("Missing the established sheep prefab: " + SheepPrefab);
            var members = new JourneyMember[10];
            AnimationClip swimClip = File.Exists(JourneySwimImport.Path) ? JourneySwimImport.Load() : null;
            if (!JourneyScatter.TryCreate(new Vector3(-11, 0, 0), 3.8f, 1.45f, members.Length, 20261023, out Vector3[] positions))
                throw new InvalidOperationException("Could not create a safely spaced sheep group.");
            var random = new WildlifeRandom(20261023, 0, "lab-positions");
            string[] paddleNames = { "UpperArm.L", "UpperArm.R", "Thigh.L", "Thigh.R" };
            for (int i = 0; i < members.Length; i++)
            {
                var sheep = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                sheep.name = "Sheep " + (i + 1).ToString("00") + (i < 4 ? " - swimmer" : " - bridge walker");
                sheep.transform.SetParent(parent, false);
                sheep.transform.position = Position(positions[i].x, positions[i].z);
                sheep.transform.rotation = Quaternion.Euler(0, random.Range(0, 360), 0);
                var old = sheep.GetComponent<WildlifeAgent>();
                if (!old) throw new InvalidOperationException("The sheep prefab has no animation binding source.");
                old.enabled = false;
                Transform visual = old.visualRoot;
                float scale = random.Range(.86f, 1.12f);
                visual.localScale *= scale; visual.localPosition *= scale;
                Transform[] bones = old.animator.GetComponentsInChildren<Transform>();
                members[i] = new JourneyMember {
                    id = i + 1, root = sheep.transform, visualRoot = visual, animator = old.animator, clips = old.clips, swimClip = swimClip,
                    headBone = bones.FirstOrDefault(t => t.name == "Head"),
                    paddleBones = paddleNames.Select(name => bones.FirstOrDefault(t => t.name == name)).Where(t => t).ToArray(),
                    sociability = random.Range(.55f, .95f), bodyLength = 1.25f * scale,
                    profile = new JourneyProfile { bodyRadius = old.bodyRadius * scale, canSwim = i < 4,
                        waterlineHeight = visual.localPosition.y + .64f * visual.localScale.y,
                        maxSwimDistance = 8f, wadeDepth = .28f, walkSpeed = random.Range(.85f, 1.05f),
                        swimSpeed = random.Range(.72f, .86f), waterAversion = .1f }
                };
                Object.DestroyImmediate(old);
            }
            return members;
        }

        private static Mesh GroundMesh()
        {
            string path = Generated + "/Ground.asset";
            Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing) return existing;
            const int nx = 160, nz = 128;
            var vertices = new Vector3[(nx + 1) * (nz + 1)];
            var uv = new Vector2[vertices.Length];
            var triangles = new[] { new List<int>(), new List<int>(), new List<int>() };
            for (int z = 0; z <= nz; z++)
                for (int x = 0; x <= nx; x++)
                {
                    int i = z * (nx + 1) + x;
                    vertices[i] = Position(-20 + x * .25f, -16 + z * .25f);
                    uv[i] = new Vector2(x / 16f, z / 16f);
                }
            for (int z = 0; z < nz; z++)
                for (int x = 0; x < nx; x++)
                {
                    int i = z * (nx + 1) + x;
                    float h = (vertices[i].y + vertices[i + 1].y + vertices[i + nx + 1].y + vertices[i + nx + 2].y) * .25f;
                    List<int> list = triangles[h > .21f ? 0 : h > -.25f ? 1 : 2];
                    list.AddRange(new[] { i, i + nx + 1, i + 1, i + 1, i + nx + 1, i + nx + 2 });
                }
            var mesh = new Mesh { name = "Journey continuous ground", vertices = vertices, uv = uv, subMeshCount = 3 };
            for (int i = 0; i < triangles.Length; i++) mesh.SetTriangles(triangles[i], i);
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }

        private static Mesh WaterMesh()
        {
            string path = Generated + "/Water.asset";
            Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing) return existing;
            var mesh = new Mesh { name = "Journey water surface", vertices = new[] {
                new Vector3(-4.2f, 0, -16), new Vector3(-4.2f, 0, 16), new Vector3(4.2f, 0, -16), new Vector3(4.2f, 0, 16) },
                uv = new[] { Vector2.zero, Vector2.up, Vector2.right, Vector2.one }, triangles = new[] { 0, 1, 2, 2, 1, 3 } };
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }

        private static Material MaterialAsset(string name, Color color, bool transparent = false)
        {
            string path = Generated + "/" + name + ".mat";
            Material existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing) return existing;
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (!shader) throw new InvalidOperationException("The sheep lab requires the project's URP Lit shader.");
            var material = new Material(shader) { name = "Journey " + name };
            material.SetColor("_BaseColor", color); material.SetFloat("_Smoothness", transparent ? .65f : .08f);
            if (transparent)
            {
                material.SetFloat("_Surface", 1); material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha); material.SetFloat("_ZWrite", 0);
                material.SetOverrideTag("RenderType", "Transparent"); material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                material.renderQueue = (int)RenderQueue.Transparent;
            }
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        private static Camera CameraAndLights(Transform parent)
        {
            var camera = NewChild("Main Camera", parent).gameObject.AddComponent<Camera>();
            camera.tag = "MainCamera"; camera.fieldOfView = 45; camera.nearClipPlane = .08f; camera.farClipPlane = 150;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.68f, .78f, .80f);
            camera.transform.position = new Vector3(-21, 27, -32);
            camera.transform.LookAt(new Vector3(0, .3f, 2));
            camera.GetUniversalAdditionalCameraData().requiresDepthTexture = true;
            camera.gameObject.AddComponent<AudioListener>();
            var sun = NewChild("Warm daylight", parent).gameObject.AddComponent<Light>();
            sun.type = LightType.Directional; sun.color = new Color(1, .94f, .81f); sun.intensity = 1.35f;
            sun.shadows = LightShadows.Soft; sun.shadowBias = .025f; sun.shadowNormalBias = .25f;
            sun.transform.rotation = Quaternion.Euler(48, -35, 0);
            var fill = NewChild("Soft sky fill", parent).gameObject.AddComponent<Light>();
            fill.type = LightType.Directional; fill.color = new Color(.71f, .83f, 1f); fill.intensity = .35f;
            fill.transform.rotation = Quaternion.Euler(25, 145, 0);
            return camera;
        }

        private static GameObject Box(string name, Transform parent, Vector3 position, Vector3 size, Material material, int layer)
        {
            var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            SceneManager.MoveGameObjectToScene(box, parent.gameObject.scene);
            box.name = name; box.layer = layer; box.transform.SetParent(parent, false);
            box.transform.position = position; box.transform.localScale = size;
            box.GetComponent<Renderer>().sharedMaterial = material;
            return box;
        }
        private static GameObject NewObject(string name, Scene scene)
        { var go = new GameObject(name); SceneManager.MoveGameObjectToScene(go, scene); return go; }
        private static Transform NewChild(string name, Transform parent)
        { Transform child = NewObject(name, parent.gameObject.scene).transform; child.SetParent(parent, false); return child; }
        private static JourneyNode Node(string name, float x, float z) { return new JourneyNode { id = name, position = Position(x, z) }; }
        private static JourneyLink Link(int from, int to, int bridge = 0)
        { return new JourneyLink { from = from, to = to, bidirectional = true, bridgeId = bridge }; }
        private static void EnsureFolders()
        {
            if (!AssetDatabase.IsValidFolder(Generated)) { Directory.CreateDirectory(Generated); AssetDatabase.Refresh(); }
            if (!AssetDatabase.IsValidFolder("Assets/Scenes/Experimental"))
            { Directory.CreateDirectory("Assets/Scenes/Experimental"); AssetDatabase.Refresh(); }
        }

        public static void BatchBuildAndCheck()
        {
            if (!Application.isBatchMode)
                throw new InvalidOperationException("BatchBuildAndCheck is only for an isolated Unity batch validation project.");
            string project = Path.GetDirectoryName(Application.dataPath);
            if (!File.Exists(Path.Combine(project, ".wildlife-journey-validation")))
                throw new InvalidOperationException("Create .wildlife-journey-validation only in the independent validation project before running this batch entrypoint.");
            string review = Path.Combine(project, "WildlifeJourneyReview");
            Directory.CreateDirectory(review);
            string reportPath = Path.Combine(review, "batch-checks.txt");
            File.WriteAllText(reportPath, "RUNNING " + DateTime.UtcNow.ToString("O") + "\n");
            try { RunBatchChecks(reportPath); }
            catch (Exception error)
            {
                File.WriteAllText(reportPath, "FAIL " + DateTime.UtcNow.ToString("O") + "\n" + error);
                throw;
            }
        }

        private static void RunBatchChecks(string reportPath)
        {
            Scene initial = SceneManager.GetActiveScene();
            string bootstrap = Generated + "/BatchBootstrap.unity";
            if (initial.path == ScenePath)
            {
                initial = File.Exists(bootstrap) ? EditorSceneManager.OpenScene(bootstrap, OpenSceneMode.Single) :
                    EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            }
            if (string.IsNullOrEmpty(initial.path))
            {
                if (SceneManager.sceneCount != 1)
                    throw new InvalidOperationException("The isolated batch bootstrap requires exactly one initial scene.");
                EnsureFolders();
                if (!EditorSceneManager.SaveScene(initial, bootstrap))
                    throw new InvalidOperationException("Could not save the isolated batch bootstrap scene.");
            }
            string path = Build();
            Scene scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
            try
            {
                JourneyHerd herd = scene.GetRootGameObjects().SelectMany(go => go.GetComponentsInChildren<JourneyHerd>()).Single();
                string report = "PASS " + DateTime.UtcNow.ToString("O") + "\n" + JourneyScatterChecks.RunLogicChecks() + "\n" + JourneyNavigationChecks.RunLogicChecks() + "\n" + JourneyCrowdPathChecks.RunLogicChecks() + "\n" + JourneyAvoidanceChecks.RunLogicChecks() + "\n" + JourneyHerdChecks.RunLogicChecks() + "\n" +
                    JourneySocialChecks.RunLogicChecks() + "\n" + JourneyHerdChecks.RunSceneChecks(herd);
                File.WriteAllText(reportPath, report);
                Debug.Log(report);
            }
            finally { EditorSceneManager.CloseScene(scene, true); }
        }
    }
}
