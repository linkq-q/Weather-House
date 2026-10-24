using System;
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
        public int BlockedStepCount { get; private set; }
        // Kept for existing diagnostics; movement is now constrained before Move and never rolled back.
        public int RollbackCount => 0;
        public float LastRejectedDepth { get; private set; }
        CharacterController body;
        readonly RaycastHit[] supportHits = new RaycastHit[16];
        Func<Vector3, float> sampleDepth;

        void OnEnable()
        {
            if (!player) player = GetComponent<ForestSpriteDemoController>();
            if (!player) return;
            body = player.GetComponent<CharacterController>();
            sampleDepth = SampleDepth;
            player.ConstrainDisplacement = ConstrainMovement;
            HasSafePosition = false;
        }

        void OnDisable()
        {
            if (player && player.ConstrainDisplacement == ConstrainMovement)
                player.ConstrainDisplacement = null;
        }

        Vector3 ConstrainMovement(Vector3 displacement)
        {
            if (!terrain || !terrain.terrainData || !surface || !body || !body.enabled) return displacement;
            Vector3 horizontal = new Vector3(displacement.x, 0, displacement.z);
            Vector3 allowed = ConstrainWadingStep(player.transform.position, horizontal, maximumWadingDepth,
                sampleDepth, out float rejectedDepth);
            if ((allowed - horizontal).sqrMagnitude > .00000001f)
            {
                BlockedStepCount++;
                LastRejectedDepth = rejectedDepth;
            }
            return new Vector3(allowed.x, displacement.y, allowed.z);
        }

        float SampleDepth(Vector3 point) => SampleSupportedDepth(point, body.bounds.min.y);

        // Probe only surfaces reachable near the feet, so a high bridge cannot make the riverbed walkable.
        public float SampleSupportedDepth(Vector3 point, float soleHeight)
        {
            if (!surface.Contains(point)) return 0;
            if (!body && player) body = player.GetComponent<CharacterController>();
            float floor = terrain.SampleHeight(point) + terrain.transform.position.y;
            float reach = body ? body.stepOffset + body.skinWidth + .05f : .35f;
            // Keep the takeoff deck visible through this controller's own jump arc; the upward reach stays unchanged.
            float supportDrop = player && !player.IsGrounded
                ? Mathf.Max(.24f, player.jumpHeight + (body ? body.skinWidth : 0) + .05f) : .24f;
            float slope = body ? body.slopeLimit : 45f;
            int count = Physics.RaycastNonAlloc(new Vector3(point.x, soleHeight + reach, point.z),
                Vector3.down, supportHits, reach + supportDrop, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                var hit = supportHits[i];
                if (hit.collider == body || (player && hit.transform.IsChildOf(player.transform)) ||
                    Vector3.Angle(hit.normal, Vector3.up) > slope) continue;
                floor = Mathf.Max(floor, hit.point.y);
            }
            float waterHeight = surface.Height(point);
            if (waterHeight - floor > maximumWadingDepth && body)
            {
                // A capsule can stand across a deck seam even when its centre ray misses both stones.
                // Keep the sphere inside the scaled foot and keep the original vertical support window.
                Vector3 scale = body.transform.lossyScale;
                float radius = Mathf.Max(0, body.radius - body.skinWidth) * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
                if (radius > .0001f)
                {
                    count = Physics.SphereCastNonAlloc(new Vector3(point.x, soleHeight + reach + radius, point.z), radius,
                        Vector3.down, supportHits, reach + supportDrop, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
                    for (int i = 0; i < count; i++)
                    {
                        var hit = supportHits[i];
                        if (hit.collider == body || (player && hit.transform.IsChildOf(player.transform)) ||
                            Vector3.Angle(hit.normal, Vector3.up) > slope || hit.point.y > soleHeight + reach ||
                            hit.point.y < soleHeight - supportDrop) continue;
                        floor = Mathf.Max(floor, hit.point.y);
                    }
                }
            }
            return Mathf.Max(0, waterHeight - floor);
        }

        // Sweeping before Move keeps corrections inside this step instead of returning to an old grounded point.
        public static Vector3 ConstrainWadingStep(Vector3 start, Vector3 displacement, float maximumDepth,
            Func<Vector3, float> depthAt, out float rejectedDepth)
        {
            displacement.y = 0;
            // An externally placed/falling character already in deep water may move sideways or toward shallower water.
            float limit = Mathf.Max(maximumDepth, depthAt(start));
            Vector3 contact = Sweep(start, displacement, limit, depthAt, out rejectedDepth);
            Vector3 remainder = displacement - (contact - start);
            if (remainder.sqrMagnitude < .00000001f) return contact - start;
            const float epsilon = .05f;
            Vector3 gradient = new Vector3(
                depthAt(contact + Vector3.right * epsilon) - depthAt(contact - Vector3.right * epsilon), 0,
                depthAt(contact + Vector3.forward * epsilon) - depthAt(contact - Vector3.forward * epsilon));
            if (gradient.sqrMagnitude < .00000001f) return contact - start;
            Vector3 normal = gradient.normalized;
            Vector3 slide = remainder - normal * Mathf.Max(0, Vector3.Dot(remainder, normal));
            Vector3 end = Sweep(contact, slide, limit, depthAt, out float slideDepth);
            rejectedDepth = Mathf.Max(rejectedDepth, slideDepth);
            return end - start;
        }

        static Vector3 Sweep(Vector3 start, Vector3 delta, float limit, Func<Vector3, float> depthAt, out float rejectedDepth)
        {
            rejectedDepth = 0;
            int steps = Mathf.Max(1, Mathf.CeilToInt(delta.magnitude / .05f));
            float safe = 0;
            for (int i = 1; i <= steps; i++)
            {
                float next = i / (float)steps;
                float depth = depthAt(start + delta * next);
                if (depth <= limit) { safe = next; continue; }
                rejectedDepth = depth;
                for (int iteration = 0; iteration < 10; iteration++)
                {
                    float middle = (safe + next) * .5f;
                    if (depthAt(start + delta * middle) <= limit) safe = middle;
                    else next = middle;
                }
                return start + delta * safe;
            }
            return start + delta;
        }

        void LateUpdate()
        {
            if (!Application.isPlaying || !player || !surface || !player.isActiveAndEnabled || !body || !body.enabled) return;
            Vector3 point = player.transform.position;
            Vector3 sole = new Vector3(point.x, body.bounds.min.y, point.z);
            float depth = surface.Contains(sole) ? surface.Height(sole) - sole.y : 0;
            if (player.IsGrounded && depth <= maximumWadingDepth)
            { LastSafePosition = point; HasSafePosition = true; }
        }
    }
}
