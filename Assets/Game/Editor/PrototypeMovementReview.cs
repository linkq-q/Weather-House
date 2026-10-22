using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace WeatherHousePrototype.Editor
{
    public static class PrototypeMovementReview
    {
        private const float StepTime = 1f / 60f;

        [MenuItem("Weather House/Check Runtime Movement")]
        public static void Run()
        {
            if (!EditorApplication.isPlaying)
                throw new InvalidOperationException("Movement review requires Play mode.");

            var walker = Object.FindObjectOfType<PrototypeWalker>();
            var terrain = Object.FindObjectOfType<Terrain>();
            if (walker == null || terrain == null)
                throw new InvalidOperationException("Movement review needs the prototype player and Terrain.");
            var controller = walker.GetComponent<CharacterController>();
            if (controller == null || !controller.enabled || walker.moveSpeed <= 0f)
                throw new InvalidOperationException("The player's controller must be enabled and moveSpeed positive.");

            var original = walker.transform.position;
            var report = new List<string>
            {
                "Play-mode CharacterController.Move review; 8 m horizontal input per segment.",
                "Existing controller and colliders retained. Simulated 1/60 s movement steps; no input/camera test.",
                "Player position is restored after review."
            };
            Directory.CreateDirectory("PrototypeReview");
            try
            {
                Physics.SyncTransforms();
                bool passed = Walk("West flat path", new Vector2(.184f, .601f),
                    new Vector2(.173f, .547f), walker, controller, terrain, report);
                passed &= Walk("North shore path", new Vector2(.37f, .235f),
                    new Vector2(.46f, .22f), walker, controller, terrain, report);
                passed &= Walk("South uphill path", new Vector2(.609f, .812f),
                    new Vector2(.692f, .818f), walker, controller, terrain, report);
                report.Add("Result: " + (passed ? "PASS" : "NEEDS REVIEW"));
            }
            catch (Exception exception)
            {
                report.Add("Result: ERROR\n" + exception);
                throw;
            }
            finally
            {
                walker.Teleport(original);
                Physics.SyncTransforms();
                report.Add("Restored player position: " + walker.transform.position.ToString("F3"));
                File.WriteAllLines("PrototypeReview/movement.txt", report);
            }
        }

        private static bool Walk(string name, Vector2 from, Vector2 toward, PrototypeWalker walker,
            CharacterController controller, Terrain terrain, List<string> report)
        {
            Vector3 size = terrain.terrainData.size;
            Vector3 origin = terrain.transform.position;
            from = new Vector2(origin.x + from.x * size.x, origin.z + (1f - from.y) * size.z);
            toward = new Vector2(origin.x + toward.x * size.x, origin.z + (1f - toward.y) * size.z);
            Vector3 start = new Vector3(from.x, 0f, from.y);
            start.y = terrain.SampleHeight(start) + terrain.transform.position.y + 0.15f;
            walker.Teleport(start);
            Physics.SyncTransforms();
            bool groundedBefore = Settle(controller);
            start = walker.transform.position;

            Vector3 direction = new Vector3(toward.x - from.x, 0f, toward.y - from.y).normalized;
            float remaining = 8f;
            float verticalSpeed = -2f;
            int groundedSteps = 0, steps = 0;
            while (remaining > 0.0001f)
            {
                float horizontalStep = Mathf.Min(remaining, walker.moveSpeed * StepTime);
                if (controller.isGrounded && verticalSpeed < 0f)
                    verticalSpeed = -2f;
                verticalSpeed -= 20f * StepTime;
                controller.Move(direction * horizontalStep + Vector3.up * (verticalSpeed * StepTime));
                if (controller.isGrounded)
                    groundedSteps++;
                steps++;
                remaining -= horizontalStep;
            }

            bool groundedAfter = Settle(controller);
            Vector3 end = walker.transform.position;
            Vector3 horizontal = new Vector3(end.x - start.x, 0f, end.z - start.z);
            float forward = Vector3.Dot(horizontal, direction);
            float sideways = (horizontal - direction * forward).magnitude;
            float groundGap = end.y - terrain.SampleHeight(end) - terrain.transform.position.y;
            bool passed = groundedBefore && groundedAfter && forward >= 7.5f && forward <= 8.5f &&
                sideways < 0.5f && Mathf.Abs(groundGap) < 0.3f;
            report.Add(string.Format(
                "{0}: {1}; start={2}; end={3}; forward={4:F3} m; sideways={5:F3} m; rise={6:F3} m; " +
                "grounded start/end={7}/{8}; grounded movement steps={9}/{10}; terrain gap={11:F3} m",
                name, passed ? "PASS" : "NEEDS REVIEW", start.ToString("F3"), end.ToString("F3"),
                forward, sideways, end.y - start.y, groundedBefore, groundedAfter, groundedSteps, steps, groundGap));
            return passed;
        }

        private static bool Settle(CharacterController controller)
        {
            float speed = -2f;
            for (int i = 0; i < 90; i++)
            {
                speed -= 20f * StepTime;
                controller.Move(Vector3.up * (speed * StepTime));
                if (controller.isGrounded)
                    return true;
            }
            return false;
        }
    }
}
