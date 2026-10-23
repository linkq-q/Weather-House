using System;
using System.IO;
using UnityEngine;

namespace WeatherHouse.WildlifeJourney
{
    public sealed class JourneyLabControls : MonoBehaviour
    {
        public JourneyHerd herd;
        public JourneySocial social;
        public JourneyNavigation navigation;
        public GameObject bridgeBlocker;
        public Camera viewCamera;
        private bool diagnostics, paused;
        private string message = "";
        private Font font;
        private Vector3 focus;
        private float yaw = 35, pitch = 48, distance = 39;

        private void Start()
        {
            string[] available = Font.GetOSInstalledFontNames();
            foreach (string candidate in new[] { "Microsoft YaHei", "Noto Sans CJK SC", "SimHei", "Arial" })
                if (Array.IndexOf(available, candidate) >= 0) { font = Font.CreateDynamicFontFromOSFont(candidate, 16); break; }
            if (viewCamera)
            {
                Vector3 angles = viewCamera.transform.eulerAngles;
                yaw = angles.y; pitch = angles.x;
                float down = viewCamera.transform.forward.y;
                distance = down < -.01f ? (.3f - viewCamera.transform.position.y) / down : viewCamera.transform.position.magnitude;
                focus = viewCamera.transform.position + viewCamera.transform.forward * distance;
            }
        }

        private void Update()
        {
            if (!herd) return;
            if (Input.GetKeyDown(KeyCode.Space)) TogglePause();
            if (Input.GetKeyDown(KeyCode.R)) MoveGroup();
            if (Input.GetKeyDown(KeyCode.B)) ToggleBridge();
            if (Input.GetKeyDown(KeyCode.Tab)) diagnostics = !diagnostics;
            if (Input.GetKeyDown(KeyCode.C)) Capture();
            if (viewCamera)
            {
                if (Input.GetMouseButton(1)) { yaw += Input.GetAxis("Mouse X") * 3; pitch = Mathf.Clamp(pitch - Input.GetAxis("Mouse Y") * 2, 20, 80); }
                distance = Mathf.Clamp(distance - Input.GetAxis("Mouse ScrollWheel") * 12, 8, 65);
                float x = Input.GetAxisRaw("Horizontal"), z = Input.GetAxisRaw("Vertical");
                focus += Quaternion.Euler(0, yaw, 0) * new Vector3(x, 0, z) * Time.unscaledDeltaTime * 8;
                Quaternion rotation = Quaternion.Euler(pitch, yaw, 0);
                viewCamera.transform.SetPositionAndRotation(focus - rotation * Vector3.forward * distance, rotation);
            }
        }

        private void TogglePause()
        {
            paused = !paused;
            // Pause only this experiment, not Unity's global timescale or any other scene.
            herd.SetPaused(paused);
            message = paused ? "观察暂停" : "继续观察";
        }

        private void MoveGroup()
        {
            if (herd.Travelling) { message = "先等这次分队会合完成"; return; }
            message = herd.BeginJourney((herd.TargetSite + 1) % Mathf.Max(1, herd.sites.Length)) ? "羊群开始比较路线并陆续出发" : "暂时没有全员可达的目的地";
        }

        private void ToggleBridge()
        {
            if (!bridgeBlocker) return;
            if (herd.Travelling)
                foreach (JourneyMember m in herd.members)
                    if (m != null && m.Mode == JourneyTravelMode.Bridge) { message = "先让桥上的羊走完，再改变桥口"; return; }
            bridgeBlocker.SetActive(!bridgeBlocker.activeSelf);
            Physics.SyncTransforms(); if (navigation) navigation.Rebuild();
            message = bridgeBlocker.activeSelf ? "桥口暂时受阻，观察选路与等待" : "桥口已恢复通行";
        }

        private void Capture()
        {
            if (!social || !viewCamera) return;
            try
            {
                string path = Path.GetFullPath(Path.Combine(Application.dataPath, "../JourneyReview/Photos", DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + ".png"));
                message = "已记录交流与双方语境：" + social.CaptureCurrent(viewCamera, path);
            }
            catch (Exception error) { message = error.Message; }
        }

        private void OnGUI()
        {
            if (!herd) return;
            if (font) GUI.skin.font = font;
            GUILayout.BeginArea(new Rect(18, 18, 430, diagnostics ? 510 : 170), GUI.skin.box);
            GUILayout.Label("羊群 · 水岸生活样板");
            GUILayout.Label(herd.GroupDecision ?? "正在观察环境");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(paused ? "继续" : "暂停")) TogglePause();
            if (GUILayout.Button("前往另一片草地")) MoveGroup();
            if (GUILayout.Button("改变桥口")) ToggleBridge();
            if (GUILayout.Button("记录交流")) Capture();
            GUILayout.EndHorizontal();
            GUILayout.Label("右键转动 · 滚轮缩放 · WASD 平移 · Tab 显示判断");
            if (!string.IsNullOrEmpty(message)) GUILayout.Label(message);
            if (diagnostics)
            {
                GUILayout.Label("会合完成 " + herd.CompletedJourneys + " 次 / 改路 " + herd.RouteChanges + " 次");
                foreach (JourneyMember m in herd.members)
                    if (m != null) GUILayout.Label("羊 " + m.id + " · " + m.LastDecision);
                GUILayout.Label("交流间隔（秒）");
                if (social) { float gap = GUILayout.HorizontalSlider(social.conversationGap.x, 3, 20); social.conversationGap = new Vector2(gap, gap + 4); }
                GUILayout.Label("会合后停留（秒）");
                herd.minimumStay = GUILayout.HorizontalSlider(herd.minimumStay, 8, 60);
            }
            GUILayout.EndArea();
        }
    }
}
