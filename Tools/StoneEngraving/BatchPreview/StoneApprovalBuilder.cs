using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object=UnityEngine.Object;

// Approval renders only, executed by the isolated Unity project. No main-project assets are saved.
public static class StoneApprovalBuilder
{
    [Serializable] public class Spec { public string output; public string[] candidates; public Item[] items; }
    [Serializable] public class Item { public string id,title,texture; public float aspect,width_m; }
    [Serializable] public class Fit
    {
        public string path,mesh; public int triangles; public Quaternion rotation;
        public Vector3 center; public Vector2 size; public float score,minNormal,coverage;
    }
    [Serializable] public class Result { public string id,title; public Fit fit; public float width_m; public string status; }
    [Serializable] public class Report { public Result[] selected; public Fit[] candidates; }
    const int N=56;
    static Spec spec; static Camera camera; static int frames;
    static readonly List<Fit> inventory=new List<Fit>();
    static readonly List<Result> chosen=new List<Result>();

    public static void Run()
    {
        if(!Application.isBatchMode)throw new InvalidOperationException("Isolated batch project only.");
        try
        {
            spec=JsonUtility.FromJson<Spec>(File.ReadAllText("stone-approval.json"));
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            var pipeline=AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>("Assets/Settings/URP-HighFidelity.asset");
            GraphicsSettings.renderPipelineAsset=pipeline;QualitySettings.renderPipeline=pipeline;
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            RenderSettings.ambientMode=AmbientMode.Flat;RenderSettings.ambientLight=new Color(.48f,.49f,.50f);
            RenderSettings.fog=false;RenderSettings.skybox=null;
            var light=new GameObject("Review light").AddComponent<Light>();light.type=LightType.Directional;
            light.intensity=1.2f;light.transform.rotation=Quaternion.Euler(48,-25,0);
            camera=new GameObject("Approval camera").AddComponent<Camera>();camera.backgroundColor=new Color(.77f,.78f,.75f);
            camera.clearFlags=CameraClearFlags.SolidColor;camera.nearClipPlane=.03f;camera.farClipPlane=200;
            camera.orthographic=true;camera.gameObject.AddComponent<UniversalAdditionalCameraData>().renderPostProcessing=false;
            EditorApplication.update+=Tick;
        }
        catch(Exception e){Fail(e);}
    }

    static Mesh MeshAt(string path) => AssetDatabase.LoadAllAssetsAtPath(path).OfType<Mesh>().OrderByDescending(m=>m.vertexCount).First();

    static void Tick()
    {
        if(++frames<12||ShaderUtil.anythingCompiling)return;
        EditorApplication.update-=Tick;
        try
        {
            foreach(string path in spec.candidates)
            {
                var mesh=MeshAt(path);
                if(mesh.triangles.Length/3>12000)continue;
                var generic=FindFit(path,mesh,1.4f);
                inventory.Add(generic);
                Debug.Log("CANDIDATE "+path+" score="+generic.score);
            }
            File.WriteAllText(spec.output+"/candidate-fit.json",JsonUtility.ToJson(new Report{candidates=inventory.ToArray()},true));
            var used=new HashSet<string>();
            foreach(var item in spec.items.OrderByDescending(x=>x.aspect))
            {
                var fits=inventory.OrderByDescending(x=>x.score).Take(10)
                    .Select(x=>FindFit(x.path,MeshAt(x.path),item.aspect)).OrderByDescending(x=>x.score).ToList();
                // Prefer a different suitable silhouette; never trade away more than 25% of usable face area.
                var best=fits.First(); var fit=fits.FirstOrDefault(x=>!used.Contains(x.path)&&x.score>=best.score*.75f)??best;
                used.Add(fit.path);
                if(fit.size.x<=0)throw new InvalidOperationException("No continuous carving face for "+item.id);
                Render(item,fit);
                chosen.Add(new Result{id=item.id,title=item.title,fit=fit,width_m=item.width_m,status="PREVIEW_ONLY_AWAITING_USER"});
            }
            File.WriteAllText(spec.output+"/selection.json",JsonUtility.ToJson(new Report{selected=chosen.ToArray(),candidates=inventory.ToArray()},true));
            Debug.Log("STONE_APPROVAL_PREVIEW_OK");EditorApplication.Exit(0);
        }
        catch(Exception e){Fail(e);}
    }

