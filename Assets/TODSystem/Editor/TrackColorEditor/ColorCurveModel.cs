using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.Timeline;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;
using UnityObject = UnityEngine.Object;

namespace TimelineColorEditor
{
    /// <summary>颜色曲线的承载对象类型。</summary>
    public enum ColorTargetKind
    {
        /// <summary>轨道自身的属性曲线（Timeline 曲线视图中的 "Track Properties"）。</summary>
        Track,

        /// <summary>片段资源上的属性曲线（Timeline 曲线视图中的 "Clip Properties"）。</summary>
        Clip
    }

    /// <summary>
    /// 一个可以编辑颜色曲线的 Timeline 对象。
    /// Track：曲线存放在 <see cref="TrackAsset.curves"/>；
    /// Clip ：曲线存放在 <see cref="TimelineClip.curves"/>。
    /// </summary>
    public sealed class ColorCurveTarget
    {
        public ColorTargetKind kind;
        public TrackAsset track;
        public TimelineClip clip;

        /// <summary>承载颜色属性的对象（TrackAsset 或 clip.asset）。</summary>
        public UnityObject owner
        {
            get
            {
                if (kind == ColorTargetKind.Track)
                    return track;
                return clip != null ? clip.asset : null;
            }
        }

        public TimelineAsset timelineAsset
        {
            get
            {
                if (kind == ColorTargetKind.Track)
                    return track != null ? track.timelineAsset : null;

                var parent = clip != null ? clip.GetParentTrack() : null;
                return parent != null ? parent.timelineAsset : null;
            }
        }

        public string label
        {
            get
            {
                if (kind == ColorTargetKind.Track)
                    return (track != null ? track.name : "<Missing Track>") + "  ·  轨道属性";
                return (clip != null ? clip.displayName : "<Missing Clip>") + "  ·  片段属性";
            }
        }

        public AnimationClip curves
        {
            get
            {
                if (kind == ColorTargetKind.Track)
                    return track != null ? track.curves : null;
                return clip != null ? clip.curves : null;
            }
        }

        public bool hasCurves
        {
            get
            {
                if (kind == ColorTargetKind.Track)
                    return track != null && track.hasCurves;
                return clip != null && clip.hasCurves;
            }
        }

        /// <summary>曲线绑定的动画类型（与 Timeline 内部保持一致：即承载对象的类型）。</summary>
        public Type bindingType
        {
            get
            {
                var o = owner;
                return o != null ? o.GetType() : typeof(TimelineAsset);
            }
        }

        public float frameRate
        {
            get
            {
                var asset = timelineAsset;
                if (asset != null)
                {
                    var rate = (float)asset.editorSettings.frameRate;
                    if (rate > 0f)
                        return rate;
                }

                var animation = curves;
                if (animation != null && animation.frameRate > 0f)
                    return animation.frameRate;

                return 30f;
            }
        }

        public double duration
        {
            get
            {
                var asset = timelineAsset;
                return asset != null ? asset.duration : 0d;
            }
        }

        /// <summary>
        /// 确保该对象上存在用于存放属性曲线的 AnimationClip（不存在时按 Timeline 的方式创建）。
        /// </summary>
        /// <returns>是否新建了曲线资源。</returns>
        public bool EnsureCurves()
        {
            if (curves != null)
                return false;

            // 对象可能已被删除（Unity 的伪 null）。
            if (owner == null)
                return false;

            var name = GetUniqueCurvesName();
            if (kind == ColorTargetKind.Track)
            {
                if (track == null)
                    return false;

                Undo.RegisterCompleteObjectUndo(track, "Create Track Curves");
                track.CreateCurves(name);
            }
            else
            {
                if (clip == null || clip.asset == null)
                    return false;

                var parent = clip.GetParentTrack();
                if (parent != null)
                    Undo.RegisterCompleteObjectUndo(parent, "Create Clip Curves");
                clip.CreateCurves(name);
            }

            return curves != null;
        }

        string GetUniqueCurvesName()
        {
            const string kDefaultCurvesName = "Track Parameters";
            var asset = timelineAsset;
            var path = asset != null ? AssetDatabase.GetAssetPath(asset) : null;
            if (string.IsNullOrEmpty(path))
                return kDefaultCurvesName;

            var existing = AssetDatabase.LoadAllAssetsAtPath(path)
                .OfType<AnimationClip>()
                .Select(c => c.name)
                .ToArray();

            return ObjectNames.GetUniqueName(existing, kDefaultCurvesName);
        }
    }

