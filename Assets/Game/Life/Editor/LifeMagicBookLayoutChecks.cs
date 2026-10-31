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
    public static class LifeMagicBookLayoutChecks
    {
        const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;

        [MenuItem("Weather House/Review 20261027/Check magic book layout isolated")]
        public static void Begin()
        {
            var life = UnityEngine.Object.FindObjectOfType<LifeSession>();
            string directory = Path.GetFullPath(GameplaySession.PersistenceDirectory);
            if (!Application.isPlaying || !life || string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WEATHERHOUSE_LIFE_REVIEW_SAVE")) ||
                !directory.StartsWith(Path.GetFullPath("LifeGameplayReview") + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Magic book checks require isolated Play.");
            life.StartCoroutine(Run(life, directory));
        }

        static IEnumerator Run(LifeSession life, string directory)
        {
            var checks = new List<string>();
            IEnumerator steps = Steps(life, checks);
            try
            {
                while (true)
                {
                    bool more; object next = null;
                    try { more = steps.MoveNext(); if (more) next = steps.Current; }
                    catch (Exception error) { checks.Add("FAIL " + error); break; }
                    if (!more) break;
                    yield return next;
                }
            }
            finally
            {
                (steps as IDisposable)?.Dispose();
                string result = (checks.Any(line => line.StartsWith("FAIL ", StringComparison.Ordinal)) ? "FAIL" : "PASS") +
                    " magic book layout\n" + string.Join("\n", checks);
                File.WriteAllText(Path.Combine(directory, "magic-book-layout-checks.txt"), result);
                Debug.Log(result);
            }
        }

        static IEnumerator Steps(LifeSession life, List<string> checks)
        {
            var game = life.Gameplay; var hud = game.GetComponent<GameplayHud>();
            var input = life.GetComponent<LifeGestureInput>();
            if (!hud || !input || hud.JournalOpen || hud.CameraMode || hud.IsAlbumOpening || LifeOnboarding.Blocks(game))
                throw new InvalidOperationException("Magic book checks start with an idle HUD.");
            string notebook = JsonUtility.ToJson(game.Notebook), progress = JsonUtility.ToJson(life.Progress), status = game.Status;
            string[] fields = { "albumPhotosSelected", "albumGesturesSelected", "albumTasksSelected", "selectedGesture", "selectedGlyph" };
            var state = fields.ToDictionary(name => name, name => typeof(GameplayHud).GetField(name, Hidden).GetValue(hud));
            var practiceStarted = typeof(LifeGestureInput).GetField("practiceStarted", Hidden);
            object priorPracticeStarted = practiceStarted?.GetValue(input);
            var files = new[] { game.SavePath, life.SavePath }.ToDictionary(path => path, path => File.Exists(path) ? File.ReadAllBytes(path) : null);
            try
            {
                JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(new LanguageNotebook()), game.Notebook); game.Notebook.Normalize(game.Catalog);
                life.Progress.learnedGestures.Clear();
                practiceStarted?.SetValue(input, false);
                hud.ToggleJournal();
                float deadline = Time.realtimeSinceStartup + 30;
                while (hud.IsAlbumOpening && Time.realtimeSinceStartup < deadline) yield return null;
                if (hud.IsAlbumOpening) throw new TimeoutException("Book opening did not settle.");
                hud.OnboardingSelectGesture("rain"); yield return null; yield return null;
                var book = Book(hud);
                var texts = book.GetComponentsInChildren<Text>(true).Where(text => text.gameObject.activeSelf &&
                    !string.IsNullOrEmpty(text.text) && text.text.Any(character => character >= '\u4e00' && character <= '\u9fff')).ToArray();
                Check(texts.Length == 1 && texts[0].text == "天气魔法施法手势" && texts[0].fontSize == 40 && texts[0].alignment == TextAnchor.MiddleCenter,
                    "The magic spread has one Chinese heading and no explanatory or reward text", checks);
                string[] ids = { "rain", "clear", "sunlight", "wind", "weak", "strong" };
                Check(ids.All(id => book.Find("Gesture " + id)?.GetComponentInChildren<AnimalGlyphGraphic>()?.SvgBody ==
                    Array.Find(game.Catalog.glyphs, glyph => glyph.id == id)?.body),
                    "All six clickable spell entries use the production animal glyphs", checks);
                var selected = book.Find("Selected gesture")?.GetComponent<AnimalGlyphGraphic>();
                Check(selected && selected.rectTransform.sizeDelta == new Vector2(104, 125) &&
                    book.Find("Gesture rain").GetComponentInChildren<AnimalGlyphGraphic>()?.rectTransform.sizeDelta == new Vector2(76, 91) &&
                    book.Find("Gesture rain").GetComponent<Image>().color.a == 0,
                    "The selected list glyph grows without a backing box and the right-page glyph uses the larger approved size", checks);
                Check(!life.IsGestureLearned("rain") && input.CanPractice("rain") && life.IsGestureUnlocked("rain"),
                    "An unlocked spell is available before optional practice", checks);

                Click(book.Find("Gesture wind").GetComponent<Button>()); deadline = Time.realtimeSinceStartup + 20;
                while (hud.IsAlbumOpening && Time.realtimeSinceStartup < deadline) yield return null;
                if (hud.IsAlbumOpening) throw new TimeoutException("Locked spell page did not settle.");
                book = Book(hud); selected = book.Find("Selected gesture")?.GetComponent<AnimalGlyphGraphic>();
                Check(hud.OnboardingGestureSelected == "wind" && selected && selected.color.a < .5f &&
                    !book.GetComponentInChildren<LifeGesturePad>() && !book.GetComponentInChildren<LifeGestureDemo>() && !input.CanPractice("wind"),
                    "Clicking a locked spell shows its faded glyph without a practice or demonstration surface", checks);

                Click(book.Find("Gesture rain").GetComponent<Button>()); deadline = Time.realtimeSinceStartup + 20;
                while (hud.IsAlbumOpening && Time.realtimeSinceStartup < deadline) yield return null;
                if (hud.IsAlbumOpening) throw new TimeoutException("Rain page did not settle.");
                book = Book(hud); var pad = book.GetComponentInChildren<LifeGesturePad>();
                var demo = book.GetComponentInChildren<LifeGestureDemo>();
                Check(hud.OnboardingGestureSelected == "rain" && pad && demo && demo.definition.id == "rain",
                    "Clicking an unlocked spell selects its live production demonstration and practice pad", checks);
                if (!pad || !demo) throw new InvalidOperationException("Rain practice is required for pointer checks.");
                var guide = demo.transform.Find("Complete gesture guide").GetComponent<LifeStrokeGraphic>();
                Check(demo.transform.parent == pad.transform && ((RectTransform)demo.transform).anchoredPosition == Vector2.zero &&
                    pad.ink.transform.GetSiblingIndex() > demo.transform.GetSiblingIndex() &&
                    demo.GetComponentsInChildren<Graphic>().All(graphic => !graphic.raycastTarget),
                    "The centered looping example sits inside the tracing pad below player ink and never intercepts pointer input", checks);
                Vector2 marker = demo.marker.anchoredPosition;
                for (int frame = 0; frame < 8; frame++) yield return null;
                Check(Vector2.Distance(marker, demo.marker.anchoredPosition) > .01f,
                    "The original demonstration animates its pen tip over subsequent frames", checks);
                var mouse = pad.transform.Find("Practice mouse hint");
                Check(mouse && mouse.gameObject.activeSelf, "The optional practice initially shows a mouse sketch", checks);
                var pointer = Pointer(demo.transform, demo.definition.points[0] * 65f);
                var hits = new List<RaycastResult>(); EventSystem.current.RaycastAll(pointer, hits);
                Check(hits.Count > 0 && hits[0].gameObject.GetComponentInParent<LifeGesturePad>() == pad,
                    "The visible practice area remains the actual top pointer target", checks);
                ExecuteEvents.Execute(pad.gameObject, pointer, ExecuteEvents.pointerDownHandler);
                for (int segment = 1; segment < demo.definition.points.Length; segment++)
                    for (int sample = 1; sample <= 8; sample++)
                    {
                        var point = Vector2.Lerp(demo.definition.points[segment - 1], demo.definition.points[segment], sample / 8f) * 65f;
                        pointer.position = ScreenPoint(demo.transform, point);
                        ExecuteEvents.Execute(pad.gameObject, pointer, ExecuteEvents.dragHandler);
                    }
                Canvas.ForceUpdateCanvases();
                Check(mouse && !mouse.gameObject.activeSelf && InkVertices(pad.ink) > 0,
                    "The first pointer stroke hides the mouse sketch and produces rendered ink", checks);
                ExecuteEvents.Execute(pad.gameObject, pointer, ExecuteEvents.pointerUpHandler);
                Check(life.IsGestureLearned("rain") && !pad.feedback,
                    "Tracing the displayed example through real pointer events learns the spell with no feedback text object", checks);
                int guideVertices = InkVertices(guide);
                Click(book.Find("Retry practice").GetComponent<Button>()); Canvas.ForceUpdateCanvases();
                Check(InkVertices(pad.ink) == 0 && mouse && !mouse.gameObject.activeSelf && !life.GestureInputActive,
                    "The centered eraser clears visible ink and drawing state while the introductory mouse remains hidden", checks);
                Check(guideVertices > 0 && InkVertices(guide) == guideVertices && demo.enabled && demo.gameObject.activeInHierarchy,
                    "Erasing player ink preserves the complete guide and the running demonstration", checks);
                yield return null; yield return null;
                mouse = Book(hud).GetComponentInChildren<LifeGesturePad>()?.transform.Find("Practice mouse hint");
                Check(mouse && !mouse.gameObject.activeSelf,
                    "The mouse sketch stays hidden after the successful-practice HUD rebuild", checks);
                demo = Book(hud).GetComponentInChildren<LifeGestureDemo>();
                bool movedAway = false, looped = false;
                deadline = Time.realtimeSinceStartup + 4.3f;
                while (demo && Time.realtimeSinceStartup < deadline)
                {
                    float distance = Vector2.Distance(demo.marker.anchoredPosition, demo.definition.points[0] * 65f);
                    if (distance > 20) movedAway = true;
                    if (movedAway && distance < 8) looped = true;
                    yield return null;
                }
                Check(looped, "The demonstration continues its loop after tracing and erasing", checks);
                checks.Add("BOUNDARY actual buttons and raycasts with synthesized pointer events; no OS mouse or visual-acceptance claim.");
            }
            finally
            {
                life.GetComponent<LifeJournal3D>()?.Cancel();
                typeof(GameplayHud).GetMethod("CompleteJournalClose", Hidden).Invoke(hud, null);
                input.SetPracticeDrawing(false);
                practiceStarted?.SetValue(input, priorPracticeStarted);
                JsonUtility.FromJsonOverwrite(notebook, game.Notebook); JsonUtility.FromJsonOverwrite(progress, life.Progress);
                foreach (var entry in state) typeof(GameplayHud).GetField(entry.Key, Hidden).SetValue(hud, entry.Value);
                foreach (var file in files)
                    if (file.Value != null) File.WriteAllBytes(file.Key, file.Value);
                    else if (File.Exists(file.Key)) File.Delete(file.Key);
                game.Notify(status); game.RefreshInputBlocking();
            }
        }

        static RectTransform Book(GameplayHud hud) => (RectTransform)typeof(GameplayHud).GetField("albumRoot", Hidden).GetValue(hud);
        static Vector2 ScreenPoint(Transform target, Vector2 point) => RectTransformUtility.WorldToScreenPoint(null, target.TransformPoint(point));
        static PointerEventData Pointer(Transform target, Vector2 point) => new PointerEventData(EventSystem.current)
        { pointerId = -1, button = PointerEventData.InputButton.Left, position = ScreenPoint(target, point) };
        static void Click(Button button)
        {
            Canvas.ForceUpdateCanvases();
            var pointer = Pointer(button.transform, Vector2.zero); var hits = new List<RaycastResult>();
            EventSystem.current.RaycastAll(pointer, hits);
            if (hits.Count == 0 || hits[0].gameObject.GetComponentInParent<Button>() != button)
                throw new InvalidOperationException("Button is not the top pointer target: " + button.name);
            ExecuteEvents.Execute(button.gameObject, pointer, ExecuteEvents.pointerClickHandler);
        }
        static int InkVertices(LifeStrokeGraphic ink) => ink.canvasRenderer.GetMesh()?.vertexCount ?? 0;
        static void Check(bool condition, string label, List<string> checks) => checks.Add((condition ? "PASS " : "FAIL ") + label);
    }
}
