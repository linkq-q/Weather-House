using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using WeatherHouse.Hillside;
using Object = UnityEngine.Object;

namespace WeatherHouse.WatersideV2.Editor
{
    // Adds authored outdoor rooms to the current scene; never regenerates the user's map.
    public static class WatersideLivingSpaces
    {
        const string Review = "WatersideV3Review";
        const string RootName = "09 成套生活空间 · 20261024";
        static Terrain terrain;
        static Transform root;
        static readonly List<Vector4> clearings = new List<Vector4>();
        static readonly List<string> notes = new List<string>();
        static int fences, chairs, settings, clusters;
        const string HarborTableId="GlobalObjectId_V1-2-0d5e02f552d97b34a8c818aceb185e42-6791745531351820810-0";
        const string HarborChairId="GlobalObjectId_V1-2-0d5e02f552d97b34a8c818aceb185e42-4478514174680647034-0";
        static readonly Vector3 HarborShift=new Vector3(-1.9660721f,0,1.385714f);
        public static void RemoveUnsavedDraft()
        {
            if(SceneManager.GetActiveScene().path!=WatersideSceneBuilder.ScenePath || EditorApplication.isPlaying)
                throw new InvalidOperationException("Remove only the V2 edit-mode dressing draft.");
            if(File.Exists(Review+"/living-placement.txt"))throw new InvalidOperationException("Do not discard an applied layout.");
            terrain=Object.FindObjectOfType<Terrain>();
            var snapshot=JsonUtility.FromJson<Snapshot>(File.ReadAllText(Review+"/user-edits-baseline.json"));
            var restore=new List<(Transform transform,Vector3 position)>();
            foreach(var item in snapshot.items)
            {
                GlobalObjectId id;GlobalObjectId.TryParse(item.id,out id);
                var t=GlobalObjectId.GlobalObjectIdentifierToObjectSlow(id) as Transform;
                Vector3 fitted;bool groundMoved=WatersideHarborGround.TryExpected(item.id,out fitted);
                bool shifted=t && (IsHarborFurniture(item)||groundMoved) && (t.position-ExpectedPosition(item)).sqrMagnitude<=.000001f;
                if(!t||(!shifted && (t.position-item.position).sqrMagnitude>.000001f)||Quaternion.Angle(t.rotation,item.rotation)>.01f||
                    (t.localScale-item.scale).sqrMagnitude>.000001f||t.gameObject.activeSelf!=item.active)
                    throw new InvalidOperationException("New user edit appeared: "+item.path);
                if(shifted && IsHarborFurniture(item))restore.Add((t,item.position));
            }
            WatersideHarborGround.Restore(terrain);
            foreach(var item in restore)item.transform.position=item.position;
            var owned=All().FirstOrDefault(t=>t.name==RootName);
            if(owned)Object.DestroyImmediate(owned.gameObject);
            EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
        }
        [Serializable] sealed class Item { public string id, path; public Vector3 position, scale; public Quaternion rotation; public bool active; }
        [Serializable] sealed class Snapshot { public string utc; public bool playing, dirty; public Item[] items; }

        public static void Inspect()
        {
            var scene = SceneManager.GetActiveScene();
            if (scene.path != WatersideSceneBuilder.ScenePath || EditorApplication.isPlaying)
                throw new InvalidOperationException("Use V2 in edit mode.");
            Directory.CreateDirectory(Review);
            var all = All();
            var snap = new Snapshot { utc = DateTime.UtcNow.ToString("O"), playing = EditorApplication.isPlaying,
                dirty = scene.isDirty, items = all.Where(t => t.name.Contains("Fence") || t.name.Contains("木椅") ||
                    t.name.Contains("小桌") || t.name.Contains("v05")).Select(t => new Item {
                    id = GlobalObjectId.GetGlobalObjectIdSlow(t).ToString(), path = PathOf(t), position = t.position,
                    rotation = t.rotation, scale = t.localScale, active = t.gameObject.activeSelf }).ToArray() };
            string file = Review + "/user-edits-baseline.json";
            if (!File.Exists(file)) File.WriteAllText(file, JsonUtility.ToJson(snap,true));
            var lines = all.Where(t => t.parent && (t.parent.name.StartsWith("1 归家") || t.parent.name.StartsWith("2 老树") ||
                t.parent.name.StartsWith("5 海岬"))).Select(t => PathOf(t)+" pos="+t.position.ToString("F2")+" bounds="+BoundsOf(t.gameObject));
            File.WriteAllLines(Review + "/live-props.txt", lines);
        }

