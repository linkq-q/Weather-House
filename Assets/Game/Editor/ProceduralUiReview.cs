using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace WeatherHousePrototype.Editor
{
    public static class ProceduralUiReview
    {
        public const string PreviewPath = "PrototypeReview/Procedural_UI_Controls.png";
        public const string PrefabPath = "Assets/Game/Generated/UI Controls.prefab";

        [MenuItem("Weather House/Review Procedural UI Controls")]
        public static void Run()
        {
            if (EditorApplication.isPlaying)
                throw new InvalidOperationException("Stop Play mode before rendering the UI controls review.");
            ProceduralUiStyle.Reload();
            Scene activeScene = SceneManager.GetActiveScene();
            Scene preview = EditorSceneManager.NewPreviewScene();
            RenderTexture target = null;
            Texture2D result = null;
            RenderTexture previousTarget = RenderTexture.active;
            try
            {
                var cameraObject = new GameObject("UI review camera", typeof(Camera));
                SceneManager.MoveGameObjectToScene(cameraObject, preview);
                var camera = cameraObject.GetComponent<Camera>();
                camera.scene = preview;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(.90f, .92f, .87f);
                camera.orthographic = true;
                camera.orthographicSize = 500f;
                camera.transform.position = new Vector3(0f, 0f, -100f);
                camera.nearClipPlane = .1f;
                camera.farClipPlane = 200f;
                camera.cullingMask = 1 << 29;
                camera.enabled = false;
                target = new RenderTexture(1600, 1000, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
                camera.targetTexture = target;

                var canvasObject = new GameObject("Weather House UI Controls", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster));
                SceneManager.MoveGameObjectToScene(canvasObject, preview);
                canvasObject.layer = 29;
                var canvas = canvasObject.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = camera;
                canvas.planeDistance = 100f;
                canvas.sortingOrder = 1000;
                BuildSamples(canvas.transform);
                Canvas.ForceUpdateCanvases();
                CheckGeometry(canvas);
                camera.Render();
                RenderTexture.active = target;
                result = new Texture2D(1600, 1000, TextureFormat.RGB24, false);
                result.ReadPixels(new Rect(0, 0, 1600, 1000), 0, 0);
                result.Apply();
                Directory.CreateDirectory("PrototypeReview");
                File.WriteAllBytes(PreviewPath, result.EncodeToPNG());

                canvas.worldCamera = null;
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                SetLayer(canvasObject.transform, 5);
                Directory.CreateDirectory(Path.GetDirectoryName(PrefabPath));
                PrefabUtility.SaveAsPrefabAsset(canvasObject, PrefabPath);
                AssetDatabase.SaveAssets();
                File.WriteAllText("PrototypeReview/procedural-ui.txt",
                    "PASS: all seven procedural marks generate finite geometry at normal, wide, and compact sizes.\n" +
                    "Saved isolated 1600 x 1000 camera render and editable Canvas prefab.\n" +
                    "Current scene was not modified. Visual acceptance is separate from geometry checks.\n");
            }
            finally
            {
                RenderTexture.active = previousTarget;
                if (target != null) { target.Release(); Object.DestroyImmediate(target); }
                if (result != null) Object.DestroyImmediate(result);
                EditorSceneManager.ClosePreviewScene(preview);
                if (activeScene.IsValid() && activeScene.isLoaded) SceneManager.SetActiveScene(activeScene);
            }
        }

        private static void BuildSamples(Transform root)
        {
            Text(root, "Title", "WEATHER HOUSE  /  UI STUDY", new Vector2(-500f, 420f), new Vector2(500f, 40f), 23, TextAnchor.MiddleLeft);
            Text(root, "Subtitle", "PROCEDURAL SHAPES     01", new Vector2(510f, 420f), new Vector2(460f, 40f), 15, TextAnchor.MiddleRight);
            Text(root, "Dialogue heading", "DIALOGUE", new Vector2(-490f, 325f), new Vector2(500f, 40f), 16, TextAnchor.MiddleLeft);
            var animal = ProceduralUiLibrary.Create(root, "Animal bubble", UiMark.AnimalBubble,
                new Vector2(-470f, 206f), new Vector2(520f, 150f), ProceduralUiLibrary.Cream);
            ProceduralUiLibrary.Create(animal.transform, "Waiting ellipsis", UiMark.Ellipsis,
                new Vector2(0f, 7f), new Vector2(100f, 30f), ProceduralUiLibrary.Ink);
            Text(root, "Animal label", "ANIMAL  /  SOFT TAIL", new Vector2(-470f, 95f), new Vector2(500f, 30f), 14);
            var player = ProceduralUiLibrary.Create(root, "Player bubble", UiMark.PlayerBubble,
                new Vector2(-470f, -36f), new Vector2(520f, 142f), ProceduralUiLibrary.Grass);
            Text(player.transform, "Player text", "A quiet moment by the lake.", Vector2.zero, new Vector2(440f, 65f), 22);
            Text(root, "Player label", "PLAYER  /  SAME CONTOUR", new Vector2(-470f, -145f), new Vector2(500f, 30f), 14);
            ProceduralUiLibrary.Button(root, "Close", UiMark.Close, new Vector2(-668f, -267f), new Vector2(66f, 66f), ProceduralUiLibrary.Ink, 3f);
            Text(root, "Close label", "CLOSE  /  OPEN RING", new Vector2(-440f, -267f), new Vector2(350f, 35f), 15, TextAnchor.MiddleLeft);
            Text(root, "Camera heading", "CAMERA", new Vector2(465f, 325f), new Vector2(540f, 40f), 16, TextAnchor.MiddleLeft);
            ProceduralUiLibrary.Create(root, "Camera viewfinder", UiMark.Viewfinder,
                new Vector2(455f, 103f), new Vector2(550f, 338f), ProceduralUiLibrary.Ink, 3f);
            ProceduralUiLibrary.Create(root, "Small dot", UiMark.Dot,
                new Vector2(455f, -106f), new Vector2(9f, 9f), ProceduralUiLibrary.Ink);
            ProceduralUiLibrary.Button(root, "Shutter", UiMark.Shutter,
                new Vector2(455f, -226f), new Vector2(96f, 96f), ProceduralUiLibrary.Ink, 4f);
            Text(root, "ESC hint", "ESC   BACK", new Vector2(663f, -300f), new Vector2(200f, 35f), 16, TextAnchor.MiddleRight);
            Text(root, "Footer", "Rounded corners, gently uneven edges.  All marks resize as editable UI geometry.",
                new Vector2(0f, -420f), new Vector2(1450f, 40f), 16, TextAnchor.MiddleLeft);
        }

        private static void Text(Transform parent, string name, string value, Vector2 position, Vector2 size,
            int fontSize, TextAnchor alignment = TextAnchor.MiddleCenter)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            go.transform.SetParent(parent, false);
            go.layer = parent.gameObject.layer;
            var rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            var label = go.GetComponent<Text>();
            label.text = value;
            label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            ProceduralUiStyle.Current.ApplyText(label, fontSize);
            label.color = ProceduralUiLibrary.Ink;
            label.alignment = alignment;
            label.raycastTarget = false;
        }

        private static void SetLayer(Transform root, int layer)
        {
            root.gameObject.layer = layer;
            foreach (Transform child in root) SetLayer(child, layer);
        }

        private static void CheckGeometry(Canvas canvas)
        {
            foreach (var mark in canvas.GetComponentsInChildren<ProceduralUiGraphic>())
            {
                Vector2 original = mark.rectTransform.sizeDelta;
                try
                {
                    foreach (Vector2 size in new[] { original, new Vector2(48f, 48f), new Vector2(440f, 90f) })
                    {
                        mark.rectTransform.sizeDelta = size;
                        mark.SetVerticesDirty();
                        Canvas.ForceUpdateCanvases();
                        var mesh = mark.canvasRenderer.GetMesh();
                        try
                        {
                            if (mesh.vertexCount == 0) throw new InvalidOperationException(mark.name + " has empty geometry.");
                            foreach (Vector3 vertex in mesh.vertices)
                                if (float.IsNaN(vertex.x) || float.IsNaN(vertex.y) || float.IsInfinity(vertex.x) || float.IsInfinity(vertex.y))
                                    throw new InvalidOperationException(mark.name + " has nonfinite geometry.");
                        }
                        finally { Object.DestroyImmediate(mesh); }
                    }
                }
                finally { mark.rectTransform.sizeDelta = original; mark.SetVerticesDirty(); }
            }
            Canvas.ForceUpdateCanvases();
        }
    }
}
