using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace WeatherHousePrototype
{
    public static class PhotoCameraChecks
    {
        [MenuItem("Weather House/Check Photo Camera Rig")]
        [MenuItem("Weather House/Review 20261027/Check photo camera rig")]
        public static void Run()
        {
            var report = new List<string>
            {
                "Photo camera component and geometry checks; no photographs rendered by this check.",
                DateTime.Now.ToString("O")
            };
            GameObject fixture = null;
            try
            {
                fixture = new GameObject("Isolated photo camera check");
                fixture.SetActive(false);
                fixture.transform.position = new Vector3(10000f, 10000f, 10000f);
                var playerObject = new GameObject("Fixture player");
                playerObject.transform.SetParent(fixture.transform, false);
                PrototypeWalker walker = playerObject.AddComponent<PrototypeWalker>();
                walker.enabled = false;
                walker.GetComponent<CharacterController>().enabled = false;
                walker.yaw = 23f;
                walker.pitch = 17f;
                walker.viewCamera = MakeCamera(playerObject.transform, "Walking camera", true);
                walker.mapCamera = MakeCamera(playerObject.transform, "Map camera", false);
                walker.visualRoot = new GameObject("Player visual").transform;
                walker.visualRoot.SetParent(playerObject.transform, false);
                Renderer visible = MakeCube(walker.visualRoot, "Visible avatar", new Vector3(0f, .8f, 0f), Vector3.one, true);
                Renderer hidden = MakeCube(walker.visualRoot, "Already hidden avatar", new Vector3(0f, 1.55f, 1f), Vector3.one * .3f, true);
                hidden.enabled = false;
                var rig = playerObject.AddComponent<PhotoCameraRig>();
                fixture.SetActive(true);
                // Awake sets the normal walking view in Play mode; use the same state in Edit mode.
                walker.SetOverview(false);
                rig.Initialize(walker);
                int childCount = fixture.GetComponentsInChildren<Transform>(true).Length;
                rig.Initialize(walker);
                Require(childCount == fixture.GetComponentsInChildren<Transform>(true).Length, "Repeated Initialize duplicated camera objects.");

                rig.SetActive(true);
                Camera photo = rig.CaptureCamera;
                Vector3 expectedEye = walker.transform.position + Vector3.up * 1.55f +
                    Quaternion.Euler(17f, 23f, 0f) * Vector3.forward * .2f;
                Require(rig.Active && photo != null && photo.enabled, "Photo mode did not enable its independent camera.");
                Require(Vector3.Distance(photo.transform.position, expectedEye) < .003f, "Photo camera is not at the player's eye position.");
                Require(!walker.viewCamera.enabled && !walker.mapCamera.enabled && visible.forceRenderingOff && hidden.forceRenderingOff,
                    "Photo mode did not hide both old cameras and the avatar renderers.");
                Require(!walker.viewCamera.GetComponent<AudioListener>().enabled &&
                    !walker.mapCamera.GetComponent<AudioListener>().enabled && photo.GetComponent<AudioListener>().enabled,
                    "Photo mode audio listeners were not switched correctly.");
                Require(photo.usePhysicalProperties && photo.sensorSize == new Vector2(36f, 24f) && Mathf.Approximately(rig.FocalLength, 24f),
                    "The default physical lens is not 24 mm on a 36 x 24 mm sensor.");
                float originalFov = photo.fieldOfView;
                rig.Zoom(100f);
                Require(Mathf.Approximately(rig.FocalLength, 100f) && photo.fieldOfView < originalFov,
                    "Zoom did not reach 100 mm and narrow the field of view.");
                float telephotoFov = photo.fieldOfView;
                rig.Zoom(-100f);
                Require(Mathf.Approximately(rig.FocalLength, 24f), "The wide lens limit is not 24 mm.");
                report.Add("PASS: independent eye-height camera, avatar visibility, listener switching, initialization idempotence and 24-100 mm zoom.");
                report.Add("Vertical FOV at 24/100 mm: " + originalFov.ToString("F2") + " / " + telephotoFov.ToString("F2") + " degrees.");

                walker.yaw = 0f;
                walker.pitch = 0f;
                Vector3 eye = walker.transform.position + Vector3.up * 1.55f + Vector3.forward * .2f;
                Renderer target = MakeCube(fixture.transform, "Physical focus target", eye + Vector3.forward * 5f,
                    Vector3.one, true, true);
                Require(!target.transform.IsChildOf(walker.transform), "The focus fixture target must be outside the player's own hierarchy.");
                Physics.SyncTransforms();
                Vector2 center = photo.pixelRect.center;
                rig.FocusAt(center);
                Require(Mathf.Abs(rig.FocusDistance - 4.5f) < .03f, "Center focus missed the physical target at 4.5 m; got " + rig.FocusDistance);
                Volume volume = photo.GetComponentInChildren<Volume>(true);
                Require(volume != null && volume.enabled && volume.sharedProfile != null &&
                    volume.sharedProfile.TryGet(out DepthOfField depth), "The dedicated focus volume/profile is missing.");
                Require(volume.sharedProfile.TryGet(out DepthOfField dof) && dof.mode.value == DepthOfFieldMode.Bokeh &&
                    Mathf.Abs(dof.focusDistance.value - rig.FocusDistance) < .001f && Mathf.Approximately(dof.aperture.value, 5.6f),
                    "Bokeh focus distance or aperture is not configured.");
                var originalData = walker.viewCamera.GetComponent<UniversalAdditionalCameraData>();
                Require((originalData.volumeLayerMask.value & (1 << volume.gameObject.layer)) == 0,
                    "The photo-only focus volume affects the original walking camera mask.");

                var animalObject = new GameObject("Collider-free animal focus target");
                animalObject.SetActive(false);
                animalObject.transform.SetParent(fixture.transform, false);
                animalObject.transform.position = eye + Vector3.forward * 3f;
                var visual = new GameObject("Visual").transform;
                visual.SetParent(animalObject.transform, false);
                MakeCube(visual, "Animal body", Vector3.zero, Vector3.one * .5f, false);
                var props = new GameObject("WeatherProps").transform;
                props.SetParent(animalObject.transform, false);
                MakeCube(props, "Large shelter leaf must not attract focus", new Vector3(0f, 0f, -1.3f), Vector3.one, false);
                var animal = animalObject.AddComponent<AnimalActor>();
                animal.enabled = false;
                animalObject.SetActive(true);
                Physics.SyncTransforms();
                rig.FocusAt(center);
                Require(Mathf.Abs(rig.FocusDistance - 2.75f) < .03f,
                    "Visual bounds did not focus the collider-free animal, or WeatherProps interfered; got " + rig.FocusDistance);
                target.transform.position = eye + Vector3.forward * 1.8f;
                Physics.SyncTransforms();
                rig.FocusAt(center);
                Require(Mathf.Abs(rig.FocusDistance - 1.3f) < .03f,
                    "Animal bounds incorrectly focused through a nearer physical occluder; got " + rig.FocusDistance);
                target.gameObject.SetActive(false);
                animalObject.SetActive(false);
                Physics.SyncTransforms();
                rig.FocusAt(center);
                Require(Mathf.Approximately(rig.FocusDistance, 150f), "An unobstructed empty view did not use the far focus limit.");
                report.Add("PASS: physical focus at 4.5 m; collider-free Visual bounds at 2.75 m; WeatherProps excluded; nearer occlusion at 1.3 m; far focus at 150 m.");
                report.Add("PASS: separate Bokeh volume configuration, f/5.6, focus distance update and original camera volume-mask isolation.");

                rig.SetActive(false);
                Require(!rig.Active && !photo.enabled && !volume.enabled, "Exiting photo mode left its camera or focus volume enabled.");
                Require(walker.viewCamera.enabled && !walker.mapCamera.enabled && visible.enabled && !hidden.enabled &&
                    !visible.forceRenderingOff && !hidden.forceRenderingOff,
                    "Exiting photo mode did not restore each original camera/renderer state.");
                Require(walker.viewCamera.GetComponent<AudioListener>().enabled &&
                    !walker.mapCamera.GetComponent<AudioListener>().enabled && !photo.GetComponent<AudioListener>().enabled,
                    "Exiting photo mode did not restore audio listeners.");
                Require(Mathf.Approximately(walker.yaw, 23f) && Mathf.Approximately(walker.pitch, 17f), "Original yaw/pitch were not restored.");
                walker.SetOverview(true);
                rig.SetActive(true);
                rig.SetActive(false);
                Require(walker.mapCamera.enabled && !walker.viewCamera.enabled && walker.mapCamera.GetComponent<AudioListener>().enabled,
                    "Entering photo mode from the map did not restore the map on exit.");
                report.Add("PASS: camera, listener, individual renderer, yaw/pitch and previous map mode restore correctly.");
                report.Add("PASS: photo camera component/geometry checks completed. Rendered appearance and photographed pixels require the separate visual review.");
            }
            catch (Exception exception)
            {
                report.Add("FAIL: " + exception);
                throw;
            }
            finally
            {
                if (fixture != null)
                {
                    fixture.GetComponentInChildren<PhotoCameraRig>(true)?.SetActive(false);
                    fixture.SetActive(false);
                    Object.DestroyImmediate(fixture);
                }
                Directory.CreateDirectory("PrototypeReview");
                File.WriteAllLines("PrototypeReview/photo-camera-checks.txt", report);
            }
        }

        private static Camera MakeCamera(Transform parent, string name, bool enabled)
        {
            var child = new GameObject(name);
            child.transform.SetParent(parent, false);
            var camera = child.AddComponent<Camera>();
            camera.enabled = enabled;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
            camera.farClipPlane = 200f;
            camera.fieldOfView = 60f;
            child.AddComponent<AudioListener>().enabled = enabled;
            var data = child.AddComponent<UniversalAdditionalCameraData>();
            data.volumeLayerMask = 1;
            data.renderPostProcessing = true;
            return camera;
        }

        private static Renderer MakeCube(Transform parent, string name, Vector3 position, Vector3 scale, bool collider, bool worldPosition = false)
        {
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = name;
            cube.transform.SetParent(parent, false);
            if (worldPosition) cube.transform.position = position;
            else cube.transform.localPosition = position;
            cube.transform.localScale = scale;
            cube.GetComponent<Collider>().enabled = collider;
            return cube.GetComponent<Renderer>();
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
