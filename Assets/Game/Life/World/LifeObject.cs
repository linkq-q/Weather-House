using System;
using UnityEngine;
using WeatherHousePrototype;

namespace WeatherHouse.Life
{
    public enum LifeObjectKind { GrassSource, Bundle, Mat, Bowl, Loom, Rest, Landmark, Ribbon }

    [Serializable]
    public sealed class LifeObjectState
    {
        public const float DryThreshold=.18f;
        public const float WaterVisibleThreshold=.01f;
        public string id, slot;
        public LifeObjectKind kind;
        public Vector3 position;
        public float wetness, water, temperature=18;
        public bool sheltered, held;
        public int revision, actionRevision, rainStrength, windStrength;
        public bool IsDry => wetness <= DryThreshold;
        public bool IsWarm => temperature >= 17f;

        public void ApplyWeather(LocalWeather weather,int strength,bool active,float dt)
        {
            dt=Mathf.Clamp(dt,0,.2f); strength=active?Mathf.Clamp(strength,1,2):0;
            if(weather==LocalWeather.Rain)
            {
                rainStrength=strength;
                if(sheltered) return;
                if(strength>0 && (kind==LifeObjectKind.Bundle || kind==LifeObjectKind.Mat || kind==LifeObjectKind.GrassSource))
                    wetness=Mathf.Clamp01(wetness+dt*.13f*strength);
                if(strength>0 && kind==LifeObjectKind.Bowl)
                    water=Mathf.Clamp01(water+dt*.075f*strength);
            }
            if(weather==LocalWeather.Wind)
            {
                windStrength=strength;
                if(strength>0) wetness=Mathf.Max(0,wetness-dt*.012f*strength);
            }
            if(weather==LocalWeather.Sun)
            {
                rainStrength=0;
                if(strength>0)wetness=Mathf.Max(0,wetness-dt*.045f*strength);
            }
        }
    }

    public sealed class LifeObject : MonoBehaviour
    {
        public LifeObjectState State;
        public string label;
        public Transform visual, waterVisual, ribbon;
        MaterialPropertyBlock block;
        Renderer[] renderers;
        Color[] baseColors;
        float breeze;
        float demonstrationWind=-1;
        float shownWetness=float.NaN;
        Quaternion ribbonRest;
        ParticleSystem drips;
        Material dripMaterial;

        public void Initialize(LifeObjectState state,string display)
        {
            State=state; label=display;
            renderers=GetComponentsInChildren<Renderer>(); baseColors=new Color[renderers.Length];
            block=new MaterialPropertyBlock();
            for(int i=0;i<renderers.Length;i++)
                baseColors[i]=renderers[i].sharedMaterial && renderers[i].sharedMaterial.HasProperty("_BaseColor")
                    ? renderers[i].sharedMaterial.GetColor("_BaseColor") : Color.white;
            foreach(Transform child in GetComponentsInChildren<Transform>())
            {
                if(child.name=="WaterSurface") waterVisual=child;
                if(child.name=="Ribbon") { ribbon=child; ribbonRest=child.localRotation; }
            }
            if(state.kind==LifeObjectKind.Mat || state.kind==LifeObjectKind.Bundle)
            {
                var go=new GameObject("表面滴水");go.transform.SetParent(transform,false);
                go.transform.localPosition=Vector3.up*.12f;
                drips=go.AddComponent<ParticleSystem>();
                var main=drips.main; main.startLifetime=.35f;main.startSpeed=.02f;main.startSize=.035f;
                main.startColor=new Color(.44f,.72f,.82f,.7f);main.gravityModifier=.7f;main.maxParticles=30;
                var shape=drips.shape;shape.shapeType=ParticleSystemShapeType.Box;shape.scale=new Vector3(.6f,.02f,.35f);
                var emission=drips.emission;emission.rateOverTime=0;
                Shader shader=Shader.Find("Universal Render Pipeline/Particles/Unlit");
                if(shader)
                {
                    dripMaterial=new Material(shader);dripMaterial.SetColor("_BaseColor",new Color(.38f,.65f,.76f,1));
                    var renderer=drips.GetComponent<ParticleSystemRenderer>();renderer.sharedMaterial=dripMaterial;
                    renderer.renderMode=ParticleSystemRenderMode.Stretch;renderer.lengthScale=2;
                }
            }
            RefreshVisual(0);
        }

