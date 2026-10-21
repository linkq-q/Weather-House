using System;
using UnityEditor;
using UnityEngine;

namespace WeatherHouse.Vegetation.Editor
{
    public static class GrassCameraChecks
    {
        public static void Verify(GrassLabCamera camera)
        {
            if (!camera || !camera.meadow) throw new InvalidOperationException("Camera checks require a connected meadow.");
            string state = EditorJsonUtility.ToJson(camera);
            Vector3 savedPosition = camera.transform.position;
            Quaternion savedRotation = camera.transform.rotation;
            bool componentDirty = EditorUtility.IsDirty(camera), transformDirty = EditorUtility.IsDirty(camera.transform);
            CursorLockMode savedLock = Cursor.lockState;
            bool savedVisibility = Cursor.visible;
            try
            {
                camera.moveSpeed = 4; camera.flySpeed = 7; camera.sprintMultiplier = 3; camera.lookSensitivity = 2;
                camera.Overview();
                Assert(camera.mode == GrassCameraMode.Orbit, "Overview enters Orbit");
                Vector3 originalTarget = camera.target;
                camera.StepNavigation(new Vector3(1, 0, 1), Vector2.zero, false, 1);
                Near(Vector3.Distance(originalTarget, camera.target), 4, "Orbit diagonal speed is normalized");
                camera.CloseGrass();
                Near(camera.distance, 4.5f, "Original close-grass preset retained");
                camera.Shore();
                Near(camera.distance, 18.5f, "Original shoreline preset retained");

                camera.SetMode(GrassCameraMode.Fly);
                camera.transform.position = new Vector3(-15, 10, -15);
                camera.yaw = 0; camera.pitch = 0;
                camera.StepNavigation(Vector3.up, Vector2.zero, false, 1);
                Near(camera.transform.position.y, 17, "Fly ascends");
                camera.StepNavigation(Vector3.down, Vector2.zero, false, 1);
                Near(camera.transform.position.y, 10, "Fly descends");
                Vector3 start = camera.transform.position;
                camera.StepNavigation(new Vector3(1, 1, 1), Vector2.zero, false, 1);
                Near(Vector3.Distance(start, camera.transform.position), 7, "Fly diagonal speed is normalized");
                camera.transform.position = new Vector3(-15, 10, -15);
                camera.StepNavigation(Vector3.forward, Vector2.zero, true, .25f);
                Near(camera.transform.position.z, -9.75f, "Sprint multiplier applied");
                camera.StepNavigation(Vector3.zero, new Vector2(3, -2), false, 0);
                Near(camera.yaw, 6, "Mouse horizontal look");
                Near(camera.pitch, 4, "Mouse vertical look");
                camera.transform.position = new Vector3(-15, -10, -15);
                camera.StepNavigation(Vector3.down, Vector2.zero, false, 1);
                Near(camera.transform.position.y, camera.meadow.GetGroundHeight(-15, -15) + .15f, "Fly stays above ground");
                camera.transform.position = new Vector3(39.4f, 10, 34.4f);
                camera.yaw = 0; camera.pitch = 0;
                camera.StepNavigation(new Vector3(1, 0, 1), Vector2.zero, true, 1);
                Assert(camera.transform.position.x <= 39.5f && camera.transform.position.z <= 34.5f, "Fly respects terrain bounds");

                camera.transform.position = new Vector3(-15, 20, -15);
                camera.SetMode(GrassCameraMode.Walk);
                Near(camera.transform.position.y, camera.meadow.GetGroundHeight(-15, -15) + camera.eyeHeight, "Walk snaps to eye height");
                start = camera.transform.position;
                camera.yaw = 0; camera.pitch = 0;
                camera.StepNavigation(new Vector3(1, 1, 1), Vector2.zero, false, .5f);
                Vector2 horizontal = new Vector2(camera.transform.position.x - start.x, camera.transform.position.z - start.z);
                Near(horizontal.magnitude, 2, "Walk ignores vertical movement and normalizes diagonals");
                Near(camera.transform.position.y, camera.meadow.GetGroundHeight(camera.transform.position.x, camera.transform.position.z) + camera.eyeHeight, "Walk follows changing ground");

                Vector2 pond = camera.meadow.pondCenter;
                camera.SetMode(GrassCameraMode.Fly);
                camera.transform.position = new Vector3(pond.x, 8, pond.y);
                camera.SetMode(GrassCameraMode.Walk);
                Assert(camera.meadow.GetGroundHeight(camera.transform.position.x, camera.transform.position.z) > camera.meadow.waterLevel + .20f,
                    "Switching to Walk over pond finds safe land");
                camera.SetMode(GrassCameraMode.Fly);
                camera.transform.position = new Vector3(pond.x + camera.meadow.pondRadius + 2, 8, pond.y);
                camera.SetMode(GrassCameraMode.Walk);
                Vector3 bank = camera.transform.position;
                camera.yaw = -90;
                camera.StepNavigation(Vector3.forward, Vector2.zero, true, 2);
                Assert(camera.meadow.GetGroundHeight(camera.transform.position.x, camera.transform.position.z) > camera.meadow.waterLevel + .20f,
                    "Walk cannot enter water during a long sprint step");
                Assert(Vector3.Distance(bank, camera.transform.position) < 3, "Walk cannot tunnel across pond");
                start = camera.transform.position;
                camera.SetMode(GrassCameraMode.Fly);
                Near(Vector3.Distance(start, camera.transform.position), 0, "Walk to Fly preserves position");
                camera.SetMode(GrassCameraMode.Orbit);
                Near(Vector3.Distance(start, camera.transform.position), 0, "Fly to Orbit preserves position");
                Debug.Log("Grass camera checks PASS: presets; walking; flying; height; shore exclusion; bounds; diagonal speed; sprint; look; mode transitions.");
            }
            finally
            {
                EditorJsonUtility.FromJsonOverwrite(state, camera);
                camera.SetMode(camera.mode);
                EditorJsonUtility.FromJsonOverwrite(state, camera);
                camera.transform.SetPositionAndRotation(savedPosition, savedRotation);
                if (!componentDirty) EditorUtility.ClearDirty(camera);
                if (!transformDirty) EditorUtility.ClearDirty(camera.transform);
                Cursor.lockState = savedLock; Cursor.visible = savedVisibility;
            }
        }

        static void Assert(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("Grass camera check failed: " + message);
        }
        static void Near(float actual, float expected, string message)
        {
            Assert(Mathf.Abs(actual - expected) < .002f, message + " (" + actual + " vs " + expected + ")");
        }
    }
}
