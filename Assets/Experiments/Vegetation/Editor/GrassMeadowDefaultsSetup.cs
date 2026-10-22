using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace WeatherHouse.Vegetation.Editor
{
    public static class GrassMeadowDefaultsSetup
    {
        [MenuItem("Weather House/Vegetation Lab/草地区域默认值 · 接入保存")]
        public static void Install()
        {
            var scene = SceneManager.GetActiveScene();
            if (EditorApplication.isPlayingOrWillChangePlaymode || scene.isDirty || scene.path != VegetationLabBuilder.ScenePath)
                throw new InvalidOperationException("请先打开并保存 VegetationLab，退出 Play 后接入区域默认值。");
            var view = Object.FindObjectOfType<GrassLabView>(true);
            if (!view || view.regions == null || view.regions.Any(m => !m)) throw new InvalidOperationException("草地区域引用不完整。");
            string folder = VegetationLabBuilder.Generated + "/MeadowDefaults";
            Directory.CreateDirectory(folder); AssetDatabase.Refresh();
            for (int i = 0; i < view.regions.Length; i++)
            {
                var meadow = view.regions[i];
                if (meadow.defaults) continue;
                string path = folder + "/Region" + i + ".asset";
                var defaults = AssetDatabase.LoadAssetAtPath<GrassMeadowDefaults>(path);
                if (!defaults)
                {
                    defaults = ScriptableObject.CreateInstance<GrassMeadowDefaults>();
                    defaults.Capture(meadow); AssetDatabase.CreateAsset(defaults, path);
                }
                Undo.RecordObject(meadow, "接入草地区域默认值");
                meadow.defaults = defaults; EditorUtility.SetDirty(meadow);
            }
            EditorSceneManager.SaveScene(scene);
        }
    }
}
