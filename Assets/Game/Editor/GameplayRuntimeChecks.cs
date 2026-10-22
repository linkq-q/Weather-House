using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace WeatherHousePrototype
{
    public static class GameplayRuntimeChecks
    {
        private const string ReportPath = "PrototypeReview/gameplay-runtime.txt";
        private static bool running;

        [MenuItem("Weather House/Check Gameplay Runtime")]
        public static void Run()
        {
            if (running) throw new InvalidOperationException("Gameplay runtime checks are already running.");
            Directory.CreateDirectory("PrototypeReview");
            var report = new List<string> { "RUNNING: Gameplay runtime integration checks", DateTime.Now.ToString("O") };
            File.WriteAllLines(ReportPath, report);
            try
            {
                Require(EditorApplication.isPlaying, "Gameplay runtime checks require Play mode.");
                var session = Object.FindObjectOfType<GameplaySession>();
                Require(session != null && session.isActiveAndEnabled, "An active GameplaySession is required.");
                Require(!session.Judging && (session.Recorder == null || !session.Recorder.IsRecording),
                    "Wait for the active language judgement or photo capture to finish before running checks.");
                running = true;
                session.StartCoroutine(RunGuarded(CheckSession(session, report), report));
            }
            catch (Exception exception)
            {
                running = false;
                report.Add("FAIL: " + exception);
                File.WriteAllLines(ReportPath, report);
                throw;
            }
        }

        // Drive nested iterators ourselves so a child coroutine failure reaches the report.
        private static IEnumerator RunGuarded(IEnumerator checks, List<string> report)
        {
            bool previousRunInBackground = Application.runInBackground;
            Application.runInBackground = true;
            var stack = new Stack<IEnumerator>();
            stack.Push(checks);
            Exception failure = null;
            bool completedAll = false;
            float startedAt = Time.realtimeSinceStartup;
            try
            {
                while (stack.Count > 0)
                {
                    if (Time.realtimeSinceStartup - startedAt > 30f)
                    {
                        failure = new TimeoutException("Gameplay runtime checks exceeded 30 seconds.");
                        break;
                    }
                    bool moved = false;
                    object yielded = null;
                    try
                    {
                        IEnumerator current = stack.Peek();
                        moved = current.MoveNext();
                        if (moved) yielded = current.Current;
                        else
                        {
                            (current as IDisposable)?.Dispose();
                            stack.Pop();
                        }
                    }
                    catch (Exception exception) { failure = exception; }
                    if (failure != null) break;
                    if (!moved) continue;
                    if (yielded is IEnumerator nested)
                    {
                        stack.Push(nested);
                        continue;
                    }
                    yield return yielded;
                }
                completedAll = stack.Count == 0 && failure == null;
            }
            finally
            {
                if (!completedAll && failure == null)
                    failure = new OperationCanceledException("Gameplay checks stopped before completion.");
                while (stack.Count > 0)
                {
                    try { (stack.Pop() as IDisposable)?.Dispose(); }
                    catch (Exception exception) { if (failure == null) failure = exception; }
                }
                Application.runInBackground = previousRunInBackground;
                running = false;
                report.Add(failure == null ? "PASS: gameplay runtime integration checks completed." : "FAIL: " + failure);
                report.Add("Elapsed: " + (Time.realtimeSinceStartup - startedAt).ToString("F2") + " s");
                File.WriteAllLines(ReportPath, report);
                if (failure != null) Debug.LogError("Gameplay runtime check failed. See " + ReportPath + "\n" + failure);
                else Debug.Log("Gameplay runtime checks passed. See " + ReportPath);
            }
        }

        private static IEnumerator CheckSession(GameplaySession session, List<string> report)
        {
            Require(session.Notebook != null && session.Catalog?.glyphs != null, "Notebook or glyph catalog is missing.");
            Require(session.Catalog.glyphs.Length == 24, "The glyph catalog must contain 24 definitions.");
            Require(session.Catalog.glyphs.All(g => g != null && !string.IsNullOrWhiteSpace(g.id)), "The glyph catalog has missing IDs.");
            var glyphIds = new HashSet<string>(session.Catalog.glyphs.Select(g => g.id));
            Require(glyphIds.Count == 24, "The glyph catalog contains duplicate IDs.");
            Require(session.walker != null && session.walker.viewCamera != null && session.walker.mapCamera != null,
                "Player or walking/map camera dependency is missing.");
            Require(session.Recorder != null && session.Recorder == session.GetComponent<MotionPhotoRecorder>(), "The session recorder is not bound.");
            Require(session.GetComponent<LanguageJudgementClient>() != null, "The language judgement client is missing.");
            var hud = session.GetComponent<GameplayHud>();
            Require(hud != null, "The GameplayHud component is missing.");
            Require(session.regions != null && session.regions.Length > 0 && session.regions.All(r => r != null),
                "Scene weather regions are missing.");
            Require(session.animals != null && session.animals.Length > 0 && session.animals.All(a => a != null),
                "Scene animals are missing.");
            Require(session.animals.All(a => !string.IsNullOrWhiteSpace(a.animalId)) &&
                session.animals.Select(a => a.animalId).Distinct().Count() == session.animals.Length,
                "Animal IDs are missing or duplicated.");
            foreach (WeatherRegion region in session.regions) CheckRegionBounds(region);
            foreach (AnimalActor animal in session.animals)
            {
                Require(animal.region != null && Array.IndexOf(session.regions, animal.region) >= 0,
                    "Animal " + animal.animalId + " is not linked to a scene weather region.");
                Require(animal.requestGlyphIds != null && animal.requestGlyphIds.Length > 0 &&
                    animal.satisfiedGlyphIds != null && animal.satisfiedGlyphIds.Length > 0,
                    "Animal " + animal.animalId + " has missing utterance glyphs.");
                Require(animal.requestGlyphIds.Concat(animal.satisfiedGlyphIds).All(glyphIds.Contains),
                    "Animal " + animal.animalId + " references an unknown glyph.");
            }
            report.Add("PASS: 24 unique glyphs; scene player, cameras, recorder, judgement, HUD, " +
                session.regions.Length + " regions and " + session.animals.Length + " animals are linked.");
            File.WriteAllLines(ReportPath, report);

            var walker = session.walker;
            bool sessionEnabled = session.enabled, hudEnabled = hud.enabled, walkerEnabled = walker.enabled;
            bool inputBlocked = walker.InputBlocked, jumpBlocked = walker.JumpBlocked, legacyHint = walker.ShowLegacyHint;
            Canvas[] canvases = hud.GetComponentsInChildren<Canvas>(true);
            bool[] canvasEnabled = canvases.Select(c => c.enabled).ToArray();
            string notebookBefore = JsonUtility.ToJson(session.Notebook);
            byte[] saveBefore = File.Exists(session.SavePath) ? File.ReadAllBytes(session.SavePath) : null;
            Vector3 playerPosition = walker.transform.position;
            Quaternion playerRotation = walker.transform.rotation;
            GameObject fixture = null;
            try
            {
                session.enabled = false;
                hud.enabled = false;
                foreach (Canvas canvas in canvases) canvas.enabled = false;
                walker.enabled = false;
                walker.InputBlocked = true;
                walker.JumpBlocked = true;

                if (!hud.JournalOpen && !hud.BlocksMovement && canvases.Length > 0)
                {
                    bool cameraMode = hud.CameraMode;
                    try
                    {
                        hud.SetCameraMode(true);
                        Require(hud.CameraMode, "HUD camera mode did not open.");
                        hud.ToggleJournal();
                        Require(hud.JournalOpen && hud.BlocksMovement && !hud.CameraMode,
                            "The journal did not open, block walking and leave camera mode.");
                        hud.ToggleJournal();
                        Require(!hud.JournalOpen && !hud.BlocksMovement, "Closing the journal did not release movement.");
                        report.Add("PASS: HUD camera mode and journal open/close state; journal blocks movement.");
                    }
                    finally
                    {
                        if (hud.JournalOpen) hud.ToggleJournal();
                        hud.SetCameraMode(cameraMode);
                    }
                }
                else report.Add("HUD mode toggles skipped: a panel/input was already open or HUD Start has not completed.");

                fixture = new GameObject("Isolated gameplay weather check");
                fixture.transform.position = new Vector3(20000f, 20000f, 20000f);
                var regionObject = new GameObject("Isolated weather region");
                regionObject.transform.SetParent(fixture.transform, false);
                WeatherRegion region = regionObject.AddComponent<WeatherRegion>();
                CheckRegionBounds(region);
                Require(region.Current == LocalWeather.Rain && region.Wetness > .9f, "Rain region did not initialize wet.");
                var rain = region.transform.Find("Local Rain")?.GetComponent<ParticleSystem>();
                var wind = region.transform.Find("Local Wind Leaves")?.GetComponent<ParticleSystem>();
                Require(rain != null && wind != null, "Local rain or wind effects were not created.");
                Require(rain.isEmitting, "Rain weather is not emitting rain particles.");

                AnimalActor source = session.animals.FirstOrDefault(a => !a.background &&
                    (a.requestedWeather == LocalWeather.Sun || a.requestedWeather == LocalWeather.Rain));
                Require(source != null, "A key animal requesting rain or sun is required for the rain/sun interaction check.");
                var animalObject = new GameObject("Isolated animal behaviour check");
                animalObject.SetActive(false);
                animalObject.transform.SetParent(fixture.transform, false);
                AnimalActor animal = animalObject.AddComponent<AnimalActor>();
                animal.animalId = source.animalId;
                animal.species = source.species;
                animal.region = region;
                animal.requestedWeather = source.requestedWeather;
                animal.requestGlyphIds = (string[])source.requestGlyphIds.Clone();
                animal.satisfiedGlyphIds = (string[])source.satisfiedGlyphIds.Clone();
                animal.visualMaterial = source.visualMaterial;
                animalObject.SetActive(true);
                animal.enabled = false;
                int objectsBefore = animal.GetComponentsInChildren<Transform>(true).Length;
                int renderersBefore = animal.GetComponentsInChildren<Renderer>(true).Length;
                animal.BuildVisual();
                animal.BuildVisual();
                Require(objectsBefore == animal.GetComponentsInChildren<Transform>(true).Length &&
                    renderersBefore == animal.GetComponentsInChildren<Renderer>(true).Length,
                    "Repeated BuildVisual duplicated animal geometry.");

                region.SetWeather(LocalWeather.Rain);
                CheckAnimalWeather(animal, LocalWeather.Rain);
                string rainyUtterance = animal.UtteranceId;
                string[] rainyGlyphs = (string[])animal.CurrentGlyphIds.Clone();
                float wetnessBefore = region.Wetness;
                region.SetWeather(LocalWeather.Sun);
                Require(region.Current == LocalWeather.Sun && !rain.isEmitting, "Sun did not change the region and stop rain emission.");
                CheckAnimalWeather(animal, LocalWeather.Sun);
                Require(animal.UtteranceId != rainyUtterance && !animal.CurrentGlyphIds.SequenceEqual(rainyGlyphs),
                    "Animal utterance did not change after rain/sun changed request satisfaction.");
                yield return new WaitForSecondsRealtime(.6f);
                Require(region.Wetness < wetnessBefore - .001f, "Sun did not start drying the wet region within 0.6 seconds.");
                float wetnessAfter = region.Wetness;
                region.SetWeather(LocalWeather.Wind);
                Require(region.Current == LocalWeather.Wind && wind.isEmitting && !rain.isEmitting,
                    "Wind did not replace rain with wind particles.");
                report.Add("PASS: region radius boundaries; rain/sun/wind effect switching; sun wetness " +
                    wetnessBefore.ToString("F3") + " -> " + wetnessAfter.ToString("F3") +
                    "; animal satisfaction and glyph changes; BuildVisual does not duplicate children.");
                File.WriteAllLines(ReportPath, report);

                MotionPhotoChecks.RunStaticChecks();
                report.Add("PASS: motion photo path, frame index and missing-file checks.");
                yield return MotionPhotoChecks.RunPlayChecks();
                report.Add("PASS: actual 18-frame camera capture, pixel motion, metadata isolation and incomplete-clip cleanup.");
                Require(JsonUtility.ToJson(session.Notebook) == notebookBefore, "Runtime checks changed the player's in-memory notebook.");
                Require(saveBefore == null ? !File.Exists(session.SavePath) :
                    File.Exists(session.SavePath) && saveBefore.SequenceEqual(File.ReadAllBytes(session.SavePath)),
                    "Runtime checks changed the player's saved notebook.");
                Require(walker.transform.position == playerPosition && walker.transform.rotation == playerRotation,
                    "Runtime checks moved the player.");
                report.Add("PASS: player transform, notebook contents and saved notebook are unchanged.");
                report.Add("Boundary: HUD visual layout, real AI answers and whole-scene subjective presentation are not asserted here.");
            }
            finally
            {
                if (fixture != null) Object.Destroy(fixture);
                for (int i = 0; i < canvases.Length; i++)
                    if (canvases[i] != null) canvases[i].enabled = canvasEnabled[i];
                if (hud != null) hud.enabled = hudEnabled;
                if (session != null) session.enabled = sessionEnabled;
                if (walker != null)
                {
                    walker.enabled = walkerEnabled;
                    walker.InputBlocked = inputBlocked;
                    walker.JumpBlocked = jumpBlocked;
                    walker.ShowLegacyHint = legacyHint;
                }
            }
        }

        private static void CheckRegionBounds(WeatherRegion region)
        {
            Require(region.radius > 0f && region.Contains(region.transform.position), "Weather region does not contain its center.");
            Require(region.Contains(region.transform.position + Vector3.right * region.radius), "Weather region excludes its radius boundary.");
            Require(!region.Contains(region.transform.position + Vector3.right * (region.radius + .1f)), "Weather region includes a point outside its radius.");
            Require(region.Contains(region.transform.position + Vector3.up * 100f), "Horizontal weather coverage incorrectly depends on altitude.");
        }

        private static void CheckAnimalWeather(AnimalActor animal, LocalWeather weather)
        {
            bool satisfied = animal.requestedWeather == weather;
            Require(animal.RequestSatisfied == satisfied, "Animal request satisfaction does not match its weather.");
            Require(animal.CurrentGlyphIds.SequenceEqual(satisfied ? animal.satisfiedGlyphIds : animal.requestGlyphIds),
                "Animal glyphs do not match its current request satisfaction.");
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
