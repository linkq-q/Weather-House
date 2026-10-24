using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using WeatherHouse.Vegetation;

namespace WeatherHouse.Hillside.Editor
{
    public sealed class HillsideGrassBrushWindow : EditorWindow
    {
        const string CatalogPath = "Assets/Experiments/Vegetation/PlantLibrary/PlantLibraryCatalog.asset";
        const string WatersidePlants = "Assets/Game/World/WatersideV2/Generated/Nature/Plants";
        static readonly string[] Modes = { "擦草", "铺草", "圈定后调整" };
        [SerializeField] HillsideGrassRenderer targetGrass;
        [SerializeField] int mode, selectedSource = 1;
        [SerializeField] float radius = 2, hardness = .5f, strength = 1;
        [SerializeField] float eraseRemaining, paintDensity = 8, densityScale = 1, heightScale = 1, widthScale = 1;
        [SerializeField] bool affectDensity = true, affectHeight = true, affectWidth = true;
        readonly List<SourceChoice> sources = new List<SourceChoice>();
        readonly Vector3[] circle = new Vector3[65];
        GrassLocalPainter painter, strokePainter;
        Vector2 scroll;
        Vector3 regionCenter;
        bool painting, hasRegion, selectingRegion;
        int strokeId = -1, strokeUndoGroup = -1, ownedControl;
        string feedback;

        internal sealed class SourceChoice
        {
            public int sourceId;
            public string label;
            public GrassSpecies definition;
            public Material material;
            public Mesh[] variants;
        }

        [MenuItem("天气屋/地形与植被/局部草地笔刷")]
        public static void Open()
        {
            var window = GetWindow<HillsideGrassBrushWindow>("草地笔刷");
            window.minSize = new Vector2(350, 570);
            window.Show();
        }

        public static void OpenFor(HillsideGrassRenderer grass)
        {
            Open();
            GetWindow<HillsideGrassBrushWindow>().SetTarget(grass);
        }

        public static void SetupCurrentSceneAndShow()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("请退出 Play 后启用草地编辑。");
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            var candidates = FindObjectsOfType<HillsideGrassRenderer>().Where(g => g.gameObject.scene == scene && g.isActiveAndEnabled).ToArray();
            var grass = SelectedGrass();
            if (!grass || grass.gameObject.scene != scene) grass = candidates.Length == 1 ? candidates[0] : null;
            if (!grass) throw new InvalidOperationException("当前场景存在多片草地或没有草地，请先选中目标草地对象。");
            OpenFor(grass);
            var window = GetWindow<HillsideGrassBrushWindow>();
            string blocked = window.EditBlockReason();
            if (!string.IsNullOrEmpty(blocked)) throw new InvalidOperationException(blocked);
            if (!window.painter) window.InitializePainter();
            window.feedback = "局部草地编辑已就绪，未添加任何示例笔触。";
        }

        void OnEnable()
        {
            painting = false;
            SceneView.duringSceneGui += DuringSceneGUI;
            Undo.undoRedoPerformed += AfterUndo;
            EditorApplication.playModeStateChanged += PlayStateChanged;
            if (!targetGrass) targetGrass = SelectedGrass();
            RefreshSources();
        }

        void OnDisable()
        {
            FinishStroke();
            SceneView.duringSceneGui -= DuringSceneGUI;
            Undo.undoRedoPerformed -= AfterUndo;
            EditorApplication.playModeStateChanged -= PlayStateChanged;
            painting = false;
            SceneView.RepaintAll();
        }

        static HillsideGrassRenderer SelectedGrass()
        {
            if (Selection.activeGameObject)
            {
                var selected = Selection.activeGameObject.GetComponentInParent<HillsideGrassRenderer>();
                if (selected) return selected;
            }
            var candidates = FindObjectsOfType<HillsideGrassRenderer>();
            return candidates.Length == 1 ? candidates[0] : null;
        }

        internal void SetTarget(HillsideGrassRenderer next)
        {
            FinishStroke();
            targetGrass = next;
            hasRegion = painting = false;
            feedback = null;
            RefreshSources();
            Repaint();
        }

