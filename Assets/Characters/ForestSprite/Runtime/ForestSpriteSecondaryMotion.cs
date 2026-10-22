using System;
using System.Collections.Generic;
using UnityEngine;

namespace ForestSpriteCharacter
{
    // Restore before Animator sampling, then add secondary rotations after the sampled pose.
    [DefaultExecutionOrder(-100), DisallowMultipleComponent]
    public sealed class ForestSpriteSecondaryMotion : MonoBehaviour
    {
        public enum MotionKind { Hat, Cape, Hair }

        [Serializable]
        public sealed class Chain
        {
            public MotionKind kind;
            public Transform[] bones = Array.Empty<Transform>();
            [Min(1)] public float stiffness = 90;
            [Min(0)] public float damping = 12;
            [Range(0, 50)] public float maxAngle = 20;
            [Min(0), Tooltip("Tail thickness in world metres.")] public float radius = .008f;
            [Tooltip("Used only when the last bone is not named *_Tip or *_End.")]
            public Vector3 localTip = new Vector3(0, .08f, 0);
            [Tooltip("Optional outward direction in the first bone parent's space. Zero disables the one-sided cape limit.")]
            public Vector3 outwardLocalDirection = Vector3.zero;
        }

        [Serializable]
        public sealed class CollisionSphere
        {
            public Transform transform;
            public Vector3 localCenter;
            [Min(0), Tooltip("Radius in world metres.")] public float radius = .1f;
        }

        public bool MotionEnabled = true;
        [Range(0, 1)] public float Strength = 1;
        public Chain[] chains = Array.Empty<Chain>();
        public CollisionSphere[] collisionSpheres = Array.Empty<CollisionSphere>();
        public Animator animator;

        public float MaxObservedAngle { get; private set; }
        public float MaxCurrentAngle { get; private set; }
        public float MaximumOffset { get; private set; }
        public float MaximumInwardOffset { get; private set; }
        public bool HasFiniteState { get; private set; } = true;
        public string LastResetReason { get; private set; } = "initial";
        public int ResetCount { get; private set; }
        public int SimulationSteps { get; private set; }
        public int LastSubsteps { get; private set; }
        public int ParticleCount => joints.Count;
        public int CollisionCorrections { get; private set; }
        public float RootSpeed { get; private set; }
        public float RootTurnSpeed { get; private set; }

        const float FixedStep = 1f / 120;
        const int MaxSubsteps = 12;
        sealed class Joint
        {
            public Transform bone, tipBone;
            public Chain chain;
            public int parent = -1;
            public Vector3 localTip, head, tip, oldHead, oldTip, position, velocity, solvedHead, outward;
            public Quaternion localRotation, worldRotation, correction = Quaternion.identity;
        }
        readonly List<Joint> joints = new List<Joint>();
        Vector3 previousRoot;
        Quaternion previousRootRotation;
        float accumulator;
        bool initialized, captured, reset = true;

        void OnEnable() { reset = true; }
        void OnDisable() { RestoreAnimatedPose(); reset = true; }
        void Update() { RestoreAnimatedPose(); }
        void LateUpdate() { Step(Time.deltaTime); }
        public void ResetSimulation() { reset = true; }

        public void RestoreAnimatedPose()
        {
            if (!captured) return;
            foreach (var joint in joints)
                if (joint.bone) joint.bone.localRotation = joint.localRotation;
            captured = false;
        }

        void Initialize()
        {
            joints.Clear();
            var assigned = new HashSet<Transform>();
            foreach (var chain in chains)
            {
                if (chain == null || chain.bones == null || chain.bones.Length == 0) continue;
                Transform end = chain.bones[chain.bones.Length - 1];
                bool realTip = end && (end.name.EndsWith("_Tip", StringComparison.Ordinal)
                    || end.name.EndsWith("_End", StringComparison.Ordinal));
                int count = chain.bones.Length - (realTip ? 1 : 0);
                int parent = -1;
                for (int i = 0; i < count; i++)
                {
                    var bone = chain.bones[i];
                    var tip = i + 1 < chain.bones.Length ? chain.bones[i + 1] : null;
                    if (!bone || !bone.IsChildOf(transform) || !assigned.Add(bone)
                        || (tip && tip.parent != bone))
                        throw new InvalidOperationException("Invalid or duplicate secondary bone in " + chain.kind);
                    Vector3 localTip = tip ? bone.InverseTransformPoint(tip.position) : chain.localTip;
                    if (bone.TransformVector(localTip).sqrMagnitude < 1e-10f)
                        throw new InvalidOperationException("Secondary bone has no usable tail: " + bone.name);
                    joints.Add(new Joint { bone = bone, tipBone = tip, localTip = localTip,
                        chain = chain, parent = parent, localRotation = bone.localRotation });
                    parent = joints.Count - 1;
                }
            }
            if (!animator) animator = GetComponentInChildren<Animator>();
            initialized = true;
            reset = true;
        }

