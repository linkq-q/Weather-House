using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;
using WeatherHouse.Hillside;
using WeatherHouse.WatersideV2;
using WeatherHousePrototype;

namespace WeatherHouse.Life
{
    // The onboarding coordinator owns the first-save decision, HUD suppression and input lock.
    [DisallowMultipleComponent, DefaultExecutionOrder(250)]
    public sealed class LifeOpeningCinematic : MonoBehaviour
    {
        public const float Duration = 44f;
        const float ReturnToPlayAt = 40f;
        public bool IsPlaying { get; private set; }
        public bool ReducedMotion { get; set; }
        public float PlaybackTime => elapsed;

        GameplaySession session;
        Camera source, view;
        bool sourceEnabled, capturedSource, returnedToPlay, heldPose;
        Action completed;
        float elapsed;
        Pose startPose, reducedPose;
        float startFieldOfView;
        Pose[] route;
        GameObject presentation;
        RectTransform canvasRect, frame, bubble;
        RectTransform[] mattes;
        Text subtitle, motionLabel;
        Image fade;
        Button motionButton;
        readonly Dictionary<HillsideGrassRenderer, Camera> grassCameras = new Dictionary<HillsideGrassRenderer, Camera>();
        static readonly float[] Times = { 0f, 7f, 14f, 22f, 30f, 35f, ReturnToPlayAt };
        static readonly string[] Captions =
        {
            "雾汐岛上有座天气屋，我们家世代学习天气魔法。",
            "我们和岛上的动物，已经一起生活了几百年。",
            "我们照顾晴雨风雪，动物们也用各自的本领帮助我们。",
            "今天，家人们去参加魔法会议，暂时离开了小岛。",
            "临走前，他们把天气屋和岛上的生活交给了我。",
            "可我才刚开始学习……"
        };

        public bool Begin(GameplaySession gameplay, Action onCompleted)
        {
            if (!Application.isPlaying || IsPlaying || !isActiveAndEnabled || !gameplay || !gameplay.PlayerTransform ||
                !gameplay.PlayerViewCamera || !gameplay.PlayerViewCamera.gameObject.activeInHierarchy ||
                (gameplay.PhotoRig && gameplay.PhotoRig.Active)) return false;
            session = gameplay;
            source = gameplay.PlayerViewCamera;
            try
            {
                route = BuildRoute();
                Font font = Resources.Load<Font>("Fonts/ZCOOLXiaoWei-Regular");
                if (!font) throw new InvalidOperationException("Opening needs the existing ZCOOLXiaoWei UI font.");
                sourceEnabled = source.enabled;
                capturedSource = true;
                startPose = new Pose(source.transform.position, source.transform.rotation);
                startFieldOfView = source.fieldOfView;
                route[0] = route[route.Length - 1] = startPose;
                elapsed = 0f;
                returnedToPlay = heldPose = false;
                completed = onCompleted;
                CreateCamera();
                CreatePresentation(font);
                foreach (var grass in FindObjectsOfType<HillsideGrassRenderer>(true))
                    if (grass.gameObject.scene == session.gameObject.scene)
                    { grassCameras.Add(grass, grass.viewCamera); grass.viewCamera = view; }
                source.enabled = false;
                IsPlaying = true;
                Present();
                return true;
            }
            catch (Exception error)
            {
                Debug.LogException(error, this);
                Cancel();
                return false;
            }
        }

        Pose[] BuildRoute()
        {
            Transform house = null;
            foreach (Transform candidate in FindObjectsOfType<Transform>())
                if (candidate.gameObject.scene == session.gameObject.scene &&
                    (candidate.name == "天气屋 · HIGHLANDS 原模型" || candidate.name == "天气屋 · v05 苔顶木构"))
                { house = candidate; break; }
            if (!house) throw new InvalidOperationException("Opening could not find the existing Weather House in this scene.");
            Vector3 housePoint = house.position;
            Renderer[] renderers = house.GetComponentsInChildren<Renderer>();
            if (renderers.Length > 0)
            {
                Bounds bounds = renderers[0].bounds;
                for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
                housePoint = bounds.center;
            }
            // These are the authored, already installed rural-region polygons; nothing is repositioned.
            Vector3 island = RegionCenter(3), forest = Vector3.Lerp(RegionCenter(0), housePoint, .28f);
            float yaw = source.transform.eulerAngles.y;
            return new[] { default(Pose), Overhead(housePoint, 40f, yaw), Overhead(island, 76f, yaw),
                Overhead(forest, 56f, yaw), Overhead(housePoint, 56f, yaw),
                Overhead(session.PlayerTransform.position + Vector3.up, 36f, yaw), default(Pose) };
        }

