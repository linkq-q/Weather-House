using System.Collections.Generic;
using ForestSpriteCharacter;
using UnityEngine;

namespace WeatherHouse.Hillside
{
    // One constraint per actual player capsule; populations retain ownership of animal movement.
    public sealed class WildlifePlayerCollision : MonoBehaviour
    {
        private readonly List<WildlifePopulation> populations = new List<WildlifePopulation>();
        private CharacterController capsule;
        private ForestSpriteDemoController movement;
        private bool uniformSweep;
        private const float Gap = .12f;

        public static WildlifePlayerCollision Register(WildlifePopulation population)
        {
            if (!population || !population.player) return null;
            var controller = population.player.GetComponentInParent<CharacterController>();
            if (!controller) return null;
            var contact = controller.GetComponent<WildlifePlayerCollision>();
            if (!contact) contact = controller.gameObject.AddComponent<WildlifePlayerCollision>();
            contact.capsule = controller;
            contact.movement = controller.GetComponent<ForestSpriteDemoController>();
            if (!contact.populations.Contains(population)) contact.populations.Add(population);
            contact.Bind();
            return contact;
        }

        public void Unregister(WildlifePopulation population)
        {
            populations.Remove(population);
            if (populations.Count == 0) Unbind();
        }

        public bool Owns(Collider collider) => Active && collider && collider.transform.IsChildOf(transform);
        private bool Active => isActiveAndEnabled && capsule && capsule.enabled && capsule.gameObject.activeInHierarchy;
        private float Radius => capsule ? capsule.radius * Mathf.Max(Mathf.Abs(transform.lossyScale.x), Mathf.Abs(transform.lossyScale.z)) : 0f;

        private void Capsule(out Vector3 center, out float bottom, out float top)
        {
            center = transform.TransformPoint(capsule.center);
            float halfHeight = Mathf.Max(Radius, capsule.height * Mathf.Abs(transform.lossyScale.y) * .5f);
            bottom = center.y - halfHeight; top = center.y + halfHeight;
        }

        public bool TryObstacle(Vector3 feet, float animalRadius, out Vector3 center, out float radius, WildlifeBodyEnvelope envelope = default)
        {
            center = default; radius = 0;
            if (!Active) return false;
            Capsule(out center, out float bottom, out float top); radius = Radius;
            Dimensions(envelope, animalRadius, out _, out float animalBottom, out float animalTop);
            return VerticalOverlap(feet.y + animalBottom, feet.y + animalTop, bottom, top);
        }

        public bool AllowsAnimalMove(Vector3 from, Vector3 to, float animalRadius, WildlifeBodyEnvelope envelope = default)
        {
            if (!Active) return true;
            Capsule(out Vector3 center, out float bottom, out float top);
            Dimensions(envelope, animalRadius, out animalRadius, out float animalBottom, out float animalTop);
            return ContactFraction(from, to - from, from.y + animalBottom, from.y + animalTop,
                center, bottom, top, animalRadius + Radius + Gap, out _, out _) >= .99999f;
        }

        public Vector3 Constrain(Vector3 displacement)
        {
            if (movement) movement.AnimalCollisionFlags = CollisionFlags.None;
            if (!Active) return displacement;
            Vector3 candidate = ApplyBodies(displacement);
            if ((ApplyBodies(candidate) - candidate).sqrMagnitude > .0000000001f)
            {
                // Axis clipping can change a sloping path. Fall back once to prefixes of the original water-safe segment.
                uniformSweep = true;
                try { candidate = ApplyBodies(displacement); }
                finally { uniformSweep = false; }
            }
            return candidate;
        }

        private void RefreshContacts(float requestedY)
        {
            if (!movement) return;
            movement.AnimalCollisionFlags = CollisionFlags.None;
            if (!Active) return;
            // Native walls can shorten the constrained move again; only the actual capsule can provide support.
            foreach (WildlifePopulation population in populations)
                if (population && population.isActiveAndEnabled)
                    movement.AnimalCollisionFlags |= population.PlayerContactFlags(this, Vector3.zero, requestedY);
        }

        private Vector3 ApplyBodies(Vector3 displacement)
        {
            for (int i = populations.Count - 1; i >= 0; i--)
            {
                WildlifePopulation population = populations[i];
                if (!population) { populations.RemoveAt(i); continue; }
                if (population.isActiveAndEnabled) displacement = population.ConstrainPlayerMove(this, displacement);
            }
            return displacement;
        }

        internal Vector3 ConstrainAgainstBody(Vector3 displacement, Transform body, float radius, WildlifeBodyEnvelope envelope = default)
        {
            if (!body || body == transform || !body.gameObject.activeInHierarchy) return displacement;
            Capsule(out Vector3 center, out float bottom, out float top);
            Dimensions(envelope, radius, out radius, out float animalBottom, out float animalTop);
            float fraction = ContactFraction(center, displacement, bottom, top, body.position, body.position.y + animalBottom,
                body.position.y + animalTop, radius + Radius + Gap, out bool vertical, out bool horizontal);
            if (uniformSweep) return displacement * fraction;
            float flat = horizontal ? fraction : 1f, rise = vertical ? fraction : 1f;
            return new Vector3(displacement.x * flat, displacement.y * rise, displacement.z * flat);
        }

