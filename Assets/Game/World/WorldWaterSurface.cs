using UnityEngine;

namespace WeatherHouse
{
    // Shared by wakes and habitat checks so a connected river has one physical water boundary.
    public abstract class WorldWaterSurface : MonoBehaviour
    {
        public abstract bool Contains(Vector3 position, float margin = 0);
        public abstract float Height(Vector3 position);
    }
}
