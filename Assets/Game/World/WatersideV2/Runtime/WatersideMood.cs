using UnityEngine;
using UnityEngine.Rendering;

namespace WeatherHouse.WatersideV2
{
    [DisallowMultipleComponent]
    public sealed class WatersideMood : MonoBehaviour
    {
        public enum Mode { Day, Dawn, Dusk, Overcast, Night }
        public Light mainLight;
        public Camera mainCamera;
        [SerializeField] Mode current = Mode.Day;
        public Mode Current => current;
        Snapshot daylight;
        static readonly int[] WaterColors = {
            Shader.PropertyToID("_ShallowColor"), Shader.PropertyToID("_DeepColor"),
            Shader.PropertyToID("_FoamColor"), Shader.PropertyToID("_RippleColor")
        };

        internal sealed class WaterPalette
        {
            internal Renderer renderer;
            internal readonly Color[] colors = new Color[4];
        }

        // Transient capture state includes the real settings, so user tuning survives a photo sequence.
        public sealed class Snapshot
        {
            internal Mode mode;
            internal Light sun;
            internal Quaternion rotation;
            internal Color sunlight, sky, equator, ground, fogColor, background;
            internal float intensity, shadow, ambientIntensity, fogStart, fogEnd, fogDensity;
            internal bool sunEnabled, fog;
            internal AmbientMode ambientMode;
            internal FogMode fogMode;
            internal Camera camera;
            internal Light[] lamps;
            internal bool[] lampEnabled;
            internal float[] lampIntensity;
            internal WaterPalette[] water;
        }

        void Start()
        {
            EnsureDaylight();
            if (current != Mode.Day) Apply(current);
        }

        void Update()
        {
            if (Input.GetKeyDown(KeyCode.F8)) Apply((Mode)(((int)current + 1) % 5));
        }

        void EnsureDaylight()
        {
            if (daylight != null) return;
            if (!mainLight) mainLight = GetComponent<Light>();
            if (!mainLight) mainLight = RenderSettings.sun;
            if (!mainLight || mainLight.type != LightType.Directional)
                throw new System.InvalidOperationException("Waterside Mood needs the scene's directional light.");
            if (!mainCamera) mainCamera = Camera.main;
            daylight = TakeSnapshot();
        }

        public Snapshot CaptureState()
        { EnsureDaylight(); return TakeSnapshot(); }

        Snapshot TakeSnapshot()
        {
            var lamps = new System.Collections.Generic.List<Light>();
            foreach (Light light in FindObjectsOfType<Light>(true))
                if (light.type == LightType.Point && light.gameObject.scene == gameObject.scene) lamps.Add(light);
            var state = new Snapshot
            {
                mode = current, sun = mainLight, rotation = mainLight.transform.rotation,
                sunlight = mainLight.color, intensity = mainLight.intensity, shadow = mainLight.shadowStrength,
                sunEnabled = mainLight.enabled, ambientMode = RenderSettings.ambientMode,
                sky = RenderSettings.ambientSkyColor, equator = RenderSettings.ambientEquatorColor,
                ground = RenderSettings.ambientGroundColor, ambientIntensity = RenderSettings.ambientIntensity,
                fog = RenderSettings.fog, fogMode = RenderSettings.fogMode, fogColor = RenderSettings.fogColor,
                fogStart = RenderSettings.fogStartDistance, fogEnd = RenderSettings.fogEndDistance,
                fogDensity = RenderSettings.fogDensity, camera = mainCamera,
                background = mainCamera ? mainCamera.backgroundColor : RenderSettings.fogColor,
                lamps = lamps.ToArray(), lampEnabled = new bool[lamps.Count], lampIntensity = new float[lamps.Count],
                water = ReadWaterPalettes()
            };
            for (int i = 0; i < lamps.Count; i++)
            { state.lampEnabled[i] = lamps[i].enabled; state.lampIntensity[i] = lamps[i].intensity; }
            return state;
        }

        public void RestoreState(Snapshot state)
        {
            if (state == null) return;
            current = state.mode;
            if (state.sun)
            {
                state.sun.transform.rotation = state.rotation; state.sun.color = state.sunlight;
                state.sun.intensity = state.intensity; state.sun.shadowStrength = state.shadow;
                state.sun.enabled = state.sunEnabled;
            }
            RenderSettings.ambientMode = state.ambientMode;
            RenderSettings.ambientSkyColor = state.sky; RenderSettings.ambientEquatorColor = state.equator;
            RenderSettings.ambientGroundColor = state.ground; RenderSettings.ambientIntensity = state.ambientIntensity;
            RenderSettings.fog = state.fog; RenderSettings.fogMode = state.fogMode; RenderSettings.fogColor = state.fogColor;
            RenderSettings.fogStartDistance = state.fogStart; RenderSettings.fogEndDistance = state.fogEnd;
            RenderSettings.fogDensity = state.fogDensity;
            if (state.camera) state.camera.backgroundColor = state.background;
            for (int i = 0; i < state.lamps.Length; i++)
                if (state.lamps[i])
                { state.lamps[i].enabled = state.lampEnabled[i]; state.lamps[i].intensity = state.lampIntensity[i]; }
            WriteWaterPalettes(state.water, Mode.Day);
            DynamicGI.UpdateEnvironment();
        }

