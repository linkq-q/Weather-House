using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace WeatherHouse.Life
{
    public enum LifePrimaryAction { None,Talk,Gather,PickUpBook }
    public static class LifeInteractionRules
    {
        public static LifePrimaryAction Primary(bool blocked,bool canTalk,bool canGather,bool canPickUpBook=false)=>
            blocked?LifePrimaryAction.None:canPickUpBook?LifePrimaryAction.PickUpBook:canTalk?LifePrimaryAction.Talk:canGather?LifePrimaryAction.Gather:LifePrimaryAction.None;
    }

    public sealed partial class LifeWorld
    {
        const float GatheringCellSize=4f;
        public const float GatheringDistance=2.15f;
        readonly Dictionary<Vector2Int,List<LifeObject>> gatheringCells=new Dictionary<Vector2Int,List<LifeObject>>();
        readonly List<LifeObject> gatheringSources=new List<LifeObject>();
        readonly Dictionary<string,LifeObject> sourceAliases=new Dictionary<string,LifeObject>();
        readonly List<(Renderer renderer,MaterialPropertyBlock block)> plantVisuals=new List<(Renderer,MaterialPropertyBlock)>();
        public IReadOnlyList<LifeObject> GatheringSources=>gatheringSources;
        public int LastGatheringVisits {get;private set;}
        public bool CanGather=>!held && craftingId==null && feeding==null && GatheringTarget();
        public bool IsCrafting => craftingId != null;

        static Vector2Int GatheringCell(Vector3 p)=>new Vector2Int(Mathf.FloorToInt(p.x/GatheringCellSize),Mathf.FloorToInt(p.z/GatheringCellSize));
        static string SceneObjectId(Transform target)
        {
            var path=new StringBuilder();
            for(Transform t=target;t;t=t.parent)path.Insert(0,"/"+t.GetSiblingIndex());
            return target.gameObject.scene.name+path;
        }
        void BindGatheringPlants()
        {
            LifePlantSpecies species=Resources.Load<LifePlantSpecies>("LifeContent/GatheringGrass");
            if(!species){SetupWarning+="缺少可采植物定义；";return;}
            var roots=new HashSet<Transform>();
            foreach(MeshFilter filter in FindObjectsOfType<MeshFilter>(true))
            {
                if(!species.Matches(filter.sharedMesh) || !filter.gameObject.scene.IsValid() || filter.transform.IsChildOf(transform))continue;
                LODGroup lod=filter.GetComponentInParent<LODGroup>();Transform root=lod?lod.transform:filter.transform;
                if(!roots.Add(root))continue;
                foreach(Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
                {var previous=new MaterialPropertyBlock();renderer.GetPropertyBlock(previous);plantVisuals.Add((renderer,previous));}
                var source=root.gameObject.AddComponent<LifeObject>();
                source.Initialize(new LifeObjectState{id="plant."+species.speciesId+"."+SceneObjectId(root),kind=LifeObjectKind.GrassSource,position=root.position},species.displayName);
                objects.Add(source.State.id,source);gatheringSources.Add(source);
                Vector2Int cell=GatheringCell(root.position);
                if(!gatheringCells.TryGetValue(cell,out List<LifeObject> sources))gatheringCells.Add(cell,sources=new List<LifeObject>());
                sources.Add(source);
            }
            if(gatheringSources.Count==0)SetupWarning+="当前场景没有已注册的可采植物；";
            foreach(var pair in bindings)
            {
                LifeObject nearest=null;float distance=float.MaxValue;
                foreach(LifeObject source in gatheringSources)
                {float d=Flat(source.transform.position,pair.Value.home);if(d<distance){nearest=source;distance=d;}}
                if(nearest)sourceAliases[pair.Key+".grass"]=nearest;
            }
        }
        public LifeObject GatheringTarget()
        {
            LastGatheringVisits=0;if(!Player)return null;
            Vector2Int cell=GatheringCell(Player.position);LifeObject nearest=null;float distance=GatheringDistance;
            for(int x=-1;x<=1;x++)for(int z=-1;z<=1;z++)
            {
                if(!gatheringCells.TryGetValue(cell+new Vector2Int(x,z),out List<LifeObject> sources))continue;
                foreach(LifeObject source in sources)
                {
                    LastGatheringVisits++;
                    if(!source || !source.gameObject.activeInHierarchy || Mathf.Abs(source.transform.position.y-Player.position.y)>2)continue;
                    float d=Flat(source.transform.position,Player.position);if(d<distance){nearest=source;distance=d;}
                }
            }
            return nearest;
        }
        public bool TryGather()
        {
            if(!owner || !Player || held || craftingId!=null || feeding!=null)return false;
            LifeObject source=GatheringTarget();if(!source)return false;
            held=CreateItem(new LifeObjectState{id="bundle."+(++serial),kind=LifeObjectKind.Bundle,position=Player.position,wetness=source.State.wetness,held=true});
            LifeAudio.PlayAt(LifeAudioCue.Fiber, source.transform.position);
            owner.SaveAndNotify("");return true;
        }
        public LifeObject ObjectAt(string id)
        {
            if(string.IsNullOrEmpty(id))return null;
            return objects.TryGetValue(id,out LifeObject value)?value:sourceAliases.TryGetValue(id,out value)?value:null;
        }
        void UnbindGatheringPlants()
        {
            foreach(var visual in plantVisuals)if(visual.renderer)visual.renderer.SetPropertyBlock(visual.block);
            foreach(LifeObject source in gatheringSources)if(source)Destroy(source);
            gatheringSources.Clear();gatheringCells.Clear();plantVisuals.Clear();
        }
    }
}
