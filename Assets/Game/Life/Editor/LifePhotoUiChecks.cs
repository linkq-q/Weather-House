using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;
using WeatherHousePrototype;
using Object = UnityEngine.Object;

namespace WeatherHouse.Life.Editor
{
    // Run by the shared Play runner only. All invented evidence is an isolated, restored UI fixture.
    public static class LifePhotoUiChecks
    {
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        static readonly string[] Tabs = { "Symbols tab", "Photos tab", "Gesture learning tab", "Photography tasks tab" };

        public static IEnumerator Run(LifeSession life, Action<string> completed)
        {
            var checks = new List<string>();
            IEnumerator steps = Steps(life, checks);
            Exception failure = null;
            while (true)
            {
                bool more; object next = null;
                try { more = steps.MoveNext(); if (more) next = steps.Current; }
                catch (Exception error) { failure = error; break; }
                if (!more) break;
                yield return next;
            }
            (steps as IDisposable)?.Dispose();
            string report = (failure == null ? "PASS" : "FAIL") + " photography UI\n" + string.Join("\n", checks) +
                (failure == null ? "" : "\n" + failure);
            Directory.CreateDirectory("LifeGameplayReview/20261026/photo-flow");
            File.WriteAllText("LifeGameplayReview/20261026/photo-flow/ui-play-checks.txt", report);
            completed?.Invoke(report);
        }

