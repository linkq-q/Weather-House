using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace WeatherHouseSquirrelLab.Editor
{
    public sealed class SquirrelWorkbenchMotion : IDisposable
    {
        public Action ApplyRigOffsets;
        public bool IsLocomotion { get; private set; }
        public float MaximumLegTargetError => stride.report.maximumTargetError;
        public float MinimumFootClearance { get; private set; }
        public int ApproximateTailContacts { get; private set; }

        readonly GameObject root;
        readonly AnimationClip clip;
        readonly Transform[] bones, tail, softBones;
        readonly Transform pelvis, spine, chest;
        readonly Quaternion[] meanRotation, sampledRotation, rigOffset, tailRestRotation;
        readonly Quaternion[] baseRotation;
        readonly Vector3[] basePosition, baseScale;
        readonly Vector3[] tailBendAxis;
        readonly float[] tailDistance;
        readonly ScalarSpring[] tailSprings;
        readonly RotationSpring[] bodySprings;
        readonly ScalarSpring stretchSpring = new ScalarSpring();
        readonly SkinnedMeshRenderer skin;
        readonly Mesh scratch;
        readonly SquirrelStrideBake stride;
        readonly int[] footVertices;
        readonly float ground, height;
        readonly int spineAxis;
        bool physicsReady, disposed;
        float previousTime = float.NaN;
        float tailClock;
        Vector3 lastCollisionCenter;
        float lastCollisionRadius, tailContactRadius;
        bool showCollision;

        public SquirrelWorkbenchMotion(GameObject previewRoot, AnimationClip sourceClip)
        {
            if (previewRoot == null || sourceClip == null) throw new ArgumentNullException("Preview root and source clip are required.");
            root = previewRoot; clip = sourceClip;
            bones = root.GetComponentsInChildren<Transform>(true).Where(t => t != root.transform).ToArray();
            basePosition = bones.Select(t => t.localPosition).ToArray();
            baseRotation = bones.Select(t => t.localRotation).ToArray();
            baseScale = bones.Select(t => t.localScale).ToArray();
            var map = bones.GroupBy(t => t.name).ToDictionary(g => g.Key, g => g.First());
            pelvis = map["Pelvis"]; spine = map["Spine"]; chest = map["Chest"];
            softBones = new[] { spine, chest, map["Head"] };
            tail = new[] { "TailRoot", "Tail02", "Tail03", "Tail04", "Tail05", "Tail06", "Tail07", "Tail08" }.Select(name => map[name]).ToArray();
            skin = root.GetComponentsInChildren<SkinnedMeshRenderer>(true).OrderByDescending(r => r.sharedMesh != null ? r.sharedMesh.vertexCount : 0).First();
            if (skin.sharedMesh == null) throw new InvalidOperationException("The preview has no skinned mesh.");
            string identity = (clip.name + " " + AssetDatabase.GetAssetPath(clip)).ToLowerInvariant();
            IsLocomotion = identity.Contains("walk") || identity.Contains("run") || identity.Contains("approach");
            scratch = new Mesh { name = "Squirrel workbench skin scratch", hideFlags = HideFlags.HideAndDontSave };
            stride = new SquirrelStrideBake(identity.Contains("run") ? "run_biped" : "walk_biped", bones, skin, scratch);
            footVertices = FindFootVertices();
            Vector3[] worldBind = skin.sharedMesh.vertices.Select(v => skin.transform.TransformPoint(v)).ToArray();
            ground = worldBind.Min(v => v.y);
            height = Mathf.Max(.001f, worldBind.Max(v => v.y) - ground);
            tailContactRadius = height * .035f;
            meanRotation = CalculateMeanRotations();
            sampledRotation = new Quaternion[bones.Length]; rigOffset = new Quaternion[bones.Length];
            bodySprings = softBones.Select(_ => new RotationSpring()).ToArray();
            tailSprings = tail.Select(_ => new ScalarSpring()).ToArray();
            tailRestRotation = new Quaternion[tail.Length]; tailBendAxis = new Vector3[tail.Length]; tailDistance = new float[tail.Length];
            InitializeTail();
            RestoreBase(); clip.SampleAnimation(root, 0f);
            Vector3 spineDirection = spine.InverseTransformPoint(chest.position);
            spineAxis = Mathf.Abs(spineDirection.y) > Mathf.Abs(spineDirection.x) ? 1 : 0;
            if (Mathf.Abs(spineDirection.z) > Mathf.Abs(spineDirection[spineAxis])) spineAxis = 2;
        }

        public void Evaluate(float time, float deltaTime, SquirrelWorkbenchParameters parameters, bool resetPhysics = false)
        {
            if (disposed) throw new ObjectDisposedException(nameof(SquirrelWorkbenchMotion));
            if (parameters == null) throw new ArgumentNullException(nameof(parameters));
            if (!Finite(time) || !Finite(deltaTime)) throw new ArgumentException("Preview time must be finite.");
            bool normalLoop = !float.IsNaN(previousTime) && previousTime > clip.length * .8f && time < clip.length * .2f && deltaTime > 0f;
            bool reset = resetPhysics || !physicsReady || deltaTime <= 0f || (!float.IsNaN(previousTime) && (!normalLoop && time < previousTime || time - previousTime > .3f));
            float dt = Mathf.Clamp(deltaTime, 0f, .1f);
            tailClock = reset ? time : tailClock + dt;
            float sampleTime = Mathf.Clamp(time, 0f, clip.length);
            // The owner advances/wraps the clock; sampling the exact end frame also supports deterministic export.
            RestoreBase(); clip.SampleAnimation(root, sampleTime);
            for (int i = 0; i < bones.Length; i++) sampledRotation[i] = bones[i].localRotation;
            ApplyRigOffsets?.Invoke();
            for (int i = 0; i < bones.Length; i++) rigOffset[i] = bones[i].localRotation * Quaternion.Inverse(sampledRotation[i]);
            Amplify(parameters);
            if (parameters.strideEnabled && IsLocomotion)
            {
                stride.Apply(clip.length > 0f ? sampleTime / clip.length : 0f, new SquirrelStrideBake.Settings
                {
                    strideLength = Safe(parameters.strideLength, .69f, .05f, .82f), footLift = Safe(parameters.footLift, .3f, 0f, .6f),
                    hipBounce = Safe(parameters.hipBounce, .12f, 0f, .18f), weightShift = Safe(parameters.weightShift, 1f, 0f, 2f),
                    upperBody = Safe(parameters.upperBody, 1f, 0f, 2f)
                });
            }
            ApplyBodySpring(sampleTime, dt, parameters, reset);
            ProtectFeet();
            ApplyTail(tailClock, dt, parameters, reset);
            showCollision = parameters.collisionEnabled;
            lastCollisionCenter = Vector3.Lerp(pelvis.position, chest.position, .38f);
            lastCollisionRadius = height * Safe(parameters.collisionRadius, .2f, .05f, .4f);
            ApproximateTailContacts = 0;
            if (showCollision) ConstrainTail();
            if (bones.Any(t => !Finite(t.localPosition) || !Finite(t.localRotation) || !Finite(t.localScale)))
            {
                ResetPhysics();
                RestoreBase(); clip.SampleAnimation(root, sampleTime);
                throw new InvalidOperationException("A non-finite preview pose was rejected and restored to the source pose.");
            }
            physicsReady = true; previousTime = time;
        }

        public void ResetPhysics()
        {
            physicsReady = false; previousTime = float.NaN; tailClock = 0f;
            stretchSpring.Reset(0f);
            foreach (var spring in tailSprings) spring.Reset(0f);
            foreach (var spring in bodySprings) spring.Reset(Quaternion.identity);
        }

        void Amplify(SquirrelWorkbenchParameters p)
        {
            for (int i = 0; i < bones.Length; i++)
            {
                string name = bones[i].name;
                bool arm = name.StartsWith("UpperArm", StringComparison.Ordinal) || name.StartsWith("Forearm", StringComparison.Ordinal);
                bool leg = name.StartsWith("Thigh", StringComparison.Ordinal) || name.StartsWith("Shin", StringComparison.Ordinal);
                bool body = name == "Pelvis" || name == "Spine" || name == "Chest";
                if (!arm && !leg && !body) continue;
                // IK controls leg reach directly when enabled; angular amplification remains available in source-pose mode.
                float gain = arm ? Safe(p.armAmplitude, 1.6f, 0f, 3f) : leg ? (p.strideEnabled && IsLocomotion ? 1f : Safe(p.legAmplitude, 1.2f, 0f, 2f)) : Safe(p.bodyAmplitude, 1.35f, 0f, 2.5f);
                Quaternion delta = Quaternion.Inverse(meanRotation[i]) * sampledRotation[i];
                float angle = Quaternion.Angle(Quaternion.identity, delta);
                if (angle > .001f) gain = Mathf.Min(gain, 1f + 40f / angle);
                bones[i].localRotation = rigOffset[i] * meanRotation[i] * Quaternion.SlerpUnclamped(Quaternion.identity, delta, gain);
            }
        }

        void ApplyBodySpring(float time, float dt, SquirrelWorkbenchParameters p, bool reset)
        {
            float stiffness = Safe(p.bodyStiffness, 130f, 20f, 400f), damping = Safe(p.bodyDamping, 16f, 0f, 50f);
            for (int i = 0; i < softBones.Length; i++)
            {
                Quaternion target = softBones[i].localRotation;
                if (reset || !p.bodyElasticity) bodySprings[i].Reset(target);
                else bodySprings[i].Step(target, dt, stiffness * (i == 2 ? .7f : 1f), damping, 28f);
                if (p.bodyElasticity) softBones[i].localRotation = bodySprings[i].value;
            }
            float phase = clip.length > .001f ? time * Mathf.PI * 4f / clip.length : 0f;
            float targetStretch = p.bodyElasticity ? Mathf.Clamp(.045f * Safe(p.bodyAmplitude, 1.35f, 0f, 2.5f) * Mathf.Sin(phase * 2f), -.10f, .10f) : 0f;
            if (reset || !p.bodyElasticity) stretchSpring.Reset(targetStretch);
            else stretchSpring.Step(targetStretch, dt, stiffness, damping, -.10f, .10f);
            float stretch = 1f + stretchSpring.value;
            Vector3 scale = spine.localScale * (1f / Mathf.Sqrt(stretch));
            scale[spineAxis] = spine.localScale[spineAxis] * stretch;
            spine.localScale = scale;
        }

        void ProtectFeet()
        {
            Vector3[] points = BakeWorldPoints();
            float lowest = footVertices.Min(v => points[v].y);
            if (lowest < ground + .003f) pelvis.position += Vector3.up * (ground + .003f - lowest);
            MinimumFootClearance = Mathf.Max(lowest, ground + .003f) - ground;
        }

        void InitializeTail()
        {
            var bind = skin.bones.Select((bone, i) => new { bone, matrix = skin.transform.localToWorldMatrix * skin.sharedMesh.bindposes[i].inverse }).ToDictionary(v => v.bone, v => v.matrix);
            Quaternion inverseBody = Quaternion.Inverse(bind[pelvis].rotation);
            Transform left = bones.First(b => b.name == "Thigh.L"), right = bones.First(b => b.name == "Thigh.R");
            Vector3 side = inverseBody * ((Vector3)bind[left].GetColumn(3) - (Vector3)bind[right].GetColumn(3)).normalized;
            float distance = 0f;
            for (int i = 0; i < tail.Length; i++)
            {
                tailRestRotation[i] = inverseBody * bind[tail[i]].rotation;
                Vector3 tangent = i < tail.Length - 1 ? (Vector3)bind[tail[i + 1]].GetColumn(3) - (Vector3)bind[tail[i]].GetColumn(3)
                    : (Vector3)bind[tail[i]].GetColumn(3) - (Vector3)bind[tail[i - 1]].GetColumn(3);
                tailDistance[i] = distance;
                if (i < tail.Length - 1) distance += tangent.magnitude;
                tailBendAxis[i] = Vector3.Cross((inverseBody * tangent).normalized, side).normalized;
            }
            if (distance < .00001f) throw new InvalidOperationException("The tail chain has no usable length.");
            for (int i = 0; i < tail.Length; i++) tailDistance[i] /= distance;
        }

        void ApplyTail(float time, float dt, SquirrelWorkbenchParameters p, bool reset)
        {
            float amplitude = Safe(p.tailAmplitude, 44f, 0f, 65f), frequency = Safe(p.tailFrequency, .65f, .05f, 2f);
            float stiffness = Safe(p.tailStiffness, 65f, 5f, 300f), damping = Safe(p.tailDamping, 9f, 0f, 40f);
            for (int i = 0; i < tail.Length; i++)
            {
                float along = tailDistance[i];
                float target = Mathf.Lerp(amplitude * .14f, amplitude, Mathf.Sin(along * Mathf.PI * .5f))
                    * Mathf.Sin(time * frequency * Mathf.PI * 2f - along * 1.15f * Mathf.PI * 2f);
                if (reset || !p.tailElasticity) tailSprings[i].Reset(target);
                else tailSprings[i].Step(target, dt, stiffness * Mathf.Lerp(1f, .6f, along), damping, -72f, 72f);
                tail[i].rotation = pelvis.rotation * Quaternion.AngleAxis(tailSprings[i].value, tailBendAxis[i]) * tailRestRotation[i];
                int boneIndex = Array.IndexOf(bones, tail[i]);
                tail[i].localRotation = rigOffset[boneIndex] * tail[i].localRotation;
            }
        }

        void ConstrainTail()
        {
            // Approximate centerline spheres only: root attachment is exempt and mesh/self collision is not solved.
            for (int pass = 0; pass < 3; pass++)
            {
                for (int i = 2; i < tail.Length; i++)
                {
                    Transform parent = tail[i - 1];
                    Vector3 point = tail[i].position, target = point;
                    Vector3 fromBody = target - lastCollisionCenter;
                    float clearance = lastCollisionRadius + tailContactRadius;
                    if (fromBody.magnitude < clearance)
                    {
                        if (fromBody.sqrMagnitude < .000001f) fromBody = (parent.position - lastCollisionCenter).normalized;
                        target = lastCollisionCenter + fromBody.normalized * clearance;
                    }
                    target.y = Mathf.Max(target.y, ground + tailContactRadius);
                    if ((target - point).sqrMagnitude < .0000001f) continue;
                    Quaternion desired = Quaternion.FromToRotation(point - parent.position, target - parent.position) * parent.rotation;
                    parent.rotation = Quaternion.RotateTowards(parent.rotation, desired, 20f);
                    ApproximateTailContacts++;
                }
            }
        }

        public void DrawGizmos()
        {
            if (disposed || !showCollision) return;
            Color previous = Handles.color;
            Handles.color = new Color(1f, .64f, .15f, .7f);
            foreach (Vector3 axis in new[] { Vector3.up, Vector3.right, Vector3.forward }) Handles.DrawWireDisc(lastCollisionCenter, axis, lastCollisionRadius);
            Handles.color = new Color(.2f, .9f, .8f, .65f);
            for (int i = 1; i < tail.Length; i++) { Handles.DrawLine(tail[i - 1].position, tail[i].position); Handles.DrawWireDisc(tail[i].position, Vector3.up, tailContactRadius); }
            Handles.color = previous;
        }

        Quaternion[] CalculateMeanRotations()
        {
            var samples = new List<Quaternion>[bones.Length];
            for (int i = 0; i < samples.Length; i++) samples[i] = new List<Quaternion>();
            for (int frame = 0; frame < 48; frame++)
            {
                RestoreBase(); clip.SampleAnimation(root, clip.length * frame / 47f);
                for (int i = 0; i < bones.Length; i++) samples[i].Add(bones[i].localRotation);
            }
            return samples.Select(values =>
            {
                Vector4 sum = Vector4.zero; Quaternion first = values[0];
                foreach (Quaternion q in values) sum += new Vector4(q.x, q.y, q.z, q.w) * (Quaternion.Dot(first, q) < 0f ? -1f : 1f);
                sum.Normalize(); return new Quaternion(sum.x, sum.y, sum.z, sum.w);
            }).ToArray();
        }
        int[] FindFootVertices()
        {
            var ids = new HashSet<int>(skin.bones.Select((bone, i) => new { bone, i }).Where(v => v.bone.name.StartsWith("Foot.", StringComparison.Ordinal)).Select(v => v.i));
            return skin.sharedMesh.boneWeights.Select((w, i) => new { w, i }).Where(v =>
                (ids.Contains(v.w.boneIndex0) ? v.w.weight0 : 0f) + (ids.Contains(v.w.boneIndex1) ? v.w.weight1 : 0f)
                + (ids.Contains(v.w.boneIndex2) ? v.w.weight2 : 0f) + (ids.Contains(v.w.boneIndex3) ? v.w.weight3 : 0f) >= .3f).Select(v => v.i).ToArray();
        }
        Vector3[] BakeWorldPoints()
        {
            skin.BakeMesh(scratch, false);
            Matrix4x4 matrix = Matrix4x4.TRS(skin.transform.position, skin.transform.rotation, Vector3.one);
            return scratch.vertices.Select(v => matrix.MultiplyPoint3x4(v)).ToArray();
        }
        void RestoreBase()
        {
            for (int i = 0; i < bones.Length; i++)
            {
                bones[i].localPosition = basePosition[i]; bones[i].localRotation = baseRotation[i]; bones[i].localScale = baseScale[i];
            }
        }
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            UnityEngine.Object.DestroyImmediate(scratch);
        }
        static float Safe(float value, float fallback, float min, float max) { return Mathf.Clamp(Finite(value) ? value : fallback, min, max); }
        static bool Finite(float value) { return !float.IsNaN(value) && !float.IsInfinity(value); }
        static bool Finite(Vector3 value) { return Finite(value.x) && Finite(value.y) && Finite(value.z); }
        static bool Finite(Quaternion value) { return Finite(value.x) && Finite(value.y) && Finite(value.z) && Finite(value.w); }

        sealed class ScalarSpring
        {
            public float value;
            float velocity;
            public void Reset(float target) { value = target; velocity = 0f; }
            public void Step(float target, float dt, float stiffness, float damping, float min, float max)
            {
                int steps = Mathf.Max(1, Mathf.CeilToInt(dt * 120f)); float h = dt / steps;
                for (int i = 0; i < steps; i++) { velocity += ((target - value) * stiffness - velocity * damping) * h; value += velocity * h; }
                float clamped = Mathf.Clamp(value, min, max);
                if (clamped != value) velocity = 0f;
                value = clamped;
            }
        }
        sealed class RotationSpring
        {
            public Quaternion value = Quaternion.identity;
            Vector3 velocity;
            public void Reset(Quaternion target) { value = target; velocity = Vector3.zero; }
            public void Step(Quaternion target, float dt, float stiffness, float damping, float limit)
            {
                int steps = Mathf.Max(1, Mathf.CeilToInt(dt * 120f)); float h = dt / steps;
                for (int i = 0; i < steps; i++)
                {
                    Quaternion error = target * Quaternion.Inverse(value);
                    error.ToAngleAxis(out float angle, out Vector3 axis);
                    if (angle > 180f) angle -= 360f;
                    if (axis.sqrMagnitude < .000001f || !Finite(axis)) axis = Vector3.up;
                    velocity = Vector3.ClampMagnitude(velocity + (axis.normalized * (angle * Mathf.Deg2Rad * stiffness) - velocity * damping) * h, 7f);
                    float speed = velocity.magnitude;
                    if (speed > .00001f) value = Quaternion.AngleAxis(speed * h * Mathf.Rad2Deg, velocity / speed) * value;
                    value = Quaternion.RotateTowards(target, value, limit);
                }
            }
        }
    }
}
