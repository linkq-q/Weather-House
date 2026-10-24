using System.Collections.Generic;
using UnityEngine;

namespace ForestSpriteCharacter
{
    // Pose accents run after animation, before the retained accessory map and physics.
    [DefaultExecutionOrder(-130)]
    public sealed class MageLocomotionPresentation : MonoBehaviour
    {
        public ForestSpriteDemoController player;
        public Transform chest,head;
        public Vector2 observationInterval=new Vector2(9f,14f);
        [Range(0,15)] public float observationAngle=8f;
        public int ObservationsStarted { get; private set; }
        public int ObservationsInterrupted { get; private set; }
        public float ObservationWeight { get; private set; }
        public float MaximumTurnLean { get; private set; }
        float nextObservation,observationTime,observationSide=1f;
        float previousYaw,previousSpeed,turnLean,startLean,lookWeight;
        bool observing;
        struct Sole { public Transform[] bones; public Matrix4x4[] bind,matrices; public Vector3[] points; public BoneWeight[] weights; }
        Sole[] soles;
        Transform hips;
        CharacterController capsule;
        Vector3 hipBeforeSupport,hipAfterSupport;
        public float TransitionSupportLift { get; private set; }

        void OnDisable()=>RestoreSupport();

        void RestoreSupport()
        {
            if(hips&&TransitionSupportLift>0&&(hips.localPosition-hipAfterSupport).sqrMagnitude<1e-10f)
                hips.localPosition=hipBeforeSupport;
            TransitionSupportLift=0;
        }

        void CacheSoles()
        {
            if(soles!=null||!player||!player.animator)return;
            capsule=player.GetComponent<CharacterController>();
            foreach(var bone in player.animator.GetComponentsInChildren<Transform>())if(bone.name=="hips")hips=bone;
            var result=new List<Sole>();
            foreach(var renderer in player.animator.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                if(!renderer.name.StartsWith("Mage_Leg"))continue;
                var mesh=renderer.sharedMesh;if(!mesh||!mesh.isReadable)continue;
                var vertices=mesh.vertices;var weights=mesh.boneWeights;var binds=mesh.bindposes;var bones=renderer.bones;
                var unique=new HashSet<(Vector3,BoneWeight)>();
                for(int i=0;i<vertices.Length;i++)unique.Add((vertices[i],weights[i]));
                var points=new Vector3[unique.Count];var sampledWeights=new BoneWeight[unique.Count];int index=0;
                foreach(var point in unique){points[index]=point.Item1;sampledWeights[index++]=point.Item2;}
                result.Add(new Sole{bones=bones,bind=binds,matrices=new Matrix4x4[bones.Length],points=points,weights=sampledWeights});
            }
            soles=result.ToArray();
        }

        void ApplyTransitionSupport()
        {
            RestoreSupport();CacheSoles();
            var animator=player?player.animator:null;
            if(!hips||!capsule||soles==null||soles.Length!=2||!animator||!animator.isActiveAndEnabled||
                !player.IsGrounded||player.CurrentState=="Jump"||!animator.IsInTransition(0))return;
            var current=animator.GetCurrentAnimatorStateInfo(0);var next=animator.GetNextAnimatorStateInfo(0);
            if(!(current.IsName("Idle")||current.IsName("Walk")||current.IsName("Run"))||
                !(next.IsName("Idle")||next.IsName("Walk")||next.IsName("Run")))return;
            float lowest=float.PositiveInfinity;
            foreach(var sole in soles)
            {
                for(int b=0;b<sole.bones.Length;b++)sole.matrices[b]=sole.bones[b].localToWorldMatrix*sole.bind[b];
                for(int i=0;i<sole.points.Length;i++)
                {
                    var v=sole.points[i];var w=sole.weights[i];var m=sole.matrices;
                    float y=m[w.boneIndex0].MultiplyPoint3x4(v).y*w.weight0+m[w.boneIndex1].MultiplyPoint3x4(v).y*w.weight1+
                        m[w.boneIndex2].MultiplyPoint3x4(v).y*w.weight2+m[w.boneIndex3].MultiplyPoint3x4(v).y*w.weight3;
                    lowest=Mathf.Min(lowest,y);
                }
            }
            // Both pure clips already meet the capsule's skin-width baseline. Rotational
            // blending can lengthen a leg; compensate only that extra pose penetration.
            float blend=animator.GetAnimatorTransitionInfo(0).normalizedTime;
            float edge=Mathf.SmoothStep(0,1,Mathf.Min(blend,1-blend)/.05f);
            TransitionSupportLift=Mathf.Max(0,capsule.bounds.min.y-capsule.skinWidth-lowest)*edge;
            hipBeforeSupport=hips.localPosition;
            hips.position+=Vector3.up*TransitionSupportLift;
            hipAfterSupport=hips.localPosition;
        }

        void OnEnable()
        {
            RestoreSupport();
            previousYaw=transform.eulerAngles.y;
            previousSpeed=player?Vector3.ProjectOnPlane(player.ActualVelocity,Vector3.up).magnitude:0;
            observing=false;observationTime=lookWeight=turnLean=startLean=0;
            ObservationWeight=0;Schedule();
        }

        void Schedule(){nextObservation=Time.time+Random.Range(observationInterval.x,observationInterval.y);}

        void LateUpdate()
        {
            if(player==null||chest==null||head==null)return;
            ApplyTransitionSupport();
            float dt=Time.deltaTime;if(dt<=0)return;
            float speed=Vector3.ProjectOnPlane(player.ActualVelocity,Vector3.up).magnitude;
            float yaw=transform.eulerAngles.y;
            float yawRate=Mathf.DeltaAngle(previousYaw,yaw)/dt;
            float acceleration=(speed-previousSpeed)/dt;
            bool canObserve=player.CurrentState=="Walk"&&player.IsGrounded&&speed>.5f&&!player.InputBlocked&&!player.JumpBlocked&&Mathf.Abs(yawRate)<30;
            if(observing&&!canObserve)
            {observing=false;ObservationsInterrupted++;Schedule();}
            if(!observing&&canObserve&&Time.time>=nextObservation)
            {observing=true;observationTime=0;observationSide=Random.value<.5f?-1:1;ObservationsStarted++;}
            float target=0;
            if(observing)
            {
                observationTime+=dt;
                target=Mathf.Sin(Mathf.Clamp01(observationTime/1.1f)*Mathf.PI);
                if(observationTime>=1.1f){observing=false;Schedule();}
            }
            lookWeight=Mathf.MoveTowards(lookWeight,target,dt*(canObserve?7f:20f));
            ObservationWeight=lookWeight;
            turnLean=Mathf.Lerp(turnLean,Mathf.Clamp(yawRate/90f,-6f,6f),1-Mathf.Exp(-18f*dt));
            startLean=Mathf.Lerp(startLean,Mathf.Clamp(acceleration*.25f,-4f,5f),1-Mathf.Exp(-14f*dt));
            if(player.CurrentState=="Jump")startLean=Mathf.Lerp(startLean,0,1-Mathf.Exp(-20f*dt));
            chest.rotation=Quaternion.AngleAxis(-turnLean,transform.forward)*Quaternion.AngleAxis(startLean,transform.right)*chest.rotation;
            head.rotation=Quaternion.AngleAxis(observationSide*observationAngle*lookWeight,transform.up)*head.rotation;
            MaximumTurnLean=Mathf.Max(MaximumTurnLean,Mathf.Abs(turnLean));
            previousYaw=yaw;previousSpeed=speed;
        }
    }
}
