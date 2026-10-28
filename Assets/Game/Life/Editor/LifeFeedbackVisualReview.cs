using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using WeatherHousePrototype;

namespace WeatherHouse.Life.Editor
{
    // Review-only rendering of the running scene and its actual canvases. No scene or player save writes.
    public static class LifeFeedbackVisualReview
    {
        const string Folder = "LifeGameplayReview/20261028/in-game-ui";
        [MenuItem("Weather House/Review 20261027/Capture feedback UI")]
        public static void Begin()
        {
            var life = UnityEngine.Object.FindObjectOfType<LifeSession>();
            if (!Application.isPlaying || !life || !Path.GetFullPath(GameplaySession.PersistenceDirectory).StartsWith(
                Path.GetFullPath("LifeGameplayReview") + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Capture requires the isolated feedback Play session.");
            life.StartCoroutine(Capture(life));
        }
        static IEnumerator Capture(LifeSession life)
        {
            var hud = life.GetComponent<GameplayHud>();
            if (hud.CameraMode || hud.JournalOpen || hud.IsAlbumOpening) throw new InvalidOperationException("Begin with idle gameplay.");
            Directory.CreateDirectory(Folder);
            Frame(life, "01_常驻图标.png");
            hud.ToggleJournal();
            float deadline = Time.realtimeSinceStartup + 30;
            while (hud.IsAlbumOpening && Time.realtimeSinceStartup < deadline) yield return null;
            if (hud.IsAlbumOpening) throw new TimeoutException("Book did not open.");
            hud.OnboardingSelectBook(0, null);
            for (int i = 0; i < 4; i++) yield return null;
            Frame(life, "02_字典与书签.png");
            var checks = new List<string>();
            foreach (var pair in new[] { ("Photos tab", 1), ("Photography tasks tab", 3), ("Gesture learning tab", 2) })
            {
                var book = (RectTransform)typeof(GameplayHud).GetField("albumRoot", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(hud);
                var tab = book.Find(pair.Item1).GetComponent<Button>();
                if (!tab.interactable || !tab.targetGraphic) throw new InvalidOperationException("Icon tab is unavailable: " + pair.Item1);
                tab.onClick.Invoke();
                deadline = Time.realtimeSinceStartup + 10;
                while (life.GetComponent<LifeJournal3D>().IsAnimating && Time.realtimeSinceStartup < deadline) yield return null;
                var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                bool photos = (bool)typeof(GameplayHud).GetField("albumPhotosSelected", flags).GetValue(hud);
                bool gestures = (bool)typeof(GameplayHud).GetField("albumGesturesSelected", flags).GetValue(hud);
                bool tasks = (bool)typeof(GameplayHud).GetField("albumTasksSelected", flags).GetValue(hud);
                int selected = tasks ? 3 : gestures ? 2 : photos ? 1 : 0;
                if (selected != pair.Item2) throw new InvalidOperationException("Icon tab opens the wrong page: " + pair.Item1);
                checks.Add("PASS Icon tab retains its page action: " + pair.Item1);
            }
            hud.OnboardingSelectGesture("rain");
            for (int i = 0; i < 5; i++) yield return null;
            Frame(life, "03_魔法书与书签.png");
            File.WriteAllLines(Folder + "/icon-navigation-checks.txt", checks);
            File.WriteAllText(Folder + "/source.txt", "Actual isolated Unity Play scene, production UI and textures, rendered by the current game camera at 1920x1080.\nOverlay canvases are temporarily routed through that camera for batch capture and immediately restored. No generated composition, scene save, player save or layout fixture.\n" + DateTime.UtcNow.ToString("O"));
            hud.ToggleJournal();
        }
        struct CanvasState { public Canvas canvas; public RenderMode mode; public Camera camera; public float distance; }
        static void Frame(LifeSession life, string name)
        {
            Camera camera = life.Gameplay.CurrentView;
            if (!camera) throw new InvalidOperationException("Game camera is missing.");
            var target = new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32);
            var pixels = new Texture2D(1920, 1080, TextureFormat.RGBA32, false);
            RenderTexture prior = camera.targetTexture, active = RenderTexture.active;
            var canvases = UnityEngine.Object.FindObjectsOfType<Canvas>().Where(c => c.isRootCanvas && c.renderMode == RenderMode.ScreenSpaceOverlay)
                .Select(c => new CanvasState { canvas = c, mode = c.renderMode, camera = c.worldCamera, distance = c.planeDistance }).ToArray();
            try
            {
                target.Create(); camera.targetTexture = target;
                foreach (var saved in canvases)
                { saved.canvas.renderMode = RenderMode.ScreenSpaceCamera; saved.canvas.worldCamera = camera; saved.canvas.planeDistance = camera.nearClipPlane + .1f; }
                Canvas.ForceUpdateCanvases(); camera.Render(); RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0, 0, 1920, 1080), 0, 0); pixels.Apply();
                File.WriteAllBytes(Path.Combine(Folder, name), pixels.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture = prior; RenderTexture.active = active;
                foreach (var saved in canvases)
                { saved.canvas.renderMode = saved.mode; saved.canvas.worldCamera = saved.camera; saved.canvas.planeDistance = saved.distance; }
                Canvas.ForceUpdateCanvases(); target.Release(); UnityEngine.Object.Destroy(target); UnityEngine.Object.Destroy(pixels);
            }
        }
    }
}
