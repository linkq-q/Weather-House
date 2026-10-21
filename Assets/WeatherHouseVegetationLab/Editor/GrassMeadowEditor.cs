using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace WeatherHouse.Vegetation.Editor
{
    [CustomEditor(typeof(GrassMeadow))]
    public class GrassMeadowEditor : UnityEditor.Editor
    {
        int selectedSpecies;
        readonly string[] names = { "短草", "高草", "白色雏菊" };
        public override void OnInspectorGUI()
        {
            EditorGUILayout.HelpBox("短草 / 高草 / 白色雏菊。Weights 是相对权重，按草丛数归一化；全部为零会清空草地。修改草种资产可改变叶宽、弯曲、叶数、颜色。", MessageType.Info);
            DrawDefaultInspector();
            var meadow = (GrassMeadow)target;
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("直接编辑草种形状与颜色", EditorStyles.boldLabel);
            selectedSpecies = GUILayout.Toolbar(selectedSpecies, names);
            if (meadow.species.Length > selectedSpecies && meadow.species[selectedSpecies])
            {
                var source = new SerializedObject(meadow.species[selectedSpecies]);
                source.Update();
                foreach (string field in new[] { "height", "width", "tipWidthRatio", "spread", "rootSpread", "bend", "bladeCount", "heightVariation", "seed", "baseColor", "tipColor" })
                    if (meadow.species[selectedSpecies].kind != GrassKind.Daisy || (field != "rootSpread" && field != "bladeCount" && field != "heightVariation"))
                        EditorGUILayout.PropertyField(source.FindProperty(field));
                if (meadow.species[selectedSpecies].kind == GrassKind.Daisy)
                    foreach (string field in new[] { "flowerCount", "flowerRadius", "flowerColor", "centerColor" })
                        EditorGUILayout.PropertyField(source.FindProperty(field));
                source.ApplyModifiedProperties();
            }
            int[] desired = GrassMeadow.Allocate(meadow.totalClumps, meadow.weights);
            EditorGUILayout.LabelField("目标数量", string.Join(" / ", desired));
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
