using UnityEngine;
using UnityEngine.Rendering;

namespace WeatherHousePrototype
{
    public enum LocalWeather { Rain, Sun, Wind }

    public class WeatherRegion : MonoBehaviour
    {
        public string regionId;
        [Min(2f)] public float radius = 20f;
        public LocalWeather initialWeather = LocalWeather.Rain;
        public LocalWeather Current => initialized ? currentWeather : initialWeather;
        public float Wetness { get; private set; }
        public bool localPlayerWeather;
        public bool SoundActive => weatherActive;
        public int SoundStrength => strength;
        private bool weatherActive = true;
        private int strength = 1;
        private readonly ParticleSystem.Particle[] boundedParticles = new ParticleSystem.Particle[3200];

        private LocalWeather currentWeather;
        private bool initialized;
        private ParticleSystem rain;
        private ParticleSystem leaves;
        private Material particleMaterial;
        private Texture2D particleTexture;
        private Light sunshine;

        private void Awake()
        {
            Wetness = initialWeather == LocalWeather.Rain ? 1f : 0f;
            CreateEffects();
            SetWeather(initialWeather);
        }

        private void Update()
        {
            float target = weatherActive && Current == LocalWeather.Rain ? 1f : 0f;
            Wetness = Mathf.MoveTowards(Wetness, target,
                Time.deltaTime / (Current == LocalWeather.Sun ? 12f : 24f));
            if (sunshine != null)
                sunshine.intensity = Mathf.MoveTowards(sunshine.intensity,
                    weatherActive && Current == LocalWeather.Sun ? 3f : 0f, Time.deltaTime * 2f);
            if (localPlayerWeather) { BoundParticles(rain); BoundParticles(leaves); }
        }

        public bool Contains(Vector3 point)
        {
            Vector3 delta = point - transform.position;
            // World-space addition/subtraction can put an exact boundary a few microns outside.
            float boundary = radius + .001f;
            return delta.x * delta.x + delta.z * delta.z <= boundary * boundary;
        }

        public void SetWeather(LocalWeather weather)
        {
            weatherActive = true;
            currentWeather = weather;
            initialized = true;
            SetEmission(rain, weather == LocalWeather.Rain);
            SetEmission(leaves, weather == LocalWeather.Wind);
        }

        public void SetLocalWeather(LocalWeather weather, int intensity, bool active)
        {
            currentWeather = weather; initialized = true;
            weatherActive = active; strength = Mathf.Clamp(intensity, 1, 2);
            SetEmission(rain, active && weather == LocalWeather.Rain);
            SetEmission(leaves, active && weather == LocalWeather.Wind);
            if (rain) { var emission = rain.emission; emission.rateOverTime = radius * radius * (strength == 2 ? 4f : 1.5f); }
            if (leaves)
            {
                var emission = leaves.emission; emission.rateOverTime = strength == 2 ? 22f : 7f;
                var velocity = leaves.velocityOverLifetime; velocity.x = strength == 2 ? 5f : 1.4f;
            }
            if (!active) { rain?.Clear(); leaves?.Clear(); }
        }

        private void BoundParticles(ParticleSystem particles)
        {
            if (!particles || !particles.isPlaying) return;
            int count = particles.GetParticles(boundedParticles);
            for (int i = 0; i < count; i++)
                if (!Contains(boundedParticles[i].position)) boundedParticles[i].remainingLifetime = 0f;
            particles.SetParticles(boundedParticles, count);
        }

        private static void SetEmission(ParticleSystem effect, bool active)
        {
            if (effect == null) return;
            if (active) effect.Play();
            else effect.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }

