using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using WeatherHousePrototype;

namespace WeatherHouse.Life
{
    [DisallowMultipleComponent, DefaultExecutionOrder(220)]
    public sealed class LifeGestureInput : MonoBehaviour
    {
        private LifeSession life;
        private GameplayHud hud;
        private Canvas canvas;
        private RectTransform root, drawRoot, taskRoot, hintRoot;
        private Text taskText, taskPageText, interactionText, helpText, drawHint;
        private Button previousTaskPage, nextTaskPage;
        private LifeStrokeGraphic stroke;
        private readonly List<Vector2> points = new List<Vector2>();
        private bool drawing, worldMode;
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
            taskRoot = LifeUi.Panel(root, "Help tasks", Vector2.zero, new Vector2(390, 270), new Color(.12f, .2f, .17f, .74f)).rectTransform;
            taskRoot.anchorMin = taskRoot.anchorMax = Vector2.one; taskRoot.pivot = Vector2.one; taskRoot.anchoredPosition = new Vector2(-28, -28);
            taskText = LifeUi.Text(taskRoot, "Understood requests", "", new Vector2(0, -16), new Vector2(350, 162), font, 21, Color.white, TextAnchor.UpperLeft);
            taskText.rectTransform.anchorMin = taskText.rectTransform.anchorMax = new Vector2(.5f, 1f); taskText.rectTransform.pivot = new Vector2(.5f, 1f);
            previousTaskPage = LifeUi.Button(taskRoot, "Previous task page", "上一页", new Vector2(-132, 22), new Vector2(82, 34), font, () => life.ChangeTaskPage(-1));
            nextTaskPage = LifeUi.Button(taskRoot, "Next task page", "下一页", new Vector2(132, 22), new Vector2(82, 34), font, () => life.ChangeTaskPage(1));
            taskPageText = LifeUi.Text(taskRoot, "Task page", "", new Vector2(0, 22), new Vector2(160, 34), font, 18, new Color(.86f, .89f, .71f), TextAnchor.MiddleCenter);
            foreach (var footer in new[] { (RectTransform)previousTaskPage.transform, (RectTransform)nextTaskPage.transform, taskPageText.rectTransform }) footer.anchorMin = footer.anchorMax = new Vector2(.5f, 0f);
            taskRoot.gameObject.SetActive(false);
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
            bool modal = Modal;
            if (worldMode && (modal || Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.J) || Input.GetKeyDown(KeyCode.C))) Cancel();
            bool activeTasks = life.Progress.tasks.Exists(task => task != null && task.status == LifeTaskStatus.Active);
            taskRoot.gameObject.SetActive(!modal && activeTasks); hintRoot.gameObject.SetActive(!modal && !worldMode); helpText.gameObject.SetActive(!modal && !worldMode);
            string tasks = life.TaskText;
            taskText.text = tasks;
            bool multiplePages = life.TaskPageCount > 1;
            float taskHeight = Mathf.Clamp(taskText.preferredHeight + 4f, 48f, 224f);
            taskText.rectTransform.sizeDelta = new Vector2(350, taskHeight);
            taskRoot.sizeDelta = new Vector2(390, taskHeight + 32f + (multiplePages ? 44f : 0f));
            taskPageText.text = multiplePages ? (life.TaskPage + 1) + " / " + life.TaskPageCount + " 页" : "";
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
            else if (!life.IsGestureLearned(id)) message = "先到 J 手账里练习这个手势。";
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
