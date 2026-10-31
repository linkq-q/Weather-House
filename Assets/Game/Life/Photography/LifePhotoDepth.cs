using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace WeatherHouse.Life
{
    // A shutter-scoped visibility query. Live preview does not allocate or render these buffers.
    // Compare the subject's own surface with the scene surface, not a bone inside its body.
    public sealed class LifePhotoDepth : IDisposable
    {
        const int Width=512;
        const float DepthQuantum=2f/16777215f;
        static LifePhotoDepth current;
#if UNITY_EDITOR
        public static string LastDiagnostic {get;private set;}
        public static string LastPointDiagnostic {get;private set;}
#endif
        readonly LifePhotoDepth previous;
        readonly Camera source,camera;
        readonly int height;
        readonly Material copy;
        readonly RenderTexture color,depth,readback;
        readonly Texture2D pixels;
        readonly float[] scene;
        readonly Dictionary<Transform,float[]> surfaces=new Dictionary<Transform,float[]>();
        readonly HashSet<Transform> transparent=new HashSet<Transform>();
        bool disposed;

        public static LifePhotoDepth Begin(Camera camera)=>new LifePhotoDepth(camera);
        public static bool ActiveFor(Camera camera)=>current!=null && current.source==camera;
        public static bool Visible(Camera camera,Vector3 viewport,Transform subject)=>
            ActiveFor(camera) && current.Query(viewport,subject);

        LifePhotoDepth(Camera original)
        {
            previous=current;source=original;height=Mathf.Max(1,Mathf.RoundToInt(Width/original.aspect));
            Shader shader=Resources.Load<Shader>("LifePhotoDepthCopy");
            if(!shader || !shader.isSupported)throw new InvalidOperationException("The photograph depth shader is unavailable or unsupported.");
            try
            {
            copy=new Material(shader){hideFlags=HideFlags.HideAndDontSave};
            color=RenderTexture.GetTemporary(Width,height,24,RenderTextureFormat.ARGB32,RenderTextureReadWrite.Linear);
            depth=RenderTexture.GetTemporary(Width,height,24,RenderTextureFormat.Depth,RenderTextureReadWrite.Linear);
            readback=RenderTexture.GetTemporary(Width,height,0,RenderTextureFormat.RFloat,RenderTextureReadWrite.Linear);
            color.filterMode=depth.filterMode=readback.filterMode=FilterMode.Point;
            pixels=new Texture2D(Width,height,TextureFormat.RFloat,false,true){hideFlags=HideFlags.HideAndDontSave};
            var go=new GameObject("Photograph depth query"){hideFlags=HideFlags.HideAndDontSave};
            camera=go.AddComponent<Camera>();camera.CopyFrom(original);camera.enabled=false;
            camera.transform.SetPositionAndRotation(original.transform.position,original.transform.rotation);
            camera.projectionMatrix=original.projectionMatrix;
            camera.targetTexture=color;camera.allowMSAA=false;camera.allowHDR=false;camera.allowDynamicResolution=false;
            camera.cullingMask&=~(1<<LayerMask.NameToLayer("UI"));
            var data=go.AddComponent<UniversalAdditionalCameraData>();data.requiresDepthTexture=true;
            data.requiresColorTexture=false;data.renderPostProcessing=false;data.renderShadows=false;
                bool copied=false;
                void CopyScene(ScriptableRenderContext context,Camera completed)
                {if(completed==camera){CopyToReadback(null,1);copied=true;}}
                RenderPipelineManager.endCameraRendering+=CopyScene;
                try{RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest{destination=color});}
                finally{RenderPipelineManager.endCameraRendering-=CopyScene;}
                if(!copied)throw new InvalidOperationException("The photograph camera did not complete its URP depth render request.");
                Texture rendered=Shader.GetGlobalTexture("_CameraDepthTexture");
                scene=ReadPixels();current=this;
#if UNITY_EDITOR
                float low=float.PositiveInfinity,high=float.NegativeInfinity;
                foreach(float value in scene){low=Mathf.Min(low,value);high=Mathf.Max(high,value);}
                LastDiagnostic="source="+original.name+" type="+original.cameraType+"; pipeline="+GraphicsSettings.currentRenderPipeline?.name+
                    "; renderScale="+UniversalRenderPipeline.asset.renderScale+"; target="+Width+"x"+height+
                    "; managedGlobal="+(rendered?rendered.name+" "+rendered.width+"x"+rendered.height:"null")+
                    "; rawRange="+low.ToString("R")+".."+high.ToString("R");
#endif
            }
            catch{Dispose();throw;}
        }
        bool Query(Vector3 viewport,Transform subject)
        {
            int x=Mathf.Clamp((int)(viewport.x*Width),0,Width-1),y=Mathf.Clamp((int)(viewport.y*height),0,height-1);
            int at=y*Width+x;float observed=scene[at];
            if(!subject)return InFront(EyeRaw(viewport.z),observed);
            if(!surfaces.TryGetValue(subject,out float[] surface))
            {
                surface=SubjectSurface(subject);surfaces.Add(subject,surface);
            }
            float own=surface[at];
#if UNITY_EDITOR
            LastPointDiagnostic="pixel="+x+","+y+"; scene="+observed.ToString("R")+"; subject="+own.ToString("R")+
                "; sceneMirroredY="+scene[(height-1-y)*Width+x].ToString("R")+"; eye="+viewport.z.ToString("R");
#endif
            if(IsBackground(own))return transparent.Contains(subject) && InFront(EyeRaw(viewport.z),observed);
            return Mathf.Abs(own-observed)<=DepthQuantum;
        }
        float[] SubjectSurface(Transform subject)
        {
            var command=new CommandBuffer{name="Photograph subject surface"};
            try
            {
                command.SetRenderTarget(color,depth);
                // Clear depth is Unity's logical far value; the backend applies reversed Z.
                CoreUtils.ClearRenderTarget(command,ClearFlag.All,Color.clear);
                RenderingUtils.SetViewAndProjectionMatrices(command,camera.worldToCameraMatrix,GL.GetGPUProjectionMatrix(camera.projectionMatrix,true),true);
                foreach(Renderer renderer in subject.GetComponentsInChildren<Renderer>())
                {
                    if(!renderer.enabled || renderer.forceRenderingOff || !renderer.gameObject.activeInHierarchy ||
                        (camera.cullingMask&(1<<renderer.gameObject.layer))==0)continue;
                    Material[] materials=renderer.sharedMaterials;
                    for(int sub=0;sub<materials.Length;sub++)
                    {
                        Material material=materials[sub];if(!material)continue;
                        if(material.renderQueue>(int)RenderQueue.GeometryLast && (!material.HasProperty("_ZWrite") || material.GetFloat("_ZWrite")==0))
                        {transparent.Add(subject);continue;}
                        // Use the material's own clipping/vertex deformation, including cutout foliage.
                        int pass=material.FindPass("DepthOnly");
                        if(pass<0)pass=material.FindPass("ForwardLit");
                        if(pass<0)pass=0;
                        command.DrawRenderer(renderer,material,sub,pass);
                    }
                }
                Graphics.ExecuteCommandBuffer(command);return Read(depth);
            }
            finally{command.Release();}
        }
        float[] Read(Texture texture)
        {CopyToReadback(texture,0);return ReadPixels();}
        void CopyToReadback(Texture texture,int pass)
        {
            var command=new CommandBuffer{name="Photograph depth readback"};
            try
            {
                if(texture)copy.SetTexture("_MainTex",texture);
                command.SetRenderTarget(readback);command.SetViewport(new Rect(0,0,Width,height));
                command.DrawProcedural(Matrix4x4.identity,copy,pass,MeshTopology.Triangles,3,1);
                Graphics.ExecuteCommandBuffer(command);
            }
            finally{command.Release();}
        }
        float[] ReadPixels()
        {
            RenderTexture active=RenderTexture.active;
            try
            {
                RenderTexture.active=readback;
                pixels.ReadPixels(new Rect(0,0,Width,height),0,0,false);
                return pixels.GetRawTextureData<float>().ToArray();
            }
            finally{RenderTexture.active=active;}
        }
        float EyeRaw(float eye)
        {
            float near=camera.nearClipPlane,far=camera.farClipPlane;
            float value=camera.orthographic?(eye-near)/(far-near):far/(far-near)-far*near/((far-near)*eye);
            return SystemInfo.usesReversedZBuffer?1-value:value;
        }
        static bool IsBackground(float value)=>SystemInfo.usesReversedZBuffer?value<=DepthQuantum:value>=1-DepthQuantum;
        static bool InFront(float candidate,float observed)=>SystemInfo.usesReversedZBuffer?
            candidate>=observed-DepthQuantum:candidate<=observed+DepthQuantum;
        public void Dispose()
        {
            if(disposed)return;disposed=true;if(current==this)current=previous;
            if(camera)UnityEngine.Object.DestroyImmediate(camera.gameObject);
            if(copy)UnityEngine.Object.DestroyImmediate(copy);if(pixels)UnityEngine.Object.DestroyImmediate(pixels);
            if(color)RenderTexture.ReleaseTemporary(color);if(depth)RenderTexture.ReleaseTemporary(depth);if(readback)RenderTexture.ReleaseTemporary(readback);
            surfaces.Clear();transparent.Clear();
        }
    }
}
