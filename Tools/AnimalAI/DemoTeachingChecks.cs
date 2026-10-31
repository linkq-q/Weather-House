// Pure progression contracts; no scene, real save, or network is needed.
using System;
using System.Collections.Generic;
using System.Linq;
using WeatherHouse.Life;

public static class DemoTeachingChecks
{
    static void Require(bool ok,string message){if(!ok)throw new InvalidOperationException(message);}
    public static IEnumerable<string> Run()
    {
        LifeRequest[] content=LifeContent.Build();
        LifeRequest[] temperature=content.Where(request=>request.eventId=="E5").ToArray();
        Require(temperature.Length==4 && temperature.Any(request=>request.id=="sheep.warm" && request.HasTask) &&
            temperature.Any(request=>request.id=="fox.warm" && request.HasTask),"Original temperature task definitions were removed");
        var fresh=new LifeProgress();
        Require(temperature.All(request=>!fresh.CanPresent(request,content)),"A new save can still enter temperature teaching");
        Require(temperature.All(request=>!request.openForPhotography),"Deferred temperature lessons still advertise open photography");
        yield return "PASS all four temperature definitions remain but a fresh demo cannot enter them";
        var heard=new LifeProgress();heard.heardRequests.AddRange(temperature.Select(request=>request.id));
        Require(temperature.All(request=>!heard.CanPresent(request,content)) && heard.tasks.Count==0 && heard.heardRequests.Count==4,
            "Hearing an old temperature line reopened teaching or mutated save records");
        yield return "PASS heard-only temperature history stays saved without reopening deferred lessons";
        foreach(LifeRequest request in temperature.Where(request=>request.HasTask))
        {
            var legacy=new LifeProgress();
            var task=new LifeTask{requestId=request.id,eventId=request.eventId,animalId="legacy-animal",status=LifeTaskStatus.Active,acceptedUtcTicks=12345};
            legacy.tasks.Add(task);legacy.Normalize();
            Require(legacy.CanPresent(request,content) && ReferenceEquals(task,legacy.FindTask(request.id)) && task.acceptedUtcTicks==12345,
                "An already accepted temperature task was hidden or replaced");
            Require(!legacy.Complete(request.id,false,true) && !legacy.Complete(request.id,true,false),"Old task lost nearby/world-result guards");
            Require(legacy.Complete(request.id,true,true) && legacy.FindTask(request.id).status==LifeTaskStatus.Complete &&
                legacy.CanPresent(request,content) && !legacy.Complete(request.id,true,true) && legacy.tasks.Count==1 && legacy.friendlyAnimals.Count==1,
                "Old temperature task cannot complete once and remain recorded");
        }
        yield return "PASS both accepted temperature tasks remain presentable and finish once under the original completion guards";
        foreach(string id in new[]{"squirrel.sequence","fox.sequence","fox.reverse","sheep.strong","sheep.weak","deer.strong"})
            Require(fresh.CanPresent(content.Single(request=>request.id==id),content),"Deferred temperature teaching blocked "+id);
        yield return "PASS two-step and strength observation lessons remain available on a fresh save";
        var advanced=new LifeProgress();
        foreach(LifeRequest request in content.Where(request=>request.requiredForEvent && request.eventId!="E5" && request.eventId!="E7"))
        {
            advanced.heardRequests.Add(request.id);
            if(request.condition!="conversation")advanced.tasks.Add(new LifeTask{requestId=request.id,eventId=request.eventId,status=LifeTaskStatus.Complete});
        }
        Require(!advanced.EventComplete("E5",content),"Fixture accidentally completed the deferred event");
        Require(advanced.CanPresent(content.Single(request=>request.id=="sheep.wind"),content) &&
            advanced.CanPresent(content.Single(request=>request.id=="deer.weak"),content),"Strength help tasks still depend on deferred temperature teaching");
        var ordinaryEnding=new LifeRequest{id="ending-without-photography-bypass",eventId="E7",requiredForEvent=true,objective="test",condition="common_rest"};
        Require(advanced.CanPresent(ordinaryEnding,content),"Ending still requires the deferred E5 event");
        Require(advanced.GetProgressHint(content).Contains("河岛"),"Progress hint is stranded at deferred temperature teaching");
        yield return "PASS ordinary ending prerequisites and the next hint skip E5 without falsely completing it";
        Require(LifePhotoRules.Tasks.Count==26 && LifePhotoRules.Tasks.Any(task=>task.id=="P18") && LifePhotoRules.Tasks.Any(task=>task.id=="P19"),
            "Full photo catalog lost old temperature definitions");
        Require(LifePhotoRules.VisibleTasks.Count==24 && LifePhotoRules.VisibleTasks.All(task=>task.id!="P18" && task.id!="P19"),
            "Demo teaching pages still show deferred photo tasks");
        foreach(string id in new[]{"P01","P02","P12","P17","P15","P16","I04"})
            Require(LifePhotoRules.VisibleTasks.Any(task=>task.id==id),"Species, strength, or sequence task was hidden: "+id);
        Require(LifePhotoRules.VisibleTasks.All(task=>ReferenceEquals(task,LifePhotoRules.Tasks.Single(original=>original.id==task.id))),
            "Teaching view cloned or replaced original task records");
        yield return "PASS demo shows 24 photo tasks while all 26 definitions and species/strength/sequence entries remain intact";
    }
}
