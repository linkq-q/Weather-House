using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace WeatherHouse.StoneEngraving.Editor
{
    // Independent study assets: never saves or replaces anything in the active scene.
    public static class StoneMonumentStudy
    {
        const string AssetsRoot = "Assets/Experiments/StoneEngraving/MonumentPainted";
        const string Output = "StoneEngravingReview/20261030/Monuments";
        static readonly Vector3 Origin = new Vector3(0,200,0);
        static readonly (string id, float width, float height, float depth, bool crown)[] Specs = {
            ("first_then",8.4f,4.0f,1.05f,false), ("here_there",6.4f,3.9f,1.0f,false),
            ("together",5.6f,4.5f,1.05f,true), ("grass_tree",5.0f,4.9f,1.1f,true)
        };

        static T Save<T>(T value,string path) where T:Object
        {
            var stored=AssetDatabase.LoadAssetAtPath<T>(path);
            if(!stored){AssetDatabase.CreateAsset(value,path);return value;}
            EditorUtility.CopySerialized(value,stored);AssetDatabase.SaveAssetIfDirty(stored);Object.DestroyImmediate(value);return stored;
        }

        // Three closed polygon rings give a broad worked face, bevel and solid side walls.
        static Mesh Slab(string name,float width,float height,float depth,bool crown,float bevel)
        {
            var p=crown ? new[]{new Vector2(-.47f,0),new Vector2(.47f,0),new Vector2(.5f,.045f),new Vector2(.485f,.84f),
                new Vector2(.34f,.96f),new Vector2(.03f,1),new Vector2(-.32f,.97f),new Vector2(-.49f,.85f),new Vector2(-.5f,.055f)}
                : new[]{new Vector2(-.475f,0),new Vector2(.475f,0),new Vector2(.5f,.06f),new Vector2(.495f,.94f),
                new Vector2(.45f,.992f),new Vector2(.12f,1),new Vector2(-.455f,.99f),new Vector2(-.5f,.93f),new Vector2(-.5f,.055f)};
            var rings=new Vector3[3][];
            for(int r=0;r<3;r++)
            {
                rings[r]=new Vector3[p.Length];
                for(int i=0;i<p.Length;i++)
                {
                    float x=p[i].x*width,y=p[i].y*height;
                    if(r==0){x*=1-2*bevel/width;y=height*.5f+(y-height*.5f)*(1-2*bevel/height);}
                    if(r==2){x*=.98f;y=height*.5f+(y-height*.5f)*.985f;}
                    rings[r][i]=new Vector3(x,y,r==0?-depth*.5f:r==1?-depth*.5f+bevel:depth*.5f);
                }
            }
            var vertices=new List<Vector3>();var normals=new List<Vector3>();var uv=new List<Vector2>();var triangles=new List<int>();
            Action<Vector3,Vector3,Vector3,Vector3> tri=(a,b,c,outward)=>{
                var n=Vector3.Cross(b-a,c-a);if(Vector3.Dot(n,outward)<0){var t=b;b=c;c=t;n=-n;}
                if(n.sqrMagnitude<1e-10f)throw new InvalidOperationException(name+" degenerate triangle");
                n.Normalize();int start=vertices.Count;
                foreach(var v in new[]{a,b,c}){vertices.Add(v);normals.Add(n);uv.Add(new Vector2(v.x+v.z*.47f,v.y+v.z*.31f)*.10f);}
                triangles.AddRange(new[]{start,start+1,start+2});
            };
            for(int i=0;i<p.Length;i++)
            {
                int j=(i+1)%p.Length;
                tri(new Vector3(0,height*.5f,-depth*.5f),rings[0][i],rings[0][j],Vector3.back);
                tri(new Vector3(0,height*.5f,depth*.5f),rings[2][i],rings[2][j],Vector3.forward);
                for(int r=0;r<2;r++)
                {
                    var outward=(rings[r][i]+rings[r][j])*.5f-new Vector3(0,height*.5f,0);outward.z=r==0?-depth:0;
                    tri(rings[r][i],rings[r+1][i],rings[r+1][j],outward);
                    tri(rings[r][i],rings[r+1][j],rings[r][j],outward);
                }
            }
            var mesh=new Mesh{name=name};mesh.SetVertices(vertices);mesh.SetNormals(normals);mesh.SetUVs(0,uv);mesh.SetTriangles(triangles,0);
            mesh.RecalculateTangents();mesh.RecalculateBounds();CheckClosed(mesh);return mesh;
        }

        static void CheckClosed(Mesh mesh)
        {
            var v=mesh.vertices;var t=mesh.triangles;var edgeCount=new Dictionary<(Vector3,Vector3),int>();
            for(int i=0;i<t.Length;i+=3)for(int j=0;j<3;j++)
            {
                var a=v[t[i+j]];var b=v[t[i+(j+1)%3]];var key=(a,b);var reverse=(b,a);
                if(edgeCount.ContainsKey(reverse))key=reverse;
                edgeCount[key]=edgeCount.TryGetValue(key,out var count)?count+1:1;
            }
            if(edgeCount.Values.Any(n=>n!=2))throw new InvalidOperationException(mesh.name+" mesh not closed");
        }

        static GameObject Piece(GameObject root,string name,Mesh mesh,Material mat,Vector3 position)
        {
            var go=new GameObject(name);go.transform.SetParent(root.transform,false);go.transform.localPosition=position;
            go.AddComponent<MeshFilter>().sharedMesh=mesh;go.AddComponent<MeshRenderer>().sharedMaterial=mat;go.AddComponent<MeshCollider>().sharedMesh=mesh;return go;
        }

        static Bounds BoundsOf(GameObject go)
        {
            var renderers=go.GetComponentsInChildren<Renderer>();var bounds=renderers[0].bounds;
            foreach(var r in renderers.Skip(1))bounds.Encapsulate(r.bounds);return bounds;
        }

        static GameObject PlayerReference(Scene preview,List<Mesh> temporary)
        {
            var source=Object.FindObjectOfType<ForestSpriteCharacter.ForestSpriteDemoController>();
            if(!source)throw new InvalidOperationException("Player reference missing");
            var root=new GameObject("Actual player model - scale reference");SceneManager.MoveGameObjectToScene(root,preview);
            Bounds sourceBounds=new Bounds();bool hasBounds=false;
            foreach(var r in source.GetComponentsInChildren<Renderer>())
            {
                if(!r.enabled||!r.gameObject.activeInHierarchy||r.shadowCastingMode==UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly)continue;
                Mesh mesh=null;
                if(r is SkinnedMeshRenderer skin){mesh=new Mesh();skin.BakeMesh(mesh,false);temporary.Add(mesh);}
                else if(r is MeshRenderer && r.GetComponent<MeshFilter>())mesh=r.GetComponent<MeshFilter>().sharedMesh;
                if(!mesh)continue;
                var go=new GameObject(r.name);go.transform.SetParent(root.transform,false);go.transform.localPosition=r.transform.position-source.transform.position;
                // This imported rig's baked vertices are already at scene scale; validate below.
                go.transform.localRotation=r.transform.rotation;go.transform.localScale=r is SkinnedMeshRenderer?Vector3.one:r.transform.lossyScale;
                go.AddComponent<MeshFilter>().sharedMesh=mesh;go.AddComponent<MeshRenderer>().sharedMaterials=r.sharedMaterials;
                if(!hasBounds){sourceBounds=r.bounds;hasBounds=true;}else sourceBounds.Encapsulate(r.bounds);
            }
            var b=BoundsOf(root);
            if(!hasBounds||Mathf.Abs(b.size.y-sourceBounds.size.y)>.1f)throw new InvalidOperationException($"Player scale reference differs from scene: source={sourceBounds.size.y} baked={b.size.y}");
            File.WriteAllText(Output+"/player-scale.txt",$"PASS sourceHeight={sourceBounds.size.y:F3}; bakedHeight={b.size.y:F3}; absolute error < 0.1m; source scale retained; no scripts copied");
            root.transform.position-=Vector3.up*b.min.y;return root;
        }

        public static void Build()
        {
            Directory.CreateDirectory(AssetsRoot);Directory.CreateDirectory(Output);AssetDatabase.Refresh();
            var scene=SceneManager.GetActiveScene();bool wasDirty=scene.isDirty;
            var preview=EditorSceneManager.NewPreviewScene();var temporary=new List<Mesh>();Material groundMat=null;
            try
            {
                var camera=StoneTabletStudy.Setup(preview);
                var ground=GameObject.CreatePrimitive(PrimitiveType.Plane);SceneManager.MoveGameObjectToScene(ground,preview);ground.transform.position=Origin;
                ground.transform.localScale=Vector3.one*5;groundMat=new Material(Shader.Find("Universal Render Pipeline/Lit"));groundMat.color=new Color(.31f,.34f,.24f);
                ground.GetComponent<Renderer>().sharedMaterial=groundMat;
                var reference=AssetDatabase.LoadAssetAtPath<Material>("Assets/Experiments/StoneEngraving/SevenPainted/together.mat");
                var plain=new Material(reference);plain.name="Monument stone";plain.SetFloat("_EngravingStrength",0);plain.SetFloat("_FillStrength",0);plain.SetFloat("_PaintStrength",0);plain.SetFloat("_BumpScale",.12f);
                plain=Save(plain,AssetsRoot+"/Stone.mat");
                var player=PlayerReference(preview,temporary);player.SetActive(false);var report=new List<string>();
                foreach(var spec in Specs)
                {
                    var root=new GameObject(spec.id+" · monumental stone");SceneManager.MoveGameObjectToScene(root,preview);root.transform.position=Origin;
                    var mesh=Save(Slab(spec.id+"_body",spec.width,spec.height,spec.depth,spec.crown,.20f),AssetsRoot+"/"+spec.id+"_body.asset");
                    var lower=Save(Slab(spec.id+"_foot",spec.width+.7f,.28f,spec.depth+1.15f,false,.065f),AssetsRoot+"/"+spec.id+"_foot.asset");
                    var upper=Save(Slab(spec.id+"_socket",spec.width+.3f,.34f,spec.depth+.65f,false,.055f),AssetsRoot+"/"+spec.id+"_socket.asset");
                    Piece(root,"Foundation",lower,plain,Vector3.zero);Piece(root,"Stone socket",upper,plain,new Vector3(0,.27f,0));
                    var mat=new Material(AssetDatabase.LoadAssetAtPath<Material>("Assets/Experiments/StoneEngraving/SevenPainted/"+spec.id+".mat"));mat.name=spec.id;mat.SetFloat("_BumpScale",.065f);
                    var body=Piece(root,"Worked inscription face",mesh,mat,new Vector3(0,.57f,0));
                    var mask=(Texture2D)mat.GetTexture("_EngravingMask");float aspect=mask.width/(float)mask.height;
                    float w=spec.width-.64f,h=w/aspect;if(h>spec.height-.63f){h=spec.height-.63f;w=h*aspect;}
                    var center=new Vector3(0,spec.height*.49f,-spec.depth*.5f);
                    mat.SetVector("_ProjectionCenter",center);mat.SetVector("_ProjectionRight",Vector3.right);mat.SetVector("_ProjectionUp",Vector3.up);
                    mat.SetVector("_ProjectionSize",new Vector4(w,h,.22f,0));
                    Physics.SyncTransforms();int samples=0,missing=0,steep=0;int rows=Mathf.RoundToInt(768/aspect);
                    var collider=body.GetComponent<MeshCollider>();
                    for(int y=0;y<rows;y++)for(int x=0;x<768;x++)
                    {
                        float u=(x+.5f)/768,v=(y+.5f)/rows;if(mask.GetPixelBilinear(u,v).a<.2f)continue;samples++;
                        var point=body.transform.TransformPoint(center+new Vector3((u-.5f)*w,(v-.5f)*h,-1));
                        if(!collider.Raycast(new Ray(point,Vector3.forward),out var hit,2))missing++;else if(hit.normal.z>-.95f)steep++;
                    }
                    if(samples<100||missing>0||steep>0)throw new InvalidOperationException(spec.id+" art clipped or bevel crossing");
                    var b=BoundsOf(root);string folder=Output+"/"+spec.id;Directory.CreateDirectory(folder);
                    StoneTabletStudy.Frame(camera,b,new Vector3(.05f,.18f,-1),folder+"/engraved.png",1600,1200,1.12f);
                    StoneTabletStudy.Frame(camera,b,new Vector3(.75f,.32f,-1),folder+"/side.png",1400,1100,1.13f);
                    body.GetComponent<Renderer>().sharedMaterial=plain;
                    StoneTabletStudy.Frame(camera,b,new Vector3(.35f,.24f,-1),folder+"/blank.png",1400,1100,1.13f);
                    body.GetComponent<Renderer>().sharedMaterial=mat;
                    player.SetActive(true);var pb=BoundsOf(player);player.transform.position+=Origin+new Vector3(-spec.width*.5f-1.05f,0,-.5f)-new Vector3(pb.center.x,pb.min.y,pb.center.z);
                    var context=b;context.Encapsulate(BoundsOf(player));
                    StoneTabletStudy.Frame(camera,context,new Vector3(.2f,.23f,-1),folder+"/scale.png",1600,1200,1.13f);
                    mat.SetFloat("_GameplayReadability",1);StoneTabletStudy.Frame(camera,context,new Vector3(-.3f,1.35f,-1),folder+"/game.png",1400,1000,1.55f);mat.SetFloat("_GameplayReadability",0);
                    report.Add($"{spec.id}: size={b.size}; artwork={w:F2}x{h:F2}; triangles={mesh.triangles.Length/3+lower.triangles.Length/3+upper.triangles.Length/3}; closed meshes=3; samples={samples}; missing={missing}; steep={steep}; playerHeight={BoundsOf(player).size.y:F2}; maps unchanged; isolated preview");
                    player.SetActive(false);var stored=Save(mat,AssetsRoot+"/"+spec.id+".mat");body.GetComponent<Renderer>().sharedMaterial=stored;
                    body.AddComponent<StoneEngravingReadability>();root.transform.position=Vector3.zero;PrefabUtility.SaveAsPrefabAsset(root,AssetsRoot+"/"+spec.id+".prefab");Object.DestroyImmediate(root);
                }
                if(ShaderUtil.ShaderHasError(reference.shader))throw new InvalidOperationException("Paint shader has errors");
                if(scene.isDirty!=wasDirty)throw new InvalidOperationException("Active scene dirty state changed");
                File.WriteAllLines(Output+"/checks.txt",report);
            }
            finally{EditorSceneManager.ClosePreviewScene(preview);if(groundMat)Object.DestroyImmediate(groundMat);foreach(var mesh in temporary)Object.DestroyImmediate(mesh);}
        }

        public static void Place()
        {
            const string groupName="宏伟彩绘石碑 · 可调摆放";
            var scene=SceneManager.GetActiveScene();
            if(EditorApplication.isPlayingOrWillChangePlaymode||scene.path!="Assets/Scenes/WeatherHouse_WatersideV2.unity"||scene.isDirty)
                throw new InvalidOperationException("Placement requires clean WatersideV2 in Edit mode");
            if(GameObject.Find(groupName))throw new InvalidOperationException("Monuments already placed; preserve the user's adjustments");
            var titles=new[]{"先与后","这里与那里","一起","草与树"};
            var originals=titles.Select(t=>GameObject.Find(t+" · 石刻")).ToArray();
            for(int i=0;i<Specs.Length;i++)
                if(!originals[i]||PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(originals[i])!="Assets/Experiments/StoneEngraving/Seven/"+Specs[i].id+"/Stone.prefab")
                    throw new InvalidOperationException("Unexpected original stone: "+titles[i]);
            var terrain=Object.FindObjectsOfType<Terrain>().First(t=>t.gameObject.scene==scene);
            string folder=Output+"/MainScene";Directory.CreateDirectory(folder);
            string backup=folder+"/before.unity";if(File.Exists(backup))throw new InvalidOperationException("Placement backup exists; inspect before retrying");
            File.Copy(scene.path,backup);
            Undo.IncrementCurrentGroup();int undoGroup=Undo.GetCurrentGroup();Undo.SetCurrentGroupName("放置四块宏伟彩绘石碑");
            var root=new GameObject(groupName);Undo.RegisterCreatedObjectUndo(root,"放置四块石碑");
            var report=new List<string>();GameObject capture=null;
            try
            {
                capture=new GameObject("Monument scene capture"){hideFlags=HideFlags.HideAndDontSave};var camera=capture.AddComponent<Camera>();
                camera.CopyFrom(Object.FindObjectOfType<ForestSpriteCharacter.ForestSpriteDemoController>().demoCamera);camera.enabled=false;camera.orthographic=true;
                camera.nearClipPlane=.03f;camera.farClipPlane=250;
                Bounds firstBounds=new Bounds();
                for(int i=0;i<Specs.Length;i++)
                {
                    var original=originals[i];var oldRenderer=original.GetComponent<Renderer>();var oldMat=oldRenderer.sharedMaterial;var oldBounds=oldRenderer.bounds;
                    var front=original.transform.localToWorldMatrix.inverse.transpose.MultiplyVector(Vector3.Cross(oldMat.GetVector("_ProjectionUp"),oldMat.GetVector("_ProjectionRight")));
                    front.y=0;front=front.sqrMagnitude>.001f?front.normalized:Vector3.back;
                    var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(AssetsRoot+"/"+Specs[i].id+".prefab");
                    if(!prefab)throw new InvalidOperationException("Missing monument prefab");
                    var stone=(GameObject)PrefabUtility.InstantiatePrefab(prefab,root.transform);stone.name=titles[i]+" · 彩绘石碑";
                    stone.transform.SetPositionAndRotation(new Vector3(oldBounds.center.x,0,oldBounds.center.z),Quaternion.LookRotation(-front,Vector3.up));
                    var b=BoundsOf(stone);float floor=float.MaxValue;
                    for(int z=0;z<=4;z++)for(int x=0;x<=4;x++)floor=Mathf.Min(floor,WeatherHouse.WatersideV2.WatersideLayout.GroundHeight(terrain,Mathf.Lerp(b.min.x,b.max.x,x/4f),Mathf.Lerp(b.min.z,b.max.z,z/4f)));
                    stone.transform.position+=Vector3.up*(floor-.06f-b.min.y);
                    PrefabUtility.RecordPrefabInstancePropertyModifications(stone);PrefabUtility.RecordPrefabInstancePropertyModifications(stone.transform);
                    Undo.RecordObject(original,"隐藏对应旧石刻");original.SetActive(false);PrefabUtility.RecordPrefabInstancePropertyModifications(original);
                    if(PrefabUtility.GetPrefabInstanceStatus(stone)!=PrefabInstanceStatus.Connected||stone.GetComponentsInChildren<MeshCollider>().Length!=3||!stone.GetComponentInChildren<StoneEngravingReadability>())
                        throw new InvalidOperationException("Invalid installed monument");
                    Physics.SyncTransforms();b=BoundsOf(stone);if(i==0)firstBounds=b;
                    StoneTabletStudy.Frame(camera,b,front+Vector3.up*.8f,folder+"/"+Specs[i].id+".png",1600,1100,1.65f);
                    report.Add($"PASS {titles[i]}: prefab={AssetsRoot}/{Specs[i].id}.prefab; position={stone.transform.position}; rotation={stone.transform.eulerAngles}; scale={stone.transform.localScale}; 3 mesh colliders; readability present; old stone disabled, original transform retained; original footprint center={oldBounds.center}");
                }
                Object.DestroyImmediate(capture);capture=null;
                Selection.activeGameObject=root;
                if(SceneView.lastActiveSceneView)SceneView.lastActiveSceneView.LookAt(firstBounds.center,Quaternion.Euler(35,0,0),12);
                EditorSceneManager.MarkSceneDirty(scene);
                if(!EditorSceneManager.SaveScene(scene))throw new InvalidOperationException("Scene save failed");
                Undo.CollapseUndoOperations(undoGroup);File.WriteAllLines(folder+"/placement.txt",report);
            }
            catch{Undo.RevertAllDownToGroup(undoGroup);throw;}
            finally{if(capture)Object.DestroyImmediate(capture);}
        }
    }
}
