// Stage only into the independent ValidationProject Editor folder. Never saves a scene.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using WeatherHouse.Hillside;
using WeatherHouse.Life;
using WeatherHousePrototype;
using Object = UnityEngine.Object;

[InitializeOnLoad]
public static class MovingRainReview
{
    const string Key = "WeatherHouse.MovingRainReview";
    const string ScenePath = "Assets/Scenes/WeatherHouse_WatersideV2.unity";
    static readonly BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    static readonly Type AnimalType = typeof(AnimalAutonomy).GetNestedType("Animal", BindingFlags.NonPublic);
    static readonly MethodInfo WetMethod = typeof(AnimalAutonomy).GetMethod("Wet", Flags);
    static readonly MethodInfo IntensityMethod = typeof(AnimalAutonomy).GetMethod("RainIntensity", Flags);
    static readonly List<string> log = new List<string>();
    static readonly List<object> upgradeEligible = new List<object>();
    static AnimalAutonomy director;
    static LifeSession life;
    static LifeLocalWeather weather;
    static WeatherRegion sky;
    static Transform player;
    static object subject, firstSubject;
    static int stage, subjectIndex, failures, lastFrame = -1;
    static float wallStart, stageStart, lastSample, enteredAt = -1, searchAt = -1, movedAt = -1, exitedAt = -1;
    static Vector3 direction, playerPosition, subjectAtEntry, subjectAtShelter;
    static bool sawSearch, sawShelter, sawMovement, mismatchReported, weakChoice, haveWeakChoice, strongCoverageChecked;
    static int episodeAtStart, observedEpisode;
    static Vector3 strongStart;
    static string Folder => SessionState.GetString(Key + ".folder", "");
    static object Field(object o, string name) => o?.GetType().GetField(name, Flags)?.GetValue(o);
    static void Set(object o, string name, object value) => o.GetType().GetField(name, Flags).SetValue(o, value);
    static object Property(object o, string name) => o?.GetType().GetProperty(name, Flags)?.GetValue(o);
    static object Reaction(object a) => Field(a, "rainReaction");
    static bool UrgentLease(object a)
    {
        var agent = (WildlifeAgent)Field(a, "agent");
        return agent && agent.HasLifeTarget && agent.AutonomousTarget &&
            Field(agent, "lifeUrgent") is bool urgent && urgent && Field(agent, "lifeRest") is bool rest && !rest;
    }
    static Transform Root(object a) => (Transform)Field(a, "root");
    static bool Wet(object a) => (bool)WetMethod.Invoke(director, new[] { a });
    static string Action => (string)Field(subject, "action");
    static float Flat(Vector3 a, Vector3 b) { a.y = b.y = 0; return Vector3.Distance(a, b); }
    static float Age => Time.time - stageStart;
    static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    static void Record(string text)
    {
        log.Add("t=" + Time.time.ToString("F3") + " frame=" + Time.frameCount + " " + text);
        File.WriteAllLines(Path.Combine(Folder, "observations.txt"), log);
    }
    static void Check(bool condition, string message) { if (!condition) failures++; Record((condition ? "PASS " : "FAIL ") + message); }
    static void Next(int value) { stage = value; stageStart = Time.time; }

