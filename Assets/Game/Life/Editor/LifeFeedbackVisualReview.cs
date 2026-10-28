using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using WeatherHousePrototype;

namespace WeatherHouse.Life.Editor
{
    // Review-only rendering of the running scene and its actual canvases. No scene or player save writes.
    public static class LifeFeedbackVisualReview
    {
        const string Folder = "LifeGameplayReview/20261028/in-game-ui-white-icons";
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
            var checks = new List<string>();
            var controls = (RectTransform)typeof(GameplayHud).GetField("controlsRoot", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(hud);
            var labels = controls.GetComponentsInChildren<Text>();
            if (!labels.Select(label => label.text).SequenceEqual(new[] { "C", "M", "J", "L" }))
                throw new InvalidOperationException("HUD shortcuts must contain only the four key letters.");
            var icons = controls.GetComponentsInChildren<RawImage>();
            if (icons.Length != 4 || icons.Any(icon => !icon.texture) || labels.Where((label, i) =>
                label.rectTransform.anchoredPosition.x != icons[i].rectTransform.anchoredPosition.x ||
                label.rectTransform.anchoredPosition.y + label.rectTransform.rect.height * .5f >=
                icons[i].rectTransform.anchoredPosition.y - icons[i].rectTransform.rect.height * .5f).Any())
                throw new InvalidOperationException("Each HUD key must be centered below its loaded icon with a gap.");
            checks.Add("PASS Four HUD icons show only C/M/J/L, centered below their icons");
            Frame(life, "01_常驻图标.png");
            float deadline;
            foreach (var shortcut in new[] { (KeyCode.M, 2), (KeyCode.L, 3), (KeyCode.J, 0) })
            {
                hud.UseBookShortcut(shortcut.Item1);
                deadline = Time.realtimeSinceStartup + 30;
                while (hud.IsAlbumOpening && Time.realtimeSinceStartup < deadline) yield return null;
                if (!hud.JournalOpen || hud.IsAlbumOpening || SelectedPage(hud) != shortcut.Item2)
                    throw new InvalidOperationException("Book shortcut opens the wrong page: " + shortcut.Item1);
                checks.Add("PASS The production " + shortcut.Item1 + " shortcut opens its book directly");
            }
            for (int i = 0; i < 4; i++) yield return null;
            Frame(life, "02_字典与书签.png");
            foreach (var pair in new[] { ("Symbols tab", 0), ("Photos tab", 1), ("Photography tasks tab", 3), ("Gesture learning tab", 2) })
            {
                Button tab = null; PointerEventData pointer = null;
                for (int attempt = 0; attempt < 3; attempt++)
                {
                    var book = (RectTransform)typeof(GameplayHud).GetField("albumRoot", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(hud);
                    tab = book.Find(pair.Item1).GetComponent<Button>();
                    if (!tab.interactable || !tab.targetGraphic) throw new InvalidOperationException("Icon tab is unavailable: " + pair.Item1);
                    Canvas.ForceUpdateCanvases();
                    var canvas = tab.GetComponentInParent<Canvas>();
                    pointer = new PointerEventData(EventSystem.current) { position = RectTransformUtility.WorldToScreenPoint(
                        canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera, tab.transform.position) };
                    var hits = new List<RaycastResult>(); EventSystem.current.RaycastAll(pointer, hits);
                    if (hits.Count == 0 || ExecuteEvents.GetEventHandler<IPointerClickHandler>(hits[0].gameObject) != tab.gameObject)
                        throw new InvalidOperationException("Icon tab cannot receive the real UI raycast: " + pair.Item1);
                    tab.OnPointerEnter(pointer); yield return new WaitForSecondsRealtime(.2f);
                    // Live animal observations can rebuild the page while the hover animation runs.
                    if (!tab) continue;
                    if (!(tab.targetGraphic is LifeJournalTabGraphic) || tab.GetComponentsInChildren<RawImage>().Any(icon => icon.enabled) ||
                        tab.targetGraphic.canvasRenderer.GetColor() != tab.colors.highlightedColor * tab.colors.colorMultiplier)
                        throw new InvalidOperationException("Tab retains a book image or loses symbol/paper hover tint: " + pair.Item1);
                    break;
                }
                if (!tab) throw new InvalidOperationException("The page did not settle for hover verification: " + pair.Item1);
                tab.OnPointerExit(pointer);
                checks.Add("PASS Icon keeps pointer routing and hover tint: " + pair.Item1);
                ExecuteEvents.Execute(tab.gameObject, pointer, ExecuteEvents.pointerClickHandler);
                deadline = Time.realtimeSinceStartup + 10;
                while (life.GetComponent<LifeJournal3D>().IsAnimating && Time.realtimeSinceStartup < deadline) yield return null;
                if (SelectedPage(hud) != pair.Item2) throw new InvalidOperationException("Icon tab opens the wrong page: " + pair.Item1);
                checks.Add("PASS Icon tab retains its page action: " + pair.Item1);
                if (pair.Item2 == 1) Frame(life, "05_照片页与书签.png");
                if (pair.Item2 == 3) Frame(life, "04_摄影任务仅字形.png");
            }
            hud.OnboardingSelectGesture("rain");
            for (int i = 0; i < 5; i++) yield return null;
            Frame(life, "03_魔法书与书签.png");
            hud.UseBookShortcut(KeyCode.M);
            deadline = Time.realtimeSinceStartup + 20;
            while (hud.JournalOpen && Time.realtimeSinceStartup < deadline) yield return null;
            if (hud.JournalOpen) throw new InvalidOperationException("Repeating the current book shortcut did not close the book.");
            checks.Add("PASS Repeating the current book shortcut closes it");
            File.WriteAllLines(Folder + "/icon-navigation-checks.txt", checks);
            File.WriteAllText(Folder + "/source.txt", "Actual isolated Unity Play scene, production UI and textures, rendered by the current game camera at 1920x1080.\nOverlay canvases are temporarily routed through that camera for batch capture and immediately restored. No generated composition, scene save, player save or layout fixture.\n" + DateTime.UtcNow.ToString("O"));
        }
        static int SelectedPage(GameplayHud hud)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            bool photos = (bool)typeof(GameplayHud).GetField("albumPhotosSelected", flags).GetValue(hud);
            bool gestures = (bool)typeof(GameplayHud).GetField("albumGesturesSelected", flags).GetValue(hud);
            bool tasks = (bool)typeof(GameplayHud).GetField("albumTasksSelected", flags).GetValue(hud);
            return tasks ? 3 : gestures ? 2 : photos ? 1 : 0;
        }
        struct CanvasState { public Canvas canvas; public RenderMode mode; public Camera camera; public float distance; }
        internal static void Frame(LifeSession life, string name, string folder = Folder)
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
                File.WriteAllBytes(Path.Combine(folder, name), pixels.EncodeToPNG());
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
