using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor.SceneManagement;
using UnityEngine;
using WeatherHouse.Hillside;
using WeatherHouse.Life;
using WeatherHousePrototype;
using Object = UnityEngine.Object;

public static class LifeGuidanceChecks
{
    const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    static object Get(object owner, string name) => owner.GetType().GetField(name, Flags).GetValue(owner);
    static void Set(object owner, string name, object value) => owner.GetType().GetField(name, Flags).SetValue(owner, value);
    static object Call(object owner, string name, params object[] values) => owner.GetType().GetMethod(name, Flags).Invoke(owner, values);
    static void Property(object owner, string name, object value) => owner.GetType().GetProperty(name, Flags).GetSetMethod(true).Invoke(owner, new[] { value });
    static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }

    sealed class Fixture : IDisposable
    {
        public readonly GameObject root;
        public readonly LifeWorld world;
        public readonly LifeSession life;
        public readonly WildlifePopulation population;
        public readonly WildlifeAgent animal;
        public readonly Transform player;
        public readonly LifeRequest request;
        public readonly LifeSpeaker speaker;
        public object Active => Get(world, "guidance");
        public Fixture(string condition = "arrive")
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            root = new GameObject("Guidance fixture");
            var session = new GameObject("Fixture session"); session.transform.SetParent(root.transform); session.SetActive(false);
            var game = session.AddComponent<GameplaySession>(); life = session.AddComponent<LifeSession>();
            Property(life, "Gameplay", game); world = session.AddComponent<LifeWorld>(); Set(world, "owner", life);
            player = new GameObject("Fixture player").transform; player.SetParent(root.transform); player.position = Vector3.left * 2.8f;
            game.walker = player.gameObject.AddComponent<PrototypeWalker>();
            population = root.AddComponent<WildlifePopulation>(); population.worldBounds = new Bounds(Vector3.up * 10, new Vector3(40, 20, 40));
            animal = new GameObject("Fixture fox").AddComponent<WildlifeAgent>(); animal.transform.SetParent(root.transform);
            animal.speciesId = "fox"; animal.scaleRange = Vector2.one; animal.bodyRadius = .3f; animal.timidity = animal.sociability = 0;
            animal.walkSpeed = 1; population.agents = new[] { animal }; population.Initialize();
            var actor = animal.gameObject.AddComponent<AnimalActor>(); actor.animalId = "fixture.fox";
            speaker = animal.gameObject.AddComponent<LifeSpeaker>(); speaker.key = "fox"; speaker.Actor = actor;
            request = new LifeRequest { id = "fixture.guide", speakerKey = "fox", targetId = "fox.tree", condition = condition,
                objective = "fixture", openForPhotography = true, glyphIds = new[] { "go", "tree" } };
            var content = new[] { request }; speaker.requests = content; speaker.ConfigureProgress(life.Progress, content); Set(world, "requests", content);
            var anchors = (IDictionary)Get(world, "anchors"); anchors["fox.tree"] = Vector3.forward * 7; anchors["fox.here"] = Vector3.forward;
            object binding = Activator.CreateInstance(typeof(LifeWorld).GetNestedType("Binding", BindingFlags.NonPublic), true);
            Set(binding, "speaker", speaker); Set(binding, "wildlife", animal); Set(binding, "population", population);
            ((IDictionary)Get(world, "bindings"))["fox"] = binding;
            life.Progress.Hear(request, actor.animalId, true, true); world.OnTaskAccepted(request);
            Physics.SyncTransforms();
        }
        public void Update(bool skipInitialCue = true)
        {
            if (Active != null)
            {
                if (skipInitialCue) Set(Active, "began", Time.time - 2);
                Set(Active, "renewAt", float.NegativeInfinity);
            }
            Call(world, "UpdateGuidance");
        }
        public void Move(bool follow)
        {
            Property(population, "SimulationTime", population.SimulationTime + .05f);
            Call(animal, "Move", .05f, population.SimulationTime);
            if (follow) player.position = animal.transform.position + Vector3.left * 2.8f;
            Physics.SyncTransforms();
        }
        public void Dispose() { Object.DestroyImmediate(root); EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single); }
    }

    public static string[] Run()
    {
        Require(Application.isBatchMode && !Application.isPlaying && Application.dataPath.Contains("ValidationProject"),
            "Guidance checks require the independent ValidationProject batch editor");
        var results = new List<string>();
        using (var f = new Fixture())
        {
            f.Update(); Require(f.Active == null && !f.animal.HasLifeTarget, "A visible task started guidance without listening");
            var forged = JsonUtility.FromJson<LifeRequest>(JsonUtility.ToJson(f.request));
            Require(!f.world.BeginGuidance(forged), "An unbound request with a copied ID started guidance");
            string progress = JsonUtility.ToJson(f.life.Progress);
            Require(f.world.BeginGuidance(f.request), "Current legal listening target could not start guidance");
            Require(!f.animal.RequestAutonomousTarget(Vector3.right * 3, false), "AI stole the guidance movement lease");
            for (int i = 0; i < 65; i++) { f.Update(); f.Move(true); }
            Require(f.animal.transform.position.z > 1.5f && f.animal.transform.position.z < 7.1f,
                "Guidance did not actually walk through the normal movement owner");
            Require(progress == JsonUtility.ToJson(f.life.Progress), "Guidance fabricated task completion or progression");
            f.player.position = f.animal.transform.position + Vector3.left * 6;
            f.Update(); Require(f.Active != null && (float)Get(f.Active,"waitingSince")>=0 && f.animal.HasLifeTarget,
                "Guidance did not enter the waiting state and retain its temporary lease");
            // A synchronous Edit-mode call cannot advance Time.time. Negative timestamps collide with
            // the unstarted-wait sentinel, so timing is verified by LifeGuidancePlayReview in real Play.
            Call(f.world,"StopGuidance",true);
            Require(f.Active == null && !f.animal.HasLifeTarget, "Explicit guidance cleanup did not release its own lease");
            object reminder = Get(f.world, "guidanceReminder");
            Require(reminder != null && (float)Get(reminder, "due") >= Time.time + 119.9f, "Reminder was scheduled before two minutes");
            f.Update(); Require(f.Active == null, "Reminder restarted before its cooldown");
            f.player.position = f.animal.transform.position + Vector3.left * 2.8f;
            Set(reminder, "playerPosition", f.player.position); Set(reminder, "due", Time.time - .1f);
            Property(f.animal, "ActualSpeed", 0f); f.Update();
            Require(f.Active != null && (bool)Get(f.Active, "cueOnly") && Get(f.Active, "search") == null,
                "An overdue reminder restarted a route instead of a stationary cue");
            Vector3 stationary = f.animal.transform.position;
            for (int i = 0; i < 10; i++) { f.Update(); f.Move(false); }
            Require(Vector3.Distance(stationary, f.animal.transform.position) < .01f, "Reminder pulled the animal into another tour");
            results.Add("PASS only current explicit interaction starts real movement; AI cannot steal it; no task writes; waiting retains its lease and explicit fixture cleanup releases it; 120-second reminder stays stationary. Actual four-second timing is checked separately in Play");
        }
        using (var f = new Fixture())
        {
            Require(f.world.BeginGuidance(f.request), "Lease replacement fixture failed");
            Require(f.animal.RequestLifeTarget(Vector3.right * 2, 1.5f), "Another explicit interaction could not replace the guide");
            int revision = f.animal.IntentRevision; f.Update();
            Require(f.Active == null && f.animal.HasLifeTarget && f.animal.IntentRevision == revision,
                "Cancelling an obsolete guide released the new interaction owner's lease");
            results.Add("PASS another real interaction cancels the guide without releasing its replacement lease");
        }
        using (var f = new Fixture())
        {
            var rain = new GameObject("Fixture rain").AddComponent<WeatherRegion>(); rain.transform.SetParent(f.root.transform);
            rain.radius = 8; rain.SetLocalWeather(LocalWeather.Rain, 2, false);
            Require(f.world.BeginGuidance(f.request), "Rain fixture failed");
            rain.SetLocalWeather(LocalWeather.Rain, 2, true); f.Update();
            Require(f.Active == null && !f.animal.HasLifeTarget, "Strong rain retained the guiding lease");
            rain.SetLocalWeather(LocalWeather.Rain, 2, false);
            Require(f.world.BeginGuidance(f.request), "Alert fixture failed");
            Property(f.animal, "State", WildlifeState.Alert); f.Update();
            Require(f.Active == null && !f.animal.HasLifeTarget, "A startled animal remained held by guidance");
            results.Add("PASS strong rain and actual Alert state safely release guidance");
        }
        using (var f = new Fixture())
        {
            var director = f.root.AddComponent<AnimalAutonomy>(); director.Initialize(f.world, f.player);
            Set(f.world, "autonomy", director);
            Type animalType = typeof(AnimalAutonomy).GetNestedType("Animal", BindingFlags.NonPublic);
            object state = Activator.CreateInstance(animalType, true);
            Set(state, "agent", f.animal); Set(state, "root", f.animal.transform); Set(state, "species", "fox"); Set(state, "id", "fixture.fox");
            Call(director, "Add", state);
            director.Rain(Vector3.back * 7, 8, true, 2);
            Require((bool)Call(director, "NeedsShelter", state), "Origin exposure fixture failed");
            // Arrange the arrival side of the boundary; this case checks ownership, not travel.
            f.animal.transform.position = Vector3.forward * 2; f.player.position = f.animal.transform.position + Vector3.left * 2.8f;
            Set(state, "action", "shelter"); Physics.SyncTransforms();
            Require(f.animal.RequestAutonomousTarget(f.animal.transform.position, false, true), "Shelter hold fixture rejected");
            Require(!(bool)Call(director, "Wet", state) && director.IsUrgentlySheltering(f.animal.transform), "Origin still-rainy condition was not established");
            Require(!f.world.BeginGuidance(f.request) && f.animal.AutonomousTarget && f.animal.HasLifeTarget,
                "Guidance stole the sheltered animal while its departure place was still in strong rain");
            director.Rain(Vector3.back * 7, 8, false, 2);
            Require(f.world.BeginGuidance(f.request), "Guidance remained blocked after the rain actually stopped");
            results.Add("PASS an animal sheltered beyond the rain edge cannot be pulled into guidance while its origin is still in strong rain; clear allows explicit interaction again");
        }
        using (var f = new Fixture())
        {
            Require(f.world.BeginGuidance(f.request), "Destroyed actor fixture failed");
            Object.DestroyImmediate(f.animal.gameObject); f.Update();
            Require(f.Active == null, "A destroyed actor retained its guidance state");
            results.Add("PASS destroying the guiding actor safely clears its state and does not access a nonexistent herd member");
        }
        using (var f = new Fixture())
        {
            Require(f.world.BeginGuidance(f.request), "Wall fixture failed");
            for (int i = 0; i < 15; i++) { f.Update(); f.Move(true); }
            float start = f.animal.transform.position.z;
            var wall = new GameObject("Inserted real wall").AddComponent<BoxCollider>(); wall.transform.SetParent(f.root.transform);
            wall.transform.position = new Vector3(0, 1, start + .8f); wall.size = new Vector3(8, 2, .1f); Physics.SyncTransforms();
            for (int i = 0; i < 10 && f.Active != null; i++) { f.Update(); f.Move(false); }
            Require(f.Active == null && !f.animal.HasLifeTarget && f.animal.transform.position.z < start + .75f,
                "Blocked guidance either retained its lease or crossed a real wall");
            results.Add("PASS a wall inserted after route acceptance cancels through actual body validation without crossing");
        }
        foreach (string condition in new[] { "dry_mat", "water_retained", "common_rest" })
            using (var f = new Fixture(condition))
            {
                Require(f.world.BeginGuidance(f.request) && (bool)Get(f.Active, "cueOnly") && Get(f.Active, "search") == null,
                    "A non-arrival request unexpectedly started leading: " + condition);
                Vector3 position = f.animal.transform.position;
                for (int i = 0; i < 10; i++) { f.Update(); f.Move(false); }
                Require(Vector3.Distance(position, f.animal.transform.position) < .01f, "A location cue moved the animal: " + condition);
            }
        results.Add("PASS water, dry-mat and common-rest requests only face their real target and do not start a walking route");
        return results.ToArray();
    }
}
