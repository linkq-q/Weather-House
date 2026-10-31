using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityObject = UnityEngine.Object;

namespace TimelineColorEditor
{
    /// <summary>新增关键帧使用的插值方式。</summary>
    public enum ColorKeyInterpolation
    {
        /// <summary>两侧限制自动（Clamped Auto）切线。</summary>
        Auto,

        Linear,
        Constant
    }

    /// <summary>
    /// 关键帧切线模式（命名与 Unity 动画窗口一致）。
    /// 只有 <see cref="Broken"/> 才允许左右两侧使用不同的切线类型。
    /// </summary>
    public enum KeyTangentMode
    {
        ClampedAuto,
        Auto,
        FreeSmooth,
        Flat,
        Broken
    }

    /// <summary>一个"时间 + 颜色"的写入请求。</summary>
    public struct ColorKeyframe
    {
        public float time;
        public Color color;

        public ColorKeyframe(float time, Color color)
        {
            this.time = time;
            this.color = color;
        }
    }

    /// <summary>
    /// 统一的 RGBA 曲线写入操作。
    /// 所有写入都以"一个颜色"为单位同时落到 R/G/B/A 四条曲线上，
    /// 因此编辑结果与 Timeline 曲线视图完全兼容（曲线视图依旧逐通道显示，但关键帧始终成组对齐）。
    /// </summary>
    public static class ColorCurveOperations
    {
        public const string UndoName = "Edit Timeline Color Curves";

        /// <summary>新增关键帧插值的界面标签（顺序与 <see cref="ColorKeyInterpolation"/> 一致）。</summary>
        public static readonly string[] InterpolationLabels = { "平滑", "线性", "恒定" };

        /// <summary>把时间吸附到帧。</summary>
        public static float SnapTime(double time, float frameRate)
        {
            if (frameRate <= 0f)
                return (float)time;
            return (float)(Math.Round(time * frameRate) / frameRate);
        }

        /// <summary>查找时间点上的关键帧索引（容差为半帧），未找到返回 -1。</summary>
        public static int FindKeyAt(AnimationCurve curve, float time, float frameRate)
        {
            if (curve == null)
                return -1;

            var range = frameRate > 0f ? 0.5f / frameRate : 0.001f;
            var keys = curve.keys;
            for (int i = 0; i < keys.Length; i++)
            {
                if (keys[i].time >= time - range && keys[i].time < time + range)
                    return i;
            }
            return -1;
        }

        public static bool HasAnyChannel(bool[] channels)
        {
            if (channels == null)
                return true;
            for (int i = 0; i < channels.Length; i++)
            {
                if (channels[i])
                    return true;
            }
            return false;
        }

        /// <summary>把时间换算成帧号（用于界面显示）。</summary>
        public static int TimeToFrame(double time, float frameRate)
        {
            if (frameRate <= 0f)
                return 0;
            return Mathf.Max(0, Mathf.RoundToInt((float)(time * frameRate)));
        }

        #region 关键帧切线

        /// <summary>切线类型下拉框的候选值（顺序与 <see cref="TangentModeLabels"/> 一致）。</summary>
        public static readonly AnimationUtility.TangentMode[] TangentModes =
        {
            AnimationUtility.TangentMode.ClampedAuto,
            AnimationUtility.TangentMode.Auto,
            AnimationUtility.TangentMode.Linear,
            AnimationUtility.TangentMode.Constant,
            AnimationUtility.TangentMode.Free
        };

        public static readonly string[] TangentModeLabels =
        {
            "自动（平滑）",
            "自动（自由）",
            "线性",
            "恒定（阶跃）",
            "自由"
        };

        /// <summary>切线模式下拉框的标签（顺序与 <see cref="KeyTangentMode"/> 一致）。</summary>
        public static readonly string[] KeyModeLabels =
        {
            "Clamped Auto",
            "Auto",
            "Free Smooth",
            "Flat",
            "Broken"
        };

        public static int TangentModeIndex(AnimationUtility.TangentMode mode)
        {
            for (int i = 0; i < TangentModes.Length; i++)
            {
                if (TangentModes[i] == mode)
                    return i;
            }
            return 0;
        }

