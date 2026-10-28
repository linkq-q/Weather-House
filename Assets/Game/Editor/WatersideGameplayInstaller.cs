using System;
using System.IO;
using System.Linq;
using ForestSpriteCharacter;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using WeatherHouse.Vegetation;

namespace WeatherHousePrototype.Editor
{
    // Adds the established gameplay layer to the existing map. Does not rebuild any scene content.
    public static class WatersideGameplayInstaller
    {
        const string ScenePath = "Assets/Scenes/WeatherHouse_WatersideV2.unity";
        const string Folder = "WatersideV3Review/gameplay-ui";

        [MenuItem("Weather House/Waterside V2/Restore Existing Gameplay UI")]
        public static void Install()
        {
            var scene = SceneManager.GetActiveScene();
            Require(!EditorApplication.isPlayingOrWillChangePlaymode && scene.path == ScenePath && !scene.isDirty,
                "Open the saved Waterside V2 scene outside Play before restoring gameplay UI.");
            var players = Components<ForestSpriteDemoController>(scene);
            Require(players.Length == 1 && players[0].demoCamera, "Expected the existing ForestSprite and its view camera.");
            Require(Components<PrototypeWalker>(scene).Length == 0, "Do not install a second player controller.");
            var existing = Components<GameplaySession>(scene);
            if (existing.Length > 0) { CheckBindings(); return; }
            var orbit = players[0].demoCamera.GetComponent<VegetationTopDownCamera>();
            Require(orbit, "The existing top-down camera is missing.");
            var style = Resources.Load<TextAsset>("WeatherHouseUiStyle");
            Require(style, "The original UI style resource is missing.");
            var parsedStyle = ProceduralUiStyle.Parse(style.text);
            var animals = WildlifeInteractionTarget.BindScene(scene);
            Require(animals.Length > 0, "The wildlife presentation adapter did not find any scene animals.");
            var root = new GameObject("11 手账、相机与动物交谈");
            Undo.RegisterCreatedObjectUndo(root, "恢复现有游戏 UI");
            var session = root.AddComponent<GameplaySession>();
            session.forestSprite = players[0]; session.walker = null; session.animals = animals;
            session.regions = Components<WeatherRegion>(scene);
            var hud = root.GetComponent<GameplayHud>();
            hud.uiStyle = parsedStyle;
            // Preserve the tuned sizes in WeatherHouse_MapDraft's existing HUD.
            hud.animalBubbleSize = new Vector2(300, 100); hud.animalGlyphHeight = 43;
            hud.animalGlyphStrokeWidth = 9;
            Undo.RecordObject(orbit, "给动物交互保留 E 键");
            orbit.turnLeftKey = KeyCode.LeftBracket; orbit.turnRightKey = KeyCode.RightBracket;
            EditorUtility.SetDirty(orbit); EditorUtility.SetDirty(session); EditorUtility.SetDirty(hud);
            EditorSceneManager.MarkSceneDirty(scene);
            Directory.CreateDirectory(Folder);
            File.WriteAllText(Folder + "/installation.txt", CheckBindings() + "\nScene is dirty; caller owns saving. E=交谈, C=相机, J=手账, Esc=关闭, [ / ] 或鼠标右键=旋转镜头。\n");
        }

        public static string CheckBindings()
        {
            var scene = SceneManager.GetActiveScene();
            var sessions = Components<GameplaySession>(scene);
            Require(sessions.Length == 1 && sessions[0].isActiveAndEnabled, "Expected one active GameplaySession.");
            var session = sessions[0]; var hud = session.GetComponent<GameplayHud>();
            Require(hud && hud.enabled && session.forestSprite && !session.walker && session.PlayerViewCamera,
                "Existing HUD must bind directly to the ForestSprite controller and camera.");
            Require(Components<PrototypeWalker>(scene).Length == 0 && Components<ForestSpriteDemoController>(scene).Length == 1,
                "Exactly one player controller must remain.");
            Require(session.animals != null && session.animals.Length > 0 && session.animals.All(a => a && a.gameObject.scene == scene),
                "Scene animal observations must be bound.");
            Require(session.animals.Distinct().Count() == session.animals.Length, "Duplicate animals in gameplay bindings.");
            var orbit = session.PlayerViewCamera.GetComponent<VegetationTopDownCamera>();
            Require(orbit && orbit.turnLeftKey != KeyCode.E && orbit.turnRightKey != KeyCode.E,
                "E must not also rotate the camera.");
            return "PASS bindings: original GameplayHud, ForestSprite player=1, legacy walkers=0, animals=" + session.animals.Length + "; existing UI style and tuned bubble sizes restored.";
        }

        static T[] Components<T>(Scene scene) where T : Component => scene.GetRootGameObjects()
            .SelectMany(r => r.GetComponentsInChildren<T>(true)).ToArray();
        static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    }
}
