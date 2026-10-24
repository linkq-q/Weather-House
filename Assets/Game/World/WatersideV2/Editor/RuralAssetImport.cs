using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace WeatherHouse.WatersideV2.Editor
{
    // Staged source: install only in the coordinated integration window.
    public static class RuralAssetImport
    {
        public const string TargetRoot = "Assets/Environment/RuralRebuild20261025";
        public const string DefaultSource = "JourneyReview/Revision_20261024/MapIntegrationPlan/staging/Models";
        const string OwnershipFile = ".rural-rebuild-owned";
        const string Ownership = "RuralRebuild20261025/schema1";
        public static readonly string[] Ids = { "A01", "A02", "A03", "A04", "A05", "A06", "A07", "A08", "A09", "B01", "B02", "P01", "P02", "P03", "P04", "P05", "P06" };

        [Serializable] public sealed class FileEntry { public string path, sha256; }
        [Serializable] public sealed class MaterialEntry
        {
            public string name, baseColorTexture, metallicSmoothnessTexture, normalTexture, emissionTexture, occlusionTexture, alphaMode;
            public float[] baseColorFactor, emissionFactor;
            public float metallicFactor, roughnessFactor, smoothness, opacity = 1, alphaCutoff = .5f, normalScale = 1;
            public bool isGlass, doubleSided, packedFactorsBaked;
        }
        [Serializable] public sealed class CollisionEntry { public string name, sourceName; public float[] center, size, rotation; }
        [Serializable] public sealed class BoundsEntry { public float[] min, max, dimensions; }
        [Serializable] public sealed class Manifest
        {
            public int schemaVersion;
            public string id, name, sourcePath, sourceSha256;
            public string[] lods;
            public MaterialEntry[] materials;
            public CollisionEntry[] colliders;
            public FileEntry[] files;
            public BoundsEntry bounds;
        }

        /// <summary>Pure file checks; no import, scene access, refresh, or writes.</summary>
        public static string[] PreflightAll(string sourceRoot = null)
        {
            sourceRoot = sourceRoot ?? DefaultSource;
            foreach (string id in Ids)
            {
                Manifest manifest = ReadAndValidate(Path.Combine(sourceRoot, id), id);
                if (!File.Exists(manifest.sourcePath) || !string.Equals(Hash(manifest.sourcePath), manifest.sourceSha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Source provenance hash mismatch: " + id);
            }
            return Ids.Select(PrefabPath).ToArray();
        }

        /// <summary>Copies only the verified manifest payload into this importer-owned folder.</summary>
        public static void StageAll(string sourceRoot = null)
        {
            RequireEditMode();
            sourceRoot = sourceRoot ?? DefaultSource;
            PreflightAll(sourceRoot);
            string owner = Path.Combine(TargetRoot, OwnershipFile);
            if (Directory.Exists(TargetRoot) && Directory.EnumerateFileSystemEntries(TargetRoot).Any() &&
                (!File.Exists(owner) || File.ReadAllText(owner) != Ownership))
                throw new InvalidOperationException("Refusing unowned destination: " + TargetRoot);
            Directory.CreateDirectory(TargetRoot);
            File.WriteAllText(owner, Ownership);
            foreach (string id in Ids)
            {
                string from = Path.Combine(sourceRoot, id), to = TargetRoot + "/" + id;
                Manifest manifest = ReadAndValidate(from, id);
                Directory.CreateDirectory(to);
                foreach (FileEntry file in manifest.files)
                {
                    string destination = LocalPath(to, file.path);
                    Directory.CreateDirectory(Path.GetDirectoryName(destination));
                    CopyIfChanged(LocalPath(from, file.path), destination);
                }
                CopyIfChanged(Path.Combine(from, "import.json"), Path.Combine(to, "import.json"));
            }
            AssetDatabase.ImportAsset(TargetRoot, ImportAssetOptions.ImportRecursive | ImportAssetOptions.ForceSynchronousImport);
        }

        public static string[] BuildAll(string sourceRoot = null)
        {
            StageAll(sourceRoot);
            return Ids.Select(BuildPrefab).ToArray();
        }

        public static string PrefabPath(string id)
        {
            RequireId(id);
            return TargetRoot + "/Prefabs/" + id + ".prefab";
        }

        public static string BuildPrefab(string id)
        {
            RequireEditMode();
            RequireId(id);
            if (!File.Exists(TargetRoot + "/" + OwnershipFile) || File.ReadAllText(TargetRoot + "/" + OwnershipFile) != Ownership)
                throw new InvalidOperationException("StageAll must establish folder ownership first.");
            string folder = TargetRoot + "/" + id;
            Manifest manifest = ReadAndValidate(folder, id);
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (!shader) throw new InvalidOperationException("URP Lit is unavailable.");
            EnsureAssetFolder(folder + "/Materials");
            EnsureAssetFolder(TargetRoot + "/Prefabs");
            var materials = manifest.materials.Select((m, index) => ImportMaterial(folder, m, index, shader)).ToArray();
            var glass = new HashSet<Material>(materials.Where((m, index) => manifest.materials[index].isGlass));
            var preview = EditorSceneManager.NewPreviewScene();
            var root = new GameObject("Rural_" + id + "_" + manifest.name);
            SceneManager.MoveGameObjectToScene(root, preview);
            try
            {
                var lods = new LOD[3];
                float[] heights = { .35f, .12f, .008f };
                for (int level = 0; level < 3; level++)
                {
                    string path = AssetPath(folder, manifest.lods[level]);
                    AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                    var importer = AssetImporter.GetAtPath(path) as ModelImporter;
                    if (!importer) throw new InvalidOperationException("Missing model importer: " + path);
                    importer.importAnimation = false;
                    importer.importCameras = false;
                    importer.importLights = false;
                    importer.addCollider = false;
                    importer.isReadable = false;
                    importer.globalScale = 1;
                    importer.useFileScale = true;
                    importer.importNormals = ModelImporterNormals.Import;
                    importer.importTangents = ModelImporterTangents.CalculateMikk;
                    importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
                    for (int m = 0; m < materials.Length; m++)
                        importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), manifest.materials[m].name), materials[m]);
                    importer.SaveAndReimport();
                    GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    if (!model) throw new InvalidOperationException("No imported model: " + path);
                    var instance = (GameObject)PrefabUtility.InstantiatePrefab(model, preview);
                    instance.name = "LOD" + level;
                    instance.transform.SetParent(root.transform, false);
                    Renderer[] renderers = instance.GetComponentsInChildren<Renderer>(true);
                    if (renderers.Length == 0 || renderers.Any(r => r.sharedMaterials.Length == 0 || r.sharedMaterials.Any(m => !m || !materials.Contains(m))))
                        throw new InvalidOperationException("Unmapped material: " + id + " LOD" + level);
                    if (instance.GetComponentsInChildren<MeshFilter>(true).Any(f => !f.sharedMesh || f.sharedMesh.vertexCount == 0))
                        throw new InvalidOperationException("Empty mesh: " + id + " LOD" + level);
                    if (level == 0)
                    {
                        Bounds actual = renderers[0].bounds;
                        foreach (Renderer renderer in renderers.Skip(1)) actual.Encapsulate(renderer.bounds);
                        Vector3 expected = Vector(manifest.bounds.dimensions);
                        for (int axis = 0; axis < 3; axis++)
                            if (Mathf.Abs(actual.size[axis] - expected[axis]) > Mathf.Max(.05f, expected[axis] * .02f))
                                throw new InvalidOperationException("Unity FBX scale/axis mismatch: " + id + " axis " + axis);
                    }
                    foreach (Renderer renderer in renderers)
                    {
                        bool hasGlass = renderer.sharedMaterials.Any(glass.Contains);
                        if (hasGlass && !renderer.sharedMaterials.All(glass.Contains))
                            throw new InvalidOperationException("Exporter must split glass and opaque mesh nodes: " + renderer.name);
                        renderer.shadowCastingMode = hasGlass ? ShadowCastingMode.Off : ShadowCastingMode.On;
                        renderer.receiveShadows = !hasGlass;
                    }
                    lods[level] = new LOD(heights[level], renderers);
                }
                var collisionRoot = new GameObject("StructuralColliders");
                collisionRoot.transform.SetParent(root.transform, false);
                foreach (CollisionEntry entry in manifest.colliders)
                {
                    var node = new GameObject(entry.name);
                    node.transform.SetParent(collisionRoot.transform, false);
                    node.transform.localPosition = Vector(entry.center);
                    if (entry.rotation != null)
                        node.transform.localRotation = new Quaternion(entry.rotation[0], entry.rotation[1], entry.rotation[2], entry.rotation[3]);
                    BoxCollider box = node.AddComponent<BoxCollider>();
                    box.center = Vector3.zero;
                    box.size = Vector(entry.size);
                }
                var group = root.AddComponent<LODGroup>();
                group.SetLODs(lods);
                group.RecalculateBounds();
                if(id=="A06")RuralA06EntrySteps.Ensure(root.transform);
                string prefab = PrefabPath(id);
                if (!PrefabUtility.SaveAsPrefabAsset(root, prefab)) throw new InvalidOperationException("Failed to save " + prefab);
                return prefab;
            }
            finally
            {
                Object.DestroyImmediate(root);
                EditorSceneManager.ClosePreviewScene(preview);
            }
        }

        static Material ImportMaterial(string folder, MaterialEntry entry, int index, Shader shader)
        {
            // Numeric filenames keep arbitrary FBX material names out of filesystem paths.
            string path = folder + "/Materials/M" + index.ToString("D3") + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!material) { material = new Material(shader); AssetDatabase.CreateAsset(material, path); }
            material.shader = shader;
            material.name = Path.GetFileNameWithoutExtension(path);
            material.shaderKeywords = Array.Empty<string>();
            var factor = entry.baseColorFactor;
            Color color = new Color(factor[0], factor[1], factor[2], factor[3]);
            bool transparent = entry.isGlass || entry.alphaMode == "BLEND";
            bool cutout = !transparent && entry.alphaMode == "MASK";
            // URP alpha glass is a deliberate light-through approximation of glTF transmission.
            if (entry.isGlass) color.a = entry.opacity;
            material.SetColor("_BaseColor", color);
            material.SetTexture("_BaseMap", Texture(folder, entry.baseColorTexture, true, false));
            material.SetTexture("_MetallicGlossMap", Texture(folder, entry.metallicSmoothnessTexture, false, false));
            material.SetTexture("_BumpMap", Texture(folder, entry.normalTexture, false, true));
            material.SetTexture("_EmissionMap", Texture(folder, entry.emissionTexture, true, false));
            float[] emission = entry.emissionFactor;
            material.SetColor("_EmissionColor", emission == null ? Color.black : new Color(emission[0], emission[1], emission[2]));
            material.SetFloat("_Metallic", entry.metallicFactor);
            material.SetFloat("_Smoothness", string.IsNullOrEmpty(entry.metallicSmoothnessTexture) ? entry.smoothness : 1);
            material.SetFloat("_SmoothnessTextureChannel", 0);
            material.SetFloat("_WorkflowMode", 1);
            material.SetFloat("_BumpScale", entry.normalScale);
            material.SetFloat("_Cull", entry.doubleSided ? 0 : 2);
            material.SetFloat("_Surface", transparent ? 1 : 0);
            material.SetFloat("_Blend", 0);
            material.SetFloat("_AlphaClip", cutout ? 1 : 0);
            material.SetFloat("_Cutoff", entry.alphaCutoff);
            material.SetFloat("_SrcBlend", transparent ? (float)BlendMode.SrcAlpha : (float)BlendMode.One);
            material.SetFloat("_DstBlend", transparent ? (float)BlendMode.OneMinusSrcAlpha : (float)BlendMode.Zero);
            material.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            material.SetFloat("_DstBlendAlpha", transparent ? (float)BlendMode.OneMinusSrcAlpha : (float)BlendMode.Zero);
            material.SetFloat("_ZWrite", transparent ? 0 : 1);
            material.SetFloat("_ReceiveShadows", entry.isGlass ? 0 : 1);
            material.SetOverrideTag("RenderType", transparent ? "Transparent" : cutout ? "TransparentCutout" : "Opaque");
            material.renderQueue = transparent ? (int)RenderQueue.Transparent : cutout ? (int)RenderQueue.AlphaTest : (int)RenderQueue.Geometry;
            material.SetShaderPassEnabled("ShadowCaster", !transparent);
            material.SetShaderPassEnabled("DepthOnly", !transparent);
            Keyword(material, "_SURFACE_TYPE_TRANSPARENT", transparent);
            Keyword(material, "_ALPHATEST_ON", cutout);
            Keyword(material, "_RECEIVE_SHADOWS_OFF", entry.isGlass);
            Keyword(material, "_METALLICSPECGLOSSMAP", !string.IsNullOrEmpty(entry.metallicSmoothnessTexture));
            Keyword(material, "_NORMALMAP", !string.IsNullOrEmpty(entry.normalTexture));
            Keyword(material, "_EMISSION", emission != null && emission.Any(v => v > 0));
            material.enableInstancing = true;
            EditorUtility.SetDirty(material);
            AssetDatabase.SaveAssetIfDirty(material);
            return material;
        }

        static Texture2D Texture(string folder, string relative, bool srgb, bool normal)
        {
            if (string.IsNullOrEmpty(relative)) return null;
            string path = AssetPath(folder, relative);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (!importer) throw new InvalidOperationException("Missing texture importer: " + path);
            importer.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            importer.sRGBTexture = srgb;
            importer.mipmapEnabled = true;
            importer.maxTextureSize = 2048;
            importer.textureCompression = TextureImporterCompression.Compressed;
            importer.SaveAndReimport();
            Texture2D result = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (!result) throw new InvalidOperationException("Texture failed import: " + path);
            return result;
        }

        static Manifest ReadAndValidate(string folder, string id)
        {
            RequireId(id);
            string manifestPath = Path.Combine(folder, "import.json");
            Manifest manifest = JsonUtility.FromJson<Manifest>(File.ReadAllText(manifestPath));
            if (manifest == null || manifest.schemaVersion != 1 || manifest.id != id || string.IsNullOrEmpty(manifest.name))
                throw new InvalidDataException("Invalid manifest identity: " + manifestPath);
            if (manifest.lods == null || manifest.lods.Length != 3 || manifest.lods.Distinct().Count() != 3 ||
                manifest.lods.Any(p => !p.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException("Three distinct FBX LODs required: " + id);
            if (manifest.materials == null || manifest.materials.Length == 0 || manifest.colliders == null || manifest.files == null || manifest.files.Length == 0)
                throw new InvalidDataException("Missing materials/collision metadata/file hashes: " + id);
            if (id.StartsWith("A", StringComparison.Ordinal) && manifest.colliders.Length == 0)
                throw new InvalidDataException("Building requires entrance-aware wall/post collision boxes before integration: " + id);
            if (manifest.bounds == null || !ValidVector(manifest.bounds.dimensions, 3) || manifest.bounds.dimensions.Any(v => v <= 0))
                throw new InvalidDataException("Expected Unity XYZ dimensions required: " + id);
            if (manifest.materials.Select(m => m.name).Distinct().Count() != manifest.materials.Length)
                throw new InvalidDataException("Duplicate FBX material names: " + id);
            var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (FileEntry file in manifest.files)
            {
                string path = LocalPath(folder, file.path);
                if (!new[] { ".fbx", ".png" }.Contains(Path.GetExtension(path).ToLowerInvariant()) ||
                    !files.Add(file.path.Replace('\\', '/')) || !File.Exists(path) || !IsHash(file.sha256) ||
                    !string.Equals(Hash(path), file.sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Missing/duplicate/hash-mismatched payload: " + path);
            }
            if (!IsHash(manifest.sourceSha256)) throw new InvalidDataException("Missing source provenance hash: " + id);
            foreach (string lod in manifest.lods) RequireListed(files, lod);
            foreach (MaterialEntry entry in manifest.materials)
            {
                if (string.IsNullOrWhiteSpace(entry.name) || !ValidVector(entry.baseColorFactor, 4) ||
                    entry.baseColorFactor.Any(v => v < 0 || v > 1) || !Unit(entry.metallicFactor) || !Unit(entry.roughnessFactor) || !Unit(entry.smoothness))
                    throw new InvalidDataException("Invalid material factors: " + id + "/" + entry.name);
                if (!string.IsNullOrEmpty(entry.alphaMode) && !new[] { "OPAQUE", "MASK", "BLEND" }.Contains(entry.alphaMode))
                    throw new InvalidDataException("Unknown alpha mode: " + entry.alphaMode);
                if (!Unit(entry.opacity) || !Unit(entry.alphaCutoff) || (entry.isGlass && (entry.opacity <= 0 || entry.opacity > .3f)))
                    throw new InvalidDataException("Invalid opacity/cutoff; glass needs explicit light-through opacity: " + entry.name);
                if (!string.IsNullOrEmpty(entry.metallicSmoothnessTexture) && !entry.packedFactorsBaked)
                    throw new InvalidDataException("Packed metallic/smoothness must include glTF factors: " + entry.name);
                if (!string.IsNullOrEmpty(entry.occlusionTexture))
                    throw new InvalidDataException("Occlusion requires R-to-G conversion before URP import: " + entry.name);
                if (entry.emissionFactor != null && (!ValidVector(entry.emissionFactor, 3) || entry.emissionFactor.Any(v => v < 0)))
                    throw new InvalidDataException("Invalid emission factor: " + entry.name);
                if (float.IsNaN(entry.normalScale) || float.IsInfinity(entry.normalScale))
                    throw new InvalidDataException("Invalid normal scale: " + entry.name);
                foreach (string texture in new[] { entry.baseColorTexture, entry.metallicSmoothnessTexture, entry.normalTexture, entry.emissionTexture })
                    if (!string.IsNullOrEmpty(texture)) RequireListed(files, texture);
            }
            foreach (CollisionEntry box in manifest.colliders)
            {
                if (string.IsNullOrWhiteSpace(box.name) || !ValidVector(box.center, 3) || !ValidVector(box.size, 3) || box.size.Any(v => v <= 0))
                    throw new InvalidDataException("Invalid structural collision box: " + id);
                if (box.rotation != null && (!ValidVector(box.rotation, 4) || Math.Abs(box.rotation.Sum(v => v * v) - 1) > .001f))
                    throw new InvalidDataException("Collision rotation must be a unit quaternion: " + id + "/" + box.name);
            }
            return manifest;
        }

        static void RequireListed(HashSet<string> files, string path)
        { if (string.IsNullOrEmpty(path) || !files.Contains(path.Replace('\\', '/'))) throw new InvalidDataException("Unhashed referenced payload: " + path); }
        static bool ValidVector(float[] values, int length)
        { return values != null && values.Length == length && values.All(v => !float.IsNaN(v) && !float.IsInfinity(v)); }
        static bool Unit(float v) { return !float.IsNaN(v) && v >= 0 && v <= 1; }
        static Vector3 Vector(float[] v) { return new Vector3(v[0], v[1], v[2]); }
        static bool IsHash(string value) { return value != null && value.Length == 64 && value.All(Uri.IsHexDigit); }
        static string Hash(string path)
        { using (var stream = File.OpenRead(path)) using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", ""); }
        static void CopyIfChanged(string source, string target)
        { if (!File.Exists(target) || Hash(source) != Hash(target)) File.Copy(source, target, true); }
        static void RequireId(string id)
        { if (!Ids.Contains(id)) throw new ArgumentException("Not one of the 17 approved assets: " + id); }
        static void RequireEditMode()
        { if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) throw new InvalidOperationException("Wait for edit mode with compilation complete."); }
        static string LocalPath(string folder, string relative)
        {
            if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative)) throw new InvalidDataException("Expected local payload path.");
            string root = Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string path = Path.GetFullPath(Path.Combine(root, relative));
            if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Payload escapes its asset folder: " + relative);
            return path;
        }
        static string AssetPath(string folder, string relative)
        { LocalPath(folder, relative); return (folder + "/" + relative).Replace('\\', '/'); }
        static void EnsureAssetFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureAssetFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
        static void Keyword(Material material, string keyword, bool enabled)
        { if (enabled) material.EnableKeyword(keyword); else material.DisableKeyword(keyword); }
    }
}
