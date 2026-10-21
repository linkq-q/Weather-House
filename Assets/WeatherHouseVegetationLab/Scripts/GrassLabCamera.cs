using UnityEngine;

namespace WeatherHouse.Vegetation
{
    public enum GrassCameraMode { Orbit, Walk, Fly }

    [RequireComponent(typeof(Camera))]
    [DefaultExecutionOrder(50)]
    public sealed class GrassLabCamera : MonoBehaviour
    {
        public GrassMeadow meadow;
        public bool pointerOverPanel;
        public Vector3 target = new Vector3(3, 1, 3);
        public float distance = 28;
        public float yaw = 34;
        public float pitch = 23;
        public float moveSpeed = 4;
        public float flySpeed = 7;
        public float sprintMultiplier = 3;
        public float lookSensitivity = 2;
        public float eyeHeight = 1.65f;
        public GrassCameraMode mode;
        [Tooltip("植物资源库专用视角；原草地水体场景保持关闭。")]
        public bool libraryLayout;
        [Tooltip("非资源库场景的地面半宽、半长；应比实际地面边缘略小。")]
        public Vector2 groundHalfExtents = new Vector2(39.5f, 34.5f);

        // This is a browsing camera, without tree/rock collision.
        bool looking, suppressLookUntilRelease;
        GrassCameraMode appliedMode;

        void Start() { SetMode(mode); PlaceCamera(); }

