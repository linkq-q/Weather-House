using UnityEngine;

namespace WeatherHousePrototype
{
    public enum AnimalKind { Squirrel, Frog, Rabbit, Bird }

    public class AnimalActor : MonoBehaviour
    {
        public string animalId;
        public string displayName;
        public AnimalKind species;
        public WeatherRegion region;
        public LocalWeather requestedWeather;
        public string[] requestGlyphIds;
        public string[] satisfiedGlyphIds;
        [TextArea] public string requestMeaning;
        public bool background;
        public Material visualMaterial;
        public bool RequestSatisfied => !ExternalPresentation && region != null && region.Current == requestedWeather;
        public string[] CurrentGlyphIds => ExternalPresentation ? (ecology ? ecology.CurrentGlyphIds : System.Array.Empty<string>()) :
            RequestSatisfied ? satisfiedGlyphIds : requestGlyphIds;
        public string UtteranceId => ExternalPresentation ? (ecology ? ecology.UtteranceId : animalId + ".unavailable") :
            animalId + (RequestSatisfied ? ".satisfied" : ".request");
        public Transform VisualRoot => ExternalPresentation ? (ecology ? ecology.VisualRoot : null) : transform.Find("Visual");
        public bool IsAvailableForObservation => isActiveAndEnabled && (!ExternalPresentation || (ecology && ecology.IsAvailable));
        public bool HasAmbientSpeech => ExternalPresentation && ecology && ecology.HasAmbientSpeech;
        public Vector3 BubbleAnchor => ExternalPresentation ? (ecology ? ecology.BubbleAnchor : transform.position) :
            transform.TransformPoint(Vector3.up * (species == AnimalKind.Rabbit ? 1.6f : 1.35f));
        public string BehaviourDescription
        {
            get
            {
                if (ExternalPresentation) return ecology ? ecology.BehaviourDescription : "暂时不在这里";
                if (region == null) return "在林间四处张望";
                if (region.Current == LocalWeather.Rain)
                    return species == AnimalKind.Frog ? "蹲在湿地里，一下一下地鼓起肚子" : "缩在叶子下面，时不时探头看雨";
                if (region.Current == LocalWeather.Wind)
                    return species == AnimalKind.Bird ? "张开翅膀，随着风轻轻起落" : "迎着风抬起头，看着身边翻动的叶片";
                return species == AnimalKind.Squirrel ? "把松果摊开晾晒，低头翻动它们" :
                    species == AnimalKind.Rabbit ? "在摊开的布旁伸展身体，看着布慢慢晒干" :
                    species == AnimalKind.Frog ? "在逐渐变干的浅洼边缓缓挪动" : "在阳光下梳理翅膀";
            }
        }

        private Transform visual;
        private Transform props;
        private Transform leftWing;
        private Transform rightWing;
        private Transform cloth;
        private Transform puddle;
        private readonly Transform[] looseLeaves = new Transform[3];
        private readonly RaycastHit[] hits = new RaycastHit[8];
        private Vector3 anchor;
        private Quaternion anchorRotation;
        private Terrain terrain;
        private bool observed;
        private float phase;
        private Material runtimeMaterial;
        private MaterialPropertyBlock tintBlock;
        [SerializeField, HideInInspector] private bool externalPresentation;
        private WildlifeInteractionTarget ecology;
        private bool ExternalPresentation
        {
            get
            {
                if (!ecology) ecology = GetComponent<WildlifeInteractionTarget>();
                // The marker is installed before AddComponent, so even its immediate Awake is safe.
                if (ecology) externalPresentation = true;
                return externalPresentation;
            }
        }

        internal void BindExternalPresentation(WildlifeInteractionTarget target)
        {
            // Inactive bodies may lose their marker before their first Awake; keep ownership latched.
            ecology = target;
            externalPresentation = true;
        }

        private void Awake()
        {
            if (ExternalPresentation) return;
            anchor = transform.position;
            anchorRotation = transform.rotation;
            terrain = Terrain.activeTerrain;
            phase = Mathf.Abs(transform.position.x * .7f + transform.position.z * .3f);
            BuildVisual();
        }

