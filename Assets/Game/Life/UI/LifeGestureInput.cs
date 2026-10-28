using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using WeatherHousePrototype;
using WeatherHouse.WatersideV2;

namespace WeatherHouse.Life
{
    [DisallowMultipleComponent, DefaultExecutionOrder(220)]
    public sealed class LifeGestureInput : MonoBehaviour
    {
        private LifeSession life;
        private GameplayHud hud;
        private Canvas canvas;
        private bool onboardingCanvasHidden, onboardingCanvasWasEnabled;
        private RectTransform root, drawRoot, taskRoot, hintRoot, taskToggleRoot;
        private Text taskPageText, interactionText, helpText, drawHint;
        private Button previousTaskPage, nextTaskPage;
        private readonly Text[] taskLines = new Text[4];
        private readonly LifeTaskMarkGraphic[] taskMarks = new LifeTaskMarkGraphic[2];
        private string displayedTasks;
        private WatersideMood taskMood;
        private RawImage taskDay, taskNight, taskPaper, taskBookmark, taskMeadow;
        private float taskFooterY;
        private const float TaskRowStep = 48f + 22f * TaskGapScale;
        private const float TaskGapScale = .85f;
        private LifeStrokeGraphic stroke;
        private readonly List<Vector2> points = new List<Vector2>();
        private bool drawing, worldMode, taskExpanded;
        private int tipLevel;
        private float feedbackUntil;
        public bool IsWorldDrawing => worldMode;
        public bool IsStrokeActive => drawing || (life && life.GestureInputActive && !worldMode);

