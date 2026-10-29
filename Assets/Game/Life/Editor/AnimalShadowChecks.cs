using System;
using System.IO;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using WeatherHouse.AnimalToon;
using WeatherHousePrototype;

namespace WeatherHouse.Life.Editor
{
    public static class AnimalShadowChecks
    {
        public static void Run(GameplaySession game)
        {
            const string folder="PhotoStillReview/20261029/Animals/";
            Directory.CreateDirectory(folder);
            var root=new GameObject("Isolated animal shadow fixture");
            var animal=new Material(Resources.Load<Material>("AnimalPaper"));
            var ground=new Material(Shader.Find("Universal Render Pipeline/Lit"));
            var images=new List<Texture2D>();
            var sun=RenderSettings.sun;var rotation=sun.transform.rotation;
            bool fog=RenderSettings.fog;
            try
            {
                root.transform.position=new Vector3(500,500,500);
                sun.transform.rotation=Quaternion.Euler(48,0,0);RenderSettings.fog=false;
                animal.SetTexture("_BaseMap",Texture2D.whiteTexture);animal.SetColor("_BaseColor",new Color(.8f,.16f,.08f));
                animal.SetFloat("_OutlineWidth",0);animal.SetFloat("_EnvironmentShadowsOnly",1);
                ground.SetColor("_BaseColor",new Color(.15f,.65f,.18f));
                Renderer Box(string name,Vector3 position,Vector3 scale,Material mat)
                {
                    var go=GameObject.CreatePrimitive(PrimitiveType.Cube);go.name=name;go.layer=31;
                    go.transform.SetParent(root.transform,false);go.transform.localPosition=position;go.transform.localScale=scale;
                    var r=go.GetComponent<Renderer>();r.sharedMaterial=mat;return r;
                }
                var body=Box("Body",new Vector3(0,1,0),new Vector3(2,2,.6f),animal);
                var head=Box("Overhanging body part",new Vector3(0,2,-.65f),new Vector3(1.5f,.4f,1.8f),animal);
                Box("Ground",new Vector3(0,-.1f,0),new Vector3(10,.2f,10),ground);
                var canopy=Box("External canopy",new Vector3(0,4,-2),new Vector3(4,.3f,3),ground);
                canopy.shadowCastingMode=ShadowCastingMode.ShadowsOnly;canopy.enabled=false;
                var cameraObject=new GameObject("Shadow regression camera",typeof(Camera),typeof(UniversalAdditionalCameraData),typeof(AnimalToonView));
                cameraObject.transform.SetParent(root.transform,false);var camera=cameraObject.GetComponent<Camera>();
                camera.CopyFrom(game.PlayerViewCamera);camera.enabled=false;camera.cullingMask=1<<31;
                camera.orthographic=true;camera.orthographicSize=3.4f;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.black;
                camera.transform.position=root.transform.position+new Vector3(3,3,-6);camera.transform.LookAt(root.transform.position+Vector3.up);
                cameraObject.GetComponent<UniversalAdditionalCameraData>().renderPostProcessing=false;
                cameraObject.GetComponent<AnimalToonView>().mergeRadius=0;
                Texture2D Capture(string name)
                {
                    var rt=RenderTexture.GetTemporary(640,640,24,RenderTextureFormat.ARGB32);var previous=RenderTexture.active;
                    var image=new Texture2D(640,640,TextureFormat.RGB24,false);images.Add(image);
                    try {camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,640,640),0,0);image.Apply();File.WriteAllBytes(folder+name+".png",image.EncodeToPNG());}
                    finally {camera.targetTexture=null;RenderTexture.active=previous;RenderTexture.ReleaseTemporary(rt);}
                    return image;
                }
                animal.SetFloat("_EnvironmentShadowsOnly",0);var before=Capture("shadow-all");
                animal.SetFloat("_EnvironmentShadowsOnly",1);var after=Capture("shadow-environment");
                canopy.enabled=true;var environment=Capture("shadow-canopy");canopy.enabled=false;
                body.shadowCastingMode=head.shadowCastingMode=ShadowCastingMode.Off;var noGround=Capture("shadow-ground-control");
                int Diff(Texture2D a,Texture2D b,bool red)
                {
                    var aa=a.GetPixels32();var bb=b.GetPixels32();int count=0;
                    for(int i=0;i<aa.Length;i++)
                    {
                        bool match=red?aa[i].r>aa[i].g*1.5f:aa[i].g>aa[i].r*1.5f;
                        if(match&&Math.Abs(aa[i].r-bb[i].r)+Math.Abs(aa[i].g-bb[i].g)+Math.Abs(aa[i].b-bb[i].b)>20)count++;
                    }
                    return count;
                }
                int self=Diff(before,after,true), external=Diff(after,environment,true),groundKept=Diff(after,noGround,false),groundUnchanged=Diff(before,after,false);
                string report=$"self-shadow changed pixels={self}; external canopy changed animal pixels={external}; ground shadow pixels={groundKept}; ground changed by receiver switch={groundUnchanged}";
                File.WriteAllText(folder+"shadow-pixels.txt",report);
                if(self<100||external<100||groundKept<100||groundUnchanged>10)throw new InvalidOperationException(report);
                File.AppendAllText(folder+"shadow-pixels.txt","\nPASS selective shadow rendering, ordinary ground atlas unchanged.");
            }
            finally
            {
                sun.transform.rotation=rotation;RenderSettings.fog=fog;
                UnityEngine.Object.DestroyImmediate(root);UnityEngine.Object.DestroyImmediate(animal);UnityEngine.Object.DestroyImmediate(ground);
                foreach(var image in images)UnityEngine.Object.DestroyImmediate(image);
            }
        }
    }
}