        void RefreshSources()
        {
            var definition = selectedSource >= 0 && selectedSource < sources.Count ? sources[selectedSource].definition : null;
            painter = targetGrass ? targetGrass.GetComponent<GrassLocalPainter>() : null;
            sources.Clear();
            sources.Add(new SourceChoice { sourceId = -1, label = "全部草种（擦除）" });
            sources.Add(new SourceChoice { sourceId = 0, label = "主草地 / 短草" });
            sources.Add(new SourceChoice { sourceId = 1, label = "主草地 / 高草" });
            sources.Add(new SourceChoice { sourceId = 2, label = "主草地 / 白色雏菊" });
            sources.AddRange(ReadCatalog());
            if (painter)
                foreach (var source in painter.GetSources())
                {
                    if (source.isBase) continue;
                    var choice = sources.FirstOrDefault(s => s.definition == source.definition && s.material == source.material);
                    if (choice != null) choice.sourceId = source.id;
                    else sources.Add(new SourceChoice { sourceId = source.id, label = "已使用 / " + source.label,
                        definition = source.definition, material = source.material });
                }
            if (definition)
            {
                int found = sources.FindIndex(s => s.definition == definition);
                if (found >= 0) selectedSource = found;
            }
            selectedSource = Mathf.Clamp(selectedSource, mode != 0 ? 1 : 0, sources.Count - 1);
        }

        internal static List<SourceChoice> ReadCatalog()
        {
            var result = new List<SourceChoice>();
            var catalog = AssetDatabase.LoadAssetAtPath<PlantLibraryCatalog>(CatalogPath);
            if (!catalog || catalog.entries == null) return result;
            foreach (var entry in catalog.entries)
            {
                if (entry == null || !entry.species) continue;
                var definition = AssetDatabase.LoadAssetAtPath<GrassSpecies>(WatersidePlants + "/Definitions/" + entry.id + ".asset");
                var material = AssetDatabase.LoadAssetAtPath<Material>(WatersidePlants + "/Materials/" + entry.id + ".mat");
                Mesh[] meshes = Enumerable.Range(1, 3).Select(v => AssetDatabase.LoadAssetAtPath<Mesh>(WatersidePlants + "/Meshes/" + entry.id + "_v" + v + ".asset")).Where(m => m).ToArray();
                if (!definition || !material)
                {
                    var original = ReadOriginalSource(entry);
                    if (original != null) result.Add(original);
                    continue;
                }
                string label = string.IsNullOrEmpty(definition.displayName) ? entry.id : definition.displayName;
                result.Add(new SourceChoice { sourceId = -2, label = "植物库 / " + label + " · " + entry.id,
                    definition = definition, material = material, variants = meshes });
            }
            // A scene-specific redesign can change a silhouette's kind (R01 became a solid seed head).
            // Keep that redesign and restore any otherwise missing original shape as a distinct option.
            foreach (var entry in catalog.entries)
            {
                if (entry == null || !entry.species || result.Any(s => s.definition.kind == entry.species.kind)) continue;
                var original = ReadOriginalSource(entry);
                if (original != null) result.Add(original);
            }
            return result;
        }

        static SourceChoice ReadOriginalSource(PlantLibraryCatalog.Entry entry)
        {
            var variant = entry.variants == null ? null : entry.variants.FirstOrDefault(v => v);
            var renderer = variant ? variant.GetComponentInChildren<Renderer>() : null;
            var material = renderer ? renderer.sharedMaterial : null;
            if (!material) return null;
            var meshes = entry.variants == null ? Array.Empty<Mesh>() : entry.variants.Where(v => v)
                .Select(v => v.GetComponentInChildren<MeshFilter>()).Where(f => f && f.sharedMesh).Select(f => f.sharedMesh).ToArray();
            string label = string.IsNullOrEmpty(entry.species.displayName) ? entry.id : entry.species.displayName;
            return new SourceChoice { sourceId = -2, label = "植物库原型 / " + label + " · " + entry.id,
                definition = entry.species, material = material, variants = meshes };
        }

        void OnGUI()
        {
            EditorGUI.BeginChangeCheck();
            DrawWindow();
            if (EditorGUI.EndChangeCheck()) SceneView.RepaintAll();
        }