        static IEnumerator Steps(LifeSession life, List<string> checks)
        {
            Check(Application.isPlaying && life && !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WEATHERHOUSE_LIFE_REVIEW_SAVE")),
                "Photography UI checks run only in isolated Play review", checks);
            Directory.CreateDirectory("LifeGameplayReview/20261026/photo-flow");
            var game = life.Gameplay; var hud = life.GetComponent<GameplayHud>();
            Check(hud && !hud.JournalOpen && !hud.CameraMode && !hud.IsDialogueOpen, "Photography UI starts idle", checks);
            string original = JsonUtility.ToJson(game.Notebook), status = game.Status;
            string[] stateFields = { "albumPhotosSelected", "albumGesturesSelected", "albumTasksSelected", "glyphPage", "photoTaskPage", "page", "selectedGlyph" };
            var values = stateFields.ToDictionary(name => name, name => Get<object>(hud, name));
            UiPhotoFixture fixture = null;
            life.World.PublicPhotoContext -= game.RecordPublicContext;
            try
            {
                JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(new LanguageNotebook()), game.Notebook);
                game.Notebook.Normalize(game.Catalog);
                Set(hud, "albumPhotosSelected", false); Set(hud, "albumGesturesSelected", false); Set(hud, "albumTasksSelected", true);
                Set(hud, "glyphPage", 0); Set(hud, "photoTaskPage", 0); Set(hud, "page", 0); Set(hud, "selectedGlyph", null);
                hud.ToggleJournal();
                var book = life.GetComponent<LifeJournal3D>();
                float deadline = Time.realtimeSinceStartup + 15;
                while (book.IsAnimating && Time.realtimeSinceStartup < deadline) yield return null;
                Check(hud.JournalOpen && !book.IsAnimating, "Photography spread opens in the real 3D book", checks);
                var album = Get<RectTransform>(hud, "albumRoot");
                Transform p01 = album.Find("Photo task P01");
                Check(album.GetComponentsInChildren<Transform>(true).Count(t => t.name.StartsWith("Photo task ", StringComparison.Ordinal)) == 6 &&
                    p01 && p01.GetComponentInChildren<AnimalGlyphGraphic>().SvgBody == game.Catalog.glyphs.Single(g => g.id == "fox").body,
                    "Six readable task rows show the real authored fox glyph", checks);
                Check(!p01.GetComponentsInChildren<Text>(true).Any(t => t.text.Contains("狐狸")) && !p01.Find("Recorded seal") &&
                    !p01.GetComponentInChildren<Button>(true), "An unfinished task reveals no Chinese answer, completion stamp or evidence button", checks);
                CheckTabs(hud, false, checks); Screenshot("photography-unlearned.png");
                yield return null; yield return null;

                fixture = new UiPhotoFixture();
                game.Notebook.AddObservation(fixture.record);
                Check(game.Notebook.ApplyPhotoTasks() == 2, "One sleeping-fox photo pays P01 and P09 in the UI fixture", checks);
                game.Notify("摄影 UI 回归检查"); yield return null; yield return null;
                album = Get<RectTransform>(hud, "albumRoot"); p01 = album.Find("Photo task P01");
                Check(p01.Find("Recorded seal") && !p01.GetComponentsInChildren<Text>(true).Any(t => t.name == "Meaning fox"),
                    "The open task page marks completion without revealing the fox meaning", checks);
                p01.GetComponentInChildren<Button>(true).onClick.Invoke();
                Check(Get<ObservationRecord>(hud, "expandedPhoto")?.id == fixture.record.id && Get<RawImage>(hud, "expandedImage").texture &&
                    Get<RawImage>(hud, "expandedImage").texture != Resources.Load<Texture2D>("AlbumBook/PhotoMissing"), "Clicking the completed task opens its actual saved PNG evidence", checks);
                var detail = Get<RectTransform>(hud, "detailRoot");
                Check(!detail.GetComponentsInChildren<Text>(true).Any(t => t.name == "Meaning fox" || t.name == "Meaning sleep"),
                    "Replay preserves the photographed glyphs without automatically revealing meanings", checks);
                Screenshot("photography-evidence.png"); yield return null; yield return null; Call(hud, "CloseTop");
                Check(hud.JournalOpen && Get<ObservationRecord>(hud, "expandedPhoto") == null, "Closing evidence returns to the same open book", checks);

                Get<RectTransform>(hud, "albumRoot").Find("Next photography page").GetComponent<Button>().onClick.Invoke();
                Check(book.IsAnimating, "Photography next-page button starts a physical page turn", checks);
                CheckTabs(hud, true, checks);
                deadline = Time.realtimeSinceStartup + 8;
                while (book.IsAnimating && Time.realtimeSinceStartup < deadline) yield return null;
                var sleepTask = Get<RectTransform>(hud, "albumRoot").Find("Photo task P09");
                Check(!book.IsAnimating && sleepTask.Find("Recorded seal") && !sleepTask.GetComponentsInChildren<Text>(true)
                    .Any(t => t.name == "Meaning sleep"), "The same photo completes sleep on another spread without confirming its meaning", checks);
                Screenshot("photography-sleep-known.png");
                yield return null; yield return null;

                game.Notebook.AddPublicContext(new LifePublicContext { id = "ui-i01", kind = "interaction", targetId = "I01", completed = true,
                    description = "回看测试：狐狸先说，松鼠再说；说话者与听者已公开交换。", glyphIds = new[] { "self", "you" } });
                game.Notebook.ApplyPhotoTasks(); Set(hud, "photoTaskPage", 3); Call(hud, "RebuildModal");
                var interaction = Get<RectTransform>(hud, "albumRoot").Find("Photo task I01");
                Check(interaction && game.Notebook.FindTaskPhotos("I01").Count == 0, "Interaction completion needs no invented photo", checks);
                interaction.GetComponentInChildren<Button>(true).onClick.Invoke();
                Check(Get<string>(hud, "publicTaskId") == "I01" && Get<RectTransform>(hud, "detailRoot").GetComponentsInChildren<Text>(true)
                    .Any(t => t.name == "Public encounter context" && t.text.Contains("说话者与听者已公开交换")), "Clicking I01 opens its persisted public encounter record", checks);
                Screenshot("photography-public-record.png"); yield return null; yield return null; Call(hud, "CloseTop");

                foreach (var word in game.Notebook.words) word.encountered = true;
                Get<RectTransform>(hud, "albumRoot").Find("Symbols tab").GetComponent<Button>().onClick.Invoke();
                deadline = Time.realtimeSinceStartup + 8;
                while (book.IsAnimating && Time.realtimeSinceStartup < deadline) yield return null;
                var seen = new HashSet<string>();
                for (int page = 0; page < 2; page++)
                {
                    album = Get<RectTransform>(hud, "albumRoot");
                    var symbols = album.GetComponentsInChildren<AnimalGlyphGraphic>(true).Where(g => g.transform.parent.name.StartsWith("Word ", StringComparison.Ordinal)).ToArray();
                    Check(symbols.Length == (page == 0 ? 22 : 9), "31 glyphs use 22 + 9 slots, page " + (page + 1), checks);
                    foreach (var symbol in symbols)
                    {
                        string id = symbol.name.Substring("Glyph ".Length); seen.Add(id);
                        bool known = game.Notebook.GetWord(id).confirmed;
                        Check(symbol.gameObject.activeSelf && symbol.SvgBody == game.Catalog.glyphs.Single(g => g.id == id).body &&
                            symbol.rectTransform.sizeDelta.x >= (known ? 47 : 53) && symbol.rectTransform.sizeDelta.y >= (known ? 56 : 69) && AnimalGlyphGraphic.ParseSvgBody(symbol.SvgBody).Count > 0,
                            "Authored glyph retains its readable grid size: " + id, checks);
                        var wordRect = (RectTransform)symbol.transform.parent;
                        Check(Fits(symbol.rectTransform, wordRect), "Glyph stays inside its original paper circle: " + id, checks);
                        var feedback = wordRect.GetComponent<LifeJournalWord>();
                        Check(feedback && !feedback.TooltipVisible && !wordRect.Find("Meaning " + id) && !wordRect.Find("Knowledge ring"),
                            "Word grid has no permanent meaning or state ring: " + id, checks);
                        var meaning = wordRect.Find("Word meaning tooltip/Confirmed word meaning")?.GetComponent<Text>();
                        Check(known ? meaning && meaning.text == game.Catalog.glyphs.Single(g => g.id == id).name && meaning.fontSize >= 16 : !meaning,
                            "Only confirmed words retain a hidden hover meaning: " + id, checks);
                    }
                    CheckTabs(hud, false, checks); Screenshot("photography-glyphs-" + (page + 1) + ".png");
                    yield return null; yield return null;
                    if (page == 0)
                    {
                        album.Find("Next symbols").GetComponent<Button>().onClick.Invoke();
                        deadline = Time.realtimeSinceStartup + 8;
                        while (book.IsAnimating && Time.realtimeSinceStartup < deadline) yield return null;
                    }
                }
                Check(seen.Count == 31, "Both pages cover every original and added glyph exactly once", checks);
                album.Find("Photography tasks tab").GetComponent<Button>().onClick.Invoke();
                Call(hud, "CloseTop"); Check(book.IsClosing, "Closing during a tab page turn queues the close", checks);
                hud.ToggleJournal(); Check(!book.IsClosing, "A second journal toggle cancels the queued close", checks);
                deadline = Time.realtimeSinceStartup + 10;
                while (book.IsAnimating && Time.realtimeSinceStartup < deadline) yield return null;
                Check(hud.JournalOpen && !book.IsAnimating && Mathf.Approximately(book.PresentationOpacity, 1f), "Cancelling close restores an opaque, interactive book", checks);
                CheckTabs(hud, false, checks);
                hud.ToggleJournal(); deadline = Time.realtimeSinceStartup + 10;
                while (hud.JournalOpen && Time.realtimeSinceStartup < deadline) yield return null;
                Check(!hud.JournalOpen && !hud.BlocksMovement && !life.GestureInputActive, "Final close releases gameplay input", checks);
            }
            finally
            {
                life.GetComponent<LifeJournal3D>()?.Cancel(); Call(hud, "CompleteJournalClose");
                JsonUtility.FromJsonOverwrite(original, game.Notebook);
                foreach (var value in values) Set(hud, value.Key, value.Value);
                fixture?.Dispose();
                life.World.PublicPhotoContext += game.RecordPublicContext;
                game.SaveNotebook(); game.Notify(status);
            }
        }

