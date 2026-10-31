using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace WeatherHouse.Life
{
    public sealed partial class LifeWorld
    {
        readonly List<Transform> sceneContainers=new List<Transform>();
        readonly List<LifeObject> boundSceneContainers=new List<LifeObject>();
        readonly List<GameObject> addedWaterSurfaces=new List<GameObject>();
        void FindSceneContainers()
        {
            LifeSceneResources resources=Resources.Load<LifeSceneResources>("LifeContent/SceneResources");
            if(!resources || !resources.waterContainer)return;
            var meshes=new HashSet<Mesh>(resources.waterContainer.GetComponentsInChildren<MeshFilter>(true).Select(f=>f.sharedMesh));
            var roots=new HashSet<Transform>();
            foreach(MeshFilter filter in FindObjectsOfType<MeshFilter>(true))
            {
                if(!filter.gameObject.scene.IsValid() || !meshes.Contains(filter.sharedMesh))continue;
                LODGroup lod=filter.GetComponentInParent<LODGroup>();Transform root=lod?lod.transform:filter.transform;
                if(roots.Add(root))sceneContainers.Add(root);
            }
        }
        LifeObject ReuseContainer(string id,Vector3 near)
        {
            Transform best=null;float distance=2;
            foreach(Transform candidate in sceneContainers)
            {
                if(!candidate || candidate.GetComponent<LifeObject>() || !candidate.gameObject.activeInHierarchy)continue;
                float d=Flat(candidate.position,near);if(d<distance){distance=d;best=candidate;}
            }
            return best?BindContainer(best,id):null;
        }
        void BindRemainingContainers()
        {
            foreach(Transform candidate in sceneContainers)
                if(candidate && !candidate.GetComponent<LifeObject>())BindContainer(candidate,"container."+SceneObjectId(candidate));
        }
        LifeObject BindContainer(Transform source,string id)
        {
            GameObject bowl=Resources.Load<GameObject>("LifeAssets/WaterBowl");
            Transform water=bowl?bowl.GetComponentsInChildren<Transform>(true).FirstOrDefault(t=>t.name=="WaterSurface"):null;
            Renderer[] renderers=source.GetComponentsInChildren<Renderer>(true);if(!water || renderers.Length==0)return null;
            Bounds bounds=renderers[0].bounds;foreach(Renderer renderer in renderers)bounds.Encapsulate(renderer.bounds);
            foreach(Renderer renderer in renderers)
            {var previous=new MaterialPropertyBlock();renderer.GetPropertyBlock(previous);plantVisuals.Add((renderer,previous));}
            var holder=new GameObject("容器液面");holder.transform.SetParent(source,false);
            holder.transform.position=new Vector3(bounds.center.x,bounds.min.y+bounds.size.y*.68f-.14f,bounds.center.z);
            Vector3 scale=source.lossyScale;float radius=Mathf.Min(bounds.size.x,bounds.size.z)*.32f/.29f;
            holder.transform.localScale=new Vector3(radius/Mathf.Abs(scale.x),1/Mathf.Abs(scale.y),radius/Mathf.Abs(scale.z));
            GameObject surface=Instantiate(water.gameObject,holder.transform);surface.name="WaterSurface";
            var obj=source.gameObject.AddComponent<LifeObject>();
            obj.Initialize(new LifeObjectState{id=id,kind=LifeObjectKind.Bowl,position=source.position,sheltered=HasExistingRoof(source.position)},"取水木桶");
            objects.Add(id,obj);interactables.Add(obj);boundSceneContainers.Add(obj);addedWaterSurfaces.Add(holder);return obj;
        }
        void UnbindSceneContainers()
        {
            foreach(LifeObject obj in boundSceneContainers)if(obj)Destroy(obj);
            foreach(GameObject surface in addedWaterSurfaces)if(surface)Destroy(surface);
        }
    }
}