        private void Start()
        {
            life = GetComponent<LifeSession>(); hud = GetComponent<GameplayHud>();
            if (!life || !hud) { enabled = false; return; }
            var go = new GameObject("Life gameplay UI", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            go.layer = 5; go.transform.SetParent(transform, false); canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 110; root = (RectTransform)go.transform;
            var scaler = go.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution = new Vector2(1600, 1000); scaler.matchWidthOrHeight = .5f;
            Font font = Resources.Load<Font>("Fonts/ZCOOLXiaoWei-Regular");
            taskToggleRoot = LifeUi.Rect(root, "Help tasks toggle", Vector2.zero, new Vector2(68f, 68f));
            taskToggleRoot.anchorMin = taskToggleRoot.anchorMax = Vector2.one;
            taskToggleRoot.anchoredPosition = new Vector2(-62f, -64f);
            var taskHit = taskToggleRoot.gameObject.AddComponent<Image>(); taskHit.color = Color.clear;
            var taskToggle = taskToggleRoot.gameObject.AddComponent<Button>(); taskToggle.targetGraphic = taskHit;
            taskToggle.onClick.AddListener(() => { taskExpanded = !taskExpanded; LifeAudio.Play(LifeAudioCue.Ui); });
            var taskIcon = LifeUi.Rect(taskToggleRoot, "Help task icon", Vector2.zero, new Vector2(38f, 48f)).gameObject.AddComponent<RawImage>();
            taskIcon.texture = Resources.Load<Texture2D>("HudIcons/Journal"); taskIcon.uvRect = new Rect(.18f, .08f, .63f, .82f);
            taskIcon.color = Color.white; taskIcon.raycastTarget = false;
            taskRoot = LifeUi.Rect(root, "Help tasks", Vector2.zero, new Vector2(430f, 256f));
            taskRoot.anchorMin = taskRoot.anchorMax = Vector2.one; taskRoot.pivot = Vector2.one; taskRoot.anchoredPosition = new Vector2(-28f, -108f);
            taskPaper = TaskLayer("Paper", new Vector2(0f, -18f), new Vector2(430f, 220f), new Rect(0.054786621f, 0.063947078f, 0.925605536f, 0.863285557f), false);
            taskBookmark = TaskLayer("Bookmark", new Vector2(-182.5f, -18f), new Vector2(65f, 200f), new Rect(0.278814490f, 0.085118703f, 0.443468716f, 0.833236827f), false);
            taskMeadow = TaskLayer("Meadow", new Vector2(139f, -69f), new Vector2(145f, 45f), new Rect(0.352288488f, 0.199449794f, 0.574664817f, 0.546079780f), false);
            taskDay = TaskLayer("Day", new Vector2(140f, 90.5f), new Vector2(150f, 75f), new Rect(0.111179741f, 0.130792997f, 0.821494750f, 0.683831102f), true);
            taskNight = TaskLayer("Night", new Vector2(140f, 90.5f), new Vector2(150f, 75f), new Rect(0.249536751f, 0.177136972f, 0.531192094f, 0.646755922f), true);
            taskMood = FindObjectOfType<WatersideMood>();
            UpdateTaskTimeIcon();
            float firstTop = 48f + 24f * TaskGapScale;
            float lineStep = 24f + 4f * TaskGapScale;
            float rowStep = TaskRowStep;
            for (int row = 0; row < 2; row++)
            {
                float top = firstTop + row * rowStep;
                taskMarks[row] = LifeUi.Rect(taskRoot, "Task marker " + row, new Vector2(89f, -top - 12f), new Vector2(24f, 24f)).gameObject.AddComponent<LifeTaskMarkGraphic>();
                taskMarks[row].rectTransform.anchorMin = taskMarks[row].rectTransform.anchorMax = new Vector2(0f, 1f);
                taskMarks[row].color = new Color32(192, 159, 74, 255); taskMarks[row].raycastTarget = false;
                for (int line = 0; line < 2; line++)
                {
                    var label = taskLines[row * 2 + line] = LifeUi.Text(taskRoot, "Task " + row + " line " + line, "", new Vector2(116f, -top - line * lineStep), new Vector2(286f, 24f), font, 21, ProceduralUiLibrary.Ink, TextAnchor.MiddleLeft);
                    label.rectTransform.anchorMin = label.rectTransform.anchorMax = new Vector2(0f, 1f);
                    label.rectTransform.pivot = new Vector2(0f, 1f); label.horizontalOverflow = HorizontalWrapMode.Overflow;
                }
            }
            float footerY = taskFooterY = -(firstTop + rowStep + lineStep + 24f + 12f * TaskGapScale + 18f);
            previousTaskPage = TaskArrow("Previous task page", new Vector2(-50f, footerY), false);
            nextTaskPage = TaskArrow("Next task page", new Vector2(90f, footerY), true);
            previousTaskPage.onClick.AddListener(() => { life.ChangeTaskPage(-1); LifeAudio.Play(LifeAudioCue.Ui); });
            nextTaskPage.onClick.AddListener(() => { life.ChangeTaskPage(1); LifeAudio.Play(LifeAudioCue.Ui); });
            taskPageText = LifeUi.Text(taskRoot, "Task page", "", new Vector2(20f, footerY), new Vector2(78f, 24f), font, 18, ProceduralUiLibrary.Ink, TextAnchor.MiddleCenter);
            taskPageText.rectTransform.anchorMin = taskPageText.rectTransform.anchorMax = new Vector2(.5f, 1f);
            taskToggleRoot.gameObject.SetActive(false); taskRoot.gameObject.SetActive(false);
            hintRoot = LifeUi.Rect(root, "Object interaction", Vector2.zero, new Vector2(1100, 60)); hintRoot.anchorMin = hintRoot.anchorMax = new Vector2(.5f, 0); hintRoot.anchoredPosition = new Vector2(0, 80);
            interactionText = LifeUi.Text(hintRoot, "Nearby objects", "", Vector2.zero, hintRoot.sizeDelta, font, 22, Color.white, TextAnchor.MiddleCenter);
            helpText = LifeUi.Text(root, "Optional guidance", "", Vector2.zero, new Vector2(660, 70), font, 19, new Color(.91f, .93f, .82f));
            helpText.rectTransform.anchorMin = helpText.rectTransform.anchorMax = new Vector2(.5f, 0); helpText.rectTransform.anchoredPosition = new Vector2(100, 32);
            drawRoot = LifeUi.Rect(root, "Weather gesture mode", Vector2.zero, new Vector2(1600, 1000));
            drawRoot.anchorMin = Vector2.zero; drawRoot.anchorMax = Vector2.one; drawRoot.offsetMin = drawRoot.offsetMax = Vector2.zero;
            stroke = LifeUi.Rect(drawRoot, "Weather ink", Vector2.zero, new Vector2(1600, 1000)).gameObject.AddComponent<LifeStrokeGraphic>();
            stroke.rectTransform.anchorMin = Vector2.zero; stroke.rectTransform.anchorMax = Vector2.one; stroke.rectTransform.offsetMin = stroke.rectTransform.offsetMax = Vector2.zero;
            drawHint = LifeUi.Text(drawRoot, "Casting feedback", "", new Vector2(0, 0), new Vector2(1000, 100), font, 29, Color.white, TextAnchor.MiddleCenter);
            drawHint.rectTransform.anchorMin = drawHint.rectTransform.anchorMax = new Vector2(.5f, 1); drawHint.rectTransform.anchoredPosition = new Vector2(-80, -110);
            drawRoot.gameObject.SetActive(false);
        }

        private bool Modal => !hud || !hud.isActiveAndEnabled || hud.JournalOpen || hud.IsDialogueOpen || hud.CameraMode || FocusedInput() || !string.IsNullOrEmpty(Input.compositionString);
        private void Update()
        {
            if (!life || !canvas) return;
            bool opening = LifeOnboarding.Blocks(life.Gameplay);
            if (opening)
            { if (!onboardingCanvasHidden) { onboardingCanvasWasEnabled = canvas.enabled; onboardingCanvasHidden = true; } canvas.enabled = false; return; }
            if (onboardingCanvasHidden) { canvas.enabled = onboardingCanvasWasEnabled; onboardingCanvasHidden = false; }
            bool modal = Modal;
            UpdateTaskTimeIcon();
            if (worldMode && (modal || Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.J) || Input.GetKeyDown(KeyCode.C))) Cancel();
            bool activeTasks = life.Progress.tasks.Exists(task => task != null && task.status == LifeTaskStatus.Active);
            if (!activeTasks) taskExpanded = false;
            bool showTasks = !modal && !worldMode && activeTasks;
            taskToggleRoot.gameObject.SetActive(showTasks);
            taskRoot.gameObject.SetActive(showTasks && taskExpanded);
            hintRoot.gameObject.SetActive(!modal && !worldMode); helpText.gameObject.SetActive(!modal && !worldMode);
            string tasks = life.TaskText;
            if (tasks != displayedTasks)
            {
                displayedTasks = tasks;
                string[] lines = tasks.Split('\n');
                for (int i = 0; i < taskLines.Length; i++) taskLines[i].text = FitTaskLine(taskLines[i], i < lines.Length ? lines[i] : "");
                for (int i = 0; i < taskMarks.Length; i++) taskMarks[i].gameObject.SetActive(i * 2 < lines.Length && !string.IsNullOrEmpty(lines[i * 2]));
                FitTaskCard(taskMarks[1].gameObject.activeSelf ? 2 : 1);
            }
            bool multiplePages = life.TaskPageCount > 1;
            taskPageText.text = multiplePages ? (life.TaskPage + 1) + " / " + life.TaskPageCount : "";
            taskPageText.gameObject.SetActive(multiplePages);
            previousTaskPage.gameObject.SetActive(multiplePages); nextTaskPage.gameObject.SetActive(multiplePages);
            previousTaskPage.interactable = nextTaskPage.interactable = !worldMode;
            string gather=modal?"":hud.GatheringHint;
            interactionText.text = string.IsNullOrEmpty(gather)?life.InteractionHint:gather+(string.IsNullOrEmpty(life.InteractionHint)?"":"  ·  "+life.InteractionHint);
            if (!modal && Input.GetKeyDown(KeyCode.H)) tipLevel = (tipLevel + 1) % 2;
            helpText.text = tipLevel > 0 ? Guidance() : "G 手势  ·  H 提示";
            if (modal) return;
            if (Input.GetKeyDown(KeyCode.G))
            {
                if (worldMode) Cancel();
                else { worldMode = true; life.SetGestureInput(true); drawRoot.gameObject.SetActive(true); drawHint.text = "按住左键描画  ·  Esc 收起"; stroke.SetPath(null); }
            }
            if (!worldMode) return;
            if (Input.GetMouseButtonDown(0) && !OverUi())
            {
                drawing = true; points.Clear(); feedbackUntil = 0;
                stroke.color = new Color(.61f, .87f, .94f); AddPoint();
            }
            if (drawing && Input.GetMouseButton(0)) AddPoint();
            if (drawing && Input.GetMouseButtonUp(0)) { AddPoint(); drawing = false; SubmitStroke(); }
            if (!drawing && feedbackUntil > 0 && Time.unscaledTime >= feedbackUntil) Cancel();
        }

