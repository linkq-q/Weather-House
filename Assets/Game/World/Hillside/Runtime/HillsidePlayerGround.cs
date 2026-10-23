using ForestSpriteCharacter;
using UnityEngine;

namespace WeatherHouse.Hillside
{
    [DefaultExecutionOrder(150)]
    public sealed class HillsidePlayerGround : MonoBehaviour
    {
        public Terrain terrain;
        public ForestSpriteDemoController player;
        public SphereCollider clothFloor;
        void LateUpdate() { PlaceFloor(); }
        public void PlaceFloor()
        {
            if (!terrain || !player || !clothFloor) return;
            Vector3 p = player.transform.position;
            float ground = terrain.SampleHeight(p) + terrain.transform.position.y;
            clothFloor.transform.position = new Vector3(p.x, ground - clothFloor.radius, p.z);
        }
    }
}