        Vector3 RegionCenter(int index)
        {
            Vector3 point = Vector3.zero;
            Vector3[] polygon = RuralMapConfiguration.Regions[index];
            foreach (Vector3 vertex in polygon) point += vertex;
            point /= polygon.Length;
            foreach (Terrain terrain in Terrain.activeTerrains)
            {
                Vector3 local = point - terrain.transform.position;
                if (terrain.gameObject.scene != session.gameObject.scene || local.x < 0 || local.z < 0 ||
                    local.x > terrain.terrainData.size.x || local.z > terrain.terrainData.size.z) continue;
                point.y = terrain.SampleHeight(point) + terrain.transform.position.y;
                break;
            }
            return point + Vector3.up * 2f;
        }

        static Pose Overhead(Vector3 target, float distance, float yaw)
        {
            Quaternion rotation = Quaternion.Euler(59f, yaw, 0f);
            return new Pose(target + rotation * Vector3.back * distance, rotation);
        }

        void CreateCamera()
        {
            var cameraObject = new GameObject("Opening camera (runtime)") { hideFlags = HideFlags.DontSave };
            view = cameraObject.AddComponent<Camera>();
            view.CopyFrom(source);
            view.enabled = false;
            view.targetTexture = null;
            view.orthographic = false;
            view.usePhysicalProperties = false;
            view.farClipPlane = Mathf.Max(source.farClipPlane, 400f);
            view.transform.SetPositionAndRotation(startPose.position, startPose.rotation);
            var data = cameraObject.AddComponent<UniversalAdditionalCameraData>();
            var original = source.GetComponent<UniversalAdditionalCameraData>();
            if (original)
            {
                data.renderPostProcessing = original.renderPostProcessing;
                data.renderShadows = original.renderShadows;
                data.requiresColorTexture = original.requiresColorTexture;
                data.requiresDepthTexture = original.requiresDepthTexture;
                data.volumeLayerMask = original.volumeLayerMask;
                data.volumeTrigger = original.volumeTrigger;
                data.antialiasing = original.antialiasing;
                data.antialiasingQuality = original.antialiasingQuality;
                data.stopNaN = original.stopNaN;
                data.dithering = original.dithering;
            }
            data.renderType = CameraRenderType.Base;
            view.enabled = true;
        }

        void CreatePresentation(Font font)
        {
            presentation = new GameObject("Opening presentation (runtime)", typeof(RectTransform), typeof(Canvas),
                typeof(CanvasScaler), typeof(GraphicRaycaster)) { hideFlags = HideFlags.DontSave, layer = 5 };
            Canvas canvas = presentation.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 300;
            canvasRect = (RectTransform)presentation.transform;
            CanvasScaler scaler = presentation.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1600f, 900f);
            scaler.matchWidthOrHeight = .5f;
            Image top = ImageAt(canvasRect, "16:9 top matte", Color.black);
            Image bottom = ImageAt(canvasRect, "16:9 bottom matte", Color.black);
            Image left = ImageAt(canvasRect, "16:9 left matte", Color.black);
            Image right = ImageAt(canvasRect, "16:9 right matte", Color.black);
            mattes = new[] { top.rectTransform, bottom.rectTransform, left.rectTransform, right.rectTransform };
            frame = RectAt(canvasRect, "16:9 content");
            frame.offsetMin = frame.offsetMax = Vector2.zero;
            subtitle = Label(frame, "Story subtitles", font, 31, Color.white);
            SetRect(subtitle.rectTransform, new Vector2(.06f, .055f), new Vector2(.94f, .15f));
            subtitle.horizontalOverflow = HorizontalWrapMode.Overflow;
            AddSubtitleShadow(subtitle);
            Button skip = CaptionButton(frame, "跳过片头", font, new Vector2(1f, 1f), new Vector2(-110f, -45f));
            skip.onClick.AddListener(Skip);
            motionButton = CaptionButton(frame, "减弱运镜", font, new Vector2(0f, 1f), new Vector2(120f, -45f));
            motionLabel = motionButton.GetComponentInChildren<Text>();
            motionButton.onClick.AddListener(() => ReducedMotion = true);
            bubble = ProceduralUiLibrary.WhitePanel(frame, "Witch thought (no tail)", Vector2.zero, new Vector2(540f, 96f)).rectTransform;
            Label(bubble, "Witch thought", font, 31, ProceduralUiLibrary.Ink).text = "可是我还什么都不会啊！";
            bubble.gameObject.SetActive(false);
            fade = ImageAt(frame, "Reduced motion transition", Color.clear);
            fade.transform.SetSiblingIndex(0);
            SetMattes();
        }

