using UnityEngine;

namespace ForestSpriteCharacter
{
    [RequireComponent(typeof(CharacterController))]
    public sealed class ForestSpriteDemoController : MonoBehaviour
    {
        public Animator animator;
        public Transform visual;
        public Camera demoCamera;
        public bool externalCameraControl;
        public bool showControls = true;
        public ForestSpriteSecondaryMotion secondaryMotion;
        public ForestSpriteCapeCloth capeCloth;
        public float walkSpeed = 2.25f;
        [Range(0f, 1f)] public float walkStride = 1f;
        public float runSpeed = 4.5f;
        [HideInInspector] public float originalWalkCycleDistance = .264f;
        [HideInInspector] public float walkCycleDistance = .72f;
        [HideInInspector] public float runCycleDistance = 1.230769f;
        public float jumpHeight = .48f;
        public float gravity = -8f;
        public float cameraHeight = .8f;
        public float cameraDistance = 3.5f;
        public float cameraYaw = 170f;
        public float cameraPitch = 8f;

        public enum Demonstration { Manual, Idle, Walk, Run, Jump, Sequence }
        public Demonstration demonstration;
        public string CurrentState { get; private set; } = "";
        public bool IsGrounded => controller != null && (controller.isGrounded || (AnimalCollisionFlags & CollisionFlags.Below) != 0);
        public float VerticalSpeed => verticalSpeed;
        public Vector3 ActualVelocity { get; private set; }
        public float JumpPhase { get; private set; }
        public float JumpOpenness => CurrentState == "Jump" ?
            Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.16f, .38f, JumpPhase)) *
            (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.78f, 1f, JumpPhase))) : 0f;
        public bool ScriptedInput { get; private set; }
        public System.Func<Vector3, Vector3> ConstrainDisplacement { get; set; }
        public System.Func<Vector3, Vector3> ConstrainAnimalDisplacement { get; set; }
        public System.Action<float> RefreshAnimalContacts { get; set; }
        public CollisionFlags AnimalCollisionFlags { get; set; }

        CharacterController controller;
        Vector3 startPosition;
        Quaternion startRotation;
        Vector2 scriptedMovement;
        bool scriptedRun, scriptedJump;
        float verticalSpeed, modeStarted, nextJump;
        enum JumpStage { None, Anticipation, Air, Landing }
        JumpStage jumpStage;
        float jumpElapsed;
        const float AnticipationDuration = .14f, LandingDuration = .22f;
        GUIStyle label, button;

        void Awake()
        {
            controller = GetComponent<CharacterController>();
            startPosition = transform.position;
            startRotation = transform.rotation;
            if (demoCamera == null) demoCamera = Camera.main;
            if (animator == null || visual == null)
            {
                Debug.LogError("Forest sprite requires an Animator and its visual root.", this);
                enabled = false;
                return;
            }
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            SwitchState("Idle");
        }

        void Update()
        {
            float stride = Mathf.Clamp01(walkStride);
            animator.SetFloat("WalkStride", stride);
            // One cycle includes both steps. Match the planted foot's backward travel to root movement.
            animator.SetFloat("WalkCadence", Mathf.Max(0f, walkSpeed) /
                Mathf.Max(.01f, Mathf.Lerp(originalWalkCycleDistance, walkCycleDistance, stride)));
            animator.SetFloat("RunCadence", Mathf.Max(0f, runSpeed) / Mathf.Max(.01f, runCycleDistance));
            if (Input.GetKeyDown(KeyCode.R)) ResetPosition();
            if (Input.GetKeyDown(KeyCode.P)) ToggleSecondaryMotion();
            for (int i = 0; i <= 5; i++)
                if (Input.GetKeyDown((KeyCode)((int)KeyCode.Alpha0 + i))) SetDemonstration((Demonstration)i);

            var movement = new Vector2((Input.GetKey(KeyCode.D) ? 1 : 0) - (Input.GetKey(KeyCode.A) ? 1 : 0),
                (Input.GetKey(KeyCode.W) ? 1 : 0) - (Input.GetKey(KeyCode.S) ? 1 : 0));
            bool running = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            bool jump = Input.GetKeyDown(KeyCode.Space);
            if (ScriptedInput)
            {
                movement = scriptedMovement;
                running = scriptedRun;
                jump = scriptedJump;
                scriptedJump = false;
            }
            else if (movement.sqrMagnitude > 0f || jump)
                demonstration = Demonstration.Manual;
            else if (demonstration != Demonstration.Manual)
                DemoInput(out movement, out running, out jump);

            Vector3 forward = demoCamera != null
                ? Vector3.ProjectOnPlane(demoCamera.transform.forward, Vector3.up).normalized : Vector3.forward;
            Vector3 direction = forward * movement.y + Vector3.Cross(Vector3.up, forward) * movement.x;
            direction = Vector3.ClampMagnitude(direction, 1f);
            AdvanceMovement(direction, running, jump, Time.deltaTime);
            animator.SetFloat("JumpPhase", JumpPhase);
            string state = jumpStage != JumpStage.None || !IsGrounded ? "Jump" :
                direction.sqrMagnitude > .001f ? (running ? "Run" : "Walk") : "Idle";
            SwitchState(state);
            if (transform.position.y < -4f) ResetPosition();

            if (demoCamera != null && !ScriptedInput && !externalCameraControl)
            {
                bool overPanel = (Input.mousePosition.x < 320f && Screen.height - Input.mousePosition.y < 310f)
                    || ForestSpriteCapePanel.PointerOverPanel;
                if (Input.GetMouseButton(1) && !overPanel)
                {
                    cameraYaw += Input.GetAxis("Mouse X") * 3f;
                    cameraPitch = Mathf.Clamp(cameraPitch - Input.GetAxis("Mouse Y") * 2f, -10f, 65f);
                }
                if (!overPanel) cameraDistance = Mathf.Clamp(cameraDistance - Input.mouseScrollDelta.y * .2f, 1.5f, 7f);
            }
        }

        void AdvanceMovement(Vector3 direction, bool running, bool jump, float deltaTime)
        {
            Vector3 positionBeforeMove = transform.position;
            // Keep collision steps bounded without discarding time on slow frames.
            int steps = Mathf.CeilToInt(deltaTime / .05f);
            float dt = deltaTime / Mathf.Max(1, steps);
            for (int step = 0; step < steps; step++)
            {
                bool grounded = IsGrounded;
                if (grounded && verticalSpeed < 0f) verticalSpeed = -2f;
                if (jump && grounded && jumpStage != JumpStage.Anticipation)
                {
                    jumpStage = JumpStage.Anticipation;
                    jumpElapsed = 0f;
                    JumpPhase = 0f;
                    SwitchState("Jump", true);
                }
                jump = false;
                if (jumpStage == JumpStage.Anticipation)
                {
                    jumpElapsed += dt;
                    JumpPhase = .22f * Mathf.Clamp01(jumpElapsed / AnticipationDuration);
                    if (jumpElapsed >= AnticipationDuration)
                    {
                        verticalSpeed = Mathf.Sqrt(-2f * gravity * jumpHeight);
                        jumpStage = JumpStage.Air;
                    }
                }
                else if (jumpStage == JumpStage.Landing)
                {
                    jumpElapsed += dt;
                    JumpPhase = Mathf.Lerp(.78f, 1f, jumpElapsed / LandingDuration);
                    if (jumpElapsed >= LandingDuration) jumpStage = JumpStage.None;
                }
                else if (!grounded && jumpStage == JumpStage.None)
                    jumpStage = JumpStage.Air;
                if (jumpStage == JumpStage.Air)
                {
                    float launchSpeed = Mathf.Sqrt(-2f * gravity * jumpHeight);
                    // The pose follows actual upward/downward velocity, so ceilings and early landing remain coherent.
                    float flight = Mathf.Clamp01(.5f - .5f * verticalSpeed / Mathf.Max(.01f, launchSpeed));
                    JumpPhase = Mathf.Lerp(.22f, .78f, flight);
                }
                if (direction.sqrMagnitude > .001f)
                    transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(direction), 540f * dt);
                verticalSpeed += gravity * dt;
                Vector3 displacement = (direction * (running ? runSpeed : walkSpeed) + Vector3.up * verticalSpeed) * dt;
                if (ConstrainDisplacement != null) displacement = ConstrainDisplacement(displacement);
                float requestedAnimalY = displacement.y;
                if (ConstrainAnimalDisplacement != null) displacement = ConstrainAnimalDisplacement(displacement);
                var collision = controller.Move(displacement);
                RefreshAnimalContacts?.Invoke(requestedAnimalY);
                collision |= AnimalCollisionFlags;
                if ((collision & CollisionFlags.Above) != 0 && verticalSpeed > 0f) verticalSpeed = 0f;
                if (IsGrounded && verticalSpeed <= 0f && jumpStage == JumpStage.Air)
                {
                    jumpStage = JumpStage.Landing;
                    jumpElapsed = 0f;
                    JumpPhase = .78f;
                }
            }
            ActualVelocity = deltaTime > 0f ? (transform.position - positionBeforeMove) / deltaTime : Vector3.zero;
        }

        void LateUpdate()
        {
            if (demoCamera == null || externalCameraControl) return;
            Vector3 target = transform.position + Vector3.up * cameraHeight;
            demoCamera.transform.position = target + Quaternion.Euler(cameraPitch, cameraYaw, 0f) * Vector3.back * cameraDistance;
            demoCamera.transform.LookAt(target);
        }

        void DemoInput(out Vector2 movement, out bool running, out bool jump)
        {
            Demonstration mode = demonstration;
            float elapsed = Time.time - modeStarted;
            if (mode == Demonstration.Sequence)
            {
                float phase = Mathf.Repeat(elapsed, 12f);
                mode = phase < 2f ? Demonstration.Idle : phase < 5f ? Demonstration.Walk :
                    phase < 8f ? Demonstration.Run : phase < 10f ? Demonstration.Jump : Demonstration.Idle;
            }
            bool moving = mode == Demonstration.Walk || mode == Demonstration.Run;
            movement = moving ? new Vector2(Mathf.Sin(elapsed * .8f), Mathf.Cos(elapsed * .8f)) : Vector2.zero;
            running = mode == Demonstration.Run;
            jump = mode == Demonstration.Jump && IsGrounded && Time.time >= nextJump;
            if (jump) nextJump = Time.time + 1.6f;
        }

        void SwitchState(string state, bool restart = false)
        {
            if (!restart && state == CurrentState) return;
            float phase = 0f;
            if (IsGait(CurrentState) && IsGait(state))
                phase = Mathf.Repeat(animator.GetCurrentAnimatorStateInfo(0).normalizedTime, 1f);
            if (CurrentState.Length == 0) animator.Play(state, 0, 0f);
            else if (state == "Jump") animator.CrossFadeInFixedTime(state, .04f, 0, 0f);
            else animator.CrossFade(state, .12f, 0, phase);
            CurrentState = state;
        }

        public static bool IsGait(string state) => state == "Walk" || state == "Run";

        public void SetDemonstration(Demonstration mode)
        {
            demonstration = mode;
            modeStarted = Time.time;
            nextJump = Time.time;
        }

        public void SetScriptedInput(Vector2 movement, bool run, bool jump)
        {
            ScriptedInput = true;
            scriptedMovement = Vector2.ClampMagnitude(movement, 1f);
            scriptedRun = run;
            scriptedJump |= jump;
        }

        public void ReleaseScriptedInput()
        {
            ScriptedInput = false;
            scriptedMovement = Vector2.zero;
            scriptedJump = false;
            SetDemonstration(Demonstration.Manual);
        }

        public void ResetPosition()
        {
            controller.enabled = false;
            transform.SetPositionAndRotation(startPosition, startRotation);
            controller.enabled = true;
            verticalSpeed = 0f;
            ActualVelocity = Vector3.zero;
            jumpStage = JumpStage.None;
            jumpElapsed = JumpPhase = 0f;
            animator.SetFloat("JumpPhase", 0f);
            if (secondaryMotion != null) secondaryMotion.ResetSimulation();
            if (capeCloth != null) capeCloth.ResetSimulation();
            SetDemonstration(Demonstration.Manual);
            SwitchState("Idle", true);
        }

        public void ToggleSecondaryMotion()
        {
            if (secondaryMotion == null) return;
            secondaryMotion.MotionEnabled = !secondaryMotion.MotionEnabled;
            if (capeCloth != null) capeCloth.SetMotionEnabled(secondaryMotion.MotionEnabled);
            secondaryMotion.ResetSimulation();
        }

        void OnGUI()
        {
            if (!showControls) return;
            if (label == null)
            {
                var font = Font.CreateDynamicFontFromOSFont("Microsoft YaHei", 14);
                label = new GUIStyle(GUI.skin.label) { font = font, fontSize = 14, wordWrap = true };
                button = new GUIStyle(GUI.skin.button) { font = font, fontSize = 14 };
            }
            GUILayout.BeginArea(new Rect(12, 12, 300, 295), GUI.skin.box);
            GUILayout.Label("森林小精灵 · 柔软布料预览", label);
            GUILayout.Label("WASD 走路 / Shift 跑步 / Space 跳跃\n右键转视角 / 滚轮远近 / R 归位", label);
            GUILayout.Label("当前动作 " + CurrentState + (IsGrounded ? " · 地面" : " · 空中"), label);
            GUILayout.BeginHorizontal();
            DemoButton("1 待机", Demonstration.Idle); DemoButton("2 走路", Demonstration.Walk);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            DemoButton("3 跑步", Demonstration.Run); DemoButton("4 跳跃", Demonstration.Jump);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            DemoButton("5 循环演示", Demonstration.Sequence); DemoButton("0 手动", Demonstration.Manual);
            GUILayout.EndHorizontal();
            if (GUILayout.Button("P 披风布料 / 帽尖与发梢 " +
                (secondaryMotion != null && secondaryMotion.MotionEnabled ? "开" : "关"), button)) ToggleSecondaryMotion();
            if (GUILayout.Button("R 角色归位", button)) ResetPosition();
            GUILayout.EndArea();
        }

        void DemoButton(string text, Demonstration mode)
        {
            if (GUILayout.Button(text, button)) SetDemonstration(mode);
        }
    }
}
