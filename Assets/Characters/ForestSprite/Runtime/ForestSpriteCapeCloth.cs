using System;
using System.IO;
using UnityEngine;

namespace ForestSpriteCharacter
{
    [DefaultExecutionOrder(-90), DisallowMultipleComponent]
    public sealed class ForestSpriteCapeCloth : MonoBehaviour
    {
        [Serializable]
        public sealed class Settings
        {
            [Range(0, 2)] public float windStrength = 1f;
            [Range(0, 1)] public float lift = .8f;
            [Range(0, 1)] public float wingSpread = .85f;
            [Range(0, 1)] public float wingFlap = 1f;
            [Range(0, 1)] public float sCurve = .7f;
            [Range(0, .12f)] public float waveAmplitude = .07f;
            [Range(.2f, 2)] public float waveSpeed = .42f;
            [Range(.1f, 2)] public float settleTime = 1.1f;
            [Range(0, 1)] public float clothFreedom = .2f;
            [Range(.02f, .5f)] public float damping = .28f;
            [Range(.02f, .8f)] public float bendStiffness = .3f;
            [Range(0, 1)] public float contactRebound = .35f;
            [Range(.08f, .4f)] public float contactResponse = .18f;
            [Range(.3f, 4f)] public float walkSpeed = 2.25f;
            [Range(0, 1)] public float walkStride = 1f;
            [Range(1, 8)] public float runSpeed = 4.5f;

            public void Clamp()
            {
                windStrength = Safe(windStrength, 0, 2, 1);
                lift = Safe(lift, 0, 1, .8f);
                wingSpread = Safe(wingSpread, 0, 1, .85f);
                wingFlap = Safe(wingFlap, 0, 1, 1f);
                sCurve = Safe(sCurve, 0, 1, .7f);
                waveAmplitude = Safe(waveAmplitude, 0, .12f, .07f);
                waveSpeed = Safe(waveSpeed, .2f, 2, .42f);
                settleTime = Safe(settleTime, .1f, 2, 1.1f);
                clothFreedom = Safe(clothFreedom, 0, 1, .2f);
                damping = Safe(damping, .02f, .5f, .28f);
                bendStiffness = Safe(bendStiffness, .02f, .8f, .3f);
                contactRebound = Safe(contactRebound, 0, 1, .35f);
                contactResponse = Safe(contactResponse, .08f, .4f, .18f);
                walkSpeed = Safe(walkSpeed, .3f, 4f, 2.25f);
                walkStride = Safe(walkStride, 0, 1, 1);
                runSpeed = Safe(runSpeed, 1, 8, 4.5f);
            }
            static float Safe(float value, float low, float high, float fallback) =>
                float.IsNaN(value) || float.IsInfinity(value) ? fallback : Mathf.Clamp(value, low, high);
        }

        public Cloth cloth;
        public Transform simulationRoot;
        public Vector3[] restParticles;
        public float[] freedomWeights;
        public ForestSpriteSecondaryMotion secondaryMotion;
        public SphereCollider floorCollision;
        public Transform[] flowUpper, flowLower;
        public Vector3[] flowAxes;
        public float[] flowInfluence;
        public Transform guideFrame;
        public Transform[] guideNodes;
        public Vector3[] guideRestPoints;
        public int guideSegments = 6;
        public int[] guideEdgeVertices;
        public float[] guideEdgeRestLengths;
        public int GuideDirectionCount => guideNodes == null ? 0 : guideNodes.Length / (guideSegments + 1);
        public bool UsesGuides => guideFrame != null && guideSegments == 6
            && (GuideDirectionCount == 4 || GuideDirectionCount == 6)
            && guideNodes.Length == GuideDirectionCount * (guideSegments + 1)
            && guideRestPoints?.Length == guideNodes.Length;
        public float[] GuideTangentDegrees { get; private set; }
        public float GuideMaxSegmentError { get; private set; }
        public float GuideTargetStretchP95 { get; private set; }
        public float GuideTargetStretchMaximum { get; private set; }
        public Settings settings = new Settings();
        public ForestSpriteCapeContact Contact { get; private set; }
        public bool MotionEnabled => motionEnabled;
        public bool WindPreview => windPreview;
        public float FlowAmount => flowAmount;
        public float UpperBendDegrees { get; private set; }
        public float LowerBendDegrees { get; private set; }
        public float WingSpreadDegrees { get; private set; }
        public float RunBlend => runBlend;
        public string SettingsPath => Path.Combine(Application.persistentDataPath, "ForestSprite", "cape-flow-v1.json");

