using System;
using UnityEditor;
using UnityEngine;

namespace WeatherHouse.Life.Editor
{
    public static class LifeWeatherPrefabChecks
    {
        [MenuItem("Weather House/Weather Effects/Check editable prefabs")]
        public static void Run()
        {
            var rangeRoot = PrefabUtility.LoadPrefabContents(LifeWeatherPrefabAssets.Root + "/CastRange.prefab");
            var windRoot = PrefabUtility.LoadPrefabContents(LifeWeatherPrefabAssets.Root + "/Wind.prefab");
            try
            {
                var range = rangeRoot.GetComponent<LifeCastRange>();
                Require(range && range.rangeMaterial, "Range component and material are serialized");
                range.preview = true;
                range.SendMessage("LateUpdate");
                var filter = rangeRoot.GetComponent<MeshFilter>();
                Require(filter.sharedMesh && rangeRoot.GetComponent<MeshRenderer>().enabled, "Prefab Mode builds a visible editable ring");
                range.rimHalfWidth = .3f; range.flameHeight = 2f;
                range.SendMessage("LateUpdate");
                var vertices = filter.sharedMesh.vertices;
                Require(Mathf.Abs(Vector3.Distance(vertices[0], vertices[1]) - .6f) < .001f,
                    "Changing authored rim width changes preview geometry");
                Require(Mathf.Abs((vertices[3] - vertices[2]).y - 2f) < .001f,
                    "Changing authored flame height changes preview geometry");
                Require(rangeRoot.GetComponent<MeshRenderer>().sharedMaterial == range.rangeMaterial,
                    "Preview uses the editable material asset");
                var wind = windRoot.GetComponent<LifeWindEffect>();
                Require(wind && wind.weakProfile && wind.strongProfile, "Wind profile references survive prefab serialization");
                foreach (var profile in new[] { wind.weakProfile, wind.strongProfile })
                {
                    var particles = profile.GetComponentsInChildren<ParticleSystem>(true);
                    Require(particles.Length >= 2, "Profile contains wind streaks and leaves");
                    bool hasTrail = false;
                    foreach (var ps in particles)
                    {
                        Require(!ps.main.playOnAwake && ps.main.loop, "Particle playback belongs to the spell controller");
                        var renderer = ps.GetComponent<ParticleSystemRenderer>();
                        Require(renderer && renderer.sharedMaterial && !ShaderUtil.ShaderHasError(renderer.sharedMaterial.shader), "Particle material is valid");
                        if (!ps.trails.enabled) continue;
                        hasTrail = true;
                        Require(renderer.trailMaterial && !ShaderUtil.ShaderHasError(renderer.trailMaterial.shader), "Wind trail material is valid");
                    }
                    Require(hasTrail, "Each strength includes editable wind trails");
                }
                Debug.Log("PASS Weather prefab authoring checks. Temporary prefab contents discarded; authored assets unchanged.");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(rangeRoot);
                PrefabUtility.UnloadPrefabContents(windRoot);
            }
        }
        static void Require(bool condition, string message)
        { if (!condition) throw new InvalidOperationException(message); }
    }
}