        public void SetObserved(bool value) { observed = value; }

        public void BuildVisual()
        {
            if (ExternalPresentation) return;
            visual = transform.Find("Visual");
            if (visual == null)
            {
                visual = new GameObject("Visual").transform;
                visual.SetParent(transform, false);
                Color fur = species == AnimalKind.Squirrel ? new Color(.57f, .29f, .13f) :
                    species == AnimalKind.Frog ? new Color(.28f, .52f, .26f) :
                    species == AnimalKind.Rabbit ? new Color(.86f, .80f, .67f) : new Color(.33f, .47f, .57f);
                Part(visual, "Body", new Vector3(0f, .33f, 0f), new Vector3(.55f, .56f, .66f), fur);
                Part(visual, "Head", new Vector3(0f, .65f, .25f), new Vector3(.43f, .43f, .43f), fur);
                Part(visual, "Muzzle", new Vector3(0f, .56f, .45f), new Vector3(.25f, .16f, .12f), new Color(.94f, .86f, .69f));
                for (int side = -1; side <= 1; side += 2)
                {
                    Part(visual, "Eye", new Vector3(side * .16f, .71f, .40f), Vector3.one * .065f, new Color(.10f, .10f, .08f));
                    Part(visual, "Foot", new Vector3(side * .20f, .075f, .10f), new Vector3(.17f, .15f, .34f), fur);
                }
                if (species == AnimalKind.Rabbit)
                {
                    Part(visual, "Ear left", new Vector3(-.12f, 1.03f, .19f), new Vector3(.14f, .55f, .16f), fur);
                    Part(visual, "Ear right", new Vector3(.12f, 1.03f, .19f), new Vector3(.14f, .55f, .16f), fur);
                    Part(visual, "Tail", new Vector3(0f, .31f, -.36f), Vector3.one * .24f, Color.white);
                }
                else if (species == AnimalKind.Squirrel)
                {
                    Part(visual, "Tail", new Vector3(0f, .56f, -.38f), new Vector3(.42f, .82f, .45f), fur * .8f);
                    Part(visual, "Ear left", new Vector3(-.15f, .85f, .17f), new Vector3(.13f, .23f, .13f), fur);
                    Part(visual, "Ear right", new Vector3(.15f, .85f, .17f), new Vector3(.13f, .23f, .13f), fur);
                }
                else if (species == AnimalKind.Frog)
                {
                    Part(visual, "Eye mound left", new Vector3(-.20f, .81f, .28f), Vector3.one * .22f, fur);
                    Part(visual, "Eye mound right", new Vector3(.20f, .81f, .28f), Vector3.one * .22f, fur);
                    Part(visual, "Haunch left", new Vector3(-.32f, .20f, -.10f), new Vector3(.35f, .28f, .38f), fur);
                    Part(visual, "Haunch right", new Vector3(.32f, .20f, -.10f), new Vector3(.35f, .28f, .38f), fur);
                }
                else
                {
                    Part(visual, "Wing left", new Vector3(-.32f, .46f, -.06f), new Vector3(.46f, .10f, .40f), fur * .8f);
                    Part(visual, "Wing right", new Vector3(.32f, .46f, -.06f), new Vector3(.46f, .10f, .40f), fur * .8f);
                    Part(visual, "Beak", new Vector3(0f, .62f, .52f), new Vector3(.14f, .10f, .24f), new Color(.92f, .62f, .19f));
                }
            }
            props = transform.Find("WeatherProps");
            if (props == null)
            {
                props = new GameObject("WeatherProps").transform;
                props.SetParent(transform, false);
                if (species != AnimalKind.Frog)
                {
                    Part(props, "Shelter stem", new Vector3(-.8f, .45f, -.1f), new Vector3(.08f, .9f, .08f), new Color(.35f, .39f, .18f));
                    Part(props, "Shelter leaf", new Vector3(-.55f, .91f, -.1f), new Vector3(1.1f, .11f, .85f), new Color(.31f, .46f, .20f));
                }
                if (species == AnimalKind.Rabbit)
                    Part(props, "Drying cloth", new Vector3(.85f, .04f, .12f), new Vector3(.73f, .055f, .62f), new Color(.69f, .64f, .44f), PrimitiveType.Cube);
                if (species == AnimalKind.Squirrel)
                    for (int i = 0; i < 3; i++)
                        Part(props, "Pine cone " + i, new Vector3(.65f + i * .16f, .09f, .3f - i * .1f), new Vector3(.12f, .18f, .12f), new Color(.40f, .22f, .10f));
                if (species == AnimalKind.Frog)
                    Part(props, "Wet hollow", new Vector3(.45f, .015f, .12f), new Vector3(1.25f, .025f, .85f), new Color(.29f, .49f, .51f));
                for (int i = 0; i < 3; i++)
                    Part(props, "Loose leaf " + i, new Vector3(.6f + i * .2f, .04f, -.5f), new Vector3(.23f, .025f, .10f), new Color(.53f, .57f, .23f));
            }
            leftWing = visual.Find("Wing left");
            rightWing = visual.Find("Wing right");
            cloth = props.Find("Drying cloth");
            puddle = props.Find("Wet hollow");
            for (int i = 0; i < 3; i++) looseLeaves[i] = props.Find("Loose leaf " + i);
            // Property blocks are not serialized; restore the part colors after scene load.
            foreach (Renderer renderer in GetComponentsInChildren<Renderer>())
            {
                renderer.sharedMaterial = GetVisualMaterial();
                ApplyPartColor(renderer);
            }
        }

