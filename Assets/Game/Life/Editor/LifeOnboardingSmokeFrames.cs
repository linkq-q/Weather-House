using System;
using System.Collections;
using System.IO;
using System.Security.Cryptography;
using UnityEngine;
using UnityEngine.SceneManagement;
using WeatherHousePrototype;

namespace WeatherHouse.Life.Editor
{
    public static class LifeOnboardingSmokeFrames
    {
        [Serializable] sealed class Frame
        {
            public string status, label, file, sha256, utc, stage, hint, reason;
            public int width, height, unityFrame;
            public string boundary = "Actual GameView at the existing isolated smoke step. The smoke's declared pointer/player fixtures remain; no mock image, tutorial state assignment or hidden-failure recapture.";
        }
        public static IEnumerator Capture(LifeSession life, string label)
        {
            string save = Environment.GetEnvironmentVariable("WEATHERHOUSE_LIFE_REVIEW_SAVE");
            if (!Application.isPlaying || !life || string.IsNullOrEmpty(save) || Environment.GetEnvironmentVariable(LifeOnboarding.ReviewOptIn) != "1" ||
                !Path.GetFullPath(save).StartsWith(Path.GetFullPath("LifeGameplayReview") + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                Path.GetFullPath(save) != Path.GetFullPath(GameplaySession.PersistenceDirectory) || SceneManager.GetActiveScene().path != "Assets/Scenes/WeatherHouse_WatersideV2.unity")
                throw new InvalidOperationException("Book screenshots require the dedicated existing isolated tutorial Play.");
            string folder = Path.Combine("LifeGameplayReview/20261027/onboarding/novice-frames", new DirectoryInfo(save).Name); Directory.CreateDirectory(folder);
            var tutorial = life.GetComponent<LifeOnboarding>(); var hud = life.GetComponent<GameplayHud>();
            var report = new Frame { label = label, utc = DateTime.UtcNow.ToString("O"), stage = tutorial?.State?.stage.ToString(), hint = tutorial?.CurrentHint };
            yield return new WaitForEndOfFrame();
            Texture2D pixels = null;
            try
            {
                if (!hud || !hud.JournalOpen || hud.IsAlbumOpening || !tutorial || !tutorial.TutorialActive)
                { report.status = "MISSING_NOT_VISIBLE"; report.reason = "The expected live book stage was not visible; no replacement was made."; }
                else
                {
                    pixels = ScreenCapture.CaptureScreenshotAsTexture(); if (!pixels) throw new InvalidOperationException("No rendered GameView pixels.");
                    report.unityFrame = Time.frameCount; report.width = pixels.width; report.height = pixels.height;
                    report.file = Path.GetFullPath(Path.Combine(folder, label + "-" + Time.frameCount + ".png")); File.WriteAllBytes(report.file, pixels.EncodeToPNG());
                    using (var sha = SHA256.Create()) report.sha256 = BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(report.file))).Replace("-", "");
                    report.status = "CAPTURED_ACTUAL_GAMEVIEW_VISUAL_REVIEW_PENDING";
                }
            }
            catch (Exception error) { report.status = "MISSING_CAPTURE_FAILED"; report.reason = error.ToString(); }
            finally { if (pixels) UnityEngine.Object.Destroy(pixels); }
            File.WriteAllText(Path.Combine(folder, label + ".json"), JsonUtility.ToJson(report, true));
        }
    }
}
