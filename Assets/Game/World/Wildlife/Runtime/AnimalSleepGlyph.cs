using UnityEngine;
using UnityEngine.Rendering;

namespace WeatherHouse.Hillside
{
    // Three reused rounded strokes: no font dependency, per-frame text rebuild or particle allocations.
    public sealed class AnimalSleepGlyph : MonoBehaviour
    {
        public bool Sleeping;
        public float height = 1;
        LineRenderer[] symbols;
        Material material;
        float age;
        void OnEnable()
        {
            Camera.onPreCull+=FaceCamera;
            RenderPipelineManager.beginCameraRendering+=BeforeCamera;
        }
        void OnDisable()
        {
            Camera.onPreCull-=FaceCamera;
            RenderPipelineManager.beginCameraRendering-=BeforeCamera;
            Hide();
        }
        void Create()
        {
            Shader shader=Shader.Find("Sprites/Default");
            if(!shader)return;
            material=new Material(shader){hideFlags=HideFlags.HideAndDontSave};
            material.color=Color.white;
            symbols=new LineRenderer[3];
            for(int i=0;i<symbols.Length;i++)
            {
                var go=new GameObject("Sleep Z");go.transform.SetParent(transform,false);
                var line=go.AddComponent<LineRenderer>();line.sharedMaterial=material;
                line.useWorldSpace=true;line.positionCount=4;line.widthMultiplier=.045f;
                line.alignment=LineAlignment.View;
                line.numCornerVertices=12;line.numCapVertices=12;
                line.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;line.receiveShadows=false;
                symbols[i]=line;
            }
        }
        void LateUpdate()
        {
            if(!Sleeping){age=0;Hide();return;}
            age+=Time.deltaTime;
        }
        void Hide(){if(symbols!=null)foreach(LineRenderer symbol in symbols)symbol.enabled=false;}
        void BeforeCamera(ScriptableRenderContext context,Camera camera)=>FaceCamera(camera);
        void FaceCamera(Camera view)
        {
            if(!Sleeping || !view || Vector3.SqrMagnitude(view.transform.position-transform.position)>900)
            {Hide();return;}
            if(symbols==null)Create();if(symbols==null)return;
            // Each rendering camera needs its own billboard; world points avoid inherited body scale/skew.
            Vector3 right=view.transform.right,up=view.transform.up;
            for(int i=0;i<symbols.Length;i++)
            {
                float phase=Mathf.Repeat(age/2.4f-i/3f,1);
                LineRenderer symbol=symbols[i];symbol.enabled=true;
                Vector3 center=transform.position+Vector3.up*(height+phase*.9f)+right*Mathf.Sin(phase*3+i)*.07f;
                float scale=.65f+phase*.7f;
                Vector3 x=right*(.07f*scale),y=up*(.09f*scale);
                symbol.SetPosition(0,center-x+y);symbol.SetPosition(1,center+x+y);
                symbol.SetPosition(2,center-x-y);symbol.SetPosition(3,center+x-y);
                symbol.widthMultiplier=.045f*scale;
                Color color=new Color(1,1,1,Mathf.Sin(phase*Mathf.PI));symbol.startColor=symbol.endColor=color;
            }
        }
        void OnDestroy(){if(material){if(Application.isPlaying)Destroy(material);else DestroyImmediate(material);}}
    }
}
