using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using WeatherHouse.Hillside;
using Object = UnityEngine.Object;

namespace WeatherHouse.WatersideV2.Editor
{
    public static class WatersideAnimalScaleChecks
    {
        public static string Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Edit-mode fixture only.");
            Scene original = SceneManager.GetActiveScene(); bool dirty = original.isDirty;
            PureChecks();
            Scene test = EditorSceneManager.NewPreviewScene();
            try
            {
                var owner = EditorUtility.CreateGameObjectWithHideFlags("Scale checks population", HideFlags.HideAndDontSave,
                    typeof(WildlifePopulation)).GetComponent<WildlifePopulation>();
                SceneManager.MoveGameObjectToScene(owner.gameObject, test);
                owner.worldBounds = new Bounds(Vector3.up * 10, new Vector3(500, 20, 500));
                Assert(WatersideAnimalScale.PredictStart(owner, .3f, Vector3.up * 4, out Vector3 projected), "Raised edit position can project");
                Near(projected, Vector3.zero, "Preview must check the runtime ground height");
                Assert(!WatersideAnimalScale.PredictStart(owner, .3f, new Vector3(250, 4, 0), out _), "Projected body must stay in world bounds");
                foreach (string species in new[] { "turtle", "squirrel" })
                    foreach (int id in new[] { 14, 43, 47 })
                    {
                        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/World/Hillside/Generated/" + species + ".prefab");
                        var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, test);
                        var agent = instance.GetComponent<WildlifeAgent>(); agent.spawnId = id;
                        float multiplier = species == "turtle" ? 3 : .5f;
                        Vector3 position = agent.transform.position, rootScale = agent.transform.localScale;
                        agent.transform.position = position + Vector3.up * 4;
                        Assert(WatersideAnimalScale.PredictStart(owner, agent.bodyRadius, agent.transform.position, out Vector3 predictedStart), "Prefab starting height can project");
                        Quaternion rotation = agent.transform.rotation;
                        float walk = agent.walkSpeed, run = agent.runSpeed, baseRadius = agent.bodyRadius;
                        agent.Initialize(owner);
                        Near(agent.transform.position, predictedStart, "Read-only start prediction agrees with actual Initialize");
                        float oldFactor = agent.ScaleFactor, oldRadius = agent.Radius;
                        Vector3 oldVisual = agent.visualRoot.localScale, oldOffset = agent.visualRoot.localPosition;
                        WildlifeBodyEnvelope oldBody = agent.BodyEnvelope;
                        Assert(oldBody.measured && oldBody.Valid, "Actual prefab body must be measured.");
                        agent.scaleRange = WatersideAnimalScale.ForNewInstance(species, agent.scaleRange);
                        agent.Initialize(owner);
                        Near(agent.ScaleFactor, oldFactor * multiplier, "Deterministic individual size ratio");
                        Near(agent.Radius, oldRadius * multiplier, "Collision radius ratio");
                        Near(agent.visualRoot.localScale, oldVisual * multiplier, "Visual size ratio");
                        Near(agent.visualRoot.localPosition, oldOffset * multiplier, "Foot offset ratio");
                        Near(agent.BodyEnvelope.half, oldBody.half * multiplier, "Measured full body ratio");
                        Vector3 scaled = agent.visualRoot.localScale;
                        agent.Initialize(owner); Near(agent.visualRoot.localScale, scaled, "Reinitialize must not accumulate scaling");
                        Near(agent.transform.position, position, "Root position unchanged");
                        Near(agent.transform.localScale, rootScale, "Root scale unchanged");
                        Assert(agent.transform.rotation == rotation && agent.walkSpeed == walk && agent.runSpeed == run &&
                            agent.bodyRadius == baseRadius, "Rotation, speed and base radius unchanged");
                        Object.DestroyImmediate(instance);
                    }
            }
            finally { EditorSceneManager.ClosePreviewScene(test); }
            Assert(SceneManager.GetActiveScene() == original && original.isDirty == dirty, "User scene state unchanged");
            string path = "JourneyReview/Revision_20261024/AnimalScale/prefab-checks.txt";
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, "PASS\n" + DateTime.UtcNow.ToString("O") + "\nPure range/idempotence/override guards and six actual prefab initialization cases passed. Main scene not changed.\n");
            return path;
        }

        public static void PureChecks()
        {
            Near(WatersideAnimalScale.ForNewInstance("turtle", new Vector2(.82f, 1.16f)), new Vector2(2.46f, 3.48f), "Turtle target");
            Near(WatersideAnimalScale.ForNewInstance("squirrel", new Vector2(.82f, 1.16f)), new Vector2(.41f, .58f), "Squirrel target");
            Near(WatersideAnimalScale.ForNewInstance("turtle", new Vector2(.91f, 1.07f)), new Vector2(2.73f, 3.21f), "New instance authored range preserved");
            Near(WatersideAnimalScale.ForNewInstance("fox", new Vector2(.9f, 1.2f)), new Vector2(.9f, 1.2f), "Other species unchanged");
            foreach (string species in new[] { "turtle", "squirrel" })
            {
                int id = species == "turtle" ? 43 : 14;
                Vector2 target = WatersideAnimalScale.TargetForExisting(species, id, new Vector2(.82f, 1.16f));
                Near(WatersideAnimalScale.TargetForExisting(species, id, target), target, "Migration is idempotent");
                Reject(() => WatersideAnimalScale.TargetForExisting(species, id, new Vector2(.9f, 1.2f)), "Existing override must be retained");
            }
            Reject(() => WatersideAnimalScale.TargetForExisting("turtle", 14, new Vector2(.82f, 1.16f)), "Unknown identity");
            Reject(() => WatersideAnimalScale.ForNewInstance("squirrel", new Vector2(.1f, .2f)), "Would trigger runtime scale clamp");
            Reject(() => WatersideAnimalScale.ForNewInstance("turtle", new Vector2(float.NaN, 1)), "Nonfinite range");
            var squirrelStart = new Vector3(-2.4301033f, 4.48882f, -1.58714676f);
            var turtleStart = new Vector3(-6.695938f, 4.48918152f, -11.4543962f);
            Quaternion squirrelHeading = Quaternion.Euler(0, 223, 0), turtleHeading = Quaternion.Euler(0, 131, 0);
            Vector3 squirrelTarget = WatersideAnimalScale.TargetPositionForExisting("squirrel", 14, squirrelStart, squirrelHeading);
            Vector3 turtleTarget = WatersideAnimalScale.TargetPositionForExisting("turtle", 44, turtleStart, turtleHeading);
            Near(squirrelTarget, new Vector3(-2.5551033f, 4.489524f, -1.58714676f), "Reviewed squirrel destination");
            Near(turtleTarget, new Vector3(-5.823142f, 4.47956467f, -10.5595627f), "Reviewed turtle destination");
            Near(WatersideAnimalScale.TargetPositionForExisting("squirrel", 14, squirrelTarget, squirrelHeading), squirrelTarget, "Squirrel relocation idempotent");
            Near(WatersideAnimalScale.TargetPositionForExisting("turtle", 44, turtleTarget, turtleHeading), turtleTarget, "Turtle relocation idempotent");
            Near(WatersideAnimalScale.TargetPositionForExisting("squirrel", 15, squirrelStart, turtleHeading), squirrelStart, "Other squirrel never moves");
            Near(WatersideAnimalScale.TargetPositionForExisting("turtle", 43, turtleStart, squirrelHeading), turtleStart, "Other turtle never moves");
            Near(WatersideAnimalScale.TargetPositionForExisting("fox", 14, squirrelStart, turtleHeading), squirrelStart, "Identity includes species");
            Reject(() => WatersideAnimalScale.TargetPositionForExisting("squirrel", 14, squirrelStart + Vector3.right * .01f, squirrelHeading), "User position override retained");
            Reject(() => WatersideAnimalScale.TargetPositionForExisting("turtle", 44, turtleStart, Quaternion.Euler(0, 132, 0)), "User heading override retained");
        }

        static void Reject(Action action, string message)
        { try { action(); } catch (InvalidOperationException) { return; } throw new Exception(message); }
        static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
        static void Near(float a, float b, string message) => Assert(Mathf.Abs(a - b) < .0001f, message + ": " + a + " / " + b);
        static void Near(Vector2 a, Vector2 b, string message) => Assert((a - b).magnitude < .0001f, message);
        static void Near(Vector3 a, Vector3 b, string message) => Assert((a - b).magnitude < .0002f, message);
    }
}
