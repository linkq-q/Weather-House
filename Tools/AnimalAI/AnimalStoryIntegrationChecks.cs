using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using WeatherHouse.Hillside;
using WeatherHouse.Life;
using WeatherHousePrototype;
using Object=UnityEngine.Object;

public static class AnimalStoryIntegrationChecks
{
    const BindingFlags Members=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
    static void Require(bool value,string message){if(!value)throw new InvalidOperationException(message);}
    static void Set(object owner,string field,object value)=>owner.GetType().GetField(field,Members).SetValue(owner,value);
    static object Get(object owner,string field)=>owner.GetType().GetField(field,Members).GetValue(owner);
    static object Call(object owner,string method,params object[] args)=>owner.GetType().GetMethod(method,Members).Invoke(owner,args);
    static void Guard()=>Require(Application.isBatchMode && !Application.isPlaying &&
        Application.dataPath.IndexOf("ValidationProject",StringComparison.OrdinalIgnoreCase)>=0,
        "Story integration checks require the independent ValidationProject batch editor");
    static LifeRequest Speech(string id)=>new LifeRequest{id=id,speakerKey="harborSheep",listenerKey="player",condition="conversation",
        glyphIds=new[]{"self","like","sea"}};

    public static string[] Run()
    {
        Guard();var results=new List<string>();
        CheckSpeaker(results);CheckIntent(results);CheckRestore(results);CheckRainInterrupt(results);
        return results.ToArray();
    }

    static void CheckSpeaker(List<string> results)
    {
        var root=new GameObject("Story speaker integration fixture");
        try
        {
            var speaker=root.AddComponent<LifeSpeaker>();var progress=new LifeProgress();
            var first=Speech("static.first");first.requiredForEvent=true;first.eventId="E0";
            var second=Speech("static.second");second.requiredForEvent=true;second.eventId="E0";
            var authored=new[]{first,second};speaker.requests=authored;speaker.ConfigureProgress(progress,authored);
            Require(ReferenceEquals(speaker.Current,first),"First authored request unavailable");
            int originalRevision=speaker.Revision;progress.heardRequests.Add(first.id);
            var dynamic=Speech("story.fixture");speaker.PublishStory(dynamic,8);
            Require(ReferenceEquals(speaker.Current,dynamic) && speaker.Revision>originalRevision && speaker.topicIndex==0,
                "Dynamic speech did not take temporary priority or altered authored topic");
            int dynamicRevision=speaker.Revision;
            for(int i=0;i<25;i++){_ = speaker.Current;speaker.AdvanceAfterHeard();}
            Require(speaker.topicIndex==0 && speaker.Revision==dynamicRevision && progress.tasks.Count==0 &&
                !progress.HasHeard(dynamic.id) && ReferenceEquals(speaker.requests,authored),
                "Reading dynamic speech advanced teaching, accepted a task, or replaced authored requests");
            speaker.StopStory();
            Require(speaker.Story==null && ReferenceEquals(speaker.Current,first) && speaker.topicIndex==0 && speaker.Revision>dynamicRevision,
                "Stopping dynamic speech failed to restore the same authored topic");
            speaker.AdvanceAfterHeard();Require(ReferenceEquals(speaker.Current,second) && speaker.topicIndex==1,
                "Original authored AdvanceAfterHeard stopped working after dynamic speech");
            speaker.PublishStory(Speech("story.expiry"),8);int expiryRevision=speaker.Revision;
            Set(speaker,"storyUntil",Time.time-.01f);
            Require(speaker.Story==null && ReferenceEquals(speaker.Current,second) && speaker.topicIndex==1 && speaker.Revision>expiryRevision,
                "Expired dynamic speech did not restore the original topic and utterance revision");
            speaker.PublishStory(Speech("story.next"),8);speaker.SelectNext();
            Require(speaker.Story==null && ReferenceEquals(speaker.Current,second) && speaker.topicIndex==1,
                "Dismissing a dynamic topic skipped the underlying authored topic");
            var forbidden=Speech("story.cannot.create.task");forbidden.objective="fixture";forbidden.condition="animal_arrived";
            speaker.PublishStory(forbidden,8);Require(speaker.Story==null && ReferenceEquals(speaker.Current,second),"Dynamic task request was accepted");
            forbidden=Speech("story.cannot.require.event");forbidden.requiredForEvent=true;
            speaker.PublishStory(forbidden,8);Require(speaker.Story==null && progress.tasks.Count==0,"Dynamic required event was accepted");
            Require(!progress.Hear(dynamic,"life.harbor.sheep",true,true) && progress.tasks.Count==0,
                "A conversation-only story could create a life task");
            results.Add("PASS real LifeSpeaker prioritizes dynamic speech temporarily, restores the exact topic on stop/expiry, and preserves authored advancement without tasks");
        }
        finally{Object.DestroyImmediate(root);}
    }

