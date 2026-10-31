using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using WeatherHouse.Hillside;
using Object=UnityEngine.Object;

// Copy into the independent ValidationProject's Editor folder; never opens or saves a production scene.
public static class EmotionGlyphRegression
{
    const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
    const int Tile=512;
    static void Require(bool value,string message){if(!value)throw new InvalidOperationException(message);}
    static void Call(object instance,string method)=>instance.GetType().GetMethod(method,Private).Invoke(instance,null);
    static void Age(AnimalEmotionGlyph glyph,float value)=>typeof(AnimalEmotionGlyph).GetField("age",Private).SetValue(glyph,value);
    static LineRenderer[,] Lines(AnimalEmotionGlyph glyph)=>(LineRenderer[,])typeof(AnimalEmotionGlyph).GetField("lines",Private).GetValue(glyph);
    static MeshRenderer Hearts(AnimalEmotionGlyph glyph)=>(MeshRenderer)typeof(AnimalEmotionGlyph).GetField("hearts",Private).GetValue(glyph);
    static void Hidden(AnimalEmotionGlyph glyph)
    {
        foreach(LineRenderer line in Lines(glyph))Require(!line.enabled,"Inactive emotion left a visible stroke");
        MeshRenderer hearts=Hearts(glyph);Require(!hearts || !hearts.enabled,"Inactive emotion left a visible filled heart");
    }

    public static void Batch()
    {
        string report=Environment.GetEnvironmentVariable("WEATHERHOUSE_EMOTION_REPORT");
        if(string.IsNullOrEmpty(report))report=Path.Combine(Path.GetDirectoryName(Application.dataPath),"EmotionGlyphEvidence");
        var checks=new List<string>();
        try
        {
            Require(Application.isBatchMode && !Application.isPlaying && Application.dataPath.IndexOf("ValidationProject",StringComparison.OrdinalIgnoreCase)>=0,
                "Run only in the independent ValidationProject batch editor.");
            Directory.CreateDirectory(report);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            Run(report,checks);
            File.WriteAllLines(Path.Combine(report,"checks.txt"),checks);
            Debug.Log("EmotionGlyphRegression PASS: "+report);EditorApplication.Exit(0);
        }
        catch(Exception error)
        {
            checks.Add("FAIL "+error);Directory.CreateDirectory(report);File.WriteAllLines(Path.Combine(report,"checks.txt"),checks);
            Debug.LogException(error);EditorApplication.Exit(1);
        }
    }