        void Update()
        {
            if (mode != appliedMode) SetMode(mode);
            bool rightHeld = Input.GetMouseButton(1);
            if (!rightHeld) { ReleasePointer(); suppressLookUntilRelease = false; }
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                ReleasePointer(); suppressLookUntilRelease = true;
            }
            if (looking && Cursor.lockState != CursorLockMode.Locked)
            {
                ReleasePointer(); suppressLookUntilRelease = true;
            }
            if (Input.GetKeyDown(KeyCode.Alpha1)) Overview();
            if (Input.GetKeyDown(KeyCode.Alpha2)) CloseGrass();
            if (Input.GetKeyDown(KeyCode.Alpha3)) Shore();
            if (Input.GetKeyDown(KeyCode.Alpha4)) SetMode(GrassCameraMode.Walk);
            if (Input.GetKeyDown(KeyCode.Alpha5)) SetMode(GrassCameraMode.Fly);
            // A locked cursor reports the viewport centre, which may overlap a narrow panel.
            if (pointerOverPanel && !looking) { PlaceCamera(); return; }
            if (rightHeld && !looking && !suppressLookUntilRelease)
            {
                looking = true; Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false;
            }
            var look = looking ? new Vector2(Input.GetAxis("Mouse X"), Input.GetAxis("Mouse Y")) : Vector2.zero;
            float vertical = 0;
            if (mode == GrassCameraMode.Fly)
            {
                if (Input.GetKey(KeyCode.E) || Input.GetKey(KeyCode.Space)) vertical += 1;
                if (Input.GetKey(KeyCode.Q) || Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)) vertical -= 1;
            }
            if (mode == GrassCameraMode.Orbit)
                distance = Mathf.Clamp(distance - Input.mouseScrollDelta.y * 1.7f, 3, 65);
            StepNavigation(new Vector3(Input.GetAxisRaw("Horizontal"), vertical, Input.GetAxisRaw("Vertical")), look,
                Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift), Time.unscaledDeltaTime);
        }

        public void StepNavigation(Vector3 movement, Vector2 look, bool sprint, float deltaTime)
        {
            if (mode != appliedMode) SetMode(mode);
            yaw += look.x * lookSensitivity;
            pitch = Mathf.Clamp(pitch - look.y * lookSensitivity,
                mode == GrassCameraMode.Orbit ? 4 : -85, mode == GrassCameraMode.Orbit ? 80 : 85);
            transform.rotation = Quaternion.Euler(pitch, yaw, 0);
            float duration = Mathf.Max(0, deltaTime);
            float speed = (mode == GrassCameraMode.Fly ? flySpeed : moveSpeed) * (sprint ? sprintMultiplier : 1);
            if (mode != GrassCameraMode.Fly) movement.y = 0;
            movement = Vector3.ClampMagnitude(movement, 1);
            if (mode == GrassCameraMode.Orbit)
                target += Quaternion.Euler(0, yaw, 0) * movement * speed * duration;
            else if (mode == GrassCameraMode.Walk)
                Walk(Quaternion.Euler(0, yaw, 0) * movement * speed * duration);
            else
            {
                Vector3 direction = transform.right * movement.x + transform.forward * movement.z + Vector3.up * movement.y;
                Vector3 position = ClampHorizontal(transform.position + Vector3.ClampMagnitude(direction, 1) * speed * duration);
                position.y = Mathf.Max(position.y, Ground(position) + .15f);
                transform.position = position;
            }
            PlaceCamera();
        }

        public void SetMode(GrassCameraMode next)
        {
            suppressLookUntilRelease |= looking;
            ReleasePointer();
            if (next == GrassCameraMode.Orbit && appliedMode != GrassCameraMode.Orbit)
                target = transform.position + Quaternion.Euler(pitch, yaw, 0) * Vector3.forward * distance;
            mode = appliedMode = next;
            if (mode == GrassCameraMode.Walk)
            {
                Vector3 position = NearestLand(ClampHorizontal(transform.position));
                position.y = Ground(position) + eyeHeight;
                transform.position = position;
            }
            PlaceCamera();
        }

        public void PlaceCamera()
        {
            transform.rotation = Quaternion.Euler(pitch, yaw, 0);
            if (mode == GrassCameraMode.Orbit)
                transform.position = target - transform.forward * distance;
            else
            {
                var position = ClampHorizontal(transform.position);
                if (mode == GrassCameraMode.Walk)
                {
                    if (!Walkable(position)) position = NearestLand(position);
                    position.y = Ground(position) + eyeHeight;
                }
                else position.y = Mathf.Max(position.y, Ground(position) + .15f);
                transform.position = position;
                target = position + transform.forward * distance;
            }
        }

        public void Overview() { if (libraryLayout) { GetComponent<Camera>().fieldOfView = 48; Preset(new Vector3(-8, 3.3f, 0), 24, 48, 31); } else Preset(new Vector3(3, 1, 3), 34, 23, 28); }
        public void CloseGrass() { if (libraryLayout) { GetComponent<Camera>().fieldOfView = 35; Preset(new Vector3(-10.5f, 3.45f, -9.8f), 0, 16, 4.5f); } else Preset(new Vector3(-6, 3.15f, -9), 35, 23, 4.5f); }
        public void Shore() { if (libraryLayout) { GetComponent<Camera>().fieldOfView = 48; Preset(new Vector3(-13.5f, 3.5f, 1), 0, 40, 12); } else Preset(new Vector3(6, .1f, 4), 34, 22, 18.5f); }

        public void FocusMeadow(GrassMeadow region)
        {
            if (!region) return;
            meadow = region;
            Vector3 center = region.transform.position;
            center.y = Ground(center) + .5f;
            float range = Mathf.Max(10, Mathf.Max(region.size.x, region.size.y) * 1.15f);
            Camera camera = GetComponent<Camera>();
            camera.farClipPlane = Mathf.Max(camera.farClipPlane, range + groundHalfExtents.magnitude * 2);
            Preset(center, yaw, 35, range);
        }

        void Preset(Vector3 point, float horizontal, float vertical, float range)
        {
            suppressLookUntilRelease |= looking;
            ReleasePointer();
            mode = appliedMode = GrassCameraMode.Orbit;
            target = point; yaw = horizontal; pitch = vertical; distance = range;
            PlaceCamera();
        }

        void Walk(Vector3 displacement)
        {
            // Small steps keep a long frame or sprint from jumping across the entire pond.
            int steps = Mathf.Max(1, Mathf.CeilToInt(displacement.magnitude / .25f));
            Vector3 step = displacement / steps;
            Vector3 position = transform.position;
            for (int i = 0; i < steps; i++)
            {
                Vector3 next = ClampHorizontal(position + step);
                if (Walkable(next)) position = next;
                else
                {
                    Vector3 alongX = ClampHorizontal(position + Vector3.right * step.x);
                    if (Walkable(alongX)) position = alongX;
                    Vector3 alongZ = ClampHorizontal(position + Vector3.forward * step.z);
                    if (Walkable(alongZ)) position = alongZ;
                }
            }
            position.y = Ground(position) + eyeHeight;
            transform.position = position;
        }

        float Ground(Vector3 position)
        {
            if (!meadow) return 0;
            Vector3 local = meadow.transform.InverseTransformPoint(position);
            return meadow.GetGroundHeight(local.x, local.z) + meadow.transform.position.y;
        }
        bool Walkable(Vector3 position) { return !meadow || Ground(position) > meadow.waterLevel + meadow.transform.position.y + .20f; }
        Vector3 ClampHorizontal(Vector3 position)
        {
            float halfWidth = Mathf.Max(1, groundHalfExtents.x), halfLength = Mathf.Max(1, groundHalfExtents.y);
            position.x = Mathf.Clamp(position.x, libraryLayout ? -22.5f : -halfWidth, libraryLayout ? 7.5f : halfWidth);
            position.z = Mathf.Clamp(position.z, libraryLayout ? -12.5f : -halfLength, libraryLayout ? 11.5f : halfLength);
            return position;
        }
        Vector3 NearestLand(Vector3 origin)
        {
            if (Walkable(origin)) return origin;
            for (float radius = .5f; radius <= 100; radius += .5f)
                for (int i = 0; i < 64; i++)
                {
                    float angle = i * (Mathf.PI * 2 / 64);
                    Vector3 point = ClampHorizontal(origin + new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * radius);
                    if (Walkable(point)) return point;
                }
            throw new System.InvalidOperationException("测试地形范围内没有可供行走的陆地。");
        }

        void ReleasePointer()
        {
            if (looking) { Cursor.lockState = CursorLockMode.None; Cursor.visible = true; }
            looking = false;
        }
        void OnDisable() { ReleasePointer(); }
        void OnApplicationFocus(bool focused)
        {
            if (!focused) { ReleasePointer(); suppressLookUntilRelease = true; }
        }
    }
}
