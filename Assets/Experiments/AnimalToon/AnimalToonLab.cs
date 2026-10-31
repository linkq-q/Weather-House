using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace WeatherHouse.AnimalToon
{
    public sealed class AnimalToonLab : MonoBehaviour
    {
        public Camera view;
        public Light key;
        public GameObject[] animals;
        public Animator[] animators;
        public AnimationClip walk;
        public Material toon, outlined;
        public GameObject shadowBlocker, foreground;
        [Range(-160, 160)] public float lightAngle = 125.88f;
        [Range(10, 80)] public float lightElevation = 35;
        [Range(0, 16)] public float edgeWidth = 8;
        [Range(0, 24)] public float mergeRadius = 12;
        [Range(0, 1)] public float toonStrength = 1;
        [Range(0.01f, 0.3f)] public float softness = 0.01f;
        public bool animate, inShadow, showForeground, showControls = true;
        public int focus = 2;
        public float animationTime;
        PlayableGraph[] graphs;
        AnimationClipPlayable[] clips;
        Material runtimeToon, runtimeOutlined;
        AnimalToonView paperView;

        void OnEnable()
        {
            // All tuning is on instances, so Play never writes the sample material assets.
            runtimeToon = new Material(toon);
            runtimeOutlined = new Material(outlined);
            for (int i = 1; i < animals.Length; i++)
                foreach (var skin in animals[i].GetComponentsInChildren<SkinnedMeshRenderer>())
                {
                    var materials = skin.sharedMaterials;
                    for (int j = 0; j < materials.Length; j++) materials[j] = i == 1 ? runtimeToon : runtimeOutlined;
                    skin.sharedMaterials = materials;
                }
            graphs = new PlayableGraph[animators.Length];
            clips = new AnimationClipPlayable[animators.Length];
            for (int i = 0; i < graphs.Length; i++)
            {
                graphs[i] = PlayableGraph.Create("Animal toon walk " + i);
                graphs[i].SetTimeUpdateMode(DirectorUpdateMode.Manual);
                clips[i] = AnimationClipPlayable.Create(graphs[i], walk);
                clips[i].SetApplyFootIK(false);
                AnimationPlayableOutput.Create(graphs[i], "Walk", animators[i]).SetSourcePlayable(clips[i]);
                graphs[i].Play();
            }
            Apply();
        }

        void Update()
        {
            if (animate) animationTime += Time.deltaTime;
            Apply();
        }

        public void Apply()
        {
            if (!paperView) paperView = view.GetComponent<AnimalToonView>();
            if (paperView) paperView.mergeRadius = mergeRadius;
            float yaw = lightAngle * Mathf.Deg2Rad, elevation = lightElevation * Mathf.Deg2Rad;
            Vector3 towardsLight = new Vector3(Mathf.Sin(yaw) * Mathf.Cos(elevation),
                Mathf.Sin(elevation), -Mathf.Cos(yaw) * Mathf.Cos(elevation));
            key.transform.rotation = Quaternion.LookRotation(-towardsLight, Vector3.up);
            shadowBlocker.transform.SetPositionAndRotation(new Vector3(0, .8f, 0) + towardsLight * 4,
                Quaternion.LookRotation(-towardsLight));
            shadowBlocker.SetActive(inShadow);
            foreground.SetActive(showForeground);
            if (runtimeOutlined)
            {
                runtimeOutlined.SetFloat("_OutlineWidth", edgeWidth);
                foreach (var material in new[] { runtimeToon, runtimeOutlined })
                {
                    material.SetFloat("_ToonStrength", toonStrength);
                    material.SetFloat("_BandSoftness", softness);
                }
            }
            for (int i = 0; i < animals.Length; i++)
            {
                animals[i].SetActive(focus < 0 || focus == i);
                animals[i].transform.localPosition = new Vector3(focus < 0 ? (i - 1) * 2.4f : 0, 0, 0);
                if (clips != null && graphs[i].IsValid())
                {
                    clips[i].SetTime(Mathf.Repeat(animationTime, walk.length));
                    graphs[i].Evaluate(0);
                }
            }
            view.orthographicSize = focus < 0 ? 2.15f : 1.05f;
        }

        void OnGUI()
        {
            if (!showControls) return;
            GUI.skin.label.fontSize = 16;
            GUI.skin.button.fontSize = 15;
            string[] labels = { "原材质", "柔和色阶", "色阶 + 受光侧描边" };
            for (int i = 0; i < labels.Length; i++)
                if (GUI.Button(new Rect(Screen.width * i / 3f + 22, 22, Screen.width / 3f - 44, 30), labels[i]))
                    focus = focus == i ? -1 : i;
            GUILayout.BeginArea(new Rect(18, Screen.height - 215, 600, 200), GUI.skin.box);
            lightAngle = Slider("光照方位", lightAngle, -160, 160);
            edgeWidth = Slider("描边宽度", edgeWidth, 0, 16);
            mergeRadius = Slider("色块合并", mergeRadius, 0, 24);
            toonStrength = Slider("色阶强度", toonStrength, 0, 1);
            softness = Slider("明暗过渡", softness, .01f, .3f);
            GUILayout.BeginHorizontal();
            animate = GUILayout.Toggle(animate, "播放行走");
            inShadow = GUILayout.Toggle(inShadow, "进入阴影");
            showForeground = GUILayout.Toggle(showForeground, "前景遮挡");
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        static float Slider(string label, float value, float min, float max)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(90));
            value = GUILayout.HorizontalSlider(value, min, max);
            GUILayout.Label(value.ToString("0.00"), GUILayout.Width(60));
            GUILayout.EndHorizontal();
            return value;
        }

        void OnDisable()
        {
            if (graphs != null) foreach (var graph in graphs) if (graph.IsValid()) graph.Destroy();
            if (runtimeToon) Destroy(runtimeToon);
            if (runtimeOutlined) Destroy(runtimeOutlined);
        }
    }
}
