using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using WeatherHousePrototype;

namespace WeatherHouse.Life.Editor
{
    public static class LifeCoreChecks
    {
        [MenuItem("Weather House/Life/Validate Core")]
        public static void Run()
        {
            var lines = new List<string>();
            Action<bool,string> check = (value,label) => { if (!value) throw new Exception(label); lines.Add("PASS " + label); };
            try
            {
                var request = new LifeRequest { id="water",eventId="E1",objective="接水",requiredForEvent=true };
                var progress = new LifeProgress();
                check(!progress.Complete(request.id,true,true),"Unaccepted help cannot complete from observation or preexisting weather");
                check(!progress.Hear(null,"a",true,true),"Missing request creates no task");
                check(!progress.Hear(request,"",true,true),"Missing animal creates no task");
                check(!progress.Hear(request,"a",false,true),"Stale utterance creates no task");
                check(!progress.Hear(request,"a",true,false),"Distant listening creates no task");
                check(progress.tasks.Count==0,"All invalid listening attempts preserve empty task list");
                check(progress.Hear(request,"a",true,true),"Current heard nearby request creates a task");
                check(!progress.Hear(request,"a",true,true) && progress.tasks.Count==1,"Repeated listening is idempotent");
                check(!progress.Complete(request.id,false,true),"World condition outside player circle cannot complete help");
                check(!progress.Complete(request.id,true,false),"Nearby player alone is insufficient");
                check(progress.Complete(request.id,true,true),"Accepted nearby world action completes help");
                check(!progress.Complete(request.id,true,true) && progress.friendlyAnimals.Count==1,"Relationship reward is granted once");
                var restored=JsonUtility.FromJson<LifeProgress>(JsonUtility.ToJson(progress)); restored.Normalize();
                check(restored.EventComplete("E1",new[]{request}) && restored.friendlyAnimals.SequenceEqual(new[]{"a"}),"Completed help and relationship survive save roundtrip");
                check(!restored.EventComplete("E7",new[]{request}),"Unknown or empty event is not automatically complete");
                var conversation=new LifeRequest{id="hello",eventId="E0",objective="问候",condition="conversation",requiredForEvent=true};
                check(!restored.Hear(conversation,"b",true,true) && restored.FindTask("hello")==null &&
                    restored.EventComplete("E0",new[]{conversation}),"Heard greetings progress conversation without creating HUD help tasks");
                var notebook=new LanguageNotebook(); notebook.Observe("a","one",new[]{"rain"});
                check(!notebook.ConfirmFromJudgement("rain","a",new[]{"rain"},true),"Legacy prototype verifier rejects a single-animal word");
                notebook.Observe("b","two",new[]{"rain"});
                check(notebook.ConfirmFromJudgement("rain","b",new[]{"rain"},true),"Legacy prototype cross-animal API remains compatible");
                check(!notebook.ConfirmFromJudgement("rain","b",new[]{"rain"},true),"Word confirmation reward is idempotent");
                var context=new LifeContextSnapshot {speakerId="a",listenerId="b",anchorPosition=new Vector3(2,3,4),sequenceId="first-rain-then-sun"};
                notebook.AddObservation(new ObservationRecord {id="photo",animalId="a",utteranceId="one",glyphIds=new[]{"rain"},context=context});
                context.listenerId="c";
                check(notebook.observations[0].context.listenerId=="b","Photo stores independent speaker/listener context");
                var reread=JsonUtility.FromJson<LanguageNotebook>(JsonUtility.ToJson(notebook));
                check(reread.observations[0].context.anchorPosition==new Vector3(2,3,4),"Photo anchor survives reload");
                check(LifeSession.InsideCircle(Vector3.zero,new Vector3(8,0,0),8),"Circle boundary is included");
                check(!LifeSession.InsideCircle(Vector3.zero,new Vector3(8.01f,0,0),8),"Remote location is excluded");
                check(!LifeSession.InsideCircle(Vector3.zero,new Vector3(0,5,0),8),"Other elevation is excluded");
                var content=LifeContent.Build();
                var catalog=JsonUtility.FromJson<GlyphCatalog>(Resources.Load<TextAsset>("WeatherHouseGlyphs").text);
                check(content.Select(r=>r.id).Distinct().Count()==content.Length,"Authored request IDs are unique");
                // The original 24 records retain their order and dialogue coverage. The seven
                // photography words are covered by the separate task/reward contract below.
                foreach(var glyph in catalog.glyphs.Take(24))
                    check(content.Where(r=>r.glyphIds.Contains(glyph.id)).Select(r=>r.speakerKey).Distinct().Count()>=2,"A/B animal coverage: "+glyph.id);
                check(content.SelectMany(r=>r.glyphIds).All(id=>catalog.glyphs.Any(g=>g.id==id)),"All phrases use real catalog IDs");
                foreach(var glyph in catalog.glyphs.Where(g=>g.id!="want" && g.id!="reject"))
                    check(LifePhotoRules.Tasks.Any(t=>t.rewards.Contains(glyph.id)),"Photography or interaction reward route: "+glyph.id);
                lines.Add("PASS core regression. This report does not establish live AI or Play visual acceptance.");
            }
            catch(Exception e) { lines.Add("FAIL "+e); throw; }
            finally { Directory.CreateDirectory("LifeGameplayReview/20261026"); File.WriteAllLines("LifeGameplayReview/20261026/core-checks.txt",lines); }
        }
        public static void RunBatch()
        {
            try { Run(); LanguageJudgementChecks.Run(); EditorApplication.Exit(0); }
            catch(Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
        }
    }
}
