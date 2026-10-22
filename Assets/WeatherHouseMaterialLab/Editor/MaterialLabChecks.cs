using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace WeatherHouse.MaterialLab.Editor
{
    [InitializeOnLoad]
    public static class MaterialLabChecks
    {
        const string Output = "VegetationLabReview/MaterialLab";
        const string Key = "WeatherHouse.MaterialLab.Checks";
        static IEnumerator steps;
        static double started, nextPoll;
        static int previousFrame;
        static string errors;
        static readonly StringBuilder report = new StringBuilder();
        static MaterialLabChecks()
        {
            EditorApplication.update += Tick;
            EditorApplication.playModeStateChanged += StateChanged;
        }
        static void Tick()
        {
            if (steps != null)
            {
                try
                {
                    EditorApplication.QueuePlayerLoopUpdate();
                    Require(EditorApplication.timeSinceStartup - started < 150, "Play 检查超过150秒。");
                    if (Time.frameCount == previousFrame) return;
                    previousFrame = Time.frameCount;
                    if (!steps.MoveNext()) Finish(null);
                }
                catch (Exception error) { Finish(error); }
                return;
            }
            if (EditorApplication.timeSinceStartup < nextPoll || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            nextPoll = EditorApplication.timeSinceStartup + 1;
            const string request = "VegetationLabReview/material-request.txt";
            if (!File.Exists(request)) return;
            string command = File.ReadAllText(request).Trim(); File.Delete(request);
            try
            {
                if (command == "verify") Begin();
                else if (command == "brush-verify")
                {
                    string previousScene = SceneManager.GetActiveScene().path;
                    MaterialLabBuilder.ApplyBrushRevision();
                    if (!string.IsNullOrEmpty(previousScene)) EditorSceneManager.OpenScene(previousScene);
                    Begin();
                }
                else if (command == "open") MaterialLabBuilder.OpenOrCreate();
                else throw new InvalidOperationException("Unknown material lab command: " + command);
            }
            catch (Exception error) { Finish(error); }
        }

        [MenuItem("Weather House/Material Lab/验证油画实验并截图")]
        public static void Begin()
        {
            Require(!EditorApplication.isPlayingOrWillChangePlaymode && SceneManager.sceneCount == 1 && !SceneManager.GetActiveScene().isDirty, "检查需要已保存的单场景且退出 Play。");
            Directory.CreateDirectory(Output);
            SessionState.SetString(Key + ".scene", SceneManager.GetActiveScene().path);
            SessionState.SetString(Key + ".quality", AssetDatabase.GetAssetPath(QualitySettings.renderPipeline));
            SessionState.SetBool(Key + ".background", Application.runInBackground);
            SessionState.SetBool(Key + ".paused", EditorApplication.isPaused);
            SessionState.SetString(Key + ".settings", HashSettings());
            SessionState.SetBool(Key + ".pending", true);
            File.WriteAllText(Output + "/result.txt", "RUNNING\n");
            try
            {
                MaterialLabBuilder.OpenOrCreate();
                foreach (string name in new[] { "Weather House/Oil Paint Surface", "Hidden/Weather House/Oil Paint Post" })
                {
                    var shader = Shader.Find(name);
                    Require(shader && shader.isSupported && !ShaderUtil.ShaderHasError(shader), name + " shader unsupported/error");
                }
                var controls = Object.FindObjectOfType<MaterialLabControls>();
                Require(controls && controls.paintedSamples.Length == 6 && controls.paintedSamples.Select(r => r.sharedMaterial).Distinct().Count() == 6, "六份独立油画材质。");
                string materialsBefore = string.Join("|", controls.paintedSamples.Select(r => EditorJsonUtility.ToJson(r.sharedMaterial)));
                SessionState.SetString(Key + ".materials", materialsBefore);
                File.WriteAllText(Output + "/materials-before.json", materialsBefore);
                SessionState.SetBool(Key + ".running", true);
                EditorApplication.isPlaying = true;
            }
            catch (Exception error) { Finish(error); }
        }

        static void StateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Key + ".running", false))
            {
                report.Clear(); report.AppendLine("Evidence: real Unity Play frames and camera GPU renders; scripted controller entrypoints, not manual keyboard testing.");
                errors = ""; started = EditorApplication.timeSinceStartup; previousFrame = Time.frameCount;
                Application.runInBackground = true; EditorApplication.isPaused = false;
                Application.logMessageReceived += Log; steps = Verify();
            }
            if (state == PlayModeStateChange.ExitingPlayMode && SessionState.GetBool(Key + ".running", false))
                Finish(new InvalidOperationException("Play was stopped before completion."));
            if (state == PlayModeStateChange.EnteredEditMode && SessionState.GetBool(Key + ".pending", false)) Restore();
        }

        static IEnumerator Verify()
        {
            var controls = Object.FindObjectOfType<MaterialLabControls>();
            var scope = Object.FindObjectOfType<MaterialLabPipelineScope>();
            var post = controls.post; Camera camera = controls.viewCamera;
            string originalPost = JsonUtility.ToJson(post);
            bool originalControls = controls.enabled;
            Texture2D baseline = null, oil = null, combined = null, pixel = null, bypass = null;
            try
            {
                controls.showPanel = false; controls.enabled = false;
                for (int i = 0; i < 12; i++) yield return null;
                Require(QualitySettings.renderPipeline == scope.pipeline, "专用管线已在 Play 生效。");
                Require(controls.SelectedMaterial != controls.paintedSamples[0].sharedMaterial || controls.SelectedMaterial.name.EndsWith("(Play)"), "Play 使用材质实例。");
                controls.SetPostMode(0); yield return null;
                baseline = Capture("01_Materials_Original", camera);
                controls.SetPostMode(1); yield return null;
                oil = Capture("02_Oil", camera);
                Require(Delta(baseline, oil) > .0001f, "油画开关必须改变实际GPU画面。");
                controls.SetPostMode(2); yield return null;
                combined = Capture("03_Oil_MicroPixel", camera);
                Require(Delta(oil, combined) > .00003f, "微像素层必须改变油画输出。");
                post.oilEnabled = false; yield return null;
                pixel = Capture("04_PixelOnly", camera);
                Require(Delta(baseline, pixel) > .00003f, "微像素可独立运行。");
                post.oilEnabled = true; post.pixelEnabled = true; post.oilStrength = 0; post.pixelStrength = 0; yield return null;
                bypass = Capture("05_ZeroStrength", camera);
                Require(Delta(baseline, bypass) < .00001f, "两项零强度须与原图一致。");
                report.AppendLine("PASS original/oil/combined/pixel-only/zero-strength, mean RGB changes: oil=" + Delta(baseline, oil).ToString("F6") + ", pixel-on-oil=" + Delta(oil, combined).ToString("F6") + ", zero=" + Delta(baseline, bypass).ToString("F6"));
                Require(Mean(combined) > Mean(baseline) * .8f && Mean(combined) < Mean(baseline) * 1.2f, "后处理应保持整体亮度。");
                JsonUtility.FromJsonOverwrite(originalPost, post);
                controls.SetPostMode(3); yield return null;
                using (var shot = new Picture(Capture("06_Split", camera)))
                {
                    Require(DeltaRegion(baseline, shot.texture, .05f, .42f) < .00001f, "分屏左侧必须保持原图。");
                    Require(DeltaRegion(baseline, shot.texture, .6f, .95f) > .0001f, "分屏右侧必须呈现处理结果。");
                }
                post.enabled = false; yield return null;
                using (var shot = new Picture(Capture("07_DisabledComponent", camera))) Require(Delta(baseline, shot.texture) < .00001f, "禁用相机组件应旁路。");
                post.enabled = true; controls.SetPostMode(0);
                var material = controls.SelectedMaterial; float oldStrength = material.GetFloat("_BrushStrength");
                material.SetFloat("_BrushStrength", 0); yield return null;
                using (var before = new Picture(Capture("08_BrushOff", camera)))
                {
                    material.SetFloat("_BrushStrength", .95f); yield return null;
                    using (var after = new Picture(Capture("09_BrushStrong", camera)))
                    {
                        float brushDelta = Delta(before.texture, after.texture);
                        report.AppendLine("Surface strength GPU mean RGB change=" + brushDelta.ToString("F6"));
                        Require(brushDelta > .0001f, "表面笔触参数须实时改变GPU画面。");
                    }
                }
                controls.ResetSelected(); Require(Mathf.Approximately(material.GetFloat("_BrushStrength"), oldStrength), "材质预设可以恢复。");
                report.AppendLine("PASS split left unchanged/right processed; disabled component bypass; live surface brush parameter and reset; brightness retained.");
                JsonUtility.FromJsonOverwrite(originalPost, post); controls.SetPostMode(2); controls.showPanel = true; controls.enabled = true;
                for (int i = 0; i < 4; i++) yield return null;
                string uiPath = Output + "/10_Controls.png";
                DateTime previousStamp = File.Exists(uiPath) ? File.GetLastWriteTimeUtc(uiPath) : DateTime.MinValue;
                ScreenCapture.CaptureScreenshot(uiPath);
                int captureFrame = Time.frameCount;
                while (Time.frameCount - captureFrame < 2 || !File.Exists(uiPath) || File.GetLastWriteTimeUtc(uiPath) <= previousStamp || new FileInfo(uiPath).Length == 0) yield return null;
                report.AppendLine("PASS runtime UI capture; real frame=" + Time.frameCount);
                controls.showPanel = false; controls.selected = 4; controls.FocusSelected(); controls.SetPostMode(0);
                for (int i = 0; i < 3; i++) yield return null;
                Require(Mathf.Abs(controls.distance - 4.8f) < .01f, "近景距离在真实 Update 后保持，避免滚轮限幅把相机弹回总览。");
                controls.enabled = false;
                using (var shot = new Picture(Capture("11_Brush_Closeup", camera))) Require(Mean(shot.texture) > .03f, "材质近景不是黑屏。");
                controls.SetPostMode(2); yield return null;
                using (var shot = new Picture(Capture("12_Brush_Closeup_Combined", camera))) Require(Mean(shot.texture) > .03f, "组合近景不是黑屏。");
                using (var small = new Picture(Capture("13_TargetTexture_720", camera, 720, 450))) Require(Mean(small.texture) > .03f, "小尺寸TargetTexture有效。");
                using (var large = new Picture(Capture("14_TargetTexture_1600", camera, 1600, 1000))) Require(Mean(large.texture) > .03f, "大尺寸TargetTexture有效。");
                controls.SetPostMode(0);
                material = controls.SelectedMaterial;
                Require(material.GetTexture("_BrushAtlas") && material.GetFloat("_UseBrushAtlas") > .5f, "真实笔触图集已接入材质。");
                material.SetFloat("_UseBrushAtlas", 0); yield return null;
                using (var analytic = new Picture(Capture("15_Analytic_Closeup", camera)))
                {
                    material.SetFloat("_UseBrushAtlas", 1); yield return null;
                    using (var stamped = new Picture(Capture("16_Stamped_Closeup", camera))) Require(Delta(analytic.texture, stamped.texture) > .001f, "印章贴图须实际改变笔触轮廓与厚薄。");
                }
                controls.Overview(); controls.selected = 0;
                report.AppendLine("PASS atlas versus analytic brush GPU comparison; closeups; 720x450 and 1600x1000 TargetTexture renders.");
            }
            finally
            {
                foreach (var texture in new[] { baseline, oil, combined, pixel, bypass }) if (texture) Object.DestroyImmediate(texture);
                JsonUtility.FromJsonOverwrite(originalPost, post); post.enabled = true; controls.enabled = originalControls;
            }
        }

        static Texture2D Capture(string name, Camera camera, int width = 1440, int height = 900)
        {
            var target = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default, 1);
            var previous = camera.targetTexture; var active = RenderTexture.active;
            var texture = new Texture2D(width, height, TextureFormat.RGB24, false);
            try
            {
                camera.targetTexture = target; camera.Render(); RenderTexture.active = target;
                texture.ReadPixels(new Rect(0, 0, width, height), 0, 0); texture.Apply();
                File.WriteAllBytes(Output + "/" + name + ".png", texture.EncodeToPNG()); return texture;
            }
            catch { Object.DestroyImmediate(texture); throw; }
            finally { camera.targetTexture = previous; RenderTexture.active = active; RenderTexture.ReleaseTemporary(target); }
        }
        sealed class Picture : IDisposable
        {
            public Texture2D texture; public Picture(Texture2D value) { texture = value; }
            public void Dispose() { if (texture) Object.DestroyImmediate(texture); }
        }
        static float Delta(Texture2D a, Texture2D b) { return DeltaRegion(a, b, 0, 1); }
        static float DeltaRegion(Texture2D a, Texture2D b, float left, float right)
        {
            var aa = a.GetPixels32(); var bb = b.GetPixels32(); double sum = 0; int count = 0;
            for (int y = 0; y < a.height; y += 2) for (int x = (int)(left * a.width); x < (int)(right * a.width); x += 2)
            { int i = y * a.width + x; sum += Math.Abs(aa[i].r - bb[i].r) + Math.Abs(aa[i].g - bb[i].g) + Math.Abs(aa[i].b - bb[i].b); count++; }
            return (float)(sum / (count * 3 * 255.0));
        }
        static float Mean(Texture2D texture) { return texture.GetPixels32().Average(c => (c.r + c.g + c.b) / 765f); }
        static void Finish(Exception error)
        {
            var pending = steps; steps = null; SessionState.SetBool(Key + ".running", false);
            try { (pending as IDisposable)?.Dispose(); } catch (Exception cleanup) { error = error ?? cleanup; }
            Application.logMessageReceived -= Log;
            if (error == null && !string.IsNullOrEmpty(errors)) error = new Exception(errors);
            Directory.CreateDirectory(Output); File.WriteAllText(Output + "/checks.txt", report.ToString());
            File.WriteAllText(Output + "/result.txt", error == null ? "PASS: GPU comparisons and live material/post controls.\n" : "FAIL: " + error + "\n");
            if (EditorApplication.isPlaying) EditorApplication.isPlaying = false;
            else if (SessionState.GetBool(Key + ".pending", false)) Restore();
        }
        static void Restore()
        {
            SessionState.SetBool(Key + ".pending", false);
            Application.runInBackground = SessionState.GetBool(Key + ".background", false);
            EditorApplication.isPaused = SessionState.GetBool(Key + ".paused", false);
            try
            {
                var controls = Object.FindObjectOfType<MaterialLabControls>();
                if (controls)
                {
                    string after = string.Join("|", controls.paintedSamples.Select(r => EditorJsonUtility.ToJson(r.sharedMaterial)));
                    File.WriteAllText(Output + "/materials-after.json", after);
                    Require(after == SessionState.GetString(Key + ".materials", ""), "Play 修改了持久材质。");
                }
                Require(AssetDatabase.GetAssetPath(QualitySettings.renderPipeline) == SessionState.GetString(Key + ".quality", ""), "原质量管线必须恢复。");
                Require(HashSettings() == SessionState.GetString(Key + ".settings", ""), "共享渲染设置不得改变。");
                File.AppendAllText(Output + "/checks.txt", "PASS saved material assets and shared pipeline settings unchanged; previous quality pipeline restored.\n");
            }
            catch (Exception error) { File.WriteAllText(Output + "/result.txt", "FAIL RESTORE: " + error + "\nPrevious result: " + File.ReadAllText(Output + "/result.txt")); }
            finally
            {
                string scene = SessionState.GetString(Key + ".scene", "");
                if (!string.IsNullOrEmpty(scene) && File.Exists(scene) && SceneManager.GetActiveScene().path != scene) EditorSceneManager.OpenScene(scene);
            }
        }
        static string HashSettings()
        {
            var paths = Directory.GetFiles("Assets/Settings", "*.asset").Concat(new[] { "ProjectSettings/GraphicsSettings.asset", "ProjectSettings/QualitySettings.asset" });
            using (var sha = SHA256.Create()) return string.Join("|", paths.OrderBy(p => p).Select(p => BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(p)))));
        }
        static void Log(string message, string stack, LogType type) { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors += message + "\n"; }
        static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    }
}
