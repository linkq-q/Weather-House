using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using WeatherHouse.Hillside;
using WeatherHouse.WildlifeJourney;

namespace WeatherHousePrototype
{
    /// <summary>Read-only presentation of an existing ecology body to the original conversation UI.</summary>
    [DisallowMultipleComponent]
    public sealed class WildlifeInteractionTarget : MonoBehaviour
    {
        [SerializeField] private WildlifeAgent agent;
        [SerializeField] private WildlifePopulation population;
        [SerializeField] private JourneyHerd herd;
        [SerializeField] private int memberId;
        [SerializeField] private string stableId;
        private Transform agentHead;

        private JourneyMember Member
        {
            get
            {
                if (herd && herd.members != null)
                    foreach (JourneyMember member in herd.members)
                        if (member != null && member.id == memberId && member.root == transform) return member;
                return null;
            }
        }
        public bool IsAvailable
        {
            get
            {
                if (!isActiveAndEnabled) return false;
                if (herd) return herd.isActiveAndEnabled && Member != null;
                return agent && agent.isActiveAndEnabled && population && population.isActiveAndEnabled &&
                    population.agents != null && Array.IndexOf(population.agents, agent) >= 0;
            }
        }
        public Transform VisualRoot => herd ? Member?.visualRoot : agent ? (agent.visualRoot ? agent.visualRoot : agent.transform) : null;
        public bool HasAmbientSpeech => IsAvailable && herd && herd.social && herd.social.isActiveAndEnabled &&
            herd.social.ActiveConversation != null && herd.social.ActiveConversation.speakerId == memberId && herd.social.VisibleBubbleCount > 0;
        private JourneyConversation VisibleConversation => HasAmbientSpeech ? herd.social.ActiveConversation : null;
        // A greeting is a life exchange, not a weather quest or evidence that a word has been learned.
        public string[] CurrentGlyphIds
        {
            get
            {
                JourneyConversation turn = VisibleConversation;
                return turn != null ? (turn.glyphIds != null ? (string[])turn.glyphIds.Clone() : Array.Empty<string>()) :
                    IsAvailable ? new[] { "hello" } : Array.Empty<string>();
            }
        }
        public string UtteranceId
        {
            get
            {
                JourneyConversation turn = VisibleConversation;
                return stableId + (turn != null ? ".social." + turn.sequenceId + "." + turn.turnIndex + "." + string.Join("+", turn.glyphIds ?? Array.Empty<string>()) :
                    IsAvailable ? ".greeting" : ".unavailable");
            }
        }
        public Vector3 BubbleAnchor
        {
            get
            {
                Transform head = herd ? Member?.headBone : agentHead;
                if (head && head.IsChildOf(transform)) return head.position + Vector3.up * .35f;
                Transform visual = VisualRoot;
                bool found = false; Bounds bounds = default;
                if (visual)
                    foreach (Renderer renderer in visual.GetComponentsInChildren<Renderer>())
                    {
                        if (!renderer.enabled) continue;
                        if (found) bounds.Encapsulate(renderer.bounds);
                        else { bounds = renderer.bounds; found = true; }
                    }
                return found ? new Vector3(bounds.center.x, bounds.max.y + .12f, bounds.center.z) : transform.position + Vector3.up;
            }
        }
        public string BehaviourDescription
        {
            get
            {
                if (!IsAvailable) return "暂时不在这里";
                if (herd)
                {
                    JourneyMember member = Member;
                    if (member.State == JourneyMemberState.Social) return "正在和附近的同伴交流";
                    if (member.State == JourneyMemberState.WaitingForBridge) return "等待桥上的通路，给同伴留出间距";
                    if (member.Mode == JourneyTravelMode.Swim) return member.ActualSpeed > .05f ? "在水面游动" : "浮在水面，等待前方通路";
                    if (member.State == JourneyMemberState.Travelling) return "跟随群体前往下一片活动地点";
                    return member.State == JourneyMemberState.Forage ? "在草地停留觅食" : "在群体附近停留，留意同伴";
                }
                if (agent.State == WildlifeState.Alert) return "停下来警觉地观察周围";
                if (agent.State == WildlifeState.Flee) return "正在远离令它不安的地方";
                WildlifeSolitaryRoutine routine = agent.SolitaryRoutine;
                if (routine != null)
                {
                    if (routine.ReturningHome) return "沿着可通行的路线返回自己的休息地";
                    string place = routine.Activity == SolitaryActivity.Drink ? "饮水处" : routine.Activity == SolitaryActivity.Forage ? "觅食处" :
                        routine.Activity == SolitaryActivity.Rest ? "休息处" : routine.Activity == SolitaryActivity.Patrol ? "巡游地点" : "观察点";
                    if (!routine.IsWorking) return routine.HasTarget ? "正在前往选定的" + place : "停下来寻找可到达的活动地点";
                    switch (routine.Activity)
                    {
                        case SolitaryActivity.Drink: return "停在可接近的岸边饮水";
                        case SolitaryActivity.Forage: return "停在选定的地点觅食";
                        case SolitaryActivity.Rest: return "在自己的休息地停留休息";
                        case SolitaryActivity.Patrol: return "查看独自巡游到达的地点";
                        default: return "停下来观察周围";
                    }
                }
                switch (agent.State)
                {
                    case WildlifeState.Greet: return "正在和附近的动物打招呼";
                    case WildlifeState.Forage: return "停下来觅食";
                    case WildlifeState.ReturnHome: return "正在返回熟悉的活动区域";
                    case WildlifeState.Takeoff: return "正在起飞";
                    case WildlifeState.Fly: return "正在空中飞行";
                    case WildlifeState.Landing: return "正在寻找落脚处降落";
                    case WildlifeState.Swim: return "正在水中游动";
                    case WildlifeState.Wander: return "在自己的活动范围内走动";
                    default: return "停下来观察身边的动静";
                }
            }
        }

