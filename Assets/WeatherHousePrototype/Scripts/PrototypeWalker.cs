using UnityEngine;

namespace WeatherHousePrototype
{
    [RequireComponent(typeof(CharacterController))]
    public class PrototypeWalker : MonoBehaviour
    {
        public Camera viewCamera;
        public Camera mapCamera;
        public Transform visualRoot;
        public float moveSpeed = 4f;
        public float yaw = 45f;
        public float pitch = 25f;
        public bool InputBlocked { get; set; }
        public bool JumpBlocked { get; set; }
        public bool ShowLegacyHint { get; set; } = true;

        private CharacterController controller;
        private float verticalSpeed;
        private float cameraDistance = 7f;
        private bool overview;
        private GUIStyle hintStyle;

        private void Awake()
        {
            controller = GetComponent<CharacterController>();
            Debug.Assert(viewCamera != null && mapCamera != null,
                "PrototypeWalker needs both a walking camera and a map camera.", this);
            SetOverview(false);
        }

        private void Update()
        {
            if (!InputBlocked && Input.GetKeyDown(KeyCode.Tab))
                SetOverview(!overview);

            if (controller == null || !controller.enabled || viewCamera == null)
                return;

            Vector3 movement = Vector3.zero;
            if (!overview && !InputBlocked)
            {
                if (Input.GetMouseButton(1))
                {
                    yaw += Input.GetAxis("Mouse X") * 3f;
                    pitch = Mathf.Clamp(pitch - Input.GetAxis("Mouse Y") * 3f, 8f, 75f);
                }

                cameraDistance = Mathf.Clamp(cameraDistance - Input.mouseScrollDelta.y, 3f, 13f);
                Vector3 input = Vector3.ClampMagnitude(new Vector3(
                    Input.GetAxisRaw("Horizontal"), 0f, Input.GetAxisRaw("Vertical")), 1f);
                movement = Quaternion.Euler(0f, yaw, 0f) * input;

                if (visualRoot != null && movement.sqrMagnitude > 0.001f)
                    visualRoot.rotation = Quaternion.RotateTowards(visualRoot.rotation,
                        Quaternion.LookRotation(movement), 720f * Time.deltaTime);
            }

            if (controller.isGrounded && verticalSpeed < 0f)
                verticalSpeed = -2f;
            if (!overview && !InputBlocked && !JumpBlocked && controller.isGrounded && Input.GetKeyDown(KeyCode.Space))
                verticalSpeed = 6f;

            verticalSpeed += -20f * Time.deltaTime;
            float speed = moveSpeed * (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift) ? 1.7f : 1f);
            CollisionFlags collision = controller.Move(
                (movement * speed + Vector3.up * verticalSpeed) * Time.deltaTime);
            if ((collision & CollisionFlags.Above) != 0 && verticalSpeed > 0f)
                verticalSpeed = 0f;
        }

        private void LateUpdate()
        {
            if (viewCamera == null || overview)
                return;

            Vector3 target = transform.position + Vector3.up * 1.3f;
            Quaternion rotation = Quaternion.Euler(pitch, yaw, 0f);
            Vector3 backward = rotation * Vector3.back;
            float distance = cameraDistance;
            foreach (RaycastHit hit in Physics.SphereCastAll(target, 0.22f, backward,
                         cameraDistance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            {
                if (!hit.transform.IsChildOf(transform))
                    distance = Mathf.Min(distance, Mathf.Max(0.15f, hit.distance - 0.08f));
            }

            viewCamera.transform.SetPositionAndRotation(target + backward * distance, rotation);
        }

        public void SetOverview(bool enabled)
        {
            overview = enabled;
            if (viewCamera != null)
            {
                viewCamera.enabled = !enabled;
                AudioListener listener = viewCamera.GetComponent<AudioListener>();
                if (listener != null)
                    listener.enabled = !enabled;
            }
            if (mapCamera != null)
            {
                mapCamera.enabled = enabled;
                AudioListener listener = mapCamera.GetComponent<AudioListener>();
                if (listener != null)
                    listener.enabled = enabled;
            }
        }

        public void Teleport(Vector3 position)
        {
            if (controller == null)
                controller = GetComponent<CharacterController>();
            bool wasEnabled = controller != null && controller.enabled;
            if (controller != null)
                controller.enabled = false;
            transform.position = position;
            verticalSpeed = 0f;
            if (controller != null)
                controller.enabled = wasEnabled;
        }

        private void OnGUI()
        {
            if (!ShowLegacyHint) return;
            if (hintStyle == null)
                hintStyle = new GUIStyle(GUI.skin.label) { fontSize = 15 };

            string hint = overview ? "俯视地图 · Tab 返回漫游" :
                "WASD 移动 · Shift 快走 · 空格跳跃\n右键拖动视角 · 滚轮缩放 · Tab 俯视地图";
            Rect bounds = new Rect(18f, Screen.height - 58f, Screen.width - 36f, 48f);
            hintStyle.normal.textColor = Color.black;
            GUI.Label(new Rect(bounds.x + 1f, bounds.y + 1f, bounds.width, bounds.height), hint, hintStyle);
            hintStyle.normal.textColor = Color.white;
            GUI.Label(bounds, hint, hintStyle);
        }
    }
}
