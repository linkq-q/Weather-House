using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using WeatherHousePrototype;

namespace WeatherHouse.Life.Editor
{
    // Invoked by the parent Play review; never starts Unity or alters the saved scene.
    public static class LifeUiPlayChecks
    {
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

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
            if (failure != null && life)
            {
                var hud = life.GetComponent<GameplayHud>();
                if (hud) { life.GetComponent<LifeJournal3D>()?.Cancel(); Call(hud, "CompleteJournalClose"); }
                life.SetGestureInput(false);
            }
            completed?.Invoke((failure == null ? "PASS" : "FAIL") + " UI lifecycle\n" + string.Join("\n", checks) +
                (failure == null ? "" : "\n" + failure));
        }

        static IEnumerator Steps(LifeSession life, List<string> checks)
        {
            Require(Application.isPlaying && life && !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WEATHERHOUSE_LIFE_REVIEW_SAVE")),
                "UI checks require the existing isolated Play review", checks);
            var hud = life.GetComponent<GameplayHud>();
            Require(hud && !hud.JournalOpen && !hud.CameraMode && !hud.IsDialogueOpen, "UI starts from an idle gameplay view", checks);
            IEnumerator bubbles = CheckBubbleReuse(life, hud, checks);
            try { while (bubbles.MoveNext()) yield return bubbles.Current; }
            finally { (bubbles as IDisposable)?.Dispose(); }
            Set(hud, "albumGesturesSelected", false); Set(hud, "albumPhotosSelected", false);
            int selectedGlyphPage = Get<int>(hud, "glyphPage"), selectedPhotoPage = Get<int>(hud, "page");
            int captureStart = life.GetComponent<LifeJournal3D>() ? life.GetComponent<LifeJournal3D>().PageCaptureCount : 0;
            int introStart = life.GetComponent<LifeJournal3D>() ? life.GetComponent<LifeJournal3D>().IntroCaptureCount : 0;
            hud.ToggleJournal();
            var book = life.GetComponent<LifeJournal3D>();
            int openingCaptures = book.PageCaptureCount, openingDecodes = hud.PhotoDecodeCount;
            int openingBookInstance = Get<RectTransform>(hud, "albumRoot").GetInstanceID();
            Color32[] targetPixels = ReadCapture(Get<RenderTexture>(book, "before"));
            Require(openingCaptures - captureStart == 2 && book.IntroCaptureCount - introStart == 1,
                "Opening captures one introductory spread and one selected spread before its first visible frame", checks);
            CheckIntroPixels(book, checks);
            CheckOpeningBindings(book, checks);
            Require(book && book.Available && book.IsAnimating, "J starts the physical opening", checks);
            Require(book.PresentationOpacity == 0 && Mathf.Abs(book.ViewingAngle - 58f) < .01f,
                "The closed book begins transparent at an oblique view", checks);
            // Exercise an actual stalled editor/player frame, as a cold first capture can do.
            // This deliberate delay belongs only to the isolated UI regression.
            System.Threading.Thread.Sleep(450);
            float deadline = Time.realtimeSinceStartup + 5;
            int observedFrame = -1;
            while (book.AnimationProgress < .18f && Time.realtimeSinceStartup < deadline)
            {
                int frame = Get<int>(book, "presentationFrames");
                if (frame != observedFrame) { checks.Add("TRACE opening " + PresentationState(book, hud)); observedFrame = frame; }
                yield return null;
            }
            checks.Add("TRACE opening checkpoint " + PresentationState(book, hud));
            Require(Get<float>(book, "maximumRawStep") >= .35f,
                "The opening was exercised through a real frame longer than the fade window", checks);
            Require(book.PresentationOpacity > 0 && book.PresentationOpacity < 1 && book.ViewingAngle > 58 && book.ViewingAngle < 70 &&
                !Get<RectTransform>(hud, "albumRoot").GetComponent<CanvasGroup>().blocksRaycasts,
                "Opening fades the entire book while its view rises and all input stays blocked; " + PresentationState(book, hud), checks);
            CaptureUiFrame("ui-book-oblique-opening.png");
            float alpha = book.PresentationOpacity, angle = book.ViewingAngle;
            hud.ToggleJournal();
            Require(book.IsClosing, "A fast second J reverses the opening", checks);
            Require(Mathf.Approximately(alpha, book.PresentationOpacity) && Mathf.Approximately(angle, book.ViewingAngle),
                "Reversing the opening preserves opacity and view angle without a jump", checks);
            hud.ToggleJournal();
            Require(!book.IsClosing && book.IsAnimating, "A fast third J resumes opening", checks);
            Require(Mathf.Approximately(alpha, book.PresentationOpacity) && Mathf.Approximately(angle, book.ViewingAngle),
                "Resuming opening also preserves opacity and view angle", checks);
            deadline = Time.realtimeSinceStartup + 15;
            int seenTurn = -1, turnCaptureCount = 0;
            float browseStarted = -1;
            var openingScreenshots = new HashSet<int>();
            var landingOrder = new List<int>(); bool coverFanSeen = false, independentFanSeen = false;
            int[] openingSkins = book.OpeningFan.Pages.Select(p => p.GetInstanceID()).ToArray();
            while (book.IsAnimating && Time.realtimeSinceStartup < deadline)
            {
                if (book.MotionName == "Opening" && book.AnimationProgress > .88f && !coverFanSeen)
                {
                    CheckIndependentFan(book, checks); coverFanSeen = true;
                    CaptureUiFrame("ui-book-opening-cover-fan.png");
                }
                if (book.IsOpeningBrowse)
                {
                    if (seenTurn != book.OpeningTurnsCompleted)
                    {
                        seenTurn = book.OpeningTurnsCompleted; turnCaptureCount = book.PageCaptureCount;
                        landingOrder.Add(seenTurn);
                        if (browseStarted < 0) browseStarted = Time.realtimeSinceStartup;
                        CheckOpeningSurfaces(hud, book, checks);
                        // A queued close may be cancelled while the flexible leaf continues moving.
                        if (seenTurn == 0)
                        {
                            hud.ToggleJournal();
                            Require(book.IsClosing && book.MotionName == "Turning", "J during the first automatic leaf queues closing without changing the pose", checks);
                            hud.ToggleJournal();
                            Require(!book.IsClosing && book.IsOpeningBrowse, "A second J safely resumes the automatic browse", checks);
                        }
                    }
                    if (book.PageCaptureCount != turnCaptureCount)
                        Require(false, "A moving leaf unexpectedly recaptured its page content at progress " + book.AnimationProgress.ToString("F2"), checks);
                    if (!independentFanSeen && book.AnimationProgress >= .22f)
                    {
                        CheckIndependentFan(book, checks); independentFanSeen = true;
                    }
                    for (int frame = 0; frame < 4; frame++)
                        if (book.AnimationProgress >= .12f + frame * .25f && openingScreenshots.Add(frame))
                        { CheckTabVisualAlpha(hud, checks); CaptureUiFrame("ui-book-opening-fan-" + (frame + 1) + ".png"); }
                }
                yield return null;
            }
            Require(hud.JournalOpen && !book.IsAnimating && Get<RectTransform>(hud, "albumRoot").GetComponent<CanvasGroup>().interactable,
                "Reversed opening restores book interaction", checks);
            Require(coverFanSeen && independentFanSeen && book.OpeningTurnsCompleted == 4 && openingScreenshots.Count == 4 &&
                landingOrder.SequenceEqual(new[] { 0, 1, 2, 3 }) && Get<int>(hud, "glyphPage") == selectedGlyphPage && Get<int>(hud, "page") == selectedPhotoPage,
                "Four independent leaves fan out before full opening, land in order, and end on the selected spread", checks);
            Require(book.OpeningBrowseTime >= .6f && book.OpeningBrowseTime <= .9f,
                "The overlapping opening fan uses " + book.OpeningBrowseTime.ToString("F3") + " s of presented animation; measured real time=" + (Time.realtimeSinceStartup - browseStarted).ToString("F3") + " s", checks);
            Require(book.PageCaptureCount == openingCaptures && hud.PhotoDecodeCount == openingDecodes && Get<RectTransform>(hud, "albumRoot").GetInstanceID() == openingBookInstance,
                "Opening never rebuilds content, decodes preview photos or captures another page", checks);
            Require(targetPixels.SequenceEqual(ReadCapture(Get<RenderTexture>(book, "before"))) &&
                Get<Renderer>(book, "pageLeft").sharedMaterial.GetTexture("_BaseMap") == Get<RenderTexture>(book, "before") &&
                Get<Renderer>(book, "pageRight").sharedMaterial.GetTexture("_BaseMap") == Get<RenderTexture>(book, "before"),
                "The final two pages display the unchanged originally selected snapshot", checks);
            CaptureUiFrame("ui-book-opening-target.png");
            yield return null; yield return null;
            Require(Mathf.Approximately(book.PresentationOpacity, 1) && book.ViewingAngle >= 68f && book.ViewingAngle <= 75f,
                "Settled reading keeps a clear slightly oblique fully visible book", checks);
            CaptureUiFrame("ui-book-oblique-reading.png");
            yield return null; yield return null;
            CheckReadingSurface(hud, book, checks);
            IEnumerator wordFeedback = CheckWordFeedback(life, hud, book, checks);
            try { while (wordFeedback.MoveNext()) yield return wordFeedback.Current; }
            finally { (wordFeedback as IDisposable)?.Dispose(); }
            CheckMotionEnvelope(hud, book, checks);
            CheckOpeningFanGeometry(book, checks);
            IEnumerator captureTints = CheckSnapshotInteractionTint(hud, book, checks);
            try { while (captureTints.MoveNext()) yield return captureTints.Current; }
            finally { (captureTints as IDisposable)?.Dispose(); }
            IEnumerator warmPaths = CheckPhotoWarmPaths(life, hud, checks);
            try { while (warmPaths.MoveNext()) yield return warmPaths.Current; }
            finally { (warmPaths as IDisposable)?.Dispose(); }
            IEnumerator introTargets = CheckIntroTargets(life, hud, book, checks);
            try { while (introTargets.MoveNext()) yield return introTargets.Current; }
            finally { (introTargets as IDisposable)?.Dispose(); }

            Call(hud, "RebuildJournalPage", 1);
            Require(book.MotionName == "Turning", "A page change starts physical turning", checks);
            CheckTurningSurfaces(hud, book, checks);
            Call(hud, "CloseTop"); // Same route as Escape; keeps the page motion continuous.
            Require(book.MotionName == "Turning" && book.IsClosing, "Escape during a turn queues closing without snapping the paper", checks);
            deadline = Time.realtimeSinceStartup + 15;
            while (book.MotionName != "Closing" && Time.realtimeSinceStartup < deadline) yield return null;
            while (book.MotionName == "Closing" && book.AnimationProgress < .80f && Time.realtimeSinceStartup < deadline) yield return null;
            checks.Add("TRACE closing checkpoint " + PresentationState(book, hud));
            Require(book.MotionName == "Closing" && book.PresentationOpacity > 0 && book.PresentationOpacity < 1 &&
                !Get<RectTransform>(hud, "albumRoot").GetComponent<CanvasGroup>().interactable,
                "Closing fades the whole book and leaves its controls disabled", checks);
            CaptureUiFrame("ui-book-oblique-closing.png");
            while (hud.JournalOpen && Time.realtimeSinceStartup < deadline) yield return null;
            Require(!hud.JournalOpen && !hud.BlocksMovement && !life.GestureInputActive, "Queued close releases gameplay input", checks);

            hud.ToggleJournal();
            deadline = Time.realtimeSinceStartup + 15;
            while (book.IsAnimating && Time.realtimeSinceStartup < deadline) yield return null;
            Require(!book.IsAnimating, "Journal reopens after queued close", checks);
            Set(hud, "albumGesturesSelected", true); Call(hud, "RebuildModal");
            yield return null; yield return null;
            int materials = OwnedMaterials(), targets = JournalTargets();
            RenderTexture view = book.ViewTexture;
            for (int i = 0; i < 50; i++) { Call(hud, "RebuildModal"); yield return null; }
            yield return null; yield return null;
            Require(OwnedMaterials() == materials, "Fifty gesture-page rebuilds leave no owned material growth after deferred destruction", checks);
            Require(targets == 3 && JournalTargets() == targets && book.ViewTexture == view, "Fifty rebuilds reuse the same three book render targets", checks);
            Require(openingSkins.SequenceEqual(book.OpeningFan.Pages.Select(p => p.GetInstanceID())) && book.OpeningFan.Pages.All(p => !p.enabled),
                "Repeated journal openings and fifty rebuilds reuse the same four hidden opening skins", checks);
            Require(life.GetComponentsInChildren<Transform>(true).Count(t => t.name == "Journal rendering studio") == 1,
                "Fifty rebuilds retain exactly one rendering studio", checks);

            var snapshot = new LifeContextSnapshot { speakerName = "拍摄时的松鼠", listenerName = "拍摄时的同伴", locationName = "树旁的水碗",
                speakerPosition = new Vector3(123, 4, 567), listenerPosition = new Vector3(125, 4, 567), anchorPosition = new Vector3(124, 4, 570),
                objectState = "碗里留着水", weatherState = "自然天气" };
            var restored = JsonUtility.FromJson<LifeContextSnapshot>(JsonUtility.ToJson(snapshot.Copy()));
            Require(restored.locationName == snapshot.locationName && restored.anchorPosition == snapshot.anchorPosition,
                "Readable place names and original coordinates both survive snapshot serialization", checks);
            string missingId = Guid.NewGuid().ToString("N");
            var missing = new ObservationRecord { id = missingId, glyphIds = Array.Empty<string>(),
                clipFolder = "MotionPhotos/" + missingId, frameCount = 2, framesPerSecond = 2,
                capturedUtcTicks = DateTime.UtcNow.Ticks, context = restored };
            for (int visit = 0; visit < 2; visit++)
            {
                Set(hud, "expandedPhoto", missing); Call(hud, "RebuildModal");
                var image = Get<RawImage>(hud, "expandedImage");
                Require(image && image.texture == Resources.Load<Texture2D>("AlbumBook/PhotoMissing"), "Missing photo uses its readable placeholder on visit " + (visit + 1), checks);
                string context = Get<RectTransform>(hud, "detailRoot").GetComponentsInChildren<Text>().First(t => t.name == "Original photo context").text;
                Require(context.Contains(restored.locationName) && context.Contains(restored.speakerName) && !context.Contains("123") && !context.Contains("567"),
                    "Photo context displays captured names and place without XYZ coordinates", checks);
                CheckDetailSizes(hud, checks);
                Get<RectTransform>(hud, "detailRoot").GetComponentsInChildren<Button>().First(b => b.name == "Play motion photo").onClick.Invoke();
                yield return new WaitForSecondsRealtime(.7f);
                Require(!Get<bool>(hud, "playingPhoto"), "A missing playback frame stops playback cleanly", checks);
                Call(hud, "CloseTop");
                Require(hud.JournalOpen && !book.IsAnimating && Get<ObservationRecord>(hud, "expandedPhoto") == null,
                    "Closing the photo restores the readable book on visit " + (visit + 1), checks);
            }
            hud.ToggleJournal();
            deadline = Time.realtimeSinceStartup + 5;
            while (hud.JournalOpen && Time.realtimeSinceStartup < deadline) yield return null;
            yield return null; yield return null;
            Require(!hud.JournalOpen && !hud.BlocksMovement && !life.GestureInputActive && !Get<bool>(hud, "playingPhoto"),
                "UI review ends with movement, camera and gesture input released", checks);
            Require(hud.CachedPhotoTextureCount == 0 && !Resources.FindObjectsOfTypeAll<Texture2D>().Any(t => t && t.name.StartsWith("Journal photo ", StringComparison.Ordinal)),
                "Closing J releases all owned photo textures after deferred destruction", checks);
            checks.Add(LifePersistencePlayChecks.Run(life));
        }

