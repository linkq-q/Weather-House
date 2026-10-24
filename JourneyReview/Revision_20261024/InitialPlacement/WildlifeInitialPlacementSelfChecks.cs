using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using WeatherHouse.Hillside;
using Object = UnityEngine.Object;

namespace WeatherHouse.Review
{
    public static class WildlifeInitialPlacementSelfChecks
    {
        // This creates a disposable fixture only in the explicitly marked independent validation project.
        public static void Run()
        {
            Require(!EditorApplication.isPlayingOrWillChangePlaymode &&
                File.Exists(Path.Combine(Path.GetDirectoryName(Application.dataPath), ".wildlife-journey-validation")),
                "Self-checks require Edit mode in the marked independent validation project.");
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            TerrainData data = null;
            string report = "";
            try
            {
                Vector3 origin = new Vector3(2500, 3, 2500);
                data = new TerrainData { heightmapResolution = 33, size = new Vector3(20, 2, 20) };
                GameObject terrainObject = Terrain.CreateTerrainGameObject(data);
                terrainObject.transform.position = origin + new Vector3(-10, 0, -10);
                var root = new GameObject("Rotated parent-pivot prediction fixture");
                WildlifePopulation population = root.AddComponent<WildlifePopulation>();
                population.worldSeed = 20261024; population.terrain = terrainObject.GetComponent<Terrain>();
                population.worldBounds = new Bounds(origin, new Vector3(40, 40, 40));
                var animal = new GameObject("Predict scale without initializing"); animal.transform.SetParent(root.transform);
                animal.transform.position = origin + Vector3.up * 2;
                animal.transform.rotation = Quaternion.Euler(0, 37, 0);
                WildlifeAgent agent = animal.AddComponent<WildlifeAgent>();
                agent.speciesId = "audit-probe"; agent.spawnId = 777; agent.locomotion = WildlifeLocomotion.Ground;
                agent.scaleRange = new Vector2(.78f, 1.24f); agent.bodyRadius = .3f;
                agent.habitatCenter = origin; agent.habitatRadius = 10;
                var pivot = new GameObject("Offset rotated parent pivot"); pivot.transform.SetParent(animal.transform, false);
                pivot.transform.localPosition = new Vector3(.2f, .1f, -.3f);
                pivot.transform.localRotation = Quaternion.Euler(0, 23, 0);
                pivot.transform.localScale = new Vector3(.9f, 1.1f, 1.2f);
                var visual = new GameObject("Visual root"); visual.transform.SetParent(pivot.transform, false);
                visual.transform.localPosition = new Vector3(.1f, .2f, .35f);
                visual.transform.localRotation = Quaternion.Euler(0, 14, 0);
                visual.transform.localScale = new Vector3(.7f, 1.3f, .8f);
                agent.visualRoot = visual.transform;
                GameObject mesh = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Object.DestroyImmediate(mesh.GetComponent<Collider>());
                mesh.transform.SetParent(visual.transform, false);
                mesh.transform.localPosition = new Vector3(.3f, .4f, .1f);
                mesh.transform.localRotation = Quaternion.Euler(0, 19, 0);
                mesh.transform.localScale = new Vector3(.6f, 1.4f, 1.1f);
                population.agents = new[] { agent };
                Require(EditorSceneManager.SaveScene(scene, "Assets/InitialPlacementSelfCheck.unity"), "Disposable scene could not be saved.");

                Transform[] transforms = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true)).ToArray();
                Vector3[] positions = transforms.Select(t => t.localPosition).ToArray(), scales = transforms.Select(t => t.localScale).ToArray();
                Quaternion[] rotations = transforms.Select(t => t.localRotation).ToArray();
                Require(!scene.isDirty, "The self-check must start the audit with a clean scene.");
                var actualReport = WildlifeInitialPlacementAudit.RunReport();
                string output = actualReport.outputDirectory;
                Require(File.ReadAllText(Path.Combine(output, "report.json")).Contains("\"animals\":[{"),
                    "The external DLL's JSON omitted its nested animal records.");
                Require(actualReport.animals.Length == 1 && actualReport.animals[0].runtimePredictionReliable,
                    "The ordinary rotated visual-root fixture was not predicted reliably.");
                Require(!scene.isDirty && !actualReport.dirtyBefore && !actualReport.dirtyAfter && actualReport.transformsUnchanged,
                    "Read-only audit changed the clean scene's dirty state or reported a transform change.");
                for (int i = 0; i < transforms.Length; i++)
                    Require(transforms[i].localPosition == positions[i] && transforms[i].localRotation == rotations[i] && transforms[i].localScale == scales[i],
                        "Read-only audit changed a fixture transform.");
                WildlifeInitialPlacementAudit.Animal expected = actualReport.animals[0];
                agent.Initialize(population);
                Require((agent.BodyEnvelope.center - expected.bodyCenter).magnitude < .002f &&
                    (agent.BodyEnvelope.half - expected.bodyHalf).magnitude < .002f &&
                    (agent.transform.position - expected.predictedStartPosition).magnitude < .0002f &&
                    Mathf.Abs(agent.Radius - expected.predictedRadius) < .00001f,
                    "Predicted scale/body/start position differs from actual Agent.Initialize: predicted center=" + expected.bodyCenter.ToString("F5") +
                    " actual=" + agent.BodyEnvelope.center.ToString("F5") + "; predicted half=" + expected.bodyHalf.ToString("F5") +
                    " actual=" + agent.BodyEnvelope.half.ToString("F5"));
                report = "PASS read-only audit preserved every fixture transform and clean scene dirty=False\n" +
                    "PASS deterministic scale/body/feet prediction matches actual Agent.Initialize with rotated root, offset rotated parent and nonuniform visual scale\n" +
                    "scale=" + expected.predictedScale.ToString("F6") + "; editY=" + expected.position.y.ToString("F3") +
                    "; predicted/actualY=" + agent.transform.position.y.ToString("F3") + "; report=" + output;
            }
            catch (Exception error) { report = "FAIL " + error; throw; }
            finally
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                if (data) Object.DestroyImmediate(data);
                File.WriteAllText(Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "self-checks.txt"), report);
            }
            Debug.Log(report);
        }
        private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    }
}
