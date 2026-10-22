using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace WeatherHouseQuadrupedMotion.Editor
{
    [InitializeOnLoad]
    public static class QuadrupedMotionSession
    {
        [Serializable] sealed class Status
        {
            public string request, command, completedAt, error;
            public bool success, capturing, playing;
        }
        static double lastPoll;
        static QuadrupedMotionSession() { EditorApplication.update += Poll; }

        static void Poll()
        {
            if (EditorApplication.isCompiling || EditorApplication.timeSinceStartup - lastPoll < .5) return;
            lastPoll = EditorApplication.timeSinceStartup;
            string path = QuadrupedMotionChecks.Review + "/request.txt";
            if (!File.Exists(path)) return;
            string request = File.ReadAllText(path).Trim();
            File.Delete(path);
            string[] parts = request.Split('|');
            string command = parts[parts.Length - 1];
            var status = new Status { request = request, command = command };
            try
            {
                switch (command)
                {
                    case "prepare": QuadrupedMotionPreview.PrepareAssets(); break;
                    case "open": QuadrupedMotionPreview.Open(); break;
                    case "check": QuadrupedMotionChecks.ValidateAndCapture(); break;
                    case "diagnose": QuadrupedMotionChecks.DiagnoseGeometry(); break;
                    case "capture": QuadrupedMotionChecks.StartSequenceCapture(); break;
                    case "check-capture": QuadrupedMotionChecks.ValidateAndCapture(); QuadrupedMotionChecks.StartSequenceCapture(); break;
                    case "prepare-check-capture": QuadrupedMotionPreview.PrepareAssets(); QuadrupedMotionChecks.ValidateAndCapture(); QuadrupedMotionChecks.StartSequenceCapture(); break;
                    default: throw new ArgumentException("Unknown quadruped preview command: " + command);
                }
                status.success = true;
            }
            catch (Exception exception)
            {
                status.error = exception.ToString();
                File.WriteAllText(QuadrupedMotionChecks.Review + "/last_error.txt", status.error);
            }
            status.capturing = QuadrupedMotionChecks.IsCapturing;
            status.playing = EditorApplication.isPlaying;
            status.completedAt = DateTimeOffset.Now.ToString("o");
            File.WriteAllText(QuadrupedMotionChecks.Review + "/status.json", JsonUtility.ToJson(status, true));
        }
    }
}
