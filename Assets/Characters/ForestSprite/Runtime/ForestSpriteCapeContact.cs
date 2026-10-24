using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.LowLevel;

namespace ForestSpriteCharacter
{
    // Contacts are against the explicit Cloth proxies, not an exact signed distance to the rendered body.
    [DisallowMultipleComponent]
    public sealed class ForestSpriteCapeContact : MonoBehaviour
    {
        public ForestSpriteCapeCloth cape;
        public bool responseEnabled = true;
        [Range(0, 1)] public float rebound = .35f;
        [Range(.08f, .6f)] public float responseTime = .18f;
        [Range(0, .025f)] public float maxCorrection = .025f;
        [Range(0, .01f)] public float contactMargin = .003f;
        public bool ShowMarkers;
        public float ActualDepth { get; private set; }
        public float SurfaceDepth { get; private set; }
        public int ContactCount { get; private set; }
        public int SurfaceContactCount { get; private set; }
        public Vector3 ContactPoint { get; private set; }
        public Vector3 ContactNormal { get; private set; }
        public Vector3 VertexContactPoint { get; private set; }
        public Vector3 SurfaceContactPoint { get; private set; }
        public string ContactCollider { get; private set; }
        public float TargetDepth { get; private set; }
        public float UnresolvedTargetDepth { get; private set; }
        public float MaximumCorrection { get; private set; }
        public float MaximumTargetCorrection { get; private set; }
        public float MaximumResponseSpeed { get; private set; }
        public float GuideArcError { get; private set; }
        public int LastSampleFrame { get; private set; } = -1;
        public int AnomalyCount { get; private set; }

        [Serializable]
        public struct ContactFrame
        {
            public int frame, vertexContacts, surfaceContacts;
            public float time, actualDepth, surfaceDepth, targetDepth, unresolvedTargetDepth, correction, targetCorrection, responseSpeed, guideArcError;
            public Vector3 point, normal;
            public string collider;
        }
        [Serializable]
        sealed class Recording
        {
            public string evidence = "World metres; native Cloth vertices and triangle midpoint/centroid samples after ClothFinish; explicit sphere/conical-capsule/capsule proxies. Guide response is bounded, not native particle restitution.";
            public int anomalyCount;
            public ContactFrame[] frames;
        }
        struct Volume
        {
            public Vector3 a, b, axis, direction;
            public float ra, rb, length, difference, coneDenominator;
            public bool singleSphere;
            public string name, firstName, secondName;
        }
        struct SampleMarker { }
        static readonly HashSet<ForestSpriteCapeContact> samplers = new HashSet<ForestSpriteCapeContact>();
        readonly List<Volume> volumes = new List<Volume>();
        readonly ContactFrame[] recent = new ContactFrame[180];
        int recentWrite, recentCount;
        SkinnedMeshRenderer skin;
        Mesh sourceMesh;
        Vector3[] restVertices, targets, originalTargets, actual, offsets, velocities, basePositions, projected, corrections;
        Quaternion[] baseRotations, segmentRotations;
        BoneWeight[] weights;
        Matrix4x4[] bindposes, matrices;
        Transform[] targetBones;
        int targetsFrame = -1;
        int[] vertexParticles, particleVertices, triangles, correctionCounts;
        float[] lengths;
        GUIStyle markerLabel;

