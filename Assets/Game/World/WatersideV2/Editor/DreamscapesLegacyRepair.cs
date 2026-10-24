using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace WeatherHouse.WatersideV2.Editor
{
    /// <summary>Adapts only Dreamscapes' baked Tree Creator materials; never regenerates trees.</summary>
    public static class DreamscapesLegacyRepair
    {
        const string Root = "Assets/Environment/DDriveLibrary/Dreamscapes";

        public static string Run()
        {
            Require(!EditorApplication.isPlayingOrWillChangePlaymode, "Run Dreamscapes repair in Edit mode.");
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            Require(shader && shader.isSupported && !ShaderUtil.ShaderHasError(shader), "URP Lit must compile first.");
            string[] paths = Directory.GetFiles(Root, "*.prefab", SearchOption.AllDirectories)
                .Select(path => path.Replace('\\', '/')).OrderBy(path => path).ToArray();
            Require(paths.Length == 45, "Expected the 45 source Dreamscapes prefabs.");

            // Preflight all geometry before changing a material; an empty mesh is never accepted.
            var materials = paths.SelectMany(AssetDatabase.LoadAllAssetsAtPath).OfType<Material>().ToArray();
            Require(materials.Length == 60, "Expected 60 baked tree materials.");
            int vertices = 0, meshes = 0;
            foreach (string path in paths)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                Require(prefab, "Prefab did not load: " + path);
                MeshFilter[] filters = prefab.GetComponentsInChildren<MeshFilter>(true);
                Require(filters.Length > 0, "Prefab has no geometry: " + path);
                foreach (MeshFilter filter in filters)
                    Require(filter.sharedMesh && filter.sharedMesh.vertexCount > 0 && filter.sharedMesh.subMeshCount > 0,
                        "Missing or empty mesh: " + path);
                foreach (Mesh mesh in AssetDatabase.LoadAllAssetsAtPath(path).OfType<Mesh>())
                { meshes++; vertices += mesh.vertexCount; }
            }
            Require(meshes == 30 && vertices == 92174, "Baked geometry differs from the source cache.");
            foreach (Material material in materials)
                Require(material.HasProperty("_BaseMap") && material.GetTexture("_BaseMap") ||
                    material.HasProperty("_MainTex") && material.GetTexture("_MainTex"),
                    "Missing original atlas: " + AssetDatabase.GetAssetPath(material));

            foreach (Material material in materials)
            {
                bool leaf = material.name == "Optimized Leaf Material";
                Require(leaf || material.name == "Optimized Bark Material", "Unexpected embedded material: " + material.name);
                bool alreadyUrp = material.shader == shader;
                Texture atlas = material.GetTexture(alreadyUrp ? "_BaseMap" : "_MainTex");
                Vector2 scale = material.GetTextureScale(alreadyUrp ? "_BaseMap" : "_MainTex");
                Vector2 offset = material.GetTextureOffset(alreadyUrp ? "_BaseMap" : "_MainTex");
                Color color = material.GetColor(alreadyUrp ? "_BaseColor" : "_Color");
                float cutoff = material.HasProperty("_Cutoff") ? material.GetFloat("_Cutoff") : .3f;
                material.shader = shader;
                material.shaderKeywords = Array.Empty<string>();
                material.SetTexture("_BaseMap", atlas);
                material.SetTextureScale("_BaseMap", scale);
                material.SetTextureOffset("_BaseMap", offset);
                material.SetColor("_BaseColor", color);
                material.SetFloat("_AlphaClip", leaf ? 1 : 0);
                material.SetFloat("_Cutoff", cutoff);
                material.SetFloat("_Cull", leaf ? (float)CullMode.Off : (float)CullMode.Back);
                material.SetFloat("_Metallic", 0);
                material.SetFloat("_Smoothness", .15f);
                material.SetFloat("_Surface", 0);
                material.SetFloat("_SrcBlend", (float)BlendMode.One);
                material.SetFloat("_DstBlend", (float)BlendMode.Zero);
                material.SetFloat("_ZWrite", 1);
                material.SetOverrideTag("RenderType", leaf ? "TransparentCutout" : "Opaque");
                material.renderQueue = leaf ? 2450 : 2000;
                if (leaf) material.EnableKeyword("_ALPHATEST_ON");
                material.SetShaderPassEnabled("ShadowCaster", true);
                material.SetShaderPassEnabled("DepthOnly", true);
                // The packed legacy bump/spec atlas remains serialized, but is not a URP normal map.
                EditorUtility.SetDirty(material);
                AssetDatabase.SaveAssetIfDirty(material);
                Require(material.GetTexture("_BaseMap") == atlas, "Atlas changed during adaptation.");
            }
            return "Dreamscapes: 45 prefabs, 30 original baked meshes / 92174 vertices, 60 URP atlas materials. Static Tree Creator geometry; no procedural regeneration or legacy wind.";
        }

        static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
