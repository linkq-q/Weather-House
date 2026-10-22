using UnityEditor;
using UnityEngine;

namespace WeatherHousePrototype.Editor
{
    [CustomEditor(typeof(AlbumMagicRevealSettings))]
    public sealed class AlbumMagicRevealSettingsEditor : UnityEditor.Editor
    {
        private const string SettingsPath = "Assets/WeatherHousePrototype/Resources/AlbumBook/RevealSettings.asset";

        [MenuItem("Weather House/Album/显影参数")]
        public static void SelectSettings()
        {
            var settings = AssetDatabase.LoadAssetAtPath<AlbumMagicRevealSettings>(SettingsPath);
            if (settings == null)
            {
                settings = CreateInstance<AlbumMagicRevealSettings>();
                AssetDatabase.CreateAsset(settings, SettingsPath);
                AssetDatabase.SaveAssetIfDirty(settings);
            }
            Selection.activeObject = settings;
            EditorGUIUtility.PingObject(settings);
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            using (new EditorGUI.DisabledScope(true))
                DrawField("m_Script");

            DrawField("playbackSpeed");
            DrawField("playbackCurve");
            DrawField("motionBlurStrength");
            DrawField("revealStart", "内容开始时间（秒）");
            DrawField("revealDuration", "内容淡入时长（秒）");
            EditorGUILayout.HelpBox("内容与星尘共用整段动画进度。关闭时沿用相同速度与曲线反向播放。", MessageType.None);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("星尘颜色", EditorStyles.boldLabel);
            DrawField("amber");
            DrawField("gold");
            DrawField("ivory");
            DrawField("photoDustCount");
            DrawField("wordDustCount");
            DrawField("dustSize");
            DrawField("starSize");

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("星尘分布（保留噪波采样）", EditorStyles.boldLabel);
            DrawField("noiseSize", "分布周期（UI 像素）");
            DrawField("noiseOffset", "分布偏移");
            DrawField("softness", "分布柔和度");
            EditorGUILayout.HelpBox("这些参数仍控制星尘出现的位置与时机；关闭内容噪波溶解后也会生效。", MessageType.None);

            var noiseDissolve = serializedObject.FindProperty("enableNoiseDissolve");
            EditorGUILayout.PropertyField(noiseDissolve);
            using (new EditorGUI.DisabledScope(!noiseDissolve.boolValue))
            {
                DrawField("edgeWidth");
                DrawField("glowIntensity");
                DrawField("glowSpread");
            }
            if (!noiseDissolve.boolValue)
                EditorGUILayout.HelpBox("当前内容仅轻微淡入并保留星尘。溶解边缘与外围光晕已关闭，原参数保持不变。", MessageType.Info);

            if (serializedObject.ApplyModifiedProperties())
                AssetDatabase.SaveAssetIfDirty(target);

            EditorGUILayout.Space();
            EditorGUILayout.HelpBox("参数保存到项目资源。进入 Play 后，点击下方按钮查看修改后的显影效果。", MessageType.Info);
            using (new EditorGUI.DisabledScope(!Application.isPlaying))
            {
                if (GUILayout.Button("重播手账显影"))
                    Replay();
            }
        }

        private void DrawField(string name, string label = null)
        {
            var property = serializedObject.FindProperty(name);
            if (label == null)
                EditorGUILayout.PropertyField(property, true);
            else
                EditorGUILayout.PropertyField(property, new GUIContent(label, property.tooltip), true);
        }

        public static void Replay()
        {
            if (!Application.isPlaying) return;
            var opening = FindObjectOfType<AlbumBookOpening>();
            if (opening != null) { opening.Play(); return; }
            var hud = FindObjectOfType<GameplayHud>();
            if (hud == null)
            {
                Debug.LogWarning("当前运行场景没有相册界面，请在游戏场景中预览手账显影。");
                return;
            }
            if (hud.JournalOpen)
            {
                Debug.Log("请先关闭照片详情，返回手账后再重播显影。", hud);
                return;
            }
            hud.ToggleJournal();
        }
    }
}
