using UnityEngine;

namespace ForestSpriteCharacter
{
    // Reads the already animated feet only at takeoff/contact; never writes bones or motor state.
    [DefaultExecutionOrder(-140),DisallowMultipleComponent]
    public sealed class MageJumpContinuity:MonoBehaviour
    {
        public ForestSpriteDemoController player;
        public Transform leftFoot,rightFoot;
        public MageJumpContinuityData poses;
        public float SelectedLead {get;private set;}
        public int TakeoffSupport {get;private set;}
        public float TakeoffSpread {get;private set;}
        public float LandingPhase {get;private set;}
        public float LandingPoseError {get;private set;}
        bool bound;
        static readonly int JumpLead=Animator.StringToHash("JumpLead");
        void OnEnable()=>Bind();
        void Start()=>Bind();
        void Bind()
        {
            if(bound||!player||!leftFoot||!rightFoot||!poses)return;
            if(player.PrepareJumpPose!=null||player.LandingPoseTime!=null)
                throw new System.InvalidOperationException("Character jump pose handlers already assigned");
            player.PrepareJumpPose=Prepare;player.LandingPoseTime=LandingTime;bound=true;
        }
        void OnDisable()
        {
            if(!bound||!player)return;
            if(player.PrepareJumpPose==Prepare)player.PrepareJumpPose=null;
            if(player.LandingPoseTime==LandingTime)player.LandingPoseTime=null;
            bound=false;
        }
        void Prepare()
        {
            var l=transform.InverseTransformPoint(leftFoot.position);var r=transform.InverseTransformPoint(rightFoot.position);
            TakeoffSpread=l.z-r.z;
            TakeoffSupport=Mathf.Abs(l.y-r.y)<.008f?0:l.y<r.y?-1:1;
            // Choose one authored supported pose. Blending two planted leg rotations inside
            // a BlendTree lengthens the legs and bypasses Animator transition support.
            SelectedLead=player.MovementIntent.sqrMagnitude<=.001f?0:
                Mathf.Abs(TakeoffSpread)>=poses.takeoffSpread*.16f?Mathf.Sign(TakeoffSpread):
                TakeoffSupport<0?-1:TakeoffSupport>0?1:0;
            player.animator.SetFloat(JumpLead,SelectedLead);
        }
        float LandingTime(string state)
        {
            var samples=state=="Run"?poses.run:poses.walk;
            if(samples==null||samples.Length==0)return 0;
            var l=transform.InverseTransformPoint(leftFoot.position);var r=transform.InverseTransformPoint(rightFoot.position);
            int best=0;float error=float.PositiveInfinity;
            for(int i=0;i<samples.Length;i++)
            {
                var dl=samples[i].left-l;var dr=samples[i].right-r;
                float e=dl.sqrMagnitude+dr.sqrMagnitude;
                if(e<error){error=e;best=i;}
            }
            LandingPhase=(float)best/samples.Length;LandingPoseError=Mathf.Sqrt(error);
            return LandingPhase*(state=="Run"?poses.runDuration:poses.walkDuration);
        }
    }
}