        Vector3 previousPosition, filteredVelocity;
        bool motionEnabled = true, windPreview, posed;
        float flowAmount, wavePhase, runBlend, appliedFreedom = -1;
        Settings smooth;
        Quaternion[] baseUpper, baseLower;
        ClothSkinningCoefficient[] coefficients;
        ForestSpriteDemoController locomotion;
        bool guidePosed;
        SkinnedMeshRenderer guideSkin;
        Vector3[] guideVertices, guideSkinned;
        BoneWeight[] guideWeights;
        Matrix4x4[] guideBindposes, guideMatrices;
        float[] guideEdgeRatios;

        // Broad travelling waves: walking approaches horizontal; running lifts the rear wing above it.
        static readonly float[,] WalkCapeMean = {
            {0f,65f,45f,50f,85f,100f}, {0f,29.75f,21.25f,38.25f,59.5f,72.25f},
            {0f,0f,0f,2f,6f,10f}, {0f,8.5f,14.875f,32.9375f,55.0375f,67.15f}
        };
        static readonly float[,] WalkCapeAmplitude = {
            {0f,3f,5f,8f,8f,6f}, {0f,1f,2f,4f,5f,4f},
            {0f,0f,0f,.5f,1f,1f}, {0f,.5f,2f,4f,4.5f,3.5f}
        };
        static readonly float[,] RunCapeMean = {
            {0f,90f,65f,80f,140f,150f}, {0f,45f,35f,55f,80f,95f},
            {0f,0f,0f,2f,6f,10f}, {0f,10f,17.5f,38.75f,64.75f,79f}
        };
        static readonly float[,] RunCapeAmplitude = {
            {0f,5f,8f,14f,16f,12f}, {0f,3f,6f,12f,14f,10f},
            {0f,0f,0f,.5f,1f,1f}, {0f,1f,4.5f,10.75f,13.25f,10f}
        };

        void OnEnable()
        {
            if (simulationRoot != null) previousPosition = simulationRoot.position;
        }
        void Start()
        {
            ApplySettings();
            if (File.Exists(SettingsPath))
            {
                try { LoadSettings(); }
                catch (Exception exception) { Debug.LogWarning("披风参数未载入：" + exception.Message, this); }
            }
        }
        void OnDisable() { RestorePose(); }
        void OnValidate() { if (settings != null) settings.Clamp(); }

        void Update()
        {
            // Restore before Animator evaluates this frame; never accumulate last frame's bend.
            RestorePose();
            // The terrain adapter supplies its sampled ground height later in LateUpdate.
            if (floorCollision != null && simulationRoot != null)
                floorCollision.transform.position = new Vector3(simulationRoot.position.x, -25f, simulationRoot.position.z);
        }

        void UpdateFlow()
        {
            if (simulationRoot == null || cloth == null) return;
            if (smooth == null) ApplySettings();
            bool desired = secondaryMotion == null || secondaryMotion.MotionEnabled;
            if (desired != motionEnabled) SetMotionEnabled(desired);
            float dt = Time.deltaTime;
            Vector3 delta = simulationRoot.position - previousPosition;
            previousPosition = simulationRoot.position;
            if (dt <= 0) return;
            float teleportDistance = Mathf.Max(1f, (locomotion == null ? 4f : Mathf.Max(locomotion.walkSpeed, locomotion.runSpeed)) * dt * 2.5f);
            if (delta.sqrMagnitude > teleportDistance * teleportDistance)
            {
                ResetSimulation();
                return;
            }
            SmoothSettings(1f - Mathf.Exp(-dt / .18f));
            UpdateClothSettings();
            // Read this frame's collision-resolved motion after the controller has consumed all of its time.
            Vector3 velocity = locomotion == null ? delta / dt : locomotion.ActualVelocity;
            filteredVelocity = Vector3.Lerp(filteredVelocity, velocity, 1f - Mathf.Exp(-7f * dt));
            Vector3 horizontal = Vector3.ProjectOnPlane(filteredVelocity, Vector3.up);
            string state = locomotion == null ? "" : locomotion.CurrentState;
            // Carry the last gait through a jump or stop; flowAmount handles the gradual settling.
            bool running = windPreview || state == "Run" || ((state == "Jump" || state == "Idle") && runBlend > .5f);
            runBlend = Mathf.Lerp(runBlend, running ? 1f : 0f, 1f - Mathf.Exp(-dt / .3f));
            float referenceSpeed = locomotion == null ? 1.6f : running ? locomotion.runSpeed : locomotion.walkSpeed;
            float speed = windPreview ? 1f : Mathf.Clamp01(horizontal.magnitude / Mathf.Max(.1f, referenceSpeed));
            float jumpOpen = locomotion != null ? locomotion.JumpOpenness : 0f;
            float target = motionEnabled ? Mathf.Max(speed, jumpOpen) * smooth.windStrength : 0;
            float response = target > flowAmount ? .22f : smooth.settleTime / 3f;
            flowAmount = Mathf.Lerp(flowAmount, target, 1f - Mathf.Exp(-dt / response));
            wavePhase += dt * smooth.waveSpeed * Mathf.Lerp(.8f, 1.5f, runBlend) * Mathf.PI * 2f;
            // The bones define the flowing silhouette. Cloth supplies restrained lag and contact.
            cloth.externalAcceleration = Vector3.up * (7.1f + flowAmount * .8f)
                - simulationRoot.forward * (flowAmount * 1.2f);
        }

