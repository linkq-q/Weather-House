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

        void OnEnable()
        {
            previousYaw=transform.eulerAngles.y;
            previousSpeed=player?Vector3.ProjectOnPlane(player.ActualVelocity,Vector3.up).magnitude:0;
            observing=false;observationTime=lookWeight=turnLean=startLean=0;
            ObservationWeight=0;Schedule();
        }

        void Schedule(){nextObservation=Time.time+Random.Range(observationInterval.x,observationInterval.y);}

        void LateUpdate()
        {
            if(player==null||chest==null||head==null)return;
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
