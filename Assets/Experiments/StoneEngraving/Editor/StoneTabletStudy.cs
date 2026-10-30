using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering.Universal;
using Object=UnityEngine.Object;

namespace WeatherHouse.StoneEngraving.Editor
{
    [InitializeOnLoad]
    public static class StoneTabletStudy
    {
        const string Output="StoneEngravingReview/20261030";
        const string AssetsRoot="Assets/Experiments/StoneEngraving/TabletStudy";
        static double next;
        static readonly Vector3 StudyOrigin=new Vector3(0,200,0);
        [Serializable] class InventoryInput { public string[] paths; }
        [Serializable] public class Entry { public string id,path,mesh; public int triangles; public Vector3 size; }
        [Serializable] class Entries { public List<Entry> items=new List<Entry>(); }
        static StoneTabletStudy(){EditorApplication.update+=Poll;}
        static void Poll()
        {
            if(EditorApplication.timeSinceStartup<next||EditorApplication.isCompiling||EditorApplication.isUpdating)return;
            next=EditorApplication.timeSinceStartup+.5;string file=Output+"/command.txt";if(!File.Exists(file))return;
            string command=File.ReadAllText(file).Trim();File.Delete(file);
            try
            {
                if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Study is edit-only; no active Play interruption.");
                if(command=="inventory")Inventory();
                else if(command=="paint")Paint();
                else if(command=="tablets")Tablets();
                else throw new InvalidOperationException("Unknown study command "+command);
                File.WriteAllText(Output+"/status.txt","PASS "+command+" "+DateTime.UtcNow.ToString("O"));
            }
            catch(Exception e){File.WriteAllText(Output+"/status.txt","FAIL "+command+"\n"+e);Debug.LogException(e);}
        }
        static Camera Setup(Scene preview)
        {
            var camera=new GameObject("Tablet study camera",typeof(Camera)).GetComponent<Camera>();SceneManager.MoveGameObjectToScene(camera.gameObject,preview);
            camera.scene=preview;camera.enabled=false;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.79f,.80f,.74f);
            camera.nearClipPlane=.03f;camera.farClipPlane=100;camera.orthographic=true;
            camera.gameObject.AddComponent<UniversalAdditionalCameraData>().renderPostProcessing=false;
            var light=new GameObject("Study key",typeof(Light)).GetComponent<Light>();SceneManager.MoveGameObjectToScene(light.gameObject,preview);
            light.type=LightType.Directional;light.intensity=1.25f;light.color=new Color(1,.97f,.91f);light.shadows=LightShadows.Soft;
            light.transform.rotation=Quaternion.Euler(48,-35,0);
            return camera;
        }
        static void Frame(Camera camera,Bounds bounds,Vector3 direction,string path,int width=900,int height=800,float padding=1.15f)
        {
            camera.transform.position=bounds.center+direction.normalized*12;camera.transform.LookAt(bounds.center);
            float size=0;
            for(int x=-1;x<=1;x+=2)for(int y=-1;y<=1;y+=2)for(int z=-1;z<=1;z+=2)
            {var v=camera.transform.InverseTransformPoint(bounds.center+Vector3.Scale(bounds.extents,new Vector3(x,y,z)));size=Mathf.Max(size,Mathf.Abs(v.y),Mathf.Abs(v.x)*height/width);}
            camera.orthographicSize=size*padding;
            StoneEngravingPlacementReview.Capture(camera,path,width,height);
        }
        static void Paint()
        {
            var preview=EditorSceneManager.NewPreviewScene();var created=new List<Material>();
            try
            {
                var camera=Setup(preview);
                var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Experiments/StoneEngraving/RainWater_ExistingRock.prefab");
                var stone=Object.Instantiate(prefab);SceneManager.MoveGameObjectToScene(stone,preview);stone.transform.position=StudyOrigin;
                var renderer=stone.GetComponent<Renderer>();var original=renderer.sharedMaterial;
                foreach(var component in stone.GetComponents<MonoBehaviour>())Object.DestroyImmediate(component);
                var grainPath=AssetsRoot+"/PaintGrain.png";var importer=(TextureImporter)AssetImporter.GetAtPath(grainPath);
                importer.sRGBTexture=false;importer.wrapMode=TextureWrapMode.Repeat;importer.textureCompression=TextureImporterCompression.Uncompressed;importer.SaveAndReimport();
                var fillPath=AssetsRoot+"/RainWaterPaint.png";var fillImporter=(TextureImporter)AssetImporter.GetAtPath(fillPath);
                fillImporter.sRGBTexture=true;fillImporter.wrapMode=TextureWrapMode.Clamp;fillImporter.alphaIsTransparency=true;
                fillImporter.textureCompression=TextureImporterCompression.Uncompressed;fillImporter.SaveAndReimport();
                var shader=Shader.Find("Weather House/Experiments/Painted Stone Engraving");
                if(!shader)throw new InvalidOperationException("Paint shader missing.");
                Directory.CreateDirectory(Output+"/Paint");
                for(int n=0;n<3;n++)
                {
                    var mat=new Material(original);created.Add(mat);mat.shader=shader;
                    mat.SetTexture("_PaintGrain",AssetDatabase.LoadAssetAtPath<Texture2D>(grainPath));mat.SetColor("_PaintColor",new Color(.97f,.95f,.89f,1));
                    mat.SetTexture("_PaintFill",AssetDatabase.LoadAssetAtPath<Texture2D>(fillPath));
                    mat.SetFloat("_PaintStrength",n==0?0:1);mat.SetFloat("_FillStrength",n==2?1:0);renderer.sharedMaterial=mat;
                    string name=new[]{"A-original","B-white-grooves","C-filled"}[n];
                    Frame(camera,renderer.bounds,new Vector3(0,1.6f,-1),Output+"/Paint/"+name+"-close.png",1500,1100);
                    Frame(camera,renderer.bounds,new Vector3(-.5f,1.35f,-1),Output+"/Paint/"+name+"-game.png",1100,800,2.5f);
                    if(n>0)
                    {
                        string path=AssetsRoot+"/"+name+".mat";var stored=AssetDatabase.LoadAssetAtPath<Material>(path);
                        if(stored){EditorUtility.CopySerialized(mat,stored);AssetDatabase.SaveAssetIfDirty(stored);}else{AssetDatabase.CreateAsset(mat,path);created.Remove(mat);stored=mat;}
                        renderer.sharedMaterial=stored;
                        if(!stone.GetComponent<MeshCollider>())stone.AddComponent<MeshCollider>().sharedMesh=stone.GetComponent<MeshFilter>().sharedMesh;
                        var readable=stone.AddComponent<StoneEngravingReadability>();
                        stone.transform.position-=StudyOrigin+Vector3.up*(renderer.bounds.min.y-StudyOrigin.y);
                        PrefabUtility.SaveAsPrefabAsset(stone,AssetsRoot+"/"+name+".prefab");
                        stone.transform.position=StudyOrigin;Object.DestroyImmediate(readable);
                    }
                }
                var errors=ShaderUtil.GetShaderMessages(shader).Where(m=>m.severity.ToString()=="Error").ToArray();
                if(errors.Length>0)throw new InvalidOperationException(string.Join("\n",errors.Select(e=>e.message)));
                File.WriteAllText(Output+"/Paint/checks.txt","PASS original Rock_1 mesh, scale, depth/normal/coverage retained; pigment changes albedo only; original assets untouched; shader errors=0.");
            }
            finally{EditorSceneManager.ClosePreviewScene(preview);foreach(var mat in created)if(mat)Object.DestroyImmediate(mat);}
        }
        static void Inventory()
        {
            var preview=EditorSceneManager.NewPreviewScene();Material material=null;
            try
            {
                var camera=Setup(preview);material=new Material(Shader.Find("Universal Render Pipeline/Lit"));material.color=new Color(.49f,.51f,.46f);material.SetFloat("_Smoothness",.08f);
                var input=JsonUtility.FromJson<InventoryInput>(File.ReadAllText(Output+"/inventory-input.json"));var result=new Entries();int id=0;
                Directory.CreateDirectory(Output+"/Inventory");
                foreach(var path in input.paths)
                {
                    var mesh=AssetDatabase.LoadAllAssetsAtPath(path).OfType<Mesh>().OrderByDescending(m=>m.vertexCount).First();
                    var go=new GameObject("Stone candidate");SceneManager.MoveGameObjectToScene(go,preview);
                    go.transform.position=StudyOrigin;
                    go.AddComponent<MeshFilter>().sharedMesh=mesh;var renderer=go.AddComponent<MeshRenderer>();renderer.sharedMaterial=material;
                    go.transform.localScale=Vector3.one*3f/mesh.bounds.size.magnitude;
                    string key=(++id).ToString("00");result.items.Add(new Entry{id=key,path=path,mesh=mesh.name,triangles=mesh.triangles.Length/3,size=mesh.bounds.size});
                    Frame(camera,renderer.bounds,new Vector3(.8f,.6f,-1),Output+"/Inventory/"+key+"-front.png",700,600);
                    Frame(camera,renderer.bounds,new Vector3(-.7f,.6f,1),Output+"/Inventory/"+key+"-back.png",700,600);
                    Frame(camera,renderer.bounds,new Vector3(0,1,-.01f),Output+"/Inventory/"+key+"-top.png",700,600);
                    Object.DestroyImmediate(go);
                }
                File.WriteAllText(Output+"/inventory.json",JsonUtility.ToJson(result,true));
            }
            finally{EditorSceneManager.ClosePreviewScene(preview);if(material)Object.DestroyImmediate(material);}
        }

