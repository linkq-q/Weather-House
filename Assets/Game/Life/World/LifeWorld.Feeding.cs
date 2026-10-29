using UnityEngine;
using System.Linq;

namespace WeatherHouse.Life
{
    public sealed partial class LifeWorld
    {
        sealed class Feed {public Binding animal;public LifeObject bundle;public float expires;}
        Feed feeding;
        sealed class Meal {public Binding animal;public LifeObject grass;public Transform head;public Vector3 mouthLocal;}
        Meal meal;
        Binding NearbyFoodAnimal()
        {
            if(!Player)return null;
            Binding nearest=null;float distance=4;
            foreach(LifeRequest request in requests)
                if(request.condition=="fed" && bindings.TryGetValue(request.speakerKey,out Binding animal) && animal.speaker && animal.herd)
                {float d=Flat(Player.position,animal.speaker.transform.position);if(d<distance){distance=d;nearest=animal;}}
            return nearest;
        }
        void BeginFeed(Binding animal)
        {
            if(feeding!=null || !held || held.State.kind!=LifeObjectKind.Bundle || animal==null || !animal.herd)return;
            if(!animal.herd.RequestLifeForage(animal.member,6f)){owner.SaveAndNotify("等它停下来再递草。");return;}
            feeding=new Feed{animal=animal,bundle=held,expires=Time.time+2.5f};
            owner.SaveAndNotify("");
        }
        void UpdateFeeding()
        {
            UpdateMeal();
            if(feeding==null)return;
            Feed f=feeding;
            if(!Player || !f.animal.herd || !f.animal.speaker || held!=f.bundle || !f.bundle ||
                Flat(Player.position,f.animal.speaker.transform.position)>4 || Time.time>f.expires || !f.animal.herd.IsLifeForaging(f.animal.member))
            {CancelFeed();owner.SaveAndNotify("递草已中断。");return;}
            if(f.animal.herd.LifeForageWeight(f.animal.member)<.5f)return;
            var member=f.animal.herd.members.First(m=>m.id==f.animal.member);
            if(!TryMouthPoint(member.root,member.headBone,out Vector3 mouth))
            {CancelFeed();owner.SaveAndNotify("它还没接住草。");return;}
            // Consuming the bundle follows actual animation playback, not merely a button press.
            foreach(LifeRequest request in requests)
                if(request.condition=="fed" && request.speakerKey==f.animal.speaker.key && accepted.ContainsKey(request.id) && !fed.Contains(request.id))fed.Add(request.id);
            LifeAudio.PlayAt(LifeAudioCue.Fiber,f.animal.speaker.Actor.BubbleAnchor);
            ClearMeal();meal=new Meal{animal=f.animal,grass=f.bundle,head=member.headBone,mouthLocal=member.headBone.InverseTransformPoint(mouth)};
            held=null;f.bundle.State.held=false;objects.Remove(f.bundle.State.id);interactables.Remove(f.bundle);
            f.bundle.transform.localScale=Vector3.one*.42f;
            feeding=null;UpdateMeal();owner.SaveAndNotify("");
        }
        void CancelFeed()
        {
            if(feeding?.animal.herd)feeding.animal.herd.CancelLifeForage(feeding.animal.member);
            feeding=null;ClearMeal();
        }
        void UpdateMeal()
        {
            if(meal==null)return;
            if(!meal.grass || !meal.head || !meal.animal.herd || !meal.animal.herd.IsLifeForaging(meal.animal.member))
            {ClearMeal();return;}
            Vector3 mouth=meal.head.TransformPoint(meal.mouthLocal);
            meal.grass.transform.position=mouth-Vector3.up*.045f;
            meal.grass.transform.rotation=meal.animal.speaker.transform.rotation;
        }
        void LateUpdate(){UpdateMeal();}
        void ClearMeal(){if(meal?.grass)Destroy(meal.grass.gameObject);meal=null;}
        // The shipped sheep's lower muzzle is fully weighted to Head (imported skin vertices 5492/5570).
        static readonly Vector3 SheepMouthLocal=new Vector3(-.0009948486f,.0024140286f,-.0017676045f);
        static bool TryMouthPoint(Transform animal,Transform head,out Vector3 point)
        {
            point=default;if(!animal || !head)return false;
            point=head.TransformPoint(SheepMouthLocal);return true;
        }
    }
}
