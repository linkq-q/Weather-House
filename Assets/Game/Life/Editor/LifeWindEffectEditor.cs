using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace WeatherHouse.Life.Editor
{
    [CustomEditor(typeof(LifeWindEffect))]
    public sealed class LifeWindEffectEditor : UnityEditor.Editor
    {
        int profile;
        bool advanced;
        void OnEnable()
        {
            var effect = target as LifeWindEffect;
            if (effect && !EditorUtility.IsPersistent(effect) && !EditorApplication.isPlayingOrWillChangePlaymode && LifeWindPreview.Target != effect)
            { LifeWindPreview.Start(effect, 1); Frame(effect); }
        }
        [MenuItem("Weather House/Weather Effects/打开起风预制体并预览")]
        public static void OpenPreview()
        {
            var stage = PrefabStageUtility.OpenPrefab(LifeWeatherPrefabAssets.Root + "/Wind.prefab");
            var effect = stage.prefabContentsRoot.GetComponent<LifeWindEffect>();
            Selection.activeGameObject = effect.gameObject;
            LifeWindPreview.Start(effect, 1); Frame(effect);
        }
        static void Frame(LifeWindEffect effect)
        {
            if (SceneView.lastActiveSceneView)
                SceneView.lastActiveSceneView.LookAt(effect.transform.position + Vector3.up * 1.5f,
                    Quaternion.Euler(8, 0, 0), 8, false, true);
        }
        public override void OnInspectorGUI()
        {
            var effect = (LifeWindEffect)target;
            if (EditorUtility.IsPersistent(effect))
            {
                EditorGUILayout.HelpBox("这是资源本身。打开 Prefab 后会自动预览弱风，并可在根物体调整强弱两组。", MessageType.Info);
                if (GUILayout.Button("打开起风预制体并预览")) OpenPreview();
                return;
            }
            EditorGUILayout.HelpBox("当前面板直接修改下面粒子系统的真实参数。预览仅在编辑状态运行，不会施放天气或改存档。", MessageType.Info);
            using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
            {
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("预览弱风")) { profile=0; LifeWindPreview.Start(effect,1); Frame(effect); }
                if (GUILayout.Button("预览强风")) { profile=1; LifeWindPreview.Start(effect,2); Frame(effect); }
                if (GUILayout.Button("停止")) LifeWindPreview.Stop();
                EditorGUILayout.EndHorizontal();
            }
            EditorGUI.BeginChangeCheck();
            profile = GUILayout.Toolbar(profile, new[] { "调整弱风", "调整强风" });
            if (EditorGUI.EndChangeCheck() && !EditorApplication.isPlayingOrWillChangePlaymode) LifeWindPreview.Start(effect,profile+1);
            Transform group = profile==0 ? effect.weakProfile : effect.strongProfile;
            if (!group) { EditorGUILayout.HelpBox("缺少对应强弱组引用。",MessageType.Error); DrawDefaultInspector(); return; }
            Float("整体风向（度）", group.localEulerAngles.y, 0, 360, group, value =>
            { var angles=group.localEulerAngles;angles.y=value;group.localEulerAngles=angles; });
            foreach (var ps in group.GetComponentsInChildren<ParticleSystem>(true)) DrawParticles(ps);
            advanced=EditorGUILayout.Foldout(advanced,"强弱组引用",true);
            if (advanced) DrawDefaultInspector();
        }
        static void DrawParticles(ParticleSystem ps)
        {
            EditorGUILayout.Space(5);EditorGUILayout.LabelField(ps.trails.enabled?"白色风丝":"随风碎叶",EditorStyles.boldLabel);
            var main=ps.main;var emission=ps.emission;var velocity=ps.velocityOverLifetime;var noise=ps.noise;var trails=ps.trails;
            Float("每秒数量", emission.rateOverTimeMultiplier,0,40,ps,v=>{var m=ps.emission;m.rateOverTimeMultiplier=v;});
            Float("粒子尺寸", main.startSizeMultiplier,.005f,.6f,ps,v=>{var m=ps.main;m.startSizeMultiplier=v;});
            Float("存活时间（秒）", main.startLifetimeMultiplier,.2f,8,ps,v=>{var m=ps.main;m.startLifetimeMultiplier=v;});
            EditorGUI.BeginChangeCheck();Color color=EditorGUILayout.ColorField("颜色（设为单色）",main.startColor.color);
            if(EditorGUI.EndChangeCheck()){Undo.RecordObject(ps,"Wind color");main.startColor=color;Changed(ps);}
            Float("前进速度",velocity.xMultiplier,0,12,ps,v=>{var m=ps.velocityOverLifetime;m.xMultiplier=v;});
            Float("上升速度",velocity.yMultiplier,-2,2,ps,v=>{var m=ps.velocityOverLifetime;m.yMultiplier=v;});
            Float("左右扰动",noise.strengthZMultiplier,0,3,ps,v=>{var m=ps.noise;m.strengthZMultiplier=v;});
            Float("上下扰动",noise.strengthYMultiplier,0,3,ps,v=>{var m=ps.noise;m.strengthYMultiplier=v;});
            Float("弯曲细密程度",noise.frequency,.01f,2,ps,v=>{var m=ps.noise;m.frequency=v;});
            Float("扰动流动速度",noise.scrollSpeedMultiplier,0,3,ps,v=>{var m=ps.noise;m.scrollSpeedMultiplier=v;});
            if(trails.enabled)
            {
                Float("拖尾宽度倍率",trails.widthOverTrailMultiplier,0,5,ps,v=>{var m=ps.trails;m.widthOverTrailMultiplier=v;});
                Float("拖尾持续比例",trails.lifetimeMultiplier,.05f,2,ps,v=>{var m=ps.trails;m.lifetimeMultiplier=v;});
                var material=ps.GetComponent<ParticleSystemRenderer>().trailMaterial;
                if(material && material.HasProperty("_Opacity"))
                {
                    EditorGUILayout.LabelField("共用风丝材质（同时影响强弱）",EditorStyles.miniBoldLabel);
                    Float("风丝不透明度",material.GetFloat("_Opacity"),0,1,material,v=>material.SetFloat("_Opacity",v));
                    Float("风丝亮度",material.GetFloat("_Brightness"),0,4,material,v=>material.SetFloat("_Brightness",v));
                    Float("亮线核心宽度",material.GetFloat("_CoreWidth"),0,.95f,material,v=>material.SetFloat("_CoreWidth",v));
                    Float("边缘柔软度",material.GetFloat("_EdgeSoftness"),.05f,2,material,v=>material.SetFloat("_EdgeSoftness",v));
                }
            }
        }
        static void Float(string label,float value,float min,float max,Object owner,System.Action<float> apply)
        {
            EditorGUI.BeginChangeCheck();float next=EditorGUILayout.Slider(label,value,min,max);
            if(!EditorGUI.EndChangeCheck())return;
            Undo.RecordObject(owner,label);apply(next);Changed(owner);
        }
        static void Changed(Object owner)
        {
            EditorUtility.SetDirty(owner);
            if(PrefabUtility.IsPartOfPrefabInstance(owner))PrefabUtility.RecordPrefabInstancePropertyModifications(owner);
            SceneView.RepaintAll();
        }
    }

    [InitializeOnLoad]
    public static class LifeWindPreview
    {
        public static LifeWindEffect Target { get; private set; }
        static ParticleSystem[] systems;
        static double last;
        static LifeWindPreview()
        {
            EditorApplication.update+=Tick;
            AssemblyReloadEvents.beforeAssemblyReload+=Stop;
            EditorApplication.playModeStateChanged+=_=>Stop();
        }
        public static void Start(LifeWindEffect effect,int strength)
        {
            if(!effect || EditorUtility.IsPersistent(effect) || EditorApplication.isPlayingOrWillChangePlaymode)return;
            Stop();Target=effect;
            foreach(var ps in effect.GetComponentsInChildren<ParticleSystem>(true))ps.Stop(false,ParticleSystemStopBehavior.StopEmittingAndClear);
            var group=strength==2?effect.strongProfile:effect.weakProfile;
            systems=group?group.GetComponentsInChildren<ParticleSystem>(true):new ParticleSystem[0];
            foreach(var ps in systems){ps.Simulate(2.4f,false,true,true);ps.Pause(false);}
            last=EditorApplication.timeSinceStartup;SceneView.RepaintAll();
        }
        public static void Stop()
        {
            if(systems!=null)foreach(var ps in systems)if(ps)ps.Stop(false,ParticleSystemStopBehavior.StopEmittingAndClear);
            systems=null;Target=null;SceneView.RepaintAll();
        }
        static void Tick()
        {
            if(!Target || EditorApplication.isPlayingOrWillChangePlaymode){if(systems!=null)Stop();return;}
            double now=EditorApplication.timeSinceStartup;float dt=Mathf.Clamp((float)(now-last),0,.05f);last=now;
            foreach(var ps in systems)if(ps){ps.Simulate(dt,false,false,true);ps.Pause(false);}
            SceneView.RepaintAll();
        }
    }
}
