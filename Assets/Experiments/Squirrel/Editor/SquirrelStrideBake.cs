using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace WeatherHouseSquirrelLab.Editor
{
    // This prepared short-legged rig needs foot targets, not repeated angular amplification.
    internal sealed class SquirrelStrideBake
    {
        public struct Settings
        {
            public float strideLength, footLift, hipBounce, weightShift, upperBody;
            public static Settings Default => new Settings { strideLength = .69f, footLift = .30f, hipBounce = .12f, weightShift = 1f, upperBody = 1f };
        }
        [Serializable] public sealed class FootMotion
        {
            public string bone;
            public float foreAftSpan, lateralSpan, soleLiftRange, minimumSoleHeight;
        }
        [Serializable] public sealed class Motion
        {
            public FootMotion[] feet;
            public float pelvisHeightSpan, pelvisLateralSpan, pelvisRollDegrees, chestYawDegrees, meanHipHeight;
        }
        [Serializable] public sealed class StrideReport
        {
            public string method = "Measured rest axes, fixed-length two-bone IK, alternating stance/swing, level soles, pelvis weight shift and counter-swing. Distances are scene units; foot motion is the actual skinned foot island centroid relative to pelvis.";
            public Motion original, previous, expressive;
            public float groundHeight, legLength, maximumTargetError, maximumLegLengthError, minimumReachMargin;
            public bool noFloorPenetration, bothFeetLift, weightShiftVisible, passed;
        }

        readonly Transform pelvis, spine, chest;
        readonly Transform[] thighs, shins, feet, upperArms, forearms;
        readonly Quaternion[] soleRotations;
        readonly Vector3[] ankleToHip;
        readonly float[] upperLength, lowerLength, ankleHeight;
        readonly int[][] footVertices;
        readonly SkinnedMeshRenderer skin;
        readonly Mesh mesh;
        readonly Vector3 forward, lateral;
        readonly bool run;
        readonly float length;
        readonly List<Vector3>[] centroids = { new List<Vector3>(), new List<Vector3>() };
        readonly List<float>[] soles = { new List<float>(), new List<float>() };
        readonly List<Vector3> pelvisPoints = new List<Vector3>();
        readonly List<float> hipHeights = new List<float>(), pelvisRoll = new List<float>(), chestYaw = new List<float>();
        public readonly StrideReport report = new StrideReport();

        public SquirrelStrideBake(string id, Transform[] bones, SkinnedMeshRenderer renderer, Mesh scratch)
        {
            run = id == "run_biped";
            var byName = bones.GroupBy(t => t.name).ToDictionary(g => g.Key, g => g.First());
            pelvis = byName["Pelvis"]; spine = byName["Spine"]; chest = byName["Chest"];
            thighs = new[] { byName["Thigh.L"], byName["Thigh.R"] };
            shins = new[] { byName["Shin.L"], byName["Shin.R"] };
            feet = new[] { byName["Foot.L"], byName["Foot.R"] };
            upperArms = new[] { byName["UpperArm.L"], byName["UpperArm.R"] };
            forearms = new[] { byName["Forearm.L"], byName["Forearm.R"] };
            skin = renderer; mesh = scratch;
            var rest = renderer.bones.Select((bone, i) => new { bone.name, matrix = renderer.transform.localToWorldMatrix * renderer.sharedMesh.bindposes[i].inverse })
                .ToDictionary(item => item.name, item => item.matrix);
            lateral = Vector3.ProjectOnPlane((Vector3)rest["Thigh.R"].GetColumn(3) - (Vector3)rest["Thigh.L"].GetColumn(3), Vector3.up).normalized;
            forward = Vector3.Cross(lateral, Vector3.up).normalized;
            Vector3 headDirection = Vector3.ProjectOnPlane((Vector3)rest["Head"].GetColumn(3) - (Vector3)rest["Pelvis"].GetColumn(3), Vector3.up);
            if (Vector3.Dot(forward, headDirection) < 0f) forward = -forward;
            footVertices = new[] { FindFootVertices("Foot.L"), FindFootVertices("Foot.R") };
            if (footVertices.Any(v => v.Length < 20)) throw new InvalidOperationException("Insufficient independently weighted foot vertices.");
            Vector3[] bindVertices = renderer.sharedMesh.vertices;
            report.groundHeight = footVertices.SelectMany(v => v).Min(v => renderer.transform.TransformPoint(bindVertices[v]).y);
            soleRotations = feet.Select(t => rest[t.name].rotation).ToArray();
            upperLength = thighs.Select((t, i) => Vector3.Distance(rest[t.name].GetColumn(3), rest[shins[i].name].GetColumn(3))).ToArray();
            lowerLength = shins.Select((t, i) => Vector3.Distance(rest[t.name].GetColumn(3), rest[feet[i].name].GetColumn(3))).ToArray();
            ankleHeight = feet.Select(t => rest[t.name].GetColumn(3).y - report.groundHeight).ToArray();
            ankleToHip = feet.Select((t, i) => (Vector3)rest[t.name].GetColumn(3) - (Vector3)rest[thighs[i].name].GetColumn(3)).ToArray();
            length = report.legLength = (upperLength.Sum() + lowerLength.Sum()) * .5f;
            report.minimumReachMargin = 1f;
        }

        public Motion MeasureClip(AnimationClip clip, GameObject model, int frames)
        {
            ResetMeasurements();
            for (int f = 0; f < frames; f++) { clip.SampleAnimation(model, clip.length * f / (frames - 1)); Measure(); }
            Motion result = FinishMeasurements();
            ResetMeasurements();
            return result;
        }

        public void Apply(float normalizedTime) { Apply(normalizedTime, Settings.Default); }

        public void Apply(float normalizedTime, Settings settings)
        {
            float phase = normalizedTime * 2f * Mathf.PI * (run ? 3f : 2f);
            float step = Mathf.Sin(phase);
            float liftAmount = Mathf.Clamp(settings.footLift, 0f, .6f) * (run ? 1.43333f : 1f);
            float strideAmount = Mathf.Clamp(settings.strideLength, .05f, .82f) * (run ? 1.07246f : 1f);
            float hipHeight = length * ((run ? .70f : .76f) + Mathf.Clamp(settings.hipBounce, 0f, .18f) * (run ? .83333f : 1f) * Mathf.Cos(phase * 2f));
            float weightShift = Mathf.Clamp(settings.weightShift, 0f, 2f), upperBody = Mathf.Clamp(settings.upperBody, 0f, 2f);
            RotateWorld(pelvis, forward, 7f * step * weightShift);
            RotateWorld(pelvis, Vector3.up, -4f * step * weightShift);
            float meanHipY = (thighs[0].position.y + thighs[1].position.y) * .5f;
            pelvis.position += Vector3.up * (report.groundHeight + ankleHeight.Average() + hipHeight - meanHipY)
                + lateral * (length * .055f * step * weightShift);

            RotateWorld(spine, Vector3.up, 9f * step * upperBody);
            RotateWorld(spine, forward, -4f * step * upperBody);
            RotateWorld(chest, Vector3.up, 7f * Mathf.Sin(phase - .18f) * upperBody);
            for (int side = 0; side < 2; side++)
            {
                float sign = side == 0 ? 1f : -1f;
                RotateWorld(upperArms[side], lateral, sign * (run ? 20f : 26f) * step * upperBody);
                RotateWorld(forearms[side], lateral, -sign * 9f * Mathf.Sin(phase - .25f) * upperBody);
                float footPhase = phase + side * Mathf.PI;
                float swing = Mathf.Max(0f, Mathf.Cos(footPhase));
                float lift = length * liftAmount * swing * swing;
                Vector3 hip = thighs[side].position;
                Vector3 target = hip + forward * (length * strideAmount * Mathf.Sin(footPhase))
                    + lateral * (Vector3.Dot(ankleToHip[side], lateral) + (side == 0 ? -.055f : .055f) * length);
                target.y = report.groundHeight + .004f + ankleHeight[side] + lift;
                SolveLeg(side, target);
                feet[side].rotation = soleRotations[side];
            }

            // Correct the actual sole, not the ankle, and only by re-solving the fixed-length leg.
            // Mixed foot/shin weights otherwise leave the toes below the floor even with a valid ankle target.
            for (int pass = 0; pass < 2; pass++)
            {
                Vector3[] points = BakePoints();
                for (int side = 0; side < 2; side++)
                {
                    float footPhase = phase + side * Mathf.PI;
                    float swing = Mathf.Max(0f, Mathf.Cos(footPhase));
                    float expectedFloor = report.groundHeight + .004f + length * liftAmount * swing * swing;
                    float sole = footVertices[side].Min(v => points[v].y);
                    Vector3 target = feet[side].position + Vector3.up * (expectedFloor - sole);
                    SolveLeg(side, target);
                    feet[side].rotation = soleRotations[side];
                }
            }
        }

        void SolveLeg(int side, Vector3 target)
        {
            Transform hip = thighs[side], knee = shins[side], ankle = feet[side];
            float a = upperLength[side], b = lowerLength[side];
            Vector3 delta = target - hip.position;
            float requestedDistance = delta.magnitude;
            float distance = Mathf.Clamp(requestedDistance, Mathf.Abs(a - b) + .001f * length, .985f * (a + b));
            Vector3 direction = delta.normalized;
            // Knees bend in the measured sagittal plane, with a small outward component.
            Vector3 pole = Vector3.ProjectOnPlane(forward + lateral * (side == 0 ? -.12f : .12f), direction).normalized;
            float along = (a * a + distance * distance - b * b) / (2f * distance);
            float across = Mathf.Sqrt(Mathf.Max(0f, a * a - along * along));
            Vector3 kneeTarget = hip.position + direction * along + pole * across;
            hip.rotation = Quaternion.FromToRotation(knee.position - hip.position, kneeTarget - hip.position) * hip.rotation;
            Vector3 reachable = hip.position + direction * distance;
            knee.rotation = Quaternion.FromToRotation(ankle.position - knee.position, reachable - knee.position) * knee.rotation;
            report.maximumTargetError = Mathf.Max(report.maximumTargetError, Vector3.Distance(ankle.position, target));
            report.minimumReachMargin = Mathf.Min(report.minimumReachMargin, 1f - requestedDistance / (a + b));
            report.maximumLegLengthError = Mathf.Max(report.maximumLegLengthError,
                Mathf.Abs(Vector3.Distance(hip.position, knee.position) - a), Mathf.Abs(Vector3.Distance(knee.position, ankle.position) - b));
        }

        public void Measure()
        {
            Vector3[] points = BakePoints();
            for (int side = 0; side < 2; side++)
            {
                Vector3 centroid = Vector3.zero;
                float floor = float.PositiveInfinity;
                foreach (int vertex in footVertices[side]) { centroid += points[vertex]; floor = Mathf.Min(floor, points[vertex].y); }
                centroids[side].Add(centroid / footVertices[side].Length - pelvis.position);
                soles[side].Add(floor - report.groundHeight);
            }
            pelvisPoints.Add(pelvis.position);
            hipHeights.Add((thighs[0].position.y + thighs[1].position.y) * .5f - report.groundHeight);
            Vector3 hips = (thighs[1].position - thighs[0].position).normalized;
            pelvisRoll.Add(Mathf.Asin(Mathf.Clamp(Vector3.Dot(hips, Vector3.up), -1f, 1f)) * Mathf.Rad2Deg);
            Vector3 shoulders = (upperArms[1].position - upperArms[0].position).normalized;
            chestYaw.Add(Mathf.Atan2(Vector3.Dot(shoulders, forward), Vector3.Dot(shoulders, lateral)) * Mathf.Rad2Deg);
        }

        public void Complete()
        {
            report.expressive = FinishMeasurements();
            report.noFloorPenetration = report.expressive.feet.All(foot => foot.minimumSoleHeight >= -.0002f);
            report.bothFeetLift = report.expressive.feet.All(foot => foot.soleLiftRange >= length * .20f);
            report.weightShiftVisible = report.expressive.pelvisHeightSpan >= length * .12f && report.expressive.pelvisRollDegrees >= 8f;
            report.passed = report.noFloorPenetration && report.bothFeetLift && report.weightShiftVisible && report.maximumLegLengthError < .00001f;
        }

        Motion FinishMeasurements()
        {
            return new Motion
            {
                feet = Enumerable.Range(0, 2).Select(side => new FootMotion
                {
                    bone = feet[side].name,
                    foreAftSpan = Range(centroids[side].Select(p => Vector3.Dot(p, forward))),
                    lateralSpan = Range(centroids[side].Select(p => Vector3.Dot(p, lateral))),
                    soleLiftRange = Range(soles[side]), minimumSoleHeight = soles[side].Min()
                }).ToArray(),
                pelvisHeightSpan = Range(pelvisPoints.Select(p => p.y)),
                pelvisLateralSpan = Range(pelvisPoints.Select(p => Vector3.Dot(p, lateral))),
                pelvisRollDegrees = Range(pelvisRoll), chestYawDegrees = Range(chestYaw), meanHipHeight = hipHeights.Average()
            };
        }
        void ResetMeasurements()
        {
            foreach (var list in centroids) list.Clear(); foreach (var list in soles) list.Clear();
            pelvisPoints.Clear(); hipHeights.Clear(); pelvisRoll.Clear(); chestYaw.Clear();
        }
        Vector3[] BakePoints()
        {
            skin.BakeMesh(mesh, false);
            Matrix4x4 toWorld = Matrix4x4.TRS(skin.transform.position, skin.transform.rotation, Vector3.one);
            return mesh.vertices.Select(point => toWorld.MultiplyPoint3x4(point)).ToArray();
        }
        int[] FindFootVertices(string name)
        {
            int index = Array.FindIndex(skin.bones, bone => bone.name == name);
            return skin.sharedMesh.boneWeights.Select((w, i) => new { w, i }).Where(item =>
                (item.w.boneIndex0 == index ? item.w.weight0 : 0f) + (item.w.boneIndex1 == index ? item.w.weight1 : 0f)
                + (item.w.boneIndex2 == index ? item.w.weight2 : 0f) + (item.w.boneIndex3 == index ? item.w.weight3 : 0f) >= .3f).Select(item => item.i).ToArray();
        }
        static float Range(IEnumerable<float> values) { return values.Max() - values.Min(); }
        static void RotateWorld(Transform bone, Vector3 axis, float degrees) { bone.rotation = Quaternion.AngleAxis(degrees, axis) * bone.rotation; }
    }
}