        public static void Apply()
        {
            WatersideSceneBuilder.RequireSaved(); Inspect();
            if (All().Any(t => t.name == RootName)) throw new InvalidOperationException("Living spaces already applied; refine that group in place.");
            terrain = Object.FindObjectOfType<Terrain>();
            WatersidePropsBuilder.PrepareAdditions(terrain);
            root = Group(RootName,null); clearings.Clear(); notes.Clear(); fences=chairs=settings=clusters=0;
            MoveHarborFurniture();
            var harborSource=All().Single(t=>t.name=="6 港湾短码头");
            WatersideHarborGround.Apply(terrain,harborSource);
            // The two unchanged generated harbor pieces move inland; all user-tuned furniture stays in place.
            foreach (Transform table in All().Where(t => t.name == "岸边小桌").ToArray()) CompleteTable(table, Group(table.parent.name+" · 四人桌席",root));

            Transform home = Group("01 归家院 · 门道与储物边界",root);
            Rails(home, "门前院栏西段", P(-48,-36),P(-44,-36));
            Rails(home, "门前院栏东段", P(-40,-36),P(-30,-36));
            Rails(home, "院后栏",P(-39,-22),P(-30,-22));
            Rails(home, "东侧院栏",P(-29,-33),P(-29,-28));
            Supply(home,P(-38.5f,-24),15); Supply(home,P(-32,-35.7f),-10);
            Firewood(home,P(-39,-35.7f),90); Planters(home,P(-39.5f,-32.5f),90);
            clearings.Add(new Vector4(-35.5f,-29.7f,6.7f,4.8f));

            Transform old = Group("02与13 老树院 · 晾晒储木和水岸边界",root);
            Rails(old,"背风院栏",P(-10,2.7f),P(2,2.7f));
            Rails(old,"东水岸护栏",P(6,1),P(6,-3.4f));
            Rails(old,"南侧柴院栏",P(-9,-11.8f),P(-2,-11.8f));
            Supply(old,P(-9.2f,-4.5f),90); Supply(old,P(4.4f,-2),-15);
            Firewood(old,P(-9.5f,-8),0); Firewood(old,P(-8.3f,.8f),0);
            Add("Camp/Dryer_2","第二组晾晒架",P(-8.5f,-.8f),2,0,old);
            Planters(old,P(3.4f,-3.3f),0);
            Benches(old,P(3,-9.7f),P(5,-6.1f),270);
            clearings.Add(new Vector4(-1.5f,-5.3f,7.5f,6.3f));

            Transform west = Group("05 西岸静潭 · 林间休息院",root);
            Rails(west,"外侧木栏",P(-27,28),P(-27,36));
            Rails(west,"台地后栏",P(-26,37),P(-21,37));
            Benches(west,P(-22.8f,29.4f),P(-22.4f,35.4f),90);
            Supply(west,P(-25.6f,30),90); Firewood(west,P(-25.2f,36.4f),90);
            Ring(west,P(-22,32.3f),4.0f,4.7f);
            clearings.Add(new Vector4(-22.6f,32.3f,4.4f,5.5f));

            Transform east = Group("10 东岸风铃院 · 两处休憩与补给",root);
            Rails(east,"山侧院栏",P(4.9f,27),P(4.9f,37));
            Rails(east,"北侧院栏",P(4,38),P(-1.4f,38));
            Benches(east,P(2.4f,32),P(.5f,36.6f),270);
            Supply(east,P(4,35.8f),270); Planters(east,P(3.4f,29.8f),0);
            Ring(east,P(-.5f,32.3f),3.8f,4.5f);
            clearings.Add(new Vector4(0,32.4f,4.8f,5.8f));

            Transform cape = Group("18 海岬院 · 四人茶席与避雨储物",root);
            var capeTable = Add("Camp/Table_1","新增四人茶桌",P(51f,-7.2f),.85f,90,cape);
            CompleteTable(capeTable.transform,cape);
            Rails(cape,"避雨院南侧栏",P(50,-10.6f),P(54.5f,-12));
            Rails(cape,"临海侧栏",P(60,-5),P(60,1.8f));
            Rails(cape,"棚后栏",P(51,4.3f),P(58.5f,4.3f));
            Supply(cape,P(50.6f,1.7f),0); Supply(cape,P(57.9f,2.7f),90);
            Firewood(cape,P(51.2f,3.2f),0); Planters(cape,P(59.2f,-2.2f),90);
            clearings.Add(new Vector4(54.3f,-3.6f,5.6f,5.8f));

            Transform harbor = Group("04 港口院 · 排列补给与候船区",root);
            Rails(harbor,"候船区护栏",P(51,-48),P(57,-48));
            Rails(harbor,"仓储背栏",P(50,-38),P(55,-38));
            Supply(harbor,P(52.4f,-37.7f),85); Supply(harbor,P(50,-45),90);
            Firewood(harbor,P(51,-47),90);
            clearings.Add(new Vector4(53.6f,-42,4.7f,5.4f));

            Transform moon = Group("20 月湾沙台 · 休憩席与外扩石边",root);
            Ring(moon,P(-9,-61.2f),4.9f,4.3f);
            Benches(moon,P(-5,-59),P(-6.5f,-64.4f),260);
            Supply(moon,P(-4.5f,-61.8f),-20); Firewood(moon,P(-14.4f,-56.8f),70);
            Rails(moon,"背风两段栏",P(-13,-55.5f),P(-8,-55.5f));
            Planters(moon,P(-5.5f,-57.6f),90);
            clearings.Add(new Vector4(-9,-61.2f,5.6f,4.8f));

            Transform pasture = Group("牧场 · 成排边栏与双食槽",root);
            Rails(pasture,"牧场南边栏",P(31,10),P(45,10));
            Rails(pasture,"牧场东边栏",P(47,12),P(47,25));
            Rails(pasture,"牧场北边栏",P(45,27),P(34,27));
            Add("Camp/Trough","牧场食槽一",P(42,12),.55f,0,pasture);
            Add("Camp/Trough","牧场食槽二",P(44,12),.55f,0,pasture);
            Supply(pasture,P(32,12),0);
            Check();
            PaintUse();
            Undo.FlushUndoRecordObjects(); AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            if (!EditorSceneManager.SaveScene(SceneManager.GetActiveScene())) throw new IOException("Could not save dressed V2 scene.");
            File.WriteAllLines(Review+"/living-placement.txt",new[]{"utc="+DateTime.UtcNow.ToString("O"),"fenceSegments="+fences,
                "addedChairs="+chairs,"tableSettings="+settings,"supplyClusters="+clusters}.Concat(notes));
        }

