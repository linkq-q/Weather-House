using System;
using System.Collections.Generic;
using System.Linq;
using Unity.VisualScripting.FullSerializer;
using UnityEditor;
using UnityEngine;
using UnityObject = UnityEngine.Object;

namespace TimelineColorEditor
{
    /// <summary>
    /// 轨道颜色曲线编辑器。
    /// 在 Timeline 的曲线编辑模式（Track Properties / Clip Properties）旁边，
    /// 用调色盘的方式统一编辑 Color 属性的 R/G/B/A 四个通道：
    /// 取一个颜色即同时写入四条曲线，避免逐通道手动对齐关键帧。
    /// </summary>
    public sealed class TrackColorCurveWindow : EditorWindow
    {
        /// <summary>调色板取色后的写入方式。</summary>
        enum ApplyMode
        {
            WriteAtTime = 0,
            AllKeys = 1
        }

        static readonly string[] k_ApplyModeLabels = { "写入当前时间点", "写入全部关键帧" };

        const string kTitle = "轨道颜色曲线";
        const string k_JumpFrameControl = "TrackColorCurveWindow.JumpFrame";

        /// <summary>"对称跳转"的对称中心帧：x → 2 * 720 - x。</summary>
        const int k_MirrorCenterFrame = 720;

        [NonSerialized] TimelineColorPalette m_Palette;
        [NonSerialized] List<ColorCurveTarget> m_Targets = new List<ColorCurveTarget>();
        [NonSerialized] List<ColorCurveProperty> m_Properties = new List<ColorCurveProperty>();
        [NonSerialized] List<float> m_KeyTimes = new List<float>();
        [NonSerialized] bool m_NeedRefresh = true;
        [NonSerialized] Vector2 m_Scroll;

        [SerializeField] int m_TargetIndex;
        [SerializeField] string m_SelectedPropertyId = string.Empty;
        [SerializeField] Color m_WorkingColor = Color.white;
        [SerializeField] bool[] m_Channels = { true, true, true, true };
        [SerializeField] bool m_Hdr;
        [SerializeField] bool m_FollowPlayhead = true;
        [SerializeField] bool m_ApplyOnPick = true;
        [SerializeField] bool m_UseManualTime;
        [SerializeField] double m_ManualTime;
        [SerializeField] ApplyMode m_ApplyMode = ApplyMode.WriteAtTime;
        [SerializeField] ColorKeyInterpolation m_Interpolation = ColorKeyInterpolation.Auto;
        [SerializeField] bool m_ShowKeyList = true;
        [SerializeField] Vector2 m_KeyListScroll;
        [SerializeField] List<float> m_ExpandedKeyTimes = new List<float>();
        [SerializeField] bool m_AutoAddToPalette = true;
        [SerializeField] int m_JumpFrame;

        [SerializeField] Color m_GradientFrom = Color.black;
        [SerializeField] Color m_GradientTo = Color.white;
        [SerializeField] int m_GradientSteps = 5;
        [SerializeField] bool m_GradientUseExistingKeys = true;
        [SerializeField] double m_GradientStart;
        [SerializeField] double m_GradientEnd = 1d;
        [SerializeField] string m_GradientInitializedFor = string.Empty;

        [NonSerialized] string m_Status = string.Empty;
        [NonSerialized] double m_LastPlayhead = -1d;
        [NonSerialized] double m_NextDataCheck;
        [NonSerialized] int m_LastCurveHash = -1;

        #region 打开窗口

        [MenuItem("Window/Sequencing/Track Color Curve Editor %#k")]
        public static TrackColorCurveWindow Open()
        {
            var window = GetWindow<TrackColorCurveWindow>(false, kTitle, true);
            window.minSize = new Vector2(340f, 460f);
            window.m_NeedRefresh = true;
            window.Show();
            return window;
        }

        /// <summary>从 Timeline 右键菜单打开，并聚焦到指定颜色属性。</summary>
        public static TrackColorCurveWindow OpenFor(ColorCurveProperty property)
        {
            var window = Open();
            if (property != null)
            {
                window.m_SelectedPropertyId = property.id;
                window.m_WorkingColor = property.Evaluate(TimelineEditorBridge.GetPlayhead());
            }
            window.m_NeedRefresh = true;
            window.Repaint();
            return window;
        }

        #endregion

        #region 生命周期

        void OnEnable()
        {
            m_Palette = TimelineColorPalette.Shared;
            m_NeedRefresh = true;
            Selection.selectionChanged += OnSelectionChanged;
            EditorApplication.update += OnEditorUpdate;
            ColorWheelSession.ColorChanged += OnWheelColorChanged;
            ColorWheelSession.Finished += OnWheelFinished;
            TimelineColorPalette.Changed += OnPaletteChanged;
        }

        void OnDisable()
        {
            Selection.selectionChanged -= OnSelectionChanged;
            EditorApplication.update -= OnEditorUpdate;
            ColorWheelSession.ColorChanged -= OnWheelColorChanged;
            ColorWheelSession.Finished -= OnWheelFinished;
            TimelineColorPalette.Changed -= OnPaletteChanged;
            ColorCurveBar.ReleaseTexture();
            m_Palette?.Save();
        }

        void OnSelectionChanged()
        {
            m_NeedRefresh = true;
            Repaint();
        }