    static void CheckIntent(List<string> results)
    {
        var policy=new AnimalExpressionPolicy(43,true,.2f,.8f);
        Require(policy.TryIntent(AnimalEmotion.Heart,0,false),"First model heart was rejected");
        Require(!policy.TryIntent(AnimalEmotion.Happy,policy.OrdinaryCooldownSeconds-.001f,false),"Model intent bypassed ordinary cooldown");
        Require(policy.TryIntent(AnimalEmotion.Happy,policy.OrdinaryCooldownSeconds,false),"Different intent failed after ordinary cooldown");
        var same=new AnimalExpressionPolicy(43,true,.2f,.8f);
        Require(same.TryIntent(AnimalEmotion.Heart,0,false) && !same.TryIntent(AnimalEmotion.Heart,119.999f,false) &&
            same.TryIntent(AnimalEmotion.Heart,120,false),"Model hearts did not enforce the exact 120-second same-emotion boundary");
        var busy=new AnimalExpressionPolicy(43,true,.2f,.8f);
        Require(!busy.TryIntent(AnimalEmotion.Heart,0,true) && busy.TryIntent(AnimalEmotion.Heart,0,false),
            "Busy rejection either allowed interruption or consumed a heart cooldown");
        var sleeping=new AnimalExpressionPolicy(43,true,.2f,.8f);
        sleeping.TryChoose(AnimalExpressionSignals.Sleeping,0,false,out _,out _);
        Require(!sleeping.TryIntent(AnimalEmotion.Heart,0,true) && sleeping.TryIntent(AnimalEmotion.Heart,0,false),
            "Caller-provided sleep/busy gate failed or blocked a new awake intent");
        var localFirst=new AnimalExpressionPolicy(43,true,.2f,.8f);
        Require(localFirst.TryChoose(AnimalExpressionSignals.Alert,0,false,out _,out _) &&
            !localFirst.TryIntent(AnimalEmotion.Heart,1,false) && localFirst.TryIntent(AnimalEmotion.Heart,localFirst.OrdinaryCooldownSeconds,false),
            "Model intent used a separate cooldown from local events");
        results.Add("PASS model TryIntent shares local ordinary cooldown, respects 120-second heart cooldown, and busy/sleep gates reject without consuming cooldown");
    }

    static void CheckRestore(List<string> results)
    {
        const string legacy="{\"version\":1,\"tasks\":[{\"requestId\":\"legacy.task\",\"animalId\":\"legacy.animal\",\"status\":0,\"acceptedUtcTicks\":123}],\"learnedGestures\":[\"rain\"],\"understoodRequests\":[\"legacy.heard\"],\"worldJson\":\"keep-world\"}";
        var progress=JsonUtility.FromJson<LifeProgress>(legacy);progress.Normalize();
        Require(progress.animalStories!=null && progress.animalStories.Count==0 && progress.tasks.Count==1 &&
            progress.tasks[0].acceptedUtcTicks==123 && progress.tasks[0].status==LifeTaskStatus.Active && progress.learnedGestures.Count==1 &&
            progress.HasHeard("legacy.heard") && progress.worldJson=="keep-world","Legacy progress migration lost existing content or invented a character");
        var sheep=AnimalStoryState.Create("life.harbor.sheep","sheep",false,.15f,.6f,true);
        sheep.preferences[0].value=.3f;sheep.wish="rest";sheep.playerRelationship=.2f;
        string heard=sheep.RecordEncounter("listened"),episode=sheep.Begin("watch_sea","harbor.tide");sheep.Arrive(episode);
        for(int i=0;i<7;i++)sheep.Observe(episode,1,true,true);
        var deer=AnimalStoryState.Create("life.harbor.deer","deer",true,.25f,.4f);deer.RecordEncounter("listened");
        progress.animalStories.Add(sheep);progress.animalStories.Add(deer);
        var restored=JsonUtility.FromJson<LifeProgress>(JsonUtility.ToJson(progress));restored.Normalize();restored.Normalize();
        var saved=restored.animalStories.Find(s=>s.animalId==sheep.animalId);
        Require(restored.animalStories.Count==2 && saved!=null && saved.active==null && saved.preferences[0].value==.3f &&
            saved.wish=="rest" && saved.playerRelationship==.2f && saved.FindEvent(heard)!=null && saved.events.Count==2 &&
            saved.FindEvent(episode).outcome=="interrupted" && saved.FindEvent(episode).reason=="reload" &&
            saved.FindEvent(episode).dwellSeconds==7 && !saved.HasCompleted("watch_sea") &&
            restored.animalStories.Find(s=>s.animalId==deer.animalId).preferences.Count==0 && restored.tasks.Count==1,
            "Reload altered preferences/relationship, duplicated interruption, simulated offline completion, or mixed individual cards");

        TextAsset source=Resources.Load<TextAsset>("WeatherHouseGlyphs");Require(source,"Real glyph catalog was missing");
        var catalog=JsonUtility.FromJson<GlyphCatalog>(source.text);
        var notebook=JsonUtility.FromJson<LanguageNotebook>("{\"version\":2,\"words\":[{\"glyphId\":\"rain\",\"guess\":\"rain\",\"encountered\":true,\"confirmed\":true}]}");
        notebook.Normalize(catalog);
        foreach(string id in new[]{"like","look","sea","past"})
        {
            Require(Array.Exists(catalog.glyphs,g=>g.id==id),"New story word missing from actual glyph catalog: "+id);
            var word=notebook.GetWord(id);Require(word!=null && !word.encountered && !word.confirmed,"Migration silently learned new story word: "+id);
        }
        notebook.Observe(sheep.animalId,"story.visible",new[]{"like","sea"});
        Require(notebook.GetWord("like").encountered && !notebook.GetWord("like").confirmed &&
            notebook.GetWord("sea").encountered && !notebook.GetWord("sea").confirmed && notebook.GetWord("rain").confirmed &&
            notebook.GetWord("want").confirmed && notebook.GetWord("reject").confirmed && !notebook.GetWord("past").encountered,
            "Story observation confirmed words or changed unrelated existing knowledge");
        results.Add("PASS real LifeProgress/JsonUtility migration preserves independent cards and old tasks, interrupts one pending activity, and never auto-learns new story glyphs");
    }

