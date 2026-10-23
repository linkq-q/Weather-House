using ForestSpriteCharacter;
using UnityEngine;

namespace WeatherHouse.Vegetation
{
    [RequireComponent(typeof(Camera)), DisallowMultipleComponent, DefaultExecutionOrder(100)]
    public sealed class VegetationTopDownCamera : MonoBehaviour
    {
        public ForestSpriteDemoController player;
        public float baseYaw = 42;
        [Range(0, 180)] public float yawLimit = 45;
        public float yawOffset;
        public float pitch = 28;
        public Vector2 pitchRange = new Vector2(24, 46);
        public float distance = 14;
        public Vector2 distanceRange = new Vector2(12, 22);
        [Range(25, 65)] public float fieldOfView = 48;
        public float targetHeight = .8f;
        [Tooltip("朝镜头前方偏移构图，让角色位于画面下部。")]
        public float lookAhead = 4.5f;
        public float followResponse = 8;
        public float mouseSensitivity = 2;
        public float keyboardTurnSpeed = 55;
        public float zoomSpeed = 1;
        [HideInInspector] public bool pointerOverPanel;

        Camera viewCamera;
        Vector3 followedPosition;
        bool hasPosition;
        public float Yaw => baseYaw + yawOffset;

        void Awake() { viewCamera = GetComponent<Camera>(); }
        void OnEnable() { hasPosition = false; }

        void Update()
        {
            if (!player || player.ScriptedInput) return;
            bool overControls = player.showControls && Input.mousePosition.x < 320 &&
                Screen.height - Input.mousePosition.y < 310;
            if (pointerOverPanel || overControls || ForestSpriteCapePanel.PointerOverPanel) return;
            float keyboard = (Input.GetKey(KeyCode.E) ? 1 : 0) - (Input.GetKey(KeyCode.Q) ? 1 : 0);
            float horizontal = keyboard * keyboardTurnSpeed * Time.unscaledDeltaTime;
            float vertical = 0;
            if (Input.GetMouseButton(1))
            {
                horizontal += Input.GetAxis("Mouse X") * mouseSensitivity;
                vertical = -Input.GetAxis("Mouse Y") * mouseSensitivity;
            }
            StepOrbit(horizontal, vertical, Input.mouseScrollDelta.y * zoomSpeed);
        }

        public void StepOrbit(float deltaYaw, float deltaPitch, float zoom)
        {
            yawOffset = Mathf.Clamp(yawOffset + deltaYaw, -Mathf.Abs(yawLimit), Mathf.Abs(yawLimit));
            pitch = Mathf.Clamp(pitch + deltaPitch, Mathf.Min(pitchRange.x, pitchRange.y), Mathf.Max(pitchRange.x, pitchRange.y));
            distance = Mathf.Clamp(distance - zoom, Mathf.Max(.1f, Mathf.Min(distanceRange.x, distanceRange.y)),
                Mathf.Max(.1f, Mathf.Max(distanceRange.x, distanceRange.y)));
        }

        public void SnapToPlayer()
        {
            if (!player) return;
            followedPosition = player.transform.position;
            hasPosition = true;
            PlaceCamera();
        }

        void LateUpdate()
        {
            if (!player) return;
            if (Input.GetKeyDown(KeyCode.R) || (hasPosition && (player.transform.position - followedPosition).sqrMagnitude > 4f)) SnapToPlayer();
            if (!hasPosition) SnapToPlayer();
            followedPosition = Vector3.Lerp(followedPosition, player.transform.position,
                1 - Mathf.Exp(-Mathf.Max(0, followResponse) * Time.unscaledDeltaTime));
            PlaceCamera();
        }

        void PlaceCamera()
        {
            if (!viewCamera) viewCamera = GetComponent<Camera>();
            StepOrbit(0, 0, 0);
            Vector3 forward = Quaternion.Euler(0, Yaw, 0) * Vector3.forward;
            // Reserve the bottom quarter for the character even at the closest, steepest orbit.
            float radians = pitch * Mathf.Deg2Rad;
            float framing = 2f * Mathf.Tan(fieldOfView * .5f * Mathf.Deg2Rad) * .24f;
            float safeAhead = framing * distance / (Mathf.Sin(radians) + framing * Mathf.Cos(radians));
            Vector3 target = followedPosition + Vector3.up * targetHeight + forward * Mathf.Min(lookAhead, safeAhead);
            Quaternion rotation = Quaternion.Euler(pitch, Yaw, 0);
            transform.SetPositionAndRotation(target + rotation * Vector3.back * distance, rotation);
            viewCamera.orthographic = false;
            viewCamera.fieldOfView = fieldOfView;
        }
    }
}
