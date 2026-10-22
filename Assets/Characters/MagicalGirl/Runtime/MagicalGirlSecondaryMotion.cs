using System;
using System.Collections.Generic;
using UnityEngine;

namespace MagicalGirlPreview
{
    [DefaultExecutionOrder(100)]
    public sealed class MagicalGirlSecondaryMotion : MonoBehaviour
    {
        [Serializable]
        public sealed class Chain
        {
            public Transform[] bones = Array.Empty<Transform>();
            [Min(0f)] public float stiffness = 24f;
            [Range(0f, 1f)] public float drag = .35f;
            [Range(0f, 40f)] public float maxAngle = 12f;
            [Min(0f)] public float radius = .018f;
        }
        [Serializable]
        public sealed class Sphere
        {
            public Transform bone;
            public Vector3 localCenter;
            [Min(0f)] public float radius = .1f;
        }
        public Chain[] chains = Array.Empty<Chain>();
        public Sphere[] collisionSpheres = Array.Empty<Sphere>();
        public bool motionEnabled = true;
        public Vector3 gravity = new Vector3(0f, -.15f, 0f);

        sealed class Joint
        {
            public Chain chain;
            public Transform bone;
            public Quaternion baseRotation;
            public Vector3 localTip, tip, previousTip;
        }
        readonly List<Joint> joints = new List<Joint>();
        bool initialized;

        void OnEnable() { initialized = false; }
        void Start() { Initialize(); }

        void Initialize()
        {
            joints.Clear();
            foreach (var chain in chains)
            {
                if (chain == null || chain.bones == null) continue;
                for (int i = 0; i + 1 < chain.bones.Length; i++)
                {
                    Transform bone = chain.bones[i], child = chain.bones[i + 1];
                    if (bone == null || child == null || Vector3.Distance(bone.position, child.position) < .0001f) continue;
                    joints.Add(new Joint { chain = chain, bone = bone, baseRotation = bone.localRotation,
                        localTip = bone.InverseTransformPoint(child.position), tip = child.position, previousTip = child.position });
                }
            }
            initialized = true;
        }

        void Update()
        {
            // Restore only our rotations before Animator evaluation, so swing never accumulates into the base pose.
            foreach (var joint in joints) if (joint.bone != null) joint.bone.localRotation = joint.baseRotation;
        }

        void LateUpdate()
        {
            if (!initialized) Initialize();
            float dt = Mathf.Min(Time.deltaTime, 1f / 30f);
            foreach (var joint in joints)
            {
                var bone = joint.bone;
                if (bone == null) continue;
                joint.baseRotation = bone.localRotation;
                Vector3 target = bone.TransformPoint(joint.localTip);
                if (!motionEnabled || dt <= 0f)
                {
                    joint.tip = joint.previousTip = target;
                    continue;
                }
                Vector3 origin = bone.position;
                Vector3 restDirection = target - origin;
                float length = restDirection.magnitude;
                Vector3 velocity = (joint.tip - joint.previousTip) * Mathf.Pow(1f - joint.chain.drag, dt * 60f);
                Vector3 next = joint.tip + velocity + gravity * dt * dt;
                next = Vector3.Lerp(next, target, 1f - Mathf.Exp(-joint.chain.stiffness * dt));
                foreach (var sphere in collisionSpheres)
                {
                    if (sphere == null || sphere.bone == null) continue;
                    Vector3 center = sphere.bone.TransformPoint(sphere.localCenter);
                    Vector3 delta = next - center;
                    float radius = sphere.radius * MaxScale(sphere.bone) + joint.chain.radius * MaxScale(bone);
                    if (delta.sqrMagnitude < radius * radius)
                        next = center + (delta.sqrMagnitude > .000001f ? delta.normalized : restDirection.normalized) * radius;
                }
                Vector3 direction = Vector3.RotateTowards(restDirection.normalized, (next - origin).normalized,
                    joint.chain.maxAngle * Mathf.Deg2Rad, 0f);
                next = origin + direction * length;
                joint.previousTip = joint.tip;
                joint.tip = next;
                bone.rotation = Quaternion.FromToRotation(restDirection, direction) * bone.rotation;
            }
        }

        static float MaxScale(Transform bone)
        {
            Vector3 scale = bone.lossyScale;
            return Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
        }

        public void ResetSimulation()
        {
            foreach (var joint in joints)
            {
                if (joint.bone == null) continue;
                joint.bone.localRotation = joint.baseRotation;
                joint.tip = joint.previousTip = joint.bone.TransformPoint(joint.localTip);
            }
        }

        void OnDisable() { ResetSimulation(); }
    }
}
