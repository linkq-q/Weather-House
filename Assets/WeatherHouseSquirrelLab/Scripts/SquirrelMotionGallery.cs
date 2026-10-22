using System;
using UnityEngine;

namespace WeatherHouseSquirrelLab
{
    [DefaultExecutionOrder(200)]
    public sealed class SquirrelMotionGallery : MonoBehaviour
    {
        [Serializable]
        public sealed class Actor
        {
            public string id;
            public string title;
            public string sourceAsset;
            public Animator animator;
            public AnimationClip clip;
            public SquirrelTailWave tail;
        }

        public Actor[] actors = new Actor[0];
        public Camera galleryCamera;
        public Vector3 cameraTarget = new Vector3(0f, 1f, 0f);
        public float cameraYaw = 0f;
        public float cameraPitch = 27f;
        public float cameraDistance = 18f;
        public bool paused;
        public bool tailWave = true;
        [Range(0f, 65f)] public float tailAmplitude = 44f;
        public bool showPanel = true;

        private float previewTime;
        private Font font;
        private GUIStyle textStyle;
        private GUIStyle headingStyle;
        private readonly Rect panel = new Rect(14f, 14f, 300f, 250f);

        private void Start()
        {
            font = Font.CreateDynamicFontFromOSFont(new[] { "Microsoft YaHei", "Arial" }, 16);
            foreach (Actor actor in actors)
            {
                if (actor.animator != null)
                {
                    actor.animator.applyRootMotion = false;
                    actor.animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                    actor.animator.speed = paused ? 0f : 1f;
                }
                if (actor.tail == null) continue;
                actor.tail.Initialize(actor.animator != null ? actor.animator.transform : actor.tail.transform);
                // The gallery owns the clock so pausing stops both animation and the tail.
                actor.tail.enabled = false;
            }
            PlaceCamera();
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.Space)) SetPaused(!paused);
            if (Input.GetKeyDown(KeyCode.T)) tailWave = !tailWave;
            if (Input.GetKeyDown(KeyCode.Tab)) showPanel = !showPanel;
            if (!paused) previewTime += Time.deltaTime;

            bool overPanel = showPanel && panel.Contains(new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y));
            if (!overPanel && Input.GetMouseButton(1))
            {
                cameraYaw += Input.GetAxis("Mouse X") * 3f;
                cameraPitch = Mathf.Clamp(cameraPitch - Input.GetAxis("Mouse Y") * 2f, 8f, 75f);
            }
            if (!overPanel && galleryCamera != null)
                galleryCamera.orthographicSize = Mathf.Clamp(galleryCamera.orthographicSize - Input.mouseScrollDelta.y * 0.4f, 2.5f, 13f);
            PlaceCamera();
        }

        private void LateUpdate()
        {
            if (!tailWave) return;
            foreach (Actor actor in actors)
                if (actor.tail != null && actor.tail.IsInitialized)
                {
                    actor.tail.amplitudeDegrees = tailAmplitude;
                    actor.tail.EvaluateAt(previewTime);
                }
        }

        public void SetPaused(bool value)
        {
            paused = value;
            foreach (Actor actor in actors)
                if (actor.animator != null) actor.animator.speed = value ? 0f : 1f;
        }

        public void Restart()
        {
            previewTime = 0f;
            foreach (Actor actor in actors)
                if (actor.animator != null)
                {
                    actor.animator.Play("Preview", 0, 0f);
                    actor.animator.Update(0f);
                }
        }

        public void PlaceCamera()
        {
            if (galleryCamera == null) return;
            galleryCamera.transform.position = cameraTarget + Quaternion.Euler(cameraPitch, cameraYaw, 0f) * Vector3.back * cameraDistance;
            galleryCamera.transform.LookAt(cameraTarget);
        }

        private void OnGUI()
        {
            if (!showPanel) return;
            if (textStyle == null)
            {
                textStyle = new GUIStyle(GUI.skin.label) { font = font, fontSize = 15, wordWrap = true };
                headingStyle = new GUIStyle(textStyle) { fontSize = 20, fontStyle = FontStyle.Bold };
            }
            Font previousFont = GUI.skin.font;
            if (font != null) GUI.skin.font = font;
            GUI.Box(panel, GUIContent.none);
            GUILayout.BeginArea(new Rect(panel.x + 14f, panel.y + 10f, panel.width - 28f, panel.height - 20f));
            GUILayout.Label("松鼠 · UniMate 动作展示", headingStyle);
            GUILayout.Label("主体保留生成结果；尾巴叠加大幅 S 形摆动。短片结束直接回到开头。", textStyle);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(paused ? "继续 Space" : "暂停 Space", GUILayout.Height(28f))) SetPaused(!paused);
            if (GUILayout.Button("重新播放", GUILayout.Height(28f))) Restart();
            GUILayout.EndHorizontal();
            tailWave = GUILayout.Toggle(tailWave, "S 形尾巴 T（关闭查看原始尾巴）");
            GUILayout.Label("尾巴幅度  " + tailAmplitude.ToString("0") + "°", textStyle);
            tailAmplitude = GUILayout.HorizontalSlider(tailAmplitude, 0f, 65f);
            GUILayout.Label("右键拖动旋转视角 · 滚轮缩放\nTab 隐藏面板", textStyle);
            GUILayout.EndArea();
            GUI.skin.font = previousFont;
        }

        [Serializable]
        private sealed class ActorReport
        {
            public string id;
            public string sourceAsset;
            public string clipName;
            public float clipSeconds;
            public int skinnedRenderers;
            public int boneCount;
            public int texturedMaterials;
            public bool tailReady;
            public bool applyRootMotion;
        }

        [Serializable]
        private sealed class GalleryReport
        {
            public string scene;
            public bool playing;
            public bool paused;
            public bool tailWave;
            public float tailAmplitude;
            public ActorReport[] actors;
        }

        public string ExportReportJson()
        {
            var report = new GalleryReport
            {
                scene = gameObject.scene.path, playing = Application.isPlaying,
                paused = paused, tailWave = tailWave, tailAmplitude = tailAmplitude,
                actors = new ActorReport[actors.Length]
            };
            for (int i = 0; i < actors.Length; i++)
            {
                Actor actor = actors[i];
                var entry = new ActorReport
                {
                    id = actor.id, sourceAsset = actor.sourceAsset,
                    clipName = actor.clip != null ? actor.clip.name : "",
                    clipSeconds = actor.clip != null ? actor.clip.length : 0f,
                    tailReady = actor.tail != null && actor.tail.IsInitialized,
                    applyRootMotion = actor.animator != null && actor.animator.applyRootMotion
                };
                if (actor.animator != null)
                    foreach (SkinnedMeshRenderer renderer in actor.animator.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    {
                        entry.skinnedRenderers++;
                        entry.boneCount = Mathf.Max(entry.boneCount, renderer.bones.Length);
                        foreach (Material material in renderer.sharedMaterials)
                            if (material != null && material.mainTexture != null) entry.texturedMaterials++;
                    }
                report.actors[i] = entry;
            }
            return JsonUtility.ToJson(report, true);
        }
    }
}
