using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using WeatherHousePrototype;

namespace WeatherHouse.Life.Editor
{
    public static partial class LifeOnboardingReview
    {
        const string CaptureArmed = Key + ".NaturalOpeningArmed", CaptureRunning = Key + ".NaturalOpeningRunning", SuiteRunning = Key + ".PostCompletionRunning";
        const string CapturePath = Folder + "/opening-capture-status.json", SuitePath = Folder + "/post-completion-status.json";
        [Serializable] sealed class OpeningShot
        {
            public float targetSeconds, playbackSeconds = -1;
            public string status = "PENDING", file, sha256, utc, reason;
            public int width, height, unityFrame;
            public bool reducedMotion;
        }
        [Serializable] sealed class OpeningCaptureReport
        {
            public string status = "RUNNING", utc = DateTime.UtcNow.ToString("O"), error, directory;
            public string boundary = "Actual GameView PNGs during the unchanged natural 44-second opening. Read-only PlaybackTime; no Step/Sample/Skip, time-scale change or timeline restart. A missed target is recorded, never recreated. Render timing tolerance is 0.5 seconds. Images require root visual review.";
            public OpeningShot[] shots = new[] { 4f, 14f, 25f, 37f, 41f }.Select(t => new OpeningShot { targetSeconds = t }).ToArray();
        }
        [Serializable] sealed class HandlerReceipt { public string handler, status, result, coverage, manifest; public List<string> gaps; }
        [Serializable] sealed class CompletionReport
        {
            public string status = "RUNNING", utc = DateTime.UtcNow.ToString("O"), error;
            public string boundary = "Explicit same dedicated isolated Play, actual completed tutorial and idle HUD. Handler callback plus UI manifest determine results; menu dispatch is not handler success. Completed tutorial Update is temporarily disabled and restored to prevent fixture word confirmations from minting tutorial hint credit. No automatic Play exit or main-scene save.";
            public List<HandlerReceipt> handlers = new List<HandlerReceipt>();
        }
        [Serializable] sealed class UiCaptureSummary { public string status = null; public List<string> gaps = null; public string[] missingUiIds = null; }

