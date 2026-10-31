using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using WeatherHouse.Vegetation;
using Object = UnityEngine.Object;

namespace WeatherHouse.EnvironmentLab.Editor
{
    public static class EnvironmentLabBuilder
    {
        public const string Root = "Assets/Experiments/Environment";
        public const string Generated = Root + "/Generated";
        public const string MossScene = "Assets/Scenes/Experimental/01_MossStudy.unity";
        const string Highlands = "Assets/HIGHLANDS - Stylized Environment";

        [MenuItem("Weather House/Environment Lab/01 苔藓 · 打开样板")]
        public static void OpenMoss()
        {
            RequireSavedScene();
            if (File.Exists(MossScene)) { EditorSceneManager.OpenScene(MossScene); return; }
            Directory.CreateDirectory(Generated); AssetDatabase.Refresh();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Lighting(false);
            var ground = Material("StudyGround", "Universal Render Pipeline/Lit", m => {
                m.SetColor("_BaseColor", new Color(.39f,.43f,.32f)); m.SetFloat("_Smoothness", .05f);
            });
            Box("Ground", new Vector3(0,-.2f,1), new Vector3(24,.4f,20), ground);
            var stone = CopyMaterial(Highlands + "/Materials/Forest/Stones_Rocks(Grass).mat", "MossRock");
            var wood = CopyMaterial(Highlands + "/Materials/Forest/Logs.mat", "MossLog");
            var moss = new GameObject("01 苔藓参数 · 进入Play试调").AddComponent<MossCoverage>();
            moss.ApplyTo(stone); moss.ApplyTo(wood);
            stone.SetTexture("_CoverageAlbedo", AssetDatabase.LoadAssetAtPath<Texture2D>(Highlands+"/Textures/Forest/Moss_Albedo.png"));
            stone.SetTexture("_CoverageMask", stone.GetTexture("_CoverageAlbedo"));
            stone.SetTexture("_CoverageNormal", AssetDatabase.LoadAssetAtPath<Texture2D>(Highlands+"/Textures/Forest/Moss_Normal.png"));
            stone.SetFloat("_CoverageNormalScale", .45f); wood.SetFloat("_CoverageNormalScale", .45f);
            stone.SetFloat("_CoverageSmoothness", .12f); wood.SetFloat("_CoverageSmoothness", .12f);
            stone.SetFloat("_SurfaceSmoothness", .1f); wood.SetFloat("_SurfaceSmoothness", .12f);
            EditorUtility.SetDirty(stone); EditorUtility.SetDirty(wood);
            var slab = Material("WarmDisplayStone", "Universal Render Pipeline/Lit", m => {
                m.SetColor("_BaseColor", new Color(.48f,.46f,.36f)); m.SetFloat("_Smoothness", .08f);
            });
            var roots = new GameObject[5];
            for (int i=0; i<3; i++)
            {
                float x=(i-1)*4;
                Box("Stone base", new Vector3(x,.08f,0), new Vector3(3.55f,.16f,3.3f), slab);
                roots[i]=Specimen("Rock_1(Grass)", "Rock " + (i*90) + " degrees", new Vector3(x,.18f,0), 2.9f, Quaternion.Euler(0,0,i*90), stone);
                Label(i*90 + " deg", new Vector3(x,.24f,-2), .07f);
            }
            roots[3]=Specimen("Log_1", "Moss log A", new Vector3(-3,0,5), 4.2f, Quaternion.Euler(0,22,0), wood);
            roots[4]=Specimen("Log_1", "Moss log B", new Vector3(3,0,5), 4.2f, Quaternion.Euler(0,-24,90), wood);
            moss.targets=roots.SelectMany(x=>x.GetComponentsInChildren<Renderer>()).ToArray();
            var view = CameraAndView(new Vector3(0,1,1.2f), 16.5f);
            view.moss = moss; view.rotatingRock = roots[1].transform;
            view.title="第一批 · 朝上苔藓";
            AssetDatabase.SaveAssets();
            EditorSceneManager.SaveScene(scene, MossScene);
            Selection.activeGameObject=moss.gameObject;
        }

