using ForestSpriteCharacter;
using UnityEngine;

namespace WeatherHouse.WatersideV2
{
    [DisallowMultipleComponent, DefaultExecutionOrder(120)]
    public sealed class WatersideWaterBoundary : MonoBehaviour
    {
        public Terrain terrain;
        public ForestSpriteDemoController player;
        public WorldWaterSurface surface;
        [Range(.1f, .8f)] public float maximumWadingDepth = .4f;
        public bool HasSafePosition { get; private set; }
        public Vector3 LastSafePosition { get; private set; }
        public int RollbackCount { get; private set; }
        public float LastRejectedDepth { get; private set; }
        CharacterController body;

        void LateUpdate()
        {
            if (!Application.isPlaying || !terrain || !terrain.terrainData || !player || !surface || !player.isActiveAndEnabled) return;
            if (!body) body = player.GetComponent<CharacterController>();
            if (!body || !body.enabled) return;
            Vector3 point = player.transform.position;
            Vector3 sole = new Vector3(point.x, body.bounds.min.y, point.z);
            float depth = surface.Contains(sole) ? surface.Height(sole) - sole.y : 0;
            if (depth <= maximumWadingDepth)
            {
                // A supported bridge deck is safe even when the terrain underneath is deeply submerged.
                Vector3 local = point - terrain.transform.position;
                if (player.IsGrounded && local.x >= 0 && local.z >= 0 &&
                    local.x <= terrain.terrainData.size.x && local.z <= terrain.terrainData.size.z)
                { LastSafePosition = point; HasSafePosition = true; }
                return;
            }
            if (!HasSafePosition) return;
            LastRejectedDepth = depth;
            body.enabled = false;
            player.transform.position = LastSafePosition;
            body.enabled = true;
            RollbackCount++;
        }
    }
}
