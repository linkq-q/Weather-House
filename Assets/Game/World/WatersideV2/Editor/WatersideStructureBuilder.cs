using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace WeatherHouse.WatersideV2.Editor
{
    public static class WatersideStructureBuilder
    {
        static Material stone, wood;
        static Mesh block;
        static Terrain terrain;
        static string output;
        public static void Build(Terrain ground, Transform parent, string folder)
        {
            terrain = ground;
            output = folder;
            stone = new Material(AssetDatabase.LoadAssetAtPath<Material>("Assets/HIGHLANDS - Stylized Environment/Materials/Forest/Stones_Rocks.mat"));
            stone.name="WarmLimestone";stone.SetColor("_Color",new Color(.64f,.62f,.57f));stone.enableInstancing=true;
            stone=WatersideNatureBuilder.Store(stone,folder+"/WarmLimestone.mat");
            wood = Material("WeatheredTimber", new Color(.36f, .25f, .13f), folder);
            var timberSource=AssetDatabase.LoadAssetAtPath<Material>("Assets/HIGHLANDS - Stylized Environment/Materials/Witches House/Fences.mat");
            if(timberSource&&timberSource.HasProperty("_Albedo"))
            {wood.SetTexture("_BaseMap",timberSource.GetTexture("_Albedo"));wood.SetColor("_BaseColor",Color.white);wood.SetTextureScale("_BaseMap",new Vector2(.07f,.63f));wood.SetTextureOffset("_BaseMap",new Vector2(.865f,.16f));}
            block = WatersideNatureBuilder.Store(BevelBlock(), folder + "/ChamferedBlock.asset");
            var root = new GameObject("石阶 · 跨水连接 · 自然岩岸").transform; root.SetParent(parent);
            foreach (var crossing in WatersideLayout.Crossings) Bridge(crossing, root);
            foreach (var step in WatersideLayout.Steps) Stair(step, root);
            ShoreRocks(root);
            Paving(root);
            PondLandmarks(root);
            ChimeGate(new Vector3(-1.8f,0,35.9f),270,root);
            ChimeGate(new Vector3(-14.6f,0,-62.6f),225,root);
            Boundaries(root);
        }
        static void Bridge(WatersideLayout.Crossing crossing, Transform parent)
        {
            var root = new GameObject(crossing.Name).transform; root.SetParent(parent);
            Vector3 direction = crossing.End - crossing.Start; direction.y = 0;
            float length = direction.magnitude;
            Quaternion yaw = Quaternion.LookRotation(direction.normalized);
            Vector3 side = yaw * Vector3.right;
            bool stones = crossing.Kind.IndexOf("Stepping", StringComparison.OrdinalIgnoreCase) >= 0;
            bool timber = crossing.Kind == "UpperBridge" || crossing.Kind == "MouthBridge";
            int count = Mathf.CeilToInt(length / (stones ? 1.55f : .6f));
            var stoneRandom = new System.Random(713);
            for (int i = 0; i < count; i++)
            {
                float t = (i + .5f) / count;
                Vector3 p = Vector3.Lerp(crossing.Start, crossing.End, t);
                float hump = !stones && !timber ? Mathf.Sin(t * Mathf.PI) * .48f : 0;
                p.y += hump;
                float height = stones ? Mathf.Max(.25f, p.y - WatersideLayout.GroundHeight(terrain, p.x, p.z)) : .28f;
                // Narrow visual joints keep a continuous collision route and leave real water below the deck.
                if(!stones&&!timber)
                {
                    for(int tile=0;tile<3;tile++)
                        Block("错缝石桥面 " + i+"-"+tile, p + side*((tile-1)*crossing.Width/3)-Vector3.up*height*.5f,
                            new Vector3(crossing.Width/3-.012f,height,length/count+.025f),yaw,stone,root,true);
                    foreach(int sign in new[]{-1,1})
                        Block("低石护栏",p+side*sign*(crossing.Width*.5f+.08f)+Vector3.up*.22f,
                            new Vector3(.36f,.46f,length/count+.01f),yaw,stone,root,true);
                }
                else if(stones)
                {
                    var rock=StoneVisual(p,new Vector3(crossing.Width*(.9f+(float)stoneRandom.NextDouble()*.1f),height+.25f,length/count*.94f),stoneRandom,root,"原石渡步 " + i);
                    rock.transform.rotation=yaw*Quaternion.Euler(0,-9+(float)stoneRandom.NextDouble()*18,0);
                    rock.transform.position+=Vector3.up*(p.y-rock.GetComponent<MeshRenderer>().bounds.max.y);
                    var tread=new GameObject("踏面 "+i).transform;tread.SetParent(root);
                    tread.SetPositionAndRotation(p-Vector3.up*.12f,yaw);
                    tread.gameObject.AddComponent<BoxCollider>().size=new Vector3(crossing.Width*.9f,.24f,length/count+.035f);
                }
                else Block("踏面 " + i, p - Vector3.up * height * .5f,
                    new Vector3(crossing.Width, height, length / count + .035f), yaw, timber ? wood : stone, root, true);
                if (!stones && (i == 0 || i == count - 1 || i % 6 == 0))
                    foreach (int sign in new[] { -1, 1 })
                    {
                        Vector3 post = p + side * (sign * (crossing.Width * .5f + .08f));
                        Block("桥栏石柱", post + Vector3.up * .38f, new Vector3(.34f, .86f, .4f), yaw, timber ? wood : stone, root, true);
                    }
            }
            if (!stones && !timber)
            {
                // Voussoirs follow the arch instead of filling the stream with a rectangular foundation.
                for (int i = 0; i < 15; i++)
                {
                    float t = (i + .5f) / 15;
                    Vector3 p = Vector3.Lerp(crossing.Start, crossing.End, t);
                    p.y -= 1.35f * Mathf.Pow(Mathf.Abs(t * 2 - 1), 1.7f) + .27f;
                    foreach (int sign in new[] { -1, 1 })
                        Block("拱券石", p + side * sign * (crossing.Width * .5f - .2f),
                            new Vector3(.45f, .62f, length / 15 + .06f), yaw * Quaternion.Euler((t - .5f) * -40, 0, 0), stone, root, false);
                }
            }
        }
        static void Stair(WatersideLayout.Crossing step, Transform parent)
        {
            Vector3 direction = step.End - step.Start; direction.y = 0;
            int count = Mathf.Max(2, Mathf.CeilToInt(Mathf.Abs(step.End.y - step.Start.y) / .09f));
            var character = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Characters/ForestSprite/Generated/ForestSprite.prefab").GetComponent<CharacterController>();
            float rise = Mathf.Abs(step.End.y - step.Start.y) / count;
            if (rise + .015f + character.skinWidth > character.stepOffset)
                throw new InvalidOperationException("The stone stairs exceed the original character's supported step height.");
            Quaternion rotation = Quaternion.LookRotation(direction.normalized);
            float tread = direction.magnitude / count;
            var root = new GameObject(step.Name).transform; root.SetParent(parent);
            for (int i = 0; i < count; i++)
            {
                Vector3 p = Vector3.Lerp(step.Start, step.End, (i + .5f) / count);
                p.y = Mathf.Lerp(step.Start.y, step.End.y, (i + 1f) / count) + .015f;
                float thickness = Mathf.Max(.22f, p.y - WatersideLayout.GroundHeight(terrain, p.x, p.z) + .12f);
                Block("苔石阶 " + i, p - Vector3.up * thickness * .5f, new Vector3(step.Width, thickness, tread + .06f), rotation, stone, root, true);
            }
        }
        static void ShoreRocks(Transform parent)
        {
            var root = new GameObject("断续岩块与草帽岸线").transform; root.SetParent(parent);
            var random = new System.Random(37651);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/HIGHLANDS - Stylized Environment/Prefabs/Forest/Rock_1.prefab");
            var placed = new List<Vector3>();
            foreach (Vector3 point in WatersideLayout.ShoreSamples)
            {
                if(WatersideLayout.TidePools.Any(pool=>Mathf.Abs(pool.SignedDistance(point.x,point.z))<.65f))continue;
                if (random.NextDouble() < .26 || WatersideLayout.PathEdgeDistance(point.x, point.z) < .85f) continue;
                if (placed.Any(p => Vector3.Distance(p, point) < 1.6f)) continue;
                Vector3 p = point; p.y = WatersideLayout.GroundHeight(terrain, p.x, p.z);
                var rock = (GameObject)PrefabUtility.InstantiatePrefab(prefab, root);
                rock.transform.rotation = Quaternion.Euler(-5 + random.Next(12), random.Next(360), -9 + random.Next(18));
                Bounds b = BoundsOf(rock);
                float size = 1.8f + (float)random.NextDouble() * 2.4f;
                rock.transform.localScale *= size / Mathf.Max(b.size.x, b.size.z);
                b = BoundsOf(rock);
                rock.transform.position += new Vector3(p.x - b.center.x, p.y - b.min.y - b.size.y * .52f, p.z - b.center.z);
                // Buried lower halves leave the top edge irregular and allow grass to overlap the rock crown.
                placed.Add(p);
            }
            foreach(var pool in WatersideLayout.TidePools)
                for(int i=0;i<12;i++)
                {
                    if(i%5==0)continue;
                    float a=i*Mathf.PI*2/12;
                    Vector3 p=pool.Center+new Vector3(Mathf.Cos(a)*(pool.Radii.x+.18f),0,Mathf.Sin(a)*(pool.Radii.y+.18f));
                    if(WatersideLayout.WalkablePathEdgeDistance(p.x,p.z)<.35f)continue;
                    float size=.48f+(float)random.NextDouble()*.45f;
                    StoneVisual(p,new Vector3(size,.38f+(float)random.NextDouble()*.25f,size*.8f),random,root,"潮池低矮礁石");
                }
            for (int i = 0; i < 200; i++)
            {
                Vector3 p = WatersideLayout.Design(14 + (float)random.NextDouble() * 67, 15 + (float)random.NextDouble() * 69);
                Vector3 origin = terrain.transform.position, size = terrain.terrainData.size;
                float slope = terrain.terrainData.GetSteepness((p.x - origin.x) / size.x, (p.z - origin.z) / size.z);
                if (slope < 29 || slope > 70 || WatersideLayout.PathEdgeDistance(p.x, p.z) < 2.5f || WatersideLayout.IsWater(p.x, p.z)) continue;
                p.y = WatersideLayout.GroundHeight(terrain, p.x, p.z);
                var rock = (GameObject)PrefabUtility.InstantiatePrefab(prefab, root);
                rock.transform.rotation = Quaternion.Euler(0, random.Next(360), 0);
                Bounds b = BoundsOf(rock);
                float width = 2.3f + (float)random.NextDouble() * 2.5f;
                rock.transform.localScale *= width / Mathf.Max(b.size.x,b.size.z);
                b = BoundsOf(rock);
                rock.transform.position += new Vector3(p.x-b.center.x,p.y-b.min.y-b.size.y*.5f,p.z-b.center.z);
            }
        }
        static void Paving(Transform parent)
        {
            var root = new GameObject("风化散石 · 草缝与零散砂砾").transform; root.SetParent(parent);
            var random = new System.Random(914);
            Vector3[] circles = { new Vector3(-23,0,32.4f),new Vector3(0,0,32.4f),WatersideLayout.MoonBay };
            foreach (var center in circles)
            {
                for (int i = 0; i < 18; i++)
                {
                    if (i % 7 == 0) continue;
                    float angle = i * Mathf.PI * 2 / 18;
                    Vector3 p = center + new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * (2.5f+(float)random.NextDouble()*.35f);
                    if(WatersideLayout.IsWater(p.x,p.z))continue;
                    StoneVisual(p, new Vector3(.7f+(float)random.NextDouble()*.65f,.12f,.65f+(float)random.NextDouble()*.4f),random,root,"断裂环台原石");
                }
            }
            foreach (Vector3[] route in WatersideLayout.Routes)
                for (int i = 1; i < route.Length - 1; i += 2)
                {
                    Vector3 p = route[i];
                    if (WatersideLayout.IsWater(p.x, p.z)) continue;
                    if(random.NextDouble()<.36)
                        StoneVisual(p, new Vector3(.55f+(float)random.NextDouble()*.7f,.11f,.4f+(float)random.NextDouble()*.6f),random,root,"埋入泥土的自然踏石");
                    for(int j=0;j<3;j++)
                    {
                        Vector3 pebble=p+new Vector3((float)random.NextDouble()-.5f,0,(float)random.NextDouble()-.5f)*2;
                        if(WatersideLayout.IsWater(pebble.x,pebble.z))continue;
                        float size=.06f+(float)random.NextDouble()*.16f;
                        StoneVisual(pebble,new Vector3(size,size*.35f,size*.7f),random,root,"小径碎石");
                    }
                }
        }

        static GameObject StoneVisual(Vector3 position,Vector3 size,System.Random random,Transform parent,string name)
        {
            var source=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/HIGHLANDS - Stylized Environment/Prefabs/Forest/Stone_"+(1+random.Next(4))+".prefab");
            var filter=source.GetComponentsInChildren<MeshFilter>().First(f=>f.name.Contains("LOD0"));
            var go=new GameObject(name,typeof(MeshFilter),typeof(MeshRenderer));go.transform.SetParent(parent);
            go.GetComponent<MeshFilter>().sharedMesh=filter.sharedMesh;
            go.GetComponent<MeshRenderer>().sharedMaterials=filter.GetComponent<MeshRenderer>().sharedMaterials;
            Bounds b=filter.sharedMesh.bounds;
            go.transform.localScale=new Vector3(size.x/b.size.x,size.y/b.size.y,size.z/b.size.z);
            go.transform.rotation=Quaternion.Euler(0,random.Next(360),0);
            Vector3 bottom=new Vector3(b.center.x,b.min.y+b.size.y*.25f,b.center.z);
            position.y=WatersideLayout.GroundHeight(terrain,position.x,position.z);
            go.transform.position=position-go.transform.TransformVector(bottom);
            go.isStatic=true;return go;
        }

        static void PondLandmarks(Transform parent)
        {
            var group=new GameObject("苔石残柱与草土台顶").transform;group.SetParent(parent);
            var random=new System.Random(654);
            foreach(Vector3 point in new[]{new Vector3(-23,0,32.4f),new Vector3(0,0,32.4f),WatersideLayout.MoonBay})
            {
                Vector3 rear=point+new Vector3(2.4f,0,3.2f);
                if(WatersideLayout.IsWater(rear.x,rear.z,.4f))rear=point+new Vector3(-2.4f,0,2.8f);
                rear.y=WatersideLayout.GroundHeight(terrain,rear.x,rear.z);
                for(int side=0;side<2;side++)
                {
                    Vector3 p=rear+Vector3.right*side*2.2f;
                    if(WatersideLayout.IsWater(p.x,p.z,.3f))continue;
                    p.y=WatersideLayout.GroundHeight(terrain,p.x,p.z);
                    int courses=side==0?5:3;
                    GameObject last=null;
                    for(int row=0;row<courses;row++)
                    {
                        last=StoneVisual(p,new Vector3(.78f+(row%2)*.06f,.42f,.72f),random,group,"堆砌风化原石柱");
                        last.transform.position+=Vector3.up*row*.30f;
                        last.AddComponent<MeshCollider>().sharedMesh=last.GetComponent<MeshFilter>().sharedMesh;
                    }
                    Vector3 cap=p;cap.y=last.GetComponent<MeshRenderer>().bounds.max.y+.005f;
                    GrassCap(cap,.27f,group,random);
                }
            }
        }

        static void GrassCap(Vector3 center,float radius,Transform parent,System.Random random)
        {
            var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(output+"/Nature/ShortGrass0.asset");
            var mat=AssetDatabase.LoadAssetAtPath<Material>(output+"/Nature/ShortGrass.mat");
            if(!mesh||!mat)throw new InvalidOperationException("Build calibrated grass before planting soil caps.");
            for(int i=0;i<18;i++)
            {
                float a=(float)random.NextDouble()*Mathf.PI*2,r=Mathf.Sqrt((float)random.NextDouble())*radius;
                var go=new GameObject("石顶土缝短草",typeof(MeshFilter),typeof(MeshRenderer));go.transform.SetParent(parent);
                go.transform.SetPositionAndRotation(center+new Vector3(Mathf.Cos(a),0,Mathf.Sin(a))*r,Quaternion.Euler(0,random.Next(360),0));
                go.transform.localScale=Vector3.one*(.3f+(float)random.NextDouble()*.2f);
                go.GetComponent<MeshFilter>().sharedMesh=mesh;go.GetComponent<MeshRenderer>().sharedMaterial=mat;
            }
        }

        static void ChimeGate(Vector3 site,float yaw,Transform parent)
        {
            site.y=WatersideLayout.GroundHeight(terrain,site.x,site.z);
            Quaternion rotation=Quaternion.Euler(0,yaw,0);
            Vector3 side=rotation*Vector3.right;
            var group=new GameObject("水畔风铃架 · 铜铃与木签").transform;group.SetParent(parent);
            foreach(int sign in new[]{-1,1})
            {
                Vector3 p=site+side*sign*1.6f;p.y=WatersideLayout.GroundHeight(terrain,p.x,p.z);
                Block("风化木柱",p+Vector3.up*1.35f,new Vector3(.19f,2.9f,.22f),rotation,wood,group,true);
            }
            Block("横梁",site+Vector3.up*2.62f,new Vector3(3.8f,.17f,.2f),rotation,wood,group,false);
            Mesh bell=WatersideNatureBuilder.Store(BellMesh(),output+"/SmallBronzeBell.asset");
            Material brass=Material("AgedBronze",new Color(.55f,.34f,.10f),output);brass.SetFloat("_Metallic",.55f);brass.SetFloat("_Smoothness",.32f);
            for(int i=0;i<3;i++)
            {
                var hanger=new GameObject("第"+(i+1)+"枚风铃").transform;hanger.SetParent(group);
                hanger.position=site+side*(i-1)*.86f+Vector3.up*2.57f;
                float drop=.25f+i*.1f;
                var rope=Block("系绳",hanger.position-Vector3.up*drop*.5f,new Vector3(.018f,drop,.018f),rotation,wood,hanger,false);
                var bellObject=new GameObject("铜铃",typeof(MeshFilter),typeof(MeshRenderer));bellObject.transform.SetParent(hanger,false);bellObject.transform.localPosition=Vector3.down*(drop+.22f);
                bellObject.GetComponent<MeshFilter>().sharedMesh=bell;bellObject.GetComponent<MeshRenderer>().sharedMaterial=brass;
                Block("铃舌木签",hanger.position-Vector3.up*(drop+.52f),new Vector3(.13f,.33f,.045f),rotation,wood,hanger,false);
                Block("木签细绳",hanger.position-Vector3.up*(drop+.3f),new Vector3(.012f,.3f,.012f),rotation,wood,hanger,false);
                var sway=hanger.gameObject.AddComponent<WatersideChimeSway>();sway.phase=i*1.9f;
            }
        }

        static Mesh BellMesh()
        {
            const int count=16;
            Vector2[] profile={new Vector2(.035f,.25f),new Vector2(.09f,.23f),new Vector2(.13f,.16f),new Vector2(.16f,.06f),
                new Vector2(.20f,0),new Vector2(.17f,.012f),new Vector2(.125f,.08f),new Vector2(.10f,.17f),new Vector2(.035f,.21f)};
            var vertices=new List<Vector3>();var triangles=new List<int>();
            foreach(Vector2 ring in profile)for(int i=0;i<count;i++)
            {float a=i*Mathf.PI*2/count;vertices.Add(new Vector3(Mathf.Cos(a)*ring.x,ring.y,Mathf.Sin(a)*ring.x));}
            for(int ring=0;ring<profile.Length;ring++)for(int i=0;i<count;i++)
            {int a=ring*count+i,b=ring*count+(i+1)%count,c=((ring+1)%profile.Length)*count+i,d=((ring+1)%profile.Length)*count+(i+1)%count;triangles.AddRange(new[]{a,b,c,b,d,c});}
            var mesh=new Mesh{name="带内壁与厚口的铜铃"};mesh.SetVertices(vertices);mesh.SetTriangles(triangles,0);mesh.RecalculateNormals();mesh.RecalculateBounds();return mesh;
        }
        static void Boundaries(Transform parent)
        {
            // Backstops are buried inside the visible mountain ridges and beyond the reachable shoreline.
            foreach (var data in new[] { new Vector4(-85, 0, 2, 180), new Vector4(0, 85, 180, 2), new Vector4(91, 0, 2, 180), new Vector4(0, -91, 180, 2) })
            {
                var go = new GameObject("地图边界碰撞"); go.transform.SetParent(parent);
                go.transform.position = new Vector3(data.x, 15, data.y);
                go.AddComponent<BoxCollider>().size = new Vector3(data.z, 70, data.w);
            }
        }
        static GameObject Block(string name, Vector3 position, Vector3 scale, Quaternion rotation, Material material, Transform parent, bool collision)
        {
            var go = new GameObject(name); go.transform.SetParent(parent); go.transform.SetPositionAndRotation(position, rotation); go.transform.localScale = scale;
            go.AddComponent<MeshFilter>().sharedMesh = block; go.AddComponent<MeshRenderer>().sharedMaterial = material;
            if (collision) go.AddComponent<BoxCollider>();
            return go;
        }
        static Material Material(string name, Color color, string folder)
        {
            var mat = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name, enableInstancing = true };
            mat.SetColor("_BaseColor", color); mat.SetFloat("_Smoothness", .08f);
            return WatersideNatureBuilder.Store(mat, folder + "/" + name + ".mat");
        }
        static Mesh BevelBlock()
        {
            var vertices = new List<Vector3>(); var triangles = new List<int>();
            Vector2[] ring = { new Vector2(-.42f,-.5f),new Vector2(.42f,-.5f),new Vector2(.5f,-.42f),new Vector2(.5f,.42f),new Vector2(.42f,.5f),new Vector2(-.42f,.5f),new Vector2(-.5f,.42f),new Vector2(-.5f,-.42f) };
            for (int row = 0; row < 4; row++)
                for (int i = 0; i < 8; i++)
                { float inset = row == 0 || row == 3 ? .88f : 1; vertices.Add(new Vector3(ring[i].x * inset, new[] { -.5f, -.42f, .42f, .5f }[row], ring[i].y * inset)); }
            for (int row = 0; row < 3; row++)
                for (int i = 0; i < 8; i++)
                { int a = row * 8 + i, b = row * 8 + (i + 1) % 8; triangles.AddRange(new[] { a,b,a+8,b,b+8,a+8 }); }
            vertices.Add(new Vector3(0,-.5f,0)); vertices.Add(new Vector3(0,.5f,0));
            for (int i = 0; i < 8; i++) triangles.AddRange(new[] {32,(i+1)%8,i,33,24+i,24+(i+1)%8});
            for (int i = 0; i < triangles.Count; i += 3) { int temp = triangles[i + 1]; triangles[i + 1] = triangles[i + 2]; triangles[i + 2] = temp; }
            var mesh = new Mesh { name = "切角石木构件" }; mesh.SetVertices(vertices); mesh.SetUVs(0,vertices.Select(v=>new Vector2(v.x+.5f,v.z+v.y*.2f+.5f)).ToList()); mesh.SetTriangles(triangles,0); mesh.RecalculateNormals(); mesh.RecalculateBounds(); return mesh;
        }
        static Bounds BoundsOf(GameObject root)
        { var renderers = root.GetComponentsInChildren<Renderer>(); var b = renderers[0].bounds; foreach (var r in renderers) b.Encapsulate(r.bounds); return b; }
    }
}