        internal CollisionFlags ContactFlags(Vector3 displacement, float requestedY, Transform body, float radius,
            WildlifeBodyEnvelope envelope = default)
        {
            if (!body || body == transform || !body.gameObject.activeInHierarchy) return CollisionFlags.None;
            Capsule(out Vector3 center, out float bottom, out float top);
            Dimensions(envelope, radius, out radius, out float animalBottom, out float animalTop);
            Vector3 away = center + displacement - body.position; away.y = 0;
            if (away.sqrMagnitude > Mathf.Pow(radius + Radius + Gap + .002f, 2f)) return CollisionFlags.None;
            if (requestedY < 0f && Mathf.Abs(bottom + displacement.y - body.position.y - animalTop) <= .004f) return CollisionFlags.Below;
            if (requestedY > 0f && Mathf.Abs(top + displacement.y - body.position.y - animalBottom) <= .004f) return CollisionFlags.Above;
            return CollisionFlags.None;
        }

        private static bool VerticalOverlap(float bottomA, float topA, float bottomB, float topB)
        { return topA > bottomB + .01f && topB > bottomA + .01f; }

        private static void Dimensions(WildlifeBodyEnvelope envelope, float fallback, out float radius, out float bottom, out float top)
        {
            bool measured = envelope.Valid && envelope.measured;
            radius = measured ? envelope.HorizontalRadius : fallback;
            bottom = measured ? envelope.Bottom : 0f; top = measured ? envelope.Top : fallback * 2f;
        }

        // Intersect the time interval inside the horizontal disc with the interval of overlapping heights.
        private static float ContactFraction(Vector3 from, Vector3 delta, float bottom, float top,
            Vector3 center, float otherBottom, float otherTop, float radius, out bool vertical, out bool horizontal)
        {
            vertical = horizontal = false;
            Vector2 offset = new Vector2(from.x - center.x, from.z - center.z), move = new Vector2(delta.x, delta.z);
            float distance = offset.sqrMagnitude - radius * radius, length = move.sqrMagnitude, toward = Vector2.Dot(offset, move);
            bool heightsOverlap = VerticalOverlap(bottom, top, otherBottom, otherTop);
            if (distance <= .000001f && heightsOverlap)
            {
                // A small outward component must not excuse crossing the body on the other axis.
                horizontal = length > .00000001f && toward < 0f;
                vertical = Mathf.Abs(delta.y) > .000001f && (bottom + top - otherBottom - otherTop) * delta.y < 0f;
                return horizontal || vertical ? 0f : 1f;
            }
            float horizontalEnter = 0f, horizontalExit = 1f;
            if (length < .00000001f) { if (distance > 0f) return 1f; }
            else
            {
                float discriminant = toward * toward - length * distance;
                if (discriminant <= 0f) return 1f;
                float root = Mathf.Sqrt(discriminant);
                horizontalEnter = Mathf.Max(0f, (-toward - root) / length);
                horizontalExit = Mathf.Min(1f, (-toward + root) / length);
            }
            float verticalEnter = 0f, verticalExit = 1f;
            if (Mathf.Abs(delta.y) < .000001f) { if (!heightsOverlap) return 1f; }
            else
            {
                float first = (otherBottom - top) / delta.y, second = (otherTop - bottom) / delta.y;
                verticalEnter = Mathf.Max(0f, Mathf.Min(first, second));
                verticalExit = Mathf.Min(1f, Mathf.Max(first, second));
            }
            float enter = Mathf.Max(horizontalEnter, verticalEnter), exit = Mathf.Min(horizontalExit, verticalExit);
            if (enter > exit || exit < 0f || enter > 1f) return 1f;
            vertical = !heightsOverlap && Mathf.Abs(delta.y) > .000001f && verticalEnter >= horizontalEnter;
            horizontal = !vertical;
            float speed = vertical ? Mathf.Abs(delta.y) : Mathf.Sqrt(length);
            return Mathf.Clamp01(enter - .002f / Mathf.Max(.000001f, speed));
        }

        private void Bind()
        {
            if (movement && isActiveAndEnabled && populations.Count > 0)
            {
                movement.ConstrainAnimalDisplacement = Constrain;
                movement.RefreshAnimalContacts = RefreshContacts;
            }
        }
        private void Unbind()
        {
            if (!movement) return;
            if (movement.ConstrainAnimalDisplacement == Constrain) movement.ConstrainAnimalDisplacement = null;
            if (movement.RefreshAnimalContacts == RefreshContacts)
            { movement.RefreshAnimalContacts = null; movement.AnimalCollisionFlags = CollisionFlags.None; }
        }
        private void OnEnable()
        {
            if (!capsule) capsule = GetComponent<CharacterController>();
            if (!movement) movement = GetComponent<ForestSpriteDemoController>();
            Bind();
        }
        private void OnDisable() { Unbind(); }
    }
}
