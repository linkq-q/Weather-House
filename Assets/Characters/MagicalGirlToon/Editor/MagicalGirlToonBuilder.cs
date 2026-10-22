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
using WeatherHousePrototype;
using Object = UnityEngine.Object;

namespace WeatherHouseToon.Editor
{
    public static class MagicalGirlToonBuilder
    {
        const string Root = "Assets/Characters/MagicalGirlToon";
        const string Generated = Root + "/Generated";
        const string Model = Root + "/Character/Character.obj";
        const string SourceScene = "Assets/Scenes/WeatherHouse_MapDraft.unity";
        public const string ScenePath = "Assets/Scenes/Experimental/WeatherHouse_iPad_Toon.unity";
        const string Review = "ToonPreviewReview";
        static readonly string[] Unsupported = {
            "_FaceMode", "_HairMode", "_BodyMode", "_UseSkinOverlay", "_FaceSDFStrength",
            "_FaceHairShadowStrength", "_FaceNoseShift", "_DiffuseRampStrength", "_StylizedMaskStrength",
            "_PackedNormalStrength", "_SourceInputsStrength", "_SourceEmissionStrength",
            "_SkinScatterStrength", "_NativeFaceOutlineMask", "_StylizedInputDebug"
        };

        [MenuItem("Weather House/iPad Character/Build Toon Scene Copy")]
        public static void Build()
        {
            Require(!EditorApplication.isPlaying, "Stop Play before building the scene copy.");
            for (int i = 0; i < SceneManager.sceneCount; i++)
                Require(!SceneManager.GetSceneAt(i).isDirty, "An open scene has unsaved changes. The scene was left untouched.");
            Require(!File.Exists(ScenePath), "Preview scene already exists; open it instead of overwriting edits.");
            Directory.CreateDirectory(Generated);
            Directory.CreateDirectory(Review);
            AssetDatabase.Refresh();
            var shader = Shader.Find("ZZZ/Character Toon");
            Require(shader != null && !ShaderUtil.ShaderHasError(shader), "The original ZZZ toon shader did not compile.");

            var importer = AssetImporter.GetAtPath(Model) as ModelImporter;
            Require(importer != null, "OBJ importer missing.");
            importer.globalScale = 1;
            importer.importNormals = ModelImporterNormals.Import;
            importer.importTangents = ModelImporterTangents.CalculateMikk;
            importer.importAnimation = false;
            importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
            importer.SaveAndReimport();
            var materials = new Dictionary<string, Material[]>();
            foreach (string texturePath in Directory.GetFiles(Root + "/Character", "*-color.png"))
            {
                string path = texturePath.Replace('\\', '/');
                string name = Path.GetFileNameWithoutExtension(path).Replace("-color", "");
                var ti = (TextureImporter)AssetImporter.GetAtPath(path);
                ti.sRGBTexture = true; ti.mipmapEnabled = true; ti.maxTextureSize = 4096;
                ti.alphaSource = TextureImporterAlphaSource.None;
                ti.textureCompression = TextureImporterCompression.CompressedHQ;
                ti.SaveAndReimport();
                var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                var toon = new Material(shader) { name = name + "_Toon" };
                toon.SetTexture("_BaseMap", texture); toon.SetColor("_BaseColor", Color.white);
                foreach (string property in Unsupported) toon.SetFloat(property, 0);
                bool face = name == "Head_Paint", eye = name.StartsWith("Eye_");
                toon.SetFloat("_AlphaClip", 0); toon.SetFloat("_Cull", 0);
                toon.SetFloat("_FormStrength", .25f);
                toon.SetFloat("_ShadeThreshold", face || eye ? .38f : .48f);
                toon.SetFloat("_ShadeSoftness", .07f);
                toon.SetFloat("_DirectStrength", .9f);
                toon.SetFloat("_FillStrength", face || eye ? 1.05f : .85f);
                toon.SetColor("_ShadeColor", new Color(.76f, .78f, .82f));
                toon.SetFloat("_ShadowStrength", face || eye ? .28f : .65f);
                toon.SetFloat("_EnvironmentShadowStrength", face || eye ? .35f : .65f);
                toon.SetFloat("_SpecStrength", face ? .025f : .07f);
                toon.SetFloat("_RimStrength", .06f);
                toon.SetFloat("_OcclusionStrength", face || eye ? .12f : .4f);
                toon.SetFloat("_OutlineWidth", eye ? 0 : face ? .8f : 1.4f);
                toon.SetFloat("_OutlineDistance", 4);
                toon.SetColor("_OutlineColor", new Color(.17f, .13f, .12f, 1));
                var unlit = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { name = name + "_Unlit" };
                unlit.SetTexture("_BaseMap", texture); unlit.SetColor("_BaseColor", Color.white);
                unlit.SetFloat("_Cull", 0);
                AssetDatabase.CreateAsset(toon, Generated + "/" + toon.name + ".mat");
                AssetDatabase.CreateAsset(unlit, Generated + "/" + unlit.name + ".mat");
                materials.Add(name, new[] { toon, unlit });
            }
            Require(materials.Count == 12, "Expected all 12 painted material groups.");
            int rendererIndex = ConfigureOutline();
            var scene = EditorSceneManager.OpenScene(SourceScene, OpenSceneMode.Single);
            Require(EditorSceneManager.SaveScene(scene, ScenePath), "Could not create the preview copy.");
            var walker = Object.FindObjectOfType<PrototypeWalker>();
            Require(walker != null && walker.visualRoot != null, "Existing player/visual missing.");
            walker.visualRoot.gameObject.SetActive(false);
            var visual = new GameObject("iPad Repainted Character");
            visual.transform.SetParent(walker.transform, false);
            var model = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Model), scene);
            model.transform.SetParent(visual.transform, false);
            var renderers = model.GetComponentsInChildren<Renderer>();
            Require(renderers.Length == 12, "Expected 12 imported mesh parts.");
            var eyeRenderers = renderers.Where(r => r.name.Contains("Eye_")).ToArray();
            var headRenderer = renderers.Single(r => r.name.Contains("Head_"));
            Require(eyeRenderers.Length == 2, "Eye parts could not be identified.");
            Vector3 faceForward = (eyeRenderers[0].bounds.center + eyeRenderers[1].bounds.center) * .5f - headRenderer.bounds.center;
            faceForward.y = 0;
            Require(faceForward.sqrMagnitude > 1e-6f, "Cannot establish the face direction.");
            model.transform.localRotation = Quaternion.FromToRotation(faceForward.normalized, Vector3.forward);
            model.transform.localScale *= 1.8f / BoundsOf(renderers).size.y;
            var bounds = BoundsOf(renderers);
            model.transform.position += new Vector3(walker.transform.position.x - bounds.center.x,
                walker.transform.position.y - bounds.min.y, walker.transform.position.z - bounds.center.z);
            var preview = visual.AddComponent<CharacterRenderPreview>();
            preview.surfaces = renderers.Select(r =>
            {
                var slots = r.sharedMaterials;
                var pair = new CharacterRenderPreview.Surface { renderer = r,
                    toon = new Material[slots.Length], unlit = new Material[slots.Length] };
                for (int i = 0; i < slots.Length; i++)
                {
                    string name = slots[i].name;
                    Require(materials.ContainsKey(name), "Unmapped material: " + name);
                    pair.toon[i] = materials[name][0]; pair.unlit[i] = materials[name][1];
                }
                r.shadowCastingMode = ShadowCastingMode.On;
                return pair;
            }).ToArray();
            preview.SetMode(0);
            PrefabUtility.SaveAsPrefabAssetAndConnect(visual, Generated + "/MagicalGirl_iPad_Toon.prefab", InteractionMode.AutomatedAction);
            walker.visualRoot = visual.transform;
            walker.yaw = 180; walker.pitch = 15;
            foreach (var camera in Object.FindObjectsOfType<Camera>(true))
                camera.GetUniversalAdditionalCameraData().SetRenderer(rendererIndex);
            var view = walker.viewCamera;
            view.transform.position = walker.transform.position + new Vector3(.6f, 1.4f, 3.5f);
            view.transform.LookAt(walker.transform.position + Vector3.up * .95f);
            view.GetUniversalAdditionalCameraData().antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            view.GetUniversalAdditionalCameraData().antialiasingQuality = AntialiasingQuality.High;
            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(scene);
            Require(EditorSceneManager.SaveScene(scene), "Preview scene save failed.");
            Selection.activeGameObject = visual;
            if (SceneView.lastActiveSceneView != null)
                SceneView.lastActiveSceneView.LookAt(BoundsOf(renderers).center, Quaternion.Euler(12, 180, 0), 3.4f, false, true);
            Validate();
            Capture();
            EditorSceneManager.SaveScene(scene);
        }

        static int ConfigureOutline()
        {
            string rendererPath = Generated + "/ToonRenderer.asset";
            Require(AssetDatabase.CopyAsset("Assets/Settings/URP-HighFidelity-Renderer.asset", rendererPath), "Renderer copy failed.");
            var data = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(rendererPath);
            var feature = ScriptableObject.CreateInstance<CharacterOutlineFeature>();
            feature.name = "ZZZ Character Outline";
            AssetDatabase.AddObjectToAsset(feature, data);
            data.rendererFeatures.Add(feature); feature.SetActive(true); data.SetDirty();
            EditorUtility.SetDirty(data);
            int sharedIndex = -1;
            foreach (string path in new[] { "Assets/Settings/URP-Performant.asset", "Assets/Settings/URP-Balanced.asset", "Assets/Settings/URP-HighFidelity.asset" })
            {
                var asset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(path);
                var so = new SerializedObject(asset);
                var list = so.FindProperty("m_RendererDataList");
                int index = list.arraySize;
                if (sharedIndex < 0) sharedIndex = index;
                Require(sharedIndex == index, "Existing renderer counts differ by quality; do not change camera indices blindly.");
                list.InsertArrayElementAtIndex(index);
                list.GetArrayElementAtIndex(index).objectReferenceValue = data;
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(asset);
            }
            return sharedIndex;
        }

        public static void Validate()
        {
            var preview = Object.FindObjectOfType<CharacterRenderPreview>();
            Require(preview != null && preview.surfaces.Length == 12, "Preview surfaces missing.");
            var toon = Shader.Find("ZZZ/Character Toon");
            Require(toon.isSupported && !ShaderUtil.ShaderHasError(toon), "Shader unsupported or has errors.");
            int triangles = 0;
            foreach (var s in preview.surfaces)
            {
                var mesh = s.renderer.GetComponent<MeshFilter>().sharedMesh;
                for (int i = 0; i < mesh.subMeshCount; i++) triangles += (int)mesh.GetIndexCount(i) / 3;
                Require(mesh.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.TexCoord0), "UV0 missing.");
                foreach (var material in s.toon)
                {
                    Require(material.shader == toon && material.GetTexture("_BaseMap") != null, "Toon/texture connection missing.");
                    foreach (string property in Unsupported) Require(material.GetFloat(property) == 0, "Unadapted effect enabled: " + property);
                }
            }
            Require(triangles == 23852, "Unexpected triangle count: " + triangles);
            var walker = Object.FindObjectOfType<PrototypeWalker>();
            Require(walker.visualRoot == preview.transform, "Existing movement controller is not linked to the new visual.");
            var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(Generated + "/ToonRenderer.asset");
            Require(renderer.rendererFeatures.Any(f => f is CharacterOutlineFeature && f.isActive), "Outline feature inactive.");
            File.WriteAllText(Review + "/validation.txt", "PASS\nScene=" + SceneManager.GetActiveScene().path +
                "\nParts=12\nTriangles=" + triangles + "\nUV0 and all 12 BaseColor textures connected" +
                "\nShader supported; shader errors=0\nOutline feature active; source-specific effects=0" +
                "\nPlayer visual linked; character is static (OBJ has no skeleton).\n");
        }

        [MenuItem("Weather House/iPad Character/Apply Warm Character Fill")]
        public static void WarmLight()
        {
            Require(!EditorApplication.isPlaying, "Stop Play before saving the warm light setup.");
            Require(SceneManager.GetActiveScene().path == ScenePath, "Only change the new preview scene.");
            var preview = Object.FindObjectOfType<CharacterRenderPreview>();
            foreach (var material in preview.surfaces.SelectMany(s => s.toon).Distinct())
            {
                bool face = material.name.StartsWith("Head_") || material.name.StartsWith("Eye_");
                material.SetFloat("_FormStrength", face ? 1f : .55f);
                material.SetFloat("_ShadeThreshold", face ? .35f : .44f);
                material.SetFloat("_ShadeSoftness", .10f);
                material.SetFloat("_DirectStrength", .78f);
                material.SetFloat("_FillStrength", .42f);
                material.SetColor("_ShadeColor", new Color(.98f, .89f, .82f));
                material.SetFloat("_ShadowStrength", face ? .12f : .45f);
                material.SetFloat("_EnvironmentShadowStrength", face ? .18f : .45f);
                material.SetFloat("_AdditionalLightStrength", .65f);
                material.SetFloat("_AdditionalLightLimit", .60f);
                material.SetFloat("_SpecStrength", face ? .02f : .035f);
                material.SetFloat("_OcclusionStrength", face ? .06f : .25f);
                EditorUtility.SetDirty(material);
                AssetDatabase.SaveAssetIfDirty(material);
            }
            var walker = Object.FindObjectOfType<PrototypeWalker>();
            ConfigureFill(walker.transform, "Warm Portrait Fill", new Vector3(.75f, 2.45f, 2.2f),
                new Color(1f, .93f, .86f), 3.5f);
            ConfigureFill(walker.transform, "Soft Rear Fill", new Vector3(-1.3f, 1.9f, -2.1f),
                new Color(1f, .96f, .90f), 1.6f);
            preview.SetMode(0);
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
            Capture();
            EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
            File.WriteAllText(Review + "/warm-light.txt", "Warm and soft fill lights affect layer 8 only. Scene sun, environment and post-processing unchanged.\nGeneric face shading softened; fitted face/SDF/hair effects remain off.\n");
        }

        static void ConfigureFill(Transform player, string name, Vector3 offset, Color color, float intensity)
        {
            var child = player.Find(name);
            if (child == null) { child = new GameObject(name, typeof(Light)).transform; child.SetParent(player, false); }
            child.localPosition = offset;
            child.LookAt(player.position + Vector3.up * .9f);
            var light = child.GetComponent<Light>();
            light.type = LightType.Spot; light.color = color; light.intensity = intensity;
            light.spotAngle = 105; light.innerSpotAngle = 80; light.range = 7;
            light.shadows = LightShadows.None; light.cullingMask = 1 << 8;
            light.renderMode = LightRenderMode.ForcePixel;
        }

        public static void RuntimeCheck()
        {
            Require(EditorApplication.isPlaying, "Runtime check requires Play.");
            Validate();
            var preview = Object.FindObjectOfType<CharacterRenderPreview>();
            var walker = Object.FindObjectOfType<PrototypeWalker>();
            for (int mode = 0; mode < 3; mode++)
            {
                preview.SetMode(mode);
                Require(preview.surfaces.All(s => s.renderer.gameObject.layer == 8), "Light layer changed when switching render modes.");
                Require(preview.surfaces.All(s => s.renderer.sharedMaterials.SequenceEqual(mode == 2 ? s.unlit : s.toon)), "Material mode switch failed.");
            }
            preview.SetMode(0);
            walker.SetOverview(true); Require(walker.mapCamera.enabled && !walker.viewCamera.enabled, "Map mode failed.");
            walker.SetOverview(false); Require(walker.viewCamera.enabled && !walker.mapCamera.enabled, "Walk camera mode failed.");
            Capture();
            var fills = walker.GetComponentsInChildren<Light>().Where(l => l.name == "Warm Portrait Fill" || l.name == "Soft Rear Fill").ToArray();
            Require(fills.Length == 2 && fills.All(l => l.cullingMask == (1 << 8)), "Character-only fill setup missing.");
            SaveCamera(walker.viewCamera, Review + "/06_FillIsolation_On.png");
            try
            {
                foreach (var light in fills) light.enabled = false;
                SaveCamera(walker.viewCamera, Review + "/07_FillIsolation_Off.png");
            }
            finally { foreach (var light in fills) light.enabled = true; }
            File.WriteAllText(Review + "/runtime-check.txt", "PASS: Play mode; 12 painted surfaces; all three render modes; character light layer stable; walking/map camera switch.\nStatic OBJ; skeletal animation not present or claimed.\n");
        }

        [MenuItem("Weather House/iPad Character/Capture Preview Comparisons")]
        public static void Capture()
        {
            var preview = Object.FindObjectOfType<CharacterRenderPreview>();
            Require(preview != null, "Open the toon preview scene first.");
            var walker = Object.FindObjectOfType<PrototypeWalker>();
            var camera = walker.viewCamera;
            Vector3 position = camera.transform.position;
            Quaternion rotation = camera.transform.rotation;
            float fov = camera.fieldOfView;
            int priorMode = preview.mode;
            try
            {
                var target = BoundsOf(preview.surfaces.Select(s => s.renderer).ToArray()).center;
                for (int mode = 0; mode < 3; mode++)
                {
                    preview.SetMode(mode);
                    camera.fieldOfView = 40;
                    camera.transform.position = target + new Vector3(.35f, .12f, 3.4f);
                    camera.transform.LookAt(target);
                    SaveCamera(camera, Review + "/" + new[] { "01_Toon_Outline", "02_Toon_NoOutline", "03_Original_Color" }[mode] + ".png");
                }
                preview.SetMode(0);
                camera.transform.position = target + new Vector3(.5f, .18f, -3.5f); camera.transform.LookAt(target);
                SaveCamera(camera, Review + "/04_Toon_Back.png");
                camera.fieldOfView = fov; camera.transform.SetPositionAndRotation(position, rotation);
                SaveCamera(camera, Review + "/05_Scene_GameCamera.png");
            }
            finally
            {
                preview.SetMode(priorMode); camera.fieldOfView = fov;
                camera.transform.SetPositionAndRotation(position, rotation);
            }
        }

        static void SaveCamera(Camera camera, string path)
        {
            const int w = 1600, h = 1200;
            var rt = RenderTexture.GetTemporary(w, h, 24, RenderTextureFormat.ARGB32);
            var prior = camera.targetTexture; var active = RenderTexture.active;
            Texture2D tex = null;
            try
            {
                camera.targetTexture = rt; camera.Render(); RenderTexture.active = rt;
                tex = new Texture2D(w, h, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, w, h), 0, 0); tex.Apply();
                File.WriteAllBytes(path, tex.EncodeToPNG());
            }
            finally { camera.targetTexture = prior; RenderTexture.active = active; RenderTexture.ReleaseTemporary(rt); if (tex != null) Object.DestroyImmediate(tex); }
        }

        static Bounds BoundsOf(Renderer[] renderers)
        {
            var bounds = renderers[0].bounds;
            foreach (var renderer in renderers.Skip(1)) bounds.Encapsulate(renderer.bounds);
            return bounds;
        }
        static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    }
}
