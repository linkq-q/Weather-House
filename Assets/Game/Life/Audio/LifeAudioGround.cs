using UnityEngine;

namespace WeatherHouse.Life
{
    public sealed class LifeAudioGround
    {
        // Authored Walk/Run cycles plant opposite feet at phase 0 and .5.
        // A slow frame emits one current step, never a burst of missed footsteps.
        public static bool CrossedStep(float previous, float current) => current >= previous &&
            Mathf.FloorToInt(current * 2) > Mathf.FloorToInt(previous * 2);
        // Include a foot at the surface, including capsule skin/contact tolerance.
        public static bool Wading(float waterHeight, float supportHeight) =>
            waterHeight >= supportHeight - .04f && waterHeight <= supportHeight + .6f;
        readonly RaycastHit[] hits = new RaycastHit[24];
        public LifeAudioSurface Resolve(Vector3 feet, Transform player, LifeAudioCatalog catalog, out Vector3 contact)
        {
            contact = feet;
            int count = Physics.RaycastNonAlloc(feet + Vector3.up * .35f, Vector3.down, hits, 1.1f, ~0, QueryTriggerInteraction.Ignore);
            int nearest = -1; float distance = float.MaxValue;
            for (int i = 0; i < count; i++)
            {
                if (!hits[i].collider || hits[i].transform.IsChildOf(player) || hits[i].normal.y < .3f || hits[i].distance >= distance) continue;
                nearest = i; distance = hits[i].distance;
            }
            if (nearest < 0) return LifeAudioSurface.Unknown;
            RaycastHit hit = hits[nearest]; contact = hit.point;
            Terrain terrain = hit.collider.GetComponent<Terrain>();
            if (terrain) return TerrainAt(terrain, hit.point, catalog);
            Renderer renderer = hit.collider.GetComponent<Renderer>() ?? hit.collider.GetComponentInParent<Renderer>();
            if (!renderer) return LifeAudioSurface.Unknown;
            Material[] materials = renderer.sharedMaterials;
            if (materials.Length == 1) return catalog.Surface(materials[0]);
            MeshCollider mesh = hit.collider as MeshCollider;
            if (!mesh || !mesh.sharedMesh || hit.triangleIndex < 0) return LifeAudioSurface.Unknown;
            // Raycast triangles address the combined submesh index buffer; no name-based material guesses.
            uint index = (uint)hit.triangleIndex * 3;
            for (int part = 0; part < mesh.sharedMesh.subMeshCount && part < materials.Length; part++)
            {
                uint length = mesh.sharedMesh.GetIndexCount(part);
                if (index < length) return catalog.Surface(materials[part]);
                index -= length;
            }
            return LifeAudioSurface.Unknown;
        }

        public static LifeAudioSurface TerrainAt(Terrain terrain, Vector3 point, LifeAudioCatalog catalog)
            => TerrainAt(terrain.terrainData, terrain.transform.position, point, catalog);

        public static LifeAudioSurface TerrainAt(TerrainData data, Vector3 origin, Vector3 point, LifeAudioCatalog catalog)
        {
            Vector3 local = point - origin;
            int x = Mathf.Clamp(Mathf.FloorToInt(local.x / data.size.x * data.alphamapWidth), 0, data.alphamapWidth - 1);
            int z = Mathf.Clamp(Mathf.FloorToInt(local.z / data.size.z * data.alphamapHeight), 0, data.alphamapHeight - 1);
            float[,,] weights = data.GetAlphamaps(x, z, 1, 1);
            int dominant = 0;
            for (int layer = 1; layer < weights.GetLength(2); layer++) if (weights[0, 0, layer] > weights[0, 0, dominant]) dominant = layer;
            TerrainLayer[] layers = data.terrainLayers;
            return dominant < layers.Length ? catalog.Surface(layers[dominant]) : LifeAudioSurface.Unknown;
        }
    }
}
