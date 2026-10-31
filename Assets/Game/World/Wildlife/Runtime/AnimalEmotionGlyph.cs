using UnityEngine;
using UnityEngine.Rendering;

namespace WeatherHouse.Hillside
{
    public enum AnimalEmotion { Heart, Happy, Cry, Anger, Question, Exclamation, Sweat, Music }

    // Display only. Behaviour and cooldown rules choose when to call Play.
    public sealed class AnimalEmotionGlyph : MonoBehaviour
    {
        public float height=1;
        const int Copies=3, MaxStrokes=5;
        const float Cycle=2.4f, Width=.028f;
        LineRenderer[,] lines;
        MeshRenderer hearts;
        Mesh heartMesh;
        Vector3[] heartVertices;
        Color[] heartColors;
        Material material;
        AnimalSleepGlyph sleep;
        AnimalEmotion emotion;
        float age,duration;
        bool active;
        public bool IsPlaying=>active && isActiveAndEnabled && !Sleeping;
        bool Sleeping {get{if(!sleep)sleep=GetComponent<AnimalSleepGlyph>();return sleep && sleep.Sleeping;}}

        public bool Play(AnimalEmotion value,float seconds=Cycle)
        {
            if(!isActiveAndEnabled || Sleeping || (int)value<0 || (int)value>=Shapes.Length ||
                float.IsNaN(seconds) || float.IsInfinity(seconds) || seconds<=0)return false;
            if(lines==null)Create();if(lines==null)return false;
            if(value==AnimalEmotion.Heart && !hearts)CreateHearts();
            if(hearts)hearts.enabled=false;
            emotion=value;duration=seconds;age=0;active=true;
            Vector2[][] shape=Shapes[(int)value];
            for(int copy=0;copy<Copies;copy++)for(int stroke=0;stroke<MaxStrokes;stroke++)
            {
                LineRenderer line=lines[copy,stroke];line.enabled=false;
                if(stroke>=shape.Length){line.positionCount=0;continue;}
                Vector2[] points=shape[stroke];
                line.loop=points.Length>2 && (points[0]-points[points.Length-1]).sqrMagnitude<.00000001f;
                line.positionCount=points.Length-(line.loop?1:0);
            }
            return true;
        }
        public void Stop(){active=false;age=0;Hide();}
        void OnEnable(){Camera.onPreCull+=FaceCamera;RenderPipelineManager.beginCameraRendering+=BeforeCamera;}
        void OnDisable(){Camera.onPreCull-=FaceCamera;RenderPipelineManager.beginCameraRendering-=BeforeCamera;Stop();}
        void LateUpdate()
        {
            if(!active)return;
            if(Sleeping){Stop();return;}
            age+=Time.deltaTime;if(age>=duration)Stop();
        }
        void Create()
        {
            Shader shader=Shader.Find("Sprites/Default");if(!shader)return;
            material=new Material(shader){hideFlags=HideFlags.HideAndDontSave,color=Color.white};
            lines=new LineRenderer[Copies,MaxStrokes];
            for(int copy=0;copy<Copies;copy++)for(int stroke=0;stroke<MaxStrokes;stroke++)
            {
                var node=new GameObject("Emotion stroke "+copy+"/"+stroke);node.transform.SetParent(transform,false);
                var line=node.AddComponent<LineRenderer>();line.sharedMaterial=material;line.useWorldSpace=true;
                line.alignment=LineAlignment.View;line.widthMultiplier=Width;line.numCornerVertices=12;line.numCapVertices=12;
                line.shadowCastingMode=ShadowCastingMode.Off;line.receiveShadows=false;line.enabled=false;
                lines[copy,stroke]=line;
            }
        }
        void Hide()
        {
            if(hearts)hearts.enabled=false;
            if(lines==null)return;
            for(int copy=0;copy<Copies;copy++)for(int stroke=0;stroke<MaxStrokes;stroke++)lines[copy,stroke].enabled=false;
        }
        void BeforeCamera(ScriptableRenderContext context,Camera camera)=>FaceCamera(camera);
        void FaceCamera(Camera view)
        {
            if(!active)return;
            if(Sleeping){Stop();return;}
            if(!view || Vector3.SqrMagnitude(view.transform.position-transform.position)>900){Hide();return;}
            if(emotion==AnimalEmotion.Heart){FaceHearts(view);return;}
            if(hearts)hearts.enabled=false;
            Vector2[][] shape=Shapes[(int)emotion];Vector3 right=view.transform.right,up=view.transform.up;
            bool alert=emotion==AnimalEmotion.Exclamation;
            for(int copy=0;copy<Copies;copy++)
            {
                float phase=alert?Mathf.Clamp01(age/duration):Mathf.Repeat(age/Cycle-copy/3f,1),scale=alert?1.2f:.65f+phase*.7f;
                Vector3 center=alert?transform.position+Vector3.up*(height+.35f+phase*.18f):
                    transform.position+Vector3.up*(height+phase*.9f)+right*Mathf.Sin(phase*3+copy)*.07f;
                Color color=new Color(1,1,1,alert?1-Mathf.SmoothStep(0,1,phase):Mathf.Sin(phase*Mathf.PI));
                for(int stroke=0;stroke<MaxStrokes;stroke++)
                {
                    LineRenderer line=lines[copy,stroke];line.enabled=(!alert || copy==0) && stroke<shape.Length;
                    if(!line.enabled)continue;
                    line.widthMultiplier=Width*scale*(emotion==AnimalEmotion.Exclamation?(stroke==0?1.7f:2f):1);line.startColor=line.endColor=color;
                    Vector2[] points=shape[stroke];
                    for(int point=0;point<line.positionCount;point++)
                        line.SetPosition(point,center+(right*points[point].x+up*points[point].y)*scale);
                }
            }
        }
        void CreateHearts()
        {
            Vector2[] outline=Shapes[(int)AnimalEmotion.Heart][0];int edgeCount=outline.Length-1,stride=edgeCount+1;
            heartVertices=new Vector3[stride*Copies];heartColors=new Color[heartVertices.Length];
            var triangles=new int[edgeCount*3*Copies];
            for(int copy=0;copy<Copies;copy++)for(int edge=0;edge<edgeCount;edge++)
            {
                int triangle=(copy*edgeCount+edge)*3,offset=copy*stride;
                triangles[triangle]=offset;triangles[triangle+1]=offset+edge+1;triangles[triangle+2]=offset+(edge+1)%edgeCount+1;
            }
            heartMesh=new Mesh{name="Filled animal hearts",hideFlags=HideFlags.HideAndDontSave};heartMesh.MarkDynamic();
            heartMesh.vertices=heartVertices;heartMesh.triangles=triangles;
            var node=new GameObject("Filled emotion hearts");node.transform.SetParent(transform,false);
            node.AddComponent<MeshFilter>().sharedMesh=heartMesh;hearts=node.AddComponent<MeshRenderer>();hearts.sharedMaterial=material;
            hearts.shadowCastingMode=ShadowCastingMode.Off;hearts.receiveShadows=false;hearts.enabled=false;
        }
        void FaceHearts(Camera view)
        {
            // One reused mesh holds three solid silhouettes; no overlapping transparent strokes or per-frame arrays.
            for(int copy=0;copy<Copies;copy++)for(int stroke=0;stroke<MaxStrokes;stroke++)lines[copy,stroke].enabled=false;
            Vector2[] outline=Shapes[(int)AnimalEmotion.Heart][0];int stride=outline.Length;
            Vector3 right=view.transform.right,up=view.transform.up;Matrix4x4 local=hearts.transform.worldToLocalMatrix;
            for(int copy=0;copy<Copies;copy++)
            {
                float phase=Mathf.Repeat(age/Cycle-copy/3f,1),scale=.65f+phase*.7f;
                Vector3 center=transform.position+Vector3.up*(height+phase*.9f)+right*Mathf.Sin(phase*3+copy)*.07f;
                Color color=new Color(1,1,1,Mathf.Sin(phase*Mathf.PI));int offset=copy*stride;
                heartVertices[offset]=local.MultiplyPoint3x4(center);heartColors[offset]=color;
                for(int point=0;point<stride-1;point++)
                {
                    // Replacing the old 0.028m outline with this contour gives about 85% of its visible width.
                    Vector2 p=outline[point]*(scale*.95f);
                    heartVertices[offset+point+1]=local.MultiplyPoint3x4(center+right*p.x+up*p.y);heartColors[offset+point+1]=color;
                }
            }
            heartMesh.vertices=heartVertices;heartMesh.colors=heartColors;heartMesh.RecalculateBounds();hearts.enabled=true;
        }
        void OnDestroy()
        {
            if(material){if(Application.isPlaying)Destroy(material);else DestroyImmediate(material);}
            if(heartMesh){if(Application.isPlaying)Destroy(heartMesh);else DestroyImmediate(heartMesh);}
        }

