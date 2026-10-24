using UnityEngine;

namespace WeatherHouse.Hillside
{
    // Heading-aligned world metres, measured once after visual scale is applied.
    public struct WildlifeBodyEnvelope
    {
        public Vector3 center, half;
        public bool measured;
        public bool Valid => half.x > 0 && half.y > 0 && half.z > 0;
        public float Bottom => center.y - half.y;
        public float Top => center.y + half.y;
        public float HorizontalRadius => new Vector2(Mathf.Abs(center.x) + half.x, Mathf.Abs(center.z) + half.z).magnitude;
        public float ContactRadius(float fallback) => Valid && measured ? HorizontalRadius : fallback;

        public float Reach(Vector3 direction, Quaternion rotation, float fallback)
        {
            if (!Valid) return fallback;
            direction.y = 0;
            if (direction.sqrMagnitude < .000001f) return fallback;
            Vector3 local = Quaternion.Inverse(rotation) * direction.normalized;
            return Vector3.Dot(local, center) + Mathf.Abs(local.x) * half.x + Mathf.Abs(local.z) * half.z;
        }

        public WildlifeBodyEnvelope WithLength(float length)
        { var body = this; body.half.z = Mathf.Max(.025f, length * .5f); return body; }

        public static WildlifeBodyEnvelope Measure(Transform root, float fallbackRadius)
        {
            Bounds bounds = default; bool found = false;
            Quaternion unrotate = Quaternion.Inverse(root.rotation);
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>())
            {
                if (!renderer.enabled) continue;
                Mesh baked = null;
                try
                {
                    Mesh mesh;
                    // Unity 2022.3.62f3c1: true + TransformPoint matches manual bone-weight world vertices,
                    // including the imported sheep's nested scale; false repeats that scale (see scale probe).
                    if (renderer is SkinnedMeshRenderer skin) { baked = new Mesh(); skin.BakeMesh(baked, true); mesh = baked; }
                    else mesh = renderer.GetComponent<MeshFilter>()?.sharedMesh;
                    if (!mesh) continue;
                    if (mesh.isReadable)
                        foreach (Vector3 vertex in mesh.vertices) Encapsulate(vertex);
                    else
                    {
                        // Static imported meshes may discard CPU vertices in a Player build.
                        Bounds local = mesh.bounds;
                        for (int corner = 0; corner < 8; corner++)
                            Encapsulate(local.center + Vector3.Scale(local.extents,
                                new Vector3((corner & 1) == 0 ? -1 : 1, (corner & 2) == 0 ? -1 : 1, (corner & 4) == 0 ? -1 : 1)));
                    }
                    void Encapsulate(Vector3 vertex)
                    {
                        Vector3 point = unrotate * (renderer.transform.TransformPoint(vertex) - root.position);
                        if (!found) { bounds = new Bounds(point, Vector3.zero); found = true; }
                        else bounds.Encapsulate(point);
                    }
                }
                finally
                {
                    if (baked) { if (Application.isPlaying) Object.Destroy(baked); else Object.DestroyImmediate(baked); }
                }
            }
            if (!found)
            {
                float radius = Mathf.Max(.05f, fallbackRadius);
                bounds = new Bounds(Vector3.up * radius, Vector3.one * radius * 2);
            }
            Vector3 half = bounds.extents;
            half.x = Mathf.Max(.025f, half.x); half.y = Mathf.Max(.025f, half.y); half.z = Mathf.Max(.025f, half.z);
            return new WildlifeBodyEnvelope { center = bounds.center, half = half, measured = found };
        }

