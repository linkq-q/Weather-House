using System;
using ForestSpriteCharacter;
using UnityEngine;

namespace WeatherHouse.Vegetation
{
    [DefaultExecutionOrder(-100), DisallowMultipleComponent]
    public sealed class VegetationCharacterPreview : MonoBehaviour
    {
        public GrassLabView labView;
        public ForestSpriteDemoController player;
        public Camera playerCamera;
        public bool startInCharacterMode = true;
        public bool CharacterMode { get; private set; }

        Camera labCamera;
        GrassLabCamera navigation;
        CharacterController character;
        ForestSpriteCapePanel[] capePanels;
        Font font;
        GUIStyle label, button;
        bool connected;

        void Start()
        {
            Connect();
            TeleportToRegion(0);
            SetCharacterMode(startInCharacterMode);
        }

        void Connect()
        {
            if (connected) return;
            if (!labView || !player || !playerCamera)
                throw new InvalidOperationException("角色试玩需要连接草地面板、ForestSprite 控制器和独立角色相机。");
            labCamera = labView.GetComponent<Camera>();
            if (labCamera == playerCamera)
                throw new InvalidOperationException("角色相机与草地浏览相机需要分别配置。");
            navigation = labView.Navigation;
            character = player.GetComponent<CharacterController>();
            capePanels = player.GetComponentsInChildren<ForestSpriteCapePanel>(true);
            player.demoCamera = playerCamera;
            connected = true;
        }

        void Update()
        {
            if (Input.GetKeyDown(KeyCode.F6)) SetCharacterMode(!CharacterMode);
        }

        public void SetCharacterMode(bool value)
        {
            Connect();
            // ResetPosition is the controller's public velocity/animation reset. Keep the final pose.
            if (CharacterMode != value || !value) MoveAndReset(player.transform.position, player.transform.rotation);
            player.ReleaseScriptedInput();
            player.SetDemonstration(value ? ForestSpriteDemoController.Demonstration.Manual : ForestSpriteDemoController.Demonstration.Idle);
            CharacterMode = value;
            player.enabled = value;
            foreach (var panel in capePanels) if (panel) panel.enabled = value;
            labView.enabled = !value;
            navigation.enabled = !value;
            labCamera.enabled = !value;
            playerCamera.enabled = value;
            SetListener(labCamera, !value);
            SetListener(playerCamera, value);
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            if (!value) navigation.PlaceCamera();
            else PlacePlayerCamera();
        }

        public void TeleportToRegion(int index)
        {
            Connect();
            GrassMeadow region = Region(index);
            if (!region) throw new ArgumentOutOfRangeException(nameof(index), "没有配置该草地区域。");
            labView.SelectRegion(index);
            Vector2 point = index == 0 ? new Vector2(-8, -10) : Vector2.zero;
            Vector3 destination = region.transform.TransformPoint(new Vector3(point.x, region.GetGroundHeight(point.x, point.y) + .1f, point.y));
            MoveAndReset(destination, player.transform.rotation);
            player.ReleaseScriptedInput();
            player.SetDemonstration(CharacterMode ? ForestSpriteDemoController.Demonstration.Manual : ForestSpriteDemoController.Demonstration.Idle);
            PlacePlayerCamera();
            UpdateCapeFloor();
        }

        void MoveAndReset(Vector3 position, Quaternion rotation)
        {
            player.ResetPosition();
            character.enabled = false;
            player.transform.SetPositionAndRotation(position, rotation);
            character.enabled = true;
            if (player.animator)
            {
                player.animator.Play("Idle", 0, 0);
                player.animator.Update(0);
            }
            if (player.secondaryMotion) player.secondaryMotion.ResetSimulation();
            if (player.capeCloth) player.capeCloth.ResetSimulation();
        }

        public void TeleportToPondShallows()
        {
            Connect();
            GrassMeadow main = Region(0);
            if (!main) return;
            labView.SelectRegion(0);
            SetCharacterMode(true);
            Vector2 direction = new Vector2(-.707107f, -.707107f * .74f);
            float low = 0, high = main.pondRadius * 2;
            Vector2 point = main.pondCenter;
            for (int i = 0; i < 30; i++)
            {
                float radius = (low + high) * .5f;
                point = main.pondCenter + direction * radius;
                if (main.GetGroundHeight(point.x, point.y) < main.waterLevel + .12f) low = radius;
                else high = radius;
            }
            Vector3 position = main.transform.TransformPoint(new Vector3(point.x, main.GetGroundHeight(point.x, point.y) + .04f, point.y));
            Vector3 forward = main.transform.TransformDirection(new Vector3(-direction.x, 0, -direction.y)).normalized;
            Quaternion rotation = Quaternion.LookRotation(forward);
            MoveAndReset(position, rotation);
            player.cameraYaw = rotation.eulerAngles.y; player.cameraPitch = 22; player.cameraDistance = 4.8f;
            player.ReleaseScriptedInput();
            player.SetDemonstration(ForestSpriteDemoController.Demonstration.Manual);
            PlacePlayerCamera(); UpdateCapeFloor();
        }