        private void FitTaskCard(int rows)
        {
            float removed = (2 - rows) * TaskRowStep;
            taskRoot.sizeDelta = new Vector2(430f, 256f - removed);
            taskPaper.rectTransform.sizeDelta = new Vector2(430f, 220f - removed);
            taskPaper.rectTransform.anchoredPosition = new Vector2(0f, -146f + removed * .5f);
            taskBookmark.rectTransform.sizeDelta = new Vector2(65f, 200f - removed);
            taskBookmark.rectTransform.anchoredPosition = new Vector2(-182.5f, -146f + removed * .5f);
            taskMeadow.rectTransform.anchoredPosition = new Vector2(139f, -197f + removed);
            foreach (var footer in new[] { (RectTransform)previousTaskPage.transform, (RectTransform)nextTaskPage.transform, taskPageText.rectTransform })
                footer.anchoredPosition = new Vector2(footer.anchoredPosition.x, taskFooterY + removed);
        }

        private RawImage TaskLayer(string name, Vector2 position, Vector2 size, Rect uv, bool preserveAspect)
        {
            var image = LifeUi.Rect(taskRoot, "Weather task " + name, position, size).gameObject.AddComponent<RawImage>();
            image.rectTransform.anchorMin = image.rectTransform.anchorMax = new Vector2(.5f, 1f);
            image.rectTransform.anchoredPosition = position - new Vector2(0f, 128f);
            image.texture = Resources.Load<Texture2D>("HudIcons/WeatherTask/" + name);
            image.uvRect = uv; image.raycastTarget = false;
            if (preserveAspect && image.texture)
            {
                Vector2 content = new Vector2(image.texture.width * uv.width, image.texture.height * uv.height);
                image.rectTransform.sizeDelta = content * Mathf.Min(size.x / content.x, size.y / content.y);
            }
            return image;
        }

