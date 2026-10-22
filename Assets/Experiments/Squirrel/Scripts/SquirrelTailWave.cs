using System.Collections.Generic;
using UnityEngine;

namespace WeatherHouseSquirrelLab
{
    [DefaultExecutionOrder(100)]
    public sealed class SquirrelTailWave : MonoBehaviour
    {
        public Transform modelRoot;
        [Range(0f, 65f)] public float amplitudeDegrees = 44f;
        [Range(0f, 15f)] public float rootAmplitudeDegrees = 6f;
        [Range(0.1f, 2f)] public float frequency = 0.65f;
        [Range(0.5f, 2f)] public float wavesAlongTail = 1.15f;
        public float phaseOffset;

        private static readonly string[] BoneNames =
            { "TailRoot", "Tail02", "Tail03", "Tail04", "Tail05", "Tail06", "Tail07", "Tail08" };
        private readonly Transform[] tail = new Transform[8];
        private readonly Quaternion[] restRotation = new Quaternion[8];
        private readonly Vector3[] bendAxis = new Vector3[8];
        private readonly float[] distance = new float[8];
        private Transform bodyParent;
        private bool initialized;

        public bool IsInitialized => initialized;

        private void Start()
        {
            if (!initialized)
                Initialize(modelRoot != null ? modelRoot : transform);
        }

        public bool Initialize(Transform root)
        {
            initialized = false;
            modelRoot = root;
            if (root == null)
                return false;

            var transforms = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < tail.Length; i++)
            {
                tail[i] = Find(transforms, BoneNames[i]);
                if (tail[i] == null || (i > 0 && tail[i].parent != tail[i - 1]))
                {
                    Debug.LogError("SquirrelTailWave requires the connected TailRoot -> Tail08 chain.", this);
                    return false;
                }
            }

            bodyParent = tail[0].parent;
            var bind = new Dictionary<Transform, Matrix4x4>();
            foreach (var renderer in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (renderer.sharedMesh == null)
                    continue;
                var bones = renderer.bones;
                var poses = renderer.sharedMesh.bindposes;
                for (int i = 0; i < Mathf.Min(bones.Length, poses.Length); i++)
                    if (bones[i] != null)
                        bind[bones[i]] = renderer.transform.localToWorldMatrix * poses[i].inverse;
            }

            // Bind poses keep reinitialization independent of the Animator's current frame.
            if (bodyParent == null || !bind.TryGetValue(bodyParent, out Matrix4x4 parentBind))
            {
                Debug.LogError("SquirrelTailWave could not find the pelvis bind pose.", this);
                return false;
            }
            for (int i = 0; i < tail.Length; i++)
                if (!bind.ContainsKey(tail[i]))
                {
                    Debug.LogError("SquirrelTailWave could not find a tail bind pose.", this);
                    return false;
                }

            Quaternion inverseBody = Quaternion.Inverse(parentBind.rotation);
            Vector3 side = root.right;
            Transform left = Find(transforms, "Thigh.L");
            Transform right = Find(transforms, "Thigh.R");
            if (left != null && right != null && bind.ContainsKey(left) && bind.ContainsKey(right))
                side = ((Vector3)bind[left].GetColumn(3) - (Vector3)bind[right].GetColumn(3)).normalized;
            side = inverseBody * side;

            float totalLength = 0f;
            for (int i = 0; i < tail.Length; i++)
            {
                Matrix4x4 currentBind = bind[tail[i]];
                restRotation[i] = inverseBody * currentBind.rotation;
                Vector3 tangent;
                if (i < tail.Length - 1)
                {
                    tangent = (Vector3)bind[tail[i + 1]].GetColumn(3) - (Vector3)currentBind.GetColumn(3);
                    distance[i] = totalLength;
                    totalLength += tangent.magnitude;
                }
                else
                {
                    tangent = (Vector3)currentBind.GetColumn(3) - (Vector3)bind[tail[i - 1]].GetColumn(3);
                    distance[i] = totalLength;
                }
                tangent = (inverseBody * tangent).normalized;
                bendAxis[i] = Vector3.Cross(tangent, side).normalized;
                if (bendAxis[i].sqrMagnitude < 0.5f)
                {
                    Debug.LogError("SquirrelTailWave found a tail segment parallel to the side axis.", this);
                    return false;
                }
            }
            if (totalLength < 0.0001f)
                return false;
            for (int i = 0; i < tail.Length; i++)
                distance[i] /= totalLength;

            initialized = true;
            return true;
        }

        private void LateUpdate()
        {
            EvaluateAt(Time.time);
        }

        public void EvaluateAt(float seconds)
        {
            if (!initialized)
                return;

            Quaternion bodyRotation = bodyParent.rotation;
            float phase = seconds * frequency * 2f * Mathf.PI + phaseOffset;
            for (int i = 0; i < tail.Length; i++)
            {
                float along = distance[i];
                float amplitude = Mathf.Lerp(rootAmplitudeDegrees, amplitudeDegrees, Mathf.Sin(along * Mathf.PI * 0.5f));
                float angle = amplitude * Mathf.Sin(phase - along * wavesAlongTail * 2f * Mathf.PI);
                // Absolute bind-relative targets replace generated tail motion without accumulating rotations.
                tail[i].rotation = bodyRotation * Quaternion.AngleAxis(angle, bendAxis[i]) * restRotation[i];
            }
        }

        private static Transform Find(Transform[] transforms, string boneName)
        {
            foreach (Transform candidate in transforms)
                if (candidate.name == boneName)
                    return candidate;
            return null;
        }
    }
}