        static IEnumerator CheckPhotoWarmPaths(LifeSession life, GameplayHud hud, List<string> checks)
        {
            // Real valid PNG files in the already-isolated review save. No production records are changed.
            using (var fixture = new PhotoFixture())
            {
                var observations = life.Gameplay.Notebook.observations;
                ObservationRecord[] saved = observations.ToArray();
                try
                {
                    observations.Clear(); observations.AddRange(fixture.records);
                    Set(hud, "page", 0); Set(hud, "selectedGlyph", null); Set(hud, "albumGesturesSelected", false);
                    int decoded = hud.PhotoDecodeCount;
                    Call(hud, "RebuildModal");
                    Require(hud.PhotoDecodeCount - decoded == 4, "Initial four-photo page decodes exactly four real covers", checks);
                    RectTransform album = Get<RectTransform>(hud, "albumRoot"); int bookId = album.GetInstanceID();
                    Button word = album.GetComponentsInChildren<Button>(true).First(b => b.name.StartsWith("Word ", StringComparison.Ordinal));
                    var elapsed = System.Diagnostics.Stopwatch.StartNew();
                    decoded = hud.PhotoDecodeCount;
                    for (int i = 0; i < 20; i++)
                    {
                        word.onClick.Invoke();
                        Require(Get<RectTransform>(hud, "albumRoot").GetInstanceID() == bookId, "Word detail keeps the book instance, cycle " + (i + 1), checks);
                        Call(hud, "CloseTop");
                    }
                    elapsed.Stop();
                    Require(hud.PhotoDecodeCount == decoded, "Twenty real word-button open/close cycles perform zero further PNG decodes (" + elapsed.ElapsedMilliseconds + " ms synchronous work)", checks);
                    Button photo = album.GetComponentsInChildren<Button>(true).First(b => b.name == "Photo " + fixture.records[0].id);
                    decoded = hud.PhotoDecodeCount; elapsed.Restart();
                    for (int visit = 0; visit < 20; visit++)
                    {
                        photo.onClick.Invoke();
                        Require(Get<RectTransform>(hud, "albumRoot").GetInstanceID() == bookId && !album.gameObject.activeSelf,
                            "Photo detail retains and temporarily hides the same book, cycle " + (visit + 1), checks);
                        Get<RectTransform>(hud, "detailRoot").GetComponentsInChildren<Button>().First(b => b.name == "Play motion photo").onClick.Invoke();
                        // Execute the normal playback update at each due instant, without waiting 60 seconds.
                        for (int frame = 1; frame < 18; frame++) { Set(hud, "nextFrameAt", 0f); Call(hud, "UpdatePlayback"); }
                        Require(Get<int>(hud, "currentFrame") == 17, "Playback reaches its last actual frame, cycle " + (visit + 1), checks);
                        Call(hud, "CloseTop");
                    }
                    elapsed.Stop();
                    Require(hud.PhotoDecodeCount - decoded == 17, "Twenty eighteen-frame replays decode only the seventeen frames not already cached as a cover (" + elapsed.ElapsedMilliseconds + " ms synchronous work)", checks);
                    yield return null;
                    // Nine distinct covers exceed the capacity; changing actual pages exercises eviction.
                    Set(hud, "selectedGlyph", null);
                    for (int page = 1; page < 3; page++)
                    {
                        Set(hud, "page", page); Call(hud, "RebuildModal"); yield return null;
                        Require(hud.CachedPhotoTextureCount <= 26, "Cover/clip storage stays within eight covers plus eighteen frames on page " + page, checks);
                    }
                }
                finally
                {
                    observations.Clear(); observations.AddRange(saved);
                    Call(hud, "HideDetails"); Call(hud, "ReleasePhotoTextures");
                    Set(hud, "page", 0); Set(hud, "selectedGlyph", null); Call(hud, "RebuildModal");
                }
            }
        }

        static void CheckIntroPixels(LifeJournal3D book, List<string> checks)
        {
            Transform intro = Get<Canvas>(book, "captureCanvas").transform.Find("Journal opening spread");
            Require(intro && intro.GetComponentsInChildren<RawImage>(true).Count(p => p.name.StartsWith("Intro empty photo ") &&
                p.texture == Resources.Load<Texture2D>("AlbumBook/PhotoEmpty")) == 4 &&
                intro.GetComponentsInChildren<AlbumPaperCircleGraphic>(true).Length == AlbumBookView.GlyphsPerPage,
                "The introductory spread reuses four real empty-photo assets and the original glyph grid", checks);
            RenderTexture target = Get<RenderTexture>(book, "after"); Color32[] pixels = ReadCapture(target);
            foreach (Rect slot in new[] { new Rect(301,159,231,293), new Rect(577,173,207,281), new Rect(291,488,239,286), new Rect(568,492,219,291), new Rect(900,170,440,560) })
            {
                int ink = 0;
                for (int y = (int)slot.yMin; y < slot.yMax; y++)
                    for (int x = (int)slot.xMin; x < slot.xMax; x++)
                    {
                        Color32 pixel = pixels[(target.height - 1 - y) * target.width + x];
                        if (pixel.a > 200 && Mathf.Min(pixel.r, Mathf.Min(pixel.g, pixel.b)) < 170) ink++;
                    }
                Require(ink > 100, "The actual introductory capture contains visible artwork in " + slot + " (ink pixels=" + ink + ")", checks);
            }
        }

