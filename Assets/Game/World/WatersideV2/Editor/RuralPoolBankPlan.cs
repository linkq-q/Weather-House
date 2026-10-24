using System.Collections.Generic;
using UnityEngine;
namespace WeatherHouse.WatersideV2.Editor
{
    // Caller additionally enforces the existing integration edit mask. No water footprint or pool floor is changed.
    public static class RuralPoolBankPlan
    {
        public const float Outer=3.5f;
        static float Smooth(float a,float b,float v)=>Mathf.SmoothStep(0,1,Mathf.InverseLerp(a,b,v));
        public static float PoolDistance(Vector3 p)
        {float d=float.MaxValue;foreach(var pool in WatersideLayout.TidePools)d=Mathf.Min(d,pool.SignedDistance(p.x,p.z));return d;}
        public static float Weight(Vector3 p,IEnumerable<Bounds> protection)
        {
            float pool=PoolDistance(p);
            if(pool<=0||pool>=Outer||WatersideLayout.IsWater(p.x,p.z,.05f)||RuralMapConfiguration.Contains(p,RuralMapConfiguration.Regions[3]))return 0;
            float distance=float.MaxValue;
            foreach(var b in protection){float dx=Mathf.Max(b.min.x-p.x,0,p.x-b.max.x),dz=Mathf.Max(b.min.z-p.z,0,p.z-b.max.z);distance=Mathf.Min(distance,Mathf.Sqrt(dx*dx+dz*dz));}
            float road=float.MaxValue;foreach(var line in RuralMapConfiguration.NewCorridors)road=Mathf.Min(road,RuralMapConfiguration.LineDistance(p,line));
            return Smooth(0,2,distance)*Smooth(2.4f,3.8f,road)*Smooth(.4f,1.8f,WatersideLayout.WalkablePathEdgeDistance(p.x,p.z));
        }
        public static float Height(Vector3 p,float before,IEnumerable<Bounds> protection)
        {
            float weight=Weight(p,protection);if(weight<=0)return before;
            float target=Mathf.Min(before,Mathf.Lerp(.22f,before,Smooth(.35f,Outer,PoolDistance(p))));
            return Mathf.Lerp(before,target,weight);
        }
    }
}