        public void Step(float deltaTime)
        {
            if (!initialized) Initialize();
            // Also safe for an explicit review step called twice in the same frame.
            RestoreAnimatedPose();
            foreach (var joint in joints)
            {
                joint.localRotation = joint.bone.localRotation;
                joint.worldRotation = joint.bone.rotation;
                joint.head = joint.bone.position;
                joint.tip = joint.tipBone ? joint.tipBone.position : joint.bone.TransformPoint(joint.localTip);
                joint.outward = joint.chain.bones[0].parent.TransformDirection(joint.chain.outwardLocalDirection);
            }
            captured = true;
            Vector3 rootDelta = transform.position - previousRoot;
            float turn = Quaternion.Angle(transform.rotation, previousRootRotation);
            bool usableTime = float.IsFinite(deltaTime) && deltaTime > 0;
            RootSpeed = usableTime ? rootDelta.magnitude / deltaTime : 0;
            RootTurnSpeed = usableTime ? turn / deltaTime : 0;
            string reason = !MotionEnabled || Strength <= 0 ? "disabled"
                : reset ? "manual" : !usableTime || (animator && animator.speed == 0) ? "paused"
                : deltaTime > .25f ? "longFrame" : rootDelta.sqrMagnitude > .75f * .75f ? "teleport"
                : turn > 100 ? "rotationJump" : null;
            previousRoot = transform.position;
            previousRootRotation = transform.rotation;
            LastSubsteps = 0;
            CollisionCorrections = 0;
            MaxCurrentAngle = MaximumOffset = MaximumInwardOffset = 0;
            if (reason != null)
            {
                ResetAtPose(reason);
                return;
            }

            float frameTime = Mathf.Min(deltaTime, FixedStep * MaxSubsteps);
            float firstStepTime = FixedStep - accumulator;
            accumulator += frameTime;
            int steps = Mathf.Min(MaxSubsteps, Mathf.FloorToInt((accumulator + 1e-7f) / FixedStep));
            accumulator = Mathf.Max(0, accumulator - steps * FixedStep);
            for (int step = 0; step < steps; step++)
            {
                float blend = Mathf.Clamp01((firstStepTime + step * FixedStep) / frameTime);
                for (int i = 0; i < joints.Count; i++) SimulateJoint(i, blend);
            }
            LastSubsteps = steps;
            SimulationSteps += steps;
            foreach (var joint in joints)
            {
                if (!Finite(joint.position) || !Finite(joint.velocity))
                {
                    HasFiniteState = false;
                    ResetAtPose("nonFinite");
                    return;
                }
                joint.bone.rotation = Quaternion.Slerp(Quaternion.identity, joint.correction, Strength) * joint.worldRotation;
                MaxCurrentAngle = Mathf.Max(MaxCurrentAngle, Quaternion.Angle(Quaternion.identity, joint.correction) * Strength);
                MaximumOffset = Mathf.Max(MaximumOffset, Vector3.Distance(joint.position, joint.tip) * Strength);
                joint.oldHead = joint.head;
                joint.oldTip = joint.tip;
            }
            MaxObservedAngle = Mathf.Max(MaxObservedAngle, MaxCurrentAngle);
        }

        void ResetAtPose(string reason)
        {
            foreach (var joint in joints)
            {
                joint.position = joint.oldTip = joint.tip;
                joint.solvedHead = joint.oldHead = joint.head;
                joint.velocity = Vector3.zero;
                joint.correction = Quaternion.identity;
                joint.bone.localRotation = joint.localRotation;
            }
            accumulator = 0;
            reset = false;
            LastResetReason = reason;
            ResetCount++;
        }

