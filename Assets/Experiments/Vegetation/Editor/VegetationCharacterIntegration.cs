using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Text;
using ForestSpriteCharacter;
using ForestSpriteCharacter.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace WeatherHouse.Vegetation.Editor
{
    public static class VegetationCharacterIntegration
    {
        const string PlayerName = "06 植被角色 · ForestSprite";
        const string CameraName = "07 角色跟随相机";
        const string FloorName = "08 角色披风地面碰撞";
        const string ReportPath = "VegetationLabReview/character-in-vegetation-checks.txt";

        [MenuItem("Weather House/Vegetation Lab/森林小精灵 · 接入植被试验场")]
        public static void Install()
        {
            Require(!EditorApplication.isPlayingOrWillChangePlaymode && SceneManager.sceneCount == 1 &&
                !SceneManager.GetActiveScene().isDirty, "先保存单一场景并退出 Play，再接入角色。");
            Require(File.Exists(VegetationLabBuilder.ScenePath), "原植被试验场不存在。");
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ForestSpriteDemoBuilder.PrefabPath);
            Require(prefab && prefab.GetComponent<ForestSpriteDemoController>(), "需要现有 ForestSprite.prefab。");
            Scene scene = SceneManager.GetActiveScene();
            if (scene.path != VegetationLabBuilder.ScenePath)
                scene = EditorSceneManager.OpenScene(VegetationLabBuilder.ScenePath);
            var view = Components<GrassLabView>(scene).Single();
            Require(view.regions != null && view.regions.Length == 5 && view.regions.All(m => m), "先准备原草地与四个植物区块。");
            var meadows = Components<GrassMeadow>(scene);
            string[] meadowState = meadows.Select(m => EditorJsonUtility.ToJson(m)).ToArray();
            Mesh[][] meadowMeshes = meadows.Select(m => m.GetComponentsInChildren<MeshFilter>(true).Select(f => f.sharedMesh).ToArray()).ToArray();

            var playerObject = RootNamed(scene, PlayerName);
            if (!playerObject)
            {
                playerObject = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                playerObject.name = PlayerName;
                GrassMeadow main = view.regions[0];
                playerObject.transform.position = new Vector3(-8, Ground(main, new Vector3(-8, 0, -10)) + .03f, -10);
            }
            Require(PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(playerObject) == ForestSpriteDemoBuilder.PrefabPath,
                "同名角色必须是原 ForestSprite 预制体实例。");
            var player = playerObject.GetComponent<ForestSpriteDemoController>();
            if (Mathf.Approximately(player.cameraYaw, 170) && Mathf.Approximately(player.cameraPitch, 8) &&
                Mathf.Approximately(player.cameraDistance, 3.5f) && Mathf.Approximately(player.cameraHeight, .8f))
            {
                player.cameraYaw = 35; player.cameraPitch = 18;
                player.cameraDistance = 4.5f; player.cameraHeight = 1;
            }
            Require(player && player.animator && player.animator.runtimeAnimatorController && player.visual &&
                player.secondaryMotion && player.capeCloth && player.capeCloth.cloth, "保留原动画、二级运动与披风引用。");
            Require(playerObject.GetComponent<CharacterController>(), "角色需要原 CharacterController。");

            Camera browseCamera = view.GetComponent<Camera>();
            GameObject cameraObject = RootNamed(scene, CameraName);
            if (!cameraObject) cameraObject = new GameObject(CameraName, typeof(Camera));
            Camera playerCamera = cameraObject.GetComponent<Camera>();
            Require(playerCamera, "同名角色相机对象需要 Camera。");
            playerCamera.CopyFrom(browseCamera);
            playerCamera.targetTexture = null;
            playerCamera.tag = "MainCamera";
            var sourceUrp = browseCamera.GetComponent<UniversalAdditionalCameraData>();
            if (sourceUrp)
            {
                var targetUrp = playerCamera.GetComponent<UniversalAdditionalCameraData>();
                if (!targetUrp) targetUrp = playerCamera.gameObject.AddComponent<UniversalAdditionalCameraData>();
                EditorUtility.CopySerialized(sourceUrp, targetUrp);
                targetUrp.renderType = CameraRenderType.Base;
                targetUrp.cameraStack.Clear();
            }
            var listener = playerCamera.GetComponent<AudioListener>();
            if (!listener) listener = playerCamera.gameObject.AddComponent<AudioListener>();
            foreach (var camera in Components<Camera>(scene)) camera.enabled = camera == playerCamera;
            foreach (var audio in Components<AudioListener>(scene)) audio.enabled = audio == listener;
            player.demoCamera = playerCamera;
            player.enabled = true;
            player.SetDemonstration(ForestSpriteDemoController.Demonstration.Manual);
            Vector3 target = player.transform.position + Vector3.up * player.cameraHeight;
            playerCamera.transform.position = target + Quaternion.Euler(player.cameraPitch, player.cameraYaw, 0) * Vector3.back * player.cameraDistance;
            playerCamera.transform.LookAt(target);

            ConnectClothFloor(scene, player, view.regions[0]);
            var adapters = Components<VegetationCharacterPreview>(scene);
            Require(adapters.Length <= 1 && (adapters.Length == 0 || adapters[0].gameObject == view.gameObject), "场景中已存在另一处角色适配器。");
            var adapter = view.GetComponent<VegetationCharacterPreview>();
            if (!adapter) adapter = view.gameObject.AddComponent<VegetationCharacterPreview>();
            adapter.labView = view;
            adapter.player = player;
            adapter.playerCamera = playerCamera;
            adapter.startInCharacterMode = true;
            view.enabled = false;
            view.Navigation.enabled = false;
            PrefabUtility.RecordPrefabInstancePropertyModifications(player);
            PrefabUtility.RecordPrefabInstancePropertyModifications(playerObject);
            PrefabUtility.RecordPrefabInstancePropertyModifications(player.transform);
            PrefabUtility.RecordPrefabInstancePropertyModifications(player.capeCloth);
            PrefabUtility.RecordPrefabInstancePropertyModifications(player.capeCloth.cloth);
            for (int i = 0; i < meadows.Length; i++)
            {
                Require(EditorJsonUtility.ToJson(meadows[i]) == meadowState[i], "角色接入不修改草地区块参数。");
                Require(meadowMeshes[i].SequenceEqual(meadows[i].GetComponentsInChildren<MeshFilter>(true).Select(f => f.sharedMesh)), "角色接入不重建草地网格。");
            }
            Require(Components<Camera>(scene).Count(c => c.isActiveAndEnabled) == 1 &&
                Components<AudioListener>(scene).Count(a => a.isActiveAndEnabled) == 1, "场景只启用一台相机与一个 AudioListener。");
            EditorSceneManager.MarkSceneDirty(scene);
            Require(EditorSceneManager.SaveScene(scene), "保存带角色的植被试验场。");
            Selection.activeGameObject = playerObject;
            Debug.Log("Forest Sprite installed in VegetationLab; existing prefab, animation and meadow assets preserved.");
        }

        static void ConnectClothFloor(Scene scene, ForestSpriteDemoController player, GrassMeadow main)
        {
            var cape = player.capeCloth;
            if (!cape.floorCollision)
            {
                var floor = RootNamed(scene, FloorName);
                if (!floor) floor = new GameObject(FloorName, typeof(SphereCollider));
                cape.floorCollision = floor.GetComponent<SphereCollider>();
                Require(cape.floorCollision, "披风地面代理需要 SphereCollider。");
                cape.floorCollision.radius = 25;
                cape.floorCollision.isTrigger = true;
            }
            var sphere = cape.floorCollision;
            float radius = sphere.radius * Mathf.Max(Mathf.Abs(sphere.transform.lossyScale.x), Mathf.Abs(sphere.transform.lossyScale.y), Mathf.Abs(sphere.transform.lossyScale.z));
            sphere.transform.position = new Vector3(player.transform.position.x, Ground(main, player.transform.position) - radius, player.transform.position.z);
            if (!cape.cloth.sphereColliders.Any(pair => pair.first == sphere || pair.second == sphere))
                cape.cloth.sphereColliders = cape.cloth.sphereColliders.Concat(new[] { new ClothSphereColliderPair(sphere) }).ToArray();
        }

        public static IEnumerator VerifyPlay()
        {
            Directory.CreateDirectory("VegetationLabReview");
            var report = new StringBuilder("Evidence: real Play frames, ForestSpriteDemoController.SetScriptedInput, actual displacement and body-bone poses.\nThis is automated entrypoint coverage, not manual keyboard/mouse testing.\n");
            File.WriteAllText(ReportPath, "RUNNING\n" + report);
            IEnumerator checks = RunChecks(report);
            try
            {
                while (true)
                {
                    bool more;
                    try { more = checks.MoveNext(); }
                    catch (Exception error) { File.WriteAllText(ReportPath, "FAIL\n" + report + error); throw; }
                    if (!more) break;
                    yield return checks.Current;
                }
                File.WriteAllText(ReportPath, "PASS\n" + report);
            }
            finally { (checks as IDisposable)?.Dispose(); }
        }

        static IEnumerator RunChecks(StringBuilder report)
        {
            Require(Application.isPlaying && SceneManager.GetActiveScene().path == VegetationLabBuilder.ScenePath, "检查需要原植被试验场的实际 Play。");
            Scene scene = SceneManager.GetActiveScene();
            var adapter = Components<VegetationCharacterPreview>(scene).Single();
            var view = adapter.labView;
            var player = adapter.player;
            Require(player && player.demoCamera == adapter.playerCamera && player.animator && player.capeCloth.cloth, "角色、相机、动画、披风已连接。");
            var prefabPlayer = AssetDatabase.LoadAssetAtPath<GameObject>(ForestSpriteDemoBuilder.PrefabPath).GetComponent<ForestSpriteDemoController>();
            Require(prefabPlayer.animator && EditorUtility.IsPersistent(prefabPlayer.animator.runtimeAnimatorController) &&
                player.animator.runtimeAnimatorController == prefabPlayer.animator.runtimeAnimatorController, "Play 动画控制器仍引用原预制体的持久资源。");
            var sourceBodies = prefabPlayer.visual.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                .Where(skin => skin.name.StartsWith("Body", StringComparison.OrdinalIgnoreCase)).ToArray();
            Require(sourceBodies.Length > 0, "原预制体包含身体蒙皮网格。");
            foreach (var sourceBody in sourceBodies)
            {
                string path = AnimationUtility.CalculateTransformPath(sourceBody.transform, prefabPlayer.visual);
                Transform bodyTransform = path.Length == 0 ? player.visual : player.visual.Find(path);
                var body = bodyTransform ? bodyTransform.GetComponent<SkinnedMeshRenderer>() : null;
                Require(body && sourceBody.sharedMesh && EditorUtility.IsPersistent(sourceBody.sharedMesh) &&
                    body.sharedMesh == sourceBody.sharedMesh, "Play 身体网格仍引用原预制体的持久资源：" + path);
            }
            report.AppendLine("Persistent resources: original AnimatorController and " + sourceBodies.Length + " body mesh reference(s) retained; runtime cloth meshes excluded.");
            Require(view.regions != null && view.regions.Length == 5, "五个草地区域可传送。");
            Transform[] bones = new[] { "Hips", "Chest", "Head", "UpperLeg.L", "LowerLeg.L", "Foot.L", "UpperLeg.R", "Foot.R" }
                .Select(name => player.visual.GetComponentsInChildren<Transform>().Single(t => t.name == name)).ToArray();
            bool savedMode = adapter.CharacterMode;
            int savedRegion = Mathf.Max(0, Array.IndexOf(view.regions, view.meadow));
            Vector3 savedPosition = player.transform.position;
            Quaternion savedRotation = player.transform.rotation;
            float savedWalk = player.walkSpeed, savedRun = player.runSpeed;
            try
            {
                adapter.SetCharacterMode(true);
                adapter.TeleportToRegion(0);
                player.walkSpeed = .78f; player.runSpeed = 1.35f;
                player.SetScriptedInput(Vector2.zero, false, false);
                var wait = WaitFrames(.6f); while (wait.MoveNext()) yield return null;
                RequireSingleCamera(adapter, true);
                CheckGround(player, view.regions[0], "main start", report);
                foreach (string state in new[] { "Idle", "Walk", "Run" })
                {
                    player.SetScriptedInput(state == "Idle" ? Vector2.zero : Vector2.up, state == "Run", false);
                    wait = WaitFrames(.3f); while (wait.MoveNext()) yield return null;
                    var sample = new PoseSample(player, bones);
                    int frame = Time.frameCount;
                    float elapsed = 0;
                    while (elapsed < 1.25f)
                    {
                        yield return null;
                        if (Time.frameCount == frame) continue;
                        frame = Time.frameCount;
                        elapsed += Mathf.Min(Time.deltaTime, .05f);
                        sample.Read(player, bones);
                    }
                    Require(player.CurrentState == state && player.animator.GetCurrentAnimatorStateInfo(0).IsName(state), state + "实际播放对应动画状态。");
                    Require(sample.frames >= 5 && (sample.rotationDelta > .01f || sample.positionDelta > .00001f), state + "跨帧身体骨骼姿态确实变化。");
                    float distance = Vector3.ProjectOnPlane(player.transform.position - sample.start, Vector3.up).magnitude;
                    if (state == "Idle") Require(distance < .04f, "待机不产生水平滑动。");
                    else
                    {
                        float speed = state == "Run" ? player.runSpeed : player.walkSpeed;
                        Require(distance > speed * elapsed * .7f && distance < speed * elapsed * 1.2f, state + "真实位移匹配移动速度。");
                        Require(sample.footMaximum - sample.footMinimum > .015f, state + "脚部有实际迈步跨度。");
                    }
                    report.AppendLine(state + ": frames=" + sample.frames + " distance=" + distance.ToString("F4") + "m boneAngle=" + sample.rotationDelta.ToString("F4") + " bonePosition=" + sample.positionDelta.ToString("F6") + " footSpan=" + (sample.footMaximum - sample.footMinimum).ToString("F4") + "m");
                    PlantLibraryChecks.Capture("Hero_" + state, adapter.playerCamera, 1600, 1000);
                }
                player.SetScriptedInput(Vector2.zero, false, false);
                wait = WaitFrames(.3f); while (wait.MoveNext()) yield return null;
                float baseY = player.transform.position.y, rise = 0;
                bool airborne = false, jumpState = false, capturedJump = false, landed = false;
                var jumpPose = new PoseSample(player, bones);
                player.SetScriptedInput(Vector2.zero, false, true);
                int jumpFrame = Time.frameCount;
                float jumpTime = 0;
                while (jumpTime < 4)
                {
                    yield return null;
                    if (jumpFrame == Time.frameCount) continue;
                    jumpFrame = Time.frameCount;
                    jumpTime += Mathf.Min(Time.deltaTime, .05f);
                    jumpPose.Read(player, bones);
                    rise = Mathf.Max(rise, player.transform.position.y - baseY);
                    airborne |= !player.IsGrounded;
                    jumpState |= player.animator.GetCurrentAnimatorStateInfo(0).IsName("Jump");
                    if (!capturedJump && player.transform.position.y > baseY + .18f)
                    {
                        PlantLibraryChecks.Capture("Hero_Jump", adapter.playerCamera, 1600, 1000);
                        capturedJump = true;
                    }
                    if (airborne && player.IsGrounded && jumpTime > .2f) { landed = true; break; }
                }
                Require(airborne && landed && jumpState && rise > .15f && capturedJump, "跳跃确实升空、播放动画并落地。");
                Require(jumpPose.rotationDelta > .1f || jumpPose.positionDelta > .001f, "跳跃身体骨骼姿态变化。");
                CheckGround(player, view.regions[0], "jump landing", report);
                report.AppendLine("Jump: rise=" + rise.ToString("F4") + "m frames=" + jumpPose.frames + " boneAngle=" + jumpPose.rotationDelta.ToString("F4") + " landed=" + landed);

                string[] ids = { "Main", "LowMeadow", "TallFlowers", "FernEdge", "Wetland" };
                for (int i = 0; i < 5; i++)
                {
                    adapter.TeleportToRegion(i);
                    player.SetScriptedInput(Vector2.zero, false, false);
                    wait = WaitFrames(.6f); while (wait.MoveNext()) yield return null;
                    Require(view.meadow == view.regions[i], ids[i] + "区域选择同步。");
                    Vector3 local = view.regions[i].transform.InverseTransformPoint(player.transform.position);
                    Require(Mathf.Abs(local.x) <= view.regions[i].size.x * .5f && Mathf.Abs(local.z) <= view.regions[i].size.y * .5f, ids[i] + "传送进入目标草地范围。");
                    CheckGround(player, view.regions[i], ids[i], report);
                    RequireSingleCamera(adapter, true);
                    PlantLibraryChecks.Capture("Hero_" + ids[i], adapter.playerCamera, 1600, 1000);
                }
                adapter.SetCharacterMode(false);
                wait = WaitFrames(.3f); while (wait.MoveNext()) yield return null;
                RequireSingleCamera(adapter, false);
                Vector3 stopped = player.transform.position;
                player.SetScriptedInput(Vector2.up, true, false);
                wait = WaitFrames(.35f); while (wait.MoveNext()) yield return null;
                Require(Vector3.Distance(stopped, player.transform.position) < .001f, "浏览模式停用角色移动入口。");
                PlantLibraryChecks.Capture("Hero_Browse", view.GetComponent<Camera>(), 1600, 1000);
                player.ReleaseScriptedInput();
                adapter.SetCharacterMode(true);
                wait = WaitFrames(.3f); while (wait.MoveNext()) yield return null;
                RequireSingleCamera(adapter, true);
                report.AppendLine("Modes: character -> browse -> character; one active scene Camera and AudioListener in each mode; character input disabled while browsing.");
            }
            finally
            {
                player.ReleaseScriptedInput();
                player.walkSpeed = savedWalk; player.runSpeed = savedRun;
                adapter.TeleportToRegion(savedRegion);
                var controller = player.GetComponent<CharacterController>();
                controller.enabled = false;
                player.transform.SetPositionAndRotation(savedPosition, savedRotation);
                controller.enabled = true;
                player.secondaryMotion.ResetSimulation(); player.capeCloth.ResetSimulation();
                adapter.SetCharacterMode(savedMode);
            }
        }

        sealed class PoseSample
        {
            readonly Quaternion[] rotations;
            readonly Vector3[] positions;
            public readonly Vector3 start;
            public int frames;
            public float rotationDelta, positionDelta, footMinimum = float.MaxValue, footMaximum = float.MinValue;
            public PoseSample(ForestSpriteDemoController player, Transform[] bones)
            {
                start = player.transform.position;
                rotations = bones.Select(b => b.localRotation).ToArray();
                positions = bones.Select(b => b.localPosition).ToArray();
            }
            public void Read(ForestSpriteDemoController player, Transform[] bones)
            {
                frames++;
                for (int i = 0; i < bones.Length; i++)
                {
                    rotationDelta = Mathf.Max(rotationDelta, Quaternion.Angle(rotations[i], bones[i].localRotation));
                    positionDelta = Mathf.Max(positionDelta, Vector3.Distance(positions[i], bones[i].localPosition));
                }
                float foot = player.transform.InverseTransformPoint(bones[5].position).z;
                footMinimum = Mathf.Min(footMinimum, foot); footMaximum = Mathf.Max(footMaximum, foot);
            }
        }

        static IEnumerator WaitFrames(float duration)
        {
            float elapsed = 0;
            int frame = Time.frameCount;
            double deadline = EditorApplication.timeSinceStartup + 45;
            while (elapsed < duration)
            {
                Require(EditorApplication.timeSinceStartup < deadline, "等待实际 Play 帧超时。");
                yield return null;
                if (Time.frameCount == frame) continue;
                frame = Time.frameCount; elapsed += Mathf.Min(Time.deltaTime, .05f);
            }
        }
        static void RequireSingleCamera(VegetationCharacterPreview adapter, bool character)
        {
            Scene scene = adapter.gameObject.scene;
            var cameras = Components<Camera>(scene).Where(c => c.isActiveAndEnabled).ToArray();
            Require(adapter.CharacterMode == character && cameras.Length == 1 && cameras[0] == (character ? adapter.playerCamera : adapter.labView.GetComponent<Camera>()), "模式切换只启用对应相机。");
            Require(Components<AudioListener>(scene).Count(a => a.isActiveAndEnabled) == 1, "模式切换只启用一个 AudioListener。");
            Require(adapter.player.enabled == character && adapter.labView.enabled != character && adapter.labView.Navigation.enabled != character, "移动和浏览控制器互斥。");
        }
        static void CheckGround(ForestSpriteDemoController player, GrassMeadow region, string label, StringBuilder report)
        {
            float ground = Ground(region, player.transform.position);
            float error = player.GetComponent<CharacterController>().bounds.min.y - ground;
            Require(player.IsGrounded && Mathf.Abs(error) < .09f, label + "角色胶囊与实际地面接触。error=" + error);
            Require(ground > region.waterLevel + .2f, label + "传送落点在安全陆地。");
            report.AppendLine(label + ": grounded=" + player.IsGrounded + " capsuleGroundError=" + error.ToString("F4") + "m world=" + player.transform.position.ToString("F3"));
        }
        static float Ground(GrassMeadow meadow, Vector3 world)
        {
            Vector3 local = meadow.transform.InverseTransformPoint(world);
            return meadow.GetGroundHeight(local.x, local.z) + meadow.transform.position.y;
        }
        static T[] Components<T>(Scene scene) where T : Component { return scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<T>(true)).ToArray(); }
        static GameObject RootNamed(Scene scene, string name)
        {
            var matches = scene.GetRootGameObjects().Where(root => root.name == name).ToArray();
            Require(matches.Length <= 1, "场景中存在重名对象：" + name);
            return matches.FirstOrDefault();
        }
        static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException("Vegetation character: " + message); }
    }
}
