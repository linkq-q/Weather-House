# 三维手账资源与接入

本次在 Blender 5.1.1 中制作的独立厚手账模型，保留原手账的绿色布面、封面小花、阅读面位置与整体比例。按用户新增反馈，移除中间棕色长书签，改用 `PaperAged.png` 纯纸面和 `CoverAged.png` 全幅封皮纹理，来源与生成提示词见 `贴图生成记录.md`。不再把包含边框、书签和书本轮廓的旧 `Book.png` 贴在真实纸面上；原项目图片未修改。

`Resources.Load<GameObject>("LifeAssets/JournalBook")` 加载模型。9 个网格、78 根骨骼、22,520 个三角面，米制单位。真实几何包含左右圆角厚封板、每侧 24 层不齐纸边、轻微压痕、可随开合变形的布面装订脊、宽 11.8 毫米且深 4.8 毫米的连续纸质书沟、起伏约 7.21 毫米的阅读面，以及由 25×3 控制网格形成柔软卷曲的双面翻页。旧两个实体页签已移除，由界面的三个同排页签提供交互。

源文件为 `Tools/LifeAssets/Source/JournalBook.blend`，生成脚本为 `Tools/LifeAssets/build_journal_book.py`，回导与预览脚本为 `Tools/LifeAssets/verify_render_journal.py`。源文件包含两张打包贴图和两段动作。派生设计及原创几何不改变原项目美术的权利归属。

## 动作与对象

用 `Resources.LoadAll<AnimationClip>("LifeAssets/JournalBook")` 查找以下同名动作，再以 `clip.SampleAnimation(bookInstance, seconds)` 采样 FBX 实例根。不要在同一时刻叠加另一套骨骼旋转。

| Clip | 时长 | 状态 |
| --- | --- | --- |
| BookOpen | 1 秒 | 0 秒闭合，1 秒完全展开；反向采样用于合书 |
| PageTurn | 0.9 秒 | 右页弯曲抬起并落到左页；非循环 |

- `PageLeft`、`PageRight` 是独立 SkinnedMeshRenderer。UV0 各覆盖 0—1，便于分配正文 RenderTexture。
- `TurningPage` 是独立 SkinnedMeshRenderer，材质槽 0 是翻动前的右页正面，槽 1 是翻过后的背面。背面的 U 已反向，翻到左边时正文保持正常阅读方向。
- 只有播放 PageTurn 时启用 TurningPage renderer；开合及静止阅读时关闭它。BookOpen 也将备用纸面略压入右侧纸叠。
- `CoverLeftBoard`、`CoverRightBoard`、`PageStackLeft`、`PageStackRight`、`PaperGutter`、`LinenBinding` 为其他对象名。新版不存在 `LeatherSpine`、`SpineStitches`、`SymbolsTab`、`PhotosTab`。
- 根骨 `BookRoot` 不移动。闭合书体位于右半，表现层若保持居中，可随开合进度平移实例根。不要改动源骨骼动画。

## 与原界面的精确对应

原 artwork 在 1672×941 的手账参考画布中为 `Rect(211,108,1228,742)`，坐标从左上计。模型不含突出页签的完整封面矩形为 Blender X `[-0.236,0.236]`、Y `[-0.1425993485,0.1425993485]`，比例严格对应 1228:742。Blender Z 为厚度，读页朝 +Z，书顶为 +Y。FBX 按标准 -Z forward / Y up 导出；Unity 实际根转换由集成工具读取，不能只按名字推断相机朝向。

首次 Unity Play 实测 `JournalRig.localEulerAngles=(270.020,0,0)`、scale=1；在正上方相机中，原右下花纹、上端皮质条、上方实体页签同时转到对面，证明导入后书面朝向比阅读契约旋转了 180°。运行时须保留 FBX 根转换，在独立上层 holder 增加 Unity Y=180°，居中平移也在 holder 上处理。不要另翻 UV，也不要直接覆盖 Rig 的 X 轴转换。`SampleAnimation` 仍针对模型实例，因此 holder 不受动作根曲线影响。此次发现来自 `LifeGameplayReview/20261026/play` 的实际画面，Blender 回导通过不能证明 Unity 画面方向正确。

| 阅读面 | 原布局中的 UV offset | UV scale | 原画布中的纸面 Rect（左上坐标） |
| --- | --- | --- | --- |
| PageLeft | (0.026, 0.032) | (0.472, 0.942) | (242.928, 127.292, 579.616, 698.964) |
| PageRight | (0.523, 0.032) | (0.454, 0.942) | (853.244, 127.292, 557.512, 698.964) |

以上纸面坐标继续直接用于模型几何，开态曲面仅改变厚度方向，X/Y 投影严格保留原矩形。Blender 阅读面 Z 为 `0.0287..0.035908` 米，开合铰链 Z 为 `0.037` 米。原图左右可见纸宽不同，翻页终点作约 4% 的宽度补偿，落在左页。运行时保留整张 1672×941 正文 RenderTexture，通过材质 offset/scale 选取表中的纸面 Rect；V 应按纹理从下往上的坐标转换。纯纸面纹理本身使用 UV0 全幅、offset=(0,0)、scale=(1,1)，不要把正文 RT 裁切参数误用在 PaperAged 上。

默认纸面直接使用 PaperAged，封面直接使用 CoverAged，完整映射在 `journal-manifest.json`。`LifeArtImporter` 只负责 Life Art 的导入，将两段动作设为 Generic、关闭压缩，并设置 URP Lit 及纹理。运行时 `LifeJournal3D` 使用独立 `JournalSurface`，以固定方向的轻微法线明暗显示曲面，不叠加世界主光。界面方案为静止与动画均将正文贴到真实纸面，透明 UGUI 保留命中；这部分须在新版 Unity Play 中验证。导入材质仍保留在资源中，代码改动后应重新导入 JournalBook。

## 已完成的检查

`journal-verification.json` 记录独立 FBX 回导检查。两段动作均以 5 个时刻逐顶点比较，两段的最大误差均低于 0.0002 毫米（检查阈值为 0.2 毫米）；78 根骨、9 个网格、双材质槽、时长及 UV 范围通过。翻页起止投影范围分别与左右阅读面相等，误差低于 0.01 毫米。闭合两页最小间距约 2.18 毫米；半翻页纸尖高度约 0.220 米，中段截面转折角 53.23°，验证纸面并非平板转轴。

翻页内侧切线限制在 0—π，避免动作刚开始下折穿纸。导出 FBX 的 28 个整数帧均每隔 8 个顶点抽样，与左右静止曲面的插值高度比较，最小间距约 0.188 毫米，未发现穿入纸叠；这不代替 Unity 实际混合采样与视频检查。

四张静帧已检查封皮纹样、纸张层次和翻页弯曲；[动态预览](preview.html) 提供完整开合与翻页序列。离线文件与动画验证不代替 Unity Play 验收。集成时需检查真实导入轴向、正文随曲面的贴合、文字正反、动画中断、点击对应和 J 键打开/关闭。

2026-10-26 14:59 的 Unity 实图确认了上一版的 holder 朝向修正、正向正文与曝光修复。该证据只支持导入方向契约；它早于本次厚纸、曲面与新纹理改版，不能当作新版视觉或操作的验收。新版仍需主任务独立拍摄 Unity 实图。

![展开](JournalOpen.png)
![封面](JournalClosed.png)
![真实弯曲翻页](JournalPageTurn.png)
