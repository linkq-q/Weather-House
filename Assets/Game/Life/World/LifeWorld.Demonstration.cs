using System;
using System.Collections.Generic;
using UnityEngine;
using WeatherHousePrototype;

namespace WeatherHouse.Life
{
    public sealed partial class LifeWorld
    {
        readonly Dictionary<string,string> shownTopics=new Dictionary<string,string>();
        string demonstrationKey,demonstrationRequest;
        LocalWeather demonstrationKind;
        int demonstrationStrength;
        float demonstrationStart,demonstrationEnd;
        WeatherRegion demonstrationWeather;
        LifeObject demonstrationRibbon;
        bool demonstrationWasActive,demonstrationRetreated;
        Vector3 demonstrationPreviousTarget;
        bool playerSkyActive,playerWindActive;
        public string DemonstrationDescription {get;private set;}="";
        public static string VisibleDemonstration(LocalWeather kind,int strength,bool active)=>kind==LocalWeather.Wind?
            (!active?"布条暂时垂下":strength==2?"布条大幅甩动":"布边轻轻摆动"):
            (!active?"空中暂时没有落水":strength==2?"密集水滴接连落下":"稀疏水滴落下");
        bool DemonstrationBlocked(LifeObject obj)
        {
            if(!obj)return false;
            string key=obj.State.id.Split('.')[0];LifeRequest request=FindSpeaker(key)?.Current;
            if(request==null || request.eventId!="E4")return false;
            return key=="sheep" || request.id=="deer.wind"?playerWindActive:playerSkyActive;
        }

        void UpdateDemonstrations()
        {
            foreach(var pair in bindings)
            {
                Binding b=pair.Value;LifeRequest r=b.speaker?b.speaker.Current:null;
                if(r==null || Flat(Player.position,b.speaker.transform.position)>LifeSession.TalkRadius)continue;
                string token=r.id+"/"+b.speaker.Revision;
                if(shownTopics.TryGetValue(pair.Key,out string last)&&last==token)continue;
                shownTopics[pair.Key]=token;
                if(r.eventId=="E4")BeginDemonstration(pair.Key,r);
                if(r.eventId=="E0" && (Array.IndexOf(r.glyphIds,"hello")>=0 || Array.IndexOf(r.glyphIds,"goodbye")>=0))
                {
                    if(b.wildlife)
                    {
                        b.wildlife.RequestLifeTarget(b.target,1.5f);
                        b.wildlife.FaceLifePoint(Player.position,1);
                        b.wildlife.RequestLifeAction(b.wildlife.clips.greet,1.8f);
                    }
                    if(b.herd)b.herd.RequestLifeGreeting(b.member);
                    if(pair.Key=="fox" && Array.IndexOf(r.glyphIds,"goodbye")>=0)b.target=AnchorPosition("fox.home");
                }
            }
            if(!demonstrationWeather || string.IsNullOrEmpty(demonstrationKey))return;
            LifeSpeaker speaker=FindSpeaker(demonstrationKey);
            if(Time.time>demonstrationEnd || !speaker || (demonstrationKind==LocalWeather.Wind?playerWindActive:playerSkyActive) ||
                speaker.Current?.id!=demonstrationRequest || Flat(Player.position,speaker.transform.position)>LifeSession.CastRadius)
            {StopDemonstration();return;}
            demonstrationWeather.transform.position=Player.position;
            float age=Time.time-demonstrationStart;
            // Every comparison uses the same eight-metre area and four-second active window.
            bool active=age>=1 && age<5;
            demonstrationWeather.SetLocalWeather(demonstrationKind,demonstrationStrength,active);
            if(demonstrationRibbon)demonstrationRibbon.SetDemonstrationWind(active?demonstrationStrength:0);
            if(active && !demonstrationWasActive && demonstrationStrength==2 && bindings.TryGetValue(demonstrationKey,out Binding binding))
            {
                if(demonstrationKind==LocalWeather.Wind && demonstrationKey=="sheep")
                {
                    demonstrationPreviousTarget=binding.target;binding.target=AnchorPosition("sheep.warm");demonstrationRetreated=true;
                }
                else if(binding.wildlife)binding.wildlife.RequestLifeAction(binding.wildlife.clips.alert,1.8f);
            }
            demonstrationWasActive=active;
            DemonstrationDescription=(active?(demonstrationStrength==2?"强":"弱"):"关闭")+(demonstrationKind==LocalWeather.Wind?"风":"雨")+" · 动物身边的自然天气对照";
        }
        void BeginDemonstration(string key,LifeRequest request)
        {
            StopDemonstration();
            if(!Player || !bindings.TryGetValue(key,out Binding binding) || Flat(binding.speaker.transform.position,Player.position)>LifeSession.CastRadius)return;
            demonstrationKey=key;demonstrationRequest=request.id;
            demonstrationKind=key=="sheep" || request.id=="deer.wind"?LocalWeather.Wind:LocalWeather.Rain;
            if(demonstrationKind==LocalWeather.Wind?playerWindActive:playerSkyActive){demonstrationKey=null;demonstrationRequest=null;return;}
            demonstrationStrength=Array.IndexOf(request.glyphIds,"strong")>=0 && Array.IndexOf(request.glyphIds,"weak")<0?2:1;
            if(!demonstrationWeather)
            {
                var go=new GameObject("可重播的近处自然天气对照");go.SetActive(false);go.transform.SetParent(transform);
                demonstrationWeather=go.AddComponent<WeatherRegion>();demonstrationWeather.localPlayerWeather=true;
                demonstrationWeather.radius=LifeSession.CastRadius;go.SetActive(true);
            }
            demonstrationStart=Time.time;demonstrationEnd=Time.time+6;
            demonstrationWasActive=false;
            demonstrationWeather.transform.position=Player.position;demonstrationWeather.SetLocalWeather(demonstrationKind,demonstrationStrength,false);
            if(demonstrationKind==LocalWeather.Wind && objects.TryGetValue(key+".ribbon",out LifeObject ribbon))demonstrationRibbon=ribbon;
            // This demonstration never calls ApplyWeather, increments a task revision, or awards help.
        }
        void StopDemonstration()
        {
            if(demonstrationRetreated && demonstrationKey!=null && bindings.TryGetValue(demonstrationKey,out Binding binding))binding.target=demonstrationPreviousTarget;
            demonstrationRetreated=false;
            if(demonstrationWeather)demonstrationWeather.SetLocalWeather(demonstrationKind,1,false);
            if(demonstrationRibbon)demonstrationRibbon.SetDemonstrationWind(-1);
            demonstrationRibbon=null;demonstrationKey=null;demonstrationRequest=null;demonstrationEnd=0;
            DemonstrationDescription="";
        }
        bool ReplayDemonstration(LifeObject obj)
        {
            if(!obj)return false;string key=obj.State.id.Split('.')[0];LifeSpeaker s=FindSpeaker(key);
            if(!s || s.Current==null || s.Current.eventId!="E4")return false;
            if(DemonstrationBlocked(obj))owner.SaveAndNotify("先收起当前魔法，再重放它的演示。");
            else {BeginDemonstration(key,s.Current);owner.SaveAndNotify("它又做了一遍。");}
            return true;
        }
    }
}
