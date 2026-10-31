using UnityEngine;
using UnityEngine.SceneManagement;
using WeatherHousePrototype;

namespace WeatherHouse.Life
{
    public static class LifeArrivalRegion
    {
        static readonly RaycastHit[] hits = new RaycastHit[32];

        public static bool Contains(Transform player, Vector3 point, float radius = 2.5f)
        {
            if (!player || float.IsNaN(radius) || float.IsInfinity(radius) || radius <= 0) return false;
            Vector3 offset = point - player.position;
            if (float.IsNaN(offset.sqrMagnitude) || float.IsInfinity(offset.sqrMagnitude) ||
                Mathf.Abs(offset.y) > 1.5f || new Vector2(offset.x, offset.z).sqrMagnitude > radius * radius) return false;
            if (offset.sqrMagnitude < .0001f) return true;
            int count = player.gameObject.scene.GetPhysicsScene().Raycast(player.position + Vector3.up * .8f,
                offset.normalized, hits, offset.magnitude, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            if (count == hits.Length) return false;
            for (int i = 0; i < count; i++)
            {
                Transform hit = hits[i].transform;
                if (hit && !hit.IsChildOf(player) && !hit.GetComponentInParent<AnimalActor>()) return false;
            }
            return true;
        }
    }

    public sealed partial class LifeWorld
    {
        bool PlayerInArrivalRegion(string id, float radius = 2.5f) =>
            !string.IsNullOrEmpty(id) && !invalidTargets.Contains(id) && anchors.TryGetValue(id, out Vector3 point) &&
            LifeArrivalRegion.Contains(Player, point, radius);

        float SequenceRegionRadius(string first, string last) =>
            Mathf.Min(2.5f, Flat(AnchorPosition(first), AnchorPosition(last)) * .4f);
    }
}
