using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;

// Runs only in the small isolated review project. No main-scene mutation or relay.
public static class StoneEngravingReviewBuilder
{
    const string Root="Assets/Experiments/StoneEngraving";
    [Serializable] public class Spec
    {
        public string source_scene,source_scene_sha256,source_prefab_instance_id,source_name;
        public string mesh_path,material_path,mesh_file_id,review_path;
        public float[] source_scale,source_position,source_rotation;
        public float groove_depth_m;
    }
    [Serializable] public class Report
    {
        public Spec source;
        public string mesh_name,shader,scene,prefab,graphics;
        public int vertices,triangles,shader_errors;
        public Vector3 dimensions,projectionCenter,projectionRight,projectionUp,projectionSize;
        public bool sourceMeshReused,normalImportedCorrectly,maskLinear;
        public string[] shaderMessages;
    }
    static Spec spec;
    static GameObject stone;
    static Camera camera;
    static Light sun;
    static Material material;
    static Mesh mesh;
    static int frames;

    public static void BuildBatch()
    {
        if(!Application.isBatchMode) throw new InvalidOperationException("Use the isolated batch project.");
        try
        {
            spec=JsonUtility.FromJson<Spec>(File.ReadAllText("source.json"));
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            ConfigureTexture(Root+"/Textures/RainWater_Normal.png",true);
            ConfigureTexture(Root+"/Textures/RainWater_Mask.png",false);
            var pipeline=AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>("Assets/Settings/URP-HighFidelity.asset");
            GraphicsSettings.renderPipelineAsset=pipeline; QualitySettings.renderPipeline=pipeline;
            QualitySettings.antiAliasing=4; QualitySettings.shadows=UnityEngine.ShadowQuality.All;
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            RenderSettings.ambientMode=AmbientMode.Trilight;
            RenderSettings.ambientSkyColor=new Color(.32f,.35f,.39f);
            RenderSettings.ambientEquatorColor=new Color(.23f,.24f,.25f);
            RenderSettings.ambientGroundColor=new Color(.12f,.13f,.14f);
            RenderSettings.fog=false; RenderSettings.skybox=null;
            mesh=AssetDatabase.LoadAllAssetsAtPath(spec.mesh_path).OfType<Mesh>().First(m=>{
                string guid;long id; AssetDatabase.TryGetGUIDAndLocalFileIdentifier(m,out guid,out id); return id.ToString()==spec.mesh_file_id;
            });
            stone=new GameObject("RainWater_ExistingRock_Copy");
            stone.AddComponent<MeshFilter>().sharedMesh=mesh;
            stone.transform.localScale=new Vector3(spec.source_scale[0],spec.source_scale[1],spec.source_scale[2]);
            var shader=Shader.Find("Weather House/Stone Engraving");
            if(!shader)throw new InvalidOperationException("Engraving shader not imported.");
            var original=AssetDatabase.LoadAssetAtPath<Material>(spec.material_path);
            material=new Material(shader){name="RainWater_EngravedStone"};
            material.SetColor("_BaseColor",original.GetColor("_BaseColor"));
            material.SetTexture("_BumpMap",original.GetTexture("_BumpMap"));
            material.SetFloat("_BumpScale",original.GetFloat("_BumpScale"));
            material.SetFloat("_Smoothness",.12f);
            material.SetFloat("_CavityTint",.42f);
            material.SetFloat("_GrooveDepth",spec.groove_depth_m);
            material.SetFloat("_GrooveShadow",.65f);
            material.SetTexture("_EngravingNormal",AssetDatabase.LoadAssetAtPath<Texture2D>(Root+"/Textures/RainWater_Normal.png"));
            material.SetTexture("_EngravingMask",AssetDatabase.LoadAssetAtPath<Texture2D>(Root+"/Textures/RainWater_Mask.png"));
            var b=mesh.bounds;
            var center=new Vector3(b.center.x,b.max.y,b.center.z);
            var width=b.size.x*.72f;
            var height=width*stone.transform.localScale.x/stone.transform.localScale.z;
            material.SetVector("_ProjectionCenter",center);
            material.SetVector("_ProjectionRight",Vector3.right);
            material.SetVector("_ProjectionUp",Vector3.forward);
            material.SetVector("_ProjectionSize",new Vector4(width,height,b.size.y*.85f,0));
            stone.AddComponent<MeshRenderer>().sharedMaterial=material;
            var bounds=stone.GetComponent<Renderer>().bounds;
            stone.transform.position-=new Vector3(bounds.center.x,bounds.min.y,bounds.center.z);
            sun=new GameObject("Review key light").AddComponent<Light>();
            sun.type=LightType.Directional;sun.intensity=1.4f;sun.color=new Color(1,.97f,.92f);
            sun.shadows=LightShadows.Soft;sun.shadowBias=.02f;sun.shadowNormalBias=.1f;
            sun.transform.rotation=Quaternion.Euler(40,-35,0);RenderSettings.sun=sun;
            var fill=new GameObject("Review fill light").AddComponent<Light>();
            fill.type=LightType.Directional;fill.intensity=.3f;fill.color=new Color(.8f,.87f,1);
            fill.transform.rotation=Quaternion.Euler(30,130,0);
            var ground=GameObject.CreatePrimitive(PrimitiveType.Plane);ground.name="Review ground";
            ground.transform.localScale=Vector3.one*bounds.size.magnitude*.8f;
            var groundMat=new Material(Shader.Find("Universal Render Pipeline/Lit"));groundMat.color=new Color(.23f,.24f,.23f);
            ground.GetComponent<Renderer>().sharedMaterial=groundMat;
            groundMat=SaveMaterial(groundMat,Root+"/ReviewGround.mat");
            ground.GetComponent<Renderer>().sharedMaterial=groundMat;
            camera=new GameObject("Stone review camera").AddComponent<Camera>();
            camera.clearFlags=CameraClearFlags.SolidColor; camera.backgroundColor=new Color(.17f,.19f,.20f);
            camera.nearClipPlane=.05f;camera.farClipPlane=200;camera.fieldOfView=35;camera.allowHDR=true;
            var data=camera.gameObject.AddComponent<UniversalAdditionalCameraData>();data.renderPostProcessing=false;
            data.SetRenderer(0);camera.tag="MainCamera";
            Directory.CreateDirectory(spec.review_path+"/Captures");
            EditorApplication.update+=Tick;
        }
        catch(Exception e){Fail(e);}
    }