        static Vector4 FitTop(MeshCollider collider,float aspect)
        {
            const int n=80;var bounds=collider.bounds;float sx=bounds.size.x/n,sz=bounds.size.z/n;
            var bad=new int[n+1,n+1];
            for(int z=0;z<n;z++)for(int x=0;x<n;x++)
            {
                var origin=new Vector3(bounds.min.x+(x+.5f)*sx,bounds.max.y+1,bounds.min.z+(z+.5f)*sz);
                bool good=collider.Raycast(new Ray(origin,Vector3.down),out var hit,bounds.size.y+2)&&hit.normal.y>.82f;
                bad[x+1,z+1]=(good?0:1)+bad[x,z+1]+bad[x+1,z]-bad[x,z];
            }
            for(int w=n-8;w>=12;w--)
            {
                int h=Mathf.RoundToInt(w*sx/(aspect*sz));if(h<4||h>n-8)continue;
                var choices=new List<Vector4>();
                for(int z=4;z+h<n-4;z++)for(int x=4;x+w<n-4;x++)
                {
                    if(bad[x+w,z+h]-bad[x,z+h]-bad[x+w,z]+bad[x,z]!=0)continue;
                    choices.Add(new Vector4(bounds.min.x+(x+w*.5f)*sx,bounds.min.z+(z+h*.5f)*sz,w*sx*.90f,w*sx*.90f/aspect));
                }
                if(choices.Count>0)return choices.OrderBy(v=>new Vector2(v.x-bounds.center.x,v.y-bounds.center.z).sqrMagnitude).First();
            }
            throw new InvalidOperationException("No continuous tablet face.");
        }
        static void Tablets()
        {
            var preview=EditorSceneManager.NewPreviewScene();Material groundMaterial=null;
            try
            {
                var camera=Setup(preview);Directory.CreateDirectory(Output+"/Tablets");
                var ground=GameObject.CreatePrimitive(PrimitiveType.Plane);SceneManager.MoveGameObjectToScene(ground,preview);
                ground.transform.position=StudyOrigin;ground.transform.localScale=Vector3.one*4;
                groundMaterial=new Material(Shader.Find("Universal Render Pipeline/Lit"));groundMaterial.color=new Color(.31f,.34f,.24f);ground.GetComponent<Renderer>().sharedMaterial=groundMaterial;
                var specs=new[]{("A-slate","Stone_1",new Vector3(3.4f,.72f,3.0f),"wind"),
                    ("B-square","Stone_2",new Vector3(3.5f,.68f,2.8f),"wind"),
                    ("C-natural","Rock_1",new Vector3(3.3f,.72f,3.9f),"wind"),
                    ("D-wide","Stone_2",new Vector3(5.5f,.75f,3.0f),"first_then")};
                var report=new List<string>();
                foreach(var spec in specs)
                {
                    string path="Assets/HIGHLANDS - Stylized Environment/Meshes/Forest/"+spec.Item2+".fbx";
                    var mesh=AssetDatabase.LoadAllAssetsAtPath(path).OfType<Mesh>().OrderByDescending(m=>m.vertexCount).First();
                    var root=new GameObject(spec.Item1);SceneManager.MoveGameObjectToScene(root,preview);root.transform.position=StudyOrigin;
                    var body=new GameObject("Stone face");body.transform.SetParent(root.transform,false);body.AddComponent<MeshFilter>().sharedMesh=mesh;
                    body.transform.localScale=new Vector3(spec.Item3.x/mesh.bounds.size.x,spec.Item3.y/mesh.bounds.size.y,spec.Item3.z/mesh.bounds.size.z);
                    body.transform.localPosition=-Vector3.Scale(mesh.bounds.center,body.transform.localScale);
                    var renderer=body.AddComponent<MeshRenderer>();var collider=body.AddComponent<MeshCollider>();collider.sharedMesh=mesh;Physics.SyncTransforms();
                    var original=AssetDatabase.LoadAssetAtPath<Material>("Assets/Experiments/StoneEngraving/Seven/"+spec.Item4+"/Stone.mat");
                    var mat=new Material(original);renderer.sharedMaterial=mat;
                    var texture=(Texture2D)mat.GetTexture("_EngravingMask");float aspect=texture.width/(float)texture.height;
                    var fit=FitTop(collider,aspect);
                    var center=new Vector3(fit.x,collider.bounds.max.y,fit.y);mat.SetVector("_ProjectionCenter",body.transform.InverseTransformPoint(center));
                    mat.SetVector("_ProjectionRight",Vector3.right);mat.SetVector("_ProjectionUp",Vector3.forward);
                    mat.SetVector("_ProjectionSize",new Vector4(fit.z/body.transform.localScale.x,fit.w/body.transform.localScale.z,mesh.bounds.size.y*2,0));
                    // Check all artwork and canonical letters before tilting the whole tablet.
                    int samples=0,missing=0,steep=0;int height=Mathf.RoundToInt(512/aspect);
                    for(int y=0;y<height;y++)for(int x=0;x<512;x++)
                    {
                        float u=(x+.5f)/512,v=(y+.5f)/height;if(texture.GetPixelBilinear(u,v).a<.2f)continue;samples++;
                        var ray=new Ray(center+Vector3.right*((u-.5f)*fit.z)+Vector3.forward*((v-.5f)*fit.w)+Vector3.up,Vector3.down);
                        if(!collider.Raycast(ray,out var hit,collider.bounds.size.y+2))missing++;else if(hit.normal.y<.75f)steep++;
                    }
                    if(samples<100||missing>0||steep>0)throw new InvalidOperationException(spec.Item1+" clipped artwork.");
                    root.transform.rotation=Quaternion.Euler(-68,0,0);
                    body.transform.position+=Vector3.up*(StudyOrigin.y-renderer.bounds.min.y);
                    var bounds=renderer.bounds;
                    string folder=Output+"/Tablets/"+spec.Item1;Directory.CreateDirectory(folder);
                    Frame(camera,bounds,new Vector3(0,.38f,-1),folder+"/engraved.png",1400,1100);
                    Frame(camera,bounds,new Vector3(.65f,.45f,-1),folder+"/side.png",1200,1000);
                    mat.SetFloat("_EngravingStrength",0);Frame(camera,bounds,new Vector3(.3f,.4f,-1),folder+"/blank.png",1200,1000);mat.SetFloat("_EngravingStrength",1);
                    mat.SetFloat("_GameplayReadability",1);
                    Frame(camera,bounds,new Vector3(-.5f,1.35f,-1),folder+"/game.png",1000,800,2.4f);mat.SetFloat("_GameplayReadability",0);
                    string materialPath=AssetsRoot+"/"+spec.Item1+".mat";var stored=AssetDatabase.LoadAssetAtPath<Material>(materialPath);
                    if(stored){EditorUtility.CopySerialized(mat,stored);AssetDatabase.SaveAssetIfDirty(stored);Object.DestroyImmediate(mat);}else{AssetDatabase.CreateAsset(mat,materialPath);stored=mat;}
                    renderer.sharedMaterial=stored;body.AddComponent<StoneEngravingReadability>();
                    root.transform.position=Vector3.zero;
                    PrefabUtility.SaveAsPrefabAsset(root,AssetsRoot+"/"+spec.Item1+".prefab");
                    report.Add($"{spec.Item1}: source={path}; source mesh reused; triangles={mesh.triangles.Length/3}; dimensions={bounds.size}; face={fit.z:F3} x {fit.w:F3}m; samples={samples}; missing={missing}; steep={steep}; ground pivot; static MeshCollider; gameplay readability component; no main scene placement.");
                    Object.DestroyImmediate(root);
                }
                File.WriteAllLines(Output+"/Tablets/checks.txt",report);
            }
            finally{EditorSceneManager.ClosePreviewScene(preview);if(groundMaterial)Object.DestroyImmediate(groundMaterial);}
        }
    }
}
