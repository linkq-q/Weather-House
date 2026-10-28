using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace WeatherHouse.DrawerPrototype
{
    public sealed partial class DrawerPrototypeController
    {
        public const float InnerLeft = -5.05f, InnerRight = 4.4f, InnerBottom = -2.85f, InnerTop = 3.3f;
        public GameObject AlbumModel { get; private set; }
        Material wax, waxInset;

        public Vector3 ConstrainPaper(DrawerToken paper, Vector3 position)
        {
            // Project all three local axes; the full paper silhouette remains inside, including when tilted.
            Vector3 half = paper.hitBox.size*.5f;
            Vector3 offset = paper.transform.TransformVector(paper.hitBox.center);
            Vector3 right = paper.transform.right*half.x, up = paper.transform.up*half.y, forward = paper.transform.forward*half.z;
            float x = Mathf.Abs(right.x)+Mathf.Abs(up.x)+Mathf.Abs(forward.x)+.035f;
            float z = Mathf.Abs(right.z)+Mathf.Abs(up.z)+Mathf.Abs(forward.z)+.035f;
            position.x = Mathf.Clamp(position.x+offset.x,InnerLeft+x,InnerRight-x)-offset.x;
            position.z = Mathf.Clamp(position.z+offset.z,InnerBottom+z,InnerTop-z)-offset.z;
            return position;
        }

        void BuildDrawer(Texture2D table)
        {
            var timber = WoodMaterial("Drawer warm timber",new Color(.84f,.82f,.74f),new Rect(.03f,.22f,1.1f,.12f));
            var lining = WoodMaterial("Drawer inner timber",new Color(.92f,.88f,.77f),new Rect(.17f,.51f,1f,.17f));
            float cx = (InnerLeft+InnerRight)*.5f, cz = (InnerBottom+InnerTop)*.5f;
            float width = InnerRight-InnerLeft, depth = InnerTop-InnerBottom;
            BevelBox(transform,"Solid drawer base",new Vector3(cx,.025f,cz),new Vector3(width+.44f,.15f,depth+.44f),.06f,.025f,lining);
            for (int plank = 0; plank < 5; plank++)
                BevelBox(transform,"Individual drawer floor board",new Vector3(cx,.078f,InnerBottom+(plank+.5f)*depth/5),new Vector3(width-.01f,.055f,depth/5-.012f),.015f,.008f,lining);
            Wall(new Vector3(cx,.025f,cz),new Vector3(width,.15f,depth));
            foreach (float z in new[] { InnerBottom-.12f,InnerTop+.12f })
            {
                Vector3 position = new Vector3(cx,.36f,z), size = new Vector3(width+.48f,.74f,.24f);
                BevelBox(transform,"Bevelled drawer rim",position,size,.06f,.035f,timber); Wall(position,size);
            }
            foreach (float x in new[] { InnerLeft-.12f,InnerRight+.12f })
            {
                Vector3 position = new Vector3(x,.36f,cz), size = new Vector3(.24f,.74f,depth);
                BevelBox(transform,"Bevelled drawer side",position,size,.06f,.035f,timber); Wall(position,size);
            }
            // A quiet brass pull, visible from the fixed overhead camera.
            var brass = Paint("Aged warm brass",new Color(.60f,.48f,.28f));
            BevelBox(transform,"Drawer pull",new Vector3(cx,.31f,InnerBottom-.39f),new Vector3(1.10f,.13f,.20f),.08f,.03f,brass);
            for (int side = -1; side <= 1; side += 2)
                BevelBox(transform,"Pull attachment",new Vector3(cx+side*.46f,.31f,InnerBottom-.25f),new Vector3(.13f,.13f,.28f),.04f,.02f,brass);
        }

        void BuildBook(Transform parent, Vector3 size, int symbol)
        {
            Material cover = symbol == 1 ? sage : green;
            float top = size.y*.5f;
            BevelBox(parent,"Ivory page block",Vector3.zero,new Vector3(size.x-.075f,size.y-.15f,size.z-.075f),.045f,.025f,paperSide);
            BevelBox(parent,"Soft lower cloth cover",new Vector3(0,-top+.045f,0),new Vector3(size.x,.09f,size.z),.10f,.025f,cover);
            BevelBox(parent,"Padded velvet cover",new Vector3(0,top-.045f,0),new Vector3(size.x,.09f,size.z),.10f,.03f,cover);
            BevelBox(parent,"Rounded cloth spine",new Vector3(-size.x*.5f+.075f,0,0),new Vector3(.15f,size.y,size.z),.055f,.025f,cover);
            var edge = Paint("Quiet page divisions",new Color(.74f,.72f,.61f));
            for (int i = 0; i < 4; i++)
            {
                float y = -.09f+i*.06f;
                Box(parent,"Page edge",new Vector3(size.x*.5f-.058f,y,.01f),new Vector3(.008f,.007f,size.z-.25f),edge);
                Box(parent,"Page edge",new Vector3(.02f,y,-size.z*.5f+.058f),new Vector3(size.x-.25f,.007f,.008f),edge);
            }
            var seamMaterial = Paint("Cloth seam",symbol == 1 ? new Color(.44f,.48f,.35f) : new Color(.48f,.53f,.35f));
            Stroke3D(parent,"Spine seam",new[] { new Vector3(-size.x*.5f+.23f,top+.002f,-size.z*.42f),new Vector3(-size.x*.5f+.23f,top+.002f,size.z*.42f) },.012f,seamMaterial);
            // The emblems match the existing HUD symbols, without a second book silhouette on the cover.
            var emblem = new GameObject(symbol == 0 ? "Sun emblem" : symbol == 1 ? "Entry rows emblem" : "Photo emblem");
            emblem.transform.SetParent(parent,false); emblem.transform.localPosition = new Vector3(.06f,top+.009f,0);
            BuildBookSymbol(emblem.transform,symbol);
            BevelBox(parent,"Cloth bookmark",new Vector3(.38f,-top+.025f,-size.z*.5f-.065f),new Vector3(.16f,.025f,.30f),.01f,.006f,cover);
        }

        void BuildBookSymbol(Transform parent, int symbol)
        {
            if (symbol == 0)
            {
                Ring(parent,"Embroidered sun",Vector3.zero,.19f,.135f,.018f,cream);
                for (int i = 0; i < 8; i++)
                {
                    float angle = i*Mathf.PI*.25f;
                    Vector3 direction = new Vector3(Mathf.Cos(angle),0,Mathf.Sin(angle));
                    Stroke3D(parent,"Sun ray",new[] { direction*.29f,direction*.40f },.044f,cream);
                }
            }
            else if (symbol == 1)
            {
                for (int row = 0; row < 3; row++)
                {
                    float z = .26f-row*.26f;
                    Stroke3D(parent,"Entry bullet",new[] { new Vector3(-.36f,0,z),new Vector3(-.23f,0,z) },.052f,cream);
                    Stroke3D(parent,"Entry line",new[] { new Vector3(-.09f,0,z),new Vector3(row == 1 ? .30f : .39f,0,z) },.052f,cream);
                }
            }
            else
            {
                Stroke3D(parent,"Photo frame",new[] { new Vector3(-.42f,0,-.34f),new Vector3(-.42f,0,.34f),new Vector3(.42f,0,.34f),new Vector3(.42f,0,-.34f),new Vector3(-.42f,0,-.34f) },.045f,cream);
                Stroke3D(parent,"Photo mountains",new[] { new Vector3(-.36f,0,-.22f),new Vector3(-.12f,0,.10f),new Vector3(.04f,0,-.04f),new Vector3(.17f,0,.08f),new Vector3(.36f,0,-.20f) },.040f,cream);
                Cylinder(parent,"Photo sun",new Vector3(-.23f,0,.20f),.085f,.014f,cream);
            }
        }

        void BuildWaxSeal(Transform parent, Vector3 position)
        {
            if (!wax)
            {
                wax = Paint("Grass green leaf wax",new Color(.55f,.59f,.40f)); wax.SetFloat("_Smoothness",.38f);
                waxInset = Paint("Pressed leaf in wax",new Color(.43f,.47f,.32f));
            }
            var seal = new GameObject("Grass green leaf wax seal"); seal.transform.SetParent(parent,false); seal.transform.localPosition = position;
            seal.transform.localScale = Vector3.one*.85f;
            WaxDisk(seal.transform);
            Ring(seal.transform,"Pressed circular rim",new Vector3(0,.043f,0),.130f,.122f,.006f,waxInset);
            LeafMotif(seal.transform,new Vector3(0,.046f,0),.19f,waxInset,wax);
        }

        void BuildCameraModel(Transform parent)
        {
            var cameraCloth = Paint("Camera moss fabric",new Color(.44f,.49f,.37f));
            cameraCloth.SetTexture("_BaseMap",Resources.Load<Texture2D>("DrawerPrototype/Velvet")); Normal(cameraCloth,paperNormal,.18f);
            var metal = Paint("Camera aged cream metal",new Color(.80f,.79f,.64f)); metal.SetFloat("_Smoothness",.32f);
            var ringShade = Paint("Lens ring metal",new Color(.38f,.40f,.31f)); ringShade.SetFloat("_Smoothness",.38f);
            var glass = Paint("Deep lens glass",new Color(.10f,.17f,.16f)); glass.SetFloat("_Smoothness",.8f);
            BevelBox(parent,"Rounded camera body",Vector3.zero,new Vector3(1.92f,.52f,1.42f),.18f,.07f,cameraCloth);
            BevelBox(parent,"Camera top plate",new Vector3(0,.03f,.48f),new Vector3(1.90f,.51f,.42f),.12f,.05f,metal);
            BevelBox(parent,"Viewfinder housing",new Vector3(.47f,.30f,.46f),new Vector3(.40f,.14f,.25f),.06f,.025f,dark);
            BevelBox(parent,"Viewfinder glass",new Vector3(.47f,.377f,.46f),new Vector3(.27f,.018f,.15f),.025f,.006f,glass);
            Cylinder(parent,"Lens mount",new Vector3(0,.285f,-.10f),1.00f,.12f,metal);
            Cylinder(parent,"Lens barrel",new Vector3(0,.405f,-.10f),.86f,.19f,dark);
            Ring(parent,"Raised focus ring",new Vector3(0,.515f,-.10f),.445f,.345f,.09f,ringShade);
            Ring(parent,"Inner lens rim",new Vector3(0,.505f,-.10f),.346f,.292f,.025f,dark);
            Cylinder(parent,"Recessed glass",new Vector3(0,.49f,-.10f),.584f,.012f,glass);
            Ring(parent,"Glass curved reflection",new Vector3(-.015f,.501f,-.08f),.246f,.234f,.003f,ringShade);
            BevelBox(parent,"Lens soft reflection",new Vector3(-.11f,.507f,.055f),new Vector3(.10f,.008f,.035f),.015f,.002f,metal).transform.localRotation = Quaternion.Euler(0,-34,0);
            Cylinder(parent,"Shutter base",new Vector3(-.62f,.305f,.48f),.25f,.08f,dark);
            Cylinder(parent,"Shutter cap",new Vector3(-.62f,.356f,.48f),.20f,.04f,metal);
            for (int side = -1; side <= 1; side += 2)
                BevelBox(parent,"Strap lug",new Vector3(side*.99f,.02f,.37f),new Vector3(.16f,.14f,.23f),.05f,.02f,ringShade);
        }

        void Stroke3D(Transform parent, string name, Vector3[] points, float width, Material material)
        {
            var obj = new GameObject(name); obj.transform.SetParent(parent,false);
            var line = obj.AddComponent<LineRenderer>(); line.useWorldSpace = false; line.positionCount = points.Length; line.SetPositions(points);
            line.generateLightingData = true;
            line.widthMultiplier = width; line.numCapVertices = 4; line.numCornerVertices = 3; line.sharedMaterial = material;
            line.shadowCastingMode = ShadowCastingMode.Off;
        }

        GameObject BevelBox(Transform parent, string name, Vector3 position, Vector3 size, float radius, float bevel, Material material)
        {
            const int perCorner = 5, count = perCorner*4;
            var vertices = new List<Vector3>(); var uv = new List<Vector2>(); var triangles = new List<int>();
            bevel = Mathf.Min(bevel,size.y*.45f); radius = Mathf.Min(radius,Mathf.Min(size.x,size.z)*.48f);
            for (int ring = 0; ring < 4; ring++)
            {
                bool inset = ring == 0 || ring == 3;
                float r = Mathf.Max(.001f,radius-(inset ? bevel : 0));
                float y = ring == 0 ? -size.y*.5f : ring == 1 ? -size.y*.5f+bevel : ring == 2 ? size.y*.5f-bevel : size.y*.5f;
                for (int corner = 0; corner < 4; corner++) for (int step = 0; step < perCorner; step++)
                {
                    float angle = Mathf.PI*.5f-corner*Mathf.PI*.5f-step/(float)(perCorner-1)*Mathf.PI*.5f;
                    float x = (corner < 2 ? 1 : -1)*(size.x*.5f-radius)+Mathf.Cos(angle)*r;
                    float z = (corner == 0 || corner == 3 ? 1 : -1)*(size.z*.5f-radius)+Mathf.Sin(angle)*r;
                    vertices.Add(new Vector3(x,y,z)); uv.Add(new Vector2(x/size.x+.5f,z/size.z+.5f));
                }
            }
            for (int ring = 0; ring < 3; ring++) for (int i = 0; i < count; i++)
            {
                int a = ring*count+i, b = ring*count+(i+1)%count;
                triangles.AddRange(new[] { a,b,b+count,a,b+count,a+count });
            }
            int bottom = vertices.Count; vertices.Add(new Vector3(0,-size.y*.5f,0)); uv.Add(Vector2.one*.5f);
            int top = vertices.Count; vertices.Add(new Vector3(0,size.y*.5f,0)); uv.Add(Vector2.one*.5f);
            for (int i = 0; i < count; i++) triangles.AddRange(new[] { bottom,(i+1)%count,i,top,3*count+i,3*count+(i+1)%count });
            var mesh = new Mesh { name = name, vertices = vertices.ToArray(), triangles = triangles.ToArray(), uv = uv.ToArray() };
            mesh.RecalculateNormals(); mesh.RecalculateTangents(); mesh.RecalculateBounds(); generated.Add(mesh);
            var obj = MeshObject(parent,name,mesh,material); obj.transform.localPosition = position; return obj;
        }

        void Ring(Transform parent, string name, Vector3 position, float outer, float inner, float height, Material material)
        {
            const int segments = 40;
            var vertices = new Vector3[segments*4]; var uv = new Vector2[vertices.Length]; var triangles = new List<int>();
            for (int row = 0; row < 4; row++) for (int i = 0; i < segments; i++)
            {
                float a = -i*Mathf.PI*2/segments, r = row < 2 ? outer : inner;
                vertices[row*segments+i] = new Vector3(Mathf.Cos(a)*r,(row == 0 || row == 3 ? -.5f : .5f)*height,Mathf.Sin(a)*r);
                uv[row*segments+i] = new Vector2(i/(float)segments,row/3f);
            }
            for (int row = 0; row < 4; row++) for (int i = 0; i < segments; i++)
            {
                int a = row*segments+i, b = row*segments+(i+1)%segments, c = ((row+1)%4)*segments+(i+1)%segments, d = ((row+1)%4)*segments+i;
                triangles.AddRange(new[] { a,b,c,a,c,d });
            }
            var mesh = new Mesh { name = name, vertices = vertices, triangles = triangles.ToArray(), uv = uv }; mesh.RecalculateNormals(); generated.Add(mesh);
            var obj = MeshObject(parent,name,mesh,material); obj.transform.localPosition = position;
        }
    }
}
