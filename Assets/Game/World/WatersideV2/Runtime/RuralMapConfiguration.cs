using UnityEngine;
namespace WeatherHouse.WatersideV2
{
    // Incremental rural layout, 2026-10-25. Keep Design() and existing world anchors unchanged.
    public static class RuralMapConfiguration
    {
        public const string Version = "rural-20261025-v1";
        public static readonly Vector3[] Coastline = { new Vector3(63f,0f,90f), new Vector3(81f,0f,88.2f), new Vector3(90f,0f,68.4f), new Vector3(86.4f,0f,41.4f), new Vector3(75.6f,0f,21.6f), new Vector3(73.8f,0f,3.6f), new Vector3(79.2f,0f,-10.8f), new Vector3(82.8f,0f,-25.2f), new Vector3(88.2f,0f,-41.4f), new Vector3(90f,0f,-57.6f), new Vector3(82.8f,0f,-72f), new Vector3(68.4f,0f,-82.8f), new Vector3(48.6f,0f,-84.6f), new Vector3(28.8f,0f,-86.4f), new Vector3(18f,0f,-86.4f), new Vector3(1.8f,0f,-82.8f), new Vector3(-12.6f,0f,-86.4f), new Vector3(-34.2f,0f,-86.4f), new Vector3(-52.2f,0f,-86.4f), new Vector3(-68.4f,0f,-79.2f), new Vector3(-90f,0f,-72f) };
        public static readonly WatersideLayout.TidePool[] TidePools = {
            new WatersideLayout.TidePool(68.4f, -18f, 2.52f, 2.16f, 0.4f, 0.4f),
            new WatersideLayout.TidePool(77.4f, -25.2f, 3.24f, 2.34f, 0.5f, 1.1f),
            new WatersideLayout.TidePool(65.7f, -30.6f, 4.86f, 3.06f, 0.65f, 1.8f),
            new WatersideLayout.TidePool(79.2f, -36f, 5.4f, 4.5f, 0.75f, 2.5f),
            new WatersideLayout.TidePool(68.4f, -43.2f, 3.24f, 2.34f, 0.5f, 3.2f),
            new WatersideLayout.TidePool(79.2f, -48.6f, 4.32f, 3.24f, 0.65f, 3.9f)
        };
        public static readonly Vector3[] OldCoastline = { new Vector3(63f,0f,90f), new Vector3(68.4f,0f,57.6f), new Vector3(61.2f,0f,34.2f), new Vector3(68.4f,0f,12.6f), new Vector3(64.8f,0f,-5.4f), new Vector3(57.6f,0f,-27f), new Vector3(59.4f,0f,-37.8f), new Vector3(54f,0f,-52.2f), new Vector3(36f,0f,-61.2f), new Vector3(19.8f,0f,-73.8f), new Vector3(-5.4f,0f,-66.6f), new Vector3(-28.8f,0f,-72f), new Vector3(-50.4f,0f,-63f), new Vector3(-90f,0f,-72f) };
        public static readonly Vector3[][] NewCorridors = {
            new[] { new Vector3(-37.8f,8f,10.8f), new Vector3(-50.4f,12f,25.2f), new Vector3(-50.4f,14f,43.2f), new Vector3(-37.8f,14f,45f), new Vector3(-27f,12f,43.2f), new Vector3(-30.6f,10.5f,34.2f), new Vector3(-25.2f,7.4f,28.8f), new Vector3(-19.8f,6.2f,23.4f) },
            new[] { new Vector3(-45f,6f,-7.2f), new Vector3(-54f,6f,-9f), new Vector3(-61.2f,10f,10.8f), new Vector3(-59.4f,13f,30.6f), new Vector3(-50.4f,14f,43.2f) },
            new[] { new Vector3(-19.8f,6.2f,23.4f), new Vector3(-21.6f,5f,14.4f) },
            new[] { new Vector3(34.2f,8f,14.4f), new Vector3(39.6f,10f,23.4f) },
            new[] { new Vector3(43.2f,3.4f,-9f), new Vector3(54f,4f,-5.4f), new Vector3(57.6f,4f,-3.6f) },
            new[] { new Vector3(41.4f,2.5f,-19.8f), new Vector3(57.6f,2f,-19.8f), new Vector3(57.6f,1.4f,-34.2f), new Vector3(63f,1.5f,-48.6f), new Vector3(77.4f,1.5f,-63f), new Vector3(61.2f,1.5f,-64.8f), new Vector3(46.8f,1.5f,-61.2f), new Vector3(28.8f,1.6f,-46.8f) },
            new[] { new Vector3(-46.8f,3f,-45f), new Vector3(-55.8f,2.5f,-52.2f), new Vector3(-46.8f,1.7f,-59.4f), new Vector3(-28.8f,1.3f,-64.8f), new Vector3(-12.6f,1.2f,-75.6f), new Vector3(-3.6f,1.2f,-70.2f), new Vector3(-9f,1.2f,-61.2f) },
            new[] { new Vector3(23.4f,0f,25.2f), new Vector3(34.2f,0f,14.4f), new Vector3(40f,6.4245758f,7.1f), new Vector3(40.3f,5.9725151f,3.7f), new Vector3(39.6f,0f,1.8f), new Vector3(43.2f,0f,-9f), new Vector3(41.4f,0f,-19.8f), new Vector3(36f,0f,-32.4f), new Vector3(28.8f,0f,-46.8f), new Vector3(19.8f,0f,-55.8f) }
        };
        public static readonly Vector3[] DrySpine = { new Vector3(57.6f,0f,-19.8f), new Vector3(57.6f,0f,-34.2f), new Vector3(63f,0f,-48.6f), new Vector3(77.4f,0f,-63f) };
        public static readonly Vector3[][] Regions = {
            new[] { new Vector3(-77.4f,0f,73.8f), new Vector3(-46.8f,0f,77.4f), new Vector3(-27f,0f,68.4f), new Vector3(-19.8f,0f,50.4f), new Vector3(-27f,0f,30.6f), new Vector3(-36f,0f,18f), new Vector3(-32.4f,0f,3.6f), new Vector3(-41.4f,0f,-12.6f), new Vector3(-54f,0f,-18f), new Vector3(-68.4f,0f,-39.6f), new Vector3(-77.4f,0f,-27f), new Vector3(-75.6f,0f,3.6f), new Vector3(-81f,0f,30.6f) },
            new[] { new Vector3(9f,0f,72f), new Vector3(39.6f,0f,77.4f), new Vector3(66.6f,0f,64.8f), new Vector3(66.6f,0f,37.8f), new Vector3(61.2f,0f,16.2f), new Vector3(68.4f,0f,3.6f), new Vector3(52.2f,0f,-5.4f), new Vector3(39.6f,0f,1.8f), new Vector3(27f,0f,10.8f), new Vector3(18f,0f,27f), new Vector3(9f,0f,41.4f) },
            new[] { new Vector3(-66.6f,0f,-52.2f), new Vector3(-46.8f,0f,-48.6f), new Vector3(-30.6f,0f,-57.6f), new Vector3(-12.6f,0f,-61.2f), new Vector3(0f,0f,-68.4f), new Vector3(5.4f,0f,-79.2f), new Vector3(-12.6f,0f,-86.4f), new Vector3(-32.4f,0f,-91.8f), new Vector3(-48.6f,0f,-84.6f), new Vector3(-64.8f,0f,-73.8f) },
            new[] { new Vector3(-7.2f,0f,10.8f), new Vector3(5.4f,0f,3.6f), new Vector3(10.8f,0f,-9f), new Vector3(9f,0f,-23.4f), new Vector3(-1.8f,0f,-32.4f), new Vector3(-12.6f,0f,-23.4f), new Vector3(-18f,0f,-12.6f), new Vector3(-14.4f,0f,0f) },
            new[] { new Vector3(66.6f,0f,-10.8f), new Vector3(75.6f,0f,-12.6f), new Vector3(81f,0f,-23.4f), new Vector3(84.6f,0f,-37.8f), new Vector3(84.6f,0f,-50.4f), new Vector3(73.8f,0f,-52.2f), new Vector3(63f,0f,-43.2f), new Vector3(59.4f,0f,-32.4f) },
            new[] { new Vector3(52.2f,0f,-54f), new Vector3(70.2f,0f,-52.2f), new Vector3(81f,0f,-57.6f), new Vector3(82.8f,0f,-70.2f), new Vector3(72f,0f,-75.6f), new Vector3(57.6f,0f,-72f), new Vector3(50.4f,0f,-63f) },
            new[] { new Vector3(-54f,0f,-19.8f), new Vector3(-37.8f,0f,-19.8f), new Vector3(-34.2f,0f,-28.8f), new Vector3(-39.6f,0f,-36f), new Vector3(-52.2f,0f,-34.2f), new Vector3(-57.6f,0f,-27f) }
        };
        public static float SegmentDistance(Vector3 p, Vector3 a, Vector3 b)
        { p.y=a.y=b.y=0; Vector3 d=b-a; float t=d.sqrMagnitude<.000001f?0:Mathf.Clamp01(Vector3.Dot(p-a,d)/d.sqrMagnitude); return Vector3.Distance(p,a+d*t); }
        public static float LineDistance(Vector3 p, Vector3[] line)
        { float d=float.MaxValue; for(int i=1;i<line.Length;i++)d=Mathf.Min(d,SegmentDistance(p,line[i-1],line[i]));return d; }
        public static float SpineDistance(float x,float z) => LineDistance(new Vector3(x,0,z),DrySpine);
        public static bool Contains(Vector3 p,Vector3[] polygon)
        { bool inside=false;for(int i=0,j=polygon.Length-1;i<polygon.Length;j=i++){var a=polygon[i];var b=polygon[j];if((a.z>p.z)!=(b.z>p.z)&&p.x<(b.x-a.x)*(p.z-a.z)/(b.z-a.z)+a.x)inside=!inside;}return inside; }
        public static float OldShoreDistance(Vector3 p)
        {
            float distance=LineDistance(p,OldCoastline);
            // Match the original LandContains mountain-boundary rules; measure distance at the original point.
            p.x = Mathf.Max(p.x, -WatersideLayout.Size * .5f + .001f);
            p.z = Mathf.Min(p.z, WatersideLayout.Size * .5f - .001f);
            var poly=new Vector3[OldCoastline.Length+1];poly[0]=new Vector3(-90,0,90);
            System.Array.Copy(OldCoastline,0,poly,1,OldCoastline.Length);
            return distance*(Contains(p,poly)?1:-1);
        }
    }
}