        void OnEditorUpdate()
        {
            // 定时检查曲线数据是否被外部修改（例如直接在 Timeline 曲线视图中编辑），保持列表同步。
            var now = EditorApplication.timeSinceStartup;
            if (now >= m_NextDataCheck)
            {
                m_NextDataCheck = now + 0.2d;
                var hash = CurrentProperty != null ? ComputeCurveHash(CurrentProperty) : 0;
                if (hash != m_LastCurveHash)
                {
                    m_LastCurveHash = hash;
                    RefreshKeys();
                    Repaint();
                }
            }

            if (!m_FollowPlayhead)
                return;

            var playhead = TimelineEditorBridge.GetPlayhead();
            if (Mathf.Abs((float)(playhead - m_LastPlayhead)) < 0.0005f)
                return;

            m_LastPlayhead = playhead;
            var property = CurrentProperty;
            if (property != null)
                m_WorkingColor = property.Evaluate(playhead);

            Repaint();
        }

        static int ComputeCurveHash(ColorCurveProperty property)
        {
            var hash = 17;
            for (int i = 0; i < 4; i++)
            {
                var curve = property.GetCurve(i);
                if (curve == null)
                    continue;

                hash = hash * 31 + curve.length;
                if (curve.length > 0)
                {
                    hash = hash * 31 + curve.keys[0].time.GetHashCode();
                    hash = hash * 31 + curve.keys[curve.length - 1].time.GetHashCode();
                }
            }
            return hash;
        }

        #endregion

        #region 数据

        ColorCurveTarget CurrentTarget
        {
            get { return m_TargetIndex >= 0 && m_TargetIndex < m_Targets.Count ? m_Targets[m_TargetIndex] : null; }
        }

        ColorCurveProperty CurrentProperty
        {
            get
            {
                if (m_Properties.Count == 0)
                    return null;
                return m_Properties.FirstOrDefault(p => p.id == m_SelectedPropertyId) ?? m_Properties[0];
            }
        }

        void RefreshTargets()
        {
            m_Targets = ColorCurveTargets.GetSelectedTargets();
            if (m_TargetIndex >= m_Targets.Count)
                m_TargetIndex = 0;
            RefreshProperties();
        }

        void RefreshProperties()
        {
            var target = CurrentTarget;
            m_Properties = target != null ? ColorCurveTargets.GetColorProperties(target) : new List<ColorCurveProperty>();

            if (m_Properties.Count > 0 && m_Properties.All(p => p.id != m_SelectedPropertyId))
            {
                m_SelectedPropertyId = m_Properties[0].id;
                m_WorkingColor = m_Properties[0].Evaluate(GetTime());
            }

            RefreshKeys();
        }

        void RefreshKeys()
        {
            var property = CurrentProperty;
            m_KeyTimes = property != null ? property.GetKeyTimes() : new List<float>();
            m_LastCurveHash = property != null ? ComputeCurveHash(property) : -1;

            // 丢弃已被删除的关键帧的展开状态，避免列表无限增长。
            if (m_ExpandedKeyTimes.Count > 0)
                m_ExpandedKeyTimes.RemoveAll(t => !m_KeyTimes.Any(k => Mathf.Abs(k - t) < 0.0001f));

            if (property != null && m_GradientInitializedFor != property.id)
            {
                property.GetTimeRange(out var start, out var end);
                m_GradientStart = start;
                m_GradientEnd = end;
                m_GradientInitializedFor = property.id;
            }
        }

        double GetTime()
        {
            return m_UseManualTime ? m_ManualTime : TimelineEditorBridge.GetPlayhead();
        }

        void SetTime(double time)
        {
            if (m_UseManualTime)
                m_ManualTime = time;
            else
                TimelineEditorBridge.TrySetPlayhead(time);

            m_LastPlayhead = time;
        }

        /// <summary>输入框未获得焦点时，让帧号跟随当前播放头（或自定义写入时间）。</summary>
        void SyncJumpFrame(ColorCurveProperty property)
        {
            if (Event.current.type != EventType.Layout || GUI.GetNameOfFocusedControl() == k_JumpFrameControl)
                return;

            var frameRate = Mathf.Max(1f, property.target.frameRate);
            var frame = Mathf.Max(0, Mathf.RoundToInt((float)(GetTime() * frameRate)));
            if (frame == m_JumpFrame)
                return;

            m_JumpFrame = frame;
            Repaint();
        }

        /// <summary>手工输入帧号跳转：移动播放头并读取该帧的颜色到调色盘。</summary>
        void JumpToFrame(int frame)
        {
            var property = CurrentProperty;
            if (property == null)
                return;

            var frameRate = Mathf.Max(1f, property.target.frameRate);
            frame = Mathf.Max(0, frame);
            var time = frame / (double)frameRate;

            var note = string.Empty;
            if (m_UseManualTime)
            {
                m_ManualTime = time;
            }
            else if (!TimelineEditorBridge.TrySetPlayhead(time))
            {
                // Timeline 窗口不可用时退回到自定义写入时间，保证跳转依然生效。
                m_UseManualTime = true;
                m_ManualTime = time;
                note = "，Timeline 窗口不可用，已改为自定义写入时间";
            }

            m_JumpFrame = frame;
            m_LastPlayhead = time;
            m_WorkingColor = property.Evaluate(time);
            m_Status = string.Format("已跳转到第 {0} 帧（{1:0.000}s）{2}。", frame, time, note);
            Repaint();
            GUI.FocusControl(null);
        }

