using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using Object = UnityEngine.Object;

namespace WeatherHouseSquirrelLab.Editor
{
    public static class SquirrelWorkbenchMotionChecks
    {
        [Serializable] sealed class Result
        {
            public bool passed, usesClone, strideApplicable, finite = true, noAccumulation, deterministic, feetProtected, boneLengthsPreserved, scaleBounded;
            public float lowFootSpan, highFootSpan, lowHandSpan, highHandSpan, bodySpringDifferenceDegrees, bodyStretchDifference, tailSpringDifferenceDegrees;
            public float maximumRepeatPositionError, maximumRepeatRotationComponentError, maximumRepeatScaleError, minimumFootClearance = float.PositiveInfinity, maximumLegLengthError, minimumRelativeSpineScale = 1f, maximumRelativeSpineScale = 1f, maximumBodyLagDegrees;
            public double seconds;
            public string clip, limitation = "Single prepared rig editor checks. Collision is a tail centerline/body-sphere approximation; subjective visual quality and mesh self-collision are not certified.";
            public string[] failures;
        }
        sealed class Pose
        {
            public Vector3[] positions, scales;
            public Quaternion[] rotations;
            public Pose(Transform[] bones) { positions = bones.Select(b => b.localPosition).ToArray(); scales = bones.Select(b => b.localScale).ToArray(); rotations = bones.Select(b => b.localRotation).ToArray(); }
            public void Restore(Transform[] bones) { for (int i = 0; i < bones.Length; i++) { bones[i].localPosition = positions[i]; bones[i].localRotation = rotations[i]; bones[i].localScale = scales[i]; } }
        }
        sealed class Samples
        {
            public readonly List<Vector3> feet = new List<Vector3>(), hands = new List<Vector3>();
            public readonly List<Quaternion> spine = new List<Quaternion>(), tail = new List<Quaternion>();
            public readonly List<Vector3> spineScale = new List<Vector3>();
        }