    static void CheckRainInterrupt(List<string> results)
    {
        var root=new GameObject("Story rain interruption integration fixture");
        try
        {
            var autonomy=root.AddComponent<AnimalAutonomy>();var client=root.AddComponent<AnimalPlannerClient>();Set(autonomy,"client",client);
            var player=new GameObject("Fixture player").transform;player.SetParent(root.transform);player.position=Vector3.right*20;Set(autonomy,"player",player);
            var body=new GameObject("Fixture sheep");body.transform.SetParent(root.transform);var agent=body.AddComponent<WildlifeAgent>();
            var speaker=body.AddComponent<LifeSpeaker>();speaker.requests=new[]{Speech("static.rain.fixture")};speaker.PublishStory(Speech("story.rain.fixture"),8);
            var state=AnimalStoryState.Create("life.harbor.sheep","sheep",true,.15f,.6f,true);
            string id=state.Begin("watch_sea","harbor.tide");state.Arrive(id);for(int i=0;i<7;i++)state.Observe(id,1,true,true);
            object animal=Activator.CreateInstance(typeof(AnimalAutonomy).GetNestedType("Animal",BindingFlags.NonPublic),true);
            Set(animal,"agent",agent);Set(animal,"root",body.transform);Set(animal,"id",state.animalId);Set(animal,"species","sheep");
            Set(animal,"outgoing",true);Set(animal,"story",state);Set(animal,"speaker",speaker);Set(animal,"storyEpisode",id);
            Set(animal,"action","watch_sea");Set(animal,"until",float.PositiveInfinity);
            Set(animal,"nextShelter",float.PositiveInfinity);Set(animal,"nextAI",float.PositiveInfinity);
            Set(animal,"rainReaction",new AnimalRainReaction(20261026,6101,"sheep"));
            autonomy.Rain(body.transform.position,8,true,2);
            // Prevent starting a route in this fixture; the real Tick must close the episode before route planning.
            Set(animal,"shelterUrgent",true);((AnimalRainReaction)Get(animal,"rainReaction")).Observe(2);
            Require((bool)Call(autonomy,"Tick",animal),"Rain interruption did not finish its local tick");
            var evidence=state.FindEvent(id);
            Require(state.active==null && evidence!=null && evidence.outcome=="interrupted" && evidence.reason=="rain" &&
                evidence.dwellSeconds==7 && !state.HasCompleted("watch_sea") && speaker.Story==null && Get(animal,"storyEpisode")==null,
                "Real Tick counted rainy watching as complete or kept its active story bubble");
            results.Add("PASS actual AnimalAutonomy.Tick interrupts a seven-second episode and stops its speech immediately when strong rain is active");
        }
        finally{Object.DestroyImmediate(root);}
    }
}