    static Fit FindFit(string path,Mesh mesh,float aspect)
    {
        var vertices=mesh.vertices;var indices=mesh.triangles;
        var normals=new List<(Vector3 normal,float area)>();
        for(int i=0;i<indices.Length;i+=3)
        {
            var cross=Vector3.Cross(vertices[indices[i+1]]-vertices[indices[i]],vertices[indices[i+2]]-vertices[indices[i]]);
            if(cross.sqrMagnitude<1e-20f)continue;
            var n=cross.normalized; int j=normals.FindIndex(x=>Vector3.Dot(x.normal,n)>.985f);
            if(j<0)normals.Add((n,cross.magnitude));else normals[j]=(normals[j].normal,normals[j].area+cross.magnitude);
        }
        var go=new GameObject("Fit candidate");go.AddComponent<MeshFilter>().sharedMesh=mesh;
        float fitScale=4f/mesh.bounds.size.magnitude;
        go.transform.localScale=Vector3.one*fitScale;
        var renderer=go.AddComponent<MeshRenderer>();var collider=go.AddComponent<MeshCollider>();collider.sharedMesh=mesh;
        var best=new Fit{path=path,mesh=mesh.name,triangles=indices.Length/3};
        try
        {
            foreach(var plane in normals.OrderByDescending(x=>x.area).Take(10))for(int turn=0;turn<4;turn++)
            {
                go.transform.rotation=Quaternion.Euler(0,turn*45,0)*Quaternion.FromToRotation(plane.normal,Vector3.up);
                Physics.SyncTransforms();var b=renderer.bounds;
                float sx=b.size.x/N,sz=b.size.z/N;
                var good=new bool[N,N];var heights=new float[N,N];
                for(int z=0;z<N;z++)for(int x=0;x<N;x++)
                {
                    var origin=new Vector3(b.min.x+(x+.5f)*sx,b.max.y+1,b.min.z+(z+.5f)*sz);
                    if(collider.Raycast(new Ray(origin,Vector3.down),out var hit,b.size.y+2))
                    {good[x,z]=Vector3.Dot(hit.normal,Vector3.up)>.80f;heights[x,z]=hit.point.y;}
                }
                var bad=new int[N+1,N+1];
                for(int z=0;z<N;z++)for(int x=0;x<N;x++)
                {
                    bool safe=x>0&&z>0&&x<N-1&&z<N-1&&good[x,z]&&good[x-1,z]&&good[x+1,z]&&good[x,z-1]&&good[x,z+1];
                    bad[x+1,z+1]=(safe?0:1)+bad[x,z+1]+bad[x+1,z]-bad[x,z];
                }
                bool found=false;
                for(int w=N-2;w>=8&&!found;w--)
                {
                    int h=Mathf.RoundToInt(w*sx/(aspect*sz));if(h<4||h>N-2)continue;
                    for(int z=1;z+h<N;z++)for(int x=1;x+w<N;x++)
                    {
                        if(bad[x+w,z+h]-bad[x,z+h]-bad[x+w,z]+bad[x,z]!=0)continue;
                        float area=w*sx*h*sz;
                        float score=area/(b.size.x*b.size.z)*Mathf.Sqrt(area)/(b.size.magnitude);
                        float mismatch=Mathf.Abs(Mathf.Log(b.size.x/b.size.z/aspect));
                        var center=new Vector3(b.min.x+(x+w*.5f)*sx,heights[x+w/2,z+h/2],b.min.z+(z+h*.5f)*sz);
                        float offset=new Vector2((center.x-b.center.x)/b.size.x,(center.z-b.center.z)/b.size.z).sqrMagnitude;
                        score*=Mathf.Exp(-2*mismatch)*(1-.5f*offset);
                        if(score>best.score)
                        {
                            best.score=score;best.rotation=go.transform.rotation;best.size=new Vector2(w*sx*.97f,h*sz*.97f);
                            best.center=center;
                            best.coverage=1;best.minNormal=.8f;
                        }
                        found=true;
                    }
                }
            }
        }
        finally {Object.DestroyImmediate(go);}
        best.center/=fitScale;best.size/=fitScale;
        return best;
    }