        static void CheckTabs(GameplayHud hud, bool disabled, List<string> checks)
        {
            Canvas.ForceUpdateCanvases(); var album = Get<RectTransform>(hud, "albumRoot");
            bool tasks = Get<bool>(hud, "albumTasksSelected"), gestures = Get<bool>(hud, "albumGesturesSelected"), photos = Get<bool>(hud, "albumPhotosSelected");
            Check(album.Find("Photography tasks tab").GetComponent<LifeJournalTabGraphic>().selected == tasks &&
                album.Find("Gesture learning tab").GetComponent<LifeJournalTabGraphic>().selected == gestures &&
                album.Find("Symbols tab").GetComponent<RawImage>().texture.name == (tasks || gestures || photos ? "TabSymbolsIdle" : "TabSymbolsSelected") &&
                album.Find("Photos tab").GetComponent<RawImage>().texture.name == (!tasks && !gestures && photos ? "TabPhotosSelected" : "TabPhotosIdle"),
                "Selected tab artwork matches the actual journal section", checks);
            foreach (string name in Tabs)
            {
                Transform tab = album.Find(name); var graphic = tab.GetComponent<Graphic>();
                float alpha = graphic.color.a * graphic.canvasRenderer.GetAlpha() * graphic.canvasRenderer.GetInheritedAlpha();
                Check(alpha >= .999f && tab.GetComponent<Button>().IsInteractable() != disabled,
                    "Tab stays fully opaque and " + (disabled ? "blocked during motion: " : "interactive when settled: ") + name, checks);
            }
        }