    /// <summary>
    /// 一个 Color 类型的可动画属性。
    /// 对外始终以"一个颜色"为单位读写，内部对应 R/G/B/A 四条 float 曲线，
    /// 这样就能用调色盘一次性编辑全部通道，而不是逐个通道编辑。
    /// </summary>
    public sealed class ColorCurveProperty
    {
        public static readonly string[] ChannelSuffix = { "r", "g", "b", "a" };
        public static readonly string[] ChannelLabel = { "R", "G", "B", "A" };

        public readonly ColorCurveTarget target;

        /// <summary>序列化属性路径，例如 template.skyColorTop。</summary>
        public readonly string propertyPath;

        /// <summary>曲线绑定使用的属性名（去掉了 playable 字段前缀），例如 skyColorTop。</summary>
        public readonly string bindingName;

        /// <summary>界面显示名，例如 Sky Color Top。</summary>
        public readonly string displayName;

        public ColorCurveProperty(ColorCurveTarget target, string propertyPath, string bindingName, string displayName)
        {
            this.target = target;
            this.propertyPath = propertyPath;
            this.bindingName = bindingName;
            this.displayName = displayName;
        }

        /// <summary>用于在刷新前后保持选中的唯一键。</summary>
        public string id
        {
            get { return (target != null && target.owner != null ? target.owner.GetInstanceID().ToString() : "0") + "|" + propertyPath; }
        }

        public AnimationClip curves
        {
            get { return target != null ? target.curves : null; }
        }

        /// <summary>某个通道（0=R,1=G,2=B,3=A）的曲线绑定。</summary>
        public EditorCurveBinding GetBinding(int channel)
        {
            var type = target != null ? target.bindingType : typeof(TimelineAsset);
            return EditorCurveBinding.FloatCurve(string.Empty, type, bindingName + "." + ChannelSuffix[channel]);
        }

        /// <summary>读取某个通道的曲线，不存在时返回 null。</summary>
        public AnimationCurve GetCurve(int channel)
        {
            var clip = curves;
            if (clip == null)
                return null;
            return AnimationUtility.GetEditorCurve(clip, GetBinding(channel));
        }

        public bool HasChannelCurve(int channel)
        {
            var curve = GetCurve(channel);
            return curve != null && curve.length > 0;
        }

        public bool hasAnyCurve
        {
            get
            {
                for (int i = 0; i < 4; i++)
                {
                    if (HasChannelCurve(i))
                        return true;
                }
                return false;
            }
        }

        /// <summary>读取尚未生成曲线时的模板值（即 Inspector 里的颜色）。</summary>
        public Color GetStaticColor()
        {
            var o = target != null ? target.owner : null;
            if (o == null)
                return Color.white;

            var so = new SerializedObject(o);
            var sp = so.FindProperty(propertyPath);
            if (sp == null || sp.propertyType != SerializedPropertyType.Color)
                return Color.white;

            return sp.colorValue;
        }

        /// <summary>把颜色写入模板（未生成曲线时，Timeline 用该值作为常量曲线）。</summary>
        public bool SetStaticColor(Color color)
        {
            var o = target != null ? target.owner : null;
            if (o == null)
                return false;

            var so = new SerializedObject(o);
            so.UpdateIfRequiredOrScript();
            var sp = so.FindProperty(propertyPath);
            if (sp == null || sp.propertyType != SerializedPropertyType.Color)
                return false;

            Undo.RecordObject(o, "Set Track Color");
            sp.colorValue = color;
            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(o);
            return true;
        }

        /// <summary>在指定时间点求值：优先使用曲线，缺失的通道回退到模板颜色。</summary>
        public Color Evaluate(double time)
        {
            var result = GetStaticColor();
            for (int i = 0; i < 4; i++)
            {
                var curve = GetCurve(i);
                if (curve != null && curve.length > 0)
                    result[i] = curve.Evaluate((float)time);
            }
            return result;
        }

        /// <summary>合并四个通道的所有关键帧时间（按帧率吸附、去重、升序）。</summary>
        public List<float> GetKeyTimes()
        {
            var frameRate = target != null ? target.frameRate : 30f;
            var times = new List<float>();
            for (int i = 0; i < 4; i++)
            {
                var curve = GetCurve(i);
                if (curve == null)
                    continue;

                foreach (var key in curve.keys)
                    times.Add(ColorCurveOperations.SnapTime(key.time, frameRate));
            }

            var result = new List<float>();
            foreach (var time in times.OrderBy(t => t))
            {
                if (result.Count == 0 || Mathf.Abs(result[result.Count - 1] - time) > 0.5f / frameRate)
                    result.Add(time);
            }
            return result;
        }

