namespace WeatherHouse.Hillside
{
    // Each exposure gets one independent draw; continuous observation never rerolls weak rain.
    public sealed class AnimalRainReaction
    {
        WildlifeRandom random;
        public int Intensity { get; private set; }
        public int Episode { get; private set; }
        public bool WeakShelterChosen { get; private set; }
        bool shelterChosen;
        public bool NeedsShelter => Intensity > 0 && shelterChosen;
        public bool Urgent => Intensity >= 2;
        public AnimalRainReaction(int seed,int individual,string species)
        { random=new WildlifeRandom(seed,individual,"rain/"+species); }
        public void Observe(int intensity)
        {
            intensity=System.Math.Max(0,System.Math.Min(2,intensity));
            if(intensity>0 && Intensity==0)
            {Episode++;WeakShelterChosen=random.Value()<.5f;shelterChosen=WeakShelterChosen;}
            // Once this exposure requires shelter, weakening rain must not send the animal back out.
            if(intensity>=2)shelterChosen=true;
            if(intensity==0)shelterChosen=false;
            Intensity=intensity;
        }
    }
}