    static MovingRainReview()
    {
        EditorApplication.playModeStateChanged += state => {
            if (!SessionState.GetBool(Key, false)) return;
            if (state == PlayModeStateChange.EnteredPlayMode)
            { wallStart = Time.realtimeSinceStartup; stage = 0; Application.runInBackground = true; }
            if (state == PlayModeStateChange.EnteredEditMode && SessionState.GetBool(Key + ".finishing", false)) VerifyAndExit();
        };
        EditorApplication.update += Tick;
    }
    public static void Batch()
    {
        Require(Application.isBatchMode && Application.dataPath.Contains("ValidationProject"), "Use independent ValidationProject batch only.");
        string folder = Environment.GetEnvironmentVariable("WEATHERHOUSE_MOVING_RAIN_REPORT");
        Require(!string.IsNullOrWhiteSpace(folder), "Set WEATHERHOUSE_MOVING_RAIN_REPORT to a fresh evidence directory.");
        folder = Path.GetFullPath(folder); Directory.CreateDirectory(folder); SessionState.SetString(Key + ".folder", folder);
        SessionState.SetString(Key + ".sceneHash", Hash(ScenePath));
        var saves = new List<string>();
        foreach (string name in new[] { "language-notebook.json", "life-progress.json", "onboarding.json" })
        { string path = Path.Combine(Application.persistentDataPath, name); saves.Add(path + "|" + Hash(path)); }
        SessionState.SetString(Key + ".saves", string.Join("\n", saves));
        Environment.SetEnvironmentVariable("WEATHERHOUSE_LIFE_REVIEW_SAVE", Path.Combine(folder, "isolated-save"));
        Directory.CreateDirectory(Path.Combine(folder, "isolated-save"));
        EditorSceneManager.OpenScene(ScenePath);
        SessionState.SetBool(Key, true); SessionState.SetBool(Key + ".finishing", false); EditorApplication.isPlaying = true;
    }
    static string State()
    {
        object query = Field(subject, "shelterQuery");
        return "subject=" + subjectIndex + "; id=" + Field(subject, "id") + "; action=" + Action +
            "; position=" + Root(subject).position.ToString("F3") + "; player=" + player.position.ToString("F3") +
            "; distance=" + Flat(player.position, Root(subject).position).ToString("F4") +
            "; Contains=" + sky.Contains(Root(subject).position) + "; Wet=" + Wet(subject) +
            "; rainRadius=" + Field(director, "rainRadius") + "; skyRadius=" + sky.radius + "; strength=" + sky.SoundStrength +
            "; skyActive=" + sky.SoundActive + "; query=" + (query != null) + "; probe=" + Field(query, "probe") +
            "; candidates=" + ((IList)Field(query, "candidates"))?.Count + "; routeQueries=" + ((AnimalLocalRoute)Field(query, "route"))?.Queries +
            "; goal=" + Field(subject, "goal") + "; available=" + subject.GetType().GetProperty("Available").GetValue(subject) +
            "; intensity=" + IntensityMethod.Invoke(director, new[] { subject }) + "; episode=" + Property(Reaction(subject), "Episode") +
            "; weakChosen=" + Property(Reaction(subject), "WeakShelterChosen") + "; needsShelter=" + Property(Reaction(subject), "NeedsShelter") +
            "; urgent=" + Property(Reaction(subject), "Urgent") + "; autonomousUrgent=" + UrgentLease(subject) +
            "; speed=" + ((WildlifeAgent)Field(subject, "agent")).ActualSpeed.ToString("F3");
    }
    static object Choose(bool first)
    {
        object best = null; float nearest = float.PositiveInfinity;
        foreach (object a in (IList)Field(director, "animals"))
        {
            var agent = (WildlifeAgent)Field(a, "agent");
            if (!agent || !agent.isActiveAndEnabled || a == firstSubject || !(bool)a.GetType().GetProperty("Available").GetValue(a)) continue;
            string id = (string)Field(a, "id");
            if (first && id.EndsWith("/sheep/6101", StringComparison.Ordinal)) return a;
            float distance = Flat(player.position, Root(a).position);
            if (!first && (distance < 12 || Wet(a))) continue;
            // Select a reproducible refusal case without changing the live animal's RNG or decision.
            if (!first && NextWeakChoosesShelter(a,agent)) continue;
            if (distance < nearest) { nearest = distance; best = a; }
        }
        return best;
    }
    static bool NextWeakChoosesShelter(object actor,WildlifeAgent agent)
    {
        var prediction=new AnimalRainReaction(agent.Population.worldSeed,agent.spawnId,agent.speciesId);
        int previous=(int)Property(Reaction(actor),"Episode");
        for(int episode=0;episode<=previous;episode++){prediction.Observe(0);prediction.Observe(1);}
        return prediction.WeakShelterChosen;
    }
    static void BeginSubject(bool first)
    {
        subject = Choose(first); Require(subject != null, "No eligible " + (first ? "first" : "second") + " animal outside the current rain area.");
        subjectIndex++;
        if (first) firstSubject = subject;
        Vector3 away = player.position - Root(subject).position; away.y = 0;
        direction = away.sqrMagnitude > .01f ? away.normalized : Vector3.back;
        enteredAt = searchAt = movedAt = exitedAt = -1; sawSearch = sawShelter = sawMovement = mismatchReported = false;
        haveWeakChoice = strongCoverageChecked = false;
        episodeAtStart = (int)Property(Reaction(subject), "Episode");
        if (first)
        {
            playerPosition = Root(subject).position + direction * 12; player.position = playerPosition;
            Record("ARRANGE initial player placed 12m outside first animal; all subsequent movement is continuous interpolation; no animal teleport.");
        }
        Record("SUBJECT " + Field(subject, "id")); Next(first ? 1 : 5);
    }
    static void MovePlayer(Vector3 destination)
    {
        // This fixture exercises moving rain coverage, not player input/pathfinding or controller collision.
        destination.y = playerPosition.y;
        playerPosition = Vector3.MoveTowards(playerPosition, destination, 1.5f * Mathf.Min(Time.deltaTime, .1f));
        if (Terrain.activeTerrain) playerPosition.y = Terrain.activeTerrain.SampleHeight(playerPosition) + Terrain.activeTerrain.transform.position.y;
        player.position = playerPosition;
    }
    static void ApplyAndObserve()
    {
        // Refresh the normal entry point after the test player moves; dt=0 is rejected by LifeWorld.
        weather.Apply(life.World, .000001f);
        bool inside = sky.Contains(Root(subject).position);
        bool wet = Wet(subject);
        if (inside != wet && !mismatchReported)
        { mismatchReported = true; Check(false, "Current rain Contains/Wet mismatch; " + State()); }
        if (inside && wet && enteredAt < 0)
        { enteredAt = Time.time; subjectAtEntry = Root(subject).position; Record("EVENT entered moving weak-rain circle; " + State()); }
        object reaction = Reaction(subject);
        if (inside && wet && (int)Property(reaction, "Intensity") == 1)
        {
            if (!haveWeakChoice)
            {
                haveWeakChoice = true; weakChoice = (bool)Property(reaction, "WeakShelterChosen"); observedEpisode = (int)Property(reaction, "Episode");
                Check(observedEpisode == episodeAtStart + 1, "Subject " + subjectIndex + " gets one new weak-rain exposure episode");
                Record("EVENT weak episode choice=" + weakChoice + "; " + State());
            }
            else if ((int)Property(reaction, "Episode") != observedEpisode || (bool)Property(reaction, "WeakShelterChosen") != weakChoice)
                throw new InvalidOperationException("Continuous weak-rain exposure rerolled its decision");
        }
        if (enteredAt >= 0 && (Field(subject, "shelterQuery") != null || Action == "shelter"))
        {
            if (!sawSearch) { searchAt = Time.time; Record("EVENT shelter search observed after entry, latency=" + (searchAt - enteredAt).ToString("F3") + "; " + State()); }
            sawSearch = true;
        }
        if (Action == "shelter" && !sawShelter)
        { sawShelter = true; subjectAtShelter = Root(subject).position; Record("EVENT accepted shelter route; " + State()); }
        if (sawShelter && !sawMovement && Flat(subjectAtShelter, Root(subject).position) > .25f)
        { sawMovement = true; movedAt = Time.time; Record("EVENT actual movement after shelter route, displacement=" + Flat(subjectAtShelter, Root(subject).position).ToString("F3") + "; " + State()); }
        if (enteredAt >= 0 && !wet && exitedAt < 0)
        { exitedAt = Time.time; Record("EVENT outside moving rain; this may occur before reaching shelter; " + State()); }
        if (Time.time - lastSample >= .5f) { lastSample = Time.time; Record("SAMPLE stage=" + stage + "; " + State()); }
    }
    static void Tick()
    {
        if (!SessionState.GetBool(Key, false) || SessionState.GetBool(Key + ".finishing", false) || !EditorApplication.isPlaying || Time.frameCount == lastFrame) return;
        lastFrame = Time.frameCount;
        try
        {
            if (Time.realtimeSinceStartup - wallStart > 900) { Check(false, "900s wall-clock deadline"); Finish(); return; }
            if (!director) director = Object.FindObjectOfType<AnimalAutonomy>();
            if (director)
            {
                Set(director, "nextRequest", float.PositiveInfinity);
                var client = director.GetComponent<AnimalPlannerClient>();
                if (client) { Set(client, "config", null); Set(client, "nextConfigurationRead", float.PositiveInfinity); }
            }
            if (stage == 0)
            {
                if (Time.time < 5 || !director || director.AnimalCount == 0) return;
                life = Object.FindObjectOfType<LifeSession>(); player = life.Gameplay.PlayerTransform;
                weather = (LifeLocalWeather)Field(life, "weather"); sky = (WeatherRegion)Field(weather, "sky");
                Require(Mathf.Approximately((float)Field(weather, "radius"), 8) && Mathf.Approximately(sky.radius, 8), "Normal 8m rain radius required; do not reuse 80m fixture.");
                Require((int)Field(weather, "skyStrength") == 1, "Expected default weak rain; no strong modifier is permitted in this scenario.");
                Record("SETUP saved formal scene, normal8m radius; initially Cast(rain) only/default strength1; each weak episode draws once at50percent, later upgrade strong; network suppressed in memory; isolated save; no animal relocation.");
                BeginSubject(true); weather.Cast("rain", out _); Record("CAST rain only; no strong/weak modifier");
            }
            if (stage == 2) MovePlayer(Root(subject).position + direction * 5.5f);
            else if (stage == 4) MovePlayer(Root(subject).position + direction * 12);
            else if (stage == 5) MovePlayer(Root(subject).position + direction * 12);
            else player.position = playerPosition;
            ApplyAndObserve();
            if (stage == 1 && Age >= 2)
            {
                Check(!Wet(subject) && !sky.Contains(Root(subject).position), "Subject " + subjectIndex + " outside before player walks in; " + State());
                Capture("animal" + subjectIndex + "-outside"); Next(2);
            }
            else if (stage == 2)
            {
                if (Flat(player.position, Root(subject).position) <= 6)
                { Check(enteredAt >= 0, "Subject " + subjectIndex + " became Wet while player walked into range"); Capture("animal" + subjectIndex + "-entered"); Next(3); }
                else if (Age > 40) { Check(false, "Failed to walk within6m without teleport"); Next(4); }
            }
            else if (stage == 3 && (haveWeakChoice && !weakChoice && Age > 4 || sawMovement || Age > 60 || exitedAt >= 0 && Age > 3))
            {
                Check(haveWeakChoice, "Subject " + subjectIndex + " weak-rain episode choice was observed");
                if(subjectIndex==2)Check(haveWeakChoice && !weakChoice,"Second subject explicitly covers weak refusal followed by a fresh strong-rain shelter search");
                if (haveWeakChoice && weakChoice)
                {
                    Check(sawSearch, "Subject " + subjectIndex + " chosen weak-rain episode triggered shelter search");
                    Check(sawShelter && sawMovement, "Subject " + subjectIndex + " chosen weak-rain episode physically moved after shelter route");
                }
                else if (haveWeakChoice)
                    Check(!sawSearch && !sawShelter, "Subject " + subjectIndex + " unchosen weak-rain episode stays in rain without shelter/search");
                Check(!mismatchReported, "Subject " + subjectIndex + " current moving rain Contains matches Wet");
                Capture("animal" + subjectIndex + "-weak-response");
                if (Wet(subject))
                {
                    upgradeEligible.Clear();
                    foreach (object a in (IList)Field(director, "animals"))
                        if (Root(a) && sky.Contains(Root(a).position) && (bool)a.GetType().GetProperty("Available").GetValue(a)) upgradeEligible.Add(a);
                    strongStart = Root(subject).position; weather.Cast("strong", out _); weather.Apply(life.World, .000001f);
                    Record("CAST strong upgrades the same existing exposure; no reentry or reroll"); Next(6);
                }
                else
                {
                    Check(false, "Subject " + subjectIndex + " left the circle before same-episode strong upgrade could be tested"); Next(4);
                }
            }
            else if (stage == 4)
            {
                if (Flat(player.position, Root(subject).position) >= 10 && Age > 3 && !Wet(subject) && Action != "shelter" && Field(subject, "shelterQuery") == null)
                {
                    Check(true, "Subject " + subjectIndex + " player carried rain away; Wet false and shelter/search released; " + State());
                    Capture("animal" + subjectIndex + "-left");
                    Check((int)Property(Reaction(subject), "Intensity") == 0 && !(bool)Property(Reaction(subject), "NeedsShelter"), "Subject " + subjectIndex + " leaving ended reaction episode");
                    if (subjectIndex == 1)
                    {
                        weather.Cast("weak", out _); weather.Apply(life.World, .000001f);
                        Record("CAST weak for second subject; rain stays on while player walks between animals"); BeginSubject(false);
                    }
                    else { weather.Cast("clear", out _); weather.Apply(life.World, .000001f); Check(!Wet(subject), "Clear leaves subject dry"); Finish(); }
                }
                else if (Age > 40) { Check(false, "Subject " + subjectIndex + " did not release shelter after rain circle moved away; " + State()); Finish(); }
            }
            else if (stage == 5)
            {
                if (Flat(player.position, Root(subject).position + direction * 12) < .1f)
                { Record("ARRIVED outside second animal continuously; no second teleport or weather recast"); Next(1); }
                else if (Age > 180) { Check(false, "Continuous travel to second animal exceeded180s"); Finish(); }
            }
            else if (stage == 6)
            {
                if (!strongCoverageChecked && Age > 1)
                {
                    int eligible = 0, reacting = 0;
                    foreach (object a in upgradeEligible)
                    {
                        if (!Root(a) || !sky.Contains(Root(a).position) || !(bool)a.GetType().GetProperty("Available").GetValue(a)) continue;
                        eligible++;
                        object current = Reaction(a);
                        if ((int)Property(current, "Intensity") == 2 && (bool)Property(current, "NeedsShelter") && (bool)Property(current, "Urgent")) reacting++;
                    }
                    Check(eligible > 0 && reacting == eligible, "Strong upgrade covers every animal eligible at upgrade and still inside/currently available: " + reacting + "/" + eligible);
                    Check((int)Property(Reaction(subject), "Episode") == observedEpisode && (bool)Property(Reaction(subject), "WeakShelterChosen") == weakChoice,
                        "Subject " + subjectIndex + " weak-to-strong keeps same episode and weak draw");
                    strongCoverageChecked = true;
                }
                bool urgentMove = Action == "shelter" && UrgentLease(subject) &&
                    Flat(strongStart, Root(subject).position) > .25f;
                if (strongCoverageChecked && urgentMove || Age > 60 || !Wet(subject) && Age > 3)
                {
                    Check(urgentMove, "Subject " + subjectIndex + " strong rain drives urgent real movement; higher speed verified separately on identical fixture path");
                    Check(urgentMove && Age<=10, "Subject " + subjectIndex + " strong-rain movement begins within10s of upgrade; actual=" + Age.ToString("F3") + "s");
                    Capture("animal" + subjectIndex + "-strong-response"); Record("MOVE player continuously away with strong rain still active"); Next(4);
                }
            }
        }
        catch (Exception error) { Check(false, error.ToString()); Finish(); }
    }
    static void Capture(string name)
    {
        var go = new GameObject("Moving rain evidence camera"); var camera = go.AddComponent<Camera>();
        Camera main = Camera.main; Require(main, "Main camera missing"); camera.CopyFrom(main); camera.enabled = false; camera.orthographic = false; camera.fieldOfView = 55;
        Vector3 midpoint = (Root(subject).position + player.position) * .5f;
        camera.transform.position = midpoint + new Vector3(5, 9, -10); camera.transform.LookAt(midpoint + Vector3.up * .5f);
        var target = new RenderTexture(1280, 720, 24); var image = new Texture2D(1280, 720, TextureFormat.RGB24, false); var old = RenderTexture.active;
        try
        {
            camera.targetTexture = target; camera.Render(); RenderTexture.active = target;
            image.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); image.Apply(); File.WriteAllBytes(Path.Combine(Folder, name + ".png"), image.EncodeToPNG());
            Record("CAPTURE " + name + "; evidence camera (not player view); " + State());
        }
        finally { RenderTexture.active = old; camera.targetTexture = null; target.Release(); Object.Destroy(target); Object.Destroy(image); Object.Destroy(go); }
    }
    static string Hash(string path)
    { if (!File.Exists(path)) return "MISSING"; using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-", ""); }
    static void Finish()
    {
        Record("RESULT moving rain weak-choice/strong-upgrade failures=" + failures + "; subjects=" + subjectIndex);
        SessionState.SetInt(Key + ".exit", failures > 0 ? 1 : 0); SessionState.SetBool(Key + ".finishing", true); EditorApplication.isPlaying = false;
    }
    static void VerifyAndExit()
    {
        int exit = SessionState.GetInt(Key + ".exit", 1); var results = new List<string>();
        foreach (string line in SessionState.GetString(Key + ".saves", "").Split('\n'))
        {
            int split = line.LastIndexOf('|'); if (split < 0) continue;
            bool same = Hash(line.Substring(0, split)) == line.Substring(split + 1); if (!same) exit = 1;
            results.Add((same ? "PASS " : "FAIL ") + "Real save unchanged: " + Path.GetFileName(line.Substring(0, split)));
        }
        bool scene = Hash(ScenePath) == SessionState.GetString(Key + ".sceneHash", ""); if (!scene) exit = 1;
        results.Add((scene ? "PASS " : "FAIL ") + "Saved scene unchanged; Play=False");
        File.WriteAllLines(Path.Combine(Folder, "save-protection.txt"), results); SessionState.SetBool(Key, false); EditorApplication.Exit(exit);
    }

    public static IEnumerable<string> Contracts()
    {
        var root = new GameObject("Moving rain contract fixture"); root.SetActive(false);
        GameObject staticRoot = null;
        try
        {
            var game = root.AddComponent<GameplaySession>(); game.walker = root.AddComponent<PrototypeWalker>();
            var owner = root.AddComponent<LifeSession>(); typeof(LifeSession).GetProperty("Gameplay").SetValue(owner, game);
            var world = root.AddComponent<LifeWorld>(); Set(world, "owner", owner);
            var autonomy = root.AddComponent<AnimalAutonomy>(); Set(world, "autonomy", autonomy);
            var body = new GameObject("Probe only, not a scene animal"); body.transform.SetParent(root.transform);
            object state = Activator.CreateInstance(AnimalType, true); Set(state, "root", body.transform);
            bool IsWet() => (bool)WetMethod.Invoke(autonomy, new[] { state });
            void Apply(LocalWeather kind, int strength, bool active) => world.ApplyWeather(root.transform.position, 8, kind, strength, active, .001f);
            root.transform.position = new Vector3(1000, 100, 1000); body.transform.position = root.transform.position + Vector3.right * 4;
            Apply(LocalWeather.Rain, 1, true); Require(IsWet(), "Weak rain inside normal8m did not trigger Wet");
            Apply(LocalWeather.Rain, 2, true); Require(IsWet(), "Strong rain inside normal8m did not trigger Wet");
            yield return "PASS actual LifeWorld.ApplyWeather weak and strong rain both mark in-range animals Wet";
            Type reactionType = typeof(AnimalAutonomy).Assembly.GetType("WeatherHouse.Hillside.AnimalRainReaction", true);
            Set(state, "rainReaction", Activator.CreateInstance(reactionType, new object[] { 42, 1, "rabbit" }));
            MethodInfo needsShelter = typeof(AnimalAutonomy).GetMethod("NeedsShelter", Flags);
            Apply(LocalWeather.Rain, 1, true); needsShelter.Invoke(autonomy, new[] { state });
            Set(state, "nextShelter", Time.time + 5);
            Apply(LocalWeather.Rain, 2, true); needsShelter.Invoke(autonomy, new[] { state });
            Require((int)Property(Reaction(state), "Intensity") == 2 && (float)Field(state, "nextShelter") <= Time.time,
                "Weak-to-strong upgrade did not clear shelter retry cooldown");
            yield return "PASS real NeedsShelter clears shelter retry cooldown on weak-to-strong upgrade";
            float retry = Time.time + 5; Set(state, "nextShelter", retry);
            needsShelter.Invoke(autonomy, new[] { state });
            Require((int)Property(Reaction(state), "Intensity") == 2 && (float)Field(state, "nextShelter") == retry,
                "Repeated strong rain observation incorrectly cleared shelter retry cooldown again");
            yield return "PASS continuous strong rain preserves a subsequent shelter retry cooldown";
            body.transform.position = root.transform.position + new Vector3(8, 25, 0); Apply(LocalWeather.Rain, 1, true);
            Require(IsWet(), "Exact8m boundary or height independence differs from visual rain circle");
            body.transform.position = root.transform.position + Vector3.right * 8.01f; Require(!IsWet(), "Outside8m+epsilon remained Wet");
            Vector3 old = body.transform.position; root.transform.position += Vector3.forward * 20; body.transform.position = old; Apply(LocalWeather.Rain, 1, true);
            Require(!IsWet(), "Old rain center stayed active after player moved"); body.transform.position = root.transform.position + Vector3.right * 4;
            Require(IsWet(), "New moving center did not cover new animal");
            yield return "PASS normal8m xz boundary, vertical independence and moving center agree";
            Apply(LocalWeather.Wind, 2, true); Require(IsWet(), "Wind erased a still-active weak rain layer");
            Apply(LocalWeather.Rain, 1, false); Require(!IsWet(), "Clear left rain active");
            Apply(LocalWeather.Wind, 2, true); Require(!IsWet(), "Wind alone falsely triggered rain shelter");
            Apply(LocalWeather.Sun, 1, true); Require(!IsWet(), "Sun falsely triggered rain shelter");
            yield return "PASS clear/sun/wind-only remain dry and simultaneous wind preserves active rain";
            staticRoot = new GameObject("Static rain contract region"); staticRoot.transform.position = root.transform.position;
            var region = staticRoot.AddComponent<WeatherRegion>(); region.radius = 8; region.localPlayerWeather = false;
            Set(autonomy, "weatherRegions", new[] { region });
            region.SetLocalWeather(LocalWeather.Rain, 1, true); Require(IsWet(), "Static weak rain ignored");
            Require((int)IntensityMethod.Invoke(autonomy, new[] { state }) == 1, "Static weak rain intensity must be1");
            region.SetLocalWeather(LocalWeather.Rain, 2, true); Require(IsWet(), "Static strong rain ignored");
            Require((int)IntensityMethod.Invoke(autonomy, new[] { state }) == 2, "Static strong rain intensity must be2");
            body.transform.position = region.transform.position + Vector3.right * 8;
            Require(region.Contains(body.transform.position) == IsWet(), "Static exact boundary differs");
            body.transform.position = region.transform.position + Vector3.right * 8.01f;
            Require(!region.Contains(body.transform.position) && !IsWet(), "Static outside boundary differs");
            body.transform.position = region.transform.position; region.SetLocalWeather(LocalWeather.Rain, 1, false); Require(!IsWet(), "Inactive static rain triggered Wet");
            region.SetLocalWeather(LocalWeather.Wind, 2, true); Require(!IsWet(), "Static wind triggered Wet");
            yield return "PASS static weak/strong rain use the same coverage; outside, inactive and wind regions do not trigger";
        }
        finally { if (staticRoot) Object.DestroyImmediate(staticRoot); Object.DestroyImmediate(root); }
        foreach (string line in ReactionContracts()) yield return line;
        yield return UrgentSpeedContract();
    }
    static IEnumerable<string> ReactionContracts()
    {
        Type type = typeof(AnimalAutonomy).Assembly.GetType("WeatherHouse.Hillside.AnimalRainReaction", true);
        MethodInfo observe = type.GetMethod("Observe"); int total = 0, chosen = 0;
        for (int seed = 0; seed < 20; seed++) for (int individual = 0; individual < 100; individual++)
        {
            object reaction = Activator.CreateInstance(type, new object[] { seed, individual, "rabbit" });
            void Observe(int value) => observe.Invoke(reaction, new object[] { value });
            Observe(1); total++; bool pick = (bool)Property(reaction, "WeakShelterChosen"); if (pick) chosen++;
            for (int i = 0; i < 40; i++) Observe(1);
            Require((int)Property(reaction, "Episode") == 1 && (bool)Property(reaction, "WeakShelterChosen") == pick, "Weak rain rerolls every observation");
            Require((bool)Property(reaction, "NeedsShelter") == pick && !(bool)Property(reaction, "Urgent"), "Weak choice/urgency contract differs");
            Observe(2); Require((bool)Property(reaction, "NeedsShelter") && (bool)Property(reaction, "Urgent"), "Strong rain did not override weak refusal");
            Require((int)Property(reaction, "Episode") == 1 && (bool)Property(reaction, "WeakShelterChosen") == pick, "Upgrade rerolled episode");
            Observe(1); Require((bool)Property(reaction, "NeedsShelter") && !(bool)Property(reaction, "Urgent") &&
                (bool)Property(reaction, "WeakShelterChosen") == pick, "Weakening rain must retain chosen shelter until dry without rerolling the original weak choice");
            Observe(0); Require(!(bool)Property(reaction, "NeedsShelter") && !(bool)Property(reaction, "Urgent"), "Leaving/clear retained rain reaction");
            Observe(1); Require((int)Property(reaction, "Episode") == 2, "Reentry did not start new episode");
        }
        Require(chosen > total * .45f && chosen < total * .55f, "Seeded weak-rain population distribution outside45-55percent: " + chosen + "/" + total);
        yield return "PASS weak-rain seeded population=" + chosen + "/" + total + "; 40 repeated observations keep one draw per episode";
        yield return "PASS all2000 strong upgrades override refusal immediately; downgrade retains shelter and original weak draw until dry; leave/clear/reentry starts next episode";
    }
    static string UrgentSpeedContract()
    {
        MethodInfo request = typeof(WildlifeAgent).GetMethod("RequestAutonomousTarget", new[] { typeof(Vector3), typeof(bool), typeof(bool) });
        Require(request != null, "Urgent RequestAutonomousTarget overload is unavailable");
        float Travel(bool urgent)
        {
            var root = new GameObject("Identical urgent speed path fixture");
            try
            {
                var population = root.AddComponent<WildlifePopulation>(); population.worldSeed = 42; population.obstacleMask = 0;
                population.worldBounds = new Bounds(new Vector3(0, 10, 0), new Vector3(80, 20, 80));
                var animal = new GameObject("Same animal").AddComponent<WildlifeAgent>(); animal.transform.SetParent(root.transform);
                animal.spawnId = 1; animal.timidity = 0; animal.sociability = 0; animal.scaleRange = Vector2.one;
                population.agents = new[] { animal }; population.Initialize();
                Vector3 start = animal.transform.position;
                Require((bool)request.Invoke(animal, new object[] { start + Vector3.forward * 6, false, urgent }), "Fixture target rejected");
                for (int step = 0; step < 20; step++) population.Simulate(.05f);
                return Flat(start, animal.transform.position);
            }
            finally { Object.DestroyImmediate(root); }
        }
        float weak = Travel(false), strong = Travel(true);
        Require(weak > .1f && strong > weak * 1.05f, "Strong urgent movement is not faster on identical path: weak=" + weak + " strong=" + strong);
        return "PASS same animal/seed/path/1s simulation: ordinary=" + weak.ToString("F3") + "m urgent=" + strong.ToString("F3") + "m; urgent is faster";
    }
    public static void BatchContracts()
    {
        Require(Application.isBatchMode && Application.dataPath.Contains("ValidationProject"), "Independent ValidationProject batch-only contracts");
        string folder = Environment.GetEnvironmentVariable("WEATHERHOUSE_MOVING_RAIN_REPORT"); Require(!string.IsNullOrEmpty(folder), "Set report directory"); Directory.CreateDirectory(folder);
        var results = new List<string>(); int exit = 0;
        try { results.AddRange(Contracts()); } catch (Exception error) { exit = 1; results.Add("FAIL " + error); }
        File.WriteAllLines(Path.Combine(folder, "moving-rain-contracts.txt"), results); EditorApplication.Exit(exit);
    }
}
