// RunLocal covers pure story rules; Run additionally exercises Unity's real JSON and client envelope parser.
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using WeatherHouse.Hillside;

public static class AnimalStoryPlanChecks
{
    [Serializable] sealed class Message { public string role,content; }
    [Serializable] sealed class Choice { public string finish_reason; public Message message; }
    [Serializable] sealed class Response { public Choice[] choices; }
    [Serializable] sealed class Payload { public string model; public int max_tokens; public Message[] messages; }
    static void Require(bool value,string message){if(!value)throw new InvalidOperationException(message);}
    static AnimalObservation Observation()=>new AnimalObservation
    {
        animal="story-protocol-animal",species="sheep",revision=7,current="resume",canShareStory=true,
        allowedWishes=AnimalStoryRules.WishIds,
        character=AnimalStoryState.Create("story-protocol-animal","sheep",true,.2f,.7f,true),
        options=new[]{new AnimalOption{id="resume",action="resume"},new AnimalOption{id="watch_sea",action="watch_sea"}},
        vocabulary="self you hello goodbye here there sheep like look sea past want reject go come together".Split(' ')
            .Select(id=>new AnimalStoryWord{id=id,meaning=id}).ToArray()
    };
    static AnimalStorySpeech Speech(string intent,string subject,string[] glyphs,string emotion="",string evidence="")=>
        new AnimalStorySpeech{intent=intent,subject=subject,glyphs=glyphs,emotion=emotion,evidenceId=evidence};
    static AnimalStorySpeech Like()=>Speech("preference","sea",new[]{"self","like","look","sea"},"heart");
    static AnimalPlan Plan(AnimalObservation observation,AnimalStorySpeech speech=null,AnimalStoryReflection reflection=null)=>
        new AnimalPlan{revision=observation.revision,choices=new[]{"resume"},speech=speech,reflection=reflection};
    static string Complete(AnimalStoryState state,bool accompanied)
    {
        string id=state.Begin("watch_sea","harbor.tide");state.Arrive(id);
        for(int i=0;i<8;i++)state.Observe(id,1,true,accompanied);
        Require(state.Complete(id),"Fixture could not complete its actual local activity");return id;
    }
    static void Reject(AnimalStorySpeech speech,AnimalObservation observation,string label)=>
        Require(!AnimalStoryRules.ValidateSpeech(speech,observation),"Accepted invalid speech: "+label);