        /// <summary>找到某个时间点上第一个有关键帧的启用通道（统一编辑时各通道切线一起改，取其一即可）。</summary>
        static bool TryFindKey(ColorCurveProperty property, float time, bool[] channels, out AnimationCurve curve, out int index)
        {
            curve = null;
            index = -1;

            if (property == null || property.target == null)
                return false;

            var frameRate = property.target.frameRate;
            var snapped = SnapTime(time, frameRate);

            for (int i = 0; i < 4; i++)
            {
                if (channels != null && !channels[i])
                    continue;

                var candidate = property.GetCurve(i);
                if (candidate == null)
                    continue;

                var found = FindKeyAt(candidate, snapped, frameRate);
                if (found < 0)
                    continue;

                curve = candidate;
                index = found;
                return true;
            }

            return false;
        }

        /// <summary>
        /// 读取某个时间点上关键帧的切线模式与左右切线类型。
        /// Free 且切线水平视为 Flat；Linear / Constant 只可能来自 Broken 模式下的独立设置。
        /// </summary>
        public static bool TryGetKeyMode(ColorCurveProperty property, float time, bool[] channels,
            out KeyTangentMode mode, out AnimationUtility.TangentMode leftMode, out AnimationUtility.TangentMode rightMode)
        {
            mode = KeyTangentMode.ClampedAuto;
            leftMode = AnimationUtility.TangentMode.ClampedAuto;
            rightMode = AnimationUtility.TangentMode.ClampedAuto;

            if (!TryFindKey(property, time, channels, out var curve, out var index))
                return false;

            leftMode = AnimationUtility.GetKeyLeftTangentMode(curve, index);
            rightMode = AnimationUtility.GetKeyRightTangentMode(curve, index);

            if (AnimationUtility.GetKeyBroken(curve, index) || leftMode != rightMode)
            {
                mode = KeyTangentMode.Broken;
                return true;
            }

            if (leftMode == AnimationUtility.TangentMode.ClampedAuto)
            {
                mode = KeyTangentMode.ClampedAuto;
            }
            else if (leftMode == AnimationUtility.TangentMode.Auto)
            {
                mode = KeyTangentMode.Auto;
            }
            else if (leftMode == AnimationUtility.TangentMode.Free)
            {
                var key = curve.keys[index];
                var flat = Mathf.Abs(key.inTangent) < 0.0001f && Mathf.Abs(key.outTangent) < 0.0001f;
                mode = flat ? KeyTangentMode.Flat : KeyTangentMode.FreeSmooth;
            }
            else
            {
                // Linear / Constant：只可能由 Broken 模式下的左右类型设置产生。
                mode = KeyTangentMode.Broken;
            }

            return true;
        }

        /// <summary>
        /// 设置某个时间点上各启用通道关键帧的左侧或右侧切线类型。
        /// 只应在 Broken 模式下使用：这里会显式保持关键帧为断开状态。
        /// </summary>
        public static bool SetKeyTangentMode(ColorCurveProperty property, float time, bool[] channels, bool leftSide,
            AnimationUtility.TangentMode mode)
        {
            return ApplyKeyTangents(property, time, channels, leftSide, mode, !leftSide, mode, true);
        }

        static bool ApplyKeyTangents(ColorCurveProperty property, float time, bool[] channels,
            bool setLeft, AnimationUtility.TangentMode leftMode,
            bool setRight, AnimationUtility.TangentMode rightMode,
            bool broken)
        {
            return ApplyToKeys(property, time, channels, "Edit Key Tangents", (curve, index) =>
            {
                if (setLeft)
                    AnimationUtility.SetKeyLeftTangentMode(curve, index, leftMode);
                if (setRight)
                    AnimationUtility.SetKeyRightTangentMode(curve, index, rightMode);

                AnimationUtility.SetKeyBroken(curve, index, broken);
            });
        }

        /// <summary>设置某个时间点上各启用通道关键帧的切线模式。</summary>
        public static bool SetKeyMode(ColorCurveProperty property, float time, bool[] channels, KeyTangentMode mode)
        {
            return ApplyToKeys(property, time, channels, "Edit Key Tangent Mode",
                (curve, index) => ApplyModeToKey(curve, index, mode));
        }

