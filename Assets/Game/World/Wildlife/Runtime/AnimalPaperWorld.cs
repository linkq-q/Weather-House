using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using WeatherHouse.AnimalToon;
using WeatherHouse.WildlifeJourney;
using WeatherHousePrototype;

namespace WeatherHouse.Hillside
{
    // Runtime material copies keep imported animals and other scenes intact.
    public sealed class AnimalPaperWorld : MonoBehaviour
    {
        readonly Dictionary<Material,Material> materials=new Dictionary<Material,Material>();
        readonly Dictionary<Renderer,Material[]> originals=new Dictionary<Renderer,Material[]>();
        readonly List<AnimalToonView> cameras=new List<AnimalToonView>();
        Material template;
        GameplaySession game;
        public int RendererCount=>originals.Count;
        public int MaterialCount=>materials.Count;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Register(){SceneManager.sceneLoaded-=Loaded;SceneManager.sceneLoaded+=Loaded;Loaded(SceneManager.GetActiveScene(),LoadSceneMode.Single);}
        static void Loaded(Scene scene,LoadSceneMode mode)
        {if(scene.name=="WeatherHouse_WatersideV2"&&!FindObjectOfType<AnimalPaperWorld>())new GameObject("Animal paper rendering").AddComponent<AnimalPaperWorld>();}
        void Start()
        {
            template=Resources.Load<Material>("AnimalPaper");game=FindObjectOfType<GameplaySession>();
            if(!template){Debug.LogError("Animal paper template is missing.",this);enabled=false;return;}
            RefreshAnimals();InvokeRepeating(nameof(RefreshAnimals),1,2);
        }
        public void RefreshAnimals()
        {
            if(!template)return;
            if(game&&game.forestSprite)Apply(game.forestSprite.transform);
            foreach(var animal in FindObjectsOfType<WildlifeAgent>())Apply(animal.visualRoot);
            foreach(var animal in FindObjectsOfType<AnimalActor>())Apply(animal.VisualRoot);
            foreach(var herd in FindObjectsOfType<JourneyHerd>())foreach(var member in herd.members)Apply(member.visualRoot);
        }
        void Apply(Transform root)
        {
            if(!root)return;
            foreach(var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                if(!(renderer is SkinnedMeshRenderer)&&!(renderer is MeshRenderer)||originals.ContainsKey(renderer))continue;
                var old=renderer.sharedMaterials;var next=(Material[])old.Clone();bool changed=false;
                for(int i=0;i<old.Length;i++)
                {
                    var source=old[i];if(!source||source.renderQueue>=3000)continue;
                    if(!materials.TryGetValue(source,out var toon))
                    {
                        toon=new Material(template){name=source.name+" · Paper (runtime)",enableInstancing=source.enableInstancing};
                        string map=source.HasProperty("_BaseMap")?"_BaseMap":"_MainTex";
                        if(source.HasProperty(map)){toon.SetTexture("_BaseMap",source.GetTexture(map));toon.SetTextureScale("_BaseMap",source.GetTextureScale(map));toon.SetTextureOffset("_BaseMap",source.GetTextureOffset(map));}
                        string color=source.HasProperty("_BaseColor")?"_BaseColor":"_Color";if(source.HasProperty(color))toon.SetColor("_BaseColor",source.GetColor(color));
                        if(source.HasProperty("_Cull"))toon.SetFloat("_Cull",source.GetFloat("_Cull"));
                        materials.Add(source,toon);
                    }
                    next[i]=toon;changed=true;
                }
                if(changed){originals.Add(renderer,old);renderer.sharedMaterials=next;}
            }
        }
        void LateUpdate()
        {
            OptIn(Camera.main);
            if(game&&game.PhotoRig)OptIn(game.PhotoRig.CaptureCamera);
        }
        void OptIn(Camera camera)
        {
            if(!camera||camera.TryGetComponent<AnimalToonView>(out _))return;
            var view=camera.gameObject.AddComponent<AnimalToonView>();view.mergeRadius=12;cameras.Add(view);
        }
        void OnDestroy()
        {
            foreach(var pair in originals)if(pair.Key)pair.Key.sharedMaterials=pair.Value;
            foreach(var material in materials.Values)if(material)Destroy(material);
            foreach(var view in cameras)if(view)Destroy(view);
        }
    }
}
