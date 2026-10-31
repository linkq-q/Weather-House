using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace WeatherHouse.EnvironmentLab.Editor
{
    public static class EnvironmentLabChecks
    {
        public const string Output="EnvironmentLabReview";
        public static void Assert(bool condition,string message)
        { if(!condition) throw new InvalidOperationException(message); }

        public static void Moss()
        {
            Assert(Application.isPlaying,"Moss tests require actual Play mode");
            var moss=Object.FindObjectOfType<MossCoverage>();
            Assert(moss && moss.targets.Length==5,"Five rock/log renderers saved");
            var originals=moss.targets.Select(r=>r.sharedMaterial).ToArray();
            Assert(originals.All(m=>m && !AssetDatabase.Contains(m)),"Runtime material copies bound");
            var shader=originals[0].shader;
            Assert(!ShaderUtil.GetShaderMessages(shader).Any(m=>m.severity==UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error),"Moss shader compilation");
            float saved=moss.coverage;
            var source=AssetDatabase.LoadAssetAtPath<Material>(EnvironmentLabBuilder.Generated+"/MossRock.mat");
            string sourceBefore=EditorJsonUtility.ToJson(source);
            moss.coverage=.9f; moss.Apply();
            Assert(moss.targets.All(r=>r.sharedMaterial.GetFloat("_CoverageLevel")>-.1f),"Coverage controls every sample");
            Assert(EditorJsonUtility.ToJson(source)==sourceBefore,"Persistent source unchanged while preview differs");
            moss.coverage=saved; moss.Apply();
            Assert(Mathf.Abs(source.GetFloat("_CoverageLevel")-Mathf.Lerp(-.85f,0,saved))<.0001f,"Persistent source material unchanged");
            var report=new StringBuilder("PASS actual Play; five saved samples; runtime clones; live coverage; source material preserved.\n");
            ProbeWorldCoverage(source,report);
            var view=Object.FindObjectOfType<EnvironmentLabView>();
            view.showPanel=false; view.Overview();
            Capture("01_Moss_Overview",view.GetComponent<Camera>(),1600,1000);
            moss.showMoss=false; moss.Apply(); Capture("02_Moss_Off",view.GetComponent<Camera>(),1600,1000);
            moss.showMoss=true; moss.Apply();
            view.navigation.target=new Vector3(0,1.35f,0); view.navigation.distance=7.2f; view.navigation.pitch=23; view.navigation.PlaceCamera();
            Capture("03_Moss_Close",view.GetComponent<Camera>(),1400,1000);
            view.Overview(); view.showPanel=true;
            report.AppendLine("PASS GPU captures: enabled/disabled overview, close view; "+SystemInfo.graphicsDeviceName);
            File.WriteAllText(Output+"/01_Moss_checks.txt",report.ToString());
        }

        static void ProbeWorldCoverage(Material source,StringBuilder report)
        {
            var go=GameObject.CreatePrimitive(PrimitiveType.Sphere); go.name="Temporary moss GPU probe";
            go.transform.position=new Vector3(1000,20,1000);
            var material=new Material(source); var renderer=go.GetComponent<Renderer>(); renderer.sharedMaterial=material;
            var camera=new GameObject("Temporary probe camera").AddComponent<Camera>();
            camera.enabled=false; camera.orthographic=true; camera.orthographicSize=.62f;
            camera.clearFlags=CameraClearFlags.SolidColor; camera.backgroundColor=Color.black;
            camera.transform.position=go.transform.position+new Vector3(0,0,-3); camera.transform.LookAt(go.transform);
            camera.nearClipPlane=.1f; camera.farClipPlane=6;
            try
            {
                material.SetTexture("_Albedo",Texture2D.whiteTexture); material.SetTexture("_CoverageAlbedo",Texture2D.whiteTexture);
                material.SetTexture("_CoverageMask",Texture2D.whiteTexture); material.SetFloat("_MaskContrast",1);
                material.SetFloat("_NormalScale",0); material.SetFloat("_CoverageNormalScale",0);
                material.SetColor("_Color",Color.red); material.SetColor("_CoverageColor",Color.green);
                material.SetFloat("_Metallic",0); material.SetFloat("_SurfaceSmoothness",0); material.SetFloat("_CoverageSmoothness",0);
                material.SetFloat("_CoverageLevel",-.05f); material.SetFloat("_CoverageFade",2);
                int[] upper=new int[3],lower=new int[3];
                for(int i=0;i<3;i++)
                {
                    go.transform.rotation=Quaternion.Euler(0,0,i*90);
                    var pixels=Render(camera,256,256);
                    try
                    {
                        Color[] colors=pixels.GetPixels();
                        for(int y=0;y<256;y++) for(int x=0;x<256;x++)
                        { Color c=colors[y*256+x]; if(c.g>c.r*1.5f && c.g>.08f) { if(y>128) upper[i]++; else lower[i]++; } }
                        File.WriteAllBytes(Output+"/Moss_probe_"+(i*90)+".png",pixels.EncodeToPNG());
                    }
                    finally { Object.DestroyImmediate(pixels); }
                    Assert(upper[i]>5000 && lower[i]<500,"GPU moss stays on world upper half at "+(i*90));
                }
                Assert((float)(upper.Max()-upper.Min())/upper.Max()<.08f,"Rotating mesh preserves world-facing coverage area");
                material.SetFloat("_CoverageLevel",-2);
                var disabled=Render(camera,128,128);
                try { Assert(disabled.GetPixels().Count(c=>c.g>c.r*1.5f && c.g>.08f)<10,"Coverage off removes GPU moss"); }
                finally { Object.DestroyImmediate(disabled); }
                report.AppendLine("PASS GPU world upper-face mask at 0/90/180 degrees. Upper green pixels: "+string.Join(",",upper)+"; lower: "+string.Join(",",lower)+"; disabled mask absent.");
            }
            finally { Object.DestroyImmediate(go); Object.DestroyImmediate(camera.gameObject); Object.DestroyImmediate(material); }
        }

        public static Texture2D Render(Camera camera,int width,int height)
        {
            var target=RenderTexture.GetTemporary(width,height,24,RenderTextureFormat.ARGB32,RenderTextureReadWrite.Default);
            var previous=camera.targetTexture; var active=RenderTexture.active;
            var texture=new Texture2D(width,height,TextureFormat.RGB24,false);
            try { camera.targetTexture=target; camera.Render(); RenderTexture.active=target; texture.ReadPixels(new Rect(0,0,width,height),0,0); texture.Apply(); return texture; }
            catch { Object.DestroyImmediate(texture); throw; }
            finally { camera.targetTexture=previous; RenderTexture.active=active; RenderTexture.ReleaseTemporary(target); }
        }
        public static void Capture(string name,Camera camera,int width=1600,int height=1000)
        {
            Directory.CreateDirectory(Output);
            var texture=Render(camera,width,height);
            try { File.WriteAllBytes(Output+"/"+name+".png",texture.EncodeToPNG()); }
            finally { Object.DestroyImmediate(texture); }
        }
    }
}
