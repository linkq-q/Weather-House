using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace WeatherHouse.DrawerPrototype
{
    public sealed partial class DrawerPrototypeController
    {
        public List<GameObject> FoliageModels { get; } = new List<GameObject>();
        Material leafFace, leafShade, stemMaterial, leafVein;

        void BuildBranchFoliage()
        {
            leafFace = Paint("Soft olive leaf",new Color(.49f,.56f,.30f)); leafFace.SetFloat("_Cull",0); leafFace.SetFloat("_Smoothness",.18f);
            leafShade = Paint("Folded olive leaf",new Color(.38f,.46f,.23f)); leafShade.SetFloat("_Cull",0); leafShade.SetFloat("_Smoothness",.18f);
            leafVein = Paint("Fine leaf midrib",new Color(.61f,.65f,.38f));
            stemMaterial = Paint("Young brown green twig",new Color(.39f,.34f,.21f));
            Branch(new Vector3(-8.25f,.60f,4.8f),145,1f,0);
            Branch(new Vector3(8.25f,.95f,5.10f),-108,1.06f,2);
            Branch(new Vector3(-8.2f,.45f,-4.95f),42,.88f,4);
            GrassSprig(new Vector3(-7.5f,.10f,-.4f),52);
        }

        void Branch(Vector3 position, float yaw, float scale, float phase)
        {
            var root = new GameObject("Live forest branch · separated pointed leaves"); root.transform.SetParent(transform,false);
            root.transform.SetPositionAndRotation(position,Quaternion.Euler(0,yaw,0)); root.transform.localScale = Vector3.one*scale;
            Vector3[] spine = {
                Vector3.zero,new Vector3(.03f,.10f,.65f),new Vector3(.09f,.19f,1.3f),
                new Vector3(.04f,.28f,1.95f),new Vector3(-.08f,.32f,2.6f),new Vector3(-.12f,.36f,3.35f)
            };
            for (int i = 0; i < spine.Length-1; i++) Twig(root.transform,spine[i],spine[i+1],.042f-i*.006f);
            for (int node = 1; node <= 4; node++)
            {
                for (int side = -1; side <= 1; side += 2)
                {
                    Vector3 attach = spine[node]+new Vector3(0,0,side == 1 ? .11f : -.08f);
                    float angle = side*(51+node%2*10+(side == 1 ? 7 : 0))*Mathf.Deg2Rad;
                    Vector3 direction = new Vector3(Mathf.Sin(angle),.04f,Mathf.Cos(angle));
                    Vector3 start = attach+direction*.20f;
                    Twig(root.transform,attach,start,.018f);
                    float length = .91f-node*.065f+(side == 1 ? .08f : 0);
                    ModelLeaf(root.transform,start,direction,length,length*(node%2 == 0 ? .53f : .59f),.09f,side*.045f);
                }
            }
            ModelLeaf(root.transform,spine[5]-new Vector3(0,0,.08f),new Vector3(-.18f,.05f,1),.70f,.38f,.08f,-.035f);
            var sway = root.AddComponent<DrawerFoliageSway>(); sway.phase = phase;
            FoliageModels.Add(root);
        }

        void Twig(Transform parent, Vector3 from, Vector3 to, float diameter)
        {
            GameObject twig = Cylinder(parent,"Slender woody stem",(from+to)*.5f,diameter,Vector3.Distance(from,to),stemMaterial);
            twig.transform.localRotation = Quaternion.FromToRotation(Vector3.up,to-from);
        }

        void ModelLeaf(Transform parent, Vector3 start, Vector3 direction, float length, float width, float rise, float bend = 0)
        {
            direction = new Vector3(direction.x,0,direction.z).normalized;
            Vector3 across = new Vector3(direction.z,0,-direction.x);
            const int rows = 12;
            var vertices = new Vector3[(rows+1)*3]; var uv = new Vector2[vertices.Length];
            var left = new List<int>(); var right = new List<int>();
            for (int i = 0; i <= rows; i++)
            {
                float t = i/(float)rows;
                float spread = Mathf.Pow(Mathf.Max(0,Mathf.Sin(t*Mathf.PI)),.82f)*width*.5f*(1.12f-.24f*t);
                Vector3 centre = start+direction*length*t+across*(bend*Mathf.Sin(t*Mathf.PI))+Vector3.up*(Mathf.Sin(t*Mathf.PI)*rise+t*t*.035f);
                for (int rail = 0; rail < 3; rail++)
                {
                    vertices[i*3+rail] = centre+across*(rail-1)*spread-Vector3.up*(rail == 1 ? 0 : Mathf.Sin(t*Mathf.PI)*.045f);
                    uv[i*3+rail] = new Vector2(rail*.5f,t);
                }
                if (i == rows) continue;
                int p = i*3;
                left.AddRange(new[] { p,p+3,p+4,p,p+4,p+1 });
                right.AddRange(new[] { p+1,p+4,p+5,p+1,p+5,p+2 });
            }
            var mesh = new Mesh { name = "Folded lanceolate leaf",vertices = vertices,uv = uv,subMeshCount = 2 };
            mesh.SetTriangles(left,0); mesh.SetTriangles(right,1); mesh.RecalculateNormals(); mesh.RecalculateTangents(); generated.Add(mesh);
            var obj = MeshObject(parent,"Pointed leaf with clear outline",mesh,leafFace);
            var renderer = obj.GetComponent<MeshRenderer>(); renderer.sharedMaterials = new[] { leafFace,leafShade }; renderer.shadowCastingMode = ShadowCastingMode.TwoSided;
            var vein = new Vector3[9];
            for (int i = 0; i < vein.Length; i++)
            {
                float t = .05f+i*.105f;
                vein[i] = start+direction*length*t+across*(bend*Mathf.Sin(t*Mathf.PI))+Vector3.up*(Mathf.Sin(t*Mathf.PI)*rise+t*t*.035f+.003f);
            }
            Stroke3D(parent,"Visible slender leaf vein",vein,.008f,leafVein);
        }

        void GrassSprig(Vector3 position, float yaw)
        {
            var root = new GameObject("Live forest grass · long separate blades"); root.transform.SetParent(transform,false);
            root.transform.SetPositionAndRotation(position,Quaternion.Euler(0,yaw,0));
            for (int i = 0; i < 7; i++)
            {
                float angle = (-68+i*22)*Mathf.Deg2Rad;
                Vector3 direction = new Vector3(Mathf.Sin(angle),0,Mathf.Cos(angle));
                ModelLeaf(root.transform,new Vector3((i-3)*.03f,0,0),direction,1.35f+(i%3)*.22f,.11f+(i%2)*.02f,.30f);
            }
            var sway = root.AddComponent<DrawerFoliageSway>(); sway.phase = 1.1f; sway.strength = 2.2f;
            FoliageModels.Add(root);
        }
    }
}
