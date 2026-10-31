using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using WeatherHouse.Hillside;
using Object = UnityEngine.Object;

namespace WeatherHouse.SquirrelOga.Editor
{
    // Explicit installation only: this class has no InitializeOnLoad or automatic scene callback.
    public static class SquirrelOgaInstaller
    {
        public const string ModelPath = "Assets/Characters/SquirrelOGA20261027/Squirrel_OGA_Natural_Unity.fbx";
        public const string PrefabPath = "Assets/Game/World/Hillside/Generated/squirrel.prefab";
        public const string ReportFolder = "LifeGameplayReview/20261027/squirrel-oga";
        const string MaterialFolder = "Assets/Characters/SquirrelOGA20261027/Materials/";
        const float NominalHeight = .72f, SurfaceTolerance = .01f;
        static readonly string[] Colors = { "Chestnut Fur", "Cream Belly", "Tail", "Muzzle", "Eyes Nose", "Paws", "Incisors", "Inner Ear" };
        static readonly string[] RequiredTakes = { "Idle.000", "RunSlow_Derived", "Run", "RestDown_Derived", "RestLoop_Derived", "RestUp_Derived", "Drink_Derived" };
        static readonly string[] BoneNames = { "Main", "Pan", "Backbone", "Backbone.001", "Neck", "Head", "Mouth",
            "Arm_L", "Arm_L.001", "Arm_L.002", "Arm_R", "Arm_R.001", "Arm_R.002",
            "Leg_L", "Leg_L.001", "Leg_L.002", "Leg_L.003", "Leg_R", "Leg_R.001", "Leg_R.002", "Leg_R.003",
            "Tail", "Tail.001", "Tail.002", "Tail.003", "Tail.004", "Tail.005", "IK-Pan", "IK-Tail", "IK-Leg_L", "IK-Leg_R", "IK-Arm_L", "IK-Arm_R", "IK-Head" };
        static readonly float[] DrinkSamples = { .8f, 1.2f, 1.8f, 2.2f };
        static readonly HashSet<string> VisualFields = new HashSet<string> { "visualRoot", "motionRoot", "animator", "clips", "forageContactData" };

        [Serializable] public sealed class Report
        {
            public string status, operation, utc, modelDependencyHash, error;
            public bool savedPrefab, savedScene;
            public int sceneAnimals, inheritedAnimals, reboundAnimals, rebuiltAnimals;
            public List<string> checks = new List<string>();
            public List<ContactResult> contacts = new List<ContactResult>();
            public List<SkinProbe> skinProbes = new List<SkinProbe>();
        }
        [Serializable] public sealed class ContactResult
        {
            public string animal, scene;
            public int vertex;
            public Vector3 mouthHeadLocal, drinkRootContact;
            public float maxSurfaceError;
        }
        [Serializable] public sealed class SkinProbe
        {
            public string scope, mesh, boundsRoot;
            public Vector3 skinLossyScale;
            public Bounds rendererWorldBounds, trueWorldBounds, falseWorldBounds, manualWorldBounds, animationLocalBounds;
            public float trueMaxError, trueRmsError, falseMaxError, falseRmsError;
            public bool rendererContainsManual, rendererConservativeDifference;
        }

        sealed class Bundle
        {
            public GameObject model;
            public AnimationClip idle, walk, run, restDown, restLoop, restUp, drink;
            public string hash;
        }

        [MenuItem("Weather House/Review 20261027/Install squirrel OGA")]
        static void MenuInstall() => InstallOpenScene(true);

        [MenuItem("Weather House/Review 20261027/Check squirrel OGA")]
        static void MenuValidate() => ValidateOpenScene(false);

        [MenuItem("Weather House/Review 20261027/Check squirrel OGA candidate prefab")]
        static void MenuValidateCandidate() => ValidateCandidatePrefab();

        // Build and check only disposable prefab contents; no importer, prefab or loaded scene is changed.
        public static string ValidateCandidatePrefab()
        {
            var report = NewReport("validate-candidate-prefab");
            try
            {
                RequireEditMode();
                var bundle = LoadBundle();
                report.modelDependencyHash = bundle.hash;
                var prefab = PrefabUtility.LoadPrefabContents(PrefabPath);
                try
                {
                    var animal = prefab.GetComponent<WildlifeAgent>();
                    Require(animal && animal.speciesId == "squirrel", "The target prefab is not the squirrel agent.");
                    string invariant = NonVisualSignature(animal);
                    ReplaceVisual(animal, bundle, report);
                    Require(NonVisualSignature(animal) == invariant, "Candidate prefab non-visual fields changed.");
                    ValidateAnimal(animal, bundle, report);
                }
                finally { PrefabUtility.UnloadPrefabContents(prefab); }
                report.status = "PASS_ISOLATED_CANDIDATE_PREFAB_NOT_INSTALLED";
            }
            catch (Exception exception) { report.status = "FAIL"; report.error = exception.ToString(); WriteReport(report); throw; }
            return WriteReport(report);
        }

