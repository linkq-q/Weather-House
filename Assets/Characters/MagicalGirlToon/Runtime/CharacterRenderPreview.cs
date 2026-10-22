using System;
using UnityEngine;

namespace WeatherHouseToon
{
    public sealed class CharacterRenderPreview : MonoBehaviour
    {
        [Serializable] public class Surface
        {
            public Renderer renderer;
            public Material[] toon;
            public Material[] unlit;
        }
        public Surface[] surfaces;
        [Range(0, 2)] public int mode;
        GUIStyle label, button;
        Font font;

        void Start() { SetMode(mode); }
        void Update()
        {
            if (Input.GetKeyDown(KeyCode.F6)) SetMode(0);
            if (Input.GetKeyDown(KeyCode.F7)) SetMode(1);
            if (Input.GetKeyDown(KeyCode.F8)) SetMode(2);
        }
        public void SetMode(int value)
        {
            mode = Mathf.Clamp(value, 0, 2);
            foreach (var surface in surfaces)
            {
                surface.renderer.sharedMaterials = mode == 2 ? surface.unlit : surface.toon;
                surface.renderer.gameObject.layer = 8;
                // Keep the character-only lights in every comparison; switch only outline width.
                for (int i = 0; i < surface.toon.Length; i++)
                {
                    var block = new MaterialPropertyBlock();
                    surface.renderer.GetPropertyBlock(block, i);
                    block.SetFloat("_OutlineWidth", mode == 1 ? 0 : surface.toon[i].GetFloat("_OutlineWidth"));
                    surface.renderer.SetPropertyBlock(block, i);
                }
            }
        }
        void OnGUI()
        {
            if (label == null)
            {
                font = Font.CreateDynamicFontFromOSFont(new[] { "Microsoft YaHei", "SimHei", "Arial" }, 14);
                label = new GUIStyle(GUI.skin.label) { font = font, fontSize = 14, wordWrap = true };
                button = new GUIStyle(GUI.skin.button) { font = font, fontSize = 14 };
            }
            GUILayout.BeginArea(new Rect(Screen.width - 300, 12, 288, 130), GUI.skin.box);
            GUILayout.Label("iPad 重绘角色 · 场景渲染预览", label);
            if (GUILayout.Button("F6  卡渲 + 描边" + (mode == 0 ? "  ✓" : ""), button)) SetMode(0);
            if (GUILayout.Button("F7  卡渲（关闭描边）" + (mode == 1 ? "  ✓" : ""), button)) SetMode(1);
            if (GUILayout.Button("F8  原贴图底色" + (mode == 2 ? "  ✓" : ""), button)) SetMode(2);
            GUILayout.Label("当前为静态外观预览，尚未接动作。", label);
            GUILayout.EndArea();
        }
        void OnDestroy() { if (font != null) Destroy(font); }
    }
}