        private void Update()
        {
            if (ExternalPresentation) return;
            if (visual == null) return;
            LocalWeather weather = region == null ? LocalWeather.Sun : region.Current;
            float time = Time.time + phase;
            bool shelter = weather == LocalWeather.Rain && species != AnimalKind.Frog;
            Vector3 offset = shelter ? new Vector3(-.55f, 0f, -.10f) :
                new Vector3(Mathf.Sin(time * .25f) * .45f, 0f, Mathf.Cos(time * .31f) * .25f);
            if (observed && !shelter) offset *= .3f;
            Vector3 desired = anchor + anchorRotation * offset;
            if (terrain != null) desired.y = terrain.SampleHeight(desired) + terrain.transform.position.y;
            Vector3 movement = Vector3.MoveTowards(transform.position, desired, Time.deltaTime * .55f) - transform.position;
            if (CanMove(movement)) transform.position += movement;
            transform.rotation = anchorRotation * Quaternion.Euler(0f,
                shelter ? 15f : Mathf.Sin(time * .45f) * (observed ? 10f : 27f), 0f);
            props.SetPositionAndRotation(anchor, anchorRotation);
            float bounce = species == AnimalKind.Frog && weather == LocalWeather.Rain ?
                Mathf.Max(0f, Mathf.Sin(time * 2.3f)) * .11f : Mathf.Sin(time * 2f) * .014f;
            if (species == AnimalKind.Bird && weather == LocalWeather.Wind)
                bounce = .20f + Mathf.Sin(time * 3f) * .08f;
            visual.localPosition = Vector3.up * bounce;
            visual.localScale = new Vector3(1f, shelter ? .77f : 1f, 1f);
            visual.localRotation = Quaternion.Euler(weather == LocalWeather.Sun ? Mathf.Sin(time * 1.8f) * 6f : 0f,
                0f, weather == LocalWeather.Wind ? Mathf.Sin(time * 3.2f) * 7f : 0f);
            if (leftWing != null)
            {
                float flap = weather == LocalWeather.Wind ? Mathf.Sin(time * 11f) * 40f : Mathf.Sin(time) * 6f;
                leftWing.localRotation = Quaternion.Euler(0f, 0f, flap);
                rightWing.localRotation = Quaternion.Euler(0f, 0f, -flap);
            }
            if (cloth != null)
            {
                cloth.localRotation = Quaternion.Euler(weather == LocalWeather.Wind ? Mathf.Sin(time * 6f) * 18f : 0f, 0f, 0f);
                float wet = region == null ? 0f : region.Wetness;
                Tint(cloth.GetComponent<Renderer>(), Color.Lerp(new Color(.92f, .86f, .67f), new Color(.47f, .49f, .40f), wet));
            }
            if (puddle != null)
            {
                float wet = region == null ? 1f : region.Wetness;
                puddle.localScale = new Vector3(1.25f, .025f, .85f) * Mathf.Lerp(.25f, 1f, wet);
            }
            for (int i = 0; i < 3; i++)
                if (looseLeaves[i] != null)
                {
                    float drift = time * 1.6f + i * 2f;
                    bool windy = weather == LocalWeather.Wind;
                    looseLeaves[i].localPosition = windy ? new Vector3(Mathf.Sin(drift) * .8f, .3f + Mathf.Cos(drift) * .2f, -.5f + Mathf.Cos(drift) * .35f) : new Vector3(.6f + i * .2f, .04f, -.5f);
                    looseLeaves[i].localRotation = windy ? Quaternion.Euler(drift * 80f, drift * 55f, 15f) : Quaternion.identity;
                }
        }

