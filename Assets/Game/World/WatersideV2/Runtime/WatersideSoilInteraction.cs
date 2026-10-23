using ForestSpriteCharacter;
using UnityEngine;

namespace WeatherHouse.WatersideV2
{
    // A small rolling, runtime-only compression field. Terrain layers remain the authority for soil coverage.
    [DisallowMultipleComponent, DefaultExecutionOrder(210)]
    public sealed class WatersideSoilInteraction : MonoBehaviour
    {
        public Terrain terrain;
        public ForestSpriteDemoController player;
        [Range(0, 3)] public int soilLayer = 1;
        [Range(.1f, 1)] public float minimumSoilWeight = .3f;
        [Range(128, 512)] public int resolution = 512;
        [Range(6, 20)] public float fieldSize = 10;
        [Range(.2f, .8f)] public float stepSpacing = .36f;
        [Range(2, 20)] public float fadeSeconds = 8;
        [Range(.001f, .025f)] public float impressionDepth = .008f;
        [Range(0, .3f)] public float darkening = .16f;
        public Texture2D CurrentTexture => field;
        public int StampCount { get; private set; }
        public int TeleportSkipCount { get; private set; }

        static readonly int TextureId = Shader.PropertyToID("_SoilFootprints");
        static readonly int RectId = Shader.PropertyToID("_SoilFieldRect");
        static readonly int TerrainRectId = Shader.PropertyToID("_SoilTerrainRect");
        static readonly int LayerId = Shader.PropertyToID("_SoilLayerWeights");
        static readonly int StrengthId = Shader.PropertyToID("_SoilInteractionStrength");
        static readonly int DepthId = Shader.PropertyToID("_SoilImpressionDepth");
        static readonly int DarkeningId = Shader.PropertyToID("_SoilDarkening");
        Texture2D field;
        byte[] marks, shifted;
        float[,,] coverage;
        Vector2 origin, previous;
        float carriedDistance, updateAge;
        int size, side;
        bool observed, dirty;
        CharacterController body;
        MaterialPropertyBlock properties;

        void Start()
        {
            if (!player) player = FindObjectOfType<ForestSpriteDemoController>();
            if (player) body = player.GetComponent<CharacterController>();
        }

        void LateUpdate()
        {
            if (!Application.isPlaying || !player || !player.isActiveAndEnabled || !terrain || !terrain.terrainData) return;
            if (!body) body = player.GetComponent<CharacterController>();
            if (!body || !body.enabled) return;
            Advance(player.transform.position, body.bounds.min.y, player.IsGrounded, Time.deltaTime);
        }

        // Also usable by a controlled Play-mode check; no Animator events or scripted player movement are required.
        public void Advance(Vector3 position, float soleHeight, bool grounded, float elapsed)
        {
            if (!terrain || !terrain.terrainData || !(elapsed > 0) || float.IsInfinity(elapsed) || float.IsNaN(elapsed)) return;
            if (!field) Initialize(position);
            var point = new Vector2(position.x, position.z);
            Recenter(point);
            updateAge += elapsed;
            if (updateAge >= .1f)
            {
                int fade = Mathf.Max(1, Mathf.RoundToInt(255 * updateAge / Mathf.Max(2, fadeSeconds)));
                for (int i = 0; i < marks.Length; i++)
                    if (marks[i] > 0) { marks[i] = (byte)Mathf.Max(0, marks[i] - fade); dirty = true; }
                updateAge = 0;
            }
            if (observed)
            {
                Vector2 delta = point - previous; float distance = delta.magnitude;
                if (distance > 1.5f) { TeleportSkipCount++; carriedDistance = 0; }
                else if (grounded && distance / elapsed > .18f && Mathf.Abs(soleHeight - Ground(position)) < .16f)
                {
                    carriedDistance += distance;
                    if (carriedDistance >= Mathf.Max(.2f, stepSpacing))
                    {
                        carriedDistance %= Mathf.Max(.2f, stepSpacing);
                        Vector2 forward = delta / Mathf.Max(.00001f, distance);
                        Vector2 foot = point + new Vector2(forward.y, -forward.x) * (side == 0 ? -.075f : .075f);
                        side ^= 1;
                        Vector3 at = new Vector3(foot.x, Ground(position), foot.y);
                        if (SoilWeight(at) >= minimumSoilWeight)
                        { Stamp(foot, forward); StampCount++; }
                    }
                }
                else carriedDistance = 0;
            }
            previous = point; observed = true;
            if (dirty) { field.SetPixelData(marks, 0); field.Apply(false, false); dirty = false; }
            Bind();
        }