        public static string InstallOpenScene(bool savePrefab = true)
        {
            var report = NewReport("install");
            try
            {
                RequireEditMode();
                var animals = SceneAnimals();
                var before = animals.ToDictionary(a => a, NonVisualSignature);
                foreach (var animal in animals) PreflightAnimal(animal);
                PrepareImporter();
                var bundle = LoadBundle();
                report.modelDependencyHash = bundle.hash;
                report.checks.Add("Run uses " + TakeName(bundle.run.name) + "; walk uses RunSlow_Derived. These are declared derived motions, not newly found native actions.");
                // Construct and calibrate in an isolated prefab scene before touching any open scene.
                var prefab = PrefabUtility.LoadPrefabContents(PrefabPath);
                try
                {
                    var template = prefab.GetComponent<WildlifeAgent>();
                    Require(template && template.speciesId == "squirrel", "The target prefab is not the squirrel agent.");
                    string invariant = NonVisualSignature(template);
                    bool changed = !IsCurrent(template, bundle) || !BindingsCurrent(template, bundle);
                    if (changed)
                    {
                        if (!IsCurrent(template, bundle)) ReplaceVisual(template, bundle, report);
                        else BindExistingVisual(template, bundle);
                        Require(NonVisualSignature(template) == invariant, "Prefab non-visual fields changed.");
                        ValidateAnimal(template, bundle, report);
                        if (savePrefab)
                        {
                            Require(PrefabUtility.SaveAsPrefabAsset(prefab, PrefabPath), "Could not save the squirrel prefab.");
                            report.savedPrefab = true;
                        }
                    }
                    else ValidateAnimal(template, bundle, report);
                }
                finally { PrefabUtility.UnloadPrefabContents(prefab); }

                foreach (var animal in animals)
                {
                    bool rebuilt = !IsCurrent(animal, bundle);
                    bool rebound = !rebuilt && !BindingsCurrent(animal, bundle);
                    if (rebuilt)
                    {
                        UnpackOnlyAnimalIfNeeded(animal);
                        Undo.RegisterFullObjectHierarchyUndo(animal.gameObject, "Install OGA squirrel visual");
                        ReplaceVisual(animal, bundle, report);
                        report.rebuiltAnimals++;
                    }
                    else
                    {
                        report.inheritedAnimals++;
                        if (rebound)
                        {
                            Undo.RecordObject(animal, "Rebind inherited OGA squirrel clips");
                            BindExistingVisual(animal, bundle);
                            report.reboundAnimals++;
                        }
                    }
                    Require(NonVisualSignature(animal) == before[animal], "Non-visual fields changed: " + AnimalName(animal));
                    ValidateAnimal(animal, bundle, report);
                    if (rebuilt || rebound) EditorSceneManager.MarkSceneDirty(animal.gameObject.scene);
                }
                report.sceneAnimals = animals.Length;
                report.checks.Add("Every loaded squirrel retained root TRS, parent, name and every non-visual public WildlifeAgent field.");
                report.checks.Add("Only the species prefab may have been saved; no scene was saved, no old asset was deleted.");
                report.status = "PASS_INSTALL_SCENE_SAVE_AND_PLAY_ACCEPTANCE_PENDING";
            }
            catch (Exception exception) { report.status = "FAIL"; report.error = exception.ToString(); WriteReport(report); throw; }
            return WriteReport(report);
        }

        // Read-only for open scenes and assets. Sampled geometry is evaluated on isolated visual clones.
        public static string ValidateOpenScene(bool allowPlay = false)
        {
            var report = NewReport("validate");
            try
            {
                Require(!EditorApplication.isCompiling, "Wait for compilation before validation.");
                Require(allowPlay || !EditorApplication.isPlayingOrWillChangePlaymode, "Pass allowPlay=true to inspect during Play.");
                var bundle = LoadBundle();
                report.modelDependencyHash = bundle.hash;
                var animals = SceneAnimals();
                Require(animals.Length > 0, "No squirrel exists in the loaded non-preview scenes.");
                foreach (var animal in animals) ValidateAnimal(animal, bundle, report);
                report.sceneAnimals = animals.Length;
                report.status = "PASS_READ_ONLY_BINDINGS_AND_CONTACT_GEOMETRY";
                report.checks.Add("Validation did not import, install, save, move scene objects, or initialize animal gameplay.");
            }
            catch (Exception exception) { report.status = "FAIL"; report.error = exception.ToString(); WriteReport(report); throw; }
            return WriteReport(report);
        }

        static void PrepareImporter()
        {
            var importer = AssetImporter.GetAtPath(ModelPath) as ModelImporter;
            Require(importer, "Missing FBX importer: " + ModelPath);
            string before = EditorJsonUtility.ToJson(importer);
            importer.animationType = ModelImporterAnimationType.Generic;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.animationCompression = ModelImporterAnimationCompression.Off;
            importer.importAnimation = true; importer.importBlendShapes = true; importer.isReadable = true;
            importer.importCameras = importer.importLights = importer.optimizeGameObjects = false;
            importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
            var takes = importer.clipAnimations.Length > 0 ? importer.clipAnimations : importer.defaultClipAnimations;
            foreach (var suffix in RequiredTakes)
                Require(takes.Count(c => TakeName(c.name) == suffix) == 1, "Expected exactly one take ending in " + suffix);
            foreach (var take in takes)
            {
                string name = TakeName(take.name);
                take.loopTime = name.StartsWith("Idle.", StringComparison.Ordinal) || name == "Run" || name == "RunGrounded_Derived" || name == "RunSlow_Derived" || name == "RestLoop_Derived";
                take.loopPose = false;
                take.keepOriginalOrientation = take.keepOriginalPositionY = take.keepOriginalPositionXZ = true;
                take.lockRootRotation = take.lockRootHeightY = take.lockRootPositionXZ = true;
            }
            importer.clipAnimations = takes;
            var remaps = importer.GetExternalObjectMap();
            foreach (var color in Colors)
            {
                string name = "OGA Natural " + color;
                var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialFolder + name.Replace(' ', '_') + ".mat");
                Require(material, "Missing ordinary-color material " + name);
                Require(!material.HasProperty("_BaseMap") || !material.GetTexture("_BaseMap"), "A source texture must not replace ordinary colors: " + name);
                var key = new AssetImporter.SourceAssetIdentifier(typeof(Material), name);
                if (!remaps.TryGetValue(key, out Object value) || value != material) importer.AddRemap(key, material);
            }
            if (before != EditorJsonUtility.ToJson(importer)) importer.SaveAndReimport();
        }

