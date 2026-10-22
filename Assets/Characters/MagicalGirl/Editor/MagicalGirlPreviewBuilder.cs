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

namespace MagicalGirlPreview.Editor
{
    public static class MagicalGirlPreviewBuilder
    {
        public const string Root = "Assets/Characters/MagicalGirl";
        public const string ModelPath = Root + "/Character/MagicalGirl_Working.fbx";
        public const string ScenePath = "Assets/Scenes/Experimental/MagicalGirl_Preview.unity";
        const string Generated = Root + "/Generated";
        const string ControllerPath = Generated + "/Preview.controller";
        const float Height = 1.65f;
        // Confirm from the imported FBX before final visual acceptance.
        public static float ModelYawOffset = 90f;
        static readonly string[] RequiredClips = { "idle", "walk", "run", "afraid", "agree", "sob" };
        static readonly string[] Regions = { "Face", "Hair", "Hair2", "Body", "Leg" };

        [MenuItem("Weather House/Magical Girl/Create Independent Preview")]
        public static void Build()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play before creating the preview.");
            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i).isDirty)
                    throw new InvalidOperationException("An open scene has unsaved changes. Save it yourself before creating this independent preview.");
            if (!File.Exists(ModelPath)) throw new FileNotFoundException("Working FBX is not ready.", ModelPath);
            Directory.CreateDirectory(Generated);
            AssetDatabase.Refresh();
            ConfigureImporter();
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if (model == null) throw new InvalidOperationException("The working FBX did not import as a model.");
            var clips = AssetDatabase.LoadAllAssetsAtPath(ModelPath).OfType<AnimationClip>()
                .Where(c => !c.name.StartsWith("__preview__", StringComparison.Ordinal))
                .GroupBy(c => StateName(c.name)).ToDictionary(g => g.Key, g => g.First());
            foreach (string required in RequiredClips)
                if (!clips.ContainsKey(required)) throw new InvalidOperationException("Missing required clip: " + required);
            var animatorController = CreateController(clips);
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var player = new GameObject("Magical Girl Preview Player");
            var visual = (GameObject)PrefabUtility.InstantiatePrefab(model, scene);
            visual.name = "Character Visual";
            visual.transform.SetParent(player.transform, false);
            visual.transform.localRotation = Quaternion.Euler(0f, ModelYawOffset, 0f);
            var renderers = visual.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) throw new InvalidOperationException("Working model has no renderers.");
            Bounds bounds = BoundsOf(renderers);
            if (bounds.size.y < .0001f) throw new InvalidOperationException("Working model height is zero.");
            visual.transform.localScale *= Height / bounds.size.y;
            bounds = BoundsOf(renderers);
            visual.transform.position -= Vector3.up * bounds.min.y;

            var animator = visual.GetComponent<Animator>();
            if (animator == null) animator = visual.AddComponent<Animator>();
            animator.runtimeAnimatorController = animatorController;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            var character = player.AddComponent<CharacterController>();
            character.height = Height * .94f;
            character.radius = Height * .13f;
            character.center = Vector3.up * character.height * .5f;
            character.skinWidth = .02f;
            character.stepOffset = .18f;
            character.minMoveDistance = 0f;
            var preview = player.AddComponent<MagicalGirlPreviewController>();
            preview.animator = animator;
            preview.visual = visual.transform;
            preview.clips = clips.Select(kv => new MagicalGirlPreviewController.ClipEntry { state = kv.Key, clip = kv.Value }).ToArray();
            preview.materials = MakePreviewMaterials(renderers);
            preview.assetStage = preview.materials.SelectMany(p => p.unlit).All(m => RegionName(m.name) == "Source")
                ? "结构检查版 · 沿用原 UV，正式五区 UV 待修模后制作"
                : "五区 UV 工作副本 · 仍需检查变形与绘画接缝";
            preview.cameraHeight = Height * .55f;
            preview.cameraDistance = Height * 2.15f;
            preview.secondaryMotion = ConfigureSecondaryMotion(visual);
            preview.runPhaseOffset = .28f;
            preview.walkSpeed = .765f;
            preview.runSpeed = 2.787f;
            preview.ordinaryLighting = false;

            var cameraObject = new GameObject("Preview Camera", typeof(Camera), typeof(AudioListener));
            var camera = cameraObject.GetComponent<Camera>();
            camera.tag = "MainCamera";
            camera.fieldOfView = 35f;
            camera.nearClipPlane = .03f;
            camera.farClipPlane = 60f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.22f, .29f, .29f);
            camera.transform.position = new Vector3(1.8f, 1.2f, 3.5f);
            camera.transform.LookAt(Vector3.up * preview.cameraHeight);
            preview.previewCamera = camera;
            var lightObject = new GameObject("Preview Key Light", typeof(Light));
            var light = lightObject.GetComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1f;
            light.color = Color.white;
            light.shadows = LightShadows.Soft;
            light.transform.rotation = Quaternion.Euler(35f, 150f, 0f);
            preview.keyLight = light;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(.65f, .65f, .65f);
            CreateGround();
            RunLogicChecks();
            AssetDatabase.SaveAssets();
            if (!EditorSceneManager.SaveScene(scene, ScenePath)) throw new IOException("Could not save the preview scene.");
            ValidateOpenScene();
            Selection.activeGameObject = player;
            var sceneView = SceneView.lastActiveSceneView;
            if (sceneView != null) sceneView.LookAt(Vector3.up * .8f, Quaternion.Euler(10f, 165f, 0f), 3.5f, false, true);
            Debug.Log("Independent preview created. Enter Play and focus Game view. " + ScenePath);
        }

        static void ConfigureImporter()
        {
            var importer = AssetImporter.GetAtPath(ModelPath) as ModelImporter;
            if (importer == null) throw new InvalidOperationException("Expected FBX ModelImporter.");
            importer.animationType = ModelImporterAnimationType.Generic;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.importAnimation = true;
            importer.importBlendShapes = true;
            importer.importCameras = false;
            importer.importLights = false;
            var clips = importer.clipAnimations.Length > 0 ? importer.clipAnimations : importer.defaultClipAnimations;
            foreach (var clip in clips)
            {
                string state = StateName(clip.name);
                clip.loopTime = state == "idle" || state == "walk" || state == "run" || state == "airborne";
                clip.loopPose = clip.loopTime;
            }
            importer.clipAnimations = clips;
            importer.SaveAndReimport();
        }

        static AnimatorController CreateController(Dictionary<string, AnimationClip> clips)
        {
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            if (controller == null) controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            var machine = controller.layers[0].stateMachine;
            foreach (var state in machine.states) machine.RemoveState(state.state);
            foreach (var clip in clips)
            {
                var state = machine.AddState(clip.Key);
                state.motion = clip.Value;
                state.writeDefaultValues = false;
                if (clip.Key == "idle") machine.defaultState = state;
            }
            EditorUtility.SetDirty(controller);
            return controller;
        }

        static MagicalGirlPreviewController.MaterialPair[] MakePreviewMaterials(Renderer[] renderers)
        {
            var textures = AssetDatabase.FindAssets("t:Texture2D", new[] { Root + "/Character" })
                .Select(AssetDatabase.GUIDToAssetPath).ToArray();
            var pairs = new List<MagicalGirlPreviewController.MaterialPair>();
            foreach (var renderer in renderers)
            {
                var source = renderer.sharedMaterials;
                var pair = new MagicalGirlPreviewController.MaterialPair
                {
                    renderer = renderer,
                    unlit = new Material[source.Length],
                    lit = new Material[source.Length]
                };
                for (int i = 0; i < source.Length; i++)
                {
                    if (source[i] == null) throw new InvalidOperationException("A material slot is empty: " + renderer.name);
                    string region = RegionName(source[i].name);
                    if (region == null) throw new InvalidOperationException("Expected Source or Face/Hair/Hair2/Body/Leg slot, got " + source[i].name);
                    var texture = FindBaseColor(region, textures) ?? source[i].mainTexture as Texture2D;
                    if (texture == null) throw new InvalidOperationException("No BaseColor texture for " + region);
                    pair.unlit[i] = PreviewMaterial(region + "_Unlit", "Universal Render Pipeline/Unlit", texture, Color.white);
                    pair.lit[i] = PreviewMaterial(region + "_Lit", "Universal Render Pipeline/Lit", texture, Color.white);
                }
                renderer.sharedMaterials = pair.unlit;
                pairs.Add(pair);
            }
            return pairs.ToArray();
        }

        static Texture2D FindBaseColor(string region, string[] paths)
        {
            var matches = paths.Where(path =>
            {
                string file = Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
                bool regionMatch = file == region.ToLowerInvariant() || file.StartsWith(region.ToLowerInvariant() + "_");
                bool color = file.Contains("basecolor") || file.Contains("albedo") || file.Contains("diffuse") || file.EndsWith("_d") || file == region.ToLowerInvariant();
                return regionMatch && color;
            }).ToArray();
            if (matches.Length > 1) throw new InvalidOperationException("Ambiguous BaseColor files for " + region + ": " + string.Join(", ", matches));
            return matches.Length == 1 ? AssetDatabase.LoadAssetAtPath<Texture2D>(matches[0]) : null;
        }

        static Material PreviewMaterial(string name, string shaderName, Texture texture, Color color)
        {
            string path = Generated + "/" + name + ".mat";
            var shader = Shader.Find(shaderName);
            if (shader == null) throw new InvalidOperationException("Required built-in URP shader is unavailable: " + shaderName);
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null) { material = new Material(shader); AssetDatabase.CreateAsset(material, path); }
            material.shader = shader;
            material.SetTexture("_BaseMap", texture);
            material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", .1f);
            if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", 0f);
            if (material.HasProperty("_Cull")) material.SetFloat("_Cull", 0f);
            EditorUtility.SetDirty(material);
            return material;
        }

        static MagicalGirlSecondaryMotion ConfigureSecondaryMotion(GameObject visual)
        {
            var secondary = visual.AddComponent<MagicalGirlSecondaryMotion>();
            var transforms = visual.GetComponentsInChildren<Transform>();
            bool Auxiliary(Transform bone) => bone.name.StartsWith("WH_HairBack_", StringComparison.Ordinal) || bone.name.StartsWith("WH_Skirt_", StringComparison.Ordinal);
            var roots = transforms.Where(bone => Auxiliary(bone) && (bone.parent == null || !Auxiliary(bone.parent))).ToArray();
            var chains = new List<MagicalGirlSecondaryMotion.Chain>();
            foreach (var root in roots)
            {
                var path = new List<Transform>();
                Transform current = root;
                while (current != null)
                {
                    path.Add(current);
                    var children = current.Cast<Transform>().Where(Auxiliary).ToArray();
                    if (children.Length > 1) throw new InvalidOperationException("Auxiliary chain branches; configure explicit paths: " + current.name);
                    current = children.FirstOrDefault();
                }
                if (path.Count >= 2) chains.Add(new MagicalGirlSecondaryMotion.Chain
                {
                    bones = path.ToArray(),
                    maxAngle = root.name.StartsWith("WH_Skirt_", StringComparison.Ordinal) ? 7f : 12f,
                    stiffness = root.name.StartsWith("WH_Skirt_", StringComparison.Ordinal) ? 32f : 24f
                });
            }
            secondary.chains = chains.ToArray();
            // Collision volumes must be fitted to the delivered geometry; empty avoids pretending they were reviewed.
            secondary.collisionSpheres = Array.Empty<MagicalGirlSecondaryMotion.Sphere>();
            // Enable only during review until collider fitting and extreme poses have passed.
            secondary.motionEnabled = false;
            return secondary;
        }

        static void CreateGround()
        {
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.name = "Preview Ground";
            floor.transform.position = new Vector3(0f, -.06f, 0f);
            floor.transform.localScale = new Vector3(20f, .12f, 20f);
            floor.GetComponent<Renderer>().sharedMaterial = PreviewMaterial("Ground", "Universal Render Pipeline/Lit", null, new Color(.38f, .45f, .43f));
            var grid = new GameObject("One Metre Reference Grid");
            var gridMaterial = PreviewMaterial("Grid", "Universal Render Pipeline/Unlit", null, new Color(.51f, .57f, .55f));
            for (int i = -9; i <= 9; i++)
            {
                for (int axis = 0; axis < 2; axis++)
                {
                    var line = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    line.name = (axis == 0 ? "X " : "Z ") + i;
                    line.transform.SetParent(grid.transform, false);
                    line.transform.position = axis == 0 ? new Vector3(i, .002f, 0f) : new Vector3(0f, .002f, i);
                    line.transform.localScale = axis == 0 ? new Vector3(.012f, .002f, 19f) : new Vector3(19f, .002f, .012f);
                    Object.DestroyImmediate(line.GetComponent<Collider>());
                    line.GetComponent<Renderer>().sharedMaterial = gridMaterial;
                }
            }
        }

        public static string StateName(string name)
        {
            int separator = name.LastIndexOf('|');
            return (separator >= 0 ? name.Substring(separator + 1) : name).Trim();
        }

        public static string RegionName(string name)
        {
            if (name.Equals("Source", StringComparison.OrdinalIgnoreCase) || name.StartsWith("Source_", StringComparison.OrdinalIgnoreCase)) return "Source";
            foreach (string region in Regions.OrderByDescending(r => r.Length))
                if (name.Equals(region, StringComparison.OrdinalIgnoreCase) || name.StartsWith(region + "_", StringComparison.OrdinalIgnoreCase) || name.StartsWith(region + ".", StringComparison.OrdinalIgnoreCase)) return region;
            return null;
        }

        static Bounds BoundsOf(Renderer[] renderers)
        {
            var bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
            return bounds;
        }

        [MenuItem("Weather House/Magical Girl/Check Preview Logic")]
        public static void RunLogicChecks()
        {
            Assert(StateName("Armature|walk") == "walk", "FBX clip naming");
            Assert(StateName("airborne") == "airborne", "Plain clip naming");
            Assert(RegionName("Hair2.001") == "Hair2", "Hair2 must not become Hair");
            Assert(RegionName("Face_Unlit") == "Face", "Preview material region");
            Assert(RegionName("Unexpected") == null, "Unknown material must remain unsupported");
            Assert(RegionName("Source_Unlit") == "Source", "Structural review explicitly preserves the source UV stage");
            Assert(Mathf.Abs(MagicalGirlPreviewController.PhaseTime(2.25f, 2f) - .5f) < .0001f, "Walk/run phase transfer");
            Assert(MagicalGirlPreviewController.PhaseTime(3f, 2f) == 0f, "Whole cycle wraps to zero");
            Assert(Mathf.Abs(MagicalGirlPreviewController.MapGaitPhase("walk", "run", .9f, .28f) - .18f) < .0001f, "Walk to run uses measured foot phase and wraps");
            Assert(Mathf.Abs(MagicalGirlPreviewController.MapGaitPhase("run", "walk", .18f, .28f) - .9f) < .0001f, "Run to walk reverses phase offset");
            Assert(!MagicalGirlPreviewController.IsLocomotion("idle"), "Idle must not transfer an arbitrary gait phase");
            Debug.Log("Magical Girl preview logic checks passed.");
        }

        [MenuItem("Weather House/Magical Girl/Validate Open Preview")]
        public static void ValidateOpenScene()
        {
            Assert(SceneManager.GetActiveScene().path == ScenePath, "Only validate the independent preview scene");
            var preview = Object.FindObjectOfType<MagicalGirlPreviewController>();
            Assert(preview != null, "Preview controller exists");
            Assert(preview.animator != null && preview.animator.runtimeAnimatorController != null, "Animator controller assigned");
            Assert(!preview.animator.applyRootMotion, "CharacterController owns displacement");
            Assert(preview.previewCamera != null, "Camera assigned");
            foreach (string clip in RequiredClips) Assert(preview.clips.Any(c => c.state == clip && c.clip != null), "Required clip " + clip);
            foreach (var pair in preview.materials)
                Assert(pair.renderer != null && pair.unlit.All(m => m != null && m.mainTexture != null) && pair.lit.Length == pair.unlit.Length, "Preview materials and BaseColor textures");
            var regions = new HashSet<string>(preview.materials.SelectMany(pair => pair.unlit).Select(material => RegionName(material.name)));
            Assert(regions.SetEquals(new[] { "Source" }) || regions.SetEquals(Regions), "Explicit source-UV review or complete five-region material layout");
            var meshes = preview.visual.GetComponentsInChildren<SkinnedMeshRenderer>();
            int blendShapes = meshes.Sum(m => m.sharedMesh == null ? 0 : m.sharedMesh.blendShapeCount);
            var report = new ValidationReport
            {
                scene = ScenePath, model = ModelPath, time = DateTime.UtcNow.ToString("O"),
                clips = preview.clips.Select(c => c.state).ToArray(),
                blendShapeCount = blendShapes, auxiliaryChains = preview.secondaryMotion.chains.Length,
                modelYawOffset = Mathf.DeltaAngle(0f, preview.visual.localEulerAngles.y),
                assetStage = preview.assetStage,
                limitation = "Edit-mode wiring checks only. Facing direction, motion, expression deformation, collisions and final visuals need Play review."
            };
            File.WriteAllText(Root + "/PreviewValidation.json", JsonUtility.ToJson(report, true));
            Debug.Log("Preview wiring validation passed; runtime and visual review remain required.");
        }

        [Serializable]
        sealed class ValidationReport
        {
            public string scene, model, time, limitation, assetStage;
            public string[] clips;
            public int blendShapeCount, auxiliaryChains;
            public float modelYawOffset;
        }
        static void Assert(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("Preview check failed: " + message);
        }
    }
}
