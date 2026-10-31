using UnityEngine;
using UnityEngine.Rendering;
using WeatherHousePrototype;

namespace WeatherHouse.StoneEngraving
{
    [DisallowMultipleComponent, RequireComponent(typeof(Renderer))]
    public sealed class StoneEngravingReadability : MonoBehaviour
    {
        static readonly int Readability = Shader.PropertyToID("_GameplayReadability");
        Renderer stone;
        MaterialPropertyBlock properties;
        GameplaySession session;

        void OnEnable()
        {
            stone = GetComponent<Renderer>();
            properties = new MaterialPropertyBlock();
            RenderPipelineManager.beginCameraRendering += BeforeCamera;
        }

        void BeforeCamera(ScriptableRenderContext context, Camera camera)
        {
            if (!session) session = FindObjectOfType<GameplaySession>();
            bool gameplay = session && session.isActiveAndEnabled && camera == session.PlayerViewCamera &&
                !(session.PhotoRig && session.PhotoRig.Active);
            SetReadability(gameplay ? 1f : 0f);
        }

        void SetReadability(float value)
        {
            if (!stone) return;
            stone.GetPropertyBlock(properties);
            properties.SetFloat(Readability, value);
            stone.SetPropertyBlock(properties);
        }

        void OnDisable()
        {
            RenderPipelineManager.beginCameraRendering -= BeforeCamera;
            SetReadability(0f);
        }
    }
}
