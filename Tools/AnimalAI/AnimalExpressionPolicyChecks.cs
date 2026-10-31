using System;
using System.Collections.Generic;
using WeatherHouse.Hillside;
using Signals=WeatherHouse.Hillside.AnimalExpressionSignals;

public static class AnimalExpressionPolicyChecks
{
    static void Require(bool value,string message){if(!value)throw new InvalidOperationException(message);}
    static AnimalExpressionPolicy New(int seed,bool outgoing=true,float timidity=.6f,float social=.8f)
        =>new AnimalExpressionPolicy(seed,outgoing,timidity,social);
    static int SeedFor(Func<AnimalExpressionPolicy,bool> matches)
    {
        for(int seed=0;seed<4096;seed++)if(matches(New(seed)))return seed;
        throw new InvalidOperationException("No deterministic expression fixture seed found");
    }
    public static string[] Run()
    {
        var results=new List<string>();
        int heartSeed=SeedFor(p=>p.TryChoose(Signals.NearPlayer,0,false,out var e,out _) && e==AnimalEmotion.Heart);
        var held=New(heartSeed);
        Require(held.TryChoose(Signals.NearPlayer,0,false,out _,out _),"Fixture did not produce initial heart");
        for(int i=1;i<=10000;i++)Require(!held.TryChoose(Signals.NearPlayer,i,false,out _,out _),"Persistent state repeated an expression");
        results.Add("PASS 10000 held-state updates produce no repeat or per-frame random retry");

        var same=New(3);
        Require(same.TryChoose(Signals.Alert,0,false,out var chosen,out var urgent) && chosen==AnimalEmotion.Exclamation && urgent,"Alert edge missing");
        same.TryChoose(Signals.None,1,false,out _,out _);
        Require(!same.TryChoose(Signals.Alert,119.999f,true,out _,out _),"Urgent expression bypassed its 120s cooldown");
        same.TryChoose(Signals.None,120,false,out _,out _);
        Require(same.TryChoose(Signals.Alert,120,true,out _,out _),"Same expression did not recover at 120s");
        results.Add("PASS same expression waits at least 120s, including urgent alerts");

        int twoSeed=SeedFor(p=>
        {
            if(!p.TryChoose(Signals.NearPlayer,0,false,out _,out _))return false;
            p.TryChoose(Signals.None,p.OrdinaryCooldownSeconds,false,out _,out _);
            return p.TryChoose(Signals.Greet,p.OrdinaryCooldownSeconds+.001f,false,out _,out _);
        });
        var cooldown=New(twoSeed);float wait=cooldown.OrdinaryCooldownSeconds;
        Require(cooldown.TryChoose(Signals.NearPlayer,0,false,out _,out _),"Initial ordinary expression missing");
        cooldown.TryChoose(Signals.None,wait-.01f,false,out _,out _);
        Require(!cooldown.TryChoose(Signals.Greet,wait-.001f,false,out _,out _),"Different ordinary expression skipped individual cooldown");
        cooldown.TryChoose(Signals.None,wait,false,out _,out _);
        Require(cooldown.TryChoose(Signals.Greet,wait+.001f,false,out chosen,out urgent) && chosen==AnimalEmotion.Happy && !urgent,
            "Ordinary expression did not recover after individual cooldown");
        var ranges=new HashSet<float>();
        for(int seed=0;seed<1000;seed++)
        {
            float seconds=New(seed).OrdinaryCooldownSeconds;
            Require(seconds>=45 && seconds<=90,"Ordinary cooldown out of 45-90s range");ranges.Add(seconds);
        }
        Require(ranges.Count>900,"Individuals share a synchronized cooldown");
        results.Add("PASS different emotions share each individual's stable 45-90s cooldown; individuals are staggered");

        var priority=New(heartSeed);
        Require(priority.TryChoose(Signals.NearPlayer,0,false,out _,out _),"Priority fixture missing ordinary expression");
        Require(priority.TryChoose(Signals.NearPlayer|Signals.Alert,1,true,out chosen,out urgent) && urgent && chosen==AnimalEmotion.Exclamation,
            "New alert could not interrupt a busy ordinary expression or its cooldown");
        Require(!priority.TryChoose(Signals.Sleeping|Signals.Greet|Signals.Flee,200,true,out _,out _),"Sleep did not suppress expressions");
        var sleep=New(7);
        Require(!sleep.TryChoose(Signals.Sleeping|Signals.Alert,0,false,out _,out _),"Urgent alert overrode sleep");
        Require(!sleep.TryChoose(Signals.Alert,1,false,out _,out _),"Suppressed sleeping alert was replayed on wake");
        sleep.TryChoose(Signals.None,2,false,out _,out _);
        Require(sleep.TryChoose(Signals.Alert,3,true,out _,out urgent) && urgent,"A genuinely new post-sleep alert was lost");
        results.Add("PASS sleep suppresses even alerts, while a new awake alert interrupts ordinary playback and cooldown");

        var busy=New(heartSeed);
        Require(!busy.TryChoose(Signals.NearPlayer,0,true,out _,out _),"Ordinary expression interrupted busy playback");
        Require(!busy.TryChoose(Signals.NearPlayer,500,false,out _,out _),"Busy event was queued and replayed later");
        results.Add("PASS ordinary busy events are dropped rather than delayed or repeated");

        int outgoingHearts=0,shyHearts=0,timidSweat=0,boldSweat=0,socialHappy=0,quietHappy=0,calmMusic=0,tenseMusic=0;
        for(int seed=0;seed<1000;seed++)
        {
            var a=New(seed);var b=New(seed);
            Require(a.OrdinaryCooldownSeconds==b.OrdinaryCooldownSeconds,"Stable identity changed cooldown");
            var events=new[]{Signals.NearPlayer,Signals.Greet,Signals.Alert,Signals.Flee,Signals.QuietRest};
            for(int i=0;i<events.Length;i++)
            {
                a.TryChoose(Signals.None,i*200,false,out _,out _);b.TryChoose(Signals.None,i*200,false,out _,out _);
                bool x=a.TryChoose(events[i],i*200+1,false,out var ea,out var ua);
                bool y=b.TryChoose(events[i],i*200+1,false,out var eb,out var ub);
                Require(x==y && ea==eb && ua==ub,"Same identity and event sequence changed its behavior");
                Require(!x || ea!=AnimalEmotion.Cry && ea!=AnimalEmotion.Anger && ea!=AnimalEmotion.Question,"Reserved emotion automatically triggered");
            }
            if(New(seed,true).TryChoose(Signals.NearPlayer,0,false,out _,out _))outgoingHearts++;
            if(New(seed,false).TryChoose(Signals.NearPlayer,0,false,out _,out _))shyHearts++;
            if(New(seed,true,1).TryChoose(Signals.Flee,0,false,out _,out _))timidSweat++;
            if(New(seed,true,0).TryChoose(Signals.Flee,0,false,out _,out _))boldSweat++;
            if(New(seed,true,.6f,1).TryChoose(Signals.Greet,0,false,out _,out _))socialHappy++;
            if(New(seed,true,.6f,0).TryChoose(Signals.Greet,0,false,out _,out _))quietHappy++;
            if(New(seed,true,0).TryChoose(Signals.QuietRest,0,false,out _,out _))calmMusic++;
            if(New(seed,true,1).TryChoose(Signals.QuietRest,0,false,out _,out _))tenseMusic++;
        }
        Require(outgoingHearts>shyHearts+400 && timidSweat>boldSweat+400 && socialHappy>quietHappy+400 && calmMusic>tenseMusic+200,
            "Existing personality fields did not affect event probabilities");
        results.Add("PASS 1000 identities reproduce stable choices; outgoing/timidity/sociability affect event odds; reserved emotions stay manual");
        return results.ToArray();
    }
}
