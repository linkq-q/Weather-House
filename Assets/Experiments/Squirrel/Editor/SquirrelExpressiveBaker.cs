using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace WeatherHouseSquirrelLab.Editor
{
    public static class SquirrelExpressiveBaker
    {
        private const int Frames = 120;
        private const string Folder = SquirrelLabBuilder.Generated + "/Expressive";
        private static readonly string[] MeasuredBones = { "UpperArm.L", "UpperArm.R", "Forearm.L", "Forearm.R", "Thigh.L", "Thigh.R", "Shin.L", "Shin.R" };
        private static readonly string[] Endpoints = { "Hand.L", "Hand.R", "Foot.L", "Foot.R" };

        [Serializable] private sealed class JointReport { public string bone; public float originalDegrees, expressiveDegrees; }
        [Serializable] private sealed class EndpointReport { public string bone; public float originalSpan, expressiveSpan; }
        [Serializable] private sealed class ClipReport
        {
            public string id, originalClip, outputClip;
            public int frames;
            public float seconds, minTorsoScale = 1f, maxTorsoScale = 1f, maximumFootFloorDelta, maximumRootCorrection, maximumLocalBoneLengthDelta, footCoordinateCalibrationError;
            public float leftArmBiasDegrees, rightArmBiasDegrees, minimumOutwardDotGain;
            public bool finite = true, armsAmplified, legsAmplified, passed;
            public JointReport[] joints;
            public EndpointReport[] endpoints;
        }
        [Serializable] private sealed class Report
        {
            public bool passed;
            public string note = "Modified animation copies; tail rotations remain original and receive the existing runtime S-wave. Foot correction preserves each source frame's lowest foot vertex, including jumps. Local bone lengths are preserved; torso scale intentionally changes global lengths. No full-body collision solver.";
            public ClipReport[] clips;
        }

        private sealed class Pose
        {
            public Vector3[] position, scale;
            public Quaternion[] rotation;
            public Pose(int count) { position = new Vector3[count]; scale = new Vector3[count]; rotation = new Quaternion[count]; }
        }

        [MenuItem("Weather House/Squirrel Lab/Bake Expressive Motion Copies")]
        public static void Build()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play before baking expressive clips.");
            Scene scene = SceneManager.GetSceneByPath(SquirrelLabBuilder.ScenePath);
            if (!scene.IsValid()) scene = EditorSceneManager.OpenScene(SquirrelLabBuilder.ScenePath, OpenSceneMode.Additive);
            if (scene.isDirty) throw new InvalidOperationException("Save the gallery scene before baking expressive clips.");
            SquirrelMotionGallery gallery = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<SquirrelMotionGallery>(true)).Single();
            Directory.CreateDirectory(Folder);
            AssetDatabase.Refresh();
            var report = new Report { clips = new ClipReport[gallery.actors.Length] };
            for (int i = 0; i < gallery.actors.Length; i++) report.clips[i] = Bake(gallery.actors[i]);
            report.passed = report.clips.All(clip => clip.passed);
            File.WriteAllText(Folder + "/validation.json", JsonUtility.ToJson(report, true));
            AssetDatabase.SaveAssets();
            if (!report.passed) throw new InvalidOperationException("Expressive animation validation failed; see " + Folder + "/validation.json");
            EditorUtility.SetDirty(gallery);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("Expressive squirrel clips baked and validated: " + Folder);
        }

        private static ClipReport Bake(SquirrelMotionGallery.Actor actor)
        {
            if (actor.animator == null || actor.clip == null) throw new InvalidOperationException("Missing source actor or clip: " + actor.id);
            GameObject clone = Object.Instantiate(actor.animator.gameObject);
            clone.name = actor.animator.gameObject.name;
            clone.hideFlags = HideFlags.HideAndDontSave;
            clone.transform.SetPositionAndRotation(actor.animator.transform.position, actor.animator.transform.rotation);
            clone.transform.localScale = actor.animator.transform.lossyScale;
            foreach (Behaviour behaviour in clone.GetComponentsInChildren<Behaviour>(true)) behaviour.enabled = false;
            var bakedMesh = new Mesh();
            try
            {
                Transform[] bones = clone.GetComponentsInChildren<Transform>(true).Where(bone => bone != clone.transform).ToArray();
                var indices = bones.Select((bone, index) => new { bone.name, index }).GroupBy(pair => pair.name).ToDictionary(group => group.Key, group => group.First().index);
                Transform pelvis = bones[indices["Pelvis"]];
                Transform spine = bones[indices["Spine"]];
                SkinnedMeshRenderer skin = clone.GetComponentsInChildren<SkinnedMeshRenderer>(true).Single();
                int[] footVertices = FootVertices(skin);
                if (footVertices.Length < 20) throw new InvalidOperationException("Could not identify foot vertices for " + actor.id);
                Pose[] original = new Pose[Frames];
                Vector3[][] originalEnds = NewPoints();
                float[] originalFloor = new float[Frames];
                for (int f = 0; f < Frames; f++)
                {
                    actor.clip.SampleAnimation(clone, actor.clip.length * f / (Frames - 1));
                    original[f] = Capture(bones);
                    originalFloor[f] = FootFloor(skin, bakedMesh, footVertices);
                    CaptureEndpoints(originalEnds, f, bones, indices, pelvis);
                }
                Quaternion[] average = new Quaternion[bones.Length];
                for (int b = 0; b < bones.Length; b++) average[b] = Mean(original.Select(pose => pose.rotation[b]));
                Pose[] expressive = new Pose[Frames];
                Vector3[][] expressiveEnds = NewPoints();
                var report = new ClipReport { id = actor.id, originalClip = AssetDatabase.GetAssetPath(actor.clip), outputClip = Folder + "/" + actor.id + ".anim", frames = Frames, seconds = actor.clip.length };
                if (actor.id == "walk_biped" || actor.id == "wave")
                {
                    report.leftArmBiasDegrees = actor.id == "walk_biped" ? 12f : 8f;
                    report.rightArmBiasDegrees = actor.id == "walk_biped" ? 12f : 34f;
                    report.minimumOutwardDotGain = 1f;
                }
                Restore(bones, original[0]);
                pelvis.position += Vector3.up * .01f;
                report.footCoordinateCalibrationError = Mathf.Abs(FootFloor(skin, bakedMesh, footVertices) - originalFloor[0] - .01f);
                if (report.footCoordinateCalibrationError > .0001f)
                    throw new InvalidOperationException("Foot mesh coordinate calibration failed for " + actor.id + ": " + report.footCoordinateCalibrationError);
                for (int f = 0; f < Frames; f++)
                {
                    Restore(bones, original[f]);
                    float phase = 2f * Mathf.PI * f / (Frames - 1);
                    for (int b = 0; b < bones.Length; b++)
                    {
                        float gain = Gain(actor.id, bones[b].name);
                        if (gain == 1f) continue;
                        Quaternion delta = Quaternion.Inverse(average[b]) * original[f].rotation[b];
                        float angle = Quaternion.Angle(Quaternion.identity, delta);
                        float limitedGain = angle < .001f ? gain : Mathf.Min(gain, 1f + 18f / angle);
                        bones[b].localRotation = average[b] * Quaternion.SlerpUnclamped(Quaternion.identity, delta, limitedGain);
                    }

                    // Body axes are measured from this pose; imported FBX local axes are not assumed.
                    Vector3 lateral = (bones[indices["Thigh.R"]].position - bones[indices["Thigh.L"]].position).normalized;
                    Vector3 forward = Vector3.Cross(lateral, Vector3.up).normalized;
                    if (report.leftArmBiasDegrees > 0f)
                    {
                        Transform leftArm = bones[indices["UpperArm.L"]];
                        Transform rightArm = bones[indices["UpperArm.R"]];
                        Vector3 leftOutward = (leftArm.position - rightArm.position).normalized;
                        float leftGain = OpenArm(leftArm, bones[indices["Forearm.L"]], leftOutward, report.leftArmBiasDegrees);
                        float rightGain = OpenArm(rightArm, bones[indices["Forearm.R"]], -leftOutward, report.rightArmBiasDegrees);
                        report.minimumOutwardDotGain = Mathf.Min(report.minimumOutwardDotGain, leftGain, rightGain);
                    }
                    if (actor.id == "wave")
                    {
                        RotateWorld(bones[indices["UpperArm.R"]], forward, 15f * Mathf.Sin(phase * 2f));
                        RotateWorld(bones[indices["UpperArm.L"]], forward, -8f * Mathf.Sin(phase * 2f));
                    }
                    if (actor.id == "dance")
                        RotateWorld(bones[indices["UpperArm.L"]], forward, -18f * Mathf.Sin(phase * 2f));

                    float cycles = actor.id == "run_biped" ? 4f : actor.id == "dance" ? 3f : actor.id == "jump_biped" ? 1f : 2f;
                    float stretch = 1f + .055f * Mathf.Sin(phase * cycles);
                    int stretchAxis = LongitudinalAxis(spine, bones[indices["Chest"]]);
                    Vector3 scale = original[f].scale[indices["Spine"]];
                    float radial = 1f / Mathf.Sqrt(stretch);
                    scale *= radial;
                    scale[stretchAxis] = original[f].scale[indices["Spine"]][stretchAxis] * stretch;
                    spine.localScale = scale;
                    report.minTorsoScale = Mathf.Min(report.minTorsoScale, stretch, radial);
                    report.maxTorsoScale = Mathf.Max(report.maxTorsoScale, stretch, radial);
                    // Small phase lag follows the existing motion without inventing a new head pose.
                    foreach (string name in new[] { "Chest", "Head" })
                    {
                        int b = indices[name];
                        int delay = name == "Head" ? 5 : 2;
                        Quaternion lag = original[(f - delay + Frames - 1) % (Frames - 1)].rotation[b];
                        bones[b].localRotation = Quaternion.Slerp(bones[b].localRotation, lag, .3f);
                    }

                    float correction = originalFloor[f] - FootFloor(skin, bakedMesh, footVertices);
                    pelvis.position += Vector3.up * correction;
                    report.maximumRootCorrection = Mathf.Max(report.maximumRootCorrection, Mathf.Abs(correction));
                    report.maximumFootFloorDelta = Mathf.Max(report.maximumFootFloorDelta, Mathf.Abs(originalFloor[f] - FootFloor(skin, bakedMesh, footVertices)));
                    expressive[f] = Capture(bones);
                    CaptureEndpoints(expressiveEnds, f, bones, indices, pelvis);
                    for (int b = 0; b < bones.Length; b++)
                    {
                        report.finite &= IsFinite(expressive[f].position[b]) && IsFinite(expressive[f].scale[b]) && IsFinite(expressive[f].rotation[b]);
                        if (bones[b] != pelvis)
                            report.maximumLocalBoneLengthDelta = Mathf.Max(report.maximumLocalBoneLengthDelta, Vector3.Distance(original[f].position[b], expressive[f].position[b]));
                    }
                }

                report.joints = MeasuredBones.Select(name => new JointReport
                {
                    bone = name, originalDegrees = AngularSpan(original, indices[name]), expressiveDegrees = AngularSpan(expressive, indices[name])
                }).ToArray();
                report.endpoints = Endpoints.Select((name, index) => new EndpointReport
                {
                    bone = name, originalSpan = Span(originalEnds[index]), expressiveSpan = Span(expressiveEnds[index])
                }).ToArray();
                report.armsAmplified = report.joints.Take(4).Any(joint => joint.expressiveDegrees >= joint.originalDegrees * 1.03f + .25f);
                report.legsAmplified = report.joints.Skip(4).Any(joint => joint.expressiveDegrees >= joint.originalDegrees * 1.03f + .25f);
                report.passed = report.finite && report.armsAmplified && report.legsAmplified && report.maximumFootFloorDelta < .0002f && report.maximumLocalBoneLengthDelta < .00001f && report.minTorsoScale >= .94f && report.maxTorsoScale <= 1.06f;
                AnimationClip output = MakeClip(actor, bones, clone.transform, expressive);
                AnimationClip existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(report.outputClip);
                if (existing == null) AssetDatabase.CreateAsset(output, report.outputClip);
                else { EditorUtility.CopySerialized(output, existing); Object.DestroyImmediate(output); output = existing; }
                actor.expressiveClip = output;
                return report;
            }
            finally { Object.DestroyImmediate(bakedMesh); Object.DestroyImmediate(clone); }
        }

        private static float Gain(string id, string bone)
        {
            bool arm = bone.StartsWith("UpperArm", StringComparison.Ordinal) || bone.StartsWith("Forearm", StringComparison.Ordinal);
            bool leg = bone.StartsWith("Thigh", StringComparison.Ordinal) || bone.StartsWith("Shin", StringComparison.Ordinal);
            if (arm)
            {
                if (id == "walk_biped") return 1.85f;
                if (id == "run_biped") return 1.18f;
                if (id == "jump_biped") return 1.05f;
                if (id == "wave") return bone == "Forearm.R" ? 1.2f : 1.35f;
                if (id == "dance") return bone.EndsWith(".L", StringComparison.Ordinal) ? 1.45f : 1.12f;
                return 1.4f;
            }
            if (leg) return id == "jump_biped" ? 1.05f : id == "wave" ? 1.1f : 1.2f;
            if (bone == "Spine" || bone == "Chest") return 1.15f;
            return 1f;
        }

        private static AnimationClip MakeClip(SquirrelMotionGallery.Actor actor, Transform[] bones, Transform root, Pose[] poses)
        {
            var clip = new AnimationClip { name = actor.id + "_expressive", frameRate = (Frames - 1) / actor.clip.length, legacy = false };
            for (int b = 0; b < bones.Length; b++)
            {
                string path = AnimationUtility.CalculateTransformPath(bones[b], root);
                for (int property = 0; property < 10; property++)
                {
                    var keys = new Keyframe[Frames];
                    for (int f = 0; f < Frames; f++)
                    {
                        float value = property < 3 ? poses[f].position[b][property] : property < 7 ? poses[f].rotation[b][property - 3] : poses[f].scale[b][property - 7];
                        keys[f] = new Keyframe(actor.clip.length * f / (Frames - 1), value);
                    }
                    string propertyName = property < 3 ? "m_LocalPosition." + "xyz"[property] : property < 7 ? "m_LocalRotation." + "xyzw"[property - 3] : "m_LocalScale." + "xyz"[property - 7];
                    var curve = new AnimationCurve(keys);
                    for (int f = 0; f < Frames; f++)
                    {
                        AnimationUtility.SetKeyLeftTangentMode(curve, f, AnimationUtility.TangentMode.ClampedAuto);
                        AnimationUtility.SetKeyRightTangentMode(curve, f, AnimationUtility.TangentMode.ClampedAuto);
                    }
                    AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(path, typeof(Transform), propertyName), curve);
                }
            }
            clip.EnsureQuaternionContinuity();
            AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(actor.clip);
            settings.loopTime = true;
            settings.loopBlend = false;
            AnimationUtility.SetAnimationClipSettings(clip, settings);
            return clip;
        }

        private static Pose Capture(Transform[] bones)
        {
            var pose = new Pose(bones.Length);
            for (int b = 0; b < bones.Length; b++) { pose.position[b] = bones[b].localPosition; pose.rotation[b] = bones[b].localRotation; pose.scale[b] = bones[b].localScale; }
            return pose;
        }
        private static void Restore(Transform[] bones, Pose pose)
        {
            for (int b = 0; b < bones.Length; b++) { bones[b].localPosition = pose.position[b]; bones[b].localRotation = pose.rotation[b]; bones[b].localScale = pose.scale[b]; }
        }
        private static Quaternion Mean(IEnumerable<Quaternion> rotations)
        {
            Quaternion first = rotations.First();
            Vector4 sum = Vector4.zero;
            foreach (Quaternion q in rotations) sum += new Vector4(q.x, q.y, q.z, q.w) * (Quaternion.Dot(first, q) < 0f ? -1f : 1f);
            sum.Normalize();
            return new Quaternion(sum.x, sum.y, sum.z, sum.w);
        }
        private static int LongitudinalAxis(Transform bone, Transform child)
        {
            Vector3 direction = bone.InverseTransformPoint(child.position);
            int axis = 0;
            for (int i = 1; i < 3; i++) if (Mathf.Abs(direction[i]) > Mathf.Abs(direction[axis])) axis = i;
            return axis;
        }
        private static void RotateWorld(Transform bone, Vector3 axis, float degrees) { bone.rotation = Quaternion.AngleAxis(degrees, axis) * bone.rotation; }
        private static float OpenArm(Transform shoulder, Transform elbow, Vector3 outward, float degrees)
        {
            Vector3 direction = (elbow.position - shoulder.position).normalized;
            Vector3 axis = Vector3.Cross(direction, outward);
            float remaining = Vector3.Angle(direction, outward);
            if (remaining < .01f) return 0f;
            if (axis.sqrMagnitude < .000001f) throw new InvalidOperationException("Ambiguous outward arm axis: " + shoulder.name);
            float before = Vector3.Dot(direction, outward);
            shoulder.rotation = Quaternion.AngleAxis(Mathf.Min(degrees, remaining), axis.normalized) * shoulder.rotation;
            float gain = Vector3.Dot((elbow.position - shoulder.position).normalized, outward) - before;
            if (gain < -.00001f) throw new InvalidOperationException("Arm opening moved inward: " + shoulder.name);
            return gain;
        }
        private static int[] FootVertices(SkinnedMeshRenderer renderer)
        {
            BoneWeight[] weights = renderer.sharedMesh.boneWeights;
            var result = new List<int>();
            for (int v = 0; v < weights.Length; v++)
            {
                BoneWeight w = weights[v];
                float footWeight = 0f;
                int[] ids = { w.boneIndex0, w.boneIndex1, w.boneIndex2, w.boneIndex3 };
                float[] values = { w.weight0, w.weight1, w.weight2, w.weight3 };
                for (int i = 0; i < 4; i++) if (renderer.bones[ids[i]].name.StartsWith("Foot.", StringComparison.Ordinal)) footWeight += values[i];
                if (footWeight >= .3f) result.Add(v);
            }
            return result.ToArray();
        }
        private static float FootFloor(SkinnedMeshRenderer renderer, Mesh mesh, int[] vertices)
        {
            // useScale=false already retains the skinned hierarchy's scale in the snapshot.
            renderer.BakeMesh(mesh, false);
            Vector3[] points = mesh.vertices;
            float min = float.PositiveInfinity;
            Matrix4x4 toWorld = Matrix4x4.TRS(renderer.transform.position, renderer.transform.rotation, Vector3.one);
            foreach (int vertex in vertices) min = Mathf.Min(min, toWorld.MultiplyPoint3x4(points[vertex]).y);
            return min;
        }
        private static Vector3[][] NewPoints() { return Endpoints.Select(_ => new Vector3[Frames]).ToArray(); }
        private static void CaptureEndpoints(Vector3[][] points, int frame, Transform[] bones, Dictionary<string, int> indices, Transform pelvis)
        {
            for (int i = 0; i < Endpoints.Length; i++) points[i][frame] = bones[indices[Endpoints[i]]].position - pelvis.position;
        }
        private static float Span(Vector3[] points)
        {
            float span = 0f;
            for (int i = 0; i < points.Length; i++) for (int j = 0; j < i; j++) span = Mathf.Max(span, Vector3.Distance(points[i], points[j]));
            return span;
        }
        private static float AngularSpan(Pose[] poses, int bone)
        {
            float span = 0f;
            for (int i = 0; i < poses.Length; i++) for (int j = 0; j < i; j++) span = Mathf.Max(span, Quaternion.Angle(poses[i].rotation[bone], poses[j].rotation[bone]));
            return span;
        }
        private static bool IsFinite(Vector3 value) { return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z); }
        private static bool IsFinite(Quaternion value) { return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z) && IsFinite(value.w); }
        private static bool IsFinite(float value) { return !float.IsNaN(value) && !float.IsInfinity(value); }
    }
}