        /// <summary>对单个关键帧应用切线模式。</summary>
        static void ApplyModeToKey(AnimationCurve curve, int index, KeyTangentMode mode)
        {
            switch (mode)
            {
                case KeyTangentMode.Broken:
                    // 仅标记为断开，保留当前左右类型，之后可用左/右切线下拉框分别调整。
                    AnimationUtility.SetKeyBroken(curve, index, true);
                    return;

                case KeyTangentMode.ClampedAuto:
                    SetSymmetricTangentMode(curve, index, AnimationUtility.TangentMode.ClampedAuto);
                    return;

                case KeyTangentMode.Auto:
                    SetSymmetricTangentMode(curve, index, AnimationUtility.TangentMode.Auto);
                    return;

                case KeyTangentMode.Flat:
                    // 先写切线值再设模式：给 curve.keys 赋值会重置切线模式。
                    SetTangentValues(curve, index, 0f);
                    SetSymmetricTangentMode(curve, index, AnimationUtility.TangentMode.Free);
                    return;

                case KeyTangentMode.FreeSmooth:
                    var slope = GetSmoothSlope(curve, index);
                    if (Mathf.Abs(slope) < 0.0001f)
                    {
                        // 极值点算出来是水平斜率，退回当前切线，避免看起来和 Flat 一样。
                        var key = curve.keys[index];
                        slope = Mathf.Abs(key.outTangent) > 0.0001f ? key.outTangent : key.inTangent;
                    }

                    SetTangentValues(curve, index, slope);
                    SetSymmetricTangentMode(curve, index, AnimationUtility.TangentMode.Free);
                    return;
            }
        }

        /// <summary>把左右两侧设为同一类型，并取消"断开"。</summary>
        static void SetSymmetricTangentMode(AnimationCurve curve, int index, AnimationUtility.TangentMode mode)
        {
            AnimationUtility.SetKeyLeftTangentMode(curve, index, mode);
            AnimationUtility.SetKeyRightTangentMode(curve, index, mode);
            AnimationUtility.SetKeyBroken(curve, index, false);
        }

        /// <summary>对某个时间点上每个启用通道的关键帧执行一次编辑（统一的 Undo / 刷新处理）。</summary>
        static bool ApplyToKeys(ColorCurveProperty property, float time, bool[] channels, string undoName,
            Action<AnimationCurve, int> edit)
        {
            if (property == null || property.target == null || edit == null)
                return false;

            var clip = property.target.curves;
            if (clip == null)
                return false;

            var frameRate = property.target.frameRate;
            var snapped = SnapTime(time, frameRate);

            var curves = new AnimationCurve[4];
            var touched = new bool[4];
            var modified = false;

            for (int i = 0; i < 4; i++)
            {
                if (channels != null && !channels[i])
                    continue;

                var curve = AnimationUtility.GetEditorCurve(clip, property.GetBinding(i));
                if (curve == null)
                    continue;

                var index = FindKeyAt(curve, snapped, frameRate);
                if (index < 0)
                    continue;

                edit(curve, index);
                curves[i] = curve;
                touched[i] = true;
                modified = true;
            }

            if (!modified)
                return false;

            Undo.RegisterCompleteObjectUndo(clip, undoName);
            for (int i = 0; i < 4; i++)
            {
                if (touched[i])
                    AnimationUtility.SetEditorCurve(clip, property.GetBinding(i), curves[i]);
            }

            EditorUtility.SetDirty(clip);
            TimelineEditorBridge.Refresh();
            return true;
        }

        #endregion

        #region 写入

        /// <summary>在指定时间点写入（新增或更新）一个统一的颜色关键帧。</summary>
        public static bool SetColorKey(ColorCurveProperty property, double time, Color color, bool[] channels, ColorKeyInterpolation interpolation)
        {
            if (property == null)
                return false;

            var frameRate = property.target != null ? property.target.frameRate : 30f;
            var frames = new List<ColorKeyframe> { new ColorKeyframe(SnapTime(time, frameRate), color) };
            return WriteKeys(property, frames, channels, interpolation);
        }

