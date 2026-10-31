using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace TimelineColorEditor
{
    /// <summary>
    /// 对 Unity 内置取色器（编辑器窗口 ColorPicker，即"调色轮盘"）的最小反射封装。
    /// <para/>
    /// Unity 2022.3 共有三个入口（反汇编自 UnityEditor.CoreModule 元数据）：
    /// <code>
    /// public  static void Show(GUIView view, Color color, bool showAlpha, bool hdr)                              // 无回调
    /// public  static void Show(Action&lt;Color&gt; onColorChanged, Color color, bool showAlpha, bool hdr)            // 纯回调（本类使用）
    /// private static void Show(GUIView view, Action&lt;Color&gt; onColorChanged, Color color, bool showAlpha, bool hdr)
    /// </code>
    /// 注意：带 view 的重载在颜色变化时会先给 view 发 "ColorPickerChanged" 命令并在需要时调用
    /// <c>GUIUtility.ExitGUI()</c>（内部实现里该调用在回调之前，会直接中断执行），因此委托视图非空时
    /// <c>onColorChanged</c> 可能永远收不到通知。本类因此固定走"纯回调"重载（view 为 null），
    /// 另外用 <c>ColorPicker.color</c> 轮询作为兜底；<c>ColorPicker.visible</c> 用于判断轮盘是否仍打开。
    /// </summary>
    public static class UnityColorWheel
    {
        static bool s_Initialized;
        static bool s_Available;
        static bool s_CallbackOnlyOverload;
        static MethodInfo s_ShowMethod;
        static PropertyInfo s_CurrentViewProperty;
        static PropertyInfo s_VisibleProperty;
        static PropertyInfo s_ColorProperty;

        static readonly Action<Color> s_Callback = OnPickerColorChanged;

        /// <summary>用户在调色轮盘中改变颜色时触发（拖动过程中会连续触发）。</summary>
        public static event Action<Color> ColorChanged;

        /// <summary>内置调色轮盘是否可用。</summary>
        public static bool IsAvailable
        {
            get
            {
                Initialize();
                return s_Available;
            }
        }

        /// <summary>调色轮盘当前是否处于打开状态。</summary>
        public static bool IsOpen
        {
            get
            {
                Initialize();
                if (s_VisibleProperty == null)
                    return false;

                try
                {
                    return s_VisibleProperty.GetValue(null) is bool visible && visible;
                }
                catch
                {
                    return false;
                }
            }
        }

        /// <summary>读取调色轮盘当前颜色（拖动过程中即为实时值）。</summary>
        public static bool TryGetColor(out Color color)
        {
            color = default;
            Initialize();
            if (s_ColorProperty == null)
                return false;

            try
            {
                if (s_ColorProperty.GetValue(null) is Color value)
                {
                    color = value;
                    return true;
                }
            }
            catch
            {
                // 轮盘实例不可用时忽略。
            }

            return false;
        }

        /// <summary>用指定颜色打开 Unity 默认调色轮盘。</summary>
        public static bool Show(Color color, bool showAlpha, bool hdr)
        {
            Initialize();
            if (!s_Available)
                return false;

            try
            {
                if (s_CallbackOnlyOverload)
                {
                    // Show(Action<Color>, Color, bool, bool)：delegate view 为 null，
                    // 因此 OnColorChanged 里不会因 GUIUtility.ExitGUI() 而跳过回调。
                    s_ShowMethod.Invoke(null, new object[] { s_Callback, color, showAlpha, hdr });
                }
                else
                {
                    var view = s_CurrentViewProperty != null ? s_CurrentViewProperty.GetValue(null) : null;
                    s_ShowMethod.Invoke(null, new object[] { view, s_Callback, color, showAlpha, hdr });
                }

                return true;
            }
            catch (Exception e)
            {
                // 反射入口在其它 Unity 版本可能不同：静默降级，不再重复尝试。
                s_Available = false;
                Debug.LogWarning("[TrackColorEditor] 无法打开 Unity 内置调色轮盘：" + e.Message);
                return false;
            }
        }

        static void OnPickerColorChanged(Color color)
        {
            //Debug.Log("hhh");
            ColorChanged?.Invoke(color);
        }

        static void Initialize()
        {
            if (s_Initialized)
                return;
            s_Initialized = true;

            var picker = FindType("UnityEditor.ColorPicker");
            if (picker == null)
                return;

            const BindingFlags flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

            // 首选"仅回调"重载：Show(Action<Color>, Color, bool, bool)。
            // Unity 的 ColorPicker.OnColorChanged(bool exitGUI) 逻辑是：
            //     if (m_DelegateView != null) { ...SendEvent("ColorPickerChanged"); if (exitGUI) GUIUtility.ExitGUI(); }
            //     if (m_ColorChangedCallback != null) m_ColorChangedCallback(color);
            // 传入 delegate view 时 ExitGUI() 会抛异常打断执行，回调永远不触发；
            // 只有 delegate view 为 null（该重载内部传 null）时回调才可靠。
            s_ShowMethod = picker.GetMethod(
                "Show", flags, null,
                new[] { typeof(Action<Color>), typeof(Color), typeof(bool), typeof(bool) },
                null);

            s_CallbackOnlyOverload = s_ShowMethod != null;

            if (s_ShowMethod == null)
            {
                var view = FindType("UnityEditor.GUIView");
                if (view == null)
                    return;

                s_ShowMethod = picker.GetMethod(
                    "Show", flags, null,
                    new[] { view, typeof(Action<Color>), typeof(Color), typeof(bool), typeof(bool) },
                    null);

                if (s_ShowMethod == null)
                    return;

                s_CurrentViewProperty = view.GetProperty("current", flags);
            }

            s_VisibleProperty = picker.GetProperty("visible", flags);
            s_ColorProperty = picker.GetProperty("color", flags);

            s_Available = true;
        }

        static Type FindType(string fullName)
        {
            // 内置取色器位于 UnityEditor.CoreModule（EditorWindow / EditorGUI 的同一模块）。
            var type = typeof(EditorWindow).Assembly.GetType(fullName)
                       ?? typeof(EditorGUI).Assembly.GetType(fullName)
                       ?? Type.GetType(fullName + ", UnityEditor");

            if (type != null)
                return type;

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                type = assembly.GetType(fullName);
                if (type != null)
                    return type;
            }

            return null;
        }
    }
}