    static void ConfigureTexture(string path,bool normal)
    {
        var importer=(TextureImporter)AssetImporter.GetAtPath(path);
        importer.textureType=normal?TextureImporterType.NormalMap:TextureImporterType.Default;
        importer.sRGBTexture=false;importer.mipmapEnabled=true;importer.wrapMode=TextureWrapMode.Clamp;
        importer.npotScale=TextureImporterNPOTScale.None;
        importer.textureCompression=TextureImporterCompression.Uncompressed;importer.maxTextureSize=2048;
        importer.filterMode=FilterMode.Trilinear;importer.anisoLevel=8;importer.SaveAndReimport();
    }

    static void Tick()
    {
        if(++frames<16)return;
        EditorApplication.update-=Tick;
        try
        {
            var bounds=stone.GetComponent<Renderer>().bounds;
            Capture("01_front",new Vector3(0,1.6f,-1),.95f);
            Capture("02_oblique",new Vector3(-.55f,1.2f,-1),1.08f);
            Capture("03_topdown",new Vector3(-.5f,1.35f,-1),1.8f);
            Capture("08_grazing",new Vector3(.4f,.7f,-1),1.05f);
            material.SetFloat("_GrooveDepth",0);
            Capture("09_grazing_without_parallax",new Vector3(.4f,.7f,-1),1.05f);
            material.SetFloat("_GrooveDepth",spec.groove_depth_m);
            sun.transform.rotation=Quaternion.Euler(32,35,0);
            Capture("04_opposite_light",new Vector3(0,1.6f,-1),.95f);
            sun.transform.rotation=Quaternion.Euler(40,-35,0);
            material.SetFloat("_EngravingStrength",0);
            Capture("05_original_front",new Vector3(0,1.6f,-1),.95f);
            Capture("06_original_back",new Vector3(0,.15f,1),1.05f);
            Capture("07_original_top",new Vector3(0,1,-.25f),1.05f);
            material.SetFloat("_EngravingStrength",1);
            var errors=ShaderUtil.GetShaderMessages(material.shader);
            var report=new Report{source=spec,mesh_name=mesh.name,vertices=mesh.vertexCount,triangles=mesh.triangles.Length/3,
                shader=material.shader.name,graphics=SystemInfo.graphicsDeviceName,dimensions=bounds.size,
                projectionCenter=material.GetVector("_ProjectionCenter"),projectionRight=material.GetVector("_ProjectionRight"),
                projectionUp=material.GetVector("_ProjectionUp"),projectionSize=material.GetVector("_ProjectionSize"),
                sourceMeshReused=stone.GetComponent<MeshFilter>().sharedMesh==mesh,
                normalImportedCorrectly=((TextureImporter)AssetImporter.GetAtPath(Root+"/Textures/RainWater_Normal.png")).textureType==TextureImporterType.NormalMap,
                maskLinear=!((TextureImporter)AssetImporter.GetAtPath(Root+"/Textures/RainWater_Mask.png")).sRGBTexture,
                shaderMessages=errors.Select(x=>x.message).ToArray(),shader_errors=errors.Count(x=>x.severity.ToString()=="Error"),
                scene="Assets/Scenes/Experimental/StoneEngraving_RainWater.unity",prefab=Root+"/RainWater_ExistingRock.prefab"};
            if(report.shader_errors>0)throw new InvalidOperationException(string.Join("\n",report.shaderMessages));
            material=SaveMaterial(material,Root+"/RainWater_EngravedStone.mat");
            stone.GetComponent<Renderer>().sharedMaterial=material;
            PrefabUtility.SaveAsPrefabAsset(stone,report.prefab);
            SetView(new Vector3(0,1.6f,-1),.95f);
            Directory.CreateDirectory("Assets/Scenes/Experimental");
            EditorSceneManager.SaveScene(SceneManager.GetActiveScene(),report.scene);
            AssetDatabase.SaveAssets();
            File.WriteAllText(spec.review_path+"/unity-report.json",JsonUtility.ToJson(report,true));
            Debug.Log("STONE_ENGRAVING_REVIEW_OK");EditorApplication.Exit(0);
        }
        catch(Exception e){Fail(e);}
    }

