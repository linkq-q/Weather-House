using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace WeatherHouse.MaterialLab
{
    public sealed class OilPaintPostFeature : ScriptableRendererFeature
    {
        public Shader shader;
        Material material;
        OilPaintPass pass;

        public override void Create()
        {
            pass?.Dispose();
            pass = null;
            CoreUtils.Destroy(material);
            material = null;
            if (!shader) shader = Shader.Find("Hidden/Weather House/Oil Paint Post");
            if (!shader || !shader.isSupported) return;
            material = CoreUtils.CreateEngineMaterial(shader);
            pass = new OilPaintPass(material);
        }

        static bool TryGetSettings(in RenderingData data, out OilPaintPostSettings settings)
        {
            settings = null;
            return data.cameraData.cameraType == CameraType.Game
                && data.cameraData.camera.TryGetComponent(out settings) && settings.HasEffect;
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (pass == null || !TryGetSettings(in renderingData, out var settings)) return;
            pass.SetSettings(settings);
            renderer.EnqueuePass(pass);
        }

        public override void SetupRenderPasses(ScriptableRenderer renderer, in RenderingData renderingData)
        {
            if (pass != null && TryGetSettings(in renderingData, out _))
                pass.SetSource(renderer.cameraColorTargetHandle);
        }

        protected override void Dispose(bool disposing)
        {
            pass?.Dispose();
            pass = null;
            CoreUtils.Destroy(material);
            material = null;
        }

        sealed class OilPaintPass : ScriptableRenderPass
        {
            static readonly int SourceSize = Shader.PropertyToID("_SourceSize");
            static readonly int OilStrength = Shader.PropertyToID("_OilStrength");
            static readonly int Radius = Shader.PropertyToID("_Radius");
            static readonly int ColorSteps = Shader.PropertyToID("_ColorSteps");
            static readonly int BrushDirection = Shader.PropertyToID("_BrushDirection");
            static readonly int PixelSize = Shader.PropertyToID("_PixelSize");
            static readonly int PixelStrength = Shader.PropertyToID("_PixelStrength");
            static readonly int SplitScreen = Shader.PropertyToID("_SplitScreen");
            static readonly int SplitPosition = Shader.PropertyToID("_SplitPosition");
            readonly Material material;
            readonly ProfilingSampler sampler = new ProfilingSampler("Material Lab Oil Paint");
            RTHandle source, scratch;
            OilPaintPostSettings settings;

            public OilPaintPass(Material material)
            {
                this.material = material;
                renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing;
                // Request a sampleable intermediate target. The shader reads the actual camera
                // color after transparents, rather than the earlier _CameraOpaqueTexture copy.
                ConfigureInput(ScriptableRenderPassInput.Color);
            }

            public void SetSettings(OilPaintPostSettings value) { settings = value; }
            public void SetSource(RTHandle value) { source = value; }

            public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
            {
                var descriptor = renderingData.cameraData.cameraTargetDescriptor;
                descriptor.depthBufferBits = 0;
                descriptor.msaaSamples = 1;
                descriptor.bindMS = false;
                descriptor.useMipMap = false;
                descriptor.autoGenerateMips = false;
                RenderingUtils.ReAllocateIfNeeded(ref scratch, descriptor, FilterMode.Bilinear,
                    TextureWrapMode.Clamp, name: "_MaterialLabOilScratch");
                ConfigureTarget(source);
            }

            public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
            {
                if (source == null || scratch == null || !settings || !settings.HasEffect) return;
                var descriptor = renderingData.cameraData.cameraTargetDescriptor;
                int width = Mathf.Max(1, descriptor.width), height = Mathf.Max(1, descriptor.height);
                float angle = settings.brushAngle * Mathf.Deg2Rad;
                float aspect = Mathf.Sqrt(Mathf.Clamp(settings.brushAspect, 1, 3));
                material.SetVector(SourceSize, new Vector4(width, height, 1f / width, 1f / height));
                material.SetFloat(OilStrength, settings.EffectiveOilStrength);
                material.SetFloat(Radius, Mathf.Clamp(settings.radius, 0, 8));
                material.SetFloat(ColorSteps, Mathf.Clamp(settings.colorSteps, 0, 32));
                material.SetVector(BrushDirection, new Vector4(Mathf.Cos(angle), Mathf.Sin(angle), aspect, 1f / aspect));
                material.SetFloat(PixelSize, Mathf.Clamp(settings.pixelSize, 1, 8));
                material.SetFloat(PixelStrength, settings.EffectivePixelStrength);
                material.SetFloat(SplitScreen, settings.splitScreen ? 1 : 0);
                material.SetFloat(SplitPosition, Mathf.Clamp01(settings.splitPosition));

                CommandBuffer cmd = CommandBufferPool.Get();
                try
                {
                    using (new ProfilingScope(cmd, sampler))
                    {
                        if (settings.EffectiveOilStrength > 0)
                            Blitter.BlitCameraTexture(cmd, source, scratch, material, 0);
                        else
                            Blitter.BlitCameraTexture(cmd, source, scratch);
                        if (settings.EffectivePixelStrength > 0)
                            Blitter.BlitCameraTexture(cmd, scratch, source, material, 1);
                        else
                            Blitter.BlitCameraTexture(cmd, scratch, source);
                    }
                    context.ExecuteCommandBuffer(cmd);
                }
                finally { CommandBufferPool.Release(cmd); }
            }

            public override void OnCameraCleanup(CommandBuffer cmd)
            {
                // The renderer owns source. Only our scratch allocation is released by Dispose.
                source = null;
                settings = null;
            }

            public void Dispose()
            {
                scratch?.Release();
                scratch = null;
                source = null;
                settings = null;
            }
        }
    }
}
