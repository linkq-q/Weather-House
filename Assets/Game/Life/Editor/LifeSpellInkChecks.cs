using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using WeatherHousePrototype;

namespace WeatherHouse.Life.Editor
{
    public static class LifeSpellInkChecks
    {
        const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        static readonly List<string> checks = new List<string>();
        static string output = "SpellInkReview/20261029";
        [MenuItem("Weather House/Life/Check spell ink")]
        public static void Run()
        {
            if(Application.isPlaying)throw new InvalidOperationException("Spell ink checks use an isolated edit-mode scene; leave Play mode first.");
            checks.Clear(); Directory.CreateDirectory(output);
            var scene = EditorSceneManager.NewPreviewScene();
            GameObject holder = null;
            try
            {
                holder = new GameObject("Spell review", typeof(RectTransform), typeof(Canvas));
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(holder, scene);
                var canvas = holder.GetComponent<Canvas>(); canvas.renderMode = RenderMode.WorldSpace;
                var rect = (RectTransform)holder.transform; rect.sizeDelta = new Vector2(744, 440);
                var cameraObject = new GameObject("Review camera",typeof(Camera));cameraObject.transform.SetParent(holder.transform,false);
                var camera=cameraObject.GetComponent<Camera>();camera.scene=scene;camera.orthographic=true;camera.orthographicSize=220;
                camera.transform.localPosition=new Vector3(0,0,-10);camera.clearFlags=CameraClearFlags.SolidColor;camera.nearClipPlane=.1f;camera.farClipPlane=100;
                camera.enabled=false;canvas.worldCamera=camera;
                var inkObject = new GameObject("Actual world ink", typeof(RectTransform)); inkObject.transform.SetParent(holder.transform, false);
                var inkRect = (RectTransform)inkObject.transform; inkRect.sizeDelta = rect.sizeDelta;
                var ink = inkObject.AddComponent<LifeStrokeGraphic>(); ink.EnableSpellEffect();
                var magic = ink.GetComponentInChildren<LifeSpellInkGraphic>();
                Check(magic.GetComponent<CanvasRenderer>(), "Spell layer owns a CanvasRenderer for final UI compositing");
                Check(magic && !magic.raycastTarget && LifeSpellInkGraphic.WidthMultiplier == 3f, "World effect exists, ignores UI raycasts and uses 3x width");
                var path = new[] { new Vector2(-220,-110), new Vector2(-220,110), new Vector2(100,110), new Vector2(-80,-110), new Vector2(240,-110) };
                ink.SetPath(path); Canvas.ForceUpdateCanvases();
                foreach (float daylight in new[] { 1f, 0f })
                {
                    ink.SetSpellDaylight(daylight); magic.RenderInk(Time.unscaledTime + .2f);
                    var rt = (RenderTexture)magic.mainTexture;
                    var pixels = Read(rt);
                    File.WriteAllBytes(Path.Combine(output, daylight > 0 ? "day-ink.png" : "night-ink.png"), pixels.EncodeToPNG());
                    camera.backgroundColor=daylight>0?new Color(.78f,.85f,.8f):new Color(.075f,.15f,.18f);
                    Canvas.ForceUpdateCanvases();
                    checks.Add("TRACE spell UI rect="+magic.rectTransform.rect+" vertices="+magic.canvasRenderer.GetMesh()?.vertexCount+" cull="+magic.canvasRenderer.cull+" material="+magic.material.name);
                    File.WriteAllLines(Path.Combine(output,"trace.txt"),checks);
                    Capture(camera,daylight>0?"day-composite.png":"night-composite.png",744,440);
                    float center = At(pixels, -100, 110).a, edge = At(pixels, -100, 116).a;
                    Check(center > .7f && edge > .35f, "Visible broad core at daylight=" + daylight + " alpha=" + center + "/" + edge);
                    Check(At(pixels,-220,110).a > .6f && At(pixels,-260,150).a < .06f, "Round corner stays connected without a long outside spike");
                    UnityEngine.Object.DestroyImmediate(pixels);
                }
                ink.SetPath(new[] { Vector2.zero, Vector2.zero, new Vector2(200,0), Vector2.zero, new Vector2(5,120) });
                magic.RenderInk(Time.unscaledTime + .2f);
                Check(magic.SampleCount == 5, "Repeated points, reversals and acute turns retain their input positions");
                ink.ReleaseSpell(true); Check(magic.Released, "Release triggers the echo");
                magic.RenderInk(Time.unscaledTime + 2.3f); Check(magic.SampleCount == 0, "Ink expires before the existing casting UI closes");
                using (var mesh = new VertexHelper())
                { LifeStrokeGraphic.PopulateStroke(mesh,path.Take(3).ToArray(),4f,Color.white); Check(mesh.currentVertCount == 47, "Journal paper ink geometry is preserved"); }
                CheckHud(holder);
                rect.sizeDelta=new Vector2(1600,1000);camera.orthographicSize=500;
                Capture(camera,"hud-g-shortcut.png",1600,1000);
                camera.transform.localPosition=new Vector3(-580,-436,-10);camera.orthographicSize=70;
                Capture(camera,"hud-g-strip.png",512,140);
                foreach (string name in new[] { "LifeGesture/SpellInk", "LifeGesture/SpellInkComposite" })
                {
                    Shader shader = Resources.Load<Shader>(name);
                    Check(shader && shader.isSupported && !ShaderUtil.ShaderHasError(shader), "Shader supported and compiled: " + name);
                }
                checks.Add("BOUNDARY isolated editor GPU and actual component/button checks; not a full game Play session or human mouse test.");
                File.WriteAllLines(Path.Combine(output,"checks.txt"),checks); Debug.Log(string.Join("\n",checks));
            }
            finally { if(holder)UnityEngine.Object.DestroyImmediate(holder); EditorSceneManager.ClosePreviewScene(scene); }
        }
        static void CheckHud(GameObject holder)
        {
            var owner = new GameObject("HUD fixture"); owner.transform.SetParent(holder.transform);
            var session = owner.AddComponent<GameplaySession>(); var life = owner.AddComponent<LifeSession>();
            var hud = owner.GetComponent<GameplayHud>(); var input = owner.AddComponent<LifeGestureInput>();
            Set(hud,"session",session);Set(hud,"canvasRect",(RectTransform)holder.transform);
            Set(hud,"font",Resources.Load<Font>("Fonts/ZCOOLXiaoWei-Regular"));Set(hud,"gestureInput",input);
            typeof(GameplayHud).GetMethod("BuildChrome",Hidden).Invoke(hud,null);
            var draw = new GameObject("Casting root",typeof(RectTransform));draw.transform.SetParent(holder.transform,false);
            var ink = draw.AddComponent<LifeStrokeGraphic>(); ink.EnableSpellEffect();
            var text = new GameObject("Hint",typeof(RectTransform)).AddComponent<Text>();text.transform.SetParent(draw.transform,false);
            Set(input,"life",life);Set(input,"hud",hud);Set(input,"drawRoot",(RectTransform)draw.transform);Set(input,"stroke",ink);Set(input,"drawHint",text);
            var button = holder.GetComponentsInChildren<Button>(true).Single(b=>b.name=="Spell shortcut button");
            var icon = button.GetComponentsInChildren<RawImage>().Single();
            Check(icon.texture && icon.texture.name=="SpellCast" && !icon.raycastTarget, "G uses the new silhouette and delegates hits to its button");
            Check(button.GetComponent<RectTransform>().rect.width>=44 && button.GetComponent<RectTransform>().rect.height>=44, "Spell target is large enough to click");
            button.onClick.Invoke(); Check(input.IsWorldDrawing && life.GestureInputActive, "Actual HUD button enters world casting");
            button.onClick.Invoke(); Check(!input.IsWorldDrawing && !life.GestureInputActive, "Actual HUD button exits world casting");
            typeof(GameplayHud).GetProperty("JournalOpen").SetValue(hud,true);
            button.onClick.Invoke(); Check(!input.IsWorldDrawing, "Book modal prevents starting a spell");
            typeof(GameplayHud).GetProperty("JournalOpen").SetValue(hud,false);
        }
        static Texture2D Read(RenderTexture rt)
        {
            RenderTexture old=RenderTexture.active;RenderTexture.active=rt;
            var image=new Texture2D(rt.width,rt.height,TextureFormat.RGBA32,false);image.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0);image.Apply();RenderTexture.active=old;return image;
        }
        static void Capture(Camera camera,string filename,int width,int height)
        {
            var target=new RenderTexture(width,height,24);target.Create();camera.targetTexture=target;
            try
            {
                Canvas.ForceUpdateCanvases();camera.Render();var image=Read(target);
                File.WriteAllBytes(Path.Combine(output,filename),image.EncodeToPNG());
                if(filename.EndsWith("-composite.png"))
                {
                    Color line=At(image,-100,110),background=At(image,-100,190);
                    Check(line.grayscale-background.grayscale>.08f,"Final UI composite visibly contains the stroke: "+filename);
                }
                UnityEngine.Object.DestroyImmediate(image);
            }
            finally{camera.targetTexture=null;target.Release();UnityEngine.Object.DestroyImmediate(target);}
        }
        static Color At(Texture2D image,int x,int y)=>image.GetPixel(image.width/2+x,image.height/2+y);
        static void Set(object target,string name,object value)=>target.GetType().GetField(name,Hidden).SetValue(target,value);
        static void Check(bool ok,string description){checks.Add((ok?"PASS ":"FAIL ")+description);if(!ok)throw new InvalidOperationException(description);}
        public static void RunBatch()
        {
            try { Run(); EditorApplication.Exit(0); }
            catch(Exception e){Directory.CreateDirectory(output);File.WriteAllText(Path.Combine(output,"failure.txt"),e.ToString());Debug.LogException(e);EditorApplication.Exit(1);}
        }
    }
}
