using UnityEngine;
using UnityEngine.Rendering;

namespace WeatherHouse.Life
{
    public sealed class LifeCastRange : MonoBehaviour
    {
        private const int Segments = 96;
        private Mesh mesh;
        private Material material;
        private MeshRenderer view;
        private readonly Vector3[] vertices = new Vector3[(Segments + 1) * 2];
        private readonly Vector2[] uv = new Vector2[(Segments + 1) * 2];
        private MaterialPropertyBlock block;
        private Vector3 lastCenter = Vector3.positiveInfinity;
        private float lastRadius, nextGroundSample;
        private Terrain[] terrain;
        public static LifeCastRange Create(Transform parent)
        {
            var go = new GameObject("Local weather boundary"); go.transform.SetParent(parent, false);
            return go.AddComponent<LifeCastRange>();
        }
        private void Awake()
        {
            mesh = new Mesh { name = "Local weather boundary mesh" }; mesh.MarkDynamic();
            var indices = new int[Segments * 6];
            for (int i = 0; i <= Segments; i++) { uv[i * 2] = new Vector2(i / (float)Segments, 0); uv[i * 2 + 1] = new Vector2(i / (float)Segments, 1); }
            for (int i = 0; i < Segments; i++)
            { int v = i * 2, t = i * 6; indices[t] = v; indices[t + 1] = v + 2; indices[t + 2] = v + 1; indices[t + 3] = v + 1; indices[t + 4] = v + 2; indices[t + 5] = v + 3; }
            mesh.vertices = vertices; mesh.uv = uv; mesh.triangles = indices;
            gameObject.AddComponent<MeshFilter>().sharedMesh = mesh; view = gameObject.AddComponent<MeshRenderer>();
            Shader shader = Resources.Load<Shader>("LifeGesture/LocalRange");
            if (shader) { material = new Material(shader) { name = "Local weather boundary (runtime)" }; view.sharedMaterial = material; }
            else Debug.LogError("Local weather boundary shader is missing.", this);
            view.shadowCastingMode = ShadowCastingMode.Off; view.receiveShadows = false;
            block = new MaterialPropertyBlock(); terrain = Terrain.activeTerrains; Hide();
        }
        public void Show(Vector3 center, float radius, Color color, float visibility)
        {
            if (!view || !material) return;
            view.enabled = visibility > .001f;
            block.SetColor("_Color", color); block.SetFloat("_Visibility", Mathf.Clamp01(visibility)); view.SetPropertyBlock(block);
            if (Time.unscaledTime < nextGroundSample && (lastCenter - center).sqrMagnitude < .01f && Mathf.Approximately(lastRadius, radius)) return;
            nextGroundSample = Time.unscaledTime + .15f; lastCenter = center; lastRadius = radius;
            transform.position = center; transform.rotation = Quaternion.identity; transform.localScale = Vector3.one;
            float band = Mathf.Clamp(radius * .012f, .045f, .12f);
            for (int i = 0; i <= Segments; i++)
            {
                float angle = i * Mathf.PI * 2 / Segments;
                Vector3 direction = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle));
                for (int edge = 0; edge < 2; edge++)
                {
                    Vector3 point = center + direction * (radius + (edge == 0 ? -band : band));
                    point.y = GroundY(point, center.y) + .055f; vertices[i * 2 + edge] = point - center;
                }
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
        public void Hide() { if (view) view.enabled = false; }
        private void OnDisable() => Hide();
        private void OnDestroy() { if (mesh) Destroy(mesh); if (material) Destroy(material); }
    }
}