        static IEnumerator CheckIntroTargets(LifeSession life, GameplayHud hud, LifeJournal3D book, List<string> checks)
        {
            var observations = life.Gameplay.Notebook.observations; var saved = new List<ObservationRecord>(observations);
            bool photos = Get<bool>(hud, "albumPhotosSelected"), gestures = Get<bool>(hud, "albumGesturesSelected"), tasks = Get<bool>(hud, "albumTasksSelected");
            int photoPage = Get<int>(hud, "page"), glyphPage = Get<int>(hud, "glyphPage");
            string selected = Get<string>(hud, "selectedGlyph"); Color32[] stableIntro = null;
            using (var fixture = new PhotoFixture())
            {
                try
                {
                    observations.Clear(); observations.AddRange(fixture.records);
                    foreach (string tab in new[] { "photos", "gestures", "tasks" })
                    {
                        book.Cancel(); Call(hud, "CompleteJournalClose");
                        Set(hud, "albumPhotosSelected", tab == "photos"); Set(hud, "albumGesturesSelected", tab == "gestures"); Set(hud, "albumTasksSelected", tab == "tasks");
                        Set(hud, "selectedGlyph", null); Set(hud, "page", 0); Set(hud, "glyphPage", 1);
                        int captures = book.PageCaptureCount, intros = book.IntroCaptureCount;
                        string notebook = JsonUtility.ToJson(life.Gameplay.Notebook), progress = JsonUtility.ToJson(life.Progress);
                        hud.ToggleJournal(); CheckIntroPixels(book, checks); CheckOpeningBindings(book, checks);
                        Require(JsonUtility.ToJson(life.Gameplay.Notebook) == notebook && JsonUtility.ToJson(life.Progress) == progress,
                            tab + " intro construction leaves notebook and life progress unchanged", checks);
                        int decoded = hud.PhotoDecodeCount, instance = Get<RectTransform>(hud, "albumRoot").GetInstanceID();
                        Color32[] intro = ReadCapture(Get<RenderTexture>(book, "after")), target = ReadCapture(Get<RenderTexture>(book, "before"));
                        Require(book.PageCaptureCount == captures + 2 && book.IntroCaptureCount == intros + 1 && book.PresentationOpacity == 0,
                            tab + " prepares one intro and one target capture while the book is still hidden", checks);
                        Require(!intro.SequenceEqual(target), tab + " has a real target distinct from the introductory layout", checks);
                        if (stableIntro == null) stableIntro = intro;
                        else Require(stableIntro.SequenceEqual(intro), tab + " reuses exactly the same introductory pixels regardless of the selected target tab", checks);
                        int phase = -1; float deadline = Time.realtimeSinceStartup + 15;
                        while (book.IsAnimating && Time.realtimeSinceStartup < deadline)
                        {
                            int next = book.IsOpeningBrowse ? (book.OpeningFan.RevealingTarget ? 2 : 1) : 0;
                            if (phase != next)
                            {
                                phase = next; CheckOpeningBindings(book, checks);
                                if (next > 0) CaptureUiFrame("ui-book-intro-" + tab + (next == 1 ? "-before-target.png" : "-reveal.png"));
                            }
                            if (book.PageCaptureCount != captures + 2 || hud.PhotoDecodeCount != decoded)
                                Require(false, tab + " recaptured or decoded content during the opening", checks);
                            yield return null;
                        }
                        Require(!book.IsAnimating && phase == 2 && Get<RectTransform>(hud, "albumRoot").GetInstanceID() == instance && Get<int>(hud, "glyphPage") == 1,
                            tab + " reaches its target without rebuilding the book or changing the selected glyph page", checks);
                        Require(intro.SequenceEqual(ReadCapture(Get<RenderTexture>(book, "after"))) && target.SequenceEqual(ReadCapture(Get<RenderTexture>(book, "before"))) &&
                            Get<Renderer>(book, "pageLeft").sharedMaterial.GetTexture("_BaseMap") == Get<RenderTexture>(book, "before") &&
                            Get<Renderer>(book, "pageRight").sharedMaterial.GetTexture("_BaseMap") == Get<RenderTexture>(book, "before"),
                            tab + " keeps both snapshots stable and lands directly on the selected target without a blank intermediate spread", checks);
                        CaptureUiFrame("ui-book-intro-" + tab + "-target.png"); yield return null;
                    }
                }
                finally
                {
                    book.Cancel(); Call(hud, "CompleteJournalClose"); observations.Clear(); observations.AddRange(saved);
                    Set(hud, "albumPhotosSelected", photos); Set(hud, "albumGesturesSelected", gestures); Set(hud, "albumTasksSelected", tasks);
                    Set(hud, "page", photoPage); Set(hud, "glyphPage", glyphPage); Set(hud, "selectedGlyph", selected);
                    hud.ToggleJournal();
                }
                float deadlineRestore = Time.realtimeSinceStartup + 15;
                while (book.IsAnimating && Time.realtimeSinceStartup < deadlineRestore) yield return null;
                Require(!book.IsAnimating, "Intro target checks restore the original readable book", checks);
            }
        }

        static IEnumerator CheckWordFeedback(LifeSession life, GameplayHud hud, LifeJournal3D book, List<string> checks)
        {
            string notebook = JsonUtility.ToJson(life.Gameplay.Notebook), selected = Get<string>(hud, "selectedGlyph");
            int page = Get<int>(hud, "glyphPage");
            GlyphDefinition[] definitions = life.Gameplay.Catalog.glyphs.Take(3).ToArray();
            try
            {
                // Only in-memory evidence is arranged; no test image or player save is created.
                for (int i = 0; i < definitions.Length; i++)
                {
                    GlyphKnowledge word = life.Gameplay.Notebook.GetWord(definitions[i].id);
                    word.confirmed = i == 2; word.encountered = i != 0;
                }
                life.Gameplay.Notebook.observations.RemoveAll(photo => photo?.glyphIds != null &&
                    (photo.glyphIds.Contains(definitions[0].id) || photo.glyphIds.Contains(definitions[1].id)));
                life.Gameplay.Notebook.observations.Add(new ObservationRecord { id = "ui-word-evidence", glyphIds = new[] { definitions[1].id } });
                Set(hud, "glyphPage", 0); Set(hud, "selectedGlyph", "ui-no-photo-evidence"); Call(hud, "RebuildModal");
                yield return null; yield return new WaitForEndOfFrame();
                RectTransform album = Get<RectTransform>(hud, "albumRoot");
                LifeJournalWord[] words = definitions.Select(definition => album.GetComponentsInChildren<LifeJournalWord>(true).First(word => word.GlyphId == definition.id)).ToArray();
                Color32[] colors = { new Color32(207, 215, 188, 255), new Color32(195, 213, 213, 255), new Color32(242, 224, 188, 255) };
                Require(words.Select(word => word.State).SequenceEqual(new[] { 0, 1, 2 }) &&
                    words.Select((word, i) => ((Color32)word.GetComponent<AlbumPaperCircleGraphic>().color).Equals(colors[i])).All(value => value),
                    "Unphotographed, photographed and understood words use green, blue and yellow paper respectively", checks);
                Require(words.All(word => !word.OutlineVisible && !word.TooltipVisible) &&
                    !album.GetComponentsInChildren<Text>(true).Any(text => text.name.StartsWith("Meaning ")),
                    "Idle word cells have no outline, glow or permanent Chinese subtitle", checks);
                Require(words.Take(2).All(word => !word.GetComponentsInChildren<Text>(true).Any(text => text.name == "Confirmed word meaning")),
                    "Unknown words do not contain a hidden translation label", checks);
                CaptureRenderedFrame("ui-word-state-colors.png");
                Color32[] idle = ReadCapture(Get<RenderTexture>(book, "before"));
                var pointer = new PointerEventData(EventSystem.current) { position = RectTransformUtility.WorldToScreenPoint(null, words[2].transform.position) };
                var hits = new List<RaycastResult>(); EventSystem.current.RaycastAll(pointer, hits);
                Require(hits.Any(hit => hit.gameObject == words[2].gameObject), "Actual 3D book raycast reaches the known word", checks);
                int captures = book.PageCaptureCount;
                ExecuteEvents.Execute(words[2].gameObject, pointer, ExecuteEvents.pointerEnterHandler);
                float deadline = Time.realtimeSinceStartup + 2f;
                while (book.PageCaptureCount == captures && Time.realtimeSinceStartup < deadline) yield return null;
                yield return new WaitForEndOfFrame();
                Require(book.PageCaptureCount == captures + 1 && words[2].OutlineVisible && words[2].TooltipVisible,
                    "Known-word hover adds a green outline and one page-texture capture", checks);
                var tip = words[2].transform.Find("Word meaning tooltip");
                var bubble = tip.GetComponent<ProceduralUiGraphic>();
                Require(bubble.mark == UiMark.AnimalBubble && bubble.color == ProceduralUiLibrary.Cream &&
                    tip.GetComponentInChildren<Text>().text == definitions[2].name,
                    "Known hover uses the existing animal speech bubble and its actual confirmed meaning", checks);
                Color32[] hover = ReadCapture(Get<RenderTexture>(book, "before"));
                object layout = Get<IDictionary>(book, "layouts")[(RectTransform)words[2].transform];
                Vector2 original = (Vector2)layout.GetType().GetField("position").GetValue(layout);
                var tipRect = (RectTransform)tip;
                Rect tipPixels = new Rect(original + tipRect.anchoredPosition + new Vector2(836, 470.5f) - tipRect.sizeDelta * .5f, tipRect.sizeDelta);
                int changed = 0;
                for (int y = Mathf.CeilToInt(tipPixels.yMin); y < tipPixels.yMax; y++)
                for (int x = Mathf.CeilToInt(tipPixels.xMin); x < tipPixels.xMax; x++) if (!idle[y * 1672 + x].Equals(hover[y * 1672 + x])) changed++;
                Require(changed > 150 && tipPixels.xMax < 1411 && tipPixels.yMax < 814,
                    "The actual captured curved-page texture contains the upper-right tooltip inside the paper crop (changed pixels=" + changed + ")", checks);
                CaptureRenderedFrame("ui-word-known-hover.png");
                for (int i = 0; i < 8; i++) { ExecuteEvents.Execute(words[2].gameObject, pointer, ExecuteEvents.pointerEnterHandler); yield return null; }
                Require(book.PageCaptureCount == captures + 1 && hover.SequenceEqual(ReadCapture(Get<RenderTexture>(book, "before"))),
                    "A stationary or repeated hover has no breathing animation and performs no further page captures", checks);
                ExecuteEvents.Execute(words[2].gameObject, pointer, ExecuteEvents.pointerExitHandler);
                deadline = Time.realtimeSinceStartup + 2f;
                while (book.PageCaptureCount == captures + 1 && Time.realtimeSinceStartup < deadline) yield return null;
                Require(book.PageCaptureCount == captures + 2 && !words[2].TooltipVisible && !words[2].OutlineVisible &&
                    idle.SequenceEqual(ReadCapture(Get<RenderTexture>(book, "before"))),
                    "Leaving the known word removes its tooltip and outline with one capture, restoring the original page pixels", checks);
                ExecuteEvents.Execute(words[1].gameObject, pointer, ExecuteEvents.pointerEnterHandler);
                yield return null; yield return new WaitForEndOfFrame();
                Require(words[1].OutlineVisible && !words[1].TooltipVisible, "Hovering a photographed but unknown word adds only the green outline", checks);
                CaptureRenderedFrame("ui-word-unknown-hover.png");
                ExecuteEvents.Execute(words[1].gameObject, pointer, ExecuteEvents.pointerExitHandler);
                words[2].GetComponent<Button>().onClick.Invoke();
                Require(Get<bool>(hud, "glyphDetailOpen") && Get<RectTransform>(hud, "detailRoot").GetComponentsInChildren<Text>().Any(text => text.name == "Confirmed meaning"),
                    "The existing known-word click still opens its detail card", checks);
                Call(hud, "CloseTop"); yield return null; yield return new WaitForEndOfFrame();
                Require(words[2].OutlineVisible && !words[2].TooltipVisible, "Selected words retain a green outline without a persistent translation bubble", checks);
                CaptureRenderedFrame("ui-word-selected.png");
            }
            finally
            {
                JsonUtility.FromJsonOverwrite(notebook, life.Gameplay.Notebook);
                Set(hud, "glyphPage", page); Set(hud, "selectedGlyph", selected); Set(hud, "glyphDetailOpen", false); Call(hud, "RebuildModal");
            }
        }

