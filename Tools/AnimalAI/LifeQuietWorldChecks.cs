// Focused runtime-object checks; invoke Run in the independent Unity validation project.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;
using WeatherHouse.Life;
using WeatherHouse.WatersideV2;
using WeatherHousePrototype;
using Object=UnityEngine.Object;

public static class LifeQuietWorldChecks
{
    static readonly BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
    static object Call(object owner,string name,params object[] args)=>owner.GetType().GetMethod(name,Hidden).Invoke(owner,args);
    static object Field(object owner,string name)=>owner.GetType().GetField(name,Hidden).GetValue(owner);
    static void Set(object owner,string name,object value)=>owner.GetType().GetField(name,Hidden).SetValue(owner,value);
    static void Require(bool ok,string message){if(!ok)throw new InvalidOperationException(message);}
    static bool NoMarkers(Transform root)=>root.GetComponentsInChildren<LifeLabel>(true).Length==0 &&
        !root.GetComponentsInChildren<Transform>(true).Any(t=>t.name=="生活点范围" || t.name=="生活点提示");
    static bool NoTaskHud(Transform root)=>!root.GetComponentsInChildren<Transform>(true).Any(t=>
        t.name=="Help tasks" || t.name=="Help tasks toggle" || t.name=="Help task icon" || t.name.StartsWith("Weather task ") ||
        t.name.StartsWith("Task marker ") || t.name.StartsWith("Task 0 line ") || t.name.StartsWith("Task 1 line ") ||
        t.name=="Task page" || t.name=="Previous task page" || t.name=="Next task page");

