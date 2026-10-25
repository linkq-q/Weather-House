using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
using WeatherHouse.Hillside;
using WeatherHouse.WildlifeJourney;

namespace WeatherHouse.Review
{
    // Read-only snapshots. The existing social review owns the Play/editor/camera lifecycle.
    public static class JourneySocialAvailabilityProbe
    {
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        static readonly MethodInfo CanSocialize = typeof(JourneyHerd).GetMethod("CanSocialize", Private);
        static readonly MethodInfo CanSocialPose = typeof(JourneyHerd).GetMethod("CanSocialPose", Private);
        static StreamWriter writer;
        static float nextSample;
        static int lastSequence, lastFirst, lastFrame;
        [Serializable] public sealed class Lifecycle
        {
            public string version="AvailabilityV2", assembly, error="";
            public int calls, snapshots, duplicateFrames, throttled, lastSeenFrame;
            public bool ended, writerReady, herdPresent, socialPresent;
        }
        static Lifecycle lifecycle;
        static string lifecyclePath;
        static void SaveLifecycle(){if(lifecycle!=null&&lifecyclePath!=null)File.WriteAllText(lifecyclePath,JsonUtility.ToJson(lifecycle,true));}
        [Serializable] public sealed class Snapshot
        {
            public int frame, sequence, first, second, completed, pendingMember;
            public float time, preparationUntil, nextConversation, stayUntil;
            public bool preparing, planning, travelling, hasActiveConversation;
            public string intent, groupDecision;
            public Member[] members;
        }
        [Serializable] public sealed class Member
        {
            public int id, bridge, segment, crowdCount, bodyStepPhase;
            public string state, mode, decision, proposedPoseReason, currentPoseReason;
            public bool availableNow, availableLastTick, arrived, currentPoseAllowed, proposedPoseAllowed;
            public float speed, partnerDistance, facingDot;
            public Vector3 position, envelopeCenter, envelopeHalf;
            public Quaternion rotation, proposedRotation;
        }
        public static void Begin(string directory, JourneyHerd herd)
        {
            End(); writer = new StreamWriter(Path.Combine(directory, "social-availability.jsonl"));
            nextSample = 0; lastSequence = lastFirst = -2; lastFrame = -1;
            lifecyclePath=Path.Combine(directory,"social-availability-lifecycle.json");
            lifecycle=new Lifecycle{assembly=typeof(JourneySocialAvailabilityProbe).Assembly.FullName,writerReady=writer!=null};
            SaveLifecycle(); Sample(herd);
        }
        public static void Sample(JourneyHerd herd)
        {
            if(lifecycle==null||writer==null)throw new InvalidOperationException("AvailabilityV2 Sample called without an open writer");
            lifecycle.calls++; lifecycle.lastSeenFrame=Time.frameCount; lifecycle.herdPresent=herd; lifecycle.socialPresent=herd&&herd.social;
            if(!herd||!herd.social)
            {
                lifecycle.error="AvailabilityV2 lost herd or herd.social"; SaveLifecycle(); throw new InvalidOperationException(lifecycle.error);
            }
            if(Time.frameCount>=0&&Time.frameCount==lastFrame){lifecycle.duplicateFrames++;return;}
            var social = herd.social; int sequence = Get<int>(social, "sequence");
            if (Time.realtimeSinceStartup < nextSample && sequence == lastSequence && social.FirstParticipantId == lastFirst) {lifecycle.throttled++;return;}
            nextSample = Time.realtimeSinceStartup + .5f; lastSequence = sequence; lastFirst = social.FirstParticipantId; lastFrame = Time.frameCount;
            var pending = Get<object>(herd, "pending"); var participants = Get<JourneySocialParticipant[]>(social, "participants");
            var s = new Snapshot { frame = Time.frameCount, time = herd.SimulationTime, sequence = sequence, first = social.FirstParticipantId,
                second = social.SecondParticipantId, completed = social.Completed.Count, preparing = Get<bool>(social, "preparing"),
                preparationUntil = Get<float>(social, "preparationUntil"), nextConversation = Get<float>(social, "nextConversation"),
                stayUntil = Get<float>(herd, "stayUntil"), planning = herd.IsPlanning, pendingMember = pending == null ? -2 : (int)pending.GetType().GetField("member").GetValue(pending),
                travelling = herd.Travelling, hasActiveConversation = social.ActiveConversation != null, intent = social.CurrentIntent, groupDecision = herd.GroupDecision };
            s.members = herd.members.Where(m => m != null && m.root && m.root.gameObject.activeInHierarchy).Select(m =>
            {
                var row = new Member { id = m.id, state = m.State.ToString(), mode = m.Mode.ToString(), arrived = m.Arrived, speed = m.ActualSpeed,
                    position = m.root.position, rotation = m.root.rotation, envelopeCenter = m.profile.bodyEnvelope.center, envelopeHalf = m.profile.bodyEnvelope.half,
                    decision = m.LastDecision, bridge = Get<int>(m, "bridge"), segment = Get<int>(m, "segment"), bodyStepPhase = Get<int>(m, "bodyStepPhase"),
                    crowdCount = Get<System.Collections.Generic.List<Vector3>>(m, "crowdPath")?.Count ?? 0,
                    availableNow = (bool)CanSocialize.Invoke(herd, new object[] { m }), availableLastTick = participants.Any(p => p.id == m.id && p.available),
                    proposedRotation = m.root.rotation, facingDot = -2, partnerDistance = -1 };
                if (social.IsParticipating(m.id))
                {
                    int partnerId = s.first == m.id ? s.second : s.first;
                    var partner = Array.Find(herd.members, p => p != null && p.id == partnerId && p.root);
                    if (partner != null)
                    {
                        Vector3 direction = partner.root.position - m.root.position; direction.y = 0;
                        row.partnerDistance = direction.magnitude; row.facingDot = direction.sqrMagnitude < .001f ? 1 : Vector3.Dot(m.root.forward, direction.normalized);
                        if (direction.sqrMagnitude > .001f)
                        {
                            Quaternion facing = Quaternion.LookRotation(direction); var c = social.ActiveConversation;
                            if (c != null && social.CurrentIntent == "greeting")
                            {
                                var pose = JourneySocialGesture.Sample("greeting", herd.SimulationTime-c.startedAt, c.endsAt-c.startedAt, c.speakerId==m.id, m.id%2==0?-1:1);
                                facing *= Quaternion.Euler(0, -pose.angles.y, 0);
                            }
                            row.proposedRotation = Quaternion.RotateTowards(m.root.rotation, facing, Time.deltaTime * 100f);
                        }
                    }
                }
                row.currentPoseAllowed = (bool)CanSocialPose.Invoke(herd, new object[] { m, row.rotation });
                row.proposedPoseAllowed = (bool)CanSocialPose.Invoke(herd, new object[] { m, row.proposedRotation });
                row.currentPoseReason = row.currentPoseAllowed ? "clear" : Reason(herd, m, row.rotation);
                row.proposedPoseReason = row.proposedPoseAllowed ? "clear" : Reason(herd, m, row.proposedRotation);
                return row;
            }).ToArray();
            writer.WriteLine(JsonUtility.ToJson(s)); writer.Flush(); lifecycle.snapshots++; SaveLifecycle();
        }
        // Exact first-rejection ordering from current CanSocialPose; no scheduler or transform mutation.
        static string Reason(JourneyHerd herd, JourneyMember member, Quaternion rotation)
        {
            var navigation=herd.navigation;
            if (!navigation || member.Mode!=JourneyTravelMode.Walk || !member.profile.bodyEnvelope.Valid) return "navigation/mode/envelope";
            var envelope=member.profile.bodyEnvelope;
            Bounds body=JourneySocialGesture.RequiredBody(envelope.center,envelope.half);
            body=JourneySocialGesture.SweptBody(body,member.root.rotation,rotation,out Quaternion midpoint);
            var surface=navigation.SampleSurface(member.root.position);
            if(!surface.valid||surface.blocked||surface.hasWater||surface.bridgeId!=0)
                return "surface valid="+surface.valid+" blocked="+surface.blocked+" water="+surface.hasWater+" waterY="+surface.waterY+" groundY="+surface.groundY+" bridge="+surface.bridgeId;
            var hits=Physics.OverlapBox(member.root.position+midpoint*body.center,body.extents,midpoint,navigation.obstacleMask,QueryTriggerInteraction.Ignore);
            if(hits.Length>=32)return "world overlap buffer full: "+hits.Length;
            foreach(var hit in hits)if(hit&&hit!=surface.supportingCollider&&!(hit is TerrainCollider)&&!hit.transform.IsChildOf(member.root))return "world: "+PathOf(hit.transform)+" "+hit.GetType().Name+" bounds="+hit.bounds;
            foreach(var other in herd.members)
            {
                if(other==null||!other.root||!other.root.gameObject.activeInHierarchy||other==member)continue;
                var e=other.profile.bodyEnvelope;
                if(!e.Valid||!JourneySocialGesture.Separated(member.root.position,midpoint,body,other.root.position,other.root.rotation,JourneySocialGesture.RequiredBody(e.center,e.half)))return "member: "+other.id+" valid="+e.Valid;
            }
            var query=new WildlifeBodyEnvelope{center=body.center,half=body.extents,measured=true};
            if(navigation.playerCollision&&navigation.playerCollision.TryObstacle(member.root.position,query.HorizontalRadius,out Vector3 player,out float radius,query)&&!JourneySocialGesture.ClearCircle(member.root.position,midpoint,body,player,radius))return "player";
            if(herd.surroundingPopulation&&herd.surroundingPopulation.agents!=null)foreach(var other in herd.surroundingPopulation.agents)
            {
                if(!other||!other.isActiveAndEnabled||other.transform==member.root||!(other.locomotion==WildlifeLocomotion.Ground||(other.locomotion==WildlifeLocomotion.Flying&&other.AirHeight<.01f)))continue;
                var e=other.BodyEnvelope;
                if(!e.Valid||!JourneySocialGesture.Separated(member.root.position,midpoint,body,other.transform.position,other.transform.rotation,new Bounds(e.center,e.half*2)))return "population: "+PathOf(other.transform)+" valid="+e.Valid;
            }
            return "guard rejected but classified clear; inspect source hash/timing";
        }
        static T Get<T>(object value,string name)=>(T)value.GetType().GetField(name,Private).GetValue(value);
        static string PathOf(Transform t)=>t.parent?PathOf(t.parent)+"/"+t.name:t.name;
        public static void End(){if(lifecycle!=null){lifecycle.ended=true;SaveLifecycle();}writer?.Dispose();writer=null;}
    }
}
