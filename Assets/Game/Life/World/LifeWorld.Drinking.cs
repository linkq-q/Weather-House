using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using WeatherHouse.Hillside;
using WeatherHouse.SquirrelOga;

namespace WeatherHouse.Life
{
    public sealed partial class LifeWorld
    {
        sealed class Drink
        {
            public Binding animal;public LifeObject bowl;public Transform mouthBone;public Vector3 mouthLocal;
            public Vector3 returnTarget,stand,forward;public bool returnRest,consumed,approaching,planning;
            public float expires,started=-1,restartAfter,retryAt;public AnimationClip clip;
            public bool shared;public int sharedRounds;
            public Quaternion facing;public SkinnedMeshRenderer skin;public Mesh baked;
            public int contactFrame=-1;public bool touching;public float skinDistance;
            public readonly List<Vector3> skinVertices=new List<Vector3>();
        }
        readonly List<Drink> drinks=new List<Drink>();
        readonly List<string> drinkCandidates=new List<string>();
        // The original FBX has a 100-scale rig; Head-local points use its original centimetre-sized frame.
        static readonly Vector3 DrinkMouthLocal=new Vector3(-.00236940026f,.00106350422f,-.00027097020f);
        static readonly Vector3 DrinkRootContact=new Vector3(.178034385f,.101095977f,.188993630f);
        bool BeginDrink(LifeObject bowl)
        {
            drinkCandidates.Clear();
            if(drinks.Count>=2 || drinks.Any(d=>d.bowl!=bowl))return false;
            if(drinks.Count==1 && bowl.State.water<.38f){owner.SaveAndNotify("再接一些水，才够同伴一起喝。");return false;}
            Binding animal=bindings.Values.Where(b=>b.wildlife && b.wildlife.isActiveAndEnabled && b.wildlife.speciesId=="squirrel" &&
                !drinks.Any(d=>d.animal==b) && Flat(b.speaker.transform.position,bowl.transform.position)<3.5f).OrderBy(b=>Flat(b.speaker.transform.position,bowl.transform.position)).FirstOrDefault();
            SquirrelOgaRig oga=animal?.wildlife.GetComponentInChildren<SquirrelOgaRig>(true);
            AnimationClip clip=oga?oga.drink:Clip("SquirrelDrink");
            Transform mouth=oga?oga.head:animal?.wildlife.GetComponentsInChildren<Transform>().FirstOrDefault(t=>t.name=="Head");
            if(animal==null || !clip || !mouth || !bowl.waterVisual)return false;
            // The new muzzle belongs to its own skin, not to the larger body renderer.
            SkinnedMeshRenderer skin=oga?oga.headSkin:animal.wildlife.GetComponentsInChildren<SkinnedMeshRenderer>().Where(s=>s.sharedMesh).OrderByDescending(s=>s.sharedMesh.vertexCount).FirstOrDefault();
            if(!skin)return false;
            Vector3 forward=bowl.transform.position-animal.speaker.transform.position;forward.y=0;
            if(forward.sqrMagnitude<.01f)forward=animal.speaker.transform.forward;
            if(drinks.Count==1)forward=-(drinks[0].facing*Vector3.forward);
            bool planned=FindDrinkStand(animal,bowl,forward,out Vector3 at,out Quaternion facing,out Vector3 approach);
            if(!planned && (drinks.Count==0 || !animal.wildlife.RequestLifeTarget(animal.speaker.transform.position,1.5f,false,.045f)))
            {owner.SaveAndNotify("碗前的路被挡住了。");return false;}
            bool sharing=drinks.Count>0;
            foreach(Drink partner in drinks){partner.shared=true;partner.expires=Time.time+32;}
            drinks.Add(new Drink{animal=animal,bowl=bowl,mouthBone=mouth,mouthLocal=oga?oga.mouthHeadLocal:DrinkMouthLocal,clip=clip,returnTarget=animal.target,returnRest=animal.rest,expires=Time.time+(sharing?32:18),shared=sharing,
                facing=facing,stand=at,forward=forward,planning=!planned,retryAt=Time.time+.5f,approaching=planned && Flat(approach,at)>.001f,
                skin=skin,baked=new Mesh{name="Squirrel mouth contact sample"}});
            animal.target=planned?approach:animal.speaker.transform.position;animal.rest=false;
            owner.SaveAndNotify(!planned?"同伴在等一条空路。":"");return true;
        }
        bool FindDrinkStand(Binding animal,LifeObject bowl,Vector3 forward,out Vector3 at,out Quaternion facing,out Vector3 approach)
        {
            at=approach=default;facing=Quaternion.identity;float rim=.29f*Mathf.Abs(bowl.waterVisual.lossyScale.x)*.72f;
            SquirrelOgaRig oga=animal.wildlife.GetComponentInChildren<SquirrelOgaRig>(true);
            Vector3 contact=oga?Vector3.Scale(oga.drinkRootContact,animal.wildlife.transform.lossyScale):DrinkRootContact;
            foreach(float angle in new[]{0f,25f,-25f,50f,-50f,75f,-75f,180f})
            {
                facing=Quaternion.LookRotation(Quaternion.Euler(0,angle,0)*forward);
                Vector3 point=bowl.waterVisual.position-facing*Vector3.forward*rim-facing*(contact*animal.wildlife.ScaleFactor);
                Terrain terrain=Terrain.activeTerrain;if(terrain)point.y=terrain.SampleHeight(point)+terrain.transform.position.y;
                bool separated=true;
                foreach(Drink other in drinks)
                {
                    if(other.animal==animal || other.planning)continue;
                    Vector3 delta=other.stand-point;delta.y=0;Vector3 direction=delta.normalized;
                    float reach=animal.wildlife.BodyEnvelope.Reach(direction,facing,animal.wildlife.Radius)+
                        other.animal.wildlife.BodyEnvelope.Reach(-direction,other.facing,other.animal.wildlife.Radius);
                    if(delta.magnitude<reach+.025f){drinkCandidates.Add("final bodies overlap: "+point.ToString("F3"));separated=false;break;}
                }
                if(!separated)continue;
                Vector3 from=animal.wildlife.transform.position;Quaternion rotation=animal.wildlife.transform.rotation;
                if(DrinkPathClear(animal,from,rotation,point,facing) && animal.wildlife.RequestLifeTarget(point,1.5f,false,.045f))
                {at=approach=point;return true;}
                if(drinks.Count==0)continue;
                // One checked outside corner avoids walking through the companion's reserved drinking place.
                foreach(float radius in new[]{1f,1.4f})for(int step=0;step<8;step++)
                {
                    Vector3 via=bowl.transform.position+Quaternion.Euler(0,step*45,0)*Vector3.right*radius;
                    if(terrain)via.y=terrain.SampleHeight(via)+terrain.transform.position.y;
                    Quaternion onward=WildlifeBodyEnvelope.Heading(point-via,rotation);
                    if(DrinkPathClear(animal,from,rotation,via,onward) && DrinkPathClear(animal,via,onward,point,facing) &&
                        animal.wildlife.RequestLifeTarget(via,1.5f,false,.045f))
                    {at=point;approach=via;return true;}
                }
            }
            return false;
        }
        bool DrinkPathClear(Binding animal,Vector3 from,Quaternion rotation,Vector3 to,Quaternion facing)
        {
            string path=from.ToString("F3")+" -> "+to.ToString("F3")+" face="+facing.eulerAngles.y.ToString("F1");
            foreach(Drink other in drinks)
            {
                if(other.animal==animal || other.planning)continue;
                if(!DrinkApproachClear(animal.wildlife.BodyEnvelope,from,rotation,to,facing,other.animal.wildlife.BodyEnvelope,other.stand,other.facing))
                {drinkCandidates.Add("reserved body: "+path);return false;}
            }
            Quaternion travel=WildlifeBodyEnvelope.Heading(to-from,rotation);
            if(!animal.population || !animal.population.TryMoveFrom(animal.wildlife,from,rotation,to,out _))
            {drinkCandidates.Add("scene route: "+path);return false;}
            if(!animal.population.TryMoveFrom(animal.wildlife,to,travel,to+facing*Vector3.forward*.001f,out _))
            {drinkCandidates.Add("scene final turn: "+path);return false;}
            drinkCandidates.Add("clear: "+path);return true;
        }
        static bool DrinkApproachClear(WildlifeBodyEnvelope body,Vector3 from,Quaternion rotation,Vector3 to,Quaternion facing,
            WildlifeBodyEnvelope partner,Vector3 partnerAt,Quaternion partnerFacing)
        {
            // Both controllers may stop just inside their arrival circles. Reserve that physical tolerance as well as the final bodies.
            partner.half+=new Vector3(.09f,0,.09f);
            Quaternion travel=WildlifeBodyEnvelope.Heading(to-from,rotation);
            return WildlifeBodyPhysics.ClearOfBody(body,from,rotation,from,travel,partner,partnerAt,partnerFacing) &&
                WildlifeBodyPhysics.ClearOfBody(body,from,travel,to,travel,partner,partnerAt,partnerFacing) &&
                WildlifeBodyPhysics.ClearOfBody(body,to,travel,to,facing,partner,partnerAt,partnerFacing);
        }
        static bool ReadyToDrink(Drink drink)=>drink.animal.wildlife && drink.animal.wildlife.isActiveAndEnabled &&
            !drink.planning && !drink.approaching && drink.animal.wildlife.HasLifeTarget && drink.animal.wildlife.State==WildlifeState.Idle &&
            Flat(drink.animal.wildlife.transform.position,drink.animal.target)<.045f && drink.animal.wildlife.ActualSpeed<=.05f &&
            drink.animal.wildlife.RestPhase==WildlifeRestPhase.Standing && Quaternion.Angle(drink.animal.wildlife.transform.rotation,drink.facing)<=5;
        void UpdateDrinks()
        {
            for(int i=drinks.Count-1;i>=0;i--)
            {
                Drink d=drinks[i];WildlifeAgent animal=d.animal.wildlife;
                if(!animal || !animal.isActiveAndEnabled || !d.bowl || !Player || Flat(Player.position,d.bowl.transform.position)>8 ||
                    d.bowl.State.water<.12f || Time.time>d.expires)
                {EndDrink(d);drinks.RemoveAt(i);continue;}
                if(d.planning)
                {
                    if(Time.time<d.retryAt)continue;
                    d.retryAt=Time.time+.5f;drinkCandidates.Clear();
                    if(FindDrinkStand(d.animal,d.bowl,d.forward,out Vector3 stand,out Quaternion facing,out Vector3 approach))
                    {d.stand=stand;d.facing=facing;d.approaching=Flat(approach,stand)>.001f;d.animal.target=approach;d.planning=false;}
                    continue;
                }
                if(d.started<0)
                {
                    if(!animal.HasLifeTarget || animal.State!=WildlifeState.Idle || Flat(animal.transform.position,d.animal.target)>=.045f ||
                        animal.ActualSpeed>.05f || animal.RestPhase!=WildlifeRestPhase.Standing)continue;
                    if(d.approaching)
                    {
                        if(animal.RequestLifeTarget(d.stand,1.5f,false,.045f)){d.animal.target=d.stand;d.approaching=false;}
                        continue;
                    }
                    animal.FaceLifePoint(animal.transform.position+d.facing*Vector3.forward,Time.deltaTime);
                    if(Quaternion.Angle(animal.transform.rotation,d.facing)>5 || Time.time<d.restartAfter)continue;
                    if(d.shared && drinks.Any(other=>other!=d && other.bowl==d.bowl &&
                        (!ReadyToDrink(other) || Time.time<other.restartAfter || (other.started>=0 && Time.time-other.started>.05f))))continue;
                    if(d.shared)d.sharedRounds++;
                    animal.RequestLifeAction(d.clip,d.clip.length);d.started=Time.time;
                }
                else
                {
                    float age=Time.time-d.started;
                    if(!d.consumed && age>.75f && age<2.3f && MouthTouchesWater(d))
                    {d.consumed=true;d.bowl.State.water=Mathf.Max(0,d.bowl.State.water-.12f);d.bowl.State.revision++;LifeAudio.PlayAt(LifeAudioCue.Drink,d.bowl.waterVisual.position);owner.SaveAndNotify("");}
                    if(age>=d.clip.length)
                    {
                        if(d.shared && d.sharedRounds<3 && drinks.Any(other=>other!=d && other.bowl==d.bowl))
                        {animal.CancelLifeAction();d.started=-1;d.restartAfter=Time.time+.25f;d.contactFrame=-1;}
                        else {EndDrink(d);drinks.RemoveAt(i);}
                    }
                }
            }
        }
        static bool MouthTouchesWater(Drink d)
        {
            if(!d.mouthBone || !d.bowl.waterVisual || d.started<0 || Time.time-d.started<.75f || Time.time-d.started>2.3f)return false;
            if(d.contactFrame==Time.frameCount)return d.touching;
            d.contactFrame=Time.frameCount;
            Vector3 point=d.mouthBone.TransformPoint(d.mouthLocal),water=d.bowl.waterVisual.position;
            d.skin.BakeMesh(d.baked,true);d.baked.GetVertices(d.skinVertices);float nearest=float.MaxValue;
            foreach(Vector3 vertex in d.skinVertices)nearest=Mathf.Min(nearest,(d.skin.transform.TransformPoint(vertex)-point).sqrMagnitude);
            d.skinDistance=Mathf.Sqrt(nearest);
            float radius=.29f*Mathf.Abs(d.bowl.waterVisual.lossyScale.x);
            return d.touching=d.skinDistance<.035f && Flat(point,water)<=radius && Mathf.Abs(point.y-water.y)<=.075f;
        }
        void EndDrink(Drink drink)
        {
            if(drink.animal.wildlife)drink.animal.wildlife.CancelLifeAction();
            if(drink.baked)Destroy(drink.baked);
            drink.animal.target=drink.returnTarget;drink.animal.rest=drink.returnRest;
        }
        void CancelDrinks(){foreach(Drink drink in drinks)EndDrink(drink);drinks.Clear();}
    }
}
