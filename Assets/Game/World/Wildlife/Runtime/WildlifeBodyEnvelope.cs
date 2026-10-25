using System.Collections.Generic;
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

    // Exact top-face height over a horizontal, heading-aligned footprint. Fixed storage is reused per owner.
    public sealed class WildlifeDeckFootprint
    {
        private readonly Vector3[] polygon = new Vector3[12], clipped = new Vector3[12];
        internal static float SeparationGuard(float centerMagnitude, float boxCenterMagnitude)
        { return .00001f + Mathf.Max(centerMagnitude, boxCenterMagnitude) * .000002f; }
        // A convex cast can report contact across a sub-millimetre empty gap. Prove separation
        // against the entire swept footprint, never from a hit point or only the final pose.
        public bool StrictlySeparatedVerticalBox(BoxCollider box, Vector3 center, Vector3 half, Quaternion heading)
        {
            if (!box) return false;
            Transform transform = box.transform;
            Vector3 vertical = transform.localToWorldMatrix.MultiplyVector(Vector3.up);
            Vector3 up = transform.rotation * Vector3.up, bodyUp = heading * Vector3.up;
            Vector3 scale = transform.lossyScale;
            if (vertical.x != 0 || vertical.z != 0 || vertical.y <= 0 || up.x != 0 || up.z != 0 || up.y <= 0 ||
                bodyUp.x != 0 || bodyUp.z != 0 || bodyUp.y <= 0 || scale.x == 0 || scale.y <= 0 || scale.z == 0) return false;
            Vector3 right = heading * Vector3.right, forward = heading * Vector3.forward;
            Vector3 boxCenter = transform.TransformPoint(box.center), size = box.size * .5f;
            // Enclose both transformed vertices and the rotation/lossyScale physical box. This
            // remains conservative when a rotated child inherits non-uniform parent scale.
            float guard = SeparationGuard(center.magnitude, boxCenter.magnitude);
            for (int side = 0; side < 4; side++)
            {
                Vector3 axis = side == 0 ? right : side == 1 ? forward : transform.rotation * (side == 2 ? Vector3.right : Vector3.forward);
                float bodyExtent = half.x * Mathf.Abs(Vector3.Dot(axis, right)) + half.z * Mathf.Abs(Vector3.Dot(axis, forward));
                float min = float.PositiveInfinity, max = float.NegativeInfinity;
                for (int corner = 0; corner < 4; corner++)
                {
                    Vector3 offset = new Vector3((corner & 1) == 0 ? -size.x : size.x, size.y, (corner & 2) == 0 ? -size.z : size.z);
                    float affine = Vector3.Dot(axis, transform.TransformPoint(box.center + offset) - center);
                    float physical = Vector3.Dot(axis, boxCenter + transform.rotation * Vector3.Scale(scale, offset) - center);
                    min = Mathf.Min(min, Mathf.Min(affine, physical)); max = Mathf.Max(max, Mathf.Max(affine, physical));
                }
                if (min > bodyExtent + guard || max < -bodyExtent - guard) return true;
            }
            return false;
        }
        public bool HighestTop(BoxCollider box, Vector3 center, Vector3 half, Quaternion heading, out float top)
        {
            top = float.NegativeInfinity;
            if (!box || box.transform.lossyScale.y <= 0 || box.transform.up.y <= .01f) return false;
            Quaternion inverse = Quaternion.Inverse(heading); Vector3 size = box.size * .5f;
            for (int corner = 0; corner < 4; corner++)
            {
                Vector3 local = box.center + new Vector3(corner == 0 || corner == 3 ? -size.x : size.x, size.y, corner < 2 ? -size.z : size.z);
                polygon[corner] = inverse * (box.transform.TransformPoint(local) - center);
            }
            int vertices = 4;
            for (int side = 0; side < 4 && vertices > 0; side++)
            {
                int output = 0; int axis = side < 2 ? 0 : 2; float sign = side % 2 == 0 ? 1 : -1;
                float extent = axis == 0 ? half.x : half.z;
                Vector3 previous = polygon[vertices - 1]; float oldDistance = extent - sign * previous[axis];
                for (int v = 0; v < vertices; v++)
                {
                    Vector3 current = polygon[v]; float distance = extent - sign * current[axis];
                    if ((distance >= 0) != (oldDistance >= 0)) clipped[output++] = Vector3.Lerp(previous, current, oldDistance / (oldDistance - distance));
                    if (distance >= 0) clipped[output++] = current;
                    previous = current; oldDistance = distance;
                }
                vertices = output; System.Array.Copy(clipped, polygon, output);
            }
            for (int v = 0; v < vertices; v++) top = Mathf.Max(top, (center + heading * polygon[v]).y);
            return vertices > 0;
        }
    }

    // Shared obstacle test for placement, translation and turning. Supporting ground is not an obstacle;
    // a bridge rail remains an obstacle even when its deck supports the feet.
    public sealed class WildlifeBodyPhysics
    {
        private readonly Collider[] overlaps = new Collider[64];
        private readonly RaycastHit[] hits = new RaycastHit[64];
        private readonly WildlifeDeckFootprint deckFootprint = new WildlifeDeckFootprint();
        internal System.Action<Collider> ObstructionObserved;
        internal bool Reject(Collider collider)
        { ObstructionObserved?.Invoke(collider); return false; }

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
            Transform owner = null, WildlifePlayerCollision player = null, bool ignoreAnimals = false,
            IReadOnlyDictionary<Collider, int> supportingDecks = null)
        {
            if (!body.Valid || mask.value == 0) return true;
            // Wildlife stays upright while turning. Only that contract permits a flat sole height;
            // tilted bodies keep the original collision test without the deck-contact exemption.
            float lowestBody = Vector3.Angle(fromRotation * Vector3.up, Vector3.up) <= .01f &&
                Vector3.Angle(toRotation * Vector3.up, Vector3.up) <= .01f
                ? Mathf.Min(from.y, to.y) + body.Bottom : float.NegativeInfinity;
            bool upright = fromRotation.x == 0 && fromRotation.z == 0 && toRotation.x == 0 && toRotation.z == 0;
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
                Vector3 localDelta = Quaternion.Inverse(facing) * (finish - start);
                Vector3 sweptHalf = half + new Vector3(Mathf.Abs(localDelta.x), 0, Mathf.Abs(localDelta.z)) * .5f;
                Vector3 sweptCenter = (start + finish) * .5f;
                int count = Physics.OverlapBoxNonAlloc(start, half, overlaps, facing, mask, QueryTriggerInteraction.Ignore);
                if (count == overlaps.Length) return Reject(null);
                for (int i = 0; i < count; i++) if (Obstacle(overlaps[i], supportFrom, supportTo, owner, player, ignoreAnimals, supportingDecks, lowestBody, sweptCenter, sweptHalf, facing, upright)) return Reject(overlaps[i]);
                Vector3 delta = finish - start;
                if (delta.sqrMagnitude > .0000001f)
                {
                    count = Physics.BoxCastNonAlloc(start, half, delta.normalized, hits, facing, delta.magnitude, mask, QueryTriggerInteraction.Ignore);
                    if (count == hits.Length) return Reject(null);
                    for (int i = 0; i < count; i++) if (Obstacle(hits[i].collider, supportFrom, supportTo, owner, player, ignoreAnimals, supportingDecks, lowestBody, sweptCenter, sweptHalf, facing, upright)) return Reject(hits[i].collider);
                }
            }
            return true;
        }

        private bool Obstacle(Collider hit, Collider supportFrom, Collider supportTo, Transform owner,
            WildlifePlayerCollision player, bool ignoreAnimals, IReadOnlyDictionary<Collider, int> supportingDecks, float lowestBody,
            Vector3 center, Vector3 half, Quaternion heading, bool upright)
        {
            return hit && !(hit is TerrainCollider) && hit != supportFrom && hit != supportTo &&
                !(owner && hit.transform.IsChildOf(owner)) && !(player && player.Owns(hit)) &&
                !(ignoreAnimals && hit.GetComponentInParent<WildlifeAgent>()) &&
                // Coplanar registered slabs can touch the feet across a seam without occupying the body.
                !(supportingDecks != null && supportingDecks.ContainsKey(hit) &&
                    (hit.bounds.max.y <= lowestBody + .001f || deckFootprint.HighestTop(hit as BoxCollider, center, half, heading, out float top) && top <= lowestBody + .001f ||
                        upright && deckFootprint.StrictlySeparatedVerticalBox(hit as BoxCollider, center, half, heading)));
        }
    }
}
