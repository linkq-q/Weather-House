using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using WeatherHousePrototype;
using WeatherHouse.Hillside;

namespace WeatherHouse.Life
{
    public sealed partial class LifeWorld
    {
        readonly List<Transform> photoTrees=new List<Transform>();
        readonly Dictionary<string,Renderer[]> shelterVisuals=new Dictionary<string,Renderer[]>();
        bool photoTreesReady;
        internal void AddPhotoAnimalState(AnimalActor animal,Camera camera,LifePhotoEntity entity)
        {
            if(meal!=null && meal.animal.speaker.Actor==animal && meal.grass && meal.head)
            {
                entity.eating=meal.animal.herd.IsLifeForaging(meal.animal.member) && meal.animal.herd.LifeForageWeight(meal.animal.member)>=.5f;
                // The food touching the lips is part of this activity; unrelated foreground geometry still occludes it.
                entity.mouthVisible=LifePhotoVisibility.Point(camera,meal.head.TransformPoint(meal.mouthLocal),animal.transform,meal.grass.transform);
                entity.foodVisible=meal.grass.GetComponentsInChildren<Renderer>().Any(r=>LifePhotoVisibility.RendererVisible(camera,r));
                entity.activityKind="eat";entity.activityId=meal.grass.State.id;
            }
            Drink drink=drinks.FirstOrDefault(d=>d.animal.speaker.Actor==animal && d.started>=0 && MouthTouchesWater(d));
            if(drink!=null)
            {
                entity.mouthVisible=LifePhotoVisibility.Point(camera,drink.mouthBone.TransformPoint(DrinkMouthLocal),animal.transform,drink.bowl.waterVisual);
                if(entity.mouthVisible){entity.activityKind="drink";entity.activityId=drink.bowl.State.id;}
            }
            if(entity.sleeping && AtSharedRest(animal))
            {entity.activityKind="rest";entity.activityId=commonRestId;}
        }
        bool AtSharedRest(AnimalActor animal)
        {
            if(!DryMatAt(commonRestId) || CommonWater(commonRestId)<.12f)return false;
            Binding bound=bindings.Values.FirstOrDefault(b=>b.speaker && b.speaker.Actor==animal);
            if(bound!=null && bound.rest && Flat(animal.transform.position,bound.target)<1.4f && Flat(bound.target,AnchorPosition(commonRestId))<3)return true;
            WildlifeAgent agent=animal.GetComponent<WildlifeAgent>();int index=agent?invited.IndexOf(agent):-1;
            return index>=0 && Flat(animal.transform.position,invitedTargets[index])<1.4f;
        }
        internal void AddPhotoScenery(Camera camera,List<LifePhotoEntity> result)
        {
            if(!photoTreesReady)
            {
                photoTreesReady=true;LifePhotoSubjectCatalog catalog=Resources.Load<LifePhotoSubjectCatalog>("LifeContent/PhotoSubjects");
                var meshes=new HashSet<Mesh>(catalog?catalog.treeMeshes??new Mesh[0]:new Mesh[0]);var roots=new HashSet<Transform>();
                foreach(MeshFilter filter in FindObjectsOfType<MeshFilter>())
                    if(meshes.Contains(filter.sharedMesh))
                    {LODGroup lod=filter.GetComponentInParent<LODGroup>();roots.Add(lod?lod.transform:filter.transform);}
                foreach(Tree tree in FindObjectsOfType<Tree>())roots.Add(tree.transform);
                photoTrees.AddRange(roots);
            }
            foreach(Transform tree in photoTrees)
                if(tree && tree.GetComponentsInChildren<Renderer>().Any(r=>LifePhotoVisibility.RendererVisible(camera,r,3)))
                    result.Add(new LifePhotoEntity{id="tree:"+StablePhotoPath(tree),kind="tree",bodyVisible=true,position=tree.position});
            // A home photograph needs a real roof/mat. The invisible semantic marker cannot match.
            foreach(string id in new[]{"fox.home","squirrel.nest"})
            {
                LifeObject home=ObjectAt(id);if(!home)continue;
                bool visible=shelterVisuals.TryGetValue(id,out Renderer[] roof) && roof.Any(r=>r && LifePhotoVisibility.RendererVisible(camera,r));
                if(!visible)visible=objects.Values.Any(o=>o && o.State.kind==LifeObjectKind.Mat && o.State.slot==id &&
                    o.GetComponentsInChildren<Renderer>().Any(r=>LifePhotoVisibility.RendererVisible(camera,r)));
                if(visible)result.Add(new LifePhotoEntity{id=id,kind="home",position=home.transform.position,bodyVisible=true});
            }
        }
        void BindExistingShelterVisuals(string id,Vector3 at)
        {
            foreach(var pair in shelterVisuals)
                if(pair.Key!=id && Flat(AnchorPosition(pair.Key),at)<.65f)
                {shelterVisuals[id]=pair.Value;return;}
            var roof=new List<Renderer>();
            foreach(RaycastHit hit in Physics.RaycastAll(at+Vector3.up*4,Vector3.down,3,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore))
                if(hit.point.y>at.y+1 && hit.normal.y>.4f && !hit.collider.GetComponentInParent<LifeObject>())
                {
                    Renderer renderer=hit.collider.GetComponent<Renderer>()??hit.collider.GetComponentInParent<Renderer>();
                    if(renderer)roof.Add(renderer);
                }
            shelterVisuals[id]=roof.ToArray();
        }
        static string StablePhotoPath(Transform obj)
        {
            string result=obj.GetSiblingIndex().ToString();while(obj.parent){obj=obj.parent;result=obj.GetSiblingIndex()+"/"+result;}
            return obj.gameObject.scene.name+":"+result;
        }
    }
}
