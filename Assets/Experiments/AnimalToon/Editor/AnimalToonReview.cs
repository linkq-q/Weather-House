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
using Object = UnityEngine.Object;

namespace WeatherHouse.AnimalToon.Editor
{
    // This builder is intentionally restricted to the disposable validation project.
    public static class AnimalToonReview
    {
        const string Generated = "Assets/AnimalToonSample";
        public const string ScenePath = Generated + "/AnimalToonSample.unity";
        public static void BuildAndReview()
        {
            if (!File.Exists(".animal-toon-review")) throw new InvalidOperationException("Use the isolated AnimalToonReview project.");
            Directory.CreateDirectory(Generated);
            AssetDatabase.Refresh();
            var shader = Shader.Find("Weather House/Animals/Paper Toon");
            if (!shader || ShaderUtil.ShaderHasError(shader)) throw new InvalidOperationException("Animal toon shader did not compile.");
            var renderer = ScriptableObject.CreateInstance<UniversalRendererData>();
            renderer.name = "AnimalToonRenderer";
            AssetDatabase.CreateAsset(renderer, Generated + "/Renderer.asset");
            var feature = ScriptableObject.CreateInstance<AnimalLightOutlineFeature>();
            feature.name = "Animal light edge";
            renderer.rendererFeatures.Add(feature);
            AssetDatabase.AddObjectToAsset(feature, renderer);
            renderer.SetDirty();
            var pipeline = UniversalRenderPipelineAsset.Create(renderer);
            pipeline.name = "AnimalToonPipeline";
            pipeline.msaaSampleCount = 4;
            pipeline.shadowDistance = 35;
            pipeline.shadowCascadeCount = 1;
            var pipelineSettings = new SerializedObject(pipeline);
            pipelineSettings.FindProperty("m_MainLightShadowsSupported").boolValue = true;
            pipelineSettings.FindProperty("m_MainLightShadowmapResolution").intValue = 2048;
            pipelineSettings.FindProperty("m_SoftShadowsSupported").boolValue = true;
            pipelineSettings.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.CreateAsset(pipeline, Generated + "/Pipeline.asset");
            GraphicsSettings.defaultRenderPipeline = pipeline;
            QualitySettings.renderPipeline = pipeline;
            PlayerSettings.colorSpace = ColorSpace.Linear;
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(.58f, .61f, .67f);
            RenderSettings.fog = false;
            var root = new GameObject("Animal Toon Comparison");
            var lab = root.AddComponent<AnimalToonLab>();
            lab.enabled = false;
            lab.animals = new GameObject[3]; lab.animators = new Animator[3];
            lab.walk = AssetDatabase.LoadAllAssetsAtPath("Assets/Source/walk.fbx").OfType<AnimationClip>().First(c => !c.name.StartsWith("__preview__"));
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Source/sheep.png");
            var original = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "Original sheep", mainTexture = texture };
            original.SetFloat("_Smoothness", .05f);
            AssetDatabase.CreateAsset(original, Generated + "/Original.mat");
            lab.toon = new Material(shader) { name = "Soft toon sheep", mainTexture = texture };
            lab.toon.SetFloat("_OutlineWidth", 0);
            lab.toon.SetFloat("_SmoothOutline", 1);
            AssetDatabase.CreateAsset(lab.toon, Generated + "/SoftToon.mat");
            lab.outlined = new Material(lab.toon) { name = "Light edge sheep" };
            lab.outlined.SetFloat("_OutlineWidth", lab.edgeWidth);
            AssetDatabase.CreateAsset(lab.outlined, Generated + "/LightEdge.mat");
            var modelAsset = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Source/idle.fbx");
            var meshes = new Dictionary<Mesh, Mesh>();
            for (int i = 0; i < 3; i++)
            {
                var holder = new GameObject(new[] { "01 Original", "02 Soft toon", "03 Light edge" }[i]);
                holder.transform.parent = root.transform;
                var model = Object.Instantiate(modelAsset, holder.transform);
                model.name = "Sheep";
                var animator = model.GetComponent<Animator>();
                if (!animator) animator = model.AddComponent<Animator>();
                animator.applyRootMotion = false; animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                lab.animators[i] = animator; lab.animals[i] = holder;
                // Match the production prefab's imported rest orientation, then choose a readable three-quarter view.
                var head = model.GetComponentsInChildren<Transform>().First(t => t.name == "Head");
                var hips = model.GetComponentsInChildren<Transform>().First(t => t.name == "Hips");
                Vector3 forward = Vector3.ProjectOnPlane(head.position - hips.position, Vector3.up);
                model.transform.rotation = Quaternion.Euler(0, 30 - Vector3.SignedAngle(Vector3.back, forward, Vector3.up), 0);
                foreach (var skin in model.GetComponentsInChildren<SkinnedMeshRenderer>())
                {
                    if (!meshes.TryGetValue(skin.sharedMesh, out var mesh))
                    {
                        mesh = SmoothOutlineMesh(skin.sharedMesh);
                        AssetDatabase.CreateAsset(mesh, Generated + "/SheepMesh" + meshes.Count + ".asset");
                        meshes.Add(skin.sharedMesh, mesh);
                    }
                    skin.sharedMesh = mesh;
                    skin.sharedMaterials = Enumerable.Repeat(i == 0 ? original : i == 1 ? lab.toon : lab.outlined, mesh.subMeshCount).ToArray();
                    skin.updateWhenOffscreen = true;
                }
                Bounds bounds = BoundsOf(model);
                model.transform.localScale *= 1.5f / bounds.size.y;
                bounds = BoundsOf(model);
                model.transform.position += new Vector3(-bounds.center.x, .02f - bounds.min.y, -bounds.center.z);
                holder.transform.localPosition = new Vector3((i - 1) * 2.4f, 0, 0);
            }
            var cameraGO = new GameObject("Comparison Camera");
            lab.view = cameraGO.AddComponent<Camera>();
            lab.view.tag = "MainCamera"; lab.view.orthographic = true; lab.view.orthographicSize = 2.35f;
            lab.view.transform.position = new Vector3(0, 3.15f, -9);
            lab.view.transform.LookAt(new Vector3(0, .5f, 0));
            lab.view.clearFlags = CameraClearFlags.SolidColor; lab.view.backgroundColor = new Color(.18f, .23f, .22f);
            lab.view.nearClipPlane = .1f; lab.view.farClipPlane = 60; lab.view.allowMSAA = true;
            lab.view.GetUniversalAdditionalCameraData().renderPostProcessing = false;
            lab.key = new GameObject("Main Light").AddComponent<Light>();
            lab.key.type = LightType.Directional; lab.key.color = new Color(1, .96f, .88f); lab.key.intensity = 1.1f;
            lab.key.shadows = LightShadows.Soft; lab.key.shadowBias = .035f; lab.key.shadowNormalBias = .12f;
            RenderSettings.sun = lab.key;
            var floorMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit")) { color = new Color(.3f, .36f, .32f) };
            floorMaterial.SetFloat("_Smoothness", 0);
            AssetDatabase.CreateAsset(floorMaterial, Generated + "/Ground.mat");
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.name = "Matte ground"; floor.transform.position = new Vector3(0, -.075f, 0); floor.transform.localScale = new Vector3(200, .1f, 200);
            floor.GetComponent<Renderer>().sharedMaterial = floorMaterial;
            lab.shadowBlocker = GameObject.CreatePrimitive(PrimitiveType.Cube);
            lab.shadowBlocker.name = "Shadow-only test blocker";
            lab.shadowBlocker.transform.localScale = new Vector3(15, 10, .2f);
            lab.shadowBlocker.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.ShadowsOnly;
            lab.foreground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            lab.foreground.name = "Foreground depth test";
            lab.foreground.transform.position = new Vector3(0, .7f, -.9f);
            lab.foreground.transform.localScale = new Vector3(.6f, 1.4f, .25f);
            lab.foreground.GetComponent<Renderer>().sharedMaterial = floorMaterial;
            lab.Apply();
            lab.enabled = true;
            AssetDatabase.SaveAssets();
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            Directory.CreateDirectory("../captures");
            SessionState.SetBool("AnimalToonReview.Run", true);
            EditorApplication.isPlaying = true;
        }