        static void CaptureRenderedFrame(string filename)
        {
            Texture2D image = ScreenCapture.CaptureScreenshotAsTexture();
            try { File.WriteAllBytes(Path.Combine("LifeGameplayReview/20261026/play", filename), image.EncodeToPNG()); }
            finally { UnityEngine.Object.Destroy(image); }
        }

        static IEnumerator CheckBubbleReuse(LifeSession life, GameplayHud hud, List<string> checks)
        {
            LifeSpeaker speaker = life.World.FindSpeaker("dog");
            Require(speaker && speaker.requests.Length > 0, "Bubble reuse has an actual authored speaker", checks);
            AnimalActor observed = life.Gameplay.Observed; int topic = speaker.topicIndex;
            Vector3 position = life.Gameplay.PlayerTransform.position; Quaternion rotation = life.Gameplay.PlayerTransform.rotation;
            float yaw = Get<float>(life.Gameplay.PhotoRig, "photoYaw"), pitch = Get<float>(life.Gameplay.PhotoRig, "photoPitch");
            string notebook = JsonUtility.ToJson(life.Gameplay.Notebook);
            string progress = JsonUtility.ToJson(life.Progress);
            var observedProperty = life.Gameplay.GetType().GetProperty("Observed");
            try
            {
                observedProperty.SetValue(life.Gameplay, speaker.Actor);
                Set(hud, "dialogueOpen", true); Set(hud, "dialogueTarget", speaker.Actor);
                // Warm each authored size once, then exercise the same clear/rebind path used after fade-out.
                for (int i = 0; i < speaker.requests.Length; i++) { speaker.topicIndex = i; Call(hud, "ClearAnimalBubble"); Call(hud, "UpdateAnimalBubble"); }
                RectTransform bubble = Get<RectTransform>(hud, "bubbleRoot"); int instance = bubble.GetInstanceID();
                int[] slots = bubble.GetComponentsInChildren<AnimalGlyphGraphic>(true).Select(g => g.GetInstanceID()).ToArray();
                for (int i = 0; i < 20; i++)
                {
                    speaker.topicIndex = i % speaker.requests.Length; Call(hud, "ClearAnimalBubble"); Call(hud, "UpdateAnimalBubble");
                    Require(Get<RectTransform>(hud, "bubbleRoot").GetInstanceID() == instance && slots.SequenceEqual(bubble.GetComponentsInChildren<AnimalGlyphGraphic>(true).Select(g => g.GetInstanceID())),
                        "Warm animal presentation reuses the bubble and every glyph slot, cycle " + (i + 1), checks);
                    CheckBubbleLayout(hud, checks);
                }
                if (!life.Progress.understoodRequests.Contains("dog.hello")) life.Progress.understoodRequests.Add("dog.hello");
                speaker.topicIndex = Array.FindIndex(speaker.requests, request => request.id == "dog.rain.lesson");
                Require(speaker.topicIndex >= 0 && speaker.Current?.id == "dog.rain.lesson" && speaker.Actor.CurrentGlyphIds.SequenceEqual(new[] { "want", "rain" }),
                    "After the greeting prerequisite, the actual current utterance is the authored want/rain lesson", checks);
                life.Gameplay.Notebook.GetWord("want").confirmed = true;
                life.Gameplay.Notebook.GetWord("rain").confirmed = false;
                life.Gameplay.PlayerTransform.position = speaker.transform.position + Vector3.right * 2.8f;
                Physics.SyncTransforms(); hud.SetCameraMode(true, true);
                var renderers = speaker.Actor.VisualRoot.GetComponentsInChildren<Renderer>().Where(renderer => renderer.enabled).ToArray();
                Require(renderers.Length > 0, "Bubble screenshot frames the actual animal geometry", checks);
                Bounds bounds = renderers[0].bounds;
                foreach (Renderer renderer in renderers.Skip(1)) bounds.Encapsulate(renderer.bounds);
                Vector3 aim = Vector3.Lerp(bounds.center, speaker.Actor.BubbleAnchor, .55f) -
                    (life.Gameplay.PlayerTransform.position + Vector3.up * Mathf.Max(.2f, life.Gameplay.forestSprite.cameraHeight));
                Vector3 angles = Quaternion.LookRotation(aim).eulerAngles;
                Set(life.Gameplay.PhotoRig, "photoYaw", angles.y); Set(life.Gameplay.PhotoRig, "photoPitch", angles.x > 180 ? angles.x - 360 : angles.x);
                Call(hud, "ClearAnimalBubble"); Call(hud, "UpdateAnimalBubble");
                float deadline = Time.realtimeSinceStartup + 4f;
                do { yield return new WaitForEndOfFrame(); } while (!BubbleDrawn(hud, speaker.Actor) && Time.realtimeSinceStartup < deadline);
                Require(BubbleDrawn(hud, speaker.Actor), "HUD bubble is drawn after LateUpdate: " + BubbleRenderState(hud, speaker.Actor), checks);
                var meanings = Get<List<Text>>(hud, "bubbleMeanings");
                Require(speaker.Current?.id == "dog.rain.lesson" && meanings.Where(text => text.transform.parent.gameObject.activeSelf).Select(text => text.name).SequenceEqual(new[] { "want", "rain" }),
                    "The visible bubble still carries want/rain: current=" + speaker.Current?.id + ", labels=" + string.Join(",", meanings.Where(text => text.transform.parent.gameObject.activeSelf).Select(text => text.name)), checks);
                Require(meanings.First(text => text.name == "want").text.Length > 0 && meanings.First(text => text.name == "rain").text.Length == 0,
                    "Only the confirmed word is translated; the visible unknown rain glyph has no Chinese label", checks);
                CheckBubbleLayout(hud, checks);
                CaptureBubbleFrame(hud, speaker.Actor, "ui-animal-bubble-known-unknown.png", checks);
                life.Gameplay.Notebook.GetWord("rain").confirmed = true; Call(hud, "OnChanged");
                yield return null; yield return new WaitForEndOfFrame();
                Require(meanings.First(text => text.name == "rain").text.Length > 0,
                    "Confirming a word updates the reused upper label without rebuilding the bubble", checks);
                CaptureBubbleFrame(hud, speaker.Actor, "ui-animal-bubble-confirmed.png", checks);
                CheckSpeechVisibility(life, hud, speaker, checks);
            }
            finally
            {
                hud.SetCameraMode(false, true);
                life.Gameplay.PlayerTransform.SetPositionAndRotation(position, rotation);
                Set(life.Gameplay.PhotoRig, "photoYaw", yaw); Set(life.Gameplay.PhotoRig, "photoPitch", pitch);
                JsonUtility.FromJsonOverwrite(notebook, life.Gameplay.Notebook);
                JsonUtility.FromJsonOverwrite(progress, life.Progress);
                speaker.topicIndex = topic; observedProperty.SetValue(life.Gameplay, observed);
                Set(hud, "dialogueOpen", false); Set(hud, "dialogueTarget", null); Call(hud, "ClearAnimalBubble");
                life.Gameplay.RefreshInputBlocking();
            }
        }

        static void CheckSpeechVisibility(LifeSession life, GameplayHud hud, LifeSpeaker speaker, List<string> checks)
        {
            GameplaySession game = life.Gameplay;
            RectTransform root = Get<RectTransform>(hud, "bubbleRoot"); CanvasGroup group = Get<CanvasGroup>(hud, "bubbleGroup");
            Camera camera = game.CurrentView; Quaternion rotation = camera.transform.rotation;
            string last = Get<string>(game, "lastUtterance"); float next = Get<float>(game, "nextObservation");
            GameObject blocker = null;
            try
            {
                speaker.topicIndex = Array.FindIndex(speaker.requests, request => request.id == "dog.hello");
                Call(hud, "ClearAnimalBubble"); Call(hud, "UpdateAnimalBubble"); Call(hud, "UpdateAnimalPresentation", .18f);
                Require(speaker.Current?.id == "dog.hello" && hud.IsAnimalSpeechVisible(speaker.Actor),
                    "Visibility negatives begin from the real visible greeting that can publish public history", checks);
                Action<string> hidden = reason =>
                {
                    Require(!hud.IsAnimalSpeechVisible(speaker.Actor), "Hidden Life utterance is rejected: " + reason, checks);
                    string before = JsonUtility.ToJson(game.Notebook);
                    Set(game, "lastUtterance", null); Set(game, "nextObservation", 0f); Call(game, "Update");
                    Require(before == JsonUtility.ToJson(game.Notebook), "Hidden utterance adds neither notebook evidence nor public history: " + reason, checks);
                };
                Set(hud, "bubbleRoot", null); hidden("no HUD bubble built"); Set(hud, "bubbleRoot", root);
                group.alpha = 0; hidden("alpha zero");
                game.TakePhoto();
                ObservationRecord pending = Get<ObservationRecord>(game.Recorder, "pending");
                Require(game.Recorder.IsRecording && pending != null && pending.frameCount >= 1 &&
                    string.IsNullOrEmpty(pending.utteranceId) && pending.glyphIds.Length == 0 && pending.context == null,
                    "The actual shutter frame excludes an unseen Life utterance, glyph sequence and language context", checks);
                Set(game.Recorder, "failed", null); game.Recorder.CancelCapture(); group.alpha = 1;
                root.gameObject.SetActive(false); hidden("inactive HUD bubble"); root.gameObject.SetActive(true);
                camera.transform.rotation = rotation * Quaternion.Euler(0, 180, 0); hidden("anchor outside the real camera view"); camera.transform.rotation = rotation;
                blocker = GameObject.CreatePrimitive(PrimitiveType.Cube); blocker.name = "Speech sight regression blocker";
                blocker.transform.position = Vector3.Lerp(camera.transform.position, speaker.Actor.BubbleAnchor, .5f);
                blocker.transform.localScale = Vector3.one * .45f; Physics.SyncTransforms(); hidden("actual physics collider occludes the anchor");
                UnityEngine.Object.DestroyImmediate(blocker); blocker = null; Physics.SyncTransforms();
                string utterance = Get<string>(hud, "bubbleUtterance"); Set(hud, "bubbleUtterance", "previous-word"); hidden("different utterance identity"); Set(hud, "bubbleUtterance", utterance);
                Require(hud.IsAnimalSpeechVisible(speaker.Actor), "Removing the visibility blockers restores the real Life utterance", checks);
                CheckAmbientSpeechOwnership(hud, checks);
            }
            finally
            {
                if (game.Recorder.IsRecording) { Set(game.Recorder, "failed", null); game.Recorder.CancelCapture(); }
                if (blocker) UnityEngine.Object.DestroyImmediate(blocker);
                camera.transform.rotation = rotation; Set(hud, "bubbleRoot", root); group.alpha = 1; root.gameObject.SetActive(true);
                Set(game, "lastUtterance", last); Set(game, "nextObservation", next); Physics.SyncTransforms();
            }
        }

