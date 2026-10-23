using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ForestSpriteCharacter;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using WeatherHouse.Hillside;
using WeatherHouse.Vegetation;
using Object = UnityEngine.Object;

namespace WeatherHouse.WatersideV2.Editor
{
    public static class WatersideSceneBuilder
    {
        public const string ScenePath = "Assets/Scenes/WeatherHouse_WatersideV2.unity";
        public const string Output = "Assets/Game/World/WatersideV2/Generated";
        public const string Review = "WatersideV2Review";

        [MenuItem("Weather House/Waterside V2/Build")]
        public static void Build()
        {
            RequireSaved();
            Directory.CreateDirectory(Output); Directory.CreateDirectory(Review);
            AssetDatabase.Refresh();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var terrain = WatersideTerrainBuilder.Build(Output, true);
            WatersideNatureBuilder.Build(terrain, Group("02 草地与水岸群落"), Output + "/Nature");
            WatersideStructureBuilder.Build(terrain, Group("03 自然台地与连接"), Output);
            WatersidePropsBuilder.Build(terrain, Group("04 生活停留点"), Output + "/Props");
            Lighting();
            var player = Player(terrain);
            var boundary = player.gameObject.AddComponent<WatersideWaterBoundary>();
            boundary.player = player; boundary.terrain = terrain;
            boundary.surface = player.gameObject.AddComponent<WatersideWaterSurface>();
            var mood = RenderSettings.sun.gameObject.AddComponent<WatersideMood>();
            mood.mainLight = RenderSettings.sun; mood.mainCamera = player.demoCamera;
            var soil = terrain.gameObject.AddComponent<WatersideSoilInteraction>();
            soil.terrain = terrain; soil.player = player; soil.soilLayer = 1;
            var soilShader = Shader.Find("Weather House/Waterside Soil Terrain");
            if (!soilShader) throw new InvalidOperationException("Soil terrain shader has not imported.");
            terrain.materialTemplate.shader = soilShader;
            EditorUtility.SetDirty(terrain.materialTemplate);
            Animals(terrain, player.transform);
            AssetDatabase.SaveAssets();
            EditorSceneManager.SaveScene(SceneManager.GetActiveScene(), ScenePath);
            EditorSceneManager.OpenScene(ScenePath);
            File.WriteAllText(Review + "/scene-checks.txt", Verify());
        }
        public static void RequireSaved()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) throw new InvalidOperationException("Exit Play and finish compilation before building.");
            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Unsaved editor scene preserved. Build has not started.");
        }
        public static void CompleteAnimalIntegration()
        {
            RequireSaved();
            var terrain = Object.FindObjectOfType<Terrain>();
            var population = Object.FindObjectOfType<WildlifePopulation>();
            if (!terrain || !population || AssetDatabase.GetAssetPath(terrain.terrainData) != Output + "/WatersideTerrain.asset")
                throw new InvalidOperationException("Only the generated Waterside V2 draft can finish integration.");
            WeatherHouse.WildlifeJourney.Editor.JourneyWatersideIntegration.Integrate(population, Camera.main);
            AssetDatabase.SaveAssets();
            EditorSceneManager.SaveScene(SceneManager.GetActiveScene(), ScenePath);
            File.WriteAllText(Review + "/scene-checks.txt", Verify());
        }
        public static void ApplyLanternDefaults()
        {
            RequireSaved();
            var scene = SceneManager.GetActiveScene();
            if (scene.path != ScenePath || SceneManager.sceneCount != 1)
                throw new InvalidOperationException("Update lanterns only with the saved Waterside V2 scene open by itself.");
            var lamps = Object.FindObjectsOfType<Light>().Where(light => light.gameObject.scene == scene &&
                light.name == "近人物暖光 · 范围4.5 无阴影").ToArray();
            if (lamps.Length != 3 || lamps.Any(light => light.type != LightType.Point))
                throw new InvalidOperationException("Expected the three existing low stone lanterns before applying their defaults.");
            foreach (var lamp in lamps) { lamp.intensity = WatersidePropsBuilder.LowLanternIntensity; EditorUtility.SetDirty(lamp); }
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("Could not save the lantern adjustment.");
            // Reload the saved scene so the transient day/night baseline takes the new light intensities.
            EditorSceneManager.OpenScene(ScenePath);
        }
        static ForestSpriteDemoController Player(Terrain terrain)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Characters/ForestSprite/Generated/ForestSprite.prefab");
            var root = (GameObject)PrefabUtility.InstantiatePrefab(prefab); root.name = "06 森林精灵";
            var player = root.GetComponent<ForestSpriteDemoController>();
            Vector3 p = new Vector3(-34.5f, 0, -30);
            p.y = WatersideLayout.GroundHeight(terrain, p.x, p.z) + .1f; root.transform.position = p;
            player.externalCameraControl = true; player.showControls = false; player.demonstration = ForestSpriteDemoController.Demonstration.Manual;
            var panel = root.GetComponent<ForestSpriteCapePanel>(); if (panel) panel.enabled = false;
            var camera = new GameObject("07 近景俯视镜头", typeof(Camera), typeof(AudioListener)).GetComponent<Camera>();
            camera.tag = "MainCamera"; camera.nearClipPlane = .1f; camera.farClipPlane = 320;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.64f,.75f,.73f); camera.allowHDR = true;
            var data = camera.gameObject.AddComponent<UniversalAdditionalCameraData>(); data.requiresDepthTexture = true; data.requiresColorTexture = true; data.renderPostProcessing = true;
            var orbit = camera.gameObject.AddComponent<VegetationTopDownCamera>();
            player.demoCamera = camera; orbit.player = player; orbit.baseYaw = 45; orbit.pitch = 43; orbit.distance = 18;
            orbit.distanceRange = new Vector2(12, 24); orbit.pitchRange = new Vector2(30, 52);
            orbit.yawLimit = 45; orbit.fieldOfView = 35; orbit.lookAhead = .8f; orbit.SnapToPlayer();
            var context = root.AddComponent<HillsidePlayerGround>(); context.terrain = terrain; context.player = player;
            if (player.capeCloth && player.capeCloth.cloth)
            {
                var floor = new GameObject("披风地面接触"); floor.layer = 2;
                var sphere = floor.AddComponent<SphereCollider>(); sphere.radius = 1000; sphere.enabled = false;
                context.clothFloor = sphere;
                player.capeCloth.cloth.sphereColliders = player.capeCloth.cloth.sphereColliders.Concat(new[] { new ClothSphereColliderPair(sphere) }).ToArray();
                context.PlaceFloor();
            }
            return player;
        }
        static void Lighting()
        {
            var light = new GameObject("05 温暖日光").AddComponent<Light>(); light.type = LightType.Directional;
            light.color = new Color(1,.95f,.83f); light.intensity = 1.05f;
            light.shadows = LightShadows.Soft; light.shadowStrength = .65f; light.shadowBias = .025f; light.shadowNormalBias = .22f;
            light.transform.rotation = Quaternion.Euler(51,-35,0); RenderSettings.sun = light;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(.57f,.69f,.72f);
            RenderSettings.ambientEquatorColor = new Color(.42f,.51f,.42f);
            RenderSettings.ambientGroundColor = new Color(.22f,.28f,.18f);
            RenderSettings.ambientIntensity = 1;
            RenderSettings.fog = true; RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = new Color(.63f,.74f,.73f); RenderSettings.fogStartDistance = 95; RenderSettings.fogEndDistance = 260;
        }
        static void Animals(Terrain terrain, Transform player)
        {
            var pop = new GameObject("08 分区动物与群体行为").AddComponent<WildlifePopulation>(); pop.terrain = terrain; pop.player = player;
            pop.waterSurface = pop.gameObject.AddComponent<WatersideWaterSurface>();
            pop.worldBounds = new Bounds(Vector3.zero, new Vector3(160,120,160));
            var areas = new List<HabitatVolume>();
            foreach (var channel in WatersideLayout.WaterChannels)
                foreach (var node in channel.Nodes)
                    areas.Add(new HabitatVolume { id = channel.Name, center = node.Position, radii = Vector2.one * Mathf.Max(.8f,node.HalfWidth * .85f) });
            pop.waterAreas = areas.ToArray();
            var agents = new List<WildlifeAgent>(); int group = 0;
            var random = new System.Random(20261023);
            Spawn("dog",3, WatersideLayout.Entry,8,-1);
            Spawn("rabbit",6, WatersideLayout.Design(26,63),10,-1);
            Spawn("rabbit",5, WatersideLayout.Design(70,40),9,-1);
            Spawn("squirrel",6, WatersideLayout.OldTree,11,-1);
            Spawn("fox",2, WatersideLayout.Design(23,45),8,-1);
            Spawn("sheep",10, WatersideLayout.Design(71,39),11,-1);
            Spawn("deer",11, WatersideLayout.Design(61,29),13,-1);
            Spawn("turtle",5, WatersideLayout.Design(46,60),8,-1);
            for (int i = 0; i < 4; i++) Spawn("robin",3, WatersideLayout.Design(30 + i * 13, 69 - i * 12),18,-1);
            int[] fishAreas = Enumerable.Range(0,areas.Count).OrderByDescending(i => areas[i].radii.x).Take(4).ToArray();
            foreach (int water in fishAreas) Spawn("fish",5,areas[water].center,areas[water].radii.x*.7f,water);
            pop.agents = agents.ToArray();
            WeatherHouse.WildlifeJourney.Editor.JourneyWatersideIntegration.Integrate(pop, Camera.main);

            void Spawn(string species, int count, Vector3 center, float radius, int water)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/World/Hillside/Generated/" + species + ".prefab");
                var parent = Group(species + " 群 " + ++group, pop.transform);
                for (int i = 0; i < count; i++)
                {
                    var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab,parent);
                    var agent = go.GetComponent<WildlifeAgent>(); agent.spawnId = agents.Count; agent.groupId = group;
                    agent.habitatCenter = center; agent.habitatRadius = radius; agent.waterAreaIndex = water;
                    bool legal = false; Vector3 p = center;
                    for (int attempt = 0; attempt < 700; attempt++)
                    {
                        float a = (float)random.NextDouble()*Mathf.PI*2, r = Mathf.Sqrt((float)random.NextDouble())*radius*.85f;
                        p = center + new Vector3(Mathf.Cos(a),0,Mathf.Sin(a))*r;
                        p.y = WatersideLayout.GroundHeight(terrain,p.x,p.z);
                        if (species == "robin") { p.y = Mathf.Max(p.y,WatersideLayout.WaterHeight(p.x,p.z))+5; legal=true;break; }
                        if (water >= 0)
                        {
                            if (!areas[water].Contains(p,.5f) || p.y > areas[water].center.y-.6f) continue;
                            p.y = pop.waterSurface.Height(p)-.4f;
                            if (!pop.TryProjectPosition(agent,p,out p)) continue;
                            legal=true;break;
                        }
                        if (WatersideLayout.IsWater(p.x,p.z,.7f) || WatersideLayout.PathEdgeDistance(p.x,p.z)<.8f) continue;
                        if (!pop.TryProjectPosition(agent,p,out p) || agents.Any(other => Vector3.Distance(other.transform.position,p)<1.1f)) continue;
                        if (Physics.OverlapSphere(p+Vector3.up*.5f,agent.bodyRadius).Any(c=>!(c is TerrainCollider)&&!c.transform.IsChildOf(go.transform))) continue;
                        legal=true;break;
                    }
                    if (!legal) throw new InvalidOperationException("No safe V2 spawn: "+species);
                    go.transform.position=p;go.transform.rotation=Quaternion.Euler(0,random.Next(360),0); agents.Add(agent);
                }
            }
        }
        public static string Verify()
        {
            if (SceneManager.GetActiveScene().path != ScenePath) throw new InvalidOperationException("Open WatersideV2 to verify.");
            Terrain terrain = Object.FindObjectOfType<Terrain>();
            var grass = Object.FindObjectOfType<HillsideGrassRenderer>();
            var pop = Object.FindObjectOfType<WildlifePopulation>();
            var herd = Object.FindObjectOfType<WeatherHouse.WildlifeJourney.JourneyHerd>();
            if (!terrain || !grass || !grass.placementMap || !pop || pop.agents.Length != 70 || !herd || herd.members.Length != 10)
                throw new InvalidOperationException("Terrain, mapped meadow, or the 70 wildlife + 10 Journey sheep are missing.");
            if (Object.FindObjectsOfType<ForestSpriteDemoController>().Length != 1) throw new InvalidOperationException("Exactly one player is required.");
            return "PASS\n" + WatersideTerrainBuilder.Verify(terrain) + "\n" + WatersidePlantBuilder.Verify(Output + "/Nature/Plants") +
                "\n80 animals, 9 species (70 original agents + 10 Journey sheep); existing character prefab and calibrated meadow; independent scene and terrain.\n";
        }
        public static Transform Group(string name, Transform parent = null)
        { var root = new GameObject(name).transform; root.SetParent(parent,false); return root; }
    }
}
