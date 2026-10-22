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
using Object = UnityEngine.Object;

namespace WeatherHousePrototype
{
    public static class GameplayUiInspectorChecks
    {
        private const string ReportPath = "PrototypeReview/ui-inspector.txt";
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private static bool running;

        [MenuItem("Weather House/Check Gameplay UI Inspector In Play")]
        public static void Run()
        {
            if (running) throw new InvalidOperationException("UI Inspector checks are already running.");
            var report = new List<string> { "RUNNING: Inspector edits -> next Play frame -> rendered Canvas", DateTime.Now.ToString("O") };
            Directory.CreateDirectory("PrototypeReview");
            File.WriteAllLines(ReportPath, report);
            try
            {
                Require(EditorApplication.isPlaying, "Enter Play mode before running UI Inspector checks.");
                var session = Object.FindObjectOfType<GameplaySession>();
                Require(session != null && session.isActiveAndEnabled, "An active GameplaySession is required.");
                var hud = session.GetComponent<GameplayHud>();
                Require(hud != null && hud.isActiveAndEnabled && Get<Canvas>(hud, "canvas") != null, "Wait for HUD Start to finish.");
                Require(!session.Judging && !session.Recorder.IsRecording, "Wait for the current judgement or capture to finish.");
                Require(!hud.JournalOpen && !hud.CameraMode && !Get<bool>(hud, "dialogueOpen") &&
                    Get<ObservationRecord>(hud, "expandedPhoto") == null, "Close the journal, reply panel and camera before running checks.");
                Require(EventSystem.current == null || EventSystem.current.currentSelectedGameObject == null,
                    "Deselect the current UI control before running checks.");
                ProceduralUiStyle.Parse(JsonUtility.ToJson(hud.uiStyle));
                running = true;
                session.StartCoroutine(Guarded(CheckHud(session, hud, report), report));
            }
            catch (Exception exception)
            {
                running = false;
                report.Add("FAIL: " + exception);
                File.WriteAllLines(ReportPath, report);
                throw;
            }
        }

        private static IEnumerator Guarded(IEnumerator checks, List<string> report)
        {
            bool background = Application.runInBackground;
            Application.runInBackground = true;
            float started = Time.realtimeSinceStartup;
            bool complete = false;
            Exception failure = null;
            try
            {
                while (!complete && failure == null)
                {
                    object yielded = null;
                    try
                    {
                        if (Time.realtimeSinceStartup - started > 15f) throw new TimeoutException("UI Inspector checks exceeded 15 seconds.");
                        complete = !checks.MoveNext();
                        if (!complete) yielded = checks.Current;
                    }
                    catch (Exception exception) { failure = exception; }
                    if (!complete && failure == null) yield return yielded;
                }
            }
            finally
            {
                try { (checks as IDisposable)?.Dispose(); }
                catch (Exception exception) { if (failure == null) failure = exception; }
                if (!complete && failure == null) failure = new OperationCanceledException("UI checks stopped before completion.");
                Application.runInBackground = background;
                running = false;
                report.Add(failure == null ? "Result: PASS (actual Play Canvas meshes; subjective appearance is separate)" : "Result: FAIL\n" + failure);
                File.WriteAllLines(ReportPath, report);
                if (failure == null) Debug.Log("UI Inspector Play checks passed: " + ReportPath);
                else Debug.LogError("UI Inspector Play checks failed: " + ReportPath + "\n" + failure);
            }
        }

