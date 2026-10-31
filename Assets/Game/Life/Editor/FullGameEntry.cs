using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

namespace WeatherHouse.Life.Editor
{
    [InitializeOnLoad]
    public static class FullGameEntry
    {
        const string Key="WeatherHouse.FullGameEntry";
        static FullGameEntry(){EditorApplication.playModeStateChanged+=Changed;}
        [MenuItem("Weather House/试玩/完整开场（抽屉→水岸）")]
        public static void Play()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode||EditorApplication.isCompiling)return;
            var scene=SceneManager.GetActiveScene();
            if(scene.isDirty)
            {
                Directory.CreateDirectory("DrawerPrototypeReview");
                if(!EditorSceneManager.SaveScene(scene,"DrawerPrototypeReview/before-full-entry-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmssfff")+".unity",true))return;
            }
            SessionState.SetString(Key+".start",AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene));SessionState.SetBool(Key,true);
            EditorSceneManager.playModeStartScene=AssetDatabase.LoadAssetAtPath<SceneAsset>("Assets/Experiments/DrawerPrototype/DrawerPrototype.unity");
            EditorApplication.isPlaying=true;
        }
        static void Changed(PlayModeStateChange state)
        {
            if(state!=PlayModeStateChange.EnteredEditMode||!SessionState.GetBool(Key,false))return;
            string path=SessionState.GetString(Key+".start","");EditorSceneManager.playModeStartScene=string.IsNullOrEmpty(path)?null:AssetDatabase.LoadAssetAtPath<SceneAsset>(path);
            SessionState.SetBool(Key,false);
        }
    }
}
