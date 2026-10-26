using UnityEngine;

namespace WeatherHouse.Vegetation
{
    [DisallowMultipleComponent, RequireComponent(typeof(Renderer)), DefaultExecutionOrder(200)]
    public sealed class PainterlyWaterInteraction : WaterInteraction
    {
        public GrassMeadow meadow;

        protected override bool TryGetWetSurface(Vector3 point, out float waterHeight)
        {
            waterHeight = 0;
            if (!meadow || !IsInsideRenderer(point)) return false;
            waterHeight = waterRenderer.bounds.center.y;
            Vector3 local = meadow.transform.InverseTransformPoint(new Vector3(point.x, waterHeight, point.z));
            if (meadow.PondDistance(new Vector2(local.x, local.z)) > 1.1f) return false;
            float groundY = meadow.transform.TransformPoint(new Vector3(local.x, meadow.GetGroundHeight(local.x, local.z), local.z)).y;
            return groundY < waterHeight - .015f;
        }

        protected override bool TryGetFieldBounds(out Bounds bounds) => base.TryGetFieldBounds(out bounds) && meadow;
    }
}