        static void CompleteTable(Transform table, Transform parent)
        {
            Quaternion yaw=Quaternion.Euler(0,table.eulerAngles.y,0);
            Bounds tableProjection=MeshProjection(table.gameObject,yaw,true);
            Vector3 center=yaw*tableProjection.center;center.y=0;
            var existing=ChairRoots().Where(t=>Flat(BoundsOf(t.gameObject).center,center)<2.8f).ToArray();
            // A four-place table has two seats on each long side; existing user chairs are left exactly where placed.
            int wanted=Mathf.Max(0,4-existing.Length);
            bool longX=tableProjection.size.x>=tableProjection.size.z;
            float longHalf=longX?tableProjection.extents.x:tableProjection.extents.z;
            float shortHalf=longX?tableProjection.extents.z:tableProjection.extents.x;
            var candidates=new List<Vector3>();
            foreach(float clearance in new[]{.48f,.73f})
            {
                foreach(float side in new[]{-1f,1f})foreach(float along in new[]{-.55f,.55f})
                {
                    Vector3 p=new Vector3(along*longHalf,0,side*(shortHalf+clearance));
                    candidates.Add(longX?p:new Vector3(p.z,0,p.x));
                }
                foreach(float end in new[]{-1f,1f})
                {
                    Vector3 p=new Vector3(end*(longHalf+clearance),0,0);
                    candidates.Add(longX?p:new Vector3(0,0,p.x));
                }
            }
            var occupied=existing.Select(t=>BoundsOf(t.gameObject).center).ToList();
            foreach(Vector3 offset in candidates.OrderByDescending(o=>occupied.Count==0?10:occupied.Min(p=>Flat(p,center+yaw*o))))
            {
                if(wanted==0)break; Vector3 p=center+yaw*offset;
                if(occupied.Any(c=>Flat(c,p)<.77f))continue;
                float turn=Mathf.Atan2(center.x-p.x,center.z-p.z)*Mathf.Rad2Deg;
                var chair=Add("Witches House/Chair","四人桌配套木椅",p,1.05f,turn,parent);
                string rejection;
                if(!SafeChair(chair,out rejection))
                {
                    notes.Add("Rejected chair at "+p.ToString("F2")+": "+rejection);
                    Object.DestroyImmediate(chair);continue;
                }
                chairs++;wanted--;occupied.Add(BoundsOf(chair).center);
            }
            if(wanted>0){File.WriteAllLines(Review+"/chair-candidates.txt",notes);throw new InvalidOperationException("Four-seat table could not be completed: "+PathOf(table));}
            float top=table.GetComponentsInChildren<Renderer>().Where(r=>r.gameObject.activeInHierarchy && !PathOf(r.transform).Contains("桌上陶器")).Max(r=>r.bounds.max.y);
            // Tableware height is taken from the table mesh, before the new place settings are parented nearby.
            for(int i=0;i<4;i++)
            {
                Vector3 local=new Vector3((i%2==0?-.43f:.43f),0,(i<2?-.27f:.27f));
                Vector3 p=center+yaw*local;p.y=top+.008f;
                var plate=WatersidePropsBuilder.CreateAddition("Camp Props/Plate","配套餐盘",p,.028f,table.eulerAngles.y,parent,false);
                foreach(var c in plate.GetComponentsInChildren<Collider>())Object.DestroyImmediate(c);
                Vector3 cup=p+yaw*new Vector3(.14f,0,i<2?.12f:-.12f);
                var go=WatersidePropsBuilder.CreateAddition("Camp Props/Cup","配套餐杯",cup,.11f,i*19,parent,false);
                foreach(var c in go.GetComponentsInChildren<Collider>())Object.DestroyImmediate(c);settings++;
            }
            clearings.Add(new Vector4(center.x,center.z,2.6f,2.3f));
        }

