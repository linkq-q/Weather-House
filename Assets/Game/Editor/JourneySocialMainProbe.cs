using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;
using ForestSpriteCharacter;
using WeatherHouse.Vegetation;
using WeatherHouse.WildlifeJourney;
using WeatherHousePrototype;
using Object = UnityEngine.Object;

namespace WeatherHouse.Review
{
    // Sampling only. RuralMainPlayReview owns Play, notebook protection and ordinary scene reload.
    public static class JourneySocialMainProbe
    {
        [Serializable] public sealed class FadeFrame
        {
            public int frame, sequence = -1, turn = -1, speakerId = -1, listenerId = -1;
            public float seconds, simulationTime, alpha;
            public bool hasActiveConversation, groupExists, groupEnabled, groupActiveInHierarchy, bubbleActive, sameGameCamera;
        }
        [Serializable] public sealed class Frame
        {
            public int frame, sequence, turn, speakerId, listenerId, visibleCount;
            public float seconds, simulationTime, age, speakerHeadDegrees, listenerHeadDegrees, alpha;
            public string intent; public string[] glyphs;
            public Vector3 speakerPosition, listenerPosition; public Quaternion speakerHead, listenerHead;
            public Vector2 bubbleSize; public bool originalAnimalBubble, sameGameCamera, speakerOverlay, listenerOverlay;
        }
        [Serializable] public sealed class Turn
        {
            public int sequence, turn, speakerId, listenerId, visibleFrames;
            public float participantCooldown, pairCooldown, speakerReadyAt, listenerReadyAt, pairReadyAt, peakHeadDegrees;
            public Frame first, peak; public List<string> images = new List<string>();
        }
        [Serializable] public sealed class Shot
        { public string path; public Frame sample; public float requestedAt; public bool written, cameraWarmup; }
        [Serializable] public sealed class Report
        {
            public string result = "RUNNING", error = "", evidence = "Natural existing JourneySocial observed through the real PlayerViewCamera. No forced conversation, seed/cooldown change, actor teleport or scripted input. Optional temporary camera framing is explicitly recorded; current visibility can influence the scheduler's existing camera preference. Edit lifecycle and notebook are owned by RuralMainPlayReview. Numeric observations are not visual acceptance.";
            public bool temporaryFraming, restored, configurationUnchanged;
            public float seconds, cameraDwell, participantCooldown, pairCooldown; public int frames, visibleFrames, randomSeed;
            public Vector3 playerStart, playerEnd, cameraPosition; public Quaternion cameraRotation;
            public List<Turn> turns = new List<Turn>(); public List<Shot> shots = new List<Shot>();
        }
        static readonly BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        static readonly FieldInfo HeadPose = typeof(JourneyMember).GetField("headPose", Private), Overlay = typeof(JourneyMember).GetField("overlay", Private);
        static readonly FieldInfo Bubble = typeof(JourneySocial).GetField("bubble", Private), Group = typeof(JourneySocial).GetField("bubbleGroup", Private);
        static readonly FieldInfo ParticipantReady = typeof(JourneySocial).GetField("participantReady", Private), PairReady = typeof(JourneySocial).GetField("pairReady", Private);
        static JourneyHerd herd; static JourneySocial social; static GameplaySession session; static ForestSpriteDemoController player;
        static Camera camera; static VegetationTopDownCamera orbit; static Vector3 originalPosition; static Quaternion originalRotation;
        static bool orbitEnabled, externalCamera, running; static float started, duration; static int lastFrame;
        static int seed; static string directory; static Report report; static StreamWriter trace;
        static StreamWriter fadeTrace; static int fadeTail;
        static readonly HashSet<int> checkedSequences = new HashSet<int>();
        public static bool IsComplete { get; private set; }
        public static string Failure => IsComplete && report != null && report.result != "COMPLETE" ? report.error : null;