        /// <summary>对多个颜色属性同时写入关键帧。</summary>
        public static int SetColorKey(IEnumerable<ColorCurveProperty> properties, double time, Color color, bool[] channels, ColorKeyInterpolation interpolation)
        {
            if (properties == null)
                return 0;

            var count = 0;
            foreach (var property in properties)
            {
                if (SetColorKey(property, time, color, channels, interpolation))
                    count++;
            }
            return count;
        }

        /// <summary>把该颜色所有已有关键帧统一改成同一个颜色。</summary>
        public static bool SetAllKeys(ColorCurveProperty property, Color color, bool[] channels)
        {
            var times = property.GetKeyTimes();
            if (times.Count == 0)
                return false;

            var frames = times.Select(t => new ColorKeyframe(t, color)).ToList();
            return WriteKeys(property, frames, channels, ColorKeyInterpolation.Auto);
        }

        /// <summary>
        /// 统一 RGBA 关键帧：把启用的通道按"所有关键帧时间的并集"重新采样，
        /// 使四个通道的关键帧始终成组对齐（后续就能整体调色）。
        /// </summary>
        public static bool AlignChannels(ColorCurveProperty property, bool[] channels)
        {
            var times = property.GetKeyTimes();
            if (times.Count == 0)
                return false;

            var frames = times.Select(t => new ColorKeyframe(t, property.Evaluate(t))).ToList();
            return WriteKeys(property, frames, channels, ColorKeyInterpolation.Auto);
        }

        /// <summary>
        /// 渐变填充：在 [startTime, endTime] 内生成 from → to 的渐变关键帧。
        /// keepExistingKeys 为 true 时沿用区间内已有的关键帧时间（颜色按时间比例插值），否则按时长均匀生成 steps 个关键帧。
        /// </summary>
        public static bool ApplyGradient(ColorCurveProperty property, double startTime, double endTime, Color from, Color to,
            int steps, bool[] channels, ColorKeyInterpolation interpolation, bool keepExistingKeys)
        {
            if (property == null)
                return false;

            var frameRate = property.target != null ? property.target.frameRate : 30f;
            var start = SnapTime(startTime, frameRate);
            var end = SnapTime(endTime, frameRate);
            if (end <= start)
                return false;

            var times = new List<float>();
            if (keepExistingKeys)
            {
                times.AddRange(property.GetKeyTimes().Where(t => t >= start - 0.5f / frameRate && t <= end + 0.5f / frameRate));
                times = times.Select(t => Mathf.Clamp(t, start, end)).Distinct().OrderBy(t => t).ToList();
            }

            if (times.Count < 2)
            {
                steps = Mathf.Max(2, steps);
                times.Clear();
                for (int i = 0; i < steps; i++)
                {
                    var t = start + (end - start) * (i / (float)(steps - 1));
                    times.Add(SnapTime(t, frameRate));
                }
                times = times.Distinct().OrderBy(t => t).ToList();
            }

            var frames = new List<ColorKeyframe>(times.Count);
            for (int i = 0; i < times.Count; i++)
            {
                var t = times[i];
                var ratio = end > start ? Mathf.Clamp01((t - start) / (end - start)) : 0f;
                frames.Add(new ColorKeyframe(t, Color.LerpUnclamped(from, to, ratio)));
            }

            return WriteKeys(property, frames, channels, interpolation);
        }

        /// <summary>删除指定时间点上的关键帧（只处理启用的通道）。返回被删除的通道数。</summary>
        public static int RemoveKeysAt(ColorCurveProperty property, double time, bool[] channels)
        {
            if (property == null || property.target == null)
                return 0;

            var clip = property.target.curves;
            if (clip == null)
                return 0;

            var frameRate = property.target.frameRate;
            var snapped = SnapTime(time, frameRate);

            var removed = 0;
            var newCurves = new AnimationCurve[4];
            for (int i = 0; i < 4; i++)
            {
                newCurves[i] = AnimationUtility.GetEditorCurve(clip, property.GetBinding(i));
                if (newCurves[i] == null || channels != null && !channels[i])
                    continue;

                var index = FindKeyAt(newCurves[i], snapped, frameRate);
                if (index < 0)
                    continue;

                newCurves[i].RemoveKey(index);
                removed++;
            }

            if (removed == 0)
                return 0;

            RegisterUndo(property, clip);
            for (int i = 0; i < 4; i++)
            {
                if (newCurves[i] == null || channels != null && !channels[i])
                    continue;

                AnimationUtility.SetEditorCurve(clip, property.GetBinding(i), newCurves[i].length > 0 ? newCurves[i] : null);
            }

            EditorUtility.SetDirty(clip);
            TimelineEditorBridge.Refresh();
            return removed;
        }