        static Bundle LoadBundle()
        {
            ValidateGeometryMath();
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            Require(model, "Missing OGA model: " + ModelPath);
            var clips = AssetDatabase.LoadAllAssetsAtPath(ModelPath).OfType<AnimationClip>().Where(c => !c.name.StartsWith("__preview__", StringComparison.Ordinal)).ToArray();
            AnimationClip Take(string name)
            {
                var matches = clips.Where(c => TakeName(c.name) == name).ToArray();
                Require(matches.Length == 1, "Missing or ambiguous FBX take: " + name);
                Require(!matches[0].legacy, "Generic Playables require non-legacy clip: " + name);
                return matches[0];
            }
            var groundedRun = clips.Where(c => TakeName(c.name) == "RunGrounded_Derived").ToArray();
            Require(groundedRun.Length <= 1, "Ambiguous RunGrounded_Derived take.");
            var bundle = new Bundle { model = model, hash = AssetDatabase.GetAssetDependencyHash(ModelPath).ToString(),
                idle = Take("Idle.000"), walk = Take("RunSlow_Derived"), run = groundedRun.Length == 1 ? groundedRun[0] : Take("Run"), restDown = Take("RestDown_Derived"),
                restLoop = Take("RestLoop_Derived"), restUp = Take("RestUp_Derived"), drink = Take("Drink_Derived") };
            foreach (var clip in AllClips(bundle)) ValidateBindings(clip, model.transform);
            return bundle;
        }

        static void ReplaceVisual(WildlifeAgent animal, Bundle bundle, Report report)
        {
            var old = animal.visualRoot ? animal.visualRoot : animal.transform.Find("Visual");
            if (old) Require(old != animal.transform && old.IsChildOf(animal.transform), "Visual is not inside its own animal.");
            // Build a complete replacement beside the old visual; failure leaves the old hierarchy intact.
            var visual = new GameObject("Visual_OGA_Pending").transform;
            visual.SetParent(animal.transform, false);
            try
            {
                var alignment = new GameObject("ModelAlignment").transform; alignment.SetParent(visual, false);
                var model = (GameObject)PrefabUtility.InstantiatePrefab(bundle.model, alignment);
                model.name = "Model";
                var animator = model.GetComponent<Animator>();
                if (!animator) animator = model.AddComponent<Animator>();
                animator.runtimeAnimatorController = null; animator.applyRootMotion = false; animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                foreach (var skin in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    skin.updateWhenOffscreen = false;
                    skin.sharedMaterials = skin.sharedMaterials.Select(ResolveColor).ToArray();
                }
                FitAnimationBounds(model, bundle);
                bundle.idle.SampleAnimation(model, 0f);
                foreach (var skin in model.GetComponentsInChildren<SkinnedMeshRenderer>(true)) ProbeSkin(skin, animal.name + "/Idle unscaled", report);
                Transform head = Named(model.transform, "Head"), pan = Named(model.transform, "Pan");
                var forward = visual.InverseTransformVector(head.position - pan.position); forward.y = 0f;
                Require(forward.sqrMagnitude > .000001f, "Head-Pan does not define a ground-plane forward direction.");
                alignment.localRotation = Quaternion.FromToRotation(forward.normalized, Vector3.forward);
                Bounds bounds = LocalBounds(model, animal.transform);
                Require(bounds.size.y > .001f && Finite(bounds.size), "Invalid Idle skinned bounds.");
                visual.localScale = Vector3.one * (NominalHeight / bounds.size.y);
                bounds = LocalBounds(model, animal.transform);
                visual.localPosition = new Vector3(-bounds.center.x, -bounds.min.y, -bounds.center.z);
                bounds = LocalBounds(model, animal.transform);
                Require(Mathf.Abs(bounds.size.y - NominalHeight) < .00025f && Mathf.Abs(bounds.min.y) < .00025f &&
                    Mathf.Abs(bounds.center.x) < .00025f && Mathf.Abs(bounds.center.z) < .00025f,
                    "Actual baked Idle bounds did not retain the calibrated 0.72m height/ground/center: " + bounds);
                forward = animal.transform.InverseTransformVector(head.position - pan.position); forward.y = 0f;
                Require(Vector3.Dot(forward.normalized, Vector3.forward) > .999f, "Calibrated Head-Pan is not forward +Z.");
                foreach (var skin in model.GetComponentsInChildren<SkinnedMeshRenderer>(true)) ProbeSkin(skin, animal.name + "/Idle calibrated", report);

                var rig = visual.gameObject.AddComponent<SquirrelOgaRig>();
                rig.idle = bundle.idle; rig.walk = bundle.walk; rig.run = bundle.run;
                rig.restDown = bundle.restDown; rig.restLoop = bundle.restLoop; rig.restUp = bundle.restUp; rig.drink = bundle.drink;
                rig.head = head;
                rig.headSkin = model.GetComponentsInChildren<SkinnedMeshRenderer>(true).Single(s => s.sharedMesh && s.sharedMesh.GetBlendShapeIndex("EyesClosed") >= 0);
                Require(rig.headSkin.sharedMaterials.Any(m => m && ColorName(m.name) == "OGA Natural Muzzle"), "Closed-eye head skin has no Muzzle submesh.");
                rig.modelDependencyHash = bundle.hash;
                rig.boundedSkins = model.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                rig.animationLocalBounds = rig.boundedSkins.Select(skin => skin.localBounds).ToArray();
                rig.ApplyAnimationBounds();
                CalibrateMouth(rig, model, animal.transform, report);
                bundle.idle.SampleAnimation(model, 0f);
                rig.headSkin.SetBlendShapeWeight(rig.headSkin.sharedMesh.GetBlendShapeIndex("EyesClosed"), 0f);
                visual.name = "Visual";
                if (old) Object.DestroyImmediate(old.gameObject);
                animal.visualRoot = visual;
                BindExistingVisual(animal, bundle);
            }
            catch { Object.DestroyImmediate(visual.gameObject); throw; }
        }