        public static Quaternion Heading(Vector3 direction, Quaternion fallback)
        { direction.y = 0; return direction.sqrMagnitude > .000001f ? Quaternion.LookRotation(direction) : fallback; }
    }

    // Shared obstacle test for placement, translation and turning. Supporting ground is not an obstacle;
    // a bridge rail remains an obstacle even when its deck supports the feet.
    public sealed class WildlifeBodyPhysics
    {
        private readonly Collider[] overlaps = new Collider[64];
        private readonly RaycastHit[] hits = new RaycastHit[64];

        public static bool ClearOfBody(WildlifeBodyEnvelope body, Vector3 from, Quaternion fromRotation,
            Vector3 to, Quaternion toRotation, WildlifeBodyEnvelope other, Vector3 otherPosition, Quaternion otherRotation)
        {
            if (!body.Valid || !other.Valid) return true;
            float angle = Quaternion.Angle(fromRotation, toRotation);
            int steps = Mathf.Max(1, Mathf.Max(Mathf.CeilToInt(angle / 3f), Mathf.CeilToInt(Vector3.Distance(from, to) / .05f)));
            float depth = BodyOverlap(body, from, fromRotation, other, otherPosition, otherRotation);
            for (int i = 0; i < steps; i++)
            {
                float a = (float)i / steps, b = (float)(i + 1) / steps;
                Vector3 start = Vector3.Lerp(from, to, a), finish = Vector3.Lerp(from, to, b);
                Quaternion middle = Quaternion.Slerp(fromRotation, toRotation, (a + b) * .5f);
                if (depth > .00001f)
                {
                    Vector3 away = start + Quaternion.Slerp(fromRotation, toRotation, a) * body.center -
                        otherPosition - otherRotation * other.center;
                    Vector3 move = finish + Quaternion.Slerp(fromRotation, toRotation, b) * body.center - start -
                        Quaternion.Slerp(fromRotation, toRotation, a) * body.center;
                    away.y = move.y = 0;
                    // Sliding farther down a long overlapping body can leave minimum depth unchanged.
                    if (Vector3.Dot(away, move) < -.000001f) return false;
                }
                Vector3 travel = Quaternion.Inverse(middle) * (finish - start) * .5f;
                float arc = 2 * body.HorizontalRadius * Mathf.Sin(angle / steps * Mathf.Deg2Rad * .25f);
                var swept = body;
                swept.half += new Vector3(Mathf.Abs(travel.x) + arc, Mathf.Abs(travel.y), Mathf.Abs(travel.z) + arc);
                // The swept box includes every intermediate pose. Existing overlap may only retreat.
                if (BodyOverlap(swept, (start + finish) * .5f, middle, other, otherPosition, otherRotation) > depth + .00001f)
                    return false;
                depth = BodyOverlap(body, finish, Quaternion.Slerp(fromRotation, toRotation, b), other, otherPosition, otherRotation);
            }
            return true;
        }

        private static float BodyOverlap(WildlifeBodyEnvelope a, Vector3 p, Quaternion q,
            WildlifeBodyEnvelope b, Vector3 otherPosition, Quaternion otherRotation)
        {
            Vector3 ac = p + q * a.center, bc = otherPosition + otherRotation * b.center;
            if (ac.y + a.half.y <= bc.y - b.half.y || bc.y + b.half.y <= ac.y - a.half.y) return 0;
            Vector3 right = q * Vector3.right, forward = q * Vector3.forward;
            Vector3 otherRight = otherRotation * Vector3.right, otherForward = otherRotation * Vector3.forward;
            Vector3 delta = ac - bc; delta.y = 0;
            float gap = Mathf.Max(Mathf.Max(Gap(right), Gap(forward)), Mathf.Max(Gap(otherRight), Gap(otherForward)));
            return Mathf.Max(0, -gap);
            float Gap(Vector3 axis) => Mathf.Abs(Vector3.Dot(delta, axis)) -
                Mathf.Abs(Vector3.Dot(right, axis)) * a.half.x - Mathf.Abs(Vector3.Dot(forward, axis)) * a.half.z -
                Mathf.Abs(Vector3.Dot(otherRight, axis)) * b.half.x - Mathf.Abs(Vector3.Dot(otherForward, axis)) * b.half.z;
        }

        public bool Clear(WildlifeBodyEnvelope body, Vector3 from, Quaternion fromRotation,
            Vector3 to, Quaternion toRotation, LayerMask mask, Collider supportFrom = null, Collider supportTo = null,
            Transform owner = null, WildlifePlayerCollision player = null, bool ignoreAnimals = false)
        {
            if (!body.Valid || mask.value == 0) return true;
            float angle = Quaternion.Angle(fromRotation, toRotation);
            int steps = Mathf.Max(1, Mathf.CeilToInt(angle / 5f));
            for (int step = 0; step < steps; step++)
            {
                float a = (float)step / steps, b = (float)(step + 1) / steps;
                Quaternion facing = Quaternion.Slerp(fromRotation, toRotation, (a + b) * .5f);
                // Every point of the rotating box lies within this inflated midpoint box.
                float arc = 2 * body.HorizontalRadius * Mathf.Sin(angle / steps * Mathf.Deg2Rad * .25f);
                Vector3 half = body.half + new Vector3(arc, 0, arc);
                Vector3 start = Vector3.Lerp(from, to, a) + facing * body.center;
                Vector3 finish = Vector3.Lerp(from, to, b) + facing * body.center;
                int count = Physics.OverlapBoxNonAlloc(start, half, overlaps, facing, mask, QueryTriggerInteraction.Ignore);
                if (count == overlaps.Length) return false;
                for (int i = 0; i < count; i++) if (Obstacle(overlaps[i], supportFrom, supportTo, owner, player, ignoreAnimals)) return false;
                Vector3 delta = finish - start;
                if (delta.sqrMagnitude > .0000001f)
                {
                    count = Physics.BoxCastNonAlloc(start, half, delta.normalized, hits, facing, delta.magnitude, mask, QueryTriggerInteraction.Ignore);
                    if (count == hits.Length) return false;
                    for (int i = 0; i < count; i++) if (Obstacle(hits[i].collider, supportFrom, supportTo, owner, player, ignoreAnimals)) return false;
                }
            }
            return true;
        }

        private static bool Obstacle(Collider hit, Collider supportFrom, Collider supportTo, Transform owner,
            WildlifePlayerCollision player, bool ignoreAnimals)
        {
            return hit && !(hit is TerrainCollider) && hit != supportFrom && hit != supportTo &&
                !(owner && hit.transform.IsChildOf(owner)) && !(player && player.Owns(hit)) &&
                !(ignoreAnimals && hit.GetComponentInParent<WildlifeAgent>());
        }
    }
}