        void OnEnable()
        {
            samplers.Add(this);
            var loop = PlayerLoop.GetCurrentPlayerLoop();
            if (!ContainsMarker(loop) && InsertMarker(ref loop)) PlayerLoop.SetPlayerLoop(loop);
        }
        void OnDisable()
        {
            samplers.Remove(this);
            if (samplers.Count == 0)
            {
                var loop = PlayerLoop.GetCurrentPlayerLoop();
                if (RemoveMarker(ref loop)) PlayerLoop.SetPlayerLoop(loop);
            }
            ResetResponse();
        }
        static void SampleAll()
        {
            foreach (var sampler in samplers) if (sampler && sampler.isActiveAndEnabled && sampler.cape != null && sampler.cape.CollectDiagnostics) sampler.SampleActualContacts();
        }
        static bool ContainsMarker(PlayerLoopSystem loop)
        {
            if (loop.type == typeof(SampleMarker)) return true;
            if (loop.subSystemList != null) foreach (var child in loop.subSystemList) if (ContainsMarker(child)) return true;
            return false;
        }
        static bool InsertMarker(ref PlayerLoopSystem loop)
        {
            var children = loop.subSystemList;
            if (children == null) return false;
            for (int i = 0; i < children.Length; i++)
            {
                if (children[i].type == typeof(UnityEngine.PlayerLoop.PostLateUpdate.PhysicsSkinnedClothFinishUpdate))
                {
                    var updated = new List<PlayerLoopSystem>(children);
                    updated.Insert(i + 1, new PlayerLoopSystem { type = typeof(SampleMarker), updateDelegate = SampleAll });
                    loop.subSystemList = updated.ToArray(); return true;
                }
                if (InsertMarker(ref children[i])) { loop.subSystemList = children; return true; }
            }
            return false;
        }
        static bool RemoveMarker(ref PlayerLoopSystem loop)
        {
            if (loop.subSystemList == null) return false;
            bool changed = false;
            var updated = new List<PlayerLoopSystem>();
            foreach (var child in loop.subSystemList)
            {
                if (child.type == typeof(SampleMarker)) { changed = true; continue; }
                var copy = child; changed |= RemoveMarker(ref copy); updated.Add(copy);
            }
            if (changed) loop.subSystemList = updated.ToArray();
            return changed;
        }

        bool Prepare()
        {
            if (cape == null || cape.cloth == null || cape.restParticles == null || !cape.UsesGuides) return false;
            if (skin == null) skin = cape.GetComponent<SkinnedMeshRenderer>();
            if (skin == null || skin.sharedMesh == null) return false;
            if (sourceMesh == skin.sharedMesh && offsets != null && offsets.Length == cape.guideNodes.Length) return true;
            targetsFrame = -1;
            targetBones = null;
            sourceMesh = skin.sharedMesh;
            restVertices = sourceMesh.vertices; weights = sourceMesh.boneWeights; bindposes = sourceMesh.bindposes;
            triangles = sourceMesh.triangles; targets = new Vector3[restVertices.Length];
            originalTargets = new Vector3[restVertices.Length];
            matrices = new Matrix4x4[skin.bones.Length];
            vertexParticles = new int[restVertices.Length]; particleVertices = new int[cape.restParticles.Length];
            for (int i = 0; i < particleVertices.Length; i++) particleVertices[i] = -1;
            for (int i = 0; i < restVertices.Length; i++)
            {
                int nearest = -1; float best = float.PositiveInfinity;
                for (int j = 0; j < cape.restParticles.Length; j++)
                {
                    float d = (restVertices[i] - cape.restParticles[j]).sqrMagnitude;
                    if (d < best) { best = d; nearest = j; }
                }
                if (nearest < 0 || best > 1e-8f) { sourceMesh = null; return false; }
                vertexParticles[i] = nearest; particleVertices[nearest] = i;
            }
            for (int i = 0; i < particleVertices.Length; i++) if (particleVertices[i] < 0) { sourceMesh = null; return false; }
            actual = new Vector3[cape.restParticles.Length];
            int count = cape.guideNodes.Length;
            offsets = new Vector3[count]; velocities = new Vector3[count]; basePositions = new Vector3[count];
            projected = new Vector3[count]; corrections = new Vector3[count]; correctionCounts = new int[count];
            baseRotations = new Quaternion[count]; segmentRotations = new Quaternion[count]; lengths = new float[count];
            for (int i = 0; i < count; i++)
                if (i % (cape.guideSegments + 1) != 0) lengths[i] = Vector3.Distance(cape.guideRestPoints[i - 1], cape.guideRestPoints[i]);
            return true;
        }
        void ReadTargets(Transform[] bones)
        {
            if (matrices.Length != bones.Length) matrices = new Matrix4x4[bones.Length];
            for (int i = 0; i < matrices.Length; i++) matrices[i] = bones[i].localToWorldMatrix * bindposes[i];
            for (int i = 0; i < restVertices.Length; i++)
            {
                BoneWeight w = weights[i]; Vector3 v = restVertices[i];
                targets[i] = matrices[w.boneIndex0].MultiplyPoint3x4(v) * w.weight0 + matrices[w.boneIndex1].MultiplyPoint3x4(v) * w.weight1
                    + matrices[w.boneIndex2].MultiplyPoint3x4(v) * w.weight2 + matrices[w.boneIndex3].MultiplyPoint3x4(v) * w.weight3;
            }
            targetBones = bones;
            targetsFrame = Time.frameCount;
        }
        internal bool TryGetGuideTargets(SkinnedMeshRenderer renderer, out Vector3[] result)
        {
            result = null;
            if (renderer != skin || renderer == null || sourceMesh != renderer.sharedMesh || targetsFrame != Time.frameCount) return false;
            var bones = renderer.bones;
            if (targetBones == null || bones.Length != targetBones.Length) return false;
            for (int i = 0; i < bones.Length; i++)
                if (bones[i] != targetBones[i] || !(bones[i].localToWorldMatrix * bindposes[i]).Equals(matrices[i])) return false;
            result = targets;
            return true;
        }

