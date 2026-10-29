using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace WeatherHouse.AnimalToon
{
    // A second atlas uses URP's same cascades/caster culling, with animal casters clipped out.
    // The normal atlas remains intact so animals still cast onto the ground and other objects.
    sealed class AnimalEnvironmentShadowPass : ScriptableRenderPass
    {
        readonly ShadowSliceData[] slices = new ShadowSliceData[4];
        RTHandle atlas;
        int count;
        public AnimalEnvironmentShadowPass() { renderPassEvent = RenderPassEvent.AfterRenderingShadows; }
        public bool Setup(ref RenderingData data)
        {
            int index = data.lightData.mainLightIndex;
            if (!data.shadowData.supportsMainLightShadows || index < 0 ||
                !data.cullResults.GetShadowCasterBounds(index, out _)) return false;
            var light = data.lightData.visibleLights[index].light;
            if (!light || light.shadows == LightShadows.None) return false;
            count = data.shadowData.mainLightShadowCascadesCount;
            int width = data.shadowData.mainLightShadowmapWidth;
            int height = data.shadowData.mainLightShadowmapHeight;
            int resolution = ShadowUtils.GetMaxTileResolutionInAtlas(width, height, count);
            if (count == 2) height >>= 1;
            for (int i = 0; i < count; i++)
                if (!ShadowUtils.ExtractDirectionalLightMatrix(ref data.cullResults, ref data.shadowData,
                    index, i, width, height, resolution, light.shadowNearPlane, out _, out slices[i])) return false;
            ShadowUtils.ShadowRTReAllocateIfNeeded(ref atlas, width, height, 16, name: "Animal Environment Shadows");
            return true;
        }
        public override void Configure(CommandBuffer cmd, RenderTextureDescriptor descriptor)
        {
            ConfigureTarget(atlas);
            ConfigureClear(ClearFlag.All, Color.black);
        }
        public override void Execute(ScriptableRenderContext context, ref RenderingData data)
        {
            var cmd = CommandBufferPool.Get("Animal environment shadows");
            int index = data.lightData.mainLightIndex;
            var light = data.lightData.visibleLights[index];
            var drawing = new ShadowDrawingSettings(data.cullResults, index, BatchCullingProjectionType.Orthographic)
            { useRenderingLayerMaskTest = UniversalRenderPipeline.asset.useRenderingLayers };
            cmd.SetGlobalFloat("_AnimalEnvironmentShadowPass", 1);
            CoreUtils.SetKeyword(cmd, "_CASTING_PUNCTUAL_LIGHT_SHADOW", false);
            for (int i = 0; i < count; i++)
            {
                drawing.splitData = slices[i].splitData;
                var bias = ShadowUtils.GetShadowBias(ref light, index, ref data.shadowData,
                    slices[i].projectionMatrix, slices[i].resolution);
                ShadowUtils.SetupShadowCasterConstantBuffer(cmd, ref light, bias);
                ShadowUtils.RenderShadowSlice(cmd, ref context, ref slices[i], ref drawing);
            }
            cmd.SetGlobalFloat("_AnimalEnvironmentShadowPass", 0);
            cmd.SetGlobalTexture("_AnimalEnvironmentShadowmap", atlas);
            cmd.SetGlobalFloat("_AnimalEnvironmentShadowReady", 1);
            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }
        public override void OnCameraCleanup(CommandBuffer cmd)
        { cmd.SetGlobalFloat("_AnimalEnvironmentShadowReady", 0); }
        public void Dispose() { atlas?.Release(); }
    }
}
