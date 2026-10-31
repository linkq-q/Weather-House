using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace TimelineColorEditor
{
    /// <summary>
    /// 从 Timeline 右键菜单弹出的快速调色板。
    /// 选一个颜色即把该颜色作为整体写入 R/G/B/A 四条曲线：
    /// 默认写入播放头时间点，也可以一次性写入该属性的全部关键帧。
    /// </summary>
    public sealed class TrackColorPalettePopup : EditorWindow
    {
        const float kWidth = 330f;
        const float kHeight = 370f;

        [NonSerialized] TimelineColorPalette m_Palette;
        [NonSerialized] List<ColorCurveProperty> m_Properties = new List<ColorCurveProperty>();

        int m_PropertyIndex;
        Color m_Color = Color.white;
        bool[] m_Channels = { true, true, true, true };
        bool m_AllKeys;
        bool m_AddToPalette = true;
        bool m_Hdr;
        ColorKeyInterpolation m_Interpolation = ColorKeyInterpolation.Auto;
        string m_Status = string.Empty;

        ColorCurveProperty CurrentProperty
        {
            get
            {
                if (m_Properties.Count == 0)
                    return null;
                return m_Properties[Mathf.Clamp(m_PropertyIndex, 0, m_Properties.Count - 1)];
            }
        }

        static double Playhead
        {
            get { return TimelineEditorBridge.GetPlayhead(); }
        }

        /// <summary>在指定屏幕区域下方弹出调色板。</summary>
        public static TrackColorPalettePopup Show(Rect activatorRect, IEnumerable<ColorCurveProperty> properties)
        {
            var window = CreateInstance<TrackColorPalettePopup>();
            window.m_Properties = properties != null ? properties.ToList() : new List<ColorCurveProperty>();
            window.m_Palette = TimelineColorPalette.Shared;

            var property = window.CurrentProperty;
            if (property != null)
                window.m_Color = property.Evaluate(Playhead);

            window.titleContent = new GUIContent("颜色调色板");
            window.ShowAsDropDown(activatorRect, new Vector2(kWidth, kHeight));
            window.Focus();
            return window;
        }

        void OnEnable()
        {
            //Debug.Log("onenable");
            // 注意：下拉窗口在轮盘抢走焦点后会立即关闭（OnDisable），因此取色过程由静态会话负责，
            // 这里的订阅只用于"窗口还活着时"的显示同步。
            ColorWheelSession.ColorChanged += OnWheelColorChanged;
            ColorWheelSession.Finished += OnWheelFinished;
            TimelineColorPalette.Changed += OnPaletteChanged;
        }

        void OnDisable()
        {
            ColorWheelSession.ColorChanged -= OnWheelColorChanged;
            ColorWheelSession.Finished -= OnWheelFinished;
            TimelineColorPalette.Changed -= OnPaletteChanged;
            ColorCurveBar.ReleaseTexture();
        }

        /// <summary>共享调色板内容变化（可能来自窗口或取色会话）。</summary>
        void OnPaletteChanged()
        {
            Repaint();
        }

        /// <summary>处理内置取色器发给本窗口的 "ColorPickerChanged" 命令事件。</summary>
        void HandleColorPickerCommand()
        {
            var evt = Event.current;
            if (evt == null || evt.type != EventType.ExecuteCommand || evt.commandName != "ColorPickerChanged")
                return;

            if (UnityColorWheel.TryGetColor(out var color))
            {
                m_Color = color;
                Repaint();
            }

            evt.Use();
        }

        /// <summary>
        /// 打开 Unity 默认调色轮盘。
        /// 取色与写回由静态会话 <see cref="ColorWheelSession"/> 完成，
        /// 因此即使本下拉窗口在轮盘获得焦点后立即关闭，取色结果也不会丢失。
        /// </summary>
        void OpenColorWheel()
        {
            var property = CurrentProperty;
            if (property == null)
                return;

            var opened = ColorWheelSession.Open(new ColorWheelSettings
            {
                color = m_Color,
                hdr = m_Hdr,
                property = property,
                applyAllKeys = m_AllKeys,
                time = Playhead,
                channels = m_Channels,
                interpolation = m_Interpolation,
                autoApply = true,
                addRecent = true,
                addToPalette = m_AddToPalette
            });

            if (!opened)
            {
                m_Status = ColorWheelSession.LastMessage;
                Repaint();
                return;
            }

            m_Status = "调色轮盘已打开：取色后关闭轮盘即会写入。";
            Repaint();
        }

        void OnWheelColorChanged(Color color)
        {
            //Debug.Log("ggg");
            m_Color = color;
            Repaint();
        }

        /// <summary>取色会话结束（窗口若已被下拉关闭则不会收到）。</summary>
        void OnWheelFinished(Color color, string message)
        {
            m_Color = color;
            m_Palette?.AddRecent(color);
            m_Status = message;
            Repaint();
        }

        void OnGUI()
        {
            HandleColorPickerCommand();

            if (m_Palette == null)
                m_Palette = TimelineColorPalette.Shared;

            var property = CurrentProperty;
            if (property == null)
            {
                EditorGUILayout.HelpBox("当前选中的对象上没有可编辑的颜色属性。", MessageType.Info);
                if (GUILayout.Button("关闭"))
                    Close();
                return;
            }

            EditorGUILayout.LabelField(property.target.label, EditorStyles.miniBoldLabel);

            if (m_Properties.Count > 1)
            {
                var index = EditorGUILayout.Popup("颜色属性", m_PropertyIndex, m_Properties.Select(p => p.displayName).ToArray());
                if (index != m_PropertyIndex)
                {
                    m_PropertyIndex = index;
                    m_Color = CurrentProperty.Evaluate(Playhead);
                    m_Status = string.Empty;
                }
            }
            else
            {
                EditorGUILayout.LabelField("颜色属性", property.displayName);
            }

            // 颜色曲线预览（含播放头位置）
            property.GetTimeRange(out var start, out var end);
            var barRect = GUILayoutUtility.GetRect(10f, 18f, GUILayout.ExpandWidth(true));
            ColorCurveBar.Draw(barRect, property, start, end);

            var playheadX = ColorCurveBar.TimeToX(barRect, Playhead, start, end);
            if (playheadX >= barRect.x && playheadX <= barRect.xMax)
                EditorGUI.DrawRect(new Rect(playheadX - 1f, barRect.y, 2f, barRect.height), new Color(0.95f, 0.25f, 0.25f));

            // 调色板
            var columns = Mathf.Max(1, m_Palette.columns);
            var gridHeight = Mathf.Min(
                TimelineColorPalette.GridHeight(m_Palette.swatches.Count, columns),
                5f * (TimelineColorPalette.SwatchSize + TimelineColorPalette.SwatchSpacing));

            var gridRect = GUILayoutUtility.GetRect(10f, gridHeight, GUILayout.ExpandWidth(true));
            if (m_Palette.DrawSwatchGrid(gridRect, m_Color, out var picked))
            {
                m_Color = picked;
                m_Palette.AddRecent(picked);
                Apply(false);
            }

            EditorGUI.BeginChangeCheck();
            m_Color = EditorGUILayout.ColorField(GUIContent.none, m_Color, true, true, m_Hdr);
            if (EditorGUI.EndChangeCheck())
                Apply(false);

            using (new EditorGUILayout.HorizontalScope())
            {
                for (int i = 0; i < 4; i++)
                    m_Channels[i] = EditorGUILayout.ToggleLeft(ColorCurveProperty.ChannelLabel[i], m_Channels[i], GUILayout.Width(34f));
                GUILayout.FlexibleSpace();
                m_Hdr = EditorGUILayout.ToggleLeft("HDR", m_Hdr, GUILayout.Width(52f));
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                m_AllKeys = EditorGUILayout.ToggleLeft(
                    new GUIContent("写入全部关键帧", "不勾选时，取色只会写入播放头所在的时间点"), m_AllKeys, GUILayout.Width(104f));

                m_AddToPalette = EditorGUILayout.ToggleLeft(
                    new GUIContent("加入调色板", "把取到的颜色作为新色块加入调色板，之后可直接点选"), m_AddToPalette, GUILayout.Width(88f));
            }

            m_Interpolation = (ColorKeyInterpolation)EditorGUILayout.Popup(
                new GUIContent("新增关键帧插值", "平滑 = 两侧限制自动（Clamped Auto，默认）；线性；恒定（阶跃）"),
                (int)m_Interpolation, ColorCurveOperations.InterpolationLabels);

            if (GUILayout.Button(new GUIContent("调色轮盘…（Unity 默认取色器）",
                    "打开 Unity 内置色轮取色窗口。注意：本下拉窗口在轮盘获得焦点后会自动关闭，取色与写回由会话继续完成"),
                    GUILayout.Height(20f)))
                OpenColorWheel();

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("写入播放头"))
                    Apply(true);

                if (GUILayout.Button(new GUIContent("＋调色板", "把当前颜色作为新色块加入调色板（与上方\u201c加入调色板\u201d开关无关，这是立即添加）")))
                {
                    m_Status = TimelineColorPalette.AddColor(m_Color);
                    m_Palette.AddRecent(m_Color);
                    Repaint();
                }

                if (GUILayout.Button("完整编辑器"))
                    TrackColorCurveWindow.OpenFor(property);

                if (GUILayout.Button("关闭"))
                    Close();
            }

            if (!string.IsNullOrEmpty(m_Status))
                EditorGUILayout.HelpBox(m_Status, MessageType.None);
        }

        void Apply(bool atPlayhead)
        {
            var property = CurrentProperty;
            if (property == null)
                return;

            if (!atPlayhead && m_AllKeys)
            {
                m_Status = ColorCurveOperations.SetAllKeys(property, m_Color, m_Channels)
                    ? "已把 #" + ColorUtility.ToHtmlStringRGBA(m_Color) + " 写入全部关键帧。"
                    : "该属性还没有关键帧：请先取消勾选后写入播放头关键帧。";
                Repaint();
                return;
            }

            var time = Playhead;
            m_Status = ColorCurveOperations.SetColorKey(property, time, m_Color, m_Channels, m_Interpolation)
                ? string.Format("已在 {0:0.000}s 写入 #{1}。", ColorCurveOperations.SnapTime(time, property.target.frameRate), ColorUtility.ToHtmlStringRGBA(m_Color))
                : "写入失败：请至少启用一个通道。";
            Repaint();
        }
    }
}
