using System;
using System.Linq;
using UnityEngine;
using WeatherHouse.Hillside;

namespace WeatherHouse.Life
{
    public sealed partial class LifeWorld
    {
        LifeObject Nearest()
        {
            if(!Player)return null;
            LifeObject nearest=null;float distance=2.15f;
            foreach(LifeObject obj in interactables)
            {
                if(!obj || obj==held || obj.State.held || obj.State.kind==LifeObjectKind.GrassSource || !string.IsNullOrEmpty(obj.State.slot))continue;
                float d=Flat(obj.transform.position,Player.position);if(d<distance){nearest=obj;distance=d;}
            }
            return nearest;
        }
        public string InteractionHint
        {
            get
            {
                if(feeding!=null)return "正在递草…";
                if(craftingId!=null)return "正在编垫…移动可中断，材料会保留";
                LifeObject obj=Nearest();
                if(held && held.State.kind==LifeObjectKind.Bundle)
                    return NearbyFoodAnimal()!=null?"F 递草  ·  Shift+F 放下":obj && obj.State.kind==LifeObjectKind.Bowl?"F 浸湿草束  ·  Shift+F 放下":"F 编成草垫  ·  Shift+F 放下";
                if(!obj)return held?"F 放下"+held.Description:ClosedBuildingHint();
                if(!held && DemonstrationBlocked(obj))return "先收起当前魔法，再按 F 重放它的演示";
                switch(obj.State.kind)
                {
                    case LifeObjectKind.Bundle:case LifeObjectKind.Mat:return held?"F 放下手中物件":"F 拿起 "+obj.Description;
                    case LifeObjectKind.Loom:return held && held.State.kind==LifeObjectKind.Bundle?"F 编成草垫":"编垫点 · 需要草束";
                    case LifeObjectKind.Bowl:return held?"F 用碗里的水浸湿手中物件":obj.Description+
                        (drinks.Count==1 && drinks[0].bowl==obj?" · F 邀请同伴":" · F 邀请饮水")+" · Shift+F 倒空";
                    case LifeObjectKind.Rest:return held && held.State.kind==LifeObjectKind.Mat?"F 铺下草垫":DryMatAt(obj.State.id)?"F 邀请附近动物休息 · Shift+F 收回草垫":obj.Description+" · 放一张草垫";
                    default:
                        if(obj.State.id=="squirrel.common.invite")return "F 邀请身边的动物一起歇脚";
                        return held?"F 放下手中物件":"F 查看 "+obj.Description;
                }
            }
        }
        public void Interact()=>InteractWithModifiers(Input.GetKey(KeyCode.LeftShift)||Input.GetKey(KeyCode.RightShift));
        void InteractWithModifiers(bool shift)
        {
            if(!owner || !Player || craftingId!=null || feeding!=null)return;
            LifeObject obj=Nearest();
            if(held && held.State.kind==LifeObjectKind.Bundle)
            {
                if(shift){Drop();return;}
                Binding animal=NearbyFoodAnimal();if(animal!=null){BeginFeed(animal);return;}
                if(!obj || obj.State.kind!=LifeObjectKind.Bowl){StartCraft();return;}
            }
            if(!obj){if(held)Drop();return;}
            if(!held && !shift && ReplayDemonstration(obj))return;
            if(obj.State.id=="squirrel.common.invite"){InviteCommon();return;}
            switch(obj.State.kind)
            {
                case LifeObjectKind.GrassSource:
                    break;
                case LifeObjectKind.Bundle:case LifeObjectKind.Mat:
                    if(held){Drop();return;}
                    held=obj;held.State.held=true;held.State.slot="";held.State.sheltered=false;
                    LifeAudio.PlayAt(LifeAudioCue.Fiber, obj.transform.position);
                    owner.SaveAndNotify("拿起"+obj.Description+"。");break;
                case LifeObjectKind.Loom:
                    if(held && held.State.kind==LifeObjectKind.Bundle)
                    StartCraft();
                    else owner.SaveAndNotify("这个编垫点需要一束草。");break;
                case LifeObjectKind.Bowl:
                    if(held)
                    {
                        if(obj.State.water<.12f){owner.SaveAndNotify("碗里的水不够。");return;}
                        held.State.wetness=Mathf.Clamp01(held.State.wetness+.75f);obj.State.water=Mathf.Max(0,obj.State.water-.1f);held.State.revision++;
                        LifeAudio.PlayAt(LifeAudioCue.Water, obj.transform.position);
                        owner.SaveAndNotify("浸湿了。");
                    }
                    else if(shift){if(obj.State.water>0)LifeAudio.PlayAt(LifeAudioCue.Water,obj.transform.position);obj.State.water=0;owner.SaveAndNotify("水碗已倒空。");}
                    else if(obj.State.water>.2f)
                    {
                        if(!BeginDrink(obj))owner.SaveAndNotify("碗里留着水。");
                    }
                    else owner.SaveAndNotify(obj.Description+"。");
                    obj.State.revision++;break;
                case LifeObjectKind.Rest:
                    if(held && held.State.kind==LifeObjectKind.Mat)
                    {
                        if(objects.Values.Any(o=>o!=held && o.State.slot==obj.State.id)){owner.SaveAndNotify("这里已经有垫子，先用 Shift+F 收回。");return;}
                        held.State.held=false;held.State.slot=obj.State.id;held.State.sheltered=obj.State.sheltered;
                        held.transform.position=obj.transform.position+Vector3.up*.045f;held.State.position=held.transform.position;
                        held.State.revision++;held.State.actionRevision++;obj.State.revision++;obj.State.actionRevision++;held=null;
                        if(obj.State.id=="squirrel.nest" && DryMatAt(obj.State.id) && bindings.TryGetValue("squirrel",out Binding squirrel) &&
                            squirrel.wildlife && Flat(Player.position,squirrel.speaker.transform.position)<4)
                            squirrel.wildlife.RequestLifeAction(Clip("SquirrelAccept"),2);
                        owner.SaveAndNotify("垫子已铺好。");
                        LifeAudio.PlayAt(LifeAudioCue.Contact, obj.transform.position);
                    }
                    else if(!held && shift)
                    {
                        held=objects.Values.FirstOrDefault(o=>o.State.slot==obj.State.id);
                        if(held){held.State.held=true;held.State.slot="";held.State.sheltered=false;obj.State.revision++;LifeAudio.PlayAt(LifeAudioCue.Fiber,obj.transform.position);owner.SaveAndNotify("取回草垫。");}
                    }
                    else if(obj.State.id==commonRestId || accepted.Values.Any(a=>Array.Find(requests,r=>r.id==a.id)?.condition=="common_rest") &&
                        bindings.TryGetValue("squirrel",out Binding host) && Flat(Player.position,host.speaker.transform.position)<4.5f)InviteCommon(obj.State.id);
                    else InviteAt(obj);
                    break;
                default:
                    if(held)Drop();
                    else{obj.State.revision++;owner.SaveAndNotify(obj.Description+"。");}
                    break;
            }
        }
        void StartCraft()
        {
            craftingId=held.State.id;craftStart=Player.position;craftEnd=Time.time+1.6f;
            owner.SaveAndNotify("开始把草束编成一张垫。");
        }
        void FinishCraft()
        {
            LifeObjectState state=held.State;state.kind=LifeObjectKind.Mat;state.revision++;
            GameObject old=held.gameObject;objects.Remove(state.id);interactables.Remove(held);held=null;Destroy(old);
            held=CreateItem(state);craftingId=null;owner.SaveAndNotify("编成了一张草垫。");
            LifeAudio.PlayAt(LifeAudioCue.Contact, Player.position);
        }
        void RemoveHeld()
        {
            if(!held)return;objects.Remove(held.State.id);interactables.Remove(held);Destroy(held.gameObject);held=null;
        }
        void Drop()
        {
            if(!held)return;
            Transform visual=owner.Gameplay.PlayerVisualRoot;
            Vector3 direction=visual?visual.forward:Player.forward;
            Vector3 at=Near(Player.position,direction*1.15f);
            held.State.held=false;held.State.slot="";held.State.sheltered=false;
            held.transform.position=at;held.State.position=at;held.State.revision++;held.State.actionRevision++;
            if(held.State.kind==LifeObjectKind.Mat)
            {
                string siteId="rest."+held.State.id;
                LifeObject site=ObjectAt(siteId);
                if(!site)site=Station(siteId,"草垫休息处",LifeObjectKind.Rest,at);
                site.State.position=site.transform.position=at;site.State.actionRevision++;
                LifeObject environment=objects.Values.Where(o=>o && o!=site && o.State.kind==LifeObjectKind.Rest && Flat(o.transform.position,at)<3)
                    .OrderBy(o=>Flat(o.transform.position,at)).FirstOrDefault();
                site.State.temperature=environment?environment.State.temperature:18;
                site.State.sheltered=HasExistingRoof(at);
                held.State.slot=siteId;held.State.sheltered=site.State.sheltered;
            }
            held=null;
            LifeAudio.PlayAt(LifeAudioCue.Contact, at);
            owner.SaveAndNotify("放下了。");
        }
        void InviteAt(LifeObject place)
        {
            Binding animal=null;float distance=4.5f;
            foreach(Binding candidate in bindings.Values)
            {
                if(!candidate.speaker || !requests.Any(r=>r.speakerKey==candidate.speaker.key && (r.condition=="dry_mat"||r.condition=="warm_rest"||r.condition=="animal_arrived")))continue;
                float d=Flat(Player.position,candidate.speaker.transform.position);if(d<distance){distance=d;animal=candidate;}
            }
            if(animal!=null)Invite(animal.speaker.key,place.State.id);
            else owner.SaveAndNotify("附近没有要歇脚的动物。");
        }
        void Invite(string key,string target)
        {
            if(!bindings.TryGetValue(key,out Binding binding) || !Player)return;
            if(Flat(Player.position,binding.speaker.transform.position)>4.5f || Flat(Player.position,AnchorPosition(target))>3)
            {owner.SaveAndNotify("走近动物和休息位再邀请。");return;}
            bool rest=objects.TryGetValue(target,out LifeObject place)&&place.State.kind==LifeObjectKind.Rest;
            if(rest && target!="fox.home" && (!DryMatAt(target)||!place.State.IsWarm))
            {owner.SaveAndNotify(Known(key=="squirrel"?"squirrel.mat":key+".warm")?"需要干垫和暖处。":"先和它聊聊。");return;}
            Vector3 destination=AnchorPosition(target);
            Drink pending=drinks.FirstOrDefault(d=>d.animal==binding);
            bool ok=pending!=null || (binding.wildlife?binding.wildlife.RequestLifeTarget(destination,1.5f,rest):binding.herd && binding.herd.RequestLifeTarget(binding.member,destination,1.5f));
            if(!ok){owner.SaveAndNotify("路被挡住了，换个位置再邀请。");return;}
            // A new invitation changes where it goes after its sip, not the approach already in progress.
            if(pending!=null){pending.returnTarget=destination;pending.returnRest=rest;}
            else {binding.target=destination;binding.rest=rest;}
            if(place){place.State.revision++;place.State.actionRevision++;}
            foreach(Accepted a in accepted.Values)
            {LifeRequest r=Array.Find(requests,q=>q.id==a.id);if(r!=null&&r.speakerKey==key&&r.targetId==target)a.stage=1;}
            owner.SaveAndNotify(pending!=null?"它喝完水就过来。":"它正走过来。");
        }
        void InviteCommon(string target=null)
        {
            target=target??commonRestId;
            if(!Player || Flat(Player.position,AnchorPosition(target))>3)return;
            if(!DryMatAt(target) || CommonWater(target)<.3f)
            {owner.SaveAndNotify(Known("squirrel.sequence")?"需要碗中有水、草垫干燥。":"先和松鼠聊聊。");return;}
            if(target!=commonRestId)
            {
                foreach(WildlifeAgent previous in invited)if(previous)previous.ReleaseLifeTarget();
                invited.Clear();invitedTargets.Clear();commonRestId=target;
            }
            Invite("squirrel",target);
            if(invited.Count==0)
            {
                bindings.TryGetValue("companion",out Binding companion);
                var nearby=secondary.Where(a=>a).OrderBy(a=>a.speciesId=="squirrel"?0:1).ThenBy(a=>Flat(a.transform.position,Player.position));
                var candidates=companion!=null && companion.wildlife?new[]{companion.wildlife}.Concat(nearby):nearby;
                foreach(WildlifeAgent animal in candidates.Where(a=>a && Flat(a.transform.position,Player.position)<LifeSession.CastRadius))
                {
                    Vector3 at=AnchorPosition("squirrel.common.companion");
                    if(target!="squirrel.common")
                    {
                        if(companion==null || animal!=companion.wildlife)continue;
                        bool found=false;
                        foreach(Vector3 next in Around(AnchorPosition(target),1.3f,1.6f))
                            if(LevelPoint(companion,next,out Vector3 point) && Route(companion,animal.transform.position,point))
                            {at=point;found=true;break;}
                        if(!found)continue;
                    }
                    if(!animal.RequestLifeTarget(at,1.5f,true))continue;
                    invited.Add(animal);invitedTargets.Add(at);
                    if(companion!=null && animal==companion.wildlife){companion.target=at;companion.rest=true;}
                    break;
                }
            }
            objects[target].State.revision++;
            owner.SaveAndNotify(drinks.Any(d=>d.returnRest && Flat(d.returnTarget,AnchorPosition(target))<.1f)?"喝完水后，它会回来歇脚。":
                invited.Count>0?"同伴正走来歇脚。":"同伴还没走近，稍后再邀请。");
        }
        bool Known(string request)=>owner && owner.Progress.understoodRequests.Contains(request);
    }
}
