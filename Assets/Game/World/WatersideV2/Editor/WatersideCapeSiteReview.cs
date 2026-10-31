using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;

namespace WeatherHouse.WatersideV2.Editor
{
    public static class WatersideCapeSiteReview
    {
        struct Site { public Vector3 center;public float yaw,span,score; }
        public static string Find()
        {
            var scene=SceneManager.GetActiveScene();
            if(scene.path!=WatersideSceneBuilder.ScenePath || Application.isPlaying)
                throw new InvalidOperationException("Inspect the current V2 scene in edit mode.");
            var terrain=Object.FindObjectsOfType<Terrain>().Single(t=>t.gameObject.scene==scene);
            var region=new Bounds(new Vector3(53,8,-3),new Vector3(17,40,19));
            var obstacles=Object.FindObjectsOfType<Collider>().Where(c=>c.enabled && !c.isTrigger &&
                c.gameObject.scene==scene && !(c is TerrainCollider) && !(c is CharacterController) && region.Intersects(c.bounds)).ToArray();
            var sites=new List<Site>();var rejected=new Dictionary<string,int>();int tested=0;
            // Table_1 is about 1.5 x .8 m; four seats use the wider .73 m clearance already available in CompleteTable.
            var seats=new[]{new Vector3(-.4125f,0,-1.13f),new Vector3(.4125f,0,-1.13f),
                new Vector3(-.4125f,0,1.13f),new Vector3(.4125f,0,1.13f)};
            for(int iz=0;iz<=35;iz++)for(int ix=0;ix<=30;ix++)foreach(float yaw in new[]{0f,45f,90f})
            {
                tested++;Vector3 center=new Vector3(47+ix*.4f,0,-10+iz*.4f);Quaternion rotation=Quaternion.Euler(0,yaw,0);
                float low=float.PositiveInfinity,high=float.NegativeInfinity;string reason=null;
                if(!Floor(terrain,center,rotation,new Vector2(.75f,.4f),ref low,ref high,out reason)) {Count(rejected,reason);continue;}
                center.y=Ground(terrain,center);
                if(Occupied(center,rotation,new Vector2(.75f,.4f),.85f,obstacles,out reason)){Count(rejected,reason);continue;}
                bool legal=true;
                foreach(var local in seats)
                {
                    Vector3 point=center+rotation*local;point.y=Ground(terrain,point);
                    if(!Floor(terrain,point,rotation,new Vector2(.25f,.25f),ref low,ref high,out reason) ||
                        Occupied(point,rotation,new Vector2(.25f,.25f),1.05f,obstacles,out reason)) {legal=false;break;}
                }
                if(!legal){Count(rejected,reason);continue;}
                if(high-low>.22f){Count(rejected,"whole seating floor span > .22m");continue;}
                sites.Add(new Site{center=center,yaw=yaw,span=high-low,score=Vector2.Distance(new Vector2(center.x,center.z),new Vector2(53,-4))});
            }
            var text=new StringBuilder("Read-only cape table-site probe\nUTC="+DateTime.UtcNow.ToString("o")+
                "\nTested="+tested+"\nAccepted="+sites.Count+"\nExistingColliders="+obstacles.Length+
                "\nGrid=x47..59,z-10..4,step.4,yaw0/45/90\nAll table/seat foot samples: dry; path-edge>=.15m; whole floor span<=.22m.\n"+
                "Seats in table-local XZ: (+/-.4125,+/-1.13), each foot square +/-.25m.\nCollision: oriented furniture footprint versus current collider bounds, with vertical separation; conservative for concave obstacles.\n");
            text.AppendLine("rank\tx\ty\tz\tyaw\tfloorSpan\tdistanceTo53,-4");
            int rank=0;
            foreach(var site in sites.OrderBy(s=>s.score).ThenBy(s=>s.span).Take(30))
                text.AppendLine(string.Format(System.Globalization.CultureInfo.InvariantCulture,"{0}\t{1:F2}\t{2:F3}\t{3:F2}\t{4:F0}\t{5:F3}\t{6:F2}",++rank,site.center.x,site.center.y,site.center.z,site.yaw,site.span,site.score));
            if(sites.Count<10)text.AppendLine("Fewer than 10 legal candidates; checks were not relaxed.");
            foreach(var entry in rejected.OrderByDescending(e=>e.Value))text.AppendLine("Rejected "+entry.Value+": "+entry.Key);
            Directory.CreateDirectory("WatersideV3Review");File.WriteAllText("WatersideV3Review/cape-sites.txt",text.ToString());
            return text.ToString();
        }

        static bool Floor(Terrain terrain,Vector3 center,Quaternion yaw,Vector2 half,ref float low,ref float high,out string reason)
        {
            float localLow=float.PositiveInfinity,localHigh=float.NegativeInfinity;
            foreach(float x in new[]{-half.x,0,half.x})foreach(float z in new[]{-half.y,0,half.y})
            {
                Vector3 p=center+yaw*new Vector3(x,0,z);
                if(WatersideLayout.IsWater(p.x,p.z,.04f)){reason="water";return false;}
                if(WatersideLayout.WalkablePathEdgeDistance(p.x,p.z)<.15f){reason="walking route margin";return false;}
                float y=Ground(terrain,p);localLow=Mathf.Min(localLow,y);localHigh=Mathf.Max(localHigh,y);
            }
            low=Mathf.Min(low,localLow);high=Mathf.Max(high,localHigh);
            if(localHigh-localLow>.22f){reason="one furniture footprint span > .22m";return false;}
            reason=null;return true;
        }

        static bool Occupied(Vector3 bottom,Quaternion yaw,Vector2 half,float height,Collider[] obstacles,out string reason)
        {
            Vector3 xAxis=yaw*Vector3.right,zAxis=yaw*Vector3.forward;
            foreach(var collider in obstacles)
            {
                Bounds b=collider.bounds;if(b.max.y<=bottom.y+.02f || b.min.y>=bottom.y+height)continue;
                Vector3 d=b.center-bottom;
                if(Mathf.Abs(d.x)>b.extents.x+Mathf.Abs(xAxis.x)*half.x+Mathf.Abs(zAxis.x)*half.y ||
                    Mathf.Abs(d.z)>b.extents.z+Mathf.Abs(xAxis.z)*half.x+Mathf.Abs(zAxis.z)*half.y ||
                    Mathf.Abs(Vector3.Dot(d,xAxis))>half.x+Mathf.Abs(xAxis.x)*b.extents.x+Mathf.Abs(xAxis.z)*b.extents.z ||
                    Mathf.Abs(Vector3.Dot(d,zAxis))>half.y+Mathf.Abs(zAxis.x)*b.extents.x+Mathf.Abs(zAxis.z)*b.extents.z)continue;
                reason="collider "+collider.name;return true;
            }
            reason=null;return false;
        }
        static float Ground(Terrain terrain,Vector3 p)=>terrain.SampleHeight(p)+terrain.transform.position.y;
        static void Count(Dictionary<string,int> counts,string reason){if(!counts.ContainsKey(reason))counts[reason]=0;counts[reason]++;}
    }
}
