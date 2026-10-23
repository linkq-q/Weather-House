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

namespace ForestSpriteCharacter.Editor
{
    public static class ForestSpriteDemoBuilder
    {
        public const string Root = "Assets/Characters/ForestSprite";
        public const string ModelPath = Root + "/Model/ForestSprite_Airy.fbx";
        public const string ScenePath = "Assets/Scenes/Experimental/ForestSprite_Demo.unity";
        public const string PrefabPath = Root + "/Generated/ForestSprite.prefab";
        public const string Review = Root + "/Verification~";
        const string Generated = Root + "/Generated";
        public static readonly string[] ClipNames = { "Idle", "Walk", "Run", "Jump" };

        [MenuItem("Weather House/Forest Sprite/Build Independent Demo")]
        public static void Build()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play before building the forest sprite demo.");
            if (!File.Exists(ModelPath)) throw new FileNotFoundException("Export the rigged FBX first.", ModelPath);
            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i).path == ScenePath)
                    throw new InvalidOperationException("Close the existing forest sprite demo before rebuilding it; this protects hand edits.");
            Directory.CreateDirectory(Generated);
            Directory.CreateDirectory(Review);
            AssetDatabase.Refresh();
            ConfigureImporter();
            var clips = LoadClips();
            foreach (string name in ClipNames) Require(clips.ContainsKey(name), "Missing animation " + name);
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            Require(model != null, "FBX imported as a GameObject");
            var previousScene = SceneManager.GetActiveScene();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            SceneManager.SetActiveScene(scene);
            try
            {
                var player = new GameObject("Forest Sprite");
                var visual = (GameObject)PrefabUtility.InstantiatePrefab(model, scene);
                visual.name = "ForestSpriteVisual";
                visual.transform.SetParent(player.transform, false);
                // Sample the imported rest animation first so FBX unit conversion on animated
                // bone transforms agrees with the pose Unity will actually display in Play.
                clips["Idle"].SampleAnimation(visual, 0f);
                var renderers = visual.GetComponentsInChildren<Renderer>();
                Require(renderers.Length > 0, "Model renderers");
                Bounds bounds = BoundsOf(renderers);
                Require(bounds.size.y > .001f, "Nonzero model height");
                visual.transform.localScale *= 1.65f / bounds.size.y;
                bounds = BoundsOf(renderers);
                visual.transform.position -= Vector3.up * bounds.min.y;
                Require(Mathf.Abs(bounds.size.y - 1.65f) < .01f, "Actual fitted character height: " + bounds.size.y);
                AssignMaterials(renderers);
                var originalWalk = clips["WalkShort"];
                clips["Walk"] = CopyAuthoredGait(clips["Walk"], false, out float originalWalkDistance, out float walkDistance);
                clips["Run"] = CopyAuthoredGait(clips["Run"], true, out _, out float runDistance);
                clips["Idle"].SampleAnimation(visual, 0f);
                var animator = visual.GetComponent<Animator>();
                if (animator == null) animator = visual.AddComponent<Animator>();
                animator.runtimeAnimatorController = CreateController(clips, originalWalk);
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                var body = renderers.FirstOrDefault(r => r.name.StartsWith("Body", StringComparison.OrdinalIgnoreCase));
                Bounds bodyBounds = body != null ? BoundsOf(new[] { body }) : BoundsOf(renderers);
                var character = player.AddComponent<CharacterController>();
                character.height = Mathf.Max(.3f, bodyBounds.max.y);
                character.radius = Mathf.Clamp(Mathf.Min(bodyBounds.size.x, bodyBounds.size.z) * .4f, .08f, .24f);
                character.center = Vector3.up * character.height * .5f;
                character.stepOffset = .12f;
                character.skinWidth = .015f;
                character.minMoveDistance = 0f;
                var preview = player.AddComponent<ForestSpriteDemoController>();
                preview.animator = animator;
                preview.visual = visual.transform;
                preview.originalWalkCycleDistance = originalWalkDistance;
                preview.walkCycleDistance = walkDistance;
                preview.runCycleDistance = runDistance;
                preview.secondaryMotion = ConfigureSecondaryMotion(visual);
                preview.capeCloth = ForestSpriteClothBuilder.Build(player, visual, preview.secondaryMotion);
                player.AddComponent<ForestSpriteCapePanel>().cape = preview.capeCloth;
                PrefabUtility.SaveAsPrefabAsset(player, PrefabPath);
                preview.demoCamera = CreateCamera();
                var sun = new GameObject("Key Light", typeof(Light)).GetComponent<Light>();
                sun.type = LightType.Directional;
                sun.color = new Color(1f, .94f, .83f);
                sun.intensity = 1.1f;
                sun.shadows = LightShadows.Soft;
                sun.transform.rotation = Quaternion.Euler(38f, 145f, 0f);
                var fill = new GameObject("Soft Fill", typeof(Light)).GetComponent<Light>();
                fill.type = LightType.Directional;
                fill.color = new Color(.78f, .88f, 1f);
                fill.intensity = .45f;
                fill.shadows = LightShadows.None;
                fill.transform.rotation = Quaternion.Euler(25f, -30f, 0f);
                RenderSettings.ambientMode = AmbientMode.Flat;
                RenderSettings.ambientLight = new Color(.68f, .73f, .75f);
                CreateGround();
                ForestSpriteClothBuilder.AddDemoFloor(preview.capeCloth);
                AssetDatabase.SaveAssets();
                Require(EditorSceneManager.SaveScene(scene, ScenePath), "Save independent demo scene");
                ForestSpriteDemoChecks.Validate(preview);
                Debug.Log("Forest sprite demo built: " + ScenePath);
            }
            finally
            {
                if (previousScene.IsValid() && previousScene.isLoaded) SceneManager.SetActiveScene(previousScene);
                EditorSceneManager.CloseScene(scene, true);
            }
        }

        static void ConfigureImporter()
        {
            AssetDatabase.ImportAsset(ModelPath, ImportAssetOptions.ForceSynchronousImport);
            var importer = AssetImporter.GetAtPath(ModelPath) as ModelImporter;
            Require(importer != null, "FBX ModelImporter");
            importer.animationType = ModelImporterAnimationType.Generic;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.importAnimation = true;
            importer.importCameras = false;
            importer.importLights = false;
            importer.isReadable = true;
            importer.optimizeGameObjects = false;
            importer.animationCompression = ModelImporterAnimationCompression.Off;
            var clips = importer.defaultClipAnimations;
            foreach (var clip in clips)
            {
                clip.name = StateName(clip.name);
                clip.loopTime = clip.name != "Jump";
                clip.loopPose = clip.loopTime;
                clip.lockRootRotation = true;
                clip.lockRootHeightY = true;
                clip.lockRootPositionXZ = true;
            }
            importer.clipAnimations = clips;
            importer.SaveAndReimport();
            foreach (string path in AssetDatabase.FindAssets("t:Texture2D", new[] { Root + "/Textures" }).Select(AssetDatabase.GUIDToAssetPath))
            {
                var texture = AssetImporter.GetAtPath(path) as TextureImporter;
                if (texture == null) continue;
                texture.maxTextureSize = 2048;
                texture.sRGBTexture = true;
                texture.mipmapEnabled = true;
                texture.SaveAndReimport();
            }
        }

        public static string StateName(string name)
        {
            int separator = name.LastIndexOf('|');
            string result = separator >= 0 ? name.Substring(separator + 1) : name;
            return ClipNames.FirstOrDefault(n => string.Equals(n, result, StringComparison.OrdinalIgnoreCase)) ?? result;
        }

        public static Dictionary<string, AnimationClip> LoadClips() => AssetDatabase.LoadAllAssetsAtPath(ModelPath)
            .OfType<AnimationClip>().Where(c => !c.name.StartsWith("__preview__", StringComparison.Ordinal))
            .GroupBy(c => StateName(c.name)).ToDictionary(g => g.Key, g => g.First());

        static AnimatorController CreateController(Dictionary<string, AnimationClip> clips, AnimationClip originalWalk)
        {
            string path = Generated + "/ForestSprite.controller";
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
            if (controller == null) controller = AnimatorController.CreateAnimatorControllerAtPath(path);
            foreach (string parameter in new[] { "WalkStride", "WalkCadence", "RunCadence", "JumpPhase" })
                if (!controller.parameters.Any(p => p.name == parameter))
                    controller.AddParameter(new AnimatorControllerParameter
                        { name = parameter, type = AnimatorControllerParameterType.Float, defaultFloat = 1f });
            // Removing a state also removes its owned BlendTree. Recreate/reuse only after removal.
            var machine = controller.layers[0].stateMachine;
            foreach (var state in machine.states) machine.RemoveState(state.state);
            var strideTree = AssetDatabase.LoadAllAssetsAtPath(path).OfType<BlendTree>().FirstOrDefault(t => t != null && t.name == "WalkStride");
            if (strideTree == null)
            {
                strideTree = new BlendTree { name = "WalkStride" };
                AssetDatabase.AddObjectToAsset(strideTree, controller);
            }
            strideTree.blendType = BlendTreeType.Simple1D;
            strideTree.blendParameter = "WalkStride";
            strideTree.useAutomaticThresholds = false;
            strideTree.children = new[]
            {
                new ChildMotion { motion = originalWalk, threshold = 0f, timeScale = 1f },
                new ChildMotion { motion = clips["Walk"], threshold = 1f, timeScale = 1f }
            };
            EditorUtility.SetDirty(strideTree);
            foreach (string name in ClipNames)
            {
                var state = machine.AddState(name);
                state.motion = name == "Walk" ? (Motion)strideTree : clips[name];
                state.writeDefaultValues = true;
                // The parameter is cycles per second; clip duration converts it to playback speed.
                if (name == "Walk")
                {
                    state.speed = clips[name].length;
                    state.speedParameter = "WalkCadence";
                    state.speedParameterActive = true;
                }
                if (name == "Run")
                {
                    state.speed = clips[name].length;
                    state.speedParameter = "RunCadence";
                    state.speedParameterActive = true;
                }
                if (name == "Jump")
                {
                    state.timeParameter = "JumpPhase";
                    state.timeParameterActive = true;
                }
                if (name == "Idle") machine.defaultState = state;
            }
            EditorUtility.SetDirty(controller);
            return controller;
        }

        // Authored in Blender with sole contact, toe roll, opposite arm swing and phase-dependent pelvis height.
        // Copying preserves one source of animation truth between the editable .blend and Unity.
        static AnimationClip CopyAuthoredGait(AnimationClip source, bool running,
            out float originalCycleDistance, out float cycleDistance)
        {
            originalCycleDistance = .52f;
            cycleDistance = running ? .34f / .22f : .38f / .5f;
            string path = Generated + "/" + (running ? "Run" : "Walk") + "_LongStride.anim";
            var result = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if (result == null)
            {
                result = Object.Instantiate(source);
                result.hideFlags = HideFlags.None;
                AssetDatabase.CreateAsset(result, path);
            }
            else EditorUtility.CopySerialized(source, result);
            result.name = running ? "Run" : "Walk";
            result.hideFlags = HideFlags.None;
            EditorUtility.SetDirty(result);
            return result;
        }

        static void AssignMaterials(Renderer[] renderers)
        {
            var materials = new Dictionary<string, Material>();
            foreach (string region in new[] { "Body", "Hair", "Hat", "Cape" })
            {
                string texturePath = AssetDatabase.FindAssets(region + "_BaseColor t:Texture2D", new[] { Root + "/Textures" })
                    .Select(AssetDatabase.GUIDToAssetPath).FirstOrDefault(p => Path.GetFileNameWithoutExtension(p) == region + "_BaseColor");
                Require(texturePath != null, "BaseColor texture " + region);
                materials[region] = Material(region, new Color(1f, 1f, 1f), AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath));
            }
            materials["Features"] = Material("Features", new Color(.025f, .014f, .012f), null);
            foreach (var renderer in renderers)
            {
                string region = new[] { "Body", "Hair", "Hat", "Cape" }.FirstOrDefault(n => renderer.name.StartsWith(n, StringComparison.OrdinalIgnoreCase));
                if (renderer.name == "Underbody") { renderer.sharedMaterial = Material("Underbody", new Color(.30f, .20f, .12f), null); continue; }
                if (region == null && (renderer.name.StartsWith("Eye", StringComparison.OrdinalIgnoreCase) || renderer.name.StartsWith("Smile", StringComparison.OrdinalIgnoreCase))) region = "Features";
                Require(region != null, "Known renderer material region: " + renderer.name);
                renderer.sharedMaterials = Enumerable.Repeat(materials[region], renderer.sharedMaterials.Length).ToArray();
            }
        }

        static Material Material(string name, Color color, Texture texture)
        {
            string path = Generated + "/" + name + ".mat";
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            Require(shader != null, "Installed URP Lit shader");
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null) { material = new Material(shader); AssetDatabase.CreateAsset(material, path); }
            material.shader = shader;
            material.SetColor("_BaseColor", color);
            material.SetTexture("_BaseMap", texture);
            material.SetFloat("_Smoothness", .12f);
            material.SetFloat("_Metallic", 0f);
            material.SetFloat("_Cull", 0f);
            EditorUtility.SetDirty(material);
            return material;
        }

        static ForestSpriteSecondaryMotion ConfigureSecondaryMotion(GameObject visual)
        {
            var motion = visual.AddComponent<ForestSpriteSecondaryMotion>();
            var bones = visual.GetComponentsInChildren<Transform>().ToDictionary(b => b.name, b => b);
            var chains = new List<ForestSpriteSecondaryMotion.Chain>();
            AddChain("Hat", new[] { "Hat_01", "Hat_02", "Hat_03", "Hat_Tip" }, ForestSpriteSecondaryMotion.MotionKind.Hat, 36f, 7.5f, 24f);
            foreach (string side in new[] { "Back", "Left", "Right" })
                AddChain("Hair_" + side, new[] { "Hair_" + side + "_01", "Hair_" + side + "_02", "Hair_" + side + "_Tip" }, ForestSpriteSecondaryMotion.MotionKind.Hair, 50f, 9f, 22f);
            motion.chains = chains.ToArray();
            motion.MotionEnabled = true;
            motion.Strength = 1f;
            return motion;

            void AddChain(string name, string[] names, ForestSpriteSecondaryMotion.MotionKind kind, float stiffness, float damping, float angle)
            {
                foreach (string bone in names) Require(bones.ContainsKey(bone), "Secondary chain bone " + bone);
                var chain = new ForestSpriteSecondaryMotion.Chain { bones = names.Select(n => bones[n]).ToArray(), kind = kind,
                    stiffness = stiffness, damping = damping, maxAngle = angle, radius = .008f };
                if (kind == ForestSpriteSecondaryMotion.MotionKind.Cape)
                {
                    var anchor = chain.bones[0];
                    Vector3 radial = Vector3.ProjectOnPlane(anchor.position - anchor.parent.position, visual.transform.up).normalized;
                    chain.outwardLocalDirection = anchor.parent.InverseTransformDirection(radial);
                }
                chains.Add(chain);
            }
        }

        static Camera CreateCamera()
        {
            var camera = new GameObject("Forest Sprite Camera", typeof(Camera), typeof(AudioListener)).GetComponent<Camera>();
            camera.tag = "MainCamera";
            camera.fieldOfView = 35f;
            camera.nearClipPlane = .03f;
            camera.farClipPlane = 60f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.17f, .23f, .23f);
            Vector3 target = Vector3.up * .8f;
            camera.transform.position = target + Quaternion.Euler(8f, 170f, 0f) * Vector3.back * 3.5f;
            camera.transform.LookAt(target);
            return camera;
        }

        static void CreateGround()
        {
            var ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ground.name = "Demo Ground";
            ground.transform.position = new Vector3(0f, -.06f, 0f);
            ground.transform.localScale = new Vector3(30f, .12f, 30f);
            ground.GetComponent<Renderer>().sharedMaterial = Material("Ground", new Color(.3f, .39f, .32f), null);
        }

        public static Bounds BoundsOf(Renderer[] renderers)
        {
            // Renderer.bounds includes imported animation envelopes, not just the visible feet.
            Bounds bounds = new Bounds();
            bool first = true;
            foreach (var renderer in renderers)
            {
                if (renderer is SkinnedMeshRenderer skin)
                {
                    // Evaluate the same four-weight bind matrices as the skinned renderer.
                    // This avoids BakeMesh scale ambiguity on FBX's 100x / 0.01x hierarchy.
                    var mesh = skin.sharedMesh;
                    Require(mesh != null && mesh.vertexCount > 0 && mesh.blendShapeCount == 0, "Supported skinned mesh: " + renderer.name);
                    var vertices = mesh.vertices;
                    var weights = mesh.boneWeights;
                    var bind = mesh.bindposes;
                    var matrices = skin.bones.Select((b, i) => b.localToWorldMatrix * bind[i]).ToArray();
                    for (int i = 0; i < vertices.Length; i++)
                    {
                        var w = weights[i];
                        Vector3 v = vertices[i];
                        Include(matrices[w.boneIndex0].MultiplyPoint3x4(v) * w.weight0
                            + matrices[w.boneIndex1].MultiplyPoint3x4(v) * w.weight1
                            + matrices[w.boneIndex2].MultiplyPoint3x4(v) * w.weight2
                            + matrices[w.boneIndex3].MultiplyPoint3x4(v) * w.weight3);
                    }
                }
                else
                {
                    var mesh = renderer.GetComponent<MeshFilter>()?.sharedMesh;
                    Require(mesh != null && mesh.vertexCount > 0, "Actual mesh bounds: " + renderer.name);
                    foreach (Vector3 vertex in mesh.vertices)
                        Include(renderer.transform.TransformPoint(vertex));
                }
            }
            Require(!first, "Nonempty actual mesh bounds");
            return bounds;

            void Include(Vector3 world)
            {
                if (first) { bounds = new Bounds(world, Vector3.zero); first = false; }
                else bounds.Encapsulate(world);
            }
        }

        public static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("Forest sprite check failed: " + message);
        }
    }
}
