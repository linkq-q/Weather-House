using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using WeatherHousePrototype;
using WeatherHouse.Hillside;

namespace WeatherHouse.Life
{
    public static partial class LifePhotoCapture
    {
        static readonly ParticleSystem.Particle[] particles=new ParticleSystem.Particle[3200];
        static partial void CaptureWorld(LifeSession session,Camera camera,LifePhotoSnapshot snapshot)
        {
            var entities=new List<LifePhotoEntity>();LifeWorld world=session.World;
            snapshot.contexts=world.PhotoContextsAtShutter();
            foreach(LifeObject obj in world.Objects)
            {
                if(!obj || !obj.gameObject.activeInHierarchy)continue;
                LifeObjectState state=obj.State;
                string kind=state.kind==LifeObjectKind.GrassSource?"grass":state.kind==LifeObjectKind.Bundle?"bundle":
                    state.kind==LifeObjectKind.Mat?"mat":state.kind==LifeObjectKind.Bowl?"bowl":state.kind==LifeObjectKind.Ribbon?"ribbon":null;
                if(kind==null)continue;
                bool visible=obj.GetComponentsInChildren<Renderer>().Any(r=>LifePhotoVisibility.RendererVisible(camera,r));
                var entity=new LifePhotoEntity{id=state.id,kind=kind,position=obj.transform.position,bodyVisible=visible,
                    wetness=state.wetness,water=state.water,wetSurfaceVisible=visible && state.wetness>LifeObjectState.DryThreshold};
                if(obj.waterVisual && obj.waterVisual.gameObject.activeInHierarchy)
                    entity.waterSurfaceVisible=obj.waterVisual.GetComponentsInChildren<Renderer>().Any(r=>LifePhotoVisibility.RendererVisible(camera,r));
                if(obj.ribbon)
                {
                    entity.windStrength=obj.VisibleWindStrength;
                    entity.windEffectVisible=entity.windStrength>0 && obj.ribbon.GetComponentsInChildren<Renderer>().Any(r=>LifePhotoVisibility.RendererVisible(camera,r));
                }
                if(visible || entity.waterSurfaceVisible || entity.windEffectVisible)entities.Add(entity);
            }
            foreach(AnimalActor animal in UnityEngine.Object.FindObjectsOfType<AnimalActor>())
            {
                if(!animal.IsAvailableForObservation || !animal.VisualRoot)continue;
                Renderer[] renderers=animal.VisualRoot.GetComponentsInChildren<Renderer>();
                if(!LifePhotoVisibility.Body(camera,animal.transform,renderers))continue;
                var ecology=animal.GetComponent<WildlifeInteractionTarget>();var wildlife=animal.GetComponent<WildlifeAgent>();
                var entity=new LifePhotoEntity{id=animal.animalId,kind="animal",speciesId=ecology?ecology.SpeciesId:wildlife?wildlife.speciesId:animal.species.ToString().ToLowerInvariant(),
                    position=animal.transform.position,bodyVisible=true,sleeping=wildlife && wildlife.isActiveAndEnabled && wildlife.RestPhase==WildlifeRestPhase.Sleeping};
                world.AddPhotoAnimalState(animal,camera,entity);
                entity.environmentVisible=snapshot.contexts.Any(c=>c.actorId==entity.id && c.kind!="hello" && c.kind!="goodbye" &&
                    LifePhotoVisibility.Point(camera,c.anchor+Vector3.up*.12f));
                entities.Add(entity);
            }
            world.AddPhotoScenery(camera,entities);
            CaptureRain(camera,entities);
            CaptureWater(camera,entities);
            CaptureSunContrast(camera,entities);
            snapshot.entities=entities.ToArray();
        }
        static void CaptureRain(Camera camera,List<LifePhotoEntity> entities)
        {
            foreach(WeatherRegion region in UnityEngine.Object.FindObjectsOfType<WeatherRegion>())
            {
                if(!region.SoundActive || region.Current!=LocalWeather.Rain)continue;
                ParticleSystem rain=region.GetComponentsInChildren<ParticleSystem>().FirstOrDefault(p=>p.name=="Local Rain");
                if(!rain || !rain.isPlaying || !rain.GetComponent<Renderer>().enabled)continue;
                int count=rain.GetParticles(particles),visible=0;
                for(int i=0;i<count && visible<3;i++)
                {
                    Vector3 point=rain.main.simulationSpace==ParticleSystemSimulationSpace.World?particles[i].position:rain.transform.TransformPoint(particles[i].position);
                    if(particles[i].remainingLifetime>0 && LifePhotoVisibility.Point(camera,point))visible++;
                }
                if(visible>=3)entities.Add(new LifePhotoEntity{id="rain:"+region.GetInstanceID(),kind="weather",rainVisible=true,rainStrength=region.SoundStrength});
            }
        }
        static void CaptureWater(Camera camera,List<LifePhotoEntity> entities)
        {
            foreach(WorldWaterSurface water in UnityEngine.Object.FindObjectsOfType<WorldWaterSurface>())
            {
                Renderer[] renderers=water.GetComponentsInChildren<Renderer>();if(!renderers.Any(r=>r.enabled && r.gameObject.activeInHierarchy))continue;
                bool visible=false;Vector3 sample=default;
                for(int y=1;y<5 && !visible;y++)for(int x=1;x<5 && !visible;x++)
                {
                    Ray ray=camera.ViewportPointToRay(new Vector3(x/5f,y/5f,0));if(ray.direction.y>=-.02f)continue;
                    // The surface supplies its real shoreline/height. No lake-wide invisible reward flag.
                    float height=water.Height(camera.transform.position);float t=(height-ray.origin.y)/ray.direction.y;
                    if(t<=0 || t>80)continue;sample=ray.GetPoint(t);
                    if(water.TryGetHeight(sample,out float exact)){sample.y=exact;visible=LifePhotoVisibility.Point(camera,sample);}
                }
                if(visible)entities.Add(new LifePhotoEntity{id="water:"+water.gameObject.scene.name+":"+water.name,kind="water",position=sample,water=1,waterSurfaceVisible=true});
            }
        }
        static void CaptureSunContrast(Camera camera,List<LifePhotoEntity> entities)
        {
            var lights=new List<Light>();
            if(RenderSettings.sun)lights.Add(RenderSettings.sun);
            foreach(WeatherRegion region in UnityEngine.Object.FindObjectsOfType<WeatherRegion>())
                if(region.SoundActive && region.Current==LocalWeather.Sun)
                    lights.AddRange(region.GetComponentsInChildren<Light>().Where(l=>l.type==LightType.Spot));
            lights=lights.Where(l=>l && l.isActiveAndEnabled && l.intensity>=.15f).Distinct().ToList();
            if(lights.Count==0)return;
            var illuminated=new bool[lights.Count];var shaded=new bool[lights.Count];Vector3 litPoint=default;
            for(int y=1;y<6;y++)for(int x=1;x<6;x++)
            {
                Ray ray=camera.ViewportPointToRay(new Vector3(x/6f,y/6f,0));
                if(!Physics.Raycast(ray,out RaycastHit hit,60,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore))continue;
                if(hit.collider.GetComponentInParent<AnimalActor>())continue;
                for(int i=0;i<lights.Count;i++)
                {
                    Light light=lights[i];Vector3 direction;float distance;
                    bool inCone=true;
                    if(light.type==LightType.Directional){direction=-light.transform.forward;distance=100;}
                    else
                    {
                        Vector3 toLight=light.transform.position-hit.point;distance=toLight.magnitude;direction=toLight.normalized;
                        inCone=distance<light.range && Vector3.Angle(light.transform.forward,-direction)<light.spotAngle*.5f;
                    }
                    // Respect the actual light's shadow setting. The authored local sunshine has no shadows.
                    bool lit=inCone && Vector3.Dot(hit.normal,direction)>.15f && (light.shadows==LightShadows.None ||
                        !Physics.Raycast(hit.point+hit.normal*.035f,direction,Mathf.Max(0,distance-.05f),Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore));
                    if(lit){illuminated[i]=true;litPoint=hit.point;}else shaded[i]=true;
                }
            }
            if(Enumerable.Range(0,lights.Count).Any(i=>illuminated[i] && shaded[i]))
                entities.Add(new LifePhotoEntity{id="sunlit-surface",kind="surface",position=litPoint,bodyVisible=true,sunContrastVisible=true});
        }
    }
}
