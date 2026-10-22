using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace WeatherHouseSquirrelLab.Editor
{
    public static class SquirrelExpressiveChecks
    {
        [Serializable] private sealed class Entry
        {
            public string id;
            public float repeatPositionError, repeatRotationError, rawPositionError, rawRotationError, rawScaleError;
            public bool passed;
        }
        [Serializable] private sealed class Report { public bool actualPlay, passed; public Entry[] actors; }

        public static void Run()
        {
            if (!EditorApplication.isPlaying) throw new InvalidOperationException("Runtime checks require Play.");
            var gallery = UnityEngine.Object.FindObjectOfType<SquirrelMotionGallery>();
            bool wasPaused = gallery.paused;
            gallery.SetPaused(true);
            var report = new Report { actualPlay = true, actors = new Entry[gallery.actors.Length] };
            try
            {
                for (int a = 0; a < gallery.actors.Length; a++)
                {
                    var actor = gallery.actors[a];
                    if (actor.expressiveClip == null) throw new InvalidOperationException("Missing enhanced clip: " + actor.id);
                    var bones = actor.animator.GetComponentsInChildren<Transform>(true).Where(t => t.name != "TailRoot" && !t.name.StartsWith("Tail0")).ToArray();
                    var entry = new Entry { id = actor.id };
                    report.actors[a] = entry;
                    gallery.SetExpressiveMotion(true);
                    actor.animator.Play("Preview", 0, .37f);
                    actor.animator.Update(0f);
                    var positions = bones.Select(t => t.localPosition).ToArray();
                    var rotations = bones.Select(t => t.localRotation).ToArray();
                    actor.animator.Play("Preview", 0, 10.37f);
                    actor.animator.Update(0f);
                    for (int b = 0; b < bones.Length; b++)
                    {
                        entry.repeatPositionError = Mathf.Max(entry.repeatPositionError, Vector3.Distance(positions[b], bones[b].localPosition));
                        entry.repeatRotationError = Mathf.Max(entry.repeatRotationError, Quaternion.Angle(rotations[b], bones[b].localRotation));
                    }
                    gallery.SetExpressiveMotion(false);
                    actor.animator.Play("Preview", 0, .37f);
                    actor.animator.Update(0f);
                    positions = bones.Select(t => t.localPosition).ToArray();
                    rotations = bones.Select(t => t.localRotation).ToArray();
                    var scales = bones.Select(t => t.localScale).ToArray();
                    actor.clip.SampleAnimation(actor.animator.gameObject, actor.clip.length * .37f);
                    for (int b = 0; b < bones.Length; b++)
                    {
                        entry.rawPositionError = Mathf.Max(entry.rawPositionError, Vector3.Distance(positions[b], bones[b].localPosition));
                        entry.rawRotationError = Mathf.Max(entry.rawRotationError, Quaternion.Angle(rotations[b], bones[b].localRotation));
                        entry.rawScaleError = Mathf.Max(entry.rawScaleError, Vector3.Distance(scales[b], bones[b].localScale));
                    }
                    entry.passed = entry.repeatPositionError < .001f && entry.repeatRotationError < .1f && entry.rawPositionError < .001f && entry.rawRotationError < .1f && entry.rawScaleError < .001f;
                }
                report.passed = report.actors.All(entry => entry.passed);
                File.WriteAllText("SquirrelLabReview/expressive_runtime_checks.json", JsonUtility.ToJson(report, true));
                if (!report.passed) throw new InvalidOperationException("Enhanced runtime checks failed; inspect expressive_runtime_checks.json");
            }
            finally
            {
                gallery.SetExpressiveMotion(true);
                gallery.Restart();
                gallery.SetPaused(wasPaused);
            }
        }
    }
}