        static void Rails(Transform parent,string label,Vector3 a,Vector3 b)
        {
            Transform group=Group(label,parent);float length=Flat(a,b);int count=Mathf.Max(1,Mathf.CeilToInt(length/2));
            for(int i=0;i<count;i++)
            {
                Vector3 p=Vector3.Lerp(a,b,(i+.5f)/count);
                if(WatersideLayout.WalkablePathEdgeDistance(p.x,p.z)<.55f || WatersideLayout.IsWater(p.x,p.z,.3f))continue;
                // Avoid layering over the user's already authored fence sections.
                if(All().Any(t=>t.name.StartsWith("Fence_",StringComparison.Ordinal)&&!t.IsChildOf(root)&&Flat(t.position,p)<1.0f))continue;
                var go=Add("Witches House/Fence_2","连续木栏 "+i,p,.88f,0,group);
                Bounds bounds=BoundsOf(go);go.transform.localScale=Vector3.Scale(go.transform.localScale,new Vector3(length/count/bounds.size.x,1,1));
                Vector3 right=(b-a).normalized;float half=length/count*.5f;
                right.y=(Height(p+right*half)-Height(p-right*half))/(2*half);right.Normalize();
                Vector3 forward=Vector3.Cross(right,Vector3.up).normalized;
                go.transform.rotation=Quaternion.LookRotation(forward,Vector3.Cross(forward,right));
                Fit(go,p,Mathf.Min(Height(p+right*half),Height(p-right*half))-.045f); fences++;
            }
            notes.Add(label+" span="+length.ToString("F1")+"m segments="+group.childCount);
        }
        static void Supply(Transform parent,Vector3 center,float yaw)
        {
            Transform group=Group("成组桶箱补给 "+clusters++,parent);
            var layout=new[]{new Vector3(-.65f,0,0),new Vector3(.3f,0,.15f),new Vector3(1.15f,0,0),new Vector3(.9f,0,1.3f)};
            for(int i=0;i<layout.Length;i++)
            {
                Vector3 p=center+Quaternion.Euler(0,yaw,0)*layout[i];
                var go=Add(i<2?"Camp/Barrel_1":"Camp Props/Box_1",i<2?"成组木桶":"成组木箱",p,i<2?.85f-i*.14f:.62f,yaw+i*7,group);
                if(i==3){Bounds b=BoundsOf(go);Vector3 top=b.center;top.y=b.max.y-.01f;WatersidePropsBuilder.CreateAddition("Camp Props/Box_2","叠放小箱",top,.38f,yaw-11,group,false);}
            }
            clearings.Add(new Vector4(center.x,center.z,2,1.5f));
        }
        static void Firewood(Transform parent,Vector3 p,float yaw)
        {
            Transform group=Group("整齐储柴与树桩",parent);
            for(int i=0;i<6;i++)
            {
                Vector3 local=new Vector3((i%3-1)*.35f,0,(i/3)*.48f);
                Add("Camp/Log_"+(i%3+1),"成排短柴",p+Quaternion.Euler(0,yaw,0)*local,.28f,yaw,group);
            }
            Add("Forest/Stump_1","劈柴矮桩",p+Quaternion.Euler(0,yaw,0)*new Vector3(1.1f,0,0),.45f,yaw,group);
            clearings.Add(new Vector4(p.x,p.z,2,1.2f));
        }
        static void Planters(Transform parent,Vector3 p,float yaw)
        {
            var group=Group("成组三只庭院花桶",parent);
            var flowers=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/World/WatersideV2/Generated/Nature/Plants/Prefabs/R06_Daisy_v1.prefab");
            if(!flowers)throw new FileNotFoundException("The approved daisy planter model is missing.");
            for(int i=0;i<3;i++)
            {
                Vector3 point=p+Quaternion.Euler(0,yaw,0)*new Vector3(i*.55f,0,(i%2)*.18f);
                var bucket=Add("Camp Props/Bucket","庭院花桶",point,.36f,i*27,group);
                if(!flowers)continue;
                var plant=(GameObject)PrefabUtility.InstantiatePrefab(flowers,group);var b=BoundsOf(plant);
                plant.transform.localScale*=.48f/Mathf.Max(.01f,b.size.y);b=BoundsOf(bucket);Fit(plant,b.center,b.max.y-.08f);
            }
        }
        static void Benches(Transform parent,Vector3 a,Vector3 b,float yaw)
        {
            foreach(var point in new[]{a,b})
            {
                var bench=Add("Forest/Log_1","成组临水倒木凳",point,.48f,yaw,parent);
                var size=BoundsOf(bench).size;bench.transform.localScale*=Mathf.Min(1,2.7f/Mathf.Max(size.x,size.z));Fit(bench,point,Height(point)-.015f);
                clearings.Add(new Vector4(point.x,point.z,1.8f,1.2f));
            }
        }
        static void Ring(Transform parent,Vector3 center,float rx,float rz)
        {
            var group=Group("外扩断续石边 · 草缝保留",parent);
            for(int i=0;i<26;i++)
            {
                if(i%8<2)continue;float a=i*Mathf.PI*2/26;
                Vector3 p=center+new Vector3(Mathf.Cos(a)*rx,0,Mathf.Sin(a)*rz);
                if(WatersideLayout.IsWater(p.x,p.z,.2f))continue;
                var rock=Add("Forest/Stone_"+(i%4+1),"浅埋外环石",p,.12f,21*i,group);
                var bounds=BoundsOf(rock);rock.transform.localScale=Vector3.Scale(rock.transform.localScale,new Vector3(.8f/bounds.size.x,1,.65f/bounds.size.z));
                Fit(rock,p,Height(p)-.035f);
            }
        }
        static void PaintUse()
        {
            TerrainData d=terrain.terrainData;int size=d.alphamapResolution;
            var alpha=d.GetAlphamaps(0,0,size,size);
            for(int z=0;z<size;z++)for(int x=0;x<size;x++)
            {
                float wx=terrain.transform.position.x+x/(float)(size-1)*d.size.x,wz=terrain.transform.position.z+z/(float)(size-1)*d.size.z;
                float wear=Use(wx,wz);if(wear<=0 || WatersideLayout.IsWater(wx,wz))continue;
                float target=wear*Mathf.Lerp(.48f,.72f,Mathf.PerlinNoise(wx*.7f+23,wz*.7f+17));
                if(target<=alpha[z,x,1])continue;float remaining=1-alpha[z,x,1];
                for(int layer=0;layer<d.alphamapLayers;layer++)if(layer!=1)alpha[z,x,layer]*=(1-target)/Mathf.Max(.0001f,remaining);
                alpha[z,x,1]=target;
            }
            d.SetAlphamaps(0,0,alpha);EditorUtility.SetDirty(d);
            var grass=Object.FindObjectOfType<HillsideGrassRenderer>();var map=grass.placementMap;var pixels=map.GetPixels();
            for(int z=0;z<map.height;z++)for(int x=0;x<map.width;x++)
            {
                float wx=terrain.transform.position.x+x/(float)(map.width-1)*d.size.x,wz=terrain.transform.position.z+z/(float)(map.height-1)*d.size.z;
                float wear=Use(wx,wz);if(wear<=0)continue;int i=z*map.width+x;
                // Grass remains on the floor and path; use mainly short, worn blades around furniture.
                pixels[i].r=Mathf.Min(pixels[i].r,Mathf.Lerp(1,.38f,wear));pixels[i].b=Mathf.Max(pixels[i].b,wear*.85f);
            }
            grass.enabled=false;map.SetPixels(pixels);map.Apply();EditorUtility.SetDirty(map);grass.enabled=true;
        }
        static float Use(float x,float z)
        {
            float result=0;foreach(var c in clearings){float q=Mathf.Sqrt(Mathf.Pow((x-c.x)/c.z,2)+Mathf.Pow((z-c.y)/c.w,2));
                result=Mathf.Max(result,1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(.55f,1.12f,q)));}return result;
        }

