using System;
using System.Collections.Generic;

namespace WeatherHouse.Hillside
{
    [Serializable] public sealed class AnimalStoryPreference
    {
        public string id;
        public float value;
    }

    [Serializable] public sealed class AnimalStoryEvent
    {
        public string id, activity, placeId, outcome, reason;
        public float dwellSeconds, companionSeconds;
        public bool playerParticipated, playerLeftEarly, reflected;
    }

    [Serializable] public sealed class AnimalStoryEpisode
    {
        public string id, activity, placeId;
        public bool arrived, playerParticipated, playerLeftEarly;
        public float dwellSeconds, companionSeconds, nearSeconds, awaySeconds;
    }

    [Serializable] public sealed class AnimalStoryReflection
    {
        public string[] evidenceIds;
        public string preferenceId, wish;
        public float preferenceDelta;
    }

    // Only local execution writes the event ledger. Model output may update bounded preferences and intentions.
    [Serializable] public sealed class AnimalStoryState
    {
        public const int EventLimit=12;
        public const float RequiredDwellSeconds=8, ParticipationSeconds=3, EarlyDepartureSeconds=2;
        public string animalId, species, wish="";
        public bool outgoing;
        public float timidity, sociability, playerRelationship;
        public List<AnimalStoryPreference> preferences=new List<AnimalStoryPreference>();
        public List<AnimalStoryEvent> events=new List<AnimalStoryEvent>();
        public AnimalStoryEpisode active;
        public int nextEvent=1;

        public static AnimalStoryState Create(string animalId,string species,bool outgoing,float timidity,float sociability,bool likesSea=false)
        {
            if(string.IsNullOrWhiteSpace(animalId))throw new ArgumentException("A story needs a stable animal identity.",nameof(animalId));
            var state=new AnimalStoryState{animalId=animalId,species=species??"",outgoing=outgoing,
                timidity=Unit(timidity),sociability=Unit(sociability)};
            if(likesSea)state.preferences.Add(new AnimalStoryPreference{id="sea",value=.75f});
            return state;
        }

        public string Begin(string activity,string placeId)
        {
            if(string.IsNullOrWhiteSpace(animalId) || string.IsNullOrWhiteSpace(activity) || string.IsNullOrWhiteSpace(placeId))return null;
            if(active!=null)Interrupt(active.id,"replaced");
            active=new AnimalStoryEpisode{id=NewId(),activity=activity,placeId=placeId};
            return active.id;
        }

        public bool Arrive(string eventId)
        {
            if(!Matches(eventId))return false;
            active.arrived=true;return true;
        }

        public bool Observe(string eventId,float dt,bool atPlaceAndStopped,bool playerNear,bool playerStopped=true)
        {
            if(!Matches(eventId) || !Finite(dt) || dt<=0)return false;
            AnimalStoryEpisode episode=active;
            bool participating=episode.arrived && atPlaceAndStopped && playerNear && playerStopped;
            if(episode.arrived && atPlaceAndStopped)episode.dwellSeconds+=dt;
            if(participating)
            {
                episode.nearSeconds+=dt;episode.awaySeconds=0;
                if(episode.playerParticipated)episode.companionSeconds+=dt;
                else if(episode.nearSeconds>=ParticipationSeconds)
                {episode.playerParticipated=true;episode.companionSeconds+=episode.nearSeconds;}
            }
            else
            {
                episode.nearSeconds=0;
                // An animal being interrupted or walking away does not prove the player left it.
                if(episode.playerParticipated && episode.arrived && atPlaceAndStopped && !playerNear)
                {
                    episode.awaySeconds+=dt;
                    if(episode.awaySeconds>=EarlyDepartureSeconds)episode.playerLeftEarly=true;
                }
                else episode.awaySeconds=0;
            }
            return true;
        }

        public bool Complete(string eventId)
        {
            if(!Matches(eventId) || !active.arrived || active.dwellSeconds<RequiredDwellSeconds)return false;
            if(active.playerParticipated)playerRelationship=Unit(playerRelationship+.05f);
            return Finish(eventId,"completed","");
        }
        public bool Interrupt(string eventId,string reason)=>Finish(eventId,"interrupted",reason);
        public bool Block(string eventId,string reason)=>Finish(eventId,"blocked",reason);

        public string RecordEncounter(string kind)
        {
            if(string.IsNullOrWhiteSpace(animalId) || string.IsNullOrWhiteSpace(kind))return null;
            var evidence=new AnimalStoryEvent{id=NewId(),activity=kind,placeId="",outcome="observed",reason=""};
            Append(evidence);return evidence.id;
        }

        public AnimalStoryEvent FindEvent(string id)=>string.IsNullOrEmpty(id)?null:events?.Find(e=>e!=null && e.id==id);
        public bool HasCompleted(string activity)=>events!=null && events.Exists(e=>e!=null && e.activity==activity && e.outcome=="completed");

        public bool TryApplyReflection(AnimalStoryReflection update,string[] allowedPreferenceIds,string[] allowedWishIds)
        {
            if(update==null || update.evidenceIds==null || update.evidenceIds.Length<1 || update.evidenceIds.Length>3)return false;
            bool changesPreference=!string.IsNullOrEmpty(update.preferenceId),changesWish=!string.IsNullOrEmpty(update.wish);
            if(!changesPreference && !changesWish || !Finite(update.preferenceDelta) || Math.Abs(update.preferenceDelta)>.15f ||
                changesPreference && !Allowed(allowedPreferenceIds,update.preferenceId) || changesWish && !Allowed(allowedWishIds,update.wish) ||
                !changesPreference && update.preferenceDelta!=0)return false;
            var references=new List<AnimalStoryEvent>();bool fresh=false;
            foreach(string id in update.evidenceIds)
            {
                AnimalStoryEvent evidence=FindEvent(id);
                if(evidence==null || references.Contains(evidence))return false;
                references.Add(evidence);fresh|=!evidence.reflected;
            }
            if(!fresh)return false;
            if(changesPreference)
            {
                if(preferences==null)preferences=new List<AnimalStoryPreference>();
                AnimalStoryPreference preference=preferences.Find(p=>p!=null && p.id==update.preferenceId);
                if(preference==null){preference=new AnimalStoryPreference{id=update.preferenceId,value=.5f};preferences.Add(preference);}
                preference.value=Unit(preference.value+update.preferenceDelta);
            }
            if(changesWish)wish=update.wish;
            foreach(AnimalStoryEvent evidence in references)evidence.reflected=true;
            return true;
        }

        public void NormalizeAfterLoad()
        {
            if(preferences==null)preferences=new List<AnimalStoryPreference>();
            if(events==null)events=new List<AnimalStoryEvent>();
            preferences.RemoveAll(p=>p==null || string.IsNullOrEmpty(p.id));
            foreach(AnimalStoryPreference preference in preferences)preference.value=Unit(preference.value);
            events.RemoveAll(e=>e==null || string.IsNullOrEmpty(e.id));
            playerRelationship=Unit(playerRelationship);timidity=Unit(timidity);sociability=Unit(sociability);
            species=species??"";wish=wish??"";nextEvent=Math.Max(1,nextEvent);
            while(events.Count>EventLimit)events.RemoveAt(0);
            if(active!=null)
            {
                if(string.IsNullOrEmpty(active.id))active=null;
                else Interrupt(active.id,"reload");
            }
        }

        bool Finish(string eventId,string outcome,string reason)
        {
            if(!Matches(eventId))return false;
            AnimalStoryEpisode episode=active;active=null;
            Append(new AnimalStoryEvent{id=episode.id,activity=episode.activity,placeId=episode.placeId,outcome=outcome,reason=reason??"",
                dwellSeconds=episode.dwellSeconds,companionSeconds=episode.companionSeconds,
                playerParticipated=episode.playerParticipated,playerLeftEarly=episode.playerLeftEarly});
            return true;
        }
        void Append(AnimalStoryEvent evidence)
        {
            if(events==null)events=new List<AnimalStoryEvent>();
            events.Add(evidence);while(events.Count>EventLimit)events.RemoveAt(0);
        }
        string NewId()
        {
            nextEvent=Math.Max(1,nextEvent);
            string id;
            do{id=animalId+".story."+nextEvent++;}while(FindEvent(id)!=null || active!=null && active.id==id);
            return id;
        }
        bool Matches(string id)=>active!=null && !string.IsNullOrEmpty(id) && active.id==id;
        static bool Allowed(string[] ids,string id)=>ids!=null && Array.IndexOf(ids,id)>=0;
        static bool Finite(float value)=>!float.IsNaN(value) && !float.IsInfinity(value);
        static float Unit(float value)=>Finite(value)?Math.Max(0,Math.Min(1,value)):0;
    }
}
