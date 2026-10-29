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

// Runs exclusively in the cached isolated review project, using the approved mesh/fit verbatim.
public static class StoneProductionBuilder
{
    [Serializable] public class Fit {public string path,mesh;public Quaternion rotation;public Vector3 center;public Vector2 size;}
    [Serializable] public class Item {public string id,title;public Fit fit;public float width_m;}
    [Serializable] public class Selection {public Item[] selected;}
    [Serializable] public class Dimension {public string id;public float scale,width,height;}
    [Serializable] public class Dimensions {public List<Dimension> items=new List<Dimension>();}
    const string AssetsRoot="Assets/Experiments/StoneEngraving/Seven";
    static string output; static Camera camera; static Selection selection; static int frames;
    // Approved preview angle; the user adjusts final placement angles in the main scene.
    static readonly Quaternion Tilt=Quaternion.Euler(-35,0,0);
    static Mesh MeshAt(Fit fit)=>AssetDatabase.LoadAllAssetsAtPath(fit.path).OfType<Mesh>().Single(x=>x.name==fit.mesh);
    static void Load()
    {
        if(!Application.isBatchMode)throw new InvalidOperationException("Isolated batch project only.");
        output=Path.GetFullPath("../SevenStoneProduction");Directory.CreateDirectory(output);
        selection=JsonUtility.FromJson<Selection>(File.ReadAllText("approved-selection.json"));
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
    }
    static GameObject Stone(Item item,out MeshRenderer renderer)
    {
        var go=new GameObject(item.title+" · 石刻");var mesh=MeshAt(item.fit);
        go.AddComponent<MeshFilter>().sharedMesh=mesh;renderer=go.AddComponent<MeshRenderer>();
        go.AddComponent<MeshCollider>().sharedMesh=mesh;
        go.transform.rotation=Tilt*item.fit.rotation;
        go.transform.localScale=Vector3.one*(item.width_m/renderer.bounds.size.x);
        go.transform.position-=new Vector3(renderer.bounds.center.x,renderer.bounds.min.y,renderer.bounds.center.z);
        return go;
    }
    public static void Measure()
    {
        try
        {
            Load();var result=new Dimensions();
            foreach(var item in selection.selected)
            {
                var go=Stone(item,out var r);float scale=go.transform.localScale.x;
                result.items.Add(new Dimension{id=item.id,scale=scale,width=item.fit.size.x*scale,height=item.fit.size.y*scale});
                Object.DestroyImmediate(go);
            }
            File.WriteAllText(output+"/dimensions.json",JsonUtility.ToJson(result,true));EditorApplication.Exit(0);
        }
        catch(Exception e){Fail(e);}
    }
    public static void Build()
    {
        try
        {
            Load();EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            var pipeline=AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>("Assets/Settings/URP-HighFidelity.asset");
            GraphicsSettings.renderPipelineAsset=pipeline;QualitySettings.renderPipeline=pipeline;
            RenderSettings.ambientMode=AmbientMode.Flat;RenderSettings.ambientLight=new Color(.48f,.49f,.50f);
            RenderSettings.fog=false;RenderSettings.skybox=null;
            var light=new GameObject("Review light").AddComponent<Light>();light.type=LightType.Directional;
            light.intensity=1.2f;light.transform.rotation=Quaternion.Euler(48,-25,0);
            camera=new GameObject("Review camera").AddComponent<Camera>();camera.backgroundColor=new Color(.77f,.78f,.75f);
            camera.clearFlags=CameraClearFlags.SolidColor;camera.nearClipPlane=.03f;camera.farClipPlane=200;
            camera.orthographic=true;camera.gameObject.AddComponent<UniversalAdditionalCameraData>().renderPostProcessing=false;
            EditorApplication.update+=Tick;
        }
        catch(Exception e){Fail(e);}
    }
    static void Tick()
    {
        if(++frames<12||ShaderUtil.anythingCompiling)return;EditorApplication.update-=Tick;
        try
        {
            var checks=new List<string>();
            foreach(var item in selection.selected)
            {
                string dir=AssetsRoot+"/"+item.id;string folder=output+"/"+item.id;Directory.CreateDirectory(folder);
                foreach(string kind in new[]{"Normal","Mask","Height"})
                {
                    var importer=(TextureImporter)AssetImporter.GetAtPath(dir+"/"+kind+".png");
                    importer.textureType=kind=="Normal"?TextureImporterType.NormalMap:TextureImporterType.Default;
                    importer.sRGBTexture=false;importer.textureCompression=TextureImporterCompression.Uncompressed;
                    importer.mipmapEnabled=true;importer.isReadable=kind=="Mask";
                    importer.wrapMode=TextureWrapMode.Clamp;importer.maxTextureSize=2048;
                    importer.npotScale=TextureImporterNPOTScale.None;importer.SaveAndReimport();
                }
                var go=Stone(item,out var renderer);var inv=Quaternion.Inverse(item.fit.rotation);
                var mat=new Material(Shader.Find("Weather House/Stone Engraving"));
                mat.SetColor("_BaseColor",new Color(.48f,.49f,.46f,1));mat.SetFloat("_Faceted",1);
                mat.SetFloat("_GrooveDepth",.08f);mat.SetFloat("_CavityTint",.42f);
                mat.SetFloat("_BumpScale",0);mat.SetFloat("_Smoothness",.12f);
                mat.SetVector("_ProjectionCenter",inv*item.fit.center);mat.SetVector("_ProjectionRight",inv*Vector3.right);
                mat.SetVector("_ProjectionUp",inv*Vector3.forward);
                mat.SetVector("_ProjectionSize",new Vector4(item.fit.size.x,item.fit.size.y,MeshAt(item.fit).bounds.size.magnitude,0));
                mat.SetTexture("_EngravingNormal",AssetDatabase.LoadAssetAtPath<Texture2D>(dir+"/Normal.png"));
                var mask=AssetDatabase.LoadAssetAtPath<Texture2D>(dir+"/Mask.png");mat.SetTexture("_EngravingMask",mask);
                var old=AssetDatabase.LoadAssetAtPath<Material>(dir+"/Stone.mat");
                if(old){EditorUtility.CopySerialized(mat,old);Object.DestroyImmediate(mat);mat=old;EditorUtility.SetDirty(mat);}
                else AssetDatabase.CreateAsset(mat,dir+"/Stone.mat");renderer.sharedMaterial=mat;
                ValidateCoverage(item,mask,checks);
                Capture(folder+"/01-close.png",go,Tilt*Vector3.up,1280,960);
                Capture(folder+"/02-oblique.png",go,new Vector3(-.35f,.85f,-.7f),1280,960);
                Capture(folder+"/03-photo-thumbnail.png",go,new Vector3(0,1.6f,-1),192,144);
                mat.SetFloat("_GameplayReadability",1);
                Capture(folder+"/04-gameplay-thumbnail.png",go,new Vector3(0,1.6f,-1),192,144);
                mat.SetFloat("_GameplayReadability",0);
                if(ShaderUtil.ShaderHasError(mat.shader))throw new InvalidOperationException("Shader error.");
                PrefabUtility.SaveAsPrefabAsset(go,dir+"/Stone.prefab");Object.DestroyImmediate(go);
            }
            AssetDatabase.SaveAssets();File.WriteAllLines(output+"/unity-checks.txt",checks);
            Debug.Log("SEVEN_STONE_PRODUCTION_OK");EditorApplication.Exit(0);
        }
        catch(Exception e){Fail(e);}
    }
    static void ValidateCoverage(Item item,Texture2D texture,List<string> checks)
    {
        var go=new GameObject("Coverage check");var mesh=MeshAt(item.fit);float scale=4/mesh.bounds.size.magnitude;
        go.transform.SetPositionAndRotation(Vector3.zero,item.fit.rotation);go.transform.localScale=Vector3.one*scale;
        var collider=go.AddComponent<MeshCollider>();collider.sharedMesh=mesh;Physics.SyncTransforms();
        int samples=0,missing=0,steep=0;int height=Mathf.CeilToInt(512f*texture.height/texture.width);
        try
        {
            for(int y=0;y<height;y++)for(int x=0;x<512;x++)
            {
                float u=(x+.5f)/512,v=(y+.5f)/height;if(texture.GetPixelBilinear(u,v).a<.2f)continue;samples++;
                var ray=new Ray(new Vector3((item.fit.center.x+(u-.5f)*item.fit.size.x)*scale,
                    collider.bounds.max.y+1,(item.fit.center.z+(v-.5f)*item.fit.size.y)*scale),Vector3.down);
                if(!collider.Raycast(ray,out var hit,collider.bounds.size.y+2))missing++;
                else if(Vector3.Dot(hit.normal,Vector3.up)<.75f)steep++;
            }
            checks.Add($"{item.id}: samples={samples}, missing={missing}, steep={steep}");
            if(samples<100||missing>0||steep>0)throw new InvalidOperationException("Carving clipped: "+item.id);
        }
        finally{Object.DestroyImmediate(go);}
    }
    static void Capture(string path,GameObject go,Vector3 direction,int width,int height)
    {
        var bounds=go.GetComponent<Renderer>().bounds;camera.transform.position=bounds.center+direction.normalized*20;
        camera.transform.LookAt(bounds.center);camera.aspect=width/(float)height;float maxX=0,maxY=0;
        foreach(var vertex in go.GetComponent<MeshFilter>().sharedMesh.vertices)
        {
            var p=camera.transform.InverseTransformPoint(go.transform.TransformPoint(vertex));
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
        finally{RenderTexture.active=prior;Object.DestroyImmediate(image);RenderTexture.ReleaseTemporary(target);}
    }
    static void Fail(Exception e){Debug.LogException(e);if(output!=null)File.WriteAllText(output+"/error.txt",e.ToString());EditorApplication.Exit(1);}
}
