using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using WeatherHousePrototype;

namespace WeatherHouse.Life.Editor
{
    // Runs only in this review's batch editor and isolated save; never saves the scene.
    [InitializeOnLoad]
    public static class LifeCastRangeReview
    {
        const string Key = "WeatherHouse.CastRangeReview20261028";
        const string Folder = "LifeGameplayReview/20261028/weather-rim-runtime";
        const string ReleaseRequest = Folder + "/release-idle-editor.txt";
        const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        static LifeCastRangeReview() { EditorApplication.update += Tick; }

        public static void Begin()
        {
            if (!Application.isBatchMode || EditorApplication.isPlayingOrWillChangePlaymode || SceneManager.GetActiveScene().isDirty)
                throw new InvalidOperationException("Use an owned, idle batch editor for this review.");
            Directory.CreateDirectory(Folder);
            SessionState.SetBool(Key, true);
            LifeFeedbackReview.Begin();
        }
        static void Tick()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            if (!Application.isBatchMode)
            {
                // Explicit handoff only: never stop Play or discard any loaded scene's edits.
                if (!File.Exists(ReleaseRequest)) return;
                string request = File.ReadAllText(ReleaseRequest).Trim(); File.Delete(ReleaseRequest);
                bool dirty = Enumerable.Range(0, SceneManager.sceneCount).Any(i => SceneManager.GetSceneAt(i).isDirty);
                if (request != "release-for-isolated-range-review" || EditorApplication.isPlayingOrWillChangePlaymode || dirty)
                { File.WriteAllText(Folder + "/editor-release.txt", "REFUSED: editor is not idle and clean, or request is invalid."); return; }
                File.WriteAllText(Folder + "/editor-release.txt", "RELEASED idle clean editor\n" + SceneManager.GetActiveScene().path + "\n" + DateTime.UtcNow.ToString("O"));
                EditorApplication.Exit(0); return;
            }
            if (SessionState.GetBool(Key + ".quit", false) && !EditorApplication.isPlayingOrWillChangePlaymode &&
                !SessionState.GetBool("WeatherHouse.FeedbackReview20261028", false))
            { SessionState.SetBool(Key + ".quit", false); LifeFeedbackReview.ExitBatch(); return; }
            if (!SessionState.GetBool(Key, false) || !EditorApplication.isPlaying ||
                !SessionState.GetBool("WeatherHouse.FeedbackReview20261028.ready", false)) return;
            var life = UnityEngine.Object.FindObjectOfType<LifeSession>();
            if (!life || !life.Gameplay) return;
            SessionState.SetBool(Key, false); life.StartCoroutine(Run(life));
        }
        static IEnumerator Run(LifeSession life)
        {
            var checks = new List<string>();
            var weather = (LifeLocalWeather)typeof(LifeSession).GetField("weather", Hidden).GetValue(life);
            var range = UnityEngine.Object.FindObjectOfType<LifeCastRange>();
            var view = range.GetComponent<MeshRenderer>();
            var player = life.Gameplay.PlayerTransform;
            Vector3 originalPosition = player.position;
            int originalCaptureRate = Time.captureFramerate;
            bool originalDirty = SceneManager.GetActiveScene().isDirty;
            void Check(bool condition, string label)
            { if (!condition) throw new InvalidOperationException(label); checks.Add("PASS " + label); }
            float Property(string name)
            { var block = new MaterialPropertyBlock(); view.GetPropertyBlock(block); return block.GetFloat(name); }
            var run = Steps();
            try
            {
                while (true)
                {
                    bool more;
                    try { more = run.MoveNext(); }
                    catch (Exception error) { checks.Add("FAIL " + error); break; }
                    if (!more) break;
                    yield return run.Current;
                }
            }
            finally
            {
                Time.captureFramerate = originalCaptureRate;
                player.position = originalPosition;
                weather.Cast("clear", out _); life.SetGestureInput(false);
                checks.Add((SceneManager.GetActiveScene().isDirty == originalDirty ? "PASS " : "FAIL ") + "Scene dirty state preserved; no scene save");
                File.WriteAllLines(Folder + "/checks.txt", checks);
                SessionState.SetBool(Key + ".quit", true);
                LifeFeedbackReview.Finish();
            }

            IEnumerator Steps()
            {
                Check(!weather.Active && !view.enabled, "No weather leaves the boundary hidden");
                Check(view.sharedMaterial && view.sharedMaterial.shader.isSupported, "Production boundary shader is supported");
                Check(!ShaderUtil.GetShaderMessages(view.sharedMaterial.shader).Any(m => m.severity.ToString() == "Error"), "Production shader has no compile errors");
                Check(life.TryCast("rain", out _), "Rain starts through the production spell entry");
                yield return new WaitForSeconds(.9f);
                Check(weather.IsRain && view.enabled && Property("_Flames") > .95f, "Rain maintains visible flames after the activation sweep");
                // Startup frames can be long; observe completed LateUpdate state rather than an earlier frame's block.
                float revealDeadline = Time.time + 2f;
                while (Property("_Reveal") < .99f && Time.time < revealDeadline) yield return null;
                Check(Property("_Reveal") > .99f, "Activation completes its sweep without removing the maintained boundary; reveal=" + Property("_Reveal"));
                var points = range.GetComponent<MeshFilter>().sharedMesh.vertices;
                float radius = LifeSession.CastRadius;
                Check(points.Length > 400 && points.Where((p,i) => i % 4 == 2).All(p => Mathf.Abs(new Vector2(p.x,p.z).magnitude-radius)<.005f),
                    "Flame feet stay exactly on the existing gameplay radius");
                Check(points.Where((p,i) => i % 4 == 3).Select((p,i) => p.y-points[i*4+2].y).All(h => h > .3f && h < 1.2f),
                    "Upright flame strip has bounded height instead of a tall wall");
                Check(view.shadowCastingMode == UnityEngine.Rendering.ShadowCastingMode.Off && !view.receiveShadows,
                    "Light boundary does not cast opaque geometry shadows");
                player.position += new Vector3(1.25f,0,.75f);
                yield return new WaitForSeconds(.3f);
                Check(Vector3.Distance(range.transform.position, player.position) < .01f, "Boundary follows the actual player position");
                player.position = originalPosition;
                life.TryCast("clear", out _);
                yield return null;
                Check(!weather.Active && view.enabled && Property("_Visibility") > 0, "Clear begins a fade instead of an abrupt disappearance");
                yield return new WaitForSeconds(.6f);
                Check(!view.enabled, "All boundary light disappears after clear");
                life.SetGestureInput(true);
                yield return new WaitForSeconds(.5f);
                Check(view.enabled && Property("_Flames") < .001f, "Gesture aiming alone shows a ground guide without claiming active weather");
                life.SetGestureInput(false);
                yield return new WaitForSeconds(.6f);
                Check(!view.enabled, "Closing the aim guide hides the boundary when weather is inactive");

                life.Gameplay.Notebook.AddObservation(new ObservationRecord { id = "rim-review-photo", glyphIds = new[] { "sunlight", "wind" }, frameCount = 1 });
                Check(life.TryCast("wind", out _), "Wind starts through the production spell entry");
                yield return new WaitForSeconds(.8f);
                Check(view.enabled && Property("_Flames") > .95f, "Wind retains the same approved flame boundary");
                life.TryCast("clear", out _);
                yield return new WaitForSeconds(.6f);
                Check(life.TryCast("sunlight", out _), "Sunlight starts through the production spell entry");
                yield return new WaitForSeconds(1f);
                Check(view.enabled && Property("_Flames") > .95f, "Sunlight retains the approved flame boundary");
                LifeFeedbackVisualReview.Frame(life, "01_概念图对应实机.png", Folder);
                Directory.CreateDirectory(Folder + "/frames");
                Time.captureFramerate = 12;
                for (int frame = 0; frame < 36; frame++)
                {
                    yield return null;
                    LifeFeedbackVisualReview.Frame(life, "frame_" + frame.ToString("D3") + ".png", Folder + "/frames");
                }
                Time.captureFramerate = originalCaptureRate;
                Check(view.enabled, "Boundary persists throughout the animation capture");
                life.TryCast("clear", out _);
                yield return new WaitForSeconds(.7f);
                Check(!view.enabled, "Final clear hides the maintained sunlight boundary");
                LifeFeedbackVisualReview.Frame(life, "02_收起后.png", Folder);
                LifeWeatherFeedbackChecks.Run();
                File.WriteAllText(Folder + "/source.txt", "Actual isolated Unity Play in WeatherHouse_WatersideV2; production LifeSession, LifeCastRange and shader. Normal player saves and scene files are not written. The photo unlock record is a declared test fixture. Camera.Render captures actual world and HUD; 36 frames advance game time at 12 fps. This is not an image composite.\n" + DateTime.UtcNow.ToString("O"));
            }
        }
    }
}
