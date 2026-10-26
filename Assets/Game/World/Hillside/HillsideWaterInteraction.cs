using ForestSpriteCharacter;
using UnityEngine;

namespace WeatherHouse.Hillside
{
    // Keep this component/GUID so existing Hillside and Waterside scenes retain their authored settings.
    [DisallowMultipleComponent, RequireComponent(typeof(Renderer)), DefaultExecutionOrder(200)]
    public sealed class HillsideWaterInteraction : WaterInteraction
    {
        public Terrain terrain;
        public WorldWaterSurface surface;

        void Start()
        {
            if (!player) player = FindObjectOfType<ForestSpriteDemoController>();
        }

        protected override bool TryGetWetSurface(Vector3 point, out float waterHeight)
        {
            waterHeight = 0;
            if (!terrain || !terrain.terrainData || !IsInsideRenderer(point)) return false;
            Vector3 origin = terrain.transform.position, size = terrain.terrainData.size;
            if (point.x < origin.x || point.x > origin.x + size.x || point.z < origin.z || point.z > origin.z + size.z) return false;
            if (surface)
            {
                if (!surface.TryGetHeight(point, out waterHeight)) return false;
            }
            else waterHeight = waterRenderer.bounds.center.y;
            return terrain.SampleHeight(point) + origin.y < waterHeight - .015f;
        }

        protected override bool TryGetFieldBounds(out Bounds bounds)
        {
            if (!base.TryGetFieldBounds(out bounds) || !terrain || !terrain.terrainData) return false;
            if (!surface) return true;
            // Offshore decoration must not spread the character's wake across the whole ocean apron.
            Vector3 origin = terrain.transform.position, extent = terrain.terrainData.size;
            Vector3 min = bounds.min, max = bounds.max;
            min.x = Mathf.Max(min.x, origin.x); min.z = Mathf.Max(min.z, origin.z);
            max.x = Mathf.Min(max.x, origin.x + extent.x); max.z = Mathf.Min(max.z, origin.z + extent.z);
            if (max.x <= min.x || max.z <= min.z) return false;
            bounds.SetMinMax(min, max);
            return true;
        }
    }
}