        static Bounds BoundsOf(GameObject model)
        {
            var skins = model.GetComponentsInChildren<SkinnedMeshRenderer>();
            var bounds = skins[0].bounds;
            foreach (var skin in skins) bounds.Encapsulate(skin.bounds);
            return bounds;
        }

        static Mesh SmoothOutlineMesh(Mesh source)
        {
            var mesh = Object.Instantiate(source); mesh.name = source.name + " LightEdge";
            var vertices = mesh.vertices; var normals = mesh.normals; var tangents = mesh.tangents;
            if (tangents.Length != vertices.Length) { mesh.RecalculateTangents(); tangents = mesh.tangents; }
            // UV seams share an outline direction. Encode it in the skinned normal/tangent frame.
            var sums = new Dictionary<Vector3Int, Vector3>();
            Func<Vector3, Vector3Int> key = p => Vector3Int.RoundToInt(p * 100000f);
            for (int i = 0; i < vertices.Length; i++)
            {
                Vector3Int k = key(vertices[i]);
                sums.TryGetValue(k, out var n); sums[k] = n + normals[i];
            }
            var smooth = new List<Vector3>(vertices.Length);
            for (int i = 0; i < vertices.Length; i++)
            {
                Vector3 n = normals[i].normalized, t = new Vector3(tangents[i].x, tangents[i].y, tangents[i].z);
                t = (t - n * Vector3.Dot(n, t)).normalized;
                Vector3 b = Vector3.Cross(n, t) * tangents[i].w, s = sums[key(vertices[i])].normalized;
                smooth.Add(new Vector3(Vector3.Dot(s, t), Vector3.Dot(s, b), Vector3.Dot(s, n)));
            }
            mesh.SetUVs(3, smooth);
            return mesh;
        }
    }
}