        void LateUpdate()
        {
            UpdateFlow();
            if (!motionEnabled || smooth == null || flowUpper == null || flowUpper.Length == 0) return;
            if (baseUpper == null || baseUpper.Length != flowUpper.Length)
            {
                baseUpper = new Quaternion[flowUpper.Length];
                baseLower = new Quaternion[flowUpper.Length];
            }
            if (UsesGuides)
            {
                // Retain old metadata for capture compatibility; these original bones are no longer driven.
                for (int i = 0; i < flowUpper.Length; i++)
                {
                    baseUpper[i] = flowUpper[i].localRotation;
                    baseLower[i] = flowLower[i].localRotation;
                }
                ApplyGuidePose();
                return;
            }
            Vector3 shape = FlowShape(wavePhase);
            UpperBendDegrees = shape.x;
            LowerBendDegrees = shape.y;
            WingSpreadDegrees = shape.z;
            for (int i = 0; i < flowUpper.Length; i++)
            {
                baseUpper[i] = flowUpper[i].localRotation;
                baseLower[i] = flowLower[i].localRotation;
                // Side/back panels stream rearward. The small front closure stays clear of the torso.
                Quaternion spread = Quaternion.AngleAxis(WingSpreadDegrees * flowAxes[i].z, simulationRoot.forward);
                Vector3 rearwardAxis = flowAxes[i].x < -.5f ? -simulationRoot.right : simulationRoot.right;
                // The hem follows the shoulder wave; the side panels follow the centre a little later.
                float sideDelay = Mathf.Abs(flowAxes[i].z) * .35f;
                Vector3 delayed = FlowShape(wavePhase - sideDelay);
                flowUpper[i].rotation = Quaternion.AngleAxis(delayed.x * flowInfluence[i], rearwardAxis) * spread * flowUpper[i].rotation;
                flowLower[i].rotation = Quaternion.AngleAxis(delayed.y * flowInfluence[i], rearwardAxis) * flowLower[i].rotation;
            }
            posed = true;
        }

        Vector3 FlowShape(float phase)
        {
            float moving = Mathf.Clamp01(flowAmount);
            float upperWave = smooth.waveAmplitude * 200f * Mathf.Lerp(.35f, 1f, runBlend) * moving;
            float upperMean = flowAmount * Mathf.Lerp(10f + smooth.lift * 20f, 26f + smooth.lift * 30f, runBlend);
            // Reserve room for the wave even at maximum wind, instead of flattening its peaks.
            upperMean = Mathf.Clamp(upperMean, upperWave, 75f - upperWave);
            float upper = upperMean + Mathf.Sin(phase) * upperWave;
            float lower = -flowAmount * smooth.sCurve * Mathf.Lerp(9f, 16f, runBlend)
                + Mathf.Sin(phase - 1.8f) * smooth.waveAmplitude * 380f * Mathf.Lerp(.25f, 1f, runBlend) * moving;
            lower = Mathf.Clamp(lower, -upper * .8f, upper * .6f);
            float opening = Mathf.Lerp(.25f, .65f, runBlend)
                + Mathf.Sin(wavePhase - .6f) * Mathf.Lerp(.06f, .35f, runBlend) * smooth.wingFlap;
            float wing = moving * smooth.wingSpread * 40f * opening;
            return new Vector3(upper, lower, wing);
        }

