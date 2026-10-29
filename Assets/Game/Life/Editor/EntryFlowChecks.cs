using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;
using WeatherHouse.DrawerPrototype;
using WeatherHousePrototype;
using Object = UnityEngine.Object;

namespace WeatherHouse.Life.Editor
{
    public static class EntryFlowChecks
    {
        static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
        public static IEnumerator Run()
        {
            var c = Object.FindObjectOfType<DrawerPrototypeController>(); var music = Object.FindObjectOfType<GameMusic>();
            Check(c && music && !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(DrawerPrototypeController.SaveOverride)), "Fresh isolated drawer and music are required.");
            var log = new StringBuilder(); c.Automation = true;
            Check(!c.Progress.Complete && !c.TryLeave(), "Cannot leave before collecting and reading the opening items.");
            foreach (var paper in c.Papers) paper.gameObject.SetActive(false);
            for (int i = 0; i < c.Items.Length; i++)
            {
                Check(c.CanPickup(c.Items[i]), "Uncovered opening item should be collectible.");
                Vector2 click = c.ViewCamera.WorldToScreenPoint(c.VisiblePoint(c.Items[i]));
                c.Pointer(click, true, true, false); yield return null; c.Pointer(click, false, false, true);
                float due = Time.realtimeSinceStartup + 10; while (c.Busy && Time.realtimeSinceStartup < due) yield return null;
                Check(c.SelectedItem == i && !c.TryLeave(), "A held unread item cannot skip directly to the world.");
                click = RectTransformUtility.WorldToScreenPoint(null, c.CloseTarget.position);
                c.Pointer(click, true, true, false); yield return null; c.Pointer(click, false, false, true);
                due = Time.realtimeSinceStartup + 10; while (c.Busy && Time.realtimeSinceStartup < due) yield return null;
                log.AppendLine("PASS actual pickup, introduction and close: " + i);
            }
            Check(c.Progress.Complete, "All opening items must be collected and read.");
            Check(DrawerProgress.Restore(File.ReadAllText(c.SavePath)).Complete, "Opening progress must persist before departure.");
            var source = music.GetComponent<AudioSource>(); int id = music.GetInstanceID(); float time = source.time;
            Check(source.isPlaying && source.loop && source.spatialBlend == 0 && source.clip.name == "Afternoon Shade" && source.clip.loadType == AudioClipLoadType.Streaming, "BGM uses the supplied stereo streaming loop.");
            yield return new WaitForEndOfFrame(); ScreenCapture.CaptureScreenshot(Path.GetFullPath("PhotoStillReview/20261029/drawer-ready.png")); yield return null;
            var panel = (RectTransform)typeof(DrawerPrototypeController).GetField("completePanel", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(c);
            c.Pointer(RectTransformUtility.WorldToScreenPoint(null, panel.position), true, true, false);
            Check(c.Busy && !c.TryLeave(), "Actual departure click starts once and rejects repeated clicks.");
            float deadline = Time.realtimeSinceStartup + 60;
            while (SceneManager.GetActiveScene().path != DrawerPrototypeController.WorldScene && Time.realtimeSinceStartup < deadline) yield return null;
            yield return new WaitForSecondsRealtime(3);
            Check(SceneManager.GetActiveScene().path == DrawerPrototypeController.WorldScene && Object.FindObjectOfType<GameplaySession>() && Object.FindObjectOfType<LifeSession>(), "Drawer must enter the real bootstrapped Waterside world.");
            Check(!Object.FindObjectOfType<DrawerPrototypeController>() && Object.FindObjectsOfType<GameMusic>().Length == 1 && music.GetInstanceID() == id && source.isPlaying && source.time > time, "Music must retain the same source and continue across the single-scene transition.");
            Check(Resources.FindObjectsOfTypeAll<AudioListener>().Count(a => a.gameObject.scene.IsValid() && a.isActiveAndEnabled) == 1, "Scene transition must leave one audio listener.");
            log.AppendLine("PASS five saved opening items -> one departure -> real Waterside scene and LifeSession.");
            log.AppendLine($"PASS same BGM source {id}, time {time:F2}s -> {source.time:F2}s, volume={source.volume:F2}, streaming stereo={source.clip.channels}, one listener.");
            yield return new WaitForEndOfFrame(); ScreenCapture.CaptureScreenshot(Path.GetFullPath("PhotoStillReview/20261029/entry-world.png")); yield return null;
            File.WriteAllText("PhotoStillReview/20261029/entry-flow-checks.txt", log.ToString());
        }
    }
}