        static void CalibrateMouth(SquirrelOgaRig rig, GameObject model, Transform animalRoot, Report report)
        {
            Require(rig.drink.length >= DrinkSamples.Max(), "Drink_Derived must include the complete 2.2-second hold sample.");
            int[] triangles = MuzzleTriangles(rig.headSkin);
            int[] indices = triangles.Distinct().ToArray();
            rig.drink.SampleAnimation(model, 1.4f);
            ProbeSkin(rig.headSkin, animalRoot.name + "/Drink calibrate 1.4", report);
            Vector3[] points = BakeWorld(rig.headSkin);
            var local = indices.ToDictionary(i => i, i => animalRoot.InverseTransformPoint(points[i]));
            float front = local.Values.Max(v => v.z), back = local.Values.Min(v => v.z);
            float low = local.Values.Min(v => v.y), high = local.Values.Max(v => v.y);
            float left = local.Values.Min(v => v.x), right = local.Values.Max(v => v.x), middle = (left + right) * .5f;
            var candidates = indices.Where(i => local[i].z >= front - (front - back) * .28f &&
                local[i].y <= low + (high - low) * .62f && Mathf.Abs(local[i].x - middle) <= Mathf.Max(.006f, (right - left) * .28f)).ToArray();
            Require(candidates.Length > 0, "No front/lower/central Muzzle lip vertices were found; inspect geometry rather than use an old constant.");
            var anchors = candidates.ToDictionary(i => i, i => rig.head.InverseTransformPoint(points[i]));
            var errors = candidates.ToDictionary(i => i, i => 0f);
            foreach (float time in DrinkSamples)
            {
                rig.drink.SampleAnimation(model, time); var surface = BakeWorld(rig.headSkin);
                ProbeSkin(rig.headSkin, animalRoot.name + "/Drink calibrate " + time.ToString("F1", CultureInfo.InvariantCulture), report);
                foreach (int i in candidates)
                    errors[i] = Mathf.Max(errors[i], SurfaceDistance(rig.head.TransformPoint(anchors[i]), surface, triangles));
            }
            var valid = candidates.Where(i => errors[i] < SurfaceTolerance).OrderByDescending(i => local[i].z - Mathf.Abs(local[i].x - middle) * .6f - errors[i]).ToArray();
            Require(valid.Length > 0, "No actual lip point stayed within 1cm of the Muzzle surface at all four Drink hold samples.");
            int selected = valid[0];
            rig.drink.SampleAnimation(model, 1.4f);
            rig.mouthVertexIndex = selected; rig.mouthHeadLocal = anchors[selected]; rig.mouthSurfaceMaxError = errors[selected];
            rig.drinkRootContact = animalRoot.InverseTransformPoint(rig.head.TransformPoint(rig.mouthHeadLocal));
            Require(Finite(rig.mouthHeadLocal) && Finite(rig.drinkRootContact), "Non-finite lip anchor.");
        }

        static void ValidateAnimal(WildlifeAgent animal, Bundle bundle, Report report)
        {
            Require(IsCurrent(animal, bundle), "Missing or stale OGA visual/profile: " + AnimalName(animal));
            var rig = animal.visualRoot.GetComponent<SquirrelOgaRig>();
            Require(animal.animator && !animal.animator.applyRootMotion && animal.animator.runtimeAnimatorController == null, "Invalid Animator root-motion/controller settings.");
            Require(animal.clips.idle == bundle.idle && animal.clips.walk == bundle.walk && animal.clips.run == bundle.run &&
                animal.clips.restDown == bundle.restDown && animal.clips.restSleep == bundle.restLoop && animal.clips.restUp == bundle.restUp,
                "A scene override still binds a former squirrel clip: " + AnimalName(animal));
            foreach (var clip in new[] { animal.clips.idle, animal.clips.forage, animal.clips.walk, animal.clips.run, animal.clips.greet,
                animal.clips.alert, animal.clips.takeoff, animal.clips.fly, animal.clips.land, animal.clips.restDown, animal.clips.restSleep, animal.clips.restUp, rig.drink })
                if (clip) { Require(AssetDatabase.GetAssetPath(clip) == ModelPath, "Foreign/old skeleton clip: " + clip.name); ValidateBindings(clip, animal.animator.transform); }
            Require(!animal.GetComponentsInChildren<MonoBehaviour>(true).Any(c => c && c.GetType().Name == "SquirrelTailWave"), "Old SquirrelTailWave still exists.");
            var skins = animal.visualRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            Require(skins.Length == 3, "Expected three OGA skinned meshes.");
            var bones = BoneNames.Select(name => Named(animal.animator.transform, name)).ToArray();
            Require(bones.Length == 34 && skins.SelectMany(s => s.bones).All(t => t && bones.Contains(t)), "Skin bindings do not belong to the complete source 34-bone hierarchy.");
            foreach (var skin in skins) foreach (var material in skin.sharedMaterials)
                Require(material && ResolveColor(material) == material, "A renderer has a missing/foreign ordinary-color material.");
            Require(rig.headSkin.sharedMesh.GetBlendShapeIndex("EyesClosed") >= 0 && rig.head.IsChildOf(animal.animator.transform), "Invalid eye/head references.");
            Require(rig.HasAnimationBounds && rig.boundedSkins.All(skin => skin && skins.Contains(skin)), "Missing serialized animation bounds.");
            Require(rig.mouthVertexIndex >= 0 && Finite(rig.mouthHeadLocal) && Finite(rig.drinkRootContact) &&
                rig.mouthSurfaceMaxError >= 0 && rig.mouthSurfaceMaxError < SurfaceTolerance, "Uncalibrated mouth profile.");
            ValidateContactClone(animal, rig, report);
            report.contacts.Add(new ContactResult { animal = animal.name, scene = animal.gameObject.scene.path, vertex = rig.mouthVertexIndex,
                mouthHeadLocal = rig.mouthHeadLocal, drinkRootContact = rig.drinkRootContact, maxSurfaceError = rig.mouthSurfaceMaxError });
        }

