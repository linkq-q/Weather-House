# 2026-10-30 石刻独立实验

本次只新增实验资产；主场景、原雨水石和七块已摆放石刻不替换。

## 上色

- 用户确认 `exec-cc175a34-abcb-4ad8-9766-eea1b42a6b1d.png` 的配色。原图逐字节复制为 `Assets/Experiments/StoneEngraving/TabletStudy/RainWaterPaint.png`。
- 内置 `image_gen` 生成透明彩绘层与细微干刷颗粒。完整提示词在 `imagegen-prompts.json`。没有使用后续另一张候选。
- `B-white-grooves.prefab` 是全刻槽白色对照；`C-filled.prefab` 加入已确认的内部色块。保留原 Rock_1 网格、缩放、深度、法线、破损遮罩、雨和水动物字。
- 独立 `PaintedEngraving.shader` 只增加反照率颜色层；不加自发光。生成图位于原 1536 方形刻画遮罩的第 376 行，尺寸 1536×1024；该变换显式留在 shader 中。
- 这是雨水图专用上色实验，未推广为任意图案的自动分色系统。

## 石碑选型

| Prefab | 原模型 | 用途 |
|---|---|---|
| A-slate | HIGHLANDS Stone_1，106 三角面 | 推荐单幅碑，风图案 |
| B-square | HIGHLANDS Stone_2，102 三角面 | 更规整的备选，风图案 |
| C-natural | HIGHLANDS Rock_1，350 三角面 | 原岩竖起的对照 |
| D-wide | HIGHLANDS Stone_2，102 三角面 | 推荐宽幅叙事碑，先与后 |

用原模型非均匀缩放、绕 X 轴旋转 -68 度制作，网格顶点未修改。各候选带静态 MeshCollider、地面根节点和现有 StoneEngravingReadability；原图及动物字完整落在连续石面中。近景按各自尺寸取景，不能据图片大小推断世界尺度。

## 重现与验证

打开项目的编辑状态，向 `StoneEngravingReview/20261030/command.txt` 写入 `paint` 或 `tablets`。`StoneTabletStudy` 在独立 PreviewScene 渲染并生成对应实验资产，Play 中拒绝执行。检查 `status.txt`。`inventory` 还依赖同目录 `inventory-input.json`。

运行 `python Tools/StoneEngraving/TabletStudy/package_review.py` 生成桌面的两份独立 HTML；仅复制原始 Unity PNG，不编辑截图。脚本同时核对 14 项原场景和原资产 SHA-256。

`Paint/checks.txt` 检查 shader 编译；`Tablets/checks.txt` 是逐刻画点落面检查（每块 11,189–17,470 点，无缺失、无落在陡边）。`verification.json` 保存哈希和图片数量。预览页还有空白石形、侧面和俯视缩小图。

本轮验证是编辑态资产与渲染验证。没有执行 Play 或在主场景替换石头；最终摆放方向和视觉选型待用户确认。

## 主场景三版本对照

用户随后要求把三个上色版本放进游戏。`place-three` 在干净的 WatersideV2 编辑场景中新增「雨水石刻 · 三版本对照」组；A 原始刻槽、B 白色刻槽、C 内部彩绘从左到右，中心 X 为 -44、-40、-36，Z 为 -47，位于天气屋附近。复制当前场景原雨水石的网格、朝向与缩放，按地形高度落地。原石仍保留。

放置前保存 `before-three-versions.unity` 场景备份；放置后生成 `three-in-scene.png` 与 `three-placement.txt`。该命令会保存已确认的主场景新增组，不用于通用场景编辑。