        public void Apply(Mode mode, Camera captureCamera = null)
        {
            EnsureDaylight();
            RestoreState(daylight);
            current = mode;
            if (mode == Mode.Day)
            {
                if (captureCamera) captureCamera.backgroundColor = daylight.background;
                return;
            }
            Color sky, equator, ground, fog;
            switch (mode)
            {
                case Mode.Dawn:
                    Sun(new Color(1,.79f,.58f), .85f, 19, -55, .42f);
                    sky = new Color(.44f,.57f,.62f); equator = new Color(.39f,.48f,.40f);
                    ground = new Color(.20f,.25f,.18f); fog = new Color(.55f,.65f,.64f);
                    RenderSettings.fogStartDistance = 38; RenderSettings.fogEndDistance = 150;
                    break;
                case Mode.Dusk:
                    Sun(new Color(1,.60f,.35f), .68f, 18, -110, .55f);
                    sky = new Color(.39f,.48f,.55f); equator = new Color(.36f,.38f,.31f);
                    ground = new Color(.18f,.21f,.17f); fog = new Color(.48f,.55f,.56f);
                    RenderSettings.fogStartDistance = 55; RenderSettings.fogEndDistance = 190;
                    break;
                case Mode.Overcast:
                    Sun(new Color(.75f,.84f,.94f), .46f, 62, -30, .26f);
                    sky = new Color(.43f,.54f,.60f); equator = new Color(.35f,.45f,.43f);
                    ground = new Color(.18f,.25f,.23f); fog = new Color(.42f,.55f,.58f);
                    RenderSettings.fogStartDistance = 42; RenderSettings.fogEndDistance = 135;
                    break;
                default:
                    Sun(new Color(.52f,.69f,.94f), .32f, 48, 135, .42f);
                    sky = new Color(.26f,.35f,.39f); equator = new Color(.23f,.30f,.29f);
                    ground = new Color(.12f,.18f,.15f); fog = new Color(.07f,.16f,.21f);
                    RenderSettings.fogStartDistance = 45; RenderSettings.fogEndDistance = 150;
                    break;
            }
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = sky; RenderSettings.ambientEquatorColor = equator;
            RenderSettings.ambientGroundColor = ground; RenderSettings.ambientIntensity = 1;
            RenderSettings.fog = true; RenderSettings.fogMode = FogMode.Linear; RenderSettings.fogColor = fog;
            Color background = mode == Mode.Night ? new Color(.035f,.095f,.14f) : fog;
            if (mainCamera) mainCamera.backgroundColor = background;
            if (captureCamera) captureCamera.backgroundColor = background;
            for (int i = 0; i < daylight.lamps.Length; i++)
                if (daylight.lamps[i])
                {
                    daylight.lamps[i].enabled = true;
                    daylight.lamps[i].intensity = daylight.lampIntensity[i] * (mode == Mode.Night ? 1.7f : 1.15f);
                }
            WriteWaterPalettes(daylight.water, mode);
            DynamicGI.UpdateEnvironment();
        }

        WaterPalette[] ReadWaterPalettes()
        {
            var result = new System.Collections.Generic.List<WaterPalette>();
            var block = new MaterialPropertyBlock();
            foreach (var surface in FindObjectsOfType<WatersideWaterSurface>(true))
            {
                if (surface.section == -2 || surface.gameObject.scene != gameObject.scene) continue;
                var renderer = surface.GetComponent<Renderer>();
                if (!renderer || !renderer.sharedMaterial || !renderer.sharedMaterial.HasProperty(WaterColors[0])) continue;
                var palette = new WaterPalette { renderer = renderer };
                renderer.GetPropertyBlock(block);
                for (int i = 0; i < WaterColors.Length; i++)
                    palette.colors[i] = block.HasColor(WaterColors[i]) ? block.GetColor(WaterColors[i]) : renderer.sharedMaterial.GetColor(WaterColors[i]);
                result.Add(palette);
            }
            return result.ToArray();
        }

        static void WriteWaterPalettes(WaterPalette[] palettes, Mode mode)
        {
            var block = new MaterialPropertyBlock();
            foreach (var palette in palettes)
            {
                if (!palette.renderer) continue;
                // Reflections and wakes update this block between photos; never restore an old block over their render textures.
                palette.renderer.GetPropertyBlock(block);
                for (int i = 0; i < WaterColors.Length; i++)
                {
                    Color tint = Color.white;
                    if (mode == Mode.Night)
                        tint = i == 0 ? new Color(.4f,.45f,.6f) : i == 1 ? new Color(.4f,.55f,.7f) : new Color(.45f,.55f,.7f);
                    else if (mode == Mode.Overcast) tint = new Color(.7f,.8f,.85f);
                    block.SetColor(WaterColors[i], palette.colors[i] * tint);
                }
                palette.renderer.SetPropertyBlock(block);
            }
        }

        void Sun(Color color, float intensity, float pitch, float yaw, float shadow)
        {
            mainLight.enabled = true; mainLight.color = color; mainLight.intensity = intensity;
            mainLight.shadowStrength = shadow; mainLight.transform.rotation = Quaternion.Euler(pitch, yaw, 0);
        }
    }
}