        private void CreateEffects()
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (shader == null)
            {
                Debug.LogError("WeatherRegion requires the installed URP particle shader.", this);
                return;
            }
            particleTexture = new Texture2D(16, 16, TextureFormat.RGBA32, false)
            { name = "Weather particle soft circle", wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < 16; y++)
                for (int x = 0; x < 16; x++)
                {
                    float distance = new Vector2((x - 7.5f) / 7.5f, (y - 7.5f) / 7.5f).magnitude;
                    particleTexture.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01((1f - distance) * 3f)));
                }
            particleTexture.Apply();
            particleMaterial = new Material(shader) { name = "Local weather particles", renderQueue = 3000 };
            particleMaterial.SetTexture("_BaseMap", particleTexture);
            particleMaterial.SetColor("_BaseColor", Color.white);
            particleMaterial.SetFloat("_Surface", 1f);
            particleMaterial.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            particleMaterial.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            particleMaterial.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            particleMaterial.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
            particleMaterial.SetFloat("_ZWrite", 0f);
            particleMaterial.SetFloat("_Cull", (float)CullMode.Off);
            particleMaterial.SetOverrideTag("RenderType", "Transparent");
            particleMaterial.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");

            rain = CreateParticles("Local Rain", 0f, 1.7f, 0.055f,
                new Color(.68f, .82f, .92f, .48f), Mathf.Clamp(radius * radius * 2f, 400f, 1800f), 16f);
            var rainVelocity = rain.velocityOverLifetime;
            rainVelocity.enabled = true;
            rainVelocity.space = ParticleSystemSimulationSpace.World;
            rainVelocity.y = -17f;
            var renderer = rain.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Stretch;
            renderer.lengthScale = 6f;
            renderer.velocityScale = .025f;
            var collision = rain.collision;
            collision.enabled = true;
            collision.type = ParticleSystemCollisionType.World;
            collision.mode = ParticleSystemCollisionMode.Collision3D;
            collision.quality = ParticleSystemCollisionQuality.Low;
            collision.collidesWith = Physics.DefaultRaycastLayers;
            collision.enableDynamicColliders = false;
            collision.lifetimeLoss = 1f;
            collision.sendCollisionMessages = false;

            leaves = CreateParticles("Local Wind Leaves", .15f, 3f, .13f,
                new Color(.57f, .62f, .26f, .8f), 14f, 1.8f);
            var velocity = leaves.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.World;
            velocity.x = 3.5f;
            velocity.y = .35f;
            velocity.z = .7f;
            var noise = leaves.noise;
            noise.enabled = true;
            noise.strength = .6f;
            noise.frequency = .5f;
            var rotation = leaves.rotationOverLifetime;
            rotation.enabled = true;
            rotation.z = 2f;

            var lightObject = new GameObject("Local sunshine");
            lightObject.transform.SetParent(transform, false);
            lightObject.transform.localPosition = Vector3.up * 12f;
            lightObject.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            sunshine = lightObject.AddComponent<Light>();
            sunshine.type = LightType.Spot;
            sunshine.color = new Color(1f, .87f, .58f);
            sunshine.range = Mathf.Min(radius * 2f, 48f);
            sunshine.spotAngle = localPlayerWeather ? 2f * Mathf.Atan(radius / 12f) * Mathf.Rad2Deg : 130f;
            sunshine.intensity = 0f;
            sunshine.shadows = LightShadows.None;
        }

        private ParticleSystem CreateParticles(string objectName, float speed, float lifetime,
            float size, Color color, float rate, float height)
        {
            var child = new GameObject(objectName);
            child.transform.SetParent(transform, false);
            child.transform.localPosition = Vector3.up * height;
            var particles = child.AddComponent<ParticleSystem>();
            particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = particles.main;
            main.playOnAwake = false;
            main.loop = true;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = lifetime;
            main.startSpeed = speed;
            main.startSize = size;
            main.startColor = color;
            main.maxParticles = 3200;
            var emission = particles.emission;
            emission.rateOverTime = rate;
            var shape = particles.shape;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = radius * (objectName == "Local Rain" ? 1f : .65f);
            shape.radiusThickness = 1f;
            shape.rotation = new Vector3(90f, 0f, 0f);
            var renderer = particles.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = particleMaterial;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return particles;
        }

        private void OnDestroy()
        {
            if (particleMaterial != null) Destroy(particleMaterial);
            if (particleTexture != null) Destroy(particleTexture);
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Current == LocalWeather.Rain ? Color.cyan :
                Current == LocalWeather.Sun ? Color.yellow : Color.green;
            Vector3 previous = transform.position + Vector3.right * radius;
            for (int i = 1; i <= 64; i++)
            {
                float angle = i * Mathf.PI / 32f;
                Vector3 next = transform.position + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * radius;
                Gizmos.DrawLine(previous, next);
                previous = next;
            }
        }
    }
}
