using UnityEngine;
using WeatherHousePrototype;

namespace WeatherHouse.Life
{
    public sealed class LifeLocalWeather : MonoBehaviour
    {
        private Transform player;
        private float radius;
        private WeatherRegion sky, wind;
        private LocalWeather skyKind = LocalWeather.Rain;
        private bool skyActive, windActive, windSelected;
        private int skyStrength = 1, windStrength = 1;
        public bool Active => skyActive || windActive;
        public bool IsRain => skyActive && skyKind == LocalWeather.Rain;
        public LocalWeather LastCastWeather => windSelected ? LocalWeather.Wind : skyKind;
        public string Description => (skyActive ? (skyKind == LocalWeather.Rain ? (skyStrength == 2 ? "强雨" : "弱雨") : "晴") : "雨已停") +
            " · " + (windActive ? (windStrength == 2 ? "强风" : "弱风") : "无风");

        public void Initialize(Transform target, float castRadius)
        {
            player = target; radius = castRadius;
            transform.position = player.position;
            sky = Create("Local rain and sun");
            wind = Create("Local wind");
        }
        private WeatherRegion Create(string label)
        {
            var go = new GameObject(label);
            go.SetActive(false);
            go.transform.SetParent(transform, false);
            var region = go.AddComponent<WeatherRegion>();
            region.radius = radius;
            region.localPlayerWeather = true;
            go.SetActive(true);
            region.SetLocalWeather(LocalWeather.Rain, 1, false);
            return region;
        }
        private void LateUpdate() { if (player) transform.position = player.position; }
        public void Apply(LifeWorld world, float dt)
        {
            if (!player) return;
            transform.position = player.position;
            world.ApplyWeather(player.position, radius, skyKind, skyStrength, skyActive, dt);
            world.ApplyWeather(player.position, radius, LocalWeather.Wind, windStrength, windActive, dt);
        }
        public bool Cast(string id, out string message)
        {
            switch (id)
            {
                case "rain": skyKind = LocalWeather.Rain; skyActive = true; windSelected = false; break;
                case "sunlight": skyKind = LocalWeather.Sun; skyActive = true; windSelected = false; break;
                case "wind": windActive = true; windSelected = true; break;
                case "clear": skyActive = false; windActive = false; break;
                case "weak": case "strong":
                    if (windSelected && windActive) windStrength = id == "strong" ? 2 : 1;
                    else if (skyActive) skyStrength = id == "strong" ? 2 : 1;
                    else { message = ""; return false; }
                    break;
                default: message = "没有认出可用的天气手势。"; return false;
            }
            sky.SetLocalWeather(skyKind, skyStrength, skyActive);
            wind.SetLocalWeather(LocalWeather.Wind, windStrength, windActive);
            message = id == "clear" ? "天气平息了。" : id == "rain" ? "雨落下了。" : id == "sunlight" ? "阳光洒下来了。" :
                id == "wind" ? "风起了。" : id == "strong" ? "它变强了。" : "它变弱了。";
            LifeAudio.PlayAt(LifeAudioCue.Cast, player.position);
            return true;
        }
    }
}