        /// <summary>删除该颜色属性的全部曲线（恢复为模板上的静态值）。</summary>
        public static bool ClearCurves(ColorCurveProperty property)
        {
            if (property == null || property.target == null)
                return false;

            var clip = property.target.curves;
            if (clip == null)
                return false;

            RegisterUndo(property, clip);
            for (int i = 0; i < 4; i++)
                AnimationUtility.SetEditorCurve(clip, property.GetBinding(i), null);

            EditorUtility.SetDirty(clip);
            TimelineEditorBridge.Refresh();
            return true;
        }

        #endregion

        #region 内部实现

        static bool WriteKeys(ColorCurveProperty property, IList<ColorKeyframe> frames, bool[] channels, ColorKeyInterpolation interpolation)
        {
            if (property == null || property.target == null || frames == null || frames.Count == 0)
                return false;

            if (!HasAnyChannel(channels))
                return false;

            var created = property.target.EnsureCurves();
            var clip = property.target.curves;
            if (clip == null)
                return false;

            RegisterUndo(property, clip);

            var frameRate = property.target.frameRate;
            var curves = new AnimationCurve[4];
            for (int i = 0; i < 4; i++)
                curves[i] = AnimationUtility.GetEditorCurve(clip, property.GetBinding(i)) ?? new AnimationCurve();

            for (int f = 0; f < frames.Count; f++)
            {
                var time = SnapTime(frames[f].time, frameRate);
                var color = frames[f].color;
                for (int i = 0; i < 4; i++)
                {
                    if (channels != null && !channels[i])
                        continue;

                    SetKey(curves[i], time, color[i], frameRate, interpolation);
                }
            }

            for (int i = 0; i < 4; i++)
            {
                if (channels != null && !channels[i])
                    continue;

                AnimationUtility.SetEditorCurve(clip, property.GetBinding(i), curves[i].length > 0 ? curves[i] : null);
            }

            EditorUtility.SetDirty(clip);
            TimelineEditorBridge.Refresh(created);
            return true;
        }

        static void RegisterUndo(ColorCurveProperty property, AnimationClip clip)
        {
            var objects = new List<UnityObject>();
            var target = property.target;
            if (target.owner != null)
                objects.Add(target.owner);
            if (clip != null)
                objects.Add(clip);

            var parentTrack = target.clip != null ? target.clip.GetParentTrack() : null;
            if (parentTrack != null && !objects.Contains(parentTrack))
                objects.Add(parentTrack);

            if (objects.Count > 0)
                Undo.RegisterCompleteObjectUndo(objects.ToArray(), UndoName);
        }

        static void SetKey(AnimationCurve curve, float time, float value, float frameRate, ColorKeyInterpolation interpolation)
        {
            var index = FindKeyAt(curve, time, frameRate);
            if (index >= 0)
            {
                UpdateExistingKey(curve, index, time, value);
                return;
            }

            index = curve.AddKey(time, value);

            // 新建关键帧：先按邻点算出平滑切线值（这样 Clamped Auto 模式下的曲线也是真正平滑的），
            // 再设置切线模式。顺序不能反：给 curve.keys 赋值会把切线模式重置。
            if (interpolation == ColorKeyInterpolation.Auto)
                SetTangentValues(curve, index, GetSmoothSlope(curve, index));

            ApplyInterpolation(curve, index, interpolation);
        }

