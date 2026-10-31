using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using WeatherHousePrototype;

namespace WeatherHouse.Life.Editor
{
    public static class LifeWeatherFeedbackChecks
    {
        const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;

        [MenuItem("Weather House/Review 20261027/Check weather feedback semantics")]
        public static void Run()
        {
            var life = UnityEngine.Object.FindObjectOfType<LifeSession>();
            string directory = Environment.GetEnvironmentVariable("WEATHERHOUSE_LIFE_REVIEW_SAVE");
            if (!Application.isBatchMode || !Application.isPlaying || !SessionState.GetBool("WeatherHouse.FeedbackReview20261028", false) ||
                !life || !life.World || !life.Gameplay || string.IsNullOrEmpty(directory) ||
                !Path.GetFullPath(directory).StartsWith(Path.GetFullPath("LifeGameplayReview/20261028/feedback") + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                Path.GetFullPath(directory) != Path.GetFullPath(GameplaySession.PersistenceDirectory) ||
                SceneManager.GetActiveScene().path != "Assets/Scenes/WeatherHouse_WatersideV2.unity")
                throw new InvalidOperationException("Weather feedback checks require the owned feedback isolated Play.");
            var input = life.GetComponent<LifeGestureInput>();
            var hud = life.Gameplay.GetComponent<GameplayHud>();
            if (!input || !hud || input.IsStrokeActive || input.IsWorldDrawing || hud.JournalOpen || hud.CameraMode || hud.IsDialogueOpen || Time.deltaTime <= 0)
                throw new InvalidOperationException("Weather feedback checks require an idle HUD and advancing Play time.");

            var checks = new List<string>();
            var originalWeather = Field<LifeLocalWeather>(life, "weather");
            string originalState = Snapshot(originalWeather), notebook = JsonUtility.ToJson(life.Gameplay.Notebook);
            bool dirty = SceneManager.GetActiveScene().isDirty;
            var objectStates = life.World.Objects.Where(obj => obj).ToDictionary(obj => obj, obj => JsonUtility.ToJson(obj.State));
            var fixture = new GameObject("Weather feedback isolated fixture");
            try
            {
                var weather = fixture.AddComponent<LifeLocalWeather>(); weather.Initialize(life.Gameplay.PlayerTransform, LifeSession.CastRadius);
                CheckController(weather, checks);
                CheckRetainedResults(weather, fixture, life, checks);
                var sky = Field<WeatherRegion>(weather, "sky"); var light = Field<Light>(sky, "sunshine");
                weather.Cast("sunlight", out _); weather.Cast("weak", out _); light.intensity = 3f;
                weather.Cast("strong", out _); Call(sky, "Update"); float brighter = light.intensity;
                Check(brighter > 3f && brighter <= 3.8f && brighter - 3f <= Time.deltaTime * 2f + .001f,
                    "Strong sunlight increases the existing light gradually within the modest 3 to 3.8 range", checks);
                weather.Cast("weak", out _); Call(sky, "Update");
                Check(light.intensity < brighter && light.intensity >= 3f, "Weak sunlight smoothly returns toward the original brightness", checks);
                weather.Cast("clear", out _);
                CheckNoTargetInk(life, input, weather, checks);
            }
            catch (Exception error) { checks.Add("FAIL " + error); }
            finally
            {
                Set(life, "weather", originalWeather);
                JsonUtility.FromJsonOverwrite(notebook, life.Gameplay.Notebook);
                UnityEngine.Object.DestroyImmediate(fixture);
                Check(Snapshot(originalWeather) == originalState && objectStates.All(pair => JsonUtility.ToJson(pair.Key.State) == pair.Value),
                    "The live weather and world object states remain unchanged by the synchronous fixtures", checks);
                Check(SceneManager.GetActiveScene().isDirty == dirty, "Scene dirty state is preserved; no scene or gameplay save is written", checks);
                string result = (checks.Any(line => line.StartsWith("FAIL ", StringComparison.Ordinal)) ? "FAIL" : "PASS") +
                    " weather feedback semantics\n" + string.Join("\n", checks) +
                    "\nBOUNDARY actual Unity controller, WeatherRegion, LifeWorld.ApplyWeather and SubmitStroke on declared temporary fixtures; light Update is invoked synchronously. No OS pointer, rendered visual acceptance or animal task-flow claim.";
                File.WriteAllText(Path.Combine(directory, "weather-feedback-checks.txt"), result); Debug.Log(result);
            }
        }

        static void CheckController(LifeLocalWeather weather, List<string> checks)
        {
            var sky = Field<WeatherRegion>(weather, "sky"); var wind = Field<WeatherRegion>(weather, "wind");
            weather.Cast("clear", out _); string stopped = Snapshot(weather);
            Check(!weather.Cast("strong", out string message) && message == "", "No maintained weather rejects strong without explanatory text", checks);
            Check(Snapshot(weather) == stopped, "A rejected modifier leaves all weather state unchanged", checks);
            weather.Cast("wind", out _); weather.Cast("weak", out _); weather.Cast("rain", out _); weather.Cast("weak", out _);
            Check(weather.Cast("strong", out _) && sky.SoundStrength == 2 && wind.SoundStrength == 1 && weather.LastCastWeather == LocalWeather.Rain,
                "Wind then rain targets the latest rain without changing wind", checks);
            string beforeInvalid = Snapshot(weather);
            Check(!weather.Cast("unknown", out _) && Snapshot(weather) == beforeInvalid,
                "An unsuccessful spell cannot replace the current modifier target", checks);
            weather.Cast("wind", out _);
            Check(weather.Cast("strong", out _) && wind.SoundStrength == 2 && sky.SoundStrength == 2 && weather.LastCastWeather == LocalWeather.Wind,
                "Rain then wind targets the latest wind", checks);
            Check(weather.Cast("weak", out _) && wind.SoundStrength == 1 && sky.SoundStrength == 2,
                "Repeated modifiers retain the latest weather target", checks);
            weather.Cast("sunlight", out _);
            Check(weather.Cast("weak", out _) && sky.Current == LocalWeather.Sun && sky.SoundStrength == 1 && wind.SoundStrength == 1,
                "Wind then sunlight allows weak to adjust only sunlight", checks);
            Check(weather.Cast("strong", out _) && sky.SoundStrength == 2 && wind.SoundStrength == 1 && weather.LastCastWeather == LocalWeather.Sun,
                "Strong adjusts the latest maintained sunlight", checks);
            Check(weather.Cast("clear", out _) && !weather.Active && !sky.SoundActive && !wind.SoundActive,
                "Clear ends every maintained weather channel", checks);
            stopped = Snapshot(weather);
            foreach (string id in new[] { "strong", "weak" })
                Check(!weather.Cast(id, out message) && message == "" && Snapshot(weather) == stopped,
                    "After clear, " + id + " is silent and cannot restart or alter stopped weather", checks);
        }

        static void CheckRetainedResults(LifeLocalWeather weather, GameObject fixture, LifeSession life, List<string> checks)
        {
            var world = fixture.AddComponent<LifeWorld>(); Set(world, "owner", life);
            var objects = Field<Dictionary<string, LifeObject>>(world, "objects");
            foreach (LifeObjectKind kind in new[] { LifeObjectKind.Bowl, LifeObjectKind.Mat })
            {
                var obj = new GameObject(kind.ToString()).AddComponent<LifeObject>(); obj.transform.SetParent(fixture.transform, false);
                obj.transform.position = life.Gameplay.PlayerTransform.position;
                obj.State = new LifeObjectState { id = kind.ToString(), kind = kind }; objects.Add(obj.State.id, obj);
            }
            weather.Cast("rain", out _); weather.Cast("weak", out _);
            for (int i = 0; i < 30; i++) weather.Apply(world, .2f);
            var bowl = objects["Bowl"].State; var mat = objects["Mat"].State;
            float water = bowl.water, wetness = mat.wetness;
            weather.Cast("wind", out _); weather.Cast("clear", out _);
            for (int i = 0; i < 30; i++) weather.Apply(world, .2f);
            Check(water > .3f && wetness > .3f && bowl.water == water && mat.wetness == wetness && bowl.rainStrength == 0 && mat.windStrength == 0,
                "Real local ApplyWeather stops rain and wind while retaining collected water and wet results", checks);
        }

        static void CheckNoTargetInk(LifeSession life, LifeGestureInput input, LifeLocalWeather weather, List<string> checks)
        {
            var points = Field<List<Vector2>>(input, "points"); var originalPoints = points.ToArray();
            var ink = Field<LifeStrokeGraphic>(input, "stroke"); var hint = Field<Text>(input, "drawHint");
            var inkPoints = Field<List<Vector2>>(ink, "points").ToArray(); Color color = ink.color;
            string text = hint.text; float until = Field<float>(input, "feedbackUntil");
            try
            {
                life.Gameplay.Notebook.AddObservation(new ObservationRecord { id = "weather-feedback-fixture", glyphIds = new[] { "strong", "weak" }, frameCount = 1 });
                Set(life, "weather", weather);
                foreach (string id in new[] { "strong", "weak" })
                {
                    points.Clear(); var controls = GestureCatalog.Find(id).points;
                    for (int segment = 1; segment < controls.Length; segment++) for (int i = 0; i < 12; i++)
                        points.Add(Vector2.Lerp(controls[segment - 1], controls[segment], i / 11f) * 100f);
                    if (!LifeGestureInput.ValidStroke(points) || !GestureCatalog.TryRecognize(points, out string found, out _) || found != id ||
                        !life.IsGestureUnlocked(id) || weather.Active || weather.Cast(id, out _))
                        throw new InvalidOperationException("Only a recognized, unlocked modifier with no active target may enter the UI fixture.");
                    ink.SetPath(points); Call(input, "SubmitStroke");
                    Check(hint.text == "" && ink.color == new Color(1f, .54f, .36f) && Field<List<Vector2>>(ink, "points").Count > 1 && !weather.Active,
                        "Recognized " + id + " with no target preserves orange ink and shows no Chinese instruction", checks);
                }
            }
            finally
            {
                points.Clear(); points.AddRange(originalPoints); ink.SetPath(inkPoints); ink.color = color; hint.text = text; Set(input, "feedbackUntil", until);
            }
        }

        static string Snapshot(LifeLocalWeather weather)
        {
            var sky = Field<WeatherRegion>(weather, "sky"); var wind = Field<WeatherRegion>(weather, "wind");
            return weather.Active + "|" + weather.LastCastWeather + "|" + sky.Current + "|" + sky.SoundStrength + "|" + sky.SoundActive + "|" + wind.SoundStrength + "|" + wind.SoundActive;
        }
        static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, Hidden).GetValue(target);
        static void Set(object target, string name, object value) => target.GetType().GetField(name, Hidden).SetValue(target, value);
        static void Call(object target, string name) => target.GetType().GetMethod(name, Hidden).Invoke(target, null);
        static void Check(bool ok, string label, List<string> checks) => checks.Add((ok ? "PASS " : "FAIL ") + label);
    }
}
