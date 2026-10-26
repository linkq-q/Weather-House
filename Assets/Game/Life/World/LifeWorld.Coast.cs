using System.Linq;
using UnityEngine;
using WeatherHousePrototype;
using WeatherHouse.Hillside;
using WeatherHouse.WildlifeJourney;

namespace WeatherHouse.Life
{
    public sealed partial class LifeWorld
    {
        GameObject coastalRoot;
        Vector3 coastalHere,coastalSheep,coastalDeer,coastalTide;
        void CreateCoastalActors()
        {
            var assets=Resources.Load<LifeCoastalAnimals>("LifeContent/CoastalAnimals");
            if(!assets || !assets.sheep || !assets.deer){SetupWarning+="缺少渡口动物资源；";return;}
            JourneyNavigation navigation=FindObjectOfType<JourneyNavigation>();
            if(!navigation || !FindCoastalSites(navigation)){SetupWarning+="渡口与潮池尚无通过检查的干岸站位；";return;}
            coastalRoot=new GameObject("渡口生活动物");coastalRoot.transform.SetParent(transform,false);
            var population=coastalRoot.AddComponent<WildlifePopulation>();
            population.terrain=Terrain.activeTerrain;population.waterSurface=FindObjectOfType<WorldWaterSurface>();
            population.player=Player;population.worldSeed=20261026;
            population.worldBounds=new Bounds(new Vector3(72,10,-54),new Vector3(28,50,28));
            WildlifeAgent sheep=CoastalAgent(assets.sheep,coastalSheep,6101,"渡口绵羊");
            WildlifeAgent deer=CoastalAgent(assets.deer,coastalDeer,6102,"渡口小鹿");
            population.agents=new[]{sheep,deer};
            WildlifeInteractionTarget.BindRuntime(sheep,population,"life.harbor.sheep","渡口绵羊");
            WildlifeInteractionTarget.BindRuntime(deer,population,"life.harbor.deer","渡口小鹿");
            // Population.Start owns initialization, body-envelope measurement and normal movement.
        }
        WildlifeAgent CoastalAgent(GameObject prefab,Vector3 point,int id,string display)
        {
            GameObject instance=Instantiate(prefab,point,Quaternion.identity,coastalRoot.transform);instance.name=display;
            WildlifeAgent animal=instance.GetComponent<WildlifeAgent>();
            animal.spawnId=id;animal.groupId=61;animal.habitatCenter=point;animal.habitatRadius=3;
            animal.timidity=.15f;animal.friendlyAcrossSpecies=true;
            return animal;
        }
        bool FindCoastalSites(JourneyNavigation navigation)
        {
            var profile=new JourneyProfile{bodyRadius=.6f,canSwim=false,wadeDepth=0,walkSpeed=1};
            // Current-scene read-only audit selected these dry points; the eastern alternative is a steep rock bank.
            Vector3[] homes={new Vector3(71,0,-55.5f)};
            Vector3[] tides={new Vector3(72,0,-49)};
            foreach(Vector3 home in homes)
            {
                if(!CoastalPoint(navigation,profile,home,out Vector3 here) ||
                    !CoastalPoint(navigation,profile,home+Vector3.back*1.1f,out Vector3 sheep) ||
                    !CoastalPoint(navigation,profile,home+Vector3.right*2.5f,out Vector3 deer))continue;
                foreach(Vector3 tide in tides)
                {
                    if(!CoastalPoint(navigation,profile,tide,out Vector3 end) || Flat(here,end)>6.7f || Flat(here,end)<3)continue;
                    JourneyRoute route=navigation.TraceDirectRoute(here,end,profile);
                    JourneyRoute pair=navigation.TraceDirectRoute(here,deer,profile);
                    if(route==null || pair==null || route.segments.Any(s=>s.mode!=JourneyTravelMode.Walk) || pair.segments.Any(s=>s.mode!=JourneyTravelMode.Walk))continue;
                    coastalHere=here;coastalSheep=sheep;coastalDeer=deer;coastalTide=end;return true;
                }
            }
            return false;
        }
        bool CoastalPoint(JourneyNavigation nav,JourneyProfile profile,Vector3 requested,out Vector3 point)
        {
            point=requested;
            if(!nav.TryProject(profile,requested,out point,out JourneyTravelMode mode) || mode!=JourneyTravelMode.Walk)return false;
            WorldWaterSurface water=FindObjectOfType<WorldWaterSurface>();
            if(water && water.Contains(point,.65f))return false;
            foreach(Collider c in Physics.OverlapCapsule(point+Vector3.up*.65f,point+Vector3.up*1.5f,.6f,
                Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore))
                if(!(c is TerrainCollider) && !c.GetComponentInParent<AnimalActor>() && !c.GetComponentInParent<WildlifeAgent>())return false;
            return true;
        }
    }
}
