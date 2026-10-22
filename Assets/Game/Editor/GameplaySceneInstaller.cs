using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace WeatherHousePrototype.Editor
{
    public static class GameplaySceneInstaller
    {
        [MenuItem("Weather House/Install Gameplay Prototype")]
        public static void InstallMenu() { Install(true); }

        public static void Install(bool save)
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play before installing gameplay.");
            var scene = SceneManager.GetActiveScene();
            if (Path.GetFileNameWithoutExtension(scene.path) != "WeatherHouse_MapDraft" && save)
                throw new InvalidOperationException("Open WeatherHouse_MapDraft before installing gameplay.");
            if (Object.FindObjectOfType<GameplaySession>() != null)
                throw new InvalidOperationException("Gameplay is already installed; edit its ordinary scene objects in place.");
            var walker = Object.FindObjectOfType<PrototypeWalker>();
            var terrain = Terrain.activeTerrain;
            if (walker == null || terrain == null) throw new InvalidOperationException("The map needs Terrain and PrototypeWalker.");
            var root = new GameObject("06 观察、天气与动物");
            Undo.RegisterCreatedObjectUndo(root, "安装天气屋玩法");
            string materialPath = "Assets/Game/Generated/AnimalPrototype.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (material == null)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                material.SetFloat("_Smoothness", .18f);
                AssetDatabase.CreateAsset(material, materialPath);
            }
            var home = Region(root.transform, "A 小屋", "home", new Vector2(-78f, 10f), 18f, LocalWeather.Sun, terrain);
            var courtyard = Region(root.transform, "B 林间庭院", "courtyard", new Vector2(-108f, 82.8f), 23f, LocalWeather.Rain, terrain);
            var lake = Region(root.transform, "C 湖畔浅洼", "lake", new Vector2(-19f, 50f), 16f, LocalWeather.Sun, terrain);
            var hill = Region(root.transform, "D 风中高台", "hill", new Vector2(111.6f, -104.4f), 18f, LocalWeather.Sun, terrain);
            var forest = Region(root.transform, "E 林中晾晒地", "forest", new Vector2(-131f, 46f), 15f, LocalWeather.Rain, terrain);
            Animal(home, material, terrain, "home-rabbit", "门前的小兔", AnimalKind.Rabbit, new Vector2(-76f, 12f), LocalWeather.Rain,
                new[] { "hello", "you", "home", "here" }, new[] { "rain", "wet", "cold" }, "", true);
            Animal(courtyard, material, terrain, "courtyard-squirrel", "晾松果的松鼠", AnimalKind.Squirrel, new Vector2(-103f, 76f), LocalWeather.Sun,
                new[] { "self", "reject", "rain", "want", "sunlight" }, new[] { "sunlight", "warm", "dry" },
                "松鼠不想再淋雨，希望这一片放晴，让湿松果能晾干。回答需要明确理解它想要停雨或日照。", false);
            Animal(lake, material, terrain, "lake-frog", "浅洼边的小蛙", AnimalKind.Frog, new Vector2(-20f, 49f), LocalWeather.Rain,
                new[] { "self", "want", "rain", "water" }, new[] { "rain", "wet", "hello" },
                "小蛙想让雨落在浅洼里，让这里重新有水。回答必须理解要下雨，只提水不够。", false);
            Animal(hill, material, terrain, "hill-bird", "练习飞翔的小鸟", AnimalKind.Bird, new Vector2(108f, -103f), LocalWeather.Wind,
                new[] { "self", "want", "wind" }, new[] { "wind", "come", "go" },
                "小鸟想让这片高台起风，借着风练习张开翅膀。", false);
            Animal(forest, material, terrain, "forest-rabbit", "晾布的小兔", AnimalKind.Rabbit, new Vector2(-129f, 45f), LocalWeather.Sun,
                new[] { "wet", "reject", "rain", "want", "sunlight" }, new[] { "sunlight", "dry", "warm" },
                "小兔的布被淋湿了，不想再下雨，希望放晴把布晾干。", false);
            Animal(courtyard, material, terrain, "courtyard-bird", "树下的小鸟", AnimalKind.Bird, new Vector2(-113f, 79f), LocalWeather.Sun,
                new[] { "rain", "wet", "tree", "here" }, new[] { "sunlight", "tree", "warm" }, "", true);
            Animal(lake, material, terrain, "lake-rabbit", "草地上的小兔", AnimalKind.Rabbit, new Vector2(-27f, 49f), LocalWeather.Rain,
                new[] { "grass", "dry", "sunlight" }, new[] { "grass", "wet", "rain" }, "", true);
            var session = root.AddComponent<GameplaySession>();
            session.walker = walker;
            session.animals = root.GetComponentsInChildren<AnimalActor>();
            session.regions = root.GetComponentsInChildren<WeatherRegion>();
            EditorSceneManager.MarkSceneDirty(scene);
            AssetDatabase.SaveAssets();
            if (save) EditorSceneManager.SaveScene(scene);
            Directory.CreateDirectory("PrototypeReview");
            File.WriteAllText("PrototypeReview/gameplay-install.txt", "PASS: installed 7 editable animals, 5 local weather regions, session and procedural HUD.\n");
        }

        private static WeatherRegion Region(Transform parent, string name, string id, Vector2 point, float radius, LocalWeather weather, Terrain terrain)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent);
            go.transform.position = Ground(point, terrain);
            var region = go.AddComponent<WeatherRegion>();
            region.regionId = id; region.radius = radius; region.initialWeather = weather;
            return region;
        }

        private static void Animal(WeatherRegion region, Material material, Terrain terrain, string id, string name, AnimalKind kind,
            Vector2 point, LocalWeather requested, string[] request, string[] satisfied, string meaning, bool background)
        {
            var go = new GameObject(name);
            go.transform.SetParent(region.transform);
            go.transform.position = SafeGround(point, terrain);
            go.transform.rotation = Quaternion.Euler(0f, 210f, 0f);
            var animal = go.AddComponent<AnimalActor>();
            animal.animalId = id; animal.displayName = name; animal.species = kind; animal.region = region;
            animal.requestedWeather = requested; animal.requestGlyphIds = request; animal.satisfiedGlyphIds = satisfied;
            animal.requestMeaning = meaning; animal.background = background; animal.visualMaterial = material;
            animal.BuildVisual();
        }

        private static Vector3 Ground(Vector2 point, Terrain terrain)
        {
            var world = new Vector3(point.x, 0f, point.y);
            world.y = terrain.SampleHeight(world) + terrain.transform.position.y;
            return world;
        }

        private static Vector3 SafeGround(Vector2 point, Terrain terrain)
        {
            for (int i = 0; i < 65; i++)
            {
                float angle = i * 2.39996f;
                var offset = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * (Mathf.Sqrt(i) * .65f);
                Vector3 candidate = Ground(point + offset, terrain);
                Vector3 uv = candidate - terrain.transform.position;
                if (candidate.y < 3.6f || terrain.terrainData.GetSteepness(uv.x / terrain.terrainData.size.x, uv.z / terrain.terrainData.size.z) > 25f) continue;
                if (Physics.OverlapCapsule(candidate + Vector3.up * .8f, candidate + Vector3.up * 1.6f, .65f)
                    .Any(c => !(c is TerrainCollider) && !(c is CharacterController) && !c.isTrigger)) continue;
                return candidate;
            }
            throw new InvalidOperationException("No clear animal position near " + point + "; inspect placement before retrying.");
        }
    }
}
