using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace WeatherHouse.AnimalToon
{
    // Only opted-in cameras and Paper Toon passes participate; scene RGB is never blurred.
    public sealed class AnimalLightOutlineFeature : ScriptableRendererFeature
    {
        [SerializeField] Shader screenShader;
        Material material;
        PaperPass pass;
        public override void Create()
        {
            pass?.Dispose();
            CoreUtils.Destroy(material);
            if (!screenShader) screenShader = Shader.Find("Hidden/Weather House/Animal Paper Screen");
            if (screenShader) material = CoreUtils.CreateEngineMaterial(screenShader);
            pass = new PaperPass(material);
        }
        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData data)
        {
            if (material && data.cameraData.camera.TryGetComponent<AnimalToonView>(out var settings) && settings.isActiveAndEnabled)
            {
                pass.settings = settings;
                renderer.EnqueuePass(pass);
            }
        }
        protected override void Dispose(bool disposing)
        {
            pass?.Dispose(); CoreUtils.Destroy(material);
        }

        sealed class PaperPass : ScriptableRenderPass
        {
            public AnimalToonView settings;
            readonly Material material;
            RTHandle raw, depth, temporary, filtered;
            readonly ShaderTagId dataTag = new ShaderTagId("AnimalPaperData");
            readonly ShaderTagId compositeTag = new ShaderTagId("AnimalPaperComposite");
            public PaperPass(Material value)
            {
                material = value;
                renderPassEvent = RenderPassEvent.AfterRenderingSkybox;
                ConfigureInput(ScriptableRenderPassInput.Depth);
            }
            public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData data)
            {
                var desc = data.cameraData.cameraTargetDescriptor;
                desc.msaaSamples = 1; desc.depthBufferBits = 0;
                desc.graphicsFormat = GraphicsFormat.R16G16B16A16_SFloat;
                RenderingUtils.ReAllocateIfNeeded(ref raw, desc, FilterMode.Point, TextureWrapMode.Clamp, name: "Animal Paper Data");
                RenderingUtils.ReAllocateIfNeeded(ref temporary, desc, FilterMode.Point, TextureWrapMode.Clamp, name: "Animal Paper Horizontal");
                RenderingUtils.ReAllocateIfNeeded(ref filtered, desc, FilterMode.Point, TextureWrapMode.Clamp, name: "Animal Paper Merged");
                desc.graphicsFormat = GraphicsFormat.None; desc.depthStencilFormat = GraphicsFormat.D32_SFloat;
                RenderingUtils.ReAllocateIfNeeded(ref depth, desc, name: "Animal Paper Depth");
                ConfigureTarget(raw, depth);
                ConfigureClear(ClearFlag.All, Color.clear);
            }
            public override void Execute(ScriptableRenderContext context, ref RenderingData data)
            {
                var cmd = CommandBufferPool.Get("Animal paper regions and light edge");
                var drawing = CreateDrawingSettings(dataTag, ref data, data.cameraData.defaultOpaqueSortFlags);
                var filtering = new FilteringSettings(RenderQueueRange.opaque);
                context.DrawRenderers(data.cullResults, ref drawing, ref filtering);
                float scale = data.cameraData.cameraTargetDescriptor.height / 1080f;
                cmd.SetGlobalVector("_AnimalTexel", new Vector4(1f / raw.rt.width, 1f / raw.rt.height, scale, 0));
                cmd.SetGlobalFloat("_AnimalMergeRadius", settings.mergeRadius * scale);
                cmd.SetGlobalVector("_AnimalFilterAxis", Vector2.right);
                Blitter.BlitCameraTexture(cmd, raw, temporary, material, 0);
                cmd.SetGlobalVector("_AnimalFilterAxis", Vector2.up);
                Blitter.BlitCameraTexture(cmd, temporary, filtered, material, 0);
                cmd.SetGlobalTexture("_AnimalMerged", filtered);
                var renderer = data.cameraData.renderer;
                CoreUtils.SetRenderTarget(cmd, renderer.cameraColorTargetHandle, renderer.cameraDepthTargetHandle);
                context.ExecuteCommandBuffer(cmd); cmd.Clear();
                drawing = CreateDrawingSettings(compositeTag, ref data, data.cameraData.defaultOpaqueSortFlags);
                context.DrawRenderers(data.cullResults, ref drawing, ref filtering);

                Vector2 direction = Vector2.zero;
                float energy = 0;
                int index = data.lightData.mainLightIndex;
                if (index >= 0)
                {
                    Color lightColor = data.lightData.visibleLights[index].finalColor;
                    energy = Mathf.Clamp01(lightColor.maxColorComponent);
                    Vector3 light = -data.lightData.visibleLights[index].localToWorldMatrix.GetColumn(2);
                    Vector3 viewLight = data.cameraData.camera.transform.InverseTransformDirection(light);
                    direction = new Vector2(viewLight.x, viewLight.y).normalized;
                    // Blitter's UVs and the camera's render-to-texture projection share this orientation.
                }
                cmd.SetGlobalVector("_AnimalEdgeDirection", direction);
                cmd.SetGlobalFloat("_AnimalEdgeEnergy", energy);
                cmd.SetGlobalColor("_AnimalEdgeColor", settings.edgeColor.linear);
                // Reads raw data and depth only, so it can blend directly into camera color.
                CoreUtils.SetRenderTarget(cmd, renderer.cameraColorTargetHandle, renderer.cameraDepthTargetHandle);
                Blitter.BlitTexture(cmd, raw, new Vector4(1, 1, 0, 0), material, 1);
                context.ExecuteCommandBuffer(cmd);
                CommandBufferPool.Release(cmd);
            }
            public void Dispose()
            {
                raw?.Release(); depth?.Release(); temporary?.Release(); filtered?.Release();
            }
        }
    }
}
