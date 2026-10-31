using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace WeatherHouse.EnvironmentLab.Editor
{
    public static class EnvironmentWindChecks
    {
        [MenuItem("Weather House/Environment Lab/Check Environment Wind")]
        public static void Run()
        {
            Require(Application.isPlaying, "请先进入 Play；风控制仅在运行时使用独立材质副本。");
            var owned = new List<Object>();
            var root = new GameObject("Temporary environment wind checks") { hideFlags = HideFlags.HideAndDontSave };
            root.SetActive(false);
            try
            {
                Material grass = Material("Weather House/Painterly Grass", .12f, owned);
                Material bark = Material("Weather House/Painterly Tree", .02f, owned);
                Material leaves = Material("Weather House/Painterly Tree", .06f, owned);
                Material replacement = Material("Weather House/Painterly Grass", .27f, owned);
                Renderer grassRenderer = Renderer(root, "Grass", grass);
                Renderer treeRenderer = Renderer(root, "Tree", bark, leaves);
                Renderer outside = Renderer(root, "Unselected grass", grass);
                var wind = root.AddComponent<EnvironmentWind>();
                wind.targets = new[] { grassRenderer, treeRenderer, grassRenderer };
                wind.force = 2;
                wind.speed = 1.7f;
                wind.direction = new Vector2(3, 4);
                root.SetActive(true);

                Require(grassRenderer.sharedMaterial != grass, "目标必须使用独立材质。");
                Require(outside.sharedMaterial == grass, "未选择对象必须保持原材质。");
                Wind(grassRenderer.sharedMaterial, .24f, 1.7f, new Vector4(.6f, 0, .8f, 0));
                Wind(treeRenderer.sharedMaterials[0], .04f, 1.7f, new Vector4(.6f, 0, .8f, 0));
                Wind(treeRenderer.sharedMaterials[1], .12f, 1.7f, new Vector4(.6f, 0, .8f, 0));
                Near(grass.GetFloat("_WindStrength"), .12f, "源草材质风幅不能改变。");
                Near(leaves.GetFloat("_WindStrength"), .06f, "源树材质风幅不能改变。");
                Near(grass.GetFloat("_WindSpeed"), .7f, "源材质风速不能改变。");

                wind.force = 0;
                wind.Apply();
                foreach (Renderer target in new[] { grassRenderer, treeRenderer })
                    foreach (Material material in target.sharedMaterials)
                        Near(material.GetFloat("_WindStrength"), 0, "force=0 必须让所有目标风幅归零。");
                for (int i = 0; i < 3; i++)
                {
                    wind.force = 3; wind.Apply();
                    wind.force = 1; wind.Apply();
                    wind.RebindTargets();
                }
                Near(grassRenderer.sharedMaterial.GetFloat("_WindStrength"), .12f, "重复应用和重绑不能累计风幅。");
                Near(treeRenderer.sharedMaterials[1].GetFloat("_WindStrength"), .06f, "树叶基准风幅必须保留。");
                wind.direction = Vector2.zero;
                wind.Apply();
                Wind(grassRenderer.sharedMaterial, .12f, 1.7f, new Vector4(1, 0, 0, 0));

                // Changes made while the controller runs belong to the user.
                grass.SetFloat("_WindStrength", .19f);
                leaves.SetColor("_BaseColor", Color.magenta);
                grassRenderer.sharedMaterial = replacement;
                var block = new MaterialPropertyBlock();
                block.SetColor("_BaseColor", Color.cyan);
                treeRenderer.SetPropertyBlock(block);
                wind.enabled = false;
                Require(grassRenderer.sharedMaterial == replacement, "停用不能覆盖用户后来换入的材质。");
                Require(treeRenderer.sharedMaterials[0] == bark && treeRenderer.sharedMaterials[1] == leaves,
                    "仍由组件占用的材质槽必须恢复源材质。");
                Near(grass.GetFloat("_WindStrength"), .19f, "停用不能覆盖用户对源材质的新值。");
                Require(leaves.GetColor("_BaseColor") == Color.magenta, "停用不能覆盖用户源材质颜色。");
                treeRenderer.GetPropertyBlock(block);
                Require(block.GetColor("_BaseColor") == Color.cyan, "停用不能覆盖用户的 PropertyBlock。");
                Require(outside.sharedMaterial == grass, "全程不能修改未选择的 Renderer。");

                treeRenderer.SetPropertyBlock(null);
                wind.enabled = true;
                Near(grassRenderer.sharedMaterial.GetFloat("_WindStrength"), .27f, "重新启用必须使用当前用户材质作为新基准。");
                wind.enabled = false;
                Require(grassRenderer.sharedMaterial == replacement, "重复启停必须恢复当前源材质。");
                Debug.Log("Environment wind checks PASS: actual Renderer material copies, grass/tree amplitude ratios, zero wind, normalized/zero direction, shared speed, repeated apply/rebind, source isolation, user replacement/property preservation, and disable restoration. GPU motion still requires visual review.");
            }
            finally
            {
                Object.DestroyImmediate(root);
                foreach (Object item in owned) if (item) Object.DestroyImmediate(item);
            }
        }

        static Material Material(string name, float amplitude, List<Object> owned)
        {
            Shader shader = Shader.Find(name);
            Require(shader && !ShaderUtil.ShaderHasError(shader), name + " 必须已导入且无编译错误。");
            var material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            owned.Add(material);
            material.SetFloat("_WindStrength", amplitude);
            material.SetFloat("_WindSpeed", .7f);
            return material;
        }

        static Renderer Renderer(GameObject root, string name, params Material[] materials)
        {
            var child = new GameObject(name) { hideFlags = HideFlags.HideAndDontSave };
            child.transform.SetParent(root.transform);
            var renderer = child.AddComponent<MeshRenderer>();
            renderer.sharedMaterials = materials;
            return renderer;
        }

        static void Wind(Material material, float strength, float speed, Vector4 direction)
        {
            Near(material.GetFloat("_WindStrength"), strength, "实际材质风幅不符。");
            Near(material.GetFloat("_WindSpeed"), speed, "实际材质风速不符。");
            Require((material.GetVector("_WindDirection") - direction).sqrMagnitude < .000001f,
                "实际材质方向必须在 XZ 平面归一化。");
        }
        static void Near(float actual, float expected, string message)
        {
            Require(!float.IsNaN(actual) && Mathf.Abs(actual - expected) < .00001f, message);
        }
        static void Require(bool value, string message)
        {
            if (!value) throw new InvalidOperationException(message);
        }
    }
}