    static void Run(string report,List<string> checks)
    {
        var body=new GameObject("Emotion renderer fixture");body.transform.localScale=new Vector3(.4f,1.8f,2.3f);body.transform.rotation=Quaternion.Euler(15,27,3);
        var sleep=body.AddComponent<AnimalSleepGlyph>();var glyph=body.AddComponent<AnimalEmotionGlyph>();
        var camera=new GameObject("Actual rendering camera").AddComponent<Camera>();
        RenderTexture target=null;Texture2D pixels=null,sheet=null;
        try
        {
            // Edit-mode AddComponent does not run the runtime lifecycle reliably.
            Call(sleep,"OnDisable");Call(sleep,"OnEnable");Call(glyph,"OnDisable");Call(glyph,"OnEnable");
            camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.15f,.18f,.2f);
            camera.orthographic=true;camera.orthographicSize=.67f;camera.nearClipPlane=.1f;camera.farClipPlane=50;
            target=new RenderTexture(Tile,Tile,24);target.antiAliasing=4;target.Create();camera.targetTexture=target;
            pixels=new Texture2D(Tile,Tile,TextureFormat.RGB24,false);sheet=new Texture2D(Tile*4,Tile*2,TextureFormat.RGB24,false);
            Require(glyph.Play(AnimalEmotion.Heart),"First Play rejected");
            var ids=new List<int>();Material material=null;
            foreach(LineRenderer line in Lines(glyph))
            {
                ids.Add(line.GetInstanceID());if(!material)material=line.sharedMaterial;
                Require(line.sharedMaterial==material && line.useWorldSpace && line.numCapVertices>=12 && line.numCornerVertices>=12,
                    "Strokes must share one material, use world points and retain rounded caps/corners");
            }
            Require(ids.Count==15,"Expected a bounded pool of 15 strokes");
            int heartRendererId=Hearts(glyph).GetInstanceID(),heartMeshId=Hearts(glyph).GetComponent<MeshFilter>().sharedMesh.GetInstanceID();
            Vector3 focus=new Vector3(0,1.45f,0);
            for(int angle=0;angle<2;angle++)
            {
                camera.transform.position=focus+(angle==0?new Vector3(0,0,-4):new Vector3(4,1.65f,0));camera.transform.LookAt(focus);
                for(int icon=0;icon<8;icon++)
                {
                    Require(glyph.Play((AnimalEmotion)icon),"Emotion rejected: "+icon);Age(glyph,icon==(int)AnimalEmotion.Exclamation?0:1.2f);
                    camera.Render();AssertBillboard(glyph,camera,(AnimalEmotion)icon);Read(target,pixels);
                    Require(BrightPixels(pixels)>150,"Missing or blank icon render: "+icon+" angle "+angle);
                    if(icon==(int)AnimalEmotion.Heart)AssertFilledHeart(glyph,camera,pixels);
                    File.WriteAllBytes(Path.Combine(report,"emotion-"+angle+"-"+((AnimalEmotion)icon)+".png"),pixels.EncodeToPNG());
                    sheet.SetPixels((icon%4)*Tile,(1-icon/4)*Tile,Tile,Tile,pixels.GetPixels());
                }
                sheet.Apply();File.WriteAllBytes(Path.Combine(report,"emotions-camera-"+angle+".png"),sheet.EncodeToPNG());
            }
            checks.Add("PASS eight symbols rendered by two cameras (front and elevated side), under nonuniform body scale; 16 original PNGs and two 4x2 contact sheets");
            checks.Add("Sheet order: top row Heart, Happy (lowercase hh), Cry (lowercase qq), Anger; bottom row Question, Exclamation, Sweat, Music. All images are original Unity renders.");
            checks.Add("PASS solid heart: 3 filled silhouettes, no center holes in either real camera render, visible width about85percent of previous outlined heart, rotated/nonuniform body scale preserved");
            camera.transform.position=focus+new Vector3(0,0,-4);camera.transform.LookAt(focus);
            CheckAlert(glyph,camera,target,pixels,report,checks);

            glyph.Play(AnimalEmotion.Happy);Age(glyph,.7f);camera.Render();Require(glyph.IsPlaying,"Ordinary expression not playing");
            sleep.Sleeping=true;camera.Render();Require(!glyph.IsPlaying,"Sleep did not preempt ordinary expression");Hidden(glyph);
            Require(!glyph.Play(AnimalEmotion.Exclamation),"Sleep must reject urgent symbols too");
            Read(target,pixels);Require(BrightPixels(pixels)>150,"Sleeping Z missing after emotion preemption");
            File.WriteAllBytes(Path.Combine(report,"sleep-priority.png"),pixels.EncodeToPNG());
            sleep.Sleeping=false;camera.Render();Require(!glyph.IsPlaying,"A preempted emotion resumed after waking");Hidden(glyph);
            checks.Add("PASS sleep preempts existing emotion, rejects new emotion and wakes without queued playback; original Z still renders");
            foreach(AnimalEmotion special in new[]{AnimalEmotion.Heart,AnimalEmotion.Exclamation})
            {
                Require(glyph.Play(special),"Special playback rejected");Age(glyph,special==AnimalEmotion.Heart?1.2f:0);camera.Render();
                sleep.Sleeping=true;camera.Render();Require(!glyph.IsPlaying,"Sleep did not cancel "+special);Hidden(glyph);
                sleep.Sleeping=false;camera.Render();Require(!glyph.IsPlaying,"Special expression resumed after waking");Hidden(glyph);
            }
            checks.Add("PASS sleep cancels both the filled heart mesh and the new single alert without a queued restart");

            Require(glyph.Play(AnimalEmotion.Music,.4f),"Finite playback rejected");Age(glyph,.39f);camera.Render();Require(glyph.IsPlaying,"One-shot ended early");
            Age(glyph,.4f);Call(glyph,"LateUpdate");camera.Render();Require(!glyph.IsPlaying,"One-shot did not expire");Hidden(glyph);
            Require(!glyph.Play(AnimalEmotion.Music,float.NaN) && !glyph.Play(AnimalEmotion.Music,0),"Invalid playback duration accepted");
            checks.Add("PASS short playback expires at its duration and leaves no visible strokes");

            Require(glyph.Play(AnimalEmotion.Sweat),"Playback before disable rejected");Age(glyph,1);camera.Render();
            glyph.enabled=false;Call(glyph,"OnDisable");Require(!glyph.IsPlaying,"Disabled renderer retained playback");Hidden(glyph);
            Require(!glyph.Play(AnimalEmotion.Heart),"Disabled component accepted playback");
            glyph.enabled=true;Call(glyph,"OnEnable");Require(!glyph.IsPlaying,"Enable resumed a cancelled expression");
            Require(glyph.Play(AnimalEmotion.Question),"Enable did not restore ability to play");Age(glyph,.8f);camera.Render();
            Require(glyph.IsPlaying,"Reenabled playback missing");AssertBillboard(glyph,camera,AnimalEmotion.Question);
            int id=0;foreach(LineRenderer line in Lines(glyph))Require(line.GetInstanceID()==ids[id++] && line.sharedMaterial==material,"Playback or enable recreated renderer resources");
            Require(Hearts(glyph).GetInstanceID()==heartRendererId && Hearts(glyph).GetComponent<MeshFilter>().sharedMesh.GetInstanceID()==heartMeshId && Hearts(glyph).sharedMaterial==material,"Filled heart resources were recreated");
            glyph.Play(AnimalEmotion.Heart);Age(glyph,1.2f);camera.Render();glyph.enabled=false;Call(glyph,"OnDisable");Hidden(glyph);
            glyph.enabled=true;Call(glyph,"OnEnable");Require(glyph.Play(AnimalEmotion.Question),"Fresh playback after filled-heart disable failed");Age(glyph,.8f);
            checks.Add("PASS disable cancels lines and filled mesh, enable permits fresh playback; all changes reuse the same 15 lines, one heart mesh/renderer and one material");

            // Sequential far then near camera renders must not leave the renderer hidden for the near view.
            camera.transform.position=new Vector3(40,1.45f,0);camera.transform.LookAt(focus);camera.Render();Hidden(glyph);
            camera.transform.position=focus+new Vector3(0,0,-4);camera.transform.LookAt(focus);camera.Render();AssertBillboard(glyph,camera,AnimalEmotion.Question);
            glyph.Play(AnimalEmotion.Heart);Age(glyph,1.2f);camera.Render();
            camera.transform.position=focus+new Vector3(40,0,0);camera.transform.LookAt(focus);camera.Render();Hidden(glyph);
            camera.transform.position=focus+new Vector3(0,0,-4);camera.transform.LookAt(focus);camera.Render();AssertBillboard(glyph,camera,AnimalEmotion.Heart);
            checks.Add("PASS distance culling recovers on the next nearby camera render");
        }
        finally
        {
            Call(glyph,"OnDisable");Call(sleep,"OnDisable");camera.targetTexture=null;
            if(target){target.Release();Object.DestroyImmediate(target);}if(pixels)Object.DestroyImmediate(pixels);if(sheet)Object.DestroyImmediate(sheet);
            Object.DestroyImmediate(camera.gameObject);Object.DestroyImmediate(body);
        }
    }
    static void AssertBillboard(AnimalEmotionGlyph glyph,Camera camera,AnimalEmotion emotion)
    {
        int[] strokes={1,4,4,3,2,2,1,3};int visible=0;
        foreach(LineRenderer line in Lines(glyph))
        {
            if(!line.enabled)continue;visible++;
            Require(line.startColor.r==1 && line.startColor.g==1 && line.startColor.b==1,"Emotion is not white");
            Vector3 start=line.GetPosition(0);
            for(int point=1;point<line.positionCount;point++)
                Require(Mathf.Abs(Vector3.Dot(line.GetPosition(point)-start,camera.transform.forward))<.0001f,"Stroke did not face the current rendering camera");
        }
        int expected=emotion==AnimalEmotion.Heart?0:strokes[(int)emotion]*(emotion==AnimalEmotion.Exclamation?1:3);
        Require(visible==expected,"Wrong stroke count for "+emotion+" (alert must be a single copy, heart must be filled, anger has three curves per copy)");
        MeshRenderer hearts=Hearts(glyph);
        if(emotion!=AnimalEmotion.Heart){Require(!hearts || !hearts.enabled,"Filled hearts remained visible over a different expression");return;}
        Require(hearts && hearts.enabled,"Filled heart mesh missing");Mesh mesh=hearts.GetComponent<MeshFilter>().sharedMesh;
        Vector3[] vertices=mesh.vertices;Require(vertices.Length%3==0,"Heart mesh must contain three copies");int stride=vertices.Length/3;
        for(int copy=0;copy<3;copy++)
        {
            Vector3 center=hearts.transform.TransformPoint(vertices[copy*stride]);
            for(int point=1;point<stride;point++)Require(Mathf.Abs(Vector3.Dot(hearts.transform.TransformPoint(vertices[copy*stride+point])-center,camera.transform.forward))<.0001f,"Filled heart did not face the current camera");
        }
    }
    static void AssertFilledHeart(AnimalEmotionGlyph glyph,Camera camera,Texture2D pixels)
    {
        MeshRenderer renderer=Hearts(glyph);Vector3[] vertices=renderer.GetComponent<MeshFilter>().sharedMesh.vertices;
        Vector3 center=renderer.transform.TransformPoint(vertices[0]);int stride=vertices.Length/3;float lo=float.PositiveInfinity,hi=float.NegativeInfinity;
        for(int point=1;point<stride;point++)
        {float x=Vector3.Dot(renderer.transform.TransformPoint(vertices[point])-center,camera.transform.right);lo=Mathf.Min(lo,x);hi=Mathf.Max(hi,x);}
        float ratio=(hi-lo)/.268f;Require(ratio>.82f && ratio<.88f,"Filled heart was not about15percent smaller than the previous0.268m outlined width: "+ratio);
        for(int x=-1;x<=1;x++)for(int y=-1;y<=1;y++)
        {
            Vector3 pixel=camera.WorldToScreenPoint(center+camera.transform.right*(x*.025f)+camera.transform.up*(y*.025f));
            Color color=pixels.GetPixel(Mathf.RoundToInt(pixel.x),Mathf.RoundToInt(pixel.y));
            Require(color.r>.9f && color.g>.9f && color.b>.9f,"Visible hole or tint inside the heart at "+x+","+y);
        }
    }
    static void CheckAlert(AnimalEmotionGlyph glyph,Camera camera,RenderTexture target,Texture2D pixels,string report,List<string> checks)
    {
        Require(glyph.Play(AnimalEmotion.Exclamation),"Single alert rejected");
        float previous=2;var values=new List<string>();
        for(int sample=0;sample<=4;sample++)
        {
            float progress=sample/4f;Age(glyph,progress*2.4f);
            if(sample==4)Call(glyph,"LateUpdate");camera.Render();Read(target,pixels);
            File.WriteAllBytes(Path.Combine(report,"single-alert-phase-"+sample+".png"),pixels.EncodeToPNG());
            if(sample==4){Require(!glyph.IsPlaying,"Single alert did not end");Hidden(glyph);Require(BrightPixels(pixels)==0,"Expired single alert left bright pixels");values.Add("1.00=0.000");continue;}
            AssertBillboard(glyph,camera,AnimalEmotion.Exclamation);float alpha=Lines(glyph)[0,0].startColor.a;
            checks.Add("SAMPLE single alert progress="+progress.ToString("F2")+" renderer opacity="+alpha.ToString("F6"));
            Require(alpha<previous,"Alert opacity did not decrease monotonically");previous=alpha;
            if(sample==0)
            {
                Require(alpha==1 && BrightPixels(pixels)>300,"Alert was not fully visible on its first rendered frame");
                Require(Lines(glyph)[0,0].widthMultiplier>.05f && Lines(glyph)[0,1].widthMultiplier>.06f,"Alert lost its thick stroke or large dot");
            }
            if(sample==1)Require(alpha>.8f && alpha<.9f,"Early fade must ease out slowly, not fade linearly");
            // Renderer colors can round to 8-bit channel values; permit one channel step, not a different curve.
            if(sample==2)Require(Mathf.Abs(alpha-.5f)<1f/255+.00001f,"Alert midpoint opacity changed: "+alpha);
            if(sample==3)Require(alpha>.1f && alpha<.2f,"Late fade must ease smoothly toward zero");
            values.Add(progress.ToString("F2")+"="+alpha.ToString("F3"));
        }
        Require(glyph.Play(AnimalEmotion.Exclamation,1),"Custom duration alert rejected");Age(glyph,.5f);camera.Render();
        Require(Mathf.Abs(Lines(glyph)[0,0].startColor.a-.5f)<1f/255+.00001f,"Fade did not use requested duration");
        Require(glyph.Play(AnimalEmotion.Exclamation),"Alert replay rejected");camera.Render();Require(Lines(glyph)[0,0].startColor.a==1,"Replayed alert did not reset to full opacity");
        checks.Add("PASS one alert only, fully visible immediately, thick stem/large dot; smooth nonlinear fade samples "+string.Join(", ",values)+"; custom duration and replay reset work");
    }
    static int BrightPixels(Texture2D pixels)
    {int count=0;foreach(Color32 pixel in pixels.GetPixels32())if(pixel.r>160 && pixel.g>160 && pixel.b>160)count++;return count;}
    static void Read(RenderTexture target,Texture2D pixels)
    {
        RenderTexture previous=RenderTexture.active;RenderTexture.active=target;
        try{pixels.ReadPixels(new Rect(0,0,Tile,Tile),0,0);pixels.Apply();}finally{RenderTexture.active=previous;}
    }
}
