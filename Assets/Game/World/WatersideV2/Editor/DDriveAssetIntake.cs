using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace WeatherHouse.WatersideV2.Editor
{
    /// <summary>C-cache-only intake. Commands are separate from scene construction.</summary>
    [InitializeOnLoad]
    public static class DDriveAssetIntake
    {
        const string Root = "Assets/Environment/DDriveLibrary";
        const string Cache = "Temp/AssetIntake/staged";
        const string Review = "AssetIntakeReview";
        const string ManifestPath = Review + "/import-manifest.json";
        const string ReceiptPath = Review + "/import-receipt.json";
        static double nextPoll;
        static bool busy;

        [Serializable] sealed class Manifest { public string root; public Pack[] packs; public Entry[] files; }
        [Serializable] sealed class Pack { public string key; public int files; }
        [Serializable] sealed class Entry { public string path, guid, sha256; public long size; }
        [Serializable] sealed class Receipt { public string root, manifestHash; }
        [Serializable] public sealed class Issue { public string severity, path, message; }
        [Serializable] public sealed class PreviewItem { public string pack, asset, image, note; }
        [Serializable] public sealed class Report
        {
            public string command, status, startedUtc, finishedUtc;
            public int filesCopied, filesRetained, adaptedMaterials, nativeMaterials;
            public int assets, prefabs, models, meshes, materials, sourceEmbeddedMaterials, textures, shaders, terrainLayers, animationClips;
            public List<Issue> issues = new List<Issue>();
            public List<PreviewItem> previews = new List<PreviewItem>();
        }

        static DDriveAssetIntake() { EditorApplication.update += Poll; }
        static void Poll()
        {
            if (busy || EditorApplication.timeSinceStartup < nextPoll || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            nextPoll = EditorApplication.timeSinceStartup + .5;
            string request = Review + "/request.txt";
            if (!File.Exists(request)) return;
            try
            {
                string command = File.ReadAllText(request).Trim().ToLowerInvariant();
                File.Delete(request); Run(command);
            }
            catch (Exception error) { Debug.LogException(error); }
        }

        public static Report Run(string command)
        {
            var report = new Report { command = command, status = "RUNNING", startedUtc = DateTime.UtcNow.ToString("O") };
            if (busy) throw new InvalidOperationException("Asset intake is already running.");
            busy = true;
            Scene active = SceneManager.GetActiveScene();
            var sceneStates = Enumerable.Range(0, SceneManager.sceneCount).Select(i => SceneManager.GetSceneAt(i))
                .ToDictionary(s => s.handle, s => s.isDirty);
            try
            {
                Directory.CreateDirectory(Review);
                File.WriteAllText(Review + "/status.txt", "RUNNING " + command + " " + report.startedUtc);
                Require(!EditorApplication.isPlayingOrWillChangePlaymode, "Asset intake refuses Play mode and Play transitions.");
                Require(!EditorApplication.isCompiling && !EditorApplication.isUpdating, "Wait for Unity compilation/import to finish.");
                Manifest manifest = ReadManifest();
                switch (command)
                {
                    case "import": Import(manifest, report); break;
                    case "adapt": RequireReceipt(); Adapt(manifest, report); break;
                    case "verify": RequireReceipt(); Verify(manifest, report); break;
                    case "preview": RequireReceipt(); Preview(manifest, report); break;
                    case "diagnose": RequireReceipt(); Diagnose(manifest, report); break;
                    case "dreamscapes": RequireReceipt(); Add(report, "info", Root + "/Dreamscapes", DreamscapesLegacyRepair.Run()); break;
                    case "terrain-apply": Add(report, "info", "terrain", WatersideTerrainMaterialAB.Apply()); break;
                    case "terrain-restore": Add(report, "info", "terrain", WatersideTerrainMaterialAB.Restore()); break;
                    default: throw new InvalidOperationException("Use import, adapt, verify or preview.");
                }
            }
            catch (Exception error) { Add(report, "error", command, error.ToString()); Debug.LogException(error); }
            finally
            {
                busy = false;
                var finalScenes = Enumerable.Range(0, SceneManager.sceneCount).Select(i => SceneManager.GetSceneAt(i)).ToDictionary(s => s.handle,s => s.isDirty);
                if (SceneManager.GetActiveScene().handle != active.handle || finalScenes.Count != sceneStates.Count ||
                    sceneStates.Any(s => !finalScenes.ContainsKey(s.Key) || finalScenes[s.Key] != s.Value))
                    Add(report, "error", "scene", "Active scene, loaded scenes or scene dirty flags changed during intake.");
                report.status = report.issues.Any(i => i.severity == "error") ? "FAIL" : "PASS";
                report.finishedUtc = DateTime.UtcNow.ToString("O");
                Directory.CreateDirectory(Review);
                string file = new[] { "import", "adapt", "verify", "preview", "diagnose", "dreamscapes" }.Contains(command) ? command : "command";
                File.WriteAllText(Review + "/" + file + "-report.json", JsonUtility.ToJson(report, true));
                File.WriteAllText(Review + "/status.txt", report.status + " " + command + " " + report.finishedUtc +
                    "\nerrors=" + report.issues.Count(i => i.severity == "error") + " warnings=" + report.issues.Count(i => i.severity == "warning"));
            }
            return report;
        }

        static Manifest ReadManifest()
        {
            var manifest = JsonUtility.FromJson<Manifest>(File.ReadAllText(ManifestPath));
            Require(manifest != null && manifest.root == Root && manifest.files != null && manifest.files.Length > 0, "Unexpected intake root or empty manifest.");
            Require(manifest.packs != null && manifest.packs.All(p => !string.IsNullOrEmpty(p.key) && !p.key.Contains("/") && !p.key.Contains("\\") && p.key != ".."), "Invalid pack keys.");
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase); var guids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Entry entry in manifest.files)
            {
                AssetPath(entry.path);
                Require(paths.Add(entry.path) && Regex.IsMatch(entry.guid ?? "", "^[0-9a-fA-F]{32}$") && guids.Add(entry.guid), "Duplicate path/GUID or malformed GUID: " + entry.path);
                Require(Regex.IsMatch(entry.sha256 ?? "", "^[0-9a-fA-F]{64}$") && entry.size >= 0, "Missing hash/size: " + entry.path);
                string extension = Path.GetExtension(entry.path).ToLowerInvariant();
                Require(!new[] { ".cs", ".dll", ".exe", ".unity", ".asmdef", ".asmref", ".meta" }.Contains(extension), "Scripts, executables, scenes and standalone metas are outside intake scope: " + entry.path);
            }
            return manifest;
        }

        static void Diagnose(Manifest manifest, Report report)
        {
            foreach (Entry entry in manifest.files.Where(e => e.path.EndsWith("/tree01.prefab") || e.path.EndsWith("/pebbles01.prefab") || e.path.EndsWith("/SM_Env_Branch_03_Convex.asset")))
            {
                AssetDatabase.ImportAsset(entry.path, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
                Object main = AssetDatabase.LoadMainAssetAtPath(entry.path);
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(entry.path);
                string detail = "main=" + (main ? main.GetType().Name : "null") + "; prefab=" + (prefab ? prefab.name : "null");
                foreach (Object item in AssetDatabase.LoadAllAssetsAtPath(entry.path))
                    detail += "\n" + item.GetType().Name + " " + item.name + (item is Mesh mesh ? " vertices=" + mesh.vertexCount : "") + (item is Material mat ? " shader=" + (mat.shader ? mat.shader.name : "null") : "");
                Add(report, "info", entry.path, detail);
            }
        }

        static string AssetPath(string path)
        {
            Require(path != null && path.StartsWith(Root + "/", StringComparison.Ordinal) && !path.Contains("\\") && !Path.IsPathRooted(path), "Out-of-root asset path: " + path);
            string full = Path.GetFullPath(path), prefix = Path.GetFullPath(Root) + Path.DirectorySeparatorChar;
            Require(full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase), "Path escapes intake root: " + path);
            for (var directory = new DirectoryInfo(Path.GetDirectoryName(full)); directory != null && directory.FullName.Length >= prefix.Length - 1; directory = directory.Parent)
                if (directory.Exists) Require((directory.Attributes & FileAttributes.ReparsePoint) == 0, "Intake does not follow directory links: " + directory.FullName);
            return full;
        }
        static string Hash(string path)
        {
            using (var stream = File.OpenRead(path)) using (var hash = SHA256.Create())
                return BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        }
        static string MetaGuid(string path)
        {
            Require(File.Exists(path), "Missing meta: " + path);
            Match match = Regex.Match(File.ReadAllText(path), @"(?m)^guid:\s*([0-9a-fA-F]{32})\s*$");
            Require(match.Success, "Meta has no GUID: " + path); return match.Groups[1].Value;
        }
        static bool HasReceipt()
        {
            if (!File.Exists(ReceiptPath)) return false;
            var receipt = JsonUtility.FromJson<Receipt>(File.ReadAllText(ReceiptPath));
            return receipt != null && receipt.root == Root && receipt.manifestHash == Hash(ManifestPath);
        }
        static void RequireReceipt() { Require(HasReceipt(), "Run import for this exact manifest before modifying or inspecting the imported library."); }
        static void RequireOwned(Entry entry)
        {
            Require(File.Exists(AssetPath(entry.path)) && MetaGuid(entry.path + ".meta") == entry.guid, "Missing or differently owned destination: " + entry.path);
            Require(AssetDatabase.GUIDToAssetPath(entry.guid) == entry.path, "GUID does not resolve to the manifest destination: " + entry.path);
        }

        static void Import(Manifest manifest, Report report)
        {
            bool resume = HasReceipt();
            var copies = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var expected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Entry entry in manifest.files)
            {
                string destination = AssetPath(entry.path), source = Path.GetFullPath(Cache + "/" + entry.path);
                Require(source.StartsWith(Path.GetFullPath(Cache + "/" + Root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase), "Cache path escapes staged root.");
                Require(File.Exists(source) && new FileInfo(source).Length == entry.size && Hash(source) == entry.sha256, "Cache content differs from manifest: " + entry.path);
                Require(MetaGuid(source + ".meta") == entry.guid, "Cache GUID differs: " + entry.path);
                string guidOwner = AssetDatabase.GUIDToAssetPath(entry.guid);
                Require(string.IsNullOrEmpty(guidOwner) || guidOwner == entry.path, "GUID already belongs to " + guidOwner);
                expected.Add(destination); expected.Add(destination + ".meta");
                bool existing = File.Exists(destination), metaExists = File.Exists(destination + ".meta");
                if (metaExists) Require(MetaGuid(destination + ".meta") == entry.guid, "Destination has another GUID: " + entry.path);
                bool identical = existing && new FileInfo(destination).Length == entry.size && Hash(destination) == entry.sha256;
                Require(!existing || identical || (resume && metaExists), "Refusing different existing content without a same-source receipt: " + entry.path);
                if (existing) { report.filesRetained++; if (!identical) Add(report, "info", entry.path, "Retained same-source imported/adapted content; no overwrite."); }
                else copies.Add(destination, source);
                if (!metaExists) copies.Add(destination + ".meta", source + ".meta");
                else if (!resume) Require(Hash(destination + ".meta") == Hash(source + ".meta"), "Existing meta differs without a same-source receipt: " + entry.path);
                for (string folder = Path.GetDirectoryName(entry.path).Replace('\\','/'); folder.StartsWith(Root, StringComparison.Ordinal); folder = Path.GetDirectoryName(folder)?.Replace('\\','/') ?? "")
                {
                    string stagedMeta = Cache + "/" + folder + ".meta", targetMeta = Path.GetFullPath(folder + ".meta");
                    expected.Add(targetMeta);
                    if (!File.Exists(stagedMeta)) continue;
                    if (File.Exists(targetMeta)) Require(MetaGuid(stagedMeta) == MetaGuid(targetMeta), "Existing folder GUID differs: " + folder);
                    else copies[targetMeta] = Path.GetFullPath(stagedMeta);
                }
            }
            if (Directory.Exists(Root)) foreach (string file in Directory.GetFiles(Root, "*", SearchOption.AllDirectories))
            {
                string full = Path.GetFullPath(file);
                bool folderMeta = full.EndsWith(".meta", StringComparison.OrdinalIgnoreCase) && Directory.Exists(full.Substring(0,full.Length-5));
                Require(expected.Contains(full) || folderMeta, "Unexpected existing file in destination; no files were copied: " + file);
            }
            File.WriteAllText(ReceiptPath, JsonUtility.ToJson(new Receipt { root = Root, manifestHash = Hash(ManifestPath) }, true));
            string scratch = Path.GetFullPath(Review + "/copy-in-progress.tmp");
            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (var copy in copies.OrderByDescending(c => c.Key.EndsWith(".meta",StringComparison.OrdinalIgnoreCase)))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(copy.Key));
                    Require(!File.Exists(copy.Key), "Destination appeared during import; refusing overwrite: " + copy.Key);
                    // Atomic rename keeps a cancelled large-file copy out of Assets and makes retry safe.
                    File.Copy(copy.Value, scratch, true); File.Move(scratch, copy.Key); report.filesCopied++;
                }
            }
            finally { AssetDatabase.StopAssetEditing(); AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport); }
            Add(report, "info", Root, "Imported files and metas only. Run adapt, then verify and preview. No sourcePackage/sourceAsset path is read.");
        }

        sealed class Saved
        {
            public readonly Dictionary<string, Tex> textures = new Dictionary<string, Tex>();
            public readonly Dictionary<string, float> floats = new Dictionary<string, float>();
            public readonly Dictionary<string, Color> colors = new Dictionary<string, Color>();
            public float Float(float fallback, params string[] keys) { foreach (string key in keys) if (floats.TryGetValue(key,out float v)) return v; return fallback; }
            public Color Color(Color fallback, params string[] keys) { foreach (string key in keys) if (colors.TryGetValue(key,out Color v)) return v; return fallback; }
        }
        sealed class Tex { public Texture texture; public Vector2 scale, offset; }
        static Saved ReadSaved(Material material)
        {
            var saved = new Saved(); var serialized = new SerializedObject(material);
            SerializedProperty values = serialized.FindProperty("m_SavedProperties.m_TexEnvs");
            for (int i=0; values != null && i<values.arraySize; i++)
            {
                var item=values.GetArrayElementAtIndex(i);var value=item.FindPropertyRelative("second");
                saved.textures[item.FindPropertyRelative("first").stringValue]=new Tex { texture=value.FindPropertyRelative("m_Texture").objectReferenceValue as Texture,
                    scale=value.FindPropertyRelative("m_Scale").vector2Value,offset=value.FindPropertyRelative("m_Offset").vector2Value };
            }
            values=serialized.FindProperty("m_SavedProperties.m_Floats");
            for(int i=0;values!=null&&i<values.arraySize;i++){var item=values.GetArrayElementAtIndex(i);saved.floats[item.FindPropertyRelative("first").stringValue]=item.FindPropertyRelative("second").floatValue;}
            values=serialized.FindProperty("m_SavedProperties.m_Colors");
            for(int i=0;values!=null&&i<values.arraySize;i++){var item=values.GetArrayElementAtIndex(i);saved.colors[item.FindPropertyRelative("first").stringValue]=item.FindPropertyRelative("second").colorValue;}
            return saved;
        }
        static bool TextureTo(Material material, Saved saved, string target, params string[] sources)
        {
            foreach(string source in sources) if(saved.textures.TryGetValue(source,out Tex value) && value.texture)
            { material.SetTexture(target,value.texture);material.SetTextureScale(target,value.scale);material.SetTextureOffset(target,value.offset);return true; }
            return false;
        }
        static bool NativeUrp(Material material) => material.shader && (material.shader.name.StartsWith("Universal Render Pipeline/",StringComparison.Ordinal) || material.GetTag("RenderPipeline",false,"")=="UniversalPipeline");

        static void Adapt(Manifest manifest, Report report)
        {
            const string legacyInspector="UnityEditor.ShaderGraph.PBRMasterGUI";
            if(!AppDomain.CurrentDomain.GetAssemblies().Any(a=>a.GetType(legacyInspector,false)!=null))
                foreach(Entry entry in manifest.files.Where(e=>e.path.EndsWith(".shader",StringComparison.OrdinalIgnoreCase)))
                {
                    RequireOwned(entry);string source=File.ReadAllText(entry.path);
                    string updated=Regex.Replace(source,@"(?m)^[ \t]*CustomEditor[ \t]+""UnityEditor\.ShaderGraph\.PBRMasterGUI""[ \t]*\r?\n?","");
                    if(updated==source)continue;
                    File.WriteAllText(entry.path,updated);AssetDatabase.ImportAsset(entry.path,ImportAssetOptions.ForceSynchronousImport);
                    Add(report,"info",entry.path,"Removed only unavailable legacy PBRMasterGUI CustomEditor declaration; shader passes, wind code and staged source cache retained.");
                }
            Shader lit=Shader.Find("Universal Render Pipeline/Lit");Require(lit && lit.isSupported && !ShaderUtil.ShaderHasError(lit),"URP Lit must compile before adaptation.");
            foreach(Entry entry in manifest.files.Where(e=>Path.GetExtension(e.path).Equals(".mat",StringComparison.OrdinalIgnoreCase)))
            {
                RequireOwned(entry);Material material=AssetDatabase.LoadAssetAtPath<Material>(entry.path);Require(material,"Cannot load material: "+entry.path);
                if(NativeUrp(material))
                {
                    report.nativeMaterials++;
                    if(!material.shader.isSupported||ShaderUtil.ShaderHasError(material.shader))
                        Add(report,"warning",entry.path,"Native URP shader retained, but is unsupported or has compilation errors: "+material.shader.name+". Run verify for compiler messages; wind features were not replaced.");
                    continue;
                }
                Saved saved=ReadSaved(material);string old=material.shader?material.shader.name:"missing shader";
                bool hdrp=old.Contains("HDRP")||saved.textures.ContainsKey("_BaseColorMap");
                float mode=saved.Float(0,"_Mode");
                bool cutout=saved.Float(0,"_AlphaClip","_AlphaCutoffEnable")>.5f||mode==1||old.IndexOf("Cutout",StringComparison.OrdinalIgnoreCase)>=0;
                bool transparent=saved.Float(0,hdrp?"_SurfaceType":"_Surface")>.5f||mode>=2||(!cutout&&material.renderQueue>=3000);
                bool premultiply=transparent&&(mode==3||saved.Float(5,"_SrcBlend")==1&&saved.Float(10,"_DstBlend")==10);
                int oldQueue=material.renderQueue;
                material.shader=lit;material.shaderKeywords=Array.Empty<string>();
                TextureTo(material,saved,"_BaseMap",hdrp?"_BaseColorMap":"_MainTex","_BaseMap","_MainTex","_BaseColorMap","_AlbedoMap");
                material.SetColor("_BaseColor",saved.Color(Color.white,hdrp?"_BaseColor":"_Color","_BaseColor","_Color"));
                bool normal=TextureTo(material,saved,"_BumpMap","_BumpMap","_NormalMap");
                material.SetFloat("_BumpScale",saved.Float(1,"_BumpScale","_NormalScale"));CoreUtils.SetKeyword(material,"_NORMALMAP",normal);
                bool specular=saved.textures.TryGetValue("_SpecGlossMap",out Tex spec)&&spec.texture||old.Contains("Specular");
                bool metallic=TextureTo(material,saved,"_MetallicGlossMap","_MetallicGlossMap","_MaskMap");
                bool specMap=TextureTo(material,saved,"_SpecGlossMap","_SpecGlossMap");
                material.SetFloat("_WorkflowMode",specular?0:1);CoreUtils.SetKeyword(material,"_SPECULAR_SETUP",specular);
                material.SetFloat("_Metallic",saved.Float(0,"_Metallic"));material.SetFloat("_Smoothness",saved.Float(.5f,"_Smoothness","_Glossiness","_GlossMapScale"));
                material.SetColor("_SpecColor",saved.Color(new Color(.2f,.2f,.2f),"_SpecColor"));
                CoreUtils.SetKeyword(material,"_METALLICSPECGLOSSMAP",specular?specMap:metallic);
                material.SetFloat("_SmoothnessTextureChannel",saved.Float(0,"_SmoothnessTextureChannel"));
                CoreUtils.SetKeyword(material,"_SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A",!transparent&&saved.Float(0,"_SmoothnessTextureChannel")==1);
                CoreUtils.SetKeyword(material,"_OCCLUSIONMAP",TextureTo(material,saved,"_OcclusionMap","_OcclusionMap","_MaskMap"));
                material.SetFloat("_OcclusionStrength",saved.Float(1,"_OcclusionStrength"));
                TextureTo(material,saved,"_EmissionMap","_EmissionMap","_EmissiveColorMap");
                Color emission=saved.Color(Color.black,hdrp?"_EmissiveColor":"_EmissionColor","_EmissionColor");material.SetColor("_EmissionColor",emission);
                bool emissive=emission.maxColorComponent>0;CoreUtils.SetKeyword(material,"_EMISSION",emissive);
                if(emissive)material.globalIlluminationFlags&=~MaterialGlobalIlluminationFlags.EmissiveIsBlack;
                TextureTo(material,saved,"_DetailAlbedoMap","_DetailAlbedoMap");TextureTo(material,saved,"_DetailNormalMap","_DetailNormalMap");TextureTo(material,saved,"_DetailMask","_DetailMask");
                material.SetFloat("_DetailAlbedoMapScale",saved.Float(1,"_DetailAlbedoMapScale"));material.SetFloat("_DetailNormalMapScale",saved.Float(1,"_DetailNormalMapScale"));
                CoreUtils.SetKeyword(material,"_DETAIL_SCALED",material.GetTexture("_DetailAlbedoMap")||material.GetTexture("_DetailNormalMap"));
                material.SetFloat("_AlphaClip",cutout?1:0);material.SetFloat("_Cutoff",saved.Float(.5f,"_Cutoff","_AlphaCutoff"));CoreUtils.SetKeyword(material,"_ALPHATEST_ON",cutout);
                material.SetFloat("_Surface",transparent?1:0);material.SetFloat("_Blend",premultiply?1:0);
                CoreUtils.SetKeyword(material,"_SURFACE_TYPE_TRANSPARENT",transparent);CoreUtils.SetKeyword(material,"_ALPHAPREMULTIPLY_ON",premultiply);
                material.SetFloat("_SrcBlend",transparent&&!premultiply?(int)BlendMode.SrcAlpha:(int)BlendMode.One);
                material.SetFloat("_DstBlend",transparent?(int)BlendMode.OneMinusSrcAlpha:(int)BlendMode.Zero);material.SetFloat("_ZWrite",transparent?0:1);
                material.SetFloat("_Cull",saved.Float(0,"_DoubleSidedEnable")>.5f?0:saved.Float(2,"_Cull","_CullMode"));
                material.SetOverrideTag("RenderType",transparent?"Transparent":cutout?"TransparentCutout":"Opaque");
                int queue=transparent?3000:cutout?2450:2000;
                material.renderQueue=oldQueue>=queue&&oldQueue<queue+100?oldQueue:queue;
                material.SetShaderPassEnabled("ShadowCaster",!transparent);material.SetShaderPassEnabled("DepthOnly",!transparent);
                EditorUtility.SetDirty(material);AssetDatabase.SaveAssetIfDirty(material);report.adaptedMaterials++;
                Add(report,"info",entry.path,old+" -> URP Lit. Original serialized properties remain; custom wind, subsurface, tessellation and HDRP remap features are not reproduced.");
            }
        }

        static void Verify(Manifest manifest, Report report)
        {
            var shaders=new HashSet<Shader>();var meshes=new HashSet<Mesh>();var materials=new HashSet<Material>();var textures=new HashSet<Texture>();
            foreach(Entry entry in manifest.files)
            {
                try
                {
                    RequireOwned(entry);Object asset=AssetDatabase.LoadMainAssetAtPath(entry.path);Require(asset,"Asset did not load: "+entry.path);report.assets++;
                    bool placeable=entry.path.EndsWith(".prefab",StringComparison.OrdinalIgnoreCase),model=asset is GameObject&&!placeable;
                    Require(!placeable || asset is GameObject, "Prefab main object is not a GameObject: " + entry.path);
                    if(model)report.models++;
                    foreach(Object child in AssetDatabase.LoadAllAssetsAtPath(entry.path))
                    {if(child is Mesh mesh)meshes.Add(mesh);if(child is Material material){if(model)report.sourceEmbeddedMaterials++;else materials.Add(material);}if(child is Texture texture)textures.Add(texture);if(child is Shader shader)shaders.Add(shader);if(child is AnimationClip)report.animationClips++;}
                    if(asset is TerrainLayer layer){report.terrainLayers++;if(!layer.diffuseTexture)Add(report,"error",entry.path,"TerrainLayer has no diffuse texture.");}
                    if(placeable&&asset is GameObject prefab)
                    {
                        report.prefabs++;
                        foreach(Transform node in prefab.GetComponentsInChildren<Transform>(true))
                            if(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(node.gameObject)>0)Add(report,"error",entry.path,"Missing script at "+node.name);
                        foreach(MeshFilter filter in prefab.GetComponentsInChildren<MeshFilter>(true))if(!filter.sharedMesh)Add(report,"error",entry.path,"Missing MeshFilter mesh at "+filter.name);else meshes.Add(filter.sharedMesh);
                        foreach(SkinnedMeshRenderer skin in prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true))if(!skin.sharedMesh)Add(report,"error",entry.path,"Missing skinned mesh at "+skin.name);else meshes.Add(skin.sharedMesh);
                        foreach(Renderer renderer in prefab.GetComponentsInChildren<Renderer>(true))
                        {
                            Material[] slots=renderer.sharedMaterials;
                            if(slots.Length==0&&(renderer is MeshRenderer||renderer is SkinnedMeshRenderer))Add(report,"error",entry.path,"Renderer has no materials: "+renderer.name);
                            foreach(Material material in slots)if(!material)Add(report,"error",entry.path,"Missing material at "+renderer.name);else materials.Add(material);
                        }
                    }
                }
                catch(Exception error){Add(report,"error",entry.path,error.Message);}
            }
            foreach(Mesh mesh in meshes)if(mesh.vertexCount==0||mesh.subMeshCount==0)Add(report,"error",AssetDatabase.GetAssetPath(mesh),"Empty mesh: "+mesh.name);
            foreach(Material material in materials)
            {
                string path=AssetDatabase.GetAssetPath(material);
                if(!material.shader||material.shader.name=="Hidden/InternalErrorShader")Add(report,"error",path,"Missing/error shader.");
                else {shaders.Add(material.shader);if(!NativeUrp(material))Add(report,"warning",path,"Material is not tagged for URP; run adapt for standalone library .mat files.");}
                foreach(Tex tex in ReadSaved(material).textures.Values)if(tex.texture)textures.Add(tex.texture);
                if(path.EndsWith(".mat",StringComparison.OrdinalIgnoreCase)&&File.Exists(path))
                    foreach(Match reference in Regex.Matches(File.ReadAllText(path),@"(?m)^\s*-\s+(\w+):\s*\r?\n\s*m_Texture:\s*\{fileID:\s*(-?\d+),\s*guid:\s*([0-9a-fA-F]{32})"))
                        if(material.HasProperty(reference.Groups[1].Value)&&reference.Groups[2].Value!="0"&&!reference.Groups[3].Value.StartsWith("0000000000000000")&&
                            !AssetDatabase.LoadAssetAtPath<Texture>(AssetDatabase.GUIDToAssetPath(reference.Groups[3].Value)))
                            Add(report,"error",path,"Missing active texture "+reference.Groups[1].Value+" GUID "+reference.Groups[3].Value);
            }
            foreach(Shader shader in shaders)
            {
                string path=AssetDatabase.GetAssetPath(shader);
                if(!shader.isSupported)Add(report,"error",path,"Unsupported shader: "+shader.name);
                if(ShaderUtil.ShaderHasError(shader))foreach(var message in ShaderUtil.GetShaderMessages(shader))
                    if(message.severity.ToString()=="Error")Add(report,"error",path,message.message);
            }
            report.meshes=meshes.Count;report.materials=materials.Count;report.textures=textures.Count;report.shaders=shaders.Count;
            Add(report,"info",Root+"/Butterflies","Butterflies are checked as static/skeletal resources. Missing flight/Idle animations and AnimatorControllers are expected pending later source recovery; no animation acceptance is claimed.");
        }

        static void Preview(Manifest manifest, Report report)
        {
            Directory.CreateDirectory(Review+"/previews");
            foreach(Pack pack in manifest.packs)
            {
                var candidates=manifest.files.Where(e=>e.path.StartsWith(Root+"/"+pack.key+"/",StringComparison.Ordinal)).ToArray();
                var objects=candidates.Where(e=>e.path.EndsWith(".prefab",StringComparison.OrdinalIgnoreCase)).ToList();
                if(objects.Count==0)objects=candidates.Where(e=>e.path.EndsWith(".terrainlayer",StringComparison.OrdinalIgnoreCase)).ToList();
                if(objects.Count==0)objects=candidates.Where(e=>e.path.EndsWith(".mat",StringComparison.OrdinalIgnoreCase)).ToList();
                string[] choices=pack.key=="TerrainTextures"?new[]{"grass","mud","stone"}:new[]{"bridge","tree","pillar","cliff","rock","house","butterfly"};
                var selected=new List<Entry>();
                string[] specimens=pack.key=="TerrainTextures" ? new[]{"Mud_2.terrainlayer","Rock_3.terrainlayer","Sand_1.terrainlayer"} :
                    pack.key=="NatureBundle" ? new[]{"Tree_01_a_LOD.prefab","Rock_01_LOD.prefab","Tree_02_a_LOD.prefab"} :
                    pack.key=="AncientRuins" ? new[]{"SM_bridge_arch_ancient_01 (7).prefab","SM_wall_stoneblocks_ruins_03.prefab","SM_root_02.prefab"} :
                    pack.key=="EnchantedForest" ? new[]{"SM_Env_Tree_Large_01.prefab","SM_Env_Fern_Tree_01.prefab","SM_Env_Rock_Pebbles_01.prefab"} : Array.Empty<string>();
                foreach(string name in specimens)
                {
                    Entry found=objects.FirstOrDefault(e=>Path.GetFileName(e.path)==name);
                    if(found!=null)selected.Add(found);
                }
                foreach(string word in choices)
                {
                    if(selected.Count>=3)break;
                    Entry found=objects.Where(e=>!selected.Contains(e)&&Path.GetFileName(e.path).IndexOf(word,StringComparison.OrdinalIgnoreCase)>=0).OrderBy(e=>e.path,StringComparer.Ordinal).FirstOrDefault();
                    if(found!=null)selected.Add(found);if(selected.Count==3)break;
                }
                foreach(Entry entry in objects.OrderBy(e=>e.path,StringComparer.Ordinal)){if(selected.Count>=3)break;if(!selected.Contains(entry))selected.Add(entry);}
                if(selected.Count==0){Add(report,"error",pack.key,"No previewable prefab, TerrainLayer or material.");continue;}
                for(int i=0;i<selected.Count;i++)
                {
                    Entry entry=selected[i];string imagePath=Review+"/previews/"+pack.key+"_"+(i+1)+".png";
                    try{RequireOwned(entry);RenderPreview(entry.path,imagePath,report);report.previews.Add(new PreviewItem{pack=pack.key,asset=entry.path,image=imagePath,note="Actual Unity URP render; independently framed render-only geometry. Scripts, cameras, particles and animation are not run."});}
                    catch(Exception error){Add(report,"error",entry.path,error.ToString());}
                }
            }
        }

        static void RenderPreview(string path,string output,Report report)
        {
            var preview=new PreviewRenderUtility();Texture2D image=null;Material temporary=null;
            bool asynchronousCompilation=ShaderUtil.allowAsyncCompilation;
            try
            {
                ShaderUtil.allowAsyncCompilation=false;
                var root=new GameObject("Intake render-only specimen"){hideFlags=HideFlags.HideAndDontSave};preview.AddSingleGO(root);
                Object source=AssetDatabase.LoadMainAssetAtPath(path);var renderers=new List<Renderer>();
                if(source is GameObject prefab)
                {
                    var lowLods=new HashSet<Renderer>();var firstLods=new HashSet<Renderer>();
                    foreach(LODGroup group in prefab.GetComponentsInChildren<LODGroup>(true))
                    {LOD[] lods=group.GetLODs();for(int i=0;i<lods.Length;i++)foreach(Renderer r in lods[i].renderers)if(r){if(i==0)firstLods.Add(r);else lowLods.Add(r);}}
                    foreach(Renderer original in prefab.GetComponentsInChildren<Renderer>(true))
                    {
                        if(lowLods.Contains(original)&&!firstLods.Contains(original))continue;
                        Mesh mesh=original is SkinnedMeshRenderer skin?skin.sharedMesh:original.GetComponent<MeshFilter>()?.sharedMesh;
                        if(!mesh)continue;
                        var go=new GameObject(original.name,typeof(MeshFilter),typeof(MeshRenderer)){hideFlags=HideFlags.HideAndDontSave};go.transform.SetParent(root.transform,false);
                        Matrix4x4 matrix=prefab.transform.worldToLocalMatrix*original.transform.localToWorldMatrix;
                        go.transform.localPosition=matrix.GetColumn(3);go.transform.localRotation=matrix.rotation;go.transform.localScale=matrix.lossyScale;
                        go.GetComponent<MeshFilter>().sharedMesh=mesh;var renderer=go.GetComponent<MeshRenderer>();renderer.sharedMaterials=original.sharedMaterials;renderers.Add(renderer);
                    }
                }
                else
                {
                    Material material=source as Material;
                    if(source is TerrainLayer layer)
                    {temporary=new Material(Shader.Find("Universal Render Pipeline/Lit")){hideFlags=HideFlags.HideAndDontSave};temporary.SetTexture("_BaseMap",layer.diffuseTexture);temporary.SetTexture("_BumpMap",layer.normalMapTexture);temporary.SetFloat("_BumpScale",layer.normalScale);temporary.SetFloat("_Smoothness",layer.smoothness);CoreUtils.SetKeyword(temporary,"_NORMALMAP",layer.normalMapTexture);material=temporary;}
                    Require(material,"Preview expects a prefab, material or TerrainLayer.");
                    var go=new GameObject("Material sphere",typeof(MeshFilter),typeof(MeshRenderer)){hideFlags=HideFlags.HideAndDontSave};go.transform.SetParent(root.transform,false);
                    go.GetComponent<MeshFilter>().sharedMesh=Resources.GetBuiltinResource<Mesh>("Sphere.fbx");go.GetComponent<MeshRenderer>().sharedMaterial=material;renderers.Add(go.GetComponent<MeshRenderer>());
                }
                Require(renderers.Count>0,"No preview mesh renderers.");Bounds bounds=renderers[0].bounds;foreach(Renderer renderer in renderers)bounds.Encapsulate(renderer.bounds);
                foreach(Material material in renderers.SelectMany(r=>r.sharedMaterials))
                    Require(material&&material.shader&&material.shader.name!="Hidden/InternalErrorShader"&&material.shader.isSupported&&!ShaderUtil.ShaderHasError(material.shader),"Preview material/shader is missing, unsupported or has compilation errors.");
                Require(bounds.size.sqrMagnitude>.000001f,"Preview geometry has empty bounds.");
                Quaternion rotation=Quaternion.Euler(24,38,0);float radius=Mathf.Max(bounds.extents.magnitude,.1f);
                preview.camera.cameraType=CameraType.Preview;preview.camera.clearFlags=CameraClearFlags.SolidColor;preview.camera.backgroundColor=new Color(.22f,.25f,.28f);
                preview.camera.orthographic=true;preview.camera.orthographicSize=radius*1.12f;preview.camera.aspect=1;
                preview.camera.nearClipPlane=.01f;preview.camera.farClipPlane=radius*8+10;
                preview.camera.transform.SetPositionAndRotation(bounds.center-rotation*Vector3.forward*(radius*3+1),rotation);
                var data=preview.camera.GetUniversalAdditionalCameraData();data.renderPostProcessing=false;data.renderShadows=false;
                preview.ambientColor=new Color(.55f,.55f,.55f);
                preview.lights[0].type=LightType.Directional;preview.lights[0].intensity=1.2f;preview.lights[0].transform.rotation=Quaternion.Euler(40,-30,0);
                preview.lights[1].type=LightType.Directional;preview.lights[1].intensity=.5f;preview.lights[1].transform.rotation=Quaternion.Euler(20,140,0);
                foreach(Renderer renderer in renderers)renderer.enabled=false;
                preview.BeginStaticPreview(new Rect(0,0,640,640));preview.Render(true);image=preview.EndStaticPreview();
                Color32[] empty=image.GetPixels32();Object.DestroyImmediate(image);image=null;
                foreach(Renderer renderer in renderers)renderer.enabled=true;
                preview.BeginStaticPreview(new Rect(0,0,640,640));preview.Render(true);image=preview.EndStaticPreview();
                Require(image&&image.width==640&&image.height==640,"Preview did not produce a 640px image.");
                Color32[] pixels=image.GetPixels32();int magenta=0,changed=0;
                for(int i=0;i<pixels.Length;i++){Color32 c=pixels[i],b=empty[i];if(c.r>220&&c.b>220&&c.g<70)magenta++;if(Math.Abs(c.r-b.r)+Math.Abs(c.g-b.g)+Math.Abs(c.b-b.b)>24)changed++;}
                File.WriteAllBytes(output,image.EncodeToPNG());
                Require(changed>100,"Preview is empty; saved image for diagnosis.");
                if(magenta>=100)Add(report,"warning",path,"Preview contains "+magenta+" magenta pixels; shaders passed explicit checks, so inspect the image for intended pink colors.");
            }
            finally{ShaderUtil.allowAsyncCompilation=asynchronousCompilation;preview.Cleanup();if(image)Object.DestroyImmediate(image);if(temporary)Object.DestroyImmediate(temporary);}
        }

        static void Add(Report report,string severity,string path,string message){report.issues.Add(new Issue{severity=severity,path=path,message=message});}
        static void Require(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
    }
}