        void RestorePose()
        {
            if (guidePosed)
            {
                for (int i = 0; i < guideNodes.Length; i++)
                    guideNodes[i].SetLocalPositionAndRotation(guideRestPoints[i], Quaternion.identity);
                guidePosed = false;
            }
            if (!posed) return;
            for (int i = 0; i < baseUpper.Length; i++)
            {
                flowUpper[i].localRotation = baseUpper[i];
                flowLower[i].localRotation = baseLower[i];
            }
            posed = false;
        }

        void ApplyGuidePose()
        {
            if (GuideTangentDegrees == null || GuideTangentDegrees.Length != GuideDirectionCount * guideSegments)
                GuideTangentDegrees = new float[GuideDirectionCount * guideSegments];
            float moving = Mathf.Clamp01(flowAmount);
            float jumpOpen = locomotion != null ? locomotion.JumpOpenness : 0f;
            float gait = Mathf.Max(runBlend, jumpOpen);
            float opening = 14f + Mathf.Lerp(3f, 10f, gait) * Mathf.Sin(wavePhase - .6f) * smooth.wingFlap;
            WingSpreadDegrees = moving * smooth.wingSpread / .85f * Mathf.Lerp(opening, 9f, jumpOpen);
            Vector3 right = guideFrame.InverseTransformDirection(simulationRoot.right).normalized;
            Vector3 forward = guideFrame.InverseTransformDirection(simulationRoot.forward).normalized;
            GuideMaxSegmentError = 0;
            for (int d = 0; d < GuideDirectionCount; d++)
            {
                int first = d * (guideSegments + 1);
                Vector3 point = guideRestPoints[first];
                guideNodes[first].SetLocalPositionAndRotation(point, Quaternion.identity);
                int family = d == 0 ? 0 : d == 3 ? 2 : d >= 4 ? 3 : 1;
                // Imported Cape_Left is physically +X; use the actual material path, not its name.
                float side = d == 1 || d == 2 || d >= 4
                    ? Mathf.Sign(Vector3.Dot(guideRestPoints[first + guideSegments] - guideRestPoints[first], right)) : 0f;
                Quaternion previous = Quaternion.identity;
                for (int s = 0; s < guideSegments; s++)
                {
                    float u = (s + .5f) / guideSegments;
                    // Spread keeps its envelope; the material arc uses the full six-segment profile.
                    float envelope = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.25f, .55f, u));
                    float mean = Mathf.Lerp(WalkCapeMean[family,s], RunCapeMean[family,s], gait);
                    float wave = Mathf.Lerp(WalkCapeAmplitude[family,s], RunCapeAmplitude[family,s], gait)
                        * Mathf.Sin(wavePhase - s * 1.05f);
                    float theta = moving * Mathf.Lerp(.5f, 1.125f, smooth.lift) *
                        (mean * smooth.sCurve / .7f + wave * smooth.waveAmplitude / .07f);
                    if (s > 0) theta += jumpOpen * 12f * u;
                    GuideTangentDegrees[d * guideSegments + s] = theta;
                    Quaternion rotation = Quaternion.AngleAxis(theta, d == 3 ? -right : right)
                        * Quaternion.AngleAxis(WingSpreadDegrees * side * envelope, forward);
                    Vector3 restSegment = guideRestPoints[first + s + 1] - guideRestPoints[first + s];
                    Vector3 next = point + rotation * restSegment;
                    if (s > 1) guideNodes[first + s].localRotation = Quaternion.Slerp(previous, rotation, .5f);
                    guideNodes[first + s + 1].SetLocalPositionAndRotation(next, rotation);
                    GuideMaxSegmentError = Mathf.Max(GuideMaxSegmentError,
                        Mathf.Abs(Vector3.Distance(next, point) - restSegment.magnitude));
                    previous = rotation;
                    point = next;
                }
                // Keep the root rotation continuous with the fixed collar.
                guideNodes[first + 1].localRotation = Quaternion.identity;
            }
            UpperBendDegrees = GuideTangentDegrees[1];
            LowerBendDegrees = GuideTangentDegrees[4] - UpperBendDegrees;
            guidePosed = true;
            if (Contact != null) Contact.ApplyGuideResponse(Time.deltaTime);
            MeasureGuideTargetStretch();
        }

        void MeasureGuideTargetStretch()
        {
            if (guideEdgeRestLengths == null || guideEdgeRestLengths.Length == 0) return;
            if (guideSkin == null)
            {
                guideSkin = GetComponent<SkinnedMeshRenderer>();
                guideVertices = guideSkin.sharedMesh.vertices;
                guideWeights = guideSkin.sharedMesh.boneWeights;
                guideBindposes = guideSkin.sharedMesh.bindposes;
                guideMatrices = new Matrix4x4[guideSkin.bones.Length];
                guideSkinned = new Vector3[guideVertices.Length];
                guideEdgeRatios = new float[guideEdgeRestLengths.Length];
            }
            for (int i = 0; i < guideMatrices.Length; i++)
                guideMatrices[i] = guideSkin.bones[i].localToWorldMatrix * guideBindposes[i];
            for (int i = 0; i < guideVertices.Length; i++)
            {
                var w = guideWeights[i]; var v = guideVertices[i];
                guideSkinned[i] = guideMatrices[w.boneIndex0].MultiplyPoint3x4(v) * w.weight0
                    + guideMatrices[w.boneIndex1].MultiplyPoint3x4(v) * w.weight1
                    + guideMatrices[w.boneIndex2].MultiplyPoint3x4(v) * w.weight2
                    + guideMatrices[w.boneIndex3].MultiplyPoint3x4(v) * w.weight3;
            }
            for (int i = 0; i < guideEdgeRatios.Length; i++)
                guideEdgeRatios[i] = Vector3.Distance(guideSkinned[guideEdgeVertices[i * 2]],
                    guideSkinned[guideEdgeVertices[i * 2 + 1]]) / guideEdgeRestLengths[i];
            Array.Sort(guideEdgeRatios);
            GuideTargetStretchP95 = guideEdgeRatios[Mathf.FloorToInt((guideEdgeRatios.Length - 1) * .95f)];
            GuideTargetStretchMaximum = guideEdgeRatios[guideEdgeRatios.Length - 1];
        }
        public void ApplySettings()
        {
            if (settings == null) settings = new Settings();
            settings.Clamp();
            if (Application.isPlaying && Contact == null)
            {
                Contact = GetComponent<ForestSpriteCapeContact>();
                if (Contact == null) Contact = gameObject.AddComponent<ForestSpriteCapeContact>();
                Contact.cape = this;
            }
            if (locomotion == null) locomotion = GetComponentInParent<ForestSpriteDemoController>();
            if (locomotion != null)
            {
                locomotion.walkSpeed = settings.walkSpeed;
                locomotion.walkStride = settings.walkStride;
                locomotion.runSpeed = settings.runSpeed;
            }
            if (smooth == null || !Application.isPlaying)
                smooth = JsonUtility.FromJson<Settings>(JsonUtility.ToJson(settings));
            UpdateClothSettings();
        }
        void SmoothSettings(float t)
        {
            smooth.windStrength = Mathf.Lerp(smooth.windStrength, settings.windStrength, t);
            smooth.lift = Mathf.Lerp(smooth.lift, settings.lift, t);
            smooth.wingSpread = Mathf.Lerp(smooth.wingSpread, settings.wingSpread, t);
            smooth.wingFlap = Mathf.Lerp(smooth.wingFlap, settings.wingFlap, t);
            smooth.sCurve = Mathf.Lerp(smooth.sCurve, settings.sCurve, t);
            smooth.waveAmplitude = Mathf.Lerp(smooth.waveAmplitude, settings.waveAmplitude, t);
            smooth.waveSpeed = Mathf.Lerp(smooth.waveSpeed, settings.waveSpeed, t);
            smooth.settleTime = Mathf.Lerp(smooth.settleTime, settings.settleTime, t);
            smooth.clothFreedom = Mathf.Lerp(smooth.clothFreedom, settings.clothFreedom, t);
            smooth.damping = Mathf.Lerp(smooth.damping, settings.damping, t);
            smooth.bendStiffness = Mathf.Lerp(smooth.bendStiffness, settings.bendStiffness, t);
            smooth.contactRebound = Mathf.Lerp(smooth.contactRebound, settings.contactRebound, t);
            smooth.contactResponse = Mathf.Lerp(smooth.contactResponse, settings.contactResponse, t);
        }
        void UpdateClothSettings()
        {
            if (cloth == null || smooth == null) return;
            if (Contact != null)
            {
                Contact.rebound = smooth.contactRebound;
                Contact.responseTime = smooth.contactResponse;
            }
            cloth.damping = UsesGuides ? Mathf.Clamp(smooth.damping * .5f, .02f, .2f) : smooth.damping;
            // Soft bending and resistance to stretching are separate: preserve the fabric area.
            cloth.stretchingStiffness = UsesGuides ? 1f : .4f;
            if (UsesGuides) cloth.useTethers = true;
            cloth.clothSolverFrequency = 480;
            cloth.worldVelocityScale = .12f;
            cloth.worldAccelerationScale = .08f;
            // Guide bones supply the broad wave; native bending only resists local folds.
            cloth.bendingStiffness = smooth.bendStiffness * (UsesGuides ? .2f : .1f);
            if (freedomWeights == null || freedomWeights.Length == 0) return;
            if (Mathf.Abs(smooth.clothFreedom - appliedFreedom) < .00001f) return;
            if (coefficients == null || coefficients.Length != freedomWeights.Length) coefficients = cloth.coefficients;
            float top = float.NegativeInfinity, bottom = float.PositiveInfinity;
            foreach (var point in restParticles)
            {
                top = Mathf.Max(top, point.y);
                bottom = Mathf.Min(bottom, point.y);
            }
            for (int i = 0; i < coefficients.Length; i++)
            {
                coefficients[i].maxDistance = (UsesGuides ? Mathf.Lerp(.025f, .3f, smooth.clothFreedom)
                    : Mathf.Lerp(.015f, .05f, smooth.clothFreedom)) * freedomWeights[i];
                if (UsesGuides && freedomWeights[i] > 0)
                {
                    // Give the open shoulder edge room to slide around the arm while the collar stays pinned.
                    float depth = Mathf.InverseLerp(top, bottom, restParticles[i].y);
                    float shoulder = Mathf.Lerp(.075f, .2f, smooth.clothFreedom)
                        * Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.07f, .11f, depth))
                        * (1f - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.30f, .55f, depth)));
                    coefficients[i].maxDistance = Mathf.Max(coefficients[i].maxDistance, shoulder);
                }
            }
            cloth.coefficients = coefficients;
            appliedFreedom = smooth.clothFreedom;
        }
        public void SaveSettings() { SaveSettings(SettingsPath); }
        public void LoadSettings() { LoadSettings(SettingsPath); }
        public void SaveSettings(string path)
        {
            settings.Clamp();
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, JsonUtility.ToJson(settings, true));
        }
        public void LoadSettings(string path)
        {
            var loaded = ReadSettings(File.ReadAllText(path));
            if (loaded == null) throw new InvalidDataException("披风参数为空");
            settings = loaded;
            ApplySettings();
        }
        public static Settings ReadSettings(string json)
        {
            var loaded = JsonUtility.FromJson<Settings>(json);
            // Existing saved files keep every prior value and gain the new wing control in memory only.
            if (loaded != null && json.IndexOf("\"wingSpread\"", StringComparison.Ordinal) < 0) loaded.wingSpread = new Settings().wingSpread;
            if (loaded != null && json.IndexOf("\"wingFlap\"", StringComparison.Ordinal) < 0) loaded.wingFlap = new Settings().wingFlap;
            if (loaded != null && json.IndexOf("\"contactRebound\"", StringComparison.Ordinal) < 0) loaded.contactRebound = new Settings().contactRebound;
            if (loaded != null && json.IndexOf("\"contactResponse\"", StringComparison.Ordinal) < 0) loaded.contactResponse = new Settings().contactResponse;
            return loaded;
        }
        public void ResetSettings() { settings = new Settings(); ApplySettings(); }
        public void ApplyWingedPreset()
        {
            var previous = settings ?? new Settings();
            settings = new Settings { walkSpeed = previous.walkSpeed, walkStride = previous.walkStride, runSpeed = previous.runSpeed };
            ApplySettings();
        }
        public void SetWindPreview(bool value) { windPreview = value; }
        public void SetMotionEnabled(bool value)
        {
            motionEnabled = value;
            if (cloth != null) cloth.enabled = value;
            ResetSimulation();
        }
        public void ResetSimulation()
        {
            RestorePose();
            if (Contact != null) Contact.ResetResponse();
            filteredVelocity = Vector3.zero;
            flowAmount = 0;
            runBlend = 0;
            wavePhase = 0;
            UpperBendDegrees = LowerBendDegrees = WingSpreadDegrees = 0;
            if (simulationRoot != null) previousPosition = simulationRoot.position;
            if (cloth == null) return;
            cloth.ClearTransformMotion();
            if (motionEnabled) { cloth.enabled = false; cloth.enabled = true; }
        }
    }
}
