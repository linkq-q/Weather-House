using UnityEditor;
using UnityEngine;

namespace WeatherHouse.Life.Editor
{
    public sealed class LifeCastRangeShaderGUI : ShaderGUI
    {
        readonly bool[] expanded = { true, true, false, false, false, false, false };
        static readonly string[] Titles = { "颜色与整体强度", "火焰形状", "焰心与光晕", "摆动与流动", "扰动与破碎", "底部连续光幕", "贴地圆环" };
        static readonly string[] Hints =
        {
            "先调主体与焰心颜色。尖端染色为 0 时保留主体色；整体不透明度为 0 时完全隐藏。",
            "宽度、高度比例、收尖和柔边决定轮廓。随机程度为 0 时各簇等高等宽。最大实际高度仍由 Prefab 的 Flame Height 决定。",
            "焰心控制中间亮条，光晕控制外围泛光。想去掉白亮焰心可先把焰心亮度降至 0。",
            "整体运动速度为 0 时全部冻结。整体倾斜可向左右倾倒；明暗波动控制沿高度流动的亮暗变化。",
            "边缘扰动改变轮廓，破碎溶解打散内部。默认使用程序扰动；指定灰度贴图并提高使用纹理的比例后，可用贴图控制细节。纹理建议使用 Repeat 环绕。",
            "强度为 0 可彻底移除底部连续光幕，只留下单簇火焰。",
            "不透明度为 0 可隐藏贴地圈。这里调材质内的亮线与柔光，整体网格宽度在 Prefab 的 Rim Half Width 调整。"
        };
        static readonly string[][] Groups =
        {
            new[] { "_Color", "_CoreColor", "_TipColor", "_TipTint", "_Brightness", "_Opacity" },
            new[] { "_FlameCount", "_FlameWidth", "_FlameHeight", "_Variation", "_PositionJitter", "_Taper", "_TipSoftness", "_HeightFade", "_EdgeSoftness" },
            new[] { "_CoreWidth", "_CoreBrightness", "_BodyBrightness", "_HaloBrightness", "_HaloWidth", "_FlameOpacity" },
            new[] { "_FlowSpeed", "_SwayAmount", "_SwaySpeed", "_SwayFrequency", "_Lean", "_HeightFlutter", "_PulseAmount", "_PulseSpeed" },
            new[] { "_Distortion", "_Erosion", "_NoiseScale", "_NoiseSpeed", "_NoiseTex", "_NoiseTextureBlend" },
            new[] { "_VeilOpacity", "_VeilHeight", "_VeilWaves", "_VeilSpeed" },
            new[] { "_RimBrightness", "_RimOpacity", "_RimCoreWidth", "_RimGlowWidth" }
        };
        public override void OnGUI(MaterialEditor editor, MaterialProperty[] properties)
        {
            EditorGUILayout.HelpBox("在 CastRange Prefab 中查看效果。施法显示、持续天气和展开进度由游戏控制，不作为外观滑杆。", MessageType.Info);
            for (int group = 0; group < Groups.Length; group++)
            {
                expanded[group] = EditorGUILayout.Foldout(expanded[group], Titles[group], true, EditorStyles.foldoutHeader);
                if (!expanded[group]) continue;
                EditorGUI.indentLevel++;
                EditorGUILayout.HelpBox(Hints[group], MessageType.None);
                foreach (string name in Groups[group])
                {
                    var property = FindProperty(name, properties);
                    var label = new GUIContent(property.displayName.Split('/')[0].Trim(), property.displayName + " (" + name + ")");
                    if (property.type == MaterialProperty.PropType.Texture)
                    {
                        editor.TexturePropertySingleLine(label, property);
                        editor.TextureScaleOffsetProperty(property);
                    }
                    else if (name == "_CoreColor" || name == "_TipColor")
                    {
                        EditorGUI.showMixedValue = property.hasMixedValue;
                        EditorGUI.BeginChangeCheck();
                        Color color = EditorGUILayout.ColorField(label, property.colorValue, true, false, false);
                        if (EditorGUI.EndChangeCheck()) { editor.RegisterPropertyChangeUndo(label.text); property.colorValue = color; }
                        EditorGUI.showMixedValue = false;
                    }
                    else editor.ShaderProperty(property, label);
                }
                EditorGUI.indentLevel--;
                EditorGUILayout.Space(5);
            }
            editor.RenderQueueField();
        }
    }
}
