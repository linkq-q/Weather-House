using System;
using System.Linq;

namespace WeatherHouse.Life.Editor
{
    public static class LifePhotoRuleChecks
    {
        public static void Run()
        {
            void Check(bool ok,string label){if(!ok)throw new InvalidOperationException("Photo rules: "+label);}
            bool Has(string task,LifePhotoEntity entity,params LifePublicContext[] contexts)=>LifePhotoRules.Match(new LifePhotoSnapshot{entities=new[]{entity},contexts=contexts},null).Contains(task);
            Check(LifePhotoRules.Tasks.Count==26 && LifePhotoRules.Tasks.Select(t=>t.id).Distinct().Count()==26,"26 unique approved tasks");
            Check(LifePhotoRules.Tasks.Where(t=>!t.IsInteraction).Count()==22 && LifePhotoRules.Tasks.Where(t=>t.IsInteraction).Count()==4,"22 P and 4 I definitions");
            foreach(var pair in new[]{("P01","fox"),("P02","squirrel"),("P12","sheep"),("P17","deer")})
            {
                var animal=new LifePhotoEntity{id="animal",kind="animal",speciesId=pair.Item2,bodyVisible=true};
                Check(Has(pair.Item1,animal),pair.Item1+" clear subject");animal.bodyVisible=false;
                Check(!Has(pair.Item1,animal),pair.Item1+" unseen subject rejected");
            }
            var grass=new LifePhotoEntity{kind="grass",bodyVisible=true,wetness=.8f,wetSurfaceVisible=true};
            Check(Has("P03",grass)&&Has("P04",grass),"One wet source can earn grass and wet together");
            grass.wetSurfaceVisible=false;Check(!Has("P04",grass),"Backend wetness alone cannot prove visible wet grass");
            var mat=new LifePhotoEntity{kind="mat",bodyVisible=true,wetness=LifeObjectState.DryThreshold};
            Check(Has("P08",mat)&&!Has("P03",mat),"A dry mat is not first-grass evidence");mat.wetness+=.001f;Check(!Has("P08",mat),"Dry boundary is the shared physical threshold");
            var rain=new LifePhotoEntity{rainVisible=true,rainStrength=1};Check(Has("P05",rain)&&Has("P15",rain),"Visible weak rain matches both words");
            rain.rainStrength=2;Check(Has("P16",rain),"Strong rain variant");rain.rainVisible=false;Check(!Has("P05",rain),"Rain outside the photograph does not match");
            var bowl=new LifePhotoEntity{water=.5f,waterSurfaceVisible=true};Check(Has("P06",bowl),"Visible retained water");bowl.waterSurfaceVisible=false;Check(!Has("P06",bowl),"Hidden water does not match");
            bowl.water=.19f;bowl.waterSurfaceVisible=true;Check(Has("P06",bowl),"Visible remaining water is still water after animals drink");
            bowl.water=LifeObjectState.WaterVisibleThreshold;Check(!Has("P06",bowl),"A visually empty water container does not match");
            Check(Has("P07",new LifePhotoEntity{bodyVisible=true,sunContrastVisible=true}),"Lit and shadowed surface contrast");
            Check(!Has("P07",new LifePhotoEntity{bodyVisible=true}),"Exposure alone is not sunlight");
            var sleeper=new LifePhotoEntity{kind="animal",speciesId="fox",bodyVisible=true,sleeping=true};
            Check(Has("P09",sleeper),"Any qualifying sleeping species is valid");sleeper.sleeping=false;Check(!Has("P09",sleeper),"Idle is not sleep");
            Check(Has("P10",new LifePhotoEntity{kind="tree",bodyVisible=true}),"Registered visible tree");
            var home=new LifePhotoSnapshot{entities=new[]{new LifePhotoEntity{id="den",kind="home",bodyVisible=true}}};
            Check(!LifePhotoRules.Match(home,null).Contains("P11"),"An unowned shelter is not home");
            var laterHome=new LifePublicContext{kind="home",targetId="den",actorId="fox"};
            Check(LifePhotoRules.Match(home,new[]{laterHome}).Contains("P11"),"Later public ownership can enrich an older home photo");
            var eater=new LifePhotoEntity{kind="animal",speciesId="sheep",bodyVisible=true,eating=true,mouthVisible=true,foodVisible=true};
            Check(Has("P13",eater),"Eating requires the action, mouth and food");eater.mouthVisible=false;Check(!Has("P13",eater),"Occluded mouth fails");
            var flag=new LifePhotoEntity{windStrength=1,windEffectVisible=true};Check(Has("P14",flag)&&Has("P15",flag),"Visible weak wind");
            flag.windStrength=2;Check(Has("P16",flag),"Visible strong wind");flag.windStrength=0;Check(!Has("P15",flag),"Off is not weak");
            foreach(var pair in new[]{("P18","cold"),("P19","warm"),("P20","hello"),("P21","goodbye")})
            {
                var actor=new LifePhotoEntity{id="actor",kind="animal",bodyVisible=true,environmentVisible=true};
                var context=new LifePublicContext{kind=pair.Item2,actorId="actor"};
                Check(!Has(pair.Item1,actor),pair.Item1+" needs public context");Check(Has(pair.Item1,actor,context),pair.Item1+" shutter context");
                Check(!LifePhotoRules.Match(new LifePhotoSnapshot{entities=new[]{actor}},new[]{context}).Contains(pair.Item1),pair.Item1+" later history cannot backfill an earlier state");
            }
            LifePhotoEntity A(string id)=>new LifePhotoEntity{id=id,kind="animal",bodyVisible=true,sleeping=true,activityId="bed",activityKind="rest"};
            var pairPhoto=new LifePhotoSnapshot{entities=new[]{A("a"),A("b")}};
            Check(LifePhotoRules.Match(pairPhoto,null).Contains("P22"),"Two different actual users share one rest activity");
            pairPhoto.entities[1].activityId="other";Check(!LifePhotoRules.Match(pairPhoto,null).Contains("P22"),"Incidental coframing is not together");
            var drinkA=A("a");var drinkB=A("b");drinkA.activityKind=drinkB.activityKind="drink";drinkA.activityId=drinkB.activityId="bowl";
            bowl.id="bowl";bowl.water=.19f;bowl.waterSurfaceVisible=true;pairPhoto.entities=new[]{drinkA,drinkB,bowl};
            Check(LifePhotoRules.Match(pairPhoto,null).Contains("P22"),"Two actual drinkers still share visible water after both measured sips");
            bowl.water=LifeObjectState.WaterVisibleThreshold;Check(!LifePhotoRules.Match(pairPhoto,null).Contains("P22"),"An empty shared bowl cannot match together drinking");
            bowl.water=.19f;bowl.waterSurfaceVisible=false;Check(!LifePhotoRules.Match(pairPhoto,null).Contains("P22"),"An unseen shared water surface cannot match together drinking");
            bowl.waterSurfaceVisible=true;drinkB.bodyVisible=false;Check(!LifePhotoRules.Match(pairPhoto,null).Contains("P22"),"A hidden second drinker cannot match together");
            foreach(string id in new[]{"I01","I02","I03","I04"})
            {
                var context=new LifePublicContext{kind="interaction",targetId=id,completed=false};
                Check(!LifePhotoRules.Match(null,new[]{context}).Contains(id),id+" public plan alone does not complete");
                context.completed=true;Check(LifePhotoRules.Match(null,new[]{context}).Contains(id),id+" actual completed public event");
            }
        }
    }
}
