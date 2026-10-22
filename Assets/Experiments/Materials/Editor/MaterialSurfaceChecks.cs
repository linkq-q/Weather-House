using System;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace WeatherHouse.MaterialLab.Editor
{
    /// <summary>Called by the existing Play runner after its real-frame warmup.</summary>
    public static class MaterialSurfaceChecks
    {
        const string Output = "VegetationLabReview/MaterialLab/SurfaceRevision";
        const int Width = 1440, Height = 900;
        static readonly string[] NewProperties = {
            "_StrokeColor", "_StrokeDensity", "_ShadowEdgeColor", "_ShadowEdgeWidth", "_ShadowEdgeStrength", "_ShadowFill"
        };
        static readonly Rect ShadowROI = new Rect(.405f, .34f, .07f, .11f);
        static readonly Rect LightROI = new Rect(.525f, .57f, .07f, .11f);

        public static void Verify(MaterialLabControls controls, bool baseline, StringBuilder report)
        {
            Require(Application.isPlaying && controls && controls.viewCamera && controls.post && controls.keyLight,
                "需要真实 Play、实验相机、后处理设置和主光。");
            Require(controls.paintedSamples != null && controls.paintedSamples.Length > 5 && controls.paintedSamples[5],
                "需要第六个青绿色厚涂球。");
            Require(report != null, "需要外层报告缓冲。");
            Directory.CreateDirectory(Output);

            Camera camera = controls.viewCamera;
            OilPaintPostSettings post = controls.post;
            Light light = controls.keyLight;
            int savedSelected = controls.selected, savedColor = controls.selectedColor;
            bool savedEnabled = controls.enabled, savedPanel = controls.showPanel, savedRotate = controls.rotateSamples;
            float savedYaw = controls.yaw, savedPitch = controls.pitch, savedDistance = controls.distance;
            Vector3 savedTarget = controls.target, savedCameraPosition = camera.transform.position;
            Quaternion savedCameraRotation = camera.transform.rotation;
            string savedPost = JsonUtility.ToJson(post);
            bool savedPostEnabled = post.enabled;
            LightShadows savedShadows = light.shadows;
            Material material = null, savedMaterial = null;
            try
            {
                controls.enabled = false; controls.showPanel = false; controls.rotateSamples = false;
                controls.selected = 5;
                controls.FocusSelected();
                controls.SetPostMode(0); post.enabled = false;
                material = controls.SelectedMaterial;
                Require(material && !EditorUtility.IsPersistent(material) && controls.paintedSamples[5].sharedMaterial == material,
                    "必须使用青绿厚涂球的 Play 材质实例。");
                savedMaterial = new Material(material) { hideFlags = HideFlags.HideAndDontSave };
                report.AppendLine("Surface " + (baseline ? "baseline" : "revision") + ": frame=" + Time.frameCount +
                    "; selected=5; FocusSelected; post disabled; 1440x900 Camera.Render; same-frame parameter A/B.");
                LogSH(report);

                using (Shot close = Capture(baseline ? "Baseline_Close" : "After_Close", camera))
                {
                    LogROI(report, baseline ? "Baseline close" : "After close", close);
                    report.AppendLine("Camera signature: " + close.signature);
                    string cameraPath = Output + (baseline ? "/Baseline_Camera.txt" : "/After_Camera.txt");
                    File.WriteAllText(cameraPath, close.signature);
                    if (!baseline && File.Exists(Output + "/Baseline_Camera.txt"))
                    {
                        bool match = File.ReadAllText(Output + "/Baseline_Camera.txt") == close.signature;
                        report.AppendLine("Baseline/revision camera signature equal=" + match);
                        Require(match, "前后对比必须使用同一相机位置、投影与输出尺寸。");
                    }

                    if (baseline)
                    {
                        var missing = new StringBuilder();
                        foreach (string property in NewProperties)
                            if (!material.HasProperty(property)) missing.Append(property).Append(' ');
                        report.AppendLine(missing.Length > 0 ? "Expected RED before shader upgrade: missing " + missing
                            : "Baseline already exposes all six new properties; capture retained as current-state evidence.");
                        light.shadows = LightShadows.None;
                        using (Shot unshadowed = Capture("Baseline_NoShadows", camera))
                        {
                            LogDifference(report, "Diagnostic light shadows disabled", close, unshadowed, false);
                            LogROI(report, "Baseline no shadows", unshadowed);
                        }
                        light.shadows = savedShadows;
                        material.SetFloat("_Impasto", 0);
                        using (Shot noRelief = Capture("Baseline_NoImpasto", camera))
                        {
                            LogDifference(report, "Diagnostic Impasto=0", close, noRelief, false);
                            LogROI(report, "Baseline no impasto", noRelief);
                        }
                        return;
                    }

                    CheckPropertyContract(material);
                    report.AppendLine("PASS six new shader properties, color types and declared numeric ranges.");

                    Color baseBefore = material.GetColor("_BaseColor");
                    controls.selectedColor = 1;
                    float testHue = ColorDifference(material.GetColor("_StrokeColor"), Color.HSVToRGB(.93f, .82f, .87f)) < .1f ? .43f : .93f;
                    controls.SetSelectedColorHSV(testHue, .82f, .87f);
                    Require(ColorDifference(material.GetColor("_BaseColor"), baseBefore) < .000001f,
                        "改变笔触 HSV 不得改变底色。");
                    Require(ColorDifference(material.GetColor("_StrokeColor"), savedMaterial.GetColor("_StrokeColor")) > .01f,
                        "实际 UI 入口必须写入独立笔触颜色。");
                    using (Shot recolored = Capture("StrokeColor_HSV", camera))
                        LogDifference(report, "UI selectedColor=1 / SetSelectedColorHSV; BaseColor unchanged", close, recolored);

                    material.CopyPropertiesFromMaterial(savedMaterial);
                    controls.ApplyWideBrushPreset();
                    Require(material.GetFloat("_StrokeAspect") < 1, "宽笔触入口需要宽于长的笔触。");
                    float wideScale = material.GetFloat("_BrushScale");
                    using (Shot wide = Capture("Preset_Wide", camera))
                        LogDifference(report, "UI ApplyWideBrushPreset", close, wide);

                    material.CopyPropertiesFromMaterial(savedMaterial);
                    controls.ApplyLargeDensePreset();
                    Require(material.GetFloat("_BrushScale") < wideScale &&
                        material.GetFloat("_StrokeDensity") > 1, "大且密入口需要更大的笔触和更密的覆盖。");
                    using (Shot large = Capture("Preset_LargeDense", camera))
                        LogDifference(report, "UI ApplyLargeDensePreset", close, large);

                    material.CopyPropertiesFromMaterial(savedMaterial);
                    material.SetFloat("_BrushScale", 2.5f);
                    CompareFloat(material, camera, report, "Density_FixedSize", "_StrokeDensity", .35f, 1.8f);
                    Require(Mathf.Approximately(material.GetFloat("_BrushScale"), 2.5f),
                        "密度对照期间笔触尺寸必须保持不变。");

                    material.CopyPropertiesFromMaterial(savedMaterial);
                    material.SetFloat("_StrokeDensity", 0);
                    using (Shot zeroDensity = Capture("Density_Zero", camera))
                    {
                        LogDifference(report, "Density=0 removes stroke coverage", close, zeroDensity);
                        material.CopyPropertiesFromMaterial(savedMaterial);
                        material.SetFloat("_BrushStrength", 0);
                        using (Shot zeroStrength = Capture("Strength_Zero", camera))
                        {
                            LogDifference(report, "BrushStrength=0 removes stroke pigment", close, zeroStrength);
                            LogDifference(report, "Density=0 versus strength=0 (relief may differ; no equality requirement)",
                                zeroDensity, zeroStrength, false);
                        }
                    }

                    material.CopyPropertiesFromMaterial(savedMaterial);
                    CompareFloat(material, camera, report, "Aspect", "_StrokeAspect", .25f, 4);

                    material.CopyPropertiesFromMaterial(savedMaterial);
                    material.SetColor("_ShadowEdgeColor", new Color(.12f, .62f, .78f, 1));
                    CompareFloat(material, camera, report, "Edge_Strength", "_ShadowEdgeStrength", 0, 1);

                    material.CopyPropertiesFromMaterial(savedMaterial);
                    material.SetColor("_ShadowEdgeColor", new Color(.12f, .62f, .78f, 1));
                    material.SetFloat("_ShadowEdgeStrength", 1);
                    CompareFloat(material, camera, report, "Edge_Width", "_ShadowEdgeWidth", .04f, .35f);

                    material.CopyPropertiesFromMaterial(savedMaterial);
                    CompareFloat(material, camera, report, "Shadow_Fill", "_ShadowFill", 0, .4f, true);

                    // Compare reset with the persistent preset, not with another invocation of ResetSelected.
                    Material preset = AssetDatabase.LoadAssetAtPath<Material>(
                        "Assets/Experiments/Materials/Generated/Materials/Oil_1_3.mat");
                    Require(preset && preset.shader == material.shader, "需要青绿厚涂的持久预设作 reset 对照。");
                    material.SetFloat("_StrokeDensity", .17f);
                    material.SetFloat("_ShadowEdgeWidth", .37f);
                    material.SetFloat("_ShadowEdgeStrength", .91f);
                    material.SetFloat("_ShadowFill", .73f);
                    controls.selectedColor = 1; controls.SetSelectedColorHSV(.93f, .82f, .87f);
                    material.SetColor("_ShadowEdgeColor", Color.magenta);
                    controls.ResetSelected();
                    foreach (string property in new[] { "_BaseColor", "_StrokeColor", "_ShadowColor", "_HighlightColor", "_ShadowEdgeColor" })
                        Require(ColorDifference(material.GetColor(property), preset.GetColor(property)) < .000001f,
                            "ResetSelected 未恢复颜色 " + property);
                    foreach (string property in new[] { "_StrokeDensity", "_ShadowEdgeWidth", "_ShadowEdgeStrength", "_ShadowFill", "_BrushScale", "_StrokeAspect", "_BrushStrength", "_Impasto" })
                        Require(Mathf.Abs(material.GetFloat(property) - preset.GetFloat(property)) < .000001f,
                            "ResetSelected 未恢复参数 " + property);
                    using (Shot reset = Capture("Reset_Preset", camera)) LogROI(report, "Reset preset", reset);
                    report.AppendLine("PASS UI reset restored all five pigment colors and new parameters to the persistent preset.");
                }
            }
            finally
            {
                if (material && savedMaterial) material.CopyPropertiesFromMaterial(savedMaterial);
                if (savedMaterial) Object.DestroyImmediate(savedMaterial);
                if (light) light.shadows = savedShadows;
                if (post) { JsonUtility.FromJsonOverwrite(savedPost, post); post.enabled = savedPostEnabled; }
                if (controls)
                {
                    controls.selected = savedSelected; controls.selectedColor = savedColor;
                    controls.showPanel = savedPanel; controls.rotateSamples = savedRotate;
                    controls.yaw = savedYaw; controls.pitch = savedPitch; controls.distance = savedDistance; controls.target = savedTarget;
                    controls.enabled = savedEnabled;
                }
                if (camera) camera.transform.SetPositionAndRotation(savedCameraPosition, savedCameraRotation);
            }
        }

        static void CheckPropertyContract(Material material)
        {
            foreach (string property in NewProperties) Require(material.HasProperty(property), "缺少 shader 属性 " + property);
            Shader shader = material.shader;
            Require(shader.GetPropertyType(shader.FindPropertyIndex("_StrokeColor")) == ShaderPropertyType.Color &&
                shader.GetPropertyType(shader.FindPropertyIndex("_ShadowEdgeColor")) == ShaderPropertyType.Color,
                "笔触色和交界色必须是 Color 属性。");
            Range(shader, "_StrokeDensity", 0, 2);
            Range(shader, "_ShadowEdgeWidth", .02f, .4f);
            Range(shader, "_ShadowEdgeStrength", 0, 1);
            Range(shader, "_ShadowFill", 0, 1);
        }

        static void Range(Shader shader, string property, float minimum, float maximum)
        {
            int index = shader.FindPropertyIndex(property);
            Require(index >= 0 && shader.GetPropertyType(index) == ShaderPropertyType.Range, property + " 必须有可编辑范围。");
            Vector2 limits = shader.GetPropertyRangeLimits(index);
            Require(Mathf.Abs(limits.x - minimum) < .000001f && Mathf.Abs(limits.y - maximum) < .000001f,
                property + " 范围不符：" + limits);
        }

        static void CompareFloat(Material material, Camera camera, StringBuilder report, string name,
            string property, float first, float second, bool logROI = false)
        {
            material.SetFloat(property, first);
            using (Shot a = Capture(name + "_A", camera))
            {
                material.SetFloat(property, second);
                using (Shot b = Capture(name + "_B", camera))
                {
                    LogDifference(report, property + " " + first.ToString("G", CultureInfo.InvariantCulture) + " -> " +
                        second.ToString("G", CultureInfo.InvariantCulture), a, b);
                    if (logROI) { LogROI(report, name + " A", a); LogROI(report, name + " B", b); }
                }
            }
        }

        static void LogDifference(StringBuilder report, string label, Shot a, Shot b, bool requireEffect = true)
        {
            Require(a.signature == b.signature, label + " 的 A/B 相机签名不一致。");
            Color32[] pa = a.Pixels, pb = b.Pixels;
            int changed = 0; double total = 0;
            for (int i = 0; i < pa.Length; i++)
            {
                int difference = Math.Abs(pa[i].r - pb[i].r) + Math.Abs(pa[i].g - pb[i].g) + Math.Abs(pa[i].b - pb[i].b);
                if (difference >= 6) changed++;
                total += difference;
            }
            report.AppendLine(label + ": changed RGBsum>=6 pixels=" + changed +
                "; meanRGBDelta=" + (total / (pa.Length * 765.0)).ToString("F8", CultureInfo.InvariantCulture));
            if (requireEffect) Require(changed > 50, label + " 必须改变超过 50 个实际 GPU 像素（RGBsum>=6）。");
        }

        static void LogROI(StringBuilder report, string name, Shot shot)
        {
            report.AppendLine(name + ": lower-left mean=" + MeanROI(shot, ShadowROI).ToString("F6", CultureInfo.InvariantCulture) +
                "; upper-right mean=" + MeanROI(shot, LightROI).ToString("F6", CultureInfo.InvariantCulture) +
                "; display RGB weighted values, not scene-linear illuminance. ROIs bottom-left normalized: " + ShadowROI + " / " + LightROI +
                ". Recorded for visual comparison; no aesthetic threshold.");
        }

        static double MeanROI(Shot shot, Rect rect)
        {
            Color32[] pixels = shot.Pixels;
            double sum = 0; int count = 0;
            for (int y = (int)(rect.yMin * Height); y < (int)(rect.yMax * Height); y++)
                for (int x = (int)(rect.xMin * Width); x < (int)(rect.xMax * Width); x++)
                {
                    Color32 p = pixels[y * Width + x];
                    sum += (.2126 * p.r + .7152 * p.g + .0722 * p.b) / 255.0; count++;
                }
            return sum / Math.Max(1, count);
        }

        static void LogSH(StringBuilder report)
        {
            var directions = new[] { Vector3.up, Vector3.down, Vector3.left, Vector3.right };
            var colors = new Color[4];
            RenderSettings.ambientProbe.Evaluate(directions, colors);
            report.AppendLine("Ambient SH evaluated up/down/left/right=" + Format(colors[0]) + " / " + Format(colors[1]) +
                " / " + Format(colors[2]) + " / " + Format(colors[3]) + "; ambientMode=" + RenderSettings.ambientMode);
        }

        static string Format(Color color) => string.Format(CultureInfo.InvariantCulture,
            "({0:F6},{1:F6},{2:F6})", color.r, color.g, color.b);

        static float ColorDifference(Color a, Color b) =>
            Mathf.Max(Mathf.Abs(a.r-b.r), Mathf.Abs(a.g-b.g), Mathf.Abs(a.b-b.b), Mathf.Abs(a.a-b.a));

        static Shot Capture(string name, Camera camera)
        {
            var target = RenderTexture.GetTemporary(Width, Height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default, 1);
            RenderTexture oldTarget = camera.targetTexture, oldActive = RenderTexture.active;
            var texture = new Texture2D(Width, Height, TextureFormat.RGB24, false);
            try
            {
                camera.targetTexture = target;
                camera.Render();
                string signature = CameraSignature(camera);
                RenderTexture.active = target;
                texture.ReadPixels(new Rect(0, 0, Width, Height), 0, 0); texture.Apply();
                File.WriteAllBytes(Output + "/" + name + ".png", texture.EncodeToPNG());
                return new Shot(texture, signature);
            }
            catch { Object.DestroyImmediate(texture); throw; }
            finally
            {
                camera.targetTexture = oldTarget; RenderTexture.active = oldActive;
                RenderTexture.ReleaseTemporary(target);
            }
        }

        static string CameraSignature(Camera camera)
        {
            var text = new StringBuilder("1440x900|");
            Vector3 p = camera.transform.position; Quaternion q = camera.transform.rotation;
            foreach (float value in new[] { p.x,p.y,p.z,q.x,q.y,q.z,q.w,camera.fieldOfView,camera.nearClipPlane,camera.farClipPlane })
                text.Append(value.ToString("F6", CultureInfo.InvariantCulture)).Append('|');
            Matrix4x4 projection = camera.projectionMatrix;
            for (int i = 0; i < 16; i++) text.Append(projection[i].ToString("F6", CultureInfo.InvariantCulture)).Append('|');
            return text.ToString();
        }

        sealed class Shot : IDisposable
        {
            readonly Texture2D texture;
            Color32[] pixels;
            public readonly string signature;
            public Color32[] Pixels => pixels ?? (pixels = texture.GetPixels32());
            public Shot(Texture2D image, string cameraSignature) { texture = image; signature = cameraSignature; }
            public void Dispose() { if (texture) Object.DestroyImmediate(texture); }
        }

        static void Require(bool value, string message)
        {
            if (!value) throw new InvalidOperationException("MaterialSurfaceChecks: " + message);
        }
    }
}
