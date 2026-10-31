using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using WeatherHouse.Hillside;
using WeatherHousePrototype;

namespace WeatherHouse.Life.Editor
{
    // Parent review supplies the isolated save directory and owns all Play/editor state.
    public static class LifeOpeningCinematicChecks
    {
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

        public static IEnumerator Run(GameplaySession session, Action<string> completed)
        {
            var lines = new List<string>();
            GameObject host = null;
            try
            {
                Require(Application.isPlaying && session && !string.IsNullOrWhiteSpace(
                    Environment.GetEnvironmentVariable("WEATHERHOUSE_LIFE_REVIEW_SAVE")), "Uses the isolated parent Play review", lines);
                Require(!UnityEngine.Object.FindObjectsOfType<LifeOpeningCinematic>().Any(c => c.IsPlaying),
                    "No other opening owns presentation", lines);
                foreach (Vector2 size in new[] { new Vector2(1920, 1080), new Vector2(1600, 1000), new Vector2(1024, 768), new Vector2(3440, 1440) })
                {
                    Rect viewport = LifeOpeningCinematic.Viewport((int)size.x, (int)size.y);
                    Require(Mathf.Abs(size.x * viewport.width / (size.y * viewport.height) - 16f / 9f) < .00001f,
                        "The movie frame stays 16:9 at " + size, lines);
                }
                Camera source = session.PlayerViewCamera;
                Require(source, "Existing player camera is available", lines);
                host = new GameObject("Opening lifecycle checks (runtime)") { hideFlags = HideFlags.DontSave };
                var cinematic = host.AddComponent<LifeOpeningCinematic>();
                var before = new Snapshot(session);
                int callbacks = 0;
                Require(cinematic.Begin(session, () => callbacks++), "Explicit Begin starts the opening", lines);
                Require(cinematic.IsPlaying && !source.enabled && View(cinematic) != source,
                    "The independent movie camera owns only the temporary view", lines);
                Require(!cinematic.Begin(session, () => callbacks += 100), "Repeated Begin cannot replace the active callback", lines);
                Step(cinematic, 14f);
                Require(Vector3.Distance(View(cinematic).transform.position, before.position) > 10f,
                    "Map tour moves the temporary camera", lines);
                Require(source.transform.position == before.position && source.transform.rotation == before.rotation && source.rect == before.rect,
                    "Tour leaves the original camera pose and viewport untouched", lines);
                cinematic.Skip(); cinematic.Skip();
                Require(callbacks == 1 && !cinematic.IsPlaying && before.Restored(session),
                    "Skip restores camera, grass bindings, HUD visibility and existing input flags once", lines);

                before = new Snapshot(session);
                Require(cinematic.Begin(session, () => callbacks++), "Complete-playback fixture starts", lines);
                Step(cinematic, 40f);
                RectTransform bubble = (RectTransform)typeof(LifeOpeningCinematic).GetField("bubble", Private).GetValue(cinematic);
                Require(source.enabled == before.enabled && bubble.gameObject.activeInHierarchy &&
                    bubble.GetComponent<ProceduralUiGraphic>().mark == UiMark.PlayerBubble &&
                    bubble.GetComponentInChildren<Text>().text == "可是我还什么都不会啊！",
                    "Original game view returns before the readable tail-free witch thought", lines);
                Step(cinematic, 4f);
                Require(callbacks == 2 && before.Restored(session), "Full 44-second playback restores its captured state", lines);

                before = new Snapshot(session);
                cinematic.ReducedMotion = true;
                Require(cinematic.Begin(session, () => callbacks++), "Reduced-motion fixture starts", lines);
                Pose held = new Pose(View(cinematic).transform.position, View(cinematic).transform.rotation);
                Step(cinematic, 25f);
                Require(View(cinematic).transform.position == held.position && View(cinematic).transform.rotation == held.rotation,
                    "Reduced motion holds composition while the story advances", lines);
                Step(cinematic, 19f);
                Require(callbacks == 3 && before.Restored(session), "Reduced motion finishes with complete restoration", lines);

                cinematic.ReducedMotion = false;
                before = new Snapshot(session);
                Require(cinematic.Begin(session, () => callbacks++), "Cancel fixture starts", lines);
                Step(cinematic, 6f); cinematic.Cancel(); cinematic.Cancel();
                Require(callbacks == 3 && !cinematic.IsPlaying && before.Restored(session),
                    "Cancel is idempotent and does not mark the opening completed", lines);

                before = new Snapshot(session);
                Require(cinematic.Begin(session, () => callbacks++), "Disable fixture starts", lines);
                cinematic.enabled = false;
                Require(callbacks == 3 && !cinematic.IsPlaying && before.Restored(session),
                    "OnDisable restores state without completing the opening", lines);
                lines.Add("These synchronous lifecycle checks do not establish rendered framing or visual acceptance.");
                completed?.Invoke("PASS opening cinematic lifecycle\n" + string.Join("\n", lines));
            }
            catch (Exception error) { completed?.Invoke("FAIL opening cinematic lifecycle\n" + string.Join("\n", lines) + "\n" + error); }
            finally { if (host) UnityEngine.Object.Destroy(host); }
            yield break;
        }

        static Camera View(LifeOpeningCinematic cinematic) =>
            (Camera)typeof(LifeOpeningCinematic).GetField("view", Private).GetValue(cinematic);
        static void Step(LifeOpeningCinematic cinematic, float delta) =>
            typeof(LifeOpeningCinematic).GetMethod("Step", Private).Invoke(cinematic, new object[] { delta });
        static void Require(bool value, string label, List<string> lines)
        { if (!value) throw new InvalidOperationException(label); lines.Add("PASS " + label); }

        sealed class Snapshot
        {
            public readonly Vector3 position;
            public readonly Quaternion rotation;
            public readonly Rect rect;
            public readonly bool enabled;
            readonly string cameraJson;
            readonly Dictionary<HillsideGrassRenderer, Camera> grass;
            readonly Dictionary<Canvas, bool> hud;
            readonly bool[] input;
            public Snapshot(GameplaySession session)
            {
                Camera camera = session.PlayerViewCamera;
                position = camera.transform.position; rotation = camera.transform.rotation; rect = camera.rect; enabled = camera.enabled;
                cameraJson = EditorJsonUtility.ToJson(camera);
                grass = UnityEngine.Object.FindObjectsOfType<HillsideGrassRenderer>(true).ToDictionary(g => g, g => g.viewCamera);
                hud = session.GetComponentsInChildren<Canvas>(true).ToDictionary(c => c, c => c.enabled);
                input = InputFlags(session);
            }
            public bool Restored(GameplaySession session) => position == session.PlayerViewCamera.transform.position &&
                rotation == session.PlayerViewCamera.transform.rotation && rect == session.PlayerViewCamera.rect &&
                cameraJson == EditorJsonUtility.ToJson(session.PlayerViewCamera) &&
                grass.All(g => g.Key && g.Key.viewCamera == g.Value) && hud.All(c => c.Key && c.Key.enabled == c.Value) &&
                input.SequenceEqual(InputFlags(session));
            static bool[] InputFlags(GameplaySession session) => session.forestSprite ?
                new[] { session.forestSprite.InputBlocked, session.forestSprite.JumpBlocked, session.forestSprite.CameraInputBlocked } :
                new[] { session.walker.InputBlocked, session.walker.JumpBlocked, session.walker.CameraInputBlocked };
        }
    }
}