        void DrawWindow()
        {
            scroll = EditorGUILayout.BeginScrollView(scroll);
            EditorGUILayout.LabelField("场景里的局部草地编辑", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("刷到哪里，修改哪里。笔触随目标场景保存。", EditorStyles.wordWrappedLabel);
            EditorGUILayout.Space(6);
            using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
            {
                var next = (HillsideGrassRenderer)EditorGUILayout.ObjectField("目标草地", targetGrass, typeof(HillsideGrassRenderer), true);
                if (next != targetGrass) SetTarget(next);
                if (GUILayout.Button("使用当前选中的草地")) SetTarget(SelectedGrass());
            }
            string invalid = EditBlockReason();
            if (!string.IsNullOrEmpty(invalid))
            {
                EditorGUILayout.HelpBox(invalid, MessageType.Info);
                EditorGUILayout.EndScrollView();
                return;
            }
            if (!painter)
            {
                EditorGUILayout.HelpBox("首次使用会在目标草地上增加局部编辑组件。原草种、材质和覆盖图保留。", MessageType.Info);
                if (GUILayout.Button("为这片草地启用局部编辑", GUILayout.Height(32))) InitializePainter();
                EditorGUILayout.EndScrollView();
                return;
            }
            if (!painter.enabled)
            {
                EditorGUILayout.HelpBox("局部草地组件已停用，启用后可继续原有笔触。", MessageType.Info);
                if (GUILayout.Button("启用局部草地组件")) { Undo.RecordObject(painter, "启用局部草地"); painter.enabled = true; MarkChanged(painter); }
                EditorGUILayout.EndScrollView();
                return;
            }
            int nextMode = GUILayout.Toolbar(mode, Modes, GUILayout.Height(29));
            if (nextMode != mode) { FinishStroke(); mode = nextMode; hasRegion = false; selectedSource = Mathf.Max(mode != 0 ? 1 : 0, selectedSource); }
            EditorGUILayout.Space(5);
            string[] labels = sources.Select(s => s.label).ToArray();
            selectedSource = EditorGUILayout.Popup("草种", selectedSource, labels);
            if (mode != 0 && selectedSource == 0) selectedSource = 1;
            if (GUILayout.Button("刷新草种库（只读）", EditorStyles.miniButton)) RefreshSources();
            radius = EditorGUILayout.Slider(new GUIContent("半径（米）", "第三模式可在 Scene 中按下圆心，拖动确定半径。"), radius, .2f, 20);
            hardness = EditorGUILayout.Slider(new GUIContent("边缘硬度", "0 为从中心到边缘渐隐；1 为整圈等强度。内圈是满强度范围。"), hardness, 0, 1);
            strength = EditorGUILayout.Slider("作用强度", strength, .05f, 1);
            if (mode == 0)
            {
                eraseRemaining = EditorGUILayout.Slider("擦后剩余密度", eraseRemaining, 0, 1);
                EditorGUILayout.HelpBox("0 为擦净，0.5 保留约一半；软边会渐渐过渡到原草地。", MessageType.None);
            }
            else if (mode == 1)
            {
                paintDensity = EditorGUILayout.Slider("新增密度（丛 / ㎡）", paintDensity, .1f, 40);
                heightScale = EditorGUILayout.Slider("高度倍率", heightScale, .1f, 4);
                widthScale = EditorGUILayout.Slider(new GUIContent("草丛宽度倍率", "缩放整丛在地面的横向范围（X/Z），不单独改叶片宽度。"), widthScale, .1f, 4);
                EditorGUILayout.HelpBox("1 倍沿用所选草种的形状。高度和草丛宽度分别缩放整丛的纵向与水平范围。再刷同一区域会按当前密度更新。", MessageType.None);
            }
            else
            {
                affectDensity = EditorGUILayout.ToggleLeft("调整选区内的密度", affectDensity);
                using (new EditorGUI.DisabledScope(!affectDensity)) densityScale = EditorGUILayout.Slider("密度倍率", densityScale, 0, 2);
                affectHeight = EditorGUILayout.ToggleLeft("调整选区内的高度", affectHeight);
                using (new EditorGUI.DisabledScope(!affectHeight)) heightScale = EditorGUILayout.Slider("高度倍率", heightScale, .1f, 4);
                affectWidth = EditorGUILayout.ToggleLeft("调整选区内的宽度", affectWidth);
                using (new EditorGUI.DisabledScope(!affectWidth)) widthScale = EditorGUILayout.Slider(new GUIContent("草丛宽度倍率", "缩放整丛在地面的横向范围（X/Z），不单独改叶片宽度。"), widthScale, .1f, 4);
                EditorGUILayout.HelpBox(hasRegion ? "圆选区已保留。参数准备好后点击下方按钮应用；拖动参数本身不会改草。" : "启用笔刷后，在 Scene 按下圆心并拖出范围，松开完成圈定。", MessageType.Info);
                using (new EditorGUI.DisabledScope(!hasRegion || !(affectDensity || affectHeight || affectWidth)))
                    if (GUILayout.Button("将参数应用到圆选区", GUILayout.Height(29))) ApplyRegion();
                using (new EditorGUI.DisabledScope(!hasRegion))
                    if (GUILayout.Button("清除圆选区", EditorStyles.miniButton)) { hasRegion = false; SceneView.RepaintAll(); }
            }
            EditorGUILayout.Space(5);
            bool nextPainting = GUILayout.Toggle(painting, painting ? "笔刷已启用 · 点击结束" : "启用 Scene 草地笔刷", "Button", GUILayout.Height(34));
            if (nextPainting != painting) { FinishStroke(); painting = nextPainting; SceneView.RepaintAll(); }
            EditorGUILayout.HelpBox("左键拖动：刷草 / 圈定区域\nShift + 左键：临时擦草（使用擦后剩余密度）\nAlt：保留视角导航　Esc：结束当前笔触并关闭笔刷\nCtrl/Cmd + Z：撤销　Ctrl/Cmd + Shift + Z：重做", MessageType.None);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(new GUIContent("Unity 撤销", "撤销 Unity 最近一次编辑；刚刷完时会撤销整笔。"))) { FinishStroke(); Undo.PerformUndo(); }
            if (GUILayout.Button(new GUIContent("Unity 重做", "重做 Unity 最近撤销的编辑。"))) { FinishStroke(); Undo.PerformRedo(); }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.LabelField("撤销顺序与 Unity 共用；若刚改过其它物体，会先撤销该物体。", EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.Space(5);
            if (GUILayout.Button("保存此场景及草地笔触", GUILayout.Height(31))) SaveTargetScene();
            EditorGUILayout.LabelField(targetGrass.gameObject.scene.isDirty ? "当前场景有未保存修改" : "当前场景已保存", EditorStyles.miniLabel);
            EditorGUILayout.LabelField("保存会同时写入此场景中其它尚未保存的修改。手工摆放的植被和树不受笔刷影响。", EditorStyles.wordWrappedMiniLabel);
            if (!string.IsNullOrEmpty(feedback)) EditorGUILayout.HelpBox(feedback, MessageType.Info);
            EditorGUILayout.EndScrollView();
        }

        string EditBlockReason() => EditBlockReason(targetGrass, EditorApplication.isPlayingOrWillChangePlaymode, PrefabStageUtility.GetCurrentPrefabStage() != null);

        internal static string EditBlockReason(HillsideGrassRenderer grass, bool isPlayingOrChangingPlaymode, bool inPrefabStage)
        {
            if (isPlayingOrChangingPlaymode) return "请退出 Play 后编辑草地。运行中不会写入笔触。";
            if (!grass) return "请选择带 HillsideGrassRenderer 的场景草地对象；有多片草地时请指定目标。";
            if (EditorUtility.IsPersistent(grass) || !grass.gameObject.scene.IsValid()) return "目标必须是场景里的草地实例。";
            if (inPrefabStage) return "请返回场景后使用草笔刷。";
            if (!grass.isActiveAndEnabled) return "请先启用目标草地。";
            if (!grass.terrain || !grass.terrain.terrainData || !grass.terrain.GetComponent<TerrainCollider>()) return "目标草地需要 Terrain、TerrainData 和 TerrainCollider，才能准确预览和落笔。";
            return null;
        }

        internal void InitializePainter()
        {
            string blocked = EditBlockReason();
            if (!string.IsNullOrEmpty(blocked)) throw new InvalidOperationException(blocked);
            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("启用局部草地编辑");
            Undo.RecordObject(targetGrass, "绑定局部草地");
            painter = Undo.AddComponent<GrassLocalPainter>(targetGrass.gameObject);
            Undo.RecordObject(painter, "绑定局部草地");
            painter.grass = targetGrass;
            targetGrass.localPainter = painter;
            painter.NotifyEdited();
            MarkChanged(painter);
            EditorUtility.SetDirty(targetGrass);
            if (PrefabUtility.IsPartOfPrefabInstance(targetGrass)) PrefabUtility.RecordPrefabInstancePropertyModifications(targetGrass);
            Undo.CollapseUndoOperations(group);
            Undo.IncrementCurrentGroup();
            RefreshSources();
        }

        internal static GrassBrushSettings MakeSettings(GrassPaintMode brushMode, int sourceId, float brushRadius,
            float brushHardness, float brushStrength, float density, float height, float width,
            bool changeDensity, bool changeHeight, bool changeWidth)
        {
            return new GrassBrushSettings {
                mode = brushMode, sourceId = sourceId, radius = Mathf.Clamp(brushRadius, .2f, 20),
                hardness = Mathf.Clamp01(brushHardness), strength = Mathf.Clamp01(brushStrength),
                density = Mathf.Clamp(density, 0, brushMode == GrassPaintMode.Paint ? 40 : brushMode == GrassPaintMode.Adjust ? 2 : 1),
                heightScale = Mathf.Clamp(height, .1f, 4), widthScale = Mathf.Clamp(width, .1f, 4),
                affectDensity = changeDensity, affectHeight = changeHeight, affectWidth = changeWidth };
        }

        GrassBrushSettings CurrentSettings(GrassPaintMode brushMode)
        {
            var source = sources[selectedSource];
            int id = source.sourceId;
            if (id == -2)
            {
                id = painter.AddSource(source.definition, source.material, source.variants);
                source.sourceId = id;
            }
            return MakeSettings(brushMode, id, radius, hardness, strength,
                brushMode == GrassPaintMode.Paint ? paintDensity : brushMode == GrassPaintMode.Erase ? eraseRemaining : densityScale,
                heightScale, widthScale, affectDensity, affectHeight, affectWidth);
        }

        internal void StartStroke(Vector3 point, GrassPaintMode brushMode)
        {
            string blocked = EditBlockReason();
            if (!string.IsNullOrEmpty(blocked)) throw new InvalidOperationException(blocked);
            Undo.IncrementCurrentGroup();
            strokeUndoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName(brushMode == GrassPaintMode.Erase ? "擦除局部草地" : brushMode == GrassPaintMode.Paint ? "铺设局部草地" : "调整选区草地");
            strokePainter = painter;
            Undo.RegisterCompleteObjectUndo(strokePainter, "草地笔触");
            try
            {
                strokeId = strokePainter.BeginStroke(CurrentSettings(brushMode));
                strokePainter.AppendStrokePoint(strokeId, point);
                MarkChanged(strokePainter);
            }
            catch (Exception error) { feedback = error.Message; FinishStroke(); throw; }
        }

        internal void FinishStroke()
        {
            if (strokePainter && strokeId >= 0)
            {
                strokePainter.EndStroke(strokeId);
                MarkChanged(strokePainter);
            }
            if (strokeUndoGroup >= 0)
            {
                Undo.CollapseUndoOperations(strokeUndoGroup);
                // Close the brush transaction before another inspector or tool records an edit.
                Undo.IncrementCurrentGroup();
            }
            strokeId = strokeUndoGroup = -1;
            strokePainter = null;
            selectingRegion = false;
            if (ownedControl != 0 && GUIUtility.hotControl == ownedControl) GUIUtility.hotControl = 0;
            ownedControl = 0;
        }

        static void MarkChanged(GrassLocalPainter changed)
        {
            EditorUtility.SetDirty(changed);
            if (PrefabUtility.IsPartOfPrefabInstance(changed)) PrefabUtility.RecordPrefabInstancePropertyModifications(changed);
            EditorSceneManager.MarkSceneDirty(changed.gameObject.scene);
            SceneView.RepaintAll();
        }

        internal void SelectRegion(Vector3 center, float selectionRadius)
        {
            regionCenter = center;
            radius = Mathf.Clamp(selectionRadius, .2f, 20);
            hasRegion = true;
        }

        internal void ApplyRegion()
        {
            if (!hasRegion || !string.IsNullOrEmpty(EditBlockReason())) return;
            FinishStroke();
            StartStroke(regionCenter, GrassPaintMode.Adjust);
            FinishStroke();
            feedback = "已应用到圆选区；需要继续微调可撤销后重新应用。";
        }

        void SaveTargetScene()
        {
            FinishStroke();
            if (!string.IsNullOrEmpty(EditBlockReason())) return;
            feedback = EditorSceneManager.SaveScene(targetGrass.gameObject.scene) ? "此场景及局部草地笔触已保存。" : "未保存；笔触仍留在当前场景，可稍后保存。";
        }

        void AfterUndo()
        {
            strokeId = strokeUndoGroup = -1;
            strokePainter = null;
            selectingRegion = false;
            if (ownedControl != 0 && GUIUtility.hotControl == ownedControl) GUIUtility.hotControl = 0;
            ownedControl = 0;
            RefreshSources();
            if (painter) painter.NotifyEdited();
            Repaint();
            SceneView.RepaintAll();
        }

        void PlayStateChanged(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.ExitingEditMode) return;
            FinishStroke();
            painting = false;
            Repaint();
        }

