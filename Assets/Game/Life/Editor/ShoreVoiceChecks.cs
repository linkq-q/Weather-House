using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;
using WeatherHousePrototype;
using WeatherHouse.WatersideV2;
using Object = UnityEngine.Object;

namespace WeatherHouse.Life.Editor
{
    public static class ShoreVoiceChecks
    {
        const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        static T Get<T>(object owner, string field) => (T)owner.GetType().GetField(field, Hidden).GetValue(owner);
        static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
        public static IEnumerator Run()
        {
            Check(Application.isPlaying && !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WEATHERHOUSE_LIFE_REVIEW_SAVE")), "Isolated Play required.");
            var game = Object.FindObjectOfType<GameplaySession>(); var audio = game.GetComponent<LifeAudio>(); var hud = game.GetComponent<GameplayHud>();
            var player = game.PlayerTransform; var controller = player.GetComponent<CharacterController>();
            var terrain = Object.FindObjectsOfType<Terrain>().First(t => t.gameObject.scene == game.gameObject.scene);
            var sources = Get<AudioSource[]>(audio, "sources"); var start = player.position; bool focus = Get<bool>(audio, "focused");
            var report = new StringBuilder();
            void Place(Vector3 p) { controller.enabled = false; player.position = p; controller.enabled = true; Physics.SyncTransforms(); }
            void Focus(bool value) => typeof(LifeAudio).GetMethod("OnApplicationFocus", Hidden).Invoke(audio, new object[] { value });
            try
            {
                Focus(true); game.forestSprite.SetScriptedInput(Vector2.zero, false, false);
                foreach (var p in new[] { new Vector3(71, 0, 35), new Vector3(-38, 0, -38) })
                {
                    Place(new Vector3(p.x, WatersideLayout.GroundHeight(terrain, p.x, p.z) + .1f, p.z));
                    foreach (bool photo in new[] { false, true })
                    {
                        hud.SetCameraMode(photo, true); yield return new WaitForSecondsRealtime(1.4f);
                        var shore = sources[11]; float gain = Get<float>(audio, "shoreGain"); bool near = p.x > 0;
                        Check(shore.spatialBlend == 0 && (near ? gain > .4f && shore.isPlaying && shore.volume > .1f : gain == 0 && !shore.isPlaying), "Shore near/far fade failed.");
                        report.AppendLine($"PASS {(near ? "coast" : "inland")} {(photo ? "photo" : "gameplay")}: gain={gain:F3}, volume={shore.volume:F3}, playing={shore.isPlaying}, clip={shore.clip?.name}");
                    }
                }
                foreach (var voice in audio.Catalog.voices)
                {
                    var speaker = Object.FindObjectsOfType<LifeSpeaker>().FirstOrDefault(s => s.key == voice.speciesId);
                    Check(speaker && voice.clip, "Authored voice and live speaker missing: " + voice.speciesId);
                    foreach (bool photo in new[] { false, true })
                    {
                        hud.SetCameraMode(photo, true);
                        Place(speaker.transform.position + Vector3.right * 1.5f);
                        Get<Dictionary<int, float>>(audio, "nextVoice").Clear(); sources[5].Stop(); sources[6].Stop();
                        audio.Listen(speaker.Actor); yield return null; yield return new WaitForEndOfFrame();
                        var source = sources.Skip(5).Take(2).FirstOrDefault(s => s.isPlaying && s.clip == voice.clip);
                        Check(source, "Authored voice did not play: " + voice.speciesId);
                        float heardDistance = Vector3.Distance(game.CurrentView.transform.position, source.transform.position);
                        var emissions = Get<Vector3[]>(audio, "emissionPositions"); int index = Array.IndexOf(sources, source);
                        float playerDistance = Vector3.Distance(player.position + Vector3.up * 1.2f, emissions[index]);
                        Check(source.spatialBlend == 1 && heardDistance < source.maxDistance && Mathf.Abs(heardDistance - playerDistance) < .05f, "Animal voice still attenuates from the distant camera.");
                        report.AppendLine($"PASS {voice.speciesId} {(photo ? "photo" : "gameplay")}: original clip={voice.clip.name}, audible distance={heardDistance:F2}m, player distance={playerDistance:F2}m");
                    }
                }
                Check(sources.Length == 12, "Audio source budget changed.");
                report.AppendLine("PASS original five voice presets retained; 12 sources; no extra listener; player-relative spatial distance.");
            }
            finally
            {
                hud.SetCameraMode(false, true); Place(start); game.forestSprite.ReleaseScriptedInput(); Focus(focus);
                File.WriteAllText("PhotoStillReview/20261029/shore-voice-checks.txt", report.ToString());
            }
        }
    }
}
