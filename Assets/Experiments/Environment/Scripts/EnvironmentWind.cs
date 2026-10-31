using System.Collections.Generic;
using UnityEngine;

namespace WeatherHouse.EnvironmentLab
{
    [DisallowMultipleComponent]
    public sealed class EnvironmentWind : MonoBehaviour
    {
        [Tooltip("仅在 Play 中控制明确列出的草树 Renderer。运行中更换列表后调用 RebindTargets。")]
        public Renderer[] targets = new Renderer[0];
        [Tooltip("原材质风幅的倍数；0 静止，1 保留草树各自的幅度。")]
        [Min(0)] public float force = 1;
        [Tooltip("草树共用的风速。")]
        [Min(0)] public float speed = 1;
        [Tooltip("世界 XZ 平面风向，自动归一化；零向量使用世界 +X。")]
        public Vector2 direction = new Vector2(1, .45f);

        static readonly int Strength = Shader.PropertyToID("_WindStrength");
        static readonly int Speed = Shader.PropertyToID("_WindSpeed");
        static readonly int Direction = Shader.PropertyToID("_WindDirection");
        readonly List<Binding> bindings = new List<Binding>();

        sealed class Binding
        {
            public Renderer renderer;
            public Material[] original, owned;
            public float[] strength;
        }

        void OnEnable() { RebindTargets(); }
        void Update() { Apply(); }
        void OnDisable() { Release(); }
        void OnDestroy() { Release(); }

        // Runtime copies keep source assets and saved scene material references intact.
        public void RebindTargets()
        {
            Release();
            if (!Application.isPlaying || !isActiveAndEnabled || targets == null) return;
            var seen = new HashSet<Renderer>();
            foreach (Renderer target in targets)
            {
                if (!target || !seen.Add(target)) continue;
                if (target.HasPropertyBlock())
                {
                    Debug.LogWarning("EnvironmentWind 跳过带有 MaterialPropertyBlock 的 Renderer，避免覆盖已有控制：" + target.name, target);
                    continue;
                }
                Material[] original = target.sharedMaterials;
                var binding = new Binding { renderer = target, original = original,
                    owned = new Material[original.Length], strength = new float[original.Length] };
                Material[] assigned = (Material[])original.Clone();
                bool changed = false;
                for (int i = 0; i < original.Length; i++)
                {
                    Material source = original[i];
                    if (!source || !source.shader) continue;
                    string shader = source.shader.name;
                    if (shader != "Weather House/Painterly Grass" && shader != "Weather House/Painterly Tree") continue;
                    binding.strength[i] = Nonnegative(source.GetFloat(Strength));
                    binding.owned[i] = new Material(source) { name = source.name + " (Environment Wind)",
                        hideFlags = HideFlags.HideAndDontSave };
                    assigned[i] = binding.owned[i];
                    changed = true;
                }
                if (!changed) continue;
                bindings.Add(binding);
                target.sharedMaterials = assigned;
            }
            Apply();
        }

        public void Apply()
        {
            if (!Application.isPlaying || !isActiveAndEnabled) return;
            float amplitude = Nonnegative(force), rate = Nonnegative(speed);
            Vector2 heading = Normalized(direction);
            var vector = new Vector4(heading.x, 0, heading.y, 0);
            foreach (Binding binding in bindings)
                for (int i = 0; i < binding.owned.Length; i++)
                {
                    Material material = binding.owned[i];
                    if (!material) continue;
                    material.SetFloat(Strength, binding.strength[i] * amplitude);
                    material.SetFloat(Speed, rate);
                    material.SetVector(Direction, vector);
                }
        }

        void Release()
        {
            foreach (Binding binding in bindings)
            {
                if (binding.renderer)
                {
                    Material[] current = binding.renderer.sharedMaterials;
                    bool changed = false;
                    for (int i = 0; i < current.Length && i < binding.owned.Length; i++)
                        if (binding.owned[i] && current[i] == binding.owned[i])
                        {
                            current[i] = binding.original[i];
                            changed = true;
                        }
                    if (changed) binding.renderer.sharedMaterials = current;
                }
                foreach (Material material in binding.owned)
                    if (material)
                    {
                        if (Application.isPlaying) Destroy(material);
                        else DestroyImmediate(material);
                    }
            }
            bindings.Clear();
        }

        static float Nonnegative(float value)
        {
            return float.IsNaN(value) || float.IsInfinity(value) ? 0 : Mathf.Max(0, value);
        }

        static Vector2 Normalized(Vector2 value)
        {
            if (float.IsNaN(value.x) || float.IsNaN(value.y)) return Vector2.right;
            float scale = Mathf.Max(Mathf.Abs(value.x), Mathf.Abs(value.y));
            if (float.IsNaN(scale) || float.IsInfinity(scale) || scale < .00001f) return Vector2.right;
            return (value / scale).normalized;
        }
    }
}