    public static string[] RunLocal()
    {
        var lines=new List<string>();var observation=Observation();
        string solo=Complete(observation.character,false);
        Require(AnimalStoryRules.ValidateSpeech(Speech("memory","sea",new[]{"self","past","look","sea"},evidence:solo),observation),
            "Real solo completed memory was rejected");
        Reject(Speech("memory","sea",new[]{"you","past","look","sea"},evidence:solo),observation,"solo memory invents player participation");
        Reject(Speech("memory","sea",new[]{"self","past","together","sea"},evidence:solo),observation,"solo memory invents together");
        string together=Complete(observation.character,true);
        Require(AnimalStoryRules.ValidateSpeech(Speech("memory","sea",new[]{"self","you","past","together","look","sea"},evidence:together),observation),
            "Actual companion evidence did not permit player/together memory");
        lines.Add("PASS completed solo memory rejects you/together; actual companion evidence permits both");

        string unfinished=observation.character.Begin("watch_sea","harbor.tide");observation.character.Arrive(unfinished);
        observation.character.Observe(unfinished,7,true,true);
        Reject(Speech("memory","sea",new[]{"past","sea"},evidence:unfinished),observation,"unfinished active episode");
        observation.character.Interrupt(unfinished,"rain");
        Reject(Speech("memory","sea",new[]{"past","sea"},evidence:unfinished),observation,"interrupted episode");
        Reject(Speech("memory","sea",new[]{"past","sea"},evidence:"invented"),observation,"nonexistent event");
        string seen=observation.character.RecordEncounter("listened");
        Reject(Speech("memory","sea",new[]{"past","sea"},evidence:seen),observation,"encounter is not completed sea activity");
        Reject(Speech("memory","sea",new[]{"look","sea"},evidence:solo),observation,"missing past token");
        lines.Add("PASS incomplete, interrupted, encounter-only and nonexistent events cannot become completed sea memories");

        Require(AnimalStoryRules.ValidateSpeech(Like(),observation),"Valid like/heart pair was rejected");
        Reject(Speech("preference","sea",new[]{"you","like","sea"},"heart"),observation,"claims the player's preference for sea");
        Reject(Speech("preference","sea",new[]{"self","you","like","sea"},"heart"),observation,"adds player preference to the animal's preference");
        Reject(Speech("preference","sea",new[]{"self","sea"},"heart"),observation,"heart without like");
        Reject(Speech("memory","sea",new[]{"past","sea"},"heart",solo),observation,"heart on memory");
        Reject(Speech("preference","sea",new[]{"like","sea"},"cry"),observation,"unsupported emotion");
        var affection=Speech("affection","player",new[]{"self","like","you"},"heart");
        Require(AnimalStoryRules.ValidateSpeech(affection,observation),"Completed companionship did not allow affection");
        Reject(affection,Observation(),"affection without an established relationship");
        Reject(Speech("preference","player",new[]{"like","you"},"heart"),Observation(),"preference absent from character card");
        Reject(Speech("preference","sea",new[]{"like","sea"},evidence:solo),observation,"evidence attached to non-memory speech");
        lines.Add("PASS like and heart form one valid expression; unsupported pairs and unsupported affection/preferences are rejected");

        Reject(Speech("invent","sea",new[]{"like","sea"}),observation,"unsupported intent");
        Reject(Speech("preference","river",new[]{"like","sea"}),observation,"unsupported subject");
        Reject(Speech("preference","sea",new[]{"like","sea","unknown"}),observation,"unknown glyph");
        Reject(Speech("preference","sea",Array.Empty<string>()),observation,"empty glyph list");
        Reject(Speech("preference","sea",null),observation,"null glyph list");
        Reject(Speech("preference","sea",new[]{"like","sea",null}),observation,"null glyph");
        string[] nine={"self","hello","goodbye","here","there","sheep","look","like","sea"};
        Require(AnimalStoryRules.ValidateSpeech(Speech("preference","sea",nine),observation),"Maximum nine-glyph expression was rejected");
        Reject(Speech("preference","sea",nine.Concat(new[]{"sea"}).ToArray()),observation,"more than nine glyphs");
        observation.vocabulary=observation.vocabulary.Where(word=>word.id!="like").ToArray();
        Reject(Like(),observation,"known protocol glyph absent from supplied vocabulary");
        observation=Observation();observation.canShareStory=false;Reject(Like(),observation,"sharing currently unavailable");
        lines.Add("PASS vocabulary, subject/intent/emotion allowlists, nine-glyph limit and canShareStory gate are enforced");

        observation=Observation();var wish=Speech("wish","sea",new[]{"want","look","sea"});
        Require(AnimalStoryRules.ValidateSpeech(wish,observation),"Current executable sea wish was rejected");
        observation.raining=true;Reject(wish,observation,"sea wish during rain");
        observation.raining=false;observation.options=new[]{new AnimalOption{id="resume",action="resume"}};
        Reject(wish,observation,"sea wish unavailable in current abilities");
        lines.Add("PASS wishes refer only to a currently available dry-weather sea activity");

        var card=Observation().character;string evidence=card.RecordEncounter("listened");float before=card.preferences[0].value;
        var update=new AnimalStoryReflection{evidenceIds=new[]{"invented"},preferenceId="sea",preferenceDelta=.1f,wish="watch_sea"};
        Require(!card.TryApplyReflection(update,AnimalStoryRules.PreferenceIds,AnimalStoryRules.WishIds) && card.preferences[0].value==before,
            "Invented reflection evidence changed the character");
        update.evidenceIds=new[]{evidence};update.preferenceDelta=.16f;
        Require(!card.TryApplyReflection(update,AnimalStoryRules.PreferenceIds,AnimalStoryRules.WishIds),"Reflection exceeded the bounded delta");
        update.preferenceDelta=.1f;
        Require(card.TryApplyReflection(update,AnimalStoryRules.PreferenceIds,AnimalStoryRules.WishIds) &&
            !card.TryApplyReflection(update,AnimalStoryRules.PreferenceIds,AnimalStoryRules.WishIds) && card.events.Count==1 && !card.HasCompleted("watch_sea"),
            "Reflection failed, replayed evidence, or invented a completed activity");
        lines.Add("PASS bounded reflection needs fresh real evidence, cannot replay it, and never fabricates completion");
        return lines.ToArray();
    }

