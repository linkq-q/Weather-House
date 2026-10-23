using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.LowLevel;
using Object = UnityEngine.Object;

namespace ForestSpriteCharacter.Editor
{
    public static class ForestSpriteDemoChecks
    {
        [Serializable]
        sealed class Report
        {
            public string time, scene, result, evidence, error;
            public string[] clips, observedStates;
            public float[] captureTimes;
            public int[] capturePhases;
            public int renderers, vertices, weightedVertices, bones, secondaryChains, runtimeSamples, clothParticles, pinnedParticles, freeParticles, capturedFrames;
            public float walkDistance, runDistance, jumpRise, springPeakAngle;
            public float walkFootSpan, runFootSpan;
            public float maximumClothDisplacement, maximumPinnedError, maximumCollisionPenetration, initialPinnedPenetration;
            public int motionConstraintViolations, motionViolationFrames, motionPeakPhase = -1, motionPeakParticle = -1;
            public float motionMaxExcess, motionViolationTime, motionLongestViolationTime, motionPeakPhaseTime;
            public string motionPeakDetails;
            public float turnDegrees, earlySettleSpeed, lateSettleSpeed;
            public float runUpperBend, runLowerBend, runUpperBoneRotation, runLowerBoneRotation;
            public bool usesGuides;
            public float guideMaximumSegmentError, guideTargetStretchP95, guideTargetStretchMaximum, tunedGuideTangentChange;
            public float[] runGuideCurvatureAmplitude, runGuideCurvaturePeakPhase, runGuideCurvatureRSquared;
            public float runGuidePhaseSpread;
            public int runSShapeSamples, strongWindSamples;
            public float windPreviewUpperBend, tunedUpperBend, tunedBoneRotation, tunedClothShapeDifference;
            public float strongWindUpperBend, strongWindLowerBend;
            public float strongWindWingMinimum, strongWindWingMaximum;
            public bool strongWindOpened, strongWindClosed;
            public float windPreviewWingSpread, tunedWingSpread;
            public int rearHemParticles, rearHemSamples;
            public float maximumRearHemLocalZ, minimumRearHemFootRearClearance;
            public bool legacySettingsMigration, legacyWingFlapMigration;
            public float idleFootHeight, walkFootHeight, runFootHeight, maximumGroundedFootError, idleCharacterHeight;
            public List<GaitFootSample> gaitFootSamples = new List<GaitFootSample>();
            public int walkCompleteFootCycles, runCompleteFootCycles;
            public bool groundedAfterJump, settingsRoundTrip, recommendedSettingsRestored, tuningKeptSimulation;
        }

        [Serializable]
        sealed class GaitFootSample
        {
            public string state;
            public int frame;
            public float normalizedTime, phaseTime, leftWorldHeight, rightWorldHeight;
        }

        static ForestSpriteDemoController player;
        static Transform strideFoot;
        static float[] gaitMinimum, gaitMaximum;
        static Report runtime;
        static readonly HashSet<string> observed = new HashSet<string>();
        static readonly List<float> captureTimes = new List<float>();
        static readonly List<int> capturePhases = new List<int>();
        static float phaseStarted, baseY;
        static Vector3 previousPosition, restorePosition;
        static Quaternion restoreRotation;
        static ForestSpriteDemoController.Demonstration restoreDemo;
        static bool restoreMotion, restoreCloth, phaseCaptured;
        static bool restoreWindPreview;
        static string restoreCapeSettings;
        static Quaternion initialUpper, initialLower, windPreviewUpper;
        static Vector3[] windPreviewParticles;
        static float[] windPreviewGuideTangents;
        static readonly List<float> guideRunPhases = new List<float>();
        static readonly List<float[]> guideRunCurvatures = new List<float[]>();
        static int phase, sampledFrame, initialSimulationSteps, frameCaptureIndex;
        static float nextCapture, previousSampleTime, earlySettleSum, lateSettleSum;
        static float motionViolationStreak;
        static float previousStrongWindWing;
        static int earlySettleSamples, lateSettleSamples;
        static Quaternion turnStart;
        static SkinnedMeshRenderer clothSkin;
        static Vector3[] meshVertices, previousOffsets, previousAnchorsWorld, previousAnchorsLocal;
        static BoneWeight[] meshWeights;
        static Matrix4x4[] bindposes;
        static int[] clothToMesh;
        static int[] rearHemParticles, footVertices;
        static int[][] gaitFootVertices;
        static SkinnedMeshRenderer footSkin;
        static Vector3[] footMeshVertices;
        static BoneWeight[] footWeights;
        static Matrix4x4[] footBindposes;
        static ClothSkinningCoefficient[] coefficients;
        static PlayerLoopSystem originalPlayerLoop;
        static bool playerLoopInstalled;
        struct ClothSampleAfterFinish { }

        [MenuItem("Weather House/Forest Sprite/Validate Open Demo")]
        public static void ValidateOpen()
        {
            Require(SceneManager.GetActiveScene().path == ForestSpriteDemoBuilder.ScenePath, "Open the independent forest sprite scene first");
            Validate(ActiveDemoController());
        }

        static ForestSpriteDemoController ActiveDemoController() => SceneManager.GetActiveScene().GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<ForestSpriteDemoController>()).SingleOrDefault();

