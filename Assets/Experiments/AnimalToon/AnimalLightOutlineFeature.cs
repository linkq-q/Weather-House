using UnityEngine;
using UnityEngine.Experimental.Rendering.Universal;
using UnityEngine.Rendering.Universal;

namespace WeatherHouse.AnimalToon
{
    // Opt-in by shader pass: ordinary scene materials are never drawn by this feature.
    public sealed class AnimalLightOutlineFeature : ScriptableRendererFeature
    {
        RenderObjectsPass pass;
        public override void Create()
        {
            pass = new RenderObjectsPass("Animal light edge", RenderPassEvent.AfterRenderingSkybox,
                new[] { "AnimalLightOutline" }, RenderQueueType.Opaque, ~0,
                new RenderObjects.CustomCameraSettings());
        }
        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (renderingData.cameraData.camera.cameraType == CameraType.Reflection) return;
            renderer.EnqueuePass(pass);
        }
    }
}