        public void RefreshVisual(float time)
        {
            if(State==null)return;
            float dark=Mathf.Lerp(1,.58f,State.wetness);
            if(renderers!=null && !Mathf.Approximately(shownWetness,State.wetness)) for(int i=0;i<renderers.Length;i++)
            {
                if(!renderers[i] || (waterVisual && renderers[i].transform==waterVisual))continue;
                block.SetColor("_BaseColor",baseColors[i]*new Color(dark,dark,dark,1));
                block.SetFloat("_Smoothness",Mathf.Lerp(.12f,.58f,State.wetness));
                renderers[i].SetPropertyBlock(block);
            }
            shownWetness=State.wetness;
            if(waterVisual)
            {
                waterVisual.gameObject.SetActive(State.water>LifeObjectState.WaterVisibleThreshold);
                var p=waterVisual.localPosition;p.y=Mathf.Lerp(.06f,.14f,State.water);waterVisual.localPosition=p;
                float scale=Mathf.Lerp(.77f,1.04f,State.water);waterVisual.localScale=new Vector3(scale,1,scale);
            }
            if(ribbon)
            {
                float intensity=demonstrationWind>=0?demonstrationWind:State.windStrength>0?State.windStrength:breeze;
                ribbon.localRotation=ribbonRest*Quaternion.Euler(0,Mathf.Sin(time*4.4f)*intensity*13,Mathf.Sin(time*3.2f)*intensity*18);
            }
            if(drips) { var emission=drips.emission;emission.rateOverTime=State.wetness>.6f?6*State.wetness:0; }
        }
        public void SetNaturalBreeze(float intensity) { breeze=Mathf.Clamp(intensity,0,2); }
        public void SetDemonstrationWind(float intensity){demonstrationWind=intensity;}
        public int VisibleWindStrength=>demonstrationWind>=0?(demonstrationWind<=0?0:demonstrationWind<1.5f?1:2):State.windStrength>0?State.windStrength:breeze>0?1:0;
        void OnDestroy(){if(dripMaterial)Destroy(dripMaterial);}

        public string Description => State==null || State.kind==LifeObjectKind.Loom || State.kind==LifeObjectKind.Landmark?label:label+" · "+VisibleState(State);
        public static string VisibleState(LifeObjectState state)
        {
            switch(state.kind)
            {
                case LifeObjectKind.Bowl:return state.water<=.01f?"碗底露着":state.water<.3f?"碗底有一层积液":"碗里的液面升高了";
                case LifeObjectKind.Bundle:case LifeObjectKind.Mat:
                    return state.IsDry?"纤维色浅，表面不挂水":"纤维颜色加深，表面挂着水珠";
                case LifeObjectKind.GrassSource:return state.IsDry?"叶面没有水珠":"草叶挂着水珠";
                case LifeObjectKind.Rest:return (state.IsWarm?"手背的凉意渐渐消退":"伸手靠近时手背发凉")+"，"+
                    (state.sheltered?"顶上有遮挡":"头顶是天空");
                case LifeObjectKind.Ribbon:return state.windStrength==2?"布条大幅甩动":state.windStrength==1?"布边轻摆":"布条自然垂下";
                default:return "";
            }
        }
        // Explicit meanings are reserved for the judge's trusted context, never the photo/UI caption.
        public string JudgementDescription => State==null?label:label+
            (State.kind==LifeObjectKind.Bowl?" · 水量 "+Mathf.RoundToInt(State.water*100)+"%":
             State.kind==LifeObjectKind.Bundle || State.kind==LifeObjectKind.Mat?" · "+(State.IsDry?"干":"湿")+"（"+Mathf.RoundToInt(State.wetness*100)+"%）":
             State.kind==LifeObjectKind.Rest?" · "+(State.IsWarm?"暖":"冷")+" · "+(State.sheltered?"遮阴背风":"明亮风口"):"");
    }
}
