using System;
using System.Collections.Generic;
using WeatherHouse.Hillside;
using WeatherHouse.WildlifeJourney;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using ForestSpriteCharacter;

namespace WeatherHousePrototype
{
    [DisallowMultipleComponent]
    public class PhotoCameraRig : MonoBehaviour
    {
        private const int FocusVolumeLayer = 28;
        public Camera CaptureCamera => photoCamera;
        public bool Active { get; private set; }
        public float FocalLength { get; private set; } = 24f;
        public float FocusDistance { get; private set; } = 8f;
        public string FocusLabel => (focusedOnSurface ? "对焦 " : "远景 ") + FocusDistance.ToString("F1") + " m" +
            (focusEffect != null && GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset && focusEffect.IsActive()
                ? string.Empty : " · 景深暂不可用");

        private PrototypeWalker walker;
        private ForestSpriteDemoController forestSprite;
        private float photoYaw, photoPitch;
        private Camera previousMovementCamera;
        private readonly Dictionary<JourneySocial, Camera> socialCameras = new Dictionary<JourneySocial, Camera>();
        private readonly Dictionary<HillsideGrassRenderer, Camera> grassCameras = new Dictionary<HillsideGrassRenderer, Camera>();
        private Transform PlayerTransform => forestSprite ? forestSprite.transform : walker ? walker.transform : null;
        private Camera ViewCamera => forestSprite ? forestSprite.demoCamera : walker ? walker.viewCamera : null;
        private Camera MapCamera => walker ? walker.mapCamera : null;
        private Transform VisualRoot => forestSprite ? forestSprite.transform : walker ? walker.visualRoot : null;
        private bool InputBlocked => forestSprite ? forestSprite.InputBlocked : walker && walker.InputBlocked;
        private float Yaw { get => forestSprite ? photoYaw : walker.yaw; set { if (forestSprite) photoYaw = value; else walker.yaw = value; } }
        private float Pitch { get => forestSprite ? photoPitch : walker.pitch; set { if (forestSprite) photoPitch = value; else walker.pitch = value; } }
        private Camera photoCamera;
        private AudioListener photoListener, viewListener, mapListener;
        private Volume focusVolume;
        private VolumeProfile focusProfile;
        private DepthOfField focusEffect;
        private Vignette vignetteEffect;
        private Renderer[] playerRenderers;
        private bool[] rendererForcedOff;
        private bool previousViewEnabled, previousMapEnabled, previousViewAudio, previousMapAudio;
        private float previousYaw, previousPitch;
        private bool focusedOnSurface = true;

        public void Initialize(PrototypeWalker player)
        {
            if (player == null || player.viewCamera == null || player.mapCamera == null)
                throw new ArgumentException("PhotoCameraRig needs a player with walking and map cameras.", nameof(player));
            if (walker != player || forestSprite)
            {
                SetActive(false);
                ReleaseResources();
                walker = player;
                forestSprite = null;
            }
            EnsureCamera();
        }

        public void Initialize(ForestSpriteDemoController player)
        {
            if (!player || !player.demoCamera) throw new ArgumentException("PhotoCameraRig needs the ForestSprite player and its existing view camera.", nameof(player));
            if (forestSprite != player || walker)
            {
                SetActive(false); ReleaseResources(); walker = null; forestSprite = player;
            }
            EnsureCamera();
        }

