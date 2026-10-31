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
        private RectTransform root, drawRoot, hintRoot;
        private Text interactionText, drawHint;
        private WatersideMood mood;
        private LifeStrokeGraphic stroke;
        private LifeStrokeGraphic practiceTail;
        private readonly List<Vector2> points = new List<Vector2>();
        private bool drawing, worldMode, practiceStarted;
        private float feedbackUntil;
        public bool IsWorldDrawing => worldMode;
        public bool IsStrokeActive => drawing || (life && life.GestureInputActive && !worldMode) ||
            (practiceTail && practiceTail.isActiveAndEnabled && Time.unscaledTime < practiceTail.SpellReleaseEndTime);
        public bool HasStartedPractice => practiceStarted;

        private void Start()
        {
            life = GetComponent<LifeSession>(); hud = GetComponent<GameplayHud>();
            if (!life || !hud) { enabled = false; return; }
            var go = new GameObject("Life gameplay UI", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            go.layer = 5; go.transform.SetParent(transform, false); canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 110; root = (RectTransform)go.transform;
            var scaler = go.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution = new Vector2(1600, 1000); scaler.matchWidthOrHeight = .5f;
            Font font = Resources.Load<Font>("Fonts/ZCOOLXiaoWei-Regular");
            mood = FindObjectOfType<WatersideMood>();
            hintRoot = LifeUi.Rect(root, "Object interaction", Vector2.zero, new Vector2(1100, 60)); hintRoot.anchorMin = hintRoot.anchorMax = new Vector2(.5f, 0); hintRoot.anchoredPosition = new Vector2(0, 80);
            interactionText = LifeUi.Text(hintRoot, "Nearby objects", "", Vector2.zero, hintRoot.sizeDelta, font, 22, Color.white, TextAnchor.MiddleCenter);
            drawRoot = LifeUi.Rect(root, "Weather gesture mode", Vector2.zero, new Vector2(1600, 1000));
            drawRoot.anchorMin = Vector2.zero; drawRoot.anchorMax = Vector2.one; drawRoot.offsetMin = drawRoot.offsetMax = Vector2.zero;
            stroke = LifeUi.Rect(drawRoot, "Weather ink", Vector2.zero, new Vector2(1600, 1000)).gameObject.AddComponent<LifeStrokeGraphic>();
            stroke.rectTransform.anchorMin = Vector2.zero; stroke.rectTransform.anchorMax = Vector2.one; stroke.rectTransform.offsetMin = stroke.rectTransform.offsetMax = Vector2.zero;
            stroke.EnableSpellEffect();
            UpdateStrokeDaylight();
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
            UpdateStrokeDaylight();
            if (worldMode && (modal || Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.J) || Input.GetKeyDown(KeyCode.C))) Cancel();
            hintRoot.gameObject.SetActive(!modal && !worldMode);
            string gather=modal?"":hud.GatheringHint;
            interactionText.text = string.IsNullOrEmpty(gather)?life.InteractionHint:gather+(string.IsNullOrEmpty(life.InteractionHint)?"":"  ·  "+life.InteractionHint);
            if (modal) return;
            if (Input.GetKeyDown(KeyCode.G)) ToggleWorldCasting();
            if (!worldMode) return;
            if (Input.GetMouseButtonDown(0) && !OverUi())
            {
                drawing = true; points.Clear(); feedbackUntil = 0;
                stroke.SetPath(null);
                stroke.color = new Color(.61f, .87f, .94f); AddPoint();
            }
            if (drawing && Input.GetMouseButton(0)) AddPoint();
            if (drawing && Input.GetMouseButtonUp(0)) { AddPoint(); drawing = false; SubmitStroke(); }
            if (!drawing && feedbackUntil > 0 && Time.unscaledTime >= feedbackUntil) Cancel();
        }

        private void UpdateStrokeDaylight()
        {
            // The scene exposes an F8 mood rather than an advancing game clock.
            bool night = mood && mood.Current == WatersideMood.Mode.Night;
            if (stroke) stroke.SetSpellDaylight(night ? 0f : 1f);
        }

        public void ToggleWorldCasting()
        {
            if (!life || !drawRoot || Modal || LifeOnboarding.Blocks(life.Gameplay)) return;
            if (worldMode) { Cancel(); return; }
            worldMode = true; life.SetGestureInput(true); drawRoot.gameObject.SetActive(true);
            drawHint.text = "按住左键描画  ·  Esc 收起"; stroke.SetPath(null);
        }

        private void AddPoint()
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(stroke.rectTransform, Input.mousePosition, null, out Vector2 point);
            if (points.Count < 512 && (points.Count == 0 || Vector2.Distance(points[points.Count - 1], point) >= 3f))
            { if (points.Count > 0) LifeAudio.StrokeMoved(true); points.Add(point); }
            stroke.SetPath(points);
        }

        private void SubmitStroke()
        {
            LifeAudio.StopStroke();
            bool success = false, noWeatherTarget = false; string message;
            if (!ValidStroke(points) || !GestureCatalog.TryRecognize(points, out string id, out float score)) message = "跟着手账里的笔尖，再画一次。";
            else if (!life.IsGestureUnlocked(id)) message = "";
            else
            {
                success = life.TryCast(id, out message);
                noWeatherTarget = !success && (id == "strong" || id == "weak") && string.IsNullOrEmpty(message);
            }
            if (!success) LifeAudio.Play(LifeAudioCue.CastFailed);
            stroke.color = success ? new Color(.66f, .95f, .73f) : new Color(1f, .54f, .36f);
            stroke.ReleaseSpell(success);
            drawHint.text = noWeatherTarget ? "" : message + "\n可以再次描画，Esc 收起";
            feedbackUntil = stroke.SpellReleaseEndTime;
        }

        public bool CanPractice(string id) => life && life.IsGestureUnlocked(id) && hud && hud.JournalOpen && !hud.IsAlbumOpening;
        public void SetPracticeDrawing(bool active)
        {
            if (active) practiceStarted = true;
            if (life && !worldMode) life.SetGestureInput(active);
        }
        public void CompletePractice(string expected, IList<Vector2> path, Text feedback, LifeStrokeGraphic ink)
        {
            string id = null; float score = 0;
            bool recognized = ValidStroke(path) && GestureCatalog.TryRecognize(path, out id, out score);
            bool success = CanPractice(expected) && recognized && id == expected;
            if (success)
            {
                life.LearnGesture(expected);
                if (feedback) feedback.text = "学会了。合上手账，按 G 试试。";
            }
            else if (feedback) feedback.text = recognized ? "跟着这一页的笔尖，再画一次。" : "画大一点，按笔尖的方向一笔画完。";
            ink.color = success ? new Color(.27f, .64f, .38f) : new Color(.76f, .42f, .29f);
            ReleasePracticeStroke(ink, success);
        }
        public void ReleasePracticeStroke(LifeStrokeGraphic ink, bool success)
        {
            ink.ReleaseSpell(success);
            // Learning marks the book dirty; defer its rebuild without retaining the input lock.
            practiceTail = ink;
        }
        public static bool ValidStroke(IList<Vector2> path) => GestureCatalog.IsUsableStroke(path);
        public void Cancel()
        {
            LifeAudio.StopStroke();
            worldMode = drawing = false; points.Clear(); feedbackUntil = 0;
            if (practiceTail) practiceTail.SetPath(null); practiceTail = null;
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
