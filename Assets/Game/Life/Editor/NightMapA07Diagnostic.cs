using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using WeatherHouse.WatersideV2;

namespace WeatherHouse.NightReview20261027
{
    // A07-only read-only rejection audit. It calls the current candidate's own point/protection predicates.
    [InitializeOnLoad]
    public static class NightMapA07Diagnostic
    {
        static double next;
        [Serializable] public sealed class Request { public string expectedSceneSha256; }
        [Serializable] public sealed class Count { public string reason; public int count; }
        [Serializable] public sealed class Result
        {
            public string status, utc, error, beforeHash, afterHash, firstSource;
            public bool dirtyBefore, dirtyAfter;
            public int sourcePool, accepted;
            public List<Count> rejections = new List<Count>();
            public List<string> blockingColliders = new List<string>();
        }
        static NightMapA07Diagnostic() { EditorApplication.update += Poll; }
        static void Poll()
        {
            if(EditorApplication.timeSinceStartup<next||EditorApplication.isPlayingOrWillChangePlaymode||EditorApplication.isCompiling||EditorApplication.isUpdating)return;
            next=EditorApplication.timeSinceStartup+1;string file=Path.Combine(NightMapSnapshot.Staging,"a07-diagnostic-request.json");if(!File.Exists(file))return;
            var request=JsonUtility.FromJson<Request>(File.ReadAllText(file));File.Delete(file);
            try{Run(request);}catch(Exception e){File.WriteAllText(Path.Combine(NightMapSnapshot.Staging,"a07-diagnostic-error.txt"),e.ToString());}
        }
        public static string Run(Request request)
        {
            Scene scene=SceneManager.GetActiveScene();if(scene.path!=NightMapSnapshot.ScenePath||EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Stable existing Edit scene required.");
            string disk=HashFile(scene.path);if(request==null||!disk.Equals(request.expectedSceneSha256,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Scene SHA mismatch.");
            Type type=typeof(NightMapLandscapeCandidate);const BindingFlags flags=BindingFlags.NonPublic|BindingFlags.Static;
            object context=type.GetMethod("ContextFor",flags).Invoke(null,new object[]{false});
            Transform[] all=(Transform[])context.GetType().GetField("all").GetValue(context);Terrain terrain=(Terrain)context.GetType().GetField("terrain").GetValue(context);
            MethodInfo protection=type.GetMethod("Protected",flags),bounds=type.GetMethod("BoundsOf",flags),safe=type.GetMethod("SafePlantPoint",flags);
            var result=new Result{utc=DateTime.UtcNow.ToString("O"),dirtyBefore=scene.isDirty,beforeHash=StateHash(all)};
            var counts=new Dictionary<string,int>();Action<string> reject=reason=>{counts[reason]=counts.TryGetValue(reason,out int n)?n+1:1;};
            Bounds GetBounds(Transform t)=>(Bounds)bounds.Invoke(null,new object[]{t});
            bool Protected(Transform t,Bounds b)=>(bool)protection.Invoke(null,new object[]{context,t,b});
            string Id(Transform t)=>GlobalObjectId.GetGlobalObjectIdSlow(t).ToString();
            var plan=JsonUtility.FromJson<NightMapLandscapeCandidate.Plan>(File.ReadAllText(Path.Combine(NightMapSnapshot.Staging,"landscape-plan.json")));
            var used=new HashSet<string>(plan.moves.Where(m=>m.node=="A01").Select(m=>m.id));var reserved=plan.moves.Where(m=>m.node=="A01").Select(m=>m.afterBounds).ToList();
            Vector3 center=new Vector3(-57.8f,0,-3);Vector2 radii=new Vector2(3.2f,2);
            Transform[] pool=all.Where(t=>t.gameObject.activeInHierarchy&&PrefabUtility.GetNearestPrefabInstanceRoot(t.gameObject)==t.gameObject&&
                new[]{"R07_BlueFlower","R12_LilacBell","R13_LeafyHerb"}.Any(f=>Path.GetFileName(PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(t.gameObject)??"").StartsWith(f+"_",StringComparison.Ordinal))&&
                !used.Contains(Id(t))&&t.GetComponentsInChildren<MonoBehaviour>(true).Length==0&&t.GetComponentsInChildren<Collider>(true).Length==0&&!Protected(t,GetBounds(t)))
                .OrderBy(t=>Vector2.Distance(new Vector2(GetBounds(t).center.x,GetBounds(t).center.z),new Vector2(center.x,center.z))).ThenBy(Id).ToArray();
            result.sourcePool=pool.Length;result.firstSource=pool.Length>0?pool[0].name:"NO_ELIGIBLE_SOURCE";
            try
            {
                for(int trial=0;trial<240&&result.accepted<12&&result.accepted<pool.Length;trial++)
                {
                    float angle=trial*2.39996323f,r=Mathf.Sqrt(((trial*37)%101+.5f)/101f);
                    Vector3 point=center+new Vector3(Mathf.Cos(angle)*radii.x*r,0,Mathf.Sin(angle)*radii.y*r);
                    if(!RuralMapConfiguration.Contains(point,RuralMapConfiguration.Regions[0])){reject("outside_forest");continue;}
                    Transform source=pool[result.accepted];Bounds old=GetBounds(source);object[] args={context,point,old,reserved,Vector3.zero};
                    bool ok=(bool)safe.Invoke(null,args);Vector3 delta=(Vector3)args[4];
                    if(!ok)
                    {
                        bool known=false;Action<string> mark=reason=>{known=true;reject(reason);};
                        if(WatersideLayout.IsWater(point.x,point.z,.8f))mark("water_margin");
                        if(WatersideLayout.WalkablePathEdgeDistance(point.x,point.z)<1.25f)mark("existing_walkable_route_buffer");
                        if(RuralMapConfiguration.NewCorridors.Any(line=>RuralMapConfiguration.LineDistance(point,line)<2.8f))mark("new_corridor_buffer_2_8m");
                        Vector3 o=terrain.transform.position,size=terrain.terrainData.size;
                        if(Vector3.Angle(terrain.terrainData.GetInterpolatedNormal((point.x-o.x)/size.x,(point.z-o.z)/size.z),Vector3.up)>28)mark("terrain_slope_over_28");
                        point.y=terrain.SampleHeight(point)+o.y;var hits=Physics.OverlapSphere(point+Vector3.up*.35f,.3f,~0,QueryTriggerInteraction.Ignore).Where(h=>!(h is TerrainCollider)).ToArray();
                        if(hits.Length>0){mark("physical_obstacle");foreach(var hit in hits)if(!result.blockingColliders.Contains(hit.name))result.blockingColliders.Add(hit.name);}
                        if(!known)reject("spacing_or_terrain_extent");continue;
                    }
                    Bounds nextBounds=old;nextBounds.center+=delta;
                    if(plan.nodes.Any(n=>n.buildingBounds.Intersects(nextBounds))){reject("existing_building_bounds");continue;}
                    if(Protected(source,nextBounds)){reject("protected_core_destination");continue;}
                    if(Vector3.Distance(source.position,source.position+delta)<.5f){reject("move_too_small");continue;}
                    reserved.Add(nextBounds);result.accepted++;
                }
            }
            catch(Exception e){result.error=e.ToString();}
            finally
            {
                result.afterHash=StateHash(all);result.dirtyAfter=scene.isDirty;
                result.status=result.beforeHash==result.afterHash&&result.dirtyBefore==result.dirtyAfter&&HashFile(scene.path)==disk?"PASS_READ_ONLY_A07_REJECTION_AUDIT":"FAIL_STATE_GUARD";
                result.rejections=counts.Select(k=>new Count{reason=k.Key,count=k.Value}).ToList();
                File.WriteAllText(Path.Combine(NightMapSnapshot.Staging,"a07-diagnostic.json"),JsonUtility.ToJson(result,true));
            }
            return Path.Combine(NightMapSnapshot.Staging,"a07-diagnostic.json");
        }
        static string StateHash(Transform[] all){using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(string.Join("\n",all.Select(t=>t.GetInstanceID()+"|"+EditorJsonUtility.ToJson(t)))))).Replace("-","");}
        static string HashFile(string p){using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(p))).Replace("-","");}
    }
}