    public static string[] Run()
    {
        var result=new List<string>();var root=new GameObject("Quiet life presentation fixture");root.SetActive(false);
        LifeGestureInput input=null;
        try
        {
            var world=root.AddComponent<LifeWorld>();
            Call(world,"Landmark","quiet.here","测试站位",new Vector3(10,0,10));
            Call(world,"Rest","quiet.rest","测试休息处",new Vector3(12,0,10),false,18f);
            var ribbon=(LifeObject)Call(world,"Model","quiet.ribbon","测试风布条",LifeObjectKind.Ribbon,"RibbonFlag",new Vector3(14,0,10));
            Require(NoMarkers(root.transform),"Station/Rest/Model still allocate a location label or ground ring, even when hidden");
            result.Add("PASS actual Station/Rest/Model construction creates neither visible nor hidden world labels/rings");
            var anchors=(Dictionary<string,Vector3>)Field(world,"anchors");
            Require(world.Objects.Count()==3 && anchors["quiet.here"]==new Vector3(10,0,10) && anchors["quiet.rest"]==new Vector3(12,0,10) &&
                world.Objects.Any(o=>o.State.id=="quiet.rest" && o.State.kind==LifeObjectKind.Rest && o.State.temperature==18) && ribbon,
                "Removing presentation also removed physical state, registered objects or destination anchors");
            Require(((ICollection<LifeObject>)Field(world,"interactables")).Count==3 && ribbon.GetComponentsInChildren<Renderer>(true).Length>0,
                "Life objects are no longer available to interaction logic or their physical model was removed");
            result.Add("PASS life objects, rest properties, interactables and exact destination anchors remain registered");

            var life=root.AddComponent<LifeSession>();root.AddComponent<GameplayHud>();input=root.AddComponent<LifeGestureInput>();
            life.Progress.tasks.Add(new LifeTask{requestId="legacy.rain",eventId="E1",animalId="legacy.dog",status=LifeTaskStatus.Active,acceptedUtcTicks=1234});
            life.Progress.tasks.Add(new LifeTask{requestId="legacy.warm",eventId="E5",animalId="legacy.sheep",status=LifeTaskStatus.Complete,acceptedUtcTicks=5678});
            string before=JsonUtility.ToJson(life.Progress);
            Call(input,"Start");Call(input,"Update");
            Require(NoTaskHud(input.transform),"Gesture UI still builds a task note, entry icon, page controls or progress text");
            Require(JsonUtility.ToJson(life.Progress)==before,"Creating/updating the new UI changed existing task records");
            result.Add("PASS actual gesture UI Start/Update builds no task HUD and leaves active/completed legacy records unchanged");
            var stroke=(LifeStrokeGraphic)Field(input,"stroke");var ink=stroke.GetComponentInChildren<LifeSpellInkGraphic>(true);
            Require(stroke && ink && Field(input,"drawRoot") is RectTransform && Field(input,"drawHint") is Text &&
                Field(input,"hintRoot") is RectTransform && input.transform.Find("Life gameplay UI").GetComponent<GraphicRaycaster>(),
                "Casting canvas, ink, feedback or operation hints disappeared with the task UI");
            var mood=root.AddComponent<WatersideMood>();Set(input,"mood",mood);
            Set(mood,"current",WatersideMood.Mode.Night);Call(input,"UpdateStrokeDaylight");Require(ink.Daylight==0,"Night spell intensity was lost");
            Set(mood,"current",WatersideMood.Mode.Day);Call(input,"UpdateStrokeDaylight");Require(ink.Daylight==1,"Day spell intensity was lost");
            result.Add("PASS casting/operation components remain and the actual day/night ink intensity still updates");

            var game=root.AddComponent<GameplaySession>();var catalog=JsonUtility.FromJson<GlyphCatalog>(Resources.Load<TextAsset>("WeatherHouseGlyphs").text);
            var notebook=new LanguageNotebook();notebook.Normalize(catalog);
            notebook.AddObservation(new ObservationRecord{id="quiet-photo",photoSnapshot=new LifePhotoSnapshot{entities=new[]{
                new LifePhotoEntity{id="quiet-fox",kind="animal",speciesId="fox",bodyVisible=true,sleeping=true}}}});
            notebook.ApplyPhotoTasks();Require(notebook.IsPhotoTaskComplete("P01"),"Photo fixture did not establish the retained photo objective");
            typeof(GameplaySession).GetProperty("Catalog").SetValue(game,catalog);typeof(GameplaySession).GetProperty("Notebook").SetValue(game,notebook);
            var page=new GameObject("Retained photography page",typeof(RectTransform));page.transform.SetParent(root.transform,false);
            Font font=Resources.Load<Font>("Fonts/ZCOOLXiaoWei-Regular");
            LifePhotoBook.Build(page.transform,game,0,font,_=>{},_=>{});
            Require(page.transform.Find("Photo task P01/Recorded seal/Seal")?.GetComponent<Text>().text=="✓" &&
                page.GetComponentsInChildren<AnimalGlyphGraphic>(true).Length>0,"Photo animal-language goals or completion checkmarks were removed");
            result.Add("PASS retained photography target page still builds animal glyphs and the real completed-objective checkmark");
            notebook.GetWord("fox").confirmed=true;
            LifePhotoBook.TokenLine(page.transform,game,new[]{"fox"},Vector2.zero,40,200,font);
            Require(page.transform.Find("Meaning fox")?.GetComponent<Text>().text==catalog.glyphs.Single(g=>g.id=="fox").name,
                "Confirmed Chinese photo meaning no longer appears");
            Require(JsonUtility.ToJson(life.Progress)==before,"Photo-page presentation changed the task ledger");
            result.Add("PASS confirmed Chinese photo meaning and existing task records remain available");
            return result.ToArray();
        }
        finally
        {
            if(input)
            {
                var canvas=(Canvas)Field(input,"canvas");Set(input,"canvas",null);Set(input,"life",null);
                if(canvas)Object.DestroyImmediate(canvas.gameObject);
            }
            Object.DestroyImmediate(root);
        }
    }

    public static string[] CheckScene(LifeSession life)
    {
        Require(life && life.World && life.Gameplay,"Actual scene life session must be ready");
        string before=JsonUtility.ToJson(life.Progress);
        Require(NoMarkers(life.World.transform),"Actual scene contains life-point rings or labels");
        var input=life.GetComponent<LifeGestureInput>();Require(input && NoTaskHud(input.transform),"Actual scene still contains the removed task HUD");
        Require(life.World.Objects.Any() && ((Dictionary<string,Vector3>)Field(life.World,"anchors")).Count>0,
            "Actual scene lost its underlying life objects or destination anchors");
        Require(Field(input,"stroke") is LifeStrokeGraphic && Field(input,"drawHint") is Text && Field(input,"hintRoot") is RectTransform,
            "Actual scene casting and operation hint components are missing");
        Require(JsonUtility.ToJson(life.Progress)==before,"Read-only scene checks changed task progress");
        return new[]{"PASS actual scene has no life-point rings/labels or task HUD; physical objects, anchors and casting controls remain; progress untouched"};
    }
}
