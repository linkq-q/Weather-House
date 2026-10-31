using System;
using System.Collections.Generic;
using UnityEngine;
using WeatherHouse.Hillside;

public static class AnimalStoryStateChecks
{
    static void Require(bool value,string message){if(!value)throw new InvalidOperationException(message);}
    static AnimalStoryState New(bool sea=true)=>AnimalStoryState.Create("life.harbor.sheep","sheep",true,.15f,.7f,sea);
    static string Arrived(AnimalStoryState state)
    {
        string id=state.Begin("watch_sea","harbor.sheep.here");Require(state.Arrive(id),"Arrival failed");return id;
    }
    static void Seconds(AnimalStoryState state,string id,int seconds,bool near,bool stopped=true)
    {for(int i=0;i<seconds;i++)Require(state.Observe(id,1,stopped,near),"Observation rejected");}

    public static string[] RunLocal()
    {
        var result=new List<string>();
        var alone=New();string id=Arrived(alone);
        Seconds(alone,id,7,false);Require(!alone.Complete(id),"Sea activity completed before eight seconds");
        Seconds(alone,id,1,false);Require(alone.Complete(id),"Alone sea activity did not complete");
        var e=alone.FindEvent(id);Require(e.outcome=="completed" && e.dwellSeconds==8 && !e.playerParticipated &&
            !e.playerLeftEarly && e.companionSeconds==0 && alone.playerRelationship==0,"Alone activity invented companionship");
        Require(alone.HasCompleted("watch_sea") && !alone.Complete(id),"Completion was not recorded exactly once");
        result.Add("PASS solo watching needs eight actual stationary seconds and never invents companionship");

        var together=New();id=Arrived(together);
        Seconds(together,id,2,true);Require(!together.active.playerParticipated,"Brief proximity counted as participation");
        Seconds(together,id,1,true);Require(together.active.playerParticipated,"Three continuous seconds did not qualify participation");
        Seconds(together,id,5,true);Require(together.Complete(id),"Shared activity did not complete");
        e=together.FindEvent(id);Require(e.companionSeconds==8 && e.playerParticipated && !e.playerLeftEarly && together.playerRelationship>0,
            "Real completed companionship was not retained");
        result.Add("PASS three continuous near-and-stopped seconds qualify a shared activity and completion increases relationship");

        var left=New();id=Arrived(left);Seconds(left,id,3,true);Seconds(left,id,1,false);
        Require(!left.active.playerLeftEarly,"One second away counted as early departure");
        Seconds(left,id,1,false);Require(left.active.playerLeftEarly,"Two seconds away did not record departure");
        Seconds(left,id,3,false);Require(left.Complete(id),"Independent continuation failed");
        e=left.FindEvent(id);Require(e.playerParticipated && e.playerLeftEarly && e.companionSeconds==3,"Departure history was conflated with alone activity");
        var passing=New();id=Arrived(passing);Seconds(passing,id,2,true);Seconds(passing,id,1,false);Seconds(passing,id,2,true);Seconds(passing,id,3,false);
        Require(passing.Complete(id) && !passing.FindEvent(id).playerParticipated && !passing.FindEvent(id).playerLeftEarly,
            "Several short visits combined into continuous participation or early departure");
        result.Add("PASS departure after joining is distinct from solitude; short repeated visits do not claim together or departure");

        var movingPlayer=New();id=Arrived(movingPlayer);Seconds(movingPlayer,id,3,true);
        for(int i=0;i<3;i++)movingPlayer.Observe(id,1,true,true,false);
        Require(!movingPlayer.active.playerLeftEarly && movingPlayer.active.companionSeconds==3,
            "Walking beside the animal counted as departure or stationary shared activity");
        Seconds(movingPlayer,id,2,false);Require(movingPlayer.active.playerLeftEarly,"Actual departure was lost after nearby walking");
        var interruptedPresence=New();id=Arrived(interruptedPresence);Seconds(interruptedPresence,id,2,true);
        interruptedPresence.Observe(id,1,false,true);Seconds(interruptedPresence,id,2,true);
        Require(!interruptedPresence.active.playerParticipated,"Separate near intervals survived an animal movement gap");
        interruptedPresence.Observe(id,1,true,true,false);Seconds(interruptedPresence,id,2,true);
        Require(!interruptedPresence.active.playerParticipated,"Separate near intervals survived a player movement gap");
        result.Add("PASS nearby player movement is not departure; either animal or player movement resets continuous participation qualification");

        var blocked=New();id=blocked.Begin("watch_sea","harbor.tide");Seconds(blocked,id,20,true);
        Require(!blocked.Complete(id) && blocked.active.dwellSeconds==0,"An unarrived plan completed");
        blocked.Arrive(id);Seconds(blocked,id,20,true,false);
        Require(!blocked.Complete(id) && !blocked.active.playerParticipated,"Movement counted as watching or companionship");
        Require(blocked.Block(id,"route_blocked") && !blocked.HasCompleted("watch_sea") && blocked.FindEvent(id).outcome=="blocked",
            "Blocked intent was recorded as success");
        id=Arrived(blocked);Seconds(blocked,id,3,true);Seconds(blocked,id,3,false,false);
        Require(!blocked.active.playerLeftEarly,"Animal interruption was blamed on player departure");
        Require(blocked.Interrupt(id,"rain") && !blocked.HasCompleted("watch_sea"),"Interrupted activity became completed");
        Require(!blocked.Observe(id,1,true,true) && !blocked.Complete(id),"Closed episode remained active");
        result.Add("PASS arrival, stationary execution and completion are separate; blocked/interrupted plans never become successful memories");

        var reflection=New();id=reflection.RecordEncounter("listen");
        var update=new AnimalStoryReflection{evidenceIds=new[]{"invented"},preferenceId="sea",preferenceDelta=.1f,wish="watch_sea"};
        Require(!reflection.TryApplyReflection(update,new[]{"sea"},new[]{"watch_sea"}) && reflection.preferences[0].value==.75f && reflection.wish=="",
            "Nonexistent evidence changed the card");
        update.evidenceIds=new[]{id};update.preferenceDelta=.3f;
        Require(!reflection.TryApplyReflection(update,new[]{"sea"},new[]{"watch_sea"}),"Unbounded preference update accepted");
        update.preferenceDelta=.1f;update.wish="leave_island";
        Require(!reflection.TryApplyReflection(update,new[]{"sea"},new[]{"watch_sea"}),"Unsupported wish accepted");
        update.wish="watch_sea";
        Require(reflection.TryApplyReflection(update,new[]{"sea"},new[]{"watch_sea"}) && Math.Abs(reflection.preferences[0].value-.85f)<.0001f &&
            reflection.wish=="watch_sea" && reflection.events.Count==1 && reflection.FindEvent(id).outcome=="observed" && !reflection.HasCompleted("watch_sea"),
            "Valid reflection failed or fabricated completion");
        Require(!reflection.TryApplyReflection(update,new[]{"sea"},new[]{"watch_sea"}),"Same evidence was replayed to inflate preference");
        string second=reflection.RecordEncounter("listen");
        Require(reflection.TryApplyReflection(new AnimalStoryReflection{evidenceIds=new[]{second},wish="rest"},new[]{"sea"},new[]{"rest"}),"Wish-only reflection rejected");
        string third=reflection.RecordEncounter("listen");
        Require(reflection.TryApplyReflection(new AnimalStoryReflection{evidenceIds=new[]{third},preferenceId="sea",preferenceDelta=-.1f},new[]{"sea"},new[]{"rest"}) &&
            reflection.wish=="rest","Preference-only reflection changed wish or failed");
        result.Add("PASS only existing fresh evidence permits bounded reflection; unsupported/replayed updates are atomic rejects, and wish/preference may update independently");

        var regular=New(false);regular.NormalizeAfterLoad();Require(regular.preferences.Count==0,"Ordinary animal was seeded with sea preference");
        var retained=New();retained.preferences[0].value=.2f;id=Arrived(retained);Seconds(retained,id,7,true);
        retained.NormalizeAfterLoad();e=retained.FindEvent(id);
        Require(retained.active==null && e.outcome=="interrupted" && e.reason=="reload" && e.dwellSeconds==7 &&
            !retained.HasCompleted("watch_sea") && retained.preferences[0].value==.2f,"Reload reset preferences or advanced unfinished activity");
        retained.NormalizeAfterLoad();Require(retained.events.Count==1,"Repeated normalization duplicated reload interruption");
        for(int i=0;i<20;i++)retained.RecordEncounter("listen");
        Require(retained.events.Count==AnimalStoryState.EventLimit && retained.FindEvent(id)==null,"Event history is unbounded");
        var ids=new HashSet<string>();foreach(var item in retained.events)Require(ids.Add(item.id),"Stable event IDs collided");
        var empty=new AnimalStoryState{preferences=null,events=null};empty.NormalizeAfterLoad();
        Require(empty.preferences!=null && empty.events!=null && empty.preferences.Count==0,"Null-safe normalization invented a sea preference");
        result.Add("PASS loading interrupts pending activity without offline time; only explicit first creation seeds sea; ledger stays bounded and IDs unique");
        return result.ToArray();
    }

    public static string[] Run()
    {
        var result=new List<string>(RunLocal());
        var original=New();original.preferences[0].value=.35f;original.wish="rest";string heard=original.RecordEncounter("listen");
        string id=Arrived(original);Seconds(original,id,5,true);
        string json=JsonUtility.ToJson(original);var restored=JsonUtility.FromJson<AnimalStoryState>(json);restored.NormalizeAfterLoad();
        Require(restored.animalId==original.animalId && restored.species==original.species && restored.outgoing==original.outgoing &&
            restored.timidity==original.timidity && restored.sociability==original.sociability && restored.preferences[0].value==.35f && restored.wish=="rest" &&
            restored.FindEvent(heard)!=null && restored.active==null && restored.FindEvent(id).outcome=="interrupted" &&
            restored.FindEvent(id).companionSeconds==5 && !restored.HasCompleted("watch_sea"),"Unity JSON roundtrip lost identity/card/facts or completed offline activity");
        Require(restored.RecordEncounter("listen")!=id,"Reload reused prior event identity");
        result.Add("PASS Unity JsonUtility roundtrip preserves character and real history; pending activity becomes one explicit reload interruption");
        return result.ToArray();
    }
}