        /// <summary>帧号输入框是否刚按下回车（需在绘制输入框之前调用）。</summary>
        bool IsJumpFrameEnterPressed()
        {
            var evt = Event.current;
            if (evt == null || evt.type != EventType.KeyDown)
                return false;

            if (evt.keyCode != KeyCode.Return && evt.keyCode != KeyCode.KeypadEnter)
                return false;

            if (GUI.GetNameOfFocusedControl() != k_JumpFrameControl)
                return false;

            // 吞掉回车，避免编辑器发出提示音或其它控件响应。
            evt.Use();
            return true;
        }

        /// <summary>把帧号输入框同步到指定时间（不触发跳转）。</summary>
        void JumpFrameFieldTo(double time, ColorCurveProperty property)
        {
            var frameRate = Mathf.Max(1f, property.target.frameRate);
            m_JumpFrame = Mathf.Max(0, Mathf.RoundToInt((float)(time * frameRate)));
        }

        #region 调色轮盘

        /// <summary>
        /// 打开 Unity 默认调色轮盘。
        /// 取色过程交给静态会话 <see cref="ColorWheelSession"/>，本窗口只负责传入写入参数与显示。
        /// </summary>
        void OpenColorWheel()
        {
            var property = CurrentProperty;
            if (property == null)
                return;

            var opened = ColorWheelSession.Open(new ColorWheelSettings
            {
                color = m_WorkingColor,
                hdr = m_Hdr,
                property = property,
                applyAllKeys = m_ApplyMode == ApplyMode.AllKeys,
                time = GetTime(),
                channels = m_Channels,
                interpolation = m_Interpolation,
                autoApply = m_ApplyOnPick,
                addRecent = true,
                addToPalette = m_AutoAddToPalette
            });

            if (!opened)
            {
                m_Status = ColorWheelSession.LastMessage;
                Repaint();
                return;
            }

            m_Status = m_ApplyOnPick
                ? "调色轮盘已打开：拖动取色，关闭轮盘后自动写入。"
                : "调色轮盘已打开：取色后请点击下方写入按钮。";
            Repaint();
        }

        /// <summary>轮盘拖动过程中实时同步到调色盘（不写曲线，避免刷出一堆 Undo）。</summary>
        void OnWheelColorChanged(Color color)
        {
            m_WorkingColor = color;
            //Debug.Log("!!!");
            Repaint();
        }

        /// <summary>
        /// 处理内置取色器发给本窗口的 "ColorPickerChanged" 命令事件。
        /// 点击 ColorField 色块打开轮盘时，Unity 就是通过这条通道通知宿主视图的。
        /// </summary>
        void HandleColorPickerCommand()
        {
            var evt = Event.current;
            if (evt == null || evt.type != EventType.ExecuteCommand || evt.commandName != "ColorPickerChanged")
                return;

            if (UnityColorWheel.TryGetColor(out var color))
            {
                m_WorkingColor = color;
                Repaint();
            }

            evt.Use();
        }

        /// <summary>取色会话结束：刷新界面（曲线可能已被会话写回）。</summary>
        void OnWheelFinished(Color color, string message)
        {
            m_WorkingColor = color;
            m_Status = message;
            RefreshKeys();
            Repaint();
        }

        /// <summary>共享调色板内容变化（可能来自弹窗或取色会话）。</summary>
        void OnPaletteChanged()
        {
            Repaint();
        }

        #endregion

        #endregion

        #region 界面

        void OnGUI()
        {
            HandleColorPickerCommand();

            if (m_Palette == null)
                m_Palette = TimelineColorPalette.Shared;

            if (m_NeedRefresh)
            {
                m_NeedRefresh = false;
                RefreshTargets();
            }

            m_Scroll = EditorGUILayout.BeginScrollView(m_Scroll);

            DrawTargetSection();

            var property = CurrentProperty;
            if (property != null)
            {
                DrawColorSection(property);
                DrawPaletteSection(property);
                DrawCurveSection(property);
                DrawOperationSection(property);
            }
            else
            {
                EditorGUILayout.HelpBox(
                    "请在 Timeline 窗口中选中一个轨道（或片段）。\n\n" +
                    "轨道需要满足以下条件才会显示颜色属性：\n" +
                    "· 颜色字段位于脚本 PlayableBehaviour 字段（例如 template）之下；\n" +
                    "· 未被 [NotKeyable] 标记。",
                    MessageType.Info);

                if (GUILayout.Button("手动刷新"))
                    m_NeedRefresh = true;
            }

            DrawStatus();

            EditorGUILayout.EndScrollView();
        }

        void DrawTargetSection()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField("选中目标", EditorStyles.boldLabel, GUILayout.Width(60f));
                    using (new EditorGUI.DisabledScope(true))
                        EditorGUILayout.ObjectField(CurrentTarget != null ? CurrentTarget.owner : null, typeof(UnityObject), true);
                    if (GUILayout.Button("刷新", GUILayout.Width(46f)))
                    {
                        m_NeedRefresh = true;
                        GUI.FocusControl(null);
                    }
                }

                if (m_Targets.Count > 1)
                {
                    EditorGUI.BeginChangeCheck();
                    var labels = m_Targets.Select(t => t.label).ToArray();
                    var newIndex = EditorGUILayout.Popup("目标", m_TargetIndex, labels);
                    if (EditorGUI.EndChangeCheck())
                    {
                        m_TargetIndex = newIndex;
                        RefreshProperties();
                    }
                }
                else if (CurrentTarget != null)
                {
                    EditorGUILayout.LabelField(" ", CurrentTarget.label, EditorStyles.miniLabel);
                }

                if (m_Properties.Count == 0)
                    return;