        void DuringSceneGUI(SceneView view)
        {
            if (!painting || !painter || !string.IsNullOrEmpty(EditBlockReason())) return;
            Event e = Event.current;
            int control = GUIUtility.GetControlID("HillsideGrassBrush".GetHashCode(), FocusType.Passive);
            if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape)
            {
                FinishStroke(); painting = false; hasRegion = false; e.Use(); Repaint(); view.Repaint(); return;
            }
            if ((strokeId >= 0 || selectingRegion) && (e.rawType == EventType.MouseUp || e.type == EventType.MouseLeaveWindow))
            {
                FinishStroke(); e.Use(); Repaint(); return;
            }
            if (e.alt) { FinishStroke(); return; }
            if (e.type == EventType.Layout) HandleUtility.AddDefaultControl(control);
            Ray ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
            bool hit = targetGrass.terrain.GetComponent<TerrainCollider>().Raycast(ray, out RaycastHit contact, 100000);
            if (hasRegion && mode == 2) DrawCircle(regionCenter, new Color(.2f, .8f, 1), "圆选区 · 点击窗口应用参数");
            if (!hit) return;
            GrassPaintMode brushMode = e.shift ? GrassPaintMode.Erase : (GrassPaintMode)mode;
            if (!selectingRegion && !(hasRegion && mode == 2 && !e.shift))
                DrawCircle(contact.point, brushMode == GrassPaintMode.Erase ? new Color(1, .42f, .25f) : new Color(.45f, 1, .5f), Modes[(int)brushMode]);
            if (e.type == EventType.MouseMove) view.Repaint();
            if (e.type == EventType.MouseDown && e.button == 0 && HandleUtility.nearestControl == control)
            {
                GUIUtility.hotControl = ownedControl = control;
                if (brushMode == GrassPaintMode.Adjust)
                {
                    SelectRegion(contact.point, radius); selectingRegion = true;
                }
                else StartStroke(contact.point, brushMode);
                e.Use(); Repaint();
            }
            else if (e.type == EventType.MouseDrag && e.button == 0 && GUIUtility.hotControl == ownedControl && ownedControl != 0)
            {
                if (selectingRegion)
                    radius = Mathf.Clamp(Vector2.Distance(new Vector2(regionCenter.x, regionCenter.z), new Vector2(contact.point.x, contact.point.z)), .2f, 20);
                else if (strokePainter && strokeId >= 0)
                {
                    try
                    {
                        if (strokePainter.AppendStrokePoint(strokeId, contact.point)) MarkChanged(strokePainter);
                    }
                    catch (InvalidOperationException error)
                    {
                        feedback = error.Message;
                        FinishStroke();
                    }
                }
                e.Use(); Repaint(); view.Repaint();
            }
        }

        void DrawCircle(Vector3 center, Color color, string caption)
        {
            if (Event.current.type != EventType.Repaint) return;
            Color old = Handles.color;
            Handles.color = color;
            DrawTerrainRing(center, radius, 2.4f);
            Handles.color = new Color(color.r, color.g, color.b, .65f);
            if (hardness > .01f) DrawTerrainRing(center, radius * hardness, 1.2f);
            Handles.Label(center + Vector3.up * .2f, caption + "  " + radius.ToString("0.0") + " m");
            Handles.color = old;
        }

        void DrawTerrainRing(Vector3 center, float ringRadius, float lineWidth)
        {
            Terrain terrain = targetGrass.terrain;
            for (int i = 0; i < circle.Length; i++)
            {
                float angle = i * Mathf.PI * 2 / (circle.Length - 1);
                var point = center + new Vector3(Mathf.Cos(angle) * ringRadius, 0, Mathf.Sin(angle) * ringRadius);
                point.y = terrain.SampleHeight(point) + terrain.transform.position.y + .06f;
                circle[i] = point;
            }
            Handles.DrawAAPolyLine(lineWidth, circle);
        }
    }
}
