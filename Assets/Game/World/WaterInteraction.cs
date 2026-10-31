using ForestSpriteCharacter;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace WeatherHouse
{
    /// <summary>Visual surface wakes; never changes the character's movement or buoyancy.</summary>
    [DisallowMultipleComponent, RequireComponent(typeof(Renderer)), DefaultExecutionOrder(200)]
    public abstract class WaterInteraction : MonoBehaviour
    {
        public Renderer waterRenderer;
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

        protected RTHandle state, nextState, normals;
        protected Material simulation;
        MaterialPropertyBlock properties;
        Renderer boundRenderer;
        ForestSpriteDemoController trackedPlayer;
        CharacterController controller;
        bool hasPrevious, previousWet;
        protected bool hasDisturbance;
        float quietDecay;
        Vector3 previousCenter;
        Vector2 previousPoint;
        int allocatedResolution;
        Vector4 allocatedRect;

        protected virtual void OnEnable()
        {
            if (!waterRenderer) waterRenderer = GetComponent<Renderer>();
            hasPrevious = false;
        }

        public Vector2 WorldToUV(Vector3 worldPosition) => new Vector2(
            (worldPosition.x - WorldRect.x) * WorldRect.z,
            (worldPosition.z - WorldRect.y) * WorldRect.w);

        /// <summary>Wet ground footprint only. Body/surface intersection is reported by IsInWater.</summary>
        public bool IsWet(Vector3 worldPosition) => TryGetWetSurface(worldPosition, out _);

        protected abstract bool TryGetWetSurface(Vector3 worldPosition, out float waterHeight);

        protected virtual bool TryGetFieldBounds(out Bounds bounds)
        {
            if (!waterRenderer) waterRenderer = GetComponent<Renderer>();
            bounds = waterRenderer ? waterRenderer.bounds : default;
            return waterRenderer;
        }

        static bool ContainsXZ(Bounds bounds, Vector3 point) =>
            point.x >= bounds.min.x && point.x <= bounds.max.x && point.z >= bounds.min.z && point.z <= bounds.max.z;

        protected bool IsInsideRenderer(Vector3 point) => waterRenderer && ContainsXZ(waterRenderer.bounds, point);

        protected virtual void LateUpdate()
        {
            if (Application.isPlaying) Advance(Time.deltaTime);
        }

        // Also exercised in an isolated fixture, without moving a character in the user's scene.
        public void Advance(float elapsed)
        {
            LastStampCount = 0;
            if (!(elapsed > 0) || float.IsInfinity(elapsed)) { Bind(); return; }
            if (!TryGetFieldBounds(out Bounds bounds)) { IsInWater = false; ClearBinding(); return; }
            WorldRect = FieldRect(bounds);
            Vector4 wake = Vector4.zero, direction = Vector4.zero, ring = Vector4.zero;
            ReadMovement(elapsed, ref wake, ref direction, ref ring);
            if (LastStampCount > 0) { hasDisturbance = true; quietDecay = 0; }
            else if (hasDisturbance) quietDecay += Mathf.Clamp(decay, .2f, 4) * elapsed;
            if (!hasDisturbance) { Bind(); return; }
            // Ten damping time constants precede a smooth two-constant tail; never freeze visible ripples.
            if (quietDecay >= 12)
            {
                ClearBuffers(); hasDisturbance = false; quietDecay = 0; Bind(); return;
            }
            if (!EnsureResources()) { hasDisturbance = false; return; }

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
            bool wet = TryGetWetSurface(body.center, out float waterY) &&
                body.min.y <= waterY + .03f && body.max.y >= waterY - .03f;
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

        protected bool EnsureResources()
        {
            if (!TryGetFieldBounds(out Bounds bounds)) { IsInWater = false; ClearBinding(); return false; }
            if (!simulationShader) simulationShader = Shader.Find("Hidden/WeatherHouse/WaterInteractionNormals");
            if (!simulationShader || !simulationShader.isSupported) { ClearBinding(); return false; }
            if (!simulation || simulation.shader != simulationShader)
            {
                if (simulation) CoreUtils.Destroy(simulation);
                simulation = CoreUtils.CreateEngineMaterial(simulationShader);
            }
            Vector4 rect = FieldRect(bounds);
            int size = Mathf.Clamp(Mathf.ClosestPowerOfTwo(resolution), 64, 512);
            if (state != null && normals != null && allocatedResolution == size && allocatedRect == rect) return true;
            ReleaseTextures();
            WorldRect = allocatedRect = rect;
            allocatedResolution = size;
            state = RTHandles.Alloc(size, size, colorFormat: GraphicsFormat.R16G16_SFloat,
                filterMode: FilterMode.Point, wrapMode: TextureWrapMode.Clamp, name: "Water interaction height A");
            nextState = RTHandles.Alloc(size, size, colorFormat: GraphicsFormat.R16G16_SFloat,
                filterMode: FilterMode.Point, wrapMode: TextureWrapMode.Clamp, name: "Water interaction height B");
            normals = RTHandles.Alloc(size, size, colorFormat: GraphicsFormat.R16G16B16A16_SFloat,
                filterMode: FilterMode.Bilinear, wrapMode: TextureWrapMode.Clamp, name: "Water interaction normals");
            ClearBuffers();
            return true;
        }

        Vector4 FieldRect(Bounds bounds)
        {
            float padding = Mathf.Clamp(fieldPadding, 0, 3);
            return new Vector4(bounds.min.x - padding, bounds.min.z - padding,
                1f / Mathf.Max(.1f, bounds.size.x + padding * 2), 1f / Mathf.Max(.1f, bounds.size.z + padding * 2));
        }

        public void ClearField()
        {
            hasDisturbance = false; quietDecay = 0;
            hasPrevious = previousWet = IsInWater = false;
            LastStampCount = TotalStampCount = TeleportSkipCount = 0;
            // Explicit authoring resets still provide a readable neutral texture; idle runtime water allocates nothing.
            if (EnsureResources()) ClearBuffers();
            Bind();
        }

        void ClearBuffers()
        {
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
            properties.SetFloat(StrengthId, hasDisturbance ? Mathf.Clamp(strength, 0, 3) *
                (1 - Mathf.SmoothStep(0, 1, (quietDecay - 10) * .5f)) : 0);
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

        protected virtual void OnDisable()
        {
            ReleaseTextures();
            if (simulation) CoreUtils.Destroy(simulation);
            simulation = null;
            WorldRect = Vector4.zero;
            IsInWater = hasPrevious = hasDisturbance = false;
            quietDecay = 0;
            LastStampCount = 0;
        }
    }
}