    static void SetView(Vector3 direction,float distanceScale)
    {
        var b=stone.GetComponent<Renderer>().bounds;
        float distance=Mathf.Max(b.size.y,b.size.x/1.4545f)*.5f/Mathf.Tan(camera.fieldOfView*Mathf.Deg2Rad*.5f);
        camera.transform.position=b.center+direction.normalized*(distance*1.3f*distanceScale+b.extents.z);
        camera.transform.LookAt(b.center);
    }

    static Material SaveMaterial(Material value,string path)
    {
        var existing=AssetDatabase.LoadAssetAtPath<Material>(path);
        if(existing==null){AssetDatabase.CreateAsset(value,path);return value;}
        EditorUtility.CopySerialized(value,existing);
        EditorUtility.SetDirty(existing);Object.DestroyImmediate(value);return existing;
    }

    static void Capture(string name,Vector3 direction,float distanceScale)
    {
        SetView(direction,distanceScale);
        var target=RenderTexture.GetTemporary(1600,1100,24,RenderTextureFormat.ARGB32);
        var prior=RenderTexture.active; Texture2D image=null;
        try
        {
            RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest{destination=target});
            RenderTexture.active=target;image=new Texture2D(1600,1100,TextureFormat.RGB24,false);
            image.ReadPixels(new Rect(0,0,1600,1100),0,0);image.Apply();
            File.WriteAllBytes(spec.review_path+"/Captures/"+name+".png",image.EncodeToPNG());
        }
        finally {RenderTexture.active=prior;if(image)Object.DestroyImmediate(image);RenderTexture.ReleaseTemporary(target);}
    }

    static void Fail(Exception e)
    {
        Debug.LogException(e);
        if(spec!=null)File.WriteAllText(spec.review_path+"/unity-error.txt",e.ToString());
        EditorApplication.Exit(1);
    }
}
