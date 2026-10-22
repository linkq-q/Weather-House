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
