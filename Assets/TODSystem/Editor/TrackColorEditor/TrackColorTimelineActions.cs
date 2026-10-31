using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.ShortcutManagement;
using UnityEditor.Timeline.Actions;
using UnityEngine;
using UnityEngine.Timeline;

namespace TimelineColorEditor
{
    /// <summary>Timeline Action 的公共辅助方法。</summary>
    static class TimelineColorActionHelpers
    {
        public static List<ColorCurveProperty> GetTrackProperties(IEnumerable<TrackAsset> tracks)
        {
            var result = new List<ColorCurveProperty>();
            if (tracks == null)
                return result;

            foreach (var track in tracks)
            {
                if (track == null || track is AnimationTrack)
                    continue;

                var target = new ColorCurveTarget { kind = ColorTargetKind.Track, track = track };
                result.AddRange(ColorCurveTargets.GetColorProperties(target));
            }

            return result;
        }

        public static List<ColorCurveProperty> GetClipProperties(IEnumerable<TimelineClip> clips)
        {
            var result = new List<ColorCurveProperty>();
            if (clips == null)
                return result;

            foreach (var clip in clips)
            {
                if (clip == null || clip.asset == null)
                    continue;

                var target = new ColorCurveTarget { kind = ColorTargetKind.Clip, clip = clip, track = clip.GetParentTrack() };
                result.AddRange(ColorCurveTargets.GetColorProperties(target));
            }

            return result;
        }

        /// <summary>弹窗锚点：优先使用鼠标位置。</summary>
        public static Rect GetActivatorRect(Vector2 fallback)
        {
            var evt = Event.current;
            if (evt != null)
                return new Rect(GUIUtility.GUIToScreenPoint(evt.mousePosition), Vector2.zero);

            return new Rect(fallback.x, fallback.y, 0f, 0f);
        }
    }

    /// <summary>轨道右键菜单：弹出调色板，统一编辑该轨道的颜色曲线。</summary>
    [MenuEntry("轨道颜色/调色板编辑颜色曲线...", MenuPriority.CustomTrackActionSection.customTrackAction)]
    public class OpenTrackColorPaletteAction : TrackAction
    {
        public override ActionValidity Validate(IEnumerable<TrackAsset> tracks)
        {
            return TimelineColorActionHelpers.GetTrackProperties(tracks).Count > 0
                ? ActionValidity.Valid
                : ActionValidity.NotApplicable;
        }

        public override bool Execute(IEnumerable<TrackAsset> tracks)
        {
            var properties = TimelineColorActionHelpers.GetTrackProperties(tracks);
            if (properties.Count == 0)
                return false;

            TrackColorPalettePopup.Show(TimelineColorActionHelpers.GetActivatorRect(new Vector2(320f, 220f)), properties);
            return true;
        }

        [TimelineShortcut("Track Color Palette", KeyCode.K, ShortcutModifiers.Action | ShortcutModifiers.Shift)]
        public static void TriggerShortcut()
        {
            var properties = TimelineColorActionHelpers.GetTrackProperties(Selection.objects.OfType<TrackAsset>());
            if (properties.Count == 0)
                return;

            TrackColorPalettePopup.Show(TimelineColorActionHelpers.GetActivatorRect(new Vector2(320f, 220f)), properties);
        }
    }

    /// <summary>轨道右键菜单：打开可停靠的颜色曲线编辑窗口。</summary>
    [MenuEntry("轨道颜色/打开颜色曲线编辑器窗口", MenuPriority.CustomTrackActionSection.customTrackAction + 10)]
    public class OpenTrackColorEditorAction : TrackAction
    {
        public override ActionValidity Validate(IEnumerable<TrackAsset> tracks)
        {
            return TimelineColorActionHelpers.GetTrackProperties(tracks).Count > 0
                ? ActionValidity.Valid
                : ActionValidity.NotApplicable;
        }

        public override bool Execute(IEnumerable<TrackAsset> tracks)
        {
            var properties = TimelineColorActionHelpers.GetTrackProperties(tracks);
            var track = tracks != null ? tracks.FirstOrDefault() : null;
            if (track != null)
                Selection.objects = new Object[] { track };

            TrackColorCurveWindow.OpenFor(properties.Count > 0 ? properties[0] : null);
            return true;
        }
    }

    /// <summary>片段右键菜单：弹出调色板，统一编辑该片段资源的颜色曲线（Clip Properties）。</summary>
    [MenuEntry("轨道颜色/调色板编辑片段颜色曲线...", MenuPriority.CustomClipActionSection.customClipAction)]
    public class OpenClipColorPaletteAction : ClipAction
    {
        public override ActionValidity Validate(IEnumerable<TimelineClip> clips)
        {
            return TimelineColorActionHelpers.GetClipProperties(clips).Count > 0
                ? ActionValidity.Valid
                : ActionValidity.NotApplicable;
        }

        public override bool Execute(IEnumerable<TimelineClip> clips)
        {
            var properties = TimelineColorActionHelpers.GetClipProperties(clips);
            if (properties.Count == 0)
                return false;

            TrackColorPalettePopup.Show(TimelineColorActionHelpers.GetActivatorRect(new Vector2(320f, 220f)), properties);
            return true;
        }
    }
}
