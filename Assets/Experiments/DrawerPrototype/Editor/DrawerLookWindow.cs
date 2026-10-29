using UnityEditor;
using UnityEngine;

namespace WeatherHouse.DrawerPrototype.Editor
{
    public sealed class DrawerLookWindow : EditorWindow
    {
        int page, grassChoice, surfaceChoice;
        Vector2 scroll;
        string message;

        [MenuItem("Weather House/抽屉原型/画面调参面板")]
        public static void Open()
        {
            var window = GetWindow<DrawerLookWindow>("抽屉画面调参");
            window.minSize = new Vector2(430,540); window.Show();
        }

        void OnInspectorUpdate() => Repaint();

        void OnGUI()
        {
            GUILayout.Label("抽屉画面调参",EditorStyles.boldLabel);
            var controller = FindObjectOfType<DrawerPrototypeController>();
            if (!Application.isPlaying || !controller || !controller.RuntimeLook)
            {
                EditorGUILayout.HelpBox("在抽屉试玩中实时调节。可将本窗口拖到 Game 视图旁边。",MessageType.Info);
                using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling))
                    if (GUILayout.Button("开始抽屉预览",GUILayout.Height(30))) DrawerPrototypeEditor.Begin();
                return;
            }
            DrawerLookSettings look = controller.RuntimeLook;
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("保存为抽屉默认值",GUILayout.Height(28)))
            {
                try { DrawerLookAssets.Save(controller); message = "已保存，退出 Play 后保留。"; }
                catch (System.Exception error) { Debug.LogException(error); message = "保存失败："+error.Message; }
            }
            if (GUILayout.Button("恢复已保存值",GUILayout.Height(28)))
            {
                DrawerLookAssets.CopyLook(controller.lookDefaults,look); controller.ApplyLookSettings(); message = "已恢复上次保存的参数。";
            }
            GUILayout.EndHorizontal();
            if (!string.IsNullOrEmpty(message)) EditorGUILayout.HelpBox(message,MessageType.Info);
            page = GUILayout.Toolbar(page,new[] { "草地 / 撒点","信纸 / 书封","油画后处理","光影" });
            scroll = EditorGUILayout.BeginScrollView(scroll);
            EditorGUI.BeginChangeCheck();
            if (page == 0) Grass(look);
            else if (page == 1) Surfaces(look);
            else if (page == 2) Post(look);
            else Lighting(look);
            if (EditorGUI.EndChangeCheck()) { controller.ApplyLookSettings(); message = "当前为试调值，点击保存后下次沿用。"; }
            EditorGUILayout.EndScrollView();
            GUILayout.Space(6);
            EditorGUILayout.LabelField("调节仅作用于抽屉；未保存的试调在退出 Play 后恢复。",EditorStyles.wordWrappedMiniLabel);
        }

        void Grass(DrawerLookSettings look)
        {
            GUILayout.Label("草地撒点",EditorStyles.boldLabel);
            look.density = EditorGUILayout.Slider("每平方米草丛数",look.density,0,80);
            look.seed = EditorGUILayout.IntField("分布种子",look.seed);
            if (GUILayout.Button("换一组草地位置")) { look.seed++; GUI.changed = true; }
            look.speciesWeights.x = EditorGUILayout.Slider("短草权重",look.speciesWeights.x,0,100);
            look.speciesWeights.y = EditorGUILayout.Slider("长草权重",look.speciesWeights.y,0,100);
            look.speciesWeights.z = EditorGUILayout.Slider("雏菊权重",look.speciesWeights.z,0,20);
            EditorGUILayout.LabelField("权重决定随机草地比例；边缘花丛保留固定构图。",EditorStyles.wordWrappedMiniLabel);
            GUILayout.Space(10);
            grassChoice = GUILayout.Toolbar(grassChoice,new[] { "短草","长草","雏菊","橙花","地面" });
            Material material = grassChoice < 3 ? look.grass[grassChoice] : grassChoice == 3 ? look.orangeFlowers : look.ground;
            ColorField(material,grassChoice == 4 ? "地面染色" : "根部颜色","_BaseColor");
            if (grassChoice == 4) return;
            ColorField(material,"叶尖颜色","_TipColor"); ColorField(material,"阴影颜色","_ShadowTint");
            Float(material,"阴影强度","_ShadowStrength",0,1); Float(material,"法线向上平滑","_UpNormals",0,1);
            Float(material,"逐叶明暗对比","_BladeContrast",0,1); Float(material,"连片草地混合","_MeadowBlend",0,1);
            ColorField(material,"连片草地底色","_MeadowBaseColor"); ColorField(material,"暖色斑块","_MeadowPatchColor");
            Float(material,"大色块频率","_MeadowPatchScale",.02f,2); Float(material,"逐丛色块强度","_PatchStrength",0,.6f);
            Float(material,"色块频率","_PatchScale",.02f,3);
            GUILayout.Space(8); GUILayout.Label("风动与边界",EditorStyles.boldLabel);
            Float(material,"风力","_WindStrength",0,.8f); Float(material,"风速","_WindSpeed",0,5); Float(material,"风波频率","_WindScale",.02f,3);
            Float(material,"明暗边界硬度","_BoundaryHardness",0,1); Float(material,"边界起伏","_BoundaryBreakup",0,.25f); Float(material,"边界分组尺度","_BoundarySize",.15f,3);
        }

        void Surfaces(DrawerLookSettings look)
        {
            surfaceChoice = GUILayout.Toolbar(surfaceChoice,new[] { "信纸正面","信纸折面","魔法书封","字典书封" });
            Material material = new[] { look.paper,look.paperFold,look.magicBook,look.dictionary }[surfaceChoice];
            ColorField(material,"底色","_BaseColor"); ColorField(material,"笔触颜色","_StrokeColor");
            ColorField(material,"阴影颜色","_ShadowColor"); ColorField(material,"亮部颜色","_HighlightColor"); ColorField(material,"交界颜色","_ShadowEdgeColor");
            Float(material,"原纹理保留","_TextureInfluence",0,1);
            float size = 1/Mathf.Max(.5f,material.GetFloat("_BrushScale"));
            float nextSize = EditorGUILayout.Slider("笔触尺寸",size,1f/24,2);
            if (nextSize != size) material.SetFloat("_BrushScale",1/nextSize);
            Float(material,"笔触密度","_StrokeDensity",0,2); Float(material,"笔触强度","_BrushStrength",0,1);
            Float(material,"笔触长宽比","_StrokeAspect",.25f,8); Float(material,"笔触方向","_StrokeAngle",-180,180); Float(material,"方向散差","_StrokeScatter",0,90);
            Float(material,"阴影填充","_ShadowFill",0,1); Float(material,"交界宽度","_ShadowEdgeWidth",.02f,.4f); Float(material,"交界着色","_ShadowEdgeStrength",0,1);
            Float(material,"明暗色阶","_ColorSteps",2,12); Float(material,"厚涂起伏","_Impasto",0,2); Float(material,"表面光滑度","_Smoothness",0,1);
        }

        static void Post(DrawerLookSettings look)
        {
            look.oilEnabled = EditorGUILayout.Toggle("启用全画面油画",look.oilEnabled);
            EditorGUILayout.LabelField("复用原油画实验的后处理；独立于信纸与书封的材质笔触。",EditorStyles.wordWrappedMiniLabel);
            look.oilStrength = EditorGUILayout.Slider("油画融合",look.oilStrength,0,1);
            look.oilRadius = EditorGUILayout.Slider("笔触半径（像素）",look.oilRadius,0,8);
            look.oilAspect = EditorGUILayout.Slider("笔触长宽比",look.oilAspect,1,3);
            look.oilAngle = EditorGUILayout.Slider("笔触方向",look.oilAngle,-180,180);
            look.oilColorSteps = EditorGUILayout.IntSlider("颜色分组（0 关闭）",look.oilColorSteps,0,32);
            look.oilCompare = EditorGUILayout.Toggle("左右对比",look.oilCompare);
        }

        static void Lighting(DrawerLookSettings look)
        {
            look.sunlight = EditorGUILayout.Slider("阳光强度",look.sunlight,.2f,2);
            look.sunElevation = EditorGUILayout.Slider("太阳高度",look.sunElevation,25,85);
            look.sunAzimuth = EditorGUILayout.Slider("太阳方位",look.sunAzimuth,-180,180);
            look.shadowStrength = EditorGUILayout.Slider("投影强度",look.shadowStrength,0,1);
            look.sunlightColor = EditorGUILayout.ColorField("阳光颜色",look.sunlightColor);
            look.ambientColor = EditorGUILayout.ColorField("环境补光颜色",look.ambientColor);
        }

        static void ColorField(Material material, string label, string property)
        {
            Color value = material.GetColor(property), next = EditorGUILayout.ColorField(label,value);
            if (value != next) material.SetColor(property,next);
        }

        static void Float(Material material, string label, string property, float minimum, float maximum)
        {
            float value = material.GetFloat(property), next = EditorGUILayout.Slider(label,value,minimum,maximum);
            if (value != next) material.SetFloat(property,next);
        }
    }
}
