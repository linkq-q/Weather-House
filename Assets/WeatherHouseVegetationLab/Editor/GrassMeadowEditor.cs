using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace WeatherHouse.Vegetation.Editor
{
    [CustomEditor(typeof(GrassMeadow))]
    public class GrassMeadowEditor : UnityEditor.Editor
    {
        int selectedSpecies;
        public override void OnInspectorGUI()
        {
            EditorGUILayout.HelpBox("Species、Materials、Weights 槽位一一对应，支持 1–32 种植物。Weights 按草丛数归一化；全部为零清空。种类数量和资产引用请在退出 Play 后配置。", MessageType.Info);
            serializedObject.Update();
            var property = serializedObject.GetIterator();
            bool enterChildren = true;
            while (property.NextVisible(enterChildren))
            {
                enterChildren = false;
                using (new EditorGUI.DisabledScope(property.name == "m_Script" || (Application.isPlaying && (property.name == "species" || property.name == "materials"))))
                    EditorGUILayout.PropertyField(property, true);
            }
            serializedObject.ApplyModifiedProperties();
            var meadow = (GrassMeadow)target;
            if (meadow.ConfigurationError != null)
            {
                EditorGUILayout.HelpBox(meadow.ConfigurationError, MessageType.Warning);
                return;
            }
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("直接编辑植物形状与颜色", EditorStyles.boldLabel);
            selectedSpecies = Mathf.Clamp(selectedSpecies, 0, meadow.SpeciesCount - 1);
            selectedSpecies = EditorGUILayout.Popup("当前植物", selectedSpecies, meadow.SpeciesLabels());
            if (meadow.species.Length > selectedSpecies && meadow.species[selectedSpecies])
            {
                var source = new SerializedObject(meadow.species[selectedSpecies]);
                source.Update();
                bool grass = GrassMeadow.IsGrass(meadow.species[selectedSpecies].kind);
                foreach (string field in new[] { "displayName", "height", "width", "spread", "bend", "seed", "baseColor", "tipColor" })
                    EditorGUILayout.PropertyField(source.FindProperty(field));
                if (grass || meadow.species[selectedSpecies].kind == GrassKind.Daisy)
                    EditorGUILayout.PropertyField(source.FindProperty("tipWidthRatio"));
                if (grass)
                    foreach (string field in new[] { "bladeCount", "rootSpread", "heightVariation", "outerBladeFraction", "clumpLayering" })
                        EditorGUILayout.PropertyField(source.FindProperty(field));
                else
                {
                    foreach (string field in new[] { "flowerCount", "flowerRadius", "flowerColor", "centerColor" })
                        EditorGUILayout.PropertyField(source.FindProperty(field));
                    if (meadow.species[selectedSpecies].kind != GrassKind.Daisy)
                        foreach (string field in new[] { "bladeCount", "rootSpread", "heightVariation", "petalCount", "headLength", "leafAspect", "curl" })
                            EditorGUILayout.PropertyField(source.FindProperty(field));
                }
                source.ApplyModifiedProperties();
            }
            int[] desired = GrassMeadow.Allocate(meadow.totalClumps, meadow.weights);
            EditorGUILayout.LabelField("目标数量", string.Join(" / ", desired), EditorStyles.wordWrappedLabel);
            if (Application.isPlaying)
            {
                EditorGUILayout.HelpBox("运行中拖动参数会自动更新草地，无需点击应用。退出 Play 后恢复保存的配置。", MessageType.Info);
                return;
            }
            if (GUILayout.Button("应用草种与比例 · 重新生成", GUILayout.Height(32)))
            {
                meadow.Rebuild();
                EditorUtility.SetDirty(meadow);
                EditorSceneManager.MarkSceneDirty(meadow.gameObject.scene);
            }
            if (GUILayout.Button("保存当前测试场景")) { AssetDatabase.SaveAssets(); EditorSceneManager.SaveScene(meadow.gameObject.scene); }
        }
    }
}