        /// <summary>某个时间点上，各通道是否已有关键帧。</summary>
        public bool[] GetChannelKeyPresence(float time)
        {
            var frameRate = target != null ? target.frameRate : 30f;
            var presence = new bool[4];
            for (int i = 0; i < 4; i++)
            {
                var curve = GetCurve(i);
                presence[i] = curve != null && ColorCurveOperations.FindKeyAt(curve, time, frameRate) >= 0;
            }
            return presence;
        }

        /// <summary>是否在某个时间点存在"完整的" RGBA 关键帧（四个通道都有键）。</summary>
        public bool HasUnifiedKeyAt(double time)
        {
            var frameRate = target != null ? target.frameRate : 30f;
            var snapped = ColorCurveOperations.SnapTime(time, frameRate);
            var presence = GetChannelKeyPresence(snapped);
            return presence[0] && presence[1] && presence[2] && presence[3];
        }

        /// <summary>该颜色的时间范围（用于渐变条与渐变生成）；没有关键帧时回退到整条 Timeline。</summary>
        public void GetTimeRange(out double start, out double end)
        {
            var times = GetKeyTimes();
            if (times.Count >= 2)
            {
                start = times.First();
                end = times.Last();
                return;
            }

            if (times.Count == 1)
            {
                start = 0d;
                end = Math.Max(target.duration, times[0]);
                return;
            }

            start = 0d;
            end = Math.Max(target.duration, 1d);
        }
    }

    /// <summary>选中目标与颜色属性的收集（与 Timeline 曲线视图使用同一套选中状态与同一份曲线资源）。</summary>
    public static class ColorCurveTargets
    {
        /// <summary>收集当前 Timeline 选中项对应的编辑目标。</summary>
        public static List<ColorCurveTarget> GetSelectedTargets()
        {
            var inspected = TimelineEditor.inspectedAsset;
            var tracks = new List<ColorCurveTarget>();
            var clips = new List<ColorCurveTarget>();

            foreach (var track in Selection.objects.OfType<TrackAsset>())
            {
                if (track == null || track is AnimationTrack)
                    continue;
                tracks.Add(new ColorCurveTarget { kind = ColorTargetKind.Track, track = track });
            }

            foreach (var clip in TimelineEditor.selectedClips)
            {
                if (clip == null || clip.asset == null)
                    continue;
                clips.Add(new ColorCurveTarget { kind = ColorTargetKind.Clip, clip = clip, track = clip.GetParentTrack() });
            }

            var all = tracks.Concat(clips).ToList();

            // 若 Timeline 窗口正在编辑某个 TimelineAsset，则优先使用属于它的选中项。
            if (inspected != null)
            {
                var matching = all.Where(t => t.timelineAsset == inspected).ToList();
                if (matching.Count > 0)
                    return matching;
            }

            return all;
        }

        /// <summary>枚举目标对象上所有可动画的 Color 属性。</summary>
        public static List<ColorCurveProperty> GetColorProperties(ColorCurveTarget target)
        {
            var result = new List<ColorCurveProperty>();
            var owner = target != null ? target.owner : null;
            if (owner == null)
                return result;

            var serializedObject = new SerializedObject(owner);
            var iterator = serializedObject.GetIterator();
            while (iterator.NextVisible(true))
            {
                if (iterator.propertyType != SerializedPropertyType.Color)
                    continue;

                if (!IsAnimatableColorProperty(target, iterator))
                    continue;

                var bindingName = GetBindingName(owner, iterator.propertyPath);
                result.Add(new ColorCurveProperty(
                    target,
                    iterator.propertyPath,
                    bindingName,
                    ObjectNames.NicifyVariableName(bindingName)));
            }

            return result;
        }

        static bool IsAnimatableColorProperty(ColorCurveTarget target, SerializedProperty property)
        {
            var owner = target.owner;
            if (owner == null || owner is AnimationTrack)
                return false;

            // 与 Timeline 的 AnimatedParameterUtility.IsParameterAnimatable 保持一致。
            if (Attribute.IsDefined(owner.GetType(), typeof(NotKeyableAttribute)))
                return false;

            if (!IsKeyableInHierarchy(property))
                return false;

            return owner is IPlayableBehaviour || IsUnderScriptPlayableField(owner as IPlayableAsset, property.propertyPath);
        }