        public static void Check()
        {
            terrain=Object.FindObjectOfType<Terrain>();
            if(!terrain)throw new InvalidOperationException("The existing V2 terrain is required for furniture support checks.");
            WatersideHarborGround.Validate(terrain);
            var snapshot=JsonUtility.FromJson<Snapshot>(File.ReadAllText(Review+"/user-edits-baseline.json"));
            var failures=new List<string>();int preserved=0;
            foreach(var item in snapshot.items)
            {
                GlobalObjectId id;if(!GlobalObjectId.TryParse(item.id,out id))throw new InvalidDataException(item.id);
                var t=GlobalObjectId.GlobalObjectIdentifierToObjectSlow(id) as Transform;
                if(!t || (t.position-ExpectedPosition(item)).sqrMagnitude>.000001f || Quaternion.Angle(t.rotation,item.rotation)>.01f ||
                    (t.localScale-item.scale).sqrMagnitude>.000001f || t.gameObject.activeSelf!=item.active)failures.Add("User transform changed: "+item.path);
                else preserved++;
            }
            var all=All();var r=all.FirstOrDefault(t=>t.name==RootName);
            if(!r)failures.Add("Missing living-space root");
            if(r && r.GetComponentsInChildren<Renderer>().Any(x=>x.sharedMaterials.Any(m=>!m||!m.shader||!m.shader.isSupported)))failures.Add("Unsupported or missing dressing material");
            if(r && r.GetComponentsInChildren<MeshFilter>().Any(x=>x.sharedMesh&&!AssetDatabase.Contains(x.sharedMesh)))failures.Add("Unsaved dressing mesh");
            var tableCounts=new List<string>();var seats=ChairRoots();int supportedChairs=0;
            foreach(var table in all.Where(t=>t.name=="岸边小桌" || t.name=="新增四人茶桌"))
            {
                Vector3 center=BoundsOf(table.gameObject).center;
                int count=seats.Count(t=>Flat(BoundsOf(t.gameObject).center,center)<2.8f);
                tableCounts.Add(PathOf(table)+" chairs="+count);
                if(count<4)failures.Add("Fewer than four real chairs at "+PathOf(table));
            }
            if(r)foreach(var chair in seats.Where(t=>t.IsChildOf(r)))
            {
                string rejection;
                if(!SafeChair(chair.gameObject,out rejection))failures.Add("Unsafe new chair "+PathOf(chair)+": "+rejection);
                else supportedChairs++;
            }
            File.WriteAllText(Review+"/living-checks.txt",(failures.Count==0?"PASS":"FAIL")+"\nutc="+DateTime.UtcNow.ToString("O")+
                "\nverifiedBaselineTransforms="+preserved+"/"+snapshot.items.Length+"\nexplicitHarborFurnitureMoves=2\nsafeNewChairs="+supportedChairs+
                "\n"+string.Join("\n",tableCounts)+"\n"+string.Join("\n",failures));
            if(failures.Count>0)throw new InvalidOperationException(string.Join("\n",failures));
        }

