using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.EventSystems;
using WeatherHousePrototype;

namespace WeatherHouse.Life.Editor
{
    // Synthetic pointer events exercise the actual pad, not the old direct recognition fixture.
    public static class LifeGesturePointerChecks
    {
        public static IEnumerator Run(LifeSession life, List<string> checks)
        {
            Require(Application.isPlaying && !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WEATHERHOUSE_LIFE_REVIEW_SAVE")), "Pointer checks require an isolated Play save", checks);
            var hud=life.GetComponent<GameplayHud>();
            var book=life.GetComponent<LifeJournal3D>();
            while(hud.IsAlbumOpening)yield return null;
            Canvas.ForceUpdateCanvases();
            var pad=hud.GetComponentsInChildren<LifeGesturePad>().Single();
            var current=life.GetComponent<LifeGestureInput>();
            var cached=Field<LifeGestureInput>(hud,"gestureInput");
            checks.Add("TRACE pointer pad="+pad.name+" input="+(pad.input?pad.input.GetInstanceID().ToString():"null")+" HUD="+(cached?cached.GetInstanceID().ToString():"null")+" current="+(current?current.GetInstanceID().ToString():"null")+" canPractice="+(current&&current.CanPractice("rain")));
            Require(pad.input && pad.input==current && cached==current, "Practice pad and HUD bind the current gesture input component", checks);
            Require(current.CanPractice("rain") && !life.IsGestureLearned("rain"), "Rain can be practised but is not yet learned in the fresh isolated session", checks);
            var definition=GestureCatalog.Find("rain");
            Vector2[] reverse=definition.points.Reverse().ToArray();
            Draw(pad,reverse,checks);
            Require(!life.IsGestureLearned("rain"), "A reverse stroke through pointer events does not learn rain", checks);
            pad.ResetStroke();
            Draw(pad,definition.points,checks);
            Require(life.IsGestureLearned("rain") && !pad.feedback, "A correct pointer stroke learns rain without restoring removed practice text", checks);
            var saved=JsonUtility.FromJson<LifeProgress>(File.ReadAllText(life.SavePath));
            Require(saved.learnedGestures.Contains("rain"), "Pointer practice persists rain only in the isolated save", checks);
            Require(Field<List<Vector2>>(pad,"points").Count>12, "The pad samples the dragged screen coordinates instead of injected ink", checks);
            book.RequestContentRefresh();
            checks.Add("BOUNDARY pointer events are synthesized in one frame; OS mouse, focus loss and prolonged drag usability remain human checks.");
            yield return null;
        }

        static void Draw(LifeGesturePad pad,IList<Vector2> controls,List<string> checks)
        {
            var rect=(RectTransform)pad.transform;
            var screen=new List<Vector2>();
            for(int segment=1;segment<controls.Count;segment++)
                for(int i=0;i<9;i++)
                    screen.Add(RectTransformUtility.WorldToScreenPoint(null,rect.TransformPoint(Vector2.Lerp(controls[segment-1],controls[segment],i/8f)*55f+Vector2.down*10f)));
            var pointer=new PointerEventData(EventSystem.current){pointerId=-1,button=PointerEventData.InputButton.Left,position=screen[0]};
            var hits=new List<RaycastResult>();EventSystem.current.RaycastAll(pointer,hits);
            checks.Add("TRACE practice raycast="+string.Join(" / ",hits.Take(5).Select(h=>h.gameObject.name)));
            Require(hits.Count>0 && hits[0].gameObject.GetComponentInParent<LifeGesturePad>()==pad, "Practice pad is the first actual UI raycast at the stroke start", checks);
            pointer.pointerCurrentRaycast=pointer.pointerPressRaycast=hits[0];
            pointer.pointerPress=ExecuteEvents.GetEventHandler<IPointerDownHandler>(hits[0].gameObject);
            pointer.pointerDrag=ExecuteEvents.GetEventHandler<IDragHandler>(hits[0].gameObject);
            ExecuteEvents.Execute(pointer.pointerPress,pointer,ExecuteEvents.pointerDownHandler);
            foreach(Vector2 point in screen)
            {
                pointer.delta=point-pointer.position;pointer.position=point;
                ExecuteEvents.Execute(pointer.pointerDrag,pointer,ExecuteEvents.dragHandler);
            }
            Require(Field<List<Vector2>>(pad,"points").Count>12 && Field<List<Vector2>>(pad.ink,"points").Count>12,
                "Actual pointer down and drag populate both practice samples and visible ink geometry",checks);
            ExecuteEvents.Execute(pointer.pointerPress,pointer,ExecuteEvents.pointerUpHandler);
        }
        static T Field<T>(object target,string name)=>(T)target.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(target);
        static void Require(bool value,string message,List<string> checks)
        {checks.Add((value?"PASS ":"FAIL ")+message);if(!value)throw new InvalidOperationException(message);}
    }
}
