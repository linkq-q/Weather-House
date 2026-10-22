using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace WeatherHouseSquirrelLab.Editor
{
    public sealed class SquirrelAnimationWorkbench : EditorWindow
    {
        [SerializeField] GameObject sourceModel;
        [SerializeField] AnimationClip sourceClip;
        [SerializeField] SquirrelWorkbenchParameters parameters = new SquirrelWorkbenchParameters();
        [SerializeField] float time, speed = 1f, yaw = -25f, pitch = 18f, zoom = 1.55f;
        [SerializeField] bool playing = true, original, showBones, showCollision = true;
        [SerializeField] int tab;
        Vector2 scroll;
        PreviewRenderUtility preview;
        GameObject model;
        Material floorMaterial;
        SquirrelWorkbenchRig rig;
        SquirrelWorkbenchMotion motion;
        double lastTick;
        int rigRevision, savedRigRevision;
        float milliseconds;
        string message = "右键旋转 · 滚轮缩放 · 拖动时间轴检查动作", error = "";
        SquirrelWorkbenchPreset selectedPreset;
        static readonly string[] Actions = { "walk_biped", "run_biped", "wave", "dance", "turn", "jump_biped" };
        static readonly string[] Labels = { "行走", "奔跑", "招手", "跳舞", "转身", "跳跃" };
        public GameObject PreviewModel => model;
        public SquirrelWorkbenchRig Rig => rig;
        public SquirrelWorkbenchMotion Motion => motion;
        public SquirrelWorkbenchParameters Parameters => parameters;
        public AnimationClip SourceClip => sourceClip;
        public Camera PreviewCamera => preview?.camera;
        public int EvaluatedFrames { get; private set; }
        public float EvaluationMilliseconds => milliseconds;

        [MenuItem("Weather House/动物动画工作台")]
        public static SquirrelAnimationWorkbench Open()
        {
            var window = GetWindow<SquirrelAnimationWorkbench>();
            window.titleContent = new GUIContent("动物动画工作台");
            window.minSize = new Vector2(920, 620);
            window.Show();
            return window;
        }

        void OnEnable()
        {
            titleContent = new GUIContent("动物动画工作台");
            minSize = new Vector2(920, 620);
            saveChangesMessage = "骨骼或权重修改尚未另存为模型副本。";
            if (sourceModel == null) sourceModel = AssetDatabase.LoadAssetAtPath<GameObject>(SquirrelLabBuilder.Root + "/Models/walk_biped.fbx");
            if (sourceClip == null) sourceClip = FindClip("walk_biped");
            EditorApplication.update += Tick;
            Undo.undoRedoPerformed += OnUndo;
            lastTick = EditorApplication.timeSinceStartup;
        }

        void OnDisable()
        {
            EditorApplication.update -= Tick;
            Undo.undoRedoPerformed -= OnUndo;
            DisposePreview();
        }

        void OnUndo()
        {
            if (rig != null) { rig.ReapplyAfterUndo(); RebuildMotion(); }
            Evaluate(true);
            Repaint();
        }

        void Tick()
        {
            double now = EditorApplication.timeSinceStartup;
            float dt = Mathf.Clamp((float)(now - lastTick), 0, .05f);
            if (now - lastTick < 1.0 / 60) return;
            lastTick = now;
            if (EditorApplication.isCompiling || preview == null || model == null) return;
            try
            {
                if (playing && tab != 2 && sourceClip != null) time = Mathf.Repeat(time + dt * speed, Mathf.Max(.01f, sourceClip.length));
                var watch = System.Diagnostics.Stopwatch.StartNew();
                Evaluate(false, playing ? dt * speed : 0);
                EvaluatedFrames++;
                milliseconds = Mathf.Lerp(milliseconds, (float)watch.Elapsed.TotalMilliseconds, .1f);
                hasUnsavedChanges = rig != null && rig.Revision != savedRigRevision;
                Repaint();
            }
            catch (Exception exception) { error = exception.Message; playing = false; }
        }

        void OnGUI()
        {
            DrawAssets();
            if (preview == null && sourceModel != null)
            {
                try { CreatePreview(); }
                catch (Exception exception) { error = exception.ToString(); }
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUILayout.VerticalScope(GUILayout.ExpandWidth(true)))
                {
                    DrawPlayback();
                    Rect area = GUILayoutUtility.GetRect(320, 300, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
                    DrawPreview(area);
                    using (new EditorGUI.DisabledScope(sourceClip == null || tab == 2))
                    {
                        EditorGUI.BeginChangeCheck();
                        float t = EditorGUILayout.Slider("时间 / 秒", time, 0, sourceClip != null ? sourceClip.length : 1);
                        if (EditorGUI.EndChangeCheck()) { time = t; playing = false; Evaluate(true); }
                    }
                    EditorGUILayout.LabelField(tab == 2 ? "绑定姿态 · 左键选择 / 刷权重 · 右键旋转" : message, EditorStyles.miniLabel);
                    EditorGUILayout.LabelField("预览计算 " + milliseconds.ToString("0.0") + " ms  ·  " + (rig == null ? 0 : rig.Bones.Length) + " 根骨骼", EditorStyles.miniLabel);
                }
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox, GUILayout.Width(340)))
                {
                    int nextTab = GUILayout.Toolbar(tab, new[] { "动作", "弹性", "骨骼 / 权重" }, GUILayout.Height(30));
                    if (nextTab != tab) { tab = nextTab; Evaluate(true); }
                    scroll = EditorGUILayout.BeginScrollView(scroll);
                    if (tab == 2)
                    {
                        if (rig != null && rig.DrawInspector()) RebuildMotion();
                    }
                    else DrawParameters();
                    EditorGUILayout.EndScrollView();
                    DrawSaveControls();
                }
            }
            if (!string.IsNullOrEmpty(error)) EditorGUILayout.HelpBox(error, MessageType.Error);
        }

        void DrawAssets()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                GUILayout.Label("动物动画工作台", EditorStyles.boldLabel, GUILayout.Width(118));
                var nextModel = (GameObject)EditorGUILayout.ObjectField(sourceModel, typeof(GameObject), false, GUILayout.MinWidth(150));
                if (nextModel != sourceModel && ConfirmDiscard()) { sourceModel = nextModel; DisposePreview(); error = ""; }
                var nextClip = (AnimationClip)EditorGUILayout.ObjectField(sourceClip, typeof(AnimationClip), false, GUILayout.MinWidth(150));
                if (nextClip != sourceClip) SetClip(nextClip);
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label("松鼠动作", GUILayout.Width(64));
                for (int i = 0; i < Actions.Length; i++) if (GUILayout.Button(Labels[i], EditorStyles.miniButton)) SetClip(FindClip(Actions[i]));
            }
        }

        static AnimationClip FindClip(string id) => AssetDatabase.LoadAllAssetsAtPath(SquirrelLabBuilder.Root + "/Models/" + id + ".fbx")
            .OfType<AnimationClip>().FirstOrDefault(c => !c.name.StartsWith("__preview__", StringComparison.Ordinal));

        void SetClip(AnimationClip value)
        {
            if (value == null) return;
            sourceClip = value; time = 0; error = "";
            RebuildMotion(); Evaluate(true);
        }

        void DrawPlayback()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                using (new EditorGUI.DisabledScope(tab == 2))
                {
                    if (GUILayout.Button(playing ? "暂停" : "播放", EditorStyles.toolbarButton, GUILayout.Width(54))) playing = !playing;
                    if (GUILayout.Button("回到开始", EditorStyles.toolbarButton, GUILayout.Width(72))) { time = 0; Evaluate(true); }
                    speed = EditorGUILayout.Slider(speed, .1f, 2f, GUILayout.Width(145));
                    GUILayout.Label("倍速", GUILayout.Width(32));
                    bool raw = GUILayout.Toggle(original, "原始动作对照", EditorStyles.toolbarButton);
                    if (raw != original) { original = raw; Evaluate(true); }
                }
                GUILayout.FlexibleSpace();
                showBones = GUILayout.Toggle(showBones, "骨骼", EditorStyles.toolbarButton);
                if (GUILayout.Button("正面", EditorStyles.toolbarButton)) { yaw = 0; pitch = 16; }
                if (GUILayout.Button("侧面", EditorStyles.toolbarButton)) { yaw = -75; pitch = 12; }
                if (GUILayout.Button("复位视角", EditorStyles.toolbarButton)) { yaw = -25; pitch = 18; zoom = 1.55f; }
            }
        }

        void DrawParameters()
        {
            var serialized = new SerializedObject(this);
            serialized.Update();
            SerializedProperty p = serialized.FindProperty("parameters");
            if (tab == 0)
            {
                EditorGUILayout.LabelField("动作幅度", EditorStyles.boldLabel);
                Slider(p, "armAmplitude", "手臂摆幅", 0, 3);
                using (new EditorGUI.DisabledScope(p.FindPropertyRelative("strideEnabled").boolValue && motion != null && motion.IsLocomotion))
                    Slider(p, "legAmplitude", "原始腿部摆幅", 0, 2f);
                Slider(p, "bodyAmplitude", "躯干摆幅", 0, 2.5f);
                EditorGUILayout.Space(8);
                EditorGUILayout.PropertyField(p.FindPropertyRelative("strideEnabled"), new GUIContent("双足步态与接地修正"));
                if (motion != null && !motion.IsLocomotion) EditorGUILayout.HelpBox("步态修正用于行走、奔跑；其他动作保留其原有节奏。", MessageType.Info);
                using (new EditorGUI.DisabledScope(!p.FindPropertyRelative("strideEnabled").boolValue || (motion != null && !motion.IsLocomotion)))
                {
                    Slider(p, "strideLength", "步幅 / 腿长", .05f, .82f);
                    Slider(p, "footLift", "抬脚 / 腿长", .03f, .55f);
                    Slider(p, "hipBounce", "髋部起伏", 0, .18f);
                    Slider(p, "weightShift", "左右重心", 0, 1.8f);
                    Slider(p, "upperBody", "肩胯反摆", 0, 2);
                }
                EditorGUILayout.HelpBox("调整步幅时会保留腿骨长度。先用侧面看前后迈步，再看正面的重心和摆臂。", MessageType.None);
            }
            else
            {
                EditorGUILayout.PropertyField(p.FindPropertyRelative("bodyElasticity"), new GUIContent("身体弹性"));
                using (new EditorGUI.DisabledScope(!p.FindPropertyRelative("bodyElasticity").boolValue))
                {
                    Slider(p, "bodyStiffness", "身体硬度", 20, 400);
                    Slider(p, "bodyDamping", "身体阻尼", 0, 50);
                }
                EditorGUILayout.Space(8);
                EditorGUILayout.PropertyField(p.FindPropertyRelative("tailElasticity"), new GUIContent("尾巴弹性"));
                Slider(p, "tailAmplitude", "尾巴左右摆幅 / 度", 0, 65);
                Slider(p, "tailFrequency", "尾巴频率 / Hz", .1f, 2);
                using (new EditorGUI.DisabledScope(!p.FindPropertyRelative("tailElasticity").boolValue))
                {
                    Slider(p, "tailStiffness", "尾巴硬度", 5, 300);
                    Slider(p, "tailDamping", "尾巴阻尼", 0, 40);
                }
                EditorGUILayout.Space(8);
                EditorGUILayout.PropertyField(p.FindPropertyRelative("collisionEnabled"), new GUIContent("简单碰撞约束"));
                Slider(p, "collisionRadius", "身体碰撞半径 / 身高", .08f, .4f);
                showCollision = EditorGUILayout.Toggle("显示碰撞范围", showCollision);
                EditorGUILayout.HelpBox("硬度越低越软，阻尼越低回弹越多。碰撞使用身体球与地面近似约束，不能代替全网格碰撞。", MessageType.None);
            }
            if (serialized.ApplyModifiedProperties()) { original = false; Evaluate(false); }
            if (GUILayout.Button("恢复默认动作与弹性"))
            {
                Undo.RecordObject(this, "恢复动画工作台参数"); parameters = new SquirrelWorkbenchParameters(); original = false; Evaluate(true);
            }
        }

        static void Slider(SerializedProperty parent, string name, string label, float min, float max)
        {
            var property = parent.FindPropertyRelative(name);
            if (property == null) throw new InvalidOperationException("工作台参数缺失：" + name);
            EditorGUILayout.Slider(property, min, max, new GUIContent(label));
        }

        void DrawSaveControls()
        {
            EditorGUILayout.Space(8);
            selectedPreset = (SquirrelWorkbenchPreset)EditorGUILayout.ObjectField("参数预设", selectedPreset, typeof(SquirrelWorkbenchPreset), false);
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(selectedPreset == null)) if (GUILayout.Button("载入预设")) LoadPreset(selectedPreset);
                if (GUILayout.Button("另存参数")) SavePreset();
            }
            using (new EditorGUI.DisabledScope(model == null || sourceClip == null))
                if (GUILayout.Button("烘焙为新动画 .anim", GUILayout.Height(29))) ExportAnimation();
            using (new EditorGUI.DisabledScope(rig == null))
                if (GUILayout.Button("另存骨骼 / 权重模型副本", GUILayout.Height(29))) ExportModel();
            EditorGUILayout.LabelField("参数预设不包含权重；权重请另存模型。", EditorStyles.miniLabel);
        }

        void CreatePreview()
        {
            DisposePreview();
            if (sourceModel == null) return;
            preview = new PreviewRenderUtility();
            preview.camera.orthographic = true;
            preview.camera.nearClipPlane = .01f; preview.camera.farClipPlane = 100;
            preview.camera.clearFlags = CameraClearFlags.SolidColor;
            preview.camera.backgroundColor = new Color(.085f, .105f, .13f);
            preview.camera.allowHDR = false;
            preview.ambientColor = new Color(.5f, .53f, .57f);
            preview.lights[0].intensity = 1.25f; preview.lights[0].transform.rotation = Quaternion.Euler(45, -35, 0);
            preview.lights[1].intensity = .9f; preview.lights[1].transform.rotation = Quaternion.Euler(20, 130, 0);
            model = Instantiate(sourceModel); model.name = sourceModel.name;
            model.hideFlags = HideFlags.HideAndDontSave;
            foreach (Behaviour behaviour in model.GetComponentsInChildren<Behaviour>(true)) behaviour.enabled = false;
            foreach (Renderer renderer in model.GetComponentsInChildren<Renderer>(true)) renderer.enabled = renderer is SkinnedMeshRenderer;
            var bones = model.GetComponentsInChildren<Transform>();
            var head = bones.FirstOrDefault(b => b.name == "Head"); var pelvis = bones.FirstOrDefault(b => b.name == "Pelvis");
            if (head != null && pelvis != null)
            {
                Vector3 forward = Vector3.ProjectOnPlane(head.position - pelvis.position, Vector3.up);
                if (forward.sqrMagnitude > .00001f) model.transform.rotation = Quaternion.FromToRotation(forward.normalized, Vector3.back) * model.transform.rotation;
            }
            Bounds bounds = BindBounds(model);
            if (bounds.size.y < .0001f) throw new InvalidOperationException("模型没有可预览的蒙皮网格。");
            model.transform.localScale *= 2.05f / bounds.size.y;
            bounds = BindBounds(model);
            model.transform.position += new Vector3(-bounds.center.x, .005f - bounds.min.y, -bounds.center.z);
            if (AssetDatabase.GetAssetPath(sourceModel).StartsWith(SquirrelLabBuilder.Root + "/Models/", StringComparison.Ordinal))
            {
                var material = AssetDatabase.LoadAssetAtPath<Material>(SquirrelLabBuilder.Generated + "/walk_biped_0.mat");
                if (material != null) foreach (var renderer in model.GetComponentsInChildren<SkinnedMeshRenderer>()) renderer.sharedMaterial = material;
            }
            preview.AddSingleGO(model);
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.hideFlags = HideFlags.HideAndDontSave; floor.name = "Workbench preview floor";
            floor.transform.position = new Vector3(0, -.02f, 0); floor.transform.localScale = new Vector3(4.4f, .04f, 4.4f);
            DestroyImmediate(floor.GetComponent<Collider>());
            floorMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
            floorMaterial.hideFlags = HideFlags.HideAndDontSave; floorMaterial.color = new Color(.2f, .26f, .3f);
            floor.GetComponent<Renderer>().sharedMaterial = floorMaterial;
            preview.AddSingleGO(floor);
            rig = new SquirrelWorkbenchRig(model); rigRevision = savedRigRevision = rig.Revision;
            RebuildMotion(); Evaluate(true); error = "";
        }

        static Bounds BindBounds(GameObject root)
        {
            bool first = true; Bounds result = default;
            foreach (var renderer in root.GetComponentsInChildren<SkinnedMeshRenderer>())
                foreach (Vector3 vertex in renderer.sharedMesh.vertices)
                {
                    Vector3 point = renderer.transform.TransformPoint(vertex);
                    if (first) { result = new Bounds(point, Vector3.zero); first = false; } else result.Encapsulate(point);
                }
            return result;
        }

        void RebuildMotion()
        {
            motion?.Dispose(); motion = null;
            if (rig == null || sourceClip == null) return;
            rig.RestoreBindPose();
            motion = new SquirrelWorkbenchMotion(model, sourceClip) { ApplyRigOffsets = rig.ApplyOffsets };
            rigRevision = rig.Revision;
        }

        void Evaluate(bool reset, float dt = 0)
        {
            if (rig == null || model == null) return;
            if (rig.Revision != rigRevision) RebuildMotion();
            if (tab == 2 || sourceClip == null) { rig.RestoreBindPose(); return; }
            if (original) { rig.RestoreBindPose(); sourceClip.SampleAnimation(model, time); rig.ApplyOffsets(); }
            else motion?.Evaluate(time, dt, parameters, reset);
        }

        void DrawPreview(Rect area)
        {
            if (preview == null || model == null) { EditorGUI.DrawRect(area, new Color(.12f, .14f, .17f)); return; }
            Event e = Event.current;
            if (area.Contains(e.mousePosition))
            {
                if (e.type == EventType.MouseDrag && e.button == 1) { yaw += e.delta.x * .5f; pitch = Mathf.Clamp(pitch - e.delta.y * .4f, -8, 70); e.Use(); }
                if (e.type == EventType.ScrollWheel) { zoom = Mathf.Clamp(zoom * Mathf.Exp(e.delta.y * .04f), .25f, 5); e.Use(); }
            }
            preview.camera.orthographicSize = zoom;
            Vector3 center = new Vector3(0, 1, 0);
            preview.camera.transform.position = center + Quaternion.Euler(pitch, yaw, 0) * Vector3.back * 8;
            preview.camera.transform.LookAt(center);
            if (e.type == EventType.Repaint)
            {
                preview.BeginPreview(area, GUIStyle.none);
                try
                {
                    preview.Render(true);
                    if (tab == 1 && showCollision && motion != null)
                    {
                        var previous = RenderTexture.active;
                        RenderTexture.active = preview.camera.targetTexture;
                        using (new Handles.DrawingScope(Matrix4x4.identity)) { Handles.SetCamera(preview.camera); motion.DrawGizmos(); }
                        RenderTexture.active = previous;
                    }
                }
                finally { GUI.DrawTexture(area, preview.EndPreview(), ScaleMode.StretchToFill, false); }
                GUI.Label(new Rect(area.x + 12, area.y + 10, area.width - 24, 24), tab == 2 ? "骨骼 / 权重编辑副本" : original ? "原始动作" : "实时调整", EditorStyles.whiteLargeLabel);
            }
            if (tab == 2 || showBones) rig.DrawOverlay(area, preview.camera, tab == 2);
            if (rig.Revision != rigRevision) { RebuildMotion(); Evaluate(true); }
        }

        void DisposePreview()
        {
            motion?.Dispose(); motion = null;
            rig?.Dispose(); rig = null;
            preview?.Cleanup(); preview = null; model = null;
            if (floorMaterial != null) DestroyImmediate(floorMaterial);
            floorMaterial = null;
        }

        bool ConfirmDiscard() => rig == null || rig.Revision == savedRigRevision || EditorUtility.DisplayDialog("骨骼或权重尚未另存", "切换模型会清除当前预览副本的骨骼和权重修改。参数仍会保留。", "继续切换", "取消");

        string SavePath(string title, string name, string extension)
        {
            string folder = SquirrelLabBuilder.Generated + "/Workbench";
            Directory.CreateDirectory(folder); AssetDatabase.Refresh();
            string path = EditorUtility.SaveFilePanelInProject(title, name, extension, "保存为新的独立资源", folder);
            return string.IsNullOrEmpty(path) ? null : AssetDatabase.GenerateUniqueAssetPath(path);
        }

        void SavePreset()
        {
            string path = SavePath("保存参数预设", "松鼠动作参数", "asset");
            if (path == null) return;
            var preset = CreateInstance<SquirrelWorkbenchPreset>();
            preset.sourceModel = sourceModel; preset.sourceClip = sourceClip;
            preset.parameters = JsonUtility.FromJson<SquirrelWorkbenchParameters>(JsonUtility.ToJson(parameters));
            preset.boneOffsets = rig?.Edits;
            AssetDatabase.CreateAsset(preset, path); AssetDatabase.SaveAssets(); selectedPreset = preset;
            message = "已保存参数预设：" + path;
        }

        public void LoadPreset(SquirrelWorkbenchPreset preset)
        {
            if (preset == null || !ConfirmDiscard()) return;
            sourceModel = preset.sourceModel; sourceClip = preset.sourceClip;
            parameters = JsonUtility.FromJson<SquirrelWorkbenchParameters>(JsonUtility.ToJson(preset.parameters));
            CreatePreview(); rig.SetEdits(preset.boneOffsets); RebuildMotion(); time = 0; Evaluate(true);
            message = "已载入预设；权重来自所选模型副本。";
        }

        void ExportAnimation()
        {
            string path = SavePath("保存新动画", sourceModel.name + "_调整动画", "anim");
            if (path == null) return;
            try
            {
                motion.ResetPhysics();
                var clip = SquirrelWorkbenchExport.BakeClip(model, sourceClip, (t, dt, reset) => motion.Evaluate(t, dt, parameters, reset), path);
                EditorGUIUtility.PingObject(clip); message = "动画已烘焙：" + path; error = ""; Evaluate(true);
            }
            catch (Exception exception) { error = exception.Message; }
        }

        bool ExportModel()
        {
            string path = SavePath("保存骨骼与权重副本", sourceModel.name + "_编辑副本", "prefab");
            if (path == null) return false;
            try
            {
                string validation = rig.Validate(); if (validation != "OK") throw new InvalidOperationException(validation);
                rig.RestoreBindPose(); var prefab = SquirrelWorkbenchExport.SaveModelCopy(model, path);
                savedRigRevision = rig.Revision; hasUnsavedChanges = false;
                EditorGUIUtility.PingObject(prefab); message = "模型副本已保存：" + path; error = ""; Evaluate(true);
                return true;
            }
            catch (Exception exception) { error = exception.Message; return false; }
        }

        public override void SaveChanges() { if (ExportModel()) base.SaveChanges(); }

        public void SetPreviewForCheck(float seconds, bool reference = false)
        {
            if (preview == null) CreatePreview();
            playing = false; tab = 0; time = seconds; original = reference; Evaluate(true); Repaint();
        }

        public void ResetAfterCheck() { hasUnsavedChanges = false; CreatePreview(); playing = true; tab = 0; time = 0; original = false; Repaint(); }

        public void ShowTab(int value) { tab = Mathf.Clamp(value, 0, 2); Evaluate(true); Repaint(); }

        public Texture2D CapturePreview(int width, int height)
        {
            if (preview == null) CreatePreview();
            preview.camera.orthographicSize = zoom;
            Vector3 center = new Vector3(0, 1, 0);
            preview.camera.transform.position = center + Quaternion.Euler(pitch, yaw, 0) * Vector3.back * 8;
            preview.camera.transform.LookAt(center);
            preview.BeginStaticPreview(new Rect(0, 0, width, height));
            preview.Render(true);
            return preview.EndStaticPreview();
        }
    }
}