        public static void Validate(ForestSpriteDemoController preview)
        {
            Require(preview != null, "Demo controller exists");
            Require(preview.animator != null && preview.animator.runtimeAnimatorController != null, "Animator and controller assigned");
            var controllerAsset = preview.animator.runtimeAnimatorController as AnimatorController;
            Require(controllerAsset != null, "Persistent AnimatorController assigned");
            foreach (var entry in controllerAsset.layers[0].stateMachine.states)
            {
                Require(entry.state.motion != null && EditorUtility.IsPersistent(entry.state.motion),
                    "Saved animation motion for " + entry.state.name);
                if (entry.state.motion is BlendTree tree)
                    Require(tree.children.Length == 2 && tree.children.All(child => child.motion != null && EditorUtility.IsPersistent(child.motion)),
                        "Saved short and long Walk clips in BlendTree");
            }
            Require(preview.demoCamera != null && preview.visual != null, "Visual and demo camera assigned");
            Require(!preview.animator.applyRootMotion, "CharacterController owns displacement");
            Require(preview.GetComponent<CharacterController>() != null, "CharacterController exists");
            var clips = ForestSpriteDemoBuilder.LoadClips();
            foreach (string name in ForestSpriteDemoBuilder.ClipNames)
            {
                Require(clips.ContainsKey(name) && clips[name].length > .1f, "Nonempty clip " + name);
                Require(AnimationUtility.GetCurveBindings(clips[name]).Length > 0, "Animated curves in " + name);
            }
            Require(ForestSpriteDemoBuilder.StateName("ForestSpriteRig|Walk") == "Walk", "FBX action naming");
            Require(ForestSpriteDemoController.IsGait("Walk") && ForestSpriteDemoController.IsGait("Run") &&
                !ForestSpriteDemoController.IsGait("Jump"), "Only locomotion states share gait phase");
            Require(Mathf.Abs(preview.walkCycleDistance - .76f) < .0001f &&
                Mathf.Abs(preview.runCycleDistance - .34f / .22f) < .0001f,
                "Baked gait cycle distances match the longer planted-foot paths");
            var transforms = preview.visual.GetComponentsInChildren<Transform>();
            foreach (string bone in new[] { "Root", "Hips", "Spine", "Chest", "Neck", "Head", "UpperLeg.L", "LowerLeg.L", "Foot.L", "UpperLeg.R", "LowerLeg.R", "Foot.R" })
                Require(transforms.Any(t => t.name == bone), "Body bone " + bone);
            var skins = preview.GetComponentsInChildren<SkinnedMeshRenderer>().Where(s => s.enabled).ToArray();
            Require(skins.Length >= 4, "Body, hair, hat and cape are skinned");
            int vertices = 0, weighted = 0;
            foreach (var skin in skins)
            {
                Require(skin.sharedMesh != null && skin.bones.Length > 0 && skin.bones.All(b => b != null), "Bound mesh " + skin.name);
                var weights = skin.sharedMesh.boneWeights;
                vertices += skin.sharedMesh.vertexCount;
                Require(weights.Length == skin.sharedMesh.vertexCount, "Every vertex has skinning data: " + skin.name);
                foreach (var weight in weights)
                {
                    float sum = weight.weight0 + weight.weight1 + weight.weight2 + weight.weight3;
                    Require(!float.IsNaN(sum) && Mathf.Abs(sum - 1f) < .02f, "Normalized nonzero vertex weights: " + skin.name);
                    weighted++;
                }
                Require(skin.sharedMaterials.All(m => m != null && m.shader != null), "Material slots: " + skin.name);
            }
            Require(preview.secondaryMotion != null && preview.secondaryMotion.chains.Length == 4, "One hat and three hair spring chains");
            foreach (var chain in preview.secondaryMotion.chains)
            {
                Require(chain.bones != null && chain.bones.Length >= 3 && chain.bones.All(b => b != null), "Secondary chain endpoints");
                Require(chain.kind != ForestSpriteSecondaryMotion.MotionKind.Cape, "Cloth is the sole cape secondary-motion solver");
            }
            Require(preview.capeCloth != null && preview.capeCloth.cloth != null, "Native cape Cloth exists");
            var cloth = preview.capeCloth.cloth;
            Require(cloth.enabled && cloth.GetComponent<SkinnedMeshRenderer>().enabled, "Cape Cloth and renderer enabled");
            Require(Vector3.Distance(cloth.transform.lossyScale, Vector3.one) < .0001f, "Cape simulation uses metre-scale geometry");
            var particles = cloth.vertices;
            var constraints = cloth.coefficients;
            Require(particles.Length > 0 && constraints.Length == particles.Length && preview.capeCloth.restParticles.Length == particles.Length,
                "Cloth constraints use simulation particle indices, including welded UV seams");
            int pins = constraints.Count(c => c.maxDistance <= .00001f);
            int free = constraints.Count(c => c.maxDistance > .01f);
            Require(pins > 0 && free > 0, "Fixed collar and free cape particles");
            Require(cloth.sphereColliders.Any(p => p.first != null) || cloth.capsuleColliders.Any(c => c != null), "Explicit body colliders assigned");
            var cape = preview.capeCloth;
            var panel = preview.GetComponent<ForestSpriteCapePanel>();
            Require(panel != null && panel.enabled && panel.cape == cape, "Live cape adjustment panel targets this cape");
            Require(cape.flowUpper?.Length == 4 && cape.flowLower?.Length == 4 && cape.flowAxes?.Length == 4 && cape.flowInfluence?.Length == 4,
                "Four cape S-shape target chains and axes");
            for (int i = 0; i < 4; i++)
            {
                Require(cape.flowUpper[i] != null && cape.flowLower[i] != null && cape.flowLower[i].parent == cape.flowUpper[i], "Connected S-shape chain " + i);
                Require(cape.flowUpper[i].name.StartsWith("Cape_", StringComparison.Ordinal) && cape.flowLower[i].name.StartsWith("Cape_", StringComparison.Ordinal), "Only cape bones receive flow targets");
                Require(IsFinite(cape.flowAxes[i]) && Mathf.Abs(cape.flowAxes[i].magnitude - 1f) < .001f && cape.flowInfluence[i] > 0,
                    "Usable S-shape rotation axis and influence " + i);
            }
            Require(cape.flowUpper.Distinct().Count() == 4 && cape.flowLower.Distinct().Count() == 4, "Each cape flow target is unique");
            Require(cape.freedomWeights?.Length == particles.Length && cape.freedomWeights.All(w => w >= 0 && w <= 1.0001f), "Particle freedom masks preserve collar pins");
            if (cape.UsesGuides)
            {
                int guideCount = cape.guideNodes == null ? 0 : cape.guideNodes.Length / (cape.guideSegments + 1);
                var pairs = cloth.sphereColliders;
                var spheres = pairs.SelectMany(p => new[] { p.first, p.second }).Where(s => s != null).Distinct().ToArray();
                foreach (string side in new[] { "L", "R" })
                {
                    var upper = spheres.SingleOrDefault(s => s.name == "Cape arm " + side + " upper");
                    var middle = spheres.SingleOrDefault(s => s.name == "Cape arm " + side + " middle");
                    var elbow = spheres.SingleOrDefault(s => s.name == "Cape arm " + side + " elbow");
                    var wrist = spheres.SingleOrDefault(s => s.name == "Cape arm " + side + " wrist");
                    Require(upper != null && elbow != null && wrist != null && upper.radius > 0 && elbow.radius > 0 && wrist.radius > 0
                        && (guideCount == 6 ? middle != null && middle.radius > 0 : middle == null),
                        "Guide variant has its expected positive arm collision spheres on side " + side);
                    bool upperConnected = guideCount == 6
                        ? pairs.Any(p => p.first == upper && p.second == middle || p.first == middle && p.second == upper)
                            && pairs.Any(p => p.first == middle && p.second == elbow || p.first == elbow && p.second == middle)
                        : pairs.Any(p => p.first == upper && p.second == elbow || p.first == elbow && p.second == upper);
                    Require(upperConnected
                        && pairs.Any(p => p.first == elbow && p.second == wrist || p.first == wrist && p.second == elbow),
                        "Native Cloth has connected upper-arm and forearm sphere pairs on side " + side);
                }
                Require(cape.guideSegments == 6 && cape.guideFrame != null && (guideCount == 4 || guideCount == 6)
                    && cape.guideNodes.Length == guideCount * 7 && cape.guideRestPoints?.Length == guideCount * 7,
                    "Four or six six-segment guide paths have a shared metre-space frame and rest positions");
                Require(cape.guideNodes.All(n => n != null) && cape.guideNodes.Distinct().Count() == guideCount * 7 && cape.guideRestPoints.All(IsFinite),
                    "Guide nodes are unique and rest positions finite");
                for (int sideIndex = 1; sideIndex <= 2; sideIndex++)
                {
                    float centreX = preview.transform.InverseTransformPoint(cape.guideFrame.position).x;
                    float originalX = preview.transform.InverseTransformPoint(cape.flowUpper[sideIndex].position).x - centreX;
                    float guideX = preview.transform.InverseTransformPoint(cape.guideNodes[sideIndex * 7 + 6].position).x - centreX;
                    Require(originalX * guideX > 0, "Cape guide follows its source bone on the same physical side: " + cape.flowUpper[sideIndex].name);
                }
                if (guideCount == 6) ValidateFrontLipBindings(cape, cloth.GetComponent<SkinnedMeshRenderer>());
                for (int direction = 0; direction < guideCount; direction++)
                    for (int segment = 0; segment < 6; segment++)
                        Require(Vector3.Distance(cape.guideRestPoints[direction * 7 + segment], cape.guideRestPoints[direction * 7 + segment + 1]) > .0001f,
                            "Guide rest segment has positive physical length");
                if (!Application.isPlaying)
                    for (int i = 0; i < constraints.Length; i++)
                        Require(Mathf.Abs(constraints[i].maxDistance - Mathf.Lerp(.025f, .3f, Mathf.Clamp01(cape.settings.clothFreedom)) * cape.freedomWeights[i]) < .0001f,
                            "Guide native residual freedom matches its explicit per-particle formula: " + i);
            }
            else if (!Application.isPlaying) Require(constraints.Max(c => c.maxDistance) < .05f, "Recommended cape physics stays within five centimetres of its flow shape");
            var report = new Report { time = DateTime.UtcNow.ToString("O"), scene = ForestSpriteDemoBuilder.ScenePath, result = "PASS",
                evidence = "Imported clips, skin weights, required bones and scene wiring. Runtime and visual acceptance are separate.",
                clips = clips.Keys.ToArray(), renderers = skins.Length, vertices = vertices, weightedVertices = weighted,
                bones = skins.SelectMany(s => s.bones).Distinct().Count(), secondaryChains = preview.secondaryMotion.chains.Length,
                clothParticles = particles.Length, pinnedParticles = pins, freeParticles = free };
            WriteReport("import-and-wiring.json", report);
            Debug.Log("Forest sprite import and scene wiring checks passed.");
        }

        [MenuItem("Weather House/Forest Sprite/Run Play Checks (14 seconds)")]
        public static void RunRuntimeChecks()
        {
            Require(EditorApplication.isPlaying, "Play mode is required");
            Require(SceneManager.GetActiveScene().path == ForestSpriteDemoBuilder.ScenePath, "Only exercise the independent demo");
            Require(player == null, "A runtime check is not already active");
            var candidate = ActiveDemoController();
            Require(candidate != null && candidate.enabled, "Active demo controller exists");
            Validate(candidate);
            player = candidate;
            restorePosition = player.transform.position;
            restoreRotation = player.transform.rotation;
            restoreDemo = player.demonstration;
            restoreMotion = player.secondaryMotion.MotionEnabled;
            restoreCloth = player.capeCloth.cloth.enabled;
            restoreWindPreview = player.capeCloth.WindPreview;
            restoreCapeSettings = JsonUtility.ToJson(player.capeCloth.settings);
            player.capeCloth.ResetSettings();
            player.capeCloth.SetWindPreview(false);
            player.ResetPosition();
            player.secondaryMotion.MotionEnabled = true;
            player.secondaryMotion.ResetSimulation();
            player.capeCloth.SetMotionEnabled(true);
            player.capeCloth.ResetSimulation();
            player.SetScriptedInput(Vector2.zero, false, false);
            observed.Clear();
            strideFoot = player.visual.GetComponentsInChildren<Transform>().Single(t => t.name == "Foot.L");
            gaitMinimum = new[] { float.MaxValue, float.MaxValue };
            gaitMaximum = new[] { float.MinValue, float.MinValue };
            runtime = new Report { time = DateTime.UtcNow.ToString("O"), scene = ForestSpriteDemoBuilder.ScenePath,
                result = "RUNNING", evidence = "Live Play-mode frames through the same movement and Animator path as keyboard input. Input is scripted; keyboard focus and subjective visual quality require human review." };
            phase = 0;
            phaseCaptured = false;
            phaseStarted = Time.time;
            sampledFrame = -1;
            initialSimulationSteps = player.secondaryMotion.SimulationSteps;
            previousPosition = player.transform.position;
            baseY = player.transform.position.y;
            frameCaptureIndex = 0;
            captureTimes.Clear();
            capturePhases.Clear();
            nextCapture = Time.time;
            previousSampleTime = Time.time;
            earlySettleSum = lateSettleSum = 0;
            motionViolationStreak = 0;
            earlySettleSamples = lateSettleSamples = 0;
            runtime.usesGuides = player.capeCloth.UsesGuides;
            guideRunPhases.Clear(); guideRunCurvatures.Clear();
            try
            {
                PrepareClothSampling();
                InstallClothSamplingLoop();
            }
            catch (Exception exception)
            {
                runtime.result = "FAIL";
                runtime.error = exception.ToString();
                FinishRuntime();
                throw;
            }
        }