        static void ObserveSupplementalReviewState(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(CaptureArmed, false))
                EditorApplication.delayCall += BeginNaturalOpeningCapture;
            if (change != PlayModeStateChange.EnteredEditMode) return;
            if (SessionState.GetBool(CaptureRunning, false))
            {
                var report = File.Exists(CapturePath) ? JsonUtility.FromJson<OpeningCaptureReport>(File.ReadAllText(CapturePath)) : new OpeningCaptureReport();
                EndCapture(report, new InvalidOperationException("Play ended before the natural capture coroutine finished."));
            }
            if (SessionState.GetBool(SuiteRunning, false))
            {
                var report = File.Exists(SuitePath) ? JsonUtility.FromJson<CompletionReport>(File.ReadAllText(SuitePath)) : new CompletionReport();
                report.status = "FAIL_INTERRUPTED"; report.error = "Play ended before the post-completion suite returned."; WriteReview(SuitePath, report); SessionState.SetBool(SuiteRunning, false);
            }
        }
        [MenuItem("Weather House/Review 20261027/Arm natural onboarding opening capture")]
        public static void ArmNaturalOpeningCapture()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Arm before the dedicated Begin; use Capture current natural onboarding opening in existing Play.");
            SessionState.SetBool(CaptureArmed, true);
            WriteReview(CapturePath, new OpeningCaptureReport { status = "ARMED_WAITING_FOR_DEDICATED_PLAY" });
        }
        [MenuItem("Weather House/Review 20261027/Capture current natural onboarding opening")]
        public static void BeginNaturalOpeningCapture()
        {
            if (SessionState.GetBool(CaptureRunning, false) || SessionState.GetBool(SuiteRunning, false))
            { Directory.CreateDirectory(Folder); File.WriteAllText(Folder + "/capture-command-receipt.txt", "REFUSED already running; existing report preserved"); return; }
            var report = new OpeningCaptureReport();
            try
            {
                var life = DedicatedSession(false);
                SessionState.SetBool(CaptureArmed, false); SessionState.SetBool(CaptureRunning, true);
                report.directory = Path.GetFullPath(Folder + "/opening-natural-" + DateTime.UtcNow.ToString("HHmmssfff")); Directory.CreateDirectory(report.directory);
                WriteReview(CapturePath, report);
                life.StartCoroutine(CollectReview(CaptureNaturalOpening(life, report), 80, error => EndCapture(report, error)));
            }
            catch (Exception error) { EndCapture(report, error); }
        }
        static IEnumerator CaptureNaturalOpening(LifeSession life, OpeningCaptureReport report)
        {
            float end = Time.realtimeSinceStartup + 15; LifeOpeningCinematic movie = null;
            while (!(movie = life.GetComponent<LifeOpeningCinematic>()) || !movie.IsPlaying)
            {
                if (Time.realtimeSinceStartup > end) throw new TimeoutException("Natural opening did not start; no replay or timeline change was attempted.");
                yield return null;
            }
            foreach (var shot in report.shots)
            {
                while (movie && movie.IsPlaying && movie.PlaybackTime < shot.targetSeconds) yield return null;
                if (!movie || !movie.IsPlaying || movie.PlaybackTime > shot.targetSeconds + .5f)
                { shot.status = "MISSING"; shot.playbackSeconds = movie ? movie.PlaybackTime : -1; shot.reason = "Target already passed or opening stopped; no substitute image."; WriteReview(CapturePath, report); continue; }
                yield return new WaitForEndOfFrame();
                if (!movie || !movie.IsPlaying || movie.PlaybackTime > shot.targetSeconds + .5f)
                { shot.status = "MISSING"; shot.playbackSeconds = movie ? movie.PlaybackTime : -1; shot.reason = "No rendered GameView frame within the target timing tolerance."; WriteReview(CapturePath, report); continue; }
                Texture2D pixels = null;
                try
                {
                    pixels = ScreenCapture.CaptureScreenshotAsTexture(); if (!pixels) throw new InvalidOperationException("GameView returned no pixels.");
                    shot.playbackSeconds = movie.PlaybackTime; shot.reducedMotion = movie.ReducedMotion; shot.width = pixels.width; shot.height = pixels.height; shot.unityFrame = Time.frameCount; shot.utc = DateTime.UtcNow.ToString("O");
                    shot.file = Path.Combine(report.directory, "opening-" + ((int)shot.targetSeconds).ToString("D3") + "s.png");
                    File.WriteAllBytes(shot.file, pixels.EncodeToPNG()); shot.sha256 = Hash(shot.file); shot.status = "CAPTURED_ACTUAL_GAMEVIEW";
                }
                catch (Exception error) { shot.status = "MISSING"; shot.reason = error.ToString(); }
                finally { if (pixels) UnityEngine.Object.Destroy(pixels); }
                WriteReview(CapturePath, report);
            }
        }
        static void EndCapture(OpeningCaptureReport report, Exception error)
        {
            foreach (var shot in report.shots.Where(s => s.status == "PENDING")) { shot.status = "MISSING"; shot.reason = "Capture ended before this target: " + error; }
            report.error = error?.ToString(); report.status = error != null ? "FAIL_CAPTURE" : report.shots.All(s => s.status == "CAPTURED_ACTUAL_GAMEVIEW") ? "CAPTURED_5_OF_5_VISUAL_REVIEW_PENDING" : "INCOMPLETE_MISSING_TARGETS";
            WriteReview(CapturePath, report); SessionState.SetBool(CaptureRunning, false); SessionState.SetBool(CaptureArmed, false);
        }
        [MenuItem("Weather House/Review 20261027/Check onboarding post-completion suite")]
        public static void BeginPostCompletionSuite()
        {
            if (SessionState.GetBool(SuiteRunning, false) || SessionState.GetBool(CaptureRunning, false))
            { Directory.CreateDirectory(Folder); File.WriteAllText(Folder + "/suite-command-receipt.txt", "REFUSED already running; existing report preserved"); return; }
            var report = new CompletionReport();
            try
            {
                var life = DedicatedSession(true);
                SessionState.SetBool(SuiteRunning, true); WriteReview(SuitePath, report);
                life.StartCoroutine(CollectReview(PostCompletion(life, report), 600, error =>
                {
                    report.error = error?.ToString(); report.status = error != null ? "FAIL_STOPPED_AFTER_HANDLER" : report.handlers.Any(h => h.coverage == "PASS_WITH_GAPS") ? "PASS_HANDLERS_WITH_DECLARED_UI_GAPS" : "PASS_HANDLERS_VISUAL_REVIEW_PENDING";
                    WriteReview(SuitePath, report); SessionState.SetBool(SuiteRunning, false);
                }));
            }
            catch (Exception error) { report.status = "FAIL_GUARD"; report.error = error.ToString(); WriteReview(SuitePath, report); }
        }
        static IEnumerator PostCompletion(LifeSession life, CompletionReport report)
        {
            var tutorial = life.GetComponent<LifeOnboarding>(); bool wasEnabled = tutorial.enabled; string sidecarHash = Hash(tutorial.StatePath); tutorial.enabled = false;
            foreach (var speech in UnityEngine.Object.FindObjectsOfType<LifeOnboardingSpeech>().Where(s => s.owner == tutorial)) speech.Silence();
            try
            {
                for (int i = 0; i < 3; i++)
                {
                    string result = null; var receipt = new HandlerReceipt { handler = new[] { "opening-lifecycle", "closed-buildings", "ui-capture" }[i], status = "RUNNING" }; report.handlers.Add(receipt); WriteReview(SuitePath, report);
                    if (i == 0) yield return LifeOpeningCinematicChecks.Run(life.Gameplay, value => result = value);
                    else if (i == 1) yield return LifeClosedBuildingsPlayChecks20261027.Run(life, value => result = value);
                    else yield return LifeUiCaptureReview.Run(life, value => result = value);
                    receipt.result = result; receipt.status = result != null && result.StartsWith("PASS ", StringComparison.Ordinal) ? "PASS_HANDLER" : "FAIL_HANDLER_OR_MISSING_CALLBACK";
                    if (i == 2 && receipt.status == "PASS_HANDLER")
                    {
                        int split = result.LastIndexOf(". ", StringComparison.Ordinal); string path = split >= 0 ? result.Substring(split + 2).Trim() : "";
                        string allowed = Path.GetFullPath("LifeGameplayReview/20261027/ui-capture") + Path.DirectorySeparatorChar;
                        if (!File.Exists(path) || !Path.GetFullPath(path).StartsWith(allowed, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("UI handler returned no accessible current UI capture manifest: " + result);
                        var summary = JsonUtility.FromJson<UiCaptureSummary>(File.ReadAllText(path)); receipt.manifest = path; receipt.coverage = summary.status; receipt.gaps = summary.gaps;
                        if (summary.status != "PASS" && summary.status != "PASS_WITH_GAPS") receipt.status = "FAIL_UI_MANIFEST";
                        if (!string.IsNullOrEmpty(life.Gameplay.LastSaveError) || !string.IsNullOrEmpty(life.LastSaveError)) receipt.status = "FAIL_UI_SAVE_RESTORE";
                        DedicatedSession(true);
                    }
                    WriteReview(Folder + "/post-" + receipt.handler + "-receipt.json", receipt); WriteReview(SuitePath, report);
                    if (receipt.status != "PASS_HANDLER") throw new InvalidOperationException(receipt.handler + " did not pass; remaining handlers were not run. " + result);
                    yield return null;
                }
            }
            finally
            {
                if (tutorial) tutorial.enabled = wasEnabled;
                if (tutorial && Hash(tutorial.StatePath) != sidecarHash) throw new InvalidOperationException("Post-completion fixtures changed the onboarding sidecar; result requires investigation.");
            }
        }
        static LifeSession DedicatedSession(bool completeIdle)
        {
            var life = UnityEngine.Object.FindObjectOfType<LifeSession>(); string path = Environment.GetEnvironmentVariable("WEATHERHOUSE_LIFE_REVIEW_SAVE");
            if (!SessionState.GetBool(Key, false) || !EditorApplication.isPlaying || !life || string.IsNullOrEmpty(path) ||
                Environment.GetEnvironmentVariable(LifeOnboarding.ReviewOptIn) != "1" || !Path.GetFullPath(path).StartsWith(Path.GetFullPath("LifeGameplayReview") + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                Path.GetFullPath(path) != Path.GetFullPath(GameplaySession.PersistenceDirectory) || SceneManager.GetActiveScene().path != "Assets/Scenes/WeatherHouse_WatersideV2.unity") throw new InvalidOperationException("Only the dedicated opted-in onboarding isolated Play may use this entry.");
            if (!completeIdle) return life;
            var game = life.Gameplay; var hud = game ? game.GetComponent<GameplayHud>() : null; var tutorial = life.GetComponent<LifeOnboarding>(); var weather = UnityEngine.Object.FindObjectOfType<LifeLocalWeather>();
            if (!life.World || !game || !hud || !tutorial || tutorial.State?.stage != LifeOnboardingStage.Complete || game.Judging || game.Recorder.IsRecording || hud.JournalOpen || hud.CameraMode || hud.IsDialogueOpen || hud.IsAlbumOpening || life.GestureInputActive ||
                (game.forestSprite && game.forestSprite.ScriptedInput) || (weather && weather.Active) || life.Progress.tasks.Any(t => t != null && t.status == LifeTaskStatus.Active) || UnityEngine.Object.FindObjectsOfType<LifeOpeningCinematic>().Any(m => m.IsPlaying))
                throw new InvalidOperationException("The real tutorial must be Complete with no active UI, recording, scripted player, task, weather or opening before the suite runs.");
            return life;
        }
        static IEnumerator CollectReview(IEnumerator steps, float timeout, Action<Exception> complete)
        {
            var stack = new Stack<IEnumerator>(); stack.Push(steps); Exception failure = null; float start = Time.realtimeSinceStartup;
            while (stack.Count > 0)
            {
                object value = null; bool more = false;
                try { if (Time.realtimeSinceStartup - start > timeout) throw new TimeoutException("Supplemental review timed out."); more = stack.Peek().MoveNext(); if (more) value = stack.Peek().Current; }
                catch (Exception error) { failure = error; break; }
                if (!more)
                { try { (stack.Pop() as IDisposable)?.Dispose(); } catch (Exception error) { failure = error; break; } continue; }
                if (value is IEnumerator nested) stack.Push(nested); else yield return value;
            }
            while (stack.Count > 0) try { (stack.Pop() as IDisposable)?.Dispose(); } catch (Exception error) { failure = failure ?? error; }
            complete(failure);
        }
        static void WriteReview(string path, object report)
        { Directory.CreateDirectory(Folder); File.WriteAllText(path, JsonUtility.ToJson(report, true)); }
    }
}
