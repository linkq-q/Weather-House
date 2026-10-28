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
        private const string ScenePath = "Assets/Scenes/WeatherHouse_MapDraft.unity";
        private const string AssetFolder = "Assets/Game/Resources/AlbumBook";
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
                bool noise = Path.GetFileNameWithoutExtension(path) == "RevealNoise";
                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                Require(importer != null, "Texture importer is missing: " + path);
                // RawImage uses the original texture, so sprite slicing is unnecessary.
                importer.textureType = TextureImporterType.Default;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.maxTextureSize = 4096;
                importer.npotScale = TextureImporterNPOTScale.None;
                importer.alphaSource = TextureImporterAlphaSource.FromInput;
                importer.alphaIsTransparency = !noise;
                importer.sRGBTexture = !noise;
                if (noise) importer.isReadable = true;
                importer.mipmapEnabled = false;
                importer.wrapMode = noise ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
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

        public static void Run(bool captureOpening = false)
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
                session.StartCoroutine(Guard(CheckLive(session, hud, report, captureOpening), report, captureOpening ? 90f : 25f));
            }
            catch (Exception error)
            {
                running = false;
                report.Add("FAIL: " + error);
                File.WriteAllLines(Output + "/checks.txt", report);
                throw;
            }
        }

        private static IEnumerator Guard(IEnumerator checks, List<string> report, float timeout)
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
                        Require(Time.realtimeSinceStartup - start < timeout, "Album checks exceeded their timeout.");
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

        private static IEnumerator CheckLive(GameplaySession session, GameplayHud hud, List<string> report, bool captureOpening)
        {
            string[] names = { "selectedGlyph", "guessGlyphId", "guessDraft", "page", "glyphPage", "albumPhotosSelected", "glyphDetailOpen",
                "expandedPhoto", "currentFrame", "playingPhoto", "nextFrameAt", "dirty" };
            object[] values = names.Select(name => Get<object>(hud, name)).ToArray();
            bool journal = hud.JournalOpen, camera = hud.CameraMode, samples = hud.useAlbumDesignSamples;
            bool hudEnabled = hud.enabled, walkerEnabled = session.walker.enabled;
            bool inputBlocked = session.walker.InputBlocked, jumpBlocked = session.walker.JumpBlocked, cameraBlocked = session.walker.CameraInputBlocked;
            float nextObservation = Get<float>(session, "nextObservation");
            LanguageNotebook originalNotebook = session.Notebook;
            string notebookJson = JsonUtility.ToJson(originalNotebook);
            byte[] save = File.Exists(session.SavePath) ? File.ReadAllBytes(session.SavePath) : null;
            float originalTimeScale = Time.timeScale;
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
                Set(hud, "<JournalOpen>k__BackingField", false);
                Rebuild(hud);
                hud.ToggleJournal();
                typeof(GameplayHud).GetMethod("PositionAnimalBubble", Fields).Invoke(hud, null);
                var opening = Find<AlbumBookOpening>(hud, "Notebook");
                var openingRoot = (RectTransform)opening.transform;
                Check(hud.IsAlbumOpening && !openingRoot.GetComponent<CanvasGroup>().blocksRaycasts,
                    "J opens the real animation and blocks book clicks during the transition", report);
                typeof(GameplayHud).GetMethod("TurnGlyphPage", Fields).Invoke(hud, new object[] { 1 });
                Check(Get<int>(hud, "glyphPage") == 0, "Symbol paging is ignored while the book is opening", report);
                yield return new WaitForSecondsRealtime(.18f);
                Check(openingRoot.GetComponentsInChildren<AlbumPaperCircleGraphic>().All(g =>
                        Vector4.Distance(g.canvasRenderer.GetColor(), Color.white) < .002f),
                    "Opening keeps symbol button tint fully colored and opaque instead of the disabled gray", report);
                float deadline = Time.realtimeSinceStartup + AlbumBookOpening.Duration + .5f;
                while (hud.IsAlbumOpening && Time.realtimeSinceStartup < deadline) yield return null;
                Check(!hud.IsAlbumOpening && openingRoot.anchoredPosition.sqrMagnitude < .001f &&
                    openingRoot.GetComponent<CanvasGroup>().blocksRaycasts,
                    "Automatic playback completes, returns to the exact final position and restores book clicks", report);
                Check(!openingRoot.GetComponentsInChildren<Graphic>().Any(g =>
                        g.material != null && g.material.name == "Album opening reveal (runtime)"),
                    "Completing the reveal restores every original UI material", report);
                CheckOpeningModes(hud, opening, report);

                if (captureOpening)
                {
                    Directory.CreateDirectory(Output + "/OpeningFrames");
                    Time.timeScale = 0f;
                    opening.Play();
                    Color32[] midRevealPixels = null;
                    var revealRegions = new Dictionary<string, Rect>();
                    for (int frame = 0; frame < 76; frame++)
                    {
                        opening.Sample(frame / 30f);
                        Canvas.ForceUpdateCanvases();
                        yield return new WaitForEndOfFrame();
                        Texture2D captured = ScreenCapture.CaptureScreenshotAsTexture();
                        try
                        {
                            File.WriteAllBytes(Output + "/OpeningFrames/frame-" + frame.ToString("D3") + ".jpg", captured.EncodeToJPG(92));
                            if (frame == 24)
                            {
                                midRevealPixels = captured.GetPixels32();
                                var visibleItems = openingRoot.GetComponentsInChildren<Graphic>()
                                    .Where(g => g is AlbumPaperCircleGraphic || g.name.StartsWith("Sample photo "));
                                foreach (Graphic item in visibleItems)
                                {
                                    var corners = new Vector3[4];
                                    item.rectTransform.GetWorldCorners(corners);
                                    Vector2 lo = RectTransformUtility.WorldToScreenPoint(null, corners[0]);
                                    Vector2 hi = RectTransformUtility.WorldToScreenPoint(null, corners[2]);
                                    revealRegions[item.name] = Rect.MinMaxRect(lo.x, lo.y, hi.x, hi.y);
                                }
                            }
                            if (frame == 40)
                            {
                                Color32[] settled = captured.GetPixels32();
                                Require(revealRegions.Count == 26 && midRevealPixels.Length == settled.Length,
                                    "The GPU reveal comparison needs all four photos and 22 symbols at a stable resolution.");
                                foreach (var region in revealRegions)
                                {
                                    int changed = 0, sampled = 0;
                                    Rect bounds = region.Value;
                                    for (int y = Mathf.CeilToInt(bounds.yMin + bounds.height*.15f); y < bounds.yMax - bounds.height*.15f; y++)
                                        for (int x = Mathf.CeilToInt(bounds.xMin + bounds.width*.15f); x < bounds.xMax - bounds.width*.15f; x++)
                                        {
                                            int pixel = y*captured.width+x;
                                            if (pixel < 0 || pixel >= settled.Length) continue;
                                            Color32 a = midRevealPixels[pixel], b = settled[pixel];
                                            if (Mathf.Abs(a.r-b.r)+Mathf.Abs(a.g-b.g)+Mathf.Abs(a.b-b.b) > 30) changed++;
                                            sampled++;
                                        }
                                    Check(sampled > 0 && changed/(float)sampled > .12f,
                                        "GPU pixels participate in reveal: " + region.Key + " (" +
                                        (100f*changed/Mathf.Max(1,sampled)).ToString("F1") + "% changing)", report);
                                }
                            }
                        }
                        finally { Object.Destroy(captured); }
                    }
                    File.WriteAllText(Output + "/OpeningFrames/capture.txt",
                        "Actual Unity gameplay Canvas, deterministic animation samples at 30 fps. Scene time paused during sampling.\n" +
                        "Frames 0..75 cover 2.5 seconds; opening duration " + AlbumBookOpening.Duration +
                        " seconds, then final view held.\n");
                    report.Add("PASS: recorded 76 actual Unity Canvas frames using the same opening renderer at 30 fps");
                    Time.timeScale = originalTimeScale;
                }

                foreach (object step in CheckClosing(hud, report, captureOpening)) yield return step;
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
                Check(!hud.IsAlbumOpening && hud.GetComponentInChildren<AlbumBookOpening>() != null,
                    "Changing the symbol page retains an idle animation controller without replaying the opening", report);
                var second = WordIds(hud);
                Check(second.Count == 2 && !first.Overlaps(second) && new HashSet<string>(first.Concat(second)).SetEquals(session.Catalog.glyphs.Select(g => g.id)),
                    "Second page contains the remaining two glyphs; both pages cover the catalog exactly once", report);
                Check(Get<int>(hud, "page") == 0, "Symbol paging leaves the independent photo page unchanged", report);
                yield return new WaitForEndOfFrame();
                SaveScreen("live-symbols-2.png");

                var pagedOpening = Find<AlbumBookOpening>(hud, "Notebook");
                hud.ToggleJournal();
                Check(hud.JournalOpen && pagedOpening.IsClosing && hud.BlocksMovement,
                    "J closes a rebuilt symbol page through its own reverse timeline", report);
                deadline = Time.realtimeSinceStartup + AlbumBookOpening.Duration + .5f;
                while (hud.JournalOpen && Time.realtimeSinceStartup < deadline) yield return null;
                Check(!hud.JournalOpen && !hud.BlocksMovement, "Closing after paging completes and releases movement", report);
                hud.ToggleJournal();
                Find<AlbumBookOpening>(hud, "Notebook").FinishImmediately();
                Check(Get<int>(hud, "glyphPage") == 1 && WordIds(hud).SetEquals(second),
                    "Closing and reopening preserves the selected symbol page", report);

                AlbumMagicRevealSettingsEditor.Replay();
                yield return null;
                Check(hud.JournalOpen && hud.IsAlbumOpening && Get<int>(hud,"glyphPage") == 1,
                    "Inspector replay reopens the book after paging and preserves its selected page", report);
                Find<AlbumBookOpening>(hud,"Notebook").FinishImmediately();
                AlbumMagicRevealSettingsEditor.Replay();
                yield return null;
                Check(hud.IsAlbumOpening && hud.GetComponentsInChildren<Transform>().Count(t => t.name == "Opening magic aura") ==
                    (AlbumMagicRevealSettings.Load().enableNoiseDissolve ? 1 : 0) &&
                    hud.GetComponentsInChildren<Transform>().Count(t => t.name == "Opening magic stars") == 1,
                    "Repeated Inspector replay keeps one star layer and only the selected mode's aura", report);
                Find<AlbumBookOpening>(hud,"Notebook").FinishImmediately();

                int symbolPage = Get<int>(hud, "glyphPage"), photoPage = Get<int>(hud, "page");
                Find<Button>(hud, "Photos tab").onClick.Invoke();
                yield return null;
                Check(!hud.IsAlbumOpening, "Changing tabs does not replay the opening animation", report);
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
                    typeof(GameplayHud).GetMethod("PositionAnimalBubble", Fields).Invoke(hud, null);
                    // Rebuild resets playback; restore its in-memory cursor/state after recreating the original view.
                    for (int i = 0; i < names.Length; i++) Set(hud, names[i], values[i]);
                }
                finally
                {
                    Time.timeScale = originalTimeScale;
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

        private static IEnumerable<object> CheckClosing(GameplayHud hud, List<string> report, bool capture)
        {
            var opening = Find<AlbumBookOpening>(hud, "Notebook");
            var root = (RectTransform)opening.transform;
            var settings = AlbumMagicRevealSettings.Load();
            AdditionalCanvasShaderChannels originalChannels = Get<Canvas>(hud, "canvas").additionalShaderChannels;
            opening.Sample(AlbumBookOpening.Duration * .7f);
            Canvas.ForceUpdateCanvases();
            float forwardTime = Get<float>(opening, "elapsed");
            Vector2 forwardPosition = root.anchoredPosition;
            hud.ToggleJournal();
            Check(hud.JournalOpen && hud.BlocksMovement && opening.IsClosing && opening.IsPlaying &&
                !root.GetComponent<CanvasGroup>().blocksRaycasts && Get<float>(opening, "elapsed") == forwardTime &&
                Vector2.Distance(root.anchoredPosition, forwardPosition) < .02f,
                "J reverses an opening book from its current pose while keeping movement and book clicks blocked", report);
            yield return null;
            Check(opening != null && opening.IsClosing && Get<float>(opening, "elapsed") < forwardTime &&
                Get<float>(opening, "elapsed") > 0f,
                "The actual Update loop moves backward through the existing playback timeline", report);
            Canvas.ForceUpdateCanvases();
            float reverseTime = Get<float>(opening, "elapsed");
            Vector2 reversePosition = root.anchoredPosition;
            float motionBlur = settings.motionBlurStrength;
            try
            {
                settings.motionBlurStrength = 0f;
                typeof(AlbumBookOpening).GetMethod("ApplySample", Fields).Invoke(opening, new object[] { reverseTime });
                Canvas.ForceUpdateCanvases();
                var reverseAlpha = root.GetComponentsInChildren<CanvasGroup>().ToDictionary(g => g, g => g.alpha);
                var reverseGeometry = root.GetComponentsInChildren<Graphic>().Where(g => g.name.StartsWith("Opening "))
                    .ToDictionary(g => g, g => g.name == "Opening magic stars"
                        ? VisibleParticleVertices(g.canvasRenderer.GetMesh()) : g.canvasRenderer.GetMesh().vertices);
                Check(reverseGeometry.Any(pair => pair.Key.name == "Opening magic stars" && pair.Value.Length > 0),
                    "The reverse-pose comparison contains visible star geometry", report);
                opening.Sample(reverseTime);
                Canvas.ForceUpdateCanvases();
                Check(Vector2.Distance(root.anchoredPosition, reversePosition) < .02f &&
                    reverseAlpha.All(pair => Mathf.Abs(pair.Key.alpha - pair.Value) < .001f) &&
                    reverseGeometry.Count > 0 && reverseGeometry.All(pair => SameVertices(pair.Value,
                        pair.Key.name == "Opening magic stars" ? VisibleParticleVertices(pair.Key.canvasRenderer.GetMesh())
                            : pair.Key.canvasRenderer.GetMesh().vertices)),
                    "Reverse playback matches forward sampling at the exact same time for position, content fade, paper and visible star geometry", report);
            }
            finally
            {
                settings.motionBlurStrength = motionBlur;
                opening.Sample(reverseTime);
            }
            hud.ToggleJournal();
            hud.ToggleJournal();
            Check(Find<AlbumBookOpening>(hud, "Notebook") == opening && !opening.IsClosing && opening.IsPlaying &&
                Get<float>(opening, "elapsed") == reverseTime && Vector2.Distance(root.anchoredPosition, reversePosition) < .02f &&
                (((Get<Canvas>(hud, "canvas").additionalShaderChannels & AdditionalCanvasShaderChannels.TexCoord1) != 0) == settings.enableNoiseDissolve),
                "J during closing resumes the same book and cursor without a position jump or stale noise channels", report);
            float deadline = Time.realtimeSinceStartup + AlbumBookOpening.Duration + .5f;
            while (hud.IsAlbumOpening && Time.realtimeSinceStartup < deadline) yield return null;
            Check(hud.JournalOpen && !hud.IsAlbumOpening && root.GetComponent<CanvasGroup>().blocksRaycasts,
                "Resumed opening finishes normally without a stale close callback", report);

            hud.ToggleJournal();
            Check(hud.JournalOpen && opening.IsClosing && Get<float>(opening, "elapsed") == AlbumBookOpening.Duration,
                "J starts a complete reverse playback from the fully open endpoint", report);
            float closingStarted = Time.realtimeSinceStartup;
            deadline = closingStarted + AlbumBookOpening.Duration + .5f;
            float previousTime = Get<float>(opening, "elapsed");
            int frame = 0;
            var timing = new List<string> { "frame,wall_seconds,playback_seconds,source_seconds,journal_open" };
            if (capture) Directory.CreateDirectory(Output + "/ClosingFrames");
            while (hud.JournalOpen && Time.realtimeSinceStartup < deadline)
            {
                yield return capture ? (object)new WaitForEndOfFrame() : null;
                float playback = opening != null && opening.IsClosing ? Get<float>(opening, "elapsed") : 0f;
                Require(playback <= previousTime, "Reverse playback must never increase its playback cursor.");
                previousTime = playback;
                if (!capture) continue;
                Texture2D captured = ScreenCapture.CaptureScreenshotAsTexture();
                try { File.WriteAllBytes(Output + "/ClosingFrames/frame-" + frame.ToString("D3") + ".jpg", captured.EncodeToJPG(92)); }
                finally { Object.Destroy(captured); }
                timing.Add(FormattableString.Invariant($"{frame},{Time.realtimeSinceStartup - closingStarted:R},{playback:R},{settings.MapPlaybackTime(playback):R},{hud.JournalOpen}"));
                frame++;
            }
            Check(!hud.JournalOpen && !hud.IsAlbumOpening && !hud.BlocksMovement &&
                !hud.GetComponentsInChildren<Transform>().Any(t => t.name == "Notebook" || t.name == "Opening magic aura" ||
                    t.name == "Opening magic stars" || t.name == "Opening visual") &&
                Get<Canvas>(hud, "canvas").additionalShaderChannels == originalChannels,
                "Complete reverse playback removes the book and temporary layers, restores Canvas channels and releases movement", report);
            if (capture)
            {
                File.WriteAllLines(Output + "/ClosingFrames/timing.csv", timing);
                File.WriteAllText(Output + "/ClosingFrames/capture.txt",
                    "Actual Unity Canvas frames from automatic reverse Update, with no manual Sample calls.\n" +
                    "Variable frame cadence: use timing.csv rather than assuming 30 fps. Frames 0.." + (frame - 1) + ".\n");
                report.Add("PASS: recorded " + frame + " actual reverse-playback frames with decreasing playback cursor and mapped source timing");
            }
            hud.ToggleJournal();
            opening = Find<AlbumBookOpening>(hud, "Notebook");
            opening.Sample(AlbumBookOpening.Duration * .5f);
            typeof(GameplayHud).GetMethod("CloseTop", Fields).Invoke(hud, null);
            Check(hud.JournalOpen && opening.IsClosing && hud.BlocksMovement,
                "Escape's CloseTop also reverses a partially opened book without removing it early", report);
            deadline = Time.realtimeSinceStartup + AlbumBookOpening.Duration + .5f;
            while (hud.JournalOpen && Time.realtimeSinceStartup < deadline) yield return null;
            Check(!hud.JournalOpen && !hud.BlocksMovement, "Escape finishes closing and releases movement", report);
            hud.ToggleJournal();
            Find<AlbumBookOpening>(hud, "Notebook").FinishImmediately();
        }

        private static void CheckOpeningModes(GameplayHud hud, AlbumBookOpening opening, List<string> report)
        {
            var settings = AlbumMagicRevealSettings.Load();
            string initialSettings = EditorJsonUtility.ToJson(settings);
            string settingsPath = AssetDatabase.GetAssetPath(settings);
            byte[] settingsBytes = File.ReadAllBytes(settingsPath);
            var openingRoot = (RectTransform)opening.transform;
            Graphic[] originalGraphics = openingRoot.GetComponentsInChildren<Graphic>();
            var originalMaterials = originalGraphics.ToDictionary(g => g, g => g.material);
            string[] navigation = { "Symbols tab", "Photos tab", "Close album" };
            CanvasGroup[] content = openingRoot.Cast<Transform>().Where(t => t.gameObject.activeInHierarchy &&
                t.name != "Book artwork").Select(t => t.GetComponent<CanvasGroup>()).ToArray();
            try
            {
                settings.playbackSpeed = 1f;
                settings.playbackCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);
                settings.motionBlurStrength = 0f;
                settings.enableNoiseDissolve = true;
                Shader revealShader = Resources.Load<Shader>("AlbumBook/AlbumMagicReveal");
                Check(revealShader != null && revealShader.isSupported && !ShaderUtil.ShaderHasError(revealShader),
                    "The dissolve shader compiles and is supported by the current graphics device" +
                    (revealShader == null ? " (missing)" : string.Join("", ShaderUtil.GetShaderMessages(revealShader)
                        .Select(m => "\n" + m.message + " at " + m.file + ":" + m.line))), report);
                opening.Sample(.20f);
                Canvas.ForceUpdateCanvases();
                var pageStack = openingRoot.Find("Opening visual/Opening page stack").GetComponent<Graphic>();
                Check(pageStack.canvasRenderer.GetMesh().vertexCount > 0,
                    "The closed-book stage retains visible page-stack thickness", report);
                var loosePage = openingRoot.Find("Opening visual/Opening loose left leaf").GetComponent<Graphic>();
                Mesh pageMesh = loosePage.canvasRenderer.GetMesh();
                pageMesh.RecalculateBounds();
                float carriedCenter = pageMesh.bounds.center.x;
                Check(pageMesh.bounds.min.x >= 0f,
                    "The carried paper remains inside the closed cover hinge without a left-side leak", report);
                opening.Sample(.40f);
                Canvas.ForceUpdateCanvases();
                Check(pageStack.canvasRenderer.GetMesh().vertexCount == 0,
                    "The temporary page stack has no geometry after the cover passes 90 degrees", report);
                pageMesh = loosePage.canvasRenderer.GetMesh();
                Vector3[] leafVertices = pageMesh.vertices;
                Vector2[] leafUvs = pageMesh.uv;
                float lowestUv = leafUvs.Min(uv => uv.y);
                float[] edgeX = leafVertices.Where((v, i) => Mathf.Abs(leafUvs[i].y - lowestUv) < .0001f)
                    .Select(v => v.x).ToArray();
                Check(edgeX.Length > 2 && edgeX.Zip(edgeX.Skip(1), (a, b) => b >= a - .001f).All(x => x),
                    "The near-edge-on paper projection stays monotonic without overlapping triangle bands", report);
                opening.Sample(.60f);
                Canvas.ForceUpdateCanvases();
                pageMesh = loosePage.canvasRenderer.GetMesh();
                pageMesh.RecalculateBounds();
                Check(carriedCenter > 0f && pageMesh.bounds.center.x < -100f,
                    "The independent inner sheet starts on the right and lands toward the left", report);
                opening.Sample(settings.revealStart + settings.revealDuration * .5f);
                var revealingPhoto = Find<RawImage>(hud, "Sample photo 1");
                Check(revealingPhoto.material.shader == revealShader &&
                    revealingPhoto.material.GetFloat("_Reveal") > 0f && revealingPhoto.material.GetFloat("_Reveal") < 1f &&
                    Find<Transform>(hud, "Opening magic stars") != null,
                    "Photo content uses the active dissolve material with a separate sparkle overlay", report);
                var secondPhoto = Find<RawImage>(hud, "Sample photo 2");
                Check(revealingPhoto.material != secondPhoto.material &&
                    revealingPhoto.material == Find<RawImage>(hud, "Photo frame 1").material,
                    "Each photo and its frame share the same reveal material and bounds", report);
                var revealingWords = openingRoot.GetComponentsInChildren<AlbumPaperCircleGraphic>();
                Check(revealingWords.Select(g => g.material).Distinct().Count() == 22 &&
                    revealingWords.All(g => g.GetComponentInChildren<AnimalGlyphGraphic>().material == g.material),
                    "All 22 symbol circles share their material with their own glyph", report);
                Transform aura = Find<Transform>(hud, "Opening magic aura");
                Check(aura != null && aura.gameObject.activeInHierarchy,
                    "Magic reveal has an exterior light layer separate from the source image", report);
                Graphic[] lights = aura.GetComponentsInChildren<Graphic>();
                Check(lights.Length == 29, "Each of the four photos, 22 symbols and three navigation elements owns an exterior aura", report);
                var sharedNoise = Resources.Load<Texture2D>("AlbumBook/RevealNoise");
                Check(sharedNoise != null && sharedNoise.isReadable && sharedNoise.wrapMode == TextureWrapMode.Repeat,
                    "The shared noise texture supports matching GPU and CPU repeat sampling", report);
                Material[] revealMaterials = openingRoot.GetComponentsInChildren<Graphic>()
                    .Where(g => g.material.shader == revealShader || g.material.shader.name == "Weather House/UI/Album Magic Aura")
                    .Select(g => g.material).Distinct().ToArray();
                Check(revealMaterials.All(m => m.GetTexture("_NoiseTex") == sharedNoise &&
                    m.GetFloat("_NoiseSize") == revealingPhoto.material.GetFloat("_NoiseSize") &&
                    m.GetVector("_NoiseOffset") == revealingPhoto.material.GetVector("_NoiseOffset") &&
                    m.GetFloat("_Reveal") == revealingPhoto.material.GetFloat("_Reveal")),
                    "All photos, symbols, navigation and exterior auras use one noise texture, coordinate scale, offset and progress", report);
                Check(AssetDatabase.Contains(settings), "Inspector settings are a persistent project asset", report);
                string savedSettings = EditorJsonUtility.ToJson(settings);
                try
                {
                    settings.noiseSize = 173f;
                    settings.noiseOffset = new Vector2(.31f, .72f);
                    settings.edgeWidth = .09f;
                    settings.softness = .04f;
                    settings.glowIntensity = 1.7f;
                    settings.glowSpread = 32f;
                    settings.gold = new Color(.7f,.9f,.3f);
                    settings.revealStart = .66f;
                    settings.revealDuration = 1.3f;
                    opening.Sample(1.31f);
                    Check(opening.IsPlaying && Mathf.Abs(AlbumBookOpening.Duration - 2.03f) < .001f &&
                        revealMaterials.All(m => Mathf.Abs(m.GetFloat("_Reveal")-.5f) < .001f &&
                            m.GetFloat("_NoiseSize") == 173f && m.GetFloat("_EdgeWidth") == .09f &&
                            m.GetFloat("_Softness") == .04f && m.GetFloat("_GlowIntensity") == 1.7f &&
                            m.GetFloat("_GlowSpread") == 32f &&
                            Vector4.Distance(m.GetColor("_GoldColor"),settings.gold) < .001f &&
                            Vector4.Distance(m.GetVector("_NoiseOffset"),new Vector4(.31f,.72f,0,0)) < .001f),
                        "Live Inspector changes reach every material and longer reveal duration completes after the unchanged book motion", report);
                }
                finally
                {
                    EditorJsonUtility.FromJsonOverwrite(savedSettings, settings);
                    opening.Sample(settings.revealStart + settings.revealDuration * .5f);
                }
                Canvas.ForceUpdateCanvases();
                foreach (Graphic item in openingRoot.GetComponentsInChildren<Graphic>().Where(g =>
                    g is AlbumPaperCircleGraphic || g.name.StartsWith("Sample photo ") || navigation.Contains(g.name)))
                {
                    Mesh sourceMesh = item.canvasRenderer.GetMesh();
                    Vector3[] positions = sourceMesh.vertices;
                    Vector2[] bookCoordinates = sourceMesh.uv2;
                    Require(bookCoordinates.Length == positions.Length && positions.Length > 0,
                        "The reveal needs explicit book coordinates on every content vertex: " + item.name);
                    for (int vertex = 0; vertex < positions.Length; vertex++)
                    {
                        Vector2 expected = openingRoot.InverseTransformPoint(item.transform.TransformPoint(positions[vertex]));
                        Require(Vector2.Distance(expected,bookCoordinates[vertex]) < .1f,
                            "Content coordinates must stay aligned during UI batching: " + item.name);
                    }
                }
                report.Add("PASS: all photo, symbol and navigation meshes carry their actual book-local coordinates");
                foreach (Graphic light in lights)
                {
                    Shader shader = light.material.shader;
                    Check(shader.isSupported && !ShaderUtil.ShaderHasError(shader),
                        "Exterior aura shader is supported: " + light.name, report);
                    Mesh lightMesh = light.canvasRenderer.GetMesh();
                    Vector4 sourceBounds = light.material.GetVector("_NoiseOrigin");
                    Vector2 minimum = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
                    Vector2 maximum = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
                    foreach (Vector3 vertex in lightMesh.vertices)
                    {
                        Vector2 local = openingRoot.InverseTransformPoint(light.transform.TransformPoint(vertex));
                        minimum = Vector2.Min(minimum, local);
                        maximum = Vector2.Max(maximum, local);
                    }
                    Require(minimum.x < sourceBounds.x - 30f && minimum.y < sourceBounds.y - 30f &&
                        maximum.x > sourceBounds.x + sourceBounds.z + 30f &&
                        maximum.y > sourceBounds.y + sourceBounds.w + 30f,
                        "Aura geometry must extend beyond the content on all four sides: " + light.name);
                }
                report.Add("PASS: all 29 aura meshes extend outside the content instead of clipping glow to source pixels");
                Shader particlesShader = Find<Transform>(hud, "Opening magic stars").GetComponent<Graphic>().material.shader;
                Check(particlesShader.name == "Weather House/UI/Album Magic Particles" &&
                    particlesShader.isSupported && !ShaderUtil.ShaderHasError(particlesShader),
                    "Escaping star dust uses its dedicated additive light shader", report);
                opening.FinishImmediately();
                Check(!openingRoot.GetComponentsInChildren<Graphic>().Any(g =>
                        g.material != null && g.material.name == "Album opening reveal (runtime)"),
                    "Finishing a sampled reveal also restores the original materials", report);
                Check(!aura.gameObject.activeInHierarchy &&
                    !openingRoot.GetComponentsInChildren<Graphic>().Any(g =>
                        g.material != null && (g.material.shader.name == "Weather House/UI/Album Magic Aura" ||
                        g.material.shader.name == "Weather House/UI/Album Magic Particles")),
                    "The aura and additive star dust leave no active light layer after the reveal", report);

                opening.Sample(settings.revealStart + settings.revealDuration * .5f);
                settings.enableNoiseDissolve = false;
                opening.Sample(settings.revealStart + settings.revealDuration * .5f);
                Canvas.ForceUpdateCanvases();
                Check(originalGraphics.All(g => g.material == originalMaterials[g]) &&
                    !openingRoot.GetComponentsInChildren<Transform>().Any(t => t.name == "Opening magic aura") &&
                    content.All(g => g != null && Mathf.Abs(g.alpha - .5f) < .001f),
                    "Noise-off content uses its original material and a half-opacity SmoothStep fade without an aura", report);
                Check(navigation.All(name => {
                    Transform item = openingRoot.Find(name);
                    Graphic graphic = item.GetComponent<Graphic>();
                    var coordinates = item.GetComponent("AlbumRevealCoordinates") as Behaviour;
                    return Mathf.Abs(item.GetComponent<CanvasGroup>().alpha - .5f) < .001f &&
                        graphic.material == originalMaterials[graphic] &&
                        (coordinates == null || !coordinates.isActiveAndEnabled);
                }), "Both tabs and the close button share the half-opacity fade with original materials and no active noise coordinates", report);
                var stars = Find<Graphic>(hud, "Opening magic stars");
                object[] sparkleGroups = Get<IList>(stars, "groups").Cast<object>().Where(g => Get<bool>(g, "sparkles")).ToArray();
                Check(sparkleGroups.Length == 29 && navigation.All(name => sparkleGroups.Any(g =>
                        Get<bool>(g, "navigation") && Mathf.Abs(Get<float>(g, "progress") - .5f) < .001f &&
                        Get<IList>(g, "graphics").Cast<Graphic>().Any(graphic => graphic.name == name))),
                    "The live star overlay includes all 29 content groups, including both tabs and the close button", report);
                Check(stars.canvasRenderer.GetMesh().vertexCount > 0 && stars.transform.parent == openingRoot &&
                    stars.GetComponentsInParent<CanvasGroup>().All(g => Mathf.Abs(g.alpha - 1f) < .001f),
                    "Star dust has visible geometry under the book root and is not multiplied by content fade", report);

                // Put the fade and stars inside the moving-page interval for a meaningful time-mapping check.
                settings.revealStart = .15f;
                settings.revealDuration = .5f;
                float sourceDuration = settings.TotalDuration;
                float sourceTime = sourceDuration * .25f;
                opening.Sample(sourceTime);
                Canvas.ForceUpdateCanvases();
                Vector2 position = openingRoot.anchoredPosition;
                var leaf = Find<Graphic>(hud, "Opening loose left leaf");
                Vector3[] leafPose = leaf.canvasRenderer.GetMesh().vertices;
                Mesh starMesh = stars.canvasRenderer.GetMesh();
                int rawStarVertexCount = starMesh.vertexCount;
                Vector3[] starPose = VisibleParticleVertices(starMesh);
                float linearMappedTime = settings.MapPlaybackTime(sourceTime);
                float linearStarTime = Get<float>(stars, "seconds");
                float alpha = content[0].alpha;
                Require(starPose.Length > 0, "The time-mapping comparison needs live star geometry.");
                settings.playbackSpeed = 2f;
                settings.playbackCurve = new AnimationCurve(new Keyframe(0f, 0f, 0f, 0f), new Keyframe(1f, 1f, 2f, 2f));
                float playbackTime = settings.PlaybackDuration * .5f;
                opening.Sample(playbackTime);
                Canvas.ForceUpdateCanvases();
                Vector3[] curvedLeafPose = leaf.canvasRenderer.GetMesh().vertices;
                Mesh curvedStarMesh = stars.canvasRenderer.GetMesh();
                Vector3[] curvedStarPose = VisibleParticleVertices(curvedStarMesh);
                report.Add($"DIAGNOSTIC: curve duration actual={AlbumBookOpening.Duration:R}, expected={sourceDuration * .5f:R}; " +
                    $"playback={playbackTime:R}, mapped linear={linearMappedTime:R}, curved={settings.MapPlaybackTime(playbackTime):R}, target={sourceTime:R}; " +
                    $"star time linear={linearStarTime:R}, curved={Get<float>(stars, "seconds"):R}");
                report.Add($"DIAGNOSTIC: root position linear=({position.x:R},{position.y:R}), " +
                    $"curved=({openingRoot.anchoredPosition.x:R},{openingRoot.anchoredPosition.y:R}), " +
                    $"distance={Vector2.Distance(position, openingRoot.anchoredPosition):R}; " +
                    $"content alpha linear={alpha:R}, curved={content[0].alpha:R}, difference={Mathf.Abs(content[0].alpha - alpha):R}");
                report.Add("DIAGNOSTIC: leaf " + VertexDifference(leafPose, curvedLeafPose) + "; visible stars " + VertexDifference(starPose, curvedStarPose) +
                    $"; raw star vertices={rawStarVertexCount}/{curvedStarMesh.vertexCount}");
                Check(Mathf.Abs(AlbumBookOpening.Duration - sourceDuration * .5f) < .001f &&
                    Mathf.Abs(settings.MapPlaybackTime(playbackTime) - sourceTime) < .001f &&
                    Vector2.Distance(position, openingRoot.anchoredPosition) < .02f &&
                    SameVertices(leafPose, curvedLeafPose) &&
                    SameVertices(starPose, curvedStarPose) &&
                    Mathf.Abs(content[0].alpha - alpha) < .001f,
                    "One global curve and 2x speed preserve the same book, loose-page, fade and star pose at equal source time", report);
                opening.Sample(settings.PlaybackDuration * .5f);
                Canvas.ForceUpdateCanvases();
                Vector3[] curvedHalf = leaf.canvasRenderer.GetMesh().vertices;
                settings.playbackCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);
                opening.Sample(settings.PlaybackDuration * .5f);
                Canvas.ForceUpdateCanvases();
                Check(!SameVertices(curvedHalf, leaf.canvasRenderer.GetMesh().vertices),
                    "A nonlinear global curve changes the actual rendered page pose at half playback time", report);
                opening.Sample(AlbumBookOpening.Duration);
                Check(!opening.IsPlaying && openingRoot.anchoredPosition.sqrMagnitude < .001f &&
                    content.All(g => Mathf.Abs(g.alpha - 1f) < .001f) &&
                    originalGraphics.All(g => g.material == originalMaterials[g]) &&
                    !openingRoot.GetComponentsInChildren<Transform>().Any(t => t.name == "Opening magic stars" || t.name == "Opening magic aura"),
                    "The global-curve endpoint restores full content, original materials and removes temporary light layers", report);
                CheckMotionBlur(opening, report);
            }
            finally
            {
                EditorJsonUtility.FromJsonOverwrite(initialSettings, settings);
                opening.FinishImmediately();
            }
            Check(EditorJsonUtility.ToJson(settings) == initialSettings && File.ReadAllBytes(settingsPath).SequenceEqual(settingsBytes),
                "Synchronous mode and curve checks preserve all user Inspector values and the settings asset bytes", report);
        }

        private static void CheckMotionBlur(AlbumBookOpening opening, List<string> report)
        {
            var settings = AlbumMagicRevealSettings.Load();
            string saved = EditorJsonUtility.ToJson(settings);
            try
            {
                settings.playbackSpeed = 1f;
                settings.playbackCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);
                settings.motionBlurStrength = 0f;
                opening.Sample(.4f);
                Canvas.ForceUpdateCanvases();
                Graphic cover = opening.GetComponentsInChildren<Graphic>().First(g => g.name == "Opening cover");
                Graphic stars = opening.GetComponentsInChildren<Graphic>().First(g => g.name == "Opening magic stars");
                UIVertex[] clear = TriangleVertices(cover), clearStars = TriangleVertices(stars);
                settings.motionBlurStrength = .65f;
                opening.Sample(.4f);
                Canvas.ForceUpdateCanvases();
                UIVertex[] forward = TriangleVertices(cover);
                Check(forward.Length == clear.Length * 7 && clear.Length > 0,
                    "Moving book cover has six short motion-blur copies plus the original image", report);
                Check(SameUiVertices(clear, forward.Skip(clear.Length * 6).ToArray()),
                    "Motion blur draws the exact original geometry, UVs and opacity last", report);
                float maximum = 0f;
                for (int vertex = 0; vertex < clear.Length; vertex++)
                {
                    int alpha = 0;
                    for (int tap = 0; tap < 6; tap++)
                    {
                        UIVertex ghost = forward[tap * clear.Length + vertex];
                        maximum = Mathf.Max(maximum, Vector3.Distance(ghost.position, clear[vertex].position));
                        alpha += ghost.color.a;
                    }
                    Require(alpha < clear[vertex].color.a * .1f, "Combined motion trail opacity must remain below ten percent.");
                }
                Check(maximum > .15f && maximum <= 6f * settings.motionBlurStrength + .0001f,
                    "The visible motion trail stays within six UI pixels times the selected strength", report);
                string[] intended = { "Opening right page", "Opening page stack", "Opening cover", "Opening cover edge", "Opening loose left leaf" };
                Check(new HashSet<string>(opening.GetComponentsInChildren<Graphic>()
                        .Where(g => g.GetComponent("AlbumOpeningMotionBlur") != null).Select(g => g.name)).SetEquals(intended) &&
                    SameUiVertices(clearStars, TriangleVertices(stars)),
                    "Only the five solid book layers receive blur; shadows, content, aura and stars retain their geometry", report);
                opening.PlayReverse(null);
                Canvas.ForceUpdateCanvases();
                UIVertex[] reverse = TriangleVertices(cover);
                int tip = Enumerable.Range(0, clear.Length).OrderByDescending(i =>
                    Vector3.Distance(forward[i].position, clear[i].position)).First();
                Check(reverse.Length == forward.Length &&
                    Vector3.Dot(forward[tip].position - clear[tip].position, reverse[tip].position - clear[tip].position) < 0f &&
                    SameUiVertices(clearStars, TriangleVertices(stars)),
                    "Reverse playback changes the trail direction while leaving the same-time stars unchanged", report);
                settings.motionBlurStrength = 0f;
                opening.Sample(.4f);
                Canvas.ForceUpdateCanvases();
                Check(SameUiVertices(clear, TriangleVertices(cover)), "Zero blur strength restores the original moving mesh", report);
                opening.Sample(0f);
                Canvas.ForceUpdateCanvases();
                UIVertex[] stationary = TriangleVertices(cover);
                settings.motionBlurStrength = .65f;
                opening.Sample(0f);
                Canvas.ForceUpdateCanvases();
                Check(SameUiVertices(stationary, TriangleVertices(cover)), "A stationary endpoint has no motion trail", report);
                opening.FinishImmediately();
                Check(!opening.GetComponentsInChildren<Graphic>().Any(g => g.GetComponent("AlbumOpeningMotionBlur") != null),
                    "Finishing removes every temporary motion-blur component", report);
            }
            finally
            {
                EditorJsonUtility.FromJsonOverwrite(saved, settings);
                opening.FinishImmediately();
            }
        }

        private static UIVertex[] TriangleVertices(Graphic graphic)
        {
            var vertices = new List<UIVertex>();
            using (var mesh = new VertexHelper(graphic.canvasRenderer.GetMesh())) mesh.GetUIVertexStream(vertices);
            return vertices.ToArray();
        }

        private static bool SameUiVertices(UIVertex[] first, UIVertex[] second)
        {
            return first.Length == second.Length && first.Zip(second, (a, b) =>
                a.position.Equals(b.position) && a.uv0.Equals(b.uv0) && a.color.Equals(b.color)).All(equal => equal);
        }

        private static bool SameVertices(Vector3[] first, Vector3[] second)
        {
            return first.Length == second.Length && first.Zip(second, (a, b) => Vector3.Distance(a, b)).All(d => d < .02f);
        }

        private static Vector3[] VisibleParticleVertices(Mesh mesh)
        {
            Vector3[] vertices = mesh.vertices;
            Color32[] colors = mesh.colors32;
            const int verticesPerParticle = 18; // Eight-point halo and core, each with a center vertex.
            Require(vertices.Length % verticesPerParticle == 0 && colors.Length == vertices.Length,
                "Star geometry must contain complete particles with vertex colors.");
            var visible = new List<Vector3>();
            for (int start = 0; start < vertices.Length; start += verticesPerParticle)
            {
                // Near birth/death, float rounding can emit an extra particle whose actual GPU alpha is entirely zero.
                bool hasVisibleVertex = false;
                for (int i = 0; i < verticesPerParticle; i++) hasVisibleVertex |= colors[start + i].a != 0;
                if (hasVisibleVertex)
                    for (int i = 0; i < verticesPerParticle; i++) visible.Add(vertices[start + i]);
            }
            return visible.ToArray();
        }

        private static string VertexDifference(Vector3[] first, Vector3[] second)
        {
            float[] distances = first.Zip(second, (a, b) => Vector3.Distance(a, b)).ToArray();
            int mismatch = Array.FindIndex(distances, d => d >= .02f);
            return $"counts={first.Length}/{second.Length}, maxDistance={distances.DefaultIfEmpty(0f).Max():R}, " +
                $"firstMismatch={mismatch}, same={SameVertices(first, second)}";
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
