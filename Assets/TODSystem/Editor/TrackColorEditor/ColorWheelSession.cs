using System;
using UnityEditor;
using UnityEngine;

namespace TimelineColorEditor
{
    /// <summary>一次取色会话的写入设置。</summary>
    public sealed class ColorWheelSettings
    {
        /// <summary>打开轮盘时的初始颜色。</summary>
        public Color color = Color.white;

        public bool showAlpha = true;
        public bool hdr;

        /// <summary>写回目标；为 null 时只取色不写曲线。</summary>
        public ColorCurveProperty property;

        /// <summary>true：写回全部关键帧；false：写回 <see cref="time"/> 时间点。</summary>
        public bool applyAllKeys;

        public double time;
        public bool[] channels = { true, true, true, true };
        public ColorKeyInterpolation interpolation = ColorKeyInterpolation.Auto;

        /// <summary>关闭轮盘后是否自动写回曲线。</summary>
        public bool autoApply = true;

        /// <summary>是否把取到的颜色记入"最近使用"。</summary>
        public bool addRecent = true;

        /// <summary>是否把取到的颜色作为新色块加入调色板。</summary>
        public bool addToPalette;
    }

    /// <summary>
    /// 一次"调色轮盘取色会话"。
    /// <para/>
    /// 为什么必须是静态会话，而不是挂在窗口实例上：
    /// 右键呼出的调色板是 <see cref="EditorWindow.ShowAsDropDown"/> 生成的下拉窗口（失焦即关），
    /// 而 Unity 内置调色轮盘是 aux window，打开时会把焦点抢走 —— 于是窗口在轮盘刚打开时就关闭并执行
    /// OnDisable，事件订阅随之被移除，之后轮盘里的颜色变化便再也送不到（表现为 OnPickerColorChanged 执行、
    /// 但 OnWheelColorChanged 不执行）。
    /// 会话与窗口解耦后，即使触发取色的窗口已经关闭，颜色同步与最终写回依然正常工作。
    /// </summary>
    public static class ColorWheelSession
    {
        /// <summary>取色过程中的颜色变化（供仍存活的视图刷新显示）。</summary>
        public static event Action<Color> ColorChanged;

        /// <summary>会话结束：最终颜色 + 状态描述。</summary>
        public static event Action<Color, string> Finished;

        static bool s_Active;
        static bool s_SeenOpen;
        static double s_OpenedAt;
        static Color s_Color;
        static ColorWheelSettings s_Settings;

        /// <summary>是否正在取色。</summary>
        public static bool IsActive
        {
            get { return s_Active; }
        }

        /// <summary>会话当前颜色。</summary>
        public static Color CurrentColor
        {
            get { return s_Color; }
        }

        /// <summary>最近一次会话的状态描述（窗口关闭后仍可读取）。</summary>
        public static string LastMessage { get; private set; } = string.Empty;

        /// <summary>开始一次取色会话。</summary>
        public static bool Open(ColorWheelSettings settings)
        {
            if (settings == null)
                return false;

            Close(false);

            s_Active = true;
            s_SeenOpen = false;
            s_OpenedAt = EditorApplication.timeSinceStartup;
            s_Settings = settings;
            s_Color = settings.color;

            UnityColorWheel.ColorChanged += OnWheelColorChanged;
            EditorApplication.update += OnEditorUpdate;

            if (!UnityColorWheel.Show(s_Color, settings.showAlpha, settings.hdr))
            {
                Close(false);
                LastMessage = "未能打开 Unity 默认调色轮盘，可直接点击色块：Unity 会用同一个取色窗口。";
                return false;
            }

            LastMessage = string.Empty;
            return true;
        }

        /// <summary>结束会话；apply 为 true 时按会话设置写回曲线。</summary>
        public static void Close(bool apply)
        {
            if (!s_Active)
                return;

            s_Active = false;
            UnityColorWheel.ColorChanged -= OnWheelColorChanged;
            EditorApplication.update -= OnEditorUpdate;

            var settings = s_Settings;
            s_Settings = null;

            var color = s_Color;
            string message;

            if (!apply || settings == null)
            {
                message = "已结束调色轮盘取色（未写回）。";
            }
            else
            {
                if (settings.addRecent)
                    TimelineColorPalette.AddRecentGlobal(color);

                if (settings.addToPalette)
                    TimelineColorPalette.AddColor(color);

                var property = settings.property;
                if (property == null)
                {
                    message = "调色轮盘取色完成：" + ToHex(color);
                }
                else if (!settings.autoApply)
                {
                    message = "调色轮盘取色完成：" + ToHex(color) + "（未自动写入）。";
                }
                else if (settings.applyAllKeys)
                {
                    message = ColorCurveOperations.SetAllKeys(property, color, settings.channels)
                        ? "调色轮盘已写入全部关键帧：" + ToHex(color) + "。"
                        : "调色轮盘取色完成，但该属性还没有关键帧。";
                }
                else
                {
                    var written = ColorCurveOperations.SetColorKey(property, settings.time, color, settings.channels, settings.interpolation);
                    message = written
                        ? string.Format("调色轮盘已在 {0:0.000}s 写入 {1}。",
                            ColorCurveOperations.SnapTime(settings.time, property.target.frameRate),
                            ToHex(color))
                        : "调色轮盘取色完成，但没有可写入的通道。";
                }
            }

            LastMessage = message;
            Finished?.Invoke(color, message);
        }

        static string ToHex(Color color)
        {
            return "#" + ColorUtility.ToHtmlStringRGBA(color);
        }

        static void OnWheelColorChanged(Color color)
        {
            // 回调能到达说明轮盘确实在工作：据此判断"已经打开过"。
            s_SeenOpen = true;
            s_Color = color;
            ColorChanged?.Invoke(color);
        }

        static void OnEditorUpdate()
        {
            if (!s_Active)
                return;

            // 兜底：直接轮询轮盘的当前颜色，不依赖回调是否到达。
            if (UnityColorWheel.TryGetColor(out var color) && !EditorTextures.IsSameColor(color, s_Color))
            {
                s_Color = color;
                ColorChanged?.Invoke(color);
            }

            if (UnityColorWheel.IsOpen)
            {
                s_SeenOpen = true;
                return;
            }

            if (s_SeenOpen)
            {
                // 轮盘已关闭：写回最终颜色。
                Close(true);
                return;
            }

            // 轮盘刚打开时 visible 可能还没生效；若长时间既读不到打开状态、也收不到颜色变化，则结束会话。
            if (EditorApplication.timeSinceStartup - s_OpenedAt > 5d)
                Close(false);
        }
    }
}
