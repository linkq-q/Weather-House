using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using ForestSpriteCharacter;
using UnityEngine;
using WeatherHouse.Hillside;
using WeatherHouse.WatersideV2;
using WeatherHouse.WildlifeJourney;
using Object = UnityEngine.Object;

namespace WeatherHouse.Review
{
    // Failure-only observation. Never calls Move, constraints with counters, or a camera render.
    public static class RuralTraversalFailureDump
    {
        const BindingFlags Fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        [Serializable] public sealed class ColliderInfo
        {
            public int id, layer; public string path, scene, type, layerName, rigidbody, ownership;
            public bool enabled, active, trigger, playerOwned, ignoredLayerPair, ignoredColliderPair;
            public Bounds bounds; public Vector3 position, scale; public float contactOffset;
        }
        [Serializable] public sealed class Hit
        { public ColliderInfo collider; public Vector3 point, normal; public float distance; }
        [Serializable] public sealed class Query
        {
            public string name; public Vector3 bottomCenter, topCenter, displacement; public float radius;
            public List<ColliderInfo> overlaps = new List<ColliderInfo>(); public List<Hit> sweep = new List<Hit>();
        }
        [Serializable] public sealed class Depth
        {
            public string name; public Vector3 point, rayOrigin; public float sole, supportedDepth, terrainY, waterY, rayDistance;
            public bool contains; public List<Hit> supportHits = new List<Hit>();
        }
        [Serializable] public sealed class Animal
        {
            public string population, path, owner, state; public bool active, external, measured;
            public Vector3 position, envelopeCenter, envelopeHalf; public float radius, bottom, top, horizontalDistance, clearance;
            public float predictedFraction; public bool predictedVertical, predictedHorizontal;
        }
        [Serializable] public sealed class Report
        {
            public string reason, leg, utc, screenshot, screenshotError, waterDelegate, animalDelegate, nativeFlags, animalFlags;
            public string basis = "Read-only failure snapshot before normal cleanup. Physics queries use the actual scaled capsule and include all layers/triggers; self/ignored pairs are labelled, not silently removed. Predictions reconstruct one next substep from current input/state, not a recording of the last Move. Water replay uses the pure static algorithm; animal predictions use the existing pure ContactFraction per registered body, not sequential constraint replay. Game image reads the existing latest buffer, without rendering/reframing. No movement, collision, path, threshold or production budget is changed.";
            public int frame, pointIndex, routeCount, waterBlockedSteps, waterBlockedStepsAfter;
            public bool grounded, nativeGrounded, inputBlocked, jumpBlocked, scriptedInput, scriptedRun, waterEnabled, animalEnabled, hasSafeWaterPosition;
            public bool stateUnchanged; public Vector2 scriptedMovement;
            public Vector3 position, target, velocity, capsuleCenter, capsuleScale, waterSafePosition, requestedStep, afterWaterStep, cameraPosition;
            public Quaternion cameraRotation;
            public float deltaTime, substep, verticalSpeed, radius, height, skinWidth, stepOffset, slopeLimit, minMoveDistance, maximumDepth, lastRejectedDepth, predictedRejectedDepth;
            public List<Query> capsuleQueries = new List<Query>(); public List<Depth> depths = new List<Depth>();
            public List<Animal> animals = new List<Animal>(); public List<string> errors = new List<string>();
        }
        static T Read<T>(object target, string name) => (T)target.GetType().GetField(name, Fields).GetValue(target);
        static string PathOf(Transform t) => t ? (t.parent ? PathOf(t.parent) + "/" : "") + t.name + "#" + t.GetInstanceID() : "null";
        static string Owner(Object o) => o ? o.GetType().FullName + ":" + (o is Component c ? PathOf(c.transform) : o.name) : "null";
        static string DelegateOf(Delegate d) => d == null ? "null" : string.Join(";", d.GetInvocationList().Select(x => x.Method.DeclaringType.FullName + "." + x.Method.Name + " target=" + (x.Target is Object o ? Owner(o) : x.Target?.GetType().FullName)));
        static ColliderInfo Describe(Collider c, CharacterController body)
        {
            var parents = c.GetComponentsInParent<Component>(true).Where(x => x && (x is WildlifeAgent || x is WildlifePopulation || x.GetType().Name == "JourneyHerd"));
            return new ColliderInfo { id=c.GetInstanceID(),path=PathOf(c.transform),scene=c.gameObject.scene.path,type=c.GetType().FullName,
                layer=c.gameObject.layer,layerName=LayerMask.LayerToName(c.gameObject.layer),enabled=c.enabled,active=c.gameObject.activeInHierarchy,
                trigger=c.isTrigger,playerOwned=c.transform.IsChildOf(body.transform),ignoredLayerPair=Physics.GetIgnoreLayerCollision(body.gameObject.layer,c.gameObject.layer),
                ignoredColliderPair=c!=body&&Physics.GetIgnoreCollision(body,c),bounds=c.bounds,position=c.transform.position,scale=c.transform.lossyScale,
                contactOffset=c.contactOffset,rigidbody=c.attachedRigidbody?Owner(c.attachedRigidbody)+" kinematic="+c.attachedRigidbody.isKinematic:"none",
                ownership=string.Join(";",parents.Select(Owner)) };
        }
        static Hit Describe(RaycastHit h, CharacterController body) => new Hit { collider=Describe(h.collider,body),point=h.point,normal=h.normal,distance=h.distance };
        static void QueryCapsule(Report r, CharacterController body, string name, Vector3 delta)
        {
            float half=Mathf.Max(r.radius,r.height*.5f); Vector3 arm=Vector3.up*(half-r.radius);
            var q=new Query { name=name,bottomCenter=r.capsuleCenter-arm,topCenter=r.capsuleCenter+arm,radius=r.radius,displacement=delta };
            foreach(var c in Physics.OverlapCapsule(q.bottomCenter,q.topCenter,q.radius,~0,QueryTriggerInteraction.Collide)) q.overlaps.Add(Describe(c,body));
            if(delta.sqrMagnitude>0.00000001f)
                foreach(var h in Physics.CapsuleCastAll(q.bottomCenter,q.topCenter,q.radius,delta.normalized,delta.magnitude,~0,QueryTriggerInteraction.Collide).OrderBy(x=>x.distance)) q.sweep.Add(Describe(h,body));
            r.capsuleQueries.Add(q);
        }
        static void SampleDepth(Report r, WatersideWaterBoundary water, CharacterController body, string name, Vector3 at)
        {
            float sole=body.bounds.min.y,reach=body.stepOffset+body.skinWidth+.05f;
            float drop=water.player&&!water.player.IsGrounded?Mathf.Max(.24f,water.player.jumpHeight+body.skinWidth+.05f):.24f;
            var d=new Depth { name=name,point=at,sole=sole,contains=water.surface.Contains(at),supportedDepth=water.SampleSupportedDepth(at,sole),
                terrainY=water.terrain.SampleHeight(at)+water.terrain.transform.position.y,waterY=water.surface.Height(at),
                rayOrigin=new Vector3(at.x,sole+reach,at.z),rayDistance=reach+drop };
            foreach(var h in Physics.RaycastAll(d.rayOrigin,Vector3.down,d.rayDistance,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore).OrderBy(x=>x.distance)) d.supportHits.Add(Describe(h,body));
            r.depths.Add(d);
        }
        static void SampleAnimals(Report r, WildlifePlayerCollision contact)
        {
            if(!contact)return;
            var fraction=typeof(WildlifePlayerCollision).GetMethod("ContactFraction",BindingFlags.NonPublic|BindingFlags.Static);
            var populations=Read<List<WildlifePopulation>>(contact,"populations");
            foreach(var population in populations)
            {
                if(!population)continue;
                if(population.agents!=null)foreach(var agent in population.agents)
                    if(agent)Add(population,agent,agent.transform,agent.Radius,agent.BodyEnvelope,agent.isActiveAndEnabled,false,agent.speciesId+" "+agent.State);
                foreach(object entry in Read<IList>(population,"externalBodies"))
                {
                    var owner=Read<Object>(entry,"owner");var root=Read<Transform>(entry,"root");
                    var member=owner is JourneyHerd herd?herd.members?.FirstOrDefault(m=>m!=null&&m.root==root):null;
                    string state=member==null?"external registered body":member.State+" "+member.Mode+" speed="+member.ActualSpeed+" "+member.LastDecision;
                    if(root)Add(population,owner,root,Read<float>(entry,"radius"),Read<WildlifeBodyEnvelope>(entry,"envelope"),
                        owner&&(!(owner is Behaviour b)||b.isActiveAndEnabled),true,state);
                }
            }
            void Add(WildlifePopulation population,Object owner,Transform root,float radius,WildlifeBodyEnvelope envelope,bool active,bool external,string state)
            {
                bool measured=envelope.Valid&&envelope.measured;
                float actualRadius=measured?envelope.HorizontalRadius:radius;
                var a=new Animal { population=PathOf(population.transform),path=PathOf(root),owner=Owner(owner),state=state,external=external,measured=measured,
                    active=contact.isActiveAndEnabled&&population.isActiveAndEnabled&&active&&root.gameObject.activeInHierarchy,
                    position=root.position,envelopeCenter=envelope.center,envelopeHalf=envelope.half,radius=actualRadius,
                    bottom=root.position.y+(measured?envelope.Bottom:0),top=root.position.y+(measured?envelope.Top:radius*2),
                    horizontalDistance=Vector2.Distance(new Vector2(r.capsuleCenter.x,r.capsuleCenter.z),new Vector2(root.position.x,root.position.z)) };
                a.clearance=a.horizontalDistance-actualRadius-r.radius-.12f;
                float half=Mathf.Max(r.radius,r.height*.5f);
                object[] args={r.capsuleCenter,r.afterWaterStep,r.capsuleCenter.y-half,r.capsuleCenter.y+half,root.position,a.bottom,a.top,actualRadius+r.radius+.12f,false,false};
                a.predictedFraction=(float)fraction.Invoke(null,args);a.predictedVertical=(bool)args[8];a.predictedHorizontal=(bool)args[9];r.animals.Add(a);
            }
        }
        public static void Save(string folder,ForestSpriteDemoController player,CharacterController body,WatersideWaterBoundary water,Camera camera,string leg,int pointIndex,int routeCount,Vector3 target,string reason)
        {
            var r=new Report { utc=DateTime.UtcNow.ToString("O"),frame=Time.frameCount,reason=reason,leg=leg,pointIndex=pointIndex,routeCount=routeCount,target=target };
            Vector3 originalPosition=player.transform.position;Quaternion originalRotation=player.transform.rotation;
            var originalWater=player.ConstrainDisplacement;var originalAnimal=player.ConstrainAnimalDisplacement;
            var originalFlags=player.AnimalCollisionFlags;int originalCount=water?water.BlockedStepCount:0;
            void Observe(Action action){try{action();}catch(Exception e){r.errors.Add(e.ToString());}}
            Observe(()=>
            {
                r.position=originalPosition;r.velocity=player.ActualVelocity;r.grounded=player.IsGrounded;r.nativeGrounded=body.isGrounded;
                r.nativeFlags=body.collisionFlags.ToString();r.animalFlags=originalFlags.ToString();r.inputBlocked=player.InputBlocked;r.jumpBlocked=player.JumpBlocked;
                r.scriptedInput=player.ScriptedInput;r.scriptedMovement=Read<Vector2>(player,"scriptedMovement");r.scriptedRun=Read<bool>(player,"scriptedRun");
                r.waterDelegate=DelegateOf(originalWater);r.animalDelegate=DelegateOf(originalAnimal);r.verticalSpeed=player.VerticalSpeed;
                r.capsuleCenter=body.transform.TransformPoint(body.center);r.capsuleScale=body.transform.lossyScale;
                r.radius=body.radius*Mathf.Max(Mathf.Abs(r.capsuleScale.x),Mathf.Abs(r.capsuleScale.z));r.height=body.height*Mathf.Abs(r.capsuleScale.y);
                r.skinWidth=body.skinWidth;r.stepOffset=body.stepOffset;r.slopeLimit=body.slopeLimit;r.minMoveDistance=body.minMoveDistance;
                r.deltaTime=Time.deltaTime;r.substep=r.deltaTime/Mathf.Max(1,Mathf.CeilToInt(r.deltaTime/.05f));
                var moveCamera=player.MovementCameraOverride?player.MovementCameraOverride:player.demoCamera;
                Vector3 forward=moveCamera?Vector3.ProjectOnPlane(moveCamera.transform.forward,Vector3.up).normalized:Vector3.forward;
                Vector3 direction=r.inputBlocked?Vector3.zero:Vector3.ClampMagnitude(forward*r.scriptedMovement.y+Vector3.Cross(Vector3.up,forward)*r.scriptedMovement.x,1);
                float vertical=r.grounded&&r.verticalSpeed<0?-2:r.verticalSpeed;
                r.requestedStep=(direction*(r.scriptedRun?player.runSpeed:player.walkSpeed)+Vector3.up*(vertical+player.gravity*r.substep))*r.substep;
                r.afterWaterStep=r.requestedStep;
            });
            Observe(()=>
            {
                if(!water||!water.surface||!water.terrain)return;
                r.waterEnabled=water.isActiveAndEnabled;r.waterBlockedSteps=water.BlockedStepCount;r.lastRejectedDepth=water.LastRejectedDepth;
                r.maximumDepth=water.maximumWadingDepth;r.hasSafeWaterPosition=water.HasSafePosition;r.waterSafePosition=water.LastSafePosition;
                SampleDepth(r,water,body,"current",r.position);SampleDepth(r,water,body,"next_substep",r.position+r.requestedStep);SampleDepth(r,water,body,"target",target);
                Vector3 allowed=WatersideWaterBoundary.ConstrainWadingStep(r.position,r.requestedStep,r.maximumDepth,
                    p=>water.SampleSupportedDepth(p,body.bounds.min.y),out r.predictedRejectedDepth);
                // A pure hypothetical sample, even if another delegate currently owns the actual constraint.
                r.afterWaterStep=new Vector3(allowed.x,r.requestedStep.y,allowed.z);
            });
            Observe(()=>{QueryCapsule(r,body,"requested_next_substep",r.requestedStep);QueryCapsule(r,body,"water_predicted_substep",r.afterWaterStep);
                Vector3 delta=target-r.position;delta.y=0;QueryCapsule(r,body,"horizontal_to_current_waypoint",delta);});
            Observe(()=>{var contact=player.GetComponent<WildlifePlayerCollision>();r.animalEnabled=contact&&contact.isActiveAndEnabled;SampleAnimals(r,contact);});
            // Synchronous read of the already-rendered buffer, before Finish changes the camera or exits Play.
            Observe(()=>
            {
                r.cameraPosition=camera.transform.position;r.cameraRotation=camera.transform.rotation;
                Texture2D image=null;
                try{image=ScreenCapture.CaptureScreenshotAsTexture();if(!image||image.width<2||image.height<2)throw new InvalidOperationException("No existing Game buffer.");
                    r.screenshot=System.IO.Path.Combine(folder,"failure_Game.png");File.WriteAllBytes(r.screenshot,image.EncodeToPNG());}
                catch(Exception e){r.screenshotError=e.ToString();}
                finally{if(image)Object.DestroyImmediate(image);}
            });
            r.waterBlockedStepsAfter=water?water.BlockedStepCount:0;
            r.stateUnchanged=player.transform.position.Equals(originalPosition)&&player.transform.rotation.Equals(originalRotation)&&player.ConstrainDisplacement==originalWater&&player.ConstrainAnimalDisplacement==originalAnimal&&player.AnimalCollisionFlags==originalFlags&&r.waterBlockedStepsAfter==originalCount;
            File.WriteAllText(System.IO.Path.Combine(folder,"failure-diagnostics.json"),JsonUtility.ToJson(r,true));
        }
    }
}
