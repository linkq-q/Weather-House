using System;
using System.IO;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;

namespace WeatherHouse.NightReview20261027
{
    // Coordinator-only explicit file commands. No request means no action. This observer never saves or plays.
    [InitializeOnLoad]
    public static class NightMapTransactionObserver
    {
        static double next;
        [Serializable] public sealed class Request
        {
            public string action, reviewedFile, reviewedFileSha256, expectedSceneSha256;
        }
        [Serializable] public sealed class Result
        {
            public string action, utc, status, input, inputSha256, output, error;
        }
        static NightMapTransactionObserver() { EditorApplication.update += Poll; }
        static void Poll()
        {
            if (EditorApplication.timeSinceStartup < next || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            next = EditorApplication.timeSinceStartup + 1;
            string commandPath = Path.Combine(NightMapSnapshot.Staging,"landscape-transaction-request.json");
            if (!File.Exists(commandPath)) return;
            var result = new Result { utc = DateTime.UtcNow.ToString("O") };
            try
            {
                Request request = JsonUtility.FromJson<Request>(File.ReadAllText(commandPath)); File.Delete(commandPath);
                if (request == null || request.action != "apply" && request.action != "restore" && request.action != "verify-before-save")
                    throw new InvalidOperationException("Only explicit apply, restore and verify-before-save are accepted; Save is unavailable.");
                result.action = request.action;
                string file = Path.GetFullPath(request.reviewedFile ?? "");
                string expected = Path.GetFullPath(Path.Combine(NightMapSnapshot.Staging,request.action == "apply" ? "landscape-plan.json" : "landscape-after.json"));
                if (!file.Equals(expected,StringComparison.OrdinalIgnoreCase) || !File.Exists(file)) throw new InvalidOperationException("Only this staging plan/receipt is accepted.");
                string sha = HashFile(file); result.input = file; result.inputSha256 = sha;
                if (!sha.Equals(request.reviewedFileSha256,StringComparison.OrdinalIgnoreCase) ||
                    !HashFile(NightMapSnapshot.ScenePath).Equals(request.expectedSceneSha256,StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Reviewed manifest or original scene file SHA changed; re-review before any operation.");
                if (request.action == "apply")
                {
                    result.output = NightMapLandscapeCandidate.Apply(file);
                    result.status = "PASS_APPLIED_UNSAVED_VISUAL_PLAY_AND_SAVE_REVIEW_PENDING";
                }
                else if (request.action == "restore")
                {
                    result.output = NightMapLandscapeCandidate.Restore(file); result.status = "RESTORE_RETURNED_INSPECT_RECEIPT_AND_DIRTY";
                }
                else
                {
                    result.output = NightMapLandscapeCandidate.VerifyForSave(file); result.status = "PASS_PRE_SAVE_STATE_GUARDS_NO_SAVE_EXECUTED";
                }
            }
            catch (Exception e) { result.status = "FAIL_ACTION_REVIEW_EDITOR_AND_RECEIPTS"; result.error = e.ToString(); }
            File.WriteAllText(Path.Combine(NightMapSnapshot.Staging,"landscape-transaction-result.json"),JsonUtility.ToJson(result,true));
        }
        static string HashFile(string p){using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(p))).Replace("-","");}
    }
}