        // Called once after the driver's fresh guide pose, before native cloth begins this frame.
        public void ApplyGuideResponse(float dt)
        {
            if (!Prepare()) return;
            var bones = skin.bones;
            bool diagnose = cape.CollectDiagnostics;
            RefreshVolumes(); ReadTargets(bones); TargetDepth = diagnose ? MaximumTargetDepth() : 0;
            Array.Copy(targets, originalTargets, targets.Length);
            if (!isActiveAndEnabled || !responseEnabled) { ResetResponse(); UnresolvedTargetDepth = TargetDepth; return; }
            for (int i = 0; i < offsets.Length; i++)
            {
                basePositions[i] = cape.guideNodes[i].localPosition;
                baseRotations[i] = cape.guideNodes[i].localRotation;
            }
            dt = Mathf.Clamp(dt, 0, .1f);
            float omega = 2f * Mathf.PI / Mathf.Clamp(responseTime, .08f, .6f);
            float damping = Mathf.Lerp(1f, .25f, Mathf.Clamp01(rebound));
            int steps = Mathf.Max(1, Mathf.CeilToInt(dt / .008f)); float h = dt / steps;
            for (int step = 0; step < steps; step++)
                for (int i = 0; i < offsets.Length; i++)
                {
                    if (!Movable(i)) { offsets[i] = velocities[i] = Vector3.zero; continue; }
                    velocities[i] += (-omega * omega * offsets[i] - 2f * damping * omega * velocities[i]) * h;
                    velocities[i] = Vector3.ClampMagnitude(velocities[i], .4f);
                    offsets[i] += velocities[i] * h;
                }
            ApplyArcPreservingOffsets();
            // A few projection passes handle overlapping body proxies; residuals remain visible if the cap prevents correction.
            for (int pass = 0; pass < 3; pass++)
            {
                ReadTargets(bones); Array.Clear(corrections, 0, corrections.Length); Array.Clear(correctionCounts, 0, correctionCounts.Length);
                for (int particle = 0; particle < particleVertices.Length; particle++)
                {
                    int vertex = particleVertices[particle];
                    if (cape.freedomWeights != null && cape.freedomWeights[particle] <= .001f) continue;
                    float depth = Penetration(targets[vertex], contactMargin, out Vector3 normal, out _);
                    if (depth <= 0) continue;
                    BoneWeight w = weights[vertex];
                    float denominator = EditableSquare(w.boneIndex0, w.weight0) + EditableSquare(w.boneIndex1, w.weight1)
                        + EditableSquare(w.boneIndex2, w.weight2) + EditableSquare(w.boneIndex3, w.weight3);
                    if (denominator < .00001f) continue;
                    Vector3 correction = cape.guideFrame.InverseTransformVector(normal * depth) / denominator;
                    Accumulate(w.boneIndex0, w.weight0, correction); Accumulate(w.boneIndex1, w.weight1, correction);
                    Accumulate(w.boneIndex2, w.weight2, correction); Accumulate(w.boneIndex3, w.weight3, correction);
                }
                for (int i = 0; i < offsets.Length; i++)
                {
                    if (correctionCounts[i] == 0) continue;
                    Vector3 correction = corrections[i] / correctionCounts[i];
                    offsets[i] += correction;
                    // A small spring impulse belongs to the guide response, never to the read-only native particles.
                    velocities[i] = Vector3.ClampMagnitude(velocities[i] + correction * omega * .2f * Mathf.Clamp01(rebound), .4f);
                }
                ApplyArcPreservingOffsets();
            }
            ReadTargets(bones); UnresolvedTargetDepth = diagnose ? MaximumTargetDepth() : 0;
            MaximumCorrection = MaximumTargetCorrection = MaximumResponseSpeed = GuideArcError = 0;
            foreach (int vertex in particleVertices) MaximumTargetCorrection = Mathf.Max(MaximumTargetCorrection,
                Vector3.Distance(originalTargets[vertex], targets[vertex]));
            for (int i = 0; i < offsets.Length; i++)
            {
                offsets[i] = cape.guideNodes[i].localPosition - basePositions[i];
                MaximumCorrection = Mathf.Max(MaximumCorrection, cape.guideFrame.TransformVector(offsets[i]).magnitude);
                MaximumResponseSpeed = Mathf.Max(MaximumResponseSpeed, cape.guideFrame.TransformVector(velocities[i]).magnitude);
                if (i % (cape.guideSegments + 1) != 0)
                    GuideArcError = Mathf.Max(GuideArcError, Mathf.Abs(Vector3.Distance(cape.guideNodes[i - 1].localPosition, cape.guideNodes[i].localPosition) - lengths[i]));
            }
        }
        bool Movable(int node) => node >= 0 && node < offsets.Length && node % (cape.guideSegments + 1) >= 3;
        float EditableSquare(int bone, float weight) => bone > 0 && Movable(bone - 1) ? weight * weight : 0;
        void Accumulate(int bone, float weight, Vector3 correction)
        {
            if (weight <= 0 || bone <= 0 || !Movable(bone - 1)) return;
            corrections[bone - 1] += correction * weight; correctionCounts[bone - 1]++;
        }
        void ApplyArcPreservingOffsets()
        {
            float cap = Mathf.Clamp(maxCorrection, 0, .025f);
            float scale = 1f;
            if (!Project(scale, cap))
            {
                float low = 0, high = 1;
                for (int step = 0; step < 8; step++) { float mid = (low + high) * .5f; if (Project(mid, cap)) low = mid; else high = mid; }
                scale = low; Project(scale, cap);
            }
            int stride = cape.guideSegments + 1;
            for (int i = 0; i < offsets.Length; i++)
            {
                int j = i % stride;
                segmentRotations[i] = j == 0 ? Quaternion.identity : Quaternion.FromToRotation(basePositions[i] - basePositions[i - 1], projected[i] - projected[i - 1]);
                cape.guideNodes[i].localPosition = projected[i];
            }
            for (int i = 0; i < offsets.Length; i++)
            {
                int j = i % stride;
                Quaternion correction = j == 0 ? Quaternion.identity : j == stride - 1 ? segmentRotations[i]
                    : Quaternion.Slerp(segmentRotations[i], segmentRotations[i + 1], .5f);
                cape.guideNodes[i].localRotation = correction * baseRotations[i];
                offsets[i] = projected[i] - basePositions[i];
            }
        }
        bool Project(float scale, float cap)
        {
            bool within = true;
            for (int i = 0; i < offsets.Length; i++)
            {
                if (!Movable(i)) projected[i] = basePositions[i];
                else
                {
                    Vector3 desired = basePositions[i] + offsets[i] * scale - projected[i - 1];
                    if (desired.sqrMagnitude < 1e-12f) desired = basePositions[i] - basePositions[i - 1];
                    projected[i] = projected[i - 1] + desired.normalized * lengths[i];
                }
                if (cape.guideFrame.TransformVector(projected[i] - basePositions[i]).magnitude > cap + .000001f) within = false;
            }
            return within;
        }
        float MaximumTargetDepth()
        {
            float maximum = 0;
            foreach (int vertex in particleVertices) maximum = Mathf.Max(maximum, Penetration(targets[vertex], 0, out _, out _));
            return maximum;
        }

