using System;
using System.Collections.Generic;
using UnityEngine;

namespace WeatherHouse.Hillside
{
    // Opt-in foot correction for a new forage clip's crossfade. It never changes the animal root.
    public sealed class WildlifeForageContact
    {
        private sealed class Sole
        {
            public Vector3 a, b, c, d;
            public Transform boneA, boneB, boneC, boneD;
            public Vector4 weight;
        }
        private sealed class Leg
        {
            public Transform[] joints;
            public Quaternion[] original;
            public Vector3[] points;
            public float[] lengths;
            public readonly List<Sole> sole = new List<Sole>();
        }
        private readonly List<Leg> legs = new List<Leg>();
        private Func<Vector3, float> ground;
        private bool applied;
        public bool Enabled => legs.Count > 0;

        public void Initialize(Transform root, WildlifeForageContactData data, Func<Vector3, float> groundHeight)
        {
            Restore(); legs.Clear(); ground = groundHeight;
            if (!data || data.bones == null || data.legs == null || ground == null) return;
            var bones = new Transform[data.bones.Length];
            for (int i = 0; i < bones.Length; i++) { bones[i] = root.Find(data.bones[i]); if (!bones[i]) return; }
            foreach (var definition in data.legs)
            {
                Transform first = root.Find(definition.first), last = root.Find(definition.last);
                if (!first || !last || first == last || definition.sole == null) continue;
                var chain = new List<Transform>(); Transform joint = last;
                while (joint && joint != first) { chain.Add(joint); joint = joint.parent; }
                if (!joint) continue;
                chain.Add(joint); chain.Reverse();
                var leg = new Leg { joints = chain.ToArray(), original = new Quaternion[chain.Count], points = new Vector3[chain.Count], lengths = new float[chain.Count - 1] };
                foreach (var s in definition.sole)
                    leg.sole.Add(new Sole { a = s.a, b = s.b, c = s.c, d = s.d, boneA = bones[s.boneA], boneB = bones[s.boneB],
                        boneC = bones[s.boneC], boneD = bones[s.boneD], weight = s.weight });
                if (leg.sole.Count > 0) legs.Add(leg);
            }
        }

        public void Restore()
        {
            if (!applied) return;
            foreach (var leg in legs)
                for (int i = 0; i < leg.joints.Length; i++) if (leg.joints[i]) leg.joints[i].localRotation = leg.original[i];
            applied = false;
        }

        public void Apply(float forageWeight)
        {
            // The contact correction vanishes continuously at both pure clips.
            float strength = Mathf.Min(1f, forageWeight / .08f, (1f - forageWeight) / .04f);
            if (strength < .001f || legs.Count == 0) return;
            foreach (var leg in legs)
                for (int i = 0; i < leg.joints.Length; i++) leg.original[i] = leg.joints[i].localRotation;
            applied = true;
            foreach (var leg in legs)
            {
                float penetration = Penetration(leg);
                if (penetration <= .0001f) continue;
                float residual = penetration * (1 - strength);
                for (int correction = 0; correction < 3; correction++)
                {
                    float lift = Penetration(leg) - residual;
                    if (lift <= .0001f) break;
                    Vector3 target = leg.joints[leg.joints.Length - 1].position + Vector3.up * lift;
                    Solve(leg, target);
                }
            }
        }

        private float Penetration(Leg leg)
        {
            float penetration = 0;
            foreach (var sole in leg.sole) { Vector3 at = Position(sole); penetration = Mathf.Max(penetration, ground(at) + .001f - at.y); }
            return penetration;
        }

        private static Vector3 Position(Sole s)
        {
            Vector3 at = s.boneA.TransformPoint(s.a) * s.weight.x;
            if (s.weight.y > 0) at += s.boneB.TransformPoint(s.b) * s.weight.y;
            if (s.weight.z > 0) at += s.boneC.TransformPoint(s.c) * s.weight.z;
            if (s.weight.w > 0) at += s.boneD.TransformPoint(s.d) * s.weight.w;
            return at;
        }

        private static void Solve(Leg leg, Vector3 target)
        {
            int last = leg.joints.Length - 1;
            for (int i = 0; i <= last; i++) leg.points[i] = leg.joints[i].position;
            Vector3 anchor = leg.points[0]; Quaternion footRotation = leg.joints[last].rotation;
            float total = 0;
            for (int i = 0; i < last; i++) { leg.lengths[i] = Vector3.Distance(leg.points[i], leg.points[i + 1]); total += leg.lengths[i]; }
            if (last == 1)
            {
                // Some source rigs have only shoulder and paw. Keep its one bone length;
                // raising the paw also retracts its horizontal reach instead of stretching it.
                float y = Mathf.Clamp(target.y - anchor.y, -total + .00001f, total - .00001f);
                Vector3 flat = leg.points[last] - anchor; flat.y = 0;
                target = anchor + flat.normalized * Mathf.Sqrt(Mathf.Max(0, total * total - y * y)) + Vector3.up * y;
            }
            target = anchor + Vector3.ClampMagnitude(target - anchor, Mathf.Max(0, total - .00001f));
            // A bounded forward/backward solve keeps every existing bone length and the current bend side.
            for (int iteration = 0; iteration < 8; iteration++)
            {
                leg.points[last] = target;
                for (int i = last - 1; i >= 0; i--) leg.points[i] = leg.points[i + 1] + (leg.points[i] - leg.points[i + 1]).normalized * leg.lengths[i];
                leg.points[0] = anchor;
                for (int i = 1; i <= last; i++) leg.points[i] = leg.points[i - 1] + (leg.points[i] - leg.points[i - 1]).normalized * leg.lengths[i - 1];
            }
            for (int i = 0; i < last; i++)
                leg.joints[i].rotation = Quaternion.FromToRotation(leg.joints[i + 1].position - leg.joints[i].position, leg.points[i + 1] - leg.points[i]) * leg.joints[i].rotation;
            leg.joints[last].rotation = footRotation;
        }
    }
}
