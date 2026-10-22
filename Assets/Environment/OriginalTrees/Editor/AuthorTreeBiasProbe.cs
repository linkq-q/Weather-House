using System;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace WeatherHouse.OriginalTrees.Editor
{
    // Fixed-camera GPU controls verify the bias actually used by URP.
    public static class AuthorTreeBiasProbe
    {
        public static string Run()
        {
            AuthorTreeStudy.Assert(Application.isPlaying, "Bias probe requires actual Play");
            var scene = SceneManager.GetActiveScene();
            AuthorTreeStudy.Assert(SceneManager.sceneCount == 1 && scene.path == AuthorTreeStudy.ScenePath,
                "Bias probe is limited to OriginalTreesComparison");
            var camera = Camera.main;
            var sun = RenderSettings.sun;
            AuthorTreeStudy.Assert(camera && sun && camera.gameObject.scene == scene && sun.gameObject.scene == scene,
                "Comparison camera and sun required");
            AuthorTreeStudy.Assert(sun.TryGetComponent<UniversalAdditionalLightData>(out var lightData),
                "Warm up Play first so URP has initialized the light data");
            var pipeline = UniversalRenderPipeline.asset;
            AuthorTreeStudy.Assert(pipeline && pipeline.supportsMainLightShadows && sun.shadows != LightShadows.None,
                "Main-light shadows required");

            bool beforeUsePipeline = lightData.usePipelineSettings;
            float beforeDepth = sun.shadowBias, beforeNormal = sun.shadowNormalBias;
            var frames = new Texture2D[5];
            string report;
            try
            {
                // Keep the small fields even in A: if the pipeline wins, A still uses the asset values.
                lightData.usePipelineSettings = true;
                sun.shadowBias = .02f; sun.shadowNormalBias = .15f;
                frames[0] = Capture(camera, "00_Pipeline");

                // Equal-value control: switching the source must not change the actual GPU image.
                lightData.usePipelineSettings = false;
                sun.shadowBias = pipeline.shadowDepthBias;
                sun.shadowNormalBias = pipeline.shadowNormalBias;
                frames[1] = Capture(camera, "01_EqualLocal");

                sun.shadowBias = .2f; sun.shadowNormalBias = .4f;
                frames[2] = Capture(camera, "02_Local_020_040");
                sun.shadowBias = .02f; sun.shadowNormalBias = .15f;
                frames[3] = Capture(camera, "03_Local_002_015");

                lightData.usePipelineSettings = true;
                frames[4] = Capture(camera, "04_PipelineRepeat");

                int equalChanged = Changed(frames[0], frames[1], 3);
                int repeatChanged = Changed(frames[0], frames[4], 3);
                int middleChanged = Changed(frames[0], frames[2], 6);
                int smallChanged = Changed(frames[0], frames[3], 6);
                int candidateChanged = Changed(frames[2], frames[3], 6);
                AuthorTreeStudy.Assert(equalChanged == 0,
                    "Pipeline vs equal local bias must produce the same GPU image; changed=" + equalChanged);
                AuthorTreeStudy.Assert(repeatChanged == 0,
                    "Repeated pipeline capture must be stable; changed=" + repeatChanged);
                AuthorTreeStudy.Assert(smallChanged > 500,
                    "Small local bias must change the actual shadow image; changed=" + smallChanged);
                report = "PASS GPU bias source: 1200x1200; pipeline=" + pipeline.shadowDepthBias + "/" +
                    pipeline.shadowNormalBias + "; equal local override changed=" + equalChanged +
                    ", repeat changed=" + repeatChanged + " (RGB8 sum > 3). Pipeline vs local .2/.4 changed=" +
                    middleChanged + "; pipeline vs .02/.15 changed=" + smallChanged +
                    "; middle vs small changed=" + candidateChanged + " (RGB8 sum > 6).\n" +
                    "Only Light bias fields and usePipelineSettings were changed. Camera, materials, exposure, " +
                    "shadow strength and soft-shadow quality were held fixed.\n" +
                    "Image differences prove the bias source reaches GPU rendering; acne, contact and visual quality " +
                    "still require review of these fixed-camera captures.";
            }
            finally
            {
                lightData.usePipelineSettings = beforeUsePipeline;
                sun.shadowBias = beforeDepth; sun.shadowNormalBias = beforeNormal;
                foreach (var frame in frames) if (frame) Object.DestroyImmediate(frame);
            }
            AuthorTreeStudy.Assert(lightData.usePipelineSettings == beforeUsePipeline &&
                sun.shadowBias == beforeDepth && sun.shadowNormalBias == beforeNormal, "Restore all temporary light state");
            File.WriteAllText(AuthorTreeStudy.Output + "/Bias/checks.txt",
                "UTC " + DateTime.UtcNow.ToString("O") + "\nGPU " + SystemInfo.graphicsDeviceName + " / " +
                SystemInfo.graphicsDeviceType + "\n" + report + "\nTemporary light state restored.\n");
            return report;
        }

        static Texture2D Capture(Camera camera, string name)
        {
            var texture = AuthorTreeStudy.Render(camera, 1200, 1200);
            try
            {
                string folder = AuthorTreeStudy.Output + "/Bias";
                Directory.CreateDirectory(folder);
                File.WriteAllBytes(folder + "/" + name + ".png", texture.EncodeToPNG());
                return texture;
            }
            catch { Object.DestroyImmediate(texture); throw; }
        }

        static int Changed(Texture2D first, Texture2D second, int threshold)
        {
            var a = first.GetPixels32(); var b = second.GetPixels32();
            int changed = 0;
            for (int i = 0; i < a.Length; i++)
                if (Mathf.Abs(a[i].r - b[i].r) + Mathf.Abs(a[i].g - b[i].g) + Mathf.Abs(a[i].b - b[i].b) > threshold)
                    changed++;
            return changed;
        }
    }
}