        private void UpdateTaskTimeIcon()
        {
            // The scene currently exposes an F8 mood, not an advancing game clock.
            bool night = taskMood && taskMood.Current == WatersideMood.Mode.Night;
            taskDay.gameObject.SetActive(!night); taskNight.gameObject.SetActive(night);
        }

        private Button TaskArrow(string name, Vector2 position, bool right)
        {
            var hit = LifeUi.Panel(taskRoot, name, position, new Vector2(36f, 36f), Color.clear, true);
            hit.rectTransform.anchorMin = hit.rectTransform.anchorMax = new Vector2(.5f, 1f);
            var button = hit.gameObject.AddComponent<Button>(); button.targetGraphic = hit;
            button.transition = Selectable.Transition.None;
            var leaf = LifeUi.Rect(hit.transform, "Leaf arrow", Vector2.zero, new Vector2(18f, 18f)).gameObject.AddComponent<LifeTaskMarkGraphic>();
            leaf.arrow = true; leaf.right = right; leaf.color = new Color32(119, 134, 83, 255); leaf.raycastTarget = false;
            return button;
        }

        private static string FitTaskLine(Text label, string value)
        {
            var settings = label.GetGenerationSettings(Vector2.zero);
            float width = label.rectTransform.sizeDelta.x * label.pixelsPerUnit;
            if (label.cachedTextGeneratorForLayout.GetPreferredWidth(value, settings) <= width) return value;
            while (value.Length > 0 && label.cachedTextGeneratorForLayout.GetPreferredWidth(value + "…", settings) > width)
                value = value.Substring(0, value.Length - (value.Length > 1 && char.IsLowSurrogate(value[value.Length - 1]) ? 2 : 1));
            return value + "…";
        }