        static Transform[] ChairRoots()=>All().Where(t=>t.gameObject.activeInHierarchy && !t.name.Contains("LOD") &&
            (t.name.Contains("木椅") || t.name.StartsWith("Chair",StringComparison.Ordinal))).ToArray();

        static bool IsHarborFurniture(Item item)=>item.id==HarborTableId || item.id==HarborChairId;
        static Vector3 ExpectedPosition(Item item)
        {
            Vector3 fitted;if(WatersideHarborGround.TryExpected(item.id,out fitted))return fitted;
            if(!IsHarborFurniture(item))return item.position;
            if(!terrain)throw new InvalidOperationException("Terrain is required to verify the two harbor furniture offsets.");
            Vector3 target=item.position+HarborShift;
            target.y=item.position.y+Height(target)-Height(item.position);
            return target;
        }

        static void MoveHarborFurniture()
        {
            var snapshot=JsonUtility.FromJson<Snapshot>(File.ReadAllText(Review+"/user-edits-baseline.json"));
            var selected=snapshot.items.Where(IsHarborFurniture).ToArray();
            if(selected.Length!=2)throw new InvalidDataException("The exact generated harbor table and chair baseline is required.");
            var moves=new List<(Transform transform,Item expected)>();
            foreach(var item in selected)
            {
                GlobalObjectId id;GlobalObjectId.TryParse(item.id,out id);
                var t=GlobalObjectId.GlobalObjectIdentifierToObjectSlow(id) as Transform;
                if(!t || (t.position-item.position).sqrMagnitude>.000001f || Quaternion.Angle(t.rotation,item.rotation)>.01f ||
                    (t.localScale-item.scale).sqrMagnitude>.000001f || t.gameObject.activeSelf!=item.active)
                    throw new InvalidOperationException("Preserve the newly edited harbor furniture: "+item.path);
                moves.Add((t,new Item{id=item.id,path=item.path,position=ExpectedPosition(item),rotation=item.rotation,scale=item.scale,active=item.active}));
            }
            File.WriteAllText(Review+"/harbor-furniture-shift.json",JsonUtility.ToJson(new Snapshot{utc=DateTime.UtcNow.ToString("O"),
                playing=false,dirty=true,items=moves.Select(m=>m.expected).ToArray()},true));
            foreach(var move in moves)move.transform.position=move.expected.position;
            notes.Add("Moved only the generated harbor table/chair inland by XZ=(-1.9660721,+1.385714), retaining each ground offset.");
        }