        static void ValidateContactClone(WildlifeAgent animal, SquirrelOgaRig source, Report report)
        {
            var preview = EditorSceneManager.NewPreviewScene(); GameObject clone = null;
            try
            {
                clone = Object.Instantiate(animal.visualRoot.gameObject); clone.SetActive(false);
                SceneManager.MoveGameObjectToScene(clone, preview);
                var rig = clone.GetComponent<SquirrelOgaRig>();
                rig.ApplyAnimationBounds();
                var model = clone.GetComponentInChildren<Animator>(true).gameObject;
                var triangles = MuzzleTriangles(rig.headSkin);
                foreach (float time in DrinkSamples)
                {
                    source.drink.SampleAnimation(model, time);
                    ProbeSkin(rig.headSkin, animal.name + "/Drink validate " + time.ToString("F1", CultureInfo.InvariantCulture), report);
                    float distance = SurfaceDistance(rig.head.TransformPoint(source.mouthHeadLocal), BakeWorld(rig.headSkin), triangles);
                    Require(float.IsFinite(distance) && distance < SurfaceTolerance * MaxAxisScale(animal.transform.lossyScale),
                        "Actual sampled lip-to-Muzzle distance exceeds 1cm (scaled): " + animal.name + " at " + time + " = " + distance);
                }
            }
            finally { if (clone) Object.DestroyImmediate(clone); EditorSceneManager.ClosePreviewScene(preview); }
        }

        static bool IsCurrent(WildlifeAgent animal, Bundle bundle)
        {
            var rig = animal.visualRoot ? animal.visualRoot.GetComponent<SquirrelOgaRig>() : null;
            var animator = rig && rig.head ? rig.head.GetComponentInParent<Animator>() : null;
            return rig && rig.HasLifeClips && rig.HasAnimationBounds && rig.head && rig.headSkin && rig.modelDependencyHash == bundle.hash &&
                rig.idle == bundle.idle && rig.walk == bundle.walk && rig.run == bundle.run && rig.restDown == bundle.restDown &&
                rig.restLoop == bundle.restLoop && rig.restUp == bundle.restUp && rig.drink == bundle.drink && animator &&
                PrefabUtility.GetCorrespondingObjectFromOriginalSource(animator.gameObject) == bundle.model;
        }

        static bool BindingsCurrent(WildlifeAgent animal, Bundle bundle)
        {
            var rig = animal.visualRoot ? animal.visualRoot.GetComponent<SquirrelOgaRig>() : null;
            var animator = rig && rig.head ? rig.head.GetComponentInParent<Animator>() : null;
            var clips = animal.clips;
            return animator && animal.animator == animator && !animal.motionRoot && !animal.forageContactData && clips != null &&
                clips.idle == bundle.idle && clips.forage == bundle.idle && clips.walk == bundle.walk && clips.run == bundle.run &&
                clips.greet == bundle.idle && clips.alert == bundle.idle && !clips.takeoff && !clips.fly && !clips.land &&
                clips.restDown == bundle.restDown && clips.restSleep == bundle.restLoop && clips.restUp == bundle.restUp;
        }

        static void BindExistingVisual(WildlifeAgent animal, Bundle bundle)
        {
            var rig = animal.visualRoot.GetComponent<SquirrelOgaRig>();
            Require(rig && rig.head, "Cannot bind a missing OGA visual.");
            animal.animator = rig.head.GetComponentInParent<Animator>(); animal.motionRoot = null; animal.forageContactData = null;
            animal.clips = new WildlifeClips { idle = bundle.idle, forage = bundle.idle, walk = bundle.walk, run = bundle.run,
                greet = bundle.idle, alert = bundle.idle, restDown = bundle.restDown, restSleep = bundle.restLoop, restUp = bundle.restUp };
            EditorUtility.SetDirty(animal);
            if (PrefabUtility.IsPartOfPrefabInstance(animal)) PrefabUtility.RecordPrefabInstancePropertyModifications(animal);
        }

