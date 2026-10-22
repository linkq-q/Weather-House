using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace WeatherHouse.MaterialLab
{
    [DefaultExecutionOrder(-500)]
    public sealed class MaterialLabPipelineScope : MonoBehaviour
    {
        public UniversalRenderPipelineAsset pipeline;
        RenderPipelineAsset previous;
        bool applied;

        void OnEnable()
        {
            if (!Application.isPlaying || !pipeline) return;
            previous = QualitySettings.renderPipeline;
            QualitySettings.renderPipeline = pipeline;
            applied = true;
        }

        void OnDisable()
        {
            if (!applied) return;
            if (QualitySettings.renderPipeline == pipeline) QualitySettings.renderPipeline = previous;
            applied = false;
        }
    }
}
