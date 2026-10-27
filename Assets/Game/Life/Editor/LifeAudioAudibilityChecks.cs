using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
using WeatherHousePrototype;

namespace WeatherHouse.Life.Editor
{
    // The root runner owns Play and isolated saves. Sampling uses the actual source and listener output.
    public static class LifeAudioAudibilityChecks
    {
        const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        sealed class Output
        {
            readonly float[] sourceData = new float[2048], listenerData = new float[2048];
            public int playingSamples;
            public float sourceRms, listenerRms, sourcePeak, listenerPeak;
            public void Sample(AudioSource source, AudioClip clip)
            {
                if (!source.isPlaying || source.clip != clip) return;
                playingSamples++;
                source.GetOutputData(sourceData, 0); AudioListener.GetOutputData(listenerData, 0);
                Measure(sourceData, ref sourceRms, ref sourcePeak); Measure(listenerData, ref listenerRms, ref listenerPeak);
            }
            static void Measure(float[] data, ref float maximumRms, ref float maximumPeak)
            {
                double sum = 0;
                foreach (float value in data) { sum += value * value; maximumPeak = Mathf.Max(maximumPeak, Mathf.Abs(value)); }
                maximumRms = Mathf.Max(maximumRms, (float)Math.Sqrt(sum / data.Length));
            }
            public string Describe(string label) => label + " samples=" + playingSamples +
                " sourceMaxBlockRmsDb=" + Db(sourceRms) + " listenerMaxBlockRmsDb=" + Db(listenerRms) +
                " sourcePeakDb=" + Db(sourcePeak) + " listenerPeakDb=" + Db(listenerPeak);
        }
        static string Db(float value) => (20 * Mathf.Log10(Mathf.Max(1e-9f, value))).ToString("F2");
        public static IEnumerator Run(LifeSession life, Action<string> completed)
        {
            var lines = new List<string>(); IEnumerator routine = Steps(life, lines); Exception failure = null;
            while (true)
            {
                bool more; object next = null;
                try { more = routine.MoveNext(); if (more) next = routine.Current; }
                catch (Exception error) { failure = error; break; }
                if (!more) break; yield return next;
            }
            try { (routine as IDisposable)?.Dispose(); } catch (Exception error) { failure = failure ?? error; }
            string result = (failure == null ? "PASS" : "FAIL") + " actual paper and shutter output\n" + string.Join("\n", lines) +
                "\nBoundary: isolated saves and focus; other sources muted temporarily to isolate measured digital output. " +
                "Book methods and GameplaySession.TakePhoto are the actual product paths. Digital RMS does not certify OS output routing, speaker volume or human listening." +
                (failure == null ? "" : "\n" + failure);
            File.WriteAllText("LifeGameplayReview/20261026/play/audio-audibility-result.txt", result); completed?.Invoke(result);
        }
        static IEnumerator Steps(LifeSession life, List<string> lines)
        {
            Check(Application.isPlaying && life && !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WEATHERHOUSE_LIFE_REVIEW_SAVE")), "Isolated Play required", lines);
            var audio = life.GetComponent<LifeAudio>(); var hud = life.GetComponent<GameplayHud>(); var game = life.Gameplay;
            Check(audio && audio.Catalog && hud && !hud.JournalOpen && !hud.CameraMode && !hud.IsDialogueOpen, "Audio and player UI are idle", lines);
            AudioSource source = Get<AudioSource[]>(audio, "sources")[0];
            var muted = UnityEngine.Object.FindObjectsOfType<AudioSource>().Where(s => s != source).ToDictionary(s => s, s => s.mute);
            bool originalMute = source.mute, focus = Get<bool>(audio, "focused");
            string notebook = JsonUtility.ToJson(game.Notebook);
            try
            {
                Call(audio, "OnApplicationFocus", true);
                foreach (AudioSource other in muted.Keys) other.mute = true;
                source.mute = false; source.Stop();
                Check(source.spatialBlend == 0 && AudioListener.volume > 0 && !AudioListener.pause &&
                    UnityEngine.Object.FindObjectsOfType<AudioListener>().Count(a => a.isActiveAndEnabled) == 1,
                    "Paper and Camera share an unmuted 2D source and one active listener", lines);
                AudioSettings.GetDSPBufferSize(out int buffer, out int buffers);
                lines.Add("OUTPUT sampleRate=" + AudioSettings.outputSampleRate + " dspBuffer=" + buffer + "x" + buffers +
                    " listenerVolume=" + AudioListener.volume + " appFocus=" + Application.isFocused + " audioFocus=" + Get<bool>(audio, "focused"));
                LifeAudioCatalog.Sound paper = audio.Catalog.Find(LifeAudioCue.Paper), camera = audio.Catalog.Find(LifeAudioCue.Camera);
                CheckClip(paper, "Paper", lines); CheckClip(camera, "Camera", lines);
                yield return null;
                int paperBefore = audio.Count(LifeAudioCue.Paper); hud.ToggleJournal();
                var book = life.GetComponent<LifeJournal3D>(); var cover = new Output(); var fan = new Output();
                float deadline = Time.unscaledTime + 12;
                while (hud.IsAlbumOpening && Time.unscaledTime < deadline)
                {
                    (book.IsOpeningBrowse ? fan : cover).Sample(source, paper.clips[0]); yield return null;
                }
                lines.Add(cover.Describe("Cover")); lines.Add(fan.Describe("Opening fan"));
                Check(!hud.IsAlbumOpening && book.OpeningTurnsCompleted == LifeJournalOpeningFan.Count &&
                    audio.Count(LifeAudioCue.Paper) == paperBefore + 2, "Actual open and four-leaf fan each issue one Paper cue", lines);
                CheckOutput(cover, "Cover", lines); CheckOutput(fan, "Opening fan", lines);
                paperBefore = audio.Count(LifeAudioCue.Paper); var turn = new Output(); book.Turn(() => { });
                deadline = Time.unscaledTime + 5;
                while (book.IsAnimating && Time.unscaledTime < deadline) { turn.Sample(source, paper.clips[0]); yield return null; }
                lines.Add(turn.Describe("Manual page turn"));
                Check(!book.IsAnimating && audio.Count(LifeAudioCue.Paper) == paperBefore + 1, "Actual manual turn issues one Paper cue", lines);
                CheckOutput(turn, "Manual page turn", lines);
                hud.ToggleJournal(); deadline = Time.unscaledTime + 5;
                while (hud.JournalOpen && Time.unscaledTime < deadline) yield return null;
                Check(!hud.JournalOpen, "Book closes before actual shutter fixture", lines);
                hud.SetCameraMode(true); deadline = Time.unscaledTime + 3;
                while (Get<float>(hud, "cameraBlend") < 1 && Time.unscaledTime < deadline) yield return null;
                Check(hud.CameraMode && game.PhotoRig.Active, "Player camera is raised through its real entry", lines);
                int cameraBefore = audio.Count(LifeAudioCue.Camera), photos = game.Notebook.observations.Count;
                var shutter = new Output(); game.TakePhoto();
                Check(game.Recorder.IsRecording, "GameplaySession.TakePhoto starts the real recorder", lines);
                deadline = Time.unscaledTime + 12;
                while (game.Recorder.IsRecording && Time.unscaledTime < deadline) { shutter.Sample(source, camera.clips[0]); yield return null; }
                shutter.Sample(source, camera.clips[0]); lines.Add(shutter.Describe("Actual shutter"));
                Check(!game.Recorder.IsRecording && game.Notebook.observations.Count == photos + 1 &&
                    audio.Count(LifeAudioCue.Camera) == cameraBefore + 1, "One real photograph produces one shutter and a stored observation", lines);
                CheckOutput(shutter, "Actual shutter", lines);
                hud.SetCameraMode(false);
            }
            finally
            {
                if (game.Recorder && game.Recorder.IsRecording) game.Recorder.FinishLiveCapture();
                if (hud.CameraMode) hud.SetCameraMode(false);
                if (hud.JournalOpen) { life.GetComponent<LifeJournal3D>()?.Cancel(); Call(hud, "CompleteJournalClose"); }
                source.Stop(); source.mute = originalMute;
                foreach (var pair in muted) if (pair.Key) pair.Key.mute = pair.Value;
                JsonUtility.FromJsonOverwrite(notebook, game.Notebook); Call(audio, "OnApplicationFocus", focus); game.RefreshInputBlocking();
            }
        }
        static void CheckClip(LifeAudioCatalog.Sound sound, string label, List<string> lines)
        {
            AudioClip clip = sound.clips[0]; var data = new float[clip.samples * clip.channels];
            Check(clip.loadState == AudioDataLoadState.Loaded && clip.GetData(data, 0), label + " imported PCM can be measured", lines);
            double sum = 0; int first = -1;
            for (int i = 0; i < data.Length; i++) { float value = data[i] * sound.volume; sum += value * value; if (first < 0 && Mathf.Abs(value) >= .01f) first = i; }
            float rms = (float)Math.Sqrt(sum / data.Length), onset = first / (float)(clip.frequency * clip.channels);
            lines.Add(label + " effectiveAssetRmsDb=" + Db(rms) + " onsetSeconds=" + onset.ToString("F4") + " gain=" + sound.volume + " seconds=" + clip.length);
            Check(rms >= .01f && first >= 0 && onset < .05f, label + " has an effective RMS above -40 dBFS and prompt onset", lines);
        }
        static void CheckOutput(Output output, string label, List<string> lines) => Check(output.playingSamples > 0 && output.sourceRms >= .008f &&
            output.listenerRms >= .006f && output.listenerPeak < .99f, label + " reaches measured source and listener output without clipping", lines);
        static T Get<T>(object target, string field) => (T)target.GetType().GetField(field, Hidden).GetValue(target);
        static void Call(object target, string method, params object[] args) => target.GetType().GetMethod(method, Hidden).Invoke(target, args);
        static void Check(bool value, string label, List<string> lines) { if (!value) throw new Exception(label); lines.Add("PASS " + label); }
    }
}
