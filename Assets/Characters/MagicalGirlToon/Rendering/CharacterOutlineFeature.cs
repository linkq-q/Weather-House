using UnityEngine;
using UnityEngine.Experimental.Rendering.Universal;
using UnityEngine.Rendering.Universal;

public sealed class CharacterOutlineFeature : ScriptableRendererFeature
{
    private RenderObjectsPass pass;

    public override void Create()
    {
        pass = new RenderObjectsPass("Character Outline", RenderPassEvent.AfterRenderingOpaques,
            new[] { "CharacterOutline" }, RenderQueueType.Opaque, 1 << 8,
            new RenderObjects.CustomCameraSettings());
        // Contact fading must have current camera depth even when SSAO is disabled.
        pass.ConfigureInput(ScriptableRenderPassInput.Depth);
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        var camera = renderingData.cameraData.camera;
        if (camera.cameraType == CameraType.Preview || (camera.cullingMask & (1 << 8)) == 0) return;
        renderer.EnqueuePass(pass);
    }
}
