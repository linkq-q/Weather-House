using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using WeatherHouse.Hillside;
using WeatherHouse.WatersideV2.Editor;
using WeatherHousePrototype;
using Object = UnityEngine.Object;

// One synchronous call inside the existing UI review's Play sequence. No new lifecycle or notebook access.
public static class WatersideAnimalScalePlayChecks
{
    const string ScenePath = "Assets/Scenes/WeatherHouse_WatersideV2.unity";
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    [Serializable] sealed class AnimalResult
    {
        public string species; public int spawnId;
        public float actualScale, expectedScale, actualRadius, expectedRadius, groundError;
        public Vector3 actualVisualScale, expectedVisualScale, actualBodySize, expectedBodySize;
        public bool groundValid, bodyClear;
    }
    [Serializable] sealed class Result
    {
        public string status = "RUNNING", oracle, error, utc;
        public int frame, butterfliesRejected;
        public List<AnimalResult> animals = new List<AnimalResult>();
        public List<string> images = new List<string>();
    }

    public static string Run(string previewReport, string outputDirectory, bool captureImages = true)
    {
        Require(Application.isPlaying && SceneManager.GetActiveScene().path == ScenePath, "Run once in the actual Waterside Play scene.");
        var oracle = JsonUtility.FromJson<WatersideAnimalScale.Report>(File.ReadAllText(previewReport));
        Require(oracle != null && oracle.scene == ScenePath && oracle.entries != null && oracle.entries.Length == 11 &&
            oracle.entries.Select(e => e.spawnId).Distinct().Count() == 11, "Use the complete size migration Preview/Apply report for this scene.");
        var result = new Result { oracle = Path.GetFullPath(previewReport), utc = DateTime.UtcNow.ToString("O"), frame = Time.frameCount };
        WildlifeAgent[] all = SceneManager.GetActiveScene().GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<WildlifeAgent>(true)).ToArray();
        WildlifeAgent[] animals = all.Where(a => a.speciesId == "turtle" || a.speciesId == "squirrel").OrderBy(a => a.spawnId).ToArray();
        Vector3[] positions = animals.Select(a => a.transform.position).ToArray();
        Quaternion[] rotations = animals.Select(a => a.transform.rotation).ToArray();
        var physics = new WildlifeBodyPhysics();
        Directory.CreateDirectory(outputDirectory);
        string reportPath = Path.Combine(outputDirectory, "animal-scale-play-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            Require(animals.Length == 11, "Expected 11 inspected turtles and squirrels in actual Play.");
            foreach (WildlifeAgent agent in animals)
            {
                var expected = oracle.entries.Single(e => e.spawnId == agent.spawnId && e.species == agent.speciesId);
                var population = agent.GetComponentInParent<WildlifePopulation>();
                Require(agent.isActiveAndEnabled && population && population.Initialized && agent.visualRoot, "Actual animal driver is not initialized: " + agent.spawnId);
                Vector3 basis = (Vector3)typeof(WildlifeAgent).GetField("baseVisualScale", Private).GetValue(agent);
                Vector3 baseOffset = (Vector3)typeof(WildlifeAgent).GetField("baseVisualPosition", Private).GetValue(agent);
                var value = new AnimalResult { species = agent.speciesId, spawnId = agent.spawnId,
                    actualScale = agent.ScaleFactor, expectedScale = expected.scaleAfter,
                    actualRadius = agent.Radius, expectedRadius = expected.radiusAfter,
                    actualVisualScale = agent.visualRoot.localScale, expectedVisualScale = basis * expected.scaleAfter,
                    actualBodySize = agent.BodyEnvelope.half * 2, expectedBodySize = expected.bodySizeAfter };
                result.animals.Add(value);
                Require(Mathf.Abs(value.actualScale - value.expectedScale) < .0001f && Mathf.Abs(value.actualRadius - value.expectedRadius) < .0001f,
                    "Actual deterministic size/radius differs from migration oracle: " + agent.spawnId);
                Require(Vector3.Distance(value.actualVisualScale, value.expectedVisualScale) < .0001f &&
                    Vector3.Distance(agent.visualRoot.localPosition, baseOffset * expected.scaleAfter) < .0001f,
                    "Actual visual scale/foot offset differs from captured authored basis: " + agent.spawnId);
                Require(agent.BodyEnvelope.measured && Vector3.Distance(value.actualBodySize, value.expectedBodySize) < .005f,
                    "Actual initialized body envelope differs from independent Edit prediction: " + agent.spawnId);
                value.groundValid = population.TryLandingPosition(agent, agent.transform.position, out Vector3 ground);
                value.groundError = Mathf.Abs(agent.transform.position.y - ground.y);
                value.bodyClear = physics.Clear(agent.BodyEnvelope, agent.transform.position, agent.transform.rotation,
                    agent.transform.position, agent.transform.rotation, population.obstacleMask, owner: agent.transform, ignoreAnimals: true);
                Require(value.groundValid && value.groundError < .02f && value.bodyClear,
                    "Actual animal is unsupported or overlaps a static obstacle: " + agent.spawnId);
            }
            WildlifeAgent[] butterflies = all.Where(a => a.speciesId == "butterfly").ToArray();
            Require(butterflies.Length == 20, "Expected the 20 existing bound butterflies.");
            foreach (WildlifeAgent butterfly in butterflies)
            {
                var actor = butterfly.GetComponent<AnimalActor>(); var marker = butterfly.GetComponent<WildlifeInteractionTarget>();
                Require(actor && marker && !actor.IsAvailableForObservation && !marker.IsAvailable &&
                    actor.CurrentGlyphIds.Length == 0 && actor.UtteranceId.EndsWith(".unavailable"),
                    "Existing butterfly is still available for greeting/E dialogue: " + butterfly.spawnId);
                result.butterfliesRejected++;
            }
            if (captureImages)
                foreach (WildlifeAgent animal in animals.Where(a => a.spawnId == 14 || a.spawnId == 44 ||
                    !oracle.entries.Single(e => e.spawnId == a.spawnId).footprintFlat))
                {
                    result.images.Add(Capture(animal, outputDirectory, 35));
                    result.images.Add(Capture(animal, outputDirectory, 215));
                }
            for (int i = 0; i < animals.Length; i++)
                Require(animals[i].transform.position == positions[i] && animals[i].transform.rotation == rotations[i], "The synchronous review changed an animal pose.");
            result.status = "PASS";
        }
        catch (Exception error) { result.status = "FAIL"; result.error = error.ToString(); throw; }
        finally { File.WriteAllText(reportPath, Json(result)); }
        return reportPath;
    }

    static string Capture(WildlifeAgent animal, string folder, float preferredYaw)
    {
        Renderer[] meshes = animal.visualRoot.GetComponentsInChildren<Renderer>().Where(r => r.enabled).ToArray();
        Require(meshes.Length > 0 && Camera.main, "A visible animal mesh and main camera are required for capture.");
        Bounds bounds = meshes[0].bounds; foreach (Renderer mesh in meshes.Skip(1)) bounds.Encapsulate(mesh.bounds);
        var go = new GameObject("Temporary animal size evidence camera") { hideFlags = HideFlags.HideAndDontSave };
        Camera camera = go.AddComponent<Camera>(); camera.CopyFrom(Camera.main); camera.enabled = false;
        camera.orthographic = true; camera.orthographicSize = Mathf.Max(1.2f, bounds.extents.magnitude * 1.5f);
        camera.nearClipPlane = .05f; camera.farClipPlane = 120; camera.aspect = 1.6f; camera.cullingMask &= ~(1 << 5);
        var urp = go.AddComponent<UniversalAdditionalCameraData>(); urp.requiresDepthTexture = true;
        var source = Camera.main.GetComponent<UniversalAdditionalCameraData>();
        if (source) { urp.renderPostProcessing = source.renderPostProcessing; urp.volumeLayerMask = source.volumeLayerMask; }
        var target = new RenderTexture(1600, 1000, 24); Texture2D pixels = null;
        RenderTexture previous = RenderTexture.active;
        var grasses = Object.FindObjectsOfType<HillsideGrassRenderer>(true).Where(g => g.gameObject.scene == animal.gameObject.scene).ToArray();
        Camera[] grassViews = grasses.Select(g => g.viewCamera).ToArray();
        try
        {
            // Try nearby viewpoints; never rotate or move the animal to make a prettier photograph.
            for (int attempt = 0; attempt < 8; attempt++)
            {
                Quaternion rotation = Quaternion.Euler(35, preferredYaw + attempt * 45, 0);
                Vector3 position = bounds.center + rotation * Vector3.back * Mathf.Max(6, bounds.extents.magnitude * 5);
                camera.transform.SetPositionAndRotation(position, rotation);
                if (!Physics.Linecast(position, bounds.center, out RaycastHit hit, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore) ||
                    hit.transform.IsChildOf(animal.transform)) break;
            }
            target.Create(); camera.targetTexture = target;
            foreach (HillsideGrassRenderer grass in grasses) { grass.viewCamera = camera; grass.DrawForCamera(camera, true); }
            camera.Render(); RenderTexture.active = target;
            pixels = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
            pixels.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); pixels.Apply();
            string path = Path.Combine(folder, "size_" + animal.speciesId + "_" + animal.spawnId + "_" + preferredYaw + ".png");
            File.WriteAllBytes(path, pixels.EncodeToPNG()); return path;
        }
        finally
        {
            for (int i = 0; i < grasses.Length; i++) if (grasses[i]) grasses[i].viewCamera = grassViews[i];
            RenderTexture.active = previous; camera.targetTexture = null;
            if (pixels) Object.DestroyImmediate(pixels);
            target.Release(); Object.DestroyImmediate(target); Object.DestroyImmediate(go);
        }
    }
    // Preserve nested evidence when this class is loaded as an external DLL after Unity's type-cache scan.
    static string Json(object value)
    {
        if (value == null) return "null";
        if (value is string text) return "\"" + text.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "\\r").Replace("\n", "\\n") + "\"";
        if (value is bool boolean) return boolean ? "true" : "false";
        if (value.GetType().IsPrimitive) return Convert.ToString(value, CultureInfo.InvariantCulture);
        if (value is IEnumerable array) return "[" + string.Join(",", array.Cast<object>().Select(Json)) + "]";
        return "{" + string.Join(",", value.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public).Select(f => Json(f.Name) + ":" + Json(f.GetValue(value)))) + "}";
    }
    static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
}
