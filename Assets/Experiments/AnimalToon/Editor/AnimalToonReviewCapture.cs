using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace WeatherHouse.AnimalToon.Editor
{
    [InitializeOnLoad]
    public static class AnimalToonReviewCapture
    {
        static AnimalToonLab lab;
        static RenderTexture target;
        static int step = -1, frame;
        static Color32[] off;
        static float rightX, leftX;
        static int litPixels, shadowPixels, darkPixels;
        static Vector3[] firstPose;
        const int Width = 1920, Height = 1080;
        static readonly string[] Shots = { "01-comparison-right", "02-comparison-left", "03-comparison-shadow",
            "04-walk-pose", "05-right-no-edge", "06-right-edge", "07-left-no-edge", "08-left-edge",
            "09-shadow-no-edge", "10-shadow-edge", "11-dark-no-edge", "12-dark-edge",
            "13-occluded-no-edge", "14-occluded-edge", "15-user-no-merge", "16-user-merged",
            "17-user-wide-edge", "18-user-max-edge", "19-wide-walk", "20-perspective" };

        static AnimalToonReviewCapture()
        {
            EditorApplication.playModeStateChanged += Changed;
            EditorApplication.update += Tick;
        }

        static void Changed(PlayModeStateChange change)
        {
            if (!SessionState.GetBool("AnimalToonReview.Run", false)) return;
            if (change == PlayModeStateChange.EnteredPlayMode)
            {
                try
                {
                    lab = UnityEngine.Object.FindObjectOfType<AnimalToonLab>();
                    if (!lab) throw new InvalidOperationException("Missing comparison lab.");
                    lab.animate = false; lab.showControls = false;
                    target = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
                    target.Create(); lab.view.targetTexture = target;
                    File.WriteAllText("../checks.txt", "Actual Unity Play + URP 14.0.12, 1920x1080, MSAA4, same model/texture/animation/camera.\n");
                    SetStep(0);
                }
                catch (Exception e) { Fail(e); }
            }
            if (change == PlayModeStateChange.EnteredEditMode)
            {
                bool passed = SessionState.GetBool("AnimalToonReview.Passed", false);
                File.AppendAllText("../checks.txt", "HANDOFF Play=False; dirty=" + UnityEngine.SceneManagement.SceneManager.GetActiveScene().isDirty + "\n");
                SessionState.SetBool("AnimalToonReview.Run", false);
                EditorApplication.Exit(passed ? 0 : 1);
            }
        }

        static void SetStep(int value)
        {
            step = value; frame = Time.frameCount;
            lab.focus = step < 4 ? -1 : 2;
            lab.animationTime = step == 3 ? lab.walk.length * .37f : 0;
            lab.lightAngle = step == 1 || step == 6 || step == 7 ? -70 : 70;
            lab.edgeWidth = step >= 4 && step % 2 == 0 ? 0 : 8;
            lab.inShadow = step == 2 || step == 8 || step == 9;
            lab.showForeground = step == 12 || step == 13;
            lab.key.intensity = step == 10 || step == 11 ? 0 : 1.1f;
            lab.mergeRadius = 12;
            if (step >= 14)
            {
                lab.lightAngle = 125.88f;
                lab.toonStrength = 1; lab.softness = .01f;
                lab.edgeWidth = step < 16 ? 3.83f : step == 16 ? 8 : 16;
                lab.mergeRadius = step == 14 ? 0 : 12;
                if (step == 18) lab.animationTime = lab.walk.length * .37f;
                if (step == 19) { lab.view.orthographic = false; lab.view.fieldOfView = 16; }
            }
            lab.Apply();
        }

        static void Tick()
        {
            if (!SessionState.GetBool("AnimalToonReview.Run", false) || !EditorApplication.isPlaying || !lab || step < 0) return;
            if (Time.frameCount - frame < 12) return;
            try
            {
                var pixels = Capture(Shots[step]);
                if (step == 0 || step == 3)
                    File.AppendAllText("../checks.txt", "POSE time=" + lab.animationTime + "; Shin.R=" + lab.animals[2].GetComponentsInChildren<Transform>().First(t => t.name == "Shin.R").localRotation + "\n");
                if (step == 0) firstPose = Pose();
                if (step == 3)
                {
                    var pose = Pose();
                    float movement = pose.Zip(firstPose, (a, b) => Vector3.Distance(a, b)).Max();
                    Require(movement > .01f, "Walk deforms skinned vertices", movement.ToString("F5") + " m");
                }
                if (step >= 4 && step <= 14 && step % 2 == 0) off = pixels;
                if (step == 5)
                {
                    litPixels = Difference(off, pixels, out rightX);
                    Require(litPixels > 100, "Enabled outline changes actual rendered pixels", litPixels.ToString());
                    int interior = 0;
                    for (int y = 500; y < 580; y++) for (int x = 910; x < 980; x++)
                        if (Delta(off[y * Width + x], pixels[y * Width + x]) > 12) interior++;
                    Require(interior == 0, "Outline leaves body interior unchanged", interior.ToString());
                }
                if (step == 7)
                {
                    int count = Difference(off, pixels, out leftX);
                    Require(count > 100 && rightX > leftX + 30, "Edge follows light side", "right centroid=" + rightX.ToString("F1") + "; left=" + leftX.ToString("F1"));
                }
                if (step == 9)
                {
                    shadowPixels = Difference(off, pixels, out _);
                    Require(shadowPixels < litPixels * .2f, "Real cast shadow suppresses edge", shadowPixels + " / " + litPixels);
                }
                if (step == 11)
                {
                    darkPixels = Difference(off, pixels, out _);
                    Require(darkPixels < 10, "Light off has no luminous outline", darkPixels.ToString());
                }
                if (step == 13)
                {
                    Vector3 bottom = lab.view.WorldToViewportPoint(new Vector3(-.2f, .3f, -1.03f));
                    Vector3 top = lab.view.WorldToViewportPoint(new Vector3(.2f, 1.1f, -1.03f));
                    int leak = 0;
                    for (int y = Mathf.CeilToInt(bottom.y * Height); y < (int)(top.y * Height); y++)
                        for (int x = Mathf.CeilToInt(bottom.x * Width); x < (int)(top.x * Width); x++)
                            if (Delta(off[y * Width + x], pixels[y * Width + x]) > 12) leak++;
                    Require(leak == 0, "Foreground depth test blocks edge", leak.ToString());
                    var shader = Shader.Find("Weather House/Animals/Paper Toon");
                    Require(!ShaderUtil.ShaderHasError(shader), "Shader compilation", "no shader errors");
                    Require(!ShaderUtil.ShaderHasError(Shader.Find("Hidden/Weather House/Animal Paper Screen")), "Screen shader compilation", "no shader errors");
                }
                if (step == 15)
                    Require(Difference(off, pixels, out _) > 100, "Merge changes actual sheep shading", Difference(off, pixels, out _).ToString());
                if (step == Shots.Length - 1)
                {
                    AnimalPaperFilterChecks.Run();
                    File.AppendAllText("../checks.txt", "PASS all checks. Visual acceptance remains user review.\n");
                    SessionState.SetBool("AnimalToonReview.Passed", true);
                    Finish(); return;
                }
                SetStep(step + 1);
            }
            catch (Exception e) { Fail(e); }
        }

        static Vector3[] Pose()
        {
            var skin = lab.animals[2].GetComponentInChildren<SkinnedMeshRenderer>();
            var mesh = new Mesh(); skin.BakeMesh(mesh, true);
            var points = mesh.vertices.Select(p => skin.transform.TransformPoint(p)).ToArray();
            UnityEngine.Object.Destroy(mesh); return points;
        }

        static Color32[] Capture(string name)
        {
            lab.view.Render();
            RenderTexture old = RenderTexture.active;
            var texture = new Texture2D(Width, Height, TextureFormat.RGB24, false);
            try
            {
                RenderTexture.active = target;
                texture.ReadPixels(new Rect(0, 0, Width, Height), 0, 0); texture.Apply();
                File.WriteAllBytes("../captures/" + name + ".png", texture.EncodeToPNG());
                return texture.GetPixels32();
            }
            finally { RenderTexture.active = old; UnityEngine.Object.DestroyImmediate(texture); }
        }

        static int Delta(Color32 a, Color32 b) => Math.Abs(a.r - b.r) + Math.Abs(a.g - b.g) + Math.Abs(a.b - b.b);
        static int Difference(Color32[] a, Color32[] b, out float centroid)
        {
            int count = 0; double sum = 0;
            for (int i = 0; i < a.Length; i++) if (Delta(a[i], b[i]) > 12) { count++; sum += i % Width; }
            centroid = count == 0 ? Width * .5f : (float)(sum / count);
            return count;
        }
        static void Require(bool condition, string label, string value)
        {
            File.AppendAllText("../checks.txt", (condition ? "PASS " : "FAIL ") + label + ": " + value + "\n");
            if (!condition) throw new InvalidOperationException(label + ": " + value);
        }
        static void Fail(Exception e)
        {
            File.AppendAllText("../checks.txt", "FAIL " + e + "\n");
            SessionState.SetBool("AnimalToonReview.Passed", false);
            Debug.LogException(e); Finish();
        }
        static void Finish()
        {
            step = -1;
            if (lab) lab.view.targetTexture = null;
            if (target) { target.Release(); UnityEngine.Object.DestroyImmediate(target); }
            EditorApplication.isPlaying = false;
        }
    }
}