        static void CheckAmbientSpeechOwnership(GameplayHud hud, List<string> checks)
        {
            var fixture = new GameObject("Isolated speech ownership fixture") { hideFlags = HideFlags.HideAndDontSave };
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(fixture, hud.gameObject.scene);
            RenderTexture viewTarget = null;
            try
            {
                // Reuse the existing social test's real schedule, participants and presentation path.
                var type = typeof(WeatherHouse.WildlifeJourney.Editor.JourneySocialChecks);
                const BindingFlags flags = BindingFlags.Static | BindingFlags.NonPublic;
                var participants = (WeatherHouse.WildlifeJourney.JourneySocialParticipant[])type.GetMethod("Participants", flags).Invoke(null, new object[] { fixture.transform, 2 });
                var social = (WeatherHouse.WildlifeJourney.JourneySocial)type.GetMethod("Social", flags).Invoke(null, new object[] { fixture.transform });
                Require(social.gameObject.scene == hud.gameObject.scene && social.gameObject.scene.GetPhysicsScene().IsValid(),
                    "The speech ownership fixture uses the formal scene's valid physics scene", checks);
                var view = (Camera)type.GetMethod("View", flags).Invoke(null, new object[] { fixture.transform });
                viewTarget = new RenderTexture(320, 200, 16); view.targetTexture = viewTarget; social.viewCamera = view;
                social.Tick(0, participants); social.Tick(1.2f, participants); social.Tick(1.21f, participants);
                type.GetMethod("ShowFully", flags).Invoke(null, new object[] { social });
                Require(social.ActiveConversation != null && social.VisibleBubbleCount == 1, "A real ecological conversation is visibly presented in the ownership fixture", checks);
                var participant = participants.First(item => item.id == social.ActiveConversation.speakerId);
                var herd = fixture.AddComponent<WeatherHouse.WildlifeJourney.JourneyHerd>(); herd.social = social;
                herd.members = new[] { new WeatherHouse.WildlifeJourney.JourneyMember { id = participant.id, root = participant.root, headBone = participant.head, visualRoot = participant.root } };
                var ecology = participant.root.gameObject.AddComponent<WildlifeInteractionTarget>();
                Set(ecology, "herd", herd); Set(ecology, "memberId", participant.id);
                var animal = participant.root.gameObject.AddComponent<AnimalActor>(); animal.animalId = "ui-social-speaker";
                var lifeSpeaker = participant.root.gameObject.AddComponent<LifeSpeaker>(); lifeSpeaker.Actor = animal;
                lifeSpeaker.requests = new[] { new LifeRequest { id = "ui-life-rain", glyphIds = new[] { "rain" } } };
                Require(animal.HasAmbientPresentation && lifeSpeaker.Current != null &&
                    !animal.CurrentGlyphIds.SequenceEqual(social.ActiveConversation.glyphIds) && !hud.IsAnimalSpeechVisible(animal),
                    "A real ecological bubble cannot stand in for the Life speaker's different, unshown word", checks);
                UnityEngine.Object.DestroyImmediate(lifeSpeaker);
                Require(hud.IsAnimalSpeechVisible(animal), "An animal without a Life request preserves the existing ecological speech visibility path", checks);
            }
            finally { UnityEngine.Object.DestroyImmediate(fixture); if (viewTarget) UnityEngine.Object.DestroyImmediate(viewTarget); }
        }

        static Rect ScreenBounds(RectTransform rect)
        {
            var corners = new Vector3[4]; rect.GetWorldCorners(corners);
            Vector2 lower = RectTransformUtility.WorldToScreenPoint(null, corners[0]);
            Vector2 upper = RectTransformUtility.WorldToScreenPoint(null, corners[2]);
            return Rect.MinMaxRect(lower.x, lower.y, upper.x, upper.y);
        }

        static bool BubbleDrawn(GameplayHud hud, AnimalActor animal)
        {
            RectTransform root = Get<RectTransform>(hud, "bubbleRoot");
            ProceduralUiGraphic shape = Get<ProceduralUiGraphic>(hud, "bubbleShape");
            if (!root || !shape || !root.gameObject.activeInHierarchy || Get<AnimalActor>(hud, "bubbleAnimal") != animal ||
                !Get<bool>(hud, "bubbleWanted") || Get<CanvasGroup>(hud, "bubbleGroup").alpha < .999f ||
                shape.canvasRenderer.cull || shape.canvasRenderer.GetInheritedAlpha() < .999f) return false;
            Rect bounds = ScreenBounds(root);
            return bounds.width > 40f && bounds.height > 40f && bounds.xMin >= 0 && bounds.yMin >= 0 &&
                bounds.xMax <= Screen.width && bounds.yMax <= Screen.height;
        }

        static string BubbleRenderState(GameplayHud hud, AnimalActor animal)
        {
            RectTransform root = Get<RectTransform>(hud, "bubbleRoot");
            ProceduralUiGraphic shape = Get<ProceduralUiGraphic>(hud, "bubbleShape");
            AnimalActor bound = Get<AnimalActor>(hud, "bubbleAnimal");
            return "frame=" + Time.frameCount + ", root=" + (root && root.gameObject.activeInHierarchy) +
                ", animal=" + (bound ? bound.animalId : "none") + ", wanted=" + Get<bool>(hud, "bubbleWanted") +
                ", alpha=" + Get<CanvasGroup>(hud, "bubbleGroup").alpha + ", cull=" + shape.canvasRenderer.cull +
                ", inheritedAlpha=" + shape.canvasRenderer.GetInheritedAlpha() + ", ambientPresentation=" + animal.HasAmbientPresentation +
                ", ambientSpeech=" + animal.HasAmbientSpeech + ", screen=" + (root ? ScreenBounds(root).ToString("F1") : "none") +
                ", anchorViewport=" + Get<GameplaySession>(hud, "session").CurrentView.WorldToViewportPoint(animal.BubbleAnchor).ToString("F3");
        }

        static void CaptureBubbleFrame(GameplayHud hud, AnimalActor animal, string filename, List<string> checks)
        {
            string folder = "LifeGameplayReview/20261026/play";
            Directory.CreateDirectory(folder);
            File.AppendAllText(Path.Combine(folder, "ui-bubble-render.txt"), filename + " " + BubbleRenderState(hud, animal) + "\n");
            Require(BubbleDrawn(hud, animal), "The screenshot frame retains the fully opaque on-screen HUD bubble", checks);
            Texture2D frame = ScreenCapture.CaptureScreenshotAsTexture();
            try
            {
                File.WriteAllBytes(Path.Combine(folder, filename), frame.EncodeToPNG());
                Rect bounds = ScreenBounds(Get<RectTransform>(hud, "bubbleRoot"));
                Color32[] pixels = frame.GetPixels32(); Color32 cream = Get<ProceduralUiGraphic>(hud, "bubbleShape").color;
                int matching = 0, sampled = 0;
                for (int y = Mathf.CeilToInt(bounds.yMin + bounds.height * .2f); y < bounds.yMax - bounds.height * .2f; y++)
                for (int x = Mathf.CeilToInt(bounds.xMin + bounds.width * .2f); x < bounds.xMax - bounds.width * .2f; x++)
                {
                    Color32 pixel = pixels[y * frame.width + x]; sampled++;
                    if (Mathf.Abs(pixel.r - cream.r) <= 12 && Mathf.Abs(pixel.g - cream.g) <= 12 && Mathf.Abs(pixel.b - cream.b) <= 12) matching++;
                }
                Require(sampled > 500 && matching > sampled * .45f,
                    "Final screenshot pixels contain the bubble's cream surface: " + matching + "/" + sampled + " in " + filename, checks);
                foreach (Text label in Get<List<Text>>(hud, "bubbleMeanings").Where(text => text.transform.parent.gameObject.activeSelf && text.text.Length > 0))
                {
                    Rect labelBounds = ScreenBounds(label.rectTransform);
                    Require(!label.canvasRenderer.cull && label.canvasRenderer.GetInheritedAlpha() >= .999f &&
                        labelBounds.yMin > bounds.yMax && labelBounds.xMin >= 0 && labelBounds.xMax <= Screen.width && labelBounds.yMax <= Screen.height,
                        "Confirmed white label is rendered above the bubble and inside the screenshot: " + label.name, checks);
                }
            }
            finally { UnityEngine.Object.Destroy(frame); }
        }

        static void CheckBubbleLayout(GameplayHud hud, List<string> checks)
        {
            RectTransform bubble = Get<RectTransform>(hud, "bubbleRoot");
            var shape = Get<ProceduralUiGraphic>(hud, "bubbleShape");
            var mesh = new Mesh();
            try
            {
                using (var vertices = new VertexHelper())
                {
                    typeof(ProceduralUiGraphic).GetMethod("OnPopulateMesh", Private, null, new[] { typeof(VertexHelper) }, null).Invoke(shape, new object[] { vertices });
                    vertices.FillMesh(mesh);
                }
                Require(mesh.vertexCount > 20, "Bubble layout checks use the rendered silhouette mesh", checks);
                float center = mesh.vertices[0].y;
                var glyphs = bubble.GetComponentsInChildren<AnimalGlyphGraphic>(true).Where(glyph => glyph.gameObject.activeSelf).ToArray();
                Require(glyphs.Length > 0 && glyphs.All(glyph => Mathf.Abs(glyph.rectTransform.anchoredPosition.y - center) < .1f),
                    "Every glyph is vertically centered on the actual bubble body, excluding its tail", checks);
                Require(glyphs.All(glyph =>
                {
                    Text label = glyph.GetComponentInChildren<Text>(true);
                    Vector3 lowerEdge = bubble.InverseTransformPoint(label.rectTransform.TransformPoint(new Vector3(0, label.rectTransform.rect.yMin)));
                    return label.color == Color.white && lowerEdge.y > bubble.rect.yMax + 4f &&
                        Mathf.Abs(label.rectTransform.anchoredPosition.x) < .1f && label.rectTransform.rect.height >= label.fontSize * 1.3f;
                }), "Chinese labels are white, above the bubble, aligned with their glyphs and tall enough to render", checks);
            }
            finally { UnityEngine.Object.Destroy(mesh); }
        }