        public static string Begin(string folder, bool temporaryFraming = true, float maxSeconds = 90)
        {
            if (running || !SessionState.GetBool("WeatherHouse.RuralMainPlay.active", false) || !EditorApplication.isPlaying || EditorApplication.isPaused)
                throw new InvalidOperationException("Requires the existing protected, unpaused RuralMainPlayReview lifecycle.");
            session = Object.FindObjectOfType<GameplaySession>(); player = session ? session.forestSprite : null; camera = session ? session.PlayerViewCamera : null;
            herd = Object.FindObjectsOfType<JourneyHerd>().SingleOrDefault(h => h.isActiveAndEnabled && h.social && h.members != null && h.members.Length > 1);
            social = herd ? herd.social : null; orbit = camera ? camera.GetComponent<VegetationTopDownCamera>() : null;
            if (!session || !player || !player.enabled || player.ScriptedInput || !camera || !camera.enabled || !orbit || !herd || !social || !social.isActiveAndEnabled)
                throw new InvalidOperationException("Need the original active player/Game camera and one natural social herd; another input owner is not allowed.");
            if (HeadPose == null || Overlay == null || Bubble == null || Group == null || ParticipantReady == null || PairReady == null)
                throw new InvalidOperationException("Read-only diagnostic fields changed.");
            directory = folder; Directory.CreateDirectory(directory); duration = Mathf.Clamp(maxSeconds, 10, 180); started = Time.realtimeSinceStartup;
            originalPosition = camera.transform.position; originalRotation = camera.transform.rotation; orbitEnabled = orbit.enabled; externalCamera = player.externalCameraControl;
            seed = social.randomSeed; report = new Report { temporaryFraming = temporaryFraming, playerStart = player.transform.position,
                randomSeed = seed, participantCooldown = social.participantCooldown, pairCooldown = social.pairCooldown };
            checkedSequences.Clear(); lastFrame = -1; fadeTail = 0; IsComplete = false; running = true;
            try
            {
                trace = new StreamWriter(Path.Combine(directory, "social-frames.jsonl"));
                fadeTrace = new StreamWriter(Path.Combine(directory, "social-fade-frames.jsonl"));
                JourneySocialAvailabilityProbe.Begin(directory, herd);
                if (temporaryFraming)
                {
                    var anchor = herd.members.First(m => m != null && m.root && m.root.gameObject.activeInHierarchy).root.position;
                    var nearby = herd.members.Where(m => m != null && m.root && m.root.gameObject.activeInHierarchy && Vector3.Distance(m.root.position, anchor) < 7).ToArray();
                    Vector3 center = nearby.Aggregate(Vector3.zero, (sum, m) => sum + m.root.position) / nearby.Length;
                    orbit.enabled = false; player.externalCameraControl = true;
                    camera.transform.SetPositionAndRotation(center + new Vector3(5.5f, 5.5f, -7), Quaternion.LookRotation(new Vector3(-5.5f, -4.7f, 7), Vector3.up));
                }
                report.cameraPosition = camera.transform.position; report.cameraRotation = camera.transform.rotation;
                EditorApplication.update += Tick; RenderPipelineManager.endCameraRendering += Rendered;
                File.WriteAllText(Path.Combine(directory, "planned.json"), JsonUtility.ToJson(report, true));
            }
            catch (Exception e) { report.error = e.ToString(); Finish("FAILED_TO_START"); throw; }
            return directory;
        }
        static void Tick()
        {
            if (!running) return;
            try
            {
                report.seconds = Time.realtimeSinceStartup - started;
                foreach (var shot in report.shots)
                {
                    shot.written = File.Exists(shot.path) && new FileInfo(shot.path).Length > 0;
                    if (!shot.written && Time.realtimeSinceStartup - shot.requestedAt > 15) throw new InvalidOperationException("Game screenshot write exceeded 15 seconds: " + shot.path);
                }
                if (!herd || !social || !camera || !player || player.ScriptedInput) throw new InvalidOperationException("Natural observation ownership changed.");
                if (social.randomSeed != seed || social.participantCooldown != report.participantCooldown || social.pairCooldown != report.pairCooldown)
                    throw new InvalidOperationException("Social seed/cooldown changed during observation.");
                if (report.seconds >= duration && report.shots.All(s => s.written))
                {
                    if (report.visibleFrames == 0) { report.error = "No natural visible exchange within the observation window."; Finish("NO_OBSERVATION"); }
                    else if (!HasCapturedGesture(report)) { report.error = "Natural speech observed, but no written Game screenshot of visible social head motion above 1 degree."; Finish("NO_GESTURE_EVIDENCE"); }
                    else Finish("COMPLETE");
                }
            }
            catch (Exception e) { report.error = e.ToString(); Finish("FAILED"); }
        }
        static void Rendered(ScriptableRenderContext context, Camera renderedCamera)
        {
            if (!running || renderedCamera != camera || Time.frameCount == lastFrame) return;
            lastFrame = Time.frameCount;
            try
            {
                report.frames++; JourneySocialAvailabilityProbe.Sample(herd); var conversation = social.ActiveConversation; SampleFade(conversation); if (conversation == null) return;
                var speaker = herd.members.SingleOrDefault(m => m != null && m.id == conversation.speakerId);
                var listener = herd.members.SingleOrDefault(m => m != null && m.id == conversation.listenerId);
                if (speaker == null || listener == null || !speaker.headBone || !listener.headBone) throw new InvalidOperationException("Active conversation has no real participant bones.");
                var bubble = Bubble.GetValue(social) as RectTransform; var group = Group.GetValue(social) as CanvasGroup;
                var graphic = bubble ? bubble.GetComponent<ProceduralUiGraphic>() : null;
                var glyph = bubble ? bubble.GetComponentInChildren<AnimalGlyphGraphic>(true) : null;
                var f = new Frame { frame = Time.frameCount, seconds = Time.realtimeSinceStartup - started, simulationTime = herd.SimulationTime,
                    sequence = conversation.sequenceId, turn = conversation.turnIndex, speakerId = speaker.id, listenerId = listener.id,
                    intent = conversation.intent, glyphs = conversation.glyphIds, age = herd.SimulationTime - conversation.startedAt,
                    visibleCount = social.VisibleBubbleCount, alpha = group ? group.alpha : 0, bubbleSize = bubble ? bubble.rect.size : Vector2.zero,
                    originalAnimalBubble = graphic && graphic.mark == UiMark.AnimalBubble && glyph,
                    sameGameCamera = social.CurrentViewCamera == camera, speakerPosition = speaker.root.position, listenerPosition = listener.root.position,
                    speakerHead = speaker.headBone.localRotation, listenerHead = listener.headBone.localRotation,
                    speakerOverlay = (bool)Overlay.GetValue(speaker), listenerOverlay = (bool)Overlay.GetValue(listener) };
                f.speakerHeadDegrees = f.speakerOverlay ? Quaternion.Angle((Quaternion)HeadPose.GetValue(speaker), f.speakerHead) : 0;
                f.listenerHeadDegrees = f.listenerOverlay ? Quaternion.Angle((Quaternion)HeadPose.GetValue(listener), f.listenerHead) : 0;
                trace.WriteLine(JsonUtility.ToJson(f));
                var turn = report.turns.FirstOrDefault(t => t.sequence == f.sequence && t.turn == f.turn);
                if (turn == null)
                {
                    var individual = (Dictionary<int, float>)ParticipantReady.GetValue(social); var pair = (Dictionary<long, float>)PairReady.GetValue(social);
                    individual.TryGetValue(speaker.id, out float speakerAt); individual.TryGetValue(listener.id, out float listenerAt);
                    pair.TryGetValue(((long)Math.Min(speaker.id, listener.id) << 32) | (uint)Math.Max(speaker.id, listener.id), out float pairAt);
                    turn = new Turn { sequence = f.sequence, turn = f.turn, speakerId = speaker.id, listenerId = listener.id, first = f,
                        participantCooldown = social.participantCooldown, pairCooldown = social.pairCooldown, speakerReadyAt = speakerAt, listenerReadyAt = listenerAt, pairReadyAt = pairAt };
                    report.turns.Add(turn);
                    if (checkedSequences.Add(f.sequence) && (speakerAt > f.simulationTime || listenerAt > f.simulationTime || pairAt > f.simulationTime))
                        throw new InvalidOperationException("A natural new sequence started before the existing cooldown deadline.");
                }
                float degrees = Mathf.Max(f.speakerHeadDegrees, f.listenerHeadDegrees); float previousPeak = turn.peakHeadDegrees;
                if (turn.peak == null || degrees > previousPeak) { turn.peak = f; turn.peakHeadDegrees = degrees; }
                if (f.visibleCount == 0) return;
                report.visibleFrames++; turn.visibleFrames++;
                if (!f.originalAnimalBubble || !f.sameGameCamera) throw new InvalidOperationException("Visible speech is not the existing AnimalBubble/AnimalGlyphGraphic on the current Game camera.");
                // Early natural exchanges can finish before grass settles; retain labeled real captures instead of losing them.
                if (!CaptureReady(f, report.frames)) return;
                if (turn.images.Count == 0) Capture(turn, f, false);
                // Keep labeled rising-peak frames; the exact sampled maximum stays in turn.peak.
                float captured = LastCapturedPeak(turn);
                if (degrees > 1 && (degrees >= captured + 4 || (previousPeak > degrees && previousPeak - degrees < .5f && degrees > captured + .1f))) Capture(turn, f, true);
            }
            catch (Exception e) { report.error = e.ToString(); Finish("FAILED"); }
        }
        static void SampleFade(JourneyConversation conversation)
        {
            var group = Group.GetValue(social) as CanvasGroup; var bubble = Bubble.GetValue(social) as RectTransform;
            bool active = group && group.enabled && group.gameObject.activeInHierarchy && bubble && bubble.gameObject.activeInHierarchy;
            if (active) fadeTail = 2;
            else if (fadeTail == 0) return;
            else fadeTail--;
            var frame = new FadeFrame { frame = Time.frameCount, seconds = Time.realtimeSinceStartup - started,
                simulationTime = herd.SimulationTime, alpha = group ? group.alpha : -1,
                hasActiveConversation = conversation != null, groupExists = group, groupEnabled = group && group.enabled,
                groupActiveInHierarchy = group && group.gameObject.activeInHierarchy, bubbleActive = bubble && bubble.gameObject.activeInHierarchy,
                sameGameCamera = social.CurrentViewCamera == camera };
            // These IDs describe the logical active conversation, never invent an owner for a retiring bubble.
            if (conversation != null)
            { frame.sequence = conversation.sequenceId; frame.turn = conversation.turnIndex; frame.speakerId = conversation.speakerId; frame.listenerId = conversation.listenerId; }
            fadeTrace.WriteLine(JsonUtility.ToJson(frame));
        }
        static bool CaptureReady(Frame frame, int renderedFrames)
            => renderedFrames >= 3 && frame.visibleCount > 0 && frame.alpha >= .2f && frame.originalAnimalBubble && frame.sameGameCamera;
        static bool HasCapturedGesture(Report value)
            => value.shots.Any(s => s.written && s.sample != null && s.sample.visibleCount > 0 && s.sample.alpha >= .2f &&
                s.sample.originalAnimalBubble && s.sample.sameGameCamera && Mathf.Max(s.sample.speakerHeadDegrees, s.sample.listenerHeadDegrees) > 1);
        static float LastCapturedPeak(Turn turn)
        { return report.shots.Where(s => s.sample.sequence == turn.sequence && s.sample.turn == turn.turn).Select(s => Mathf.Max(s.sample.speakerHeadDegrees, s.sample.listenerHeadDegrees)).DefaultIfEmpty(0).Max(); }
        static void Capture(Turn turn, Frame frame, bool peak)
        {
            string path = Path.Combine(directory, "social_" + turn.sequence + "_turn" + turn.turn + (peak ? "_peak_" + frame.frame + "_Game.png" : "_bubble_Game.png"));
            ScreenCapture.CaptureScreenshot(path);
            var shot = report.shots.FirstOrDefault(s => s.path == path);
            if (shot == null) { shot = new Shot { path = path }; report.shots.Add(shot); turn.images.Add(path); }
            shot.sample = frame; shot.requestedAt = Time.realtimeSinceStartup; shot.written = false; shot.cameraWarmup = frame.seconds < 5;
        }
        public static string Finish(string status)
        {
            if (!running) return directory; running = false; EditorApplication.update -= Tick; RenderPipelineManager.endCameraRendering -= Rendered;
            try
            {
                JourneySocialAvailabilityProbe.End();
                trace?.Dispose(); trace = null;
                fadeTrace?.Dispose(); fadeTrace = null;
                if (report.temporaryFraming)
                {
                    if (camera) camera.transform.SetPositionAndRotation(originalPosition, originalRotation);
                    if (orbit) orbit.enabled = orbitEnabled; if (player) player.externalCameraControl = externalCamera;
                }
                report.playerEnd = player ? player.transform.position : report.playerStart;
                report.configurationUnchanged = social && social.randomSeed == seed && social.participantCooldown == report.participantCooldown && social.pairCooldown == report.pairCooldown;
                report.restored = !report.temporaryFraming || (camera && orbit && player && camera.transform.position == originalPosition &&
                    Quaternion.Angle(camera.transform.rotation, originalRotation) < .001f && orbit.enabled == orbitEnabled && player.externalCameraControl == externalCamera);
                if (!report.restored || !report.configurationUnchanged) { status = "FAILED"; report.error += " Camera restoration or social configuration check failed."; }
                report.cameraDwell = Time.realtimeSinceStartup - started;
            }
            catch (Exception e) { status = "FAILED"; report.error += e.ToString(); }
            if (status == "COMPLETE" && !HasCapturedGesture(report))
            { status = "NO_GESTURE_EVIDENCE"; report.error += " Completion requires a written screenshot of visible original-bubble head motion above 1 degree."; }
            report.result = status; IsComplete = true; File.WriteAllText(Path.Combine(directory, "sample.json"), JsonUtility.ToJson(report, true)); return directory;
        }
    }
}
