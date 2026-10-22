using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace WeatherHouse.OriginalTrees.Editor
{
    public static class AuthorTreeStudy
    {
        public const string Root = "Assets/WeatherHouseOriginalTrees";
        public const string ScenePath = Root + "/OriginalTreesComparison.unity";
        public const string Output = "OriginalTreesReview";
        public static readonly string[] Species = { "Birch", "Beech", "FirTree" };

        [MenuItem("Weather House/Original Trees/打开原作者树木对照")]
        public static void Open()
        {
            RequireSaved();
            if (!File.Exists(ScenePath)) Build();
            else EditorSceneManager.OpenScene(ScenePath);
        }

        public static void RequireSaved()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || SceneManager.sceneCount != 1 ||
                SceneManager.GetActiveScene().isDirty || string.IsNullOrEmpty(SceneManager.GetActiveScene().path))
                throw new InvalidOperationException("需要一个已保存且未进入 Play 的场景；当前编辑内容已保留。");
        }

        public static void Build()
        {
            RequireSaved();
            var originalScene = SceneManager.GetActiveScene();
            string originalPath = originalScene.path;
            try { BuildSavedScene(); }
            catch
            {
                // Synchronous construction owns this replacement scene, including a dirty partial build.
                if (SceneManager.GetActiveScene().handle != originalScene.handle)
                    EditorSceneManager.OpenScene(originalPath);
                throw;
            }
        }

        static void BuildSavedScene()
        {
            Directory.CreateDirectory(Root + "/Materials");
            Directory.CreateDirectory(Root + "/Prefabs");
            AuthorTreeMaterials.Create();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            for (int i = 0; i < Species.Length; i++)
            {
                string name = Species[i];
                var original = AssetDatabase.LoadAssetAtPath<GameObject>(Root + "/Models/SM_" + name + ".fbx");
                if (!original) throw new InvalidOperationException("Missing original tree " + name);
                var tree = (GameObject)PrefabUtility.InstantiatePrefab(original);
                tree.name = "Author " + name;
                tree.transform.position = new Vector3((i - 1) * 4, 0, 0);
                // Imported FBX maps UE (X,Y,Z) to Unity (X,Z,-Y); rotate -90 so the study uses (Y,Z,X).
                tree.transform.rotation = Quaternion.Euler(0, -90, 0);
                var trunk = LoadMaterial("Trunk_" + name);
                var foliage = LoadMaterial("Foliage_" + name);
                foreach (var renderer in tree.GetComponentsInChildren<MeshRenderer>())
                {
                    renderer.sharedMaterials = name == "FirTree" ? new[] { foliage, trunk } : new[] { trunk, foliage };
                    renderer.shadowCastingMode = ShadowCastingMode.TwoSided;
                }
                tree.AddComponent<AuthorTreeBounds>();
                var position=tree.transform.position;
                tree.transform.position=Vector3.zero;
                Assert(PrefabUtility.SaveAsPrefabAsset(tree,Root+"/Prefabs/Author_"+name+".prefab"),"Save reusable "+name+" prefab");
                tree.transform.position=position;
            }
            var sun = new GameObject("Author directional light").AddComponent<Light>();
            // UE Lambert includes 1/PI; URP Lit omits it. Convert the source illuminance for the study.
            sun.type = LightType.Directional; sun.intensity = 6 / Mathf.PI; sun.color = Color.white;
            sun.transform.rotation = Quaternion.LookRotation(new Vector3(-.116f, -.761f, .639f));
            sun.shadows = LightShadows.Soft; sun.shadowBias = .02f; sun.shadowNormalBias = .15f;
            RenderSettings.sun = sun;
            RenderSettings.fog = false;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(.55f,.65f,.78f);
            RenderSettings.ambientEquatorColor = new Color(.34f,.39f,.46f);
            RenderSettings.ambientGroundColor = new Color(.23f,.22f,.20f);
            RenderSettings.skybox = Material("StudySky", "Weather House/Original Trees/Study Sky");

            string checkerPath = Root + "/Materials/StudyChecker.asset";
            var checker = AssetDatabase.LoadAssetAtPath<Texture2D>(checkerPath);
            if (!checker)
            {
                checker = new Texture2D(2, 2, TextureFormat.RGB24, false) { name="StudyChecker", filterMode=FilterMode.Point, wrapMode=TextureWrapMode.Repeat };
                var a = new Color(.48f,.48f,.48f); var b = new Color(.54f,.54f,.54f);
                checker.SetPixels(new[] { a,b,b,a }); checker.Apply();
                AssetDatabase.CreateAsset(checker, checkerPath);
            }
            var groundMat = Material("StudyGround", "Universal Render Pipeline/Lit");
            groundMat.SetTexture("_BaseMap", checker); groundMat.SetTextureScale("_BaseMap", new Vector2(45,45));
            groundMat.SetFloat("_Smoothness",0); groundMat.SetColor("_BaseColor",Color.white);
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane); ground.name="Comparison ground";
            ground.transform.localScale = Vector3.one * 6;
            ground.GetComponent<Renderer>().sharedMaterial=groundMat;
            Object.DestroyImmediate(ground.GetComponent<Collider>());

            var camera = new GameObject("Main Camera").AddComponent<Camera>(); camera.tag="MainCamera";
            camera.transform.position=new Vector3(-.36f,13.48f,-30.5f);
            camera.transform.LookAt(new Vector3(-.36f,8.1f,0));
            camera.fieldOfView=35; camera.nearClipPlane=.1f; camera.farClipPlane=200;
            camera.allowHDR=true; camera.clearFlags=CameraClearFlags.Skybox;
            camera.GetUniversalAdditionalCameraData().renderPostProcessing=true;
            camera.gameObject.AddComponent<AudioListener>();
            var volume = new GameObject("Comparison exposure").AddComponent<Volume>(); volume.isGlobal=true;
            string profilePath=Root+"/Materials/StudyExposure.asset";
            var profile=AssetDatabase.LoadAssetAtPath<VolumeProfile>(profilePath);
            if (!profile)
            {
                profile=ScriptableObject.CreateInstance<VolumeProfile>(); AssetDatabase.CreateAsset(profile,profilePath);
                var tonemap=profile.Add<Tonemapping>(true); tonemap.mode.value=TonemappingMode.ACES;
                AssetDatabase.AddObjectToAsset(tonemap,profile);
                EditorUtility.SetDirty(profile);
            }
            volume.sharedProfile=profile;
            EditorUtility.SetDirty(groundMat);
            foreach (string guid in AssetDatabase.FindAssets("", new[] { Root + "/Materials" }))
            {
                var asset = AssetDatabase.LoadMainAssetAtPath(AssetDatabase.GUIDToAssetPath(guid));
                if (asset) AssetDatabase.SaveAssetIfDirty(asset);
            }
            Assert(EditorSceneManager.SaveScene(scene,ScenePath), "Could not save the original tree comparison scene");
            AuthorTreeLighting.ApplySaved();
        }

        public static Material LoadMaterial(string name) => AssetDatabase.LoadAssetAtPath<Material>(Root+"/Materials/"+name+".mat");
        public static Material Material(string name,string shaderName)
        {
            var material=LoadMaterial(name);
            var shader=Shader.Find(shaderName);
            if (!shader) throw new InvalidOperationException("Missing shader "+shaderName);
            if (!material) { material=new Material(shader) { name=name }; AssetDatabase.CreateAsset(material,Root+"/Materials/"+name+".mat"); }
            return material;
        }

        public static void Assert(bool condition,string message)
        { if (!condition) throw new InvalidOperationException(message); }

        public static void VerifyAndCapture()
        {
            Assert(Application.isPlaying,"Captures must run in actual Play mode");
            var report=new StringBuilder("Original trees — static material port\nUTC "+DateTime.UtcNow.ToString("O")+"\nGPU "+SystemInfo.graphicsDeviceName+"\n");
            var camera=Camera.main; Assert(camera,"Main camera exists");
            float[] heights={15.96560f,9.10423f,6.61013f}; int[] triangles={7296,10389,27268};
            for(int i=0;i<Species.Length;i++)
            {
                string name=Species[i]; var tree=GameObject.Find("Author "+name);
                Assert(tree,"Saved original tree "+name);
                var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(Root+"/Prefabs/Author_"+name+".prefab");
                Assert(prefab && prefab.GetComponent<AuthorTreeBounds>(),name+" reusable prefab and leaf bounds helper saved");
                Assert(Vector3.Distance(tree.transform.position,new Vector3((i-1)*4,0,0))<.001f,name+" original world position and 4 m tree spacing");
                Assert(Vector3.Distance(tree.transform.lossyScale,Vector3.one)<.001f,name+" original unit world scale");
                var meshFilter=tree.GetComponentInChildren<MeshFilter>();
                Assert(Vector3.Distance(meshFilter.transform.lossyScale,Vector3.one)<.001f,name+" mesh unit world scale");
                var mesh=meshFilter.sharedMesh;
                var renderer=tree.GetComponentInChildren<Renderer>();
                Assert(renderer.localBounds.size.y>=mesh.bounds.size.y+(name=="Birch"?1.19f:name=="Beech"?.89f:0),name+" GPU leaf culling margin survives scene reload");
                float min=mesh.vertices.Min(v=>v.y),max=mesh.vertices.Max(v=>v.y);
                Assert(Mathf.Abs(max-min-heights[i])<.01f,name+" imported metre-scale height matches original");
                float worldMin=mesh.vertices.Min(v=>meshFilter.transform.TransformPoint(v).y);
                float worldMax=mesh.vertices.Max(v=>meshFilter.transform.TransformPoint(v).y);
                Assert(Mathf.Abs(worldMax-worldMin-heights[i])<.01f,name+" world-space height matches original");
                Assert(mesh.triangles.Length/3==triangles[i],name+" original triangle count preserved");
                Assert(mesh.normals.Length==mesh.vertexCount && mesh.tangents.Length==mesh.vertexCount,name+" normals and tangents present");
                Assert(mesh.uv.Length==mesh.vertexCount && mesh.uv2.Length==mesh.vertexCount && mesh.uv3.Length==mesh.vertexCount,name+" three UV channels present");
                var materials=tree.GetComponentInChildren<Renderer>().sharedMaterials;
                Assert(materials.Length==2 && materials.All(m=>m && m.shader.isSupported),name+" both material slots supported");
                Assert(materials[name=="FirTree"?0:1].name=="Foliage_"+name,name+" original foliage slot");
                foreach(var m in materials)
                    Assert(!ShaderUtil.GetShaderMessages(m.shader).Any(msg=>msg.severity==UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error),m.shader.name+" compilation");
                report.AppendLine(AuthorTreeSourceChecks.Verify(tree,name));
                report.AppendLine("PASS "+name+": "+(max-min).ToString("F5")+" m local/world height; original position and unit world scale; "+triangles[i]+" triangles; normals/tangents/UV0-2 present; correct material slots.");
                for(int v=0;v<Math.Min(4,mesh.vertexCount);v++)
                    report.AppendLine("  vertex "+v+" pos="+mesh.vertices[v].ToString("F5")+" normal="+mesh.normals[v].ToString("F5")+" tangent="+mesh.tangents[v].ToString("F5")+" uv="+mesh.uv[v].ToString("F5"));
            }
            var sh=RenderSettings.ambientProbe;
            report.AppendLine("Ambient SH L0 RGB: "+sh[0,0]+", "+sh[1,0]+", "+sh[2,0]);
            string lightingReport=AuthorTreeLighting.Verify();
            lightingReport+="\n"+AuthorTreeBiasProbe.Run();
            report.AppendLine(lightingReport);
            File.WriteAllText(Output+"/lighting-checks.txt", "UTC "+DateTime.UtcNow.ToString("O")+"\nGPU "+SystemInfo.graphicsDeviceName+" / "+SystemInfo.graphicsDeviceType+"\n"+lightingReport+"\n");
            Capture(camera,"Unity_ThreeTrees",1200,1200);
            var position=camera.transform.position; var rotation=camera.transform.rotation; float fov=camera.fieldOfView;
            try
            {
                for(int i=0;i<Species.Length;i++)
                {
                    float height=new[]{15.4f,8.18f,6.1f}[i];
                    camera.transform.position=new Vector3((i-1)*4,height*.63f,-height*1.8f);
                    camera.transform.LookAt(new Vector3((i-1)*4,height*.5f,0)); camera.fieldOfView=35;
                    Capture(camera,"Unity_"+Species[i],900,1200);
                }
            }
            finally { camera.transform.position=position; camera.transform.rotation=rotation; camera.fieldOfView=fov; }
            var birch=LoadMaterial("Foliage_Birch"); var before=birch.GetFloat("_IsFluffy"); var beforeCutoff=birch.GetFloat("_Cutoff");
            var runtime=new Material(birch); var birchRenderer=GameObject.Find("Author Birch").GetComponentInChildren<Renderer>();
            var saved=birchRenderer.sharedMaterials;
            Texture2D a=null,b=null,masked=null;
            try
            {
                var changed=(Material[])saved.Clone(); changed[1]=runtime; birchRenderer.sharedMaterials=changed;
                runtime.SetFloat("_IsFluffy",1); a=Render(camera,512,512);
                runtime.SetFloat("_IsFluffy",0); b=Render(camera,512,512);
                int diff=ChangedPixels(a,b);
                Assert(diff>500,"GPU responds to original fluffy displacement");
                report.AppendLine("PASS GPU original fluffy displacement: "+diff+" pixels changed in 512 square capture.");
                runtime.SetFloat("_IsFluffy",1);runtime.SetFloat("_Cutoff",1.1f);
                masked=Render(camera,512,512);
                SaveCapture(masked,"Probe_LeafMaskOff");
                int maskDiff=ChangedPixels(a,masked);
                Assert(maskDiff>500,"GPU leaf clipping removes visible foliage");
                report.AppendLine("PASS GPU leaf clipping: "+maskDiff+" pixels changed with all Birch leaves clipped.");
            }
            finally
            {
                birchRenderer.sharedMaterials=saved;Object.DestroyImmediate(runtime);
                if(a) Object.DestroyImmediate(a);if(b) Object.DestroyImmediate(b);if(masked) Object.DestroyImmediate(masked);
            }
            Assert(birch.GetFloat("_IsFluffy")==before && birch.GetFloat("_Cutoff")==beforeCutoff,"GPU probes preserve source material");
            report.AppendLine("PASS actual Play captures. Artistic similarity remains a visual review, not a test assertion.");
            File.WriteAllText(Output+"/checks.txt",report.ToString());
        }

        internal static Texture2D Render(Camera camera,int width,int height)
        {
            var target=RenderTexture.GetTemporary(width,height,24,RenderTextureFormat.ARGB32);
            var previous=camera.targetTexture;var active=RenderTexture.active;
            var texture=new Texture2D(width,height,TextureFormat.RGB24,false);
            try { camera.targetTexture=target;camera.Render();RenderTexture.active=target;texture.ReadPixels(new Rect(0,0,width,height),0,0);texture.Apply();return texture; }
            catch { Object.DestroyImmediate(texture);throw; }
            finally { camera.targetTexture=previous;RenderTexture.active=active;RenderTexture.ReleaseTemporary(target); }
        }
        internal static void Capture(Camera camera,string name,int width,int height)
        {
            Directory.CreateDirectory(Output);var texture=Render(camera,width,height);
            try { SaveCapture(texture,name); }
            finally { Object.DestroyImmediate(texture); }
        }
        static void SaveCapture(Texture2D texture,string name)
        {
            int magenta=texture.GetPixels32().Count(c=>c.r>220 && c.b>220 && c.g<30);
            Assert(magenta<100,"No pink error shader in "+name);
            File.WriteAllBytes(Output+"/"+name+".png",texture.EncodeToPNG());
        }
        static int ChangedPixels(Texture2D a,Texture2D b)
        {
            var aa=a.GetPixels32();var bb=b.GetPixels32();int diff=0;
            for(int p=0;p<aa.Length;p++) if(Mathf.Abs(aa[p].r-bb[p].r)+Mathf.Abs(aa[p].g-bb[p].g)+Mathf.Abs(aa[p].b-bb[p].b)>24) diff++;
            return diff;
        }
    }
}
