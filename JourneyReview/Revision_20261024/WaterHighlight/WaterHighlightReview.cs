using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using WeatherHouse.Vegetation;
using Object = UnityEngine.Object;

public static class WaterHighlightReview
{
    const string Output = "C:/unity project/Weather House/JourneyReview/Revision_20261024/WaterHighlight/GpuReview";
    [Serializable] class Difference { public string name; public float max, mean; public int changed; }
    [Serializable] class Result { public string gpu, evidence; public int atlasWidth; public float maxProductionLod; public List<Difference> tests = new List<Difference>(); public int errors; }
    static Result result;
    static readonly List<string> errors = new List<string>();
    public static void Run()
    {
        try
        {
            Directory.CreateDirectory(Output);
            Application.logMessageReceived += Log;
            result = new Result { gpu = SystemInfo.graphicsDeviceName, evidence = "Isolated Unity Editor URP Camera.Render GPU readback; copied actual Waterside material and atlas; fixed water time; not main-scene Play." };
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var pipeline=AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>("Assets/Settings/URP-Balanced.asset");
            Require(pipeline, "URP pipeline missing");
            GraphicsSettings.defaultRenderPipeline=pipeline;
            QualitySettings.renderPipeline=pipeline;
            RenderSettings.ambientMode=AmbientMode.Flat; RenderSettings.ambientLight=new Color(.58f,.68f,.70f);
            RenderSettings.fog=false;
            var sun=new GameObject("Sun").AddComponent<Light>(); sun.type=LightType.Directional;
            sun.transform.rotation=Quaternion.Euler(38, -20, 0); sun.intensity=1; sun.shadows=LightShadows.Soft;
            RenderSettings.sun=sun;
            var camera=new GameObject("Review Camera").AddComponent<Camera>(); camera.enabled=false;
            camera.clearFlags=CameraClearFlags.SolidColor; camera.backgroundColor=new Color(.65f,.78f,.82f);
            camera.nearClipPlane=.05f; camera.farClipPlane=100; camera.fieldOfView=48;
            camera.allowHDR=true; camera.allowMSAA=false;
            var data=camera.GetUniversalAdditionalCameraData(); data.requiresColorOption=CameraOverrideOption.On;
            data.requiresDepthOption=CameraOverrideOption.On; data.renderPostProcessing=false;
            var water=GameObject.CreatePrimitive(PrimitiveType.Plane); water.name="Water"; water.layer=4;
            var renderer=water.GetComponent<Renderer>();
            var saved=AssetDatabase.LoadAssetAtPath<Material>("Assets/Game/World/WatersideV2/Generated/Nature/Water.mat");
            Require(saved && saved.GetTexture("_WaterBrushAtlas"),"Actual material/atlas missing");
            result.atlasWidth=saved.GetTexture("_WaterBrushAtlas").width;
            result.maxProductionLod=Mathf.Max(0,Mathf.Log(result.atlasWidth*.5f,2)-4);
            var materials = new [] { new Material(saved), new Material(saved) };
            for(int v=0;v<2;v++)
            {
                materials[v].shader=Shader.Find("Review/Water"+(v==0?"Before":"After"));
                Require(materials[v].shader,"Review shader missing");
                materials[v].SetFloat("_ReviewTime",0);
                CheckShader(materials[v].shader);
            }
            camera.orthographic=true; camera.orthographicSize=5; camera.aspect=1;
            camera.transform.SetPositionAndRotation(new Vector3(0,10,0),Quaternion.Euler(90,0,0));
            for(int v=0;v<2;v++)
            {
                var m=materials[v]; renderer.sharedMaterial=m; string label=v==0?"Before":"After";
                m.SetFloat("_ReviewMode",1);
                for(int lod=1;lod+.0001f<result.maxProductionLod;lod++)
                {
                    m.SetFloat("_ReviewLod",lod-.0001f); var a=Render(camera,512,512);
                    m.SetFloat("_ReviewLod",lod+.0001f); var b=Render(camera,512,512);
                    result.tests.Add(Compare(label+" atlas LOD "+lod,a,b));
                    if(lod==4) { Save(a,label+"_lod_left"); Save(b,label+"_lod_right"); }
                    Object.DestroyImmediate(a); Object.DestroyImmediate(b);
                }
                m.SetFloat("_ReviewMode",2);
                for(int level=1;level<8;level++)
                {
                    float threshold=(level+.5f)/8;
                    m.SetFloat("_ReviewLuminance",(threshold-.0001f)*(threshold-.0001f)); var a=Render(camera,64,64);
                    m.SetFloat("_ReviewLuminance",(threshold+.0001f)*(threshold+.0001f)); var b=Render(camera,64,64);
                    result.tests.Add(Compare(label+" pigment threshold "+level,a,b));
                    Object.DestroyImmediate(a); Object.DestroyImmediate(b);
                }
                m.SetFloat("_ReviewMode",0);
            }
            water.transform.localScale=Vector3.one*3;
            var reflection=water.AddComponent<PainterlyWaterReflection>(); reflection.resolution=1024; reflection.resolutionScale=.5f;
            Box("Bed", new Vector3(0,-1.1f,0),new Vector3(45,.2f,45),new Color(.32f,.45f,.36f));
            for(int i=0;i<11;i++)
            {
                float x=(i-5)*2.1f;
                Box("Tree "+i,new Vector3(x,3.2f,11), new Vector3(.35f,6.4f,.4f),new Color(.16f,.22f,.18f));
                var crown=GameObject.CreatePrimitive(PrimitiveType.Sphere); crown.transform.position=new Vector3(x,5.5f,11);
                crown.transform.localScale=new Vector3(2.5f,3.8f,2.5f); crown.GetComponent<Renderer>().sharedMaterial=Lit(new Color(.15f+i*.012f,.35f,.21f));
            }
            Box("Cream landmark",new Vector3(0,2,13),new Vector3(2.5f,4,2),new Color(.94f,.90f,.65f));
            camera.orthographic=false; camera.aspect=1.6f;
            for(int v=0;v<2;v++)
            {
                string label=v==0?"Before":"After"; string folder=Path.Combine(Output,label); Directory.CreateDirectory(folder);
                renderer.sharedMaterial=materials[v];
                var samples = new List<string>();
                for(int frame=0;frame<65;frame++)
                {
                    float f=frame/64f;
                    camera.transform.position=new Vector3(Mathf.Lerp(-.8f,.8f,f),14,-12);
                    camera.transform.LookAt(new Vector3(Mathf.Lerp(-.3f,.3f,f),0,2));
                    var image=Render(camera,960,600);
                    File.WriteAllBytes(Path.Combine(folder,frame.ToString("D3")+".png"),image.EncodeToPNG());
                    var pixels=image.GetPixels();
                    var values=new List<string>{frame.ToString()};
                    for(int z=-2;z<=7;z++) for(int x=-7;x<=7;x++)
                    {
                        var p=camera.WorldToViewportPoint(new Vector3(x*.6f,0,z*.6f));
                        Color c=image.GetPixelBilinear(p.x,p.y);
                        values.Add((c.r*.2126f+c.g*.7152f+c.b*.0722f).ToString("R",System.Globalization.CultureInfo.InvariantCulture));
                    }
                    samples.Add(string.Join(",",values));
                    Object.DestroyImmediate(image);
                    var block=new MaterialPropertyBlock(); renderer.GetPropertyBlock(block);
                    Require(block.GetFloat("_HasPlanarReflection")>.5f && block.GetFloat("_HasSceneColor")>.5f,"Actual reflection/scene color not bound");
                }
                File.WriteAllLines(Path.Combine(folder,"world-samples.csv"),samples);
                string zoom=Path.Combine(folder,"Zoom"); Directory.CreateDirectory(zoom);
                for(int frame=0;frame<65;frame++)
                {
                    float distance=Mathf.Lerp(9,38,frame/64f);
                    camera.transform.position=new Vector3(.2f,distance,-distance*.85f);
                    camera.transform.LookAt(new Vector3(0,0,2));
                    var image=Render(camera,960,600);
                    File.WriteAllBytes(Path.Combine(zoom,frame.ToString("D3")+".png"),image.EncodeToPNG());
                    Object.DestroyImmediate(image);
                }
                CheckShader(materials[v].shader);
            }
            // A stationary-camera repeat separates water time from view-dependent pigment changes.
            renderer.sharedMaterial=materials[1]; camera.transform.position=new Vector3(.2f,14,-12);
            camera.transform.LookAt(new Vector3(0,0,2));
            var stillA=Render(camera,960,600); var stillB=Render(camera,960,600);
            var repeat=Compare("After fixed camera and fixed time",stillA,stillB); result.tests.Add(repeat);
            Require(repeat.max<.001f,"Frozen capture itself is unstable");
            Object.DestroyImmediate(stillB);
            string flow=Path.Combine(Output,"Flow"); Directory.CreateDirectory(flow);
            for(int frame=0;frame<=32;frame++)
            {
                materials[1].SetFloat("_ReviewTime",frame*.1f);
                var image=Render(camera,960,600);
                File.WriteAllBytes(Path.Combine(flow,frame.ToString("D3")+".png"),image.EncodeToPNG());
                if(frame==32) { var motion=Compare("After real water time 0 to 3.2s",stillA,image); result.tests.Add(motion); Require(motion.changed>50,"Water animation was lost"); }
                Object.DestroyImmediate(image);
            }
            Object.DestroyImmediate(stillA);
            result.errors=errors.Count;
            File.WriteAllText(Path.Combine(Output,"result.json"),JsonUtility.ToJson(result,true));
            Require(errors.Count==0,string.Join("\n",errors));
            foreach(var t in result.tests.Where(t=>t.name.StartsWith("After pigment") || t.name.StartsWith("After atlas")))
                Require(t.max<.005f,"Discontinuous production helper: "+t.name+" = "+t.max);
            Require(result.tests.Any(t=>t.name.StartsWith("Before pigment") && t.max>.01f),"Pigment RED was not reproduced");
            Require(result.tests.Any(t=>t.name.StartsWith("Before atlas") && t.max>.01f),"Atlas RED was not reproduced in valid production LOD range");
            File.WriteAllText(Path.Combine(Output,"status.txt"),"PASS actual GPU RED -> GREEN continuity, valid production LOD range, shader compile, fixed-time repeat, water time remains active. Main-scene Play still requires review.");
            EditorApplication.Exit(0);
        }
        catch(Exception e) { File.WriteAllText(Path.Combine(Output,"status.txt"),"FAIL\n"+e+"\n"+string.Join("\n",errors)); EditorApplication.Exit(1); }
    }
    static void Box(string name,Vector3 position,Vector3 scale,Color color)
    { var go=GameObject.CreatePrimitive(PrimitiveType.Cube); go.name=name; go.transform.position=position; go.transform.localScale=scale; go.GetComponent<Renderer>().sharedMaterial=Lit(color); }
    static Material Lit(Color color) { var m=new Material(Shader.Find("Universal Render Pipeline/Lit")); m.color=color; return m; }
    static void Log(string message,string stack,LogType type) { if(type==LogType.Error || type==LogType.Exception || type==LogType.Assert) errors.Add(message); }
    static void CheckShader(Shader shader) { Require(!ShaderUtil.ShaderHasError(shader),"Shader compile errors: "+string.Join("; ",ShaderUtil.GetShaderMessages(shader).Select(m=>m.message))); }
    static void Require(bool condition,string message) { if(!condition)throw new InvalidOperationException(message); }
    static void Save(Texture2D image,string name) { File.WriteAllBytes(Path.Combine(Output,name+".png"),image.EncodeToPNG()); }
    static Texture2D Render(Camera camera,int width,int height)
    {
        var rt=RenderTexture.GetTemporary(width,height,24,RenderTextureFormat.ARGBHalf,RenderTextureReadWrite.Linear);
        var prior=camera.targetTexture; var active=RenderTexture.active;
        try { camera.targetTexture=rt; camera.Render(); RenderTexture.active=rt;
            var image=new Texture2D(width,height,TextureFormat.RGBAFloat,false,true); image.ReadPixels(new Rect(0,0,width,height),0,0); image.Apply(); return image; }
        finally { camera.targetTexture=prior; RenderTexture.active=active; RenderTexture.ReleaseTemporary(rt); }
    }
    static Difference Compare(string name,Texture2D a,Texture2D b)
    {
        var left=a.GetPixels(); var right=b.GetPixels(); var result=new Difference { name=name };
        for(int i=0;i<left.Length;i++) { float d=Mathf.Max(Mathf.Abs(left[i].r-right[i].r),Mathf.Abs(left[i].g-right[i].g),Mathf.Abs(left[i].b-right[i].b)); result.max=Mathf.Max(result.max,d); result.mean+=d/left.Length; if(d>.01f)result.changed++; }
        return result;
    }
}