        public static void RequireSavedScene()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || SceneManager.sceneCount != 1 || SceneManager.GetActiveScene().isDirty)
                throw new InvalidOperationException("请先结束其他测试并保存当前场景。样板不会覆盖未保存工作。");
        }

        public static Material CopyMaterial(string sourcePath, string name)
        {
            string path=Generated + "/"+name+".mat";
            var existing=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(existing) return existing;
            var source=AssetDatabase.LoadAssetAtPath<Material>(sourcePath);
            if(!source) throw new InvalidOperationException("Missing material: "+sourcePath);
            var created=new Material(source) { name=name };
            AssetDatabase.CreateAsset(created,path); return created;
        }

        public static Material Material(string name, string shaderName, Action<Material> configure)
        {
            string path=Generated+"/"+name+".mat";
            var existing=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(existing) return existing;
            var shader=Shader.Find(shaderName);
            if(!shader) throw new InvalidOperationException("Missing shader: "+shaderName);
            var created=new Material(shader) { name=name }; configure(created);
            AssetDatabase.CreateAsset(created,path); return created;
        }

        public static GameObject Box(string name, Vector3 position, Vector3 scale, Material material)
        {
            var go=GameObject.CreatePrimitive(PrimitiveType.Cube); go.name=name;
            go.transform.position=position; go.transform.localScale=scale;
            go.GetComponent<Renderer>().sharedMaterial=material; return go;
        }

        public static GameObject Specimen(string asset, string name, Vector3 basePosition, float size, Quaternion rotation, Material material)
        {
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(Highlands+"/Prefabs/Forest/"+asset+".prefab");
            if(!prefab) throw new InvalidOperationException("Missing specimen "+asset);
            // LOD0 mesh only: leave original prefab, colliders, and LOD settings untouched.
            var source=prefab.GetComponent<MeshFilter>();
            var root=new GameObject(name);
            var meshObject=new GameObject("Visible mesh"); meshObject.transform.SetParent(root.transform,false);
            meshObject.AddComponent<MeshFilter>().sharedMesh=source.sharedMesh;
            var renderer=meshObject.AddComponent<MeshRenderer>(); renderer.sharedMaterial=material;
            Bounds b=source.sharedMesh.bounds;
            float scale=size/Mathf.Max(b.size.x,Mathf.Max(b.size.y,b.size.z));
            meshObject.transform.localScale=Vector3.one*scale;
            meshObject.transform.localPosition=-b.center*scale;
            root.transform.rotation=rotation;
            root.transform.position=basePosition+Vector3.up*(basePosition.y-renderer.bounds.min.y);
            root.transform.position-=Vector3.up*basePosition.y;
            return root;
        }

        public static void Label(string text, Vector3 position, float size)
        {
            var label=new GameObject(text).AddComponent<TextMesh>(); label.text=text;
            label.fontSize=64; label.characterSize=size; label.anchor=TextAnchor.MiddleCenter;
            label.color=new Color(.88f,.91f,.78f); label.transform.position=position;
            label.transform.rotation=Quaternion.Euler(65,0,0);
        }

        public static void Lighting(bool fog)
        {
            var sun=new GameObject("Soft afternoon sun").AddComponent<Light>();
            sun.type=LightType.Directional; sun.intensity=1.05f; sun.color=new Color(1,.95f,.84f);
            sun.transform.rotation=Quaternion.Euler(45,-28,0); sun.shadows=LightShadows.Soft;
            sun.shadowBias=.025f; sun.shadowNormalBias=.25f;
            RenderSettings.sun=sun; RenderSettings.ambientMode=AmbientMode.Trilight;
            RenderSettings.ambientSkyColor=new Color(.62f,.70f,.76f);
            RenderSettings.ambientEquatorColor=new Color(.42f,.49f,.40f);
            RenderSettings.ambientGroundColor=new Color(.25f,.29f,.21f);
            RenderSettings.fog=fog; RenderSettings.fogMode=FogMode.Linear;
            RenderSettings.fogColor=new Color(.64f,.74f,.72f); RenderSettings.fogStartDistance=28; RenderSettings.fogEndDistance=95;
            RenderSettings.skybox=AssetDatabase.LoadAssetAtPath<Material>("Assets/Experiments/Vegetation/Generated/QuietDaySky.mat");
        }

        public static EnvironmentLabView CameraAndView(Vector3 target,float distance)
        {
            var camera=new GameObject("Main Camera").AddComponent<Camera>(); camera.tag="MainCamera";
            camera.fieldOfView=45; camera.nearClipPlane=.08f; camera.farClipPlane=200;
            camera.clearFlags=CameraClearFlags.Skybox; camera.allowHDR=true;
            var data=camera.GetUniversalAdditionalCameraData(); data.requiresDepthTexture=true;
            camera.gameObject.AddComponent<AudioListener>();
            var nav=camera.gameObject.AddComponent<GrassLabCamera>(); nav.target=target;
            nav.yaw=0; nav.pitch=24; nav.distance=distance; nav.groundHalfExtents=new Vector2(11,9);
            nav.PlaceCamera();
            var view=camera.gameObject.AddComponent<EnvironmentLabView>();
            view.navigation=nav; view.overviewTarget=target; view.overviewDistance=distance;
            return view;
        }
    }
}
