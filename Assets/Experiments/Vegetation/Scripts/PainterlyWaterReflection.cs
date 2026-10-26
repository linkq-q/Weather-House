using System.Collections.Generic;
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
        [Min(0.001f)] public float clipOffset = 0.04f;
        public LayerMask reflectionLayers = ~(1 << 4);
        [Tooltip("URP renderer slot for reflections; -1 uses the pipeline default.")]
        public int reflectionRendererIndex = -1;

        static bool renderingReflection;
        Camera reflectionCamera;
        sealed class Capture
        {
            public RenderTexture texture;
            public Matrix4x4 viewProjection;
            public bool valid, used;
        }
        struct CaptureKey : System.IEquatable<CaptureKey>
        {
            public Vector4 plane;
            public int width, height, layers, renderer;
            public float far, clip;
            public bool Equals(CaptureKey other) => plane.Equals(other.plane) && width == other.width && height == other.height &&
                layers == other.layers && renderer == other.renderer && far.Equals(other.far) && clip.Equals(other.clip);
            public override bool Equals(object other) => other is CaptureKey key && Equals(key);
            public override int GetHashCode()
            {
                unchecked { return ((((((plane.GetHashCode() * 397 ^ width) * 397 ^ height) * 397 ^ layers) * 397 ^ renderer) * 397 ^ far.GetHashCode()) * 397 ^ clip.GetHashCode()); }
            }
        }
        static readonly List<PainterlyWaterReflection> surfaces = new List<PainterlyWaterReflection>();
        static readonly Dictionary<Camera, Dictionary<CaptureKey, Capture>> captures = new Dictionary<Camera, Dictionary<CaptureKey, Capture>>();
        static readonly List<Camera> expiredSources = new List<Camera>();
        static readonly List<CaptureKey> expiredGroups = new List<CaptureKey>();
        Renderer waterRenderer;
        MaterialPropertyBlock properties;
        readonly Plane[] cameraFrustum = new Plane[6];
        static readonly int ReflectionTextureId = Shader.PropertyToID("_PlanarReflectionTex");
        static readonly int HasReflectionId = Shader.PropertyToID("_HasPlanarReflection");
        static readonly int ReflectionMatrixId = Shader.PropertyToID("_PlanarReflectionVP");
        static readonly int HasSceneColorId = Shader.PropertyToID("_HasSceneColor");

        void OnEnable()
        {
            waterRenderer = GetComponent<Renderer>();
            properties = new MaterialPropertyBlock();
            if (surfaces.Contains(this)) return;
            if (surfaces.Count == 0) RenderPipelineManager.beginCameraRendering += RenderReflections;
            surfaces.Add(this);
        }

        void OnDisable()
        {
            surfaces.Remove(this);
            if (surfaces.Count == 0)
            {
                RenderPipelineManager.beginCameraRendering -= RenderReflections;
                foreach (var source in captures.Values) foreach (var capture in source.Values) Release(capture);
                captures.Clear(); expiredSources.Clear(); expiredGroups.Clear();
            }
            if (waterRenderer && properties != null)
            {
                waterRenderer.GetPropertyBlock(properties);
                properties.SetFloat(HasReflectionId, 0);
                properties.SetFloat(HasSceneColorId, 0);
                properties.SetTexture(ReflectionTextureId, Texture2D.blackTexture);
                waterRenderer.SetPropertyBlock(properties);
            }
            if (reflectionCamera) Dispose(reflectionCamera.gameObject);
            reflectionCamera = null;
        }

        static void RenderReflections(ScriptableRenderContext context, Camera source)
        {
            if (renderingReflection || !source || (source.cameraType != CameraType.Game && source.cameraType != CameraType.SceneView)) return;
            expiredSources.Clear();
            foreach (var entry in captures)
                if (!entry.Key)
                {
                    foreach (var capture in entry.Value.Values) Release(capture);
                    expiredSources.Add(entry.Key);
                }
            foreach (var expired in expiredSources) captures.Remove(expired);
            if (!captures.TryGetValue(source, out var groups))
            {
                groups = new Dictionary<CaptureKey, Capture>(); captures.Add(source, groups);
            }
            // Freshness belongs to this callback, not Time.frameCount: a camera can render twice in one frame.
            foreach (var capture in groups.Values) { capture.valid = false; capture.used = false; }
            for (int i = 0; i < surfaces.Count; i++)
            {
                var surface = surfaces[i];
                if (!surface || !surface.isActiveAndEnabled) continue;
                CaptureKey key = surface.Key(source);
                // Retain off-screen textures to avoid reallocating on each visibility transition.
                if (groups.TryGetValue(key, out var existing)) existing.used = true;
                surface.RenderReflection(context, source, groups, key);
            }
            expiredGroups.Clear();
            foreach (var entry in groups) if (!entry.Value.used) expiredGroups.Add(entry.Key);
            foreach (var expired in expiredGroups) { Release(groups[expired]); groups.Remove(expired); }
        }

        CaptureKey Key(Camera source)
        {
            float aspect = Mathf.Max(.1f, source.aspect);
            int longest = Mathf.RoundToInt(Mathf.Clamp(resolution, 256, 1536) * Mathf.Clamp(resolutionScale, .25f, 1));
            Vector3 normal = transform.up;
            return new CaptureKey {
                plane = new Vector4(normal.x, normal.y, normal.z, -Vector3.Dot(normal, transform.position)),
                width = Mathf.Max(1, aspect >= 1 ? longest : Mathf.RoundToInt(longest * aspect)),
                height = Mathf.Max(1, aspect >= 1 ? Mathf.RoundToInt(longest / aspect) : longest),
                layers = source.cullingMask & reflectionLayers.value & ~(1 << 4), renderer = reflectionRendererIndex,
                far = Mathf.Min(source.farClipPlane, Mathf.Max(source.nearClipPlane + .01f, maxReflectionDistance)), clip = clipOffset
            };
        }

        void RenderReflection(ScriptableRenderContext context, Camera source, Dictionary<CaptureKey, Capture> groups, CaptureKey key)
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

            Capture capture = GetCapture(groups, key);
            // One fresh capture per exact plane/settings group, including animated reflected objects.
            if (!capture.valid)
            {
                if (!reflectionCamera)
                {
                    var go = new GameObject("Painterly Water Reflection Camera", typeof(Camera), typeof(UniversalAdditionalCameraData));
                    go.hideFlags = HideFlags.HideAndDontSave;
                    reflectionCamera = go.GetComponent<Camera>();
                    reflectionCamera.enabled = false;
                }
                CaptureReflection(context, source, capture);
            }

            waterRenderer.GetPropertyBlock(properties);
            properties.SetTexture(ReflectionTextureId, capture.texture);
            properties.SetFloat(HasReflectionId, capture.valid ? 1 : 0);
            properties.SetFloat(HasSceneColorId, hasSceneColor ? 1 : 0);
            properties.SetMatrix(ReflectionMatrixId, capture.viewProjection);
            waterRenderer.SetPropertyBlock(properties);
        }

        static Capture GetCapture(Dictionary<CaptureKey, Capture> groups, CaptureKey key)
        {
            if (!groups.TryGetValue(key, out Capture capture))
            {
                capture = new Capture(); groups.Add(key, capture);
            }
            capture.used = true;
            int width = key.width, height = key.height;
            if (!capture.texture || !capture.texture.IsCreated() || capture.texture.width != width || capture.texture.height != height)
            {
                capture.valid = false;
                Release(capture);
                capture.texture = new RenderTexture(width, height, 24, RenderTextureFormat.ARGBHalf)
                {
                    name = "Painterly Pond Reflection",
                    hideFlags = HideFlags.HideAndDontSave,
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp,
                    useMipMap = false,
                    autoGenerateMips = false
                };
                capture.texture.Create();
            }

            return capture;
        }

        static void Release(Capture capture)
        {
            capture.valid = false;
            if (!capture.texture) return;
            capture.texture.Release();
            Dispose(capture.texture);
            capture.texture = null;
        }

        void CaptureReflection(ScriptableRenderContext context, Camera source, Capture capture)
        {
            capture.valid = false;
            reflectionCamera.CopyFrom(source);
            reflectionCamera.enabled = false;
            // URP 14 only reads UniversalAdditionalCameraData for Game/VR camera types.
            reflectionCamera.cameraType = CameraType.Game;
            reflectionCamera.targetTexture = capture.texture;
            reflectionCamera.rect = new Rect(0, 0, 1, 1);
            reflectionCamera.cullingMask = source.cullingMask & reflectionLayers.value & ~(1 << 4);
            reflectionCamera.useOcclusionCulling = false;
            reflectionCamera.allowMSAA = false;
            reflectionCamera.farClipPlane = Mathf.Min(source.farClipPlane, Mathf.Max(source.nearClipPlane + 0.01f, maxReflectionDistance));
            reflectionCamera.ResetProjectionMatrix();
            Matrix4x4 distanceProjection = reflectionCamera.projectionMatrix;
            var data = reflectionCamera.GetUniversalAdditionalCameraData();
            data.SetRenderer(reflectionRendererIndex);
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
                reflectionCamera.targetTexture = null;
                GL.invertCulling = oldInvertCulling;
                RenderSettings.fog = oldFog;
                renderingReflection = false;
            }
            Matrix4x4 textureBias = Matrix4x4.identity;
            textureBias.m00 = 0.5f;
            textureBias.m03 = 0.5f;
            textureBias.m11 = SystemInfo.graphicsUVStartsAtTop ? -0.5f : 0.5f;
            textureBias.m13 = 0.5f;
            capture.viewProjection = textureBias * GL.GetGPUProjectionMatrix(reflectionCamera.projectionMatrix, true)
                * reflectionCamera.worldToCameraMatrix;
            capture.valid = true;
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
