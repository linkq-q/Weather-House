using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace WeatherHousePrototype.Editor
{
    // The generated scene is ordinary editable Terrain and prefab instances. Rebuild is explicit.
    [InitializeOnLoad]
    public static class PrototypeSceneBuilder
    {
        const string Root = "Assets/WeatherHousePrototype";
        const string Output = Root + "/Generated";
        const string ScenePath = Root + "/WeatherHouse_MapDraft.unity";
        const string Pack = "Assets/HIGHLANDS - Stylized Environment/";
        const string Command = "Temp/WeatherHousePrototype.command";
        const float Size = 360f, WaterY = 3f, LakeScale = 132f / Size;
        static Terrain terrain;
        static Transform environment, plants, props;
        static List<Vector2> shore, route;
        static readonly List<List<Vector2>> branches=new List<List<Vector2>>();
        static readonly Dictionary<Material,Material> palette = new Dictionary<Material,Material>();
        static System.Random random;
        static double nextPoll;
        static Material wood, rope, cloth, moss;
        static readonly Vector2 A = P(.347f,.48f)+new Vector2(-8,3.6f), B = P(.20f,.27f), C = P(.347f,.48f)+new Vector2(42.72f,40.8f), D = P(.81f,.79f);
        static readonly Vector2[] Glades = { P(.12f,.31f), P(.25f,.14f), P(.91f,.37f), P(.16f,.77f) };

        static PrototypeSceneBuilder() { EditorApplication.update += Poll; }
        static void Poll()
        {
            if (EditorApplication.timeSinceStartup < nextPoll || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            nextPoll = EditorApplication.timeSinceStartup + 1;
            if (!File.Exists(Command)) return;
            string command = File.ReadAllText(Command).Trim();
            File.Delete(Command);
            try
            {
                if (command == "build") Build();
                else if (command == "capture") Capture();
                else if (command == "validate") Validate();
                else if (command == "play") EditorApplication.isPlaying = true;
                else if (command == "stop") EditorApplication.isPlaying = false;
                else if (command == "runtime") RuntimeCheck();
                else if (command == "movement") PrototypeMovementReview.Run();
                else if (command == "gameplay-check") GameplayStateChecks.Run();
                else if (command == "ui-review") ProceduralUiReview.Run();
                else if (command == "refresh") AssetDatabase.Refresh();
                else if (command == "inspect") Inspect();
                else if (command == "finish") Finish();
                else throw new InvalidOperationException("Unknown prototype command: " + command);
                File.WriteAllText("PrototypeReview/last-command.txt", command + " OK " + DateTime.Now.ToString("O"));
            }
            catch (Exception e)
            {
                File.WriteAllText("PrototypeReview/last-command.txt", command + " FAILED\n" + e);
                Debug.LogException(e);
            }
        }

        static Vector2 P(float u, float v) { return new Vector2((u-.5f)*Size, (.5f-v)*Size); }
        static void Layout()
        {
            shore = Smooth(new [] {
                P(.35f,.385f),P(.35f,.344f),P(.39f,.314f),P(.407f,.25f),P(.46f,.234f),
                P(.515f,.226f),P(.56f,.251f),P(.615f,.255f),P(.658f,.24f),P(.711f,.232f),
                P(.724f,.269f),P(.765f,.31f),P(.803f,.342f),P(.811f,.365f),P(.788f,.41f),
                P(.798f,.453f),P(.812f,.49f),P(.812f,.535f),P(.793f,.573f),P(.799f,.621f),
                P(.765f,.652f),P(.714f,.671f),P(.69f,.713f),P(.661f,.742f),P(.624f,.745f),
                P(.582f,.765f),P(.531f,.752f),P(.495f,.779f),P(.46f,.754f),P(.405f,.743f),
                P(.365f,.725f),P(.321f,.719f),P(.284f,.692f),P(.26f,.658f),P(.232f,.635f),
                P(.217f,.598f),P(.227f,.568f),P(.267f,.55f),P(.307f,.539f),P(.337f,.519f),
                P(.348f,.481f),P(.347f,.434f),P(.35f,.385f)
            }, true, 5);
            var lakePivot=P(.347f,.48f);
            for(int i=0;i<shore.Count;i++)shore[i]=lakePivot+(shore[i]-lakePivot)*LakeScale;
            route = Smooth(new [] {
                P(.245f,.475f),P(.265f,.414f),P(.23f,.365f),P(.20f,.305f),P(.24f,.272f),
                P(.31f,.286f),P(.37f,.235f),P(.46f,.22f),P(.49f,.275f),C,
                P(.65f,.225f),P(.72f,.25f),P(.79f,.29f),P(.84f,.35f),P(.80f,.43f),
                P(.87f,.49f),P(.91f,.55f),P(.87f,.62f),P(.90f,.67f),P(.85f,.70f),
                P(.812f,.731f),P(.798f,.778f),P(.77f,.803f),P(.692f,.818f),P(.609f,.812f),
                P(.52f,.801f),P(.423f,.77f),P(.334f,.744f),P(.267f,.711f),P(.218f,.657f),
                P(.184f,.601f),P(.173f,.547f),P(.189f,.492f),P(.215f,.457f),P(.245f,.475f)
            }, true, 7);
            branches.Clear();
            branches.Add(Smooth(new[]{P(.24f,.272f),P(.29f,.20f),P(.33f,.145f),P(.4f,.13f),P(.47f,.16f),P(.52f,.205f),C},false,8));
            branches.Add(Smooth(new[]{C,P(.65f,.27f),P(.735f,.335f),P(.75f,.43f),P(.76f,.52f),P(.73f,.60f),P(.76f,.69f),P(.798f,.778f)},false,8));
            branches.Add(Smooth(new[]{P(.798f,.778f),P(.70f,.74f),P(.60f,.735f),P(.49f,.735f),P(.37f,.70f),P(.265f,.65f),P(.22f,.59f),P(.205f,.53f),P(.245f,.475f)},false,8));
            branches.Add(Smooth(new[]{P(.189f,.492f),P(.13f,.43f),Glades[0],P(.20f,.305f)},false,8));
            branches.Add(Smooth(new[]{Glades[0],P(.12f,.22f),Glades[1],P(.33f,.145f)},false,8));
            branches.Add(Smooth(new[]{Glades[1],P(.23f,.09f),P(.17f,.075f)},false,8));
            branches.Add(Smooth(new[]{P(.84f,.35f),Glades[2],P(.94f,.43f),P(.87f,.49f)},false,8));
            branches.Add(Smooth(new[]{P(.218f,.657f),P(.13f,.68f),Glades[3],P(.25f,.82f),P(.334f,.744f)},false,8));
            branches.Add(Smooth(new[]{P(.735f,.335f),P(.79f,.39f),P(.80f,.43f)},false,8));
            branches.Add(Smooth(new[]{P(.33f,.145f),P(.34f,.075f),P(.47f,.065f),P(.58f,.115f),P(.65f,.225f)},false,8));
            branches.Add(Smooth(new[]{P(.423f,.77f),P(.43f,.85f),P(.34f,.88f),P(.27f,.86f)},false,8));
            branches.Add(Smooth(new[]{P(.245f,.475f),A+new Vector2(-10,0)},false,8));
        }

        [MenuItem("Weather House/Build Map Draft")]
        public static void Build()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play before building.");
            for (int i=0;i<SceneManager.sceneCount;i++)
                if (SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Save the open scene before building the draft.");
            Directory.CreateDirectory(Output); Directory.CreateDirectory("PrototypeReview");
            Layout(); random = new System.Random(171026); palette.Clear();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            environment = Group("01 地形与湖泊");
            props = Group("02 四个区域与生活道具");
            plants = Group("03 林木与低矮植被");
            wood=Mat("Weathered timber",new Color(.32f,.20f,.105f));
            rope=Mat("Rope",new Color(.55f,.42f,.24f));
            cloth=Mat("Warm linen",new Color(.88f,.72f,.4f));
            moss=Mat("Moss bed",new Color(.28f,.42f,.17f));
            BuildTerrain(); BuildWater(); BuildAreas(); Plant(); GrassDetails(); Lighting(); Cameras();
            AssetDatabase.SaveAssets();
            EditorSceneManager.SaveScene(SceneManager.GetActiveScene(), ScenePath);
            var sv = SceneView.lastActiveSceneView;
            if(sv != null) sv.LookAt(new Vector3(0,4,0), Quaternion.Euler(60,0,0), Size*.7f, false, true);
            Validate();
            Debug.Log("Weather House map draft built: " + ScenePath);
        }

        static Transform Group(string name, Transform parent = null)
        {
            var t=new GameObject(name).transform; t.SetParent(parent,false); return t;
        }
        static T Load<T>(string path) where T:Object
        {
            var item=AssetDatabase.LoadAssetAtPath<T>(path);
            if(item==null) throw new FileNotFoundException("Required asset: " + path);
            return item;
        }
        static void SaveAsset(Object asset,string filename)
        {
            string path=Output+"/"+filename;
            var existing=AssetDatabase.LoadAssetAtPath<Object>(path);
            if(existing!=null) { EditorUtility.CopySerialized(asset,existing); Object.DestroyImmediate(asset); }
            else AssetDatabase.CreateAsset(asset,path);
        }
        static Material Mat(string name,Color color)
        {
            string path=Output+"/"+name+".mat";
            var m=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(m==null) { m=new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(m,path); }
            m.color=color; m.SetFloat("_Smoothness",.13f); return m;
        }
        static float Rand(float min,float max) { return Mathf.Lerp(min,max,(float)random.NextDouble()); }
        static float Ease(float a,float b,float value) { return Mathf.SmoothStep(0,1,Mathf.InverseLerp(a,b,value)); }
        static float DistanceSegment(Vector2 p,Vector2 a,Vector2 b)
        { var d=b-a; return Vector2.Distance(p,a+d*Mathf.Clamp01(Vector2.Dot(p-a,d)/Mathf.Max(.00001f,d.sqrMagnitude))); }
        static float DistanceLine(Vector2 p,List<Vector2> points)
        { float d=float.MaxValue; for(int i=1;i<points.Count;i++) d=Mathf.Min(d,DistanceSegment(p,points[i-1],points[i])); return d; }
        static float PathDistance(Vector2 p)
        { float d=DistanceLine(p,route);foreach(var branch in branches)d=Mathf.Min(d,DistanceLine(p,branch)+.6f);return d; }
        static float ShoreDistance(Vector2 p)
        {
            bool inside=false;
            for(int i=0,j=shore.Count-1;i<shore.Count;j=i++)
            { var a=shore[i]; var b=shore[j]; if((a.y>p.y)!=(b.y>p.y) && p.x<(b.x-a.x)*(p.y-a.y)/(b.y-a.y)+a.x) inside=!inside; }
            return DistanceLine(p,shore)*(inside?-1:1);
        }
        static List<Vector2> Smooth(Vector2[] points,bool closed,int divisions)
        {
            int n=closed?points.Length-1:points.Length;
            var result=new List<Vector2>();
            for(int i=0;i<(closed?n:n-1);i++)
            {
                var a=points[closed?(i+n-1)%n:Mathf.Max(0,i-1)]; var b=points[i];
                var c=points[(i+1)%n]; var d=points[closed?(i+2)%n:Mathf.Min(n-1,i+2)];
                for(int s=0;s<divisions;s++) { float t=(float)s/divisions; result.Add(.5f*((2*b)+(-a+c)*t+(2*a-5*b+4*c-d)*t*t+(-a+3*b-3*c+d)*t*t*t)); }
            }
            result.Add(closed?result[0]:points[n-1]); return result;
        }
        static float Ridge(Vector2 p, Vector4[] points)
        {
            float result=4.6f;
            for(int i=1;i<points.Length;i++)
            {
                Vector2 a=P(points[i-1].x,points[i-1].y), b=P(points[i].x,points[i].y), ab=b-a;
                float t=Mathf.Clamp01(Vector2.Dot(p-a,ab)/ab.sqrMagnitude), d=Vector2.Distance(p,a+ab*t);
                float width=Mathf.Lerp(points[i-1].w,points[i].w,t), height=Mathf.Lerp(points[i-1].z,points[i].z,t);
                result=Mathf.Max(result,4.6f+(height-4.6f)*Mathf.Pow(Mathf.Clamp01(1-d/width),1.6f));
            }
            return result;
        }
        static readonly Vector4[] WestRidge={new Vector4(.08f,.76f,25,65),new Vector4(.09f,.45f,34,70),new Vector4(.16f,.14f,43,65),new Vector4(.43f,.08f,48,60)};
        static readonly Vector4[] EastRidge={new Vector4(.72f,.14f,37,65),new Vector4(.88f,.36f,46,70),new Vector4(.90f,.62f,39,65),new Vector4(.81f,.79f,33,65)};
        static readonly Vector4[] NorthRidge={new Vector4(.16f,.14f,43,65),new Vector4(.35f,.10f,52,65),new Vector4(.52f,.10f,46,62),new Vector4(.72f,.14f,37,65)};
        static float Hill(Vector2 p,Vector2 center,float height,float radius)
        {return height*Mathf.Pow(Mathf.Clamp01(1-Vector2.Distance(p,center)/radius),1.5f);}
        static float Height(Vector2 p)
        {
            float sd=ShoreDistance(p);
            if(sd<0) return WaterY-Mathf.Lerp(.05f,2.3f,Ease(0,5,-sd));
            float h=Mathf.Max(Ridge(p,WestRidge),Ridge(p,EastRidge),Ridge(p,NorthRidge));
            h+=Hill(p,P(.30f,.72f),18,62)+Hill(p,P(.56f,.70f),14,55)+Hill(p,P(.32f,.27f),12,42);
            h+=19*(1-Ease(23,65,Vector2.Distance(p,P(.69f,.49f))));
            float valley=Mathf.Min(DistanceSegment(p,P(.37f,.63f),P(.44f,.76f)),DistanceSegment(p,P(.44f,.76f),P(.38f,.90f)));
            h=Mathf.Max(4.8f,h-8*(1-Ease(4,24,valley)));
            h+=2.2f*Mathf.Sin(p.x*.045f)*Mathf.Cos(p.y*.038f)+(Mathf.PerlinNoise((p.x+211)*.045f,(p.y+211)*.045f)-.5f)*2;
            h=Mathf.Max(4.6f,h);
            h=Mathf.Lerp(h,4.65f,1-Ease(8,14,Vector2.Distance(p,A)));
            h=Mathf.Lerp(h,8f,1-Ease(9,33,Vector2.Distance(p,B)));
            h=Mathf.Lerp(h,5.8f,1-Ease(7,15,Vector2.Distance(p,C)));
            h=Mathf.Lerp(h,26f,1-Ease(7,20,Vector2.Distance(p,D)));
            h=Mathf.Lerp(WaterY,h,Ease(0,Mathf.Lerp(4,14,Mathf.InverseLerp(5,32,h)),sd));
            return h;
        }
        static float Ground(Vector2 p) { return terrain.SampleHeight(new Vector3(p.x,0,p.y))+terrain.transform.position.y; }
        static Vector3 At(Vector2 p,float extra=0) { return new Vector3(p.x,Ground(p)+extra,p.y); }
        static void BuildTerrain()
        {
            var data=AssetDatabase.LoadAssetAtPath<TerrainData>(Output+"/Map Terrain.asset");
            if(data==null) { data=new TerrainData(); AssetDatabase.CreateAsset(data,Output+"/Map Terrain.asset"); }
            data.heightmapResolution=257; data.alphamapResolution=512; data.baseMapResolution=1024; data.size=new Vector3(Size,80,Size);
            var paths=new List<List<Vector2>>{route};paths.AddRange(branches);
            var nodes=new List<Vector2>();var offsets=new List<int>();var links=new List<Vector2Int>();
            foreach(var path in paths)
            {
                offsets.Add(nodes.Count);
                for(int i=0;i<path.Count;i++){if(i>0)links.Add(new Vector2Int(nodes.Count-1,nodes.Count));nodes.Add(path[i]);}
            }
            for(int i=0;i<nodes.Count;i++)for(int j=i+1;j<nodes.Count;j++)
                if(Vector2.Distance(nodes[i],nodes[j])<3)links.Add(new Vector2Int(i,j));
            var nodeHeights=nodes.Select(p=>Mathf.Max(4.6f,Height(p))).ToArray();
            // Shared junction heights and bounded grades avoid steps where two independently carved paths meet.
            for(int pass=0;pass<180;pass++)foreach(var link in links)
            {
                float allowed=Vector2.Distance(nodes[link.x],nodes[link.y])*.24f;
                float delta=nodeHeights[link.y]-nodeHeights[link.x];
                if(Mathf.Abs(delta)>allowed)
                {
                    float correction=(Mathf.Abs(delta)-allowed)*.5f*Mathf.Sign(delta);
                    nodeHeights[link.x]+=correction;nodeHeights[link.y]-=correction;
                }
            }
            var heights=new float[257,257];var corridor=new float[257,257];
            for(int z=0;z<257;z++) for(int x=0;x<257;x++)
            {
                var p=new Vector2(x/256f*Size-Size/2,z/256f*Size-Size/2);float h=Height(p),distance=float.MaxValue,target=h;
                for(int k=0;k<paths.Count;k++)for(int i=1;i<paths[k].Count;i++)
                {
                    var path=paths[k];var ab=path[i]-path[i-1];float t=Mathf.Clamp01(Vector2.Dot(p-path[i-1],ab)/Mathf.Max(.0001f,ab.sqrMagnitude));float d=Vector2.Distance(p,path[i-1]+t*ab);
                    if(d<distance){distance=d;target=Mathf.Lerp(nodeHeights[offsets[k]+i-1],nodeHeights[offsets[k]+i],t);}
                }
                if(ShoreDistance(p)>.2f)h=Mathf.Lerp(h,target,1-Ease(3,9,distance));
                heights[z,x]=h/80;
                corridor[z,x]=1-Ease(2,6,distance);
            }
            for(int pass=0;pass<12;pass++)
            {
                var smoothed=(float[,])heights.Clone();
                for(int z=1;z<256;z++)for(int x=1;x<256;x++)
                    smoothed[z,x]=Mathf.Lerp(heights[z,x],(heights[z-1,x]+heights[z+1,x]+heights[z,x-1]+heights[z,x+1])*.25f,corridor[z,x]*.65f);
                heights=smoothed;
            }
            data.SetHeights(0,0,heights);
            var layers=new TerrainLayer[4];
            string[] names={"Grass_1","Grass_2","Mud_1","Mud_2"};
            for(int i=0;i<4;i++)
            {
                var layer=Object.Instantiate(Load<TerrainLayer>(Pack+"Terrain Layers/"+names[i]+".terrainlayer")); layer.name=names[i];
                if(i<2){layer.diffuseRemapMax=new Vector4(2.5f,1.15f,.16f,1);layer.normalScale=.55f;}
                layer.tileSize=Vector2.one*(i<2?3:3.5f); SaveAsset(layer,names[i]+".terrainlayer"); layers[i]=Load<TerrainLayer>(Output+"/"+names[i]+".terrainlayer");
            }
            data.terrainLayers=layers;
            var alpha=new float[512,512,4];
            for(int z=0;z<512;z++) for(int x=0;x<512;x++)
            {
                var p=new Vector2(x/511f*Size-Size/2,z/511f*Size-Size/2); float sd=ShoreDistance(p);
                float path=1-Ease(1.5f,2.35f,PathDistance(p));
                path=Mathf.Max(path,(1-Ease(6,9,Vector2.Distance(p,B)))*.65f,(1-Ease(5,8,Vector2.Distance(p,C)))*.6f);
                foreach(var glade in Glades)path=Mathf.Max(path,(1-Ease(3,7,Vector2.Distance(p,glade)))*.35f);
                float bank=1-Ease(.2f,2.5f,sd);
                float grass=(1-path)*(1-bank); float variation=Mathf.PerlinNoise(x*.024f,z*.024f);
                alpha[z,x,0]=grass*(.03f+.05f*variation); alpha[z,x,1]=grass*(.97f-.05f*variation);
                alpha[z,x,2]=path*(1-bank); alpha[z,x,3]=bank;
            }
            data.SetAlphamaps(0,0,alpha); EditorUtility.SetDirty(data);
            var go=Terrain.CreateTerrainGameObject(data); go.name="可编辑地形 · 湖岸 林间路网 缓坡"; go.transform.SetParent(environment); go.transform.position=new Vector3(-Size/2,0,-Size/2);
            terrain=go.GetComponent<Terrain>(); terrain.heightmapPixelError=3; terrain.basemapDistance=100;
            var material=new Material(Shader.Find("Universal Render Pipeline/Terrain/Lit")); SaveAsset(material,"Terrain.mat");
            terrain.materialTemplate=Load<Material>(Output+"/Terrain.mat"); terrain.drawInstanced=true;
            terrain.Flush();
        }
        static void BuildWater()
        {
            var go=GameObject.CreatePrimitive(PrimitiveType.Plane); go.name="同一池塘 · 水面"; go.transform.SetParent(environment);
            go.transform.position=new Vector3(0,WaterY,0); go.transform.localScale=new Vector3(Size/10,1,Size/10);
            Object.DestroyImmediate(go.GetComponent<Collider>()); go.GetComponent<Renderer>().sharedMaterial=Load<Material>(Pack+"Materials/Forest/Water.mat");
        }
        static Bounds BoundsOf(GameObject go)
        { var rr=go.GetComponentsInChildren<Renderer>(); var bounds=rr.Length>0?rr[0].bounds:new Bounds(go.transform.position,Vector3.one); foreach(var r in rr) bounds.Encapsulate(r.bounds); return bounds; }
        static GameObject Prop(string relative,Vector2 p,float rotation,Transform parent,float scale=1,float offset=0)
        {
            var go=(GameObject)PrefabUtility.InstantiatePrefab(Load<GameObject>(Pack+"Prefabs/"+relative+".prefab"));
            go.transform.SetParent(parent,true); go.transform.rotation=Quaternion.Euler(0,rotation,0); go.transform.localScale=Vector3.one*scale;
            go.transform.position=At(p,offset);
            foreach(var rb in go.GetComponentsInChildren<Rigidbody>()) rb.isKinematic=true;
            TintFoliage(go);
            return go;
        }
        static void TintFoliage(GameObject go)
        {
            foreach(var renderer in go.GetComponentsInChildren<Renderer>())
            {
                var materials=renderer.sharedMaterials;
                for(int i=0;i<materials.Length;i++)
                {
                    var source=materials[i]; if(source==null || !source.HasProperty("_MainColor")) continue;
                    Material tinted;
                    if(!palette.TryGetValue(source,out tinted))
                    {
                        var m=new Material(source); bool tree=source.name.Contains("Fir");
                        m.SetColor("_MainColor",tree?new Color(.32f,.43f,.25f,1):new Color(.36f,.47f,.22f,1));
                        if(m.HasProperty("_SecondColor"))m.SetColor("_SecondColor",tree?new Color(.52f,.58f,.35f,1):new Color(.6f,.62f,.32f,1));
                        if(source.name.StartsWith("Grass"))
                        {
                            m.SetColor("_MainColor",new Color(.19f,.29f,.095f,1));
                            m.SetColor("_SecondColor",new Color(.52f,.61f,.25f,1));
                            m.SetFloat("_SecondColorOffset",-.12f);m.SetFloat("_SecondColorFade",.6f);
                            if(m.HasProperty("_Translucency"))m.SetFloat("_Translucency",.3f);
                        }
                        m.enableInstancing=true;
                        SaveAsset(m,"Green "+source.name+".mat"); tinted=Load<Material>(Output+"/Green "+source.name+".mat"); palette[source]=tinted;
                    }
                    materials[i]=tinted;
                }
                renderer.sharedMaterials=materials;
            }
        }
        static GameObject SizedProp(string relative,Vector2 p,float rotation,Transform parent,float height)
        {
            var go=Prop(relative,p,rotation,parent); var b=BoundsOf(go); go.transform.localScale*=height/Mathf.Max(.1f,b.size.y);
            foreach(var group in go.GetComponentsInChildren<LODGroup>())
            {var levels=group.GetLODs();if(levels.Length>0){levels[levels.Length-1].screenRelativeTransitionHeight=.002f;group.SetLODs(levels);}}
            b=BoundsOf(go); go.transform.position+=Vector3.up*(Ground(p)-b.min.y-.04f); return go;
        }
        static GameObject Box(string name,Vector3 position,Vector3 size,Material material,Transform parent,bool collider=true)
        {
            var go=GameObject.CreatePrimitive(PrimitiveType.Cube); go.name=name; go.transform.SetParent(parent); go.transform.position=position; go.transform.localScale=size;
            go.GetComponent<Renderer>().sharedMaterial=material; if(!collider) Object.DestroyImmediate(go.GetComponent<Collider>()); return go;
        }
        static GameObject Pole(string name,Vector3 a,Vector3 b,float radius,Material material,Transform parent,bool collider=false)
        {
            var go=GameObject.CreatePrimitive(PrimitiveType.Cylinder); go.name=name; go.transform.SetParent(parent); go.transform.position=(a+b)/2;
            go.transform.up=(b-a).normalized; go.transform.localScale=new Vector3(radius*2,Vector3.Distance(a,b)/2,radius*2);
            go.GetComponent<Renderer>().sharedMaterial=material; if(!collider) Object.DestroyImmediate(go.GetComponent<Collider>()); return go;
        }
        static void BuildAreas()
        {
            var home=Group("A 天气屋 · 西岸贴水",props);
            var house=Prop("Witches House/House",A,270,home,.88f); house.name="天气屋（HIGHLANDS 可替换实例）";
            var hb=BoundsOf(house); house.transform.position+=Vector3.up*(Ground(A)-hb.min.y);
            Prop("Witches House/Fence_1",A+new Vector2(-3,9),0,home);
            Prop("Witches House/Fence_2",A+new Vector2(-7,-5),90,home);
            Prop("Camp/Table_1",A+new Vector2(-5,8),20,home);
            Prop("Camp/Barrel_1",A+new Vector2(-5,6),0,home,.8f);
            Prop("Camp Props/Bucket",A+new Vector2(-5,5),0,home);
            for(int i=0;i<8;i++) Box("岸边木平台",At(A+new Vector2(6,-2+i*.55f),.16f),new Vector3(3,.25f,.5f),wood,home);
            var yard=Group("B 树屋小院 · 晾晒架与苔藓床",props);
            var oldTree=SizedProp("Forest/Fir_Tall_2",B+new Vector2(0,5),15,yard,19); oldTree.name="小院老树";
            Vector3 platform=At(B+new Vector2(0,5),3.1f);
            Box("树屋平台",platform,new Vector3(4.5f,.3f,3.7f),wood,yard);
            Box("树屋小木屋",platform+new Vector3(.4f,1.1f,.3f),new Vector3(2.4f,2,2.1f),wood,yard);
            var roof=Box("树屋斜屋顶",platform+new Vector3(.4f,2.3f,.3f),new Vector3(3.2f,.22f,2.7f),moss,yard); roof.transform.rotation=Quaternion.Euler(0,0,-13);
            var dark=Mat("Dark doorway",new Color(.075f,.09f,.055f)); Box("树屋洞口",platform+new Vector3(.4f,.9f,-.77f),new Vector3(.7f,1.3f,.03f),dark,yard,false);
            for(int i=0;i<9;i++) Box("木梯踏步",platform+new Vector3(-1,-2.9f+i*.33f,-2.1f-i*.07f),new Vector3(.85f,.1f,.15f),wood,yard,false);
            Pole("木梯左侧",platform+new Vector3(-1.45f,-3,-2.1f),platform+new Vector3(-1.45f,.2f,-2.75f),.06f,wood,yard);
            Pole("木梯右侧",platform+new Vector3(-.55f,-3,-2.1f),platform+new Vector3(-.55f,.2f,-2.75f),.06f,wood,yard);
            Vector2 rack=B+new Vector2(-8,-3); ClothesRack(rack,yard);
            Vector2 bed=B+new Vector2(-3,-6); Box("苔藓床",At(bed,.12f),new Vector3(3.5f,.22f,2),moss,yard);
            for(int i=0;i<9;i++) Prop("Forest/Clovers",bed+new Vector2(Rand(-1.5f,1.5f),Rand(-.8f,.8f)),Rand(0,360),yard,Rand(.7f,1.1f));
            Prop("Forest/Log_1",B+new Vector2(6,-3),80,yard,.8f);
            Prop("Camp Props/Bucket",rack+new Vector2(1,-1),0,yard);
            var bank=Group("C 北岸动物活动空地",props);
            Prop("Forest/Log_2",C+new Vector2(-5,5),75,bank,1.1f);
            Prop("Forest/Stump_1",C+new Vector2(3,-4),0,bank,1.2f);
            Prop("Camp/Trough",C+new Vector2(5,5),-20,bank);
            var hill=Group("D 风铃坡 · 回望天气屋与湖",props);
            Chime(D+new Vector2(2,0),hill);
            Prop("Forest/Log_1",D+new Vector2(3,-4),60,hill,.9f);
            SizedProp("Forest/Rock_1(Grass)",D+new Vector2(9,-5),70,hill,2.2f);
            var forest=Group("E 林间探索支路与停留点",props);
            Prop("Forest/Log_2",P(.40f,.145f),40,forest,.9f);
            SizedProp("Forest/Rock_2(Grass)",P(.375f,.085f),35,forest,2.5f);
            Prop("Forest/Stump_2",P(.735f,.47f),0,forest,1.2f);
            Prop("Forest/Log_1",P(.735f,.48f),5,forest,.9f);
            for(int i=0;i<Glades.Length;i++)
            {
                var clearing=Group(new[]{"西林蕨草空地","北林岔口","东林岩隙","西南林中洼地"}[i],forest);
                Prop("Forest/Log_1",Glades[i]+new Vector2(-5,4),30+i*57,clearing,.7f);
                Prop("Forest/Stump_1",Glades[i]+new Vector2(4,5),i*71,clearing,.8f);
            }
        }
        static void ClothesRack(Vector2 p,Transform parent)
        {
            Vector3 o=At(p); Pole("晾晒架立柱",o+new Vector3(-2,0,0),o+new Vector3(-2,2.4f,0),.11f,wood,parent,true);
            Pole("晾晒架立柱",o+new Vector3(2,0,0),o+new Vector3(2,2.4f,0),.11f,wood,parent,true);
            Pole("晾衣绳",o+new Vector3(-2,2.2f,0),o+new Vector3(2,2.2f,0),.025f,rope,parent);
            for(int i=0;i<3;i++) Box("晾晒布",o+new Vector3(-1.2f+i*1.1f,1.65f,0),new Vector3(.75f,1.1f,.04f),cloth,parent,false);
        }
        static void Chime(Vector2 p,Transform parent)
        {
            Vector3 o=At(p);
            Pole("风铃立柱",o,o+new Vector3(-.12f,3.7f,0),.17f,wood,parent,true);
            Pole("风铃横杆",o+new Vector3(-.9f,3.6f,0),o+new Vector3(2.8f,3.6f,0),.13f,wood,parent);
            for(int i=0;i<3;i++)
            { float x=.65f+i*.7f; float bottom=1.5f+i*.23f;
                Pole("风铃细绳",o+new Vector3(x,3.55f,0),o+new Vector3(x,2.8f,0),.018f,rope,parent);
                Pole("木风铃",o+new Vector3(x,bottom,0),o+new Vector3(x,2.8f,0),.1f,cloth,parent);
            }
        }
        static bool Clear(Vector2 p,float margin)
        {
            if(ShoreDistance(p)<margin || PathDistance(p)<margin+1.2f) return false;
            if(Glades.Any(g=>Vector2.Distance(p,g)<7+margin))return false;
            if(DistanceSegment(p,A+new Vector2(-23,-14),A)<7)return false;
            if(Vector2.Distance(p,A)<12+margin || Vector2.Distance(p,B)<12+margin || Vector2.Distance(p,C)<8+margin || Vector2.Distance(p,D)<10+margin) return false;
            if(DistanceSegment(p,D,A)<9 || DistanceSegment(p,D,new Vector2(10,0))<8) return false;
            return true;
        }
        static void GrassDetails()
        {
            var data=terrain.terrainData; var prototypes=new DetailPrototype[2];
            for(int i=0;i<2;i++)
            {
                var source=Load<GameObject>(Pack+"Prefabs/Forest/Grass_"+(i+1)+".prefab"); var go=Object.Instantiate(source); go.name="GrassBrush_"+(i+1); TintFoliage(go);
                // Terrain instancing uses a single root mesh; the source LODGroup is unnecessary here.
                foreach(var lod in go.GetComponentsInChildren<LODGroup>())Object.DestroyImmediate(lod);
                var prefab=PrefabUtility.SaveAsPrefabAsset(go,Output+"/GrassBrush_"+(i+1)+".prefab"); Object.DestroyImmediate(go);
                prototypes[i]=new DetailPrototype { prototype=prefab,usePrototypeMesh=true,useInstancing=true,renderMode=DetailRenderMode.VertexLit,minWidth=.7f,maxWidth=1.3f,minHeight=.65f,maxHeight=1.2f,healthyColor=Color.white,dryColor=Color.white,noiseSpread=.3f };
            }
            data.SetDetailScatterMode(DetailScatterMode.InstanceCountMode);
            const int resolution=512;
            data.SetDetailResolution(resolution,16); data.detailPrototypes=prototypes;
            var map0=new int[resolution,resolution]; var map1=new int[resolution,resolution]; int count=0;
            for(int z=0;z<resolution;z++)for(int x=0;x<resolution;x++)
            {
                Vector2 p=new Vector2((x+.5f)/resolution*Size-Size/2,(z+.5f)/resolution*Size-Size/2);
                if(ShoreDistance(p)<.8f || PathDistance(p)<2.15f || Vector2.Distance(p,A)<7 || Vector2.Distance(p,B)<6 || Vector2.Distance(p,C)<5 || Vector2.Distance(p,D)<3)continue;
                if(random.NextDouble()<.09)continue;
                if(random.NextDouble()<.73)map0[z,x]=1;else map1[z,x]=1; count++;
            }
            data.SetDetailLayer(0,0,0,map0);data.SetDetailLayer(0,0,1,map1); data.RefreshPrototypes(); terrain.Flush(); terrain.drawTreesAndFoliage=true; terrain.detailObjectDistance=180;terrain.detailObjectDensity=1;
            EditorUtility.SetDirty(data); File.WriteAllText("PrototypeReview/grass.txt","Terrain Paint Details mesh grass cells: "+count+"\nBrushes: GrassBrush_1, GrassBrush_2\nInstanced meshes; "+resolution+" x "+resolution+" detail grid.");
        }
        static void Plant()
        {
            var trees=Group("外围树林 · 保留环路和观景开口",plants);
            var low=Group("林缘草花与灌木",plants); var stones=Group("湖岸石群",environment);
            var placed=new List<Vector2>();
            // Tree masses follow the outer woodland footprint rather than filling all empty ground.
            for(int i=0;i<15000 && placed.Count<1900;i++)
            {
                Vector2 p=new Vector2(Rand(-Size*.47f,Size*.47f),Rand(-Size*.47f,Size*.47f)); if(!Clear(p,2.5f)) continue;
                float border=Mathf.Max(Mathf.Abs(p.x),Mathf.Abs(p.y)); if(border<Size*.37f && random.NextDouble()>.82) continue;
                if(placed.Any(q=>Vector2.Distance(p,q)<4.2f)) continue;
                var go=SizedProp("Forest/"+(random.NextDouble()<.25?"Fir_Tall_":"Fir_")+(random.Next(3)+1),p,Rand(0,360),trees,Rand(9,17));
                placed.Add(p);
            }
            SizedProp("Forest/Fir_2",A+new Vector2(-2,13),20,trees,12);
            string[] small={"Grass_1","Grass_2","Clovers","Flowers_1","Fern_1","Bush_1","Bush_2"};
            for(int i=0;i<4500;i++)
            {
                Vector2 p=new Vector2(Rand(-Size*.48f,Size*.48f),Rand(-Size*.48f,Size*.48f)); float sd=ShoreDistance(p); if(sd<1 || PathDistance(p)<2.6f) continue;
                if(Vector2.Distance(p,A)<9 || Vector2.Distance(p,B)<8 || Vector2.Distance(p,C)<6 || Vector2.Distance(p,D)<5) continue;
                if(Mathf.PerlinNoise((p.x+100)*.15f,(p.y+100)*.15f)<.4f) continue;
                int type=random.Next(100)<72?random.Next(4):random.Next(small.Length);
                float s=type>=5?Rand(.65f,1.2f):Rand(.8f,1.6f); Prop("Forest/"+small[type],p,Rand(0,360),low,s);
            }
            // Broken outcrops at shore bends and slope feet, with smaller stones around each anchor.
            var anchors=new[]{shore[12],shore[62],shore[112],shore[157],P(.09f,.37f),P(.90f,.32f),P(.86f,.66f),P(.18f,.81f)};
            foreach(var anchor in anchors)
            {
                for(int i=0;i<5;i++)
                {
                    Vector2 p=anchor+new Vector2(Rand(-3.5f,3.5f),Rand(-2,2));
                    if(PathDistance(p)<5 || Vector2.Distance(p,A)<12)continue;
                    float height=i==0?Rand(2.1f,3):Rand(.35f,1.3f);
                    var rock=SizedProp("Forest/"+(i==0?"Rock_1(Grass)":"Stone_"+(random.Next(4)+1)),p,Rand(0,360),stones,height);
                    rock.transform.position-=Vector3.up*height*.18f;
                }
            }
            Prop("Wind Settings",new Vector2(0,0),0,environment);
        }
        static void Lighting()
        {
            var group=Group("04 晴日光照与空气感");
            var light=new GameObject("午后阳光").AddComponent<Light>(); light.transform.SetParent(group);
            light.type=LightType.Directional; light.transform.rotation=Quaternion.Euler(48,-35,0); light.intensity=1.25f; light.color=new Color(1,.94f,.79f);
            light.shadows=LightShadows.Soft; light.shadowBias=.035f; light.shadowNormalBias=.35f;
            RenderSettings.sun=light; RenderSettings.ambientMode=AmbientMode.Trilight;
            RenderSettings.ambientSkyColor=new Color(.64f,.74f,.78f); RenderSettings.ambientEquatorColor=new Color(.53f,.57f,.47f); RenderSettings.ambientGroundColor=new Color(.33f,.38f,.28f);
            RenderSettings.fog=true; RenderSettings.fogMode=FogMode.ExponentialSquared; RenderSettings.fogColor=new Color(.70f,.81f,.80f); RenderSettings.fogDensity=.0011f;
            RenderSettings.skybox=Load<Material>(Pack+"Materials/Forest/Skybox.mat");
            var volume=new GameObject("色调与轻微泛光").AddComponent<Volume>(); volume.transform.SetParent(group); volume.isGlobal=true;
            var profile=AssetDatabase.LoadAssetAtPath<VolumeProfile>(Output+"/Daylight Profile.asset");
            if(profile==null){profile=ScriptableObject.CreateInstance<VolumeProfile>();AssetDatabase.CreateAsset(profile,Output+"/Daylight Profile.asset");}
            profile.components.RemoveAll(c=>c==null);
            Tonemapping tone;if(!profile.TryGet(out tone))tone=profile.Add<Tonemapping>(); tone.mode.Override(TonemappingMode.ACES);
            ColorAdjustments color;if(!profile.TryGet(out color))color=profile.Add<ColorAdjustments>(); color.postExposure.Override(.25f); color.contrast.Override(-6); color.saturation.Override(-12);
            Bloom bloom;if(!profile.TryGet(out bloom))bloom=profile.Add<Bloom>(); bloom.intensity.Override(.12f); bloom.threshold.Override(1.1f);
            foreach(var effect in profile.components){if(!AssetDatabase.Contains(effect))AssetDatabase.AddObjectToAsset(effect,profile);EditorUtility.SetDirty(effect);}
            EditorUtility.SetDirty(profile);volume.sharedProfile=profile;
        }
        static Camera Camera(string name,Vector3 pos,Vector3 target,Transform parent,bool ortho=false)
        {
            var c=new GameObject(name).AddComponent<Camera>(); c.transform.SetParent(parent); c.transform.position=pos; c.transform.LookAt(target);
            c.fieldOfView=55; c.nearClipPlane=.15f; c.farClipPlane=650; c.orthographic=ortho; c.orthographicSize=Size*.535f; c.allowHDR=true;
            var extra=c.GetUniversalAdditionalCameraData(); extra.renderPostProcessing=true; extra.requiresColorOption=CameraOverrideOption.On; extra.requiresDepthOption=CameraOverrideOption.On;
            c.enabled=false; return c;
        }
        static void Cameras()
        {
            var group=Group("05 漫游与验收视角");
            var camera=Camera("Main Camera",At(A+new Vector2(-19,-9),6),At(A,3),group); camera.tag="MainCamera"; camera.enabled=true; camera.gameObject.AddComponent<AudioListener>();
            var map=Camera("Map Camera",new Vector3(0,Size*1.13f,0),Vector3.zero,group); map.transform.rotation=Quaternion.Euler(90,0,0);
            Camera("Overview Camera",new Vector3(0,Size*.75f,-Size*.70f),new Vector3(0,3,0),group);
            Camera("D Vista Camera",At(D+new Vector2(-3,-6),3),At(A+new Vector2(15,0),4),group);
            Camera("B Courtyard Camera",At(B+new Vector2(0,-16),5),At(B+new Vector2(-2,2),2),group);
            var player=new GameObject("Player · 第三人称漫游"); player.transform.SetParent(group); player.transform.position=At(A+new Vector2(-13,-3),.1f);
            var cc=player.AddComponent<CharacterController>(); cc.height=1.8f; cc.center=new Vector3(0,.9f,0); cc.radius=.28f; cc.stepOffset=.3f; cc.slopeLimit=40;
            var visual=Group("草稿人物（可替换）",player.transform);
            var body=GameObject.CreatePrimitive(PrimitiveType.Capsule); body.name="旅行者"; body.transform.SetParent(visual,false); body.transform.localPosition=new Vector3(0,.85f,0); body.transform.localScale=new Vector3(.62f,.65f,.62f);
            Object.DestroyImmediate(body.GetComponent<Collider>()); body.GetComponent<Renderer>().sharedMaterial=Mat("Traveler cloak",new Color(.12f,.28f,.22f));
            var head=GameObject.CreatePrimitive(PrimitiveType.Sphere); head.transform.SetParent(visual,false); head.transform.localPosition=new Vector3(0,1.55f,0); head.transform.localScale=Vector3.one*.4f;
            Object.DestroyImmediate(head.GetComponent<Collider>()); head.GetComponent<Renderer>().sharedMaterial=Mat("Traveler face",new Color(.73f,.53f,.32f));
            var walker=player.AddComponent<PrototypeWalker>(); walker.viewCamera=camera; walker.mapCamera=map; walker.visualRoot=visual; walker.yaw=45;
        }

        [MenuItem("Weather House/Validate Map Draft")]
        public static void Validate()
        {
            Layout(); terrain=Object.FindObjectOfType<Terrain>(); if(terrain==null) throw new Exception("Terrain missing"); Physics.SyncTransforms();
            var lines=new List<string>(); int failures=0; float length=0,maxSlope=0; int blocked=0,wet=0;
            var paths=new List<List<Vector2>>{route};paths.AddRange(branches);
            for(int pathIndex=0;pathIndex<paths.Count;pathIndex++)
            {
            var dense=new List<Vector2>();var path=paths[pathIndex];
            for(int i=1;i<path.Count;i++) {int steps=Mathf.Max(1,Mathf.CeilToInt(Vector2.Distance(path[i-1],path[i])/.3f));for(int j=0;j<steps;j++)dense.Add(Vector2.Lerp(path[i-1],path[i],(float)j/steps));} dense.Add(path[path.Count-1]);
            for(int i=1;i<dense.Count;i++)
            {
                Vector3 a=At(dense[i-1]),b=At(dense[i]); float horizontal=Vector2.Distance(dense[i-1],dense[i]);
                length+=Vector3.Distance(a,b); float slope=Mathf.Atan2(Mathf.Abs(a.y-b.y),horizontal)*Mathf.Rad2Deg;
                if(slope>35 && slope>maxSlope)lines.Add("Steep path "+pathIndex+" at "+a+" -> "+b+" slope="+slope.ToString("F1"));
                maxSlope=Mathf.Max(maxSlope,slope);
                if(a.y<WaterY+.25f) wet++;
                var hits=Physics.OverlapCapsule(a+Vector3.up*.45f,a+Vector3.up*1.5f,.32f);
                var obstacles=hits.Where(h=>!(h is TerrainCollider) && !(h is CharacterController) && !h.isTrigger).ToArray();
                if(obstacles.Length>0) { blocked++; lines.Add("Blocked path "+pathIndex+" sample "+i+" at "+a+": "+string.Join(", ",obstacles.Select(h=>h.name))); }
            }
            }
            lines.Add("Map: "+Size+" x "+Size+" m; main route + "+branches.Count+" exploration paths, total metres: "+length.ToString("F1")); lines.Add("Maximum sampled path slope (degrees): "+maxSlope.ToString("F1"));
            lines.Add("Underwater route samples: "+wet); lines.Add("Obstructed route samples (0.64m capsule): "+blocked);
            if(wet>0 || blocked>0 || maxSlope>35) failures++;
            var vista=GameObject.Find("D Vista Camera").GetComponent<Camera>();
            var home=GameObject.Find("天气屋（HIGHLANDS 可替换实例）"); var bounds=BoundsOf(home); Vector3 roof=bounds.center+Vector3.up*bounds.extents.y*.65f;
            RaycastHit hit; bool blockedView=Physics.Linecast(vista.transform.position,roof,out hit) && !hit.transform.IsChildOf(home.transform);
            lines.Add("D to A roof sightline blocked: "+blockedView+(blockedView?" by "+hit.collider.name:"")); if(blockedView) failures++;
            int missing=0; foreach(var go in SceneManager.GetActiveScene().GetRootGameObjects()) foreach(var t in go.GetComponentsInChildren<Transform>(true)) missing+=GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject);
            lines.Add("Missing scripts: "+missing); if(missing>0) failures++;
            lines.Add("Result: "+(failures==0?"PASS":"NEEDS REVIEW"));
            File.WriteAllLines("PrototypeReview/validation.txt",lines);
        }
        [MenuItem("Weather House/Capture Review Views")]
        public static void Capture()
        {
            Directory.CreateDirectory("PrototypeReview");
            string[] names={"Map Camera","Overview Camera","D Vista Camera","B Courtyard Camera","Main Camera"};
            foreach(string name in names)
            {
                var c=GameObject.Find(name).GetComponent<Camera>(); int w=name=="Map Camera"?1600:1920; int h=name=="Map Camera"?1600:1080;
                var rt=RenderTexture.GetTemporary(w,h,24,RenderTextureFormat.ARGB32); var old=c.targetTexture; var active=RenderTexture.active;
                try { c.targetTexture=rt; c.Render(); RenderTexture.active=rt; var tex=new Texture2D(w,h,TextureFormat.RGB24,false); tex.ReadPixels(new Rect(0,0,w,h),0,0); tex.Apply(); File.WriteAllBytes("PrototypeReview/"+name.Replace(" ","_")+".png",tex.EncodeToPNG()); Object.DestroyImmediate(tex); }
                finally { c.targetTexture=old; RenderTexture.active=active; RenderTexture.ReleaseTemporary(rt); }
            }
        }
        static void RuntimeCheck()
        {
            if(!EditorApplication.isPlaying) throw new Exception("Runtime check requires Play mode");
            var walker=Object.FindObjectOfType<PrototypeWalker>(); Layout(); terrain=Object.FindObjectOfType<Terrain>();
            var original=walker.transform.position; walker.Teleport(At(D)+Vector3.up*.1f); walker.SetOverview(true);
            bool overview=walker.mapCamera.enabled&&!walker.viewCamera.enabled; walker.SetOverview(false);
            bool walking=walker.viewCamera.enabled&&!walker.mapCamera.enabled;
            walker.Teleport(original);
            File.WriteAllText("PrototypeReview/runtime.txt","Play mode active\nThird person / overview switch: "+(overview&&walking?"PASS":"FAIL")+"\nTeleport controller enabled: "+walker.GetComponent<CharacterController>().enabled);
        }
        static void Inspect()
        {
            Layout(); terrain=Object.FindObjectOfType<Terrain>(); var data=terrain.terrainData; var log=new List<string>();
            foreach(var p in new[]{route[12],Vector2.zero,B,D})
            { int x=Mathf.Clamp((int)((p.x+Size/2)/Size*data.alphamapWidth),0,data.alphamapWidth-1),z=Mathf.Clamp((int)((p.y+Size/2)/Size*data.alphamapHeight),0,data.alphamapHeight-1); var aa=data.GetAlphamaps(x,z,1,1); log.Add(p+" alpha="+string.Join(",",Enumerable.Range(0,data.alphamapLayers).Select(k=>aa[0,0,k].ToString("F3")))); }
            log.Add("Terrain material "+terrain.materialTemplate.shader.name+" textures="+data.alphamapTextureCount);
            log.Add("Pipeline "+GraphicsSettings.currentRenderPipeline.name); log.Add("Quality "+QualitySettings.names[QualitySettings.GetQualityLevel()]);
            foreach(var l in data.terrainLayers) log.Add(l.name+" "+l.diffuseTexture.name);
            File.WriteAllLines("PrototypeReview/inspect.txt",log);
        }
        static void Finish()
        {
            terrain=Object.FindObjectOfType<Terrain>();
            var fence=GameObject.Find("A 天气屋 · 西岸贴水").transform.Find("Fence_1");
            if(fence!=null){fence.position=At(A+new Vector2(-3,9));fence.rotation=Quaternion.identity;}
            EditorSceneManager.SaveScene(SceneManager.GetActiveScene(),ScenePath);Validate();Capture();
        }
    }
}
