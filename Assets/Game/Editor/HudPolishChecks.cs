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

namespace WeatherHousePrototype.Editor
{
    public static class HudPolishChecks
    {
        const string Report = "PrototypeReview/HudPolish/checks.txt";
        const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        [MenuItem("Weather House/Check HUD Polish In Play")]
        public static void Run()
        {
            if (!EditorApplication.isPlaying) throw new InvalidOperationException("Enter Play first.");
            Object.FindObjectOfType<GameplaySession>().StartCoroutine(Review());
        }

        static IEnumerator Review()
        {
            var session = Object.FindObjectOfType<GameplaySession>();
            var hud = session.GetComponent<GameplayHud>();
            var report = new List<string> { DateTime.UtcNow.ToString("O"), "Actual Play HUD; simulated state, no AI calls." };
            string[] hudNames = { "<JournalOpen>k__BackingField", "glyphDetailOpen", "dirty", "expandedPhoto", "albumPhotosSelected", "page", "selectedGlyph", "lastCompositionFrame", "displayedStatus", "statusUntil" };
            var state = hudNames.ToDictionary(n => n, n => Get(hud, n));
            var notebook = session.Notebook;
            string json = JsonUtility.ToJson(notebook), status = session.Status;
            byte[] save = File.Exists(session.SavePath) ? File.ReadAllBytes(session.SavePath) : null;
            bool enabled = session.enabled, walker = session.walker.enabled;
            var observed = session.Observed;
            bool judging = session.Judging, background = Application.runInBackground;
            try
            {
                Application.runInBackground = true;
                session.enabled = false; session.walker.enabled = false;
                Set(hud, "<JournalOpen>k__BackingField", false); Set(hud, "expandedPhoto", null);
                var label = (Text)Get(hud, "statusLabel");
                Check(label.color.r > .95f && label.color.g > .95f && label.color.b > .95f &&
                    label.transform.parent.GetComponentsInChildren<Graphic>(true).All(g => g is Text),
                    "Status is white text with no background or ornament", report);
                Check(Mathf.Abs(label.rectTransform.parent.GetComponent<RectTransform>().anchorMin.y - .2f) < .001f && label.fontSize == 33,
                    "Status is centered at 20 percent screen height with 1.3x text", report);
                Check(Get(hud, "statusGlow") is BreathingTextGlow, "Glow is attached to the text itself", report);
                var statusRect = (RectTransform)label.transform.parent;
                Vector2 statusPosition = statusRect.anchoredPosition;
                Set(session, "<Judging>k__BackingField", true);
                Call(hud, "UpdateStatus");
                Check(!label.text.Contains("动物在听你说"), "Legacy judgement state cannot display removed reply feedback", report);
                Set(session, "<Judging>k__BackingField", false);
                Set(session, "<Status>k__BackingField", "AI请求失败或超时，本次未确认任何词义，可以重试。");
                Call(hud, "UpdateStatus");
                Check(label.text.Contains("重试"), "Failure is shown by the shared bottom status", report);
                var glow = (BreathingTextGlow)Get(hud, "statusGlow");
                // Two deterministic unscaled timestamps exercise the pulse, even in an unfocused editor.
                typeof(GameplayHud).GetMethod("AnimateStatusGlow", Flags).Invoke(hud, new object[] { .25f });
                float low = glow.Intensity;
                typeof(GameplayHud).GetMethod("AnimateStatusGlow", Flags).Invoke(hud, new object[] { 1.25f });
                Check(Mathf.Abs(low - glow.Intensity) > .01f, "Text glow breathes over unscaled time", report);
                Check(statusRect.anchoredPosition == statusPosition, "Judging and failure stay at the same height", report);
                Set(session, "<Observed>k__BackingField", null);
                hud.ListenToAnimal(); Call(hud, "RebuildModal");
                Check(!hud.IsDialogueOpen && hud.DialogueTarget == null && !hud.BlocksMovement,
                    "The removed reply entry cannot open a modal or lock movement", report);
                Check(!hud.GetComponentsInChildren<Transform>(true).Any(t => new[] {
                    "Player reply bubble", "Chinese reply", "Player spoken reply", "Send reply" }.Contains(t.name)),
                    "HUD has no player reply input, echo or send control", report);
                Set(session, "<Observed>k__BackingField", observed);
                Call(hud, "UpdateStatus");
                Check(statusRect.anchoredPosition == statusPosition, "Listening keeps the shared status height fixed", report);
                var empty = Resources.Load<Texture2D>("AlbumBook/PhotoEmpty");
                var missing = Resources.Load<Texture2D>("AlbumBook/PhotoMissing");
                Check(empty != null && missing != null && empty != missing, "Distinct generated empty and missing images load", report);
                var controls = hud.GetComponentsInChildren<RectTransform>().First(t => t.name == "Controls");
                Check(controls.GetComponent<Graphic>() == null && controls.GetComponentsInChildren<RawImage>().Count(i => i.texture != null) == 3,
                    "Footer uses three generated filled icon textures and no background bar", report);
                Check(!controls.GetComponentsInChildren<Text>().Any(t => t.text.Contains("交谈") || t.text.Contains("右键") || t.text.Contains("雨")),
                    "Footer removes interaction, view and weather shortcuts", report);
                var book = new GameObject("Temporary photo check", typeof(RectTransform));
                try
                {
                    var slot = AlbumBookView.Photo(book.transform, 0, null, "empty");
                    Check(slot != null && slot.texture == empty, "Empty illustration fills the existing photo opening", report);
                    var error = AlbumBookView.Photo(book.transform, 1, null, "missing", () => { });
                    Check(error.texture == missing && error.GetComponent<Button>() != null, "Missing photo stays clickable for details", report);
                }
                finally { Object.Destroy(book); }
                Set(hud, "<JournalOpen>k__BackingField", true); Set(hud, "albumPhotosSelected", true);
                Call(hud, "RebuildModal");
                Check(!hud.GetComponentsInChildren<Transform>().Any(t => new[] { "No photos", "Photo page", "Previous photos", "Next photos" }.Contains(t.name)),
                    "Album removes bottom empty copy, page number and both page buttons", report);
                Check(JsonUtility.ToJson(notebook) == json && (save == null ? !File.Exists(session.SavePath) : save.SequenceEqual(File.ReadAllBytes(session.SavePath))),
                    "Notebook and save file are unchanged", report);
                report.Add("PASS: behavior and Canvas structure. Physical IME and subjective appearance require separate acceptance.");
            }
            catch (Exception error) { report.Add("FAIL: " + error); Debug.LogError(error); }
            finally
            {
                foreach (var pair in state) Set(hud, pair.Key, pair.Value);
                Set(session, "<Status>k__BackingField", status); Set(session, "<Observed>k__BackingField", observed);
                Set(session, "<Judging>k__BackingField", judging);
                Call(hud, "RebuildInspectorPreview");
                if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
                session.enabled = enabled; session.walker.enabled = walker;
                Application.runInBackground = background;
                Directory.CreateDirectory(Path.GetDirectoryName(Report)); File.WriteAllLines(Report, report);
            }
            yield break;
        }

        static object Get(object o, string n) => o.GetType().GetField(n, Flags).GetValue(o);
        static void Set(object o, string n, object v) => o.GetType().GetField(n, Flags).SetValue(o, v);
        static void Call(object o, string n) => o.GetType().GetMethod(n, Flags).Invoke(o, null);
        static void Check(bool value, string message, List<string> report)
        { if (!value) throw new InvalidOperationException(message); report.Add("PASS: " + message); }
    }
}