        private sealed class Source
        {
            public Transform root;
            public WildlifeAgent agent;
            public WildlifePopulation population;
            public JourneyHerd herd;
            public int memberId;
            public string id, species;
        }

        public static AnimalActor[] BindScene(Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded) throw new ArgumentException("Bind a loaded scene.", nameof(scene));
            var sources = new List<Source>(); var seen = new HashSet<Transform>();
            GameObject[] roots = scene.GetRootGameObjects();
            // Herd ownership wins even if a transferred member still retains a disabled legacy component.
            foreach (GameObject root in roots)
                foreach (JourneyHerd herd in root.GetComponentsInChildren<JourneyHerd>(true))
                    foreach (JourneyMember member in herd.members ?? Array.Empty<JourneyMember>())
                        if (member != null && member.root && member.root.gameObject.scene == scene && seen.Add(member.root))
                            sources.Add(new Source { root = member.root, herd = herd, memberId = member.id, species = "sheep",
                                id = Identity(scene, herd.transform, "herd.sheep", member.id) });
            foreach (GameObject root in roots)
                foreach (WildlifePopulation population in root.GetComponentsInChildren<WildlifePopulation>(true))
                    foreach (WildlifeAgent agent in population.agents ?? Array.Empty<WildlifeAgent>())
                        if (agent && agent.gameObject.scene == scene && seen.Add(agent.transform))
                            sources.Add(new Source { root = agent.transform, population = population, agent = agent, species = agent.speciesId,
                                id = Identity(scene, population.transform, "agent." + agent.speciesId, agent.spawnId) });

            // Validate the whole set before adding anything, including duplicate persisted identities.
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (Source source in sources)
            {
                var existing = source.root.GetComponent<WildlifeInteractionTarget>();
                if (existing && !string.IsNullOrEmpty(existing.stableId)) source.id = existing.stableId;
                if (!ids.Add(source.id)) throw new InvalidOperationException("Duplicate ecology interaction identity: " + source.id);
                if (!existing && source.root.GetComponent<AnimalActor>())
                    throw new InvalidOperationException("An authored AnimalActor already owns " + source.root.name + "; do not silently convert it.");
            }
            var result = new AnimalActor[sources.Count];
            for (int i = 0; i < sources.Count; i++)
            {
                Source source = sources[i];
                var target = source.root.GetComponent<WildlifeInteractionTarget>() ?? source.root.gameObject.AddComponent<WildlifeInteractionTarget>();
                target.agent = source.agent; target.population = source.population; target.herd = source.herd;
                target.memberId = source.memberId; target.stableId = source.id; target.FindHead();
                // Installing this marker first prevents the immediate Actor.Awake from creating or tinting geometry.
                var actor = source.root.GetComponent<AnimalActor>() ?? source.root.gameObject.AddComponent<AnimalActor>();
                actor.BindExternalPresentation(target);
                actor.animalId = source.id; actor.displayName = DisplayName(source.species); actor.background = true;
                actor.requestGlyphIds = new[] { "hello" }; actor.satisfiedGlyphIds = new[] { "hello" };
                result[i] = actor;
            }
            return result;
        }
        private void FindHead()
        {
            agentHead = null;
            if (!agent) return;
            foreach (Transform bone in agent.GetComponentsInChildren<Transform>(true))
                if (bone.name == "Head" || bone.name == "head" || (agent.speciesId == "fox" && bone.name == "bone_1"))
                { agentHead = bone; break; }
        }
        private void Awake() { FindHead(); }
        private static string Identity(Scene scene, Transform owner, string kind, int id)
        {
            var path = new List<string>();
            for (Transform node = owner; node; node = node.parent) path.Add(node.name + "[" + node.GetSiblingIndex() + "]");
            path.Reverse();
            return scene.name + "/" + string.Join("/", path) + "/" + kind + "/" + id;
        }
        private static string DisplayName(string species)
        {
            switch (species)
            {
                case "fox": return "狐狸"; case "sheep": return "绵羊"; case "dog": return "小狗";
                case "deer": return "鹿"; case "rabbit": return "兔子"; case "squirrel": return "松鼠";
                case "turtle": return "乌龟"; case "robin": return "知更鸟"; case "fish": return "小鱼";
                case "butterfly": return "蝴蝶"; case "frog": return "青蛙";
                default: return string.IsNullOrEmpty(species) ? "动物" : species;
            }
        }
    }
}
