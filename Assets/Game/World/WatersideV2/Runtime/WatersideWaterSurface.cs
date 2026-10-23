using UnityEngine;

namespace WeatherHouse.WatersideV2
{
    public sealed class WatersideWaterSurface : WorldWaterSurface
    {
        public int section = -2;
        public override bool Contains(Vector3 p, float margin = 0) => WatersideLayout.IsWater(p.x,p.z,margin) && (section == -2 || Section(p.x,p.z) == section);
        public override float Height(Vector3 p) => WatersideLayout.WaterHeight(p.x,p.z);
        public static int Section(float x,float z) => WatersideLayout.WaterSection(x,z);
    }
}
