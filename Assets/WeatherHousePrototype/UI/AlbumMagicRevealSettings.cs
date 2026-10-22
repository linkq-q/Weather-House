using UnityEngine;

namespace WeatherHousePrototype
{
    [CreateAssetMenu(menuName = "Weather House/手账显影参数", fileName = "RevealSettings")]
    public sealed class AlbumMagicRevealSettings : ScriptableObject
    {
        [Header("整段动画")]
        [InspectorName("播放速度"), Range(.25f, 3f)]
        public float playbackSpeed = 1f;
        [InspectorName("整段动画进度曲线"), Tooltip("横轴为归一化播放时间，纵轴为整个开书、内容出现和星尘的统一进度。")]
        public AnimationCurve playbackCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);
        [InspectorName("动态模糊强度"), Range(0f, 1f), Tooltip("仅在书本移动与翻页时柔化边缘；0为关闭，停稳后恢复清晰。")]
        public float motionBlurStrength = .65f;

        [Header("统一噪波")]
        [InspectorName("启用噪波溶解与光晕"), Tooltip("关闭时内容仅轻微淡入，保留现有星尘和全部参数。")]
        public bool enableNoiseDissolve = false;
        [InspectorName("噪波周期（UI 像素）"), Range(20f, 800f)]
        public float noiseSize = 120f;
        [InspectorName("噪波偏移"), Tooltip("所有照片、字符和光晕共用此偏移。单位为纹理周期。")]
        public Vector2 noiseOffset;

        [Header("显影时序")]
        [InspectorName("开始时间（秒）"), Range(0f, 3f)]
        public float revealStart = .66f;
        [InspectorName("显影时长（秒）"), Range(.05f, 3f)]
        public float revealDuration = .42f;

        [Header("显影边缘")]
        [InspectorName("金色边缘宽度"), Range(.001f, .3f)]
        public float edgeWidth = .06f;
        [InspectorName("边缘柔和度"), Range(.001f, .2f)]
        public float softness = .025f;
        [InspectorName("琥珀色")]
        public Color amber = new Color(1f, .22f, .015f, 1f);
        [InspectorName("金色")]
        public Color gold = new Color(1f, .66f, .075f, 1f);
        [InspectorName("亮芯色")]
        public Color ivory = new Color(1f, .97f, .73f, 1f);

        [Header("外围光晕")]
        [InspectorName("光晕强度"), Range(0f, 3f)]
        public float glowIntensity = 1f;
        [InspectorName("光晕范围（UI 像素）"), Range(2f, 80f)]
        public float glowSpread = 24f;

        [Header("星尘")]
        [InspectorName("每张照片小光点数"), Range(0, 300)]
        public int photoDustCount = 140;
        [InspectorName("每个字符小光点数"), Range(0, 60)]
        public int wordDustCount = 28;
        [InspectorName("小光点半径范围"), Tooltip("X 为最小、Y 为最大，单位为 UI 像素。")]
        public Vector2 dustSize = new Vector2(1f, 2.2f);
        [InspectorName("四角星半径范围"), Tooltip("X 为最小、Y 为最大，单位为 UI 像素。")]
        public Vector2 starSize = new Vector2(7f, 11f);

        private static AlbumMagicRevealSettings fallback;

        public static AlbumMagicRevealSettings Load()
        {
            var settings = Resources.Load<AlbumMagicRevealSettings>("AlbumBook/RevealSettings");
            if (settings != null) return settings;
            if (fallback == null)
            {
                fallback = CreateInstance<AlbumMagicRevealSettings>();
                fallback.name = "Album reveal defaults";
                fallback.hideFlags = HideFlags.HideAndDontSave;
            }
            return fallback;
        }

        public float TotalDuration
        {
            get { return Mathf.Max(1.15f, Mathf.Max(0f, revealStart) + Mathf.Max(.05f, revealDuration) + .07f); }
        }

        public float ProgressAt(float seconds)
        {
            return Mathf.Clamp01((seconds - Mathf.Max(0f, revealStart)) / Mathf.Max(.05f, revealDuration));
        }

        public float PlaybackDuration
        {
            get { return TotalDuration / Mathf.Clamp(playbackSpeed, .25f, 3f); }
        }

        public float MapPlaybackTime(float seconds)
        {
            if (seconds <= 0f) return 0f;
            if (seconds >= PlaybackDuration) return TotalDuration;
            float progress = seconds / PlaybackDuration;
            if (playbackCurve != null && playbackCurve.length > 0) progress = playbackCurve.Evaluate(progress);
            return Mathf.Clamp01(progress) * TotalDuration;
        }
    }
}
