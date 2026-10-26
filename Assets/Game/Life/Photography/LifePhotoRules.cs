using System;
using System.Collections.Generic;
using System.Linq;

namespace WeatherHouse.Life
{
    // A pure matcher. Old photographs never consult today's weather, animals or learned words.
    public static partial class LifePhotoRules
    {
        public static string[] Match(LifePhotoSnapshot snapshot,IReadOnlyList<LifePublicContext> publicHistory)
        {
            var result=new List<string>();
            foreach(LifePublicContext context in publicHistory??Array.Empty<LifePublicContext>())
                if(context!=null && context.kind=="interaction" && context.completed &&
                    Tasks.Any(t=>t.IsInteraction && t.id==context.targetId) && !result.Contains(context.targetId))result.Add(context.targetId);
            if(snapshot==null || !string.IsNullOrEmpty(snapshot.visibilityError))return result.ToArray();
            LifePhotoEntity[] entities=snapshot.entities??Array.Empty<LifePhotoEntity>();
            bool Any(Func<LifePhotoEntity,bool> predicate)=>entities.Any(e=>e!=null && predicate(e));
            bool Animal(string species)=>Any(e=>e.kind=="animal" && e.speciesId==species && e.bodyVisible);
            bool Grass(LifePhotoEntity e)=>e.kind=="grass" || e.kind=="bundle";
            void Add(string id,bool matched){if(matched)result.Add(id);}
            bool Context(string kind)=>Any(e=>e.kind=="animal" && e.bodyVisible &&
                (snapshot.contexts??Array.Empty<LifePublicContext>()).Any(c=>c!=null && c.kind==kind && c.actorId==e.id &&
                ((kind=="hello" || kind=="goodbye") || e.environmentVisible)));
            Add("P01",Animal("fox"));Add("P02",Animal("squirrel"));
            Add("P03",Any(e=>Grass(e) && e.bodyVisible));
            Add("P04",Any(e=>Grass(e) && e.bodyVisible && e.wetSurfaceVisible && e.wetness>LifeObjectState.DryThreshold));
            Add("P05",Any(e=>e.rainVisible && e.rainStrength>0));
            Add("P06",Any(e=>e.waterSurfaceVisible && e.water>LifeObjectState.WaterVisibleThreshold));
            Add("P07",Any(e=>e.bodyVisible && e.sunContrastVisible));
            Add("P08",Any(e=>(e.kind=="bundle" || e.kind=="mat") && e.bodyVisible && e.wetness<=LifeObjectState.DryThreshold));
            Add("P09",Any(e=>e.kind=="animal" && e.bodyVisible && e.sleeping));
            Add("P10",Any(e=>e.kind=="tree" && e.bodyVisible));
            // Home ownership is the sole later context allowed to enrich an older photo.
            Add("P11",Any(e=>e.kind=="home" && e.bodyVisible &&
                (snapshot.contexts??Array.Empty<LifePublicContext>()).Concat(publicHistory??Array.Empty<LifePublicContext>())
                .Any(c=>c!=null && c.kind=="home" && c.targetId==e.id && !string.IsNullOrEmpty(c.actorId))));
            Add("P12",Animal("sheep"));
            Add("P13",Any(e=>e.kind=="animal" && e.speciesId=="sheep" && e.bodyVisible && e.eating && e.mouthVisible && e.foodVisible));
            Add("P14",Any(e=>e.windEffectVisible && e.windStrength>0));
            Add("P15",Any(e=>(e.windEffectVisible && e.windStrength==1) || (e.rainVisible && e.rainStrength==1)));
            Add("P16",Any(e=>(e.windEffectVisible && e.windStrength==2) || (e.rainVisible && e.rainStrength==2)));
            Add("P17",Animal("deer"));Add("P18",Context("cold"));Add("P19",Context("warm"));
            Add("P20",Context("hello"));Add("P21",Context("goodbye"));
            Add("P22",entities.Where(e=>e!=null && e.kind=="animal" && e.bodyVisible && !string.IsNullOrEmpty(e.activityId) &&
                    (e.activityKind=="drink" || (e.activityKind=="rest" && e.sleeping)))
                .GroupBy(e=>e.activityKind+":"+e.activityId).Any(g=>g.Select(e=>e.id).Distinct().Count()>=2 &&
                    (g.First().activityKind=="rest" || Any(e=>e.id==g.First().activityId && e.waterSurfaceVisible && e.water>LifeObjectState.WaterVisibleThreshold))));
            return result.ToArray();
        }
    }
}
