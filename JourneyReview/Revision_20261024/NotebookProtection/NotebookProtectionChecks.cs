using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using WeatherHouse.WildlifeJourney.Editor;

[InitializeOnLoad]
public static class NotebookProtectionChecks
{
    const string Key = "WeatherHouse.EcologyReview.", TestKey = "WeatherHouse.NotebookProtectionTest.";
    const string Report = "NotebookProtectionReview/results.txt";
    static readonly string Domain = Guid.NewGuid().ToString("N");
    static readonly Type Review = typeof(JourneyEcologyPlayReview);
    static NotebookProtectionChecks() { EditorApplication.update += AfterReload; }
    static object Call(string name, params object[] args)
    {
        try { return Review.GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, args); }
        catch (TargetInvocationException error) { throw error.InnerException; }
    }
    static void Set(string name, object value) => Review.GetField(name, BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, value);
    static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    static void ExpectFailure(Action action)
    {
        bool failed = false;
        try { action(); } catch (IOException) { failed = true; }
        Check(failed, "Expected an IO failure.");
    }
    static string Hash(string path) => (string)Call("FileHash", path);
    static void Log(string value) { File.AppendAllText(Report, value + "\n"); Debug.Log(value); }

    public static void Run()
    {
        Check(File.Exists(".wildlife-journey-validation") && !EditorApplication.isPlayingOrWillChangePlaymode, "Isolated Edit fixture only.");
        Check(!SessionState.GetBool(Key + "running", false) && !SessionState.GetBool(Key + "cleanup", false), "Do not replace a pending review.");
        Directory.CreateDirectory("NotebookProtectionReview"); File.WriteAllText(Report, "Only synthetic notebook files under the isolated project are read/written.\n");
        string root = Path.GetFullPath("NotebookProtectionReview/fake-" + DateTime.UtcNow.Ticks); Directory.CreateDirectory(root);
        try
        {
            for (int mask = 0; mask < 4; mask++)
            {
                var f = new Fixture(root, "presence-" + mask, (mask & 1) != 0, (mask & 2) != 0);
                f.Mutate(); string result = (string)Call("RestoreNotebook", f.manifest); f.AssertRestored();
                Check(result.Contains("original=") && result.Contains("restored="), "Missing SHA256 restore evidence.");
                Check(Directory.GetFiles(f.report, "*.during-review-*").Length == 2, "Review outputs were not retained.");
            }
            Log("PASS all four initial notebook/tmp presence combinations; binary bytes and absence restored, both review outputs retained");

            var locked = new Fixture(root, "backup-locked", true, true);
            string failingReport = Path.Combine(root, "locked-copy"); Directory.CreateDirectory(failingReport);
            using (var handle = File.Open(locked.path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                ExpectFailure(() => Call("BackupNotebook", locked.path, failingReport));
            locked.AssertRestored();
            Check(!File.Exists(Path.Combine(failingReport, "notebook-backup.json")) && !EditorApplication.isPlayingOrWillChangePlaymode &&
                !SessionState.GetBool(Key + "running", false), "Backup failure armed Play or a recovery manifest.");
            Log("PASS locked original fails backup before Play; original bytes remain intact");

            var damaged = new Fixture(root, "damaged-temp-backup", true, true); damaged.Mutate();
            File.WriteAllText(Path.Combine(damaged.report, "notebook-temp.before"), "CORRUPTED SYNTHETIC BACKUP");
            string live = Hash(damaged.path), temp = Hash(damaged.path + ".tmp");
            damaged.Arm(); Call("Finish", "FAIL", "Synthetic restore-failure case", false);
            bool background = Application.runInBackground; SessionState.SetBool(Key + "background", background); Application.runInBackground = !background;
            Call("PlayChanged", PlayModeStateChange.EnteredEditMode);
            Check(Hash(damaged.path) == live && Hash(damaged.path + ".tmp") == temp, "Failed preflight changed a live notebook.");
            Check(SessionState.GetBool(Key + "cleanup", false) && SessionState.GetString(Key + "notebookManifest", "") == damaged.manifest &&
                Application.runInBackground == background, "Failure lost recovery state or editor options.");
            Check(File.ReadAllText(Path.Combine(damaged.report, "summary.txt")).Contains("FAIL notebook restore"), "Failure was not reported.");
            File.WriteAllBytes(Path.Combine(damaged.report, "notebook-temp.before"), damaged.beforeTemp);
            Call("CompleteCleanup"); damaged.AssertRestored();
            Check(!SessionState.GetBool(Key + "cleanup", false), "Repair and retry did not finish recovery.");
            Log("PASS corrupt second backup blocks all live-file changes, retains cleanup/manifest, restores editor options and supports retry; expected FAIL log above");

            foreach (string end in new[] { "normal", "exception", "manual-exit" })
            {
                var f = new Fixture(root, end, true, true); f.Mutate(); f.Arm();
                if (end == "manual-exit") Call("PlayChanged", PlayModeStateChange.ExitingPlayMode);
                else Call("Finish", end == "normal" ? "CAPTURED" : "FAIL", "Synthetic completion route: " + end, false);
                Check(SessionState.GetBool(Key + "cleanup", false) && Hash(f.path) != f.original, "Finish restored before EnteredEdit.");
                Call("PlayChanged", PlayModeStateChange.EnteredEditMode); f.AssertRestored();
                Check(!SessionState.GetBool(Key + "cleanup", false) && SessionState.GetString(Key + "notebookManifest", "") == "", "Successful cleanup retained pending flags.");
            }
            Log("PASS real Finish/PlayChanged method routing for normal, exception and manual-exit cases (callbacks invoked in Edit fixture)");

            var reload = new Fixture(root, "actual-domain-reload", true, true); reload.Mutate(); reload.Arm();
            Call("Finish", "FAIL", "Synthetic pending cleanup before actual domain reload", false);
            SessionState.SetString(TestKey + "path", reload.path); SessionState.SetString(TestKey + "original", reload.original);
            SessionState.SetString(TestKey + "temp", reload.temporary); SessionState.SetString(TestKey + "domain", Domain);
            SessionState.SetString(TestKey + "started", DateTime.UtcNow.Ticks.ToString()); SessionState.SetBool(TestKey + "pending", true);
            // Defer this pending recovery until the actual new domain re-registers the production Poll callback.
            EditorApplication.update -= (EditorApplication.CallbackFunction)Delegate.CreateDelegate(typeof(EditorApplication.CallbackFunction), Review.GetMethod("Poll", BindingFlags.Static | BindingFlags.NonPublic));
            EditorUtility.RequestScriptReload();
        }
        catch (Exception error) { Log("FAIL " + error); EditorApplication.Exit(1); }
    }

    static void AfterReload()
    {
        if (!SessionState.GetBool(TestKey + "pending", false) || SessionState.GetString(TestKey + "domain", "") == Domain) return;
        try
        {
            if (SessionState.GetBool(Key + "cleanup", false))
            {
                Check(DateTime.UtcNow.Ticks - long.Parse(SessionState.GetString(TestKey + "started", "0")) < TimeSpan.FromSeconds(60).Ticks, "Reload recovery timed out.");
                return;
            }
            string path = SessionState.GetString(TestKey + "path", "");
            Check(Hash(path) == SessionState.GetString(TestKey + "original", "") && Hash(path + ".tmp") == SessionState.GetString(TestKey + "temp", ""), "Reload lost original notebook bytes.");
            Check(SessionState.GetString(Key + "notebookManifest", "") == "" && !EditorApplication.isPlaying, "Reload did not complete Edit recovery.");
            SessionState.SetBool(TestKey + "pending", false); Log("PASS actual Unity domain reload recovered pending cleanup using SessionState plus disk manifest; Play=False");
            Log("PASS complete. No real player notebook, scene, request file, 210-second run or main Unity was touched."); EditorApplication.Exit(0);
        }
        catch (Exception error) { SessionState.SetBool(TestKey + "pending", false); Log("FAIL " + error); EditorApplication.Exit(1); }
    }

    sealed class Fixture
    {
        public readonly string path, report, manifest, original, temporary;
        public readonly byte[] beforeTemp;
        public Fixture(string root, string name, bool notebook, bool temp)
        {
            string directory = Path.Combine(root, name); Directory.CreateDirectory(directory);
            report = Path.Combine(directory, "report"); Directory.CreateDirectory(report);
            path = Path.Combine(directory, "language-notebook.json");
            if (notebook) File.WriteAllBytes(path, new byte[] { 0, 255, 1, 13, 10, 42 });
            beforeTemp = new byte[] { 254, 0, 9, 13, 10, 17 };
            if (temp) File.WriteAllBytes(path + ".tmp", beforeTemp);
            original = Hash(path); temporary = Hash(path + ".tmp"); manifest = (string)Call("BackupNotebook", path, report);
        }
        public void Mutate() { File.WriteAllText(path, "SYNTHETIC PLAY OUTPUT"); File.WriteAllText(path + ".tmp", "SYNTHETIC TEMP OUTPUT"); }
        public void AssertRestored() { Check(Hash(path) == original && Hash(path + ".tmp") == temporary, "Original file state did not round-trip."); }
        public void Arm()
        {
            SessionState.SetString(Key + "folder", report); SessionState.SetString(Key + "notebookManifest", manifest);
            SessionState.SetBool(Key + "running", true); SessionState.SetBool(Key + "cleanup", false); SessionState.SetBool(Key + "entered", true);
            SessionState.SetBool(Key + "background", Application.runInBackground); SessionState.SetBool(Key + "paused", EditorApplication.isPaused);
            Set("folder", report); Set("notebookRecoveryAttempted", false);
        }
    }
}
