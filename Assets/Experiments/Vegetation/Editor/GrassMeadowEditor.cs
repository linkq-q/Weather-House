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
                using (new EditorGUI.DisabledScope(property.name == "m_Script" || (Application.isPlaying && (property.name == "species" || property.name == "materials" || property.name == "defaults"))))
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
                GrassKind kind = meadow.species[selectedSpecies].kind;
                foreach (string field in new[] { "displayName", "height", "width", "spread", "bend", "tipWidthRatio", "bladeCount", "rootSpread", "heightVariation", "outerBladeFraction", "clumpLayering", "flowerCount", "flowerRadius", "petalCount", "headLength", "leafAspect", "curl", "seed", "baseColor", "tipColor", "flowerColor", "centerColor" })
                {
                    string caption = PlantControlRules.Label(kind, field);
                    if (caption == null) continue;
                    var parameter = source.FindProperty(field);
                    if (field == "bladeCount")
                    {
                        PlantControlRules.DensityRange(kind, out int min, out int max);
                        int value = PlantControlRules.DensityValue(kind, parameter.intValue);
                        int next = EditorGUILayout.IntSlider(caption, value, min, max);
                        if (next != value) parameter.intValue = PlantControlRules.DensityRaw(kind, next);
                    }
                    else if (field == "headLength" && kind == GrassKind.Fern)
                    {
                        int pairs = PlantControlRules.FernPairs(parameter.floatValue);
                        int next = EditorGUILayout.IntSlider(caption, pairs, 12, 18);
                        if (next != pairs) parameter.floatValue = Mathf.Max(.05f, (next - 12) * .1f);
                    }
                    else if (field == "flowerCount")
                    {
                        int max = kind == GrassKind.Plume || kind == GrassKind.LeafyHerb ? 5 : 7;
                        int count = Mathf.Clamp(parameter.intValue, 1, max);
                        int next = EditorGUILayout.IntSlider(caption, count, 1, max);
                        if (next != count) parameter.intValue = next;
                    }
                    else if (field == "petalCount")
                    {
                        int max = kind == GrassKind.FlowerSpike || kind == GrassKind.FlowerSpray ? 5 : 12;
                        int count = Mathf.Clamp(parameter.intValue, 3, max);
                        int next = EditorGUILayout.IntSlider(caption, count, 3, max);
                        if (next != count) parameter.intValue = next;
                    }
                    else EditorGUILayout.PropertyField(parameter, new GUIContent(caption));
                }
                source.ApplyModifiedProperties();
            }
            int[] desired = GrassMeadow.Allocate(meadow.totalClumps, meadow.weights);
            EditorGUILayout.LabelField("目标数量", string.Join(" / ", desired), EditorStyles.wordWrappedLabel);
            if (Application.isPlaying)
            {
                EditorGUILayout.HelpBox("运行中拖动参数会自动更新草地；在 Game 草地面板上方点击“保存草地与画风为默认值”可保留比例、形状、颜色和风。", MessageType.Info);
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