        private void EnsureCamera()
        {
            if (photoCamera != null) return;
            var cameraObject = new GameObject("Photo Camera (runtime)") { hideFlags = HideFlags.DontSave };
            cameraObject.transform.SetParent(PlayerTransform, false);
            photoCamera = cameraObject.AddComponent<Camera>();
            photoCamera.CopyFrom(ViewCamera);
            photoCamera.enabled = false;
            photoCamera.orthographic = false;
            photoCamera.usePhysicalProperties = true;
            photoCamera.sensorSize = new Vector2(36f, 24f);
            photoCamera.gateFit = Camera.GateFitMode.Horizontal;
            photoCamera.focalLength = FocalLength;
            photoCamera.aperture = 5.6f;
            photoCamera.nearClipPlane = .05f;
            photoCamera.targetTexture = null;
            photoListener = cameraObject.AddComponent<AudioListener>();
            photoListener.enabled = false;

            var data = cameraObject.AddComponent<UniversalAdditionalCameraData>();
            var sourceData = ViewCamera.GetComponent<UniversalAdditionalCameraData>();
            data.renderType = CameraRenderType.Base;
            data.renderPostProcessing = true;
            data.requiresDepthTexture = true;
            data.requiresColorTexture = sourceData != null && sourceData.requiresColorTexture;
            data.volumeLayerMask = (sourceData != null ? sourceData.volumeLayerMask.value : 1) | (1 << FocusVolumeLayer);
            data.volumeTrigger = photoCamera.transform;
            if (sourceData != null)
            {
                data.renderShadows = sourceData.renderShadows;
                data.antialiasing = sourceData.antialiasing;
                data.antialiasingQuality = sourceData.antialiasingQuality;
                data.stopNaN = sourceData.stopNaN;
                data.dithering = sourceData.dithering;
            }
            var volumeObject = new GameObject("Photo focus volume") { layer = FocusVolumeLayer, hideFlags = HideFlags.DontSave };
            volumeObject.transform.SetParent(cameraObject.transform, false);
            focusVolume = volumeObject.AddComponent<Volume>();
            focusVolume.isGlobal = true;
            focusVolume.priority = 1000f;
            focusVolume.weight = 1f;
            focusProfile = ScriptableObject.CreateInstance<VolumeProfile>();
            focusProfile.name = "Photo focus runtime profile";
            focusProfile.hideFlags = HideFlags.DontSave;
            focusEffect = focusProfile.Add<DepthOfField>(true);
            focusEffect.mode.Override(DepthOfFieldMode.Bokeh);
            focusEffect.focusDistance.Override(FocusDistance);
            focusEffect.focalLength.Override(FocalLength);
            focusEffect.aperture.Override(5.6f);
            vignetteEffect = focusProfile.Add<Vignette>(true);
            vignetteEffect.color.Override(Color.black);
            vignetteEffect.center.Override(new Vector2(.5f, .5f));
            vignetteEffect.rounded.Override(false);
            vignetteEffect.smoothness.Override(.8f);
            // URP 14 multiplies intensity by 3 and smoothness by 5: .084 leaves about 88% at the extreme corners.
            vignetteEffect.intensity.Override(.084f);
            focusVolume.sharedProfile = focusProfile;
            focusVolume.enabled = false;
        }

        public void SetActive(bool value)
        {
            if (value == Active) return;
            if (value)
            {
                if (!PlayerTransform) throw new InvalidOperationException("Initialize PhotoCameraRig before entering photo mode.");
                EnsureCamera();
                if (forestSprite) { photoYaw = ViewCamera.transform.eulerAngles.y; photoPitch = Mathf.Clamp(forestSprite.cameraPitch, -10f, 15f); }
                previousYaw = Yaw;
                previousPitch = Pitch;
                previousViewEnabled = ViewCamera.enabled;
                previousMapEnabled = MapCamera && MapCamera.enabled;
                viewListener = ViewCamera.GetComponent<AudioListener>();
                mapListener = MapCamera ? MapCamera.GetComponent<AudioListener>() : null;
                previousViewAudio = viewListener != null && viewListener.enabled;
                previousMapAudio = mapListener != null && mapListener.enabled;
                if (walker) walker.SetOverview(false);
                ViewCamera.enabled = false;
                if (MapCamera) MapCamera.enabled = false;
                if (viewListener != null) viewListener.enabled = false;
                if (mapListener != null) mapListener.enabled = false;
                // Cape Cloth is a sibling of the model under ForestSprite; hide renderers without suspending its simulation.
                playerRenderers = VisualRoot ? VisualRoot.GetComponentsInChildren<Renderer>(true) : Array.Empty<Renderer>();
                rendererForcedOff = new bool[playerRenderers.Length];
                for (int i = 0; i < playerRenderers.Length; i++)
                {
                    rendererForcedOff[i] = playerRenderers[i].forceRenderingOff;
                    playerRenderers[i].forceRenderingOff = true;
                }
                Active = true;
                if (forestSprite) { previousMovementCamera = forestSprite.MovementCameraOverride; forestSprite.MovementCameraOverride = photoCamera; }
                foreach (var grass in FindObjectsOfType<HillsideGrassRenderer>(true))
                    if (grass.gameObject.scene == gameObject.scene) { grassCameras[grass] = grass.viewCamera; grass.viewCamera = photoCamera; }
                foreach (var social in FindObjectsOfType<JourneySocial>(true))
                    if (social.gameObject.scene == gameObject.scene) { socialCameras[social] = social.viewCamera; social.viewCamera = photoCamera; }
                UpdatePose();
                focusVolume.enabled = true;
                photoCamera.enabled = true;
                photoListener.enabled = true;
            }
            else
            {
                Active = false;
                if (forestSprite) forestSprite.MovementCameraOverride = previousMovementCamera;
                foreach (var entry in grassCameras) if (entry.Key) entry.Key.viewCamera = entry.Value;
                grassCameras.Clear();
                foreach (var entry in socialCameras) if (entry.Key) entry.Key.viewCamera = entry.Value;
                socialCameras.Clear();
                if (photoCamera != null) photoCamera.enabled = false;
                if (photoListener != null) photoListener.enabled = false;
                if (focusVolume != null) focusVolume.enabled = false;
                if (PlayerTransform)
                {
                    Yaw = previousYaw;
                    Pitch = previousPitch;
                    if (walker) walker.SetOverview(previousMapEnabled && !previousViewEnabled);
                    if (ViewCamera) ViewCamera.enabled = previousViewEnabled;
                    if (MapCamera) MapCamera.enabled = previousMapEnabled;
                }
                if (viewListener != null) viewListener.enabled = previousViewAudio;
                if (mapListener != null) mapListener.enabled = previousMapAudio;
                if (playerRenderers != null)
                    for (int i = 0; i < playerRenderers.Length; i++)
                        if (playerRenderers[i] != null) playerRenderers[i].forceRenderingOff = rendererForcedOff[i];
                playerRenderers = null;
                rendererForcedOff = null;
            }
        }

