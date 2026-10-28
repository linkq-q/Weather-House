using System;
using System.Collections;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using WeatherHousePrototype;

namespace WeatherHouse.Life.Editor
{
    public static class LifeWeatherVisualReview
    {
        const string Folder = "LifeGameplayReview/20261028/weather-feedback/visual";
        [MenuItem("Weather House/Review 20261027/Capture weather strength")]
        public static void Begin()
        {
            var life = UnityEngine.Object.FindObjectOfType<LifeSession>();
            if (!Application.isBatchMode || !Application.isPlaying || !life ||
                !SessionState.GetBool("WeatherHouse.FeedbackReview20261028", false))
                throw new InvalidOperationException("Weather capture requires feedback-owned isolated Play.");
            var hud = life.Gameplay.GetComponent<GameplayHud>();
            var weather = (LifeLocalWeather)typeof(LifeSession).GetField("weather", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(life);
            if (weather.Active || hud.CameraMode || hud.JournalOpen) throw new InvalidOperationException("Begin with idle weather and HUD.");
            life.StartCoroutine(Capture(life, weather));
        }
        static IEnumerator Capture(LifeSession life, LifeLocalWeather weather)
        {
            Directory.CreateDirectory(Folder);
            try
            {
                weather.Cast("sunlight", out _); weather.Cast("weak", out _);
                yield return new WaitForSeconds(2f);
                LifeFeedbackVisualReview.Frame(life, "01_弱日照.png", Folder);
                weather.Cast("strong", out _);
                yield return new WaitForSeconds(1f);
                LifeFeedbackVisualReview.Frame(life, "02_强日照.png", Folder);
                File.WriteAllText(Folder + "/source.txt", "Actual isolated Unity Play, same player camera and production weather. Weak light 3.0, strong light 3.8, with real Update interpolation. World simulation continues between frames. No scene or normal player-save writes.\n" + DateTime.UtcNow.ToString("O"));
            }
            finally { weather.Cast("clear", out _); }
        }
    }
}