        static bool SafeChair(GameObject chair,out string reason)
        {
            Bounds b=BoundsOf(chair);float low=float.PositiveInfinity,high=float.NegativeInfinity;
            // Check the complete visible footprint: a dry center alone can leave two feet in the sea.
            foreach(float x in new[]{b.min.x,b.center.x,b.max.x})foreach(float z in new[]{b.min.z,b.center.z,b.max.z})
            {
                if(WatersideLayout.IsWater(x,z,.04f)){reason="footprint enters water";return false;}
                if(WatersideLayout.WalkablePathEdgeDistance(x,z)<.04f){reason="footprint blocks the walking route";return false;}
                float y=Height(P(x,z));low=Mathf.Min(low,y);high=Mathf.Max(high,y);
            }
            if(high-low>.26f || b.min.y-low>.18f || high-b.min.y>.18f)
            {reason="uneven support (ground range "+low.ToString("F2")+".."+high.ToString("F2")+")";return false;}
            // World AABBs overlap around rotated furniture even when the actual shapes are separated.
            Bounds local=MeshProjection(chair,chair.transform.rotation,false);
            var probeObject=new GameObject("Chair placement collision probe"){hideFlags=HideFlags.HideAndDontSave};
            probeObject.transform.SetPositionAndRotation(chair.transform.rotation*local.center,chair.transform.rotation);
            var probe=probeObject.AddComponent<BoxCollider>();probe.size=local.size;probe.enabled=false;
            try
            {
                foreach(var other in Object.FindObjectsOfType<Collider>())
                {
                    if(!other.enabled || other.isTrigger || other is TerrainCollider || other is CharacterController ||
                        other.gameObject.scene!=chair.scene || other.transform.IsChildOf(chair.transform) || !b.Intersects(other.bounds))continue;
                    Vector3 direction;float distance;
                    if(Physics.ComputePenetration(probe,probeObject.transform.position,probeObject.transform.rotation,
                        other,other.transform.position,other.transform.rotation,out direction,out distance) && distance>.01f)
                    {reason="penetrates "+PathOf(other.transform)+" by "+distance.ToString("F3")+"m";return false;}
                }
            }
            finally{Object.DestroyImmediate(probeObject);}
            reason=null;return true;
        }

