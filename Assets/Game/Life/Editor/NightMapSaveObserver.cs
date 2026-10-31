using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace WeatherHouse.NightReview20261027
{
    // Install and explicitly request only from the root coordinator after preview review.
    // No request means no Save. This observer never enters Play, opens a scene or saves other assets.
    [InitializeOnLoad]
    public static class NightMapSaveObserver
    {
        const string Stage = NightMapSnapshot.Staging;
        static double next;
        [Serializable] public sealed class Request
        {
            public string action, reviewedReceipt, reviewedReceiptSha256, expectedOriginalSceneSha256;
        }
        [Serializable] public sealed class Result
        {
            public string status, utc, scene, receiptSha256, originalSceneSha256, savedSceneSha256, backupPath, backupSha256, metaBackupPath,
                originalMemoryHash, savedMemoryHash, terrainSha256, preSaveGuard, error;
            public long originalBytes, backupBytes, savedBytes;
            public int originalTransforms, savedTransforms;
            public bool dirtyBefore, dirtyAfter, saveCalled, saveReturned;
        }
        [Serializable] sealed class TransformRow
        {
            public int instanceId, parentId, sibling;
            public string name;
            public bool active;
            public Vector3 position, localPosition, localScale;
            public Quaternion rotation, localRotation;
        }
        [Serializable] sealed class TransformRows { public List<TransformRow> rows; }
        static NightMapSaveObserver() { EditorApplication.update += Poll; }
        static void Poll()
        {
            if (EditorApplication.timeSinceStartup < next || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            next = EditorApplication.timeSinceStartup + 1;
            string file = Path.Combine(Stage,"landscape-save-request.json"); if (!File.Exists(file)) return;
            var result = new Result { utc = DateTime.UtcNow.ToString("O"), status = "NOT_SAVED" };
            try
            {
                Request request = JsonUtility.FromJson<Request>(File.ReadAllText(file)); File.Delete(file);
                Save(request,result);
            }
            catch (Exception e)
            {
                result.status = result.saveCalled ? "SAVE_OR_POST_GUARD_FAILED_REVIEW_BYTE_BACKUP" : "REFUSED_BEFORE_SAVE_NO_SAVE_CALLED";
                result.error = e.ToString(); result.dirtyAfter = SceneManager.GetActiveScene().isDirty;
            }
            File.WriteAllText(Path.Combine(Stage,"landscape-save-result.json"),JsonUtility.ToJson(result,true));
        }
        static void Save(Request request,Result result)
        {
            Scene scene = SceneManager.GetActiveScene(); result.scene = scene.path; result.dirtyBefore = scene.isDirty;
            if (request == null || request.action != "save-reviewed-map" || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating ||
                SceneManager.sceneCount != 1 || scene.path != NightMapSnapshot.ScenePath || !scene.isDirty)
                throw new InvalidOperationException("Explicit save-reviewed-map needs the stable, dirty, already-open WatersideV2 Edit scene alone.");
            string receipt = Path.GetFullPath(request.reviewedReceipt ?? "");
            if (!receipt.Equals(Path.GetFullPath(Path.Combine(Stage,"landscape-after.json")),StringComparison.OrdinalIgnoreCase) || !File.Exists(receipt))
                throw new InvalidOperationException("Only this staging landscape-after receipt may authorize Save.");
            result.receiptSha256 = HashFile(receipt);
            if (!result.receiptSha256.Equals(request.reviewedReceiptSha256,StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Reviewed receipt changed.");
            var plan = JsonUtility.FromJson<NightMapLandscapeCandidate.Plan>(File.ReadAllText(receipt));
            if (plan.version != "night-map-reuse-v2" || plan.status != "APPLIED_UNSAVED_VISUAL_AND_PLAY_PENDING" || plan.dirtyBefore)
                throw new InvalidOperationException("A matching v2 Apply receipt with a clean in-memory baseline is required. The existing on-disk Git dirty scene will be backed up byte-for-byte.");
            result.originalSceneSha256 = HashFile(scene.path);
            if (!result.originalSceneSha256.Equals(request.expectedOriginalSceneSha256,StringComparison.OrdinalIgnoreCase) || result.originalSceneSha256 != plan.sceneSha256)
                throw new InvalidOperationException("Original scene disk bytes changed after the reviewed transaction.");
            result.preSaveGuard = NightMapLandscapeCandidate.VerifyForSave(receipt);
            var guard = JsonUtility.FromJson<NightMapLandscapeCandidate.PreSaveResult>(File.ReadAllText(result.preSaveGuard));
            if (guard.status != "PASS_STATE_GUARDS_COORDINATOR_SAVE_REVIEW_PENDING" || guard.playing || !guard.dirty)
                throw new InvalidOperationException("Full original/protected/after manifest and collider approach pre-save guards did not pass.");
            Transform[] original = All(scene); result.originalTransforms = original.Length;
            result.originalMemoryHash = MemoryHash(original);
            string terrain = plan.terrainPath; result.terrainSha256 = HashFile(terrain);
            if (result.terrainSha256 != plan.terrainSha256) throw new InvalidOperationException("Terrain source changed.");

            byte[] bytes = File.ReadAllBytes(scene.path); result.originalBytes = bytes.LongLength;
            if (HashBytes(bytes) != result.originalSceneSha256) throw new InvalidOperationException("Scene changed while reading backup bytes.");
            string folder = Path.Combine(Stage,"scene-backups",DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff")+"-"+result.originalSceneSha256.Substring(0,10));
            Directory.CreateDirectory(folder);
            result.backupPath = Path.Combine(folder,"WeatherHouse_WatersideV2.before.unity");
            using (var backup = new FileStream(result.backupPath,FileMode.CreateNew,FileAccess.Write,FileShare.Read))
            { backup.Write(bytes,0,bytes.Length); backup.Flush(true); }
            result.backupSha256 = HashFile(result.backupPath); result.backupBytes = new FileInfo(result.backupPath).Length;
            if (result.backupSha256 != result.originalSceneSha256 || result.backupBytes != result.originalBytes)
                throw new InvalidOperationException("Byte-exact backup verification failed; Save is refused.");
            if (File.Exists(scene.path+".meta"))
            {
                result.metaBackupPath = Path.Combine(folder,"WeatherHouse_WatersideV2.before.unity.meta");
                File.Copy(scene.path+".meta",result.metaBackupPath,false);
                if (HashFile(result.metaBackupPath) != HashFile(scene.path+".meta")) throw new InvalidOperationException("Scene meta backup mismatch.");
            }
            File.WriteAllText(Path.Combine(folder,"backup-manifest.json"),JsonUtility.ToJson(result,true));
            if (HashFile(scene.path) != result.originalSceneSha256 || MemoryHash(All(scene)) != result.originalMemoryHash || HashFile(receipt) != result.receiptSha256 || !scene.isDirty)
                throw new InvalidOperationException("State changed after backup, before Save.");

            result.saveCalled = true;
            result.saveReturned = EditorSceneManager.SaveScene(scene,scene.path,false);
            if (!result.saveReturned) throw new IOException("Unity SaveScene returned false; preserve the byte backup and inspect the editor.");
            Transform[] after = All(scene); result.savedTransforms = after.Length; result.savedMemoryHash = MemoryHash(after);
            result.savedSceneSha256 = HashFile(scene.path); result.savedBytes = new FileInfo(scene.path).Length; result.dirtyAfter = scene.isDirty;
            if (result.savedTransforms != result.originalTransforms || result.savedMemoryHash != result.originalMemoryHash ||
                HashFile(terrain) != result.terrainSha256 || result.dirtyAfter || EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Post-save transform/binding/count/terrain/Dirty/Play guard failed. Do not silently overwrite or auto-restore; review the backup.");
            result.status = "PASS_SAVED_WITH_BYTE_EXACT_ORIGINAL_BACKUP_VISUAL_PLAY_AND_HUMAN_ACCEPTANCE_SEPARATE";
            File.WriteAllText(Path.Combine(folder,"save-manifest.json"),JsonUtility.ToJson(result,true));
        }
        static Transform[] All(Scene scene) => scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Transform>(true)).OrderBy(t=>t.GetInstanceID()).ToArray();
        static string MemoryHash(Transform[] all)
        {
            // Instance IDs remain fixed through this one Save call, even if serialized local IDs are assigned to new objects.
            var rows = new TransformRows { rows = all.Select(t=>new TransformRow { instanceId=t.GetInstanceID(),parentId=t.parent?t.parent.GetInstanceID():0,sibling=t.GetSiblingIndex(),
                name=t.name,active=t.gameObject.activeSelf,position=t.position,rotation=t.rotation,localPosition=t.localPosition,localRotation=t.localRotation,localScale=t.localScale }).ToList() };
            string bindings = string.Join("\n",all.SelectMany(t=>t.GetComponents<Renderer>()).OrderBy(r=>r.GetInstanceID()).Select(r=>r.GetInstanceID()+"|"+r.enabled+"|"+
                string.Join(",",r.sharedMaterials.Select(m=>m?m.GetInstanceID():0))+"|"+MeshId(r)))+"\n"+
                string.Join("\n",all.SelectMany(t=>t.GetComponents<Collider>()).OrderBy(c=>c.GetInstanceID()).Select(c=>c.GetInstanceID()+"|"+c.enabled+"|"+c.isTrigger+"|"+EditorJsonUtility.ToJson(c)));
            return HashBytes(Encoding.UTF8.GetBytes(JsonUtility.ToJson(rows)+"\n"+bindings));
        }
        static int MeshId(Renderer renderer)
        { MeshFilter filter=renderer.GetComponent<MeshFilter>();Mesh mesh=renderer is SkinnedMeshRenderer skin?skin.sharedMesh:filter?filter.sharedMesh:null;return mesh?mesh.GetInstanceID():0; }
        static string HashFile(string p)=>HashBytes(File.ReadAllBytes(p));
        static string HashBytes(byte[] bytes){using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-","");}
    }
}
