using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace WeatherHouse.Life
{
    public sealed partial class LifeWorld
    {
        public event Action<LifePublicContext> PublicPhotoContext;
        readonly List<LifePublicContext> photoContexts=new List<LifePublicContext>();
        readonly Dictionary<string,float> photoContextUntil=new Dictionary<string,float>();
        readonly Dictionary<string,int> photoWalkStages=new Dictionary<string,int>();
        readonly Dictionary<string,LifePublicContext> pendingPhotoInteractions=new Dictionary<string,LifePublicContext>();
        readonly List<string> finishedPhotoInteractions=new List<string>();
        public IReadOnlyList<LifePublicContext> PublicPhotoContexts=>photoContexts;

        public void RestorePublicPhotoContexts(IEnumerable<LifePublicContext> history)
        {
            photoContexts.Clear();photoContextUntil.Clear();photoWalkStages.Clear();pendingPhotoInteractions.Clear();
            foreach(LifePublicContext context in history??Array.Empty<LifePublicContext>())
                if(context!=null && !string.IsNullOrEmpty(context.id) && !photoContexts.Any(c=>c.id==context.id))
                {LifePublicContext copy=context.Copy();photoContexts.Add(copy);if(IsPendingPhotoKind(copy.kind) && !copy.completed)pendingPhotoInteractions[copy.id]=copy;}
        }

        // Called only after the public utterance has actually been shown, never by AI judgement.
        public void ObservePhotoConversation(LifeRequest request)
        {
            if(request==null || !Player)return;
            LifeSpeaker speaker=FindSpeaker(request.speakerKey);
            if(!speaker || !speaker.Actor || Flat(Player.position,speaker.transform.position)>LifeSession.TalkRadius)return;
            bool Has(string id)=>Array.IndexOf(request.glyphIds??Array.Empty<string>(),id)>=0;
            LifePublicContext Make(string kind,string target,string description)=>new LifePublicContext{
                id=kind+":"+request.id+":"+speaker.Actor.animalId,kind=kind,actorId=speaker.Actor.animalId,
                listenerId=request.listenerKey=="player"?"player":FindSpeaker(request.listenerKey)?.Actor?.animalId,
                targetId=target,description=description,occurredUtc=DateTime.UtcNow.ToString("o"),
                anchor=AnchorPosition(target),origin=Player.position,glyphIds=(string[])request.glyphIds.Clone()};
            if(Has("hello"))PublishPhotoContext(Make("hello",request.targetId,"它看向听者，发出了这一句招呼。"),10);
            if(Has("goodbye"))PublishPhotoContext(Make("goodbye",request.targetId,"交谈告一段落，它向听者道别。"),10);
            if(Has("home"))
            {
                string target=request.speakerKey=="squirrel"?"squirrel.nest":request.speakerKey+".home";
                if(ObjectAt(target))PublishPhotoContext(Make("home",target,"它明确指认了自己的住处；住处的位置已留在记录里。"));
            }
            if(Has("cold") || Has("warm"))
            {
                LifeObject place=ObjectAt(request.targetId);
                if(place && place.State.kind==LifeObjectKind.Rest)
                {
                    string kind=place.State.IsWarm?"warm":"cold";
                    // A request for a warm bed is not evidence of already feeling warm there.
                    if(!place.State.IsWarm || AnimalAt(request.speakerKey,place.State.id))
                        PublishPhotoContext(Make(kind,place.State.id,place.State.IsWarm?"它在遮棚内舒展身体，表达了对这处休息位的感受。":"它指向风口或石面，表达了伸手靠近时的感受。"),20);
                    else PublishPhotoContext(Make("comfort-plan",place.State.id,"它指向附近的休息位，等待安顿。"));
                }
            }
            if(Has("self") && Has("you"))
            {
                PublishPhotoContext(Make("roles",request.targetId,"这一话轮已记录说话者和听者；可以再看双方的位置。"));
                LifePublicContext[] roles=photoContexts.Where(c=>c.kind=="roles").ToArray();
                if(roles.Any(a=>roles.Any(b=>a.actorId!=b.actorId && a.actorId==b.listenerId && a.listenerId==b.actorId)))
                    CompletePhotoInteraction("I01",roles.Last());
            }
            if(Has("come") && Has("here") && !Has("first"))PublishPhotoContext(Make("invitation",request.targetId,"它指向身旁的落脚点，邀请听者走过来。"));
            if(Has("go") && Has("tree") && !Has("first"))PublishPhotoContext(Make("tree-route",request.targetId,"它把目光与指向转向那棵树。"));
            if(Has("first") && Has("then"))
            {
                string kind=Has("rain")?"water-plan":request.condition=="walk_reverse"?"reverse-plan":"walk-plan";
                PublishPhotoContext(Make(kind,request.targetId,Has("rain")?"它展示了两步安排：接水，再让垫子晾干。":"它展示了两处落脚点的先后安排。"));
            }
        }
        void PublishPhotoContext(LifePublicContext context,float seconds=0)
        {
            // Looking back at the same invitation must not move its already-observed departure point.
            if(IsPendingPhotoKind(context.kind) && pendingPhotoInteractions.TryGetValue(context.id,out LifePublicContext pending))
            {context.origin=pending.origin;context.anchor=pending.anchor;}
            int prior=photoContexts.FindIndex(c=>c.id==context.id);
            if(prior>=0)photoContexts[prior]=context;else photoContexts.Add(context);
            if(IsPendingPhotoKind(context.kind) && !context.completed)pendingPhotoInteractions[context.id]=context;
            if(seconds>0)photoContextUntil[context.id]=Time.time+seconds;
            PublicPhotoContext?.Invoke(context.Copy());
        }
        static bool IsPendingPhotoKind(string kind)=>kind=="invitation" || kind=="tree-route" || kind=="water-plan" ||
            kind=="walk-plan" || kind=="reverse-plan" || kind=="comfort-plan";
        void CompletePhotoInteraction(string id,LifePublicContext source)
        {
            if(photoContexts.Any(c=>c.kind=="interaction" && c.targetId==id && c.completed))return;
            LifePublicContext completed=source.Copy();completed.id="interaction:"+id;completed.kind="interaction";completed.targetId=id;
            completed.completed=true;completed.occurredUtc=DateTime.UtcNow.ToString("o");
            LifePhotoTask task=LifePhotoRules.Tasks.First(t=>t.id==id);completed.glyphIds=(string[])task.tokens.Clone();
            completed.relatedContextIds=id=="I01"?photoContexts.Where(c=>c.kind=="roles").Select(c=>c.id).ToArray():new[]{source.id};
            completed.description=source.description+(id=="I01"?" 双方已经交换了说话者与听者的位置。":id=="I02"?" 你已经走到它邀请的落脚点。":
                id=="I03"?" 你已经走到它指向的那棵树旁。":source.kind=="water-plan"?" 碗里留下了水，旁边的垫子也已经干燥。":" 你已依照这次安排走完两处落脚点。");
            PublishPhotoContext(completed);
        }
        void UpdatePhotoInteractions()
        {
            if(!Player || pendingPhotoInteractions.Count==0)return;
            finishedPhotoInteractions.Clear();
            foreach(LifePublicContext context in pendingPhotoInteractions.Values)
            {
                if(context.completed){finishedPhotoInteractions.Add(context.id);continue;}
                bool moved=Flat(Player.position,context.origin)>.5f;
                if((context.kind=="invitation" || context.kind=="tree-route") && moved && Flat(Player.position,context.anchor)<1.15f &&
                    !invalidTargets.Contains(context.targetId??""))
                {context.completed=true;CompletePhotoInteraction(context.kind=="invitation"?"I02":"I03",context);}
                if(context.kind=="water-plan" && PhotoWaterPlanReady(context.anchor))
                {context.completed=true;CompletePhotoInteraction("I04",context);}
                if(context.kind=="walk-plan" || context.kind=="reverse-plan")
                {
                    bool reverse=context.kind=="reverse-plan";string first=reverse?"fox.tree":"fox.here",last=reverse?"fox.here":"fox.tree";
                    photoWalkStages.TryGetValue(context.id,out int stage);
                    if(stage==0 && moved && Flat(Player.position,AnchorPosition(first))<1.15f)photoWalkStages[context.id]=1;
                    else if(stage==1 && Flat(Player.position,AnchorPosition(last))<1.15f){context.completed=true;CompletePhotoInteraction("I04",context);}
                }
                if(context.kind=="comfort-plan")
                {
                    Binding binding=bindings.Values.FirstOrDefault(b=>b.speaker && b.speaker.Actor.animalId==context.actorId);
                    LifeObject place=ObjectAt(context.targetId);
                    if(binding!=null && place && place.State.IsWarm && DryMatAt(place.State.id) && AnimalAt(binding.speaker.key,place.State.id))
                    {
                        context.completed=true;LifePublicContext warm=context.Copy();warm.id="warm:"+context.actorId+":"+context.targetId;warm.kind="warm";
                        warm.description="安顿下来后，它舒展身体，表达了对这处休息位的感受。";warm.occurredUtc=DateTime.UtcNow.ToString("o");PublishPhotoContext(warm,30);
                    }
                }
                if(context.completed)finishedPhotoInteractions.Add(context.id);
            }
            foreach(string id in finishedPhotoInteractions){pendingPhotoInteractions.Remove(id);photoWalkStages.Remove(id);}
        }
        bool PhotoWaterPlanReady(Vector3 anchor)
        {
            return objects.Values.Any(mat=>mat && mat.State.kind==LifeObjectKind.Mat && !mat.State.held && mat.State.IsDry &&
                Flat(mat.transform.position,anchor)<8 && Flat(Player.position,mat.transform.position)<8 &&
                objects.Values.Any(bowl=>bowl && bowl.State.kind==LifeObjectKind.Bowl && bowl.State.water>=.3f &&
                    Flat(bowl.transform.position,mat.transform.position)<3.5f && Flat(Player.position,bowl.transform.position)<8));
        }
        internal LifePublicContext[] PhotoContextsAtShutter()
        {
            return photoContexts.Where(c=>c.kind=="home" ||
                (photoContextUntil.TryGetValue(c.id,out float until) && Time.time<=until && ContextStillPresent(c))).Select(c=>c.Copy()).ToArray();
        }
        bool ContextStillPresent(LifePublicContext context)
        {
            if(context.kind=="hello" || context.kind=="goodbye")return true;
            Binding animal=bindings.Values.FirstOrDefault(b=>b.speaker && b.speaker.Actor.animalId==context.actorId);
            LifeObject place=ObjectAt(context.targetId);
            return animal!=null && place && Flat(animal.speaker.transform.position,place.transform.position)<3 &&
                ((context.kind=="warm" && place.State.IsWarm) || (context.kind=="cold" && !place.State.IsWarm));
        }
    }
}
