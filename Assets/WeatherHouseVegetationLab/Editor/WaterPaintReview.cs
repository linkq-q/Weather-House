using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace WeatherHouse.Vegetation.Editor
{
    [InitializeOnLoad]
    public static class WaterPaintReview
    {
        const string Request = "VegetationLabReview/water-paint-request.txt";
        const string Output = "VegetationLabReview/MaterialLab";
        const string CameraRecord = Output + "/Water_Baseline_Camera.txt";
        static bool busy;
        static double nextPoll;

        static WaterPaintReview() { EditorApplication.update += Poll; }

        static void Poll()
        {
            if (busy || EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.timeSinceStartup < nextPoll) return;
            nextPoll = EditorApplication.timeSinceStartup + 1;
            if (!File.Exists(Request)) return;
            string command;
            try { command = File.ReadAllText(Request).Trim().ToLowerInvariant(); File.Delete(Request); }
            catch (Exception error)
            {
                Directory.CreateDirectory(Output);
                File.WriteAllText(Output + "/Water_Result.txt", "FAIL: request could not be read/consumed.\n" + error);
                return;
            }
            Run(command);
        }

        public static void Run(string command)
        {
            if (busy) return;
            busy = true;
            Directory.CreateDirectory(Output);
            var report = new StringBuilder("Evidence: actual Editor Camera.Render GPU readback through URP; NOT Play-mode evidence.\n");
            report.AppendLine("Command: " + command);
            report.AppendLine("GPU: " + SystemInfo.graphicsDeviceName);
            File.WriteAllText(Output + "/Water_Result.txt", "RUNNING " + command + "\n");
            Exception failure = null;
            string originalScene = null, cameraJson = null, dataJson = null, viewJson = null, navigationJson = null;
            byte[] originalBytes = null, labBytes = null;
            Camera camera = null;
            Camera[] cameras = null;
            bool[] cameraEnabled = null;
            UniversalAdditionalCameraData cameraData = null;
            bool addedCameraData = false;
            GrassLabView view = null;
            GrassLabCamera navigation = null;
            Renderer waterRenderer = null;
            Material savedMaterial = null, temporaryMaterial = null;
            Vector3 cameraPosition = Vector3.zero;
            Quaternion cameraRotation = Quaternion.identity;
            Matrix4x4 cameraProjection = Matrix4x4.identity, cameraView = Matrix4x4.identity;
            CursorLockMode cursorLock = Cursor.lockState;
            bool cursorVisible = Cursor.visible;
            var loggedErrors = new StringBuilder();
            Application.LogCallback log = (message, stack, type) => {
                if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
                    loggedErrors.AppendLine(message + "\n" + stack);
            };
            try
            {
                Require(command == "baseline" || command == "verify", "Expected baseline or verify.");
                Require(!EditorApplication.isPlayingOrWillChangePlaymode && SceneManager.sceneCount == 1,
                    "Start only outside Play with one scene open.");
                Scene startingScene = SceneManager.GetActiveScene();
                Require(!startingScene.isDirty && !string.IsNullOrEmpty(startingScene.path) && File.Exists(startingScene.path),
                    "Save the open scene before starting; unsaved work is never discarded.");
                Require(File.Exists(VegetationLabBuilder.ScenePath), "Existing VegetationLab scene required.");
                if (command == "verify") Require(File.Exists(CameraRecord), "Capture baseline first so the camera can be matched exactly.");
                originalScene = startingScene.path;
                originalBytes = File.ReadAllBytes(originalScene);
                labBytes = File.ReadAllBytes(VegetationLabBuilder.ScenePath);
                Application.logMessageReceived += log;
                Scene scene = originalScene == VegetationLabBuilder.ScenePath ? startingScene :
                    EditorSceneManager.OpenScene(VegetationLabBuilder.ScenePath, OpenSceneMode.Single);
                view = Components<GrassLabView>(scene).Single();
                navigation = view.GetComponent<GrassLabCamera>();
                camera = view.GetComponent<Camera>();
                Require(navigation && camera, "Use the existing lab navigation and camera.");
                var reflection = Components<PainterlyWaterReflection>(scene).Single();
                Require(reflection.isActiveAndEnabled, "ExecuteAlways planar reflection must be enabled.");
                waterRenderer = reflection.GetComponent<Renderer>();
                savedMaterial = waterRenderer.sharedMaterial;
                Require(savedMaterial && savedMaterial.shader, "Existing water material and shader required.");
                CheckShader(savedMaterial.shader, report);

                cameraJson = EditorJsonUtility.ToJson(camera);
                viewJson = EditorJsonUtility.ToJson(view);
                navigationJson = EditorJsonUtility.ToJson(navigation);
                cameraPosition = camera.transform.position; cameraRotation = camera.transform.rotation;
                cameraProjection = camera.projectionMatrix; cameraView = camera.worldToCameraMatrix;
                cameras = Components<Camera>(scene);
                cameraEnabled = cameras.Select(c => c.enabled).ToArray();
                cameraData = camera.GetComponent<UniversalAdditionalCameraData>();
                addedCameraData = !cameraData;
                if (cameraData) dataJson = EditorJsonUtility.ToJson(cameraData);
                else cameraData = camera.gameObject.AddComponent<UniversalAdditionalCameraData>();
                cameraData.requiresColorOption = CameraOverrideOption.On;
                cameraData.requiresDepthOption = CameraOverrideOption.On;
                Require(cameraData.renderType == CameraRenderType.Base, "The review camera must be a URP Base camera.");
                foreach (var other in cameras) other.enabled = other == camera;
                view.enabled = true; view.showPanel = false;
                navigation.enabled = true; navigation.libraryLayout = false;
                navigation.Shore();
                camera.orthographic = false;
                camera.fieldOfView = 50;
                camera.aspect = 1.6f;
                camera.nearClipPlane = .05f; camera.farClipPlane = 350;
                camera.rect = new Rect(0,0,1,1);
                camera.targetTexture = null;
                camera.ResetWorldToCameraMatrix(); camera.ResetProjectionMatrix();
                Require(Components<Camera>(scene).Count(c => c.isActiveAndEnabled) == 1,
                    "Only the selected lab camera may be enabled.");
                Require(cameraData.requiresColorTexture && cameraData.requiresDepthTexture,
                    "Camera opaque color and depth textures must be enabled.");
                report.AppendLine("Camera: existing GrassLabCamera.Shore; FOV=50; aspect=1.6; near=.05; far=350.");
                report.AppendLine("URP opaque color/depth: On / On; one enabled scene camera.");

                // Freeze water motion on a temporary material, leaving the persistent material intact.
                temporaryMaterial = new Material(savedMaterial) { hideFlags = HideFlags.HideAndDontSave };
                temporaryMaterial.SetFloat("_RippleSpeed",0);
                temporaryMaterial.SetFloat("_CausticSpeed",0);
                waterRenderer.sharedMaterial = temporaryMaterial;
                string signature = CameraSignature(camera);
                if (command == "verify") Require(File.ReadAllText(CameraRecord) == signature, "Before/after view and projection matrices must match exactly.");
                Capture(camera, Output + (command == "baseline" ? "/Water_Before.png" : "/Water_After.png"));
                var properties = new MaterialPropertyBlock(); waterRenderer.GetPropertyBlock(properties);
                Require(properties.GetFloat("_HasPlanarReflection") > .5f && properties.GetFloat("_HasSceneColor") > .5f,
                    "ExecuteAlways reflection and current-camera scene color were bound during the render.");
                CheckShader(savedMaterial.shader, report);
                report.AppendLine("GPU capture: 1600x1000; water ripple/caustic speeds held at zero on a temporary material.");
                report.AppendLine("Actual planar reflection and current-camera scene color: bound.");
                if (command == "baseline") File.WriteAllText(CameraRecord,signature);
                else
                {
                    WaterStyleChecks.Verify(camera);
                    const string legacyReport = "VegetationLabReview/water-style-checks.txt";
                    Require(File.Exists(legacyReport), "WaterStyleChecks must produce its GPU comparison report.");
                    report.AppendLine("Existing WaterStyleChecks.Verify(camera), Editor GPU comparisons:");
                    report.AppendLine(File.ReadAllText(legacyReport));
                    Require(waterRenderer.sharedMaterial == temporaryMaterial, "WaterStyleChecks restored the material it received.");
                    camera.transform.position = new Vector3(2,4.2f,-2.8f);
                    camera.transform.LookAt(new Vector3(6,0,4.5f));
                    camera.fieldOfView = 46;
                    Capture(camera,Output + "/Water_Close.png");
                    CaptureOrbit(camera,temporaryMaterial,report);
                    CheckShader(savedMaterial.shader, report);
                }
                Require(loggedErrors.Length == 0,"Editor render logged errors:\n" + loggedErrors);
            }
            catch (Exception error) { failure = error; }
            finally
            {
                Application.logMessageReceived -= log;
                try
                {
                    if (waterRenderer && savedMaterial) waterRenderer.sharedMaterial = savedMaterial;
                    if (temporaryMaterial) Object.DestroyImmediate(temporaryMaterial);
                    if (camera && cameraJson != null)
                    {
                        EditorJsonUtility.FromJsonOverwrite(cameraJson,camera);
                        camera.transform.SetPositionAndRotation(cameraPosition,cameraRotation);
                        camera.projectionMatrix = cameraProjection; camera.worldToCameraMatrix = cameraView;
                    }
                    if (cameraData)
                    {
                        if (addedCameraData) Object.DestroyImmediate(cameraData);
                        else if (dataJson != null) EditorJsonUtility.FromJsonOverwrite(dataJson,cameraData);
                    }
                    if (navigation && navigationJson != null) EditorJsonUtility.FromJsonOverwrite(navigationJson,navigation);
                    if (view && viewJson != null) EditorJsonUtility.FromJsonOverwrite(viewJson,view);
                    if (cameras != null && cameraEnabled != null)
                        for (int i=0;i<cameras.Length;i++) if (cameras[i]) cameras[i].enabled = cameraEnabled[i];
                }
                catch (Exception error) { failure = Combine(failure,"Restore temporary camera/material state",error); }
                try
                {
                    // Reload once to discard temporary scene changes, never saving either scene.
                    if (originalScene != null) EditorSceneManager.OpenScene(originalScene,OpenSceneMode.Single);
                    if (originalBytes != null)
                    {
                        Require(originalBytes.SequenceEqual(File.ReadAllBytes(originalScene)), "Original scene bytes changed.");
                        Require(labBytes.SequenceEqual(File.ReadAllBytes(VegetationLabBuilder.ScenePath)), "VegetationLab scene bytes changed.");
                        Require(SceneManager.GetActiveScene().path == originalScene && !SceneManager.GetActiveScene().isDirty,
                            "Restore the original saved scene cleanly.");
                        report.AppendLine("Original scene and VegetationLab file bytes: unchanged; original saved scene restored.");
                    }
                }
                catch (Exception error) { failure = Combine(failure,"Restore saved scene / byte comparison",error); }
                Cursor.lockState = cursorLock; Cursor.visible = cursorVisible;
                if (failure != null) report.AppendLine("FAIL: " + failure);
                File.WriteAllText(Output + "/Water_Checks.txt",report.ToString());
                File.WriteAllText(Output + "/Water_Result.txt",failure == null ? "PASS: " + command + " Editor camera GPU review; no scene saved.\n" : "FAIL: " + failure + "\n");
                busy = false;
            }
        }

        static void CaptureOrbit(Camera camera, Material water, StringBuilder report)
        {
            const int frames = 24, width = 960, height = 600;
            string directory = Output + "/WaterOrbit";
            Directory.CreateDirectory(directory);
            Require(water.GetFloat("_RippleSpeed") == 0 && water.GetFloat("_CausticSpeed") == 0,
                "Orbit water ripple and caustic speeds must remain zero.");
            camera.orthographic = false;
            camera.fieldOfView = 50; camera.aspect = 1.6f;
            camera.nearClipPlane = .05f; camera.farClipPlane = 350;
            camera.rect = new Rect(0,0,1,1);
            camera.ResetWorldToCameraMatrix(); camera.ResetProjectionMatrix();
            Vector3 focus = new Vector3(6,.1f,4);
            var representativeMatrices = new System.Collections.Generic.List<string>();
            report.AppendLine("Orbit: 24 actual Editor Camera.Render GPU frames, NOT Play; 960x600; focus=(6,.1,4), pitch=22, distance=18.5, yaw=10..70.");
            report.AppendLine("Orbit water ripple/caustic speeds: zero; the existing water effect comparison suite runs once only.");
            for (int frame=0;frame<frames;frame++)
            {
                float yaw = Mathf.Lerp(10,70,frame/(float)(frames-1));
                Quaternion rotation = Quaternion.Euler(22,yaw,0);
                camera.transform.SetPositionAndRotation(focus-rotation*Vector3.forward*18.5f,rotation);
                bool representative = frame == 0 || frame == 12 || frame == 23;
                int nonBlackPixels = Capture(camera,directory + "/" + frame.ToString("000") + ".png",width,height,representative);
                if (!representative) continue;
                Require(nonBlackPixels > width*height/20,"Orbit frame " + frame + " must contain more than 5% nonblack pixels (RGB sum > 12).");
                string signature = CameraSignature(camera);
                representativeMatrices.Add(signature);
                report.AppendLine("Orbit frame " + frame.ToString("000") + ": yaw=" + yaw.ToString("R",CultureInfo.InvariantCulture) +
                    "; nonblack=" + nonBlackPixels + "/" + width*height + "; view/projection=" + signature);
            }
            Require(representativeMatrices.Count == 3 && representativeMatrices.Distinct().Count() == 3,
                "Orbit frames 000/012/023 must have distinct camera matrices.");
            report.AppendLine("Orbit representative frames 000/012/023: nonblack GPU captures and distinct camera matrices passed. Camera restored by outer finally.");
        }

        static int Capture(Camera camera, string path, int width = 1600, int height = 1000, bool measureNonBlack = false)
        {
            var target = RenderTexture.GetTemporary(width,height,24,RenderTextureFormat.ARGB32,RenderTextureReadWrite.Default,4);
            RenderTexture prior = camera.targetTexture, active = RenderTexture.active;
            var image = new Texture2D(width,height,TextureFormat.RGB24,false);
            try
            {
                camera.targetTexture = target; camera.Render(); RenderTexture.active = target;
                image.ReadPixels(new Rect(0,0,width,height),0,0); image.Apply();
                File.WriteAllBytes(path,image.EncodeToPNG());
                if (!measureNonBlack) return 0;
                int nonBlackPixels = 0;
                foreach (Color32 pixel in image.GetPixels32())
                    if (pixel.r+pixel.g+pixel.b > 12) nonBlackPixels++;
                return nonBlackPixels;
            }
            finally
            {
                camera.targetTexture = prior; RenderTexture.active = active;
                RenderTexture.ReleaseTemporary(target); Object.DestroyImmediate(image);
            }
        }
        static void CheckShader(Shader shader, StringBuilder report)
        {
            Require(shader && shader.isSupported,"Water shader is supported.");
            var errors = ShaderUtil.GetShaderMessages(shader).Where(message => message.severity == UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error).ToArray();
            Require(errors.Length == 0 && !ShaderUtil.ShaderHasError(shader),"Water shader compiler errors: " + string.Join("; ",errors.Select(error => error.message)));
            report.AppendLine("ShaderUtil: " + shader.name + " has no compiler errors.");
        }
        static string CameraSignature(Camera camera)
        {
            var signature = new StringBuilder();
            foreach (Matrix4x4 matrix in new[] { camera.worldToCameraMatrix,camera.projectionMatrix })
                for (int i=0;i<16;i++) signature.Append(matrix[i].ToString("R",CultureInfo.InvariantCulture)).Append('|');
            signature.Append(camera.cullingMask).Append('|').Append((int)camera.clearFlags);
            return signature.ToString();
        }
        static T[] Components<T>(Scene scene) where T : Component { return scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<T>(true)).ToArray(); }
        static Exception Combine(Exception prior, string action, Exception error) { return new Exception(action + " failed.",prior == null ? error : new AggregateException(prior,error)); }
        static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException("WaterPaintReview: " + message); }
    }
}