        private static IEnumerator CheckHud(GameplaySession session, GameplayHud hud, List<string> report)
        {
            AnimalActor animal = session.animals.FirstOrDefault(a => a != null && a.CurrentGlyphIds?.Length > 0 &&
                session.CurrentView.WorldToScreenPoint(a.BubbleAnchor).z > 0f);
            Require(animal != null, "At least one animal must be in front of the current view for a live bubble mesh check.");
            string serializedHud = EditorJsonUtility.ToJson(hud);
            string replyDraft = Get<string>(hud, "replyDraft"), guessDraft = Get<string>(hud, "guessDraft");
            bool dirty = Get<bool>(hud, "dirty"), walkerEnabled = session.walker.enabled;
            bool inputBlocked = session.walker.InputBlocked, jumpBlocked = session.walker.JumpBlocked;
            bool cameraBlocked = session.walker.CameraInputBlocked;
            AnimalActor observed = session.Observed;
            float nextObservation = Get<float>(session, "nextObservation");
            string notebook = JsonUtility.ToJson(session.Notebook);
            byte[] save = File.Exists(session.SavePath) ? File.ReadAllBytes(session.SavePath) : null;
            int[] canvases = Ids<Canvas>(), events = Ids<EventSystem>();
            try
            {
                Set(session, "nextObservation", float.PositiveInfinity);
                Set(session, "<Observed>k__BackingField", animal);
                session.walker.enabled = false;
                Set(hud, "dialogueOpen", true);
                Set(hud, "replyDraft", "调参时保留这段未发送的中文草稿。");
                Apply(hud, serialized =>
                {
                    serialized.FindProperty("animalBubbleSize").vector2Value = new Vector2(240f, 90f);
                    serialized.FindProperty("playerBubbleSize").vector2Value = new Vector2(500f, 140f);
                    serialized.FindProperty("animalGlyphHeight").floatValue = 25f;
                    serialized.FindProperty("animalGlyphStrokeWidth").floatValue = 3f;
                    serialized.FindProperty("uiStyle.lineScale").floatValue = 1f;
                    serialized.FindProperty("uiStyle.fontScale").floatValue = 1f;
                    serialized.FindProperty("uiStyle.fontWeight").intValue = 400;
                });
                yield return null;
                Canvas.ForceUpdateCanvases();
                Vector2 animalBefore = Find<ProceduralUiGraphic>(hud, "Animal bubble").rectTransform.sizeDelta;
                Vector2 playerBefore = Find<ProceduralUiGraphic>(hud, "Player reply bubble").rectTransform.sizeDelta;
                var input = Find<InputField>(hud, "Chinese reply");
                input.text = "我还没发出去，调整字形和气泡后也要保留。";
                string draft = input.text;

                Apply(hud, serialized => serialized.FindProperty("animalBubbleSize").vector2Value = new Vector2(360f, 130f));
                yield return null;
                Canvas.ForceUpdateCanvases();
                Vector2 animalAfter = Find<ProceduralUiGraphic>(hud, "Animal bubble").rectTransform.sizeDelta;
                Check((animalAfter - animalBefore).sqrMagnitude > 1f &&
                    Near(Find<ProceduralUiGraphic>(hud, "Player reply bubble").rectTransform.sizeDelta, playerBefore),
                    "Changing animal size updates its live RectTransform and leaves player size unchanged", report);

                Apply(hud, serialized => serialized.FindProperty("playerBubbleSize").vector2Value = new Vector2(650f, 180f));
                yield return null;
                Canvas.ForceUpdateCanvases();
                Check((Find<ProceduralUiGraphic>(hud, "Player reply bubble").rectTransform.sizeDelta - playerBefore).sqrMagnitude > 1f &&
                    Near(Find<ProceduralUiGraphic>(hud, "Animal bubble").rectTransform.sizeDelta, animalAfter),
                    "Changing player size updates its live RectTransform and leaves animal size unchanged", report);

                var glyphBefore = hud.GetComponentsInChildren<AnimalGlyphGraphic>().First();
                float oldStroke = SolidStroke(glyphBefore);
                int oldGlyphId = glyphBefore.GetInstanceID();
                Apply(hud, serialized => serialized.FindProperty("animalGlyphStrokeWidth").floatValue = 9f);
                yield return null;
                Canvas.ForceUpdateCanvases();
                var glyphAfter = hud.GetComponentsInChildren<AnimalGlyphGraphic>().First();
                Check(glyphAfter.GetInstanceID() != oldGlyphId && Mathf.Abs(glyphAfter.lineWidth - 9f) < .001f &&
                    Mathf.Abs(SolidStroke(glyphAfter) / oldStroke - 3f) < .02f,
                    "OnValidate rebuilds next frame; rendered glyph stroke grows from 3 to 9", report);

                hud.SetCameraMode(true);
                yield return null;
                Canvas.ForceUpdateCanvases();
                var closeBefore = Find<ProceduralUiGraphic>(hud, "Close");
                float closeStroke = SolidStroke(closeBefore), closeWidth = closeBefore.lineWidth;
                int oldFontSize = Find<Text>(hud, "Input text").fontSize;
                Apply(hud, serialized =>
                {
                    serialized.FindProperty("uiStyle.lineScale").floatValue = 1.8f;
                    serialized.FindProperty("uiStyle.fontScale").floatValue = 1.25f;
                    serialized.FindProperty("uiStyle.fontWeight").intValue = 700;
                });
                yield return null;
                Canvas.ForceUpdateCanvases();
                var closeAfter = Find<ProceduralUiGraphic>(hud, "Close");
                Check(Mathf.Abs(closeAfter.lineWidth / closeWidth - 1.8f) < .001f &&
                    Mathf.Abs(SolidStroke(closeAfter) / closeStroke - 1.8f) < .02f,
                    "Overall line scale reaches the actual close-button Canvas mesh", report);
                Text instruction = Find<Text>(hud, "Input text");
                Check(instruction.fontStyle == FontStyle.Bold && instruction.fontSize == Mathf.RoundToInt(oldFontSize * 1.25f) &&
                    HasRenderedMesh(instruction),
                    "Font weight 700 and font scale 1.25 reach rendered Chinese text", report);
                Check(Find<InputField>(hud, "Chinese reply").text == draft && Get<string>(hud, "replyDraft") == draft &&
                    Get<string>(hud, "guessDraft") == guessDraft,
                    "Repeated Inspector rebuilds preserve the unsent reply and existing guess draft", report);
                int validCloseId = closeAfter.GetInstanceID();
                Apply(hud, serialized => serialized.FindProperty("uiStyle.ink").stringValue = "#");
                yield return null;
                Canvas.ForceUpdateCanvases();
                Check(Find<ProceduralUiGraphic>(hud, "Close").GetInstanceID() == validCloseId &&
                    Find<InputField>(hud, "Chinese reply").text == draft && HasRenderedMesh(Find<Text>(hud, "Input text")),
                    "An unfinished HEX edit preserves the last valid rendered HUD and draft", report);
                Check(canvases.SequenceEqual(Ids<Canvas>()) && events.SequenceEqual(Ids<EventSystem>()),
                    "Inspector edits reuse existing Canvas and EventSystem instances", report);
                Check(JsonUtility.ToJson(session.Notebook) == notebook &&
                    (save == null ? !File.Exists(session.SavePath) : File.Exists(session.SavePath) && save.SequenceEqual(File.ReadAllBytes(session.SavePath))),
                    "Inspector checks do not change the notebook or its saved file", report);
            }
            finally
            {
                try
                {
                    Set(session, "<Observed>k__BackingField", observed);
                    if (hud != null)
                    {
                        hud.SetCameraMode(false);
                        var input = Get<InputField>(hud, "replyInput");
                        if (input != null) input.SetTextWithoutNotify(replyDraft ?? "");
                        if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
                        Set(hud, "dialogueOpen", false);
                        Set(hud, "replyDraft", replyDraft);
                        Set(hud, "guessDraft", guessDraft);
                        EditorJsonUtility.FromJsonOverwrite(serializedHud, hud);
                        // Cleanup is synchronous even when an assertion stops the iterator.
                        typeof(GameplayHud).GetMethod("RebuildInspectorPreview", Private).Invoke(hud, null);
                        Set(hud, "dirty", dirty);
                    }
                }
                finally
                {
                    Set(session, "nextObservation", nextObservation);
                    session.walker.enabled = walkerEnabled;
                    session.walker.InputBlocked = inputBlocked;
                    session.walker.JumpBlocked = jumpBlocked;
                    session.walker.CameraInputBlocked = cameraBlocked;
                }
            }
            Check(EditorJsonUtility.ToJson(hud) == serializedHud && !hud.JournalOpen && !hud.CameraMode && !Get<bool>(hud, "dialogueOpen") &&
                Get<string>(hud, "replyDraft") == replyDraft && Get<string>(hud, "guessDraft") == guessDraft && session.Observed == observed,
                "Original Inspector values, idle HUD state, drafts and observation are restored", report);
        }

