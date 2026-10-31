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

        void BuildMeadow()
        {
            if (!meadowGround || !meadowLayer || meadowVariants == null || meadowVariants.Length != 12 || meadowMaterials == null || meadowMaterials.Length != 3)
                throw new System.InvalidOperationException("Rebuild the drawer scene to bind the existing Waterside meadow assets.");
            var data = new TerrainData { name = "Drawer meadow ground",heightmapResolution = 33,alphamapResolution = 32,size = new Vector3(24,1,18) };
            generated.Add(data); data.terrainLayers = new[] { meadowLayer };
            data.SetHeights(0,0,new float[33,33]);
            var weights = new float[32,32,1];
            for (int z = 0; z < 32; z++) for (int x = 0; x < 32; x++) weights[z,x,0] = 1;
            data.SetAlphamaps(0,0,weights);
            GameObject ground = Terrain.CreateTerrainGameObject(data); ground.name = "Existing Waterside meadow · isolated patch";
            ground.transform.SetParent(transform,false); ground.transform.position = new Vector3(-12,-.06f,-9);
            var terrain = ground.GetComponent<Terrain>(); terrain.drawInstanced = true; terrain.materialTemplate = meadowGround;
            terrain.basemapDistance = 100; terrain.heightmapPixelError = 2;
            var mask = new Texture2D(128,128,TextureFormat.RGBA32,false,true) { name = "Grass excluded beneath drawer",wrapMode = TextureWrapMode.Clamp };
            generated.Add(mask); var coverage = new Color[128*128];
            for (int z = 0; z < 128; z++) for (int x = 0; x < 128; x++)
            {
                float wx = -12+x/127f*24, wz = -9+z/127f*18;
                float dx = Mathf.Max(InnerLeft-wx,wx-InnerRight,0);
                float dz = Mathf.Max(InnerBottom-wz,wz-InnerTop,0);
                float distance = Mathf.Sqrt(dx*dx+dz*dz);
                float amount = Mathf.SmoothStep(0,1,Mathf.InverseLerp(.22f,.32f,distance));
                // The existing trampled-grass channel keeps long windblown blades out of the drawer.
                float wear = 1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(.25f,.80f,distance));
                coverage[z*128+x] = new Color(amount,0,wear,1);
            }
            mask.SetPixels(coverage); mask.Apply();
            Meadow = ground.AddComponent<WeatherHouse.Hillside.HillsideGrassRenderer>();
            Meadow.terrain = terrain; Meadow.variants = meadowVariants; Meadow.materials = meadowMaterials;
            Meadow.seed = 431; Meadow.clumpsPerSquareMeter = 28.011204f; Meadow.speciesWeights = new[] { 78f,6f,.8f };
            Meadow.placementMap = mask; Meadow.viewCamera = ViewCamera;
            BuildMeadowDecorations();
        }

        void BuildMeadowDecorations()
        {
            if (meadowPlants == null || meadowPlants.Length != 3 || System.Array.Exists(meadowPlants,p => !p))
                throw new System.InvalidOperationException("Rebuild the drawer scene to bind the existing orange flowers.");
            var border = new GameObject("Meadow decorations"); border.transform.SetParent(transform,false);
            // Keep the right-side collection shelf and the drawer's picking area open.
            Vector4[] patches =
            {
                new Vector4(-7.05f,1.95f,15,1.65f),new Vector4(-6.60f,1.60f,80,1.25f),new Vector4(-6.05f,-3.15f,210,1.60f),
                new Vector4(-2.75f,4.40f,120,1.45f),new Vector4(-2.20f,4.55f,65,1.10f),new Vector4(4.85f,4.1f,290,1.50f)
            };
            for (int i = 0; i < patches.Length; i++)
            {
                Vector4 placement = patches[i];
                var plant = Instantiate(meadowPlants[i%3],border.transform);
                plant.GetComponent<MeshRenderer>().sharedMaterial = RuntimeLook.orangeFlowers;
                plant.transform.localPosition = new Vector3(placement.x,-.06f,placement.y);
                plant.transform.localRotation = Quaternion.Euler(0,placement.z,0);
                plant.transform.localScale = Vector3.one*placement.w;
            }
            Vector4[] daisies =
            {
                new Vector4(-7.35f,3.3f,20,1.65f),new Vector4(-7.25f,-.35f,165,1.55f),
                new Vector4(-7.45f,-3.2f,300,1.75f),new Vector4(-3.45f,-4.85f,110,1.65f),
                new Vector4(1.40f,-4.90f,225,1.75f),new Vector4(-.40f,5.55f,65,1.65f),
                new Vector4(5.85f,5.00f,280,1.60f),new Vector4(8.15f,-.30f,40,1.55f)
            };
            for (int i = 0; i < daisies.Length; i++)
            {
                Vector4 placement = daisies[i];
                var flower = MeshObject(border.transform,"Larger meadow daisies",meadowVariants[8+i%4],meadowMaterials[2]);
                flower.transform.localPosition = new Vector3(placement.x,-.06f,placement.y);
                flower.transform.localRotation = Quaternion.Euler(0,placement.z,0);
                flower.transform.localScale = Vector3.one*placement.w;
            }
        }

        void BuildPaperFeedback()
        {
            paperHighlight = new Material(Shader.Find("Weather House/Drawer Envelope Highlight")); generated.Add(paperHighlight);
            var outline = new GameObject("Paper selection breathing edge"); outline.transform.SetParent(transform,false);
            PaperOutline = outline.AddComponent<LineRenderer>(); PaperOutline.useWorldSpace = false;
            PaperOutline.loop = true; PaperOutline.widthMultiplier = .085f; PaperOutline.numCornerVertices = 4;
            PaperOutline.sharedMaterial = paperHighlight; PaperOutline.shadowCastingMode = ShadowCastingMode.Off;
            PaperOutline.receiveShadows = false; outline.SetActive(false);
        }

        void ShowPaperFeedback(DrawerToken paper)
        {
            if (!PaperOutline) return;
            PaperOutline.gameObject.SetActive(paper);
            if (!paper) { PaperOutline.transform.SetParent(transform,false); return; }
            PaperOutline.transform.SetParent(paper.transform,false); PaperOutline.transform.localPosition = Vector3.zero;
            float x = paper.dimensions.x*.5f, z = paper.dimensions.z*.5f;
            PaperOutline.positionCount = 4;
            PaperOutline.SetPositions(new[] { new Vector3(-x,.055f,-z),new Vector3(-x,.055f,z),new Vector3(x,.055f,z),new Vector3(x,.055f,-z) });
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
