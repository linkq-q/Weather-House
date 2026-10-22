using ForestSpriteCharacter;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace WeatherHouse.Vegetation
{
    /// <summary>Visual surface wakes; never changes the character's movement or buoyancy.</summary>
    [DisallowMultipleComponent, RequireComponent(typeof(Renderer)), DefaultExecutionOrder(200)]
    public sealed class PainterlyWaterInteraction : MonoBehaviour
    {
        public Renderer waterRenderer;
        public GrassMeadow meadow;
        public ForestSpriteDemoController player;
        public Shader simulationShader;
        [Range(64, 512)] public int resolution = 256;
        [Range(0, 3)] public float strength = 1;
        [Range(.1f, 1.5f)] public float radius = .38f;
        [Range(.2f, 4)] public float decay = 1.25f;
        [Range(.2f, 3)] public float waveSpeed = 1.15f;
        [Range(.25f, 10)] public float teleportDistance = 1.5f;
        [Range(0, 3)] public float fieldPadding = .4f;

        public RenderTexture CurrentTexture => normals == null ? null : normals.rt;
        public Vector4 WorldRect { get; private set; }
        public bool IsInWater { get; private set; }
        public int LastStampCount { get; private set; }
        public int TotalStampCount { get; private set; }
        public int TeleportSkipCount { get; private set; }

        static readonly int NormalTexId = Shader.PropertyToID("_InteractionNormalTex");
        static readonly int WorldRectId = Shader.PropertyToID("_InteractionWorldRect");
        static readonly int StrengthId = Shader.PropertyToID("_InteractionStrength");
        static readonly int FieldRectId = Shader.PropertyToID("_FieldRect");
        static readonly int CellId = Shader.PropertyToID("_Cell");
        static readonly int StepId = Shader.PropertyToID("_Step");
        static readonly int WakeId = Shader.PropertyToID("_Wake");
        static readonly int WakeDirectionId = Shader.PropertyToID("_WakeDirection");
        static readonly int RingId = Shader.PropertyToID("_Ring");

        RTHandle state, nextState, normals;
        Material simulation;
        MaterialPropertyBlock properties;
        Renderer boundRenderer;
        ForestSpriteDemoController trackedPlayer;
        CharacterController controller;
        bool hasPrevious, previousWet;
        Vector3 previousCenter;
        Vector2 previousPoint;
        int allocatedResolution;

        void OnEnable()
        {
            if (!waterRenderer) waterRenderer = GetComponent<Renderer>();
            hasPrevious = false;
        }

        public Vector2 WorldToUV(Vector3 worldPosition) => new Vector2(
            (worldPosition.x - WorldRect.x) * WorldRect.z,
            (worldPosition.z - WorldRect.y) * WorldRect.w);

        /// <summary>Wet ground footprint only. Body/surface intersection is reported by IsInWater.</summary>
        public bool IsWet(Vector3 worldPosition)
        {
            if (!meadow || !waterRenderer) return false;
            Bounds bounds = waterRenderer.bounds;
            if (worldPosition.x < bounds.min.x || worldPosition.x > bounds.max.x ||
                worldPosition.z < bounds.min.z || worldPosition.z > bounds.max.z) return false;
            Vector3 local = meadow.transform.InverseTransformPoint(new Vector3(worldPosition.x, bounds.center.y, worldPosition.z));
            if (meadow.PondDistance(new Vector2(local.x, local.z)) > 1.1f) return false;
            float groundY = meadow.transform.TransformPoint(new Vector3(local.x, meadow.GetGroundHeight(local.x, local.z), local.z)).y;
            return groundY < bounds.center.y - .015f;
        }

        void LateUpdate()
        {
            LastStampCount = 0;
            if (!Application.isPlaying || !EnsureResources()) return;
            float elapsed = Time.deltaTime;
            if (!(elapsed > 0) || float.IsInfinity(elapsed)) { Bind(); return; }

            Vector4 wake = Vector4.zero, direction = Vector4.zero, ring = Vector4.zero;
            ReadMovement(elapsed, ref wake, ref direction, ref ring);

            float cellX = 1f / (WorldRect.z * allocatedResolution);
            float cellZ = 1f / (WorldRect.w * allocatedResolution);
            float speed = Mathf.Clamp(waveSpeed, .2f, 3);
            // 2-D explicit wave equation: c*dt/minCell < 1/sqrt(2).
            float stableStep = .45f * Mathf.Min(cellX, cellZ) / speed;
            float simulationTime = Mathf.Min(elapsed, .1f, stableStep * 8);
            int substeps = Mathf.Clamp(Mathf.CeilToInt(simulationTime / stableStep), 1, 8);
            simulation.SetVector(FieldRectId, WorldRect);
            simulation.SetVector(CellId, new Vector4(1f / allocatedResolution, 1f / allocatedResolution, cellX, cellZ));
            simulation.SetVector(StepId, new Vector4(simulationTime / substeps,
                Mathf.Exp(-Mathf.Clamp(decay, .2f, 4) * elapsed / substeps), speed * speed, 1f / substeps));
            simulation.SetVector(WakeId, wake);
            simulation.SetVector(WakeDirectionId, direction);
            simulation.SetVector(RingId, ring);

            CommandBuffer cmd = CommandBufferPool.Get("Painterly water interaction");
            try
            {
                for (int i = 0; i < substeps; i++)
                {
                    Blitter.BlitCameraTexture(cmd, state, nextState, simulation, 0);
                    RTHandle swap = state; state = nextState; nextState = swap;
                }
                Blitter.BlitCameraTexture(cmd, state, normals, simulation, 1);
                Graphics.ExecuteCommandBuffer(cmd);
            }
            finally { CommandBufferPool.Release(cmd); }
            Bind();
        }

        void ReadMovement(float elapsed, ref Vector4 wake, ref Vector4 direction, ref Vector4 ring)
        {
            if (trackedPlayer != player)
            {
                trackedPlayer = player;
                controller = player ? player.GetComponent<CharacterController>() : null;
                hasPrevious = false;
            }
            if (!player || !player.isActiveAndEnabled || !controller || !controller.enabled || !player.gameObject.activeInHierarchy)
            {
                hasPrevious = false;
                IsInWater = false;
                return;
            }
            Bounds body = controller.bounds;
            Vector2 point = new Vector2(body.center.x, body.center.z);
            float waterY = waterRenderer.bounds.center.y;
            bool wet = IsWet(body.center) && body.min.y <= waterY + .03f && body.max.y >= waterY - .03f;
            IsInWater = wet;
            if (hasPrevious)
            {
                Vector2 displacement = point - previousPoint;
                float distance = displacement.magnitude;
                if (Vector3.Distance(body.center, previousCenter) > Mathf.Max(.25f, teleportDistance))
                    TeleportSkipCount++;
                else
                {
                    float stampRadius = Mathf.Clamp(radius, .1f, 1.5f);
                    if (wet && distance / elapsed > .04f)
                    {
                        Vector2 heading = displacement / Mathf.Max(distance, .00001f);
                        // Total injection follows travelled distance, independent of frame/substep rate.
                        wake = new Vector4(point.x, point.y, stampRadius, .28f * distance);
                        direction = new Vector4(heading.x, heading.y, 0, 0);
                        LastStampCount++;
                    }
                    if (wet != previousWet)
                    {
                        Vector2 at = wet ? point : previousPoint;
                        ring = new Vector4(at.x, at.y, stampRadius, .014f);
                        LastStampCount++;
                    }
                }
            }
            // First observation and teleports establish a new origin without a streak across the pond.
            hasPrevious = true;
            previousWet = wet;
            previousPoint = point;
            previousCenter = body.center;
            TotalStampCount += LastStampCount;
        }

        bool EnsureResources()
        {
            if (!waterRenderer) waterRenderer = GetComponent<Renderer>();
            if (!waterRenderer || !meadow) { IsInWater = false; ClearBinding(); return false; }
            if (!simulationShader) simulationShader = Shader.Find("Hidden/WeatherHouse/WaterInteractionNormals");
            if (!simulationShader || !simulationShader.isSupported) { ClearBinding(); return false; }
            if (!simulation || simulation.shader != simulationShader)
            {
                if (simulation) CoreUtils.Destroy(simulation);
                simulation = CoreUtils.CreateEngineMaterial(simulationShader);
            }
            Bounds bounds = waterRenderer.bounds;
            float padding = Mathf.Clamp(fieldPadding, 0, 3);
            float sizeX = Mathf.Max(.1f, bounds.size.x + padding * 2), sizeZ = Mathf.Max(.1f, bounds.size.z + padding * 2);
            Vector4 rect = new Vector4(bounds.min.x - padding, bounds.min.z - padding, 1f / sizeX, 1f / sizeZ);
            int size = Mathf.Clamp(Mathf.ClosestPowerOfTwo(resolution), 64, 512);
            if (state != null && normals != null && allocatedResolution == size && WorldRect == rect) return true;
            ReleaseTextures();
            WorldRect = rect;
            allocatedResolution = size;
            state = RTHandles.Alloc(size, size, colorFormat: GraphicsFormat.R16G16_SFloat,
                filterMode: FilterMode.Point, wrapMode: TextureWrapMode.Clamp, name: "Water interaction height A");
            nextState = RTHandles.Alloc(size, size, colorFormat: GraphicsFormat.R16G16_SFloat,
                filterMode: FilterMode.Point, wrapMode: TextureWrapMode.Clamp, name: "Water interaction height B");
            normals = RTHandles.Alloc(size, size, colorFormat: GraphicsFormat.R16G16B16A16_SFloat,
                filterMode: FilterMode.Bilinear, wrapMode: TextureWrapMode.Clamp, name: "Water interaction normals");
            ClearField();
            return true;
        }

        public void ClearField()
        {
            hasPrevious = false;
            previousWet = false;
            IsInWater = false;
            LastStampCount = TotalStampCount = TeleportSkipCount = 0;
            if (state == null || nextState == null || normals == null) return;
            CommandBuffer cmd = CommandBufferPool.Get("Clear water interaction");
            try
            {
                CoreUtils.SetRenderTarget(cmd, state, ClearFlag.Color, Color.clear);
                CoreUtils.SetRenderTarget(cmd, nextState, ClearFlag.Color, Color.clear);
                CoreUtils.SetRenderTarget(cmd, normals, ClearFlag.Color, new Color(.5f, .5f, 1, 1));
                Graphics.ExecuteCommandBuffer(cmd);
            }
            finally { CommandBufferPool.Release(cmd); }
            Bind();
        }

        void Bind()
        {
            if (boundRenderer != waterRenderer) { ClearBinding(); boundRenderer = waterRenderer; }
            if (!boundRenderer || normals == null) return;
            if (properties == null) properties = new MaterialPropertyBlock();
            // Reflection uses the same block; preserve every field owned by the other component.
            boundRenderer.GetPropertyBlock(properties);
            properties.SetTexture(NormalTexId, normals.rt);
            properties.SetVector(WorldRectId, WorldRect);
            properties.SetFloat(StrengthId, Mathf.Clamp(strength, 0, 3));
            boundRenderer.SetPropertyBlock(properties);
        }

        void ClearBinding()
        {
            if (!boundRenderer) return;
            if (properties == null) properties = new MaterialPropertyBlock();
            boundRenderer.GetPropertyBlock(properties);
            properties.SetTexture(NormalTexId, Texture2D.grayTexture);
            properties.SetVector(WorldRectId, Vector4.zero);
            properties.SetFloat(StrengthId, 0);
            boundRenderer.SetPropertyBlock(properties);
            boundRenderer = null;
        }

        void ReleaseTextures()
        {
            ClearBinding();
            state?.Release(); nextState?.Release(); normals?.Release();
            state = nextState = normals = null;
            allocatedResolution = 0;
        }

        void OnDisable()
        {
            ReleaseTextures();
            if (simulation) CoreUtils.Destroy(simulation);
            simulation = null;
            WorldRect = Vector4.zero;
            IsInWater = hasPrevious = false;
            LastStampCount = 0;
        }
    }
}