                EditorGUILayout.Space(2f);
                EditorGUILayout.LabelField("颜色属性（统一编辑 RGBA）", EditorStyles.boldLabel);

                var current = m_Properties.FindIndex(p => p.id == m_SelectedPropertyId);
                if (current < 0)
                    current = 0;

                var picked = GUILayout.SelectionGrid(current, m_Properties.Select(p => p.displayName).ToArray(), 2);
                if (picked != current)
                {
                    m_SelectedPropertyId = m_Properties[picked].id;
                    m_WorkingColor = m_Properties[picked].Evaluate(GetTime());
                    RefreshKeys();
                    GUI.FocusControl(null);
                }

                var property = CurrentProperty;
                if (property != null)
                {
                    EditorGUILayout.LabelField(
                        property.hasAnyCurve ? "曲线：已生成（与 Timeline 曲线视图共用同一份 AnimationClip）" : "曲线：未生成（写入关键帧时会自动创建）",
                        EditorStyles.miniLabel);
                    EditorGUILayout.LabelField("曲线绑定：", property.bindingName + ".r / .g / .b / .a", EditorStyles.miniLabel);
                }
            }
        }

        void DrawColorSection(ColorCurveProperty property)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField("调色盘", EditorStyles.boldLabel, GUILayout.Width(60f));
                    m_ApplyOnPick = EditorGUILayout.ToggleLeft(
                        new GUIContent("选色后立即写入", "关闭后只在点击下方按钮时写入曲线"), m_ApplyOnPick);
                }

                EditorGUI.BeginChangeCheck();
                var color = EditorGUILayout.ColorField(GUIContent.none, m_WorkingColor, true, true, m_Hdr);
                if (EditorGUI.EndChangeCheck())
                {
                    m_WorkingColor = color;
                    if (m_ApplyOnPick)
                        ApplyFromPick();
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(new GUIContent("参与统一编辑", "取消勾选可锁定该通道：统一编辑时不会写入或修改它"), GUILayout.Width(90f));
                    for (int i = 0; i < 4; i++)
                        m_Channels[i] = EditorGUILayout.ToggleLeft(ColorCurveProperty.ChannelLabel[i], m_Channels[i], GUILayout.Width(36f));
                    GUILayout.FlexibleSpace();
                    m_Hdr = EditorGUILayout.ToggleLeft("HDR", m_Hdr, GUILayout.Width(56f));
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("读取播放头颜色"))
                    {
                        m_WorkingColor = property.Evaluate(TimelineEditorBridge.GetPlayhead());
                        GUI.FocusControl(null);
                    }

                    if (GUILayout.Button(new GUIContent("添加到调色板", "把当前调色盘颜色作为一个新色块加入下方调色板，之后可直接点选它作为关键帧颜色"),
                            GUILayout.Width(96f)))
                    {
                        m_Status = TimelineColorPalette.AddColor(m_WorkingColor);
                        m_Palette.AddRecent(m_WorkingColor);
                        GUI.FocusControl(null);
                        Repaint();
                    }
                }

                m_AutoAddToPalette = EditorGUILayout.ToggleLeft(
                    new GUIContent("调色轮盘取色后自动加入调色板", "关闭后取色只记入\u201c最近使用\u201d，需要手动点\u201c添加到调色板\u201d"),
                    m_AutoAddToPalette);

                if (GUILayout.Button(new GUIContent("调色轮盘…（Unity 默认取色器）",
                        "打开 Unity 内置的色轮 / 滑块取色窗口，拖动时实时同步到调色盘，关闭后按当前写入方式落地"),
                        GUILayout.Height(22f)))
                    OpenColorWheel();
            }
        }

        void DrawPaletteSection(ColorCurveProperty property)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField("调色板（左键取色 / 右键移除）", EditorStyles.boldLabel);
                    if (GUILayout.Button("恢复默认", GUILayout.Width(64f)))
                    {
                        m_Palette.ResetToDefaults();
                        GUI.FocusControl(null);
                    }
                }

                var columns = Mathf.Max(1, m_Palette.columns);
                var height = TimelineColorPalette.GridHeight(m_Palette.swatches.Count, columns);
                var rect = GUILayoutUtility.GetRect(10f, height, GUILayout.ExpandWidth(true));
                if (m_Palette.DrawSwatchGrid(rect, m_WorkingColor, out var picked))
                {
                    m_WorkingColor = picked;
                    m_Palette.AddRecent(picked);
                    if (m_ApplyOnPick)
                        ApplyFromPick();
                }

                if (m_Palette.recent.Count > 0)
                {
                    EditorGUILayout.LabelField("最近使用", EditorStyles.miniLabel);
                    var rowRect = GUILayoutUtility.GetRect(10f, TimelineColorPalette.SwatchSize, GUILayout.ExpandWidth(true));
                    if (DrawSwatchRow(rowRect, m_Palette.recent, m_WorkingColor, out var recentPicked))
                    {
                        m_WorkingColor = recentPicked;
                        if (m_ApplyOnPick)
                            ApplyFromPick();
                    }
                }
            }
        }

        bool DrawSwatchRow(Rect rect, List<Color> colors, Color current, out Color picked)
        {
            picked = default;
            var evt = Event.current;
            var clicked = false;

            for (int i = 0; i < colors.Count; i++)
            {
                var cell = new Rect(rect.x + i * (TimelineColorPalette.SwatchSize + TimelineColorPalette.SwatchSpacing), rect.y,
                    TimelineColorPalette.SwatchSize, TimelineColorPalette.SwatchSize);
                if (cell.xMax > rect.xMax)
                    break;

                EditorTextures.DrawCheckerboard(cell);
                var previous = GUI.color;
                GUI.color = colors[i];
                GUI.DrawTexture(cell, Texture2D.whiteTexture, ScaleMode.StretchToFill, false);
                GUI.color = previous;

                var hovered = cell.Contains(evt.mousePosition);
                EditorTextures.DrawBorder(cell, EditorTextures.IsSameColor(colors[i], current)
                    ? new Color(1f, 0.85f, 0.2f, 1f)
                    : hovered ? new Color(1f, 1f, 1f, 0.65f) : new Color(0f, 0f, 0f, 0.45f));

                if (hovered && evt.type == EventType.MouseDown && evt.button == 0)
                {
                    picked = colors[i];
                    clicked = true;
                    evt.Use();
                    break;
                }
            }

            return clicked;
        }

        void DrawCurveSection(ColorCurveProperty property)
        {
            property.GetTimeRange(out var start, out var end);
            var time = GetTime();
            SyncJumpFrame(property);

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField("颜色曲线预览", EditorStyles.boldLabel);
                    GUILayout.FlexibleSpace();
                    EditorGUILayout.LabelField(string.Format("{0:0.000}s ~ {1:0.000}s", start, end), EditorStyles.miniLabel, GUILayout.Width(110f));
                }

                var barRect = GUILayoutUtility.GetRect(10f, ColorCurveBar.Height, GUILayout.ExpandWidth(true));
                ColorCurveBar.Draw(barRect, property, start, end);
                DrawKeyTicks(barRect, property, start, end, time);

                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField("播放头", GUILayout.Width(46f));
                    EditorGUILayout.LabelField(string.Format("{0:0.000}s（第 {1:0} 帧）", TimelineEditorBridge.GetPlayhead(), TimelineEditorBridge.GetPlayhead() * property.target.frameRate),
                        EditorStyles.miniLabel);
                    if (GUILayout.Button("跳到播放头", GUILayout.Width(76f)))
                    {
                        m_UseManualTime = false;
                        m_WorkingColor = property.Evaluate(TimelineEditorBridge.GetPlayhead());
                        JumpFrameFieldTo(TimelineEditorBridge.GetPlayhead(), property);
                        GUI.FocusControl(null);
                    }
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(new GUIContent("跳转帧", "输入帧号后按回车或点击\u201c跳转\u201d：移动播放头并读取该帧颜色"), GUILayout.Width(46f));

                    // 必须在绘制输入框之前检查回车：文本编辑器会先消费掉 KeyDown 事件，
                    // 在这里检测才能拿到"回车跳转"。
                    var enterPressed = IsJumpFrameEnterPressed();

                    GUI.SetNextControlName(k_JumpFrameControl);
                    var frame = EditorGUILayout.IntField(m_JumpFrame, GUILayout.Width(64f));
                    if (frame != m_JumpFrame)
                        m_JumpFrame = frame;

                    if (GUILayout.Button(new GUIContent("跳转", "把播放头移动到该帧"), GUILayout.Width(52f)) || enterPressed)
                        JumpToFrame(m_JumpFrame);

                    if (GUILayout.Button(new GUIContent("对称", "跳到与当前帧关于 720 对称的帧（x → 1440 - x）"), GUILayout.Width(40f)))
                        JumpToFrame(k_MirrorCenterFrame * 2 - m_JumpFrame);

                    if (GUILayout.Button(new GUIContent("◀", "上一帧"), EditorStyles.miniButtonLeft, GUILayout.Width(24f)))
                        JumpToFrame(m_JumpFrame - 1);

                    if (GUILayout.Button(new GUIContent("▶", "下一帧"), EditorStyles.miniButtonRight, GUILayout.Width(24f)))
                        JumpToFrame(m_JumpFrame + 1);

                    GUILayout.FlexibleSpace();
                    EditorGUILayout.LabelField(string.Format("共 {0:0} 帧", property.target.duration * property.target.frameRate),
                        EditorStyles.miniLabel, GUILayout.Width(62f));
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    m_FollowPlayhead = EditorGUILayout.ToggleLeft(new GUIContent("跟随播放头取色", "播放头移动时自动读取该时间点的颜色到调色盘"), m_FollowPlayhead, GUILayout.Width(140f));
                    m_ShowKeyList = EditorGUILayout.ToggleLeft("显示关键帧列表", m_ShowKeyList, GUILayout.Width(120f));
                }

                if (m_ShowKeyList)
                    DrawKeyList(property);
            }
        }

        void DrawKeyTicks(Rect barRect, ColorCurveProperty property, double start, double end, double time)
        {
            foreach (var keyTime in m_KeyTimes)
            {
                var x = ColorCurveBar.TimeToX(barRect, keyTime, start, end);
                if (x < barRect.x || x > barRect.xMax)
                    continue;

                var unified = property.GetChannelKeyPresence(keyTime).All(present => present);
                var color = unified ? new Color(1f, 1f, 1f, 0.9f) : new Color(1f, 0.75f, 0.2f, 0.9f);
                EditorGUI.DrawRect(new Rect(x - 1f, barRect.yMax - 8f, 3f, 8f), color);
            }

            var playheadX = ColorCurveBar.TimeToX(barRect, time, start, end);
            if (playheadX >= barRect.x && playheadX <= barRect.xMax)
                EditorGUI.DrawRect(new Rect(playheadX - 1f, barRect.y, 2f, barRect.height), new Color(0.95f, 0.25f, 0.25f, 0.95f));

            var evt = Event.current;
            if (evt.type != EventType.MouseDown || !barRect.Contains(evt.mousePosition))
                return;

            var ratio = Mathf.Clamp01((evt.mousePosition.x - barRect.x) / Mathf.Max(1f, barRect.width));
            var pickedTime = start + (end - start) * ratio;
            SetTime(pickedTime);
            m_WorkingColor = property.Evaluate(pickedTime);

            if (evt.control || evt.command)
                WriteKeyAtTime();

            evt.Use();
            GUI.FocusControl(null);
        }

        void DrawKeyList(ColorCurveProperty property)
        {
            if (m_KeyTimes.Count == 0)
            {
                EditorGUILayout.LabelField("暂无关键帧。写入第一个颜色键后即可在这里统一调整。", EditorStyles.miniLabel);
                return;
            }

            EditorGUILayout.LabelField(string.Format("关键帧（{0} 个时间点）", m_KeyTimes.Count), EditorStyles.miniLabel);

            var height = Mathf.Min(150f, m_KeyTimes.Count * 22f + 4f);

            // 滚动位置必须保存在字段里：如果每帧都传入 Vector2.zero，
            // 拖动滚动条后会在下一次 OnGUI 立刻被重置回顶端。
            m_KeyListScroll = EditorGUILayout.BeginScrollView(m_KeyListScroll, GUILayout.Height(height));
            try
            {
                var frameRate = property.target.frameRate;

                foreach (var keyTime in m_KeyTimes.ToArray())
                {
                    var keyColor = property.Evaluate(keyTime);
                    var frame = ColorCurveOperations.TimeToFrame(keyTime, frameRate);
                    var expanded = IsKeyExpanded(keyTime);

                    using (new EditorGUILayout.HorizontalScope())
                    {
                        if (GUILayout.Button(new GUIContent(expanded ? "▾" : "▸", "展开 / 收起该关键帧的左右切线设置"),
                                EditorStyles.miniButton, GUILayout.Width(18f)))
                            ToggleKeyExpanded(keyTime);

                        if (GUILayout.Button(new GUIContent(keyTime.ToString("0.000") + "s",
                                string.Format("第 {0} 帧（{1:0.000}s）：点击可把写入时间与颜色切到该关键帧", frame, keyTime)),
                                EditorStyles.miniButton, GUILayout.Width(62f)))
                        {
                            SetTime(keyTime);
                            m_WorkingColor = keyColor;
                            GUI.FocusControl(null);
                        }

                        EditorGUILayout.LabelField(new GUIContent("帧 " + frame, "该关键帧所在的帧号"),
                            EditorStyles.miniLabel, GUILayout.Width(46f));

                        var swatchRect = GUILayoutUtility.GetRect(TimelineColorPalette.SwatchSize, TimelineColorPalette.SwatchSize, GUILayout.Width(TimelineColorPalette.SwatchSize));
                        EditorTextures.DrawCheckerboard(swatchRect);
                        var previous = GUI.color;
                        GUI.color = keyColor;
                        GUI.DrawTexture(swatchRect, Texture2D.whiteTexture, ScaleMode.StretchToFill, false);
                        GUI.color = previous;
                        EditorTextures.DrawBorder(swatchRect, new Color(0f, 0f, 0f, 0.45f));

                        var presence = property.GetChannelKeyPresence(keyTime);
                        var channelText = string.Join("", Enumerable.Range(0, 4).Select(i => presence[i] ? ColorCurveProperty.ChannelLabel[i] : ColorCurveProperty.ChannelLabel[i].ToLowerInvariant()));
                        EditorGUILayout.LabelField(new GUIContent(channelText, "大写=该通道在此时间点有关键帧；小写=该通道缺失"),
                            EditorStyles.miniLabel, GUILayout.Width(34f));

                        EditorGUILayout.LabelField(new GUIContent("#" + ColorUtility.ToHtmlStringRGBA(keyColor), "该关键帧的颜色"),
                            EditorStyles.miniLabel, GUILayout.Width(68f));

                        if (GUILayout.Button(new GUIContent("写入", "把调色盘当前颜色写入这个时间点"), EditorStyles.miniButton, GUILayout.Width(36f)))
                        {
                            ColorCurveOperations.SetColorKey(property, keyTime, m_WorkingColor, m_Channels, m_Interpolation);
                            m_Status = "已更新关键帧 " + keyTime.ToString("0.000") + "s。";
                            RefreshKeys();
                            GUI.FocusControl(null);
                        }

                        if (GUILayout.Button(new GUIContent("删除", "删除这个时间点上的关键帧"), EditorStyles.miniButton, GUILayout.Width(36f)))
                        {
                            ColorCurveOperations.RemoveKeysAt(property, keyTime, m_Channels);
                            m_Status = "已删除关键帧 " + keyTime.ToString("0.000") + "s。";
                            RefreshKeys();
                            GUI.FocusControl(null);
                        }
                    }

                    if (expanded)
                        DrawKeyTangents(property, keyTime, frameRate);
                }
            }
            finally
            {
                EditorGUILayout.EndScrollView();
            }
        }

        #region 关键帧切线

        bool IsKeyExpanded(float time)
        {
            for (int i = 0; i < m_ExpandedKeyTimes.Count; i++)
            {
                if (Mathf.Abs(m_ExpandedKeyTimes[i] - time) < 0.0001f)
                    return true;
            }
            return false;
        }

        void ToggleKeyExpanded(float time)
        {
            for (int i = 0; i < m_ExpandedKeyTimes.Count; i++)
            {
                if (Mathf.Abs(m_ExpandedKeyTimes[i] - time) >= 0.0001f)
                    continue;

                m_ExpandedKeyTimes.RemoveAt(i);
                return;
            }

            m_ExpandedKeyTimes.Add(time);
        }

        /// <summary>某个关键帧的切线模式与左右切线类型（对启用的通道统一生效）。</summary>
        void DrawKeyTangents(ColorCurveProperty property, float keyTime, float frameRate)
        {
            var frame = ColorCurveOperations.TimeToFrame(keyTime, frameRate);

            if (!ColorCurveOperations.TryGetKeyMode(property, keyTime, m_Channels,
                    out var mode, out var leftMode, out var rightMode))
            {
                EditorGUILayout.LabelField(string.Format("第 {0} 帧的启用通道上没有关键帧，无法编辑切线。", frame), EditorStyles.miniLabel);
                return;
            }

            using (new EditorGUI.IndentLevelScope())
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(new GUIContent("模式", "切线模式：Clamped Auto / Auto / Free Smooth / Flat / Broken"),
                        GUILayout.Width(48f));

                    var modeIndex = (int)mode;
                    var newMode = EditorGUILayout.Popup(modeIndex, ColorCurveOperations.KeyModeLabels);

                    if (newMode != modeIndex)
                    {
                        ColorCurveOperations.SetKeyMode(property, keyTime, m_Channels, (KeyTangentMode)newMode);
                        m_Status = string.Format("已把第 {0} 帧的切线模式设为 {1}。", frame, ColorCurveOperations.KeyModeLabels[newMode]);
                        Repaint();
                        GUI.FocusControl(null);
                    }
                }

                // 左右切线类型只有在 Broken 模式下才有意义，其它模式下一律禁用。
                using (new EditorGUI.DisabledScope(mode != KeyTangentMode.Broken))
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        // 标签与下拉框分开布局：如果把它们挤在同一个受限宽度里，
                        // 受 EditorGUIUtility.labelWidth 影响，下拉框会被压窄到看不见当前类型。
                        EditorGUILayout.LabelField(new GUIContent("左切线", "Broken 模式下的左侧（入场）切线类型，对所有启用通道统一设置"),
                            GUILayout.Width(48f));

                        var leftIndex = ColorCurveOperations.TangentModeIndex(leftMode);
                        var newLeft = EditorGUILayout.Popup(leftIndex, ColorCurveOperations.TangentModeLabels);

                        if (newLeft != leftIndex)
                        {
                            ColorCurveOperations.SetKeyTangentMode(property, keyTime, m_Channels, true, ColorCurveOperations.TangentModes[newLeft]);
                            m_Status = string.Format("已把第 {0} 帧的左切线设为 {1}。", frame, ColorCurveOperations.TangentModeLabels[newLeft]);
                            Repaint();
                            GUI.FocusControl(null);
                        }
                    }

                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUILayout.LabelField(new GUIContent("右切线", "Broken 模式下的右侧（出场）切线类型，对所有启用通道统一设置"),
                            GUILayout.Width(48f));

                        var rightIndex = ColorCurveOperations.TangentModeIndex(rightMode);
                        var newRight = EditorGUILayout.Popup(rightIndex, ColorCurveOperations.TangentModeLabels);

                        if (newRight != rightIndex)
                        {
                            ColorCurveOperations.SetKeyTangentMode(property, keyTime, m_Channels, false, ColorCurveOperations.TangentModes[newRight]);
                            m_Status = string.Format("已把第 {0} 帧的右切线设为 {1}。", frame, ColorCurveOperations.TangentModeLabels[newRight]);
                            Repaint();
                            GUI.FocusControl(null);
                        }
                    }
                }
            }
        }

        #endregion

        void DrawOperationSection(ColorCurveProperty property)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("统一 RGBA 操作", EditorStyles.boldLabel);

                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField("写入时间", GUILayout.Width(60f));
                    m_UseManualTime = EditorGUILayout.ToggleLeft(new GUIContent("自定义", "不勾选时使用 Timeline 播放头时间"), m_UseManualTime, GUILayout.Width(70f));
                    using (new EditorGUI.DisabledScope(!m_UseManualTime))
                        m_ManualTime = EditorGUILayout.DoubleField(m_ManualTime, GUILayout.Width(70f));
                    if (GUILayout.Button("取播放头", GUILayout.Width(64f)))
                    {
                        m_ManualTime = TimelineEditorBridge.GetPlayhead();
                        GUI.FocusControl(null);
                    }
                }

                m_ApplyMode = (ApplyMode)EditorGUILayout.Popup(
                    new GUIContent("选色写入方式", "调色板取色后，是把颜色写到当前时间点，还是统一写到该属性的所有关键帧"),
                    (int)m_ApplyMode, k_ApplyModeLabels);
                m_Interpolation = (ColorKeyInterpolation)EditorGUILayout.Popup(
                    new GUIContent("新增关键帧插值", "平滑 = 两侧限制自动（Clamped Auto，默认）；线性；恒定（阶跃）"),
                    (int)m_Interpolation, ColorCurveOperations.InterpolationLabels);

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button(new GUIContent("写入当前时间点（RGBA）", "把调色盘颜色作为一个整体写入 R/G/B/A 四条曲线")))
                        WriteKeyAtTime();

                    if (GUILayout.Button(new GUIContent("应用到全部关键帧", "把调色盘颜色统一写入该属性的所有关键帧")))
                        WriteAllKeys();
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button(new GUIContent("统一 RGBA 关键帧", "把四个通道按关键帧时间的并集重新采样，使 RGBA 始终成组出现"), GUILayout.Width(140f)))
                    {
                        ColorCurveOperations.AlignChannels(property, m_Channels);
                        m_Status = "已对齐 RGBA 关键帧。";
                        RefreshKeys();
                        GUI.FocusControl(null);
                    }

                    if (GUILayout.Button("删除该时间点颜色键"))
                    {
                        var removed = ColorCurveOperations.RemoveKeysAt(property, GetTime(), m_Channels);
                        m_Status = removed > 0 ? "已删除 " + removed + " 个通道关键帧。" : "该时间点没有关键帧。";
                        RefreshKeys();
                        GUI.FocusControl(null);
                    }
                }

                GUILayout.Space(4f);
                EditorGUILayout.LabelField("渐变填充", EditorStyles.boldLabel);

                using (new EditorGUILayout.HorizontalScope())
                {
                    m_GradientStart = EditorGUILayout.DoubleField("起始时间", m_GradientStart);
                    if (GUILayout.Button("曲线范围", GUILayout.Width(64f)))
                    {
                        property.GetTimeRange(out var start, out var end);
                        m_GradientStart = start;
                        m_GradientEnd = end;
                        GUI.FocusControl(null);
                    }
                }

                m_GradientEnd = EditorGUILayout.DoubleField("结束时间", m_GradientEnd);
                m_GradientFrom = EditorGUILayout.ColorField(new GUIContent("起始颜色"), m_GradientFrom, true, true, m_Hdr);
                m_GradientTo = EditorGUILayout.ColorField(new GUIContent("结束颜色"), m_GradientTo, true, true, m_Hdr);
                m_GradientSteps = EditorGUILayout.IntSlider("生成关键帧数", m_GradientSteps, 2, 32);
                m_GradientUseExistingKeys = EditorGUILayout.ToggleLeft("沿用区间内已有时间点", m_GradientUseExistingKeys);

                if (GUILayout.Button("生成渐变颜色键"))
                {
                    ColorCurveOperations.ApplyGradient(property, m_GradientStart, m_GradientEnd, m_GradientFrom, m_GradientTo,
                        m_GradientSteps, m_Channels, m_Interpolation, m_GradientUseExistingKeys);
                    m_Status = "已生成渐变颜色键。";
                    RefreshKeys();
                    GUI.FocusControl(null);
                }

                GUILayout.Space(4f);

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button(new GUIContent("写入静态颜色", "未生成曲线时使用：直接修改行为模板上的颜色值"), GUILayout.Width(120f)))
                    {
                        if (property.SetStaticColor(m_WorkingColor))
                        {
                            TimelineEditorBridge.Refresh();
                            m_Status = "已写入静态颜色（未生成曲线时生效）。";
                        }
                        else
                        {
                            m_Status = "写入静态颜色失败：找不到对应的颜色字段。";
                        }
                        GUI.FocusControl(null);
                    }

                    if (GUILayout.Button(new GUIContent("清除该颜色的曲线", "删除 R/G/B/A 四条曲线，恢复为静态颜色"), GUILayout.Width(140f)))
                    {
                        if (ColorCurveOperations.ClearCurves(property))
                        {
                            m_Status = "已清除该颜色的所有曲线。";
                            RefreshKeys();
                        }
                        GUI.FocusControl(null);
                    }
                }
            }
        }

        void DrawStatus()
        {
            if (string.IsNullOrEmpty(m_Status))
                return;

            EditorGUILayout.HelpBox(m_Status, MessageType.None);
        }

        #endregion

        #region 写入

        /// <summary>调色板取色后按当前"选色写入方式"写回曲线。</summary>
        void ApplyFromPick()
        {
            if (m_ApplyMode == ApplyMode.AllKeys)
                WriteAllKeys();
            else
                WriteKeyAtTime();
        }

        /// <summary>把调色盘颜色作为一个整体写入四条曲线（当前写入时间点）。</summary>
        void WriteKeyAtTime()
        {
            var property = CurrentProperty;
            if (property == null)
                return;

            var time = GetTime();
            if (ColorCurveOperations.SetColorKey(property, time, m_WorkingColor, m_Channels, m_Interpolation))
            {
                m_Status = string.Format("已在 {0:0.000}s 写入颜色 #{1}（通道：{2}）。",
                    ColorCurveOperations.SnapTime(time, property.target.frameRate),
                    ColorUtility.ToHtmlStringRGBA(m_WorkingColor),
                    ChannelSummary());
            }
            else
            {
                m_Status = "写入失败：请至少启用一个通道。";
            }

            RefreshKeys();
            GUI.FocusControl(null);
        }

        /// <summary>把调色盘颜色统一写入该属性的全部关键帧。</summary>
        void WriteAllKeys()
        {
            var property = CurrentProperty;
            if (property == null)
                return;

            if (ColorCurveOperations.SetAllKeys(property, m_WorkingColor, m_Channels))
                m_Status = "已把当前颜色应用到全部关键帧。";
            else
                m_Status = "该属性还没有关键帧，请先写入第一个颜色键。";

            RefreshKeys();
            GUI.FocusControl(null);
        }

        string ChannelSummary()
        {
            return string.Join("/", Enumerable.Range(0, 4).Where(i => m_Channels[i]).Select(i => ColorCurveProperty.ChannelLabel[i]));
        }

        #endregion
    }
}
