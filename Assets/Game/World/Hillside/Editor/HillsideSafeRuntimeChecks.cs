using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace WeatherHouse.Hillside.Editor
{
    public static class HillsideSafeRuntimeChecks
    {
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

        [MenuItem("Weather House/Hillside/Performance/Check Equivalent Runtime Optimizations")]
        public static void RunMenu() { Debug.Log(Run()); }

        public static string Run()
        {
            Require(!EditorApplication.isPlayingOrWillChangePlaymode, "Run isolated checks outside Play mode.");
            return CheckAnimation() + "\n" + CheckFlatWater();
        }

        static string CheckAnimation()
        {
            Scene preview = EditorSceneManager.NewPreviewScene();
            var temporary = new List<Object>();
            PlayableGraph reference = default;
            object optimized = null;
            Type type = typeof(WildlifePopulation).Assembly.GetType("WeatherHouse.Hillside.WildlifeAnimation", true);
            try
            {
                var clips = new AnimationClip[9];
                for (int i = 0; i < clips.Length; i++)
                {
                    clips[i] = new AnimationClip { name = "Equivalence clip " + i };
                    clips[i].SetCurve("Bone", typeof(Transform), "localPosition.x",
                        AnimationCurve.Linear(0f, i * .1f, .7f + i * .13f, (i + 1) * .3f));
                    temporary.Add(clips[i]);
                }
                var source = new WildlifeClips { idle = clips[0], forage = clips[1], walk = clips[2], run = clips[3],
                    greet = clips[4], alert = clips[5], takeoff = clips[6], fly = clips[7], land = clips[8] };
                Animator actualAnimator = Rig("Optimized", preview, temporary, out Transform actualBone);
                Animator expectedAnimator = Rig("Original", preview, temporary, out Transform expectedBone);
                optimized = Activator.CreateInstance(type, true);
                MethodInfo initialize = type.GetMethod("Initialize"), play = type.GetMethod("Play"), tick = type.GetMethod("Tick");
                initialize.Invoke(optimized, new object[] { actualAnimator, source, .37f });
                PlayableGraph actual = Field<PlayableGraph>(optimized, "graph");
                AnimationMixerPlayable actualMixer = Field<AnimationMixerPlayable>(optimized, "mixer");
                var actualClips = Field<List<AnimationClipPlayable>>(optimized, "playables");
                actual.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                reference = PlayableGraph.Create("Original animation reference");
                reference.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                var mixer = AnimationMixerPlayable.Create(reference, clips.Length);
                AnimationPlayableOutput.Create(reference, "Reference", expectedAnimator).SetSourcePlayable(mixer);
                var inputs = new AnimationClipPlayable[clips.Length];
                for (int i = 0; i < clips.Length; i++)
                {
                    inputs[i] = AnimationClipPlayable.Create(reference, clips[i]);
                    inputs[i].SetApplyFootIK(false); inputs[i].SetTime(.37f * clips[i].length);
                    reference.Connect(inputs[i], 0, mixer, i);
                }
                reference.Play();
                int target = -1;
                bool cycle = false, poseChanged = false;
                Vector3 initialPose = actualBone.localPosition;
                for (int frame = 0; frame < 1200; frame++)
                {
                    int requested = frame / 35 % clips.Length;
                    bool repeat = frame / 113 % 2 == 0;
                    float speed = .6f + (frame / 7 % 5) * .3f;
                    AnimationClip clip = frame % 97 == 96 ? null : clips[requested];
                    play.Invoke(optimized, new object[] { clip, repeat, speed });
                    // Frozen pre-optimization behavior reads native weights on every tick.
                    if (clip != null)
                    {
                        if (target != requested)
                        {
                            if (!repeat) inputs[requested].SetTime(0d);
                            if (target < 0) mixer.SetInputWeight(requested, 1f);
                            target = requested;
                        }
                        cycle = repeat; inputs[target].SetSpeed(speed);
                    }
                    float dt = frame % 17 == 0 ? 0f : frame % 3 == 0 ? .11f : .016f;
                    tick.Invoke(optimized, new object[] { dt });
                    float blend = 1f - Mathf.Exp(-dt * 12f);
                    for (int i = 0; i < clips.Length; i++)
                        mixer.SetInputWeight(i, Mathf.Lerp(mixer.GetInputWeight(i), i == target ? 1f : 0f, blend));
                    double time = inputs[target].GetTime();
                    float length = clips[target].length;
                    if (length > 0f && time >= length)
                        inputs[target].SetTime(cycle ? time % length : Mathf.Max(0f, length - .001f));
                    actual.Evaluate(dt); reference.Evaluate(dt);
                    for (int i = 0; i < clips.Length; i++)
                    {
                        Require(actualMixer.GetInputWeight(i) == mixer.GetInputWeight(i), "Animation weight changed at frame " + frame);
                        Require(actualClips[i].GetTime() == inputs[i].GetTime(), "Animation time changed at frame " + frame);
                        Require(actualClips[i].GetSpeed() == inputs[i].GetSpeed(), "Animation speed changed at frame " + frame);
                    }
                    Require((actualBone.localPosition - expectedBone.localPosition).sqrMagnitude < .000000001f,
                        "Animation pose changed at frame " + frame);
                    poseChanged |= (actualBone.localPosition - initialPose).sqrMagnitude > .0001f;
                }
                Require(poseChanged, "Animation equivalence check never moved its bound bone.");
                initialize.Invoke(optimized, new object[] { actualAnimator, source, 0f });
                play.Invoke(optimized, new object[] { clips[8], true, 1f });
                tick.Invoke(optimized, new object[] { .016f });
                Require(Field<AnimationMixerPlayable>(optimized, "mixer").GetInputWeight(8) == 1f,
                    "Animation reinitialization retained stale cached weights.");
                return "PASS animation: 1200 frames, 9 clips, swaps, repeats, one-shots, null clips, variable speeds/dt; exact native weights/times/speeds, matching bone poses, reinitialization";
            }
            finally
            {
                if (optimized != null) type.GetMethod("Dispose").Invoke(optimized, null);
                if (reference.IsValid()) reference.Destroy();
                foreach (Object value in temporary) if (value) Object.DestroyImmediate(value);
                EditorSceneManager.ClosePreviewScene(preview);
            }
        }

        static string CheckFlatWater()
        {
            Scene preview = EditorSceneManager.NewPreviewScene();
            GameObject water = null, ground = null;
            TerrainData terrainData = null;
            Texture2D readback = null;
            RenderTexture previous = RenderTexture.active;
            try
            {
                water = GameObject.CreatePrimitive(PrimitiveType.Plane);
                SceneManager.MoveGameObjectToScene(water, preview);
                water.GetComponent<Renderer>().enabled = false;
                ground = new GameObject("Isolated terrain"); SceneManager.MoveGameObjectToScene(ground, preview);
                terrainData = new TerrainData { heightmapResolution = 33, size = new Vector3(16f, 1f, 16f) };
                var terrain = ground.AddComponent<Terrain>(); terrain.terrainData = terrainData;
                var interaction = water.AddComponent<HillsideWaterInteraction>();
                interaction.terrain = terrain; interaction.resolution = 64;
                Require((bool)typeof(HillsideWaterInteraction).GetMethod("EnsureResources", Private).Invoke(interaction, null),
                    "Water simulation resources could not be initialized.");
                Require(!Field<bool>(interaction, "hasDisturbance"), "Cleared water should have no prior disturbance.");
                RTHandle state = Field<RTHandle>(interaction, "state"), next = Field<RTHandle>(interaction, "nextState"), normals = Field<RTHandle>(interaction, "normals");
                Material simulation = Field<Material>(interaction, "simulation");
                simulation.SetVector("_FieldRect", interaction.WorldRect);
                simulation.SetVector("_Cell", new Vector4(1f / 64f, 1f / 64f,
                    1f / (interaction.WorldRect.z * 64), 1f / (interaction.WorldRect.w * 64)));
                simulation.SetVector("_Wake", Vector4.zero); simulation.SetVector("_Ring", Vector4.zero);
                CommandBuffer cmd = CommandBufferPool.Get("Isolated flat-water equivalence");
                try
                {
                    for (int step = 0; step < 32; step++)
                    {
                        simulation.SetVector("_Step", new Vector4(.001f + step * .0001f, .99f, 1.15f * 1.15f, 1f));
                        Blitter.BlitCameraTexture(cmd, state, next, simulation, 0);
                        RTHandle swap = state; state = next; next = swap;
                        Graphics.ExecuteCommandBuffer(cmd); cmd.Clear();
                    }
                    Blitter.BlitCameraTexture(cmd, state, normals, simulation, 1);
                    Graphics.ExecuteCommandBuffer(cmd);
                }
                finally { CommandBufferPool.Release(cmd); }
                readback = new Texture2D(64, 64, TextureFormat.RGBAFloat, false, true);
                RenderTexture.active = normals.rt; readback.ReadPixels(new Rect(0, 0, 64, 64), 0, 0); readback.Apply();
                foreach (Color pixel in readback.GetPixels())
                    Require(pixel.r == .5f && pixel.g == .5f && pixel.b == 1f && pixel.a == 1f,
                        "Unstamped water changed its exact neutral normal.");
                typeof(HillsideWaterInteraction).GetField("hasDisturbance", Private).SetValue(interaction, true);
                interaction.ClearField();
                Require(!Field<bool>(interaction, "hasDisturbance"), "ClearField failed to reset pristine-water state.");
                return "PASS water: 32 actual GPU integrations leave an unstamped field exactly neutral; ClearField resets disturbance tracking";
            }
            finally
            {
                RenderTexture.active = previous;
                if (water) Object.DestroyImmediate(water);
                if (ground) Object.DestroyImmediate(ground);
                if (terrainData) Object.DestroyImmediate(terrainData);
                if (readback) Object.DestroyImmediate(readback);
                EditorSceneManager.ClosePreviewScene(preview);
            }
        }

        static Animator Rig(string name, Scene scene, List<Object> temporary, out Transform bone)
        {
            var root = new GameObject(name); SceneManager.MoveGameObjectToScene(root, scene); temporary.Add(root);
            bone = new GameObject("Bone").transform; bone.SetParent(root.transform, false);
            Animator animator = root.AddComponent<Animator>(); animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            return animator;
        }

        static T Field<T>(object value, string name) => (T)value.GetType().GetField(name, Private).GetValue(value);
        static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    }
}