        void PlacePlayerCamera()
        {
            Vector3 target = player.transform.position + Vector3.up * player.cameraHeight;
            playerCamera.transform.position = target + Quaternion.Euler(player.cameraPitch, player.cameraYaw, 0) * Vector3.back * player.cameraDistance;
            playerCamera.transform.LookAt(target);
        }

        void LateUpdate() { UpdateCapeFloor(); }

        void UpdateCapeFloor()
        {
            if (!player || !player.capeCloth || !player.capeCloth.floorCollision) return;
            GrassMeadow ground = Region(0);
            if (!ground) return;
            SphereCollider floor = player.capeCloth.floorCollision;
            Vector3 local = ground.transform.InverseTransformPoint(player.transform.position);
            float height = ground.GetGroundHeight(local.x, local.z) + ground.transform.position.y;
            Vector3 scale = floor.transform.lossyScale;
            float radius = floor.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
            // Cape Update places its demo floor at Y=-25. Replace that after all Updates,
            // retaining the existing cloth contact proxy and its calibrated radius.
            Vector3 center = new Vector3(player.transform.position.x, height - radius, player.transform.position.z);
            floor.transform.position = center - floor.transform.TransformVector(floor.center);
        }

        GrassMeadow Region(int index)
        {
            if (!labView) return null;
            if (labView.regions != null && labView.regions.Length > 0)
                return index >= 0 && index < labView.regions.Length ? labView.regions[index] : null;
            return index == 0 ? labView.meadow : null;
        }

        static void SetListener(Camera camera, bool value)
        {
            AudioListener listener = camera.GetComponent<AudioListener>();
            if (listener) listener.enabled = value;
        }

        void OnGUI()
        {
            if (!connected) return;
            if (label == null)
            {
                font = Font.CreateDynamicFontFromOSFont(new[] { "Microsoft YaHei", "SimHei", "Arial" }, 14);
                label = new GUIStyle(GUI.skin.label) { font = font, fontSize = 14, wordWrap = true };
                button = new GUIStyle(GUI.skin.button) { font = font, fontSize = 14, wordWrap = true };
            }
            int count = labView.regions != null && labView.regions.Length > 0 ? labView.regions.Length : 1;
            float left = CharacterMode ? 324 : 398, right = CharacterMode ? 324 : 12;
            float width = Mathf.Min(760, Mathf.Max(120, Screen.width - left - right));
            int columns = Mathf.Clamp(Mathf.FloorToInt((width - 16) / 116), 1, count);
            float height = 98 + Mathf.CeilToInt(count / (float)columns) * 34;
            float x = Mathf.Clamp(left + (Screen.width - left - right - width) * .5f, 12, Mathf.Max(12, Screen.width - width - 12));
            GUILayout.BeginArea(new Rect(x, Mathf.Max(12, Screen.height - height - 12), width, height), GUI.skin.box);
            if (GUILayout.Button(CharacterMode ? "F6 · 切回草地浏览与参数" : "F6 · 进入角色试玩", button, GUILayout.Height(28)))
                SetCharacterMode(!CharacterMode);
            if (GUILayout.Button("池塘浅滩 · 向前走入水，查看波纹拖尾", button, GUILayout.Height(28)))
                TeleportToPondShallows();
            GUILayout.Label(CharacterMode ? "传送区域 · WASD 走 / Shift 跑 / Space 跳" : "角色暂停在原地 · 切换后可传送", label);
            bool enabledBefore = GUI.enabled;
            GUI.enabled = enabledBefore && CharacterMode;
            for (int row = 0; row < Mathf.CeilToInt(count / (float)columns); row++)
            {
                GUILayout.BeginHorizontal();
                for (int col = 0; col < columns; col++)
                {
                    int index = row * columns + col;
                    if (index >= count) break;
                    GrassMeadow region = Region(index);
                    if (GUILayout.Button(index == 0 ? "原始草地" : region ? region.name : "未配置", button, GUILayout.Height(30)))
                        TeleportToRegion(index);
                }
                GUILayout.EndHorizontal();
            }
            GUI.enabled = enabledBefore;
            GUILayout.EndArea();
        }

        void OnDisable()
        {
            if (Application.isPlaying && connected && labView && player && playerCamera) SetCharacterMode(false);
        }
        void OnDestroy() { if (font) Destroy(font); }
    }
}