        sealed class UiPhotoFixture : IDisposable
        {
            public readonly ObservationRecord record;
            readonly string folder, root;
            public UiPhotoFixture()
            {
                string id = Guid.NewGuid().ToString("N");
                root = Path.GetFullPath(Path.Combine(GameplaySession.PersistenceDirectory, "MotionPhotos"));
                folder = Path.GetFullPath(Path.Combine(root, id));
                Directory.CreateDirectory(folder);
                var texture = new Texture2D(512, 288, TextureFormat.RGB24, false);
                try
                {
                    texture.SetPixels32(Enumerable.Repeat(new Color32(118, 148, 105, 255), 512 * 288).ToArray()); texture.Apply();
                    File.WriteAllBytes(Path.Combine(folder, "frame_000.png"), texture.EncodeToPNG());
                }
                finally { Object.Destroy(texture); }
                record = new ObservationRecord { id = id, clipFolder = "MotionPhotos/" + id, frameCount = 1, framesPerSecond = 6,
                    glyphIds = new[] { "fox", "sleep" }, capturedUtcTicks = DateTime.UtcNow.Ticks,
                    photoSnapshot = new LifePhotoSnapshot { entities = new[] { new LifePhotoEntity { id = "ui-fox", kind = "animal", speciesId = "fox", bodyVisible = true, sleeping = true } } } };
                File.WriteAllText(Path.Combine(folder, "metadata.json"), JsonUtility.ToJson(record));
            }
            public void Dispose()
            {
                if (!folder.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal)) throw new InvalidOperationException("UI fixture escaped persistence root");
                if (Directory.Exists(folder)) Directory.Delete(folder, true);
            }
        }

        static void Screenshot(string name) => ScreenCapture.CaptureScreenshot(Path.Combine("LifeGameplayReview/20261026/photo-flow", name));
        static bool Fits(RectTransform item, RectTransform container)
        {
            var corners = new Vector3[4]; item.GetWorldCorners(corners);
            Rect bounds = container.rect; bounds.xMin -= .1f; bounds.yMin -= .1f; bounds.xMax += .1f; bounds.yMax += .1f;
            return corners.All(corner => bounds.Contains(container.InverseTransformPoint(corner)));
        }
        static T Get<T>(object owner, string field) => (T)owner.GetType().GetField(field, Private).GetValue(owner);
        static void Set(object owner, string field, object value) => owner.GetType().GetField(field, Private).SetValue(owner, value);
        static void Call(object owner, string method, params object[] args) => owner.GetType().GetMethod(method, Private).Invoke(owner, args);
        static void Check(bool condition, string label, List<string> checks)
        { if (!condition) throw new InvalidOperationException(label); checks.Add("PASS " + label); }
    }
}