        public void SetVignetteVisibility(float visibility)
        {
            if (vignetteEffect != null) vignetteEffect.intensity.Override(.084f * Mathf.Clamp01(visibility));
        }

        private void Update()
        {
            if (!Active || !PlayerTransform || InputBlocked) return;
            if (Input.GetMouseButton(1))
            {
                Yaw += Input.GetAxis("Mouse X") * 3f;
                Pitch = Mathf.Clamp(Pitch - Input.GetAxis("Mouse Y") * 3f, -60f, 75f);
            }
            if (Mathf.Abs(Input.mouseScrollDelta.y) > .001f) Zoom(Input.mouseScrollDelta.y);
        }

        private void LateUpdate()
        {
            if (Active) UpdatePose();
        }

        private void UpdatePose()
        {
            if (!PlayerTransform || photoCamera == null) return;
            Quaternion rotation = Quaternion.Euler(Pitch, Yaw, 0f);
            float height = forestSprite ? Mathf.Max(.2f, forestSprite.cameraHeight) : 1.55f;
            photoCamera.transform.SetPositionAndRotation(PlayerTransform.position + Vector3.up * height +
                rotation * Vector3.forward * .2f, rotation);
        }

        public void Zoom(float scrollDelta)
        {
            if (!Active || float.IsNaN(scrollDelta) || float.IsInfinity(scrollDelta)) return;
            FocalLength = Mathf.Clamp(FocalLength + scrollDelta * 5f, 24f, 100f);
            photoCamera.focalLength = FocalLength;
            focusEffect.focalLength.Override(FocalLength);
        }

        public void FocusAt(Vector2 screenPoint)
        {
            if (!Active || photoCamera == null) return;
            UpdatePose();
            Ray ray = photoCamera.ScreenPointToRay(screenPoint);
            float nearest = 150f;
            Vector3 focusPoint = ray.GetPoint(nearest);
            focusedOnSurface = false;
            foreach (RaycastHit hit in Physics.RaycastAll(ray, 150f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            {
                if (hit.transform.IsChildOf(PlayerTransform) || hit.distance >= nearest) continue;
                nearest = hit.distance;
                focusPoint = hit.point;
                focusedOnSurface = true;
            }
            foreach (AnimalActor animal in FindObjectsOfType<AnimalActor>())
            {
                if (!TryGetAnimalBounds(animal, out Bounds bounds) ||
                    !bounds.IntersectRay(ray, out float distance) || distance >= nearest) continue;
                nearest = distance;
                focusPoint = ray.GetPoint(distance);
                focusedOnSurface = true;
            }
            FocusDistance = focusedOnSurface ? Mathf.Clamp(Vector3.Dot(focusPoint - photoCamera.transform.position,
                photoCamera.transform.forward), .5f, 150f) : 150f;
            focusEffect.focusDistance.Override(FocusDistance);
        }

        internal static bool TryGetAnimalBounds(AnimalActor animal, out Bounds bounds)
        {
            bounds = default;
            Transform visual = animal.VisualRoot;
            if (visual == null) return false;
            bool found = false;
            foreach (Renderer renderer in visual.GetComponentsInChildren<Renderer>())
            {
                if (!renderer.enabled) continue;
                if (found) bounds.Encapsulate(renderer.bounds);
                else { bounds = renderer.bounds; found = true; }
            }
            return found;
        }

        private void OnDisable() { SetActive(false); }

        private void OnDestroy()
        {
            SetActive(false);
            ReleaseResources();
        }

        private void ReleaseResources()
        {
            if (photoCamera != null) DestroyOwned(photoCamera.gameObject);
            if (focusEffect != null) DestroyOwned(focusEffect);
            if (vignetteEffect != null) DestroyOwned(vignetteEffect);
            if (focusProfile != null) DestroyOwned(focusProfile);
            photoCamera = null;
            photoListener = null;
            focusVolume = null;
            focusEffect = null;
            vignetteEffect = null;
            focusProfile = null;
        }

        private static void DestroyOwned(UnityEngine.Object value)
        {
            if (Application.isPlaying) Destroy(value); else DestroyImmediate(value);
        }
    }
}
