using System;
using System.Linq;
using UnityEngine;
using WeatherHousePrototype;
using WeatherHouse.Hillside;
using WeatherHouse.WildlifeJourney;
using WeatherHouse.SquirrelOga;

namespace WeatherHouse.Life
{
    public sealed partial class LifeWorld
    {
        void BindActors()
        {
            AnimalActor[] actors=FindObjectsOfType<AnimalActor>();
            Bind("dog","agent.dog/0",actors);
            Bind("squirrel","agent.squirrel/14",actors);
            Bind("fox","agent.fox/20",actors);
            Bind("sheep","herd.sheep/23",actors);
            Bind("deer","agent.deer/42",actors);
            Bind("companion","agent.squirrel/19",actors);
            Bind("harborSheep","life.harbor.sheep",actors);
            Bind("harborDeer","life.harbor.deer",actors);
        }
        void Bind(string key,string suffix,AnimalActor[] all)
        {
            AnimalActor actor=all.FirstOrDefault(a=>a && (a.animalId??"").EndsWith(suffix,StringComparison.Ordinal));
            if(!actor){SetupWarning+="缺少关键个体 "+suffix+"；";return;}
            LifeSpeaker speaker=actor.GetComponent<LifeSpeaker>()??actor.gameObject.AddComponent<LifeSpeaker>();
            speaker.Actor=actor;speaker.key=key;speaker.requests=requests.Where(r=>r.speakerKey==key).ToArray();
            var b=new Binding {speaker=speaker,wildlife=actor.GetComponent<WildlifeAgent>(),home=actor.transform.position,target=actor.transform.position};
            if(b.wildlife && !b.wildlife.enabled)b.wildlife=null;
            if(b.wildlife)b.population=FindObjectsOfType<WildlifePopulation>().FirstOrDefault(p=>p.agents.Contains(b.wildlife));
            if(b.wildlife && b.wildlife.speciesId=="squirrel")
            {
                PrepareInitialLifePosition(b);
                SquirrelOgaRig oga=b.wildlife.GetComponentInChildren<SquirrelOgaRig>(true);
                // An installed OGA model must never receive the former 27-bone clips.
                AnimationClip down=oga?oga.restDown:Clip("SquirrelRestDown"),sleep=oga?oga.restLoop:Clip("SquirrelRestLoop"),up=oga?oga.restUp:Clip("SquirrelRestUp");
                if(down&&sleep&&up)
                {
                    b.oldIdle=b.wildlife.clips.idle;b.oldDown=b.wildlife.clips.restDown;b.oldSleep=b.wildlife.clips.restSleep;b.oldUp=b.wildlife.clips.restUp;b.changedClips=true;
                    b.wildlife.ConfigureLifeRest(down,sleep,up,oga?oga.idle:Clip("SquirrelIdle"));
                    b.tailWave=b.wildlife.GetComponentInChildren<WeatherHouseSquirrelLab.SquirrelTailWave>();
                    if(b.tailWave){b.oldTailEnabled=b.tailWave.enabled;b.tailWave.enabled=false;}
                }
            }
            if(!b.wildlife)
                foreach(JourneyHerd herd in FindObjectsOfType<JourneyHerd>())
                {
                    JourneyMember member=Array.Find(herd.members,m=>m!=null && m.root==actor.transform);
                    if(member!=null){b.herd=herd;b.member=member.id;break;}
                }
            bindings.Add(key,b);
        }
        static AnimationClip Clip(string id)=>Resources.LoadAll<AnimationClip>("LifeAssets/Animations/"+id)
            .FirstOrDefault(c=>!c.name.StartsWith("__preview__",StringComparison.Ordinal));

