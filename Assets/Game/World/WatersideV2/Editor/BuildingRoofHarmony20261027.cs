using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace WeatherHouse.WatersideV2.Editor
{
    // Explicit entry points only. The coordinating chat owns refresh, Play and scene saves.
    public static class BuildingRoofHarmony20261027
    {
        const string Folder = "Assets/Environment/BuildingRoofHarmony20261027";
        const string Reference = "Assets/Environment/WeatherHouseV05/OilPaintPreview/WH05_";
        const string Review = "Docs/NightReview20261027/建筑屋顶与油画材质";
        static readonly string[] Pigments = { "_BaseColor", "_ShadowColor", "_HighlightColor", "_StrokeColor", "_ShadowEdgeColor" };
        [Serializable] sealed class Manifest { public string id, status, sourcePrefab; public Level[] lods; public MaterialEntry[] materials; }
        [Serializable] sealed class Level { public int level; public string sourceModel, sourceSha256, model, modelSha256; public Replacement[] replacements; }
        [Serializable] sealed class Replacement { public string objectName, sourceMaterial; public bool disabled, meshChanged; public string[] materials; }
        [Serializable] sealed class MaterialEntry { public string importName, path, family, sourcePath; public bool transparent, palette; }
        [Serializable] sealed class Result
        {
            public string id, status, utc, prefabBackup, error;
            public int sceneInstances, meshReplacements, disabledRoofMeshes, materialRenderers;
            public bool allSceneTransformsUnchanged, collidersUnchanged, referenceMaterialsUnchanged, sceneSaved;
            public bool lodRendererArraysUnchanged=true, additionalRenderersUnchanged=true;
            public List<string> preservedAdditionalRenderers=new List<string>();
        }
        sealed class Frame
        {
            public Vector3 position, scale; public Quaternion rotation; public int parent;
            public Frame(Transform t) { position=t.localPosition; rotation=t.localRotation; scale=t.localScale; parent=t.parent?t.parent.GetInstanceID():0; }
            public bool Equals(Transform t) => position.Equals(t.localPosition) && rotation.Equals(t.localRotation) && scale.Equals(t.localScale) && parent==(t.parent?t.parent.GetInstanceID():0);
        }

        [MenuItem("Weather House/Review 20261027/Inspect A09 Roof Harmony")]
        static void InspectA09Menu() => Debug.Log(InspectA09());
        [MenuItem("Weather House/Review 20261027/Apply A09 Roof Harmony")]
        static void ApplyA09Menu() => Debug.Log(ApplyA09());
        [MenuItem("Weather House/Review 20261027/Inspect Prepared Building Roofs")]
        static void InspectAllMenu()
        {
            foreach (string id in new[] { "A01", "A02", "A03", "A04", "A06", "A07", "A09" })
                if (File.Exists(Folder+"/"+id+"/integration.json")) Debug.Log(Inspect(id));
        }
        [MenuItem("Weather House/Review 20261027/Apply Prepared Building Roofs")]
        static void ApplyAllMenu() => Debug.Log(ApplyAllPresent());

        public static string InspectA09() => Inspect("A09");
        public static string ApplyA09() => Apply("A09");
        public static string ApplyAllPresent()
        {
            RequireEdit();
            var reports = new List<string>();
            foreach (string id in new[] { "A01", "A02", "A03", "A04", "A06", "A07", "A09" })
            {
                string path=Folder+"/"+id+"/integration.json";
                if (!File.Exists(path)) continue;
                Manifest manifest=JsonUtility.FromJson<Manifest>(File.ReadAllText(path));
                if (Instances(manifest.sourcePrefab).Length==0) { reports.Add(id+" skipped: no existing scene instance"); continue; }
                reports.Add(Apply(id));
            }
            return string.Join("\n",reports);
        }

        static void RequireEdit()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
                throw new InvalidOperationException("Run this explicit appearance apply in edit mode after compilation.");
        }
        static string Hash(string path)
        {
            using (var hash=SHA256.Create()) using (var stream=File.OpenRead(path))
                return BitConverter.ToString(hash.ComputeHash(stream)).Replace("-","").ToLowerInvariant();
        }
        static GameObject[] Instances(string prefab)
        {
            return Resources.FindObjectsOfTypeAll<GameObject>().Where(go => go.scene.IsValid() && go.scene.isLoaded &&
                PrefabUtility.IsAnyPrefabInstanceRoot(go) && PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(go)==prefab).ToArray();
        }
        static Manifest Read(string id)
        {
            Manifest m=JsonUtility.FromJson<Manifest>(File.ReadAllText(Folder+"/"+id+"/integration.json"));
            if (m.id!=id || m.lods==null || m.lods.Length!=3) throw new InvalidOperationException("Invalid three-LOD manifest: "+id);
            if (m.status!="OFFLINE_PASS_UNITY_PENDING") throw new InvalidOperationException("This roof version is not ready for integration: "+id+" "+m.status);
            foreach (MaterialEntry entry in m.materials)
            {
                Material material=AssetDatabase.LoadAssetAtPath<Material>(entry.path);
                if (!material || !material.shader || !material.shader.isSupported || ShaderUtil.ShaderHasError(material.shader))
                    throw new InvalidOperationException("Mapped material shader unavailable: "+entry.path);
                if (entry.transparent && (material.shader.name!="Weather House/Oil Paint Glass Harmony" || material.renderQueue!=3000))
                    throw new InvalidOperationException("Preserve transparent glass rendering: "+entry.path);
            }
            foreach (Level level in m.lods)
            {
                if (Hash(level.sourceModel)!=level.sourceSha256 || Hash(level.model)!=level.modelSha256)
                    throw new InvalidOperationException("Model changed since offline validation: "+level.sourceModel);
                GameObject source=AssetDatabase.LoadAssetAtPath<GameObject>(level.sourceModel);
                GameObject replacement=AssetDatabase.LoadAssetAtPath<GameObject>(level.model);
                if (!source || !replacement) throw new InvalidOperationException("Import the staged models before applying "+id);
                foreach (Replacement entry in level.replacements)
                {
                    MeshFilter old=FindMesh(source,entry.objectName);
                    if (entry.disabled) continue;
                    MeshFilter next=FindMesh(replacement,entry.objectName);
                    float maximum=0;
                    for (int r=0;r<4;r++) for (int c=0;c<4;c++) maximum=Mathf.Max(maximum,Mathf.Abs(old.transform.localToWorldMatrix[r,c]-next.transform.localToWorldMatrix[r,c]));
                    if (maximum>.0001f) throw new InvalidOperationException("Imported local frame changed: "+id+" "+entry.objectName);
                    if (!next.sharedMesh || next.sharedMesh.subMeshCount!=entry.materials.Length) throw new InvalidOperationException("Material slot mismatch: "+entry.objectName);
                    foreach (string path in entry.materials) if (!AssetDatabase.LoadAssetAtPath<Material>(path)) throw new InvalidOperationException("Missing mapped material "+path);
                }
            }
            return m;
        }
        static MeshFilter FindMesh(GameObject root,string name)
        {
            MeshFilter[] found=root.GetComponentsInChildren<MeshFilter>(true).Where(f=>f.name==name).ToArray();
            if (found.Length!=1) throw new InvalidOperationException(root.name+" expected one mesh named "+name+"; got "+found.Length);
            return found[0];
        }
        static Dictionary<Transform,Frame> SceneFrames()
        {
            return Resources.FindObjectsOfTypeAll<Transform>().Where(t=>t.gameObject.scene.IsValid() && t.gameObject.scene.isLoaded).ToDictionary(t=>t,t=>new Frame(t));
        }
        static Dictionary<Collider,string> ColliderState(GameObject[] roots)
        {
            return roots.SelectMany(r=>r.GetComponentsInChildren<Collider>(true)).Distinct().ToDictionary(c=>c,c=>EditorJsonUtility.ToJson(c));
        }
        static void ConfigureMaterials(Manifest m)
        {
            foreach (MaterialEntry entry in m.materials)
            {
                // The moss uses the actual shared weather-house material, including current in-memory tuning.
                if (entry.family=="Moss") continue;
                Material reference=AssetDatabase.LoadAssetAtPath<Material>(Reference+entry.family+"_Oil.mat");
                Material source=AssetDatabase.LoadAssetAtPath<Material>(entry.sourcePath);
                Material target=AssetDatabase.LoadAssetAtPath<Material>(entry.path);
                if (!reference || !source || !target || !reference.shader || ShaderUtil.ShaderHasError(reference.shader))
                    throw new InvalidOperationException("Material dependencies unavailable: "+entry.path);
                Texture paletteMap=entry.palette?target.GetTexture("_BaseMap"):null;
                target.shader=reference.shader;target.CopyPropertiesFromMaterial(reference);
                Color tint=entry.palette?Color.white:(source.HasProperty("_BaseColor")?source.GetColor("_BaseColor"):Color.white);
                foreach (string pigment in Pigments)
                {
                    Color value=reference.GetColor(pigment)*tint;value.a=1;target.SetColor(pigment,value);
                }
                Texture map=entry.palette?paletteMap:(source.HasProperty("_BaseMap")?source.GetTexture("_BaseMap"):null);
                target.SetTexture("_BaseMap",map);target.SetFloat("_TextureInfluence",map?1:0);
                if (map) { target.SetTextureScale("_BaseMap",entry.palette?Vector2.one:source.GetTextureScale("_BaseMap"));target.SetTextureOffset("_BaseMap",entry.palette?Vector2.zero:source.GetTextureOffset("_BaseMap")); }
                if (source.HasProperty("_Cull")) target.SetFloat("_Cull",source.GetFloat("_Cull"));
                if (entry.transparent)
                {
                    Shader transparent=Shader.Find("Weather House/Oil Paint Glass Harmony");
                    if (!transparent || !transparent.isSupported || ShaderUtil.ShaderHasError(transparent)) throw new InvalidOperationException("Transparent oil shader unavailable.");
                    target.shader=transparent;target.SetFloat("_Opacity",tint.a);target.renderQueue=3000;
                    target.SetShaderPassEnabled("ShadowCaster",false);target.SetShaderPassEnabled("DepthOnly",false);target.SetShaderPassEnabled("DepthNormals",false);
                }
                EditorUtility.SetDirty(target);AssetDatabase.SaveAssetIfDirty(target);
            }
        }

        static string RendererState(Renderer renderer) => string.Join("\n",renderer.GetComponents<Component>().Where(c=>c)
            .Select(c=>c.GetType().FullName+":"+EditorJsonUtility.ToJson(c)));

        static void ApplyRoot(GameObject root,Manifest m,Result result,bool recordUndo,bool validateOnly=false)
        {
            LODGroup group=root.GetComponent<LODGroup>();
            if (!group || group.GetLODs().Length!=3) throw new InvalidOperationException("Expected the existing three LOD groups on "+root.name);
            LOD[] lods=group.GetLODs();
            foreach (Level level in m.lods)
            {
                Renderer[] renderers=lods[level.level].renderers;
                // Existing stone steps and other additions are outside the replacement manifest.
                // Require every target below, while preserving additions and null LOD slots as authored.
                var names=new HashSet<string>(level.replacements.Select(entry=>entry.objectName));
                var preserved=renderers.Where(r=>r && !names.Contains(r.name)).Distinct().ToDictionary(r=>r,RendererState);
                foreach (Renderer renderer in preserved.Keys)
                {
                    string label=root.name+"/"+renderer.name;
                    if (!result.preservedAdditionalRenderers.Contains(label)) result.preservedAdditionalRenderers.Add(label);
                }
                GameObject replacement=AssetDatabase.LoadAssetAtPath<GameObject>(level.model);
                foreach (Replacement entry in level.replacements)
                {
                    Renderer[] found=renderers.Where(r=>r && r.name==entry.objectName).ToArray();
                    if (found.Length!=1) throw new InvalidOperationException("Existing renderer identity mismatch: "+root.name+"/"+entry.objectName);
                    Renderer renderer=found[0];MeshFilter filter=renderer.GetComponent<MeshFilter>();
                    if (!filter) throw new InvalidOperationException("Missing MeshFilter: "+renderer.name);
                    string meshPath=filter.sharedMesh?AssetDatabase.GetAssetPath(filter.sharedMesh):"";
                    if (meshPath!=level.sourceModel && meshPath!=level.model && !(entry.disabled && !filter.sharedMesh))
                        throw new InvalidOperationException("Preserving unexpected mesh override: "+root.name+"/"+filter.name+" = "+meshPath);
                    if (validateOnly) continue;
                    if (recordUndo) Undo.RecordObjects(new Object[]{filter,renderer},"Unify building moss and oil materials");
                    if (entry.disabled) { filter.sharedMesh=null;renderer.enabled=false;result.disabledRoofMeshes++; }
                    else
                    {
                        if (entry.meshChanged) { filter.sharedMesh=FindMesh(replacement,entry.objectName).sharedMesh;result.meshReplacements++; }
                        renderer.sharedMaterials=entry.materials.Select(path=>AssetDatabase.LoadAssetAtPath<Material>(path)).ToArray();
                        result.materialRenderers++;
                    }
                    if (recordUndo)
                    {
                        PrefabUtility.RecordPrefabInstancePropertyModifications(filter);
                        PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
                    }
                }
                result.additionalRenderersUnchanged &= preserved.All(kv=>kv.Key && RendererState(kv.Key)==kv.Value);
                if (!result.additionalRenderersUnchanged) throw new InvalidOperationException("Additional renderer changed on "+root.name+" LOD"+level.level);
            }
            LOD[] after=group.GetLODs();
            result.lodRendererArraysUnchanged &= after.Length==lods.Length && Enumerable.Range(0,lods.Length).All(i=>after[i].renderers.SequenceEqual(lods[i].renderers));
            if (!result.lodRendererArraysUnchanged) throw new InvalidOperationException("LOD renderer bindings changed on "+root.name);
        }
        public static string Inspect(string id)
        {
            RequireEdit();Manifest m=Read(id);
            var result=new Result();
            foreach (GameObject root in Instances(m.sourcePrefab)) ApplyRoot(root,m,result,false,true);
            return id+" preflight passed; existing scene instances="+Instances(m.sourcePrefab).Length+"; preserved additions="+string.Join(", ",result.preservedAdditionalRenderers)+"; appearance-only changes; no scene save.";
        }
        public static string Apply(string id)
        {
            RequireEdit();Manifest m=Read(id);GameObject[] roots=Instances(m.sourcePrefab);
            if (roots.Length==0) throw new InvalidOperationException("No existing "+id+" scene instance; no prefab changed.");
            foreach (GameObject root in roots) ApplyRoot(root,m,new Result(),false,true);
            ApplyRoot(AssetDatabase.LoadAssetAtPath<GameObject>(m.sourcePrefab),m,new Result(),false,true);
            var frames=SceneFrames();var colliders=ColliderState(roots);
            var referenceHashes=Directory.GetFiles(Path.GetDirectoryName(Reference),"WH05_*_Oil.mat").ToDictionary(p=>p,Hash);
            Result result=new Result{id=id,status="RUNNING",utc=DateTime.UtcNow.ToString("O"),sceneInstances=roots.Length,sceneSaved=false};
            Directory.CreateDirectory(Review+"/Backups");
            result.prefabBackup=Review+"/Backups/"+id+"_"+DateTime.UtcNow.ToString("yyyyMMdd_HHmmssfff")+".prefab";
            File.Copy(m.sourcePrefab,result.prefabBackup,false);
            try
            {
                ConfigureMaterials(m);
                GameObject contents=PrefabUtility.LoadPrefabContents(m.sourcePrefab);
                try { ApplyRoot(contents,m,result,false);PrefabUtility.SaveAsPrefabAsset(contents,m.sourcePrefab); }
                finally { PrefabUtility.UnloadPrefabContents(contents); }
                // Apply the same narrow references to live instances, including existing mesh/material overrides.
                foreach (GameObject root in roots) ApplyRoot(root,m,result,true);
                result.allSceneTransformsUnchanged=frames.All(kv=>kv.Key && kv.Value.Equals(kv.Key));
                result.collidersUnchanged=colliders.All(kv=>kv.Key && EditorJsonUtility.ToJson(kv.Key)==kv.Value);
                result.referenceMaterialsUnchanged=referenceHashes.All(kv=>Hash(kv.Key)==kv.Value);
                if (!result.allSceneTransformsUnchanged || !result.collidersUnchanged || !result.referenceMaterialsUnchanged)
                    throw new InvalidOperationException("Appearance guard failed. Inspect the report and retain the source backup.");
                result.status="APPLIED_EDIT_MODE_VISUAL_REVIEW_PENDING";
            }
            catch (Exception error) { result.status="FAILED";result.error=error.ToString();throw; }
            finally { File.WriteAllText(Review+"/"+id+"-unity-apply.json",JsonUtility.ToJson(result,true)); }
            return Review+"/"+id+"-unity-apply.json";
        }
    }
}
