using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace WeatherHouse.DrawerPrototype
{
    public sealed partial class DrawerPrototypeController
    {
        Material paperInk;

        void ApplyLighting()
        {
            if (!drawerSun) return;
            drawerSun.transform.rotation = Quaternion.Euler(sunElevation,sunAzimuth,0);
            drawerSun.color = sunlightColor; drawerSun.intensity = sunlight; drawerSun.shadowStrength = shadowStrength;
            RenderSettings.ambientLight = ambientColor;
        }

        Material WoodMaterial(string name, Color color, Rect uv)
        {
            Material material = Paint(name,color); material.SetTexture("_BaseMap",timberAlbedo);
            material.SetTextureScale("_BaseMap",uv.size); material.SetTextureOffset("_BaseMap",uv.position);
            material.SetFloat("_Smoothness",.22f); return material;
        }

        void BuildTableAndPlants()
        {
            var under = Paint("Dark wood joints",new Color(.24f,.20f,.15f));
            Box(transform,"Table solid support",new Vector3(0,-.25f,0),new Vector3(18,.3f,11),under);
            for (int i = 0; i < 8; i++)
            {
                float tint = 1+(i%3-1)*.025f;
                var board = WoodMaterial("Table plank "+i,new Color(.86f*tint,.86f*tint,.81f*tint),new Rect(i*.17f,i*.13f,1.8f,.20f));
                BevelBox(transform,"Solid oak tabletop board",new Vector3(0,-.10f,-4.64f+i*1.32f),new Vector3(18,.20f,1.30f),.03f,.018f,board);
            }
            BuildBranchFoliage();
        }

        void EnvelopeDecoration(Transform parent, float x, float z, float tip)
        {
            if (!paperInk) paperInk = Paint("Faded sage letterpress",new Color(.66f,.65f,.52f));
            // Thin letterpress rules and small botanical corners keep the paper mostly white.
            float y = .041f;
            Stroke3D(parent,"Inset envelope border",new[] {
                new Vector3(-x+.12f,y,-z+.13f),new Vector3(x-.12f,y,-z+.13f),new Vector3(x-.12f,y,z-.13f)
            },.007f,paperInk);
            Stroke3D(parent,"Inset envelope border",new[] {
                new Vector3(-x+.12f,y,z-.13f),new Vector3(-x+.12f,y,-z+.13f)
            },.007f,paperInk);
            for (int side = -1; side <= 1; side += 2)
            {
                Vector3 root = new Vector3(side*(x-.24f),y+.003f,-z+.24f);
                var twig = new GameObject("Pressed botanical corner"); twig.transform.SetParent(parent,false); twig.transform.localPosition = root;
                twig.transform.localRotation = Quaternion.Euler(0,side*22,0);
                Stroke3D(twig.transform,"Fine stem",new[] { Vector3.zero,new Vector3(0,0,.34f) },.007f,paperInk);
                for (int leaf = 0; leaf < 3; leaf++)
                {
                    var motif = new GameObject("Letterpress leaf"); motif.transform.SetParent(twig.transform,false);
                    motif.transform.localPosition = new Vector3((leaf%2 == 0 ? 1 : -1)*.033f,0,.06f+leaf*.085f);
                    motif.transform.localRotation = Quaternion.Euler(0,leaf%2 == 0 ? 65 : -65,0);
                    LeafMotif(motif.transform,Vector3.zero,.10f,paperInk,paperFold);
                }
            }
            Stroke3D(parent,"Flap inset",new[] {
                new Vector3(-x+.15f,.038f,z-.13f),new Vector3(0,.050f,tip+.13f),new Vector3(x-.15f,.038f,z-.13f)
            },.007f,paperInk);
        }

        void LeafMotif(Transform parent, Vector3 position, float length, Material face, Material vein)
        {
            var points = new List<Vector3> { new Vector3(0,.001f,0) }; var tris = new List<int>();
            const int count = 24;
            for (int i = 0; i < count; i++)
            {
                float a = i*Mathf.PI*2/count;
                points.Add(new Vector3(Mathf.Sin(a)*Mathf.Abs(Mathf.Sin(a))*.26f*length,0,Mathf.Cos(a)*length*.5f));
            }
            for (int i = 0; i < count; i++) tris.AddRange(new[] { 0,i+1,(i+1)%count+1 });
            var mesh = new Mesh { name = "Curved botanical leaf", vertices = points.ToArray(),triangles = tris.ToArray() };
            mesh.RecalculateNormals(); generated.Add(mesh);
            var obj = MeshObject(parent,"Leaf impression",mesh,face); obj.transform.localPosition = position;
            Stroke3D(obj.transform,"Leaf midrib",new[] { new Vector3(0,.002f,-length*.42f),new Vector3(0,.002f,length*.35f) },length*.035f,vein);
            for (int i = 0; i < 2; i++) for (int side = -1; side <= 1; side += 2)
                Stroke3D(obj.transform,"Leaf fine vein",new[] { new Vector3(0,.002f,-length*.18f+i*length*.22f),new Vector3(side*length*.15f,.002f,i*length*.21f) },length*.02f,vein);
        }

        void WaxDisk(Transform parent)
        {
            const int count = 64;
            var points = new List<Vector3>(); var tris = new List<int>();
            float[] radius = { .145f,.174f,.170f,.147f }, height = { 0,.011f,.030f,.045f };
            for (int ring = 0; ring < 4; ring++) for (int i = 0; i < count; i++)
            {
                float angle = -i*Mathf.PI*2/count;
                float r = radius[ring]*(1+.025f*Mathf.Sin(angle*5)+.018f*Mathf.Sin(angle*9+.3f));
                points.Add(new Vector3(Mathf.Cos(angle)*r,height[ring],Mathf.Sin(angle)*r));
            }
            for (int ring = 0; ring < 3; ring++) for (int i = 0; i < count; i++)
            {
                int a = ring*count+i,b = ring*count+(i+1)%count;
                tris.AddRange(new[] { a,b,b+count,a,b+count,a+count });
            }
            int centre = points.Count; points.Add(new Vector3(0,.042f,0));
            for (int i = 0; i < count; i++) tris.AddRange(new[] { centre,3*count+i,3*count+(i+1)%count });
            var mesh = new Mesh { name = "Soft poured wax",vertices = points.ToArray(),triangles = tris.ToArray() };
            mesh.RecalculateNormals(); generated.Add(mesh); MeshObject(parent,"Poured wax with rounded edge",mesh,wax);
        }
    }
}