        static Bounds MeshProjection(GameObject go,Quaternion axes,bool excludeTableware)
        {
            bool first=true;Bounds result=new Bounds();Quaternion inverse=Quaternion.Inverse(axes);
            foreach(var filter in go.GetComponentsInChildren<MeshFilter>())
            {
                if(!filter.sharedMesh || (excludeTableware && PathOf(filter.transform).Contains("桌上陶器")))continue;
                Bounds mesh=filter.sharedMesh.bounds;
                foreach(float x in new[]{-1f,1f})foreach(float y in new[]{-1f,1f})foreach(float z in new[]{-1f,1f})
                {
                    Vector3 p=inverse*filter.transform.TransformPoint(mesh.center+Vector3.Scale(mesh.extents,new Vector3(x,y,z)));
                    if(first){result=new Bounds(p,Vector3.zero);first=false;}else result.Encapsulate(p);
                }
            }
            if(first)throw new InvalidOperationException("No furniture mesh to project: "+go.name);
            return result;
        }
        static GameObject Add(string asset,string name,Vector3 point,float height,float yaw,Transform parent)
            =>WatersidePropsBuilder.CreateAddition(asset,name,point,height,yaw,parent);
        static Vector3 P(float x,float z)=>new Vector3(x,0,z);
        static float Height(Vector3 p)=>terrain.SampleHeight(p)+terrain.transform.position.y;
        static float Flat(Vector3 a,Vector3 b)=>Vector2.Distance(new Vector2(a.x,a.z),new Vector2(b.x,b.z));
        static Transform Group(string name,Transform parent){var go=new GameObject(name);go.transform.SetParent(parent,false);return go.transform;}
        static void Fit(GameObject go,Vector3 point,float y){var b=BoundsOf(go);go.transform.position+=new Vector3(point.x-b.center.x,y-b.min.y,point.z-b.center.z);}
        static Bounds BoundsOf(GameObject go){var rs=go.GetComponentsInChildren<Renderer>();if(rs.Length==0)return new Bounds(go.transform.position,Vector3.zero);
            Bounds b=rs[0].bounds;foreach(var r in rs.Skip(1))b.Encapsulate(r.bounds);return b;}
        static Transform[] All()=>SceneManager.GetActiveScene().GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Transform>(true)).ToArray();
        static string PathOf(Transform t){string path=t.name;for(var p=t.parent;p;p=p.parent)path=p.name+"/"+path;return path;}
    }
}
