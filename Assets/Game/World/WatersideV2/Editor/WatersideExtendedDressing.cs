using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;

namespace WeatherHouse.WatersideV2.Editor
{
    public static class WatersideExtendedDressing
    {
        const string RootName="10 院落外延 · 石墙花境与工作角";
        static Terrain terrain;static Transform root;static readonly List<string> report=new List<string>();
        static int walls,flowers,props,moved;
        public static void Apply()
        {
            WatersideSceneBuilder.RequireSaved();terrain=Object.FindObjectOfType<Terrain>();
            if(SceneManager.GetActiveScene().path!=WatersideSceneBuilder.ScenePath)throw new InvalidOperationException("Open V2.");
            if(All().Any(t=>t.name==RootName))throw new InvalidOperationException("Refine the existing extension instead of duplicating it.");
            root=new GameObject(RootName).transform;report.Clear();walls=flowers=props=moved=0;
            WatersidePropsBuilder.PrepareAdditions(terrain);
            MoveCapeTea();
            RelocateWetStorage();
            // The wall runs flank spaces and openings, never form a complete barricade.
            WallRun("归家桥头",P(-31,-24),P(-27,-24));
            WallRun("老树东岸",P(7,-4),P(7,-9));
            WallRun("踏石近岸入口",P(7.8f,-16),P(10.8f,-16));
            WallRun("踏石远岸入口",P(24,-10),P(27,-10));
            WallRun("西潭林后院",P(-27.4f,32),P(-27.4f,36));
            WallRun("风铃北庭",P(-3.3f,37.7f),P(1,37.7f));
            WallRun("海岬后庭",P(50,3.8f),P(54,3.8f));
            WallRun("月湾风铃背景",P(-15.4f,-59.4f),P(-15.4f,-64.1f));
            var patches=new[]{P(-37.5f,-34),P(-30.7f,-24),P(-8,-9),P(5,-7),P(8,-15),P(24,-10),
                P(-25,28),P(-24.8f,36.5f),P(2.5f,37),P(4,29),P(49.8f,.4f),P(59.3f,2.8f),
                P(48.8f,-41),P(50,-47.7f),P(-13.5f,-58.2f),P(-5,-64)};
            foreach(var patch in patches)FlowerBed(patch);
            foreach(var pair in new[]{new[]{P(-25,30),P(-23,35.8f)},new[]{P(2.8f,30),P(2,36.4f)},
                new[]{P(-12.2f,-64),P(-6,-58)},new[]{P(50,-5),P(59,1)}})Lamps(pair);
            foreach(var bench in All().Where(t=>t.name=="成组临水倒木凳").ToArray())
            {
                var b=Bounds(bench);Vector3 desired=b.center+bench.right*1.6f;
                var group=new GameObject("双水罐与茶具 · 休憩补给").transform;group.SetParent(root);
                for(int i=0;i<2;i++)Small("Camp Props/Pot","成对陶水罐",desired+Vector3.forward*i*.55f,.4f+i*.05f,group);
            }
            // Repeated small useful objects sit on existing crate tops, not scattered in walking space.
            foreach(var box in All().Where(t=>t.name=="成组木箱").Where((t,i)=>i%2==0).ToArray())
            {
                var b=Bounds(box);var group=new GameObject("箱上两只贮藏瓶").transform;group.SetParent(root);
                for(int i=0;i<2;i++)
                {
                    Vector3 p=b.center+new Vector3(i==0?-.12f:.12f,0,0);p.y=b.max.y-.008f;
                    var go=WatersidePropsBuilder.CreateAddition("Camp Props/Bottle_"+(i+1),"储物玻璃瓶",p,.23f,25+i*40,group,false);
                    foreach(var c in go.GetComponentsInChildren<Collider>())Object.DestroyImmediate(c);props++;
                }
            }
            Check();EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());AssetDatabase.SaveAssets();EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
            File.WriteAllLines("WatersideV3Review/extended-dressing.txt",new[]{"PASS saved extensions", "wallsAndPillars="+walls,"flowerClumps="+flowers,"smallProps="+props,"relocatedWetProps="+moved}.Concat(report));
        }
        static void MoveCapeTea()
        {
            var cape=All().Single(t=>t.name=="18 海岬院 · 四人茶席与避雨储物");
            var table=cape.Cast<Transform>().Single(t=>t.name=="新增四人茶桌");var old=Bounds(table).center;
            Vector3 destination=P(51,-7.2f);Quaternion turn=Quaternion.Euler(0,90,0);
            float oldTableBottom=Bounds(table).min.y;
            var set=cape.Cast<Transform>().Where(t=>t==table||t.name=="四人桌配套木椅"||t.name=="配套餐盘"||t.name=="配套餐杯").ToArray();
            foreach(var t in set)
            {
                Vector3 offset=t.position-new Vector3(old.x,0,old.z);
                t.position=destination+turn*offset;t.rotation=turn*t.rotation;
            }
            float lift=Ground(destination)-.015f-oldTableBottom;
            foreach(var t in set)
            {
                if(t.name=="四人桌配套木椅")Fit(t,Ground(Bounds(t).center)-.015f);
                else t.position+=Vector3.up*lift;
            }
            var rail=cape.Cast<Transform>().Single(t=>t.name=="避雨院南侧栏");rail.position+=Vector3.back*3;
            foreach(Transform segment in rail)Fit(segment,Ground(Bounds(segment).center)-.04f);
            report.Add("Cape tea set moved to (51,-7.2), leaving the photographed through-route clear.");
        }
        static void RelocateWetStorage()
        {
            foreach(var group in All().Where(t=>t.name.StartsWith("成组桶箱补给")&&t.GetComponentsInChildren<Renderer>().Any()).ToArray())
            {
                Bounds b=Bounds(group);if(Dry(b,.08f))continue;
                var before=group.position;
                Vector3 desired=new Vector3(48.6f,0,Mathf.Clamp(b.center.z,-47,-33));
                if(!FindSite(desired,new Vector2(Mathf.Min(1.8f,b.extents.x),Mathf.Min(1.6f,b.extents.z)),group,out Vector3 p,7))
                    throw new InvalidOperationException("No dry storage group site: "+group.name);
                group.position+=new Vector3(p.x-b.center.x,0,p.z-b.center.z);
                foreach(Transform item in group)
                {
                    if(item.name=="叠放小箱")continue;Fit(item,Ground(Bounds(item).center)-.015f);
                }
                foreach(Transform top in group.Cast<Transform>().Where(t=>t.name=="叠放小箱"))
                {
                    var bottom=group.Cast<Transform>().Where(t=>t.name=="成组木箱").OrderBy(t=>Flat(Bounds(t).center,Bounds(top).center)).First();Fit(top,Bounds(bottom).max.y-.01f);
                }
                report.Add("Moved own storage group "+group.name+" by "+(group.position-before));moved+=group.childCount;
            }
            var harbor=All().Single(t=>t.name=="6 港湾短码头");
            foreach(Transform item in harbor.Cast<Transform>().Where(t=>new[]{"旧木桶","补给木箱","取水木桶","杂物箱"}.Contains(t.name)))
            {
                Bounds b=Bounds(item);
                if(Dry(b,.08f)&&Mathf.Abs(b.min.y-Ground(b.center))<.25f)continue;
                if(!FindSite(new Vector3(49.8f,0,-40.6f),new Vector2(Mathf.Min(.85f,b.extents.x),Mathf.Min(.85f,b.extents.z)),item,out Vector3 p,8))throw new InvalidOperationException("No dry harbor prop site.");
                Vector3 position=item.position+new Vector3(p.x-b.center.x,Ground(p)-.015f-b.min.y,p.z-b.center.z);
                report.Add("Relocated existing harbor "+item.name+" "+item.position+" -> "+position);
                WatersideHarborGround.MoveStorage(item,position);moved++;
            }
        }
        static void WallRun(string name,Vector3 start,Vector3 end)
        {
            var group=new GameObject(name+" · 断墙与花境边界").transform;group.SetParent(root);
            float yaw=Mathf.Atan2(end.x-start.x,end.z-start.z)*Mathf.Rad2Deg+90;int count=Mathf.CeilToInt(Flat(start,end)/1.8f);
            for(int i=0;i<count;i++)
            {
                Vector3 desired=Vector3.Lerp(start,end,(i+.5f)/count);
                if(!FindSite(desired,new Vector2(.9f,.45f),null,out Vector3 p,2.4f))continue;
                Ruin("SM_wall_stoneblocks_ruins_03",name+"低墙",p,new Vector3(1.6f,.75f+(i%2)*.1f,.42f),yaw,group);walls++;
            }
            foreach(var desired in new[]{start,end})
            {
                if(!FindSite(desired,new Vector2(.4f,.4f),null,out Vector3 p,2))continue;
                Ruin("SM_square_pillar_03",name+"边界残柱",p,new Vector3(.48f,1.2f,.48f),yaw,group);walls++;
            }
        }
        static void Ruin(string asset,string name,Vector3 p,Vector3 size,float yaw,Transform parent)
        {
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Environment/DDriveLibrary/AncientRuins/PREFABS/Ruins/"+asset+".prefab");
            var go=(GameObject)PrefabUtility.InstantiatePrefab(prefab,parent);go.name=name;
            Bounds b=Bounds(go.transform);go.transform.localScale=Vector3.Scale(go.transform.localScale,new Vector3(size.x/b.size.x,size.y/b.size.y,size.z/b.size.z));
            go.transform.rotation=Quaternion.Euler(0,yaw,0);b=Bounds(go.transform);
            go.transform.position+=new Vector3(p.x-b.center.x,Ground(p)-.06f-b.min.y,p.z-b.center.z);
            if(go.GetComponentsInChildren<Collider>().Length==0){var filter=go.GetComponent<MeshFilter>();if(filter)go.AddComponent<MeshCollider>().sharedMesh=filter.sharedMesh;}
        }
        static void FlowerBed(Vector3 p)
        {
            var group=new GameObject("院缘双层花境").transform;group.SetParent(root);
            for(int i=0;i<7;i++)
            {
                Vector3 desired=p+new Vector3((i%4)*.42f,0,(i/4)*.4f);
                if(!FindSite(desired,new Vector2(.12f,.12f),null,out Vector3 site,1.2f))continue;
                var source=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/World/WatersideV2/Generated/Nature/Plants/Prefabs/R06_Daisy_v"+(1+i%3)+".prefab");
                var go=(GameObject)PrefabUtility.InstantiatePrefab(source,group);go.name="成片院花";var b=Bounds(go.transform);
                go.transform.localScale*= (.28f+.04f*(i%3))/b.size.y;go.transform.rotation=Quaternion.Euler(0,i*71,0);b=Bounds(go.transform);
                go.transform.position+=new Vector3(site.x-b.center.x,Ground(site)-.02f-b.min.y,site.z-b.center.z);flowers++;
            }
        }
        static void Small(string source,string name,Vector3 desired,float height,Transform parent)
        {if(FindSite(desired,new Vector2(.26f,.26f),null,out Vector3 p,2)){WatersidePropsBuilder.CreateAddition(source,name,p,height,0,parent);props++;}}
        static void Lamps(Vector3[] sites)
        {
            var source=All().First(t=>t.name=="圆台低位暖石灯 · 原灯头与风化石");
            foreach(var desired in sites)
            {
                if(!FindSite(desired,new Vector2(.4f,.4f),null,out Vector3 p,2))continue;
                var go=Object.Instantiate(source.gameObject,root);go.name="外院成对石灯";var b=Bounds(go.transform);
                go.transform.position+=new Vector3(p.x-b.center.x,Ground(p)-.02f-b.min.y,p.z-b.center.z);props++;
            }
        }
        static bool FindSite(Vector3 desired,Vector2 half,Transform ignore,out Vector3 p,float radius)
        {
            Physics.SyncTransforms();
            for(float ring=0;ring<=radius;ring+=.4f)for(int i=0;i<(ring==0?1:16);i++)
            {
                float angle=i*Mathf.PI/8;p=desired+new Vector3(Mathf.Cos(angle)*ring,0,Mathf.Sin(angle)*ring);p.y=Ground(p);
                var b=new Bounds(p+Vector3.up*.5f,new Vector3(half.x*2,1,half.y*2));
                if(!Dry(b,.12f))continue;float min=1000,max=-1000;bool legal=true;
                foreach(float x in new[]{b.min.x,b.max.x})foreach(float z in new[]{b.min.z,b.max.z})
                {if(WatersideLayout.WalkablePathEdgeDistance(x,z)<.2f)legal=false;float y=Ground(P(x,z));min=Mathf.Min(min,y);max=Mathf.Max(max,y);}
                if(!legal||max-min>.35f)continue;
                if(Physics.OverlapBox(b.center,b.extents,Quaternion.identity,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore)
                    .Any(c=>!(c is TerrainCollider)&&!(c is CharacterController)&&(!ignore||!c.transform.IsChildOf(ignore))))continue;
                return true;
            }
            p=default;return false;
        }
        static bool Dry(Bounds b,float margin)
        {foreach(float x in new[]{b.min.x,b.center.x,b.max.x})foreach(float z in new[]{b.min.z,b.center.z,b.max.z})if(WatersideLayout.IsWater(x,z,margin))return false;return true;}
        public static void Check()
        {
            var owned=All().First(t=>t.name==RootName);
            if(owned.GetComponentsInChildren<Renderer>().Any(r=>r.sharedMaterials.Any(m=>!m||!m.shader||!m.shader.isSupported)))throw new InvalidOperationException("Missing extension material.");
            WatersideLivingSpaces.Check();
        }
        static Vector3 P(float x,float z)=>new Vector3(x,0,z);
        static float Ground(Vector3 p)=>terrain.SampleHeight(p)+terrain.transform.position.y;
        static float Flat(Vector3 a,Vector3 b)=>Vector2.Distance(new Vector2(a.x,a.z),new Vector2(b.x,b.z));
        static void Fit(Transform t,float y){t.position+=Vector3.up*(y-Bounds(t).min.y);}
        static Bounds Bounds(Transform t){var rs=t.GetComponentsInChildren<Renderer>();Bounds b=rs[0].bounds;foreach(var r in rs.Skip(1))b.Encapsulate(r.bounds);return b;}
        static Transform[] All()=>SceneManager.GetActiveScene().GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Transform>(true)).ToArray();
    }
}
