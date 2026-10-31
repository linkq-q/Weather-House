using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEditor;
using UnityEditor.SceneManagement;
using WeatherHouse.StoneEngraving;
using WeatherHouse.Life;
using Object=UnityEngine.Object;

// Isolated review project only. Does not open, save, or mutate the shared main scene.
public static class DemoStoneBuilder
{
    [Serializable] public class Spec { public string id,title; public float width,height,depth,faceWidth,faceHeight,glyphX,artBottom,readX,readY,readWidth,readHeight; public float[] fillUV; public bool glyphReady; }
    [Serializable] public class Specs { public Spec[] items; }
    const string Root="Assets/Experiments/StoneEngraving/DemoWords";
    static string output;
    static Camera camera;
    static Specs specs;
    static int frames;
    static readonly List<string> checks=new List<string>();
    public static void Build()
    {
        try
        {
            if(!Application.isBatchMode || !Application.dataPath.Replace('\\','/').Contains("/DemoWords/UnityProject/"))
                throw new InvalidOperationException("Run only in the isolated DemoWords review project.");
            output=Path.GetFullPath("../Renders");Directory.CreateDirectory(output);
            specs=JsonUtility.FromJson<Specs>(File.ReadAllText("specs.json"));
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            // A plain URP renderer deliberately excludes main-scene gameplay/animal renderer features.
            var data=AssetDatabase.LoadAssetAtPath<UniversalRendererData>("Assets/ReviewRenderer.asset");
            if(!data){data=ScriptableObject.CreateInstance<UniversalRendererData>();AssetDatabase.CreateAsset(data,"Assets/ReviewRenderer.asset");}
            var pipeline=AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>("Assets/ReviewPipeline.asset");
            if(!pipeline){pipeline=UniversalRenderPipelineAsset.Create(data);AssetDatabase.CreateAsset(pipeline,"Assets/ReviewPipeline.asset");}
            pipeline.msaaSampleCount=4;pipeline.renderScale=1;pipeline.shadowDistance=30;
            var pipelineSettings=new SerializedObject(pipeline);
            pipelineSettings.FindProperty("m_MainLightShadowmapResolution").intValue=4096;
            pipelineSettings.ApplyModifiedPropertiesWithoutUndo();pipeline.shadowCascadeCount=4;
            GraphicsSettings.renderPipelineAsset=pipeline;QualitySettings.renderPipeline=pipeline;
            ShaderUtil.allowAsyncCompilation=false;
            RenderSettings.ambientMode=AmbientMode.Flat;RenderSettings.ambientLight=new Color(.48f,.49f,.50f);RenderSettings.fog=false;RenderSettings.skybox=null;
            var sun=new GameObject("Review sunlight").AddComponent<Light>();sun.type=LightType.Directional;sun.intensity=1.2f;sun.shadows=LightShadows.Soft;sun.transform.rotation=Quaternion.Euler(48,-25,0);
            camera=new GameObject("Stone review camera").AddComponent<Camera>();camera.enabled=false;
            camera.backgroundColor=new Color(.33f,.37f,.29f);camera.clearFlags=CameraClearFlags.SolidColor;camera.nearClipPlane=.03f;camera.farClipPlane=100;
            camera.gameObject.AddComponent<UniversalAdditionalCameraData>().renderPostProcessing=false;
            var ground=GameObject.CreatePrimitive(PrimitiveType.Plane);ground.name="Neutral review ground";ground.transform.localScale=Vector3.one*7;
            var groundMat=new Material(Shader.Find("Universal Render Pipeline/Lit"));groundMat.color=new Color(.30f,.34f,.23f);ground.GetComponent<Renderer>().sharedMaterial=groundMat;
            EditorApplication.update+=Tick;
        }
        catch(Exception e){Fail(e);}
    }
    static void Import(string path,bool normal,bool linear)
    {
        var t=(TextureImporter)AssetImporter.GetAtPath(path);t.textureType=normal?TextureImporterType.NormalMap:TextureImporterType.Default;
        t.sRGBTexture=!linear;t.alphaSource=TextureImporterAlphaSource.FromInput;t.alphaIsTransparency=!linear;
        t.textureCompression=TextureImporterCompression.Uncompressed;t.maxTextureSize=2048;t.npotScale=TextureImporterNPOTScale.None;
        t.wrapMode=TextureWrapMode.Clamp;t.mipmapEnabled=true;t.isReadable=!normal&&linear;t.SaveAndReimport();
    }
    static Material Material(string path,Spec s=null)
    {
        var mat=new Material(Shader.Find("Weather House/Stone Engraving/Seven Mineral Paint"));
        mat.SetColor("_BaseColor",new Color(.5566037f,.5566037f,.5566037f));mat.SetFloat("_BumpScale",s==null?.12f:.065f);
        mat.SetTexture("_BumpMap",AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/HIGHLANDS - Stylized Environment/Textures/Forest/StonesRocks_Normal.png"));
        mat.SetTexture("_PaintGrain",AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Experiments/StoneEngraving/TabletStudy/PaintGrain.png"));
        mat.SetFloat("_EngravingStrength",s==null?0:1);mat.SetFloat("_FillStrength",s==null?0:1);mat.SetFloat("_PaintStrength",s==null?0:1);
        mat.SetFloat("_GrooveDepth",.08f);mat.SetFloat("_GrooveShadow",.65f);mat.SetFloat("_CavityTint",.42f);mat.SetFloat("_Smoothness",.12f);
        mat.SetColor("_PaintColor",new Color(.97f,.95f,.89f));
        if(s!=null)
        {
            mat.SetTexture("_EngravingMask",AssetDatabase.LoadAssetAtPath<Texture2D>(Root+"/"+s.id+"/Mask.png"));
            mat.SetTexture("_EngravingNormal",AssetDatabase.LoadAssetAtPath<Texture2D>(Root+"/"+s.id+"/Normal.png"));
            mat.SetTexture("_PaintFill",AssetDatabase.LoadAssetAtPath<Texture2D>(Root+"/"+s.id+"/Illustration.png"));
            mat.SetVector("_ProjectionCenter",new Vector4(0,s.height*.49f,FrontZ(s),0));mat.SetVector("_ProjectionSize",new Vector4(s.faceWidth,s.faceHeight,.22f,0));
            mat.SetVector("_ProjectionRight",Vector3.right);mat.SetVector("_ProjectionUp",Vector3.up);
            mat.SetVector("_FillUV",new Vector4(s.fillUV[0],s.fillUV[1],s.fillUV[2],s.fillUV[3]));
        }
        var old=AssetDatabase.LoadAssetAtPath<Material>(path);
        if(old){EditorUtility.CopySerialized(mat,old);Object.DestroyImmediate(mat);mat=old;EditorUtility.SetDirty(old);}else AssetDatabase.CreateAsset(mat,path);
        return mat;
    }
    static float BaseHeight(Spec s)=>s.id=="sea"?.30f:s.id=="look"?.45f:.55f;
    static float FrontZ(Spec s)=>-s.depth*.5f+(s.id=="past"?.10f:0);
    static Mesh Shape(Spec s,string part)
    {
        bool body=part=="body";
        Vector2[] outline;
        if(body&&s.id=="sea") outline=new[]{new Vector2(-.48f,0),new Vector2(.48f,0),new Vector2(.5f,.06f),new Vector2(.5f,.80f),new Vector2(.34f,.93f),new Vector2(.04f,1),new Vector2(-.31f,.96f),new Vector2(-.5f,.82f),new Vector2(-.5f,.07f)};
        else if(body&&s.id=="look") outline=new[]{new Vector2(-.37f,0),new Vector2(.37f,0),new Vector2(.40f,.10f),new Vector2(.35f,.40f),new Vector2(.50f,.78f),new Vector2(.37f,.94f),new Vector2(0,1),new Vector2(-.37f,.94f),new Vector2(-.50f,.78f),new Vector2(-.35f,.40f),new Vector2(-.40f,.10f)};
        else if(body) outline=new[]{new Vector2(-.45f,0),new Vector2(.45f,0),new Vector2(.49f,.05f),new Vector2(.50f,.58f),new Vector2(.46f,.75f),new Vector2(.34f,.89f),new Vector2(.18f,.975f),new Vector2(0,1),new Vector2(-.18f,.975f),new Vector2(-.34f,.89f),new Vector2(-.46f,.75f),new Vector2(-.50f,.58f),new Vector2(-.49f,.05f)};
        else if(s.id=="past") outline=new[]{new Vector2(-.45f,0),new Vector2(.45f,0),new Vector2(.50f,.18f),new Vector2(.50f,.78f),new Vector2(.44f,1),new Vector2(-.44f,1),new Vector2(-.50f,.78f),new Vector2(-.50f,.18f)};
        else outline=new[]{new Vector2(-.44f,0),new Vector2(.44f,0),new Vector2(.50f,.30f),new Vector2(.48f,.83f),new Vector2(.35f,1),new Vector2(-.40f,.97f),new Vector2(-.50f,.70f),new Vector2(-.50f,.18f)};
        float width=body?s.width:s.width*(s.id=="look"?(part=="foot"?.86f:.72f):(part=="foot"?1.08f:1.01f));
        float height=body?s.height:BaseHeight(s)*(part=="foot"?.55f:.45f);
        float depth=body?s.depth:s.depth*(part=="foot"?1.7f:1.30f);
        float bevel=body?.14f:.035f;
        bool arch=body&&s.id=="past";
        var rings=new Vector3[arch?4:3][];
        for(int r=0;r<rings.Length;r++)
        {
            rings[r]=new Vector3[outline.Length];
            for(int i=0;i<outline.Length;i++)
            {
                float x=outline[i].x*width,y=outline[i].y*height;
                float z;
                if(arch)
                {
                    // A recessed picture face and broad stone lip follow the arch silhouette.
                    float inset=r==0?.28f:r==1?.18f:r==2?0:.06f;
                    x*=1-2*inset/width;y=height*.5f+(y-height*.5f)*(1-2*inset/height);
                    z=r==0?FrontZ(s):r==1?-depth*.5f:r==2?-depth*.5f+.06f:depth*.5f;
                }
                else
                {
                    if(r==0){x*=1-2*bevel/width;y=height*.5f+(y-height*.5f)*(1-2*bevel/height);}
                    if(r==2){x*=.98f;y=height*.5f+(y-height*.5f)*.985f;}
                    z=r==0?-depth*.5f:r==1?-depth*.5f+bevel:depth*.5f;
                }
                rings[r][i]=new Vector3(x,y,z);
            }
        }
        var vs=new List<Vector3>();var ns=new List<Vector3>();var uv=new List<Vector2>();var ts=new List<int>();
        Action<Vector3,Vector3,Vector3,Vector3> tri=(a,b,c,outward)=>{
            var n=Vector3.Cross(b-a,c-a);if(Vector3.Dot(n,outward)<0){var swap=b;b=c;c=swap;n=-n;}
            if(n.sqrMagnitude<1e-10f)throw new Exception("Degenerate mesh triangle");n.Normalize();int k=vs.Count;
            foreach(var v in new[]{a,b,c}){vs.Add(v);ns.Add(n);uv.Add(new Vector2(v.x+v.z*.47f,v.y+v.z*.31f)*.1f);}ts.AddRange(new[]{k,k+1,k+2});
        };
        for(int i=0;i<outline.Length;i++)
        {
            int j=(i+1)%outline.Length;tri(new Vector3(0,height*.5f,arch?FrontZ(s):-depth*.5f),rings[0][i],rings[0][j],Vector3.back);
            int last=rings.Length-1;tri(new Vector3(0,height*.5f,depth*.5f),rings[last][i],rings[last][j],Vector3.forward);
            for(int r=0;r<last;r++){var outward=(rings[r][i]+rings[r][j])*.5f-new Vector3(0,height*.5f,0);outward.z=r<last-1?-depth:0;
                if(arch&&r<last-1)outward=Vector3.back;
                tri(rings[r][i],rings[r+1][i],rings[r+1][j],outward);tri(rings[r][i],rings[r+1][j],rings[r][j],outward);}
        }
        var edges=new Dictionary<(Vector3,Vector3),int>();
        for(int i=0;i<ts.Count;i+=3)for(int j=0;j<3;j++){var a=vs[ts[i+j]];var b=vs[ts[i+(j+1)%3]];var key=edges.ContainsKey((b,a))?(b,a):(a,b);edges[key]=edges.TryGetValue(key,out int n)?n+1:1;}
        if(edges.Values.Any(n=>n!=2))throw new Exception("Open stone mesh");
        var mesh=new Mesh{name=s.id+" "+part};mesh.SetVertices(vs);mesh.SetNormals(ns);mesh.SetUVs(0,uv);mesh.SetTriangles(ts,0);mesh.RecalculateBounds();mesh.RecalculateTangents();
        string path=Root+"/"+s.id+"/"+part+".asset";var old=AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if(old)
        {
            // Use the Mesh API so existing renderer vertex buffers also change in this capture session.
            old.Clear();old.vertices=mesh.vertices;old.normals=mesh.normals;old.uv=mesh.uv;
            old.triangles=mesh.triangles;old.tangents=mesh.tangents;old.bounds=mesh.bounds;old.UploadMeshData(false);
            Object.DestroyImmediate(mesh);mesh=old;EditorUtility.SetDirty(old);
        }
        else AssetDatabase.CreateAsset(mesh,path);
        return mesh;
    }
    static void Tick()
    {
        if(++frames<15||ShaderUtil.anythingCompiling)return;EditorApplication.update-=Tick;
        try
        {
            var plain=Material(Root+"/Stone.mat");
            foreach(var s in specs.items)
            {
                string dir=Root+"/"+s.id;Import(dir+"/Illustration.png",false,false);Import(dir+"/Mask.png",false,true);Import(dir+"/Normal.png",true,true);
                var painted=Material(dir+"/Stone.mat",s);var go=new GameObject(s.title+" · 彩绘石碑");MeshCollider face=null;
                foreach(string part in new[]{"foot","socket","body"})
                {
                    var mesh=Shape(s,part);
                    var piece=new GameObject(part=="body"?"Worked inscription face":part);piece.transform.SetParent(go.transform,false);
                    piece.transform.localPosition=Vector3.up*(part=="body"?BaseHeight(s):part=="socket"?BaseHeight(s)*.55f:0);
                    piece.AddComponent<MeshFilter>().sharedMesh=mesh;piece.AddComponent<MeshRenderer>().sharedMaterial=part=="body"?painted:plain;
                    var collider=piece.AddComponent<MeshCollider>();collider.sharedMesh=mesh;
                    if(part=="body"){face=collider;piece.AddComponent<StoneEngravingReadability>();}
                }
                var readingFace=new GameObject("Reading face - outward").transform;readingFace.SetParent(face.transform,false);
                readingFace.localPosition=new Vector3(s.readX,s.readY,FrontZ(s)-.005f);readingFace.localRotation=Quaternion.Euler(0,180,0);
                var inscription=go.AddComponent<LifeGlyphInscription>();inscription.inscriptionId="stone."+s.id;inscription.glyphIds=new[]{s.id};
                inscription.inscriptionRenderer=face.GetComponent<Renderer>();inscription.readingFace=readingFace;inscription.readingSize=new Vector2(s.readWidth,s.readHeight);
                Physics.SyncTransforms();Validate(go,face,s,painted);
                PrefabUtility.SaveAsPrefabAsset(go,dir+"/Stone.prefab");
                Capture(go,s.id+"-close.png",new Vector3(0,.12f,-1),1600,1200,false);
                Capture(go,s.id+"-oblique.png",new Vector3(-.50f,.34f,-1),1600,1200,false);
                Capture(go,s.id+"-game-angle.png",Quaternion.Euler(43,0,0)*Vector3.back,1600,900,true);
                Object.DestroyImmediate(go);
            }
            AssetDatabase.SaveAssets();File.WriteAllLines(output+"/checks.txt",checks);Debug.Log("DEMO_STONES_OK");EditorApplication.Exit(0);
        }
        catch(Exception e){Fail(e);}
    }
    static void Validate(GameObject go,MeshCollider face,Spec s,Material mat)
    {
        var mask=(Texture2D)mat.GetTexture("_EngravingMask");int samples=0,missing=0;
        for(int y=0;y<128;y++)for(int x=0;x<256;x++)
        {
            float u=(x+.5f)/256,v=(y+.5f)/128;if(mask.GetPixelBilinear(u,v).a<.25f)continue;samples++;
            var local=new Vector3((u-.5f)*s.faceWidth,(v-.5f)*s.faceHeight+s.height*.49f,-s.depth*.5f-1);
            if(!face.Raycast(new Ray(face.transform.TransformPoint(local),Vector3.forward),out var hit,2)||Vector3.Dot(hit.normal,Vector3.back)<.95f)missing++;
        }
        int triangles=go.GetComponentsInChildren<MeshFilter>().Sum(f=>f.sharedMesh.triangles.Length/3);
        if(samples<100||missing!=0||triangles>200||ShaderUtil.ShaderHasError(mat.shader))throw new Exception($"{s.id}: samples={samples}, missing={missing}, triangles={triangles}");
        if(!go.GetComponentInChildren<StoneEngravingReadability>()||go.GetComponentsInChildren<MeshCollider>().Length!=3)throw new Exception("Components missing");
        var inscription=go.GetComponent<LifeGlyphInscription>();
        if(!inscription||inscription.inscriptionId!="stone."+s.id||inscription.glyphIds.Length!=1||inscription.glyphIds[0]!=s.id||inscription.inscriptionRenderer!=face.GetComponent<Renderer>()||Vector3.Dot(inscription.readingFace.forward,Vector3.back)<.999f||inscription.readingSize!=new Vector2(s.readWidth,s.readHeight))throw new Exception("Inscription authoring mismatch");
        for(int y=-1;y<=1;y++)for(int x=-1;x<=1;x++)
        {
            var p=inscription.readingFace.TransformPoint(new Vector3(x*s.readWidth*.42f,y*s.readHeight*.42f,0));
            if(!face.Raycast(new Ray(p+Vector3.back*.1f,Vector3.forward),out _,.2f))throw new Exception("Reading region leaves the actual face");
        }
        checks.Add($"PASS inscription {s.id}: id={inscription.inscriptionId}; canonical glyph={s.id}; front renderer; readingFace +Z outward; readingSize={inscription.readingSize}; runtime shutter integration not exercised here");
        checks.Add($"PASS {s.id}: glyphReady={s.glyphReady}; {samples} carving samples on flat face; missing={missing}; triangles={triangles}; 3 colliders; gameplay readability present; source mineral shader reused");
    }
    static void Capture(GameObject go,string file,Vector3 direction,int width,int height,bool gameplay)
    {
        var rs=go.GetComponentsInChildren<Renderer>();var b=rs[0].bounds;foreach(var r in rs.Skip(1))b.Encapsulate(r.bounds);
        camera.aspect=width/(float)height;camera.orthographic=!gameplay;
        camera.transform.position=b.center+direction.normalized*(gameplay?18:20);camera.transform.LookAt(b.center);
        camera.fieldOfView=35;camera.orthographicSize=Mathf.Max(b.size.y*.62f,b.size.x/camera.aspect*.60f);
        var session=new GameObject("Render-only gameplay context").AddComponent<WeatherHousePrototype.GameplaySession>();
        session.PlayerViewCamera=gameplay?camera:null;
        var target=RenderTexture.GetTemporary(width,height,24,RenderTextureFormat.ARGB32);var previous=RenderTexture.active;var img=new Texture2D(width,height,TextureFormat.RGB24,false);
        try
        {
            // Warm the material/render pipeline once before readback; the first submission can precede its constant-buffer upload.
            RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest{destination=target});
            RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest{destination=target});
            RenderTexture.active=target;img.ReadPixels(new Rect(0,0,width,height),0,0);img.Apply();File.WriteAllBytes(output+"/"+file,img.EncodeToPNG());
        }
        finally{RenderTexture.active=previous;Object.DestroyImmediate(img);RenderTexture.ReleaseTemporary(target);Object.DestroyImmediate(session.gameObject);}
    }
    static void Fail(Exception e){Debug.LogException(e);if(output!=null)File.WriteAllText(output+"/error.txt",e.ToString());EditorApplication.Exit(1);}
}
