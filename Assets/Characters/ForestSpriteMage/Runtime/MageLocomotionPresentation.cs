using System.Collections.Generic;
using UnityEngine;
using WeatherHouse.WatersideV2;

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
        public bool recordedMotionEnabled;
        public bool fullBodyHopsEnabled;
        [Range(0f, .5f)] public float recordedVariationWeight = .3f;
        public string CurrentAccent { get; private set; } = "";
        public float AccentPhase { get; private set; }
        public float VariationWeight { get; private set; }
        public int AccentsStarted { get; private set; }
        public int AccentsInterrupted { get; private set; }
        float accentElapsed,accentDuration,previousMotionSpeed;
        Vector3 previousIntent;
        string previousMotionState="";
        int variationLayer=-1;
        bool wasRecorded,returningToGait;
        float walkClipLength=2f;
        WatersideWaterBoundary water;
        float nextObservation,observationTime,observationSide=1f;
        float previousYaw,previousSpeed,turnLean,startLean,lookWeight;
        bool observing;
        struct Sole { public Transform[] bones; public Matrix4x4[] bind,matrices; public Vector3[] points; public BoneWeight[] weights; }
        Sole[] soles;
        Transform hips;
        CharacterController capsule;
        Vector3 hipBeforeSupport,hipAfterSupport;
        public float TransitionSupportLift { get; private set; }

        void OnDisable()
        {
            if(player)player.MovementUpdated-=AfterMovement;
            if(player&&fullBodyHopsEnabled)player.CancelGaitHops();
            CancelAccent(true);ClearVariation();RestoreSupport();
            if(player&&player.capeCloth)player.capeCloth.PresentationOpenness=0;
        }

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
                !player.IsGrounded||!animator.IsInTransition(0))return;
            var current=animator.GetCurrentAnimatorStateInfo(0);var next=animator.GetNextAnimatorStateInfo(0);
            bool landing = player.lightMovingLanding && IsJumpPose(current) && IsGroundPose(next);
            bool takeoff = player.lightMovingLanding && player.JumpPhase<=.22f &&
                (IsGroundPose(current)||IsJumpPose(current)) && (IsGroundPose(next)||IsJumpPose(next));
            if(!landing && !takeoff && (!IsGroundPose(current)||!IsGroundPose(next)))return;
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

        static bool IsJumpPose(AnimatorStateInfo state)=>state.IsName("Jump")||state.IsName("HopLeft")||state.IsName("HopRight");

        static bool IsGroundPose(AnimatorStateInfo state)=>state.IsName("Idle")||state.IsName("Walk")||state.IsName("Run")||
            state.IsName("CMU_Start")||state.IsName("CMU_Stop")||state.IsName("CMU_TurnLeft90")||state.IsName("CMU_TurnRight90");

        void OnEnable()
        {
            RestoreSupport();
            previousYaw=transform.eulerAngles.y;
            previousSpeed=player?Vector3.ProjectOnPlane(player.ActualVelocity,Vector3.up).magnitude:0;
            observing=false;observationTime=lookWeight=turnLean=startLean=0;
            ObservationWeight=0;Schedule();
            previousIntent=Vector3.zero;previousMotionState="";previousMotionSpeed=0;
            CurrentAccent="";AccentPhase=VariationWeight=0;
            if(player)
            {
                player.MovementUpdated+=AfterMovement;
                capsule=player.GetComponent<CharacterController>();
                water=player.GetComponent<WatersideWaterBoundary>();
                variationLayer=player.animator?player.animator.GetLayerIndex("Recorded Variation"):-1;
                if(player.animator&&player.animator.runtimeAnimatorController)
                    foreach(var clip in player.animator.runtimeAnimatorController.animationClips)
                        if(clip.name=="Walking_MageBroad_MageSoles")walkClipLength=clip.length;
            }
        }

        void Schedule(){nextObservation=Time.time+Random.Range(observationInterval.x,observationInterval.y);}

        bool InWater()=>water&&water.isActiveAndEnabled&&water.terrain&&water.terrain.terrainData&&water.surface&&capsule&&
            water.SampleSupportedDepth(player.transform.position,capsule.bounds.min.y)>.005f;

        void AfterMovement(float dt)
        {
            if(!player||!player.animator)return;
            if(!recordedMotionEnabled)
            {
                if(wasRecorded){player.CancelGaitHops();CancelAccent(true);ClearVariation();wasRecorded=false;}
                return;
            }
            wasRecorded=true;
            if(fullBodyHopsEnabled && player.GaitHopsActive && !CanBeginGaitHop())
            {player.CancelGaitHops();ClearVariation();}
            if(fullBodyHopsEnabled && player.GaitHopsActive)
            {
                ObservationWeight=Mathf.Sin(player.GaitHopsProgress*Mathf.PI);
                VariationWeight=recordedVariationWeight*ObservationWeight;
                if(variationLayer>=0)player.animator.SetLayerWeight(variationLayer,0);
                previousIntent=player.MovementIntent;previousMotionState=player.CurrentState;
                previousMotionSpeed=Vector3.ProjectOnPlane(player.ActualVelocity,Vector3.up).magnitude;
                return;
            }
            var animator=player.animator;
            Vector3 intent=player.MovementIntent;
            float speed=Vector3.ProjectOnPlane(player.ActualVelocity,Vector3.up).magnitude;
            float angle=previousIntent.sqrMagnitude>.001f&&intent.sqrMagnitude>.001f?
                Vector3.SignedAngle(previousIntent,intent,Vector3.up):0;
            bool allowed=dt>0&&player.IsGrounded&&!player.InputBlocked&&!player.JumpBlocked&&
                player.CurrentState!="Jump"&&player.CurrentState!="Run"&&!InWater();
            if(!allowed||Mathf.Abs(angle)>=135f)
            {
                CancelAccent(player.CurrentState!="Jump"&&player.CurrentState!="Run");ClearVariation();
            }
            else
            {
                bool moving=intent.sqrMagnitude>.001f;
                if(moving&&Mathf.Abs(angle)>=55f)
                    BeginAccent(angle<0?"CMU_TurnLeft90":"CMU_TurnRight90",.28f);
                else if(moving&&previousIntent.sqrMagnitude<=.001f&&speed>.02f&&player.walkStartSeconds>0)
                    BeginAccent("CMU_Start",.32f);
                else if(!moving&&previousIntent.sqrMagnitude>.001f&&previousMotionState=="Walk"&&previousMotionSpeed>.2f&&player.walkStopSeconds>0)
                    BeginAccent("CMU_Stop",.28f);
                else if(CurrentAccent.Length>0&&moving&&speed<.02f)
                    CancelAccent(true);
                if(CurrentAccent.Length>0)
                {
                    accentElapsed+=dt;
                    AccentPhase=Mathf.Clamp01(accentElapsed/accentDuration);
                    animator.SetFloat("RecordedMotionPhase",AccentPhase);
                    // The recorded weight shift yields to the existing broad gait as movement reaches full speed.
                    if(!returningToGait&&AccentPhase>=.4f)
                    {
                        float phase=CurrentAccent=="CMU_Start"?.21666667f:CurrentAccent=="CMU_TurnLeft90"?.68333333f:.65f;
                        float offset=player.CurrentState=="Walk"?walkClipLength*phase:0;
                        animator.CrossFadeInFixedTime(player.CurrentState,Mathf.Max(0f,accentDuration-accentElapsed),0,offset);
                        returningToGait=true;
                    }
                    if(AccentPhase>=1f)CurrentAccent="";
                }
                bool steady=moving&&speed>.5f&&player.CurrentState=="Walk"&&CurrentAccent.Length==0&&
                    Mathf.Abs(angle)<20f&&Mathf.Abs(Mathf.DeltaAngle(previousYaw,transform.eulerAngles.y))/Mathf.Max(.001f,dt)<30f;
                UpdateRecordedVariation(steady,dt);
            }
            previousIntent=intent;previousMotionState=player.CurrentState;previousMotionSpeed=speed;
        }

        void BeginAccent(string state,float duration)
        {
            if(!player.animator.HasState(0,Animator.StringToHash(state)))return;
            if(CurrentAccent.Length>0)AccentsInterrupted++;
            ClearVariation();returningToGait=false;CurrentAccent=state;accentDuration=Mathf.Max(.01f,duration);accentElapsed=AccentPhase=0;
            player.animator.SetFloat("RecordedMotionPhase",0f);
            player.animator.CrossFadeInFixedTime(state,.05f,0,0f);AccentsStarted++;
        }

        void CancelAccent(bool restore)
        {
            if(CurrentAccent.Length==0)return;
            string cancelled=CurrentAccent;CurrentAccent="";AccentPhase=0;AccentsInterrupted++;
            if(restore&&player&&player.animator&&player.animator.isActiveAndEnabled)
            {
                float phase=cancelled=="CMU_Start"?.21666667f:cancelled=="CMU_TurnLeft90"?.68333333f:.65f;
                if(returningToGait&&player.animator.IsInTransition(0))phase=Mathf.Repeat(player.animator.GetNextAnimatorStateInfo(0).normalizedTime,1f);
                player.animator.CrossFadeInFixedTime(player.CurrentState,.04f,0,player.CurrentState=="Walk"?walkClipLength*phase:0);
            }
        }

        void ClearVariation()
        {
            if(observing){observing=false;ObservationsInterrupted++;Schedule();}
            VariationWeight=ObservationWeight=lookWeight=0;
            if(variationLayer>=0&&player&&player.animator)player.animator.SetLayerWeight(variationLayer,0);
        }

        bool CanBeginGaitHop()=>isActiveAndEnabled && recordedMotionEnabled && fullBodyHopsEnabled &&
            player && player.isActiveAndEnabled && Vector3.ProjectOnPlane(player.ActualVelocity,Vector3.up).magnitude>.2f && !InWater();

        void UpdateRecordedVariation(bool steady,float dt)
        {
            if(fullBodyHopsEnabled)
            {
                if(observing){observing=false;VariationWeight=ObservationWeight=0;Schedule();}
                if(!steady)return;
                if(Time.time>=nextObservation && player.TryBeginGaitHops(CanBeginGaitHop))
                {observing=true;ObservationsStarted++;ObservationWeight=VariationWeight=0;}
                return;
            }
            if(!steady||variationLayer<0){ClearVariation();return;}
            if(!observing&&Time.time>=nextObservation)
            {
                observing=true;observationTime=0;ObservationsStarted++;
                player.animator.Play("CMU_Happy",variationLayer,0);
            }
            if(!observing)return;
            observationTime+=dt;
            float phase=Mathf.Clamp01(observationTime/1.1f);
            ObservationWeight=Mathf.Sin(phase*Mathf.PI);
            VariationWeight=recordedVariationWeight*ObservationWeight;
            player.animator.SetFloat("RecordedVariationPhase",phase);
            player.animator.SetLayerWeight(variationLayer,VariationWeight);
            if(phase>=1f){observing=false;ClearVariation();Schedule();}
        }

        void LateUpdate()
        {
            if(player==null||chest==null||head==null)return;
            if(recordedMotionEnabled&&!player.isActiveAndEnabled)
            {
                CancelAccent(true);ClearVariation();RestoreSupport();
                if(player.capeCloth)player.capeCloth.PresentationOpenness=0;
                return;
            }
            ApplyTransitionSupport();
            float dt=Time.deltaTime;if(dt<=0)return;
            float speed=Vector3.ProjectOnPlane(player.ActualVelocity,Vector3.up).magnitude;
            float yaw=transform.eulerAngles.y;
            float yawRate=Mathf.DeltaAngle(previousYaw,yaw)/dt;
            float acceleration=(speed-previousSpeed)/dt;
            bool canObserve=!recordedMotionEnabled&&player.CurrentState=="Walk"&&player.IsGrounded&&speed>.5f&&!player.InputBlocked&&!player.JumpBlocked&&Mathf.Abs(yawRate)<30;
            if(!recordedMotionEnabled&&observing&&!canObserve)
            {observing=false;ObservationsInterrupted++;Schedule();}
            if(!observing&&canObserve&&Time.time>=nextObservation)
            {observing=true;observationTime=0;observationSide=Random.value<.5f?-1:1;ObservationsStarted++;}
            float target=0;
            if(!recordedMotionEnabled&&observing)
            {
                observationTime+=dt;
                target=Mathf.Sin(Mathf.Clamp01(observationTime/1.1f)*Mathf.PI);
                if(observationTime>=1.1f){observing=false;Schedule();}
            }
            lookWeight=Mathf.MoveTowards(lookWeight,target,dt*(canObserve?7f:20f));
            if(!recordedMotionEnabled)ObservationWeight=lookWeight;
            if(player.capeCloth)player.capeCloth.PresentationOpenness=recordedMotionEnabled&&!InWater()?ObservationWeight:0;
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