        public static void Run(GameObject root, AnimationClip clip, Action offsets = null)
        {
            if (root == null || clip == null) throw new ArgumentNullException("Motion checks require a preview root and clip.");
            DateTime started = DateTime.UtcNow;
            var result = new Result { clip = clip.name, usesClone = offsets == null };
            GameObject target = result.usesClone ? Object.Instantiate(root) : root;
            target.name = result.usesClone ? "Squirrel motion check clone" : root.name;
            if (result.usesClone) target.hideFlags = HideFlags.HideAndDontSave;
            Transform[] bones = target.GetComponentsInChildren<Transform>(true);
            Pose saved = new Pose(bones);
            SquirrelWorkbenchRig cloneRig = null;
            var mesh = new Mesh { hideFlags = HideFlags.HideAndDontSave };
            var failures = new List<string>();
            try
            {
                // An Action may capture the original root, so only the no-offset path uses a clone.
                if (result.usesClone) { cloneRig = new SquirrelWorkbenchRig(target); cloneRig.RestoreBindPose(); }
                foreach (Behaviour behaviour in target.GetComponentsInChildren<Behaviour>(true)) if (result.usesClone) behaviour.enabled = false;
                var skin = target.GetComponentsInChildren<SkinnedMeshRenderer>(true).OrderByDescending(r => r.sharedMesh.vertexCount).First();
                var map = bones.GroupBy(b => b.name).ToDictionary(g => g.Key, g => g.First());
                int[] footIds = Vertices(skin, name => name == "Foot.L"), handIds = Vertices(skin, name => name == "Hand.L" || name == "Forearm.L"), allFeet = Vertices(skin, name => name.StartsWith("Foot.", StringComparison.Ordinal));
                if (footIds.Length == 0 || handIds.Length == 0 || allFeet.Length == 0) throw new InvalidOperationException("Missing hand or foot skin islands for motion checks.");
                float floor = skin.sharedMesh.vertices.Min(v => skin.transform.TransformPoint(v).y);
                float height = skin.sharedMesh.vertices.Max(v => skin.transform.TransformPoint(v).y) - floor;
                float legLength = Vector3.Distance(map["Thigh.L"].position, map["Shin.L"].position) + Vector3.Distance(map["Shin.L"].position, map["Foot.L"].position);
                Vector3 referenceScale = map["Spine"].localScale;
                using (var motion = new SquirrelWorkbenchMotion(target, clip) { ApplyRigOffsets = offsets })
                {
                    result.strideApplicable = motion.IsLocomotion;
                    Func<SquirrelWorkbenchParameters, int, Samples> sample = (parameters, frames) =>
                    {
                        motion.ResetPhysics();
                        var samples = new Samples();
                        float dt = clip.length / (frames - 1);
                        for (int f = 0; f < frames; f++)
                        {
                            motion.Evaluate(dt * f, dt, parameters, f == 0);
                            skin.BakeMesh(mesh, false);
                            Matrix4x4 world = Matrix4x4.TRS(skin.transform.position, skin.transform.rotation, Vector3.one);
                            Vector3[] points = mesh.vertices.Select(v => world.MultiplyPoint3x4(v)).ToArray();
                            samples.feet.Add(Centroid(points, footIds) - map["Pelvis"].position);
                            samples.hands.Add(Centroid(points, handIds) - map["Pelvis"].position);
                            samples.spine.Add(map["Spine"].localRotation); samples.tail.Add(map["Tail08"].rotation); samples.spineScale.Add(map["Spine"].localScale);
                            result.minimumFootClearance = Mathf.Min(result.minimumFootClearance, allFeet.Min(v => points[v].y) - floor);
                            float actualLength = Vector3.Distance(map["Thigh.L"].position, map["Shin.L"].position) + Vector3.Distance(map["Shin.L"].position, map["Foot.L"].position);
                            result.maximumLegLengthError = Mathf.Max(result.maximumLegLengthError, Mathf.Abs(actualLength - legLength));
                            for (int axis = 0; axis < 3; axis++)
                            {
                                float relativeScale = map["Spine"].localScale[axis] / referenceScale[axis];
                                result.minimumRelativeSpineScale = Mathf.Min(result.minimumRelativeSpineScale, relativeScale);
                                result.maximumRelativeSpineScale = Mathf.Max(result.maximumRelativeSpineScale, relativeScale);
                            }
                            result.finite &= bones.All(b => Finite(b.localPosition) && Finite(b.localScale) && Finite(b.localRotation));
                        }
                        return samples;
                    };
                    var lowStride = Neutral(); lowStride.strideEnabled = motion.IsLocomotion; lowStride.strideLength = .18f; lowStride.legAmplitude = .5f;
                    var highStride = Neutral(); highStride.strideEnabled = motion.IsLocomotion; highStride.strideLength = .72f; highStride.legAmplitude = 1.8f;
                    result.lowFootSpan = Span(sample(lowStride, 48).feet); result.highFootSpan = Span(sample(highStride, 48).feet);
                    var lowArm = Neutral(); lowArm.armAmplitude = .45f;
                    var highArm = Neutral(); highArm.armAmplitude = 2.3f;
                    result.lowHandSpan = Span(sample(lowArm, 48).hands); result.highHandSpan = Span(sample(highArm, 48).hands);
                    var soft = new SquirrelWorkbenchParameters { bodyStiffness = 25f, bodyDamping = 2f, tailStiffness = 8f, tailDamping = 1f, collisionEnabled = false };
                    var firm = new SquirrelWorkbenchParameters { bodyStiffness = 300f, bodyDamping = 32f, tailStiffness = 240f, tailDamping = 28f, collisionEnabled = false };
                    Samples slow = sample(soft, 64), fast = sample(firm, 64);
                    for (int i = 0; i < slow.spine.Count; i++)
                    {
                        result.bodySpringDifferenceDegrees = Mathf.Max(result.bodySpringDifferenceDegrees, Quaternion.Angle(slow.spine[i], fast.spine[i]));
                        result.tailSpringDifferenceDegrees = Mathf.Max(result.tailSpringDifferenceDegrees, Quaternion.Angle(slow.tail[i], fast.tail[i]));
                        result.bodyStretchDifference = Mathf.Max(result.bodyStretchDifference, Vector3.Distance(slow.spineScale[i], fast.spineScale[i]));
                    }
                    var rigid = new SquirrelWorkbenchParameters { bodyElasticity = false, collisionEnabled = false };
                    var elastic = new SquirrelWorkbenchParameters { bodyElasticity = true, collisionEnabled = false };
                    Samples rigidMotion = sample(rigid, 48), elasticMotion = sample(elastic, 48);
                    for (int i = 0; i < rigidMotion.spine.Count; i++)
                        result.maximumBodyLagDegrees = Mathf.Max(result.maximumBodyLagDegrees, Quaternion.Angle(rigidMotion.spine[i], elasticMotion.spine[i]));
                    sample(new SquirrelWorkbenchParameters { bodyAmplitude = 2.5f, armAmplitude = 3f, bodyDamping = 0f, tailDamping = 0f, tailAmplitude = 65f, strideLength = .82f }, 48);
                    var normal = new SquirrelWorkbenchParameters();
                    sample(normal, 48); Pose first = new Pose(bones);
                    sample(normal, 48); Pose second = new Pose(bones);
                    Compare(first, second, result);
                    result.deterministic = result.maximumRepeatPositionError < .000001f && result.maximumRepeatRotationComponentError < .000001f && result.maximumRepeatScaleError < .000001f;
                    motion.Evaluate(clip.length * .37f, 0f, normal, true); Pose fixedPose = new Pose(bones);
                    for (int repeat = 0; repeat < 12; repeat++) motion.Evaluate(clip.length * .37f, 0f, normal, true);
                    Compare(fixedPose, new Pose(bones), result);
                    result.noAccumulation = result.maximumRepeatPositionError < .000001f && result.maximumRepeatRotationComponentError < .000001f && result.maximumRepeatScaleError < .000001f;
                    result.feetProtected = result.minimumFootClearance >= -.0002f;
                    result.boneLengthsPreserved = result.maximumLegLengthError < .00001f;
                    result.scaleBounded = result.minimumRelativeSpineScale >= .89f && result.maximumRelativeSpineScale <= 1.11f;
                    if (result.highFootSpan <= result.lowFootSpan + height * .035f) failures.Add("Foot skin motion did not respond visibly to the leg/stride control.");
                    if (result.highHandSpan <= result.lowHandSpan + height * .02f) failures.Add("Hand skin motion did not respond visibly to arm amplitude.");
                    if (result.bodySpringDifferenceDegrees < .5f && result.bodyStretchDifference < .005f) failures.Add("Body spring settings did not alter the trajectory.");
                    if (result.tailSpringDifferenceDegrees < 2f) failures.Add("Tail spring settings did not alter the trajectory.");
                    if (!result.finite) failures.Add("Non-finite pose detected.");
                    if (!result.deterministic || !result.noAccumulation) failures.Add("Reset/replay changed the same pose or accumulated transforms.");
                    if (!result.feetProtected) failures.Add("A foot skin island crossed the binding floor.");
                    if (!result.boneLengthsPreserved) failures.Add("Leg bone length changed.");
                    if (!result.scaleBounded) failures.Add("Torso stretch exceeded its 10% safety bound.");
                    if (result.maximumBodyLagDegrees > 28.1f) failures.Add("Body spring angular lag exceeded its 28 degree safety bound.");
                }
            }
            catch (Exception error) { failures.Add(error.ToString()); }
            finally
            {
                if (result.usesClone) { cloneRig?.Dispose(); Object.DestroyImmediate(target); }
                else saved.Restore(bones);
                Object.DestroyImmediate(mesh);
                result.seconds = (DateTime.UtcNow - started).TotalSeconds;
                result.failures = failures.ToArray(); result.passed = failures.Count == 0;
                Directory.CreateDirectory("SquirrelLabReview");
                File.WriteAllText("SquirrelLabReview/workbench_motion_checks.json", JsonUtility.ToJson(result, true));
            }
            if (!result.passed) throw new InvalidOperationException("Workbench motion checks failed: " + string.Join(" | ", failures));
            Debug.Log("Workbench motion checks passed; see SquirrelLabReview/workbench_motion_checks.json");
        }

