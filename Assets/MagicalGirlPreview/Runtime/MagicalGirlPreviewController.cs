using System;
using System.Collections.Generic;
using UnityEngine;

namespace MagicalGirlPreview
{
    [RequireComponent(typeof(CharacterController))]
    public sealed class MagicalGirlPreviewController : MonoBehaviour
    {
        [Serializable]
        public sealed class ClipEntry { public string state; public AnimationClip clip; }
        [Serializable]
        public sealed class MaterialPair
        {
            public Renderer renderer;
            public Material[] unlit;
            public Material[] lit;
        }

        public Animator animator;
        public Transform visual;
        public Camera previewCamera;
        public Light keyLight;
        public MagicalGirlSecondaryMotion secondaryMotion;
        public ClipEntry[] clips = Array.Empty<ClipEntry>();
        public MaterialPair[] materials = Array.Empty<MaterialPair>();
        public string assetStage = "结构检查副本";
        [Min(.01f)] public float walkSpeed = .8f;
        [Min(.01f)] public float runSpeed = 2.8f;
        [Min(.01f)] public float jumpHeight = .65f;
        public float gravity = -14f;
        public float cameraDistance = 3.5f;
        public float cameraHeight = .9f;
        public float cameraYaw = 165f;
        public float cameraPitch = 10f;
        public float transitionSeconds = .16f;
        [Range(0f, 1f)] public float runPhaseOffset;
        public bool automaticBlink = true;
        public bool ordinaryLighting;
        [Range(0f, 100f)] public float expressionWeight = 70f;
        [Range(0f, 100f)] public float manualBlink;

        CharacterController controller;
        readonly Dictionary<string, AnimationClip> available = new Dictionary<string, AnimationClip>();
        readonly Dictionary<string, List<ShapeBinding>> shapes = new Dictionary<string, List<ShapeBinding>>();
        readonly string[] expressionNames = { "Smile", "Surprised", "BrowUp", "BrowDown" };
        string currentState = "", emotion = "", expression = "", stationaryPreview = "";
        float verticalSpeed, emotionUntil, airborneSince, landUntil, blinkStart = -10f, nextBlink;
        bool wasGrounded = true, jumpActive;
        Vector3 startPosition;
        Quaternion startRotation;
        Font uiFont;
        GUIStyle labelStyle, smallStyle, buttonStyle;
        Vector2 panelScroll;
        Rect PanelRect => new Rect(14, 14, 310, Mathf.Min(Screen.height - 28, 650));

        sealed class ShapeBinding { public SkinnedMeshRenderer renderer; public int index; }
        public string CurrentState => currentState;
        public bool IsGrounded => controller != null && controller.isGrounded;
        public bool HasClip(string name) => available.ContainsKey(name);
        public bool HasExpression(string name) => shapes.ContainsKey(name);

        void Awake()
        {
            controller = GetComponent<CharacterController>();
            startPosition = transform.position;
            startRotation = transform.rotation;
            if (animator == null || previewCamera == null || visual == null)
            {
                Debug.LogError("Preview requires its Animator, Visual and Camera references.", this);
                enabled = false;
                return;
            }
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            foreach (var entry in clips)
                if (entry != null && entry.clip != null && !string.IsNullOrEmpty(entry.state))
                    available[entry.state] = entry.clip;
            foreach (var renderer in visual.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                var mesh = renderer.sharedMesh;
                if (mesh == null) continue;
                for (int i = 0; i < mesh.blendShapeCount; i++)
                {
                    string name = mesh.GetBlendShapeName(i);
                    int dot = name.LastIndexOf('.');
                    if (dot >= 0) name = name.Substring(dot + 1);
                    if (!shapes.TryGetValue(name, out var list)) shapes[name] = list = new List<ShapeBinding>();
                    list.Add(new ShapeBinding { renderer = renderer, index = i });
                }
            }
            uiFont = Font.CreateDynamicFontFromOSFont("Microsoft YaHei", 15);
            nextBlink = Time.time + UnityEngine.Random.Range(2f, 4f);
            SetLighting(ordinaryLighting);
            SwitchState("idle", false);
        }

