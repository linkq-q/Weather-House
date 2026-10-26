using UnityEngine;

namespace WeatherHouse.Life
{
    public sealed partial class LifeWorld
    {
        bool NearTaskAnimal(LifeRequest request)
        {
            LifeSpeaker speaker=FindSpeaker(request.speakerKey);
            return Player && speaker && LifeSession.InsideCircle(Player.position,speaker.transform.position,LifeSession.CastRadius);
        }
        bool NearTaskAnimal(LifeRequest request,Vector3 at)
        {
            LifeSpeaker speaker=FindSpeaker(request.speakerKey);
            return NearTaskAnimal(request) && LifeSession.InsideCircle(speaker.transform.position,at,LifeSession.CastRadius) &&
                LifeSession.InsideCircle(Player.position,at,LifeSession.CastRadius);
        }
        static bool IsLocationCondition(string condition)=>condition=="arrive" || condition=="animal_arrived" || condition=="walk_sequence" || condition=="walk_reverse";
        bool ActedAfter(Accepted accepted,LifeObject obj,LifeRequest request)
        {
            ObjectBaseline baseline=accepted.objects?.Find(v=>v.id==obj.State.id);
            if(obj.State.actionRevision>(baseline?.action??0))return true;
            // Before per-object histories existed, placement was recorded on the authored slot.
            // Preserve that pending action only for its original slot, never for arbitrary old mats.
            return accepted.legacyObjectHistory && request!=null &&
                (obj.State.id==request.targetId || obj.State.slot==request.targetId) &&
                Revision(request.targetId,true)>accepted.baseline;
        }
        static bool CollectedAfter(Accepted accepted,LifeObject obj)=>accepted.objects?.Exists(v=>v.id==obj.State.id && v.collected)==true;
        float CommonWater(string site)
        {
            float water=0;Vector3 at=AnchorPosition(site);
            foreach(LifeObject obj in objects.Values)
                if(obj && obj.State.kind==LifeObjectKind.Bowl && Flat(obj.transform.position,at)<3.5f)water=Mathf.Max(water,obj.State.water);
            return water;
        }
        bool CommonReady(Accepted accepted,string site)
        {
            LifeRequest request=System.Array.Find(requests,r=>r.id==accepted.id);
            if(request==null || !NearTaskAnimal(request,AnchorPosition(site)) || !DryMatAt(site) || CommonWater(site)<.3f)return false;
            if(accepted.legacyObjectHistory && accepted.stage>=1 && site=="squirrel.common")return true;
            bool mat=false,water=false;
            foreach(LifeObject obj in objects.Values)
            {
                if(!obj)continue;ObjectBaseline baseline=accepted.objects?.Find(v=>v.id==obj.State.id);if(baseline==null)continue;
                if(obj.State.kind==LifeObjectKind.Mat && obj.State.slot==site && obj.State.IsDry && baseline.rainSoaked)mat=true;
                if(obj.State.kind==LifeObjectKind.Bowl && obj.State.water>=.3f && Flat(obj.transform.position,AnchorPosition(site))<3.5f && baseline.collected)water=true;
            }
            return mat && water;
        }
        bool HasExistingRoof(Vector3 at)
        {
            foreach(RaycastHit hit in Physics.RaycastAll(at+Vector3.up*4,Vector3.down,3,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore))
                if(hit.point.y>at.y+1 && hit.normal.y>.4f && !hit.collider.GetComponentInParent<LifeObject>())return true;
            foreach(LifeObject site in objects.Values)
                if(site && site.State.kind==LifeObjectKind.Rest && site.State.sheltered && Flat(site.transform.position,at)<.65f)return true;
            return false;
        }
    }
}
