using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
using WeatherHouse.Hillside;

namespace WeatherHouse.Life.Editor
{
    public static class LifeWorldRouteAudit
    {
        const BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
        [Serializable] sealed class Row
        {
            public string key,label,reason,collider;
            public Vector3 from,to,failedPoint,bodyCenter,bodyHalf;
            public bool accepted;
        }
        [Serializable] sealed class Report
        {
            public string utc,boundary="Read-only live animal routes. No transforms, collision, scenes or population state changed.";
            public List<Row> routes=new List<Row>();
        }
        public static void Run()
        {
            LifeSession life=UnityEngine.Object.FindObjectOfType<LifeSession>();
            if(!Application.isPlaying || !life || !life.World)throw new InvalidOperationException("Route audit needs the initialized formal Play world.");
            var report=new Report{utc=DateTime.UtcNow.ToString("O")};
            foreach(string key in new[]{"squirrel","companion"})
            {
                WildlifeAgent animal=life.World.FindSpeaker(key).GetComponent<WildlifeAgent>();
                foreach(string id in new[]{"squirrel.nest","squirrel.common","squirrel.common.bowl"})report.routes.Add(Check(animal,key,id,life.World.AnchorPosition(id)));
                foreach(float radius in new[]{1.8f,2.6f,3.4f})for(int i=0;i<24;i++)
                {
                    Vector3 point=animal.transform.position+Quaternion.Euler(0,i*15,0)*Vector3.forward*radius;
                    report.routes.Add(Check(animal,key,"radial "+radius+" / "+i*15,point));
                }
            }
            Directory.CreateDirectory("LifeGameplayReview/20261026/play");
            File.WriteAllText("LifeGameplayReview/20261026/play/world-route-audit.json",JsonUtility.ToJson(report,true));
        }
        static Row Check(WildlifeAgent animal,string key,string label,Vector3 point)
        {
            var population=(WildlifePopulation)typeof(WildlifeAgent).GetField("population",Hidden).GetValue(animal);
            var physics=(WildlifeBodyPhysics)typeof(WildlifePopulation).GetField("bodyPhysics",Hidden).GetValue(population);
            FieldInfo observer=typeof(WildlifeBodyPhysics).GetField("ObstructionObserved",Hidden);
            object old=observer.GetValue(physics);Collider obstruction=null;
            observer.SetValue(physics,new Action<Collider>(c=>obstruction=c));
            var row=new Row{key=key,label=label,from=animal.transform.position,to=point,bodyCenter=animal.BodyEnvelope.center,bodyHalf=animal.BodyEnvelope.half};
            try
            {
                row.accepted=population.TryMove(animal,point,out Vector3 projected);
                if(row.accepted){row.to=projected;row.reason="clear";return row;}
                Quaternion facing=WildlifeBodyEnvelope.Heading(point-animal.transform.position,animal.transform.rotation);
                if(!population.CanRotate(animal,facing)){row.reason="initial turn";row.failedPoint=animal.transform.position;return row;}
                Vector3 previous=animal.transform.position;
                int steps=Mathf.Max(1,Mathf.CeilToInt(LifeWorld.Flat(previous,point)/Mathf.Max(.12f,animal.Radius)));
                for(int i=1;i<=steps;i++)
                {
                    Vector3 requested=Vector3.Lerp(animal.transform.position,point,(float)i/steps);row.failedPoint=requested;
                    if(!population.TryProjectPosition(animal,requested,out Vector3 sample)){row.reason="ground/water/slope projection";return row;}
                    row.failedPoint=sample;
                    if(Mathf.Abs(sample.y-previous.y)>.45f){row.reason="height step";return row;}
                    if(population.PlayerCollision && !population.PlayerCollision.AllowsAnimalMove(previous,sample,animal.Radius,animal.BodyEnvelope)){row.reason="player body";return row;}
                    if(!(bool)typeof(WildlifePopulation).GetMethod("ExternalBodiesAllowMove",Hidden).Invoke(population,new object[]{animal,previous,sample})){row.reason="external animal body";return row;}
                    if(!physics.Clear(animal.BodyEnvelope,previous,facing,sample,facing,population.obstacleMask,owner:animal.transform,player:population.PlayerCollision,ignoreAnimals:true)){row.reason="static body sweep";return row;}
                    foreach(WildlifeAgent other in population.agents)
                        if(other && other!=animal && other.isActiveAndEnabled && other.locomotion==WildlifeLocomotion.Ground &&
                            !WildlifeBodyPhysics.ClearOfBody(animal.BodyEnvelope,previous,facing,sample,facing,other.BodyEnvelope,other.transform.position,other.transform.rotation))
                        {row.reason="animal body: "+other.name;return row;}
                    previous=sample;
                }
                row.reason="other population constraint";return row;
            }
            finally
            {
                if(obstruction)row.collider=obstruction.name+" / "+obstruction.GetType().Name+" / "+obstruction.bounds;
                observer.SetValue(physics,old);
            }
        }
    }
}
