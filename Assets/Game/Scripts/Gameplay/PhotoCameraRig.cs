using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace WeatherHousePrototype
{
    [DisallowMultipleComponent]
    public class PhotoCameraRig : MonoBehaviour
    {
        private const int FocusVolumeLayer = 28;
        public Camera CaptureCamera => photoCamera;
        public bool Active { get; private set; }
        public float FocalLength { get; private set; } = 35f;
        public float FocusDistance { get; private set; } = 8f;
        public string FocusLabel => (focusedOnSurface ? "对焦 " : "远景 ") + FocusDistance.ToString("F1") + " m" +
            (focusEffect != null && GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset && focusEffect.IsActive()
                ? string.Empty : " · 景深暂不可用");

        private PrototypeWalker walker;
        private Camera photoCamera;
        private AudioListener photoListener, viewListener, mapListener;
        private Volume focusVolume;
        private VolumeProfile focusProfile;
        private DepthOfField focusEffect;
        private Renderer[] playerRenderers;
        private bool[] rendererEnabled;
        private bool previousViewEnabled, previousMapEnabled, previousViewAudio, previousMapAudio;
        private float previousYaw, previousPitch;
        private bool focusedOnSurface = true;

        public void Initialize(PrototypeWalker player)
        {
            if (player == null || player.viewCamera == null || player.mapCamera == null)
                throw new ArgumentException("PhotoCameraRig needs a player with walking and map cameras.", nameof(player));
            if (walker != player)
            {
                SetActive(false);
                ReleaseResources();
                walker = player;
            }
            EnsureCamera();
        }

        private void EnsureCamera()
        {
            if (photoCamera != null) return;
            var cameraObject = new GameObject("Photo Camera (runtime)") { hideFlags = HideFlags.DontSave };
            cameraObject.transform.SetParent(walker.transform, false);
            photoCamera = cameraObject.AddComponent<Camera>();
            photoCamera.CopyFrom(walker.viewCamera);
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
            var sourceData = walker.viewCamera.GetComponent<UniversalAdditionalCameraData>();
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
            focusVolume.sharedProfile = focusProfile;
            focusVolume.enabled = false;
        }

        public void SetActive(bool value)
        {
            if (value == Active) return;
            if (value)
            {
                if (walker == null) throw new InvalidOperationException("Initialize PhotoCameraRig before entering photo mode.");
                EnsureCamera();
                previousYaw = walker.yaw;
                previousPitch = walker.pitch;
                previousViewEnabled = walker.viewCamera.enabled;
                previousMapEnabled = walker.mapCamera.enabled;
                viewListener = walker.viewCamera.GetComponent<AudioListener>();
                mapListener = walker.mapCamera.GetComponent<AudioListener>();
                previousViewAudio = viewListener != null && viewListener.enabled;
                previousMapAudio = mapListener != null && mapListener.enabled;
                walker.SetOverview(false);
                walker.viewCamera.enabled = false;
                walker.mapCamera.enabled = false;
                if (viewListener != null) viewListener.enabled = false;
                if (mapListener != null) mapListener.enabled = false;
                playerRenderers = walker.visualRoot != null ? walker.visualRoot.GetComponentsInChildren<Renderer>(true) : Array.Empty<Renderer>();
                rendererEnabled = new bool[playerRenderers.Length];
                for (int i = 0; i < playerRenderers.Length; i++)
                {
                    rendererEnabled[i] = playerRenderers[i].enabled;
                    playerRenderers[i].enabled = false;
                }
                Active = true;
                UpdatePose();
                focusVolume.enabled = true;
                photoCamera.enabled = true;
                photoListener.enabled = true;
            }
            else
            {
                Active = false;
                if (photoCamera != null) photoCamera.enabled = false;
                if (photoListener != null) photoListener.enabled = false;
                if (focusVolume != null) focusVolume.enabled = false;
                if (walker != null)
                {
                    walker.yaw = previousYaw;
                    walker.pitch = previousPitch;
                    walker.SetOverview(previousMapEnabled && !previousViewEnabled);
                    if (walker.viewCamera != null) walker.viewCamera.enabled = previousViewEnabled;
                    if (walker.mapCamera != null) walker.mapCamera.enabled = previousMapEnabled;
                }
                if (viewListener != null) viewListener.enabled = previousViewAudio;
                if (mapListener != null) mapListener.enabled = previousMapAudio;
                if (playerRenderers != null)
                    for (int i = 0; i < playerRenderers.Length; i++)
                        if (playerRenderers[i] != null) playerRenderers[i].enabled = rendererEnabled[i];
                playerRenderers = null;
                rendererEnabled = null;
            }
        }

        private void Update()
        {
            if (!Active || walker == null || walker.InputBlocked) return;
            if (Input.GetMouseButton(1))
            {
                walker.yaw += Input.GetAxis("Mouse X") * 3f;
                walker.pitch = Mathf.Clamp(walker.pitch - Input.GetAxis("Mouse Y") * 3f, -60f, 75f);
            }
            if (Mathf.Abs(Input.mouseScrollDelta.y) > .001f) Zoom(Input.mouseScrollDelta.y);
        }

        private void LateUpdate()
        {
            if (Active) UpdatePose();
        }

        private void UpdatePose()
        {
            if (walker == null || photoCamera == null) return;
            Quaternion rotation = Quaternion.Euler(walker.pitch, walker.yaw, 0f);
            photoCamera.transform.SetPositionAndRotation(walker.transform.position + Vector3.up * 1.55f +
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
                if (hit.transform.IsChildOf(walker.transform) || hit.distance >= nearest) continue;
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
            Transform visual = animal.transform.Find("Visual");
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
            if (focusProfile != null) DestroyOwned(focusProfile);
            photoCamera = null;
            photoListener = null;
            focusVolume = null;
            focusEffect = null;
            focusProfile = null;
        }

        private static void DestroyOwned(UnityEngine.Object value)
        {
            if (Application.isPlaying) Destroy(value); else DestroyImmediate(value);
        }
    }
}
