using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace WeatherHouse.WildlifeJourney.Editor
{
    /// <summary>Batch entry point for a disposable validation project, never the shared editor.</summary>
    [InitializeOnLoad]
    public static class JourneyPlayReview
    {
        private const string Key = "WeatherHouse.JourneyPlayReview.";
        private const string MainProject = @"C:\unity project\Weather House";
        private const int Width = 1600, Height = 1000;
        private const float MaxSimulationTime = 420f;
        private const float MinimumLegMotionDegrees = 3f;
        private const int MinimumMovingLegs = 2, AttemptsPerAnimalPerJourney = 2;
        private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
        private static JourneyHerd herd;
        private static JourneySocial social;
        private static Camera camera;
        private static RenderTexture target;
        private static ReviewState state;
        private static int lastFrame = -1, captureAfterFrame;
        private static string pendingShot;
        private static int pendingSequence, pendingTurn;
        private static int pendingYieldingId, pendingTravellerId;
        private static Transform cycleRoot;
        private static AnimationClip cycleClip;
        private static readonly List<BoneProbe> cycleBones = new List<BoneProbe>();
        private static readonly Dictionary<Transform, Quaternion> originalBones = new Dictionary<Transform, Quaternion>();
        private static readonly HashSet<Transform> changedBones = new HashSet<Transform>();

        [Serializable]
        private sealed class Shot
        {
            public string file, subject, glyphs;
            public float simulationTime;
            public int frame, speakerId = -1, listenerId = -1, visibleBubbles, yieldingId = -1, travellerId = -1;
            public Vector3 cameraPosition;
            public Quaternion cameraRotation;
        }

        [Serializable]
        private sealed class ReviewState
        {
            public string status = "RUNNING", error = "", output, startedUtc, finishedUtc, renderPipeline;
            public bool initialized, journeyStarted, reunited, returnStarted, returnReunited;
            public float journeyStartedAt, reunionAt, returnStartedAt, returnReunionAt, lastTraceAt = -1f, simulationTime;
            public int frames, animatedBonesChanged, completedJourneys, recordedTurns;
            public List<int> swimmers = new List<int>(), bridgeWalkers = new List<int>(), queued = new List<int>();
            public List<Shot> shots = new List<Shot>();
            public List<SwimCycle> swimCycles = new List<SwimCycle>();
            public string[] missingShots;
        }

        [Serializable]
        private sealed class SwimCycle
        {
            public int attempt, animalId, journey, journeyAttempt, frames, movingLegBones;
            public string status = "RUNNING", reason = "", folder, clipName;
            public float clipLength, startedAt, endedAt, rootMinWaterClearance, headMinWaterClearance;
            public float minimumLegMotionDegrees = MinimumLegMotionDegrees;
            public int minimumMovingLegs = MinimumMovingLegs;
            public List<BoneMotion> boneMotion = new List<BoneMotion>();
            public List<float> sampleTimes = new List<float>();
            public List<string> files = new List<string>();
        }

        [Serializable]
        private sealed class BoneMotion
        {
            public string name;
            public bool head;
            public float maxLocalRotationDegrees, maxLocalDisplacement;
        }

        private sealed class BoneProbe
        {
            public Transform bone;
            public Quaternion initialRotation;
            public Vector3 initialPosition;
            public BoneMotion motion;
        }

        static JourneyPlayReview()
        {
            EditorApplication.update += Update;
            EditorApplication.playModeStateChanged += PlayChanged;
            Application.logMessageReceived += OnLog;
        }

        private static string ProjectRoot => Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        private static bool Running => SessionState.GetBool(Key + "active", false);
        private static bool Ending => SessionState.GetBool(Key + "ending", false);

        // Use -batchmode -projectPath <isolated-copy> -executeMethod ...JourneyPlayReview.Begin, without -quit/-nographics.
        public static void Begin()
        {
            try
            {
                RequireIsolatedProject();
                if (Running || EditorApplication.isPlayingOrWillChangePlaymode)
                    throw new InvalidOperationException("A play review is already running or Unity is changing Play state.");
                if (!File.Exists(JourneyLabBuilder.ScenePath))
                    throw new FileNotFoundException("Build the independent lab in the validation copy first.", JourneyLabBuilder.ScenePath);
                string folder = Path.Combine(ProjectRoot, "WildlifeJourneyReview", "Play_" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff"));
                Directory.CreateDirectory(folder);
                state = new ReviewState { output = folder, startedUtc = DateTime.UtcNow.ToString("O") };
                File.WriteAllText(Path.Combine(folder, "trajectory.csv"),
                    "simulation_time,frame,animal,state,mode,arrived,x,y,z,speed,target_site,completed_journeys,speaker,listener,glyphs,LastDecision\n");
                File.WriteAllText(Path.Combine(ProjectRoot, "WildlifeJourneyReview", "latest-play-run.txt"), folder);
                SessionState.SetString(Key + "originalPipeline", AssetDatabase.GetAssetPath(GraphicsSettings.defaultRenderPipeline));
                SessionState.SetString(Key + "originalQualityPipeline", AssetDatabase.GetAssetPath(QualitySettings.renderPipeline));
                EnsureUrp();
                EditorSceneManager.OpenScene(JourneyLabBuilder.ScenePath, OpenSceneMode.Single);
                EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(JourneyLabBuilder.ScenePath);
                SessionState.SetBool(Key + "ending", false); SessionState.SetInt(Key + "exitCode", 1);
                SessionState.SetBool(Key + "enteredPlay", false);
                SessionState.SetString(Key + "runtimeErrors", "");
                SessionState.SetString(Key + "startedTicks", DateTime.UtcNow.Ticks.ToString(Invariant));
                SessionState.SetBool(Key + "active", true);
                SaveState();
                Application.runInBackground = true;
                EditorApplication.isPlaying = true;
            }
            catch (Exception error)
            {
                if (!Application.isBatchMode) throw;
                if (state != null && !string.IsNullOrEmpty(state.output))
                {
                    state.status = "FAIL"; state.error = error.ToString(); state.finishedUtc = DateTime.UtcNow.ToString("O");
                    SaveState();
                }
                Debug.LogError("Journey Play review could not start: " + error);
                EditorApplication.Exit(1);
            }
        }

        private static void RequireIsolatedProject()
        {
            if (!Application.isBatchMode || !File.Exists(Path.Combine(ProjectRoot, ".wildlife-journey-validation")) ||
                string.Equals(ProjectRoot.TrimEnd(Path.DirectorySeparatorChar), MainProject, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Only a disposable batch validation copy with .wildlife-journey-validation may run this entry point.");
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                throw new InvalidOperationException("Visual review requires a graphics device. Do not pass -nographics.");
        }

        private static void EnsureUrp()
        {
            var pipeline = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (!pipeline)
            {
                string id = AssetDatabase.FindAssets("t:UniversalRenderPipelineAsset").FirstOrDefault();
                if (string.IsNullOrEmpty(id)) throw new InvalidOperationException("The validation copy has no URP pipeline asset.");
                pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(AssetDatabase.GUIDToAssetPath(id));
            }
            GraphicsSettings.defaultRenderPipeline = pipeline;
            QualitySettings.renderPipeline = pipeline;
            state.renderPipeline = AssetDatabase.GetAssetPath(pipeline);
        }

        private static void PlayChanged(PlayModeStateChange change)
        {
            if (!Running) return;
            if (change == PlayModeStateChange.EnteredPlayMode)
            {
                SessionState.SetBool(Key + "enteredPlay", true);
                state = LoadState(); lastFrame = -1; pendingShot = null;
                try { SetupRuntime(); }
                catch (Exception error) { Finish(error); }
            }
            else if (change == PlayModeStateChange.EnteredEditMode)
            {
                if (!Ending) Finish(new InvalidOperationException("Play stopped before the visual review completed."));
                ExitAfterPlay();
            }
        }

        private static void SetupRuntime()
        {
            RequireIsolatedProject();
            herd = Object.FindObjectOfType<JourneyHerd>();
            social = Object.FindObjectOfType<JourneySocial>();
            camera = social && social.viewCamera ? social.viewCamera : Camera.main;
            if (!herd || !social || !camera) throw new InvalidOperationException("The saved lab is missing its herd, social component or camera.");
            foreach (JourneyLabControls controls in Object.FindObjectsOfType<JourneyLabControls>()) controls.enabled = false;
            herd.autoAdvance = false; herd.SetPaused(false);
            Time.timeScale = 1f; Time.captureFramerate = 30;
            QualitySettings.vSyncCount = 0; Application.targetFrameRate = 60;
            camera.enabled = true; camera.aspect = (float)Width / Height;
            camera.GetUniversalAdditionalCameraData().requiresDepthTexture = true;
            target = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32) { name = "Journey play review 1600x1000" };
            target.Create(); camera.targetTexture = target;
            social.viewCamera = camera;
            Overview();
            originalBones.Clear(); changedBones.Clear();
            foreach (JourneyMember member in herd.members)
                if (member != null && member.animator)
                    foreach (Transform bone in member.animator.GetComponentsInChildren<Transform>()) originalBones[bone] = bone.localRotation;
            state.initialized = true;
            if (ActiveCycle() != null) RejectCycle("Domain reload interrupted continuous observation; frames were not joined across reloads.");
            SaveState();
        }

        private static void Update()
        {
            if (!Running) return;
            try
            {
                RequireIsolatedProject();
                if (Ending)
                {
                    if (!EditorApplication.isPlayingOrWillChangePlaymode) ExitAfterPlay();
                    return;
                }
                if (state == null) state = LoadState();
                long began = long.Parse(SessionState.GetString(Key + "startedTicks", "0"), Invariant);
                if (DateTime.UtcNow.Ticks - began > TimeSpan.FromMinutes(10).Ticks)
                    throw new TimeoutException("Play review exceeded ten wall-clock minutes.");
                string errors = SessionState.GetString(Key + "runtimeErrors", "");
                if (!string.IsNullOrEmpty(errors)) throw new InvalidOperationException("Unity reported runtime errors:\n" + errors);
                if (EditorApplication.isCompiling || EditorApplication.isUpdating || !EditorApplication.isPlaying ||
                    !SessionState.GetBool(Key + "enteredPlay", false)) return;
                EditorApplication.QueuePlayerLoopUpdate();
                if (!herd || !camera || !target) SetupRuntime();
                if (Time.frameCount == lastFrame) return;
                lastFrame = Time.frameCount;
                TickFrame();
            }
            catch (Exception error) { Finish(error); }
        }

        private static void TickFrame()
        {
            float elapsed = herd.SimulationTime;
            state.simulationTime = elapsed; state.frames++;
            state.completedJourneys = herd.CompletedJourneys; state.recordedTurns = social.Completed.Count;
            foreach (var pair in originalBones)
                if (pair.Key && Quaternion.Angle(pair.Value, pair.Key.localRotation) > 3f) changedBones.Add(pair.Key);
            state.animatedBonesChanged = changedBones.Count;
            foreach (JourneyMember member in herd.members)
            {
                if (member == null || !member.root) continue;
                if (member.Mode == JourneyTravelMode.Swim && !state.swimmers.Contains(member.id)) state.swimmers.Add(member.id);
                if (member.Mode == JourneyTravelMode.Bridge && !state.bridgeWalkers.Contains(member.id)) state.bridgeWalkers.Add(member.id);
                if (member.State == JourneyMemberState.WaitingForBridge && !state.queued.Contains(member.id)) state.queued.Add(member.id);
            }
            if (elapsed - state.lastTraceAt >= .25f) { Trace(); state.lastTraceAt = elapsed; SaveState(); }
            if (elapsed > MaxSimulationTime) throw new TimeoutException("The required real-time states or captures did not occur within 420 simulated seconds.");
            if (!state.journeyStarted && elapsed >= 15f)
            {
                pendingShot = null;
                if (!herd.BeginJourney(1)) throw new InvalidOperationException("The herd rejected its first east-bank journey after 15 seconds of natural idle activity.");
                state.journeyStarted = true; state.journeyStartedAt = elapsed; SaveState();
            }
            if (state.journeyStarted && !state.reunited && herd.CompletedJourneys > 0 && !herd.Travelling && herd.TargetSite == 1)
            { state.reunited = true; state.reunionAt = elapsed; SaveState(); }
            if (state.reunited && !state.returnStarted && elapsed >= state.reunionAt + 12f &&
                Has("06_reunion") && social.FirstParticipantId < 0)
            {
                pendingShot = null;
                if (!herd.BeginJourney(0)) throw new InvalidOperationException("The reunited herd rejected its return journey after the social rest.");
                state.returnStarted = true; state.returnStartedAt = elapsed; SaveState();
            }
            if (state.returnStarted && !state.returnReunited && herd.CompletedJourneys >= 2 && !herd.Travelling && herd.TargetSite == 0)
            { state.returnReunited = true; state.returnReunionAt = elapsed; SaveState(); }
            if (state.returnReunited && Has("09_return_reunion") && !HasCompleteCycle())
                throw new InvalidOperationException("Both crossings ended without one uninterrupted 12-frame swimming cycle; partial animals were not combined.");
            if (RequiredShots().Length == 0 && state.animatedBonesChanged > 5 && state.returnReunited &&
                herd.CompletedJourneys >= 2 && elapsed > state.returnReunionAt + 3f)
            { Finish(null); return; }
            if (ActiveCycle() != null) { ContinueCycle(elapsed); return; }

            if (pendingShot != null)
            {
                if (Time.frameCount < captureAfterFrame) return;
                if (!ShotStillValid(pendingShot)) { pendingShot = null; return; }
                Capture(pendingShot);
                pendingShot = null;
                return;
            }
            if (!Has("01_overview") && elapsed >= 1f) { Overview(); QueueShot("01_overview"); return; }
            JourneyMember swimmer = herd.members.FirstOrDefault(m => m != null && m.root && m.Mode == JourneyTravelMode.Swim &&
                m.ActualSpeed > .05f && Mathf.Abs(m.root.position.x) < 1.5f);
            if (swimmer != null && !Has("03_swimming_front")) { Swimming(swimmer, false); QueueShot("03_swimming_front"); return; }
            if (swimmer != null && !Has("04_swimming_side")) { Swimming(swimmer, true); QueueShot("04_swimming_side"); return; }
            if (!HasCompleteCycle())
            {
                JourneyMember candidate = herd.members.Where(m => m != null && m.root && m.Mode == JourneyTravelMode.Swim &&
                    m.ActualSpeed > .05f && Mathf.Abs(m.root.position.x) < 1.5f && CycleAttempts(m.id) < AttemptsPerAnimalPerJourney)
                    .OrderBy(m => CycleAttempts(m.id)).FirstOrDefault();
                if (candidate != null) { BeginCycle(candidate, elapsed); return; }
            }
            if (!Has("05_bridge_queue") && HasBridgeQueue())
            {
                Look(new Vector3(-8f, 5f, JourneyLabBuilder.BridgeZ - 8f), new Vector3(-.8f, .8f, JourneyLabBuilder.BridgeZ), 43f);
                QueueShot("05_bridge_queue"); return;
            }
            if (state.reunited && !Has("06_reunion"))
            {
                Vector3 center = herd.sites[1].center;
                Look(center + new Vector3(-8f, 8f, -10f), center + Vector3.up * .5f, 44f);
                QueueShot("06_reunion"); return;
            }
            if (state.returnReunited && !Has("09_return_reunion"))
            {
                Vector3 center = herd.sites[0].center;
                Look(center + new Vector3(8f, 8f, -10f), center + Vector3.up * .5f, 44f);
                QueueShot("09_return_reunion"); return;
            }
            var conversation = social.ActiveConversation;
            string dialogueShot = conversation != null && conversation.isClue ? "07_you_listener_closeup" : "02_greeting_closeup";
            if (conversation != null && !Has(dialogueShot) &&
                elapsed - conversation.startedAt > .25f && conversation.endsAt - elapsed > .25f)
            {
                JourneyMember speaker = Member(conversation.speakerId), listener = Member(conversation.listenerId);
                if (speaker != null && listener != null)
                {
                    Dialogue(speaker, listener);
                    if (social.ObserveFrom(camera))
                    {
                        pendingSequence = conversation.sequenceId; pendingTurn = conversation.turnIndex;
                        QueueShot(dialogueShot); return;
                    }
                }
            }
            if (!Has("10_yielding_companion"))
            {
                foreach (JourneyMember yielding in herd.members.Where(IsYielding))
                {
                    JourneyMember traveller = herd.members.Where(m => m != null && m.root && m.root.gameObject.activeInHierarchy && !m.Arrived &&
                        Vector3.Distance(m.root.position, yielding.root.position) < 3.5f)
                        .OrderBy(m => Vector3.Distance(m.root.position, yielding.root.position)).FirstOrDefault();
                    if (traveller == null) continue;
                    Dialogue(yielding, traveller);
                    if (!MemberInFrame(yielding) || !MemberInFrame(traveller)) continue;
                    pendingYieldingId = yielding.id; pendingTravellerId = traveller.id;
                    QueueShot("10_yielding_companion"); return;
                }
            }
        }

        private static bool ShotStillValid(string name)
        {
            if (name.Contains("closeup"))
            {
                var active = social.ActiveConversation;
                return active != null && active.sequenceId == pendingSequence && active.turnIndex == pendingTurn &&
                    social.VisibleBubbleCount > 0 && social.ObserveFrom(camera);
            }
            if (name.Contains("swimming")) return herd.members.Any(m => m != null && m.Mode == JourneyTravelMode.Swim);
            if (name.Contains("bridge_queue")) return HasBridgeQueue();
            if (name == "10_yielding_companion")
            {
                JourneyMember yielding = Member(pendingYieldingId), traveller = Member(pendingTravellerId);
                return IsYielding(yielding) && traveller != null && traveller.root.gameObject.activeInHierarchy && !traveller.Arrived &&
                    Vector3.Distance(yielding.root.position, traveller.root.position) < 3.5f && MemberInFrame(yielding) && MemberInFrame(traveller);
            }
            return true;
        }

        private static bool IsYielding(JourneyMember member) => member != null && member.root && member.root.gameObject.activeInHierarchy &&
            member.Arrived && member.ActualSpeed > .05f && member.LastDecision != null && member.LastDecision.Contains("挪开一点");

        private static bool MemberInFrame(JourneyMember member)
        {
            Vector3 point = camera.WorldToViewportPoint(member.root.position + Vector3.up * .7f);
            return point.z > camera.nearClipPlane && point.x > .1f && point.x < .9f && point.y > .1f && point.y < .9f;
        }

        private static bool HasBridgeQueue()
        {
            var crossing = herd.members.Where(m => m != null && m.root && m.root.gameObject.activeInHierarchy &&
                m.Mode == JourneyTravelMode.Bridge && Mathf.Abs(m.root.position.x) < JourneyLabBuilder.BridgeHalfLength &&
                Mathf.Abs(m.root.position.z - JourneyLabBuilder.BridgeZ) < JourneyLabBuilder.BridgeWidth * .5f)
                .OrderBy(m => m.root.position.x).ToArray();
            if (crossing.Length == 0) return false;
            if (herd.members.Any(m => m != null && m.root && m.root.gameObject.activeInHierarchy && m.State == JourneyMemberState.WaitingForBridge)) return true;
            if (crossing.Length < 2 || crossing.Max(m => m.root.position.z) - crossing.Min(m => m.root.position.z) > .35f) return false;
            for (int i = 1; i < crossing.Length; i++)
                if (crossing[i].root.position.x - crossing[i - 1].root.position.x < (crossing[i].bodyLength + crossing[i - 1].bodyLength) * .5f - .05f) return false;
            return true;
        }

        private static void QueueShot(string name) { pendingShot = name; captureAfterFrame = Time.frameCount + 2; }
        private static bool Has(string name) => state.shots.Any(shot => shot.subject == name);
        private static JourneyMember Member(int id) => herd.members.FirstOrDefault(m => m != null && m.root && m.id == id);

        private static string[] RequiredShots()
        {
            var missing = new List<string>();
            foreach (string name in new[] { "01_overview", "02_greeting_closeup", "03_swimming_front", "04_swimming_side", "05_bridge_queue", "06_reunion", "07_you_listener_closeup", "09_return_reunion" })
                if (!Has(name)) missing.Add(name);
            if (!HasCompleteCycle()) missing.Add("one_animal_uninterrupted_12_frame_swim_cycle");
            return missing.ToArray();
        }

        private static SwimCycle ActiveCycle() => state.swimCycles.LastOrDefault(c => c.status == "RUNNING");
        private static bool HasCompleteCycle() => state.swimCycles.Any(c => c.status == "PASS" && c.frames == 12);
        private static int CurrentJourney => state.returnStarted ? 2 : 1;
        private static int CycleAttempts(int animalId) => state.swimCycles.Count(c => c.animalId == animalId && c.journey == CurrentJourney);

        private static void BeginCycle(JourneyMember member, float elapsed)
        {
            if (!member.swimClip || Mathf.Abs(member.swimClip.length - 1.2f) > .04f)
                throw new InvalidOperationException("A full swimming review requires the imported 1.2-second swim clip on animal " + member.id + ".");
            if (!member.headBone) throw new InvalidOperationException("Swimming clearance requires the real head bone on animal " + member.id + ".");
            JourneySurface surface = herd.navigation.SampleSurface(member.root.position);
            if (!surface.hasWater) throw new InvalidOperationException("The swimming subject has no sampled water surface.");
            var cycle = new SwimCycle
            {
                attempt = state.swimCycles.Count + 1, animalId = member.id, journey = CurrentJourney, journeyAttempt = CycleAttempts(member.id) + 1,
                folder = "SwimAttempt_" + (state.swimCycles.Count + 1).ToString("00"),
                clipName = member.swimClip.name, clipLength = member.swimClip.length, startedAt = elapsed,
                rootMinWaterClearance = member.root.position.y - surface.waterY,
                headMinWaterClearance = member.headBone.position.y - surface.waterY
            };
            cycleBones.Clear();
            foreach (Transform bone in member.paddleBones.Where(b => b && b != member.root).Distinct()) AddCycleBone(cycle, bone, false);
            if (cycleBones.Count < MinimumMovingLegs) throw new InvalidOperationException("The swimming subject is missing its leg animation bindings.");
            AddCycleBone(cycle, member.headBone, true);
            state.swimCycles.Add(cycle); cycleRoot = member.root; cycleClip = member.swimClip;
            Directory.CreateDirectory(Path.Combine(state.output, cycle.folder));
            pendingShot = null;
            Swimming(member, true);
            SaveState();
        }

        private static void ContinueCycle(float elapsed)
        {
            SwimCycle cycle = ActiveCycle();
            JourneyMember member = Member(cycle.animalId);
            if (member == null || member.root != cycleRoot || member.swimClip != cycleClip ||
                member.Mode != JourneyTravelMode.Swim || Mathf.Abs(member.root.position.x) >= 1.5f)
            { RejectCycle("The same animal/root/clip did not stay continuously swimming in the water center."); return; }
            JourneySurface surface = herd.navigation.SampleSurface(member.root.position);
            if (!surface.hasWater || !member.headBone) { RejectCycle("Water or head context disappeared during the cycle."); return; }
            cycle.rootMinWaterClearance = Mathf.Min(cycle.rootMinWaterClearance, member.root.position.y - surface.waterY);
            cycle.headMinWaterClearance = Mathf.Min(cycle.headMinWaterClearance, member.headBone.position.y - surface.waterY);
            foreach (BoneProbe probe in cycleBones)
            {
                if (!probe.bone || !probe.bone.IsChildOf(cycleRoot)) { RejectCycle("A tracked swimming bone disappeared or changed animal."); return; }
                probe.motion.maxLocalRotationDegrees = Mathf.Max(probe.motion.maxLocalRotationDegrees,
                    Quaternion.Angle(probe.initialRotation, probe.bone.localRotation));
                probe.motion.maxLocalDisplacement = Mathf.Max(probe.motion.maxLocalDisplacement,
                    Vector3.Distance(probe.initialPosition, probe.bone.localPosition));
            }
            cycle.movingLegBones = cycle.boneMotion.Count(b => !b.head && b.maxLocalRotationDegrees >= MinimumLegMotionDegrees);
            Swimming(member, true);
            // Twelve phase samples at +0.1 .. +1.2 seconds cover the complete cycle; every intervening engine frame is checked above.
            float due = cycle.startedAt + (cycle.frames + 1) * .1f;
            if (elapsed + .0001f < due) return;
            if (elapsed - due > .05f)
            { RejectCycle("A 0.1-second sample was missed; duplicate or catch-up frames were not substituted."); return; }
            string subject = "08_swim_cycle_" + cycle.frames.ToString("00");
            Capture(subject, cycle.folder);
            cycle.files.Add(Path.Combine(cycle.folder, subject + ".png"));
            cycle.sampleTimes.Add(elapsed);
            cycle.frames++; cycle.endedAt = elapsed;
            if (cycle.frames == 12)
            {
                if (elapsed - cycle.startedAt < 1.195f) throw new InvalidOperationException("The swimming review did not cover a full 1.2-second window.");
                if (cycle.movingLegBones < MinimumMovingLegs)
                { RejectCycle("This swimmer did not move at least two leg bones by three degrees during the captured cycle."); return; }
                if (cycle.headMinWaterClearance <= 0f)
                { RejectCycle("The swimmer's head bone did not remain above the sampled water surface for the complete cycle."); return; }
                cycle.status = "PASS";
                // Canonical files come from one successful attempt only. Partial attempts remain separately labelled for diagnosis.
                foreach (string file in cycle.files)
                    File.Copy(Path.Combine(state.output, file), Path.Combine(state.output, Path.GetFileName(file)), false);
                cycleRoot = null; cycleClip = null; cycleBones.Clear();
            }
            SaveState();
        }

        private static void RejectCycle(string reason)
        {
            SwimCycle cycle = ActiveCycle();
            if (cycle == null) return;
            cycle.status = "FAIL"; cycle.reason = reason; cycle.endedAt = herd ? herd.SimulationTime : state.simulationTime;
            cycleRoot = null; cycleClip = null; cycleBones.Clear();
            SaveState();
        }

        private static void AddCycleBone(SwimCycle cycle, Transform bone, bool head)
        {
            // Parent-local measurements exclude the swimming animal's translation and turning.
            var motion = new BoneMotion { name = bone.name, head = head };
            cycle.boneMotion.Add(motion);
            cycleBones.Add(new BoneProbe { bone = bone, initialRotation = bone.localRotation, initialPosition = bone.localPosition, motion = motion });
        }

        private static void Overview() { Look(new Vector3(-22f, 26f, -31f), new Vector3(0f, .4f, 2f), 46f); }
        private static void Look(Vector3 position, Vector3 focus, float fov)
        { camera.transform.position = position; camera.transform.LookAt(focus); camera.fieldOfView = fov; }

        private static void Swimming(JourneyMember member, bool side)
        {
            Vector3 forward = member.root.forward; forward.y = 0f; forward.Normalize();
            Vector3 direction = side ? Vector3.Cross(Vector3.up, forward) : forward;
            Vector3 focus = member.root.position + Vector3.up * .65f;
            Look(focus + direction * 4.8f + Vector3.up * 1.1f, focus, 39f);
        }

        private static void Dialogue(JourneyMember speaker, JourneyMember listener)
        {
            Vector3 center = (speaker.root.position + listener.root.position) * .5f;
            Vector3 axis = listener.root.position - speaker.root.position; axis.y = 0f;
            Vector3 side = Vector3.Cross(Vector3.up, axis.normalized);
            if (side.sqrMagnitude < .01f) side = Vector3.back;
            Look(center + side * 5.2f + Vector3.up * 2.4f, center + Vector3.up * .9f, 40f);
        }

        private static void Capture(string subject, string folder = "")
        {
            Canvas.ForceUpdateCanvases();
            camera.Render();
            RenderTexture old = RenderTexture.active;
            Texture2D pixels = null;
            try
            {
                RenderTexture.active = target;
                pixels = new Texture2D(Width, Height, TextureFormat.RGB24, false);
                pixels.ReadPixels(new Rect(0f, 0f, Width, Height), 0, 0); pixels.Apply(false, false);
                string file = Path.Combine(folder, subject + ".png");
                File.WriteAllBytes(Path.Combine(state.output, file), pixels.EncodeToPNG());
                var active = social.ActiveConversation;
                state.shots.Add(new Shot
                {
                    file = file, subject = subject, simulationTime = herd.SimulationTime, frame = Time.frameCount,
                    cameraPosition = camera.transform.position, cameraRotation = camera.transform.rotation,
                    speakerId = active == null ? -1 : active.speakerId, listenerId = active == null ? -1 : active.listenerId,
                    glyphs = active == null ? "" : string.Join(" ", active.glyphIds), visibleBubbles = social.VisibleBubbleCount,
                    yieldingId = subject == "10_yielding_companion" ? pendingYieldingId : -1,
                    travellerId = subject == "10_yielding_companion" ? pendingTravellerId : -1
                });
                SaveState();
                Debug.Log("Journey Play capture: " + Path.Combine(state.output, file));
            }
            finally
            {
                RenderTexture.active = old;
                if (pixels) Object.Destroy(pixels);
            }
        }

        private static void Trace()
        {
            var text = new StringBuilder();
            var active = social.ActiveConversation;
            foreach (JourneyMember member in herd.members)
            {
                if (member == null || !member.root) continue;
                Vector3 p = member.root.position;
                text.AppendFormat(Invariant, "{0:F3},{1},{2},{3},{4},{5},{6:F3},{7:F3},{8:F3},{9:F3},{10},{11},{12},{13},{14},{15}\n",
                    herd.SimulationTime, Time.frameCount, member.id, member.State, member.Mode, member.Arrived,
                    p.x, p.y, p.z, member.ActualSpeed, herd.TargetSite, herd.CompletedJourneys,
                    active == null ? -1 : active.speakerId, active == null ? -1 : active.listenerId,
                    active == null ? "" : string.Join(" ", active.glyphIds), Csv(member.LastDecision));
            }
            File.AppendAllText(Path.Combine(state.output, "trajectory.csv"), text.ToString());
        }

        private static string Csv(string value) => "\"" + (value ?? "").Replace("\"", "\"\"") + "\"";

        private static void Finish(Exception error)
        {
            if (Ending) return;
            SessionState.SetBool(Key + "ending", true);
            SessionState.SetInt(Key + "exitCode", error == null ? 0 : 1);
            try
            {
                if (state == null) state = LoadState();
                state.status = error == null ? "PASS" : "FAIL"; state.error = error == null ? "" : error.ToString();
                state.finishedUtc = DateTime.UtcNow.ToString("O"); state.missingShots = RequiredShots();
                SaveState();
            }
            catch (Exception writeError) { Debug.LogError("Could not write Play review summary: " + writeError); SessionState.SetInt(Key + "exitCode", 1); }
            if (camera) camera.targetTexture = null;
            if (target) { target.Release(); Object.DestroyImmediate(target); target = null; }
            Time.captureFramerate = 0; Time.timeScale = 1f;
            if (EditorApplication.isPlayingOrWillChangePlaymode) EditorApplication.isPlaying = false;
            else ExitAfterPlay();
        }

        private static void ExitAfterPlay()
        {
            if (!Running || EditorApplication.isPlayingOrWillChangePlaymode) return;
            GraphicsSettings.defaultRenderPipeline = AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>(SessionState.GetString(Key + "originalPipeline", ""));
            QualitySettings.renderPipeline = AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>(SessionState.GetString(Key + "originalQualityPipeline", ""));
            int code = SessionState.GetInt(Key + "exitCode", 1);
            SessionState.SetBool(Key + "active", false); SessionState.SetBool(Key + "ending", false);
            EditorApplication.delayCall += () => EditorApplication.Exit(code);
        }

        private static void SaveState()
        {
            string json = JsonUtility.ToJson(state, true);
            SessionState.SetString(Key + "state", json);
            File.WriteAllText(Path.Combine(state.output, "summary.json"), json);
        }

        private static ReviewState LoadState()
        {
            string json = SessionState.GetString(Key + "state", "");
            if (string.IsNullOrEmpty(json)) throw new InvalidOperationException("Play review lost its session checkpoint.");
            return JsonUtility.FromJson<ReviewState>(json);
        }

        private static void OnLog(string message, string stack, LogType type)
        {
            if (!Running || Ending || (type != LogType.Error && type != LogType.Exception && type != LogType.Assert)) return;
            string previous = SessionState.GetString(Key + "runtimeErrors", "");
            if (previous.Length < 8000) SessionState.SetString(Key + "runtimeErrors", previous + message + "\n" + stack + "\n");
        }
    }
}
