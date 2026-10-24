using UnityEngine;

namespace WeatherHouse.WildlifeJourney
{
    // A short additive head pose; the herd retains all movement, pairing and cooldown ownership.
    public static class JourneySocialGesture
    {
        static readonly Collider[] hits = new Collider[32];
        public struct Pose
        {
            public Vector3 angles;
            public float weight;
        }

        struct Motion
        {
            public Vector3 peak;
            public float seconds, pulses;
            public Motion(Vector3 peak, float seconds, float pulses)
            { this.peak = peak; this.seconds = seconds; this.pulses = pulses; }
        }

        static readonly Motion Sniff = new Motion(new Vector3(11, 3, 0), 1.55f, 2);
        static readonly Motion Nose = new Motion(new Vector3(6, 0, 0), 1.35f, 1);
        static readonly Motion Rub = new Motion(new Vector3(5, 13, 8), 1.7f, 1);
        static readonly Motion LookBack = new Motion(new Vector3(-3, 34, -3), 1.7f, 1);

        public static Pose Sample(string intent, float age, float turnDuration, bool speaker, float side = 1)
        {
            Motion motion;
            switch (intent)
            {
                case "sniff": motion = Sniff; break;
                case "nuzzle": motion = Nose; break;
                case "rub": motion = Rub; break;
                case "greeting": motion = LookBack; break;
                default: return default;
            }
            float duration = Mathf.Min(motion.seconds, turnDuration);
            if (duration <= 0 || age <= 0 || age >= duration) return default;
            float phase = age / duration;
            float envelope = Mathf.SmoothStep(0, 1, phase / .22f) * Mathf.SmoothStep(0, 1, (1 - phase) / .28f);
            float pulse = .5f - .5f * Mathf.Cos(phase * motion.pulses * Mathf.PI * 2);
            float response = speaker ? 1 : .35f;
            Vector3 angles = motion.peak;
            angles.y *= side < 0 ? -1 : 1;
            angles.z *= side < 0 ? -1 : 1;
            if (intent == "sniff") angles.x *= .68f + .32f * pulse;
            if (intent == "nuzzle") angles.x *= pulse;
            if (intent == "rub") { angles.y *= 1 - 2 * phase; angles.z *= pulse; }
            return new Pose { angles = angles * envelope * response, weight = envelope * response };
        }

        public static Quaternion Rotation(Transform root, Quaternion animatedHeadWorld, Pose pose)
        {
            return Quaternion.AngleAxis(pose.angles.z, root.forward) *
                Quaternion.AngleAxis(pose.angles.y, root.up) *
                Quaternion.AngleAxis(pose.angles.x, root.right) * animatedHeadWorld;
        }

        // Measured on the current sheep mesh. This grows a query only; never changes its movement profile.
        public static Bounds RequiredBody(Vector3 center, Vector3 half)
        {
            float scale = half.y / .475f;
            center.y += .01f * scale;
            half += new Vector3(.04f, .01f, 0) * scale;
            return new Bounds(center, half * 2);
        }

        public static Bounds SweptBody(Bounds body, Quaternion from, Quaternion to, out Quaternion midpoint)
        {
            midpoint = Quaternion.Slerp(from, to, .5f);
            float radius = new Vector2(Mathf.Abs(body.center.x) + body.extents.x, Mathf.Abs(body.center.z) + body.extents.z).magnitude;
            float arc = 2 * radius * Mathf.Sin(Quaternion.Angle(from, to) * Mathf.Deg2Rad * .25f);
            body.Expand(new Vector3(arc * 2, 0, arc * 2));
            return body;
        }

        public static bool ClearWorld(Transform root, Quaternion rotation, Bounds body, int mask, Collider supportingGround)
        {
            int count = Physics.OverlapBoxNonAlloc(root.position + rotation * body.center,
                body.extents, hits, rotation, mask, QueryTriggerInteraction.Ignore);
            if (count == hits.Length) return false;
            for (int i = 0; i < count; i++)
            {
                var hit = hits[i];
                if (hit && hit != supportingGround && !(hit is TerrainCollider) && !hit.transform.IsChildOf(root)) return false;
            }
            return true;
        }

        public static bool Separated(Vector3 firstPosition, Quaternion firstRotation, Bounds first,
            Vector3 secondPosition, Quaternion secondRotation, Bounds second)
        {
            Vector3 a = firstPosition + firstRotation * first.center, b = secondPosition + secondRotation * second.center;
            Vector3 offset = b - a;
            if (Mathf.Abs(offset.y) >= first.extents.y + second.extents.y + .005f) return true;
            Vector3 ar = firstRotation * Vector3.right, af = firstRotation * Vector3.forward;
            Vector3 br = secondRotation * Vector3.right, bf = secondRotation * Vector3.forward;
            return Along(ar) || Along(af) || Along(br) || Along(bf);
            bool Along(Vector3 axis)
            {
                float radius = Mathf.Abs(Vector3.Dot(ar, axis)) * first.extents.x + Mathf.Abs(Vector3.Dot(af, axis)) * first.extents.z +
                    Mathf.Abs(Vector3.Dot(br, axis)) * second.extents.x + Mathf.Abs(Vector3.Dot(bf, axis)) * second.extents.z;
                return Mathf.Abs(Vector3.Dot(offset, axis)) >= radius + .005f;
            }
        }

        public static bool ClearCircle(Vector3 rootPosition, Quaternion rootRotation, Bounds body, Vector3 center, float radius)
        {
            Vector3 local = Quaternion.Inverse(rootRotation) * (center - rootPosition) - body.center;
            float x = Mathf.Max(0, Mathf.Abs(local.x) - body.extents.x), z = Mathf.Max(0, Mathf.Abs(local.z) - body.extents.z);
            return x * x + z * z >= (radius + .005f) * (radius + .005f);
        }
    }
}