        public void SampleActualContacts()
        {
            if (LastSampleFrame == Time.frameCount || !Prepare() || !cape.cloth.enabled) return;
            var particles = cape.cloth.vertices;
            if (particles.Length != actual.Length) return;
            RefreshVolumes(); LastSampleFrame = Time.frameCount;
            ActualDepth = SurfaceDepth = 0; ContactCount = SurfaceContactCount = 0;
            ContactPoint = ContactNormal = VertexContactPoint = SurfaceContactPoint = Vector3.zero; ContactCollider = "";
            for (int i = 0; i < particles.Length; i++)
            {
                actual[i] = cape.cloth.transform.TransformPoint(particles[i]);
                Check(actual[i], false);
            }
            for (int i = 0; i < triangles.Length; i += 3)
            {
                Vector3 a = actual[vertexParticles[triangles[i]]], b = actual[vertexParticles[triangles[i + 1]]], c = actual[vertexParticles[triangles[i + 2]]];
                Check((a + b) * .5f, true); Check((b + c) * .5f, true); Check((c + a) * .5f, true); Check((a + b + c) / 3f, true);
            }
            if (ActualDepth > .015f || SurfaceDepth > .015f || UnresolvedTargetDepth > .003f) AnomalyCount++;
            recent[recentWrite] = new ContactFrame { frame = Time.frameCount, time = Time.time, vertexContacts = ContactCount, surfaceContacts = SurfaceContactCount,
                actualDepth = ActualDepth, surfaceDepth = SurfaceDepth, targetDepth = TargetDepth, unresolvedTargetDepth = UnresolvedTargetDepth,
                correction = MaximumCorrection, targetCorrection = MaximumTargetCorrection, responseSpeed = MaximumResponseSpeed, guideArcError = GuideArcError,
                point = ContactPoint, normal = ContactNormal, collider = ContactCollider };
            recentWrite = (recentWrite + 1) % recent.Length; recentCount = Mathf.Min(recentCount + 1, recent.Length);
        }
        void Check(Vector3 point, bool surface)
        {
            float previousPeak = Mathf.Max(ActualDepth, SurfaceDepth);
            float depth = Penetration(point, 0, out Vector3 normal, out string name);
            if (depth > .00001f) { if (surface) SurfaceContactCount++; else ContactCount++; }
            if (surface)
            {
                if (depth > SurfaceDepth) SurfaceContactPoint = point;
                SurfaceDepth = Mathf.Max(SurfaceDepth, depth);
            }
            else
            {
                if (depth > ActualDepth) VertexContactPoint = point;
                ActualDepth = Mathf.Max(ActualDepth, depth);
            }
            if (depth > previousPeak) { ContactPoint = point; ContactNormal = normal; ContactCollider = name; }
        }
        void RefreshVolumes()
        {
            int index = 0;
            foreach (var pair in cape.cloth.sphereColliders)
            {
                if (!pair.first) continue;
                Vector3 a = pair.first.transform.TransformPoint(pair.first.center); float ra = SphereRadius(pair.first);
                SetVolume(index++, a, pair.second ? pair.second.transform.TransformPoint(pair.second.center) : a,
                    ra, pair.second ? SphereRadius(pair.second) : ra, pair.first.name, pair.second ? pair.second.name : null);
            }
            foreach (var capsule in cape.cloth.capsuleColliders)
            {
                if (!capsule) continue;
                Vector3 scale = capsule.transform.lossyScale;
                float axial = Mathf.Abs(scale[capsule.direction]);
                float radial = Mathf.Max(Mathf.Abs(scale[(capsule.direction + 1) % 3]), Mathf.Abs(scale[(capsule.direction + 2) % 3]));
                float radius = capsule.radius * radial, half = Mathf.Max(0, capsule.height * axial * .5f - radius);
                Vector3 localAxis = Vector3.zero; localAxis[capsule.direction] = 1;
                Vector3 axis = capsule.transform.TransformDirection(localAxis).normalized;
                Vector3 centre = capsule.transform.TransformPoint(capsule.center);
                SetVolume(index++, centre - axis * half, centre + axis * half, radius, radius, capsule.name, null);
            }
            if (index < volumes.Count) volumes.RemoveRange(index, volumes.Count - index);
        }
        void SetVolume(int index, Vector3 a, Vector3 b, float ra, float rb, string firstName, string secondName)
        {
            string name = index < volumes.Count && volumes[index].firstName == firstName && volumes[index].secondName == secondName
                ? volumes[index].name : firstName + (secondName == null ? "" : " -> " + secondName);
            Vector3 axis = b - a;
            float length = axis.magnitude, difference = rb - ra;
            bool singleSphere = length <= Mathf.Abs(difference) + .00001f;
            var volume = new Volume { a = a, b = b, ra = ra, rb = rb, axis = axis, length = length, difference = difference,
                singleSphere = singleSphere, direction = singleSphere ? Vector3.zero : axis / length,
                coneDenominator = singleSphere ? 0 : Mathf.Sqrt(length * length - difference * difference),
                name = name, firstName = firstName, secondName = secondName };
            if (index < volumes.Count) volumes[index] = volume;
            else volumes.Add(volume);
        }
        static float SphereRadius(SphereCollider sphere)
        {
            Vector3 scale = sphere.transform.lossyScale;
            return sphere.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
        }
        float Penetration(Vector3 point, float margin, out Vector3 normal, out string colliderName)
        {
            float maximum = 0; normal = Vector3.zero; colliderName = "";
            foreach (Volume volume in volumes)
            {
                Vector3 centre; float radius;
                if (volume.singleSphere)
                {
                    bool useB = volume.rb - Vector3.Distance(point, volume.b) > volume.ra - Vector3.Distance(point, volume.a);
                    centre = useB ? volume.b : volume.a; radius = useB ? volume.rb : volume.ra;
                }
                else
                {
                    float z = Vector3.Dot(point - volume.a, volume.direction);
                    float radial = (point - volume.a - volume.axis * (z / volume.length)).magnitude;
                    float t = Mathf.Clamp01((z + volume.difference * radial / volume.coneDenominator) / volume.length);
                    centre = volume.a + volume.axis * t; radius = Mathf.Lerp(volume.ra, volume.rb, t);
                }
                Vector3 delta = point - centre; float distance = delta.magnitude, depth = radius + Mathf.Max(0, margin) - distance;
                if (depth <= maximum) continue;
                maximum = depth; normal = distance > .000001f ? delta / distance : Vector3.up; colliderName = volume.name;
            }
            return maximum;
        }
        public void ResetResponse()
        {
            if (offsets != null) Array.Clear(offsets, 0, offsets.Length);
            if (velocities != null) Array.Clear(velocities, 0, velocities.Length);
            MaximumCorrection = MaximumTargetCorrection = MaximumResponseSpeed = GuideArcError = 0;
        }
        public Vector3 GetGuideResponseWorld(int node) => offsets != null && node >= 0 && node < offsets.Length
            ? cape.guideFrame.TransformVector(offsets[node]) : Vector3.zero;
        public Vector3 GetGuideResponseVelocityWorld(int node) => velocities != null && node >= 0 && node < velocities.Length
            ? cape.guideFrame.TransformVector(velocities[node]) : Vector3.zero;
        public ContactFrame[] RecentFrames
        {
            get
            {
                var result = new ContactFrame[recentCount];
                for (int i = 0; i < result.Length; i++) result[i] = recent[(recentWrite - recentCount + i + recent.Length) % recent.Length];
                return result;
            }
        }
        public void ExportDiagnostics(string path)
        {
            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            File.WriteAllText(path, JsonUtility.ToJson(new Recording { anomalyCount = AnomalyCount, frames = RecentFrames }, true));
        }
        void OnDrawGizmos()
        {
            if (!ShowMarkers || Mathf.Max(ActualDepth, SurfaceDepth) <= 0) return;
            Gizmos.color = Color.red; Gizmos.DrawSphere(ContactPoint, .005f);
            Gizmos.color = Color.yellow; Gizmos.DrawLine(ContactPoint, ContactPoint + ContactNormal * .05f);
        }
        void OnGUI()
        {
            if (!ShowMarkers || Camera.main == null || cape == null || !cape.MotionEnabled) return;
            if (markerLabel == null) markerLabel = new GUIStyle(GUI.skin.label)
                { font = Font.CreateDynamicFontFromOSFont("Microsoft YaHei", 12), fontSize = 12 };
            DrawMarker(VertexContactPoint, ActualDepth, "顶点", Color.red);
            DrawMarker(SurfaceContactPoint, SurfaceDepth, "表面", Color.yellow);
        }
        void DrawMarker(Vector3 point, float depth, string label, Color color)
        {
            if (depth <= .00001f) return;
            Vector3 screen = Camera.main.WorldToScreenPoint(point);
            if (screen.z <= 0 || screen.x < 0 || screen.x > Screen.width || screen.y < 0 || screen.y > Screen.height) return;
            float x = screen.x, y = Screen.height - screen.y;
            Color previous = GUI.color; GUI.color = color;
            GUI.Label(new Rect(x - 7f, y - 9f, 20f, 20f), "●");
            // The label stays next to the character instead of occupying the tuning panel.
            GUI.Label(new Rect(Mathf.Clamp(x + 9f, 0, Screen.width - 150f), Mathf.Clamp(y - 10f, 0, Screen.height - 22f), 150f, 22f),
                label + " " + (depth * 1000f).ToString("0.0") + " mm", markerLabel);
            GUI.color = previous;
        }
    }
}
