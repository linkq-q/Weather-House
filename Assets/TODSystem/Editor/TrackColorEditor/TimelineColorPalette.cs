using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityObject = UnityEngine.Object;

namespace TimelineColorEditor
{
    /// <summary>编辑器用的小贴图与绘制工具（棋盘格用于表现透明度）。</summary>
    public static class EditorTextures
    {
        static readonly Color s_Dark = new Color(0.26f, 0.26f, 0.26f, 1f);
        static readonly Color s_Light = new Color(0.36f, 0.36f, 0.36f, 1f);
        static Texture2D s_Checker;

        public static Texture2D Checker
        {
            get
            {
                if (s_Checker == null)
                {
                    s_Checker = new Texture2D(8, 8, TextureFormat.RGBA32, false)
                    {
                        hideFlags = HideFlags.HideAndDontSave,
                        wrapMode = TextureWrapMode.Repeat,
                        filterMode = FilterMode.Point
                    };

                    for (int y = 0; y < 8; y++)
                    {
                        for (int x = 0; x < 8; x++)
                            s_Checker.SetPixel(x, y, (x / 4 + y / 4) % 2 == 0 ? s_Dark : s_Light);
                    }
                    s_Checker.Apply(false, false);
                }
                return s_Checker;
            }
        }

        /// <summary>绘制棋盘格背景（表现颜色透明度）。</summary>
        public static void DrawCheckerboard(Rect rect)
        {
            GUI.DrawTexture(rect, Checker, ScaleMode.StretchToFill, false);
        }

