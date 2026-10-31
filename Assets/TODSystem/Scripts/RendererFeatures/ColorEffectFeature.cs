using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// 以指定的 RenderTexture（RT_Test）作为输入贴图（_BlitTexture），
/// 经由后处理材质 ColorEffect 处理后写回相机颜色目标，作为最终画面颜色输出到屏幕。
/// 材质的 _Red 由 Volume 中的 ColorEffectVolume 覆盖驱动，可在运行时动态改变。
/// </summary>
public class ColorEffectFeature : ScriptableRendererFeature
{
    [System.Serializable]
    public class Settings
    {
        [Tooltip("后处理材质，其 _BlitTexture 即为输入贴图")]
        public Material material;

        [Tooltip("输入贴图（RT_Test）")]
        public RenderTexture sourceTexture;

        [Tooltip("输出时机，默认在后期处理之后，即取代相机画面成为最终颜色")]
        public RenderPassEvent renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing;
    }

    public Settings settings = new Settings();

    ColorEffectPass m_Pass;
    Material m_RuntimeMaterial;

    public override void Create()
    {
        m_Pass = new ColorEffectPass();
        m_Pass.renderPassEvent = settings.renderPassEvent;
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (settings.material == null || settings.sourceTexture == null)
            return;

        // 只处理游戏相机，Scene / Preview 视图保持原样
        if (renderingData.cameraData.cameraType != CameraType.Game)
            return;

        // 从 Volume 栈读取覆盖：Global Volume 里没添加该 Override（或未勾选）时不做处理
        VolumeStack stack = VolumeManager.instance != null ? VolumeManager.instance.stack : null;
        ColorEffectVolume volume = stack != null ? stack.GetComponent<ColorEffectVolume>() : null;
        if (volume == null || !volume.IsActive())
            return;

        m_Pass.renderPassEvent = settings.renderPassEvent;
        m_Pass.Setup(GetRuntimeMaterial(), settings.sourceTexture, volume.red.value);
        renderer.EnqueuePass(m_Pass);
    }

    protected override void Dispose(bool disposing)
    {
        CoreUtils.Destroy(m_RuntimeMaterial);
        m_RuntimeMaterial = null;
    }

    // 使用运行时材质实例：只改副本，避免把项目里的材质资产改脏
    Material GetRuntimeMaterial()
    {
        if (settings.material == null)
            return null;

        if (m_RuntimeMaterial == null || m_RuntimeMaterial.shader != settings.material.shader)
        {
            CoreUtils.Destroy(m_RuntimeMaterial);
            m_RuntimeMaterial = new Material(settings.material);
        }

        return m_RuntimeMaterial;
    }

    class ColorEffectPass : ScriptableRenderPass
    {
        const string k_ProfilerTag = "ColorEffect";

        static readonly int s_RedId = Shader.PropertyToID("_Red");

        Material m_Material;
        RenderTexture m_Source;
        float m_Red;

        public void Setup(Material material, RenderTexture source, float red)
        {
            m_Material = material;
            m_Source = source;
            m_Red = red;
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            RTHandle target = renderingData.cameraData.renderer.cameraColorTargetHandle;
            if (target == null || m_Material == null || m_Source == null)
                return;

            // 用 Volume 的参数驱动 shader property
            m_Material.SetFloat(s_RedId, m_Red);

            CommandBuffer cmd = CommandBufferPool.Get(k_ProfilerTag);

            // RT_Test 作为 _BlitTexture 输入，ColorEffect 的 frag 结果写回相机颜色目标；
            // 之后 URP 的最终 Blit 会把它呈现到屏幕，即成为最终画面颜色
            cmd.SetRenderTarget(target);
            Blitter.BlitTexture(cmd, new RenderTargetIdentifier(m_Source), new Vector4(1f, 1f, 0f, 0f), m_Material, 0);

            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }
    }
}