        void Initialize(Vector3 position)
        {
            size = Mathf.Clamp(resolution, 128, 512);
            field = new Texture2D(size, size, TextureFormat.R8, false, true)
            { name = "Runtime soil impressions", hideFlags = HideFlags.DontSave, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            marks = new byte[size * size]; shifted = new byte[marks.Length];
            coverage = terrain.terrainData.GetAlphamaps(0, 0, terrain.terrainData.alphamapWidth, terrain.terrainData.alphamapHeight);
            float pixel = Mathf.Max(6, fieldSize) / size;
            origin = new Vector2(Mathf.Floor(position.x / pixel) - size / 2, Mathf.Floor(position.z / pixel) - size / 2) * pixel;
            observed = false; dirty = true; StampCount = TeleportSkipCount = 0;
            properties = new MaterialPropertyBlock();
        }

        void Recenter(Vector2 point)
        {
            float extent = Mathf.Max(6, fieldSize), pixel = extent / size;
            if ((point - (origin + Vector2.one * extent * .5f)).sqrMagnitude < extent * extent * .0225f) return;
            var next = new Vector2(Mathf.Floor(point.x / pixel) - size / 2, Mathf.Floor(point.y / pixel) - size / 2) * pixel;
            int dx = Mathf.RoundToInt((next.x - origin.x) / pixel), dz = Mathf.RoundToInt((next.y - origin.y) / pixel);
            for (int z = 0; z < size; z++)
                for (int x = 0; x < size; x++)
                    shifted[z * size + x] = x + dx >= 0 && x + dx < size && z + dz >= 0 && z + dz < size ? marks[(z + dz) * size + x + dx] : (byte)0;
            var swap = marks; marks = shifted; shifted = swap; origin = next; dirty = true;
        }

        void Stamp(Vector2 point, Vector2 forward)
        {
            float pixel = Mathf.Max(6, fieldSize) / size;
            int cx = Mathf.RoundToInt((point.x - origin.x) / pixel), cz = Mathf.RoundToInt((point.y - origin.y) / pixel);
            int range = Mathf.CeilToInt(.21f / pixel);
            Vector2 right = new Vector2(forward.y, -forward.x);
            for (int z = Mathf.Max(0, cz - range); z <= Mathf.Min(size - 1, cz + range); z++)
                for (int x = Mathf.Max(0, cx - range); x <= Mathf.Min(size - 1, cx + range); x++)
                {
                    Vector2 world = origin + new Vector2(x, z) * pixel, delta = world - point;
                    float lateral = Vector2.Dot(delta, right), along = Vector2.Dot(delta, forward);
                    float toe = 1 - lateral * lateral / (.063f * .063f) - (along - .045f) * (along - .045f) / (.105f * .105f);
                    float heel = 1 - lateral * lateral / (.045f * .045f) - (along + .075f) * (along + .075f) / (.055f * .055f);
                    float weight = Mathf.SmoothStep(0, 1, Mathf.Clamp01(Mathf.Max(toe, heel)));
                    if (weight <= 0 || SoilWeight(new Vector3(world.x, 0, world.y)) < minimumSoilWeight) continue;
                    int index = z * size + x;
                    marks[index] = (byte)Mathf.Max(marks[index], Mathf.RoundToInt(weight * 210)); dirty = true;
                }
        }

        public float SoilWeight(Vector3 point)
        {
            if (!terrain || !terrain.terrainData || coverage == null || soilLayer < 0 || soilLayer >= coverage.GetLength(2) || WatersideLayout.IsWater(point.x, point.z, .05f)) return 0;
            Vector3 local = point - terrain.transform.position, extent = terrain.terrainData.size;
            float u = local.x / extent.x, v = local.z / extent.z;
            if (u < 0 || u > 1 || v < 0 || v > 1) return 0;
            float x = u * (coverage.GetLength(1) - 1), z = v * (coverage.GetLength(0) - 1);
            int x0 = Mathf.FloorToInt(x), z0 = Mathf.FloorToInt(z), x1 = Mathf.Min(x0 + 1, coverage.GetLength(1) - 1), z1 = Mathf.Min(z0 + 1, coverage.GetLength(0) - 1);
            return Mathf.Lerp(Mathf.Lerp(coverage[z0, x0, soilLayer], coverage[z0, x1, soilLayer], x - x0),
                Mathf.Lerp(coverage[z1, x0, soilLayer], coverage[z1, x1, soilLayer], x - x0), z - z0);
        }

        public float SampleMark(Vector3 point)
        {
            if (!field) return 0;
            Vector2 uv = (new Vector2(point.x, point.z) - origin) / Mathf.Max(6, fieldSize);
            return uv.x < 0 || uv.x > 1 || uv.y < 0 || uv.y > 1 ? 0 : field.GetPixelBilinear(uv.x, uv.y).r;
        }
        float Ground(Vector3 point) => terrain.SampleHeight(point) + terrain.transform.position.y;
        void Bind()
        {
            terrain.GetSplatMaterialPropertyBlock(properties);
            float extent = Mathf.Max(6, fieldSize);
            properties.SetTexture(TextureId, field);
            properties.SetVector(RectId, new Vector4(origin.x, origin.y, 1 / extent, 1 / extent));
            Vector3 p = terrain.transform.position, s = terrain.terrainData.size;
            properties.SetVector(TerrainRectId, new Vector4(p.x, p.z, 1 / s.x, 1 / s.z));
            Vector4 layer = Vector4.zero; layer[Mathf.Clamp(soilLayer, 0, 3)] = 1; properties.SetVector(LayerId, layer);
            properties.SetFloat(StrengthId, 1); properties.SetFloat(DepthId, impressionDepth); properties.SetFloat(DarkeningId, darkening);
            terrain.SetSplatMaterialPropertyBlock(properties);
        }
        void OnDisable()
        {
            if (terrain && properties != null)
            {
                terrain.GetSplatMaterialPropertyBlock(properties);
                properties.SetTexture(TextureId, Texture2D.blackTexture); properties.SetFloat(StrengthId, 0);
                terrain.SetSplatMaterialPropertyBlock(properties);
            }
            if (field) { if (Application.isPlaying) Destroy(field); else DestroyImmediate(field); }
            field = null; marks = shifted = null; coverage = null; observed = false;
            carriedDistance = updateAge = 0;
        }
    }
}