        static void PreflightAnimal(WildlifeAgent animal)
        {
            Require(animal.visualRoot || animal.transform.Find("Visual"), "No isolated Visual subtree exists: " + AnimalName(animal));
            // A larger parent prefab may inherit the corrected species prefab without being unpacked.
            // UnpackOnlyAnimalIfNeeded rejects that hierarchy only if an old local visual still needs replacement.
        }

        static void UnpackOnlyAnimalIfNeeded(WildlifeAgent animal)
        {
            if (!PrefabUtility.IsPartOfPrefabInstance(animal.gameObject)) return;
            var nearest = PrefabUtility.GetNearestPrefabInstanceRoot(animal.gameObject);
            Require(nearest == animal.gameObject, "Refused to unpack a parent/map prefab for " + AnimalName(animal));
            PrefabUtility.UnpackPrefabInstance(nearest, PrefabUnpackMode.OutermostRoot, InteractionMode.AutomatedAction);
        }

        static WildlifeAgent[] SceneAnimals() => Resources.FindObjectsOfTypeAll<WildlifeAgent>().Where(a => a && a.speciesId == "squirrel" &&
            !EditorUtility.IsPersistent(a) && a.gameObject.scene.IsValid() && a.gameObject.scene.isLoaded && !EditorSceneManager.IsPreviewScene(a.gameObject.scene)).ToArray();

        static string NonVisualSignature(WildlifeAgent animal)
        {
            var t = animal.transform;
            var lines = new List<string> { "name=" + animal.name, "parent=" + (t.parent ? t.parent.GetInstanceID() : 0),
                "sibling=" + t.GetSiblingIndex(), "position=" + Value(t.localPosition), "rotation=" + Value(t.localRotation),
                "scale=" + Value(t.localScale), "active=" + animal.gameObject.activeSelf, "enabled=" + animal.enabled,
                "layer=" + animal.gameObject.layer, "tag=" + animal.tag };
            foreach (var field in typeof(WildlifeAgent).GetFields(BindingFlags.Instance | BindingFlags.Public).OrderBy(f => f.Name))
                if (!VisualFields.Contains(field.Name)) lines.Add(field.Name + "=" + Value(field.GetValue(animal)));
            return string.Join("\n", lines);
        }

        static string Value(object value)
        {
            if (value == null) return "null";
            if (value is Object reference) return reference ? reference.GetInstanceID().ToString(CultureInfo.InvariantCulture) : "null";
            if (value is string text) return text;
            string F(float number) => number.ToString("R", CultureInfo.InvariantCulture);
            if (value is Vector3 v3) return F(v3.x) + "," + F(v3.y) + "," + F(v3.z);
            if (value is Vector2 v2) return F(v2.x) + "," + F(v2.y);
            if (value is Quaternion rotation) return F(rotation.x) + "," + F(rotation.y) + "," + F(rotation.z) + "," + F(rotation.w);
            if (value is float single) return F(single);
            if (value is bool boolean) return boolean ? "true" : "false";
            if (value is IFormattable number) return number.ToString(null, CultureInfo.InvariantCulture);
            return JsonUtility.ToJson(value);
        }

