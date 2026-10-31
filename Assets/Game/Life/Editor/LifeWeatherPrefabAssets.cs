using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace WeatherHouse.Life.Editor
{
    public static class LifeWeatherPrefabAssets
    {
        public const string Root = "Assets/Game/Life/Resources/LifeWeather";
        [MenuItem("Weather House/Weather Effects/选中起风预制体")]
        public static void SelectWind() => LifeWindEffectEditor.OpenPreview();
        [MenuItem("Weather House/Weather Effects/选中施法圈材质")]
        public static void SelectRangeMaterial() => SelectAsset("CastRange.mat");
        static void SelectAsset(string name)
        {
            Selection.activeObject = AssetDatabase.LoadMainAssetAtPath(Root + "/" + name);
            EditorGUIUtility.PingObject(Selection.activeObject);
        }
        [MenuItem("Weather House/Weather Effects/Create missing prefabs")]
        public static void CreateMissing()
        {
            Directory.CreateDirectory(Root); AssetDatabase.Refresh();
            if (!AssetDatabase.LoadAssetAtPath<GameObject>(Root + "/CastRange.prefab")) CreateRange();
            if (!AssetDatabase.LoadAssetAtPath<GameObject>(Root + "/Wind.prefab")) CreateWind();
            AssetDatabase.SaveAssets();
        }
        static Material Material(string name, string shader)
        {
            string path = Root + "/" + name + ".mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing) return existing;
            var source = Shader.Find(shader);
            if (!source) throw new System.InvalidOperationException("Missing shader " + shader);
            var material = new Material(source) { name = name };
            if (shader == "Universal Render Pipeline/Particles/Unlit")
            {
                material.SetFloat("_Surface", 1); material.SetFloat("_Blend", 0);
                material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha); material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                material.SetFloat("_ZWrite", 0); material.SetFloat("_Cull", 0); material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                material.renderQueue = 3000;
            }
            AssetDatabase.CreateAsset(material, path); return material;
        }
        static void CreateRange()
        {
            var go = new GameObject("CastRange"); go.SetActive(false);
            try
            {
                var effect = go.AddComponent<LifeCastRange>();
                effect.rangeMaterial = Material("CastRange", "Weather House/Life/Local Weather Range");
                go.GetComponent<MeshRenderer>().sharedMaterial = effect.rangeMaterial;
                go.SetActive(true);
                PrefabUtility.SaveAsPrefabAsset(go, Root + "/CastRange.prefab");
            }
            finally { Object.DestroyImmediate(go); }
        }
        static void CreateWind()
        {
            var ribbon = Material("WindRibbon", "Weather House/Life/Wind Ribbon");
            var leaves = Material("WindLeaves", "Universal Render Pipeline/Particles/Unlit");
            string meshPath = Root + "/WindLeaf.asset";
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
            if (!mesh)
            {
                mesh = new Mesh { name = "Small folded leaf" };
                mesh.vertices = new[] { new Vector3(-.5f,0,0), new Vector3(0,.06f,.2f), new Vector3(.5f,0,0), new Vector3(0,-.06f,-.2f) };
                mesh.uv = new[] { new Vector2(0,.5f), new Vector2(.5f,1), new Vector2(1,.5f), new Vector2(.5f,0) };
                mesh.triangles = new[] { 0,1,2,0,2,3 }; mesh.RecalculateNormals(); mesh.RecalculateBounds();
                AssetDatabase.CreateAsset(mesh, meshPath);
            }
            var go = new GameObject("Wind"); go.SetActive(false);
            try
            {
                var effect = go.AddComponent<LifeWindEffect>();
                effect.weakProfile = Profile(go.transform, "Weak", false, ribbon, leaves, mesh);
                effect.strongProfile = Profile(go.transform, "Strong", true, ribbon, leaves, mesh);
                go.SetActive(true);
                PrefabUtility.SaveAsPrefabAsset(go, Root + "/Wind.prefab");
            }
            finally { Object.DestroyImmediate(go); }
        }
        static Transform Profile(Transform root, string name, bool strong, Material ribbon, Material leaves, Mesh mesh)
        {
            var profile = new GameObject(name).transform; profile.SetParent(root, false);
            var streaks = Particles(profile, "Wind Streaks", strong, true);
            var renderer = streaks.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = ribbon; renderer.trailMaterial = ribbon;
            var trails = streaks.trails; trails.enabled = true; trails.ratio = 1;
            trails.lifetime = strong ? .85f : 1.1f; trails.minVertexDistance = .07f;
            trails.worldSpace = true; trails.dieWithParticles = true; trails.sizeAffectsWidth = true;
            trails.inheritParticleColor = true;
            trails.widthOverTrail = new ParticleSystem.MinMaxCurve(1, new AnimationCurve(new Keyframe(0,0), new Keyframe(.3f,1), new Keyframe(.7f,.65f), new Keyframe(1,0)));
            var bits = Particles(profile, "Leaves", strong, false);
            renderer = bits.GetComponent<ParticleSystemRenderer>(); renderer.renderMode = ParticleSystemRenderMode.Mesh;
            renderer.mesh = mesh; renderer.sharedMaterial = leaves;
            var spin = bits.rotationOverLifetime; spin.enabled = true; spin.separateAxes = true;
            spin.x = .7f; spin.y = 1.8f; spin.z = strong ? 2.7f : 1.5f;
            return profile;
        }
        static ParticleSystem Particles(Transform parent, string name, bool strong, bool streak)
        {
            var go = new GameObject(name); go.transform.SetParent(parent, false); go.transform.localPosition = new Vector3(0,1.5f,0);
            var ps = go.AddComponent<ParticleSystem>(); ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main; main.playOnAwake = false; main.loop = true; main.duration = 5;
            main.simulationSpace = ParticleSystemSimulationSpace.World; main.maxParticles = streak ? 64 : 48;
            main.startSpeed = 0; main.startLifetime = streak ? new ParticleSystem.MinMaxCurve(2.2f,3.5f) : new ParticleSystem.MinMaxCurve(2.8f,4.2f);
            main.startSize = streak ? new ParticleSystem.MinMaxCurve(.045f,.085f) : new ParticleSystem.MinMaxCurve(.1f,.19f);
            main.startColor = streak ? new Color(.91f,.97f,1,.48f) : new Color(.65f,.68f,.37f,.8f);
            var emission = ps.emission; emission.rateOverTime = streak ? (strong ? 7 : 3.2f) : (strong ? 6 : 2.5f);
            var shape = ps.shape; shape.shapeType = ParticleSystemShapeType.Box; shape.scale = new Vector3(9,2.1f,9);
            var velocity = ps.velocityOverLifetime; velocity.enabled = true; velocity.space = ParticleSystemSimulationSpace.Local;
            velocity.x = strong ? 3.9f : 2.2f; velocity.y = .06f; velocity.z = strong ? .65f : .35f;
            var noise = ps.noise; noise.enabled = true; noise.separateAxes = true;
            noise.strengthX = .12f; noise.strengthY = streak ? .35f : .5f; noise.strengthZ = streak ? .55f : .65f;
            noise.frequency = .3f; noise.scrollSpeed = .3f; noise.damping = true; noise.quality = ParticleSystemNoiseQuality.Medium;
            var fade = ps.colorOverLifetime; fade.enabled = true;
            var gradient = new Gradient(); gradient.SetKeys(new[] { new GradientColorKey(Color.white,0), new GradientColorKey(Color.white,1) },
                new[] { new GradientAlphaKey(0,0), new GradientAlphaKey(1,.18f), new GradientAlphaKey(.8f,.7f), new GradientAlphaKey(0,1) });
            fade.color = gradient;
            var renderer = ps.GetComponent<ParticleSystemRenderer>(); renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
            return ps;
        }
    }
}
