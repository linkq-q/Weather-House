using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using WeatherHouse.StoneEngraving.Editor;

namespace WeatherHouse.Life.Editor
{
    public static class PhotoTaskBookBuilder
    {
        public const string PrefabPath = "Assets/Game/Life/Props/PhotoTaskBook.prefab";
        [MenuItem("Weather House/手账/生成可拾取照片任务书")]
        public static void Build()
        {
            if (Application.isPlaying) throw new InvalidOperationException("Build in Edit mode.");
            Directory.CreateDirectory("Assets/Game/Life/Props"); AssetDatabase.Refresh();
            var preview = EditorSceneManager.NewPreviewScene();
            try
            {
                var root = new GameObject("照片任务书 · 拾取后解锁"); SceneManager.MoveGameObjectToScene(root, preview);
                var source = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Experiments/DrawerPrototype/Models/AlbumTaskBook.prefab");
                var model = UnityEngine.Object.Instantiate(source, root.transform); model.name = "任务书模型"; model.transform.localScale = Vector3.one * .4f;
                var emblem = model.GetComponentsInChildren<Transform>().First(t => t.name == "Photo emblem");
                var ink = emblem.GetComponentInChildren<Renderer>().sharedMaterial; var top = emblem.localPosition;
                UnityEngine.Object.DestroyImmediate(emblem.gameObject);
                var mark = new GameObject("任务清单"); mark.transform.SetParent(model.transform, false); mark.transform.localPosition = top;
                void Line(float x1, float z1, float x2, float z2)
                {
                    var stroke = GameObject.CreatePrimitive(PrimitiveType.Cube); stroke.name = "Checklist stitch";
                    UnityEngine.Object.DestroyImmediate(stroke.GetComponent<Collider>()); stroke.transform.SetParent(mark.transform, false);
                    var a = new Vector3(x1, 0, z1); var b = new Vector3(x2, 0, z2);
                    stroke.transform.localPosition = (a + b) * .5f; stroke.transform.localRotation = Quaternion.FromToRotation(Vector3.right, b - a);
                    stroke.transform.localScale = new Vector3(Vector3.Distance(a, b), .012f, .025f); stroke.GetComponent<Renderer>().sharedMaterial = ink;
                }
                Line(-.36f,.22f,-.29f,.15f); Line(-.29f,.15f,-.18f,.30f); Line(-.04f,.22f,.36f,.22f);
                foreach(float z in new[]{0f,-.22f})
                { Line(-.36f,z-.06f,-.24f,z-.06f); Line(-.24f,z-.06f,-.24f,z+.06f); Line(-.24f,z+.06f,-.36f,z+.06f); Line(-.36f,z+.06f,-.36f,z-.06f); Line(-.04f,z,.36f,z); }
                var bounds = new Bounds(root.transform.position, Vector3.zero); foreach (var r in root.GetComponentsInChildren<Renderer>()) bounds.Encapsulate(r.bounds);
                // Put the prefab pivot on the ground so hand placement is straightforward.
                model.transform.localPosition -= Vector3.up * bounds.min.y;
                var collider = root.AddComponent<BoxCollider>(); collider.center = new Vector3(bounds.center.x, bounds.size.y * .5f, bounds.center.z); collider.size = bounds.size; collider.isTrigger = true;
                root.AddComponent<LifePhotoTaskBook>();
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                var camera = new GameObject("Task book review", typeof(Camera)).GetComponent<Camera>(); SceneManager.MoveGameObjectToScene(camera.gameObject, preview);
                camera.scene = preview; camera.enabled = false; camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.88f,.88f,.80f);
                camera.transform.position = new Vector3(.75f,1.3f,-1.2f); camera.transform.LookAt(new Vector3(0,.1f,0)); camera.orthographic = true; camera.orthographicSize = .64f;
                var light = new GameObject("Review sun", typeof(Light)).GetComponent<Light>(); SceneManager.MoveGameObjectToScene(light.gameObject, preview);
                light.type = LightType.Directional; light.intensity = 1.2f; light.transform.rotation = Quaternion.Euler(50,-35,0);
                StoneEngravingPlacementReview.Capture(camera,"PhotoStillReview/20261029/task-book.png",900,900);
                File.WriteAllText("PhotoStillReview/20261029/task-book-prefab.txt", "PASS prefab saved; checklist cover; trigger collider; persistent pickup component; no instance added to the main scene.");
            }
            finally { EditorSceneManager.ClosePreviewScene(preview); }
        }
    }
}
