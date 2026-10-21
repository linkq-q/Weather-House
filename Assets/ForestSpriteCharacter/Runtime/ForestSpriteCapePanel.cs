using System;
using UnityEngine;

namespace ForestSpriteCharacter
{
    [DisallowMultipleComponent]
    public sealed class ForestSpriteCapePanel : MonoBehaviour
    {
        public ForestSpriteCapeCloth cape;

        static ForestSpriteCapePanel activePanel;
        bool expanded = true;
        bool fineTuning;
        bool pointerCaptured;
        int previousScreenWidth;
        Vector2 scroll;
        string status = "实时试调 · 调好后点击保存参数";
        GUIStyle label, heading, button, toggle, note;

        public static bool PointerOverPanel
        {
            get
            {
                if (activePanel == null || !activePanel.isActiveAndEnabled) return false;
                Vector2 pointer = new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);
                return activePanel.PanelRect.Contains(pointer) ||
                    (activePanel.pointerCaptured && (Input.GetMouseButton(0) || Input.GetMouseButton(1)));
            }
        }

        Rect PanelRect
        {
            get
            {
                float width = Mathf.Min(expanded ? 300f : 150f, Mathf.Max(60f, Screen.width - 24f));
                float x = Mathf.Max(12f, Screen.width - width - 12f);
                // The existing controls occupy the upper-left 300 by 295 area.
                float y = x < 324f ? Mathf.Min(319f, Mathf.Max(12f, Screen.height - 46f)) : 12f;
                float height = expanded ? Mathf.Min(790f, Screen.height - y - 12f) : 34f;
                return new Rect(x, y, width, Mathf.Max(34f, height));
            }
        }

        void OnEnable()
        {
            activePanel = this;
            previousScreenWidth = Screen.width;
            expanded = Screen.width >= 660;
            if (cape == null) cape = GetComponent<ForestSpriteCapeCloth>();
        }

        void OnDisable()
        {
            pointerCaptured = false;
            if (activePanel == this) activePanel = null;
        }

        void EnsureStyles()
        {
            if (label != null) return;
            Font font = Font.CreateDynamicFontFromOSFont("Microsoft YaHei", 14);
            label = new GUIStyle(GUI.skin.label) { font = font, fontSize = 14, wordWrap = true };
            heading = new GUIStyle(label) { fontStyle = FontStyle.Bold };
            button = new GUIStyle(GUI.skin.button) { font = font, fontSize = 14 };
            toggle = new GUIStyle(GUI.skin.toggle) { font = font, fontSize = 14, wordWrap = true };
            note = new GUIStyle(label) { fontSize = 12 };
        }

        void OnGUI()
        {
            EnsureStyles();
            if (previousScreenWidth >= 660 && Screen.width < 660) expanded = false;
            previousScreenWidth = Screen.width;
            Rect area = PanelRect;
            Event current = Event.current;
            if (current.rawType == EventType.MouseDown && area.Contains(current.mousePosition))
                pointerCaptured = true;
            if (current.rawType == EventType.MouseUp) pointerCaptured = false;

            GUILayout.BeginArea(area, GUI.skin.box);
            if (!expanded)
            {
                if (GUILayout.Button("展开披风调节", button, GUILayout.Height(24f))) expanded = true;
                GUILayout.EndArea();
                return;
            }

            GUILayout.BeginHorizontal();
            GUILayout.Label("实时调节 · 展翼披风", heading);
            if (GUILayout.Button("收起", button, GUILayout.Width(56f))) expanded = false;
            GUILayout.EndHorizontal();
            scroll = GUILayout.BeginScrollView(scroll, false, true);
            if (cape == null)
            {
                GUILayout.Label("当前角色未连接披风。", label);
            }
            else
            {
                bool preview = GUILayout.Toggle(cape.WindPreview, "站立试风", toggle);
                if (preview != cape.WindPreview) cape.SetWindPreview(preview);
                if (!cape.MotionEnabled) GUILayout.Label("物理已暂停，按 P 开启后查看变化。", note);
                if (Screen.width < 660) GUILayout.Label("窗口较窄，调好后收起面板方便观察。", note);
                GUILayout.Space(5f);

                bool changed = false;
                cape.settings.windStrength = Slider("风力", cape.settings.windStrength, 0f, 2f, ref changed);
                cape.settings.lift = Slider("向后抬起", cape.settings.lift, 0f, 1f, ref changed);
                cape.settings.wingSpread = Slider("翼展（两侧展开）", cape.settings.wingSpread, 0f, 1f, ref changed, 100f, "%");
                cape.settings.sCurve = Slider("S 形弯曲", cape.settings.sCurve, 0f, 1f, ref changed);
                cape.settings.waveAmplitude = Slider("波浪幅度", cape.settings.waveAmplitude, 0f, .12f, ref changed, 120f, "°");
                cape.settings.waveSpeed = Slider("波浪速度", cape.settings.waveSpeed, .2f, 2f, ref changed, 1f, " 次/秒");
                cape.settings.clothFreedom = Slider("自由摆幅", cape.settings.clothFreedom, 0f, 1f, ref changed, 100f, "%");
                cape.settings.settleTime = Slider("回落时间", cape.settings.settleTime, .1f, 2f, ref changed, 1f, " 秒");
                fineTuning = GUILayout.Toggle(fineTuning, "细调", toggle);
                if (fineTuning)
                {
                    cape.settings.damping = Slider("阻尼", cape.settings.damping, .02f, .5f, ref changed);
                    cape.settings.bendStiffness = Slider("弯曲刚度", cape.settings.bendStiffness, .02f, .8f, ref changed);
                }
                GUILayout.Space(6f);
                GUILayout.Label("移动调节", heading);
                cape.settings.walkStride = Slider("走路步幅（原步幅 → 大步）", cape.settings.walkStride, 0f, 1f, ref changed, 100f, "%");
                cape.settings.walkSpeed = Slider("走路速度", cape.settings.walkSpeed, .3f, 1.2f, ref changed, 1f, " m/s");
                cape.settings.runSpeed = Slider("跑步速度", cape.settings.runSpeed, 1f, 2f, ref changed, 1f, " m/s");
                if (changed)
                {
                    cape.ApplySettings();
                    status = "实时试调 · 尚未保存";
                }

                GUILayout.Space(6f);
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("保存参数", button)) Run(cape.SaveSettings, "已保存 · 停止 Play 后仍保留", "保存失败");
                if (GUILayout.Button("载入", button)) Run(cape.LoadSettings, "已载入保存参数", "载入失败");
                if (GUILayout.Button("恢复推荐", button)) Run(cape.ResetSettings, "已恢复推荐 · 点击保存可保留", "恢复失败");
                GUILayout.EndHorizontal();
                GUILayout.Label(status, note);
                GUILayout.Label("波浪幅度控制整片大波浪；自由摆幅控制局部细动。翼展控制两侧展开，回落时间控制停下后收拢速度。", note);
            }
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        float Slider(string title, float value, float minimum, float maximum, ref bool changed,
            float displayScale = 1f, string unit = "")
        {
            GUILayout.Label(title + "  " + (value * displayScale).ToString(unit == "%" ? "0" : "0.00") + unit, label);
            float next = GUILayout.HorizontalSlider(value, minimum, maximum);
            if (!Mathf.Approximately(next, value)) changed = true;
            GUILayout.Space(3f);
            return next;
        }

        void Run(Action operation, string success, string failure)
        {
            try
            {
                operation();
                status = success;
            }
            catch (Exception exception)
            {
                status = failure + "，当前试调值仍可继续使用。";
                Debug.LogException(exception, this);
            }
        }
    }
}
