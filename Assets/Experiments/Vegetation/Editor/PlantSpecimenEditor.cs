using UnityEditor;

namespace WeatherHouse.Vegetation.Editor
{
    [CustomEditor(typeof(PlantSpecimen))]
    public sealed class PlantSpecimenEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            var sample = (PlantSpecimen)target;
            using (new EditorGUI.DisabledScope(true)) DrawDefaultInspector();
            if (sample.source && UnityEngine.GUILayout.Button("选中 Source 编辑植物参数")) Selection.activeObject = sample.source;
            EditorGUILayout.HelpBox("Source 是独立植物参数。修改后刷新四个种子变体；共享这些网格的陈列和预制体同时更新。混合草地在 Play 中支持实时拖动。", MessageType.Info);
            using (new EditorGUI.DisabledScope(EditorApplication.isPlaying || !sample.source))
                if (UnityEngine.GUILayout.Button("按 Source 参数刷新四个变体"))
                    PlantLibraryBuilder.RefreshSpecies(sample.source);
        }
    }
}
