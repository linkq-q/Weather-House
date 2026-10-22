using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace WeatherHouseSquirrelLab.Editor
{
    public static class SquirrelLabBuilder
    {
        public const string Root = "Assets/WeatherHouseSquirrelLab";
        public const string ScenePath = Root + "/Scenes/SquirrelMotionGallery.unity";
        public const string Generated = Root + "/Generated";
        private const string GalleryRootName = "Squirrel Motion Gallery";
        private static readonly string[] RequiredIds = { "walk_biped", "run_biped", "wave", "dance", "turn", "jump_biped" };
        private static readonly string[] Titles = { "01 BIPED WALK", "02 BIPED RUN", "03 WAVE", "04 DANCE", "05 TURN", "06 BIPED JUMP" };

        [MenuItem("Weather House/Squirrel Lab/Build or Refresh Motion Gallery")]
        private static void BuildFromMenu() { Debug.Log(Build()); }

        public static string Build()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play before building the squirrel gallery.");
            foreach (string id in RequiredIds)
                if (!File.Exists(Root + "/Models/" + id + ".fbx"))
                    throw new FileNotFoundException("Missing generated squirrel motion: " + id);

            Directory.CreateDirectory(Generated);
            Directory.CreateDirectory(Root + "/Scenes");
            AssetDatabase.Refresh();
            string[] models = Directory.GetFiles(Root + "/Models", "*.fbx")
                .Select(path => path.Replace('\\', '/'))
                .OrderBy(path => Order(Path.GetFileNameWithoutExtension(path))).ThenBy(path => path).ToArray();
            foreach (string model in models) PrepareModel(model);

            Scene scene = SceneManager.GetSceneByPath(ScenePath);
            if (scene.IsValid() && scene.isDirty)
                throw new InvalidOperationException("Save the squirrel gallery scene before refreshing it.");
            if (!scene.IsValid())
                scene = File.Exists(ScenePath)
                    ? EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive)
                    : EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            SceneManager.SetActiveScene(scene);
            foreach (GameObject root in scene.GetRootGameObjects())
                if (root.name == GalleryRootName) Object.DestroyImmediate(root);

            var galleryRoot = new GameObject(GalleryRootName);
            var gallery = galleryRoot.AddComponent<SquirrelMotionGallery>();
            var actors = new List<SquirrelMotionGallery.Actor>();
            Material floorMaterial = MakeMaterial("Floor", new Color(.15f, .20f, .24f));
            Material pedestalMaterial = MakeMaterial("Pedestal", new Color(.30f, .40f, .46f));
            CreateBox("Floor", galleryRoot.transform, new Vector3(0f, -.16f, 0f), new Vector3(18f, .2f, 16f), floorMaterial);

            for (int i = 0; i < models.Length; i++)
            {
                string id = Path.GetFileNameWithoutExtension(models[i]);
                Vector3 station = new Vector3((i % 3 - 1) * 4.5f, 0f, 2.4f - i / 3 * 4.8f);
                CreateBox("Platform " + id, galleryRoot.transform, station + Vector3.down * .015f, new Vector3(3.9f, .12f, 3.9f), pedestalMaterial);
                actors.Add(CreateActor(models[i], id, station, galleryRoot.transform));
                CreateLabel(Label(id), station + new Vector3(0f, .08f, -1.85f), galleryRoot.transform);
            }
            gallery.actors = actors.ToArray();
            CreateCameraAndLights(galleryRoot.transform, gallery, models.Length > 6);
            gallery.PlaceCamera();
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();
            string reportPath = ExportReport();
            Selection.activeGameObject = galleryRoot;
            Debug.Log("Squirrel gallery created additively: " + ScenePath + " | " + reportPath);
            return ScenePath;
        }

        private static int Order(string id)
        {
            int index = Array.IndexOf(RequiredIds, id);
            return index >= 0 ? index : RequiredIds.Length;
        }

        private static string Label(string id)
        {
            int index = Array.IndexOf(RequiredIds, id);
            return index >= 0 ? Titles[index] : id == "reference_walk" ? "REFERENCE WALK / CFG 6" : id;
        }

        private static void PrepareModel(string path)
        {
            var importer = AssetImporter.GetAtPath(path) as ModelImporter;
            if (importer == null) throw new InvalidOperationException("No ModelImporter for " + path);
            importer.animationType = ModelImporterAnimationType.Generic;
            importer.importAnimation = true;
            importer.animationCompression = ModelImporterAnimationCompression.Off;
            importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
            ModelImporterClipAnimation[] clips = importer.defaultClipAnimations;
            if (clips.Length != 1) throw new InvalidOperationException("Expected one original clip in " + path + ", got " + clips.Length);
            clips[0].loopTime = true;
            clips[0].loopPose = false;
            importer.clipAnimations = clips;
            importer.SaveAndReimport();

            string textureFolder = Generated + "/Textures/" + Path.GetFileNameWithoutExtension(path);
            Directory.CreateDirectory(textureFolder);
            AssetDatabase.Refresh();
            importer = (ModelImporter)AssetImporter.GetAtPath(path);
            importer.ExtractTextures(textureFolder);
            AssetDatabase.Refresh();
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        }

        private static SquirrelMotionGallery.Actor CreateActor(string modelPath, string id, Vector3 station, Transform parent)
        {
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            if (source == null) throw new InvalidOperationException("Could not load " + modelPath);
            var pedestal = new GameObject("Actor " + id);
            pedestal.transform.SetParent(parent, false);
            pedestal.transform.position = station;
            var model = (GameObject)PrefabUtility.InstantiatePrefab(source, pedestal.transform);
            model.name = id;
            foreach (Renderer renderer in model.GetComponentsInChildren<Renderer>(true))
                renderer.enabled = renderer is SkinnedMeshRenderer;

            Transform head = FindBone(model.transform, "Head");
            Transform pelvis = FindBone(model.transform, "Pelvis");
            if (head != null && pelvis != null)
            {
                Vector3 forward = Vector3.ProjectOnPlane(head.position - pelvis.position, Vector3.up);
                if (forward.sqrMagnitude > .0001f)
                    model.transform.rotation = Quaternion.FromToRotation(forward.normalized, Vector3.back) * model.transform.rotation;
            }
            Bounds bounds = ModelBounds(model);
            if (bounds.size.y < .001f) throw new InvalidOperationException("Empty squirrel mesh bounds: " + id);
            model.transform.localScale *= 2.05f / bounds.size.y;
            bounds = ModelBounds(model);
            model.transform.position += new Vector3(station.x - bounds.center.x, .05f - bounds.min.y, station.z - bounds.center.z);

            foreach (SkinnedMeshRenderer renderer in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                renderer.shadowCastingMode = ShadowCastingMode.On;
                renderer.receiveShadows = true;
                var materials = renderer.sharedMaterials;
                for (int m = 0; m < materials.Length; m++)
                    if (materials[m] != null) materials[m] = ConvertMaterial(materials[m], id + "_" + m);
                renderer.sharedMaterials = materials;
                renderer.updateWhenOffscreen = true;
            }

            AnimationClip[] clips = AssetDatabase.LoadAllAssetsAtPath(modelPath).OfType<AnimationClip>()
                .Where(clip => !clip.name.StartsWith("__preview__", StringComparison.Ordinal)).ToArray();
            if (clips.Length != 1) throw new InvalidOperationException("Expected one playable clip in " + modelPath);
            Animator animator = model.GetComponent<Animator>();
            if (animator == null) animator = model.AddComponent<Animator>();
            string controllerPath = Generated + "/" + id + ".controller";
            AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
            if (controller == null) controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
            var machine = controller.layers[0].stateMachine;
            foreach (ChildAnimatorState state in machine.states) machine.RemoveState(state.state);
            AnimatorState preview = machine.AddState("Preview");
            preview.motion = clips[0];
            preview.writeDefaultValues = true;
            machine.defaultState = preview;
            animator.runtimeAnimatorController = controller;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            var tail = model.AddComponent<SquirrelTailWave>();
            tail.modelRoot = model.transform;
            tail.phaseOffset = 0f;
            if (!tail.Initialize(model.transform)) throw new InvalidOperationException("Squirrel tail bind validation failed for " + id);
            var skinnedRenderers = model.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            if (skinnedRenderers.Length == 0 || skinnedRenderers.Any(renderer => renderer.bones.Length != 27))
                throw new InvalidOperationException("Expected the original 27-bone skinned squirrel: " + id);
            return new SquirrelMotionGallery.Actor
            {
                id = id, title = Label(id), sourceAsset = modelPath,
                animator = animator, clip = clips[0], tail = tail
            };
        }

        private static Bounds ModelBounds(GameObject model)
        {
            SkinnedMeshRenderer[] renderers = model.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            if (renderers.Length == 0) throw new InvalidOperationException("No renderer in " + model.name);
            Bounds bounds = renderers[0].bounds;
            foreach (Renderer renderer in renderers) bounds.Encapsulate(renderer.bounds);
            return bounds;
        }

        private static Transform FindBone(Transform root, string boneName)
        {
            return root.GetComponentsInChildren<Transform>(true).FirstOrDefault(bone => bone.name == boneName);
        }

        private static Material ConvertMaterial(Material source, string id)
        {
            Texture texture = source.HasProperty("_BaseMap") ? source.GetTexture("_BaseMap") : source.mainTexture;
            if (texture == null) texture = source.mainTexture;
            if (texture == null) throw new InvalidOperationException("Missing embedded squirrel texture in material " + source.name);
            Material material = MakeMaterial(id, Color.white);
            material.SetTexture("_BaseMap", texture);
            material.SetFloat("_Smoothness", .18f);
            material.SetFloat("_Metallic", 0f);
            EditorUtility.SetDirty(material);
            return material;
        }

        private static Material MakeMaterial(string id, Color color)
        {
            string path = Generated + "/" + id + ".mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) throw new InvalidOperationException("URP Lit shader is unavailable.");
            if (material == null)
            {
                material = new Material(shader) { name = id };
                AssetDatabase.CreateAsset(material, path);
            }
            material.shader = shader;
            material.SetColor("_BaseColor", color);
            EditorUtility.SetDirty(material);
            return material;
        }

        private static void CreateBox(string name, Transform parent, Vector3 position, Vector3 scale, Material material)
        {
            var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = name;
            box.transform.SetParent(parent, false);
            box.transform.position = position;
            box.transform.localScale = scale;
            box.GetComponent<Renderer>().sharedMaterial = material;
            Object.DestroyImmediate(box.GetComponent<Collider>());
        }

        private static void CreateLabel(string title, Vector3 position, Transform parent)
        {
            var label = new GameObject(title);
            label.transform.SetParent(parent, false);
            label.transform.position = position;
            label.transform.rotation = Quaternion.Euler(55f, 0f, 0f);
            var text = label.AddComponent<TextMesh>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            label.GetComponent<MeshRenderer>().sharedMaterial = text.font.material;
            text.text = title;
            text.anchor = TextAnchor.MiddleCenter;
            text.alignment = TextAlignment.Center;
            text.characterSize = .065f;
            text.fontSize = 42;
            text.color = new Color(.93f, .96f, 1f);
        }

        private static void CreateCameraAndLights(Transform parent, SquirrelMotionGallery gallery, bool extraRow)
        {
            var cameraObject = new GameObject("Squirrel Gallery Camera");
            cameraObject.transform.SetParent(parent, false);
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.09f, .13f, .18f);
            camera.orthographic = true;
            camera.orthographicSize = extraRow ? 8.5f : 6.4f;
            camera.nearClipPlane = .05f;
            camera.farClipPlane = 80f;
            camera.depth = 10f;
            gallery.galleryCamera = camera;
            gallery.cameraTarget = new Vector3(0f, .8f, extraRow ? -2.2f : 0f);
            var key = new GameObject("Squirrel Gallery Key").AddComponent<Light>();
            key.transform.SetParent(parent, false);
            key.type = LightType.Directional;
            key.transform.rotation = Quaternion.Euler(45f, -30f, 0f);
            key.intensity = 1.1f;
            key.shadows = LightShadows.Soft;
            var fill = new GameObject("Squirrel Gallery Fill").AddComponent<Light>();
            fill.transform.SetParent(parent, false);
            fill.type = LightType.Directional;
            fill.transform.rotation = Quaternion.Euler(25f, 150f, 0f);
            fill.intensity = .55f;
            fill.shadows = LightShadows.None;
        }

        [MenuItem("Weather House/Squirrel Lab/Export Gallery Validation")]
        private static void ExportReportFromMenu() { Debug.Log(ExportReport()); }

        public static string ExportReport()
        {
            Scene scene = SceneManager.GetSceneByPath(ScenePath);
            if (!scene.IsValid()) throw new InvalidOperationException("The squirrel gallery scene is not open.");
            SquirrelMotionGallery gallery = scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<SquirrelMotionGallery>(true)).FirstOrDefault();
            if (gallery == null) throw new InvalidOperationException("No squirrel gallery controller in scene.");
            Directory.CreateDirectory(Generated);
            string path = Generated + (Application.isPlaying ? "/gallery_runtime_validation.json" : "/gallery_validation.json");
            File.WriteAllText(path, gallery.ExportReportJson());
            return path;
        }
    }
}
