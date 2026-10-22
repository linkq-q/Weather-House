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

namespace WeatherHouse.OriginalTrees.Editor
{
    public static class AuthorTreeLighting
    {
        const string Thickness = "_TransmissionThicknessScale";
        const string ProfilePath = AuthorTreeStudy.Root + "/Materials/StudyExposure.asset";

        public static void ApplySaved()
        {
            AuthorTreeStudy.RequireSaved();
            var scene = ComparisonScene();
            var camera = Camera.main;
            var sun = RenderSettings.sun;
            AuthorTreeStudy.Assert(camera && camera.gameObject.scene == scene, "Comparison camera required");
            AuthorTreeStudy.Assert(sun && sun.gameObject.scene == scene, "Comparison sun required");
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(ProfilePath);
            var volume = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Volume>())
                .SingleOrDefault(value => value.sharedProfile == profile);
            AuthorTreeStudy.Assert(profile && volume, "Comparison's own StudyExposure profile required");
            var renderers = LeafRenderers(scene);
            var materials = new Material[renderers.Length];
            for (int i = 0; i < renderers.Length; i++)
            {
                materials[i] = AuthorTreeStudy.LoadMaterial("Foliage_" + AuthorTreeStudy.Species[i]);
                AuthorTreeStudy.Assert(materials[i] && materials[i].HasProperty(Thickness) &&
                    renderers[i].sharedMaterials[LeafSlot(i)] == materials[i], "Expected owned foliage material " + AuthorTreeStudy.Species[i]);
            }

            var data = camera.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = true;
            data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            data.antialiasingQuality = AntialiasingQuality.High;
            sun.shadowStrength = .9f;
            var shadowData=sun.GetUniversalAdditionalLightData();
            shadowData.usePipelineSettings=false;
            shadowData.softShadowQuality=SoftShadowQuality.High;
            RenderSettings.ambientMode=AmbientMode.Trilight;
            RenderSettings.ambientSkyColor=new Color(.53f,.60f,.72f);
            RenderSettings.ambientEquatorColor=new Color(.34f,.37f,.43f);
            RenderSettings.ambientGroundColor=new Color(1f,.9f,.65f);
            // High PCF needs a small caster margin to prevent ground self-shadow stripes.
            sun.shadowBias=.15f;
            sun.shadowNormalBias=.35f;
            EditorUtility.SetDirty(shadowData);
            if (!profile.TryGet<ColorAdjustments>(out var exposure))
            {
                exposure = profile.Add<ColorAdjustments>(false);
                AssetDatabase.AddObjectToAsset(exposure, profile);
            }
            exposure.active = true;
            exposure.postExposure.Override(.65f);
            exposure.saturation.Override(10);
            if (!profile.TryGet<WhiteBalance>(out var whiteBalance))
            {
                whiteBalance = profile.Add<WhiteBalance>(false);
                AssetDatabase.AddObjectToAsset(whiteBalance, profile);
            }
            whiteBalance.temperature.Override(10);
            EditorUtility.SetDirty(whiteBalance);
            AssetDatabase.SaveAssetIfDirty(whiteBalance);
            var ground = AuthorTreeStudy.LoadMaterial("StudyGround");
            ground.SetColor("_BaseColor", Color.white * 1.12f);
            EditorUtility.SetDirty(ground);
            AssetDatabase.SaveAssetIfDirty(ground);
            EditorUtility.SetDirty(exposure);
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssetIfDirty(exposure);
            AssetDatabase.SaveAssetIfDirty(profile);
            foreach (var material in materials)
            {
                material.SetFloat(Thickness, .75f);
                EditorUtility.SetDirty(material);
                AssetDatabase.SaveAssetIfDirty(material);
            }
            EditorUtility.SetDirty(data);
            EditorUtility.SetDirty(sun);
            EditorSceneManager.MarkSceneDirty(scene);
            AuthorTreeStudy.Assert(scene.path == AuthorTreeStudy.ScenePath &&
                EditorSceneManager.SaveScene(scene, AuthorTreeStudy.ScenePath), "Save only the original-tree comparison scene");
        }