        static SquirrelWorkbenchParameters Neutral() { return new SquirrelWorkbenchParameters { bodyAmplitude = 1f, armAmplitude = 1f, legAmplitude = 1f, strideEnabled = false, bodyElasticity = false, tailElasticity = false, tailAmplitude = 0f, collisionEnabled = false, weightShift = 0f, upperBody = 0f }; }
        static void Compare(Pose first, Pose second, Result result)
        {
            for (int i = 0; i < first.positions.Length; i++)
            {
                result.maximumRepeatPositionError = Mathf.Max(result.maximumRepeatPositionError, Vector3.Distance(first.positions[i], second.positions[i]));
                result.maximumRepeatScaleError = Mathf.Max(result.maximumRepeatScaleError, Vector3.Distance(first.scales[i], second.scales[i]));
                Quaternion a = first.rotations[i], b = second.rotations[i];
                result.maximumRepeatRotationComponentError = Mathf.Max(result.maximumRepeatRotationComponentError, Vector4.Distance(new Vector4(a.x, a.y, a.z, a.w), new Vector4(b.x, b.y, b.z, b.w)));
            }
        }
        static int[] Vertices(SkinnedMeshRenderer skin, Func<string, bool> predicate)
        {
            var ids = new HashSet<int>(skin.bones.Select((b, i) => new { b, i }).Where(v => predicate(v.b.name)).Select(v => v.i));
            return skin.sharedMesh.boneWeights.Select((w, i) => new { w, i }).Where(v =>
                (ids.Contains(v.w.boneIndex0) ? v.w.weight0 : 0f) + (ids.Contains(v.w.boneIndex1) ? v.w.weight1 : 0f) + (ids.Contains(v.w.boneIndex2) ? v.w.weight2 : 0f) + (ids.Contains(v.w.boneIndex3) ? v.w.weight3 : 0f) >= .3f).Select(v => v.i).ToArray();
        }
        static Vector3 Centroid(Vector3[] points, int[] vertices) { Vector3 result = Vector3.zero; foreach (int v in vertices) result += points[v]; return result / vertices.Length; }
        static float Span(List<Vector3> points) { float result = 0f; foreach (Vector3 a in points) foreach (Vector3 b in points) result = Mathf.Max(result, Vector3.Distance(a, b)); return result; }
        static bool Finite(float value) { return !float.IsNaN(value) && !float.IsInfinity(value); }
        static bool Finite(Vector3 v) { return Finite(v.x) && Finite(v.y) && Finite(v.z); }
        static bool Finite(Quaternion v) { return Finite(v.x) && Finite(v.y) && Finite(v.z) && Finite(v.w); }
    }
}
