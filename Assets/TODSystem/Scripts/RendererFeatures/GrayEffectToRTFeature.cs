using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// 把选中相机（默认是 Tag 为 MainCamera 的相机，可通过 cameraMode 指定其它相机）捕获的画面，作为后处理材质
/// （GrayEffect）的全屏四边形输入贴图 _BlitTexture，并把 frag shader 的渲染结果输出到指定的 RenderTexture（RT_Test）。
/// 绘制前会先把掩码物体渲染进 RT_Test 写入模板值，因此 GrayEffect 只会作用于模板值匹配（stencil == 1）的区域。
/// </summary>
public class GrayEffectToRTFeature : ScriptableRendererFeature
{
    /// <summary>选择捕获哪个相机的画面</summary>
    public enum CameraTargetMode
    {
        [InspectorName("Main Camera（MainCamera 标签）")]
        MainCamera = 0,

        [InspectorName("指定名称的相机")]
        CameraByName = 1,

        [InspectorName("所有相机")]
        AllCameras = 2,
    }

    [System.Serializable]
    public class Settings
    {
        [Tooltip("全屏四边形使用的后处理材质，其 _BlitTexture 即相机画面")]
        public Material material;

        [Tooltip("渲染结果的输出目标（RT_Test）")]
        public RenderTexture targetTexture;

        [Tooltip("渲染时机，默认在后期处理之后，即捕获最终画面")]
        public RenderPassEvent renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing;

        [Tooltip("捕获哪个相机的画面")]
        public CameraTargetMode cameraMode = CameraTargetMode.MainCamera;

        [Tooltip("cameraMode = 指定名称的相机 时生效：相机在 Hierarchy 中的名字")]
        public string cameraName = "Main Camera";

        [Tooltip("是否先把掩码物体渲染进 RT_Test（由它们的材质写模板值），GrayEffect 只画模板匹配的区域")]
        public bool drawStencilMask = true;

        [Tooltip("参与写模板的物体所在层。保持 Everything 时由材质决定：只有写模板的材质会成为掩码")]
        public LayerMask stencilMaskLayers = ~0;
    }

    public Settings settings = new Settings();

    GrayEffectToRTPass m_Pass;

    public override void Create()
    {
        m_Pass = new GrayEffectToRTPass();
        m_Pass.renderPassEvent = settings.renderPassEvent;
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (settings.material == null || settings.targetTexture == null)
            return;

        CameraData cameraData = renderingData.cameraData;

        // Preview / Reflection 相机不参与
        if (cameraData.cameraType == CameraType.Preview || cameraData.cameraType == CameraType.Reflection)
            return;

        if (!IsCameraSelected(cameraData.camera))
            return;

        // 相机本身就以该 RT 为输出目标时跳过，避免渲染到自己
        if (cameraData.camera.targetTexture == settings.targetTexture)
            return;

        m_Pass.renderPassEvent = settings.renderPassEvent;
        m_Pass.Setup(settings.material, settings.targetTexture, settings.drawStencilMask, settings.stencilMaskLayers.value);
        renderer.EnqueuePass(m_Pass);
    }

    // 判断当前这次调用（某个相机）是否要被捕获
    bool IsCameraSelected(Camera camera)
    {
        switch (settings.cameraMode)
        {
            case CameraTargetMode.MainCamera:
                return camera.CompareTag("MainCamera");
            case CameraTargetMode.CameraByName:
                return camera.gameObject.name == settings.cameraName;
            default:
                return true;
        }
    }

    class GrayEffectToRTPass : ScriptableRenderPass
    {
        const string k_ProfilerTag = "GrayEffectToRT";

        // 掩码物体以哪个 LightMode 的 Pass 渲染进 RT_Test
        static readonly ShaderTagId k_MaskShaderTag = new ShaderTagId("UniversalForward");

        Material m_Material;
        RenderTexture m_Target;
        bool m_DrawMask;
        int m_MaskLayerMask;

        public void Setup(Material material, RenderTexture target, bool drawMask, int maskLayerMask)
        {
            m_Material = material;
            m_Target = target;
            m_DrawMask = drawMask;
            m_MaskLayerMask = maskLayerMask;
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            // 相机颜色附件（URP 中间纹理）作为后处理材质的 _BlitTexture 输入
            RTHandle source = renderingData.cameraData.renderer.cameraColorTargetHandle;
            if (source == null || m_Material == null || m_Target == null)
                return;

            CommandBuffer cmd = CommandBufferPool.Get(k_ProfilerTag);

            // 1) 绑定 RT_Test 的颜色 + 深度/模板附件，并清空：模板清 0、深度复位、颜色清透明
            BindTarget(cmd);
            cmd.ClearRenderTarget(true, true, Color.clear);
            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();

            // 2) 写模板掩码：把掩码物体渲染进 RT_Test，它们的材质用
            //    Stencil { Ref n, Comp Always, Pass Replace } 把对应像素的模板值写成 n（例如 1）
            if (m_DrawMask && m_MaskLayerMask != 0)
            {
                DrawingSettings drawingSettings = CreateDrawingSettings(k_MaskShaderTag, ref renderingData, SortingCriteria.CommonOpaque);
                FilteringSettings filteringSettings = new FilteringSettings(RenderQueueRange.opaque, m_MaskLayerMask);
                context.DrawRenderers(renderingData.cullResults, ref drawingSettings, ref filteringSettings);
            }

            // 3) 丢掉掩码步骤写入的颜色（只清颜色，模板与深度保留），模板值为 0 的区域保持透明
            cmd.ClearRenderTarget(false, true, Color.clear);

            // 4) GrayEffect 全屏四边形：材质里 Stencil { Ref 1, Comp Equal } 使 frag 只作用于模板 = 1 的区域，
            //    采样相机画面后把结果写入 RT_Test
            BindTarget(cmd);
            Blitter.BlitTexture(cmd, source, GetViewportScale(source), m_Material, 0);

            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }

        // 同时绑定 RT_Test 的颜色与深度/模板附件：Stencil 测试必须有模板缓冲才会生效
        void BindTarget(CommandBuffer cmd)
        {
            if (m_Target.depthStencilFormat != 0)
                cmd.SetRenderTarget(m_Target.colorBuffer, m_Target.depthBuffer);
            else
                cmd.SetRenderTarget(new RenderTargetIdentifier(m_Target));
        }

        static Vector4 GetViewportScale(RTHandle source)
        {
            return source.useScaling
                ? new Vector4(source.rtHandleProperties.rtHandleScale.x, source.rtHandleProperties.rtHandleScale.y, 0f, 0f)
                : new Vector4(1f, 1f, 0f, 0f);
        }
    }
}
