using UnityEngine;

namespace WeatherHouse.Hillside
{
    public static class AnimalWorkBudget
    {
        public enum Kind { Decision, Planning }
        static int frame = -1, depth, generation, activeKind;
        static readonly int[] work=new int[2];
        static readonly double[] spent=new double[2];
        static readonly System.Collections.Generic.List<int> decisionWaiters=new System.Collections.Generic.List<int>();
        static readonly System.Collections.Generic.Dictionary<int,int> decisionWaitingFrames=new System.Collections.Generic.Dictionary<int,int>();
        static bool decisionReserved;
        static int reservedDecisionOwner;
        static double start;
        static void ResetFrame(int value)
        {
            frame=value;depth=0;System.Array.Clear(work,0,2);System.Array.Clear(spent,0,2);generation++;
            decisionReserved=false;
            while(decisionWaiters.Count>0)
            {
                int owner=decisionWaiters[0];decisionWaiters.RemoveAt(0);
                if(!decisionWaitingFrames.TryGetValue(owner,out int seen))continue;
                decisionWaitingFrames.Remove(owner);
                // Disabled/destroyed producers stop refreshing their request; an absent turn lasts at most one frame.
                if(value-seen>2)continue;
                reservedDecisionOwner=owner;decisionReserved=true;break;
            }
        }
#if UNITY_EDITOR
        static System.Func<double> testClock;
        static System.Func<int> testFrame;
        internal static void SetTestClock(System.Func<double> clock, System.Func<int> frameClock)
        {
            testClock=clock;testFrame=frameClock;decisionWaiters.Clear();decisionWaitingFrames.Clear();ResetFrame(-1);
        }
#endif
        static double Now
        {
            get
            {
#if UNITY_EDITOR
                if(testClock!=null)return testClock();
#endif
                return System.Diagnostics.Stopwatch.GetTimestamp()*1000d/System.Diagnostics.Stopwatch.Frequency;
            }
        }
        static bool PrepareFrame()
        {
            int currentFrame;
#if UNITY_EDITOR
            if(testClock!=null)currentFrame=testFrame();
            else
#endif
            {
                if(!Application.isPlaying)return false;
                currentFrame=Time.frameCount;
            }
            if(frame!=currentFrame)ResetFrame(currentFrame);
            return true;
        }
        // A producer denied this frame gets a FIFO turn before an earlier-updating population can consume the next frame.
        // Once that one start is granted, the existing shared count/time limits still govern all remaining work.
        public static bool TryBeginDecision(int owner,out Scope scope)
        {
            scope=default;if(!PrepareFrame())return true;
            if(depth>0)return TryBegin(Kind.Decision,out scope);
            if(decisionReserved && reservedDecisionOwner!=owner){WaitForDecision(owner);return false;}
            if(!TryBegin(Kind.Decision,out scope)){WaitForDecision(owner);return false;}
            // Another owner can also be served from this frame's remainder; remove its now-obsolete queued turn.
            decisionWaitingFrames.Remove(owner);decisionWaiters.Remove(owner);
            decisionReserved=false;return true;
        }
        static void WaitForDecision(int owner)
        {
            if(!decisionWaitingFrames.ContainsKey(owner))decisionWaiters.Add(owner);
            decisionWaitingFrames[owner]=frame;
        }
        // Each producer class gets 4 starts/1ms: ordinary decisions cannot consume planning's share.
        // Only scoped work is charged; a synchronous unit may overrun its own soft time limit.
        public static bool TryBegin(Kind kind,out Scope scope)
        {
            scope=default;if(!PrepareFrame())return true;
            if(depth==0)
            {
                int lane=(int)kind;
                if(work[lane]>=4 || spent[lane]>=1)return false;
                activeKind=lane;work[lane]++;start=Now;
            }
            depth++;scope=new Scope(generation);return true;
        }
        public struct Scope : System.IDisposable
        {
            readonly int token;
            bool active;
            internal Scope(int token){this.token=token;active=true;}
            public void Dispose()
            {
                if(!active)return;active=false;
                if(token!=generation || depth==0)return;
                if(--depth==0)spent[activeKind]+=System.Math.Max(0,Now-start);
            }
        }
    }
}
