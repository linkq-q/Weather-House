using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using WeatherHousePrototype;
using WeatherHouse.Hillside;
using WeatherHouse.WildlifeJourney;

namespace WeatherHouse.Life.Editor
{
    public static class LifeWorldChecks
    {
        [Serializable] sealed class Result { public string status; public string[] checks; public string boundary; }
        [MenuItem("Weather House/Life/Check world rules")]
        public static void Run()
        {
            var passed=new List<string>();
            Action<bool,string> check=(ok,label)=>{if(!ok)throw new InvalidOperationException("LifeWorld: "+label);passed.Add(label);};
            LifeRequest[] requests=LifeContent.Build();
            string[] expected={"rain","sunlight","wind","water","wet","dry","cold","warm","strong","weak","want","reject","tree","grass","home","here","come","go","first","then","hello","goodbye","self","you"};
            check(requests.Select(r=>r.id).Distinct().Count()==requests.Length,"请求身份不重复");
            check(expected.OrderBy(v=>v).SequenceEqual(requests.SelectMany(r=>r.glyphIds).Distinct().OrderBy(v=>v)),"完整24字，无伪造字ID");
            foreach(string word in expected)
                check(requests.Where(r=>r.glyphIds.Contains(word)).Select(r=>r.speakerKey).Distinct().Count()>=2,"跨动物情境 "+word);
            string[] events={"E0","E1","E2","E3","E4","E5","E6a","E6b","E7"};
            foreach(string ev in events)check(requests.Any(r=>r.eventId==ev&&r.requiredForEvent&&(r.HasTask||r.condition=="conversation")),"事件有可完成条件 "+ev);
            string[] conditions={"conversation","rain_seen","water_retained","dry_mat","fed","arrive","animal_arrived","wind_weak","rain_weak","warm_rest","walk_sequence","walk_reverse","common_rest"};
            check(requests.Where(r=>r.HasTask).All(r=>conditions.Contains(r.condition)),"所有任务条件有执行入口");
            var bowl=new LifeObjectState{kind=LifeObjectKind.Bowl};
            Step(bowl,LocalWeather.Rain,1,6);float retained=bowl.water;
            check(retained>.3f,"六秒弱雨可集到教学水量");
            bowl.ApplyWeather(LocalWeather.Rain,1,false,.1f);
            check(bowl.rainStrength==0 && bowl.water==retained,"停雨清雨态并留水");
            Step(bowl,LocalWeather.Sun,1,20);
            check(bowl.water==retained && bowl.rainStrength==0,"短时日照不蒸空水碗");
            var wet=new LifeObjectState{kind=LifeObjectKind.Bundle,wetness=.8f};wet.kind=LifeObjectKind.Mat;
            check(wet.wetness==.8f,"湿草编垫保留湿度");Step(wet,LocalWeather.Sun,1,20);
            check(wet.IsDry,"湿垫可逐渐晒干");
            check(wet.actionRevision==0,"持续天气不能伪造接任务后的铺垫或邀请动作");
            foreach(LifeObjectKind kind in new[]{LifeObjectKind.GrassSource,LifeObjectKind.Bundle,LifeObjectKind.Mat})
            {
                var boundary=new LifeObjectState{kind=kind,wetness=LifeObjectState.DryThreshold};
                check(boundary.IsDry,"同一干湿边界包含阈值 "+kind);
                boundary.wetness=LifeObjectState.DryThreshold+.0001f;
                check(!boundary.IsDry && !JsonUtility.FromJson<LifeObjectState>(JsonUtility.ToJson(boundary)).IsDry,"阈值以上及存档恢复仍湿 "+kind);
            }
            check(LifeInteractionRules.Primary(false,true,true)==LifePrimaryAction.Talk,"E有动物可交谈时优先交谈");
            check(LifeInteractionRules.Primary(false,false,true)==LifePrimaryAction.Gather,"E只有实际可采目标时采集");
            check(LifeInteractionRules.Primary(false,false,false)==LifePrimaryAction.None,"没有目标不出现E动作");
            check(LifeInteractionRules.Primary(true,true,true)==LifePrimaryAction.None,"输入或模态界面阻止E世界动作");
            var sheltered=new LifeObjectState{kind=LifeObjectKind.Mat,sheltered=true};Step(sheltered,LocalWeather.Rain,2,10);
            check(sheltered.IsDry,"遮棚阻挡强雨");
            var brightCold=new LifeObjectState{kind=LifeObjectKind.Rest,temperature=8};Step(brightCold,LocalWeather.Sun,2,20);
            check(!brightCold.IsWarm,"日照不会瞬间把冷风口变暖");
            var shadedWarm=new LifeObjectState{kind=LifeObjectKind.Rest,temperature=20,sheltered=true};
            check(shadedWarm.IsWarm,"遮阴的休息位可以暖");
            var weak=new LifeObjectState{kind=LifeObjectKind.Ribbon};weak.ApplyWeather(LocalWeather.Wind,1,true,.1f);
            check(weak.windStrength==1,"弱风仍存在");weak.ApplyWeather(LocalWeather.Wind,1,false,.1f);
            check(weak.windStrength==0,"停风与弱风不同");
            Vector3 origin=Vector3.zero;
            check(LifeWorld.InPlayerWeatherRange(origin,origin,new Vector3(7.9f,0,0),8),"圈内物件受天气影响");
            check(!LifeWorld.InPlayerWeatherRange(origin,origin,new Vector3(8.1f,0,0),8),"圈外物件不受天气影响");
            check(!LifeWorld.InPlayerWeatherRange(origin,new Vector3(30,0,0),new Vector3(30,0,0),8),"不能远程指定天气中心");
            var firstRain=new LifeObjectState{kind=LifeObjectKind.Mat,wetness=.5f};
            var firstSun=new LifeObjectState{kind=LifeObjectKind.Mat,wetness=.5f};
            Step(firstRain,LocalWeather.Rain,1,10);Step(firstRain,LocalWeather.Sun,1,25);
            Step(firstSun,LocalWeather.Sun,1,25);Step(firstSun,LocalWeather.Rain,1,10);
            check(firstRain.IsDry&&!firstSun.IsDry,"相同初态反序产生不同物理终态");
            var persisted=JsonUtility.FromJson<LifeObjectState>(JsonUtility.ToJson(firstRain));
            check(persisted.kind==firstRain.kind&&persisted.wetness==firstRain.wetness,"物件状态序列化可恢复");
            string dryCaption=LifeObject.VisibleState(new LifeObjectState{kind=LifeObjectKind.Mat,wetness=0});
            string wetCaption=LifeObject.VisibleState(new LifeObjectState{kind=LifeObjectKind.Mat,wetness=1});
            check(dryCaption!=wetCaption && !dryCaption.Contains("干") && !wetCaption.Contains("湿"),"玩家物件描述呈现不同表面现象而不直接给出干湿字义");
            string coldCaption=LifeObject.VisibleState(new LifeObjectState{kind=LifeObjectKind.Rest,temperature=8});
            string warmCaption=LifeObject.VisibleState(new LifeObjectState{kind=LifeObjectKind.Rest,temperature=20});
            check(coldCaption!=warmCaption && !coldCaption.Contains("冷") && !warmCaption.Contains("暖"),"玩家休息位描述保留可感知温差而不直接给出冷暖字义");
            foreach(LocalWeather channel in new[]{LocalWeather.Wind,LocalWeather.Rain})
            {
                string one=LifeWorld.VisibleDemonstration(channel,1,true),two=LifeWorld.VisibleDemonstration(channel,2,true);
                check(one!=two && !one.Contains("弱") && !two.Contains("强"),"照片自然演示保留实际强度差异但不泄字义 "+channel);
            }
            foreach(string resource in new[]{"GrassPatch","GrassBundle","GrassMat","WaterBowl","RibbonFlag","ShadeShelter"})
                check(Resources.Load<GameObject>("LifeAssets/"+resource),"模型引用 "+resource);
            CheckLeases(check);
            CheckDrinkApproach(check);
            CheckCurrentSquirrelRig(check);
            CheckTailCache(check);
            string path=Path.Combine(Application.dataPath,"../Docs/LifeGameplay/World/world-rule-checks.json");
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path,JsonUtility.ToJson(new Result {status="PASS",checks=passed.ToArray(),boundary="规则和资源检查；不等于场景可达性、动画接地或玩家视觉验收。"},true));
            Debug.Log("LifeWorld checks PASS: "+passed.Count);
        }
        static void Step(LifeObjectState state,LocalWeather kind,int strength,float seconds)
        { for(int i=0;i<Mathf.RoundToInt(seconds*10);i++)state.ApplyWeather(kind,strength,true,.1f); }
        static void CheckDrinkApproach(Action<bool,string> check)
        {
            var method=typeof(LifeWorld).GetMethod("DrinkApproachClear",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static);
            var body=new WildlifeBodyEnvelope{center=new Vector3(0,.18446f,0),half=new Vector3(.19325f,.18446f,.14687f),measured=true};
            var other=new WildlifeBodyEnvelope{center=new Vector3(0,.16147f,0),half=new Vector3(.16916f,.16147f,.12857f),measured=true};
            Vector3 from=new Vector3(-.678f,4.498f,-4.091f),reserved=new Vector3(-1.48522f,4.47761f,-5.36150f);
            Quaternion partnerFacing=Quaternion.LookRotation(new Vector3(.434f,0,-1.069f));
            bool Path(Vector3 to,Quaternion facing)=>(bool)method.Invoke(null,new object[]{body,from,Quaternion.identity,to,facing,other,reserved,partnerFacing});
            check(!Path(new Vector3(-1.56349f,4.43754f,-5.91853f),Quaternion.LookRotation(-(partnerFacing*Vector3.forward))),
                "真实失败站位的接近路线穿过伙伴将占据的位置，必须预先拒绝");
            check(Path(new Vector3(-.8f,4.46f,-5.7f),Quaternion.LookRotation(Vector3.left)),
                "绕开伙伴身体的侧面接近及最终转向可以通过同一几何检查");
        }
        static void CheckCurrentSquirrelRig(Action<bool,string> check)
        {
            const string modelPath="Assets/Characters/SquirrelOGA20261027/Squirrel_OGA_Natural_Unity.fbx";
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/World/Hillside/Generated/squirrel.prefab");
            check(prefab,"当前正式松鼠prefab存在");
            check(prefab.GetComponentsInChildren<WeatherHouseSquirrelLab.SquirrelTailWave>(true).Length==0,"当前OGA松鼠不挂旧27骨尾巴组件");
            var animal=prefab.GetComponent<WildlifeAgent>();
            var rig=animal && animal.visualRoot?animal.visualRoot.GetComponent<WeatherHouse.SquirrelOga.SquirrelOgaRig>():null;
            check(rig && rig.HasLifeClips && rig.head && rig.headSkin && animal.animator && rig.head.IsChildOf(animal.animator.transform),"当前OGA头部和七段生活动画绑定完整");
            check(animal.clips.idle==rig.idle && animal.clips.walk==rig.walk && animal.clips.run==rig.run &&
                animal.clips.restDown==rig.restDown && animal.clips.restSleep==rig.restLoop && animal.clips.restUp==rig.restUp,"当前OGA行为动画引用对应新rig");
            foreach(var clip in new[]{rig.idle,rig.walk,rig.run,rig.restDown,rig.restLoop,rig.restUp,rig.drink})
            {
                check(AssetDatabase.GetAssetPath(clip)==modelPath,"当前OGA动画来自新模型 "+clip.name);
                var bindings=AnimationUtility.GetCurveBindings(clip);
                check(bindings.Length>0 && bindings.Concat(AnimationUtility.GetObjectReferenceCurveBindings(clip)).All(binding=>
                    string.IsNullOrEmpty(binding.path) || animal.animator.transform.Find(binding.path)),"当前OGA动画曲线绑定实际骨架 "+clip.name);
            }
        }
        static void CheckTailCache(Action<bool,string> check)
        {
            Scene preview=EditorSceneManager.NewPreviewScene();
            GameObject empty=new GameObject("Tail cache cold initialization fixture"){hideFlags=HideFlags.HideAndDontSave};
            SceneManager.MoveGameObjectToScene(empty,preview);GameObject model=null;
            int errors=0;Application.LogCallback log=(message,stack,type)=>{if(type==LogType.Error || type==LogType.Exception)errors++;};
            Application.logMessageReceived+=log;
            try
            {
                const System.Reflection.BindingFlags hidden=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
                var cold=empty.AddComponent<WeatherHouseSquirrelLab.SquirrelTailWave>();
                var enable=typeof(WeatherHouseSquirrelLab.SquirrelTailWave).GetMethod("OnEnable",hidden);
                enable.Invoke(cold,null);
                check(!cold.IsInitialized && errors==0,"首次添加尾巴组件不抢先初始化空骨架");
                // This cache belongs to the original 27-bone model, not the current OGA prefab.
                GameObject prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Experiments/Squirrel/Models/turn.fbx");
                check(prefab,"热重载回归使用原27骨松鼠源模型");
                var skins=prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                check(skins.Length>0 && skins.All(s=>s.bones.Length==27),"旧尾巴回归模型保留完整27骨蒙皮");
                model=UnityEngine.Object.Instantiate(prefab);SceneManager.MoveGameObjectToScene(model,preview);
                var wave=model.GetComponent<WeatherHouseSquirrelLab.SquirrelTailWave>()??model.AddComponent<WeatherHouseSquirrelLab.SquirrelTailWave>();
                check(wave.Initialize(model.transform),"原27骨尾巴骨架可以初始化");wave.EvaluateAt(.37f);
                var tail=(Transform[])wave.GetType().GetField("tail",hidden).GetValue(wave);
                Quaternion[] expected=tail.Select(t=>t.rotation).ToArray();
                foreach(string name in new[]{"tail","restRotation","bendAxis","distance"})
                {var cache=(Array)wave.GetType().GetField(name,hidden).GetValue(wave);Array.Clear(cache,0,cache.Length);}
                check(wave.IsInitialized && tail.All(t=>!t),"回归布置重现initialized保留而readonly缓存丢失");
                enable.Invoke(wave,null);wave.EvaluateAt(.37f);
                check(wave.IsInitialized && tail.All(t=>t) && tail.Select((t,i)=>Quaternion.Angle(t.rotation,expected[i])).All(a=>a<.001f),"重新启用重建全部尾巴缓存且恢复相同动作姿态");
                check(errors==0,"热重载缓存恢复不产生错误日志");
            }
            finally
            {
                Application.logMessageReceived-=log;
                if(model)UnityEngine.Object.DestroyImmediate(model);UnityEngine.Object.DestroyImmediate(empty);
                EditorSceneManager.ClosePreviewScene(preview);
            }
        }
        static void CheckLeases(Action<bool,string> check)
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("租约检查需在编辑模式的独立附加场景运行。");
            Scene originalScene=SceneManager.GetActiveScene();
            // Preview scenes have separate physics; production Physics queries use the default scene.
            Scene fixture=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
            SceneManager.SetActiveScene(fixture);
            GameObject root=new GameObject("Life lease isolated fixture");
            SceneManager.MoveGameObjectToScene(root,fixture);
            try
            {
                check(fixture.GetPhysicsScene()==Physics.defaultPhysicsScene,"碰撞测试场景与实际行走查询使用同一物理空间");
                Vector3 origin=new Vector3(-1500,0,-1500);
                var population=root.AddComponent<WildlifePopulation>();
                population.worldBounds=new Bounds(origin+Vector3.up*5,new Vector3(100,10,100));population.player=null;
                var body=new GameObject("Wildlife lease body");body.transform.SetParent(root.transform);body.transform.position=origin+new Vector3(-15,0,-8);
                var animal=body.AddComponent<WildlifeAgent>();animal.speciesId="squirrel";animal.timidity=0;
                animal.habitatCenter=body.transform.position;animal.habitatRadius=5;
                population.agents=new[]{animal};population.Initialize();
                Vector3 start=animal.transform.position;
                Quaternion rotation=animal.transform.rotation;
                bool direct=population.TryMove(animal,start+Vector3.right*2,out Vector3 directEnd);
                bool queried=population.TryMoveFrom(animal,start,rotation,start+Vector3.right*2,out Vector3 queryEnd);
                check(direct==queried && directEnd==queryEnd,"挂点只读查询与实际行走采用相同路径规则");
                check(animal.transform.position==start && animal.transform.rotation==rotation,"挂点查询不改动物位置和朝向");
                var blocker=new GameObject("Layout query obstacle");blocker.transform.SetParent(root.transform);
                blocker.transform.position=start+Vector3.right*4+Vector3.up*.5f;
                var collider=blocker.AddComponent<BoxCollider>();collider.size=new Vector3(.8f,1,.8f);Physics.SyncTransforms();
                check(Physics.OverlapBox(blocker.transform.position,Vector3.one*.1f).Contains(collider),"默认物理查询确实命中测试障碍");
                check(!population.TryMoveFrom(animal,start+Vector3.right*3,rotation,start+Vector3.right*5,out _),"挂点间只读查询保留实际静态身体阻挡");
                check(animal.transform.position==start,"失败的挂点查询也不临时移动动物");
                UnityEngine.Object.DestroyImmediate(blocker);
                var envelopeField=typeof(WildlifeAgent).GetField("<BodyEnvelope>k__BackingField",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);
                WildlifeBodyEnvelope originalEnvelope=animal.BodyEnvelope;
                envelopeField.SetValue(animal,new WildlifeBodyEnvelope{center=Vector3.up*.2f,half=new Vector3(.4f,.2f,.1f),measured=true});
                blocker=new GameObject("Arrived lease turn obstacle");blocker.transform.SetParent(root.transform);
                blocker.transform.position=start+new Vector3(0,.25f,.3f);
                collider=blocker.AddComponent<BoxCollider>();collider.size=new Vector3(2,.5f,.05f);Physics.SyncTransforms();
                check(!population.TryMove(animal,start+Vector3.right*.03f,out _),"到达点残余向量的完整转身确实被墙阻挡，重现续约失败前提");
                check(animal.RequestLifeTarget(start+Vector3.right*.03f,1,false,.045f),"已到达动物保留当前朝向即可安全续约");
                check(animal.transform.position==start && animal.transform.rotation==rotation,"近点续约不移动或旋转动物");
                blocker.transform.position=start+new Vector3(.45f,.25f,0);collider.size=new Vector3(.04f,.5f,2);Physics.SyncTransforms();
                check(!animal.RequestLifeTarget(start+Vector3.right*.04f,1,false,.045f),"近点续约仍拒绝真正与墙重叠的目标身体");
                animal.ReleaseLifeTarget();envelopeField.SetValue(animal,originalEnvelope);UnityEngine.Object.DestroyImmediate(blocker);
                var labelObject=new GameObject("Life label photo fixture");labelObject.transform.SetParent(root.transform);
                labelObject.AddComponent<LifeLabel>().Initialize("院边空地");
                check(labelObject.layer==LayerMask.NameToLayer("UI"),"生活点文字使用UI层供动态照片排除");
                check(!animal.RequestLifeTarget(start+Vector3.right*9,1),"单体动物拒绝远距剧情目标");
                check(animal.RequestLifeTarget(start+Vector3.right*2,1),"单体动物接受安全短程目标");
                population.Simulate(.1f);
                check(LifeWorld.Flat(start,animal.transform.position)<.2f,"单体动物按行走速度移动，未瞬移");
                for(int i=0;i<15;i++)population.Simulate(.1f);
                check(!animal.HasLifeTarget,"单体剧情租约过期后归还自主生活");
                check(animal.RequestLifeTarget(animal.transform.position,1),"单体租约可以重新邀请");
                animal.enabled=false;
                // Non-ExecuteAlways behaviours do not receive normal OnDisable callbacks in this Edit-mode fixture.
                typeof(WildlifeAgent).GetMethod("OnDisable",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(animal,null);
                animal.enabled=true;check(!animal.HasLifeTarget,"单体禁用清除旧目标租约，重新启用不复活旧目标");animal.enabled=false;

                var navObject=new GameObject("Herd lease fixture");navObject.transform.SetParent(root.transform);
                var nav=navObject.AddComponent<JourneyNavigation>();
                nav.surfaceSampler=p=>new JourneySurface{valid=Mathf.Abs(p.x-origin.x)<50&&Mathf.Abs(p.z-origin.z)<50,groundY=0};nav.Rebuild();
                var sheepObject=new GameObject("Sheep body");sheepObject.transform.SetParent(navObject.transform);sheepObject.transform.position=origin+new Vector3(10,0,8);
                var herd=navObject.AddComponent<JourneyHerd>();herd.navigation=nav;herd.autoAdvance=false;
                herd.sites=new[]{new JourneyActivitySite{name="test",center=sheepObject.transform.position,radius=3}};
                herd.members=new[]{new JourneyMember{id=23,root=sheepObject.transform,sociability=0,bodyLength=.8f,profile=new JourneyProfile{bodyRadius=.2f,walkSpeed=1}}};herd.Initialize();
                Vector3 sheepStart=sheepObject.transform.position;
                check(!herd.RequestLifeTarget(23,sheepStart+Vector3.right*9,1),"群体动物拒绝远距剧情目标");
                check(herd.RequestLifeTarget(23,sheepStart+Vector3.right*2,1),"群体动物接受安全短程目标");
                herd.Simulate(.1f);
                check(LifeWorld.Flat(sheepStart,sheepObject.transform.position)<.2f,"群体动物保持原导航速度");
                for(int i=0;i<15;i++)herd.Simulate(.1f);
                check(!herd.HasLifeTarget(23),"群体剧情租约到期归还控制");
                check(herd.RequestLifeTarget(23,sheepObject.transform.position,1),"群体动物可重复邀请");
                herd.enabled=false;
                typeof(JourneyHerd).GetMethod("OnDisable",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(herd,null);
                herd.enabled=true;check(!herd.HasLifeTarget(23),"群体禁用清除旧目标租约，重新启用不复活旧目标");herd.enabled=false;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);EditorSceneManager.CloseScene(fixture,true);
                if(originalScene.IsValid() && originalScene.isLoaded)SceneManager.SetActiveScene(originalScene);
            }
        }
    }
}