        static Vector2[] Arc(float x,float y,float rx,float ry,float from,float to,int steps=16)
        {
            var points=new Vector2[steps+1];
            for(int i=0;i<=steps;i++){float a=Mathf.Lerp(from,to,i/(float)steps)*Mathf.Deg2Rad;points[i]=new Vector2(x+Mathf.Cos(a)*rx,y+Mathf.Sin(a)*ry);}
            return points;
        }
        static Vector2[] Curve(Vector2 a,Vector2 b,Vector2 c,Vector2 d)
        {
            var points=new Vector2[17];
            for(int i=0;i<points.Length;i++){float t=i/16f,u=1-t;points[i]=u*u*u*a+3*u*u*t*b+3*u*t*t*c+t*t*t*d;}
            return points;
        }
        static Vector2[] Join(Vector2[] a,Vector2[] b)
        {var result=new Vector2[a.Length+b.Length-1];System.Array.Copy(a,result,a.Length);System.Array.Copy(b,1,result,a.Length,b.Length-1);return result;}
        static Vector2[] Rotate(Vector2[] points,float angle)
        {
            var rotated=new Vector2[points.Length];float c=Mathf.Cos(angle*Mathf.Deg2Rad),s=Mathf.Sin(angle*Mathf.Deg2Rad);
            for(int i=0;i<points.Length;i++)rotated[i]=new Vector2(points[i].x*c-points[i].y*s,points[i].x*s+points[i].y*c);
            return rotated;
        }
        static Vector2[] Drop(float x,float y,float size)
        {
            return Join(Curve(new Vector2(x,y+size),new Vector2(x-size*1.1f,y-size*.1f),new Vector2(x-size*.8f,y-size),new Vector2(x,y-size)),
                Curve(new Vector2(x,y-size),new Vector2(x+size*.8f,y-size),new Vector2(x+size*1.1f,y-size*.1f),new Vector2(x,y+size)));
        }
        static Vector2[] Dot(float x,float y)=>new[]{new Vector2(x-.0005f,y),new Vector2(x+.0005f,y)};
        static Vector2[] Hump(float left,float right)=>Join(Curve(new Vector2(left,-.005f),new Vector2(left,.065f),new Vector2(right,.065f),new Vector2(right,-.005f)),
            new[]{new Vector2(right,-.005f),new Vector2(right,-.1f)});
        static Vector2[][][] BuildShapes()
        {
            // Smooth the notch and tip instead of letting a zero-tangent cusp fold the thick stroke over itself.
            var heart=Curve(new Vector2(-.01f,.067f),new Vector2(-.05f,.13f),new Vector2(-.12f,.12f),new Vector2(-.12f,.055f));
            heart=Join(heart,Curve(new Vector2(-.12f,.055f),new Vector2(-.12f,0),new Vector2(-.055f,-.065f),new Vector2(-.012f,-.105f)));
            heart=Join(heart,Curve(new Vector2(-.012f,-.105f),new Vector2(-.005f,-.112f),new Vector2(.005f,-.112f),new Vector2(.012f,-.105f)));
            heart=Join(heart,Curve(new Vector2(.012f,-.105f),new Vector2(.055f,-.065f),new Vector2(.12f,0),new Vector2(.12f,.055f)));
            heart=Join(heart,Curve(new Vector2(.12f,.055f),new Vector2(.12f,.12f),new Vector2(.05f,.13f),new Vector2(.01f,.067f)));
            heart=Join(heart,Curve(new Vector2(.01f,.067f),new Vector2(.005f,.055f),new Vector2(-.005f,.055f),new Vector2(-.01f,.067f)));
            Vector2[] anger=Arc(.13f,.13f,.11f,.11f,180,270,16);
            return new[]
            {
                new[]{heart},
                new[]{new[]{new Vector2(-.105f,.11f),new Vector2(-.105f,-.1f)},Hump(-.105f,-.025f),new[]{new Vector2(.025f,.11f),new Vector2(.025f,-.1f)},Hump(.025f,.105f)},
                new[]{Arc(-.065f,.025f,.045f,.055f,0,360,24),new[]{new Vector2(-.02f,.075f),new Vector2(-.02f,-.11f)},Arc(.065f,.025f,.045f,.055f,0,360,24),new[]{new Vector2(.11f,.075f),new Vector2(.11f,-.11f)}},
                new[]{anger,Rotate(anger,120),Rotate(anger,240)},
                new[]{Join(Arc(0,.045f,.07f,.065f,165,-70,16),Curve(new Vector2(.024f,-.016f),new Vector2(0,-.025f),new Vector2(0,-.03f),new Vector2(0,-.045f))),Dot(0,-.1f)},
                new[]{new[]{new Vector2(0,.145f),new Vector2(0,-.035f)},Dot(0,-.12f)},
                new[]{Drop(0,0,.125f)},
                new[]{Arc(-.035f,-.075f,.045f,.028f,0,360,16),new[]{new Vector2(.01f,-.075f),new Vector2(.01f,.12f)},Curve(new Vector2(.01f,.12f),new Vector2(.035f,.065f),new Vector2(.12f,.1f),new Vector2(.085f,.015f))}
            };
        }
        static readonly Vector2[][][] Shapes=BuildShapes();
    }
}