        static void SampleRuntime()
        {
            try
            {
                Require(EditorApplication.isPlaying && player != null, "Play remained active during checks");
                if (Time.frameCount == sampledFrame) return;
                sampledFrame = Time.frameCount;
                float elapsed = Time.time - phaseStarted;
                runtime.runtimeSamples++;
                Vector3 position = player.transform.position;
                Require(IsFinite(position) && player.secondaryMotion.HasFiniteState, "Finite root and spring state");
                observed.Add(player.CurrentState);
                runtime.springPeakAngle = Mathf.Max(runtime.springPeakAngle, player.secondaryMotion.MaxCurrentAngle);
                if (phase < 9) SampleCloth(elapsed);
                if (phase < 9 && player.capeCloth.UsesGuides) SampleGuides();
                if (phase < 9 && !player.capeCloth.UsesGuides)
                    Require(player.capeCloth.UpperBendDegrees >= 0f && player.capeCloth.UpperBendDegrees <= 75.001f
                        && player.capeCloth.UpperBendDegrees + player.capeCloth.LowerBendDegrees >= -.001f
                        && player.capeCloth.LowerBendDegrees >= -player.capeCloth.UpperBendDegrees * .8f - .001f
                        && player.capeCloth.LowerBendDegrees <= player.capeCloth.UpperBendDegrees * .6f + .001f
                        && player.capeCloth.WingSpreadDegrees >= 0f && player.capeCloth.WingSpreadDegrees <= 40.001f,
                        "Cape wave preserves rearward bend and bounded wing opening");
                if (phase == 2 && elapsed > .2f)
                {
                    var cape = player.capeCloth;
                    if (cape.UsesGuides)
                    {
                        var curvature = ActualGuideRearCurvature(cape);
                        guideRunCurvatures.Add(curvature);
                        guideRunPhases.Add((float)typeof(ForestSpriteCapeCloth).GetField("wavePhase", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(cape));
                        if (curvature.Min() < -2f && curvature.Max() > 2f) runtime.runSShapeSamples++;
                    }
                    else
                    {
                    float upperRotation = Quaternion.Angle(initialUpper, cape.flowUpper[0].localRotation);
                    float lowerRotation = Quaternion.Angle(initialLower, cape.flowLower[0].localRotation);
                    if (cape.UpperBendDegrees > 5 && cape.LowerBendDegrees < -5 && upperRotation > 5 && lowerRotation > 5)
                    {
                        runtime.runSShapeSamples++;
                        runtime.runUpperBend = cape.UpperBendDegrees;
                        runtime.runLowerBend = cape.LowerBendDegrees;
                        runtime.runUpperBoneRotation = upperRotation;
                        runtime.runLowerBoneRotation = lowerRotation;
                    }
                    }
                }
                if (phase == 8 && elapsed > .4f)
                {
                    var cape = player.capeCloth;
                    bool first = runtime.strongWindSamples++ == 0;
                    runtime.strongWindUpperBend = Mathf.Max(runtime.strongWindUpperBend, cape.UpperBendDegrees);
                    runtime.strongWindLowerBend = Mathf.Min(runtime.strongWindLowerBend, cape.LowerBendDegrees);
                    runtime.strongWindWingMinimum = first ? cape.WingSpreadDegrees : Mathf.Min(runtime.strongWindWingMinimum, cape.WingSpreadDegrees);
                    runtime.strongWindWingMaximum = first ? cape.WingSpreadDegrees : Mathf.Max(runtime.strongWindWingMaximum, cape.WingSpreadDegrees);
                    if (!first)
                    {
                        runtime.strongWindOpened |= cape.WingSpreadDegrees > previousStrongWindWing + .05f;
                        runtime.strongWindClosed |= cape.WingSpreadDegrees < previousStrongWindWing - .05f;
                    }
                    previousStrongWindWing = cape.WingSpreadDegrees;
                    if (!cape.UsesGuides)
                    {
                    Require(cape.UpperBendDegrees + cape.LowerBendDegrees > 0f, "Strong-wind wave remains swept rearward throughout the cycle");
                    // The travelling wave now permits lower counter-bend up to 80% of the upper angle:
                    // 0.8 * 75 = 60 degrees, retaining at least 20% net rearward bend.
                    Require(Quaternion.Angle(initialUpper, cape.flowUpper[0].localRotation) <= 75.1f
                        && Quaternion.Angle(initialLower, cape.flowLower[0].localRotation) <= 60.1f,
                        "Actual cape bones respect the travelling-wave bend limits");
                    }
                }
                float distance = Vector3.ProjectOnPlane(position - previousPosition, Vector3.up).magnitude;
                if (phase == 1) runtime.walkDistance += distance;
                if (phase == 2) runtime.runDistance += distance;
                if ((phase == 1 || phase == 2) && elapsed > .2f)
                {
                    float ankleForward = player.transform.InverseTransformPoint(strideFoot.position).z;
                    gaitMinimum[phase - 1] = Mathf.Min(gaitMinimum[phase - 1], ankleForward);
                    gaitMaximum[phase - 1] = Mathf.Max(gaitMaximum[phase - 1], ankleForward);
                }
                if (phase == 1 || phase == 2) SampleGaitFootContacts(elapsed);
                if (phase == 3) runtime.jumpRise = Mathf.Max(runtime.jumpRise, position.y - baseY);
                previousPosition = position;
                if (Time.time >= nextCapture && phase < 9)
                {
                    QueueCapture("cloth-frame-" + frameCaptureIndex.ToString("D3") + ".png", 540);
                    captureTimes.Add(Time.time);
                    capturePhases.Add(phase);
                    frameCaptureIndex++;
                    runtime.capturedFrames = frameCaptureIndex;
                    nextCapture = Time.time + 1f / 30f;
                }
                if (phase < 4 && !phaseCaptured && elapsed >= (phase == 3 ? .25f : phase == 0 ? .35f : .5f))
                {
                    QueueCapture("unity-" + ForestSpriteDemoBuilder.ClipNames[phase].ToLowerInvariant() + ".png", 900);
                    phaseCaptured = true;
                }
                float duration = phase == 0 ? .7f : phase == 3 ? 1.8f : phase == 5 ? 3f : phase == 9 ? .4f : 1.3f;
                if (phase == 2 && player.capeCloth.UsesGuides) duration = 4f;
                if (phase == 8 && player.capeCloth.UsesGuides) duration = 3.2f;
                if (elapsed < duration) return;
                if (phase == 0)
                {
                    var body = player.visual.GetComponentsInChildren<SkinnedMeshRenderer>().Single(r => r.name == "Body_Boots");
                    float foot = ForestSpriteDemoBuilder.BoundsOf(new Renderer[] { body }).min.y;
                    if (phase == 0)
                    {
                        runtime.idleCharacterHeight = ForestSpriteDemoBuilder.BoundsOf(player.visual.GetComponentsInChildren<Renderer>()).size.y;
                        Require(runtime.idleCharacterHeight > 1.5f && runtime.idleCharacterHeight < 1.8f, "Actual visible character height: " + runtime.idleCharacterHeight);
                    }
                    if (phase == 0) runtime.idleFootHeight = foot;
                    runtime.maximumGroundedFootError = Mathf.Max(runtime.maximumGroundedFootError, Mathf.Abs(foot));
                    Require(Mathf.Abs(foot) <= .05f, "Visible foot contacts the floor in " + ForestSpriteDemoBuilder.ClipNames[phase] + ": " + foot);
                }
                if (phase == 0) Require(player.IsGrounded, "Controller settles on floor");
                if (phase == 1 || phase == 2)
                {
                    string expected = phase == 1 ? "Walk" : "Run";
                    Require(player.CurrentState == expected && player.animator.GetCurrentAnimatorStateInfo(0).IsName(expected), "Animator reached " + expected);
                    ValidateGaitFootCycles(expected);
                    float span = gaitMaximum[phase - 1] - gaitMinimum[phase - 1];
                    if (phase == 1) runtime.walkFootSpan = span;
                    else runtime.runFootSpan = span;
                    Require(span >= (phase == 1 ? .34f : .42f), "Live " + expected + " really moves the ankle through its wider stride: " + span);
                }
                if (phase == 2)
                {
                    Require(runtime.rearHemSamples > 0, "Actual rear hem direction was sampled during steady running");
                    if (player.capeCloth.UsesGuides) CheckGuideWave();
                    else Require(runtime.runSShapeSamples > 0, "Running wave passes through opposing S-shape bends above five degrees on actual cape bones");
                }
                if (phase == 3)
                {
                    runtime.groundedAfterJump = player.IsGrounded;
                    Require(runtime.jumpRise > .2f && runtime.groundedAfterJump, "Jump rises then lands");
                    Require(runtime.walkDistance > .5f && runtime.runDistance > runtime.walkDistance * 1.5f, "Run travels faster than walk");
                    Require(observed.IsSupersetOf(ForestSpriteDemoBuilder.ClipNames), "All four states ran");
                    Require(player.secondaryMotion.SimulationSteps > initialSimulationSteps && runtime.springPeakAngle > .05f, "Secondary simulation responds during locomotion");
                }
                if (phase == 4)
                {
                    runtime.turnDegrees = Quaternion.Angle(turnStart, player.transform.rotation);
                    Require(runtime.turnDegrees > 60f, "Controller exercised a running turn");
                }
                if (phase == 5)
                {
                    runtime.earlySettleSpeed = earlySettleSamples > 0 ? earlySettleSum / earlySettleSamples : 0;
                    runtime.lateSettleSpeed = lateSettleSamples > 0 ? lateSettleSum / lateSettleSamples : 0;
                    Require(runtime.maximumClothDisplacement > .01f, "Native cloth deforms independently of the animated mesh");
                    Require(earlySettleSamples > 0 && lateSettleSamples > 0 && runtime.lateSettleSpeed <= runtime.earlySettleSpeed + .03f,
                        "Cape motion does not grow after stopping");
                    // Isolate live tuning response from the continuously travelling wave.
                    player.capeCloth.settings.waveAmplitude = 0;
                    player.capeCloth.settings.wingFlap = 0;
                    player.capeCloth.settings.wingSpread = .5f;
                    // Test a deliberate lift change; 0.8 -> 0.85 only asks for one degree on the guide rig.
                    player.capeCloth.settings.lift = .4f;
                    player.capeCloth.ApplySettings();
                    player.capeCloth.SetWindPreview(true);
                }
                if (phase == 6)
                {
                    var cape = player.capeCloth;
                    if (cape.UsesGuides)
                    {
                        Require(player.CurrentState == "Idle" && cape.FlowAmount > .5f && cape.guideNodes.Select((n, i) =>
                            Vector3.Distance(cape.guideFrame.InverseTransformPoint(n.position), cape.guideRestPoints[i])).Max() > .01f,
                            "Standing wind preview deforms actual guide paths without moving the character");
                        windPreviewGuideTangents = (float[])cape.GuideTangentDegrees.Clone();
                    }
                    else Require(player.CurrentState == "Idle" && cape.FlowAmount > .5f && cape.UpperBendDegrees > 5 && cape.LowerBendDegrees < -5,
                            "Standing wind preview drives the cape without moving the character");
                    runtime.windPreviewUpperBend = cape.UpperBendDegrees;
                    runtime.windPreviewWingSpread = cape.WingSpreadDegrees;
                    windPreviewUpper = cape.flowUpper[0].localRotation;
                    windPreviewParticles = CapeParticlesInPlayerSpace();
                    CheckSettingsRoundTrip(cape);
                }
                if (phase == 7)
                {
                    var cape = player.capeCloth;
                    runtime.tunedUpperBend = cape.UpperBendDegrees;
                    runtime.tunedWingSpread = cape.WingSpreadDegrees;
                    runtime.tunedBoneRotation = Quaternion.Angle(windPreviewUpper, cape.flowUpper[0].localRotation);
                    var changed = CapeParticlesInPlayerSpace();
                    runtime.tunedClothShapeDifference = changed.Select((p, i) => Vector3.Distance(p, windPreviewParticles[i])).Average();
                    if (cape.UsesGuides)
                    {
                        runtime.tunedGuideTangentChange = cape.GuideTangentDegrees.Select((a, i) => Mathf.Abs(Mathf.DeltaAngle(windPreviewGuideTangents[i], a))).Max();
                        Require(runtime.tunedGuideTangentChange > 3f && runtime.tunedClothShapeDifference > .005f,
                            "Live parameters change actual guide tangents by more than three degrees and simulated cloth by five millimetres");
                        Require(runtime.tunedWingSpread > runtime.windPreviewWingSpread + 2f,
                            "Live guide wing tuning changes opening by more than two degrees within the forty-degree range");
                    }
                    else
                    {
                        Require(runtime.tunedUpperBend > runtime.windPreviewUpperBend + 3f && runtime.tunedBoneRotation > 3f && runtime.tunedClothShapeDifference > .005f,
                            "Live parameter changes alter the cape target and simulated fabric");
                        Require(runtime.tunedWingSpread > runtime.windPreviewWingSpread + 5f, "Live wing spread tuning opens the cape without restarting simulation");
                    }
                    cape.settings.windStrength = 2f;
                    cape.settings.lift = 1f;
                    cape.settings.sCurve = 1f;
                    cape.settings.wingSpread = 1f;
                    cape.settings.wingFlap = 1f;
                    cape.settings.waveAmplitude = .07f;
                    cape.ApplySettings();
                }
                if (phase == 8)
                {
                    var cape = player.capeCloth;
                    if (cape.UsesGuides)
                        Require(runtime.strongWindSamples > 0 && runtime.strongWindWingMaximum > 8f
                            && runtime.strongWindWingMaximum - runtime.strongWindWingMinimum > 2f && runtime.strongWindOpened && runtime.strongWindClosed,
                            "Strong wind guides open beyond eight degrees and close by over two degrees within the forty-degree limit");
                    else Require(runtime.strongWindSamples > 0 && runtime.strongWindUpperBend > 60f && runtime.strongWindLowerBend < -20f
                        && runtime.strongWindWingMaximum > 35f && runtime.strongWindWingMaximum - runtime.strongWindWingMinimum > 10f
                        && runtime.strongWindOpened && runtime.strongWindClosed,
                        "Maximum wind retains a broad travelling S wave and visibly opens and closes the wings");
                    float flow = cape.FlowAmount;
                    cape.ResetSettings();
                    Require(JsonUtility.ToJson(cape.settings) == JsonUtility.ToJson(new ForestSpriteCapeCloth.Settings()), "Reset restores recommended parameters");
                    Require(cape.cloth.enabled && cape.FlowAmount == flow, "Resetting parameters preserves running simulation");
                    runtime.recommendedSettingsRestored = true;
                    cape.SetWindPreview(false);
                    player.secondaryMotion.MotionEnabled = false;
                    player.secondaryMotion.ResetSimulation();
                    cape.SetMotionEnabled(false);
                }
                if (phase == 9)
                {
                    Require(player.secondaryMotion.MaxCurrentAngle < .01f, "Physics toggle restores the animated pose");
                    Require(!player.capeCloth.cloth.enabled, "Physics toggle disables cape Cloth");
                    Require(runtime.motionConstraintViolations == 0, "Cloth motion constraints failed in " + runtime.motionViolationFrames
                        + " frames / " + runtime.motionConstraintViolations + " particle samples; maximum excess above maxDistance + .025m="
                        + runtime.motionMaxExcess + "; violationTime=" + runtime.motionViolationTime
                        + "; longestContinuousTime=" + runtime.motionLongestViolationTime + "; peak: " + runtime.motionPeakDetails);
                    runtime.result = "PASS";
                    FinishRuntime();
                    return;
                }
                phase++;
                phaseCaptured = false;
                phaseStarted = Time.time;
                if (phase == 1) player.SetScriptedInput(Vector2.up, false, false);
                else if (phase == 2) player.SetScriptedInput(Vector2.up, true, false);
                else if (phase == 3) { baseY = position.y; player.SetScriptedInput(Vector2.zero, false, true); }
                else if (phase == 4) { turnStart = player.transform.rotation; player.SetScriptedInput(Vector2.left, true, false); }
                else player.SetScriptedInput(Vector2.zero, false, false);
            }
            catch (Exception exception)
            {
                runtime.result = "FAIL";
                runtime.error = exception.ToString();
                Debug.LogException(exception);
                FinishRuntime();
            }
        }

        static void SampleGaitFootContacts(float elapsed)
        {
            string expected = phase == 1 ? "Walk" : "Run";
            var state = player.animator.GetCurrentAnimatorStateInfo(0);
            if (player.animator.IsInTransition(0) || !state.IsName(expected)) return;
            var matrices = footSkin.bones.Select((b, i) => b.localToWorldMatrix * footBindposes[i]).ToArray();
            float[] heights = new float[2];
            for (int side = 0; side < 2; side++)
            {
                heights[side] = float.PositiveInfinity;
                foreach (int i in gaitFootVertices[side])
                {
                    BoneWeight w = footWeights[i]; Vector3 v = footMeshVertices[i];
                    Vector3 world = matrices[w.boneIndex0].MultiplyPoint3x4(v) * w.weight0
                        + matrices[w.boneIndex1].MultiplyPoint3x4(v) * w.weight1
                        + matrices[w.boneIndex2].MultiplyPoint3x4(v) * w.weight2
                        + matrices[w.boneIndex3].MultiplyPoint3x4(v) * w.weight3;
                    heights[side] = Mathf.Min(heights[side], world.y);
                }
            }
            runtime.gaitFootSamples.Add(new GaitFootSample { state = expected, frame = Time.frameCount,
                normalizedTime = state.normalizedTime, phaseTime = elapsed, leftWorldHeight = heights[0], rightWorldHeight = heights[1] });
            Require(!float.IsNaN(heights[0]) && !float.IsInfinity(heights[0]) && !float.IsNaN(heights[1]) && !float.IsInfinity(heights[1]),
                "Finite actual shoe sole heights in " + expected);
            // Keep the established gait-capture penetration limit; positive flight height is legitimate in Run.
            Require(heights[0] >= -.01f && heights[1] >= -.01f,
                "Actual shoes do not penetrate the floor by more than one centimetre in " + expected
                + ": phase=" + Mathf.Repeat(state.normalizedTime, 1f) + ", left=" + heights[0] + ", right=" + heights[1]);
        }

        static void ValidateGaitFootCycles(string expected)
        {
            var samples = runtime.gaitFootSamples.Where(s => s.state == expected).ToArray();
            Require(samples.Length > 0, "Actual foot samples exist in " + expected);
            int firstComplete = Mathf.CeilToInt(samples.First().normalizedTime);
            int afterLastComplete = Mathf.FloorToInt(samples.Last().normalizedTime);
            Require(afterLastComplete > firstComplete, "At least one complete actual gait cycle was sampled in " + expected);
            float worstContact = 0;
            for (int cycle = firstComplete; cycle < afterLastComplete; cycle++)
            {
                var fullCycle = samples.Where(s => Mathf.FloorToInt(s.normalizedTime) == cycle).ToArray();
                Require(fullCycle.Length > 0, "Consecutive native samples cover gait cycle " + cycle);
                float left = fullCycle.Min(s => Mathf.Abs(s.leftWorldHeight));
                float right = fullCycle.Min(s => Mathf.Abs(s.rightWorldHeight));
                worstContact = Mathf.Max(worstContact, Mathf.Max(left, right));
                Require(left <= .05f && right <= .05f,
                    "Both visible feet contact the floor during each complete " + expected + " cycle " + cycle
                    + ": left=" + left + ", right=" + right);
            }
            runtime.maximumGroundedFootError = Mathf.Max(runtime.maximumGroundedFootError, worstContact);
            if (expected == "Walk") { runtime.walkFootHeight = worstContact; runtime.walkCompleteFootCycles = afterLastComplete - firstComplete; }
            else { runtime.runFootHeight = worstContact; runtime.runCompleteFootCycles = afterLastComplete - firstComplete; }
        }

        static void SampleGuides()
        {
            var cape = player.capeCloth;
            int guideCount = cape.guideNodes.Length / (cape.guideSegments + 1);
            Require((guideCount == 4 || guideCount == 6) && cape.GuideTangentDegrees?.Length == guideCount * cape.guideSegments
                && cape.GuideTangentDegrees.All(a => !float.IsNaN(a) && !float.IsInfinity(a)),
                "Finite actual per-segment guide rotation increments");
            Require(cape.guideNodes.All(n => n != null && IsFinite(n.position)), "Finite actual guide node positions");
            Require(cape.WingSpreadDegrees >= 0 && cape.WingSpreadDegrees <= 40.001f, "Guide opening remains within forty degrees");
            runtime.guideMaximumSegmentError = Mathf.Max(runtime.guideMaximumSegmentError, cape.GuideMaxSegmentError);
            runtime.guideTargetStretchP95 = Mathf.Max(runtime.guideTargetStretchP95, cape.GuideTargetStretchP95);
            runtime.guideTargetStretchMaximum = Mathf.Max(runtime.guideTargetStretchMaximum, cape.GuideTargetStretchMaximum);
            float measuredLengthError = 0;
            for (int direction = 0; direction < guideCount; direction++)
                for (int segment = 0; segment < 6; segment++)
                {
                    int i = direction * 7 + segment;
                    float actual = Vector3.Distance(cape.guideFrame.InverseTransformPoint(cape.guideNodes[i].position),
                        cape.guideFrame.InverseTransformPoint(cape.guideNodes[i + 1].position));
                    float rest = Vector3.Distance(cape.guideRestPoints[i], cape.guideRestPoints[i + 1]);
                    measuredLengthError = Mathf.Max(measuredLengthError, Mathf.Abs(actual - rest));
                }
            runtime.guideMaximumSegmentError = Mathf.Max(runtime.guideMaximumSegmentError, measuredLengthError);
            Require(measuredLengthError <= .0001f, "Every guide segment preserves its rest length within 0.1 millimetre: " + measuredLengthError);
        }

        static void ValidateFrontLipBindings(ForestSpriteCapeCloth cape, SkinnedMeshRenderer skin)
        {
            Require(skin != null && skin.bones.Length == 47, "Six guides, Chest and corresponding upper/lower arms bind the open-front cape");
            for (int side = 0; side < 2; side++)
            {
                float sign = side == 0 ? 1f : -1f;
                string prefix = side == 0 ? "Cape_FrontLipPositiveX_Guide_" : "Cape_FrontLipNegativeX_Guide_";
                int first = (4 + side) * 7;
                Require(cape.guideNodes.Skip(first).Take(7).All(n => n.name.StartsWith(prefix, StringComparison.Ordinal))
                    && cape.guideRestPoints[first + 6].x * sign > 0,
                    "Independent front-lip rest path is on its named physical side: " + prefix);
                bool[] lipBones = skin.bones.Select(b => b.name.StartsWith(prefix, StringComparison.Ordinal)).ToArray();
                bool[] oppositeSideBones = skin.bones.Select(b => {
                    for (int direction = 1; direction <= 2; direction++)
                        if (cape.guideNodes.Skip(direction * 7).Take(7).Contains(b))
                            return cape.guideRestPoints[direction * 7 + 6].x * sign < 0;
                    return false;
                }).ToArray();
                int dominatedVertices = 0;
                foreach (var weight in skin.sharedMesh.boneWeights)
                {
                    float lipWeight = SelectedWeight(weight, lipBones);
                    if (lipWeight > .5f) dominatedVertices++;
                    if (lipWeight > .00001f)
                        Require(SelectedWeight(weight, oppositeSideBones) <= .00001f,
                            "Front-lip vertices never rotate around the remote opposite-side guide: " + prefix);
                }
                Require(dominatedVertices > 0, "The open front really uses its independent lip guides: " + prefix);
            }
        }

        static float[] ActualGuideRearCurvature(ForestSpriteCapeCloth cape)
        {
            var p = cape.guideNodes.Take(7).Select(n => cape.guideFrame.InverseTransformPoint(n.position)).ToArray();
            return Enumerable.Range(1, 5).Select(i => {
                Vector3 a = p[i] - p[i - 1], b = p[i + 1] - p[i];
                a.x = b.x = 0;
                return Mathf.Atan2(Vector3.Cross(a, b).x, Vector3.Dot(a, b)) * Mathf.Rad2Deg;
            }).ToArray();
        }

        static void CheckGuideWave()
        {
            Require(guideRunPhases.Count > 30 && guideRunPhases.Last() - guideRunPhases.First() >= 4f * Mathf.PI,
                "Guide curvature was sampled across at least two measured wave cycles");
            runtime.runGuideCurvatureAmplitude = new float[5];
            runtime.runGuideCurvaturePeakPhase = new float[5];
            runtime.runGuideCurvatureRSquared = new float[5];
            var reliable = new List<int>();
            for (int position = 0; position < 5; position++)
            {
                // Fit actual local turning angle = c + a*sin(wavePhase) + b*cos(wavePhase).
                var normal = new double[3, 4];
                double mean = guideRunCurvatures.Average(v => v[position]);
                for (int frame = 0; frame < guideRunPhases.Count; frame++)
                {
                    double[] basis = { 1, Math.Sin(guideRunPhases[frame]), Math.Cos(guideRunPhases[frame]) };
                    for (int r = 0; r < 3; r++)
                    {
                        for (int c = 0; c < 3; c++) normal[r, c] += basis[r] * basis[c];
                        normal[r, 3] += basis[r] * guideRunCurvatures[frame][position];
                    }
                }
                for (int pivot = 0; pivot < 3; pivot++)
                {
                    double divisor = normal[pivot, pivot];
                    Require(Math.Abs(divisor) > 1e-9, "Guide curvature harmonic fit has independent phase samples");
                    for (int c = pivot; c < 4; c++) normal[pivot, c] /= divisor;
                    for (int r = 0; r < 3; r++) if (r != pivot)
                    {
                        double factor = normal[r, pivot];
                        for (int c = pivot; c < 4; c++) normal[r, c] -= factor * normal[pivot, c];
                    }
                }
                double residual = 0, variance = 0;
                for (int frame = 0; frame < guideRunPhases.Count; frame++)
                {
                    double y = guideRunCurvatures[frame][position];
                    double fitted = normal[0, 3] + normal[1, 3] * Math.Sin(guideRunPhases[frame]) + normal[2, 3] * Math.Cos(guideRunPhases[frame]);
                    residual += (y - fitted) * (y - fitted); variance += (y - mean) * (y - mean);
                }
                float amplitude = (float)Math.Sqrt(normal[1, 3] * normal[1, 3] + normal[2, 3] * normal[2, 3]);
                float rSquared = variance > 1e-9 ? (float)(1 - residual / variance) : 0;
                runtime.runGuideCurvatureAmplitude[position] = amplitude;
                runtime.runGuideCurvaturePeakPhase[position] = Mathf.Repeat((float)Math.Atan2(normal[1, 3], normal[2, 3]), 2f * Mathf.PI);
                runtime.runGuideCurvatureRSquared[position] = rSquared;
                if (amplitude > 1f && rSquared > .5f) reliable.Add(position);
            }
            foreach (int a in reliable) foreach (int b in reliable)
                runtime.runGuidePhaseSpread = Mathf.Max(runtime.runGuidePhaseSpread, Mathf.Abs(Mathf.DeltaAngle(
                    runtime.runGuideCurvaturePeakPhase[a] * Mathf.Rad2Deg, runtime.runGuideCurvaturePeakPhase[b] * Mathf.Rad2Deg)) * Mathf.Deg2Rad);
            Require(runtime.runSShapeSamples > 0, "Actual guide path contains opposing local curvature beyond two degrees");
            Require(reliable.Count >= 3 && runtime.runGuidePhaseSpread > .2f,
                "At least three guide curvature locations vary by over one degree with R-squared above 0.5 and spatial peak-phase separation above 0.2 radians");
        }

        static void CheckSettingsRoundTrip(ForestSpriteCapeCloth cape)
        {
            // Never write the user's persistent settings: exercise the same API in Verification~.
            string path = Path.GetFullPath(Path.Combine(ForestSpriteDemoBuilder.Review, "cape-settings-check-" + Guid.NewGuid().ToString("N") + ".json"));
            float flow = cape.FlowAmount, upperBend = cape.UpperBendDegrees, runBlend = cape.RunBlend;
            Quaternion upper = cape.flowUpper[0].localRotation;
            try
            {
                cape.settings.walkSpeed = 1.7f;
                cape.settings.runSpeed = 3.4f;
                cape.settings.walkStride = .6f;
                cape.ApplyWingedPreset();
                Require(Mathf.Abs(player.walkSpeed - 1.7f) < .0001f && Mathf.Abs(player.runSpeed - 3.4f) < .0001f
                    && Mathf.Abs(player.walkStride - .6f) < .0001f && cape.cloth.enabled && cape.FlowAmount == flow,
                    "Winged preset preserves live movement tuning and continuous simulation");
                Require(cape.settings.wingSpread == new ForestSpriteCapeCloth.Settings().wingSpread,
                    "Winged preset applies the recommended cape shape");
                cape.settings = new ForestSpriteCapeCloth.Settings { windStrength = 1.2f, lift = .85f, wingSpread = 1f, sCurve = .7f,
                    waveAmplitude = 0, wingFlap = 0, waveSpeed = .9f, settleTime = .7f, clothFreedom = .3f, damping = .3f,
                    bendStiffness = .22f, contactRebound = 0, contactResponse = .32f };
                cape.ApplySettings();
                string expected = JsonUtility.ToJson(cape.settings);
                cape.SaveSettings(path);
                cape.settings = new ForestSpriteCapeCloth.Settings();
                cape.LoadSettings(path);
                Require(JsonUtility.ToJson(cape.settings) == expected, "All cape parameters survive save/load round trip");
                Require(cape.settings.contactRebound == 0, "Explicit zero rebound is preserved, not replaced by a missing-field default");
                string legacy = expected;
                var migrated = JsonUtility.FromJson<ForestSpriteCapeCloth.Settings>(expected);
                // Check yesterday's JSON without wingFlap first, then older JSON missing both wing controls.
                foreach (string field in new[] { "wingFlap", "wingSpread", "contactRebound", "contactResponse" })
                {
                    int start = legacy.IndexOf("\"" + field + "\":", StringComparison.Ordinal);
                    int end = legacy.IndexOf(',', start);
                    legacy = legacy.Remove(start, end - start + 1);
                    File.WriteAllText(path, legacy);
                    cape.LoadSettings(path);
                    if (field == "wingFlap") migrated.wingFlap = new ForestSpriteCapeCloth.Settings().wingFlap;
                    else if (field == "wingSpread") migrated.wingSpread = new ForestSpriteCapeCloth.Settings().wingSpread;
                    else if (field == "contactRebound") migrated.contactRebound = new ForestSpriteCapeCloth.Settings().contactRebound;
                    else migrated.contactResponse = new ForestSpriteCapeCloth.Settings().contactResponse;
                    Require(JsonUtility.ToJson(cape.settings) == JsonUtility.ToJson(migrated) && File.ReadAllText(path) == legacy,
                        "Legacy settings preserve prior values, supply missing " + field + ", and are not rewritten on load");
                }
                runtime.legacySettingsMigration = runtime.legacyWingFlapMigration = true;
                CheckSettingsFiniteBounds();
                File.WriteAllText(path, expected);
                cape.LoadSettings(path);
                Require(cape.cloth.enabled && cape.FlowAmount == flow && cape.RunBlend == runBlend && cape.UpperBendDegrees == upperBend && Quaternion.Angle(upper, cape.flowUpper[0].localRotation) < .001f,
                    "Saving, loading and applying settings do not reset flow or bone pose");
                runtime.settingsRoundTrip = runtime.tuningKeptSimulation = true;
            }
            finally { if (File.Exists(path)) File.Delete(path); }
        }

        static void CheckSettingsFiniteBounds()
        {
            var defaults = new ForestSpriteCapeCloth.Settings();
            foreach (var field in typeof(ForestSpriteCapeCloth.Settings).GetFields())
            {
                if (field.FieldType != typeof(float)) continue;
                var range = (RangeAttribute)Attribute.GetCustomAttribute(field, typeof(RangeAttribute));
                Require(range != null, "Every exposed settings float has an explicit range: " + field.Name);
                foreach (float invalid in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
                {
                    var sample = new ForestSpriteCapeCloth.Settings();
                    field.SetValue(sample, invalid); sample.Clamp();
                    Require((float)field.GetValue(sample) == (float)field.GetValue(defaults),
                        "Nonfinite settings fall back to the documented default: " + field.Name);
                }
                var extremes = new ForestSpriteCapeCloth.Settings();
                field.SetValue(extremes, -1000f); extremes.Clamp();
                Require((float)field.GetValue(extremes) == range.min, "Settings lower bound: " + field.Name);
                field.SetValue(extremes, 1000f); extremes.Clamp();
                Require((float)field.GetValue(extremes) == range.max, "Settings upper bound: " + field.Name);
            }
        }

        static Vector3[] CapeParticlesInPlayerSpace() => player.capeCloth.cloth.vertices
            .Select(p => player.transform.InverseTransformPoint(ParticleWorld(p))).ToArray();

        static void InstallClothSamplingLoop()
        {
            Require(!playerLoopInstalled, "Cloth sampling loop is not already installed");
            originalPlayerLoop = PlayerLoop.GetCurrentPlayerLoop();
            var instrumented = originalPlayerLoop;
            var before = new List<string>();
            DescribeLoop(originalPlayerLoop, 0, before);
            bool found = InsertAfterCloth(ref instrumented);
            Directory.CreateDirectory(ForestSpriteDemoBuilder.Review);
            var description = new List<string> { "Original PlayerLoop (native execution order):" };
            description.AddRange(before);
            description.Add("\nInstrumented PlayerLoop:");
            DescribeLoop(instrumented, 0, description);
            File.WriteAllLines(ForestSpriteDemoBuilder.Review + "/cloth-playerloop.txt", description);
            Require(found, "PhysicsSkinnedClothFinishUpdate exists in the current PlayerLoop");
            var after = new List<string>();
            DescribeLoop(originalPlayerLoop, 0, after);
            Require(before.SequenceEqual(after), "Original PlayerLoop remains unchanged for restoration");
            PlayerLoop.SetPlayerLoop(instrumented);
            playerLoopInstalled = true;
            AssemblyReloadEvents.beforeAssemblyReload += RestorePlayerLoop;
            EditorApplication.playModeStateChanged += HandlePlayModeChange;
        }

        static bool InsertAfterCloth(ref PlayerLoopSystem loop)
        {
            if (loop.subSystemList == null) return false;
            // Clone each edited array: PlayerLoopSystem is a struct but its children are shared arrays.
            var children = (PlayerLoopSystem[])loop.subSystemList.Clone();
            for (int i = 0; i < children.Length; i++)
            {
                if (children[i].type == typeof(UnityEngine.PlayerLoop.PostLateUpdate.PhysicsSkinnedClothFinishUpdate))
                {
                    var expanded = new PlayerLoopSystem[children.Length + 1];
                    Array.Copy(children, 0, expanded, 0, i + 1);
                    expanded[i + 1] = new PlayerLoopSystem { type = typeof(ClothSampleAfterFinish), updateDelegate = SampleRuntime };
                    Array.Copy(children, i + 1, expanded, i + 2, children.Length - i - 1);
                    loop.subSystemList = expanded;
                    return true;
                }
                if (InsertAfterCloth(ref children[i]))
                {
                    loop.subSystemList = children;
                    return true;
                }
            }
            return false;
        }

        static void DescribeLoop(PlayerLoopSystem loop, int depth, List<string> lines)
        {
            lines.Add(new string(' ', depth * 2) + (loop.type?.FullName ?? "<root>"));
            if (loop.subSystemList != null)
                foreach (var child in loop.subSystemList) DescribeLoop(child, depth + 1, lines);
        }

        static void RestorePlayerLoop()
        {
            AssemblyReloadEvents.beforeAssemblyReload -= RestorePlayerLoop;
            EditorApplication.playModeStateChanged -= HandlePlayModeChange;
            if (!playerLoopInstalled) return;
            PlayerLoop.SetPlayerLoop(originalPlayerLoop);
            playerLoopInstalled = false;
        }

        static void HandlePlayModeChange(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.ExitingPlayMode || player == null) return;
            runtime.result = "FAIL";
            runtime.error = "Play stopped before the cloth runtime checks completed.";
            FinishRuntime();
        }

        static readonly Queue<KeyValuePair<string, int>> captures = new Queue<KeyValuePair<string, int>>();
        static void QueueCapture(string name, int size)
        {
            captures.Enqueue(new KeyValuePair<string, int>(name, size));
            // Keep explicit capture outside the native cloth update and sampling callback.
            EditorApplication.delayCall -= CaptureQueued;
            EditorApplication.delayCall += CaptureQueued;
        }
        static void CaptureQueued()
        {
            while (captures.Count > 0)
            {
                var capture = captures.Dequeue();
                if (EditorApplication.isPlaying) ForestSpriteDemoRelay.CaptureDemo(capture.Key, capture.Value);
            }
        }

        static void PrepareClothSampling()
        {
            var cape = player.capeCloth;
            clothSkin = cape.cloth.GetComponent<SkinnedMeshRenderer>();
            meshVertices = clothSkin.sharedMesh.vertices;
            meshWeights = clothSkin.sharedMesh.boneWeights;
            bindposes = clothSkin.sharedMesh.bindposes;
            coefficients = cape.cloth.coefficients;
            clothToMesh = new int[cape.restParticles.Length];
            previousOffsets = new Vector3[clothToMesh.Length];
            previousAnchorsWorld = new Vector3[clothToMesh.Length];
            previousAnchorsLocal = new Vector3[clothToMesh.Length];
            initialUpper = cape.flowUpper[0].localRotation;
            initialLower = cape.flowLower[0].localRotation;
            // Cloth welds duplicate seam vertices; match positions once, never assume mesh-index parity.
            for (int i = 0; i < clothToMesh.Length; i++)
            {
                float nearest = float.PositiveInfinity;
                for (int j = 0; j < meshVertices.Length; j++)
                {
                    float distance = (cape.restParticles[i] - meshVertices[j]).sqrMagnitude;
                    if (distance < nearest) { nearest = distance; clothToMesh[i] = j; }
                }
                Require(nearest < .000001f, "Cloth particle maps to a metre-scale bind vertex: " + i);
            }
            runtime.clothParticles = clothToMesh.Length;
            runtime.pinnedParticles = coefficients.Count(c => c.maxDistance <= .00001f);
            runtime.freeParticles = coefficients.Length - runtime.pinnedParticles;
            bool[] backBones = clothSkin.bones.Select(b => cape.UsesGuides ? b.name.StartsWith("Cape_Back_", StringComparison.Ordinal) : b.name == "Cape_Back_01" || b.name == "Cape_Back_02").ToArray();
            float hemY = Mathf.Lerp(cape.restParticles.Min(p => p.y), cape.restParticles.Max(p => p.y), .35f);
            rearHemParticles = Enumerable.Range(0, clothToMesh.Length).Where(i => cape.restParticles[i].y <= hemY
                && SelectedWeight(meshWeights[clothToMesh[i]], backBones) >= .75f).ToArray();
            Require(rearHemParticles.Length >= 3, "Back-weighted lower hem particles exist independently of the front flap");
            runtime.rearHemParticles = rearHemParticles.Length;
            footSkin = player.visual.GetComponentsInChildren<SkinnedMeshRenderer>().Single(r => r.name == "Body_Boots");
            footMeshVertices = footSkin.sharedMesh.vertices;
            footWeights = footSkin.sharedMesh.boneWeights;
            footBindposes = footSkin.sharedMesh.bindposes;
            bool[] footBones = footSkin.bones.Select(b => b.name.StartsWith("Foot.") || b.name.StartsWith("Toe.")).ToArray();
            footVertices = Enumerable.Range(0, footWeights.Length).Where(i => SelectedWeight(footWeights[i], footBones) >= .25f).ToArray();
            Require(footVertices.Length > 0, "Actual foot-weighted body mesh vertices exist");
            gaitFootVertices = new[] { ".L", ".R" }.Select(suffix => {
                bool[] sideBones = footSkin.bones.Select(b => (b.name.StartsWith("Foot.") || b.name.StartsWith("Toe.")) && b.name.EndsWith(suffix)).ToArray();
                return Enumerable.Range(0, footWeights.Length).Where(i => SelectedWeight(footWeights[i], sideBones) >= .25f).ToArray();
            }).ToArray();
            Require(gaitFootVertices.All(v => v.Length > 0), "Both actual shoe meshes have independently weighted vertices");
            var matrices = SkinMatrices();
            for (int i = 0; i < clothToMesh.Length; i++)
            {
                Vector3 anchor = SkinnedWorld(clothToMesh[i], matrices);
                previousAnchorsWorld[i] = anchor;
                previousAnchorsLocal[i] = player.transform.InverseTransformPoint(anchor);
                if (coefficients[i].maxDistance <= .00001f)
                    runtime.initialPinnedPenetration = Mathf.Max(runtime.initialPinnedPenetration, BodyPenetration(anchor));
            }
            Require(runtime.initialPinnedPenetration <= .0001f, "Pinned collar begins outside body collision volumes");
        }

        static void SampleCloth(float phaseElapsed)
        {
            var particles = player.capeCloth.cloth.vertices;
            coefficients = player.capeCloth.cloth.coefficients;
            Require(particles.Length == clothToMesh.Length, "Stable cloth particle count during Play");
            var matrices = SkinMatrices();
            float sampleTime = Time.time - previousSampleTime;
            float squaredSpeed = 0;
            int moving = 0;
            bool motionViolation = false;
            for (int i = 0; i < particles.Length; i++)
            {
                Require(IsFinite(particles[i]), "Finite cloth particle " + i);
                Vector3 world = ParticleWorld(particles[i]);
                Vector3 anchor = SkinnedWorld(clothToMesh[i], matrices);
                Vector3 rebasedPreviousAnchor = player.transform.TransformPoint(previousAnchorsLocal[i]);
                Vector3 offset = world - anchor;
                float displacement = offset.magnitude;
                Require(IsFinite(world) && IsFinite(anchor) && !float.IsNaN(displacement) && !float.IsInfinity(displacement),
                    "Finite cloth world position, skin anchor and displacement at particle " + i);
                // Keep the original strict bound, but collect its full excursion before failing the run.
                // motionMaxExcess is measured above maxDistance + .025m, not above maxDistance alone.
                if (!(displacement < coefficients[i].maxDistance + .025f))
                {
                    runtime.motionConstraintViolations++;
                    if (!motionViolation)
                    {
                        motionViolation = true;
                        runtime.motionViolationFrames++;
                        runtime.motionViolationTime += sampleTime;
                        motionViolationStreak += sampleTime;
                        runtime.motionLongestViolationTime = Mathf.Max(runtime.motionLongestViolationTime, motionViolationStreak);
                    }
                    float excess = displacement - (coefficients[i].maxDistance + .025f);
                    if (runtime.motionPeakParticle < 0 || excess > runtime.motionMaxExcess)
                    {
                        runtime.motionMaxExcess = excess;
                        runtime.motionPeakPhase = phase;
                        runtime.motionPeakParticle = i;
                        runtime.motionPeakPhaseTime = phaseElapsed;
                        runtime.motionPeakDetails = "Cloth skin constraint violation at particle " + i + ": " + displacement
                            + "; max=" + coefficients[i].maxDistance + "; point=" + world.ToString("F5") + "; anchor=" + anchor.ToString("F5")
                            + "; distanceToPreviousWorldAnchor=" + Vector3.Distance(world, previousAnchorsWorld[i])
                            + "; distanceToRebasedPreviousAnchor=" + Vector3.Distance(world, rebasedPreviousAnchor)
                            + "; previousWorldAnchor=" + previousAnchorsWorld[i].ToString("F5") + "; rebasedPreviousAnchor=" + rebasedPreviousAnchor.ToString("F5")
                            + "; root=" + player.transform.position.ToString("F5")
                            + "; anchorPenetration=" + BodyPenetration(anchor) + "; particlePenetration=" + BodyPenetration(world)
                            + "; sampleDt=" + sampleTime
                            + "; previousOffset=" + previousOffsets[i].ToString("F5") + "; phase=" + phase + "; phaseTime=" + phaseElapsed;
                    }
                }
                if (coefficients[i].maxDistance <= .00001f)
                {
                    runtime.maximumPinnedError = Mathf.Max(runtime.maximumPinnedError, displacement);
                    Require(displacement <= .01f, "Pinned collar follows the animated chest: " + displacement);
                }
                else
                {
                    runtime.maximumClothDisplacement = Mathf.Max(runtime.maximumClothDisplacement, displacement);
                    if (sampleTime > .00001f && previousSampleTime > phaseStarted)
                    {
                        squaredSpeed += (offset - previousOffsets[i]).sqrMagnitude / (sampleTime * sampleTime);
                        moving++;
                    }
                }
                float penetration = BodyPenetration(world);
                runtime.maximumCollisionPenetration = Mathf.Max(runtime.maximumCollisionPenetration, penetration);
                Require(penetration <= .015f, "Cape stays outside collision volumes: " + penetration + "; particle=" + i
                    + "; point=" + world.ToString("F5") + "; phase=" + phase + "; phaseTime=" + phaseElapsed);
                previousOffsets[i] = offset;
                previousAnchorsWorld[i] = anchor;
                previousAnchorsLocal[i] = player.transform.InverseTransformPoint(anchor);
            }
            if (!motionViolation) motionViolationStreak = 0;
            if ((phase == 2 || phase == 8) && phaseElapsed >= .4f) SampleRearHemDirection(particles);
            if (phase == 5 && moving > 0)
            {
                float speed = Mathf.Sqrt(squaredSpeed / moving);
                if (phaseElapsed <= .75f) { earlySettleSum += speed; earlySettleSamples++; }
                if (phaseElapsed >= 2.25f) { lateSettleSum += speed; lateSettleSamples++; }
            }
            previousSampleTime = Time.time;
        }

        static float SelectedWeight(BoneWeight w, bool[] bones) =>
            (bones[w.boneIndex0] ? w.weight0 : 0) + (bones[w.boneIndex1] ? w.weight1 : 0)
            + (bones[w.boneIndex2] ? w.weight2 : 0) + (bones[w.boneIndex3] ? w.weight3 : 0);

        static void SampleRearHemDirection(Vector3[] particles)
        {
            float maxZ = rearHemParticles.Max(i => player.transform.InverseTransformPoint(ParticleWorld(particles[i])).z);
            var matrices = footSkin.bones.Select((b, i) => b.localToWorldMatrix * footBindposes[i]).ToArray();
            float footBackZ = float.PositiveInfinity;
            foreach (int i in footVertices)
            {
                var w = footWeights[i];
                var v = footMeshVertices[i];
                Vector3 world = matrices[w.boneIndex0].MultiplyPoint3x4(v) * w.weight0
                    + matrices[w.boneIndex1].MultiplyPoint3x4(v) * w.weight1
                    + matrices[w.boneIndex2].MultiplyPoint3x4(v) * w.weight2
                    + matrices[w.boneIndex3].MultiplyPoint3x4(v) * w.weight3;
                footBackZ = Mathf.Min(footBackZ, player.transform.InverseTransformPoint(world).z);
            }
            float clearance = footBackZ - maxZ;
            bool first = runtime.rearHemSamples++ == 0;
            runtime.maximumRearHemLocalZ = first ? maxZ : Mathf.Max(runtime.maximumRearHemLocalZ, maxZ);
            runtime.minimumRearHemFootRearClearance = first ? clearance : Mathf.Min(runtime.minimumRearHemFootRearClearance, clearance);
            Require(maxZ < 0f && clearance > 0f, "Native rear hem stays behind the character and actual foot mesh: maxZ=" + maxZ
                + "; footBackZ=" + footBackZ + "; clearance=" + clearance + "; phase=" + phase);
        }

        static Vector3 ParticleWorld(Vector3 particle) => player.capeCloth.cloth.transform.TransformPoint(particle);

        static Matrix4x4[] SkinMatrices() => clothSkin.bones.Select((bone, i) => bone.localToWorldMatrix * bindposes[i]).ToArray();

        static Vector3 SkinnedWorld(int index, Matrix4x4[] matrices)
        {
            var w = meshWeights[index];
            var v = meshVertices[index];
            return matrices[w.boneIndex0].MultiplyPoint3x4(v) * w.weight0
                + matrices[w.boneIndex1].MultiplyPoint3x4(v) * w.weight1
                + matrices[w.boneIndex2].MultiplyPoint3x4(v) * w.weight2
                + matrices[w.boneIndex3].MultiplyPoint3x4(v) * w.weight3;
        }

        static float BodyPenetration(Vector3 world)
        {
            float penetration = 0;
            foreach (var pair in player.capeCloth.cloth.sphereColliders)
            {
                if (!pair.first) continue;
                Vector3 a = pair.first.transform.TransformPoint(pair.first.center);
                float radiusA = WorldRadius(pair.first);
                if (!pair.second) penetration = Mathf.Max(penetration, radiusA - Vector3.Distance(world, a));
                else
                {
                    Vector3 b = pair.second.transform.TransformPoint(pair.second.center);
                    Vector3 axis = b - a;
                    float length = axis.magnitude;
                    float radiusB = WorldRadius(pair.second), difference = radiusB - radiusA;
                    if (length <= Mathf.Abs(difference) + .00001f)
                        penetration = Mathf.Max(penetration, radiusA - Vector3.Distance(world, a), radiusB - Vector3.Distance(world, b));
                    else
                    {
                        float z = Vector3.Dot(world - a, axis / length);
                        float radial = (world - a - axis * (z / length)).magnitude;
                        float t = Mathf.Clamp01((z + difference * radial / Mathf.Sqrt(length * length - difference * difference)) / length);
                        penetration = Mathf.Max(penetration, Mathf.Lerp(radiusA, radiusB, t) - Vector3.Distance(world, a + axis * t));
                    }
                }
            }
            return penetration;
        }

        static float WorldRadius(SphereCollider sphere)
        {
            Vector3 scale = sphere.transform.lossyScale;
            return sphere.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
        }

        static void FinishRuntime()
        {
            RestorePlayerLoop();
            runtime.observedStates = observed.ToArray();
            runtime.captureTimes = captureTimes.ToArray();
            runtime.capturePhases = capturePhases.ToArray();
            WriteReport("runtime.json", runtime);
            if (player != null)
            {
                player.ReleaseScriptedInput();
                player.ResetPosition();
                var controller = player.GetComponent<CharacterController>();
                controller.enabled = false;
                player.transform.SetPositionAndRotation(restorePosition, restoreRotation);
                controller.enabled = true;
                player.secondaryMotion.MotionEnabled = restoreMotion;
                player.secondaryMotion.ResetSimulation();
                player.capeCloth.SetMotionEnabled(restoreCloth);
                player.capeCloth.ResetSimulation();
                player.capeCloth.settings = JsonUtility.FromJson<ForestSpriteCapeCloth.Settings>(restoreCapeSettings);
                player.capeCloth.ApplySettings();
                player.capeCloth.SetWindPreview(restoreWindPreview);
                player.SetDemonstration(restoreDemo);
            }
            Debug.Log("Forest sprite live runtime checks: " + runtime.result + ". " + ForestSpriteDemoBuilder.Review + "/runtime.json");
            player = null;
        }

        static bool IsFinite(Vector3 value) => !(float.IsNaN(value.x) || float.IsNaN(value.y) || float.IsNaN(value.z) ||
            float.IsInfinity(value.x) || float.IsInfinity(value.y) || float.IsInfinity(value.z));

        static void WriteReport(string name, Report report)
        {
            Directory.CreateDirectory(ForestSpriteDemoBuilder.Review);
            File.WriteAllText(ForestSpriteDemoBuilder.Review + "/" + name, JsonUtility.ToJson(report, true));
        }

        static void Require(bool condition, string message) => ForestSpriteDemoBuilder.Require(condition, message);
    }
}
