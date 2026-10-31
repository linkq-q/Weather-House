#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using WeatherHousePrototype;

namespace WeatherHouse.Life
{
    public static class LifeWeatherTestUnlock
    {
        [MenuItem("Weather House/Weather Effects/解锁当前存档天气魔法（测试）")]
        public static void UnlockCurrent()
        {
            string path = Path.Combine(GameplaySession.PersistenceDirectory, "life-progress.json");
            if (!File.Exists(path)) throw new InvalidOperationException("当前存档不存在，未创建或修改其他存档。");
            var life = Application.isPlaying ? UnityEngine.Object.FindObjectOfType<LifeSession>() : null;
            if (Application.isPlaying && (!life || !life.World || life.SavePath != path))
                throw new InvalidOperationException("等待当前游戏的生活存档载入完成后再解锁。");
            string backup = path + ".before-weather-test-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + ".bak";
            File.Copy(path, backup);
            var progress = life ? life.Progress : JsonUtility.FromJson<LifeProgress>(File.ReadAllText(path));
            Apply(progress);
            if (life) life.SaveAndNotify("");
            else LifeSaveFile.Write(path, JsonUtility.ToJson(progress, true));
            var saved = JsonUtility.FromJson<LifeProgress>(File.ReadAllText(path));
            if (!saved.weatherMagicTestUnlocked || (life && !string.IsNullOrEmpty(life.LastSaveError)))
                throw new IOException("天气测试解锁未能保存；备份位于 " + backup);
            Directory.CreateDirectory("Temp/WeatherPrefabReview");
            File.WriteAllText("Temp/WeatherPrefabReview/unlock-result.txt", "PASS\nSave=" + path + "\nBackup=" + backup +
                "\nPlaying=" + Application.isPlaying + "\nLearned=" + string.Join(",", saved.learnedGestures));
            Debug.Log("当前存档的六种天气手势已解锁并学会。备份：" + backup);
        }
        public static void Apply(LifeProgress progress)
        {
            if (progress == null) throw new InvalidOperationException("无法读取生活存档。");
            progress.weatherMagicTestUnlocked = true;
            if (progress.learnedGestures == null) progress.learnedGestures = new System.Collections.Generic.List<string>();
            foreach (var gesture in GestureCatalog.All)
                if (!progress.learnedGestures.Contains(gesture.id)) progress.learnedGestures.Add(gesture.id);
        }
    }
}
#endif