        void BuildStations()
        {
            if(bindings.ContainsKey("harborSheep") && bindings.ContainsKey("harborDeer"))
            {
                Landmark("harbor.sheep.here","渡口干岸站位",coastalHere);
                Landmark("harbor.deer.here","渡口交谈点",coastalDeer);
                Landmark("harbor.tide","潮池旁的干岸站位",coastalTide);
            }
            if(bindings.TryGetValue("dog",out Binding dog))
            {
                Landmark("dog.here","院门交谈点",Near(dog.home,new Vector3(0,0,1.7f)));
                Model("dog.bowl","院门集雨碗",LifeObjectKind.Bowl,"WaterBowl",Near(dog.home,new Vector3(1.8f,0,0)));
                Landmark("dog.sun","院边空地",Near(dog.home,new Vector3(-1.8f,0,0)));
            }
            if(bindings.TryGetValue("squirrel",out Binding squirrel))
            {
                BuildSquirrelPlaces(squirrel);
                Kit("squirrel",squirrel.home);
                Landmark("squirrel.tree","松鼠指向的树",FindTree("squirrel.tree",squirrel.home));
                Landmark("squirrel.home","松鼠的住处",AnchorPosition("squirrel.nest"));
                Landmark("squirrel.here","松鼠的交谈点",Near(squirrel.home,new Vector3(0,0,1.7f)));
            }
            if(bindings.TryGetValue("sheep",out Binding sheep))
            {
                Kit("sheep",sheep.home);
                Landmark("sheep.here","绵羊指向的这里",Near(sheep.home,new Vector3(0,0,1.8f)));
                Landmark("sheep.away","绵羊指向的另一处安全落脚点",Near(sheep.home,new Vector3(0,0,-3.4f)));
                anchors["sheep.feed"]=sheep.home;
                Model("sheep.ribbon","牧场系牢的风布条",LifeObjectKind.Ribbon,"RibbonFlag",Near(sheep.home,new Vector3(1.5f,0,1.5f)));
                Rest("sheep.cold","牧场风口",Near(sheep.home,new Vector3(2.3f,0,-1)),false,8);
                Rest("sheep.warm","牧场遮棚",FindShelter(sheep.home),true,20);
                Model("sheep.bowl","固定饮水槽",LifeObjectKind.Bowl,"WaterBowl",Near(sheep.home,new Vector3(-2.6f,0,1.5f))).State.water=.65f;
            }
            if(bindings.TryGetValue("fox",out Binding fox))
            {
                Kit("fox",fox.home);
                Landmark("fox.here","狐狸指向的这里",Near(fox.home,new Vector3(0,0,1.7f)));
                Landmark("fox.tree","狐狸的路标树",FindTree("fox.tree",fox.home));
                Rest("fox.home","狐狸的地面住处",Near(fox.home,new Vector3(0,0,-2)),true,19);
                Rest("fox.cold","林下小棚",Near(fox.home,new Vector3(-2.2f,0,0)),true,8);
                Rest("fox.warm","林地遮棚",FindShelter(fox.home),true,20);
                Model("fox.bowl","树荫边留水的碗",LifeObjectKind.Bowl,"WaterBowl",Near(fox.home,new Vector3(1.9f,0,0))).State.water=.55f;
                Landmark("fox.sun","林间空地",Near(fox.home,new Vector3(2.3f,0,2)));
            }
            if(bindings.TryGetValue("deer",out Binding deer))
            {
                Landmark("deer.here","鹿的交谈点",Near(deer.home,new Vector3(0,0,1.7f)));
                Model("deer.ribbon","芦苇边的风布条",LifeObjectKind.Ribbon,"RibbonFlag",Near(deer.home,new Vector3(1.7f,0,0)));
                Model("deer.bowl","鹿身边的集雨碗",LifeObjectKind.Bowl,"WaterBowl",Near(deer.home,new Vector3(-1.7f,0,0)));
            }
        }
        void Kit(string key,Vector3 center)
        {
            // Old loom coordinates remain a save/observation anchor; crafting travels with the bundle.
            anchors[key+".loom"]=center;
            LifeObject wet=Model(key+".wet","露天示范垫",LifeObjectKind.Mat,"GrassMat",Near(center,new Vector3(-2.1f,0,2.2f)));
            wet.State.wetness=.85f;
            if(key=="squirrel" && objects.TryGetValue(key+".nest",out LifeObject nest) &&
                shelterVisuals.TryGetValue(key+".nest",out Renderer[] roof) && roof.Length>0)
            {
                // The existing nest has room along its side. Reuse it instead of adding a wall
                // between the two communal seats beside the original courtyard furniture.
                Vector3 facing=center-nest.transform.position;facing.y=0;
                Vector3 side=Vector3.Cross(Vector3.up,facing.normalized);
                Vector3 at=nest.transform.position+side*.68f;
                Terrain terrain=Terrain.activeTerrain;if(terrain)at.y=terrain.SampleHeight(at)+terrain.transform.position.y;
                LifeObject dry=Model(key+".dry","棚下示范垫",LifeObjectKind.Mat,"GrassMat",at);
                dry.transform.localScale=Vector3.one*.65f;dry.State.sheltered=true;shelterVisuals[key+".dry"]=roof;
            }
            else
            {
                LifeObject dry=Model(key+".dry","棚下示范垫",LifeObjectKind.Mat,"GrassMat",Near(center,new Vector3(2.2f,0,-2.1f)));
                dry.State.sheltered=AddShelter(key+".dry",dry.transform.position);
            }
        }
        LifeObject Model(string id,string label,LifeObjectKind kind,string resource,Vector3 position)
        {
            if(kind==LifeObjectKind.Bowl && id!="squirrel.common.bowl")
            {LifeObject reused=ReuseContainer(id,position);if(reused)return reused;}
            var go=new GameObject(label+" ["+id+"]");go.transform.SetParent(transform);go.transform.position=position;
            GameObject asset=Resources.Load<GameObject>("LifeAssets/"+resource);
            if(asset)
            {
                GameObject model=Instantiate(asset,go.transform);model.name=resource;
                foreach(Collider collider in model.GetComponentsInChildren<Collider>())collider.enabled=false;
            }
            else SetupWarning+="缺少生活模型 "+resource+"；";
            var result=go.AddComponent<LifeObject>();
            result.Initialize(new LifeObjectState{id=id,kind=kind,position=position},label);objects.Add(id,result);interactables.Add(result);
            if(kind!=LifeObjectKind.Bundle && kind!=LifeObjectKind.Mat)AddLabel(go.transform,label);
            return result;
        }
        LifeObject CreateItem(LifeObjectState state)
        {
            string resource=state.kind==LifeObjectKind.Mat?"GrassMat":"GrassBundle";
            var item=Model(state.id,state.kind==LifeObjectKind.Mat?"草垫":"草束",state.kind,resource,state.position);
            item.State=state;return item;
        }
        void Landmark(string id,string label,Vector3 at)
        {
            if(invalidTargets.Contains(id))label="树目标暂不可达";
            LifeObject obj=Station(id,label,LifeObjectKind.Landmark,at);
            Ring(obj.transform,.45f,new Color(.9f,.75f,.39f,.65f));
        }
        void Rest(string id,string label,Vector3 at,bool sheltered,float temperature)
        {
            LifeObject obj=Station(id,label,LifeObjectKind.Rest,at);
            obj.State.sheltered=sheltered && AddShelter(id,at);obj.State.temperature=temperature;
            Ring(obj.transform,.7f,temperature>=17?new Color(.83f,.61f,.3f,.65f):new Color(.37f,.7f,.85f,.65f));
        }
        LifeObject Station(string id,string label,LifeObjectKind kind,Vector3 at)
        {
            var go=new GameObject(label+" ["+id+"]");go.transform.SetParent(transform);go.transform.position=at;
            var item=go.AddComponent<LifeObject>();item.Initialize(new LifeObjectState{id=id,kind=kind,position=at},label);
            objects.Add(id,item);interactables.Add(item);anchors[id]=at;AddLabel(go.transform,label);return item;
        }
        void Ring(Transform parent,float radius,Color color)
        {
            var ring=new GameObject("生活点范围");ring.layer=LayerMask.NameToLayer("UI");ring.transform.SetParent(parent,false);
            var line=ring.AddComponent<LineRenderer>();line.useWorldSpace=false;line.loop=true;line.positionCount=40;
            line.startWidth=line.endWidth=.025f;line.startColor=line.endColor=color;
            if(!markerMaterial)markerMaterial=new Material(Shader.Find("Universal Render Pipeline/Unlit"));line.sharedMaterial=markerMaterial;
            for(int i=0;i<40;i++){float a=i*Mathf.PI/20;line.SetPosition(i,new Vector3(Mathf.Cos(a)*radius,.035f,Mathf.Sin(a)*radius));}
        }
        void AddLabel(Transform parent,string text)
        {
            var go=new GameObject("生活点提示");go.transform.SetParent(parent,false);go.transform.localPosition=Vector3.up*.55f;
            var label=go.AddComponent<LifeLabel>();label.Initialize(text);labels.Add(label);
        }
        Vector3 FindTree(string id,Vector3 center)
        {
            Transform best=null;float distance=7;
            foreach(Transform t in FindObjectsOfType<Transform>())
            {
                if(t.IsChildOf(transform) || !(t.name.Contains("Tree")||t.name.Contains("树")||t.name.Contains("Fir_")))continue;
                Renderer[] parts=t.GetComponentsInChildren<Renderer>();
                if(parts.Length==0 || parts.Length>20)continue;
                Bounds bounds=parts[0].bounds;foreach(Renderer part in parts)bounds.Encapsulate(part.bounds);
                if(bounds.size.y<1.5f || bounds.size.x>15 || bounds.size.z>15)continue;
                float d=Flat(center,t.position);if(d>1.5f && d<distance){distance=d;best=t;}
            }
            if(best)
            {
                Vector3 toward=center-best.position;toward.y=0;
                Vector3 point=Near(best.position,toward.normalized*1.15f);
                if(Flat(point,best.position)<1.6f)return point;
            }
            SetupWarning+="缺少可达真树落点 "+center.ToString("F1")+"；";
            invalidTargets.Add(id);
            return Near(center,new Vector3(3.6f,0,0));
        }
        Vector3 FindShelter(Vector3 center)
        {
            Transform best=null;float distance=7;
            foreach(Transform t in FindObjectsOfType<Transform>())
            {
                if(t.IsChildOf(transform) || !(t.name.Contains("暖棚")||t.name.Contains("温室")||t.name.Contains("雨棚")||t.name.Contains("Shelter")))continue;
                float d=Flat(center,t.position);if(d>1 && d<distance){distance=d;best=t;}
            }
            return Near(center,best?best.position-center:new Vector3(-3.2f,0,-.5f));
        }
        bool AddShelter(string id,Vector3 point)
        {
            if(HasExistingRoof(point)){BindExistingShelterVisuals(id,point);return true;}
            GameObject shelter=Resources.Load<GameObject>("LifeAssets/ShadeShelter");
            if(!shelter){SetupWarning+="缺少遮棚资源 "+id+"；";return false;}
            Quaternion rotation=Quaternion.identity;
            if(bindings.TryGetValue(id.Split('.')[0],out Binding animal))
            {
                Vector3 toward=animal.home-point;toward.y=0;
                if(toward.sqrMagnitude>.01f)rotation=Quaternion.LookRotation(toward);
            }
            // The roof belongs to the place: picking up its mat must leave it standing.
            GameObject roof=Instantiate(shelter,point,rotation,transform);roof.name="生活背风遮棚 ["+id+"]";
            shelterVisuals[id]=roof.GetComponentsInChildren<Renderer>();
            return true;
        }
        Vector3 Near(Vector3 origin,Vector3 offset)
        {
            Terrain terrain=Terrain.activeTerrain;WorldWaterSurface water=FindObjectOfType<WorldWaterSurface>();
            for(int i=0;i<16;i++)
            {
                Vector3 p=origin+Quaternion.Euler(0,i*22.5f,0)*offset;p.y=terrain?terrain.SampleHeight(p)+terrain.transform.position.y:origin.y;
                if(water && water.Contains(p,.55f))continue;
                if(Mathf.Abs(p.y-origin.y)>1.2f)continue;
                if(objects.Values.Any(o=>o && Flat(p,o.transform.position)<1.05f))continue;
                bool clear=true;
                foreach(Collider c in Physics.OverlapSphere(p+Vector3.up*.5f,.42f,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore))
                    if(!(c is TerrainCollider) && !c.GetComponentInParent<WildlifeAgent>() && !c.GetComponentInParent<AnimalActor>()){clear=false;break;}
                if(clear)return p;
            }
            SetupWarning+="生活点附近通路需复查 "+origin.ToString("F1")+"；";
            return origin+new Vector3(offset.x,0,offset.z)*.45f;
        }
    }
    public sealed class LifeLabel : MonoBehaviour
    {
        TextMesh text;
        static Font font;
        public void Initialize(string value)
        {
            gameObject.layer=LayerMask.NameToLayer("UI");
            text=gameObject.AddComponent<TextMesh>();text.text=value;text.fontSize=40;text.characterSize=.035f;
            text.anchor=TextAnchor.LowerCenter;text.alignment=TextAlignment.Center;text.color=new Color(.97f,.91f,.74f);
            if(!font)font=Font.CreateDynamicFontFromOSFont(new[]{"Microsoft YaHei","Arial Unicode MS","Arial"},40);
            if(font){text.font=font;GetComponent<MeshRenderer>().sharedMaterial=font.material;}
        }
        public void Refresh(Transform player,Camera camera,bool photographing=false)
        {
            if(!text)return;
            text.gameObject.SetActive(!photographing && player && LifeWorld.Flat(player.position,transform.position)<3);
            if(camera)transform.rotation=camera.transform.rotation;
        }
    }
}