        public static void DrawBorder(Rect rect, Color color)
        {
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, 1f), color);
            EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - 1f, rect.width, 1f), color);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, 1f, rect.height), color);
            EditorGUI.DrawRect(new Rect(rect.xMax - 1f, rect.y, 1f, rect.height), color);
        }

        public static bool IsSameColor(Color a, Color b, float tolerance = 0.004f)
        {
            return Mathf.Abs(a.r - b.r) < tolerance
                   && Mathf.Abs(a.g - b.g) < tolerance
                   && Mathf.Abs(a.b - b.b) < tolerance
                   && Mathf.Abs(a.a - b.a) < tolerance;
        }
    }

    /// <summary>
    /// 调色板：内置色板 + 收藏色 + 最近使用色。
    /// 收藏色与最近使用通过 EditorPrefs 持久化，换工程后依旧保留。
    /// </summary>
    public sealed class TimelineColorPalette
    {
        const string kPrefKey = "TimelineColorEditor.Palette.v1";
        const int kMaxRecent = 8;
        public const int kMaxSwatches = 64;

        public const float SwatchSize = 18f;
        public const float SwatchSpacing = 2f;

        public readonly List<Color> swatches = new List<Color>();
        public readonly List<Color> recent = new List<Color>();

        public int columns = 8;

        static TimelineColorPalette s_Shared;

        /// <summary>
        /// 全局共享的调色板。
        /// 窗口、右键弹窗、取色会话都必须通过它读写，否则会出现"在某处加了色块、另一处看不到"的多份副本问题。
        /// </summary>
        public static TimelineColorPalette Shared
        {
            get
            {
                if (s_Shared == null)
                    s_Shared = Load();
                return s_Shared;
            }
        }

        /// <summary>调色板内容发生变化（供所有打开的界面立即刷新）。</summary>
        public static event Action Changed;

        public TimelineColorPalette()
        {
            EnsureDefaults();
        }

        #region 持久化

        public static TimelineColorPalette Load()
        {
            var palette = new TimelineColorPalette();
            var data = EditorPrefs.GetString(kPrefKey, string.Empty);
            if (string.IsNullOrEmpty(data))
                return palette;

            var parts = data.Split('|');
            if (parts.Length >= 2)
            {
                var saved = ParseColors(parts[0]);
                var savedRecent = ParseColors(parts[1]);
                if (saved.Count > 0)
                {
                    palette.swatches.Clear();
                    palette.swatches.AddRange(saved);
                }
                if (savedRecent.Count > 0)
                {
                    palette.recent.Clear();
                    palette.recent.AddRange(savedRecent);
                }
            }

            if (parts.Length >= 3 && int.TryParse(parts[2], out var cols) && cols > 0)
                palette.columns = cols;

            return palette;
        }

        public void Save()
        {
            var data = ColorsToString(swatches) + "|" + ColorsToString(recent) + "|" + columns;
            EditorPrefs.SetString(kPrefKey, data);
            Changed?.Invoke();
        }

        static string ColorsToString(IEnumerable<Color> colors)
        {
            return string.Join(";", colors.Select(ColorUtility.ToHtmlStringRGBA));
        }

        static List<Color> ParseColors(string text)
        {
            var result = new List<Color>();
            if (string.IsNullOrEmpty(text))
                return result;

            foreach (var token in text.Split(';'))
            {
                if (string.IsNullOrEmpty(token))
                    continue;
                if (ColorUtility.TryParseHtmlString("#" + token, out var color))
                    result.Add(color);
            }
            return result;
        }

        #endregion

        /// <summary>恢复为内置色板。</summary>
        public void ResetToDefaults()
        {
            swatches.Clear();
            EnsureDefaults();
            Save();
        }

        void EnsureDefaults()
        {
            if (swatches.Count > 0)
                return;

            // 灰度行
            for (int i = 0; i < 8; i++)
            {
                var v = i / 7f;
                swatches.Add(new Color(v, v, v, 1f));
            }

            // 色相行（多档饱和度 / 明度）
            float[] saturations = { 1f, 0.78f, 0.55f, 0.32f };
            float[] values = { 1f, 0.92f, 0.78f, 0.6f };
            for (int row = 0; row < saturations.Length; row++)
            {
                for (int col = 0; col < 8; col++)
                {
                    var hue = col / 8f;
                    swatches.Add(Color.HSVToRGB(hue, saturations[row], values[row]));
                }
            }

            // 常用色：白 / 半透明白 / 黑 / 透明
            swatches.Add(Color.white);
            swatches.Add(new Color(1f, 1f, 1f, 0.5f));
            swatches.Add(Color.black);
            swatches.Add(new Color(0f, 0f, 0f, 0f));
        }

        public bool Contains(Color color)
        {
            return swatches.Any(c => EditorTextures.IsSameColor(c, color));
        }

        /// <summary>把颜色作为一个新色块加入调色板（追加到末尾）。返回是否加入成功。</summary>
        public bool AddSwatch(Color color)
        {
            if (Contains(color))
                return false;

            if (swatches.Count >= kMaxSwatches)
                return false;

            swatches.Add(color);
            Save();
            return true;
        }

        /// <summary>
        /// 把颜色加入共享调色板，并返回可直接显示在状态栏的结果描述。
        /// 供"添加到调色板"按钮与取色会话共用。
        /// </summary>
        public static string AddColor(Color color)
        {
            var palette = Shared;
            var hex = "#" + ColorUtility.ToHtmlStringRGBA(color);

            if (palette.Contains(color))
                return "该颜色已在调色板中：" + hex;

            if (!palette.AddSwatch(color))
                return "调色板已满（上限 " + kMaxSwatches + " 色），请右键移除一些色块后再添加。";

            return "已把 " + hex + " 加入调色板（第 " + palette.swatches.Count + " 个色块）。";
        }

        public void RemoveSwatchAt(int index)
        {
            if (index < 0 || index >= swatches.Count)
                return;
            swatches.RemoveAt(index);
            Save();
        }

        /// <summary>独立于窗口实例写入"最近使用"（供取色会话在窗口已关闭时调用）。</summary>
        public static void AddRecentGlobal(Color color)
        {
            Shared.AddRecent(color);
        }

        public void AddRecent(Color color)
        {
            recent.RemoveAll(c => EditorTextures.IsSameColor(c, color));
            recent.Insert(0, color);
            while (recent.Count > kMaxRecent)
                recent.RemoveAt(recent.Count - 1);
            Save();
        }

        #region 绘制

        public static float GridHeight(int count, int columns)
        {
            columns = Mathf.Max(1, columns);
            var rows = Mathf.CeilToInt(count / (float)columns);
            return rows * (SwatchSize + SwatchSpacing);
        }

        /// <summary>
        /// 绘制色板网格。左键点击返回 true 并通过 picked 输出颜色；右键点击从色板移除该颜色。
        /// </summary>
        public bool DrawSwatchGrid(Rect rect, Color current, out Color picked)
        {
            picked = default;
            var clicked = false;
            var evt = Event.current;
            var cols = Mathf.Max(1, columns);

            for (int i = 0; i < swatches.Count; i++)
            {
                var row = i / cols;
                var col = i % cols;
                var cell = new Rect(
                    rect.x + col * (SwatchSize + SwatchSpacing),
                    rect.y + row * (SwatchSize + SwatchSpacing),
                    SwatchSize,
                    SwatchSize);

                if (cell.yMax > rect.yMax)
                    break;

                var color = swatches[i];
                var hovered = cell.Contains(evt.mousePosition);
                EditorTextures.DrawCheckerboard(cell);
                var previous = GUI.color;
                GUI.color = color;
                GUI.DrawTexture(cell, Texture2D.whiteTexture, ScaleMode.StretchToFill, false);
                GUI.color = previous;

                EditorTextures.DrawBorder(cell, EditorTextures.IsSameColor(color, current)
                    ? new Color(1f, 0.85f, 0.2f, 1f)
                    : hovered ? new Color(1f, 1f, 1f, 0.65f) : new Color(0f, 0f, 0f, 0.45f));

                if (!hovered || evt.type != EventType.MouseDown)
                    continue;

                if (evt.button == 0)
                {
                    picked = color;
                    clicked = true;
                    evt.Use();
                    break;
                }

                if (evt.button == 1 && evt.type == EventType.MouseDown)
                {
                    RemoveSwatchAt(i);
                    evt.Use();
                    break;
                }
            }

            return clicked;
        }

        #endregion
    }

    /// <summary>曲线颜色的渐变预览条（把 RGBA 曲线在时间上的变化画成一条色带）。</summary>
    public static class ColorCurveBar
    {
        /// <summary>渐变条高度。</summary>
        public const float Height = 22f;

        static Texture2D s_Texture;
        static string s_CacheKey;

        /// <summary>绘制渐变条。</summary>
        public static void Draw(Rect rect, ColorCurveProperty property, double start, double end)
        {
            if (property == null)
            {
                EditorGUI.DrawRect(rect, new Color(0.2f, 0.2f, 0.2f, 1f));
                return;
            }

            EditorTextures.DrawCheckerboard(rect);
            var width = Mathf.Clamp(Mathf.RoundToInt(rect.width), 2, 1024);
            GUI.DrawTexture(rect, GetTexture(property, start, end, width), ScaleMode.StretchToFill, false);
            EditorTextures.DrawBorder(rect, new Color(0f, 0f, 0f, 0.5f));
        }

        /// <summary>把时间映射到渐变条上的 x 坐标。</summary>
        public static float TimeToX(Rect rect, double time, double start, double end)
        {
            if (Math.Abs(end - start) < 0.0001d)
                return rect.x;
            return rect.x + (float)((time - start) / (end - start)) * rect.width;
        }

        public static void ReleaseTexture()
        {
            if (s_Texture == null)
                return;

            UnityObject.DestroyImmediate(s_Texture);
            s_Texture = null;
            s_CacheKey = null;
        }

        static Texture2D GetTexture(ColorCurveProperty property, double start, double end, int width)
        {
            var cacheKey = BuildCacheKey(property, start, end, width);
            if (s_Texture != null && s_Texture.width == width && s_CacheKey == cacheKey)
                return s_Texture;

            if (s_Texture == null || s_Texture.width != width)
            {
                ReleaseTexture();
                s_Texture = new Texture2D(width, 1, TextureFormat.RGBA32, false)
                {
                    hideFlags = HideFlags.HideAndDontSave,
                    wrapMode = TextureWrapMode.Clamp
                };
            }

            s_CacheKey = cacheKey;

            var pixels = new Color[width];
            for (int i = 0; i < width; i++)
            {
                var time = start + (end - start) * (i / (double)(width - 1));
                pixels[i] = property.Evaluate(time);
            }

            s_Texture.SetPixels(pixels);
            s_Texture.Apply(false, false);
            return s_Texture;
        }

        static string BuildCacheKey(ColorCurveProperty property, double start, double end, int width)
        {
            var hash = 17;
            hash = hash * 31 + property.Evaluate(start).GetHashCode();
            hash = hash * 31 + property.Evaluate(end).GetHashCode();

            for (int i = 0; i < 4; i++)
            {
                var curve = property.GetCurve(i);
                if (curve == null)
                    continue;

                var keys = curve.keys;
                for (int k = 0; k < keys.Length; k++)
                {
                    // Keyframe.GetHashCode 覆盖 time / value / inTangent / outTangent，
                    // 因此切线类型变化也会让渐变预览重新生成。
                    hash = hash * 31 + keys[k].GetHashCode();
                }
            }

            return property.id + "|" + start.ToString("F4") + "|" + end.ToString("F4") + "|" + width + "|" + hash;
        }
    }
}
