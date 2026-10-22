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
            [Range(0, 1)] public float sCurve = .7f;
            [Range(0, .12f)] public float waveAmplitude = .07f;
            [Range(.2f, 2)] public float waveSpeed = .42f;
            [Range(.1f, 2)] public float settleTime = 1.1f;
            [Range(0, 1)] public float clothFreedom = .2f;
            [Range(.02f, .5f)] public float damping = .28f;
            [Range(.02f, .8f)] public float bendStiffness = .3f;
            [Range(.3f, 2f)] public float walkSpeed = 1.5f;
            [Range(0, 1)] public float walkStride = 1f;
            [Range(1, 4)] public float runSpeed = 3f;

            public void Clamp()
            {
                windStrength = Safe(windStrength, 0, 2, 1);
                lift = Safe(lift, 0, 1, .8f);
                wingSpread = Safe(wingSpread, 0, 1, .85f);
                sCurve = Safe(sCurve, 0, 1, .7f);
                waveAmplitude = Safe(waveAmplitude, 0, .12f, .07f);
                waveSpeed = Safe(waveSpeed, .2f, 2, .42f);
                settleTime = Safe(settleTime, .1f, 2, 1.1f);
                clothFreedom = Safe(clothFreedom, 0, 1, .2f);
                damping = Safe(damping, .02f, .5f, .28f);
                bendStiffness = Safe(bendStiffness, .02f, .8f, .3f);
                walkSpeed = Safe(walkSpeed, .3f, 2f, 1.5f);
                walkStride = Safe(walkStride, 0, 1, 1);
                runSpeed = Safe(runSpeed, 1, 4, 3f);
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
        public Settings settings = new Settings();
        public bool MotionEnabled => motionEnabled;
        public bool WindPreview => windPreview;
        public float FlowAmount => flowAmount;
        public float UpperBendDegrees { get; private set; }
        public float LowerBendDegrees { get; private set; }
        public float WingSpreadDegrees { get; private set; }
        public string SettingsPath => Path.Combine(Application.persistentDataPath, "ForestSprite", "cape-flow-v1.json");

        Vector3 previousPosition, filteredVelocity;
        bool motionEnabled = true, windPreview, posed;
        float flowAmount, wavePhase, appliedFreedom = -1;
        Settings smooth;
        Quaternion[] baseUpper, baseLower;
        ClothSkinningCoefficient[] coefficients;
        ForestSpriteDemoController locomotion;

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
            if (simulationRoot == null || cloth == null) return;
            if (smooth == null) ApplySettings();
            if (floorCollision != null)
                floorCollision.transform.position = new Vector3(simulationRoot.position.x, -25f, simulationRoot.position.z);
            bool desired = secondaryMotion == null || secondaryMotion.MotionEnabled;
            if (desired != motionEnabled) SetMotionEnabled(desired);
            float dt = Time.deltaTime;
            Vector3 delta = simulationRoot.position - previousPosition;
            previousPosition = simulationRoot.position;
            if (dt <= 0 || dt > .2f || delta.sqrMagnitude > .75f * .75f)
            {
                ResetSimulation();
                return;
            }
            SmoothSettings(1f - Mathf.Exp(-dt / .18f));
            UpdateClothSettings();
            filteredVelocity = Vector3.Lerp(filteredVelocity, delta / dt, 1f - Mathf.Exp(-7f * dt));
            Vector3 horizontal = Vector3.ProjectOnPlane(filteredVelocity, Vector3.up);
            float speed = windPreview ? 1f : Mathf.Clamp01(horizontal.magnitude / 1.6f);
            float target = motionEnabled ? speed * smooth.windStrength : 0;
            float response = target > flowAmount ? .22f : smooth.settleTime / 3f;
            flowAmount = Mathf.Lerp(flowAmount, target, 1f - Mathf.Exp(-dt / response));
            wavePhase += dt * smooth.waveSpeed * Mathf.PI * 2f;
            // The bones define the flowing silhouette. Cloth supplies restrained lag and contact.
            cloth.externalAcceleration = Vector3.up * (7.1f + flowAmount * .8f)
                - simulationRoot.forward * (flowAmount * 1.2f);
        }

        void LateUpdate()
        {
            if (!motionEnabled || smooth == null || flowUpper == null || flowUpper.Length == 0) return;
            if (baseUpper == null || baseUpper.Length != flowUpper.Length)
            {
                baseUpper = new Quaternion[flowUpper.Length];
                baseLower = new Quaternion[flowUpper.Length];
            }
            // Keep a rearward carrier shape; the delayed wave bends that sheet without folding it forward.
            float ripple = smooth.waveAmplitude * 120f;
            UpperBendDegrees = Mathf.Clamp(flowAmount * (25f + smooth.lift * 35f + Mathf.Sin(wavePhase) * ripple), 0f, 75f);
            LowerBendDegrees = Mathf.Clamp(flowAmount * (-smooth.sCurve * 20f + Mathf.Sin(wavePhase - 1.8f) * ripple * .8f),
                -UpperBendDegrees * .45f, UpperBendDegrees * .2f);
            WingSpreadDegrees = Mathf.Clamp01(flowAmount) * smooth.wingSpread * 40f;
            for (int i = 0; i < flowUpper.Length; i++)
            {
                baseUpper[i] = flowUpper[i].localRotation;
                baseLower[i] = flowLower[i].localRotation;
                // Side/back panels stream rearward. The small front closure stays clear of the torso.
                Quaternion spread = Quaternion.AngleAxis(WingSpreadDegrees * flowAxes[i].z, simulationRoot.forward);
                Vector3 rearwardAxis = flowAxes[i].x < -.5f ? -simulationRoot.right : simulationRoot.right;
                // A broad wave travels from shoulder to hem. Both wings share the same phase
                // to keep one sheet, with a small delay relative to the centre panel.
                float sideDelay = Mathf.Abs(flowAxes[i].z) * .35f;
                float delayedUpper = Mathf.Clamp(flowAmount * (25f + smooth.lift * 35f +
                    Mathf.Sin(wavePhase - sideDelay) * ripple), 0, 75f);
                float delayedLower = Mathf.Clamp(flowAmount * (-smooth.sCurve * 20f +
                    Mathf.Sin(wavePhase - 1.8f - sideDelay) * ripple * .8f),
                    -delayedUpper * .45f, delayedUpper * .2f);
                flowUpper[i].rotation = Quaternion.AngleAxis(delayedUpper * flowInfluence[i], rearwardAxis) * spread * flowUpper[i].rotation;
                flowLower[i].rotation = Quaternion.AngleAxis(delayedLower * flowInfluence[i], rearwardAxis) * flowLower[i].rotation;
            }
            posed = true;
        }

        void RestorePose()
        {
            if (!posed) return;
            for (int i = 0; i < baseUpper.Length; i++)
            {
                flowUpper[i].localRotation = baseUpper[i];
                flowLower[i].localRotation = baseLower[i];
            }
            posed = false;
        }
        public void ApplySettings()
        {
            if (settings == null) settings = new Settings();
            settings.Clamp();
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
            smooth.sCurve = Mathf.Lerp(smooth.sCurve, settings.sCurve, t);
            smooth.waveAmplitude = Mathf.Lerp(smooth.waveAmplitude, settings.waveAmplitude, t);
            smooth.waveSpeed = Mathf.Lerp(smooth.waveSpeed, settings.waveSpeed, t);
            smooth.settleTime = Mathf.Lerp(smooth.settleTime, settings.settleTime, t);
            smooth.clothFreedom = Mathf.Lerp(smooth.clothFreedom, settings.clothFreedom, t);
            smooth.damping = Mathf.Lerp(smooth.damping, settings.damping, t);
            smooth.bendStiffness = Mathf.Lerp(smooth.bendStiffness, settings.bendStiffness, t);
        }
        void UpdateClothSettings()
        {
            if (cloth == null || smooth == null) return;
            cloth.damping = smooth.damping;
            // Soft bending and resistance to stretching are separate: preserve the fabric area.
            cloth.stretchingStiffness = .4f;
            cloth.clothSolverFrequency = 480;
            cloth.worldVelocityScale = .12f;
            cloth.worldAccelerationScale = .08f;
            // Broad waves come from the cape bones; local fabric should not break that silhouette into tiny folds.
            cloth.bendingStiffness = smooth.bendStiffness * .1f;
            if (freedomWeights == null || freedomWeights.Length == 0) return;
            if (Mathf.Abs(smooth.clothFreedom - appliedFreedom) < .00001f) return;
            if (coefficients == null || coefficients.Length != freedomWeights.Length) coefficients = cloth.coefficients;
            for (int i = 0; i < coefficients.Length; i++)
                coefficients[i].maxDistance = Mathf.Lerp(.015f, .05f, smooth.clothFreedom) * freedomWeights[i];
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
            filteredVelocity = Vector3.zero;
            flowAmount = 0;
            UpperBendDegrees = LowerBendDegrees = WingSpreadDegrees = 0;
            if (simulationRoot != null) previousPosition = simulationRoot.position;
            if (cloth == null) return;
            cloth.ClearTransformMotion();
            if (motionEnabled) { cloth.enabled = false; cloth.enabled = true; }
        }
    }
}