        void SimulateJoint(int index, float blend)
        {
            var joint = joints[index];
            Vector3 authoredHead = Vector3.Lerp(joint.oldHead, joint.head, blend);
            Vector3 authoredDirection = Vector3.Lerp(joint.oldTip - joint.oldHead, joint.tip - joint.head, blend);
            Quaternion inherited = joint.parent >= 0 ? joints[joint.parent].correction : Quaternion.identity;
            Vector3 anchor = joint.parent >= 0 ? joints[joint.parent].position : authoredHead;
            Vector3 direction = inherited * authoredDirection;
            float length = direction.magnitude;
            if (length < 1e-6f) return;
            Vector3 anchorVelocity = (anchor - joint.solvedHead) / FixedStep;
            joint.solvedHead = anchor;
            Vector3 target = anchor + direction;
            joint.velocity = SpringVelocity(joint.velocity, target - joint.position,
                joint.chain.stiffness, joint.chain.damping, FixedStep);
            Vector3 predicted = joint.position + joint.velocity * FixedStep;
            // Cape segments each retain their authored outward reach, so parent sway cannot accumulate inward.
            Vector3 constraintAxis = joint.outward.sqrMagnitude > 1e-12f ? authoredDirection : direction;
            // Keep the hat crown and band stable while the two distal joints carry the larger bounce.
            float angle = joint.chain.maxAngle * (joint.chain.kind == MotionKind.Hat && joint.parent < 0 ? .35f : 1f);
            Vector3 next = LimitTail(anchor, predicted, constraintAxis, angle, joint.outward);
            for (int pass = 0; pass < 3; pass++)
            {
                bool changed = false;
                foreach (var sphere in collisionSpheres)
                {
                    if (sphere == null || !sphere.transform || sphere.radius <= 0) continue;
                    Vector3 center = sphere.transform.TransformPoint(sphere.localCenter);
                    float radius = sphere.radius + Mathf.Max(0, joint.chain.radius);
                    Vector3 away = next - center;
                    if (away.sqrMagnitude >= radius * radius) continue;
                    if (away.sqrMagnitude < 1e-12f) away = direction;
                    next = center + away.normalized * radius;
                    CollisionCorrections++;
                    changed = true;
                }
                next = LimitTail(anchor, next, constraintAxis, angle, joint.outward);
                if (!changed) break;
            }
            Vector3 unit = (next - anchor).normalized;
            Vector3 relativeVelocity = Vector3.ProjectOnPlane(joint.velocity - anchorVelocity, unit);
            // A limit removes outward momentum instead of feeding constraint corrections back as impulses.
            Vector3 removed = predicted - next;
            if (removed.sqrMagnitude > 1e-12f)
                relativeVelocity -= removed.normalized * Mathf.Max(0, Vector3.Dot(relativeVelocity, removed.normalized));
            joint.velocity = anchorVelocity + relativeVelocity;
            joint.position = next;
            joint.correction = Quaternion.FromToRotation(direction, next - anchor) * inherited;
            if (joint.outward.sqrMagnitude > 1e-12f)
                MaximumInwardOffset = Mathf.Max(MaximumInwardOffset,
                    -Vector3.Dot(next - authoredHead - authoredDirection, joint.outward.normalized));
        }

        static bool Finite(Vector3 value) => float.IsFinite(value.x) && float.IsFinite(value.y) && float.IsFinite(value.z);
        static Vector3 SpringVelocity(Vector3 velocity, Vector3 error, float stiffness, float damping, float dt)
            => (velocity + error * Mathf.Max(1, stiffness) * dt) / (1 + Mathf.Max(0, damping) * dt);

