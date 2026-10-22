using System;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace WeatherHouse.Vegetation.Editor
{
    [InitializeOnLoad]
    public static class WaterGrassDefaultChecks
    {
        const string Folder = "VegetationLabReview/DefaultsRevision";
        const string Stage = "WeatherHouse.WaterGrassDefaults.Stage";
        static double next, ready;
        static WaterGrassDefaultChecks() { EditorApplication.update += Poll; }
        static void Log(string text) { File.AppendAllText(Folder + "/checks.txt", text + "\n"); }
        static void Require(bool value, string text) { if (!value) throw new InvalidOperationException(text); }
        static Material Asset(string name) { return AssetDatabase.LoadAssetAtPath<Material>(VegetationLabBuilder.Generated + "/" + name + ".mat"); }
        const string Expected = @"ShortGrass|_MeadowBlend|0.94
ShortGrass|_BladeContrast|0.325
ShortGrass|_MeadowPatchScale|0.17
ShortGrass|_BoundaryHardness|0.71
ShortGrass|_BoundaryBreakup|0.121
ShortGrass|_BoundarySize|1.48
ShortGrass|_MeadowBaseColor|0.5764705882352941|0.6235294117647059|0.2627450980392157|1
ShortGrass|_MeadowPatchColor|0.8117647058823529|0.7490196078431373|0.3803921568627451|1
ShortGrass|_ShadowTint|0.56|0.69|0.62|1
TallGrass|_MeadowBlend|0.73
TallGrass|_BladeContrast|0
TallGrass|_MeadowPatchScale|0.19
TallGrass|_BoundaryHardness|0.71
TallGrass|_BoundaryBreakup|0.121
TallGrass|_BoundarySize|1.48
TallGrass|_MeadowBaseColor|0.4980392156862745|0.5843137254901961|0.27058823529411763|1
TallGrass|_MeadowPatchColor|0.9294117647058824|0.8549019607843137|0.43137254901960786|1
TallGrass|_ShadowTint|0.56|0.69|0.62|1
PainterlyGround|_PatchScale|0.15
PainterlyGround|_BoundaryHardness|0.71
PainterlyGround|_BoundaryBreakup|0.121
PainterlyGround|_BoundarySize|1.48
PainterlyGround|_BaseColor|0.5294117647058824|0.592156862745098|0.24313725490196078|1
PainterlyGround|_PatchColor|0.6862745098039216|0.6980392156862745|0.3607843137254902|1
PainterlyGround|_ShoreColor|0.8117647058823529|0.796078431372549|0.611764705882353|1
PainterlyWater|_WaterSaturation|1.09
PainterlyWater|_Opacity|0.73
PainterlyWater|_ReflectionNear|0.845
PainterlyWater|_ReflectionFar|0.345
PainterlyWater|_ReflectionFalloff|3.05
PainterlyWater|_ReflectionStrength|0.802
PainterlyWater|_ReflectionTintStrength|0
PainterlyWater|_ReflectionDistortion|0.0045
PainterlyWater|_SurfaceStrokeStrength|0.417
PainterlyWater|_SmallStrokeWeight|0.623
PainterlyWater|_WaveletStrength|0.289
PainterlyWater|_WaveletScale|0.58
PainterlyWater|_WaveletSpeed|0.15
PainterlyWater|_WaveletLength|1.25
PainterlyWater|_ShoreStrength|1
PainterlyWater|_ShoreWidth|0.191
PainterlyWater|_FoamSpeed|0
PainterlyWater|_FoamTravel|0.183
PainterlyWater|_FoamSoftness|0.101
PainterlyWater|_ShallowColor|0.19215686274509805|1.0|0.8117647058823529|1
PainterlyWater|_DeepColor|0.0|0.43137254901960786|0.6352941176470588|1
PainterlyWater|_ReflectionTint|0.2|0.44313725490196076|0.3568627450980392|1
PainterlyWater|_RippleColor|1.0|0.984313725490196|0.8862745098039215|1
PainterlyWater|_FoamColor|0.9921568627450981|1.0|0.9058823529411765|1";
        static void CheckDefaults()
        {
            int count = 0;
            foreach (string line in Expected.Split('\n'))
            {
                var fields = line.Split('|'); var material = Asset(fields[0]);
                Require(material && material.HasProperty(fields[1]), "Missing " + line);
                float expected = float.Parse(fields[2], CultureInfo.InvariantCulture);
                if (fields.Length == 3) Require(Mathf.Abs(material.GetFloat(fields[1]) - expected) < .00001f, "Default mismatch " + line);
                else
                {
                    var color = material.GetColor(fields[1]);
                    for (int i = 0; i < 4; i++)
                        Require(Mathf.Abs(color[i] - float.Parse(fields[i + 2], CultureInfo.InvariantCulture)) < .00001f, "Color mismatch " + line);
                }
                count++;
            }
            Log("PASS " + count + " screenshot float/color defaults loaded from material assets.");
        }
        static void Poll()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.timeSinceStartup < next) return;
            next = EditorApplication.timeSinceStartup + .3;
            string stage = SessionState.GetString(Stage, "");
            try
            {
                if (stage == "play" && EditorApplication.isPlaying)
                {
                    if (ready == 0) { ready = EditorApplication.timeSinceStartup + 2; return; }
                    if (EditorApplication.timeSinceStartup < ready) return;
                    var character = Object.FindObjectOfType<VegetationCharacterPreview>();
                    if (character) character.SetCharacterMode(false);
                    var view = Object.FindObjectOfType<GrassLabView>(true); view.Shore(); view.ShowWaterGrassLook();
                    var materials = view.WaterGrassMaterials;
                    var water = materials.Single(m => m.shader.name == "Weather House/Painterly Water");
                    var shortGrass = materials.Single(m => m.name.StartsWith("ShortGrass"));
                    var tallGrass = materials.Single(m => m.name.StartsWith("TallGrass"));
                    Require(view.meadow.ConfigurationError == null, "Runtime meadow ownership failed.");
                    Require(Mathf.Abs(shortGrass.GetFloat("_MeadowPatchScale") - .17f) < .00001f && Mathf.Abs(tallGrass.GetFloat("_MeadowPatchScale") - .19f) < .00001f, "Distinct grass fields lost.");
                    var camera = view.GetComponent<Camera>();
                    var a = Render(camera, "runtime_defaults");
                    water.SetFloat("_Opacity", .25f); var b = Render(camera, "runtime_adjusted");
                    int changed = a.Where((pixel, index) => Mathf.Abs(pixel.r-b[index].r)+Mathf.Abs(pixel.g-b[index].g)+Mathf.Abs(pixel.b-b[index].b) > 9).Count();
                    Require(changed > 100, "Runtime water adjustment did not affect GPU pixels.");
                    water.SetFloat("_Opacity", .731f); shortGrass.SetFloat("_BladeContrast", .326f);
                    var source = view.meadow.SourceMaterial(shortGrass); Color root = source.GetColor("_BaseColor"); float wind = source.GetFloat("_WindStrength");
                    view.SaveWaterGrassDefaults();
                    Require(Mathf.Abs(Asset("PainterlyWater").GetFloat("_Opacity") - .731f) < .00001f && Mathf.Abs(source.GetFloat("_BladeContrast") - .326f) < .00001f, "Save did not reach persistent source materials.");
                    Require(source.GetColor("_BaseColor") == root && source.GetFloat("_WindStrength") == wind, "Look save changed species/wind settings.");
                    Require(File.ReadAllText(AssetDatabase.GetAssetPath(source)).Contains("_BladeContrast: 0.326"), "Runtime save did not reach disk.");
                    water.SetFloat("_Opacity", .73f); shortGrass.SetFloat("_BladeContrast", .325f); view.SaveWaterGrassDefaults();
                    CheckDefaults();
                    foreach (var m in materials) Require(m.shader.isSupported && !ShaderUtil.ShaderHasError(m.shader), "Shader errors " + m.name);
                    Log("PASS actual Play: runtime panel opened; water GPU adjustment changed " + changed + " pixels; independent short/tall fields retained; save reached assets and disk; species/wind preserved.");
                    ScreenCapture.CaptureScreenshot(Folder + "/runtime_panel.png");
                    SessionState.SetString(Stage, "screenshot"); ready = EditorApplication.timeSinceStartup + 1;
                    return;
                }
                if (stage == "screenshot" && EditorApplication.isPlaying && EditorApplication.timeSinceStartup > ready)
                { SessionState.SetString(Stage, "restore"); EditorApplication.isPlaying = false; return; }
                if (stage == "restore" && !EditorApplication.isPlayingOrWillChangePlaymode)
                {
                    foreach (string file in Directory.GetFiles(Folder + "/BeforeTest", "*.mat"))
                    {
                        string path = VegetationLabBuilder.Generated + "/" + Path.GetFileName(file);
                        File.WriteAllBytes(path, File.ReadAllBytes(file)); AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                    }
                    EditorSceneManager.OpenScene(VegetationLabBuilder.ScenePath);
                    CheckDefaults();
                    Require(File.ReadAllBytes(VegetationLabBuilder.ScenePath).SequenceEqual(File.ReadAllBytes(Folder + "/scene-before.bytes")), "Saved scene bytes changed.");
                    SessionState.EraseString(Stage); ready = 0;
                    Log("PASS exited Play and reopened VegetationLab: screenshot defaults retained, source test edits rolled back, saved scene bytes unchanged.");
                    return;
                }
                if (stage != "" || !File.Exists(Folder + "/request.txt")) return;
                string command = File.ReadAllText(Folder + "/request.txt").Trim(); File.Delete(Folder + "/request.txt");
                Require(command == "verify", "Unknown request.");
                var scene = SceneManager.GetActiveScene();
                Require(!EditorApplication.isPlayingOrWillChangePlaymode && SceneManager.sceneCount == 1 && !scene.isDirty && scene.path == VegetationLabBuilder.ScenePath, "Need saved VegetationLab outside Play; current work preserved.");
                File.WriteAllText(Folder + "/checks.txt", "GPU " + SystemInfo.graphicsDeviceName + "; Unity " + Application.unityVersion + "\n");
                Directory.CreateDirectory(Folder + "/BeforeTest");
                foreach (string name in new[] { "ShortGrass", "TallGrass", "WhiteDaisy", "PainterlyWater", "PainterlyGround", "QuietDaySky" })
                    File.Copy(AssetDatabase.GetAssetPath(Asset(name)), Folder + "/BeforeTest/" + name + ".mat", true);
                File.Copy(VegetationLabBuilder.ScenePath, Folder + "/scene-before.bytes", true);
                CheckDefaults(); SessionState.SetString(Stage, "play"); EditorApplication.isPlaying = true;
            }
            catch (Exception error)
            {
                Log("FAIL " + error); Debug.LogException(error);
                if (EditorApplication.isPlaying) { SessionState.SetString(Stage, "restore"); EditorApplication.isPlaying = false; }
                else SessionState.EraseString(Stage);
            }
        }
        static Color32[] Render(Camera camera, string name)
        {
            var rt = RenderTexture.GetTemporary(1200, 750, 24); var before = camera.targetTexture; var active = RenderTexture.active;
            var texture = new Texture2D(1200, 750, TextureFormat.RGB24, false);
            try
            {
                camera.targetTexture = rt; camera.Render(); RenderTexture.active = rt;
                texture.ReadPixels(new Rect(0,0,1200,750),0,0); texture.Apply();
                File.WriteAllBytes(Folder + "/" + name + ".png", texture.EncodeToPNG()); return texture.GetPixels32();
            }
            finally { camera.targetTexture = before; RenderTexture.active = active; RenderTexture.ReleaseTemporary(rt); Object.DestroyImmediate(texture); }
        }
    }
}
