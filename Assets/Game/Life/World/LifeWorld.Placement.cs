using System.Collections.Generic;
using UnityEngine;
using WeatherHouse.Hillside;

namespace WeatherHouse.Life
{
    public sealed partial class LifeWorld
    {
        readonly WildlifeBodyPhysics placementPhysics=new WildlifeBodyPhysics();

        void PrepareInitialLifePosition(Binding animal)
        {
            WildlifePopulation population=animal.population;
            // Only the initial scene setup may repair an authored spawn inside furniture.
            // A loaded task, invitation or later obstruction must use ordinary movement.
            if(!population || !population.Initialized || population.SimulationTime!=0 || !animal.wildlife.BodyEnvelope.Valid)return;
            Vector3 original=animal.speaker.transform.position;
            if(ClearTurn(animal,original))return;
            foreach(Vector3 candidate in Around(original,.4f,.8f,1.2f,1.6f,2f))
            {
                if(!population.TryProjectPosition(animal.wildlife,candidate,out Vector3 point) || !ClearTurn(animal,point))continue;
                animal.speaker.transform.position=point;
                animal.home=animal.target=point;
                Physics.SyncTransforms();
                animal.wildlife.RequestLifeTarget(point,1.5f);
                return;
            }
            SetupWarning+="松鼠初始位置没有可转身的安全落点；";
        }

        bool ClearTurn(Binding animal,Vector3 point)
        {
            if(!animal.population || !animal.population.Initialized || !animal.wildlife.BodyEnvelope.Valid)return false;
            WildlifeBodyEnvelope envelope=animal.wildlife.BodyEnvelope;
            envelope.half+=new Vector3(.08f,0,.08f);
            for(int i=0;i<4;i++)
            {
                Quaternion from=Quaternion.Euler(0,i*90,0),to=Quaternion.Euler(0,(i+1)*90,0);
                if(!placementPhysics.Clear(envelope,point,from,point,to,animal.population.obstacleMask,
                    owner:animal.wildlife.transform,player:animal.population.PlayerCollision,ignoreAnimals:true) ||
                    !animal.population.TryMoveFrom(animal.wildlife,point,from,point,out _))return false;
            }
            return true;
        }

        static IEnumerable<Vector3> Around(Vector3 center,params float[] radii)
        {
            foreach(float radius in radii)
                for(int i=0;i<(radius==0?1:16);i++)yield return center+Quaternion.Euler(0,i*22.5f,0)*Vector3.forward*radius;
        }
        bool Route(Binding animal,Vector3 from,Vector3 to)
        {
            return animal.population && animal.population.Initialized && Flat(from,to)<=7.5f &&
                animal.population.TryMoveFrom(animal.wildlife,from,animal.speaker.transform.rotation,to,out _);
        }
        bool LevelPoint(Binding animal,Vector3 requested,out Vector3 point)
        {
            point=requested;
            if(!animal.population || !animal.population.TryProjectPosition(animal.wildlife,requested,out point) || !ClearTurn(animal,point))return false;
            foreach(Vector3 edge in Around(point,.6f))
                if(!animal.population.TryProjectPosition(animal.wildlife,edge,out Vector3 p) || Mathf.Abs(p.y-point.y)>.13f)return false;
            return true;
        }
        bool SharedBowlSpace(Vector3 point)
        {
            // A body-sized destination alone can fit a bowl against a stump. Reserve the two
            // drinking bodies and a level view around the bowl, while retaining original furniture.
            foreach(Collider hit in Physics.OverlapSphere(point+Vector3.up*.4f,.9f,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore))
                if(!(hit is TerrainCollider) && !hit.GetComponentInParent<WeatherHousePrototype.AnimalActor>() &&
                    (!Player || !hit.transform.IsChildOf(Player)))return false;
            Terrain terrain=Terrain.activeTerrain;if(!terrain)return false;
            int levelViews=0;
            foreach(Vector3 at in Around(point,1.8f))
                if(Mathf.Abs(terrain.SampleHeight(at)+terrain.transform.position.y-point.y)<.2f)levelViews++;
            return levelViews>=8;
        }

        void BuildSquirrelPlaces(Binding main)
        {
            if(!bindings.TryGetValue("companion",out Binding companion)){SetupWarning+="共同歇脚缺少固定同伴；";return;}
            Vector3 middle=(main.home+companion.home)*.5f;
            foreach(Vector3 candidate in Around(middle,0,.6f,1.2f,1.8f,2.4f,3f))
            {
                if(Flat(candidate,main.home)>3.8f || Flat(candidate,companion.home)>6.7f ||
                    !LevelPoint(main,candidate,out Vector3 common) || !Route(main,main.home,common))continue;
                Vector3 seat=default,nest=default,bowl=default;bool foundSeat=false,foundNest=false,foundBowl=false;
                foreach(Vector3 next in Around(common,1.3f,1.6f))
                {
                    if(!LevelPoint(companion,next,out seat) || !Route(companion,companion.home,seat))continue;
                    foundSeat=true;break;
                }
                if(!foundSeat)continue;
                foreach(Vector3 next in Around(main.home,1.7f,2.1f,2.5f))
                {
                    if(Flat(next,common)<2.2f || Flat(next,seat)<1.4f || !LevelPoint(main,next,out nest) ||
                        !Route(main,main.home,nest) || !Route(main,nest,common) || !Route(main,common,nest))continue;
                    foundNest=true;break;
                }
                if(!foundNest)continue;
                foreach(Vector3 next in Around(common,1.25f,1.6f,1.9f))
                {
                    if(Flat(next,seat)<1.1f || Flat(next,nest)<1.6f || !LevelPoint(main,next,out bowl) ||
                        Mathf.Abs(bowl.y-common.y)>.1f || !SharedBowlSpace(bowl) || !Route(main,common,bowl) || !Route(companion,seat,bowl))continue;
                    foundBowl=true;break;
                }
                if(!foundBowl)continue;
                Rest("squirrel.nest","松鼠窝",nest,true,19);
                Rest("squirrel.common","歇脚处",common,false,19);
                LifeObject commonBowl=Model("squirrel.common.bowl","水碗",LifeObjectKind.Bowl,"WaterBowl",bowl);
                // The original 17.7cm rim intersects the small squirrels' drinking bodies.
                Transform basin=commonBowl.transform.Find("WaterBowl");
                if(basin)basin.localScale=new Vector3(1f,.5f,1f);
                // The companion has a separate body-sized seat, not a second identity or teleport.
                anchors["squirrel.common.companion"]=seat;
                return;
            }
            SetupWarning+="松鼠生活点缺少同时可达的窝、歇脚处和水碗；";
            invalidTargets.Add("squirrel.nest");invalidTargets.Add("squirrel.common");
        }
    }
}
