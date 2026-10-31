using System;
using System.Collections.Generic;

namespace WeatherHouse.Hillside
{
    [Flags]
    public enum AnimalExpressionSignals
    {
        None=0, NearPlayer=1, Greet=2, Alert=4, Flee=8, QuietRest=16, Sleeping=32
    }

    // Event edges choose a short expression. Rendering and movement keep their own owners.
    public sealed class AnimalExpressionPolicy
    {
        public const float SameExpressionCooldownSeconds=120f;
        public float OrdinaryCooldownSeconds { get; }
        readonly bool outgoing;
        readonly float timidity,sociability;
        readonly Dictionary<AnimalEmotion,float> nextEmotion=new Dictionary<AnimalEmotion,float>();
        WildlifeRandom random;
        AnimalExpressionSignals previous;
        float nextOrdinary=float.NegativeInfinity;

        public AnimalExpressionPolicy(int stableSeed,bool outgoing,float timidity,float sociability)
        {
            this.outgoing=outgoing;
            this.timidity=Math.Max(0,Math.Min(1,timidity));
            this.sociability=Math.Max(0,Math.Min(1,sociability));
            // Independent stream: expression choices never advance movement or size randomness.
            random=new WildlifeRandom(stableSeed,0,"expression");
            OrdinaryCooldownSeconds=45f+45f*random.Value();
        }

        public bool TryChoose(AnimalExpressionSignals signals,float now,bool busy,out AnimalEmotion emotion,out bool urgent)
        {
            emotion=default;urgent=false;
            AnimalExpressionSignals entered=signals & ~previous;
            previous=signals;
            if((signals & AnimalExpressionSignals.Sleeping)!=0)return false;
            float chance;
            if((entered & AnimalExpressionSignals.Alert)!=0)
            {emotion=AnimalEmotion.Exclamation;chance=1;urgent=true;}
            else if((entered & AnimalExpressionSignals.Flee)!=0)
            {emotion=AnimalEmotion.Sweat;chance=.3f+.65f*timidity;}
            else if((entered & AnimalExpressionSignals.Greet)!=0)
            {emotion=AnimalEmotion.Happy;chance=.25f+.65f*sociability;}
            else if((entered & AnimalExpressionSignals.NearPlayer)!=0)
            {emotion=AnimalEmotion.Heart;chance=outgoing?.65f+.3f*sociability:.08f+.22f*sociability;}
            else if((entered & AnimalExpressionSignals.QuietRest)!=0)
            {emotion=AnimalEmotion.Music;chance=.15f+.35f*(1-timidity);}
            else return false;

            if(!urgent && (busy || now<nextOrdinary) ||
                nextEmotion.TryGetValue(emotion,out float next) && now<next)return false;
            if(chance<1 && random.Value()>=chance)return false;
            nextEmotion[emotion]=now+SameExpressionCooldownSeconds;
            nextOrdinary=now+OrdinaryCooldownSeconds;
            return true;
        }

        public bool TryIntent(AnimalEmotion emotion,float now,bool busy)
        {
            if(busy || now<nextOrdinary || nextEmotion.TryGetValue(emotion,out float next) && now<next)return false;
            nextEmotion[emotion]=now+SameExpressionCooldownSeconds;
            nextOrdinary=now+OrdinaryCooldownSeconds;
            return true;
        }
    }
}