        void Update()
        {
            float dt = Time.deltaTime;
            bool grounded = controller.isGrounded;
            if (grounded && verticalSpeed < 0f) verticalSpeed = -2f;
            if (grounded && !wasGrounded && jumpActive)
            {
                jumpActive = false;
                landUntil = Time.time + (HasClip("land") ? Mathf.Min(.32f, available["land"].length) : .1f);
            }
            Vector2 input = new Vector2(
                (Input.GetKey(KeyCode.D) ? 1 : 0) - (Input.GetKey(KeyCode.A) ? 1 : 0),
                (Input.GetKey(KeyCode.W) ? 1 : 0) - (Input.GetKey(KeyCode.S) ? 1 : 0));
            input = Vector2.ClampMagnitude(input, 1f);
            bool moving = input.sqrMagnitude > .001f;
            bool running = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            Vector3 forward = Vector3.ProjectOnPlane(previewCamera.transform.forward, Vector3.up).normalized;
            Vector3 right = Vector3.Cross(Vector3.up, forward);
            Vector3 direction = forward * input.y + right * input.x;
            if (moving)
            {
                emotion = "";
                stationaryPreview = "";
                transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(direction), 720f * dt);
            }
            if (Input.GetKeyDown(KeyCode.Space) && grounded)
            {
                emotion = "";
                stationaryPreview = "";
                verticalSpeed = Mathf.Sqrt(-2f * gravity * jumpHeight);
                airborneSince = Time.time;
                jumpActive = true;
                grounded = false;
            }
            verticalSpeed += gravity * dt;
            controller.Move((direction * (running ? runSpeed : walkSpeed) + Vector3.up * verticalSpeed) * dt);
            if (transform.position.y < -3f) ResetPosition();

            string state;
            if (jumpActive || !grounded)
            {
                float startDuration = HasClip("jumpStart") ? Mathf.Min(.18f, available["jumpStart"].length) : 0f;
                state = Time.time - airborneSince < startDuration ? "jumpStart" : "airborne";
                if (!HasClip(state)) state = "idle";
            }
            else if (Time.time < landUntil && HasClip("land")) state = "land";
            else if (!moving && emotion.Length > 0 && Time.time < emotionUntil) state = emotion;
            else if (!moving && stationaryPreview.Length > 0) state = stationaryPreview;
            else state = moving ? (running ? "run" : "walk") : "idle";
            SwitchState(state, IsLocomotion(currentState) && IsLocomotion(state));
            wasGrounded = grounded;

