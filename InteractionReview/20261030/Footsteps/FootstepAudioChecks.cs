using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using WeatherHouse.Life;
using WeatherHousePrototype;
using ForestSpriteCharacter;

[InitializeOnLoad]
public static class FootstepAudioChecks
{
    public const string Output = "../../../InteractionReview/20261030/Footsteps/checks.txt";
    static FootstepAudioChecks() { EditorApplication.playModeStateChanged += state => {
        if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool("FootstepAudioChecks", false)) {
            SessionState.SetBool("FootstepAudioChecks", false); new GameObject("Magic audio checks").AddComponent<FootstepAudioProbe>();
        }
    }; }
    public static void Run()
    {
        var script = AssetDatabase.LoadAllAssetsAtPath("Assets/WeatherHousePhotoCandidate.dll").OfType<MonoScript>()
            .Single(s => s.GetClass() == typeof(LifeAudioCatalog));
        AssetDatabase.TryGetGUIDAndLocalFileIdentifier(script, out string guid, out long id);
        const string asset = "Assets/Game/Life/Audio/Resources/LifeAudio/Catalog.asset";
        File.WriteAllText(asset, System.Text.RegularExpressions.Regex.Replace(File.ReadAllText(asset), @"m_Script: \{[^}]+\}",
            "m_Script: {fileID: " + id + ", guid: " + guid + ", type: 3}"));
        AssetDatabase.ImportAsset(asset, ImportAssetOptions.ForceSynchronousImport);
        var catalog = Resources.Load<LifeAudioCatalog>("LifeAudio/Catalog");
        var lines = new List<string>();
        void Check(bool ok, string label) { if (!ok) throw new Exception(label); lines.Add("PASS " + label); }
        int contacts=0;
        for(int i=1;i<=600;i++) if(LifeAudioGround.CrossedStep((i-1)/100f,i/100f)) contacts++;
        Check(contacts==12,"Six animation cycles produce twelve contacts at any movement speed");
        Check(!LifeAudioGround.CrossedStep(.4f,.3f),"Rewound animation does not replay old contacts");
        Check(LifeAudioGround.CrossedStep(.9f,2.1f),"Slow frame consumes one contact without a catch-up burst");
        Check(LifeAudioGround.Wading(0,0),"Surface-level foot is included in wading");
        Check(!LifeAudioGround.Wading(0,1),"Dry bridge above water is excluded");
        Check(!LifeAudioGround.Wading(0,-1),"Deep underwater support is excluded");
        File.WriteAllLines(Output,lines);
        Environment.SetEnvironmentVariable("WEATHERHOUSE_LIFE_REVIEW_SAVE", Path.GetFullPath("../../../InteractionReview/20261030/Footsteps/Save"));
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        SessionState.SetBool("FootstepAudioChecks", true); EditorApplication.isPlaying = true;
    }
    public static void Add(string line) => File.AppendAllText(Output, line + "\n");
}
public class FootstepAudioProbe : MonoBehaviour
{
    readonly List<string> errors = new List<string>();
    const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    static void Set(object obj, string field, object value) => obj.GetType().GetField(field, Hidden).SetValue(obj, value);
    static T Get<T>(object obj, string field) => (T)obj.GetType().GetField(field, Hidden).GetValue(obj);
    static void Call(object obj, string method, params object[] args) => obj.GetType().GetMethod(method, Hidden).Invoke(obj, args);
    static void Property(object obj, string name, object value) => obj.GetType().GetProperty(name).SetValue(obj, value);
    static void Check(bool ok, string label) { if (!ok) throw new Exception(label); FootstepAudioChecks.Add("PASS " + label); }
    IEnumerator Start()
    {
        Application.logMessageReceived += Log;
        var steps = Steps();
        while (true) {
            bool more; object next = null;
            try { more = steps.MoveNext(); if (more) next = steps.Current; }
            catch (Exception e) { FootstepAudioChecks.Add("FAIL " + e); EditorApplication.Exit(1); yield break; }
            if (!more) break; yield return next;
        }
        foreach (var error in errors) FootstepAudioChecks.Add("FAIL runtime log: " + error);
        if (errors.Count == 0) FootstepAudioChecks.Add("PASS No runtime errors or exceptions");
        Application.logMessageReceived -= Log;
        EditorApplication.Exit(errors.Count == 0 ? 0 : 1);
    }
    void Log(string condition, string stack, LogType type) { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors.Add(condition); }
    IEnumerator Steps()
    {
        var root = new GameObject("Disabled game fixture"); root.SetActive(false);
        var game = root.AddComponent<GameplaySession>(); var life = root.AddComponent<LifeSession>();
        foreach (var component in root.GetComponents<MonoBehaviour>()) component.enabled = false;
        var floor=GameObject.CreatePrimitive(PrimitiveType.Cube); floor.transform.position=new Vector3(0,-.1f,0); floor.transform.localScale=new Vector3(100,.2f,100);
        var playerObject = new GameObject("Player fixture"); playerObject.SetActive(false);
        var animator=playerObject.AddComponent<Animator>();
        var animation=new AnimationClip(); animation.SetCurve("Visual",typeof(Transform),"localPosition.x",AnimationCurve.Linear(0,0,2,0));
        var ac=new UnityEditor.Animations.AnimatorController(); var layer=new UnityEditor.Animations.AnimatorControllerLayer {name="Base",defaultWeight=1,stateMachine=new UnityEditor.Animations.AnimatorStateMachine()}; ac.AddLayer(layer);
        var idle=ac.layers[0].stateMachine.AddState("Idle"); idle.motion=animation;
        var walk=ac.layers[0].stateMachine.AddState("Walk"); walk.motion=animation; animator.runtimeAnimatorController=ac;
        var visual=new GameObject("Visual");visual.transform.SetParent(playerObject.transform);
        var player=playerObject.AddComponent<ForestSpriteDemoController>(); player.animator=animator;player.visual=visual.transform;player.enabled=false;
        var body=player.GetComponent<CharacterController>();body.height=1.8f;body.center=Vector3.up*.9f;
        player.demoCamera = new GameObject("Listener", typeof(Camera), typeof(AudioListener)).GetComponent<Camera>();
        playerObject.SetActive(true); game.forestSprite=player;Set(life,"saveBlocked",true);
        var world = root.AddComponent<LifeWorld>(); world.enabled = false; Property(life, "World", world);
        var weather = new GameObject("Local weather").AddComponent<LifeLocalWeather>(); weather.Initialize(player.transform, 8); Set(life, "weather", weather);
        var input = root.AddComponent<LifeGestureInput>(); input.enabled = false; Set(input, "life", life);
        var ink = new GameObject("Ink", typeof(RectTransform)).AddComponent<LifeStrokeGraphic>(); Set(input, "stroke", ink);
        Set(input, "drawHint", new GameObject("Hint", typeof(RectTransform)).AddComponent<Text>());
        var audio = root.AddComponent<LifeAudio>(); root.SetActive(true); yield return null; yield return null;
        Call(audio, "OnApplicationFocus", true);
        var sources = Get<AudioSource[]>(audio, "sources");
        var water=new GameObject("Test shallow water").AddComponent<FootstepTestWater>();
        Set(audio,"waters",new WeatherHouse.WorldWaterSurface[]{water});
        body.Move(Vector3.down*.1f);yield return null;
        animator.Play("Walk",0,0);animator.speed=0;animator.Update(0);
        Call(audio,"SyncPresentation");
        int before=audio.FootstepCount;
        for(int i=1;i<=12;i++) {
            body.Move(new Vector3(.12f,-.1f,0));
            animator.Play("Walk",0,i*.1f);animator.Update(0);
            // Let the production LateUpdate consume this evaluated pose and actual supported move.
            yield return null;
            yield return new WaitForSecondsRealtime(.19f);
        }
        int steps=audio.FootstepCount-before;
        Check(steps>=2 && steps<=3,"Actual supported movement follows animation contacts, not distance; steps="+steps);
        Check(sources[3].clip && audio.Catalog.Find(LifeAudioCue.Water).clips.Contains(sources[3].clip),"Surface-level supported walking emits a water step on the footstep source");
        int still=audio.FootstepCount; yield return new WaitForSecondsRealtime(.3f);
        Check(audio.FootstepCount==still,"Stationary character emits no extra steps");
        audio.enabled=false;Check(sources.All(x=>!x.isPlaying),"Disable stops all sources");
    }
}
public sealed class FootstepTestWater : WeatherHouse.WorldWaterSurface {
    public override bool Contains(Vector3 p,float margin=0)=>true;
    public override float Height(Vector3 p)=>0;
}
