using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using WeatherHousePrototype;
using WeatherHouse.Hillside;
using WeatherHouse.WildlifeJourney;

namespace WeatherHouse.Life.Editor
{
    public static class LifeSpatialAudit
    {
        [Serializable] public sealed class Site
        {
            public string name; public Vector3 position; public bool dry,projected,clear;
            public float slope,depth; public string[] colliders;
        }
        [Serializable] public sealed class Route
        {
            public string from,to; public bool directWalk; public float horizontalLength;
        }
        [Serializable] public sealed class Nearby
        {
            public string animalId; public Vector3 position; public float ferryDistance,tideDistance;
        }
        [Serializable] public sealed class Report
        {
            public string utc,scene,boundary; public bool play,dirty; public Site[] sites; public Route[] routes; public Nearby[] nearby;
        }
        [MenuItem("Weather House/Life/Audit spatial candidates read only")]
        public static Report Run()
        {
            Scene scene=SceneManager.GetActiveScene();
            if(scene.name!="WeatherHouse_WatersideV2")throw new InvalidOperationException("Spatial audit only reads the already-open formal Waterside scene.");
            JourneyNavigation nav=UnityEngine.Object.FindObjectOfType<JourneyNavigation>();
            if(!nav)throw new InvalidOperationException("Existing JourneyNavigation is required.");
            // A conservative dry-land capsule; arrival with each final animal envelope remains a separate Play check.
            var profile=new JourneyProfile{bodyRadius=.42f,canSwim=false,wadeDepth=0,walkSpeed=1};
            Site[] sites={
                new Site{name="渡口北缘候选A",position=new Vector3(71,0,-55.5f)},
                new Site{name="渡口北缘候选B",position=new Vector3(73.5f,0,-55.5f)},
                new Site{name="潮池干岸候选A",position=new Vector3(72,0,-49)},
                new Site{name="潮池干岸候选B",position=new Vector3(74,0,-49.5f)},
                new Site{name="既有海岸干路",position=new Vector3(71,0,-61.5f)},
                new Site{name="芦苇湾候选",position=new Vector3(-28.8f,0,-64.8f)}
            };
            foreach(Site site in sites)
            {
                JourneySurface sample=nav.SampleSurface(site.position);
                site.position.y=sample.groundY;site.depth=sample.hasWater?Mathf.Max(0,sample.waterY-sample.groundY):0;
                site.dry=sample.valid && site.depth<=.01f;site.slope=sample.slope;
                site.projected=nav.TryProject(profile,site.position,out Vector3 projected,out JourneyTravelMode mode) && mode==JourneyTravelMode.Walk;
                if(site.projected)site.position=projected;
                Collider[] blockers=Physics.OverlapCapsule(site.position+Vector3.up*.5f,site.position+Vector3.up*1.2f,.42f,
                    Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore)
                    .Where(c=>!(c is TerrainCollider) && c!=sample.supportingCollider && !c.GetComponentInParent<AnimalActor>() && !c.GetComponentInParent<WildlifeAgent>()).ToArray();
                site.clear=blockers.Length==0;site.colliders=blockers.Select(c=>c.name).ToArray();
            }
            int[][] pairs={new[]{0,1},new[]{0,2},new[]{1,3},new[]{2,3},new[]{4,0},new[]{4,1}};
            Route[] routes=pairs.Select(pair=>
            {
                Site a=sites[pair[0]],b=sites[pair[1]];
                JourneyRoute route=nav.TraceDirectRoute(a.position,b.position,profile);
                return new Route{from=a.name,to=b.name,horizontalLength=LifeWorld.Flat(a.position,b.position),
                    directWalk=route!=null && route.segments.Count>0 && route.segments.All(s=>s.mode==JourneyTravelMode.Walk)};
            }).ToArray();
            Nearby[] nearby=UnityEngine.Object.FindObjectsOfType<AnimalActor>().Where(a=>
            {
                WildlifeAgent wildlife=a.GetComponent<WildlifeAgent>();return wildlife ? wildlife.locomotion==WildlifeLocomotion.Ground : a.animalId.Contains("herd.sheep");
            }).Select(a=>new Nearby{animalId=a.animalId,position=a.transform.position,ferryDistance=LifeWorld.Flat(a.transform.position,sites[0].position),
                tideDistance=LifeWorld.Flat(a.transform.position,sites[2].position)}).OrderBy(a=>Mathf.Min(a.ferryDistance,a.tideDistance)).Take(12).ToArray();
            var report=new Report{utc=DateTime.UtcNow.ToString("O"),scene=scene.path,play=Application.isPlaying,dirty=scene.isDirty,sites=sites,routes=routes,nearby=nearby,
                boundary="Read-only current terrain, water, colliders and direct dry-land navigation sample. No animals or player moved, no scene saved. Candidate positions do not authorize placement; final chosen model envelopes and walking require separate Play verification."};
            string path="Docs/LifeGameplay/World/spatial-candidates.json";
            Directory.CreateDirectory(Path.GetDirectoryName(path));File.WriteAllText(path,JsonUtility.ToJson(report,true));
            Debug.Log("LifeSpatialAudit wrote "+path);return report;
        }
    }
}
