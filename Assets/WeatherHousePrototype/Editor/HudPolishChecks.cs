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
            string[] hudNames = { "<JournalOpen>k__BackingField", "glyphDetailOpen", "dialogueOpen", "replyDraft", "lastReply", "echoUntil", "dirty", "expandedPhoto", "albumPhotosSelected", "page", "selectedGlyph", "lastCompositionFrame", "displayedStatus", "statusUntil" };
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
                Check(label.rectTransform.parent.GetComponent<RectTransform>().anchorMin.y == 0f,
                    "Status lives in a bottom-anchored group", report);
                Check(Get(hud, "statusGlow") is BreathingTextGlow, "Glow is attached to the text itself", report);
                var statusRect = (RectTransform)label.transform.parent;
                Vector2 statusPosition = statusRect.anchoredPosition;
                Set(session, "<Judging>k__BackingField", true);
                Call(hud, "UpdateStatus");
                Check(label.text.Contains("听"), "Judging takes priority over the interaction hint", report);
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
                Set(hud, "dialogueOpen", true); Set(hud, "replyDraft", ""); Call(hud, "RebuildModal");
                var input = (InputField)Get(hud, "replyInput");
                Check(input.GetComponent<Image>().color.a == 0f && input.textComponent.fontSize >= Mathf.RoundToInt(hud.playerBubbleSize.x / 900f * 30f),
                    "Reply has no inner white panel and uses larger text", report);
                Check(!hud.GetComponentsInChildren<Transform>().Any(t => t.name == "Reply instruction" || t.name == "Send reply" || (t.name == "Close" && t.IsChildOf(input.transform.parent))),
                    "Reply removes its heading, send button and close button", report);
                input.onSubmit.Invoke("");
                Check((bool)Get(hud, "dialogueOpen"), "Empty Enter does not close or send", report);
                Call(hud, "UpdateStatus");
                Check(statusRect.anchoredPosition == statusPosition, "Opening the reply keeps the status height fixed", report);
                input.text = "先选择中文，再按回车。";
                Set(hud, "lastCompositionFrame", Time.frameCount);
                input.onSubmit.Invoke(input.text);
                Check((bool)Get(hud, "dialogueOpen"), "An IME commit frame does not send", report);
                Set(hud, "lastCompositionFrame", -10);
                // A background animal exits Reply before any network request or notebook write.
                Set(session, "<Observed>k__BackingField", session.animals.First(a => a != null && a.background));
                input.onSubmit.Invoke(input.text);
                Check(!(bool)Get(hud, "dialogueOpen") && (string)Get(hud, "lastReply") == input.text,
                    "Input submit reaches the existing reply flow exactly once", report);
                var empty = Resources.Load<Texture2D>("AlbumBook/PhotoEmpty");
                var missing = Resources.Load<Texture2D>("AlbumBook/PhotoMissing");
                Check(empty != null && missing != null && empty != missing, "Distinct generated empty and missing images load", report);
                var controls = hud.GetComponentsInChildren<RectTransform>().First(t => t.name == "Controls");
                Check(controls.GetComponent<Graphic>() == null && controls.GetComponentsInChildren<ProceduralUiGraphic>().Length == 3,
                    "Footer has three minimal icons and no background bar", report);
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