        public static string Verify()
        {
            AuthorTreeStudy.Assert(Application.isPlaying, "Transmission verification requires actual Play");
            var scene = ComparisonScene();
            var sun = RenderSettings.sun;
            AuthorTreeStudy.Assert(sun && sun.gameObject.scene == scene, "Comparison sun required");
            AuthorTreeStudy.Assert(sun.TryGetComponent<UniversalAdditionalLightData>(out var lightData),
                "Warm up Play first so URP has initialized the light data");
            AuthorTreeStudy.Assert(!lightData.usePipelineSettings,
                "Comparison sun must use its explicit shadow bias; pipeline settings currently override it");
            var camera = Camera.main;
            AuthorTreeStudy.Assert(camera && camera.gameObject.scene == scene, "Comparison camera required");
            var renderers = LeafRenderers(scene);
            var originals = new Material[renderers.Length][];
            var copies = new Material[renderers.Length];
            var sourceValues = new float[renderers.Length];
            Texture2D thick = null, thin = null, repeated = null;
            string report;
            try
            {
                for (int i = 0; i < renderers.Length; i++)
                {
                    originals[i] = renderers[i].sharedMaterials;
                    var source = originals[i][LeafSlot(i)];
                    AuthorTreeStudy.Assert(source && source.HasProperty(Thickness), "Leaf thickness property " + AuthorTreeStudy.Species[i]);
                    sourceValues[i] = source.GetFloat(Thickness);
                    copies[i] = new Material(source) { hideFlags = HideFlags.HideAndDontSave };
                    var assigned = (Material[])originals[i].Clone();
                    assigned[LeafSlot(i)] = copies[i];
                    renderers[i].sharedMaterials = assigned;
                    copies[i].SetFloat(Thickness, 1);
                }
                thick = AuthorTreeStudy.Render(camera, 768, 768);
                foreach (var material in copies) material.SetFloat(Thickness, .5f);
                thin = AuthorTreeStudy.Render(camera, 768, 768);
                foreach (var material in copies) material.SetFloat(Thickness, 1);
                repeated = AuthorTreeStudy.Render(camera, 768, 768);

                var a = thick.GetPixels32(); var b = thin.GetPixels32(); var repeat = repeated.GetPixels32();
                int changed = 0, unstable = 0;
                double thickLight = 0, thinLight = 0;
                for (int p = 0; p < a.Length; p++)
                {
                    if (Difference(a[p], repeat[p]) > 3) unstable++;
                    if (Difference(a[p], b[p]) <= 6) continue;
                    // Only the leaf forward-lighting scalar changes; this mask includes its SMAA edge pixels.
                    changed++;
                    thickLight += Luminance(a[p]);
                    thinLight += Luminance(b[p]);
                }
                AuthorTreeStudy.Assert(unstable == 0, "Repeated thickness=1 capture must be stable; changed pixels=" + unstable);
                AuthorTreeStudy.Assert(changed > 500, "Thickness 1 vs .5 must visibly affect leaves; changed pixels=" + changed);
                AuthorTreeStudy.Assert(thinLight - thickLight > changed * .001,
                    "Higher leaf thickness must lower affected-leaf total luminance");
                report = "PASS GPU transmission: 768x768, thickness .5 -> 1 changes " + changed +
                    " pixels (RGB8 absolute sum > 6); affected-leaf linear luminance sum " +
                    thinLight.ToString("F3") + " -> " + thickLight.ToString("F3") + " (" +
                    (100 * (thinLight - thickLight) / thinLight).ToString("F2") +
                    "% lower). Repeated thickness=1 capture is stable. Only runtime leaf material copies were changed.";
            }
            finally
            {
                for (int i = 0; i < renderers.Length; i++)
                {
                    if (renderers[i] && originals[i] != null) renderers[i].sharedMaterials = originals[i];
                    if (copies[i]) Object.DestroyImmediate(copies[i]);
                }
                if (thick) Object.DestroyImmediate(thick);
                if (thin) Object.DestroyImmediate(thin);
                if (repeated) Object.DestroyImmediate(repeated);
            }
            for (int i = 0; i < renderers.Length; i++)
                AuthorTreeStudy.Assert(renderers[i].sharedMaterials.SequenceEqual(originals[i]) &&
                    originals[i][LeafSlot(i)].GetFloat(Thickness) == sourceValues[i], "Restore source material bindings and values");
            return report + " Source materials and renderer bindings preserved; camera, light and profile untouched.";
        }

