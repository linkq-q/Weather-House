using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace WeatherHouse.Vegetation
{
    [ExecuteAlways, RequireComponent(typeof(Renderer))]
    public sealed class PainterlyWaterReflection : MonoBehaviour
    {
        [Range(256, 1536)] public int resolution = 1024;
        [Range(0.25f, 1f)] public float resolutionScale = 0.5f;
        [Min(1f)] public float maxReflectionDistance = 120f;
        [Range(1f, 60f)] public float updateRate = 15f;
        [Min(0.001f)] public float clipOffset = 0.04f;
        public LayerMask reflectionLayers = ~(1 << 4);

        static bool renderingReflection;
        Camera reflectionCamera;
        RenderTexture reflectionTexture;
        Renderer waterRenderer;
        MaterialPropertyBlock properties;
        Camera capturedSource;
        Vector3 capturedPosition;
        Quaternion capturedRotation;
        Matrix4x4 capturedProjection, capturedViewProjection;
        double nextCaptureTime;
        bool hasCapture;
        readonly Plane[] cameraFrustum = new Plane[6];
        static readonly int ReflectionTextureId = Shader.PropertyToID("_PlanarReflectionTex");
        static readonly int HasReflectionId = Shader.PropertyToID("_HasPlanarReflection");
        static readonly int ReflectionMatrixId = Shader.PropertyToID("_PlanarReflectionVP");
        static readonly int HasSceneColorId = Shader.PropertyToID("_HasSceneColor");

        void OnEnable()
        {
            waterRenderer = GetComponent<Renderer>();
            properties = new MaterialPropertyBlock();
            RenderPipelineManager.beginCameraRendering += RenderReflection;
        }

        void OnDisable()
        {
            RenderPipelineManager.beginCameraRendering -= RenderReflection;
            if (waterRenderer && properties != null)
            {
                waterRenderer.GetPropertyBlock(properties);
                properties.SetFloat(HasReflectionId, 0);
                properties.SetFloat(HasSceneColorId, 0);
                properties.SetTexture(ReflectionTextureId, Texture2D.blackTexture);
                waterRenderer.SetPropertyBlock(properties);
            }
            if (reflectionTexture) { reflectionTexture.Release(); Dispose(reflectionTexture); }
            if (reflectionCamera) Dispose(reflectionCamera.gameObject);
            reflectionTexture = null;
            reflectionCamera = null;
            capturedSource = null;
            hasCapture = false;
        }

        void RenderReflection(ScriptableRenderContext context, Camera source)
        {
            if (renderingReflection || source == reflectionCamera || !isActiveAndEnabled || !waterRenderer || !waterRenderer.enabled) return;
            if (source.cameraType != CameraType.Game && source.cameraType != CameraType.SceneView) return;
            if ((source.cullingMask & (1 << gameObject.layer)) == 0) return;
            GeometryUtility.CalculateFrustumPlanes(source, cameraFrustum);
            if (!GeometryUtility.TestPlanesAABB(cameraFrustum, waterRenderer.bounds)) return;

            // URP 14 initializes CameraData after this callback. Request the textures only on
            // game cameras rendering this pond; the pipeline asset and reflection camera stay unchanged.
            bool hasSceneColor;
            if (source.cameraType == CameraType.Game)
            {
                bool hadCameraData = source.TryGetComponent<UniversalAdditionalCameraData>(out _);
                var sourceData = source.GetUniversalAdditionalCameraData();
                sourceData.requiresDepthOption = CameraOverrideOption.On;
                sourceData.requiresColorOption = CameraOverrideOption.On;
                // URP caches this component before BeginCameraRendering; newly added data takes one frame.
                hasSceneColor = hadCameraData && sourceData.renderType == CameraRenderType.Base;
            }
            else
            {
                // SceneView ignores additional camera data and follows the pipeline's opaque option.
                var pipeline = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
                hasSceneColor = pipeline && pipeline.supportsCameraOpaqueTexture;
            }

            if (!reflectionCamera)
            {
                var go = new GameObject("Painterly Water Reflection Camera", typeof(Camera), typeof(UniversalAdditionalCameraData));
                go.hideFlags = HideFlags.HideAndDontSave;
                reflectionCamera = go.GetComponent<Camera>();
                reflectionCamera.enabled = false;
            }
            float aspect = Mathf.Max(0.1f, source.aspect);
            int longest = Mathf.RoundToInt(Mathf.Clamp(resolution, 256, 1536) * Mathf.Clamp(resolutionScale, 0.25f, 1f));
            int width = Mathf.Max(1, aspect >= 1 ? longest : Mathf.RoundToInt(longest * aspect));
            int height = Mathf.Max(1, aspect >= 1 ? Mathf.RoundToInt(longest / aspect) : longest);
            if (!reflectionTexture || !reflectionTexture.IsCreated() || reflectionTexture.width != width || reflectionTexture.height != height)
            {
                hasCapture = false;
                if (reflectionTexture) { reflectionTexture.Release(); Dispose(reflectionTexture); }
                reflectionTexture = new RenderTexture(width, height, 24, RenderTextureFormat.ARGBHalf)
                {
                    name = "Painterly Pond Reflection",
                    hideFlags = HideFlags.HideAndDontSave,
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp,
                    useMipMap = false,
                    autoGenerateMips = false
                };
                reflectionTexture.Create();
            }

            double now = Time.realtimeSinceStartupAsDouble;
            bool throttled = Application.isPlaying && source.cameraType == CameraType.Game && source == Camera.main;
            bool cameraChanged = capturedSource != source || (source.transform.position - capturedPosition).sqrMagnitude > 1f ||
                Quaternion.Angle(source.transform.rotation, capturedRotation) > 8f || source.projectionMatrix != capturedProjection;
            // One texture is shared by views: switching cameras must capture before binding it.
            if (!hasCapture || !throttled || cameraChanged || now >= nextCaptureTime)
                CaptureReflection(context, source, now);

            waterRenderer.GetPropertyBlock(properties);
            properties.SetTexture(ReflectionTextureId, reflectionTexture);
            properties.SetFloat(HasReflectionId, hasCapture ? 1 : 0);
            properties.SetFloat(HasSceneColorId, hasSceneColor ? 1 : 0);
            properties.SetMatrix(ReflectionMatrixId, capturedViewProjection);
            waterRenderer.SetPropertyBlock(properties);
        }

        void CaptureReflection(ScriptableRenderContext context, Camera source, double now)
        {
            hasCapture = false;
            reflectionCamera.CopyFrom(source);
            reflectionCamera.enabled = false;
            // URP 14 only reads UniversalAdditionalCameraData for Game/VR camera types.
            reflectionCamera.cameraType = CameraType.Game;
            reflectionCamera.targetTexture = reflectionTexture;
            reflectionCamera.rect = new Rect(0, 0, 1, 1);
            reflectionCamera.cullingMask = source.cullingMask & reflectionLayers.value & ~(1 << 4);
            reflectionCamera.useOcclusionCulling = false;
            reflectionCamera.allowMSAA = false;
            reflectionCamera.farClipPlane = Mathf.Min(source.farClipPlane, Mathf.Max(source.nearClipPlane + 0.01f, maxReflectionDistance));
            reflectionCamera.ResetProjectionMatrix();
            Matrix4x4 distanceProjection = reflectionCamera.projectionMatrix;
            var data = reflectionCamera.GetUniversalAdditionalCameraData();
            data.renderType = CameraRenderType.Base;
            data.renderPostProcessing = false;
            data.renderShadows = false;
            data.requiresColorOption = CameraOverrideOption.Off;
            data.requiresDepthOption = CameraOverrideOption.Off;

            Vector3 position = transform.position;
            Vector3 normal = transform.up;
            float planeDistance = -Vector3.Dot(normal, position);
            Matrix4x4 reflection = ReflectionMatrix(new Vector4(normal.x, normal.y, normal.z, planeDistance));
            Vector3 reflectedPosition = reflection.MultiplyPoint(source.transform.position);
            Vector3 reflectedForward = reflection.MultiplyVector(source.transform.forward);
            Vector3 reflectedUp = reflection.MultiplyVector(source.transform.up);
            reflectionCamera.transform.SetPositionAndRotation(reflectedPosition, Quaternion.LookRotation(reflectedForward, reflectedUp));
            reflectionCamera.worldToCameraMatrix = source.worldToCameraMatrix * reflection;
            Vector3 clipPosition = reflectionCamera.worldToCameraMatrix.MultiplyPoint(position + normal * clipOffset);
            Vector3 clipNormal = reflectionCamera.worldToCameraMatrix.MultiplyVector(normal).normalized;
            var clipPlane = new Vector4(clipNormal.x, clipNormal.y, clipNormal.z, -Vector3.Dot(clipPosition, clipNormal));
            reflectionCamera.projectionMatrix = reflectionCamera.CalculateObliqueMatrix(clipPlane);
            // Oblique clipping tilts the far plane; keep distance culling on the capped base frustum.
            reflectionCamera.cullingMatrix = distanceProjection * reflectionCamera.worldToCameraMatrix;

            bool oldInvertCulling = GL.invertCulling;
            bool oldFog = RenderSettings.fog;
            renderingReflection = true;
            try
            {
                GL.invertCulling = !oldInvertCulling;
                // Oblique projection changes clip.z, so depth fog would incorrectly wash out the reflection.
                RenderSettings.fog = false;
                // URP 14's standalone API renders inside the current render context without camera callbacks.
                UniversalRenderPipeline.RenderSingleCamera(context, reflectionCamera);
            }
            finally
            {
                GL.invertCulling = oldInvertCulling;
                RenderSettings.fog = oldFog;
                renderingReflection = false;
            }
            Matrix4x4 textureBias = Matrix4x4.identity;
            textureBias.m00 = 0.5f;
            textureBias.m03 = 0.5f;
            textureBias.m11 = SystemInfo.graphicsUVStartsAtTop ? -0.5f : 0.5f;
            textureBias.m13 = 0.5f;
            capturedViewProjection = textureBias * GL.GetGPUProjectionMatrix(reflectionCamera.projectionMatrix, true)
                * reflectionCamera.worldToCameraMatrix;
            capturedSource = source;
            capturedPosition = source.transform.position;
            capturedRotation = source.transform.rotation;
            capturedProjection = source.projectionMatrix;
            nextCaptureTime = now + 1.0 / Mathf.Max(1f, updateRate);
            hasCapture = true;
        }

        static Matrix4x4 ReflectionMatrix(Vector4 plane)
        {
            Matrix4x4 m = Matrix4x4.identity;
            m.m00 = 1 - 2 * plane.x * plane.x;
            m.m01 = -2 * plane.x * plane.y;
            m.m02 = -2 * plane.x * plane.z;
            m.m03 = -2 * plane.w * plane.x;
            m.m10 = -2 * plane.y * plane.x;
            m.m11 = 1 - 2 * plane.y * plane.y;
            m.m12 = -2 * plane.y * plane.z;
            m.m13 = -2 * plane.w * plane.y;
            m.m20 = -2 * plane.z * plane.x;
            m.m21 = -2 * plane.z * plane.y;
            m.m22 = 1 - 2 * plane.z * plane.z;
            m.m23 = -2 * plane.w * plane.z;
            return m;
        }

        static void Dispose(Object value)
        {
            if (Application.isPlaying) Destroy(value); else DestroyImmediate(value);
        }
    }
}