        sealed class PhotoFixture : IDisposable
        {
            public readonly List<ObservationRecord> records = new List<ObservationRecord>();
            readonly List<string> files = new List<string>(), folders = new List<string>();
            readonly string root = Path.GetFullPath(Path.Combine(GameplaySession.PersistenceDirectory, "MotionPhotos"));
            public PhotoFixture()
            {
                var texture = new Texture2D(512, 288, TextureFormat.RGB24, false);
                try
                {
                    texture.SetPixels32(Enumerable.Repeat(new Color32(118, 148, 105, 255), 512 * 288).ToArray()); texture.Apply();
                    byte[] png = texture.EncodeToPNG();
                    for (int i = 0; i < 9; i++)
                    {
                        string id = Guid.NewGuid().ToString("N"), folder = Path.Combine(root, id);
                        Directory.CreateDirectory(folder); folders.Add(folder);
                        var record = new ObservationRecord { id = id, clipFolder = "MotionPhotos/" + id, frameCount = i == 0 ? 18 : 1, framesPerSecond = 6, glyphIds = Array.Empty<string>() };
                        records.Add(record);
                        for (int frame = 0; frame < record.frameCount; frame++)
                        {
                            string path = Path.Combine(folder, "frame_" + frame.ToString("D3") + ".png");
                            File.WriteAllBytes(path, png); files.Add(path);
                        }
                    }
                }
                catch { Dispose(); throw; }
                finally { UnityEngine.Object.Destroy(texture); }
            }
            public void Dispose()
            {
                foreach (string file in files) if (Path.GetFullPath(file).StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal) && File.Exists(file)) File.Delete(file);
                foreach (string folder in folders) if (Path.GetFullPath(folder).StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal) && Directory.Exists(folder) && !Directory.EnumerateFileSystemEntries(folder).Any()) Directory.Delete(folder);
            }
        }

        static void CheckReadingSurface(GameplayHud hud, LifeJournal3D book, List<string> checks)
        {
            RectTransform album = Get<RectTransform>(hud, "albumRoot");
            var tabs = new[] { "Symbols tab", "Photos tab", "Gesture learning tab", "Photography tasks tab" }.Select(name => (RectTransform)album.Find(name)).ToArray();
            var paper = (RectTransform)album.Find("Left paper");
            float paperTop = paper.anchoredPosition.y + paper.sizeDelta.y * paper.localScale.y * .5f;
            Require(tabs.All(t => t && t.sizeDelta == new Vector2(82, 69)) && tabs.All(t => Mathf.Approximately(t.anchoredPosition.y, tabs[0].anchoredPosition.y)) &&
                tabs.All(t => t.anchoredPosition.y - 20f * t.localScale.y > paperTop) && tabs[3].anchoredPosition.x > tabs[2].anchoredPosition.x && tabs[2].anchoredPosition.x > tabs[1].anchoredPosition.x && tabs[1].anchoredPosition.x > tabs[0].anchoredPosition.x,
                "Four matching tabs share a single ordered row with their icons raised clear of the paper", checks);
            Require(!album.Find("Glyph state legend") && !album.GetComponentsInChildren<Text>(true).Any(t => t.name == "Knowledge state") &&
                !album.GetComponentsInChildren<Transform>(true).Any(t => t.name == "Knowledge ring") &&
                album.GetComponentsInChildren<LifeJournalWord>(true).Length > 0 && album.Find("Collected count"),
                "Glyph states use paper colors without permanent rings; the original collection count remains", checks);
            Require(book.HasPageSnapshot && album.Find("Left paper").GetComponent<RawImage>().texture == Resources.Load<Texture2D>("LifeAssets/JournalTextures/PaperAged") &&
                !album.Find("Book artwork").gameObject.activeSelf,
                "Settled curved pages retain their content and the separate new paper texture", checks);
            Material pageMaterial = book.Model.GetComponentsInChildren<Renderer>().First(r => r.name == "PageRight").sharedMaterial;
            Require(pageMaterial.GetTexture("_BumpMap") == Resources.Load<Texture2D>("LifeAssets/JournalTextures/PaperAged_Normal") && pageMaterial.GetTexture("_BumpMap") &&
                pageMaterial.GetTexture("_OcclusionMap") == Resources.Load<Texture2D>("LifeAssets/JournalTextures/PaperAged_AO") && pageMaterial.GetTexture("_OcclusionMap") &&
                pageMaterial.GetTextureScale("_BumpMap") == Vector2.one && pageMaterial.GetTextureOffset("_BumpMap") == Vector2.zero,
                "Composed page keeps its paper normal/AO in uncropped page UVs", checks);
            Require(Mathf.Approximately(pageMaterial.GetFloat("_MinimumShade"), .80f),
                "Page lighting preserves the text and photo readability floor", checks);
            var word = album.GetComponentsInChildren<LifeJournalWord>(true).First().transform as RectTransform;
            Require(word.GetComponent<CanvasGroup>().alpha == 0 && word.GetComponent<CanvasGroup>().blocksRaycasts,
                "Invisible flat content retains only its interaction surface", checks);
            Require(album.GetComponentsInChildren<Graphic>(true).All(graphic => graphic.GetComponent<CanvasRenderer>()),
                "Every journal graphic owns the CanvasRenderer needed by rendering and raycasts", checks);
            CheckNavigationCapture(album, book, checks);
            Canvas.ForceUpdateCanvases();
            var hits = new List<RaycastResult>();
            var pointer = new PointerEventData(EventSystem.current) { position = RectTransformUtility.WorldToScreenPoint(null, word.TransformPoint(Vector3.zero)) };
            EventSystem.current.RaycastAll(pointer, hits);
            Require(hits.Any(hit => hit.gameObject == word.gameObject), "A real UI raycast reaches the word through its transparent interaction surface", checks);
            CheckProjectedWord(book, word, checks);
        }

        static void CaptureUiFrame(string name)
        {
            string folder = "LifeGameplayReview/20261026/play";
            Directory.CreateDirectory(folder); ScreenCapture.CaptureScreenshot(Path.Combine(folder, name));
        }

        static void CheckTurningSurfaces(GameplayHud hud, LifeJournal3D book, List<string> checks)
        {
            RectTransform album = Get<RectTransform>(hud, "albumRoot");
            Require(book.PresentationOpacity == 1 && new[] { "Symbols tab", "Photos tab", "Gesture learning tab", "Photography tasks tab" }
                .All(name => album.Find(name).GetComponent<CanvasGroup>().alpha == 1) && !album.GetComponent<CanvasGroup>().interactable,
                "A moving leaf keeps all four tabs opaque while clicks remain disabled", checks);
            CheckTabVisualAlpha(hud, checks);
            var leaf = Get<Renderer>(book, "turningPage");
            RenderTexture front = Get<RenderTexture>(book, "before"), back = Get<RenderTexture>(book, "after");
            Material[] faces = leaf.sharedMaterials;
            Require(front != back && faces.Length == 2 && faces[0].GetTexture("_BaseMap") == front && faces[1].GetTexture("_BaseMap") == back &&
                faces[0].GetTextureOffset("_BaseMap").x > .5f && faces[1].GetTextureOffset("_BaseMap").x < .5f,
                "The moving leaf has distinct current-right and next-left snapshots on its two faces", checks);
        }

        static void CheckOpeningSurfaces(GameplayHud hud, LifeJournal3D book, List<string> checks)
        {
            CheckTabVisualAlpha(hud, checks);
            CheckOpeningBindings(book, checks);
            Require(book.OpeningFan.Pages.Count(p => p.enabled) == 4 - book.OpeningTurnsCompleted,
                "Each completed leaf joins the stack while later independent leaves keep moving", checks);
        }

        static void CheckOpeningBindings(LifeJournal3D book, List<string> checks)
        {
            Texture intro = Get<RenderTexture>(book, "after"), target = Get<RenderTexture>(book, "before");
            Material left = Get<Renderer>(book, "pageLeft").sharedMaterial, right = Get<Renderer>(book, "pageRight").sharedMaterial;
            bool revealing = book.IsOpeningBrowse && book.OpeningFan.RevealingTarget;
            Require(left.GetTexture("_BaseMap") == intro && right.GetTexture("_BaseMap") == (revealing ? target : intro) && !Get<Renderer>(book, "turningPage").enabled,
                "Both visible pages begin with the introductory layout; the target right appears only when the final leaf moves", checks);
            for (int i = 0; i < LifeJournalOpeningFan.Count; i++)
            {
                Material[] faces = book.OpeningFan.Pages[i].sharedMaterials;
                Require(faces[0].GetTexture("_BaseMap") == intro && faces[0].GetTextureOffset("_BaseMap").x > .5f && faces[1].GetTextureOffset("_BaseMap").x < .5f &&
                    faces[0].GetTexture("_BumpMap") == Resources.Load<Texture2D>("LifeAssets/JournalTextures/PaperAged_Normal") &&
                    faces[1].GetTexture("_BaseMap") == (i == 3 ? target : intro),
                    "Independent leaf " + (i + 1) + " has the original right-grid/left-placeholder layout, with target only on the final reverse", checks);
            }
        }

        static void CheckIndependentFan(LifeJournal3D book, List<string> checks)
        {
            SkinnedMeshRenderer[] pages = book.OpeningFan.Pages;
            Require(pages.Length == 4 && pages.All(p => p.enabled) && pages.Select(p => p.GetInstanceID()).Distinct().Count() == 4 &&
                pages.Select(p => p.bones[0]).Distinct().Count() == 4 && pages.All(p => p.sharedMesh == pages[0].sharedMesh),
                "Four visible independent bone rigs share one authored flexible leaf mesh", checks);
            var tips = new List<Vector2>(); var mesh = new Mesh();
            var camera = Get<Camera>(book, "view"); Vector2 size = Get<RawImage>(book, "image").rectTransform.sizeDelta;
            try
            {
                for (int i = 0; i < pages.Length; i++)
                {
                    pages[i].BakeMesh(mesh); Vector2[] uv = mesh.uv; Vector3[] vertices = mesh.vertices;
                    int tip = mesh.GetIndices(0).OrderBy(index => (uv[index] - new Vector2(1, .5f)).sqrMagnitude).First();
                    Vector3 viewport = camera.WorldToViewportPoint(pages[i].transform.TransformPoint(vertices[tip]));
                    tips.Add(Vector2.Scale(viewport, size));
                }
                float separation = float.MaxValue;
                for (int i = 0; i < tips.Count - 1; i++) separation = Mathf.Min(separation, Vector2.Distance(tips[i], tips[i + 1]));
                Require(separation > 6f, "Actual baked outer-edge silhouettes of adjacent leaves differ by at least " + separation.ToString("F1") + " reference pixels", checks);
                Require(Enumerable.Range(0, 3).All(i => book.OpeningFan.Progress(i) - book.OpeningFan.Progress(i + 1) > .02f),
                    "All four visible leaves have distinctly ordered animation progress in the same frame", checks);
            }
            finally { UnityEngine.Object.Destroy(mesh); }
        }

        static IEnumerator CheckSnapshotInteractionTint(GameplayHud hud, LifeJournal3D book, List<string> checks)
        {
            CanvasGroup input = Get<RectTransform>(hud, "albumRoot").GetComponent<CanvasGroup>();
            Button button = Get<RectTransform>(hud, "albumRoot").GetComponentsInChildren<Button>(true).First(b => b.name.StartsWith("Word ", StringComparison.Ordinal));
            Graphic word = button.targetGraphic;
            Selectable.Transition transition = button.transition;
            Color originalTint = word.canvasRenderer.GetColor();
            RenderTexture target = Get<RenderTexture>(book, "before");
            Call(book, "Capture", target); Color32[] enabled = ReadCapture(target);
            try
            {
                input.interactable = false;
                yield return new WaitForSecondsRealtime(.2f);
                Call(book, "Capture", target);
                Require(transition == Selectable.Transition.None && word.canvasRenderer.GetAlpha() >= .999f && enabled.SequenceEqual(ReadCapture(target)),
                    "The word's own paper and outline presentation stays unchanged under the animation interaction lock", checks);
                input.interactable = true;
                // Recreate the legacy ColorTint on the real control so the shared capture regression remains exercised.
                button.transition = Selectable.Transition.ColorTint;
                yield return new WaitForSecondsRealtime(.2f);
                Call(book, "Capture", target); enabled = ReadCapture(target);
                input.interactable = false;
                yield return new WaitForSecondsRealtime(.2f);
                Require(word.canvasRenderer.GetAlpha() < .8f, "The actual word button with legacy ColorTint reproduces the transient pale tint before capture", checks);
                Call(book, "Capture", target);
                Require(enabled.SequenceEqual(ReadCapture(target)),
                    "Captured paper pixels are identical with enabled or disabled interaction, preventing a pale content layer during turns", checks);
                input.interactable = true; button.interactable = false;
                yield return new WaitForSecondsRealtime(.2f);
                Call(book, "Capture", target); Color32[] intentionallyDisabled = ReadCapture(target);
                Require(!enabled.SequenceEqual(intentionallyDisabled) && word.canvasRenderer.GetAlpha() < .8f,
                    "An intentionally disabled control keeps its distinct disabled appearance in the snapshot", checks);
                input.interactable = false; Call(book, "Capture", target);
                Require(intentionallyDisabled.SequenceEqual(ReadCapture(target)),
                    "The animation lock does not brighten or otherwise change an intentionally disabled control", checks);
            }
            finally
            {
                input.interactable = true; button.interactable = true; button.transition = transition;
                word.canvasRenderer.SetColor(originalTint); Call(book, "Capture", target);
            }
            yield return new WaitForSecondsRealtime(.2f);
        }

        static void CheckTabVisualAlpha(GameplayHud hud, List<string> checks)
        {
            Canvas.ForceUpdateCanvases();
            RectTransform album = Get<RectTransform>(hud, "albumRoot");
            foreach (string name in new[] { "Symbols tab", "Photos tab", "Gesture learning tab", "Photography tasks tab" })
            {
                Transform tab = album.Find(name); Graphic graphic = tab.GetComponent<Graphic>();
                float renderedAlpha = graphic.color.a * graphic.canvasRenderer.GetAlpha() * graphic.canvasRenderer.GetInheritedAlpha();
                Require(renderedAlpha >= .999f && !tab.GetComponent<Button>().IsInteractable(),
                    "The disabled " + name + " retains final Graphic/CanvasRenderer alpha=" + renderedAlpha.ToString("F3"), checks);
                var hits = new List<RaycastResult>();
                var pointer = new PointerEventData(EventSystem.current) { position = RectTransformUtility.WorldToScreenPoint(null, tab.position) };
                EventSystem.current.RaycastAll(pointer, hits);
                Require(hud.BlocksMovement && hits.Count > 0 && !hits.Any(hit => hit.gameObject.transform == tab || hit.gameObject.transform.IsChildOf(tab)),
                    "The moving " + name + " cannot receive a real pointer hit and the modal backdrop still catches it", checks);
            }
        }

        static void CheckMotionEnvelope(GameplayHud hud, LifeJournal3D book, List<string> checks)
        {
            var renderers = book.Model.GetComponentsInChildren<SkinnedMeshRenderer>();
            var leaf = Get<Renderer>(book, "turningPage") as SkinnedMeshRenderer;
            Camera camera = Get<Camera>(book, "view");
            RectTransform display = Get<RawImage>(book, "image").rectTransform;
            AnimationClip opening = Get<AnimationClip>(book, "openClip"), turning = Get<AnimationClip>(book, "turnClip");
            object idleMotion = Get<object>(book, "motion"); float originalElapsed = Get<float>(book, "elapsed");
            var mesh = new Mesh();
            try
            {
                foreach (bool turn in new[] { false, true })
                {
                    Vector2 minimum = Vector2.one * float.MaxValue, maximum = Vector2.one * float.MinValue;
                    Vector2 screenMin = minimum, screenMax = maximum;
                    for (int step = 0; step <= 20; step++)
                    {
                        float time = step / 20f * (turn ? turning.length : opening.length);
                        Set(book, "motion", Enum.Parse(idleMotion.GetType(), turn ? "Turning" : "Opening")); Set(book, "elapsed", time);
                        leaf.enabled = turn;
                        Call(book, "SampleOpen", turn ? opening.length : time);
                        if (turn) { turning.SampleAnimation(book.Model, time); Call(book, "UpdatePresentation", 1f); }
                        foreach (SkinnedMeshRenderer renderer in renderers.Where(r => r.enabled))
                        {
                            renderer.BakeMesh(mesh);
                            foreach (Vector3 vertex in mesh.vertices)
                            {
                                Vector3 viewport = camera.WorldToViewportPoint(renderer.transform.TransformPoint(vertex));
                                if (viewport.z <= camera.nearClipPlane || viewport.z >= camera.farClipPlane)
                                    Require(false, "Book geometry crossed the view depth planes", checks);
                                minimum = Vector2.Min(minimum, viewport); maximum = Vector2.Max(maximum, viewport);
                                Vector3 uiPoint = display.TransformPoint(new Vector3((viewport.x - .5f) * display.sizeDelta.x, (viewport.y - .5f) * display.sizeDelta.y, 0));
                                Vector2 screen = RectTransformUtility.WorldToScreenPoint(null, uiPoint);
                                screenMin = Vector2.Min(screenMin, screen); screenMax = Vector2.Max(screenMax, screen);
                            }
                        }
                        if (turn && step == 0) CheckLeafDepthPrecision(leaf, mesh, checks);
                    }
                    string range = " viewport=" + minimum.ToString("F4") + ".." + maximum.ToString("F4") + ", screen=" + screenMin.ToString("F1") + ".." + screenMax.ToString("F1");
                    Require(minimum.x > .005f && minimum.y > .005f && maximum.x < .995f && maximum.y < .995f,
                        "All baked vertices stay inside the render target across 21 " + (turn ? "turning" : "opening") + " poses;" + range, checks);
                    Require(screenMin.x >= 4 && screenMin.y >= 4 && screenMax.x <= Screen.width - 4 && screenMax.y <= Screen.height - 4,
                        "The entire " + (turn ? "turning" : "opening") + " motion stays inside the actual screen without shrinking the resting book;" + range, checks);
                }
            }
            finally
            {
                Set(book, "motion", idleMotion); Set(book, "elapsed", originalElapsed); leaf.enabled = false;
                Call(book, "SampleOpen", opening.length); Call(book, "RenderBook"); UnityEngine.Object.Destroy(mesh);
            }
        }

        static void CheckOpeningFanGeometry(LifeJournal3D book, List<string> checks)
        {
            var main = book.Model.GetComponentsInChildren<SkinnedMeshRenderer>();
            var pages = book.OpeningFan.Pages;
            var renderers = main.Concat(pages).ToArray();
            Camera camera = Get<Camera>(book, "view"); RectTransform display = Get<RawImage>(book, "image").rectTransform;
            AnimationClip opening = Get<AnimationClip>(book, "openClip");
            object idle = Get<object>(book, "motion"); float oldElapsed = Get<float>(book, "elapsed");
            var meshes = renderers.Select(r => new Mesh()).ToArray();
            var colliders = renderers.Select(r => new GameObject("Opening paper intersection check", typeof(MeshCollider))
                { hideFlags = HideFlags.HideAndDontSave }.GetComponent<MeshCollider>()).ToArray();
            Vector3 origin = Get<GameObject>(book, "studio").transform.position;
            foreach (var collider in colliders) collider.transform.position = origin;
            var edges = new HashSet<Vector2Int>();
            try
            {
                // Imported skins need not be CPU-readable; BakeMesh supplies the same
                // topology in a readable test-owned mesh without changing asset settings.
                pages[0].BakeMesh(meshes[main.Length]);
                int[] triangles = meshes[main.Length].GetTriangles(0);
                Require(triangles.Length >= 3 && triangles.Length % 3 == 0,
                    "Independent-leaf collision check reads nonempty baked front triangles", checks);
                for (int i = 0; i < triangles.Length; i += 3)
                    for (int edge = 0; edge < 3; edge++)
                    {
                        int a = triangles[i + edge], b = triangles[i + (edge + 1) % 3];
                        edges.Add(new Vector2Int(Mathf.Min(a, b), Mathf.Max(a, b)));
                    }
                Require(edges.Count >= 3, "Independent-leaf collision check tests nonempty mesh edges (" + edges.Count + ")", checks);
                foreach (bool cover in new[] { true, false })
                {
                    Vector2 minimum = Vector2.one * float.MaxValue, maximum = Vector2.one * float.MinValue;
                    Vector2 screenMin = minimum, screenMax = maximum;
                    for (int step = 0; step <= 20; step++)
                    {
                        float fraction = step / 20f;
                        Set(book, "motion", Enum.Parse(idle.GetType(), cover ? "Opening" : "Turning"));
                        Set(book, "openingSequence", true); Set(book, "openingFanPending", cover); Set(book, "openingTurn", !cover);
                        Set(book, "elapsed", fraction * (cover ? opening.length : LifeJournalOpeningFan.Duration));
                        Call(book, "SampleOpen", cover ? fraction * opening.length : opening.length);
                        if (!cover) { book.OpeningFan.Sample(fraction * LifeJournalOpeningFan.Duration); Call(book, "UpdatePresentation", 1f); }
                        var points = new Vector3[renderers.Length][];
                        for (int r = 0; r < renderers.Length; r++)
                        {
                            SkinnedMeshRenderer renderer = renderers[r]; colliders[r].sharedMesh = null; colliders[r].enabled = renderer.enabled;
                            if (!renderer.enabled) continue;
                            renderer.BakeMesh(meshes[r]); points[r] = meshes[r].vertices;
                            for (int v = 0; v < points[r].Length; v++)
                            {
                                Vector3 world = renderer.transform.TransformPoint(points[r][v]);
                                points[r][v] = world - origin;
                                Vector3 viewport = camera.WorldToViewportPoint(world);
                                minimum = Vector2.Min(minimum, viewport); maximum = Vector2.Max(maximum, viewport);
                                Vector3 ui = display.TransformPoint(new Vector3((viewport.x - .5f) * display.sizeDelta.x, (viewport.y - .5f) * display.sizeDelta.y, 0));
                                Vector2 screen = RectTransformUtility.WorldToScreenPoint(null, ui);
                                screenMin = Vector2.Min(screenMin, screen); screenMax = Vector2.Max(screenMax, screen);
                            }
                        }
                        bool checkIntersection = cover ? step == 18 || step == 20 : step == 4 || step == 8 || step == 12 || step == 16;
                        if (checkIntersection)
                        {
                            for (int r = 0; r < renderers.Length; r++)
                            {
                                if (!renderers[r].enabled) continue;
                                colliders[r].sharedMesh = null; meshes[r].vertices = points[r]; meshes[r].RecalculateBounds();
                                colliders[r].sharedMesh = meshes[r];
                            }
                            Physics.SyncTransforms();
                            for (int source = main.Length; source < renderers.Length; source++)
                            {
                                if (!renderers[source].enabled) continue;
                                for (int target = 0; target < renderers.Length; target++)
                                {
                                    if (source == target || !renderers[target].enabled || !colliders[source].bounds.Intersects(colliders[target].bounds)) continue;
                                    foreach (Vector2Int edge in edges)
                                    {
                                        Vector3 a = points[source][edge.x] + origin, b = points[source][edge.y] + origin;
                                        Vector3 delta = b - a; float distance = delta.magnitude;
                                        if (distance < .00004f) continue;
                                        bool hit = Crosses(colliders[target], a, delta / distance, distance) || Crosses(colliders[target], b, -delta / distance, distance);
                                        if (hit) Require(false, "Independent leaf penetrates " + renderers[target].name + " at " + (cover ? "cover " : "fan ") + fraction + ", leaf=" + (source - main.Length + 1), checks);
                                    }
                                }
                            }
                            checks.Add("PASS actual baked leaf edges do not cross other leaves or book surfaces at " + (cover ? "cover " : "fan ") + fraction.ToString("F2"));
                        }
                    }
                    string bounds = " viewport=" + minimum.ToString("F4") + ".." + maximum.ToString("F4") + ", screen=" + screenMin.ToString("F1") + ".." + screenMax.ToString("F1");
                    Require(minimum.x > .005f && minimum.y > .005f && maximum.x < .995f && maximum.y < .995f &&
                        screenMin.x >= 4 && screenMin.y >= 4 && screenMax.x <= Screen.width - 4 && screenMax.y <= Screen.height - 4,
                        "Every independent skin remains inside RT and screen across 21 " + (cover ? "opening" : "overlapping fan") + " poses;" + bounds, checks);
                }
            }
            finally
            {
                Set(book, "motion", idle); Set(book, "elapsed", oldElapsed);
                Set(book, "openingSequence", false); Set(book, "openingFanPending", false); Set(book, "openingTurn", false); book.OpeningFan.Hide();
                Call(book, "SampleOpen", opening.length); Call(book, "RenderBook");
                foreach (var collider in colliders) UnityEngine.Object.Destroy(collider.gameObject);
                foreach (Mesh mesh in meshes) UnityEngine.Object.Destroy(mesh);
            }
        }

        static bool Crosses(Collider target, Vector3 point, Vector3 direction, float length) =>
            target.Raycast(new Ray(point, direction), out RaycastHit hit, length) && hit.distance > .00002f && hit.distance < length - .00002f;

        static void CheckLeafDepthPrecision(SkinnedMeshRenderer leaf, Mesh mesh, List<string> checks)
        {
            leaf.BakeMesh(mesh);
            Vector3[] points = mesh.vertices; Vector2[] uv = mesh.uv;
            var back = new Dictionary<Vector2Int, int>();
            foreach (int index in mesh.GetIndices(1)) back[UvKey(uv[index])] = index;
            int pairs = 0, oldCollapsed = 0; float minimum = float.MaxValue;
            foreach (int front in mesh.GetIndices(0).Distinct())
            {
                if (!back.TryGetValue(UvKey(new Vector2(1 - uv[front].x, uv[front].y)), out int reverse)) continue;
                Vector3 a = leaf.transform.TransformPoint(points[front]), b = leaf.transform.TransformPoint(points[reverse]);
                minimum = Mathf.Min(minimum, Vector3.Distance(a, b)); pairs++;
                if (Vector3.Distance(a + Vector3.down * 9900f, b + Vector3.down * 9900f) < .00005f) oldCollapsed++;
            }
            Require(pairs > 1000 && minimum > .00015f,
                "Actual world-space leaf faces retain their thickness: pairs=" + pairs + ", minimum=" + (minimum * 1000).ToString("F3") + " mm", checks);
            Require(oldCollapsed > 0,
                "The previous far-away studio position reproduces collapsed front/back vertices (" + oldCollapsed + "), independently of snapshot loading", checks);
        }

        static Vector2Int UvKey(Vector2 uv) => new Vector2Int(Mathf.RoundToInt(uv.x * 10000), Mathf.RoundToInt(uv.y * 10000));

        static string PresentationState(LifeJournal3D book, GameplayHud hud)
        {
            CanvasGroup group = Get<RectTransform>(hud, "albumRoot")?.GetComponent<CanvasGroup>();
            return "motion=" + book.MotionName + ", progress=" + book.AnimationProgress.ToString("F4") +
                ", alpha=" + book.PresentationOpacity.ToString("F4") + ", angle=" + book.ViewingAngle.ToString("F3") +
                ", raycast=" + (group && group.blocksRaycasts) + ", interact=" + (group && group.interactable) +
                ", unityFrame=" + Time.frameCount + ", presented=" + Get<int>(book, "presentationFrames") +
                ", rawDelta=" + Get<float>(book, "lastRawStep").ToString("F4") + ", appliedDelta=" + Get<float>(book, "lastStep").ToString("F4") +
                ", maximumRawDelta=" + Get<float>(book, "maximumRawStep").ToString("F4");
        }

        static void CheckProjectedWord(LifeJournal3D book, RectTransform word, List<string> checks)
        {
            object layout = Get<IDictionary>(book, "layouts")[word];
            Vector2 original = (Vector2)layout.GetType().GetField("position").GetValue(layout);
            Vector2 uv = new Vector2((original.x - 17.244f) / 557.512f, (original.y + 355.756f) / 698.964f);
            var page = book.Model.GetComponentsInChildren<SkinnedMeshRenderer>().First(r => r.name == "PageRight");
            var mesh = new Mesh();
            try
            {
                page.BakeMesh(mesh); Vector2[] texcoords = mesh.uv; Vector3[] vertices = mesh.vertices; int[] triangles = mesh.triangles;
                bool found = false; Vector3 world = default;
                for (int i = 0; i < triangles.Length; i += 3)
                {
                    int a = triangles[i], b = triangles[i + 1], c = triangles[i + 2];
                    Vector2 ab = texcoords[b] - texcoords[a], ac = texcoords[c] - texcoords[a], ap = uv - texcoords[a];
                    float determinant = ab.x * ac.y - ab.y * ac.x;
                    if (Mathf.Abs(determinant) < .000001f) continue;
                    float v = (ap.x * ac.y - ap.y * ac.x) / determinant, w = (ab.x * ap.y - ab.y * ap.x) / determinant;
                    if (v < -.0001f || w < -.0001f || v + w > 1.0001f) continue;
                    world = page.transform.TransformPoint(vertices[a] * (1 - v - w) + vertices[b] * v + vertices[c] * w); found = true; break;
                }
                Require(found, "The sampled word lies on the actual authored paper UV triangles", checks);
                var view = Get<Camera>(book, "view"); var display = Get<RawImage>(book, "image").rectTransform;
                Vector3 point = view.WorldToViewportPoint(world);
                Vector3 projected = display.TransformPoint(new Vector3((point.x - .5f) * display.sizeDelta.x, (point.y - .5f) * display.sizeDelta.y, 0));
                Vector2 expected = RectTransformUtility.WorldToScreenPoint(null, projected), actual = RectTransformUtility.WorldToScreenPoint(null, word.position);
                float scale = word.GetComponentInParent<Canvas>().scaleFactor;
                Require(Vector2.Distance(expected, actual) <= 4f * scale,
                    "The real curved-paper word projection agrees with its UI hit center within four reference pixels", checks);
            }
            finally { UnityEngine.Object.Destroy(mesh); }
        }

        static void CheckNavigationCapture(RectTransform album, LifeJournal3D book, List<string> checks)
        {
            Transform[] navigation = new[] { "Symbols tab", "Photos tab", "Gesture learning tab", "Photography tasks tab", "Close album", "Next symbols" }.Select(name => album.Find(name)).ToArray();
            Graphic[] graphics = navigation.SelectMany(part => part.GetComponentsInChildren<Graphic>(true)).ToArray();
            Color[] colors = graphics.Select(graphic => graphic.color).ToArray();
            RenderTexture target = Get<RenderTexture>(book, "before");
            try
            {
                Call(book, "Capture", target); Color32[] normal = ReadCapture(target);
                foreach (Graphic graphic in graphics) graphic.color = Color.magenta;
                Call(book, "Capture", target); Color32[] tinted = ReadCapture(target);
                Require(normal.SequenceEqual(tinted), "Changing every navigation graphic leaves the actual captured paper pixels unchanged", checks);
                Require(navigation.All(part => part.GetComponent<CanvasGroup>().alpha == 1 && part.GetComponent<CanvasGroup>().interactable),
                    "Capturing pages restores the separate navigation interaction layer", checks);
            }
            finally
            {
                for (int i = 0; i < graphics.Length; i++) if (graphics[i]) graphics[i].color = colors[i];
                Call(book, "Capture", target);
            }
        }

        static Color32[] ReadCapture(RenderTexture source)
        {
            RenderTexture previous = RenderTexture.active;
            var pixels = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false);
            try
            {
                RenderTexture.active = source;
                pixels.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0);
                return pixels.GetPixels32();
            }
            finally { RenderTexture.active = previous; UnityEngine.Object.Destroy(pixels); }
        }

        static void CheckDetailSizes(GameplayHud hud, List<string> checks)
        {
            RectTransform original = Get<RectTransform>(hud, "canvasRect"), panel = Get<RectTransform>(hud, "detailRoot");
            var fixture = new GameObject("UI layout size check", typeof(RectTransform)) { hideFlags = HideFlags.HideAndDontSave };
            var canvas = (RectTransform)fixture.transform;
            try
            {
                Set(hud, "canvasRect", canvas);
                foreach (Vector2 size in new[] { new Vector2(900, 450), new Vector2(650, 1000), new Vector2(2400, 720), new Vector2(1600, 1000) })
                {
                    canvas.sizeDelta = size; Call(hud, "FitDetails");
                    Vector3 scale = panel.localScale; Vector2 drawn = panel.sizeDelta * scale.x;
                    Require(Mathf.Approximately(scale.x, scale.y) && scale.x <= 1 && drawn.x <= size.x - 48 + .01f && drawn.y <= size.y - 48 + .01f,
                        "Photo detail retains aspect and margins for canvas " + size, checks);
                }
            }
            finally { Set(hud, "canvasRect", original); Call(hud, "FitDetails"); UnityEngine.Object.Destroy(fixture); }
        }

        static int OwnedMaterials() => Resources.FindObjectsOfTypeAll<Material>().Count(m => m &&
            (m.name.StartsWith("Journal surface:", StringComparison.Ordinal) || m.name == "Journal page snapshot" || m.name == "Journal opening leaf" || m.name == "Gesture ink (runtime)"));
        static int JournalTargets() => Resources.FindObjectsOfTypeAll<RenderTexture>().Count(t => t &&
            (t.name == "Journal 3D view" || t.name == "Journal current page" || t.name == "Journal next page"));
        static T Get<T>(object owner, string field) => (T)owner.GetType().GetField(field, Private).GetValue(owner);
        static void Set(object owner, string field, object value) => owner.GetType().GetField(field, Private).SetValue(owner, value);
        static void Call(object owner, string method, params object[] args) => owner.GetType().GetMethod(method, Private).Invoke(owner, args);
        static void Require(bool value, string message, List<string> checks)
        { if (!value) throw new InvalidOperationException(message); checks.Add("PASS " + message); }
    }
}