        private static void Apply(GameplayHud hud, Action<SerializedObject> change)
        {
            using (var serialized = new SerializedObject(hud))
            {
                change(serialized);
                serialized.ApplyModifiedProperties();
            }
        }

        private static T Find<T>(GameplayHud hud, string name) where T : Component
        {
            T component = hud.GetComponentsInChildren<T>().FirstOrDefault(c => c.name == name);
            Require(component != null, "The active Canvas is missing " + name);
            return component;
        }

        private static float SolidStroke(Graphic graphic)
        {
            Mesh mesh = graphic.canvasRenderer.GetMesh();
            Require(mesh != null && mesh.vertexCount >= 4, "CanvasRenderer has no rendered mesh for " + graphic.name);
            Vector3[] vertices = mesh.vertices;
            float width = Vector3.Distance(vertices[1], vertices[2]);
            Require(width > .01f, "Canvas mesh has no solid stroke width for " + graphic.name);
            return width;
        }

        private static bool HasRenderedMesh(Graphic graphic)
        {
            Mesh mesh = graphic.canvasRenderer.GetMesh();
            return mesh != null && mesh.vertexCount > 0;
        }

        private static int[] Ids<T>() where T : Object { return Object.FindObjectsOfType<T>(true).Select(c => c.GetInstanceID()).OrderBy(id => id).ToArray(); }
        private static T Get<T>(object target, string name) { return (T)target.GetType().GetField(name, Private).GetValue(target); }
        private static void Set(object target, string name, object value) { target.GetType().GetField(name, Private).SetValue(target, value); }
        private static bool Near(Vector2 a, Vector2 b) { return (a - b).sqrMagnitude < .001f; }
        private static void Check(bool condition, string message, List<string> report) { Require(condition, message); report.Add("PASS: " + message); }
        private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    }
}