        private string Guidance() => life.ProgressHint + "  H 收起提示";

        private void AddPoint()
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(stroke.rectTransform, Input.mousePosition, null, out Vector2 point);
            if (points.Count < 512 && (points.Count == 0 || Vector2.Distance(points[points.Count - 1], point) >= 3f))
            { if (points.Count > 0) LifeAudio.StrokeMoved(); points.Add(point); }
            stroke.SetPath(points);
        }

        private void SubmitStroke()
        {
            LifeAudio.StopStroke();
            bool success = false; string message;
            if (!ValidStroke(points) || !GestureCatalog.TryRecognize(points, out string id, out float score)) message = "跟着手账里的笔尖，再画一次。";
            else if (!life.IsGestureUnlocked(id)) message = "";
            else success = life.TryCast(id, out message);
            stroke.color = success ? new Color(.66f, .95f, .73f) : new Color(1f, .54f, .36f);
            drawHint.text = message + "\n可以再次描画，Esc 收起";
            feedbackUntil = Time.unscaledTime + 2.2f;
        }

        public bool CanPractice(string id) => life && life.IsGestureUnlocked(id) && hud && hud.JournalOpen && !hud.IsAlbumOpening;
        public void SetPracticeDrawing(bool active) { if (life && !worldMode) life.SetGestureInput(active); }
        public void CompletePractice(string expected, IList<Vector2> path, Text feedback, LifeStrokeGraphic ink)
        {
            string id = null; float score = 0;
            bool recognized = ValidStroke(path) && GestureCatalog.TryRecognize(path, out id, out score);
            bool success = CanPractice(expected) && recognized && id == expected;
            if (success)
            {
                life.LearnGesture(expected);
                feedback.text = "学会了。合上手账，按 G 试试。";
            }
            else feedback.text = recognized ? "跟着这一页的笔尖，再画一次。" : "画大一点，按笔尖的方向一笔画完。";
            ink.color = success ? new Color(.27f, .64f, .38f) : new Color(.76f, .42f, .29f);
        }
        public static bool ValidStroke(IList<Vector2> path) => GestureCatalog.IsUsableStroke(path);
        public void Cancel()
        {
            LifeAudio.StopStroke();
            worldMode = drawing = false; points.Clear(); feedbackUntil = 0;
            if (stroke) stroke.SetPath(null); if (drawRoot) drawRoot.gameObject.SetActive(false);
            if (life) life.SetGestureInput(false);
        }
        private static bool OverUi() => EventSystem.current && EventSystem.current.IsPointerOverGameObject();
        private static bool FocusedInput()
        {
            var selected = EventSystem.current ? EventSystem.current.currentSelectedGameObject : null;
            var field = selected ? selected.GetComponentInParent<InputField>() : null; return field && field.isFocused;
        }
        private void OnDisable() { Cancel(); if (canvas) canvas.enabled = false; }
        private void OnEnable() { if (canvas) canvas.enabled = true; }
        private void OnApplicationFocus(bool focused) { if (!focused) Cancel(); }
        private void OnDestroy() { if (canvas) Destroy(canvas.gameObject); }
    }
}