        public static string VerifySoftShadowQuality()
        {
            AuthorTreeStudy.Assert(Application.isPlaying, "Soft-shadow verification requires actual Play");
            var scene = ComparisonScene();
            var camera = Camera.main; var sun = RenderSettings.sun;
            AuthorTreeStudy.Assert(camera && sun && camera.gameObject.scene == scene && sun.gameObject.scene == scene,
                "Comparison camera and sun required");
            AuthorTreeStudy.Assert(sun.TryGetComponent<UniversalAdditionalLightData>(out var data), "Warm up Play first");
            var before = data.softShadowQuality;
            AuthorTreeStudy.Assert(before == SoftShadowQuality.High && !data.usePipelineSettings, "Calibrated High shadows with explicit bias required");
            float depth = sun.shadowBias, normal = sun.shadowNormalBias;
            Texture2D medium = null, high = null, repeat = null;
            string report;
            try
            {
                data.softShadowQuality = SoftShadowQuality.Medium;
                sun.shadowBias = depth * (3.5f/2.5f);
                sun.shadowNormalBias = normal * (3.5f/2.5f);
                medium = AuthorTreeStudy.Render(camera, 1200, 1200);
                data.softShadowQuality = SoftShadowQuality.High;
                sun.shadowBias = depth; sun.shadowNormalBias = normal;
                high = AuthorTreeStudy.Render(camera, 1200, 1200);
                repeat = AuthorTreeStudy.Render(camera, 1200, 1200);
                string folder = AuthorTreeStudy.Output + "/Softness";
                Directory.CreateDirectory(folder);
                File.WriteAllBytes(folder + "/Medium.png", medium.EncodeToPNG());
                File.WriteAllBytes(folder + "/High.png", high.EncodeToPNG());
                var a = medium.GetPixels32(); var b = high.GetPixels32(); var c = repeat.GetPixels32();
                int changed = 0, unstable = 0, groundChanged = 0;
                for (int i = 0; i < a.Length; i++)
                {
                    if (Difference(a[i], b[i]) > 6) changed++;
                    if (Difference(b[i], c[i]) > 3) unstable++;
                    // Fixed unoccluded foreground rectangle; Texture2D rows start at the bottom.
                    int x=i%1200, y=i/1200;
                    if(x>=300 && x<900 && y>=20 && y<80 && Difference(a[i],b[i])>3) groundChanged++;
                }
                AuthorTreeStudy.Assert(groundChanged == 0, "Unoccluded foreground must stay free of PCF self-shadow stripes; changed=" + groundChanged);
                AuthorTreeStudy.Assert(changed > 100, "Medium to High must affect GPU pixels; changed=" + changed);
                AuthorTreeStudy.Assert(unstable == 0, "Repeated High capture must be stable; changed=" + unstable);
                report = "PASS soft-shadow quality GPU: 1200x1200; Medium -> High changed " + changed +
                    " pixels (RGB8 sum > 6); repeated High changed " + unstable + " (sum > 3). " +
                    "Unoccluded foreground changed=0 (600x60, sum > 3). Depth and normal fields were compensated by the 3.5/2.5 kernel ratio to hold effective caster bias fixed.";
            }
            finally
            {
                data.softShadowQuality = before;
                sun.shadowBias = depth; sun.shadowNormalBias = normal;
                if (medium) Object.DestroyImmediate(medium);
                if (high) Object.DestroyImmediate(high);
                if (repeat) Object.DestroyImmediate(repeat);
            }
            AuthorTreeStudy.Assert(data.softShadowQuality == before && sun.shadowBias == depth && sun.shadowNormalBias == normal, "Restore soft-shadow quality and bias");
            return report;
        }

        static Scene ComparisonScene()
        {
            var scene = SceneManager.GetActiveScene();
            AuthorTreeStudy.Assert(SceneManager.sceneCount == 1 && scene.path == AuthorTreeStudy.ScenePath,
                "Only OriginalTreesComparison is supported");
            return scene;
        }

        static Renderer[] LeafRenderers(Scene scene)
        {
            var roots = scene.GetRootGameObjects();
            var result = new Renderer[AuthorTreeStudy.Species.Length];
            for (int i = 0; i < result.Length; i++)
            {
                var tree = roots.SingleOrDefault(root => root.name == "Author " + AuthorTreeStudy.Species[i]);
                AuthorTreeStudy.Assert(tree, "Original tree " + AuthorTreeStudy.Species[i]);
                var meshes = tree.GetComponentsInChildren<MeshRenderer>();
                AuthorTreeStudy.Assert(meshes.Length == 1 && meshes[0].sharedMaterials.Length == 2,
                    "One original mesh with two material slots required");
                result[i] = meshes[0];
            }
            return result;
        }

        static int LeafSlot(int index) => AuthorTreeStudy.Species[index] == "FirTree" ? 0 : 1;
        static int Difference(Color32 a, Color32 b) => Mathf.Abs(a.r - b.r) + Mathf.Abs(a.g - b.g) + Mathf.Abs(a.b - b.b);
        static double Luminance(Color32 color) => .2126 * Mathf.GammaToLinearSpace(color.r / 255f) +
            .7152 * Mathf.GammaToLinearSpace(color.g / 255f) + .0722 * Mathf.GammaToLinearSpace(color.b / 255f);

    }
}