        void SetMattes()
        {
            Rect rect = Viewport(Screen.width, Screen.height);
            SetRect(mattes[0], new Vector2(0f, rect.yMax), Vector2.one);
            SetRect(mattes[1], Vector2.zero, new Vector2(1f, rect.yMin));
            SetRect(mattes[2], new Vector2(0f, rect.yMin), new Vector2(rect.xMin, rect.yMax));
            SetRect(mattes[3], new Vector2(rect.xMax, rect.yMin), new Vector2(1f, rect.yMax));
        }

        public static Rect Viewport(int width, int height)
        {
            float aspect = Mathf.Max(1, width) / (float)Mathf.Max(1, height), target = 16f / 9f;
            return aspect > target ? new Rect((1f - target / aspect) * .5f, 0f, target / aspect, 1f) :
                new Rect(0f, (1f - aspect / target) * .5f, 1f, aspect / target);
        }

        void Update()
        {
            if (!IsPlaying) return;
            if (Input.GetKeyDown(KeyCode.Escape)) { Skip(); return; }
            try { Step(Time.unscaledDeltaTime); }
            catch (Exception error) { Debug.LogException(error, this); Cancel(); }
        }

        void Step(float deltaTime)
        {
            if (!IsPlaying) return;
            if (!source || !session || !session.PlayerTransform || (!returnedToPlay && !view)) { Cancel(); return; }
            elapsed = Mathf.Min(Duration, elapsed + Mathf.Max(0f, deltaTime));
            Present();
            if (elapsed >= Duration) Finish();
        }

