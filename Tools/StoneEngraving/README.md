# 雨与水石刻样板

这是第一块可复用的贴图石刻。流程由 Codex 生成黑白刻线图及崩边候选，再由脚本计算凹槽高度、法线和遮罩，最后由 Unity 投影材质把图案贴合到现有石头网格。上方为项目动物语的“雨”，下方为“水”。无需手工雕刻或重新展 UV。

## 交付入口

- 预览场景：`Assets/Scenes/Experimental/StoneEngraving_RainWater.unity`。
- 石头预制体：`Assets/Experiments/StoneEngraving/RainWater_ExistingRock.prefab`。
- 材质：`Assets/Experiments/StoneEngraving/RainWater_EngravedStone.mat`。
- 实际 Unity 截图与验证报告：`StoneEngravingReview/20261029`。

预制体复用 WatersideV2 内 Rock_1 的网格和选定实例的缩放，保留 350 个三角面。来源实例、原位置及场景哈希保存在 `source-selected.json`。这块石头较扁，图案放在宽阔顶面以便俯视观察。独立预览中为检查刻槽另设了灯光、地面和相机。

## 主地图查看

样板已放入 `Assets/Scenes/WeatherHouse_WatersideV2.unity`，位于天气屋南侧小路旁的草地，离原出生点约 11 米。层级名称为 **雨与水石刻 · 天气屋旁**，位置 `(-36.7036, 2.6329, -40.5344)`。保留原预制体的网格、缩放、刻线、字形和材质，仅调整位置并在场景实例上添加静态 MeshCollider。没有改地形或搬动原场景物体。

在 Unity **顶部菜单**选择 **Weather House → 试玩 → 雨与水石刻（独立存档）**。需要先打开上述主地图并退出其他 Play；此入口会把角色临时放到石刻南侧供查看，使用 `StoneEngravingReview/20261029/MainScene/isolated-save`，退出 Play 后恢复编辑态原来的角色和相机。

主场景的 Play 证据位于 `StoneEngravingReview/20261029/MainScene`。`02-play-overhead.png` 使用角色实际俯视镜头，`03-play-close.png` 使用临时近景检查镜头；灯光和环境仍来自主地图。检查包括落地、真实网格碰撞、露出地表的石头与道路／水域的间距、源网格／缩放／材质未改，以及 Shader 编译。`before-placement.unity` 保留放置前的工作副本，`scene-delta.json` 检查场景只新增该实例及其碰撞体。

本次接入用于查看石刻在场景中的外观；照片识别、谜题奖励和玩家拍照镜头的可读性尚未接入或验收。

## 自动制作

1. Codex 通过 ImageGen 参考概念图生成刻线图。原始工具调用保存在 `Source/generation-call.txt`，生成结果是 `Source/RainWaterStencil.png`。`RainWaterChippedCandidate.png` 是 AI 生成的破边候选，不会整张直接应用。
2. `build_maps.py` 读取 `rain_water.json`，按固定随机种子仅选择部分局部区域混入破边候选，将黑线转成向内的高度场。字形直接读取 `Assets/Game/Art/AnimalGlyphsV04/rain.png` 与 `water.png` 的原始 alpha；字库资产不改动。高度场求导得到 Unity 使用的 +Y 法线；凹槽深度和遮蔽写入遮罩。
3. `select_source.py` 从已保存的 WatersideV2 场景提取候选实例；`prepare_review.py` 将选定网格、贴图和现有 URP 包缓存复制或引用到隔离的验证工程。
4. `StoneEngravingReviewBuilder.cs` 在隔离工程导入贴图、创建材质、预制体和场景，并用 Unity 渲染近景、斜角、俯视和不同方向照明的截图。

从项目根目录运行以下命令可重新生成验证结果。需要 Python 的 NumPy、Pillow 和项目对应版本的 Unity；脚本不改变主编辑器的场景或 Play 状态。

```powershell
& Tools/StoneEngraving/run_review.ps1 -Python '<Python 路径>'
```

验证工程输出位于 `StoneEngravingReview/20261029/UnityProject`，不会自动覆盖主项目的已交付资产。复用到其他石头时，由 Codex 为该石头复制材质并设置投影中心、方向和尺寸。后续图案沿用相同计算流程，无须逐笔雕刻。

## 材质参数

| 参数 | 用途 |
| --- | --- |
| Carving normal / Carving depth… | 自动生成的法线与遮罩 |
| Carving strength | 控制刻槽视觉强度，0 可比较未雕刻表面 |
| Groove darkening | 控制槽底颜色，避免依赖固定光照画出阴影 |
| Groove depth in metres | 控制视差凹槽深度，与生成贴图时的 depth_m 保持一致 |
| Groove self shadow | 控制沿主光方向估算的槽内遮挡强度 |
| Carving center / right / up | 在石头局部坐标中定位图案 |
| Width / height / depth extent | 调节覆盖范围，限制投影穿到背面 |
| Stone color / normal / smoothness | 石头底材 |

运行时只需要法线与遮罩，遮罩的 R 通道同时提供视差高度。16 位 Height 图保留为制作数据。本版使用 16 步视差追踪和 8 步主光方向的槽内遮挡估算，让槽壁在斜视时具有深度感；不产生真实几何凹槽或轮廓，也不做高低模切换。主光槽内阴影是材质近似，不会向其他物体投射凹槽阴影。

当前名义槽深为 80 mm，带有窄倒角、槽底明暗、视差与槽内遮挡。`chip_probability=0.23` 控制制作时局部区域被选中的概率，`chip_seed=20261029` 保证重复生成时破损位置稳定；它不是每帧抖动的随机效果。仅局部边缘混入崩口，图案与字形的主体保留。`BeforeDepth80mm` 保存上一版 50 mm 槽深的同机位截图。`BeforeDepthTuning` 保存最初较平版本，`BeforeChipping` 保存加深但没有崩口和文字的版本。`Captures/08_grazing.png` 与 `09_grazing_without_parallax.png` 分别展示启用和禁用视差及槽内遮挡的斜视结果。

## 验证范围

原独立样板的 6 项数值测试检查平面不被污染、槽深为负、四个方向的边缘法线正确、法线单位长度、可重复生成、稀疏破损的开关与固定种子，以及上雨下水的排布。原 `verification.json` 和 `Captures/` 是独立场景证据；新增的主地图验证单独保存在 `MainScene/`，不覆盖原报告。玩家拍照流程、谜题判定及运行性能仍需后续接入时验证。
