using UnityEngine;
using UnityEngine.Rendering;

namespace WeatherHouse.Life
{
    [ExecuteAlways, RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public sealed class LifeCastRange : MonoBehaviour
    {
        public Material rangeMaterial;
        [Min(.001f)] public float rimHalfWidth = .096f;
        [Min(.01f)] public float flameHeight = 1.12f;
        public float groundOffset = .055f;
        [Min(.01f)] public float openingDuration = .65f;
        [Min(.01f)] public float fadeSpeed = 2.5f;
        [Header("Prefab preview (edit mode only)")]
        public bool preview = true;
        public bool previewFlames = true;
        [Min(2f)] public float previewRadius = 8f;
        private const int Segments = 192;
        private Mesh mesh;
        private Material material;
        private MeshRenderer view;
        private readonly Vector3[] vertices = new Vector3[(Segments + 1) * 4];
        private readonly Vector2[] uv = new Vector2[(Segments + 1) * 4];
        private MaterialPropertyBlock block;
        private Vector3 lastCenter = Vector3.positiveInfinity;
        private float lastRadius, nextGroundSample;
        private float targetVisibility, visibility, targetFlames, flames, appearedAt;
        private Terrain[] terrain;
        public static LifeCastRange Create(Transform parent)
        {
            var prefab = Resources.Load<LifeCastRange>("LifeWeather/CastRange");
            if (!prefab) { Debug.LogError("Missing LifeWeather/CastRange prefab."); return null; }
            return Instantiate(prefab, parent, false);
        }
        private void OnEnable()
        {
            // Unity can retain the native mesh across a reload while managed preview helpers are lost.
            view = GetComponent<MeshRenderer>(); material = rangeMaterial;
            block = new MaterialPropertyBlock(); terrain = Terrain.activeTerrains;
            if (mesh) return;
            mesh = new Mesh { name = "Local weather boundary mesh" }; mesh.MarkDynamic();
            mesh.hideFlags = HideFlags.HideAndDontSave;
            var indices = new int[Segments * 12];
            for (int i = 0; i <= Segments; i++)
            {
                float u = i / (float)Segments;
                // Negative V identifies the ground rim; 0..1 is the upright flame strip.
                uv[i * 4] = new Vector2(u, -2); uv[i * 4 + 1] = new Vector2(u, -1);
                uv[i * 4 + 2] = new Vector2(u, 0); uv[i * 4 + 3] = new Vector2(u, 1);
            }
            for (int i = 0; i < Segments; i++)
            {
                for (int strip = 0; strip < 2; strip++)
                {
                    int v = i * 4 + strip * 2, t = i * 12 + strip * 6;
                    indices[t] = v; indices[t + 1] = v + 4; indices[t + 2] = v + 1;
                    indices[t + 3] = v + 1; indices[t + 4] = v + 4; indices[t + 5] = v + 5;
                }
            }
            mesh.vertices = vertices; mesh.uv = uv; mesh.triangles = indices;
            GetComponent<MeshFilter>().sharedMesh = mesh; view = GetComponent<MeshRenderer>();
            material = rangeMaterial; view.sharedMaterial = material;
            view.shadowCastingMode = ShadowCastingMode.Off; view.receiveShadows = false;
            block = new MaterialPropertyBlock(); terrain = Terrain.activeTerrains; view.enabled = false;
        }
        public void Show(Vector3 center, float radius, float amount, bool maintainedWeather)
        {
            if (!view || !material) return;
            if (targetVisibility <= 0 && visibility <= .001f) appearedAt = Time.time;
            targetVisibility = Mathf.Clamp01(amount); targetFlames = maintainedWeather ? 1 : 0;
            if (Time.unscaledTime < nextGroundSample && (lastCenter - center).sqrMagnitude < .01f && Mathf.Approximately(lastRadius, radius)) return;
            nextGroundSample = Time.unscaledTime + .15f; lastCenter = center; lastRadius = radius;
            transform.position = center; transform.rotation = Quaternion.identity; transform.localScale = Vector3.one;
            float band = rimHalfWidth;
            float height = flameHeight;
            for (int i = 0; i <= Segments; i++)
            {
                float angle = i * Mathf.PI * 2 / Segments;
                Vector3 direction = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle));
                for (int edge = 0; edge < 2; edge++)
                {
                    Vector3 point = center + direction * (radius + (edge == 0 ? -band : band));
                    point.y = GroundY(point, center.y) + groundOffset; vertices[i * 4 + edge] = point - center;
                }
                Vector3 foot = center + direction * radius;
                foot.y = GroundY(foot, center.y) + groundOffset;
                vertices[i * 4 + 2] = foot - center;
                vertices[i * 4 + 3] = foot - center + Vector3.up * height;
            }
            mesh.vertices = vertices; mesh.RecalculateBounds();
        }
        private float GroundY(Vector3 point, float fallback)
        {
            foreach (Terrain t in terrain)
            {
                if (!t || !t.terrainData) continue;
                Vector3 local = point - t.transform.position, size = t.terrainData.size;
                if (local.x >= 0 && local.z >= 0 && local.x <= size.x && local.z <= size.z)
                    return t.SampleHeight(point) + t.transform.position.y;
            }
            return fallback;
        }
        private void LateUpdate()
        {
            if (view && material != rangeMaterial) { material = rangeMaterial; view.sharedMaterial = material; }
            if (!view || !material) return;
            if (!Application.isPlaying)
            {
                nextGroundSample = 0;
                Show(transform.position, previewRadius, preview ? .9f : 0, previewFlames);
                visibility = targetVisibility; flames = targetFlames;
            }
            else
            {
                visibility = Mathf.MoveTowards(visibility, targetVisibility, Time.deltaTime * fadeSpeed);
                flames = Mathf.MoveTowards(flames, targetFlames, Time.deltaTime * fadeSpeed);
            }
            view.enabled = visibility > .001f;
            if (!view.enabled) return;
            block.SetFloat("_Visibility", visibility);
            block.SetFloat("_Flames", flames); block.SetFloat("_Reveal", Application.isPlaying ? Mathf.Clamp01((Time.time - appearedAt) / openingDuration) : 1);
            view.SetPropertyBlock(block);
        }
        public void Hide() { targetVisibility = 0; targetFlames = 0; }
        private void OnDisable() { Hide(); visibility = flames = 0; if (view) view.enabled = false; }
        private void OnDestroy() { if (mesh) { if (Application.isPlaying) Destroy(mesh); else DestroyImmediate(mesh); } }
    }
}
