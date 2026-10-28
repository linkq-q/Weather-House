using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace WeatherHouse.DrawerPrototype.Editor
{
    [InitializeOnLoad]
    public static class DrawerPrototypeEditor
    {
        public const string Root = "Assets/Experiments/DrawerPrototype";
        public const string ScenePath = Root + "/DrawerPrototype.unity";
        public const string Review = "DrawerPrototypeReview";
        const string Key = "WeatherHouse.DrawerPrototype";
        const string CommandPath = Review + "/command.txt";
        static double nextPoll;

        static DrawerPrototypeEditor()
        {
            EditorApplication.update += Poll;
            EditorApplication.playModeStateChanged += PlayChanged;
        }

        // Fixed local review commands; the relay never executes arbitrary code or edits a formal scene.
        static void Poll()
        {
            if (EditorApplication.timeSinceStartup < nextPoll || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            nextPoll = EditorApplication.timeSinceStartup + .5;
            if (!File.Exists(CommandPath)) return;
            string command = File.ReadAllText(CommandPath).Trim(); File.Delete(CommandPath);
            try
            {
                switch (command)
                {
                    case "status": break;
                    case "build": Build(); break;
                    case "play": Begin(); break;
                    case "checks": BeginChecks(); break;
                    case "resume-checks": StartPlay(false,true); break;
                    case "model-review": StartPlay(false,false,true); break;
                    case "export-models": ExportModels(UnityEngine.Object.FindObjectOfType<DrawerPrototypeController>()); break;
                    case "capture": CaptureCurrent(); break;
                    case "stop": Stop(); break;
                    default: throw new InvalidOperationException("Unknown drawer review command: " + command);
                }
                WriteStatus("OK " + command);
            }
            catch (Exception e) { WriteStatus("FAIL " + command + "\n" + e); Debug.LogException(e); }
        }

        [MenuItem("Weather House/试玩/抽屉翻找原型（P2 立体版）")]
        public static void Begin() => StartPlay(false);

        public static void BatchVisualReview()
        {
            Require(Application.isBatchMode,"This entry is for a separate batch review process.");
            SessionState.SetBool(Key+".batch",true); SessionState.SetBool(Key+".failed",false);
            StartPlay(false,false,true);
        }

        public static void BatchFlowReview()
        {
            Require(Application.isBatchMode,"This entry is for a separate batch review process.");
            SessionState.SetBool(Key+".batch",true); SessionState.SetBool(Key+".failed",false);
            StartPlay(true);
        }

        [MenuItem("Weather House/抽屉原型/构建独立场景")]
        public static void Build()
        {
            Require(!EditorApplication.isPlayingOrWillChangePlaymode,"Build the drawer only in Edit mode.");
            Require(!SceneManager.GetSceneByPath(ScenePath).isLoaded,"Close the drawer scene before rebuilding it.");
            ImportArt(); CheckProgress();
            Scene previous = SceneManager.GetActiveScene(); bool dirty = previous.isDirty;
            Scene prototype = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
            try
            {
                var root = new GameObject("P2 · three-dimensional drawer prototype");
                SceneManager.MoveGameObjectToScene(root,prototype);
                var controller = root.AddComponent<DrawerPrototypeController>();
                const string paper = "Assets/Game/Life/Art/Resources/LifeAssets/JournalTextures/";
                controller.timberAlbedo = AssetDatabase.LoadAssetAtPath<Texture2D>(Root+"/Resources/DrawerPrototype/Oak.png");
                controller.paperGrain = AssetDatabase.LoadAssetAtPath<Texture2D>(paper+"PaperAged.png");
                controller.paperNormal = AssetDatabase.LoadAssetAtPath<Texture2D>(paper+"PaperAged_Normal.png");
                Require(EditorSceneManager.SaveScene(prototype,ScenePath),"Could not save the independent drawer scene.");
            }
            finally
            {
                if (previous.IsValid()) SceneManager.SetActiveScene(previous);
                EditorSceneManager.CloseScene(prototype,true);
            }
            Require(!previous.IsValid() || previous.isDirty == dirty,"Original scene dirty state changed.");
            AssetDatabase.Refresh(); WriteStatus("BUILT independent prototype");
        }

        static void ImportArt()
        {
            foreach (string file in new[] { "Props", "Velvet", "Oak" })
            {
                string path = Root + "/Resources/DrawerPrototype/" + file + ".png";
                AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);
                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                Require(importer != null,"Missing generated P2 image: " + path);
                importer.textureType = TextureImporterType.Default; importer.alphaSource = TextureImporterAlphaSource.FromInput;
                importer.alphaIsTransparency = file == "Props"; importer.sRGBTexture = true; importer.mipmapEnabled = file != "Props";
                importer.isReadable = file == "Props"; importer.maxTextureSize = 2048;
                importer.filterMode = FilterMode.Bilinear; importer.wrapMode = file == "Props" ? TextureWrapMode.Clamp : TextureWrapMode.Repeat;
                importer.textureCompression = TextureImporterCompression.Uncompressed; importer.SaveAndReimport();
            }
            var atlas = AssetDatabase.LoadAssetAtPath<Texture2D>(Root + "/Resources/DrawerPrototype/Props.png");
            Require(atlas.GetPixel(0,0).a < .05f,"The prop atlas must have real transparent alpha.");
            Require(Resources.Load<Font>("Fonts/ZCOOLXiaoWei-Regular"),"Existing game font is missing.");
        }

        static void StartPlay(bool checks, bool resume = false, bool models = false)
        {
            Require(!EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isCompiling,"Finish the current Play before starting the drawer.");
            if (!File.Exists(ScenePath)) Build(); else ImportArt();
            Directory.CreateDirectory(Review);
            SessionState.SetString(Key+".start",AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene));
            SessionState.SetString(Key+".save",Environment.GetEnvironmentVariable(DrawerPrototypeController.SaveOverride) ?? "");
            SessionState.SetString(Key+".life",Environment.GetEnvironmentVariable("WEATHERHOUSE_LIFE_REVIEW_SAVE") ?? "");
            SessionState.SetString(Key+".scene",SceneManager.GetActiveScene().path);
            SessionState.SetBool(Key+".dirty",SceneManager.GetActiveScene().isDirty);
            SessionState.SetBool(Key+".background",Application.runInBackground);
            SessionState.SetString(Key+".formalHash",Hash("Assets/Scenes/WeatherHouse_WatersideV2.unity"));
            SessionState.SetBool(Key+".owned",true); SessionState.SetBool(Key+".checks",checks || resume); SessionState.SetBool(Key+".resume",resume);
            SessionState.SetBool(Key+".models",models);
            if (checks || models)
            {
                string directory = Path.GetFullPath(Review + "/checks-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss"));
                Directory.CreateDirectory(directory); SessionState.SetString(Key+".checkDirectory",directory);
                Environment.SetEnvironmentVariable(DrawerPrototypeController.SaveOverride,Path.Combine(directory,"progress.json"));
            }
            else if (resume)
            {
                string file = Path.Combine(SessionState.GetString(Key+".checkDirectory",""),"progress.json");
                Require(File.Exists(file),"Run the fresh flow check before the reload check.");
                Environment.SetEnvironmentVariable(DrawerPrototypeController.SaveOverride,file);
            }
            Environment.SetEnvironmentVariable("WEATHERHOUSE_LIFE_REVIEW_SAVE",Path.GetFullPath(Review+"/isolated-life"));
            EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath);
            Application.runInBackground = true; EditorApplication.isPaused = false; EditorApplication.isPlaying = true;
        }

        static void PlayChanged(PlayModeStateChange change)
        {
            if (!SessionState.GetBool(Key+".owned",false)) return;
            if (change == PlayModeStateChange.EnteredPlayMode)
            {
                var gameView = typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView");
                if (gameView != null) EditorWindow.GetWindow(gameView).Focus();
                if (SessionState.GetBool(Key+".checks",false))
                {
                    var controller = UnityEngine.Object.FindObjectOfType<DrawerPrototypeController>();
                    Require(controller,"The independent drawer controller is missing.");
                    controller.StartCoroutine(Guard(controller,SessionState.GetBool(Key+".resume",false) ? RunResume(controller) : RunFlow(controller)));
                }
                else if (SessionState.GetBool(Key+".models",false))
                {
                    var controller = UnityEngine.Object.FindObjectOfType<DrawerPrototypeController>();
                    controller.StartCoroutine(Guard(controller,ReviewModels(controller)));
                }
            }
            if (change != PlayModeStateChange.EnteredEditMode) return;
            string start = SessionState.GetString(Key+".start","");
            EditorSceneManager.playModeStartScene = string.IsNullOrEmpty(start) ? null : AssetDatabase.LoadAssetAtPath<SceneAsset>(start);
            Environment.SetEnvironmentVariable(DrawerPrototypeController.SaveOverride,EmptyNull(SessionState.GetString(Key+".save","")));
            Environment.SetEnvironmentVariable("WEATHERHOUSE_LIFE_REVIEW_SAVE",EmptyNull(SessionState.GetString(Key+".life","")));
            Application.runInBackground = SessionState.GetBool(Key+".background",false);
            SessionState.SetBool(Key+".owned",false);
            bool preserved = Hash("Assets/Scenes/WeatherHouse_WatersideV2.unity") == SessionState.GetString(Key+".formalHash","") &&
                SceneManager.GetActiveScene().path == SessionState.GetString(Key+".scene","") &&
                SceneManager.GetActiveScene().isDirty == SessionState.GetBool(Key+".dirty",false);
            Directory.CreateDirectory(Review);
            File.WriteAllText(Review+"/handoff.txt","Play=False\nOriginalSceneAndDirtyPreserved="+preserved+"\nActiveScene="+SceneManager.GetActiveScene().path);
            WriteStatus(preserved ? "STOPPED; original scene preserved" : "FAIL original scene preservation");
            if (Application.isBatchMode && SessionState.GetBool(Key+".batch",false))
                EditorApplication.delayCall += () => EditorApplication.Exit(preserved && !SessionState.GetBool(Key+".failed",false) ? 0 : 1);
        }

        [MenuItem("Weather House/抽屉原型/运行交互检查")]
        public static void BeginChecks() => StartPlay(true);

        [MenuItem("Weather House/抽屉原型/结束试玩")]
        public static void Stop()
        {
            Require(SessionState.GetBool(Key+".owned",false) && EditorApplication.isPlaying,"No drawer-owned Play is running.");
            EditorApplication.isPlaying = false;
        }

        [MenuItem("Weather House/抽屉原型/拍摄当前画面")]
        public static void CaptureCurrent()
        {
            Require(EditorApplication.isPlaying && SceneManager.GetActiveScene().path == ScenePath,"Capture requires the drawer Play scene.");
            var controller = UnityEngine.Object.FindObjectOfType<DrawerPrototypeController>();
            controller.StartCoroutine(Capture(Path.GetFullPath(Review+"/interactive-"+DateTime.UtcNow.ToString("HHmmss")+".png")));
        }

        public static void CheckProgress()
        {
            var state = new DrawerProgress(); Require(!state.Complete && state.Count == 0,"Fresh progress must start empty.");
            for (int i = 0; i < 5; i++) state.collected[i] = true;
            Require(!state.Complete,"Collecting all props must not skip their introductions.");
            for (int i = 0; i < 5; i++) state.read[i] = true;
            Require(state.Complete && state.Count == 5,"Five collected and read props complete the drawer.");
            var restored = DrawerProgress.Restore(JsonUtility.ToJson(state));
            Require(restored.Complete && restored.Count == 5,"Collection and read progress must round-trip.");
            restored.collected[2] = false; restored.Normalize();
            Require(!restored.read[2] && !restored.Complete,"A missing item cannot remain marked as read.");
            var incomplete = DrawerProgress.Restore("{\"collected\":[true],\"read\":[false]}");
            Require(incomplete.collected.Length == 5 && incomplete.Count == 1 && !incomplete.read[0],"Partial progress retains unread introductions.");
            Directory.CreateDirectory(Review); File.WriteAllText(Review+"/state-checks.txt","PASS: fresh state, completion gate, collection/read round-trip, missing-item normalization, pending introduction.");
        }

        static IEnumerator Guard(DrawerPrototypeController controller, IEnumerator operation)
        {
            Exception failure = null;
            while (true)
            {
                bool next; object value = null;
                try { next = operation.MoveNext(); if (next) value = operation.Current; }
                catch (Exception e) { failure = e; break; }
                if (!next) break;
                // Nested coroutines must be driven here so their assertion failures reach the report.
                if (value is IEnumerator child)
                {
                    var stack = new Stack<IEnumerator>(); stack.Push(child);
                    while (stack.Count > 0)
                    {
                        bool childNext; object childValue = null;
                        try { childNext = stack.Peek().MoveNext(); if (childNext) childValue = stack.Peek().Current; }
                        catch (Exception e) { failure = e; break; }
                        if (!childNext) { stack.Pop(); continue; }
                        if (childValue is IEnumerator nested) stack.Push(nested); else yield return childValue;
                    }
                    if (failure != null) break;
                }
                else yield return value;
            }
            if (controller) controller.Automation = false;
            if (failure != null)
            {
                SessionState.SetBool(Key+".failed",true);
                File.WriteAllText(Review+"/flow-checks.txt","FAIL\n"+failure); Debug.LogException(failure);
                yield return Capture(Path.GetFullPath(Review+"/failed-flow.png"));
            }
            EditorApplication.delayCall += () => { if (SessionState.GetBool(Key+".owned",false)) EditorApplication.isPlaying = false; };
        }

        static IEnumerator RunFlow(DrawerPrototypeController c)
        {
            yield return new WaitForSeconds(2.8f); c.Automation = true;
            var pipeline = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline as UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset;
            File.WriteAllText(Review+"/render-state.txt","Pipeline="+(pipeline ? pipeline.name : "none")+"\nShadows="+(pipeline && pipeline.supportsMainLightShadows)+"\nShadowDistance="+(pipeline ? pipeline.shadowDistance : 0)+"\nQuality="+QualitySettings.names[QualitySettings.GetQualityLevel()]);
            string directory = SessionState.GetString(Key+".checkDirectory","");
            var checks = new List<string>();
            Require(c.ViewCamera.orthographic && Vector3.Dot(c.ViewCamera.transform.forward,Vector3.down) > .9999f,"Camera must be completely vertical orthographic."); checks.Add("Vertical orthographic camera");
            Require(c.Papers.Count == 26 && c.Items.Length == 5,"Expected 26 movable envelopes and five distinct props.");
            Require(c.Papers.All(p => p.body && p.hitBox && !p.body.isKinematic),"Letters need actual dynamic rigidbodies."); checks.Add("26 physical envelopes and 5 modeled props");
            yield return Capture(Path.Combine(directory,"01-initial.png"));
            Require(c.Papers.All(p => p.transform.Find("Grass green leaf wax seal")),"Every envelope needs the same leaf wax seal.");
            Require(c.AlbumModel && !c.AlbumModel.activeSelf && !c.AlbumModel.GetComponent<DrawerToken>(),"The album model is built but cannot be collected in the drawer.");
            yield return CheckPaperPhysics(c,checks);
            DrawerToken top = c.Papers.LastOrDefault(p => c.TopAt(p.transform.position) == p);
            Require(top,"A top envelope is clickable.");
            Vector2 click = c.ViewCamera.WorldToScreenPoint(c.VisiblePoint(top));
            c.Pointer(click,true,true,false); yield return null; c.Pointer(click,false,false,true);
            Require(!c.Dragging && c.Progress.Count == 0,"A short paper click cannot drag or collect a hidden item."); checks.Add("Short press does not drag or collect through paper");
            Vector3 before = top.transform.position;
            yield return Drag(c,top,new Vector3(-8.5f,0,4.5f));
            Require(Vector3.Distance(before,top.transform.position) > .8f && !top.body.isKinematic,"A held paper moves and resumes physical gravity on release."); checks.Add("Long press, lifting, dragging and dynamic release");
            Require(c.Papers.All(p => (c.ConstrainPaper(p,p.body.position)-p.body.position).sqrMagnitude < .003f),"Pulling outside must keep the entire envelope inside the drawer."); checks.Add("Outside-pointer drag remains inside the physical drawer");
            for (int id = 0; id < 5; id++)
            {
                DrawerToken item = c.Items[id];
                for (int attempt = 0; !c.CanPickup(item) && attempt < 40; attempt++)
                {
                    DrawerToken blocker = null;
                    Vector3 blockerPoint = Vector3.zero;
                    for (int x = -1; x <= 1 && !blocker; x++) for (int z = -1; z <= 1 && !blocker; z++)
                    {
                        Vector3 sample = item.transform.TransformPoint(new Vector3(item.dimensions.x*x*.27f,0,item.dimensions.z*z*.27f));
                        var candidate = c.TopAt(sample); if (candidate && candidate.IsPaper) { blocker = candidate; blockerPoint = sample; }
                    }
                    Require(blocker,"A covered prop must have a movable paper above it: " + id);
                    Vector3 location = item.transform.position;
                    // Fold letters into the opposite side of the drawer, using the same bounded input route.
                    float tx = location.x < -.3f ? 3.0f : -3.7f;
                    float tz = location.z < .2f ? 2.0f : -1.6f;
                    Vector3 beforeDrag = blocker.body.position;
                    yield return Drag(c,blocker,new Vector3(tx+(attempt%3-1)*.22f,0,tz),blockerPoint);
                    File.AppendAllText(Path.Combine(directory,"drag-trace.txt"),"item="+id+" attempt="+attempt+" itemPosition="+item.body.position+" paper="+c.Papers.IndexOf(blocker)+" before="+beforeDrag+" after="+blocker.body.position+" target="+new Vector2(tx,tz)+"\n");
                }
                Require(c.CanPickup(item),"Could not uncover prop " + id);
                click = c.ViewCamera.WorldToScreenPoint(c.VisiblePoint(item));
                c.Pointer(click,true,true,false); yield return null; c.Pointer(click,false,false,true);
                yield return new WaitForSeconds(.55f);
                Require(c.SelectedItem == id && c.Progress.collected[id],"Uncovered prop must open its introduction: " + id);
                if (id == 0) yield return Capture(Path.Combine(directory,"02-magic-book.png"));
                if (id == 2) yield return Capture(Path.Combine(directory,"02b-camera.png"));
                if (id == 4) yield return Capture(Path.Combine(directory,"03-grandmother-letter.png"));
                click = RectTransformUtility.WorldToScreenPoint(null,c.CloseTarget.position);
                c.Pointer(click,true,true,false); yield return null; c.Pointer(click,false,false,true);
                yield return new WaitForSeconds(.5f);
                Require(c.SelectedItem < 0 && item.docked && c.Progress.read[id],"Closing the introduction docks and marks the prop read: " + id);
                checks.Add("Uncover / pick up / introduce / dock: " + id);
            }
            Require(c.Progress.Complete && c.Progress.Count == 5,"Completion requires all five props and introductions.");
            var saved = DrawerProgress.Restore(File.ReadAllText(c.SavePath));
            Require(saved.Complete && string.IsNullOrEmpty(c.SaveError),"Physical flow saves all collection and read progress."); checks.Add("Completion and isolated progress persistence");
            click = c.ViewCamera.WorldToScreenPoint(c.Items[0].transform.position);
            c.Pointer(click,true,true,false); yield return null; c.Pointer(click,false,false,true);
            yield return new WaitForSeconds(.55f);
            Require(c.SelectedItem == 0 && c.Progress.Count == 5,"Shelf click reopens the existing introduction without duplicate collection.");
            c.CloseItem(); yield return new WaitForSeconds(.5f); checks.Add("Reopen collected item from the edge shelf");
            yield return Capture(Path.Combine(directory,"04-complete.png"));
            File.WriteAllText(Review+"/flow-checks.txt","PASS\n"+string.Join("\n",checks)+"\nEvidence="+directory+"\nInput route: synthetic screen-pointer events through the same runtime handlers; real Unity physics and GameView captures. Human feel and visual approval remain separate.");
        }

        static IEnumerator CheckPaperPhysics(DrawerPrototypeController c, List<string> checks)
        {
            // A deterministic contact fixture verifies transfer of force independently of the random-looking pile.
            var all = c.Papers.Concat(c.Items).ToArray();
            Vector3[] positions = all.Select(p => p.body.position).ToArray();
            Quaternion[] rotations = all.Select(p => p.body.rotation).ToArray();
            foreach (var p in all) { p.hitBox.enabled = false; p.body.isKinematic = true; }
            DrawerToken first = c.Papers[0], second = c.Papers[1];
            first.body.position = new Vector3(-2,.16f,0); second.body.position = new Vector3(.38f,.16f,0);
            foreach (var p in new[] { first,second })
            { p.body.rotation = Quaternion.identity; p.hitBox.enabled = true; p.body.isKinematic = false; }
            Physics.SyncTransforms(); yield return new WaitForSeconds(.5f);
            Vector3 before = second.body.position;
            yield return Drag(c,first,new Vector3(1.0f,0,0));
            Require(second.body.position.x > before.x+.15f,"Dragged envelope must push its neighbour through physics contact.");
            Require(!first.body.isKinematic && first.body.useGravity,"Release must restore gravity while retaining a dynamic body.");
            checks.Add("Two-envelope contact transfers force; release restores gravity");
            for (int i = 0; i < all.Length; i++)
            {
                all[i].body.position = positions[i]; all[i].body.rotation = rotations[i];
                all[i].hitBox.enabled = true; all[i].body.isKinematic = false;
                all[i].body.velocity = Vector3.zero; all[i].body.angularVelocity = Vector3.zero;
            }
            Physics.SyncTransforms(); yield return new WaitForSeconds(.8f);
        }

        static IEnumerator ReviewModels(DrawerPrototypeController c)
        {
            yield return new WaitForSeconds(2.8f); c.Automation = true;
            foreach (var item in c.Papers.Concat(c.Items)) item.body.isKinematic = true;
            yield return Capture(Path.GetFullPath(Review+"/latest-drawer.png"));
            var foliageRotation = c.FoliageModels.Select(p => p.transform.rotation).ToArray();
            yield return new WaitForSeconds(1.0f);
            yield return Capture(Path.GetFullPath(Review+"/wind-next-frame.png"));
            Require(c.FoliageModels.Where((p,i) => Quaternion.Angle(foliageRotation[i],p.transform.rotation) > .01f).Any(),"Live foliage must move between captured frames.");
            float azimuth = c.sunAzimuth; c.sunAzimuth += 35;
            yield return null; yield return Capture(Path.GetFullPath(Review+"/lighting-adjusted.png"));
            c.sunAzimuth = azimuth;
            Require(c.FoliageModels.Count == 4 && c.FoliageModels.All(p => p.GetComponent<DrawerFoliageSway>()),"Three modeled branches and a grass sprig need live sway.");
            foreach (var leaf in c.FoliageModels.SelectMany(p => p.GetComponentsInChildren<MeshFilter>()))
                Require(leaf.sharedMesh.vertices.All(v => !float.IsNaN(v.x) && !float.IsNaN(v.y) && !float.IsNaN(v.z) && !float.IsInfinity(v.sqrMagnitude)),"Foliage vertices must be finite: "+leaf.name);
            File.WriteAllText(Review+"/plant-bounds.txt",string.Join("\n",c.FoliageModels.Select(p => p.name+" meshes="+p.GetComponentsInChildren<MeshFilter>().Length+" sway="+p.GetComponent<DrawerFoliageSway>().strength)));
            foreach (var p in c.Papers) p.gameObject.SetActive(false);
            c.Items[4].gameObject.SetActive(false);
            Vector3[] locations = { new Vector3(-3,1,1.45f),new Vector3(-.4f,1,1.45f),new Vector3(-2.0f,1,-1.35f),new Vector3(1.1f,1,-1.1f) };
            for (int i = 0; i < 4; i++)
            {
                c.Items[i].body.isKinematic = true; c.Items[i].transform.localScale = Vector3.one;
                c.Items[i].transform.SetPositionAndRotation(locations[i],Quaternion.Euler(-18,-7,5));
            }
            c.AlbumModel.SetActive(true); c.AlbumModel.transform.SetPositionAndRotation(new Vector3(2.1f,1,1.45f),Quaternion.Euler(-18,-7,5));
            yield return new WaitForSeconds(.4f);
            yield return Capture(Path.GetFullPath(Review+"/model-review.png"));
            File.WriteAllText(Review+"/visual-checks.txt",DateTime.UtcNow.ToString("O")+"\nPASS: foliage mesh vertices finite; branch transforms move between captures; initial, changed-light and prop-display frames captured in Unity. Visual approval remains with the user.");
        }

        static void ExportModels(DrawerPrototypeController c)
        {
            Require(c && EditorApplication.isPlaying,"Model export requires the independent drawer Play scene.");
            string directory = Root+"/Models"; Directory.CreateDirectory(directory);
            GameObject[] sources = { c.Items[0].gameObject,c.Items[1].gameObject,c.AlbumModel,c.Items[2].gameObject,c.Items[3].gameObject,c.FoliageModels[0],c.FoliageModels[3] };
            string[] names = { "MagicBook","Dictionary","AlbumTaskBook","Camera","Wand","LeafBranch","GrassSprig" };
            for (int i = 0; i < sources.Length; i++)
            {
                string assetPath = directory+"/"+names[i]+"Parts.asset";
                Require(!File.Exists(assetPath),"Model export already exists; preserve it instead of overwriting: "+assetPath);
                var container = new Mesh { name = names[i]+" parts" }; AssetDatabase.CreateAsset(container,assetPath);
                var copy = UnityEngine.Object.Instantiate(sources[i]); copy.name = names[i];
                try
                {
                    foreach (var token in copy.GetComponentsInChildren<DrawerToken>(true)) UnityEngine.Object.DestroyImmediate(token);
                    foreach (var body in copy.GetComponentsInChildren<Rigidbody>(true)) UnityEngine.Object.DestroyImmediate(body);
                    foreach (var collider in copy.GetComponentsInChildren<Collider>(true)) UnityEngine.Object.DestroyImmediate(collider);
                    var assets = new Dictionary<UnityEngine.Object,UnityEngine.Object>();
                    foreach (var filter in copy.GetComponentsInChildren<MeshFilter>(true))
                    {
                        if (AssetDatabase.Contains(filter.sharedMesh)) continue;
                        if (!assets.TryGetValue(filter.sharedMesh,out var saved))
                        { saved = UnityEngine.Object.Instantiate(filter.sharedMesh); saved.name = filter.sharedMesh.name; AssetDatabase.AddObjectToAsset(saved,container); assets.Add(filter.sharedMesh,saved); }
                        filter.sharedMesh = (Mesh)saved;
                    }
                    foreach (var renderer in copy.GetComponentsInChildren<Renderer>(true))
                    {
                        Material[] materials = renderer.sharedMaterials;
                        for (int m = 0; m < materials.Length; m++)
                        {
                            if (AssetDatabase.Contains(materials[m])) continue;
                            if (!assets.TryGetValue(materials[m],out var saved))
                            { saved = UnityEngine.Object.Instantiate(materials[m]); saved.name = materials[m].name; AssetDatabase.AddObjectToAsset(saved,container); assets.Add(materials[m],saved); }
                            materials[m] = (Material)saved;
                        }
                        renderer.sharedMaterials = materials;
                    }
                    copy.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity); copy.transform.localScale = Vector3.one; copy.SetActive(true);
                    EditorUtility.SetDirty(container); AssetDatabase.SaveAssetIfDirty(container);
                    PrefabUtility.SaveAsPrefabAsset(copy,directory+"/"+names[i]+".prefab");
                }
                finally { UnityEngine.Object.DestroyImmediate(copy); }
            }
            File.WriteAllText(Review+"/model-export.txt","Exported seven standalone visual prefabs: three books, camera, wand, leafy branch, grass sprig. AlbumTaskBook is not a drawer collectible.");
        }

        static IEnumerator RunResume(DrawerPrototypeController c)
        {
            yield return new WaitForSeconds(2.8f); c.Automation = true;
            Require(c.Progress.Complete && c.Items.All(item => item.docked && item.body.isKinematic),"A fresh Play must restore all five collected props to the shelf.");
            Require(c.Papers.Count == 26 && c.Papers.All(p => !p.body.isKinematic),"Reload restores collection progress but rebuilds movable paper.");
            Require(c.SelectedItem < 0 && string.IsNullOrEmpty(c.SaveError),"Read introductions must stay closed after reload.");
            string directory = SessionState.GetString(Key+".checkDirectory","");
            yield return Capture(Path.Combine(directory,"05-resumed.png"));
            File.WriteAllText(Review+"/resume-checks.txt","PASS: fresh Play restored five collected/read props on the shelf; all 26 papers rebuilt as dynamic bodies; no duplicate popup or save error.\n"+directory);
        }

        static IEnumerator Drag(DrawerPrototypeController c, DrawerToken paper, Vector3 target, Vector3? exposedPoint = null)
        {
            Vector2 from = c.ViewCamera.WorldToScreenPoint(exposedPoint ?? c.VisiblePoint(paper));
            Vector2 to = c.ViewCamera.WorldToScreenPoint(target);
            Require(c.Hit(from) == paper,"The drag fixture must press an exposed point on the intended envelope.");
            c.Pointer(from,true,true,false); yield return new WaitForSeconds(.23f);
            for (int frame = 0; frame < 36; frame++)
            {
                c.Pointer(Vector2.Lerp(from,to,(frame+1)/36f),false,true,false); yield return new WaitForFixedUpdate();
            }
            c.Pointer(to,false,true,false); yield return new WaitForSeconds(.4f); c.Pointer(to,false,false,true);
            yield return new WaitForSeconds(.65f);
        }

        static IEnumerator Capture(string path)
        {
            if (Application.isBatchMode)
            {
                yield return null;
                var controller = UnityEngine.Object.FindObjectOfType<DrawerPrototypeController>();
                Camera camera = controller.ViewCamera;
                var target = RenderTexture.GetTemporary(1600,900,24,RenderTextureFormat.ARGB32);
                Rect priorRect = camera.rect; RenderTexture priorActive = RenderTexture.active;
                Texture2D rendered = null;
                try
                {
                    camera.rect = new Rect(0,0,1,1);
                    var request = new UnityEngine.Rendering.Universal.UniversalRenderPipeline.SingleCameraRequest { destination = target };
                    UnityEngine.Rendering.RenderPipeline.SubmitRenderRequest(camera,request);
                    RenderTexture.active = target;
                    rendered = new Texture2D(1600,900,TextureFormat.RGB24,false);
                    rendered.ReadPixels(new Rect(0,0,1600,900),0,0); rendered.Apply();
                    Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllBytes(path,rendered.EncodeToPNG());
                }
                finally
                {
                    camera.rect = priorRect; RenderTexture.active = priorActive;
                    if (rendered) UnityEngine.Object.Destroy(rendered); RenderTexture.ReleaseTemporary(target);
                }
                yield break;
            }
            yield return new WaitForEndOfFrame();
            Texture2D image = ScreenCapture.CaptureScreenshotAsTexture();
            try { Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllBytes(path,image.EncodeToPNG()); }
            finally { UnityEngine.Object.Destroy(image); }
        }

        static void WriteStatus(string message)
        {
            Directory.CreateDirectory(Review);
            File.WriteAllText(Review+"/status.txt",DateTime.UtcNow.ToString("O")+"\n"+message+"\nPlay="+EditorApplication.isPlaying+"\nCompiling="+EditorApplication.isCompiling+
                "\nActiveScene="+SceneManager.GetActiveScene().path+"\nDirty="+SceneManager.GetActiveScene().isDirty);
        }

        static string Hash(string path)
        {
            if (!File.Exists(path)) return "missing";
            using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path)));
        }
        static string EmptyNull(string value) => string.IsNullOrEmpty(value) ? null : value;
        static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    }
}
