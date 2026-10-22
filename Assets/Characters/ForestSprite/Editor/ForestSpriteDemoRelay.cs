using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ForestSpriteCharacter.Editor
{
    // An explicit local request is required; importing this script never opens or saves a scene.
    [InitializeOnLoad]
    public static class ForestSpriteDemoRelay
    {
        const string RequestPath = ForestSpriteDemoBuilder.Review + "/request.json";
        const string ResponsePath = ForestSpriteDemoBuilder.Review + "/response.json";
        static readonly string Key = "ForestSpriteDemoRelay:" + Application.dataPath;
        static double nextPoll;
        static string lastMalformed = "", pendingId = "";

        [Serializable]
        sealed class Request { public string requestId = "", command = ""; }

        [Serializable]
        sealed class SceneStatus
        {
            public string name, path;
            public bool dirty, loaded, valid;
            public int rootObjects;
        }

        [Serializable]
        sealed class Response
        {
            public string requestId, command, status, message, utc, project, activeScene, playModeStartScene;
            public string runtimeReportPath, runtimeReportModifiedUtc;
            public bool playing, changingPlayMode, compiling, updating, demoPlayOwnedByRelay;
            public SceneStatus[] scenes;
        }

        static ForestSpriteDemoRelay()
        {
            if (!SessionState.GetBool(Key + ":initialized", false))
            {
                // A request left on disk from a previous editor session is not a new instruction.
                if (File.Exists(RequestPath))
                {
                    try
                    {
                        var existing = JsonUtility.FromJson<Request>(File.ReadAllText(RequestPath));
                        if (existing != null && !string.IsNullOrWhiteSpace(existing.requestId))
                            MarkHandled(existing.requestId);
                    }
                    catch (Exception exception) { Debug.LogWarning("Forest sprite relay ignored an invalid request left on disk: " + exception.Message); }
                }
                SessionState.SetBool(Key + ":initialized", true);
            }
            EditorApplication.update += Poll;
            EditorApplication.playModeStateChanged += RestorePlayStartScene;
        }

        static void Poll()
        {
            if (EditorApplication.timeSinceStartup < nextPoll) return;
            nextPoll = EditorApplication.timeSinceStartup + .25;
            if (!File.Exists(RequestPath)) return;
            Request request = null;
            string source = "";
            try
            {
                source = File.ReadAllText(RequestPath);
                request = JsonUtility.FromJson<Request>(source);
                if (request == null || string.IsNullOrWhiteSpace(request.requestId) || string.IsNullOrWhiteSpace(request.command))
                    throw new InvalidDataException("request.json requires nonempty requestId and command strings.");
                if (request.requestId.Contains("\n") || request.requestId.Contains("\r"))
                    throw new InvalidDataException("requestId must be a single line.");
                if (WasHandled(request.requestId)) return;
                if (request.command != "Status" && (EditorApplication.isCompiling || EditorApplication.isUpdating))
                {
                    if (pendingId != request.requestId)
                    {
                        pendingId = request.requestId;
                        Respond(request, "pending", "Waiting for compilation and asset import to finish.");
                    }
                    return;
                }
                // Record before execution so a Play/domain reload cannot repeat a mutation.
                MarkHandled(request.requestId);
                pendingId = "";
                Execute(request);
            }
            catch (Exception exception)
            {
                if (request != null && !string.IsNullOrWhiteSpace(request.requestId))
                    MarkHandled(request.requestId);
                else if (source == lastMalformed) return;
                else lastMalformed = source;
                Respond(request, "error", exception.ToString());
                Debug.LogException(exception);
            }
        }

        static void Execute(Request request)
        {
            switch (request.command)
            {
                case "Status":
                    Respond(request, "success", "Editor state only; no scene or Play changes.");
                    return;
                case "Build":
                    ForestSpriteDemoBuilder.Build();
                    Respond(request, "success", "Built the independent demo; previously open scenes were retained.");
                    return;
                case "Validate":
                    ForestSpriteDemoChecks.ValidateOpen();
                    Respond(request, "success", "Imported animation, weights, bones and scene wiring passed.");
                    return;
                case "RunRuntimeChecks":
                    ForestSpriteDemoChecks.RunRuntimeChecks();
                    Respond(request, "accepted", "Live checks started; read Verification~/runtime.json for the result.");
                    return;
                case "Capture":
                    CaptureDemo();
                    Respond(request, "success", "Saved only the demo camera view to Verification~/unity-preview.png (900 x 900).");
                    return;
                case "DemoSequence":
                    Require(EditorApplication.isPlaying && SceneManager.GetActiveScene().path == ForestSpriteDemoBuilder.ScenePath,
                        "Automatic demonstration requires Play in the forest sprite demo.");
                    var player = SceneManager.GetActiveScene().GetRootGameObjects()
                        .SelectMany(root => root.GetComponentsInChildren<ForestSpriteDemoController>()).SingleOrDefault();
                    Require(player != null, "Demo controller exists.");
                    player.SetDemonstration(ForestSpriteDemoController.Demonstration.Sequence);
                    Respond(request, "success", "Started the idle, walk, run and jump demonstration loop.");
                    return;
                case "OpenDemo":
                    Require(!EditorApplication.isPlayingOrWillChangePlaymode, "Stop Play before opening the demo.");
                    Require(File.Exists(ForestSpriteDemoBuilder.ScenePath), "Build the demo scene first.");
                    var demo = SceneManager.GetSceneByPath(ForestSpriteDemoBuilder.ScenePath);
                    if (!demo.IsValid() || !demo.isLoaded)
                        demo = EditorSceneManager.OpenScene(ForestSpriteDemoBuilder.ScenePath, OpenSceneMode.Additive);
                    Require(SceneManager.SetActiveScene(demo), "Could not activate the demo scene.");
                    Respond(request, "success", "Demo opened additively and made active; other scenes remain open.");
                    return;
                case "Play":
                    StartDemoPlay();
                    Respond(request, "accepted", "Entering Play with the saved demo scene only. No scenes were saved.");
                    return;
                case "Stop":
                    Require(EditorApplication.isPlayingOrWillChangePlaymode, "The editor is not in Play.");
                    Require(SceneManager.GetActiveScene().path == ForestSpriteDemoBuilder.ScenePath ||
                        SessionState.GetBool(Key + ":ownedPlay", false), "Refusing to stop an unrelated Play session.");
                    EditorApplication.isPlaying = false;
                    Respond(request, "accepted", "Leaving demo Play without saving scene changes.");
                    return;
                default:
                    throw new InvalidOperationException("Unsupported command. Allowed: Build, Validate, RunRuntimeChecks, Status, OpenDemo, Play, Stop, Capture, DemoSequence.");
            }
        }

        public static void CaptureDemo(string fileName = "unity-preview.png", int size = 900)
        {
            Require(Path.GetFileName(fileName) == fileName && fileName.EndsWith(".png", StringComparison.OrdinalIgnoreCase), "Capture requires a PNG filename within Verification~.");
            var demo = SceneManager.GetActiveScene();
            Require(demo.path == ForestSpriteDemoBuilder.ScenePath && demo.isLoaded, "Only capture the active forest sprite demo.");
            var player = demo.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<ForestSpriteDemoController>()).SingleOrDefault();
            Require(player != null && player.demoCamera != null, "Demo camera exists.");
            var camera = player.demoCamera;
            var previousTarget = camera.targetTexture;
            var previousActive = RenderTexture.active;
            var previousScene = camera.scene;
            float previousAspect = camera.aspect;
            var target = new RenderTexture(size, size, 24, RenderTextureFormat.ARGB32);
            var pixels = new Texture2D(size, size, TextureFormat.RGB24, false);
            try
            {
                // Camera.scene limits both Edit and Play captures to the demo's scene contents.
                camera.scene = demo;
                camera.targetTexture = target;
                camera.aspect = 1f;
                camera.Render();
                RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0, 0, size, size), 0, 0);
                pixels.Apply();
                Directory.CreateDirectory(ForestSpriteDemoBuilder.Review);
                File.WriteAllBytes(ForestSpriteDemoBuilder.Review + "/" + fileName, pixels.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture = previousTarget;
                camera.scene = previousScene;
                camera.aspect = previousAspect;
                RenderTexture.active = previousActive;
                target.Release();
                UnityEngine.Object.DestroyImmediate(target);
                UnityEngine.Object.DestroyImmediate(pixels);
            }
        }

        static void StartDemoPlay()
        {
            Require(!EditorApplication.isPlayingOrWillChangePlaymode, "The editor is already playing or changing Play state.");
            Require(SceneManager.GetActiveScene().path == ForestSpriteDemoBuilder.ScenePath, "Open and activate the forest sprite demo first.");
            for (int i = 0; i < SceneManager.sceneCount; i++)
                Require(!SceneManager.GetSceneAt(i).isDirty, "An open scene has unsaved changes; the relay will not save or discard them.");
            var scene = AssetDatabase.LoadAssetAtPath<SceneAsset>(ForestSpriteDemoBuilder.ScenePath);
            Require(scene != null, "Saved demo scene asset exists.");
            SessionState.SetString(Key + ":previousPlayStart", AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene));
            SessionState.SetBool(Key + ":ownedPlay", true);
            // The other edit-mode scenes remain open, but only this saved scene enters Play.
            EditorSceneManager.playModeStartScene = scene;
            EditorApplication.isPlaying = true;
        }

        static void RestorePlayStartScene(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.EnteredEditMode || !SessionState.GetBool(Key + ":ownedPlay", false)) return;
            string previous = SessionState.GetString(Key + ":previousPlayStart", "");
            EditorSceneManager.playModeStartScene = string.IsNullOrEmpty(previous) ? null : AssetDatabase.LoadAssetAtPath<SceneAsset>(previous);
            SessionState.SetBool(Key + ":ownedPlay", false);
        }

        static void Respond(Request request, string status, string message)
        {
            string runtimePath = ForestSpriteDemoBuilder.Review + "/runtime.json";
            var response = new Response
            {
                requestId = request == null ? "" : request.requestId,
                command = request == null ? "" : request.command,
                status = status, message = message, utc = DateTime.UtcNow.ToString("O"),
                project = Application.dataPath, activeScene = SceneManager.GetActiveScene().path,
                playModeStartScene = AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene),
                playing = EditorApplication.isPlaying, changingPlayMode = EditorApplication.isPlayingOrWillChangePlaymode != EditorApplication.isPlaying,
                compiling = EditorApplication.isCompiling, updating = EditorApplication.isUpdating,
                demoPlayOwnedByRelay = SessionState.GetBool(Key + ":ownedPlay", false),
                runtimeReportPath = runtimePath,
                runtimeReportModifiedUtc = File.Exists(runtimePath) ? File.GetLastWriteTimeUtc(runtimePath).ToString("O") : "",
                scenes = Enumerable.Range(0, SceneManager.sceneCount).Select(i =>
                {
                    var scene = SceneManager.GetSceneAt(i);
                    return new SceneStatus { name = scene.name, path = scene.path, dirty = scene.isDirty,
                        loaded = scene.isLoaded, valid = scene.IsValid(), rootObjects = scene.isLoaded ? scene.rootCount : 0 };
                }).ToArray()
            };
            Directory.CreateDirectory(ForestSpriteDemoBuilder.Review);
            File.WriteAllText(ResponsePath, JsonUtility.ToJson(response, true));
        }

        static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        static bool WasHandled(string requestId) => SessionState.GetString(Key + ":handledIds", "")
            .Split('\n').Contains(requestId);

        static void MarkHandled(string requestId)
        {
            if (string.IsNullOrEmpty(requestId) || WasHandled(requestId)) return;
            SessionState.SetString(Key + ":handledIds", SessionState.GetString(Key + ":handledIds", "") + "\n" + requestId);
        }
    }
}
