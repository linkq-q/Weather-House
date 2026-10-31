using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace WeatherHouse.Life
{
    public sealed class LifeGesturePad : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        public LifeGestureInput input;
        public string gestureId;
        public Text feedback;
        public LifeStrokeGraphic ink;
        public GameObject mouseHint;
        private readonly List<Vector2> points = new List<Vector2>();
        private int pointer = int.MinValue;
        private bool leftBounds;
        private void Update()
        {
            if (pointer != int.MinValue && (Input.GetKeyDown(KeyCode.Escape) || !Application.isFocused)) ResetStroke();
        }
        public void OnPointerDown(PointerEventData e)
        {
            if (e.button != PointerEventData.InputButton.Left || pointer != int.MinValue || !input || !input.CanPractice(gestureId)) return;
            pointer = e.pointerId; leftBounds = false; points.Clear(); ink.SetPath(null); ink.color = new Color(.25f, .57f, .65f);
            input.SetPracticeDrawing(true); Add(e);
        }
        public void OnDrag(PointerEventData e) { if (e.pointerId == pointer) Add(e); }
        private void Add(PointerEventData e)
        {
            var rect = (RectTransform)transform;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(rect, e.position, e.pressEventCamera, out Vector2 point);
            if (!rect.rect.Contains(point)) { leftBounds = true; return; }
            if (points.Count >= 512) { leftBounds = true; return; }
            if (points.Count == 0 || Vector2.Distance(points[points.Count - 1], point) > 2f)
            { if (points.Count > 0) LifeAudio.StrokeMoved(); points.Add(point); if (mouseHint) mouseHint.SetActive(false); }
            ink.SetPath(points);
        }
        public void OnPointerUp(PointerEventData e)
        {
            if (e.pointerId != pointer) return;
            Add(e); pointer = int.MinValue; input.SetPracticeDrawing(false);
            LifeAudio.StopStroke();
            if (leftBounds) { if (feedback) feedback.text = "笔迹离开了练习页，请在框内重试。"; ink.color = new Color(.75f, .4f, .3f); input.ReleasePracticeStroke(ink, false); }
            else input.CompletePractice(gestureId, points, feedback, ink);
        }
        public void ResetStroke() { pointer = int.MinValue; points.Clear(); LifeAudio.StopStroke(); if (ink) ink.SetPath(null); if (input) input.SetPracticeDrawing(false); }
        private void OnDisable() { ResetStroke(); }
    }
}
