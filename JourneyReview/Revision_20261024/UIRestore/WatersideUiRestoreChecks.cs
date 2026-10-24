using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using WeatherHousePrototype.Editor;

// Copy into the marked validation project's Editor assembly. Every notebook path below is synthetic.
public static class WatersideUiRestoreChecks
{
    const string Key = "Waterside.GameplayUiReview.";
    const BindingFlags StaticPrivate = BindingFlags.Static | BindingFlags.NonPublic;
    static readonly Type Helper = typeof(WatersideGameplayPlayReview);
    static readonly FieldInfo ReportField = Helper.GetField("report", StaticPrivate);
    static readonly byte[] Original = { 0, 255, 13, 10, 73, 19 }, OriginalTemp = { 42, 0, 128, 7 };
    static readonly byte[] During = { 3, 1, 4, 1, 5 }, DuringTemp = { 9, 2, 6, 5 };

    public static void Run()
    {
        string project = Path.GetDirectoryName(Application.dataPath);
        Require(File.Exists(Path.Combine(project, ".wildlife-journey-validation")) &&
            Application.platform == RuntimePlatform.WindowsEditor && !EditorApplication.isPlayingOrWillChangePlaymode,
            "Run only in Edit mode in the marked Windows validation project.");
        Require(!SessionState.GetBool(Key + "active", false), "Do not replace an existing pending UI review.");
        string folder = Path.Combine(project, "JourneyReview", "ui-restore-fixture-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        string[] boolKeys = { "active", "ending", "success", "editConfirmed", "restoreBlocked", "background", "paused", "notebook.existed", "notebook-temp.existed" };
        string[] stringKeys = { "folder", "startScene", "runtimeError", "checkpoint" };
        var savedBools = boolKeys.ToDictionary(k => k, k => SessionState.GetBool(Key + k, false));
        var savedStrings = stringKeys.ToDictionary(k => k, k => SessionState.GetString(Key + k, ""));
        object previousReport = ReportField.GetValue(null);
        bool background = Application.runInBackground, paused = EditorApplication.isPaused;
        SceneAsset startScene = EditorSceneManager.playModeStartScene;
        var result = new StringBuilder();
        int exit = 1;
        try
        {
            CheckCorruptBackup(Path.Combine(folder, "corrupt-backup"), background, paused, startScene, result);
            CheckReportConflict(Path.Combine(folder, "report-conflict"), background, paused, startScene, result);
            CheckEditConfirmation(Path.Combine(folder, "edit-confirmation"), background, paused, startScene, result);
            result.AppendLine("PASS synthetic files only; no GameplaySession.SavePath, real notebook, Play entry or scene save was used.");
            exit = 0;
        }
        catch (Exception error) { result.AppendLine("FAIL " + error); }
        finally
        {
            foreach (KeyValuePair<string, bool> value in savedBools) SessionState.SetBool(Key + value.Key, value.Value);
            foreach (KeyValuePair<string, string> value in savedStrings) SessionState.SetString(Key + value.Key, value.Value);
            ReportField.SetValue(null, previousReport);
            Application.runInBackground = background; EditorApplication.isPaused = paused;
            EditorSceneManager.playModeStartScene = startScene;
            File.WriteAllText(Path.Combine(folder, "results.txt"), result.ToString());
            Debug.Log("UI restoration fixture: " + folder + "\n" + result);
            EditorApplication.Exit(exit);
        }
    }

    static void CheckCorruptBackup(string folder, bool background, bool paused, SceneAsset startScene, StringBuilder result)
    {
        string live = Prepare(folder, background, paused, startScene);
        File.WriteAllBytes(Path.Combine(folder, "notebook-temp.before"), new byte[] { 99 });
        Call("Restore");
        RequireBlocked();
        RequireBytes(live, During); RequireBytes(live + ".tmp", DuringTemp);
        RequireBytes(Path.Combine(folder, "notebook.before"), Original);
        RequireOptions(background, paused, startScene);
        Require((string)Field("status") == "RESTORE_FAILED", "Corrupt backup did not produce an explicit failure report.");
        // Repair alone must not trigger an unbounded retry loop; only the explicit retry action resumes restoration.
        File.WriteAllBytes(Path.Combine(folder, "notebook-temp.before"), OriginalTemp);
        Call("Tick");
        RequireBlocked(); RequireBytes(live, During); RequireBytes(live + ".tmp", DuringTemp);
        WatersideGameplayPlayReview.RetryRestore();
        RequireCompleted(live, folder, background, paused, startScene);
        result.AppendLine("PASS second backup corruption leaves BOTH live files unchanged, retains pending/backups and restores editor options; Tick stays blocked; repair plus explicit RetryRestore restores original bytes.");
    }

    static void CheckReportConflict(string folder, bool background, bool paused, SceneAsset startScene, StringBuilder result)
    {
        string live = Prepare(folder, background, paused, startScene);
        string final = Path.Combine(folder, "checks.json");
        byte[] occupied = Encoding.UTF8.GetBytes("synthetic occupied report; must never be overwritten");
        File.WriteAllBytes(final, occupied);
        // A mapped/occupied report must not affect per-step checkpoints or be silently replaced at completion.
        using (var locked = new FileStream(final, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            Call("Save"); Call("Save");
            RequireBytes(final, occupied);
            Require(!Directory.GetFiles(folder, "*.tmp").Any(p => p != live + ".tmp"), "Per-step checkpoint unexpectedly wrote a temporary report.");
            object loaded = Helper.GetMethod("Load", StaticPrivate).Invoke(null, null);
            Require((string)loaded.GetType().GetField("profilePath").GetValue(loaded) == live,
                "Load did not prefer its valid SessionState checkpoint over the occupied disk report.");
            Call("Restore");
            RequireBlocked();
            RequireBytes(final, occupied);
            RequireBytes(live, Original); RequireBytes(live + ".tmp", OriginalTemp);
            RequireBytes(Path.Combine(folder, "notebook.before"), Original);
            RequireBytes(Path.Combine(folder, "notebook-temp.before"), OriginalTemp);
            RequireOptions(background, paused, startScene);
            Call("Tick"); RequireBlocked();
        }
        File.Move(final, Path.Combine(folder, "occupied-report.before"));
        WatersideGameplayPlayReview.RetryRestore();
        RequireCompleted(live, folder, background, paused, startScene);
        RequireBytes(Path.Combine(folder, "occupied-report.before"), occupied);
        result.AppendLine("PASS repeated checkpoints use SessionState while the disk report is occupied; Load prefers that checkpoint. Final-report creation conflict preserves existing bytes and pending/backups/options; Tick does not retry. Moving the conflicting file aside plus RetryRestore creates the final report once.");
    }

    static void CheckEditConfirmation(string folder, bool background, bool paused, SceneAsset startScene, StringBuilder result)
    {
        string live = Prepare(folder, background, paused, startScene);
        SessionState.SetBool(Key + "editConfirmed", false);
        Call("Restore");
        Require(SessionState.GetBool(Key + "active", false) && SessionState.GetBool(Key + "ending", false), "Unconfirmed Edit mode cleared pending cleanup.");
        RequireBytes(live, During); RequireBytes(live + ".tmp", DuringTemp);
        // This is a synchronous callback fixture, not a claim that a real domain reload was executed.
        Call("Changed", PlayModeStateChange.EnteredEditMode);
        RequireCompleted(live, folder, background, paused, startScene);
        result.AppendLine("PASS unconfirmed Edit mode cannot restore or clear pending; explicit EnteredEditMode callback completes restoration (synthetic lifecycle check, no real domain reload).");
    }

    static string Prepare(string folder, bool background, bool paused, SceneAsset startScene)
    {
        Require(!SessionState.GetBool(Key + "active", false), "Previous synthetic restoration must complete before the next case.");
        Directory.CreateDirectory(folder);
        string live = Path.Combine(folder, "fake-notebook.bin"), scene = Path.Combine(folder, "fake-scene.txt");
        File.WriteAllBytes(live, During); File.WriteAllBytes(live + ".tmp", DuringTemp);
        File.WriteAllBytes(Path.Combine(folder, "notebook.before"), Original);
        File.WriteAllBytes(Path.Combine(folder, "notebook-temp.before"), OriginalTemp);
        File.WriteAllText(scene, "synthetic immutable scene bytes");
        SessionState.SetString(Key + "folder", folder); SessionState.SetString(Key + "runtimeError", "");
        SessionState.SetString(Key + "startScene", AssetDatabase.GetAssetPath(startScene));
        foreach (string flag in new[] { "active", "ending", "success", "editConfirmed", "notebook.existed", "notebook-temp.existed" }) SessionState.SetBool(Key + flag, true);
        SessionState.SetBool(Key + "restoreBlocked", false);
        SessionState.SetBool(Key + "background", background); SessionState.SetBool(Key + "paused", paused);
        Application.runInBackground = !background; EditorApplication.isPaused = !paused;
        EditorSceneManager.playModeStartScene = null;
        object report = Activator.CreateInstance(Helper.GetNestedType("Report", BindingFlags.NonPublic), true);
        ReportField.SetValue(null, report);
        Set("scene", scene); Set("sceneHash", Hash(File.ReadAllBytes(scene)));
        Set("profilePath", live); Set("profileHash", Hash(Original)); Set("profileTempHash", Hash(OriginalTemp));
        Set("startedUtc", DateTime.UtcNow.ToString("O"));
        Call("Save");
        return live;
    }

    static void RequireBlocked()
    {
        Require(SessionState.GetBool(Key + "active", false) && SessionState.GetBool(Key + "ending", false) &&
            SessionState.GetBool(Key + "restoreBlocked", false), "Restoration failure lost its pending/blocked state.");
    }

    static void RequireCompleted(string live, string folder, bool background, bool paused, SceneAsset startScene)
    {
        RequireBytes(live, Original); RequireBytes(live + ".tmp", OriginalTemp);
        RequireBytes(Path.Combine(folder, "notebook.before"), Original); RequireBytes(Path.Combine(folder, "notebook-temp.before"), OriginalTemp);
        RequireOptions(background, paused, startScene);
        Require(!SessionState.GetBool(Key + "active", false) && !SessionState.GetBool(Key + "ending", false) &&
            !SessionState.GetBool(Key + "restoreBlocked", false), "Successful retry did not release pending restoration.");
        Require((bool)Field("notebookRestored") && (bool)Field("returnedToEdit") && (bool)Field("editorOptionsRestored"), "Restored result flags are incorrect.");
        Require((string)Field("status") == "FAIL", "Synthetic files contain no UI screenshots and must never claim a UI PASS.");
        string final = Path.Combine(folder, "checks.json");
        Require(File.Exists(final) && File.ReadAllText(final) == SessionState.GetString(Key + "checkpoint", ""),
            "Successful restoration did not create the final report from its last checkpoint.");
    }

    static void RequireOptions(bool background, bool paused, SceneAsset startScene)
    {
        Require(Application.runInBackground == background && EditorApplication.isPaused == paused &&
            EditorSceneManager.playModeStartScene == startScene, "Editor options were not restored despite a notebook/report failure.");
    }
    static void RequireBytes(string path, byte[] expected) => Require(File.ReadAllBytes(path).SequenceEqual(expected), "Unexpected synthetic bytes at " + path);
    static string Hash(byte[] bytes) { using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)); }
    static object Field(string name) { object report = ReportField.GetValue(null); return report.GetType().GetField(name).GetValue(report); }
    static void Set(string name, object value) { object report = ReportField.GetValue(null); report.GetType().GetField(name).SetValue(report, value); }
    static void Call(string name, params object[] args)
    {
        try { Helper.GetMethod(name, StaticPrivate).Invoke(null, args); }
        catch (TargetInvocationException error) when (error.InnerException != null) { throw error.InnerException; }
    }
    static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
}
