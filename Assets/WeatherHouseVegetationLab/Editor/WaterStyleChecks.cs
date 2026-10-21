using System;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace WeatherHouse.Vegetation.Editor
{
    public static class WaterStyleChecks
    {
        // Render the actual scene through URP; property existence alone cannot prove an effect is wired.
        public static void Verify(Camera camera)
        {
            var reflection = Object.FindObjectOfType<PainterlyWaterReflection>();
            if (!reflection) throw new Exception("Water reflection component missing.");
            var renderer = reflection.GetComponent<Renderer>();
            var saved = renderer.sharedMaterial;
            var test = new Material(saved);
            renderer.sharedMaterial = test;
            try
            {
                var data = camera.GetUniversalAdditionalCameraData();
                if (!data.requiresColorTexture || !data.requiresDepthTexture)
                    throw new Exception("The lab camera needs opaque color and depth textures.");
                if (Mathf.Abs(saved.GetFloat("_Opacity") - .70f) > .001f)
                    throw new Exception("Default water opacity must be 70 percent.");
                test.SetFloat("_RippleSpeed", 0);
                test.SetFloat("_CausticSpeed", 0);
                var report = new StringBuilder("PASS: actual URP render comparisons at fixed camera and time.\n");
                Compare(test, camera, "_Opacity", .7f, 1, "13_Opacity", report);
                Compare(test, camera, "_ShoreStrength", 0, 1, "14_Shore", report);
                Compare(test, camera, "_SurfaceStrokeStrength", 0, .6f, "15_Strokes", report);
                Compare(test, camera, "_CausticStrength", 0, 1, "16_Caustics", report);
                Compare(test, camera, "_ReflectionBrushStrength", 0, 1, "17_BrushReflection", report);
                File.WriteAllText("VegetationLabReview/water-style-checks.txt", report.ToString());
            }
            finally { renderer.sharedMaterial = saved; Object.DestroyImmediate(test); }
        }

        static void Compare(Material material, Camera camera, string property, float off, float on, string name, StringBuilder report)
        {
            if (!material.HasProperty(property)) throw new Exception("Missing water control: " + property);
            float saved = material.GetFloat(property);
            Texture2D a = null, b = null;
            try
            {
                material.SetFloat(property, off); a = Render(camera);
                material.SetFloat(property, on); b = Render(camera);
                var left = a.GetPixels32(); var right = b.GetPixels32();
                int changed = 0;
                for (int i = 0; i < left.Length; i++)
                    if (Math.Abs(left[i].r - right[i].r) + Math.Abs(left[i].g - right[i].g) + Math.Abs(left[i].b - right[i].b) >= 6) changed++;
                File.WriteAllBytes("VegetationLabReview/" + name + "_A.png", a.EncodeToPNG());
                File.WriteAllBytes("VegetationLabReview/" + name + "_B.png", b.EncodeToPNG());
                if (changed < 50) throw new Exception(property + " is ineffective in the real render: " + changed + " changed pixels.");
                report.AppendLine(property + ": " + off + " -> " + on + ", changed pixels=" + changed + " / " + left.Length);
            }
            finally
            {
                material.SetFloat(property, saved);
                if (a) Object.DestroyImmediate(a);
                if (b) Object.DestroyImmediate(b);
            }
        }

        static Texture2D Render(Camera camera)
        {
            var target = RenderTexture.GetTemporary(800, 500, 24, RenderTextureFormat.ARGB32);
            var prior = camera.targetTexture; var active = RenderTexture.active;
            try
            {
                camera.targetTexture = target;
                camera.Render();
                RenderTexture.active = target;
                var image = new Texture2D(800, 500, TextureFormat.RGB24, false);
                image.ReadPixels(new Rect(0, 0, 800, 500), 0, 0); image.Apply();
                return image;
            }
            finally { camera.targetTexture = prior; RenderTexture.active = active; RenderTexture.ReleaseTemporary(target); }
        }
    }
}