        void Present()
        {
            if (elapsed >= ReturnToPlayAt)
            {
                ReturnToPlay();
                subtitle.transform.parent.gameObject.SetActive(false);
                bubble.gameObject.SetActive(true);
                RectTransformUtility.ScreenPointToLocalPointInRectangle(frame,
                    source.WorldToScreenPoint(session.PlayerTransform.position + Vector3.up * 1.6f), null, out Vector2 local);
                local.y += bubble.sizeDelta.y * .5f + 25f;
                local.x = Mathf.Clamp(local.x, frame.rect.xMin + 290f, frame.rect.xMax - 290f);
                local.y = Mathf.Clamp(local.y, frame.rect.yMin + 70f, frame.rect.yMax - 105f);
                bubble.anchoredPosition = local;
                fade.color = new Color(0f, 0f, 0f, heldPose ? Mathf.InverseLerp(ReturnToPlayAt + .45f, ReturnToPlayAt, elapsed) : 0f);
                return;
            }
            Rect viewport = Viewport(Screen.width, Screen.height);
            view.rect = viewport;
            SetRect(frame, viewport.min, viewport.max);
            SetMattes();
            int segment = 0;
            while (segment < Times.Length - 2 && elapsed >= Times[segment + 1]) segment++;
            subtitle.text = Captions[segment];
            float t = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(Times[segment], Times[segment + 1], elapsed));
            Pose pose = new Pose(Vector3.Lerp(route[segment].position, route[segment + 1].position, t),
                Quaternion.Slerp(route[segment].rotation, route[segment + 1].rotation, t));
            if (ReducedMotion)
            {
                // Holding the current composition removes all travel; narration and the final return still run.
                if (!heldPose) { reducedPose = new Pose(view.transform.position, view.transform.rotation); heldPose = true; }
                pose = reducedPose;
                motionLabel.text = "已减弱运镜";
                motionButton.interactable = false;
                fade.color = new Color(0f, 0f, 0f, Mathf.InverseLerp(ReturnToPlayAt - .45f, ReturnToPlayAt, elapsed));
            }
            view.transform.SetPositionAndRotation(pose.position, pose.rotation);
            float fov = segment == 0 ? Mathf.Lerp(startFieldOfView, 48f, t) :
                segment == Times.Length - 2 ? Mathf.Lerp(48f, startFieldOfView, t) : 48f;
            if (!heldPose) view.fieldOfView = fov;
        }

        void ReturnToPlay()
        {
            if (returnedToPlay) return;
            returnedToPlay = true;
            if (capturedSource && source) source.enabled = sourceEnabled;
            if (view) view.enabled = false;
            foreach (var item in grassCameras) if (item.Key) item.Key.viewCamera = item.Value;
            grassCameras.Clear();
            SetRect(frame, Vector2.zero, Vector2.one);
            foreach (Transform child in canvasRect)
                if (child != frame) child.gameObject.SetActive(false);
        }

        public void Skip() { if (IsPlaying) Finish(); }
        public void Cancel() { completed = null; Cleanup(); }
        void Finish()
        {
            Action callback = completed;
            completed = null;
            Cleanup();
            callback?.Invoke();
        }

        void Cleanup()
        {
            IsPlaying = false;
            if (capturedSource && source) source.enabled = sourceEnabled;
            capturedSource = false;
            foreach (var item in grassCameras) if (item.Key) item.Key.viewCamera = item.Value;
            grassCameras.Clear();
            if (view) { view.enabled = false; view.gameObject.SetActive(false); Destroy(view.gameObject); }
            if (presentation) { presentation.SetActive(false); Destroy(presentation); }
            view = null; source = null; session = null; presentation = null;
        }

        void OnDisable() => Cancel();
        void OnDestroy() => Cancel();

        static RectTransform RectAt(Transform parent, string name)
        {
            var child = new GameObject(name, typeof(RectTransform)) { layer = 5 };
            child.transform.SetParent(parent, false);
            var rect = (RectTransform)child.transform;
            SetRect(rect, Vector2.zero, Vector2.one);
            return rect;
        }
        static void SetRect(RectTransform rect, Vector2 min, Vector2 max)
        { rect.anchorMin = min; rect.anchorMax = max; rect.offsetMin = rect.offsetMax = Vector2.zero; }
        static Image ImageAt(Transform parent, string name, Color color)
        { var image = RectAt(parent, name).gameObject.AddComponent<Image>(); image.color = color; image.raycastTarget = false; return image; }
        static Text Label(Transform parent, string name, Font font, int size, Color color)
        {
            var text = RectAt(parent, name).gameObject.AddComponent<Text>();
            text.font = font; text.fontSize = size; text.color = color; text.alignment = TextAnchor.MiddleCenter;
            text.raycastTarget = false; text.horizontalOverflow = HorizontalWrapMode.Wrap; text.verticalOverflow = VerticalWrapMode.Truncate;
            return text;
        }
        static void AddSubtitleShadow(Text text)
        {
            var shadow = text.gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(0, 0, 0, .65f); shadow.effectDistance = new Vector2(1, -1);
        }

        static Button CaptionButton(Transform parent, string caption, Font font, Vector2 anchor, Vector2 position)
        {
            var panel = ImageAt(parent, caption, Color.clear);
            panel.rectTransform.anchorMin = panel.rectTransform.anchorMax = anchor;
            panel.rectTransform.anchoredPosition = position; panel.rectTransform.sizeDelta = new Vector2(190f, 54f);
            panel.raycastTarget = true;
            var button = panel.gameObject.AddComponent<Button>();
            var label = Label(panel.transform, caption, font, 24, Color.white); label.text = caption;
            button.targetGraphic = label; AddSubtitleShadow(label);
            return button;
        }
    }
}
