using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace WeatherHousePrototype.Editor
{
    public static class AlbumBookChecks
    {
        private const string ScenePath = "Assets/WeatherHousePrototype/WeatherHouse_MapDraft.unity";
        private const string AssetFolder = "Assets/WeatherHousePrototype/Resources/AlbumBook";
        private const string Output = "PrototypeReview/AlbumBook";
        private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        private static readonly string[] Samples = { "SampleHouse", "SampleFrog", "SampleSquirrel", "SampleTraveler" };
        private static bool running;

        public static void ImportAssets()
        {
            Require(!EditorApplication.isPlayingOrWillChangePlaymode, "Stop Play before importing album textures.");
            Require(Directory.Exists(AssetFolder), "AlbumBook resources have not been copied into the project.");
            AssetDatabase.Refresh();
            foreach (string path in Directory.GetFiles(AssetFolder, "*.png").Select(p => p.Replace('\\', '/')))
            {
                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                Require(importer != null, "Texture importer is missing: " + path);
                // RawImage uses the original texture, so sprite slicing is unnecessary.
                importer.textureType = TextureImporterType.Default;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.maxTextureSize = 4096;
                importer.npotScale = TextureImporterNPOTScale.None;
                importer.alphaSource = TextureImporterAlphaSource.FromInput;
                importer.alphaIsTransparency = true;
                importer.sRGBTexture = true;
                importer.mipmapEnabled = false;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.filterMode = FilterMode.Bilinear;
                importer.SaveAndReimport();
            }
        }

        public static void Prepare()
        {
            Require(!EditorApplication.isPlayingOrWillChangePlaymode, "Stop Play before preparing the album review.");
            for (int i = 0; i < SceneManager.sceneCount; i++)
                Require(!SceneManager.GetSceneAt(i).isDirty,
                    "Unsaved scene changes were preserved. Save or close that scene yourself before preparing the review: " + SceneManager.GetSceneAt(i).path);
            ImportAssets();
            if (SceneManager.GetActiveScene().path != ScenePath)
                EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            Debug.Log("Album review prepared. Enter Play, then run AlbumBookChecks.Run(). No scene was saved.");
        }

        public static void Run()
        {
            Require(!running, "Album checks are already running.");
            Directory.CreateDirectory(Output);
            var report = new List<string> { "Album book checks: actual Play Canvas", DateTime.UtcNow.ToString("O") };
            try
            {
                Require(EditorApplication.isPlaying, "Enter Play before running album checks.");
                var session = Object.FindObjectOfType<GameplaySession>();
                Require(session != null && session.isActiveAndEnabled, "An active GameplaySession is required.");
                var hud = session.GetComponent<GameplayHud>();
                Require(hud != null && hud.isActiveAndEnabled && Get<Canvas>(hud, "canvas") != null, "Wait for the actual GameplayHud to start.");
                Require(!session.Judging && !session.Recorder.IsRecording, "Wait for the current reply judgement or photo capture to finish.");
                running = true;
                File.WriteAllLines(Output + "/checks.txt", report);
                session.StartCoroutine(Guard(CheckLive(session, hud, report), report));
            }
            catch (Exception error)
            {
                running = false;
                report.Add("FAIL: " + error);
                File.WriteAllLines(Output + "/checks.txt", report);
                throw;
            }
        }

        private static IEnumerator Guard(IEnumerator checks, List<string> report)
        {
            bool background = Application.runInBackground;
            Application.runInBackground = true;
            float start = Time.realtimeSinceStartup;
            bool complete = false;
            Exception failure = null;
            try
            {
                while (!complete && failure == null)
                {
                    object next = null;
                    try
                    {
                        Require(Time.realtimeSinceStartup - start < 25f, "Album checks exceeded 25 seconds.");
                        complete = !checks.MoveNext();
                        if (!complete) next = checks.Current;
                    }
                    catch (Exception error) { failure = error; }
                    if (!complete && failure == null) yield return next;
                }
            }
            finally
            {
                try { (checks as IDisposable)?.Dispose(); }
                catch (Exception error) { if (failure == null) failure = error; }
                if (!complete && failure == null) failure = new OperationCanceledException("Album checks stopped before completion.");
                running = false;
                Application.runInBackground = background;
                report.Add(failure == null ? "PASS: Play structure and interactions. Inspect PNGs separately for visual acceptance." : "FAIL: " + failure);
                File.WriteAllLines(Output + "/checks.txt", report);
                if (failure == null) Debug.Log("Album checks passed: " + Output + "/checks.txt");
                else Debug.LogError("Album checks failed: " + failure);
            }
        }

        private static IEnumerator CheckLive(GameplaySession session, GameplayHud hud, List<string> report)
        {
            string[] names = { "selectedGlyph", "guessGlyphId", "guessDraft", "replyDraft", "page", "glyphPage", "albumPhotosSelected", "glyphDetailOpen",
                "dialogueOpen", "expandedPhoto", "currentFrame", "playingPhoto", "nextFrameAt", "dirty" };
            object[] values = names.Select(name => Get<object>(hud, name)).ToArray();
            bool journal = hud.JournalOpen, camera = hud.CameraMode, samples = hud.useAlbumDesignSamples;
            bool hudEnabled = hud.enabled, walkerEnabled = session.walker.enabled;
            bool inputBlocked = session.walker.InputBlocked, jumpBlocked = session.walker.JumpBlocked, cameraBlocked = session.walker.CameraInputBlocked;
            float nextObservation = Get<float>(session, "nextObservation");
            LanguageNotebook originalNotebook = session.Notebook;
            string notebookJson = JsonUtility.ToJson(originalNotebook);
            byte[] save = File.Exists(session.SavePath) ? File.ReadAllBytes(session.SavePath) : null;
            try
            {
                Set(session, "nextObservation", float.PositiveInfinity);
                Set(session, "<Notebook>k__BackingField", JsonUtility.FromJson<LanguageNotebook>(notebookJson));
                session.walker.enabled = false;
                hud.enabled = false;
                hud.useAlbumDesignSamples = true;
                Set(hud, "selectedGlyph", null);
                Set(hud, "expandedPhoto", null);
                Set(hud, "glyphDetailOpen", false);
                Set(hud, "glyphPage", 0);
                Set(hud, "page", 0);
                Set(hud, "albumPhotosSelected", false);
                if (hud.JournalOpen) hud.ToggleJournal();
                hud.ToggleJournal();
                yield return null;
                Canvas.ForceUpdateCanvases();

                var book = Find<RectTransform>(hud, "Notebook");
                Check((book.sizeDelta - new Vector2(1672f, 941f)).sqrMagnitude < .01f, "Notebook uses the 1672 x 941 reference layout", report);
                for (int i = 0; i < 4; i++)
                {
                    var photo = Find<RawImage>(hud, "Sample photo " + (i + 1));
                    var frame = Find<RawImage>(hud, "Photo frame " + (i + 1));
                    Check(photo.texture == Resource(Samples[i]) && photo.transform.parent == frame.transform.parent &&
                        photo.transform.GetSiblingIndex() > frame.transform.GetSiblingIndex(),
                        "Sample photo " + (i + 1) + " uses its own texture and renders above its frame", report);
                }
                foreach (int slot in new[] { 2, 3 })
                {
                    var sticker = Find<RawImage>(hud, "Photo sticker " + slot);
                    var photo = Find<RawImage>(hud, "Sample photo " + slot);
                    Check(sticker.texture != null && sticker.transform.parent == book && photo.transform.parent == book &&
                        sticker.transform.GetSiblingIndex() > photo.transform.GetSiblingIndex() &&
                        Bounds(sticker.rectTransform).Overlaps(Bounds(photo.rectTransform)),
                        "Sticker " + slot + " overlaps its photo and renders after it under the same book parent", report);
                }
                var first = WordIds(hud);
                Check(!hud.GetComponentsInChildren<RawImage>().Any(i => i.texture != null && i.texture.name == "ArrowRight") &&
                    Find<Button>(hud, "Next symbols").GetComponent<Image>().color.a == 0f,
                    "No exterior right arrow; an invisible hit target over the counter turns the symbol page", report);
                Check(first.Count == 22, "First symbol page contains 22 distinct authored glyphs", report);
                Check(Find<Text>(hud, "Collected count").text == "24" && Find<Text>(hud, "Total count").text == "/24",
                    "Design sample counter reads 24 /24", report);
                CheckTabs(hud, false, report);
                yield return new WaitForEndOfFrame();
                SaveScreen("live-symbols-1.png");

                Find<Button>(hud, "Next symbols").onClick.Invoke();
                yield return null;
                var second = WordIds(hud);
                Check(second.Count == 2 && !first.Overlaps(second) && new HashSet<string>(first.Concat(second)).SetEquals(session.Catalog.glyphs.Select(g => g.id)),
                    "Second page contains the remaining two glyphs; both pages cover the catalog exactly once", report);
                Check(Get<int>(hud, "page") == 0, "Symbol paging leaves the independent photo page unchanged", report);
                yield return new WaitForEndOfFrame();
                SaveScreen("live-symbols-2.png");

                int symbolPage = Get<int>(hud, "glyphPage"), photoPage = Get<int>(hud, "page");
                Find<Button>(hud, "Photos tab").onClick.Invoke();
                yield return null;
                CheckTabs(hud, true, report);
                Check(Get<int>(hud, "page") == photoPage && Get<int>(hud, "glyphPage") == symbolPage,
                    "Switching tabs preserves independent photo and symbol page indices", report);
                yield return new WaitForEndOfFrame();
                SaveScreen("live-photos-tab.png");
                Find<Button>(hud, "Symbols tab").onClick.Invoke();
                yield return null;
                CheckTabs(hud, false, report);

                int seenIndex = Array.FindIndex(session.Catalog.glyphs, g => session.Notebook.GetWord(g.id)?.encountered == true);
                Set(hud, "glyphPage", Mathf.Max(0, seenIndex) / 22);
                Rebuild(hud);
                yield return null;
                Button word = seenIndex >= 0 ? Find<Button>(hud, "Word " + session.Catalog.glyphs[seenIndex].id) :
                    hud.GetComponentsInChildren<Button>().FirstOrDefault(b => b.name.StartsWith("Word ") && b.interactable);
                Require(word != null, "The sample symbol page has no clickable glyph.");
                string beforeClick = JsonUtility.ToJson(session.Notebook);
                word.onClick.Invoke();
                yield return null;
                Check(Get<bool>(hud, "glyphDetailOpen"), "Clicking a sample glyph opens the existing glyph detail flow", report);
                Check(JsonUtility.ToJson(session.Notebook) == beforeClick, "Opening glyph detail does not encounter or unlock vocabulary", report);
                if (seenIndex < 0) report.Add("NOTE: no encountered glyph existed in the current notebook; detail was checked with an unencountered sample glyph.");

                Find<Button>(hud, "Close").onClick.Invoke();
                string draftGlyph = session.Catalog.glyphs[0].id;
                GlyphKnowledge draftWord = session.Notebook.GetWord(draftGlyph);
                if (draftWord == null)
                {
                    draftWord = new GlyphKnowledge { glyphId = draftGlyph };
                    session.Notebook.words.Add(draftWord);
                }
                draftWord.encountered = true;
                draftWord.confirmed = false;
                string storedGuess = draftWord.guess;
                Set(hud, "glyphPage", 0);
                Rebuild(hud);
                Find<Button>(hud, "Word " + draftGlyph).onClick.Invoke();
                const string unsavedDraft = "未保存的猜字草稿，切换页签后仍然保留。";
                Find<InputField>(hud, "Word guess").text = unsavedDraft;
                Find<Button>(hud, "Close").onClick.Invoke();
                Find<Button>(hud, "Photos tab").onClick.Invoke();
                Find<Button>(hud, "Symbols tab").onClick.Invoke();
                Find<Button>(hud, "Word " + draftGlyph).onClick.Invoke();
                yield return null;
                Check(Find<InputField>(hud, "Word guess").text == unsavedDraft && Get<string>(hud, "guessDraft") == unsavedDraft && draftWord.guess == storedGuess,
                    "Regression: an unsaved input draft survives detail Close -> Photos tab -> Symbols tab -> same glyph, without replacing the stored guess (isolated cloned word)", report);
                Find<Button>(hud, "Close").onClick.Invoke();

                ObservationRecord savedPhoto = null;
                foreach (var record in session.Notebook.observations)
                {
                    Texture2D probe = null;
                    try { probe = MotionPhotoRecorder.LoadFrame(record, 0); if (probe != null) savedPhoto = record; }
                    catch (Exception) { /* Missing older captures are not fabricated for this check. */ }
                    finally { if (probe != null) Object.Destroy(probe); }
                    if (savedPhoto != null) break;
                }
                if (savedPhoto == null) report.Add("SKIP: no readable saved photo exists; no capture or replacement save was created for playback checks.");
                else
                {
                    Set(hud, "page", session.Notebook.observations.IndexOf(savedPhoto) / 4);
                    Find<Button>(hud, "Photos tab").onClick.Invoke();
                    Find<Button>(hud, "Photo " + savedPhoto.id).onClick.Invoke();
                    Require(Get<ObservationRecord>(hud, "expandedPhoto") == savedPhoto && Find<RawImage>(hud, "Motion photo frame").texture != null,
                        "The existing photo did not open with its saved first frame.");
                    Find<Button>(hud, "Play motion photo").onClick.Invoke();
                    Require(Get<bool>(hud, "playingPhoto"), "The existing photo did not enter playback.");
                    if (savedPhoto.frameCount > 1)
                    {
                        yield return new WaitForSecondsRealtime(1f / Mathf.Max(1f, savedPhoto.framesPerSecond) + .02f);
                        typeof(GameplayHud).GetMethod("UpdatePlayback", Fields).Invoke(hud, null);
                        Require(Get<int>(hud, "currentFrame") == 1 && Find<RawImage>(hud, "Motion photo frame").texture != null,
                            "Playback did not advance to the actual saved second frame.");
                    }
                    Find<Button>(hud, "Play motion photo").onClick.Invoke();
                    Require(!Get<bool>(hud, "playingPhoto"), "The existing photo did not pause.");
                    Find<Button>(hud, "Close").onClick.Invoke();
                    Check(Get<ObservationRecord>(hud, "expandedPhoto") == null && hud.JournalOpen,
                        "Existing saved photo opens, plays" + (savedPhoto.frameCount > 1 ? " a real second frame" : " its single frame") + ", pauses and closes back to the album", report);
                }
                Check(JsonUtility.ToJson(originalNotebook) == notebookJson && SameSave(session.SavePath, save),
                    "Original notebook and saved file are byte-for-byte unchanged; no SaveGuess or TakePhoto was called", report);
            }
            finally
            {
                try
                {
                    Set(session, "<Notebook>k__BackingField", originalNotebook);
                    hud.useAlbumDesignSamples = samples;
                    Set(hud, "<JournalOpen>k__BackingField", journal);
                    for (int i = 0; i < names.Length; i++) Set(hud, names[i], values[i]);
                    if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
                    hud.SetCameraMode(camera);
                    Rebuild(hud);
                    // Rebuild resets playback; restore its in-memory cursor/state after recreating the original view.
                    for (int i = 0; i < names.Length; i++) Set(hud, names[i], values[i]);
                }
                finally
                {
                    hud.enabled = hudEnabled;
                    Set(session, "nextObservation", nextObservation);
                    session.walker.enabled = walkerEnabled;
                    session.walker.InputBlocked = inputBlocked;
                    session.walker.JumpBlocked = jumpBlocked;
                    session.walker.CameraInputBlocked = cameraBlocked;
                }
                report.Add("RESTORED: original notebook object, sample setting, HUD open state, drafts, pages, playback cursor and player input. No review view was left open.");
            }
        }

        public static void CaptureReference()
        {
            Require(!EditorApplication.isPlayingOrWillChangePlaymode, "Stop Play before creating isolated reference renders.");
            Directory.CreateDirectory(Output);
            CaptureLayout(1672, 941, "layout-1672.png");
            CaptureLayout(1280, 720, "layout-1280.png");
            File.WriteAllText(Output + "/reference.txt",
                "PASS: isolated Unity preview-scene Canvas rendered original album layers, four separate sample textures and 22 real catalog glyphs at 1672x941 and 1280x720.\n" +
                "Neutral camera background; no flattened reference screenshot. This verifies the view layer, not the live gameplay entrypoint.\n");
        }

        private static void CaptureLayout(int width, int height, string file)
        {
            Scene active = SceneManager.GetActiveScene();
            Scene preview = EditorSceneManager.NewPreviewScene();
            RenderTexture target = null;
            Texture2D result = null;
            Font countFont = null;
            RenderTexture previousTarget = RenderTexture.active;
            try
            {
                var cameraObject = new GameObject("Album review camera", typeof(Camera));
                SceneManager.MoveGameObjectToScene(cameraObject, preview);
                var camera = cameraObject.GetComponent<Camera>();
                camera.scene = preview;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(.18f, .24f, .21f);
                camera.orthographic = true;
                camera.orthographicSize = height * .5f;
                camera.transform.position = new Vector3(0f, 0f, -100f);
                camera.nearClipPlane = .1f;
                camera.farClipPlane = 200f;
                camera.cullingMask = 1 << 29;
                camera.enabled = false;
                target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
                camera.targetTexture = target;
                var canvasObject = new GameObject("Album reference Canvas", typeof(RectTransform), typeof(Canvas));
                SceneManager.MoveGameObjectToScene(canvasObject, preview);
                var canvas = canvasObject.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = camera;
                canvas.planeDistance = 100f;
                canvas.scaleFactor = Mathf.Min(width / 1672f, height / 941f);
                Canvas.ForceUpdateCanvases();
                var book = AlbumBookView.Create(canvas.transform, false, null, null, null, null);
                for (int i = 0; i < 4; i++) AlbumBookView.Photo(book, i, Resource(Samples[i]), "Sample photo " + (i + 1));
                var catalog = JsonUtility.FromJson<GlyphCatalog>(Resources.Load<TextAsset>("WeatherHouseGlyphs").text);
                Require(catalog.glyphs.Length == 24, "Reference render requires the real 24-glyph catalog.");
                for (int i = 0; i < 22; i++) AlbumBookView.Word(book, i, catalog.glyphs[i], true, false, null);
                countFont = Font.CreateDynamicFontFromOSFont(new[] { "Comic Sans MS", "Segoe Print", "Arial" }, 72);
                AlbumBookView.Count(book, 24, 24, countFont);
                AlbumBookView.AddStickers(book);
                foreach (Transform child in canvasObject.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = 29;
                Canvas.ForceUpdateCanvases();
                camera.Render();
                RenderTexture.active = target;
                result = new Texture2D(width, height, TextureFormat.RGB24, false);
                result.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                result.Apply();
                File.WriteAllBytes(Output + "/" + file, result.EncodeToPNG());
            }
            finally
            {
                RenderTexture.active = previousTarget;
                if (target != null) { target.Release(); Object.DestroyImmediate(target); }
                if (result != null) Object.DestroyImmediate(result);
                EditorSceneManager.ClosePreviewScene(preview);
                if (countFont != null) Object.DestroyImmediate(countFont);
                if (active.IsValid() && active.isLoaded) SceneManager.SetActiveScene(active);
            }
        }

        private static void CheckTabs(GameplayHud hud, bool photos, List<string> report)
        {
            Check(Get<bool>(hud, "albumPhotosSelected") == photos &&
                Find<RawImage>(hud, "Symbols tab").texture == Resource(photos ? "TabSymbolsIdle" : "TabSymbolsSelected") &&
                Find<RawImage>(hud, "Photos tab").texture == Resource(photos ? "TabPhotosSelected" : "TabPhotosIdle"),
                photos ? "Photo tab selected texture and symbol idle texture are active" : "Symbol tab selected texture and photo idle texture are active", report);
        }

        private static HashSet<string> WordIds(GameplayHud hud)
        {
            string[] ids = hud.GetComponentsInChildren<Button>().Where(b => b.name.StartsWith("Word ")).Select(b => b.name.Substring(5)).ToArray();
            Require(ids.Distinct().Count() == ids.Length, "Duplicate glyph buttons on one page.");
            return new HashSet<string>(ids);
        }

        private static Rect Bounds(RectTransform rect)
        {
            var corners = new Vector3[4];
            rect.GetWorldCorners(corners);
            return Rect.MinMaxRect(corners.Min(c => c.x), corners.Min(c => c.y), corners.Max(c => c.x), corners.Max(c => c.y));
        }

        private static void SaveScreen(string name)
        {
            Texture2D image = ScreenCapture.CaptureScreenshotAsTexture();
            try { File.WriteAllBytes(Output + "/" + name, image.EncodeToPNG()); }
            finally { Object.Destroy(image); }
        }

        private static bool SameSave(string path, byte[] before) => before == null ? !File.Exists(path) : File.Exists(path) && before.SequenceEqual(File.ReadAllBytes(path));
        private static Texture2D Resource(string name) { var texture = Resources.Load<Texture2D>("AlbumBook/" + name); Require(texture != null, "Missing AlbumBook/" + name); return texture; }
        private static T Find<T>(GameplayHud hud, string name) where T : Component { var result = hud.GetComponentsInChildren<T>().FirstOrDefault(c => c.name == name); Require(result != null, "Active HUD is missing " + name); return result; }
        private static T Get<T>(object target, string name) => (T)target.GetType().GetField(name, Fields).GetValue(target);
        private static void Set(object target, string name, object value) => target.GetType().GetField(name, Fields).SetValue(target, value);
        private static void Rebuild(GameplayHud hud) => typeof(GameplayHud).GetMethod("RebuildModal", Fields).Invoke(hud, null);
        private static void Check(bool condition, string message, List<string> report) { Require(condition, message); report.Add("PASS: " + message); }
        private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    }
}
