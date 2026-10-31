using System;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace WeatherHouse.EnvironmentLab.Editor
{
    public static class FogCardChecks
    {
        const string SourcePath = "Assets/HIGHLANDS - Stylized Environment/Materials/FX/Fog.mat";

        [MenuItem("Weather House/Environment Lab/Check Fog Card Parameters")]
        static void CheckParametersMenu() { Debug.Log(Run()); }

        public static string Run()
        {
            var source = AssetDatabase.LoadAssetAtPath<Material>(SourcePath);
            Require(source && !ShaderUtil.ShaderHasError(source.shader), "Source fog shader must have no import errors.");
            string original = EditorJsonUtility.ToJson(source);
            var go = new GameObject("Fog parameter checks") { hideFlags = HideFlags.HideAndDontSave };
            go.SetActive(false);
            var material = new Material(source);
            try
            {
                var card = go.AddComponent<FogCard>();
                card.tint = new Color(.3f, .5f, .7f, .1f);
                card.opacity = .25f;
                card.nearFadeStart = 4;
                card.nearFadeEnd = 12;
                card.intersectionFadeDistance = 2;
                card.ApplyTo(material);
                Require(material.GetColor("_BaseColor") == new Color(.3f, .5f, .7f, .25f), "Opacity is independent of tint alpha.");
                Near(material.GetVector("_CameraFadeParams"), new Vector4(4, .125f, 0, 0), "Camera fade covers 4 to 12 meters.");
                Near(material.GetVector("_SoftParticleFadeParams"), new Vector4(0, .5f, 0, 0), "Intersection fade reaches full opacity at 2 meters.");
                Require(material.IsKeywordEnabled("_SOFTPARTICLES_ON") && material.IsKeywordEnabled("_FADING_ON"), "Fade shader variants are enabled.");
                Require(material.GetTexture("_BaseMap") == source.GetTexture("_BaseMap"), "Existing smoke alpha texture is retained.");
                Require(material.GetFloat("_ZWrite") == 0 && material.GetFloat("_Cull") == 0, "Fog keeps transparent double-sided rendering.");
                card.nearFadeStart = -2;
                card.nearFadeEnd = -3;
                card.intersectionFadeDistance = 0;
                card.ApplyTo(material);
                Near(material.GetVector("_CameraFadeParams"), new Vector4(0, 100, 0, 0), "Degenerate camera ranges remain finite.");
                Near(material.GetVector("_SoftParticleFadeParams"), new Vector4(0, 100, 0, 0), "Zero intersection width remains finite.");
                Require(EditorJsonUtility.ToJson(source) == original, "Shared source material is unchanged.");
                return "PASS: fog shader diagnostics, fade parameters, source isolation. GPU visibility requires a scene render.";
            }
            finally { Object.DestroyImmediate(go); Object.DestroyImmediate(material); }
        }

        public static string RunRuntime()
        {
            Require(Application.isPlaying, "Runtime fog checks require Play mode.");
            var source = new Material(AssetDatabase.LoadAssetAtPath<Material>(SourcePath));
            string original = EditorJsonUtility.ToJson(source);
            var go = new GameObject("Fog lifecycle checks") { hideFlags = HideFlags.HideAndDontSave };
            go.SetActive(false);
            try
            {
                var target = go.AddComponent<MeshRenderer>();
                target.sharedMaterial = source;
                var card = go.AddComponent<FogCard>();
                card.targetRenderer = target;
                go.SetActive(true);
                var instance = target.sharedMaterial;
                Require(instance && instance != source, "Play creates a private material instance.");
                card.opacity = .43f;
                card.ApplySettings();
                Require(target.sharedMaterial == instance, "Parameter changes reuse the owned instance.");
                Require(Mathf.Abs(instance.GetColor("_BaseColor").a - .43f) < .0001f, "Play controls reach the instance.");
                card.enabled = false;
                Require(target.sharedMaterial == source, "Disabling restores the prior material.");
                Require(EditorJsonUtility.ToJson(source) == original, "Play controls preserve the source material.");
                return "PASS: private Play material, parameter update, disable restoration, source isolation.";
            }
            finally { Object.DestroyImmediate(go); Object.DestroyImmediate(source); }
        }

        static void Near(Vector4 actual, Vector4 expected, string message)
        {
            Require((actual - expected).sqrMagnitude < .000001f, message);
        }

        static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("Fog card: " + message);
        }
    }
}