    static void Render(Item item,Fit fit)
    {
        string folder=spec.output+"/"+item.id;Directory.CreateDirectory(folder);
        var importer=(TextureImporter)AssetImporter.GetAtPath(item.texture);importer.sRGBTexture=false;
        importer.textureCompression=TextureImporterCompression.Uncompressed;importer.mipmapEnabled=true;
        importer.isReadable=true;
        importer.wrapMode=TextureWrapMode.Clamp;importer.maxTextureSize=2048;importer.npotScale=TextureImporterNPOTScale.None;importer.SaveAndReimport();
        ValidateCoverage(item,fit,folder);
        var mesh=MeshAt(fit.path);var go=new GameObject(item.id);go.AddComponent<MeshFilter>().sharedMesh=mesh;
        var renderer=go.AddComponent<MeshRenderer>();var mat=new Material(Shader.Find("Hidden/WeatherHouse/StoneApproval"));
        renderer.sharedMaterial=mat;
        var inv=Quaternion.Inverse(fit.rotation);
        mat.SetVector("_Center",inv*fit.center);mat.SetVector("_Right",inv*Vector3.right);mat.SetVector("_Up",inv*Vector3.forward);
        mat.SetVector("_Size",new Vector4(fit.size.x,fit.size.y,1,0));
        mat.SetTexture("_Coverage",AssetDatabase.LoadAssetAtPath<Texture2D>(item.texture));
        var tilt=Quaternion.Euler(-35,0,0);go.transform.rotation=tilt*fit.rotation;
        float scale=item.width_m/renderer.bounds.size.x;go.transform.localScale=Vector3.one*scale;
        go.transform.position-=new Vector3(renderer.bounds.center.x,renderer.bounds.min.y,renderer.bounds.center.z);
        var bounds=renderer.bounds;
        var face=go.transform.TransformPoint(inv*fit.center);
        try
        {
            Capture(folder+"/01-overlay.png",go,mesh,bounds,tilt*Vector3.up,1280,960);
            Capture(folder+"/02-oblique.png",go,mesh,bounds,new Vector3(-.35f,.85f,-.7f),1280,960);
            Capture(folder+"/03-gameplay-thumbnail.png",go,mesh,bounds,new Vector3(0,1.6f,-1),192,144);
            mat.SetTexture("_Coverage",Texture2D.blackTexture);
            Capture(folder+"/00-blank-stone.png",go,mesh,bounds,tilt*Vector3.up,1280,960);
            if(ShaderUtil.ShaderHasError(mat.shader))throw new InvalidOperationException("Preview shader error.");
        }
        finally{Object.DestroyImmediate(go);Object.DestroyImmediate(mat);}
    }

    static void ValidateCoverage(Item item,Fit fit,string folder)
    {
        var mesh=MeshAt(fit.path);var go=new GameObject("Coverage check");
        float scale=4/mesh.bounds.size.magnitude;
        go.transform.SetPositionAndRotation(Vector3.zero,fit.rotation);go.transform.localScale=Vector3.one*scale;
        var collider=go.AddComponent<MeshCollider>();collider.sharedMesh=mesh;Physics.SyncTransforms();
        var texture=AssetDatabase.LoadAssetAtPath<Texture2D>(item.texture);
        int strokes=0,missing=0,steep=0;
        try
        {
            int height=Mathf.CeilToInt(512/item.aspect);
            for(int y=0;y<height;y++)for(int x=0;x<512;x++)
            {
                float u=(x+.5f)/512,v=(y+.5f)/height;
                if(texture.GetPixelBilinear(u,v).r<.2f)continue;
                strokes++;
                var ray=new Ray(new Vector3((fit.center.x+(u-.5f)*fit.size.x)*scale,
                    collider.bounds.max.y+1,(fit.center.z+(v-.5f)*fit.size.y)*scale),Vector3.down);
                if(!collider.Raycast(ray,out var hit,collider.bounds.size.y+2))missing++;
                else if(Vector3.Dot(hit.normal,Vector3.up)<.75f)steep++;
            }
            File.WriteAllText(folder+"/coverage-check.txt",$"stroke_samples={strokes}\nmissing_surface={missing}\nsteep_surface={steep}\nAll canonical glyphs included in the same coverage test.");
            if(strokes<100||missing>0||steep>0)throw new InvalidOperationException("Clipped or steep carving content: "+item.id);
        }
        finally{Object.DestroyImmediate(go);}
    }

    static void Capture(string path,GameObject stone,Mesh mesh,Bounds bounds,Vector3 direction,int width,int height)
    {
        camera.transform.position=bounds.center+direction.normalized*20;camera.transform.LookAt(bounds.center);
        camera.aspect=width/(float)height;
        float maxX=0,maxY=0;
        foreach(var vertex in mesh.vertices)
        {
            var p=camera.transform.InverseTransformPoint(stone.transform.TransformPoint(vertex));
            maxX=Mathf.Max(maxX,Mathf.Abs(p.x));maxY=Mathf.Max(maxY,Mathf.Abs(p.y));
        }
        camera.orthographicSize=Mathf.Max(maxY,maxX/camera.aspect)*1.12f;
        var target=RenderTexture.GetTemporary(width,height,24,RenderTextureFormat.ARGB32);
        var prior=RenderTexture.active;var image=new Texture2D(width,height,TextureFormat.RGB24,false);
        try
        {
            RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest{destination=target});
            RenderTexture.active=target;image.ReadPixels(new Rect(0,0,width,height),0,0);image.Apply();
            File.WriteAllBytes(path,image.EncodeToPNG());
        }
        finally {RenderTexture.active=prior;Object.DestroyImmediate(image);RenderTexture.ReleaseTemporary(target);}
    }
    static void Fail(Exception e){Debug.LogException(e);if(spec!=null)File.WriteAllText(spec.output+"/error.txt",e.ToString());EditorApplication.Exit(1);}
}
