using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace WeatherHouse.DrawerPrototype
{
    public sealed partial class DrawerPrototypeController
    {
        const string RefinedArt = "DrawerPrototype/RefinedProps/";
        DrawerLetterContent letterContent;

        void BuildCameraModel(Transform parent)
        {
            var leather = Paint("Camera · fine sage leather",new Color(.47f,.52f,.41f));
            leather.SetTexture("_BaseMap",Resources.Load<Texture2D>(RefinedArt+"CameraLeather"));
            leather.SetTextureScale("_BaseMap",Vector2.one*1.6f); leather.SetFloat("_Smoothness",.22f);
            var metal = CameraFinish("Camera · satin ivory alloy",new Color(.78f,.77f,.66f),.35f,.48f);
            var edge = CameraFinish("Camera · polished chamfer",new Color(.70f,.69f,.57f),.55f,.64f);
            var graphite = CameraFinish("Camera · graphite lens metal",new Color(.12f,.15f,.14f),.38f,.42f);
            var rubber = Paint("Camera · focus grip",new Color(.19f,.22f,.19f)); rubber.SetFloat("_Smoothness",.16f);
            var iris = Paint("Camera · recessed optical interior",new Color(.035f,.065f,.064f));
            var blade = CameraFinish("Camera · aperture blades",new Color(.045f,.080f,.075f),.20f,.42f);
            var ink = Paint("Camera · engraved index",new Color(.70f,.69f,.55f));

            BevelBox(parent,"Camera chassis",Vector3.zero,new Vector3(1.96f,.54f,1.42f),.16f,.065f,metal);
            BevelBox(parent,"Leather wrap",new Vector3(0,.004f,-.055f),new Vector3(1.974f,.43f,1.23f),.17f,.04f,leather);
            BevelBox(parent,"Inset front leather",new Vector3(0,.275f,-.035f),new Vector3(1.82f,.055f,1.12f),.13f,.017f,leather);
            BevelBox(parent,"Lower alloy rail",new Vector3(0,.016f,-.66f),new Vector3(1.87f,.51f,.13f),.045f,.025f,edge);
            BevelBox(parent,"Satin top shoulder",new Vector3(0,.045f,.52f),new Vector3(1.94f,.48f,.33f),.10f,.045f,metal);
            BevelBox(parent,"Leather finger grip",new Vector3(.74f,.295f,-.13f),new Vector3(.30f,.11f,.82f),.11f,.025f,leather);
            BevelBox(parent,"Back door seam",new Vector3(0,-.263f,-.05f),new Vector3(1.67f,.026f,1.09f),.12f,.008f,graphite);

            Vector3 lens = new Vector3(-.12f,0,-.14f);
            TurnedPart(parent,"Bevelled lens mount",lens,new[] { new Vector2(.50f,.29f),new Vector2(.52f,.31f),new Vector2(.52f,.34f),new Vector2(.48f,.36f),new Vector2(.37f,.36f),new Vector2(.37f,.29f),new Vector2(.50f,.29f) },edge);
            TurnedPart(parent,"Stepped optical barrel",lens,new[] { new Vector2(.442f,.34f),new Vector2(.455f,.36f),new Vector2(.455f,.50f),new Vector2(.42f,.53f),new Vector2(.31f,.53f),new Vector2(.31f,.34f),new Vector2(.442f,.34f) },graphite);
            TurnedPart(parent,"Knurled focus ring",lens,new[] { new Vector2(.45f,.39f),new Vector2(.468f,.41f),new Vector2(.468f,.49f),new Vector2(.45f,.51f),new Vector2(.43f,.51f),new Vector2(.43f,.39f),new Vector2(.45f,.39f) },rubber,128,.008f);
            TurnedPart(parent,"Satin front lens rim",lens,new[] { new Vector2(.42f,.515f),new Vector2(.43f,.54f),new Vector2(.43f,.59f),new Vector2(.406f,.61f),new Vector2(.315f,.61f),new Vector2(.300f,.59f),new Vector2(.300f,.53f),new Vector2(.42f,.515f) },edge);
            TurnedPart(parent,"Dark inner bevel",lens,new[] { new Vector2(.333f,.594f),new Vector2(.316f,.615f),new Vector2(.295f,.615f),new Vector2(.282f,.57f),new Vector2(.282f,.55f),new Vector2(.333f,.594f) },graphite);
            Cylinder(parent,"Optical depth",lens+Vector3.up*.551f,.565f,.018f,iris);
            for (int i=0;i<6;i++)
            {
                float angle=i*Mathf.PI/3;
                var flap=BevelBox(parent,"Iris blade",lens+new Vector3(Mathf.Cos(angle)*.145f,.566f,Mathf.Sin(angle)*.145f),new Vector3(.27f,.007f,.18f),.018f,.002f,blade);
                flap.transform.localRotation=Quaternion.Euler(0,-angle*Mathf.Rad2Deg+24,0);
            }
            Cylinder(parent,"Aperture opening",lens+Vector3.up*.574f,.15f,.008f,iris);
            var glass=CameraFinish("Camera · curved coated glass",new Color(.26f,.44f,.39f,.34f),.05f,.94f);
            glass.SetFloat("_Surface",1); glass.SetFloat("_ZWrite",0);
            glass.SetFloat("_SrcBlend",(float)BlendMode.One); glass.SetFloat("_DstBlend",(float)BlendMode.OneMinusSrcAlpha);
            glass.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); glass.EnableKeyword("_ALPHAPREMULTIPLY_ON");
            glass.SetOverrideTag("RenderType","Transparent"); glass.renderQueue=(int)RenderQueue.Transparent;
            var lensGlass=TurnedPart(parent,"Convex optical glass",lens,new[] { new Vector2(.292f,.596f),new Vector2(.25f,.611f),new Vector2(.17f,.627f),new Vector2(.08f,.636f),new Vector2(0,.638f) },glass);
            lensGlass.GetComponent<Renderer>().shadowCastingMode=ShadowCastingMode.Off;
            for(int i=0;i<12;i++)
            {
                float angle=(i*18+25)*Mathf.Deg2Rad;
                var mark=BevelBox(parent,"Focus scale tick",lens+new Vector3(Mathf.Cos(angle)*.365f,.614f,Mathf.Sin(angle)*.365f),new Vector3(.010f,.003f,i%3==0?.047f:.025f),.003f,.001f,ink);
                mark.transform.localRotation=Quaternion.Euler(0,90-angle*Mathf.Rad2Deg,0);
            }
            BevelBox(parent,"Viewfinder surround",new Vector3(.43f,.31f,.49f),new Vector3(.37f,.115f,.23f),.05f,.02f,graphite);
            BevelBox(parent,"Viewfinder inset",new Vector3(.43f,.372f,.49f),new Vector3(.27f,.012f,.13f),.022f,.004f,blade);
            BevelBox(parent,"Viewfinder glint",new Vector3(.38f,.381f,.51f),new Vector3(.10f,.004f,.017f),.006f,.001f,metal);
            foreach(float x in new[]{-.70f,.73f})
            {
                TurnedPart(parent,x<0?"Knurled shutter collar":"Exposure dial",new Vector3(x,0,.50f),new[] { new Vector2(.13f,.285f),new Vector2(.14f,.30f),new Vector2(.14f,.365f),new Vector2(.12f,.38f),new Vector2(0,.38f),new Vector2(0,.285f),new Vector2(.13f,.285f) },graphite,64,.004f);
                Cylinder(parent,"Satin dial cap",new Vector3(x,.385f,.50f),.225f,.022f,metal);
                if(x<0) Cylinder(parent,"Recessed shutter button",new Vector3(x,.402f,.50f),.13f,.018f,edge);
            }
            foreach(float x in new[]{-.81f,.81f}) foreach(float z in new[]{-.57f,.48f})
            {
                Cylinder(parent,"Flush face screw",new Vector3(x,.306f,z),.055f,.009f,edge);
                BevelBox(parent,"Screw slot",new Vector3(x,.312f,z),new Vector3(.034f,.003f,.006f),.002f,.001f,graphite);
            }
            for(int side=-1;side<=1;side+=2)
            {
                BevelBox(parent,"Strap anchor",new Vector3(side*.99f,.05f,.40f),new Vector3(.12f,.16f,.20f),.04f,.015f,edge);
                Wire(parent,"Metal strap loop",new[]{new Vector3(side*1.02f,.10f,.31f),new Vector3(side*1.09f,.10f,.31f),new Vector3(side*1.10f,.10f,.49f),new Vector3(side*1.02f,.10f,.49f)},.012f,edge);
            }
            var probeObject=new GameObject("Camera local reflections"); probeObject.transform.SetParent(parent,false);
            var probe=probeObject.AddComponent<ReflectionProbe>(); probe.mode=ReflectionProbeMode.Realtime;
            probe.refreshMode=ReflectionProbeRefreshMode.OnAwake; probe.timeSlicingMode=ReflectionProbeTimeSlicingMode.AllFacesAtOnce;
            probe.resolution=128; probe.size=new Vector3(2.6f,2.2f,2.2f); probe.center=Vector3.up*.2f;
            probe.nearClipPlane=.08f; probe.farClipPlane=24; probe.intensity=.7f;
        }

        Material CameraFinish(string name, Color color, float metallic, float smoothness)
        {
            var material=Paint(name,color); material.SetFloat("_Metallic",metallic); material.SetFloat("_Smoothness",smoothness);
            material.SetFloat("_EnvironmentReflections",1); return material;
        }

        void BuildGrandmotherLetter(Transform parent)
        {
            letterContent=Resources.Load<DrawerLetterContent>(RefinedArt+"GrandmaLetterContent");
            var paper=RuntimeLook.letterPaper;
            var reverse=Paint("Grandmother · warm paper edge",new Color(.86f,.83f,.72f));
            var sheet=CurvedSheet(parent,"Modeled writing paper",new Vector2(1.64f,2.05f),.012f,.035f,paper,reverse,new Rect(0,0,1,1));
            sheet.transform.localPosition=new Vector3(-.16f,.015f,-.015f);
            var photoRoot=new GameObject("Clipped companion photograph"); photoRoot.transform.SetParent(parent,false);
            photoRoot.transform.localPosition=new Vector3(.38f,.095f,.60f); photoRoot.transform.localRotation=Quaternion.Euler(0,-9,0);
            var border=Paint("Photograph · ivory card",new Color(.96f,.94f,.85f)); border.SetFloat("_Smoothness",.19f);
            CurvedSheet(photoRoot.transform,"Photograph card thickness",new Vector2(1.13f,.85f),.018f,.020f,border,reverse,new Rect(0,0,1,1));
            var print=Paint("Photograph · witch and border collie",Color.white);
            print.SetTexture("_BaseMap",Resources.Load<Texture2D>(RefinedArt+"CompanionPhoto")); print.SetFloat("_Smoothness",.25f);
            var printed=CurvedSheet(photoRoot.transform,"Printed companion picture",new Vector2(1.01f,.6733f),.001f,.005f,print,border,new Rect(0,0,1,1));
            printed.transform.localPosition=new Vector3(0,.015f,.026f);
            var clip=CameraFinish("Letter · aged brass clip",new Color(.55f,.49f,.32f),.45f,.48f);
            Wire(parent,"Paper clip · upper wire",new[]{new Vector3(.13f,.15f,.79f),new Vector3(.13f,.15f,1.05f),new Vector3(.16f,.15f,1.09f),new Vector3(.25f,.15f,1.09f),new Vector3(.28f,.15f,1.05f),new Vector3(.28f,.15f,.77f),new Vector3(.25f,.15f,.74f),new Vector3(.20f,.15f,.74f),new Vector3(.18f,.15f,.78f),new Vector3(.18f,.15f,.99f)},.010f,clip);
            Wire(parent,"Paper clip · returning wire",new[]{new Vector3(.13f,.15f,1.05f),new Vector3(.13f,-.014f,1.08f),new Vector3(.25f,-.014f,1.08f),new Vector3(.25f,-.014f,.80f)},.010f,clip);
            if(letterContent)
            {
                LetterText(parent,"Letter salutation",0,new Vector3(-.82f,.063f,.83f),.55f,.17f);
                LetterText(parent,"Letter editable message",1,new Vector3(-.82f,.045f,.04f),1.33f,.86f);
                LetterText(parent,"Letter signature",2,new Vector3(.25f,.049f,-.63f),.30f,.16f);
            }
        }

        void LetterText(Transform parent,string name,int section,Vector3 position,float width,float height)
        {
            var obj=new GameObject(name); obj.transform.SetParent(parent,false); obj.transform.localPosition=position;
            obj.transform.localRotation=Quaternion.Euler(90,0,0);
            var writing=obj.AddComponent<DrawerLetterWriting>(); writing.content=letterContent; writing.section=section;
            writing.maximumSize=new Vector2(width,height); writing.Refresh();
        }

        GameObject TurnedPart(Transform parent,string name,Vector3 position,Vector2[] profile,Material material,int segments=64,float knurl=0)
        {
            var vertices=new List<Vector3>(); var uv=new List<Vector2>(); var triangles=new List<int>();
            for(int row=0;row<profile.Length;row++) for(int i=0;i<=segments;i++)
            {
                float angle=-i*Mathf.PI*2/segments;
                float radius=profile[row].x+(profile[row].x>0 && i%2==0?knurl:0);
                vertices.Add(new Vector3(Mathf.Cos(angle)*radius,profile[row].y,Mathf.Sin(angle)*radius));
                uv.Add(new Vector2(i/(float)segments,row/(float)(profile.Length-1)));
                if(row==0 || i==segments) continue;
                int a=(row-1)*(segments+1)+i,b=a+1,c=b+segments+1,d=a+segments+1;
                triangles.AddRange(new[]{a,b,c,a,c,d});
            }
            var mesh=new Mesh { name=name }; mesh.SetVertices(vertices); mesh.SetUVs(0,uv); mesh.SetTriangles(triangles,0);
            mesh.RecalculateNormals(); mesh.RecalculateTangents(); generated.Add(mesh);
            var obj=MeshObject(parent,name,mesh,material); obj.transform.localPosition=position; return obj;
        }

        GameObject CurvedSheet(Transform parent,string name,Vector2 size,float thickness,float curl,Material front,Material back,Rect uvRect)
        {
            const int columns=16, rows=20, count=(columns+1)*(rows+1);
            var vertices=new List<Vector3>(); var uv=new List<Vector2>(); var top=new List<int>(); var underside=new List<int>();
            for(int side=0;side<2;side++) for(int z=0;z<=rows;z++) for(int x=0;x<=columns;x++)
            {
                float u=x/(float)columns,v=z/(float)rows;
                float bend=curl*Mathf.Pow(Mathf.Abs(u*2-1),5)*Mathf.Pow(Mathf.Abs(v*2-1),4);
                vertices.Add(new Vector3((u-.5f)*size.x,bend+(side==0?thickness*.5f:-thickness*.5f),(v-.5f)*size.y));
                uv.Add(new Vector2(uvRect.x+u*uvRect.width,uvRect.y+v*uvRect.height));
                if(x==columns || z==rows)continue;
                int a=side*count+z*(columns+1)+x,b=a+columns+1;
                (side==0?top:underside).AddRange(side==0?new[]{a,b,b+1,a,b+1,a+1}:new[]{a,b+1,b,a,a+1,b+1});
            }
            var perimeter=new List<int>();
            for(int z=0;z<=rows;z++)perimeter.Add(z*(columns+1));
            for(int x=1;x<=columns;x++)perimeter.Add(rows*(columns+1)+x);
            for(int z=rows-1;z>=0;z--)perimeter.Add(z*(columns+1)+columns);
            for(int x=columns-1;x>0;x--)perimeter.Add(x);
            for(int i=0;i<perimeter.Count;i++)
            {
                int a=perimeter[i],b=perimeter[(i+1)%perimeter.Count];
                underside.AddRange(new[]{a,b+count,b,a,a+count,b+count});
            }
            var mesh=new Mesh { name=name,subMeshCount=2 }; mesh.SetVertices(vertices); mesh.SetUVs(0,uv);
            mesh.SetTriangles(top,0); mesh.SetTriangles(underside,1); mesh.RecalculateNormals(); mesh.RecalculateTangents(); generated.Add(mesh);
            var obj=MeshObject(parent,name,mesh,front); obj.GetComponent<MeshRenderer>().sharedMaterials=new[]{front,back}; return obj;
        }

        void Wire(Transform parent,string name,Vector3[] points,float radius,Material material)
        {
            const int sides=8;
            var vertices=new List<Vector3>(); var uv=new List<Vector2>(); var triangles=new List<int>();
            for(int i=0;i<points.Length;i++)
            {
                Vector3 tangent=(points[Mathf.Min(i+1,points.Length-1)]-points[Mathf.Max(0,i-1)]).normalized;
                Vector3 cross=Vector3.Cross(tangent,Vector3.up).normalized;
                if(cross.sqrMagnitude<.01f)cross=Vector3.right;
                Vector3 second=Vector3.Cross(tangent,cross).normalized;
                for(int j=0;j<sides;j++)
                {
                    float angle=j*Mathf.PI*2/sides;
                    vertices.Add(points[i]+radius*(cross*Mathf.Cos(angle)+second*Mathf.Sin(angle))); uv.Add(new Vector2(i/(float)points.Length,j/(float)sides));
                    if(i==0)continue;
                    int a=(i-1)*sides+j,b=(i-1)*sides+(j+1)%sides,c=i*sides+(j+1)%sides,d=i*sides+j;
                    triangles.AddRange(new[]{a,c,b,a,d,c});
                }
            }
            var mesh=new Mesh { name=name }; mesh.SetVertices(vertices); mesh.SetUVs(0,uv); mesh.SetTriangles(triangles,0);
            mesh.RecalculateNormals(); mesh.RecalculateTangents(); generated.Add(mesh); MeshObject(parent,name,mesh,material);
        }
    }
}
