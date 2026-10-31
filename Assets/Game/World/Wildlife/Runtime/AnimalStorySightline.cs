using UnityEngine;
using UnityEngine.SceneManagement;

namespace WeatherHouse.Hillside
{
    public static class AnimalStorySightline
    {
        static readonly RaycastHit[] hits=new RaycastHit[32];

        public static bool IsClear(Vector3 eye,Vector3 view,Transform animal,out Collider obstruction)
        {
            obstruction=null;
            Vector3 direction=view-eye;float distance=direction.magnitude;
            if(!animal || float.IsNaN(distance) || float.IsInfinity(distance) || distance<.001f)return false;
            PhysicsScene physics=animal.gameObject.scene.GetPhysicsScene();
            int count=physics.Raycast(eye,direction/distance,hits,distance,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore);
            // A full buffer may hide a real wall behind ignored self/boundary hits.
            if(count==hits.Length)return false;
            float nearest=float.PositiveInfinity;
            for(int i=0;i<count;i++)
            {
                Collider collider=hits[i].collider;
                if(!collider || collider.transform.IsChildOf(animal) || IsGeneratedBackstop(collider))continue;
                if(hits[i].distance<nearest){nearest=hits[i].distance;obstruction=collider;}
            }
            return !obstruction;
        }

        static bool IsGeneratedBackstop(Collider collider)
        {
            // These four unrendered movement limits are authored by WatersideStructureBuilder.Boundaries.
            // Ordinary collision proxies still obstruct sight, including objects with the same name elsewhere.
            if(!(collider is BoxCollider box) || collider.name!="地图边界碰撞" || collider.GetComponentInChildren<Renderer>(true))return false;
            Transform t=collider.transform,parent=t.parent;
            if(!parent || parent.name!="石阶 · 跨水连接 · 自然岩岸" || !parent.parent || parent.parent.name!="03 自然台地与连接" ||
                Quaternion.Angle(t.rotation,Quaternion.identity)>.01f || !Same(t.lossyScale,Vector3.one) || !Same(box.center,Vector3.zero))return false;
            Vector3 point=t.position;
            return Same(box.size,new Vector3(2,70,180)) && (Same(point,new Vector3(-85,15,0)) || Same(point,new Vector3(91,15,0))) ||
                Same(box.size,new Vector3(180,70,2)) && (Same(point,new Vector3(0,15,85)) || Same(point,new Vector3(0,15,-91)));
        }
        static bool Same(Vector3 a,Vector3 b)=>(a-b).sqrMagnitude<.000001f;
    }
}
