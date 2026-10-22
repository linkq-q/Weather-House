using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace WeatherHouseQuadrupedMotion.Editor
{
    public sealed class QuadrupedMotionPreview : EditorWindow
    {
        public const string Root = "Assets/Experiments/QuadrupedMotion";
        public static readonly string[] Species = { "sheep", "deer" };
        public static readonly string[] Actions = { "walk", "trot", "run", "turn", "sniff", "jump" };
        static readonly string[] SpeciesLabels = { "小羊", "小鹿" };
        static readonly string[] ActionLabels = { "行走", "小跑", "奔跑", "转身", "低头嗅闻", "跳跃" };
        [SerializeField] int species, action;
        [SerializeField] float time, speed = 1f, yaw = -35, pitch = 15, zoom = 1;
        [SerializeField] bool playing = true;
        QuadrupedPreviewScene scene;
        double lastTick;
        string error;

        [MenuItem("Weather House/小羊小鹿四足动作预览")]
        public static QuadrupedMotionPreview Open()
        {
            var window = GetWindow<QuadrupedMotionPreview>();
            window.titleContent = new GUIContent("小羊 · 小鹿动作");
            window.minSize = new Vector2(760, 520);
            window.Show();
            return window;
        }

        [MenuItem("Weather House/四足动作/准备模型与材质")]
        public static void PrepareAssets()
        {
            if (!AssetDatabase.IsValidFolder(Root + "/Materials")) AssetDatabase.CreateFolder(Root, "Materials");
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) throw new InvalidOperationException("项目缺少 URP/Lit 着色器。");
            foreach (string animal in Species)
            {
                string texturePath = Root + "/Textures/" + animal + ".png";
                AssetDatabase.ImportAsset(texturePath, ImportAssetOptions.ForceSynchronousImport);
                var textureImporter = AssetImporter.GetAtPath(texturePath) as TextureImporter;
                if (textureImporter != null && (textureImporter.maxTextureSize != 2048 || !textureImporter.mipmapEnabled))
                {
                    textureImporter.maxTextureSize = 2048; textureImporter.mipmapEnabled = true;
                    textureImporter.SaveAndReimport();
                }
                var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
                if (texture == null) throw new FileNotFoundException("缺少 " + animal + " 贴图。");
                string path = Root + "/Materials/" + animal + ".mat";
                var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (material == null)
                {
                    material = new Material(shader) { name = animal };
                    material.mainTexture = texture;
                    material.color = Color.white;
                    material.SetFloat("_Smoothness", .18f);
                    material.SetFloat("_Metallic", 0);
                    AssetDatabase.CreateAsset(material, path);
                }
                if (material.mainTexture != texture) { material.mainTexture = texture; EditorUtility.SetDirty(material); }
                AssetDatabase.SaveAssetIfDirty(material);
                foreach (string motion in Actions)
                {
                    string modelPath = ModelPath(animal, motion);
                    if (!File.Exists(modelPath)) continue;
                    AssetDatabase.ImportAsset(modelPath, ImportAssetOptions.ForceSynchronousImport);
                    var importer = AssetImporter.GetAtPath(modelPath) as ModelImporter;
                    if (importer == null) continue;
                    bool changed = importer.animationType != ModelImporterAnimationType.Generic || !importer.importAnimation
                        || importer.importCameras || importer.importLights || importer.animationCompression != ModelImporterAnimationCompression.Off;
                    if (!changed) continue;
                    importer.animationType = ModelImporterAnimationType.Generic;
                    importer.importAnimation = true;
                    importer.importCameras = false;
                    importer.importLights = false;
                    importer.animationCompression = ModelImporterAnimationCompression.Off;
                    importer.SaveAndReimport();
                }
            }
        }

        public static string ModelPath(string animal, string motion) => Root + "/Models/" + animal + "/" + motion + ".fbx";
        public static AnimationClip FindClip(string path) => AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>()
            .FirstOrDefault(clip => !clip.name.StartsWith("__preview__", StringComparison.Ordinal));

        void OnEnable()
        {
            EditorApplication.update += Tick;
            lastTick = EditorApplication.timeSinceStartup;
        }

        void OnDisable()
        {
            EditorApplication.update -= Tick;
            scene?.Dispose(); scene = null;
        }

        void Tick()
        {
            double now = EditorApplication.timeSinceStartup;
            if (now - lastTick < 1.0 / 60) return;
            float dt = Mathf.Clamp((float)(now - lastTick), 0, .1f);
            lastTick = now;
            if (scene == null || EditorApplication.isCompiling) return;
            if (playing) { time = Mathf.Repeat(time + dt * speed, scene.Clip.length); scene.Sample(time); }
            Repaint();
        }

        public void Select(string animal, string motion, float seconds = 0, bool play = true)
        {
            int animalIndex = Array.IndexOf(Species, animal), actionIndex = Array.IndexOf(Actions, motion);
            if (animalIndex < 0 || actionIndex < 0) throw new ArgumentException("未知动物或动作。");
            species = animalIndex; action = actionIndex; time = seconds; playing = play;
            Reload();
        }

        void Reload()
        {
            scene?.Dispose(); scene = null; error = null;
            try { scene = new QuadrupedPreviewScene(Species[species], Actions[action]); scene.Sample(time); }
            catch (Exception exception) { error = exception.Message; }
            Repaint();
        }

        void OnGUI()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                int next = GUILayout.Toolbar(species, SpeciesLabels, EditorStyles.toolbarButton, GUILayout.Width(150));
                if (next != species) { species = next; time = 0; Reload(); }
                GUILayout.Space(10);
                int motion = GUILayout.Toolbar(action, ActionLabels, EditorStyles.toolbarButton);
                if (motion != action) { action = motion; time = 0; Reload(); }
                if (GUILayout.Button("重新载入", EditorStyles.toolbarButton, GUILayout.Width(72))) Reload();
            }
            if (scene == null && error == null) Reload();
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                if (GUILayout.Button(playing ? "暂停" : "播放", EditorStyles.toolbarButton, GUILayout.Width(48))) playing = !playing;
                if (GUILayout.Button("从头播放", EditorStyles.toolbarButton, GUILayout.Width(72))) { time = 0; scene?.Sample(time); playing = true; }
                speed = EditorGUILayout.Slider(speed, .1f, 2f, GUILayout.Width(150));
                GUILayout.Label("倍速", GUILayout.Width(35));
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("正面", EditorStyles.toolbarButton)) { yaw = 0; pitch = 12; }
                if (GUILayout.Button("侧面", EditorStyles.toolbarButton)) { yaw = -90; pitch = 10; }
                if (GUILayout.Button("斜视", EditorStyles.toolbarButton)) { yaw = -35; pitch = 15; }
                if (GUILayout.Button("复位视角", EditorStyles.toolbarButton)) { yaw = -35; pitch = 15; zoom = 1; }
            }
            Rect area = GUILayoutUtility.GetRect(300, 300, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            var e = Event.current;
            if (area.Contains(e.mousePosition))
            {
                if (e.type == EventType.MouseDrag && e.button == 1) { yaw += e.delta.x * .5f; pitch = Mathf.Clamp(pitch - e.delta.y * .4f, -15, 75); e.Use(); }
                if (e.type == EventType.ScrollWheel) { zoom = Mathf.Clamp(zoom * Mathf.Exp(e.delta.y * .04f), .35f, 4); e.Use(); }
            }
            if (e.type == EventType.Repaint)
            {
                if (scene != null) scene.Draw(area, yaw, pitch, zoom);
                else EditorGUI.DrawRect(area, new Color(.08f, .11f, .14f));
                GUI.Label(new Rect(area.x + 16, area.y + 12, area.width - 32, 30), SpeciesLabels[species] + "  ·  " + ActionLabels[action], EditorStyles.whiteLargeLabel);
            }
            using (new EditorGUI.DisabledScope(scene == null))
            {
                EditorGUI.BeginChangeCheck();
                float next = EditorGUILayout.Slider("时间 / 秒", time, 0, scene?.Clip.length ?? 1);
                if (EditorGUI.EndChangeCheck()) { time = next; playing = false; scene?.Sample(time); }
            }
            EditorGUILayout.LabelField("UniMate 原始生成动作 · 右键旋转 · 滚轮缩放 · 无需进入 Play", EditorStyles.miniLabel);
            if (!string.IsNullOrEmpty(error)) EditorGUILayout.HelpBox(error, MessageType.Error);
        }

        public Texture2D CapturePreview(int width = 960, int height = 720)
        {
            if (scene == null) Reload();
            if (scene == null) throw new InvalidOperationException(error);
            return scene.Capture(width, height, yaw, pitch, zoom);
        }
    }

    internal sealed class QuadrupedPreviewScene : IDisposable
    {
        readonly PreviewRenderUtility preview;
        readonly GameObject displayRoot;
        readonly Material floorMaterial;
        readonly Transform[] transforms;
        readonly Vector3[] positions, scales;
        readonly Quaternion[] rotations;
        readonly Mesh scratch;
        readonly Bounds frameBounds;
        public GameObject Model { get; }
        public AnimationClip Clip { get; }
        public SkinnedMeshRenderer[] Skins { get; }
        public Material Material { get; }
        public Bounds FrameBounds => frameBounds;
        public Vector3 DisplayScale => displayRoot.transform.localScale;
        public Vector3 DisplayPosition => displayRoot.transform.position;
        public float FrontYaw { get; }
        public Bounds? SharedCameraBounds { get; set; }
        public float? SharedFrontYaw { get; set; }

        public QuadrupedPreviewScene(string animal, string motion, Vector3? displayScale = null, Vector3? displayPosition = null)
        {
            string path = QuadrupedMotionPreview.ModelPath(animal, motion);
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Clip = QuadrupedMotionPreview.FindClip(path);
            Material = AssetDatabase.LoadAssetAtPath<Material>(QuadrupedMotionPreview.Root + "/Materials/" + animal + ".mat");
            if (asset == null || Clip == null || Clip.length <= 0) throw new InvalidOperationException("尚未载入模型或动画：" + path);
            if (Material == null || Material.mainTexture == null) throw new InvalidOperationException("缺少动物材质；先执行 Weather House/四足动作/准备模型与材质。");
            preview = new PreviewRenderUtility();
            try
            {
                scratch = new Mesh();
                preview.camera.orthographic = true;
                preview.camera.nearClipPlane = .01f; preview.camera.farClipPlane = 200;
                preview.camera.clearFlags = CameraClearFlags.SolidColor;
                preview.camera.backgroundColor = new Color(.085f, .115f, .15f);
                preview.camera.allowHDR = false;
                preview.ambientColor = new Color(.55f, .57f, .6f);
                preview.lights[0].intensity = 1.2f; preview.lights[0].transform.rotation = Quaternion.Euler(45, -35, 0);
                preview.lights[1].intensity = .8f; preview.lights[1].transform.rotation = Quaternion.Euler(20, 135, 0);
                displayRoot = new GameObject("Quadruped preview only") { hideFlags = HideFlags.HideAndDontSave };
                preview.AddSingleGO(displayRoot);
                Model = Object.Instantiate(asset, displayRoot.transform);
                Model.name = asset.name;
                foreach (Behaviour behaviour in Model.GetComponentsInChildren<Behaviour>(true)) behaviour.enabled = false;
                Skins = Model.GetComponentsInChildren<SkinnedMeshRenderer>();
                if (Skins.Length == 0) throw new InvalidOperationException("模型没有蒙皮网格：" + path);
                foreach (var skin in Skins)
                {
                    skin.sharedMaterials = Enumerable.Repeat(Material, Mathf.Max(1, skin.sharedMesh.subMeshCount)).ToArray();
                    skin.updateWhenOffscreen = true;
                }
                transforms = Model.GetComponentsInChildren<Transform>();
                positions = transforms.Select(t => t.localPosition).ToArray();
                rotations = transforms.Select(t => t.localRotation).ToArray();
                scales = transforms.Select(t => t.localScale).ToArray();
                Sample(0);
                var head = transforms.FirstOrDefault(t => t.name == "Head");
                var hips = transforms.FirstOrDefault(t => t.name == "Hips");
                if (head != null && hips != null)
                {
                    Vector3 forward = Vector3.ProjectOnPlane(head.position - hips.position, Vector3.up);
                    if (forward.sqrMagnitude > .000001f) FrontYaw = Vector3.SignedAngle(Vector3.back, forward, Vector3.up);
                }
                Bounds initial = BoundsNow();
                if (initial.size.y < .0001f) throw new InvalidOperationException("模型高度无效。");
                displayRoot.transform.localScale = displayScale ?? Vector3.one * (2f / initial.size.y);
                initial = BoundsNow();
                displayRoot.transform.position = displayPosition ?? new Vector3(-initial.center.x, .005f - initial.min.y, -initial.center.z);
                Bounds union = BoundsNow();
                int frameSamples = Mathf.Max(24, Mathf.CeilToInt(Clip.length * 15));
                for (int i = 1; i <= frameSamples; i++) { Sample(Clip.length * i / frameSamples); union.Encapsulate(BoundsNow()); }
                frameBounds = union;
                Sample(0);
                var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
                floor.name = "Preview ground"; floor.hideFlags = HideFlags.HideAndDontSave;
                preview.AddSingleGO(floor);
                Object.DestroyImmediate(floor.GetComponent<Collider>());
                floor.transform.position = new Vector3(union.center.x, -.025f, union.center.z);
                floor.transform.localScale = new Vector3(Mathf.Max(6, union.size.x + 3), .05f, Mathf.Max(6, union.size.z + 3));
                floorMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit")) { hideFlags = HideFlags.HideAndDontSave, color = new Color(.2f, .28f, .32f) };
                floorMaterial.SetFloat("_Smoothness", .12f);
                floor.GetComponent<Renderer>().sharedMaterial = floorMaterial;
            }
            catch { Dispose(); throw; }
        }

        public void Sample(float time)
        {
            for (int i = 0; i < transforms.Length; i++) { transforms[i].localPosition = positions[i]; transforms[i].localRotation = rotations[i]; transforms[i].localScale = scales[i]; }
            Clip.SampleAnimation(Model, Mathf.Clamp(time, 0, Clip.length));
        }

        public Vector3[] VerticesNow()
        {
            return Skins.SelectMany(skin =>
            {
                // FBX renderer scale is included by the false path; true keeps these vertices local before TransformPoint.
                skin.BakeMesh(scratch, true);
                return scratch.vertices.Select(v => skin.transform.TransformPoint(v)).ToArray();
            }).ToArray();
        }

        public Bounds BoundsNow()
        {
            var points = VerticesNow();
            if (points.Length == 0) throw new InvalidOperationException("蒙皮网格没有顶点。");
            var bounds = new Bounds(points[0], Vector3.zero);
            foreach (var point in points) bounds.Encapsulate(point);
            return bounds;
        }

        void SetCamera(float yaw, float pitch, float zoom, float aspect)
        {
            Bounds bounds = SharedCameraBounds ?? frameBounds;
            Vector3 center = bounds.center;
            Quaternion view = Quaternion.Euler(pitch, yaw + (SharedFrontYaw ?? FrontYaw), 0);
            Vector3 right = view * Vector3.right, up = view * Vector3.up;
            Vector3 extents = bounds.extents;
            float halfHorizontal = Mathf.Abs(right.x) * extents.x + Mathf.Abs(right.y) * extents.y + Mathf.Abs(right.z) * extents.z;
            float halfVertical = Mathf.Abs(up.x) * extents.x + Mathf.Abs(up.y) * extents.y + Mathf.Abs(up.z) * extents.z;
            preview.camera.orthographicSize = Mathf.Max(halfVertical, halfHorizontal / Mathf.Max(.3f, aspect)) * 1.15f * zoom + .1f;
            preview.camera.transform.position = center + view * Vector3.back * 30;
            preview.camera.transform.LookAt(center);
        }

        public void Draw(Rect area, float yaw, float pitch, float zoom)
        {
            SetCamera(yaw, pitch, zoom, area.width / Mathf.Max(1, area.height));
            preview.BeginPreview(area, GUIStyle.none);
            try { preview.Render(true); }
            finally { GUI.DrawTexture(area, preview.EndPreview(), ScaleMode.StretchToFill, false); }
        }

        public Texture2D Capture(int width, int height, float yaw = -35, float pitch = 15, float zoom = 1)
        {
            SetCamera(yaw, pitch, zoom, (float)width / height);
            preview.BeginStaticPreview(new Rect(0, 0, width, height));
            preview.Render(true);
            return preview.EndStaticPreview();
        }

        public void Dispose()
        {
            preview?.Cleanup();
            if (displayRoot != null) Object.DestroyImmediate(displayRoot);
            if (floorMaterial != null) Object.DestroyImmediate(floorMaterial);
            if (scratch != null) Object.DestroyImmediate(scratch);
        }
    }
}