        static Vector3 LimitTail(Vector3 anchor, Vector3 tail, Vector3 axis, float angle, Vector3 outward = default)
        {
            if (axis.sqrMagnitude < 1e-12f) return anchor;
            Vector3 direction = tail - anchor;
            if (direction.sqrMagnitude < 1e-12f) direction = axis;
            Vector3 unitAxis = axis.normalized;
            float radians = Mathf.Clamp(angle, 0, 50) * Mathf.Deg2Rad;
            Vector3 unit = Vector3.RotateTowards(unitAxis, direction.normalized, radians, 0);
            if (outward.sqrMagnitude > 1e-12f)
            {
                Vector3 normal = outward.normalized;
                float minimum = Vector3.Dot(unitAxis, normal);
                if (Vector3.Dot(unit, normal) < minimum)
                {
                    float circleRadiusSquared = Mathf.Max(0, 1 - minimum * minimum);
                    if (circleRadiusSquared < 1e-10f) unit = unitAxis;
                    else
                    {
                        Vector3 circleAxis = Vector3.ProjectOnPlane(unitAxis, normal).normalized;
                        Vector3 circleTarget = Vector3.ProjectOnPlane(unit, normal);
                        if (circleTarget.sqrMagnitude < 1e-12f) circleTarget = circleAxis;
                        // Intersect the constant-length sphere, bend cone and authored outward plane.
                        float azimuth = Mathf.Acos(Mathf.Clamp((Mathf.Cos(radians) - minimum * minimum) / circleRadiusSquared, -1, 1));
                        Vector3 circleDirection = Vector3.RotateTowards(circleAxis, circleTarget.normalized, azimuth, 0);
                        unit = normal * minimum + circleDirection * Mathf.Sqrt(circleRadiusSquared);
                    }
                }
            }
            return anchor + unit * axis.magnitude;
        }

#if UNITY_EDITOR
        public static string RunLogicChecks()
        {
            void Require(bool condition, string message)
            {
                if (!condition) throw new InvalidOperationException("Forest sprite spring check: " + message);
            }
            Vector3 anchor = new Vector3(.2f, 1, -.3f), axis = Vector3.up * .25f;
            foreach (float angle in new[] { 0f, 12f, 25f, 50f })
                foreach (var tail in new[] { anchor, anchor - axis, anchor + Vector3.right * 4, anchor + axis * 8 })
                {
                    Vector3 result = LimitTail(anchor, tail, axis, angle) - anchor;
                    Require(Finite(result) && Mathf.Abs(result.magnitude - axis.magnitude) < 1e-6f, "length or finite constraint");
                    Require(Vector3.Angle(axis, result) <= angle + .001f, "bend constraint");
                }
            foreach (float radial in new[] { -.8f, 0f, .35f, .8f })
            {
                Vector3 capeAxis = new Vector3(radial, -Mathf.Sqrt(1 - radial * radial), 0) * .23f;
                foreach (float angle in new[] { 0f, 10f, 15f, 30f })
                    for (int sample = 0; sample < 24; sample++)
                    {
                        Vector3 candidate = Quaternion.Euler(sample * 17, sample * 31, sample * 11) * capeAxis;
                        Vector3 result = LimitTail(anchor, anchor + candidate, capeAxis, angle, Vector3.right) - anchor;
                        Require(Finite(result) && Mathf.Abs(result.magnitude - capeAxis.magnitude) < 1e-6f, "one-sided length");
                        Require(Vector3.Dot(capeAxis.normalized, result.normalized) >= Mathf.Cos(angle * Mathf.Deg2Rad) - 1e-6f, "one-sided bend");
                        Require(result.x >= capeAxis.x - 1e-6f, "cape moved inward from authored reach");
                    }
            }
            Vector3 reference = Vector3.zero;
            foreach (int frameRate in new[] { 30, 60, 120 })
            {
                Vector3 position = Vector3.right * .12f, velocity = Vector3.zero;
                float remainder = 0;
                int total = 0;
                for (int frame = 0; frame < frameRate * 3; frame++)
                {
                    remainder += 1f / frameRate;
                    while (remainder + 1e-7f >= FixedStep)
                    {
                        velocity = SpringVelocity(velocity, -position, 90, 12, FixedStep);
                        position += velocity * FixedStep;
                        remainder -= FixedStep;
                        total++;
                        Require(Finite(position) && Finite(velocity) && position.magnitude <= .121f, "bounded impulse response");
                    }
                }
                Require(total == 360 && position.magnitude < .0001f, "fixed step count or settling");
                if (frameRate == 30) reference = position;
                else Require(Vector3.Distance(position, reference) < 1e-7f, "30/60/120 fps agreement");
            }
            return "PASS: tail length/bend and one-sided cape constraints, finite bounded response, settling, 30/60/120 fps fixed-step agreement";
        }
#endif
    }
}