        static bool IsKeyableInHierarchy(SerializedProperty property)
        {
            const BindingFlags bindingFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            var segments = property.propertyPath.Split('.');
            var type = property.serializedObject.targetObject.GetType();

            foreach (var segment in segments)
            {
                if (type.GetCustomAttributes(typeof(NotKeyableAttribute), false).Any())
                    return false;

                if (type.IsArray)
                    return false;

                var field = type.GetField(segment, bindingFlags);
                if (field == null ||
                    field.GetCustomAttributes(typeof(NotKeyableAttribute), false).Any() ||
                    field.GetCustomAttributes(typeof(HideInInspector), false).Any())
                    return false;

                type = field.FieldType;

                if (!type.IsValueType && !typeof(IPlayableBehaviour).IsAssignableFrom(type))
                    return false;
            }

            return true;
        }

        /// <summary>属性的路径是否位于某个脚本 playable 字段（例如 template）之下。</summary>
        static bool IsUnderScriptPlayableField(IPlayableAsset asset, string propertyPath)
        {
            foreach (var field in GetScriptPlayableFields(asset))
            {
                if (propertyPath.StartsWith(field.Name, StringComparison.Ordinal) &&
                    propertyPath.Length > field.Name.Length &&
                    propertyPath[field.Name.Length] == '.')
                    return true;
            }
            return false;
        }

        /// <summary>把序列化路径转换为曲线绑定的属性名（去掉 playable 字段前缀）。</summary>
        static string GetBindingName(UnityObject owner, string propertyPath)
        {
            if (owner is IPlayableBehaviour)
                return propertyPath;

            foreach (var field in GetScriptPlayableFields(owner as IPlayableAsset))
            {
                if (propertyPath.StartsWith(field.Name, StringComparison.Ordinal) &&
                    propertyPath.Length > field.Name.Length &&
                    propertyPath[field.Name.Length] == '.')
                    return propertyPath.Substring(field.Name.Length + 1);
            }

            return propertyPath;
        }

        static FieldInfo[] GetScriptPlayableFields(IPlayableAsset asset)
        {
            if (asset == null)
                return new FieldInfo[0];

            return asset.GetType()
                .GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Where(f => typeof(IPlayableBehaviour).IsAssignableFrom(f.FieldType) &&
                            (f.IsPublic || f.GetCustomAttributes(typeof(SerializeField), false).Any()) &&
                            !f.GetCustomAttributes(typeof(NotKeyableAttribute), false).Any() &&
                            !f.GetCustomAttributes(typeof(HideInInspector), false).Any() &&
                            !f.FieldType.GetCustomAttributes(typeof(NotKeyableAttribute), false).Any())
                .ToArray();
        }
    }

    /// <summary>对 Timeline 编辑器内部接口的最小反射封装（仅用于读写播放头时间）。</summary>
    public static class TimelineEditorBridge
    {
        static readonly PropertyInfo s_InspectedSequenceTime =
            typeof(UnityEditor.Timeline.TimelineEditor).GetProperty("inspectedSequenceTime", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);

        /// <summary>当前 Timeline 播放头时间（秒）。</summary>
        public static double GetPlayhead()
        {
            if (s_InspectedSequenceTime != null)
            {
                try
                {
                    var value = s_InspectedSequenceTime.GetValue(null);
                    if (value is double d)
                        return d;
                }
                catch
                {
                    // 忽略：Timeline 窗口未打开时内部状态不可用。
                }
            }

            var director = TimelineEditor.inspectedDirector;
            return director != null ? director.time : 0d;
        }

        /// <summary>移动播放头。</summary>
        public static bool TrySetPlayhead(double time)
        {
            if (s_InspectedSequenceTime == null)
                return false;

            try
            {
                s_InspectedSequenceTime.SetValue(null, time);
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>通知 Timeline 窗口刷新（曲线视图会同步到新的曲线数据）。</summary>
        public static void Refresh(bool contentsAddedOrRemoved = false)
        {
            if (contentsAddedOrRemoved)
            {
                UnityEditor.Timeline.TimelineEditor.Refresh(RefreshReason.ContentsAddedOrRemoved);
            }
            else
            {
                UnityEditor.Timeline.TimelineEditor.Refresh(
                    RefreshReason.ContentsModified | RefreshReason.SceneNeedsUpdate | RefreshReason.WindowNeedsRedraw);
            }
        }
    }
}