    public static string[] Run()
    {
        var lines=new List<string>(RunLocal());var observation=Observation();
        var legacy=new AnimalObservation{revision=7,options=observation.options};
        Require(AnimalPlan.TryRead("{\"revision\":7,\"choices\":[\"resume\"]}",legacy,out _),"Legacy plan without story became incompatible");
        Require(!AnimalPlan.TryRead("{\"revision\":6,\"choices\":[\"resume\"]}",legacy,out _) &&
            !AnimalPlan.TryRead("{\"revision\":7,\"choices\":[\"teleport\"]}",legacy,out _) &&
            !AnimalPlan.TryRead("{\"revision\":7,\"choices\":[\"resume\",\"resume\"]}",legacy,out _),"Old revision or invalid movement choices were accepted");
        string paired=JsonUtility.ToJson(Plan(observation,Like()));
        Require(AnimalPlan.TryRead(paired,observation,out AnimalPlan accepted) && accepted.speech.emotion=="heart" && accepted.speech.glyphs.Contains("like"),
            "Same-response like/heart expression was not preserved by JSON parsing");
        observation.canShareStory=false;Require(!AnimalPlan.TryRead(paired,observation,out _),"JSON plan bypassed the live sharing gate");
        lines.Add("PASS JSON parser preserves like/heart atomically, keeps legacy plans, rejects stale revisions and invalid choices");

        observation=Observation();string id=observation.character.RecordEncounter("listened");
        var reflection=new AnimalStoryReflection{evidenceIds=new[]{id},preferenceId="sea",preferenceDelta=.1f,wish="watch_sea"};
        string reflected=JsonUtility.ToJson(Plan(observation,reflection:reflection));
        observation.allowedWishes=AnimalStoryRules.OrdinaryWishIds;
        Require(!AnimalPlan.TryRead(reflected,observation,out _),"Ordinary animal accepted the demo-only watch_sea reflection wish");
        observation.allowedWishes=null;
        Require(!AnimalPlan.TryRead(reflected,observation,out _),"Missing wish allowlist implicitly enabled the demo-only watch_sea activity");
        observation.allowedWishes=AnimalStoryRules.WishIds;
        float preference=observation.character.preferences[0].value;
        Require(AnimalPlan.TryRead(reflected,observation,out _) && !observation.character.FindEvent(id).reflected &&
            observation.character.preferences[0].value==preference,"Validation consumed evidence or mutated the live character");
        Require(observation.character.TryApplyReflection(reflection,AnimalStoryRules.PreferenceIds,AnimalStoryRules.WishIds) &&
            !AnimalPlan.TryRead(reflected,observation,out _),"Parser accepted a replay after real reflection application");
        lines.Add("PASS parser validates reflection on a copy; ordinary animals cannot request demo-only watch_sea; applying once makes replay invalid");

        observation=Observation();string content=JsonUtility.ToJson(Plan(observation,Like()));
        var envelope=new Response{choices=new[]{new Choice{finish_reason="stop",message=new Message{role="assistant",content=content}}}};
        Require(AnimalPlannerClient.ReadResponse(JsonUtility.ToJson(envelope),observation)?.speech?.emotion=="heart","Client lost valid structured expression");
        envelope.choices[0].finish_reason="length";
        Require(AnimalPlannerClient.ReadResponse(JsonUtility.ToJson(envelope),observation)==null,"Client accepted a truncated response");
        var payload=JsonUtility.FromJson<Payload>(AnimalPlannerClient.Payload(observation,"fixture-model"));
        Require(payload.messages?.Length==2 && payload.max_tokens>256,"Story request lacks its structured context or response allowance");
        AnimalObservation sent=JsonUtility.FromJson<AnimalObservation>(payload.messages[1].content);
        Require(sent.character?.animalId==observation.character.animalId && sent.canShareStory && sent.vocabulary.Any(word=>word.id=="like"),
            "Request omitted character, vocabulary or live sharing permission");
        lines.Add("PASS client envelope rejects truncation and request carries the character, vocabulary and sharing gate without network access");
        return lines.ToArray();
    }
}