        static AnimationClip[] AllClips(Bundle bundle) => new[] { bundle.idle, bundle.walk, bundle.run, bundle.restDown, bundle.restLoop, bundle.restUp, bundle.drink };
        static string TakeName(string name) => name.Substring(name.LastIndexOf('|') + 1);
        static string ColorName(string name) => name.Replace(" (Instance)", "").Replace('_', ' ');
        static Material ResolveColor(Material source)
        {
            Require(source, "Missing material on OGA mesh.");
            string name = ColorName(source.name);
            Require(Colors.Any(c => name == "OGA Natural " + c), "Unknown OGA material: " + source.name);
            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialFolder + name.Replace(' ', '_') + ".mat");
            Require(material, "Missing material asset: " + name);
            Require((!material.HasProperty("_BaseMap") || !material.GetTexture("_BaseMap")) &&
                (!material.HasProperty("_MainTex") || !material.GetTexture("_MainTex")), "Ordinary-color material contains an unexpected texture: " + name);
            return material;
        }

        static void ValidateBindings(AnimationClip clip, Transform model)
        {
            Require(AssetDatabase.GetAssetPath(clip) == ModelPath, "Animation belongs to a foreign rig: " + clip.name);
            var bindings = AnimationUtility.GetCurveBindings(clip);
            Require(bindings.Length > 0, "Animation contains no readable curves: " + clip.name);
            foreach (var binding in bindings.Concat(AnimationUtility.GetObjectReferenceCurveBindings(clip)))
                Require(string.IsNullOrEmpty(binding.path) || model.Find(binding.path), "Unbound curve " + clip.name + " -> " + binding.path);
        }

        static Transform Named(Transform root, string name)
        {
            var found = root.GetComponentsInChildren<Transform>(true).Where(t => t.name == name).ToArray();
            Require(found.Length == 1, "Expected exactly one bone named " + name);
            return found[0];
        }

        static int[] MuzzleTriangles(SkinnedMeshRenderer skin)
        {
            var mesh = skin.sharedMesh; var triangles = new List<int>();
            Require(mesh && mesh.isReadable, "Head mesh must be readable for actual lip calibration.");
            for (int i = 0; i < skin.sharedMaterials.Length && i < mesh.subMeshCount; i++)
                if (skin.sharedMaterials[i] && ColorName(skin.sharedMaterials[i].name) == "OGA Natural Muzzle") triangles.AddRange(mesh.GetTriangles(i));
            Require(triangles.Count > 0 && triangles.Count % 3 == 0, "Head Muzzle submesh has no triangles.");
            return triangles.ToArray();
        }

        static Vector3[] BakeWorld(SkinnedMeshRenderer skin, bool useScale = true)
        {
            var mesh = new Mesh();
            try { skin.BakeMesh(mesh, useScale); return mesh.vertices.Select(v => skin.transform.TransformPoint(v)).ToArray(); }
            finally { Object.DestroyImmediate(mesh); }
        }

        static void ProbeSkin(SkinnedMeshRenderer skin, string scope, Report report)
        {
            var mesh = skin.sharedMesh;
            for (int i = 0; i < mesh.blendShapeCount; i++)
                Require(Mathf.Abs(skin.GetBlendShapeWeight(i)) < .00001f, "Manual base-vertex skin probe requires zero blend shapes: " + scope);
            bool previousRecalculation = skin.forceMatrixRecalculationPerRender;
            skin.forceMatrixRecalculationPerRender = true;
            try
            {
            var truePoints = BakeWorld(skin, true); var falsePoints = BakeWorld(skin, false);
            var vertices = mesh.vertices; var weights = mesh.boneWeights; var poses = mesh.bindposes; var bones = skin.bones;
            Require(vertices.Length == weights.Length && poses.Length == bones.Length, "Cannot independently skin imported vertex weights: " + scope);
            var matrices = bones.Select((bone, index) => bone.localToWorldMatrix * poses[index]).ToArray();
            var manual = new Vector3[vertices.Length];
            var probe = new SkinProbe { scope = scope, mesh = mesh.name, skinLossyScale = skin.transform.lossyScale,
                boundsRoot = skin.rootBone ? skin.rootBone.name : "NULL", animationLocalBounds = skin.localBounds };
            double trueSquares = 0d, falseSquares = 0d;
            for (int i = 0; i < vertices.Length; i++)
            {
                BoneWeight weight = weights[i];
                Require(Mathf.Abs(weight.weight0 + weight.weight1 + weight.weight2 + weight.weight3 - 1f) < .0001f, "Imported four-bone weights are not normalized: " + scope + " vertex " + i);
                manual[i] = matrices[weight.boneIndex0].MultiplyPoint3x4(vertices[i]) * weight.weight0 +
                    matrices[weight.boneIndex1].MultiplyPoint3x4(vertices[i]) * weight.weight1 +
                    matrices[weight.boneIndex2].MultiplyPoint3x4(vertices[i]) * weight.weight2 +
                    matrices[weight.boneIndex3].MultiplyPoint3x4(vertices[i]) * weight.weight3;
                float trueError = Vector3.Distance(manual[i], truePoints[i]), falseError = Vector3.Distance(manual[i], falsePoints[i]);
                probe.trueMaxError = Mathf.Max(probe.trueMaxError, trueError); probe.falseMaxError = Mathf.Max(probe.falseMaxError, falseError);
                trueSquares += trueError * trueError; falseSquares += falseError * falseError;
            }
            probe.trueRmsError = (float)Math.Sqrt(trueSquares / vertices.Length); probe.falseRmsError = (float)Math.Sqrt(falseSquares / vertices.Length);
            probe.trueWorldBounds = PointBounds(truePoints); probe.falseWorldBounds = PointBounds(falsePoints); probe.manualWorldBounds = PointBounds(manual);
            probe.rendererWorldBounds = skin.bounds;
            var paddedRenderer = probe.rendererWorldBounds; paddedRenderer.Expand(.004f);
            probe.rendererContainsManual = paddedRenderer.Contains(probe.manualWorldBounds.min) && paddedRenderer.Contains(probe.manualWorldBounds.max);
            probe.rendererConservativeDifference = Vector3.Distance(probe.rendererWorldBounds.center, probe.manualWorldBounds.center) > .01f ||
                Vector3.Distance(probe.rendererWorldBounds.size, probe.manualWorldBounds.size) > Mathf.Max(.01f, probe.manualWorldBounds.size.magnitude * .1f);
            report.skinProbes.Add(probe);
            Require(Finite(probe.manualWorldBounds.size) && float.IsFinite(probe.trueMaxError) && probe.trueMaxError < .001f,
                "BakeMesh(true)+TransformPoint differs from independent bone*bindpose skinning by >=1mm: " + scope + "; true=" + probe.trueMaxError + "; false=" + probe.falseMaxError);
            Require(probe.rendererContainsManual, "Animation bounds exclude independently skinned geometry: " + scope);
            }
            finally { skin.forceMatrixRecalculationPerRender = previousRecalculation; }
        }

        static void FitAnimationBounds(GameObject model, Bundle bundle)
        {
            var skins = model.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            foreach (var skin in skins) Require(skin.rootBone, "OGA skin must have an explicit bounds root bone: " + skin.name);
            var bounds = new Bounds[skins.Length];
            var initialized = new bool[skins.Length];
            foreach (var clip in AllClips(bundle))
                for (int sample = 0; sample <= 16; sample++)
                {
                    clip.SampleAnimation(model, clip.length * sample / 16f);
                    for (int i = 0; i < skins.Length; i++)
                        foreach (var world in BakeWorld(skins[i]))
                        {
                            // Unity's skinned localBounds follow rootBone, while BakeMesh vertices use the renderer transform.
                            var local = skins[i].rootBone.InverseTransformPoint(world);
                            if (!initialized[i]) { bounds[i] = new Bounds(local, Vector3.zero); initialized[i] = true; }
                            else bounds[i].Encapsulate(local);
                        }
                }
            for (int i = 0; i < skins.Length; i++)
            {
                Require(initialized[i], "Cannot bound an empty animated skin.");
                Vector3 scale = skins[i].rootBone.lossyScale;
                // Expand changes total size; 0.08 gives 0.04m on each side for this orthogonal model hierarchy.
                bounds[i].Expand(new Vector3(.08f / Mathf.Max(.00001f, Mathf.Abs(scale.x)),
                    .08f / Mathf.Max(.00001f, Mathf.Abs(scale.y)), .08f / Mathf.Max(.00001f, Mathf.Abs(scale.z))));
                skins[i].localBounds = bounds[i];
            }
        }

        static Bounds PointBounds(Vector3[] points)
        {
            Require(points.Length > 0, "No points for measured bounds.");
            var bounds = new Bounds(points[0], Vector3.zero);
            for (int i = 1; i < points.Length; i++) bounds.Encapsulate(points[i]);
            return bounds;
        }

        static Bounds LocalBounds(GameObject model, Transform root)
        {
            bool any = false; Bounds bounds = default;
            foreach (var skin in model.GetComponentsInChildren<SkinnedMeshRenderer>(true)) foreach (var world in BakeWorld(skin))
            {
                Vector3 point = root.InverseTransformPoint(world);
                if (!any) { bounds = new Bounds(point, Vector3.zero); any = true; } else bounds.Encapsulate(point);
            }
            Require(any, "No skinned vertices to measure."); return bounds;
        }

        static float SurfaceDistance(Vector3 point, Vector3[] vertices, int[] triangles)
        {
            float minimum = float.PositiveInfinity;
            for (int i = 0; i < triangles.Length; i += 3)
                minimum = Mathf.Min(minimum, TriangleDistance(point, vertices[triangles[i]], vertices[triangles[i + 1]], vertices[triangles[i + 2]]));
            return minimum;
        }

        static float TriangleDistance(Vector3 point, Vector3 a, Vector3 b, Vector3 c)
        {
            Vector3 ab = b - a, ac = c - a, normal = Vector3.Cross(ab, ac);
            if (normal.sqrMagnitude > 1e-16f)
            {
                Vector3 projected = point - normal * (Vector3.Dot(point - a, normal) / normal.sqrMagnitude);
                float d00 = Vector3.Dot(ab, ab), d01 = Vector3.Dot(ab, ac), d11 = Vector3.Dot(ac, ac);
                float d20 = Vector3.Dot(projected - a, ab), d21 = Vector3.Dot(projected - a, ac), denominator = d00 * d11 - d01 * d01;
                float u = (d11 * d20 - d01 * d21) / denominator, v = (d00 * d21 - d01 * d20) / denominator;
                if (u >= 0f && v >= 0f && u + v <= 1f) return Vector3.Distance(point, projected);
            }
            return Mathf.Min(SegmentDistance(point, a, b), Mathf.Min(SegmentDistance(point, b, c), SegmentDistance(point, c, a)));
        }

        static float SegmentDistance(Vector3 p, Vector3 a, Vector3 b)
        {
            Vector3 edge = b - a; float length = edge.sqrMagnitude;
            return Vector3.Distance(p, a + edge * (length > 0f ? Mathf.Clamp01(Vector3.Dot(p - a, edge) / length) : 0f));
        }

        static void ValidateGeometryMath()
        {
            Vector3 a = Vector3.zero, b = Vector3.right, c = Vector3.forward;
            Require(TriangleDistance(new Vector3(.2f, 0f, .2f), a, b, c) < .00001f, "Surface-distance self-check: triangle interior.");
            Require(Mathf.Abs(TriangleDistance(new Vector3(.2f, .3f, .2f), a, b, c) - .3f) < .00001f, "Surface-distance self-check: normal projection.");
            Require(Mathf.Abs(TriangleDistance(new Vector3(-1f, 0f, -1f), a, b, c) - Mathf.Sqrt(2f)) < .00001f, "Surface-distance self-check: nearest vertex.");
            Require(Mathf.Abs(TriangleDistance(new Vector3(.5f, 0f, -1f), a, b, c) - 1f) < .00001f, "Surface-distance self-check: nearest edge.");
            Require(Mathf.Abs(TriangleDistance(Vector3.up, a, a, a) - 1f) < .00001f, "Surface-distance self-check: degenerate face.");
        }

        static bool Finite(Vector3 value) => float.IsFinite(value.x) && float.IsFinite(value.y) && float.IsFinite(value.z);
        static float MaxAxisScale(Vector3 value) => Mathf.Max(Mathf.Abs(value.x), Mathf.Max(Mathf.Abs(value.y), Mathf.Abs(value.z)));
        static string AnimalName(WildlifeAgent animal) => animal.gameObject.scene.path + "/" + animal.name + " (spawn " + animal.spawnId + ")";
        static void RequireEditMode() => Require(!EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isCompiling, "Install only outside Play and after compilation.");
        static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        static Report NewReport(string operation) => new Report { operation = operation, utc = DateTime.UtcNow.ToString("O"), savedScene = false };
        static string WriteReport(Report report)
        {
            Directory.CreateDirectory(ReportFolder);
            string path = Path.GetFullPath(Path.Combine(ReportFolder, report.operation + "-latest.json"));
            File.WriteAllText(path, JsonUtility.ToJson(report, true));
            Debug.Log("OGA squirrel " + report.status + ": " + path);
            return path;
        }
    }
}