            if (Input.GetKeyDown(KeyCode.Alpha1)) PlayEmotion("afraid");
            if (Input.GetKeyDown(KeyCode.Alpha2)) PlayEmotion("agree");
            if (Input.GetKeyDown(KeyCode.Alpha3)) PlayEmotion("sob");
            if (Input.GetKeyDown(KeyCode.R)) ResetPosition();
            bool overPanel = PanelRect.Contains(new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y));
            if (Input.GetMouseButton(1) && !overPanel)
            {
                cameraYaw += Input.GetAxis("Mouse X") * 3f;
                cameraPitch = Mathf.Clamp(cameraPitch - Input.GetAxis("Mouse Y") * 2f, -5f, 55f);
            }
            if (!overPanel) cameraDistance = Mathf.Clamp(cameraDistance - Input.mouseScrollDelta.y * .2f, 1.2f, 6f);
        }

        void LateUpdate()
        {
            Vector3 target = transform.position + Vector3.up * cameraHeight;
            previewCamera.transform.position = target + Quaternion.Euler(cameraPitch, cameraYaw, 0f) * Vector3.back * cameraDistance;
            previewCamera.transform.LookAt(target);
            if (automaticBlink && Time.time >= nextBlink) Blink();
            float blinkTime = (Time.time - blinkStart) / .2f;
            float blink = blinkTime >= 0f && blinkTime < 1f ? Mathf.Sin(blinkTime * Mathf.PI) * 100f : 0f;
            if (manualBlink > 0f) blink = manualBlink;
            SetShape("Blink_L", blink);
            SetShape("Blink_R", blink);
            foreach (string name in expressionNames)
            {
                if (!shapes.TryGetValue(name, out var list)) continue;
                foreach (var shape in list)
                {
                    float current = shape.renderer.GetBlendShapeWeight(shape.index);
                    shape.renderer.SetBlendShapeWeight(shape.index, Mathf.MoveTowards(current, name == expression ? expressionWeight : 0f, Time.deltaTime * 280f));
                }
            }
        }

        public static bool IsLocomotion(string state) => state == "walk" || state == "run";
        public static float PhaseTime(float normalizedTime, float targetLength) => Mathf.Repeat(normalizedTime, 1f) * Mathf.Max(0f, targetLength);
        public static float MapGaitPhase(string source, string target, float phase, float runOffset)
        {
            // These clips start on different foot poses; equal clip time does not mean equal gait phase.
            float canonical = phase - (source == "run" ? runOffset : 0f);
            return Mathf.Repeat(canonical + (target == "run" ? runOffset : 0f), 1f);
        }

        void SwitchState(string state, bool keepPhase)
        {
            if (state == currentState || !available.TryGetValue(state, out var clip)) return;
            float offset = 0f;
            if (keepPhase)
            {
                var info = animator.IsInTransition(0) ? animator.GetNextAnimatorStateInfo(0) : animator.GetCurrentAnimatorStateInfo(0);
                offset = PhaseTime(MapGaitPhase(currentState, state, info.normalizedTime, runPhaseOffset), clip.length);
            }
            if (currentState.Length == 0) animator.Play(state, 0, 0f);
            else animator.CrossFadeInFixedTime(state, transitionSeconds, 0, offset);
            currentState = state;
        }

        public void PlayEmotion(string name)
        {
            if (!available.TryGetValue(name, out var clip) || !controller.isGrounded) return;
            emotion = name;
            stationaryPreview = "";
            emotionUntil = Time.time + clip.length;
            animator.CrossFadeInFixedTime(name, transitionSeconds, 0, 0f);
            currentState = name;
        }

        public void Blink()
        {
            blinkStart = Time.time;
            nextBlink = Time.time + UnityEngine.Random.Range(2.4f, 5.2f);
        }

        void SetShape(string name, float weight)
        {
            if (!shapes.TryGetValue(name, out var list)) return;
            foreach (var shape in list) shape.renderer.SetBlendShapeWeight(shape.index, weight);
        }

        public void SetLighting(bool lit)
        {
            ordinaryLighting = lit;
            foreach (var pair in materials)
                if (pair.renderer != null) pair.renderer.sharedMaterials = lit ? pair.lit : pair.unlit;
        }

        public void ResetPosition()
        {
            controller.enabled = false;
            transform.SetPositionAndRotation(startPosition, startRotation);
            controller.enabled = true;
            verticalSpeed = 0f;
            jumpActive = false;
            emotion = "";
            stationaryPreview = "";
            landUntil = 0f;
            if (secondaryMotion != null) secondaryMotion.ResetSimulation();
        }

        void OnGUI()
        {
            if (labelStyle == null)
            {
                labelStyle = new GUIStyle(GUI.skin.label) { font = uiFont, fontSize = 15, wordWrap = true };
                smallStyle = new GUIStyle(labelStyle) { fontSize = 12 };
                buttonStyle = new GUIStyle(GUI.skin.button) { font = uiFont, fontSize = 14 };
            }
            GUILayout.BeginArea(PanelRect, GUI.skin.box);
            panelScroll = GUILayout.BeginScrollView(panelScroll);
            GUILayout.Label("魔法少女 · 独立资产预览", labelStyle);
            GUILayout.Label(assetStage, smallStyle);
            GUILayout.Label("WASD 移动 / Shift 跑 / Space 跳\n右键拖动旋转视角 / 滚轮远近 / R 归位", smallStyle);
            GUILayout.Label("动作：" + currentState + (IsGrounded ? "（地面）" : "（空中）"), labelStyle);
            if (!HasClip("jumpStart") || !HasClip("airborne") || !HasClip("land"))
                GUILayout.Label("跳跃动作尚未齐全：当前可测试位移，缺失阶段使用待机占位。", smallStyle);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("纯底色", buttonStyle)) SetLighting(false);
            if (GUILayout.Button("普通光照", buttonStyle)) SetLighting(true);
            GUILayout.EndHorizontal();
            GUILayout.Label("仅 URP 基础材质，非最终 Toon。", smallStyle);
            if (ordinaryLighting && keyLight != null)
            {
                GUILayout.Label("主灯方向", smallStyle);
                float yaw = GUILayout.HorizontalSlider(keyLight.transform.eulerAngles.y, 0f, 360f);
                keyLight.transform.rotation = Quaternion.Euler(35f, yaw, 0f);
            }
            GUILayout.Space(6);
            GUILayout.Label("原地检查动作（WASD 恢复移动）", labelStyle);
            GUILayout.BeginHorizontal();
            StationaryButton("idle", "待机"); StationaryButton("walk", "走路"); StationaryButton("run", "跑步");
            GUILayout.EndHorizontal();
            GUILayout.Label("情绪动作（移动可退出）", labelStyle);
            GUILayout.BeginHorizontal();
            EmotionButton("afraid", "1 害怕"); EmotionButton("agree", "2 同意"); EmotionButton("sob", "3 哭泣");
            GUILayout.EndHorizontal();
            GUILayout.Space(6);
            GUILayout.Label("表情", labelStyle);
            automaticBlink = GUILayout.Toggle(automaticBlink, "自动眨眼", new GUIStyle(GUI.skin.toggle) { font = uiFont });
            if (GUILayout.Button("眨眼测试", buttonStyle)) Blink();
            GUILayout.Label("闭眼检查 " + Mathf.RoundToInt(manualBlink) + "%（0 恢复自动）", smallStyle);
            manualBlink = GUILayout.HorizontalSlider(manualBlink, 0f, 100f);
            GUILayout.Label("表情强度 " + Mathf.RoundToInt(expressionWeight) + "%", smallStyle);
            expressionWeight = GUILayout.HorizontalSlider(expressionWeight, 0f, 100f);
            GUILayout.BeginHorizontal();
            ExpressionButton("Smile", "微笑"); ExpressionButton("Surprised", "惊讶");
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            ExpressionButton("BrowUp", "抬眉"); ExpressionButton("BrowDown", "压眉");
            GUILayout.EndHorizontal();
            if (GUILayout.Button("表情归零", buttonStyle)) { expression = ""; manualBlink = 0f; }
            if (!HasExpression("Blink_L") && !HasExpression("Blink_R")) GUILayout.Label("本版本未导入眨眼 BlendShape。", smallStyle);
            if (secondaryMotion != null)
            {
                bool enabledMotion = GUILayout.Toggle(secondaryMotion.motionEnabled, "辅助骨摆动试验（待验收）", new GUIStyle(GUI.skin.toggle) { font = uiFont });
                if (enabledMotion != secondaryMotion.motionEnabled)
                {
                    secondaryMotion.motionEnabled = enabledMotion;
                    secondaryMotion.ResetSimulation();
                }
                GUILayout.Label("辅助链：" + secondaryMotion.chains.Length + "；需要逐姿态检查碰撞。", smallStyle);
            }
            if (GUILayout.Button("角色归位", buttonStyle)) ResetPosition();
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        void EmotionButton(string name, string label)
        {
            GUI.enabled = HasClip(name);
            if (GUILayout.Button(label, buttonStyle)) PlayEmotion(name);
            GUI.enabled = true;
        }

        void StationaryButton(string name, string label)
        {
            GUI.enabled = HasClip(name);
            if (GUILayout.Button(label, buttonStyle)) { stationaryPreview = name; emotion = ""; }
            GUI.enabled = true;
        }

        void ExpressionButton(string name, string label)
        {
            GUI.enabled = HasExpression(name);
            if (GUILayout.Button(label, buttonStyle)) expression = name;
            GUI.enabled = true;
        }

        void OnDestroy() { if (uiFont != null) Destroy(uiFont); }
    }
}
