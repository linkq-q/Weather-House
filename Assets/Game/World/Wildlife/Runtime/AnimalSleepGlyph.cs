using UnityEngine;

namespace WeatherHouse.Hillside
{
    // Three reused rounded strokes: no font dependency, per-frame text rebuild or particle allocations.
    public sealed class AnimalSleepGlyph : MonoBehaviour
    {
        public bool Sleeping;
        public float height = 1;
        LineRenderer[] symbols;
        Material material;
        Camera view;
        float age;
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
                line.useWorldSpace=false;line.positionCount=4;line.widthMultiplier=.045f;
                line.numCornerVertices=12;line.numCapVertices=12;
                line.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;line.receiveShadows=false;
                line.SetPositions(new[]{new Vector3(-.07f,.09f,0),new Vector3(.07f,.09f,0),new Vector3(-.07f,-.09f,0),new Vector3(.07f,-.09f,0)});
                symbols[i]=line;
            }
        }
        void LateUpdate()
        {
            if(!Sleeping){age=0;if(symbols!=null)foreach(LineRenderer symbol in symbols)symbol.enabled=false;return;}
            if(!view)view=Camera.main;
            if(!view || Vector3.SqrMagnitude(view.transform.position-transform.position)>900)
            {if(symbols!=null)foreach(LineRenderer symbol in symbols)symbol.enabled=false;return;}
            if(symbols==null)Create();if(symbols==null)return;
            age+=Time.deltaTime;
            for(int i=0;i<symbols.Length;i++)
            {
                float phase=Mathf.Repeat(age/2.4f-i/3f,1);
                LineRenderer symbol=symbols[i];symbol.enabled=true;
                symbol.transform.position=transform.position+Vector3.up*(height+phase*.9f)+view.transform.right*Mathf.Sin(phase*3+i)*.07f;
                symbol.transform.rotation=view.transform.rotation;
                float scale=.65f+phase*.7f;
                Vector3 parent=transform.lossyScale;
                symbol.transform.localScale=new Vector3(scale/Mathf.Max(.01f,Mathf.Abs(parent.x)),scale/Mathf.Max(.01f,Mathf.Abs(parent.y)),scale/Mathf.Max(.01f,Mathf.Abs(parent.z)));
                Color color=new Color(1,1,1,Mathf.Sin(phase*Mathf.PI));symbol.startColor=symbol.endColor=color;
            }
        }
        void OnDestroy(){if(material){if(Application.isPlaying)Destroy(material);else DestroyImmediate(material);}}
    }
}
