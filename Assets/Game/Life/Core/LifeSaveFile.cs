using System.IO;
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
using System.ComponentModel;
using System.Runtime.InteropServices;
#endif

namespace WeatherHouse.Life
{
    // Stage beside the destination. A failed replacement must never delete the last good save.
    internal static class LifeSaveFile
    {
        internal static void Write(string path, string json)
        {
            string staged = path + ".tmp";
            File.WriteAllText(staged, json);
            if (!File.Exists(path)) { File.Move(staged, path); return; }
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
            try { File.Replace(staged, path, null); }
            // Windows 1175 leaves both original names intact. Mono's ReplaceFile can fail
            // here while a same-volume rename succeeds; other failures still propagate.
            catch (IOException error) when ((error.HResult & 0xffff) == 1175)
            {
                MoveReplacementOnWindows(staged, path);
            }
#else
            File.Replace(staged, path, null);
#endif
        }

#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
        private static void MoveReplacementOnWindows(string staged, string path)
        {
            const int ReplaceExisting = 1, WriteThrough = 8;
            if (!MoveFileEx(staged, path, ReplaceExisting | WriteThrough))
                throw new IOException("The staged save could not replace the existing save.",
                    new Win32Exception(Marshal.GetLastWin32Error()));
        }

        [DllImport("kernel32.dll", EntryPoint = "MoveFileExW", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool MoveFileEx(string existing, string destination, int flags);
#endif
    }
}