        private bool CanMove(Vector3 movement)
        {
            if (movement.sqrMagnitude < .000001f) return true;
            int count = Physics.SphereCastNonAlloc(transform.position + Vector3.up * .4f, .22f,
                movement.normalized, hits, movement.magnitude, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
                if (!(hits[i].collider is TerrainCollider) && !hits[i].transform.IsChildOf(transform)) return false;
            return true;
        }

        private void Part(Transform parent, string objectName, Vector3 position, Vector3 scale,
            Color color, PrimitiveType primitive = PrimitiveType.Sphere)
        {
            GameObject part = GameObject.CreatePrimitive(primitive);
            part.name = objectName;
            part.transform.SetParent(parent, false);
            part.transform.localPosition = position;
            part.transform.localScale = scale;
            Collider collider = part.GetComponent<Collider>();
            collider.enabled = false;
            if (Application.isPlaying) Destroy(collider); else DestroyImmediate(collider);
            Renderer renderer = part.GetComponent<Renderer>();
            renderer.sharedMaterial = GetVisualMaterial();
            Tint(renderer, color);
        }

        private Material GetVisualMaterial()
        {
            if (visualMaterial != null) return visualMaterial;
            if (runtimeMaterial == null)
                runtimeMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "Animal prototype" };
            return runtimeMaterial;
        }

        private void ApplyPartColor(Renderer renderer)
        {
            Color fur = species == AnimalKind.Squirrel ? new Color(.57f, .29f, .13f) :
                species == AnimalKind.Frog ? new Color(.28f, .52f, .26f) :
                species == AnimalKind.Rabbit ? new Color(.86f, .80f, .67f) : new Color(.33f, .47f, .57f);
            string part = renderer.name;
            Color color = part == "Eye" ? new Color(.10f, .10f, .08f) :
                part == "Muzzle" ? new Color(.94f, .86f, .69f) :
                part == "Beak" ? new Color(.92f, .62f, .19f) :
                part.StartsWith("Shelter") ? new Color(.31f, .46f, .20f) :
                part.StartsWith("Loose leaf") ? new Color(.53f, .57f, .23f) :
                part.StartsWith("Pine cone") ? new Color(.40f, .22f, .10f) :
                part == "Drying cloth" ? new Color(.69f, .64f, .44f) :
                part == "Wet hollow" ? new Color(.29f, .49f, .51f) :
                part == "Tail" && species == AnimalKind.Rabbit ? Color.white : fur;
            Tint(renderer, color);
        }

        private void Tint(Renderer renderer, Color color)
        {
            if (tintBlock == null) tintBlock = new MaterialPropertyBlock();
            tintBlock.SetColor("_BaseColor", color);
            renderer.SetPropertyBlock(tintBlock);
        }

        private void OnDestroy()
        {
            if (runtimeMaterial != null)
            {
                if (Application.isPlaying) Destroy(runtimeMaterial); else DestroyImmediate(runtimeMaterial);
            }
        }
    }
}
