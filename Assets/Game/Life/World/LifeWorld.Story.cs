using UnityEngine;
using WeatherHouse.WatersideV2;

namespace WeatherHouse.Life
{
    public sealed partial class LifeWorld
    {
        public bool TrySeaWatchPoint(out Vector3 point, out Vector3 view)
        {
            view = default;
            if (!anchors.TryGetValue("harbor.tide", out point) || invalidTargets.Contains("harbor.tide")) return false;
            Vector3 nearest=default;float best=float.PositiveInfinity;
            Vector3[] coast=WatersideLayout.Coastline;
            for(int i=1;i<coast.Length;i++)
            {
                Vector3 segment=coast[i]-coast[i-1],offset=point-coast[i-1];segment.y=offset.y=0;
                Vector3 candidate=coast[i-1]+segment*Mathf.Clamp01(Vector3.Dot(offset,segment)/segment.sqrMagnitude);
                float distance=(candidate-point).sqrMagnitude;
                if(distance<best){best=distance;nearest=candidate;}
            }
            Vector3 outward=nearest-point;outward.y=0;
            view=nearest+outward.normalized*8;view.y=WatersideLayout.SeaLevel+.5f;
            return WatersideLayout.SeaSignedDistance(view.x,view.z)<-1;
        }
        public void RecordAnimalEncounter(string id) { if (autonomy) autonomy.RecordEncounter(id); }
    }
}