        /// <summary>更新已有键的时间与数值，并保留它原有的切线模式（写颜色不应该改变切线设置）。</summary>
        static void UpdateExistingKey(AnimationCurve curve, int index, float time, float value)
        {
            var leftMode = AnimationUtility.GetKeyLeftTangentMode(curve, index);
            var rightMode = AnimationUtility.GetKeyRightTangentMode(curve, index);
            var broken = AnimationUtility.GetKeyBroken(curve, index);

            var keys = curve.keys;
            keys[index].time = time;
            keys[index].value = value;
            curve.keys = keys;

            // curve.keys 赋值可能重置切线模式，这里恢复原状。
            AnimationUtility.SetKeyLeftTangentMode(curve, index, leftMode);
            AnimationUtility.SetKeyRightTangentMode(curve, index, rightMode);
            AnimationUtility.SetKeyBroken(curve, index, broken);
        }

        /// <summary>写入单个关键帧的左右切线值（两侧对称）。</summary>
        static void SetTangentValues(AnimationCurve curve, int index, float slope)
        {
            if (curve == null || index < 0 || index >= curve.length)
                return;

            var keys = curve.keys;
            var key = keys[index];
            key.inTangent = slope;
            key.outTangent = slope;
            keys[index] = key;
            curve.keys = keys;
        }

        /// <summary>按相邻关键帧计算限制自动（平滑）斜率；极值点返回 0。</summary>
        static float GetSmoothSlope(AnimationCurve curve, int index)
        {
            if (curve == null || index < 0 || index >= curve.length)
                return 0f;

            var keys = curve.keys;
            var key = keys[index];
            var hasPrev = index > 0;
            var hasNext = index < keys.Length - 1;

            if (hasPrev && hasNext)
            {
                var dtPrev = key.time - keys[index - 1].time;
                var dtNext = keys[index + 1].time - key.time;
                var mPrev = dtPrev > 0f ? (key.value - keys[index - 1].value) / dtPrev : 0f;
                var mNext = dtNext > 0f ? (keys[index + 1].value - key.value) / dtNext : 0f;

                // 斜率异号说明该点是极值，取平。
                if (!Mathf.Approximately(Mathf.Sign(mPrev), Mathf.Sign(mNext)))
                    return 0f;

                // 单调区间：取两侧斜率均值，并限制幅度避免过冲。
                var slope = (mPrev + mNext) * 0.5f;
                var limit = 3f * Mathf.Min(Mathf.Abs(mPrev), Mathf.Abs(mNext));
                return Mathf.Clamp(slope, -limit, limit);
            }

            if (hasPrev)
            {
                var dt = key.time - keys[index - 1].time;
                return dt > 0f ? (key.value - keys[index - 1].value) / dt : 0f;
            }

            if (hasNext)
            {
                var dt = keys[index + 1].time - key.time;
                return dt > 0f ? (keys[index + 1].value - key.value) / dt : 0f;
            }

            return 0f;
        }

        static void ApplyInterpolation(AnimationCurve curve, int index, ColorKeyInterpolation interpolation)
        {
            if (curve == null || index < 0 || index >= curve.length)
                return;

            switch (interpolation)
            {
                case ColorKeyInterpolation.Linear:
                    AnimationUtility.SetKeyLeftTangentMode(curve, index, AnimationUtility.TangentMode.Linear);
                    AnimationUtility.SetKeyRightTangentMode(curve, index, AnimationUtility.TangentMode.Linear);
                    break;

                case ColorKeyInterpolation.Constant:
                    AnimationUtility.SetKeyLeftTangentMode(curve, index, AnimationUtility.TangentMode.Constant);
                    AnimationUtility.SetKeyRightTangentMode(curve, index, AnimationUtility.TangentMode.Constant);
                    break;

                default:
                    // 平滑：默认切线类型为 Clamped Auto（两侧限制自动）。
                    AnimationUtility.SetKeyLeftTangentMode(curve, index, AnimationUtility.TangentMode.ClampedAuto);
                    AnimationUtility.SetKeyRightTangentMode(curve, index, AnimationUtility.TangentMode.ClampedAuto);
                    break;
            }

            AnimationUtility.SetKeyBroken(curve, index, false);
        }

        #endregion
    }
}
