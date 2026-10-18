# 天气屋 · Unity 场景原型

使用 Unity **2022.3.62f3c1** 和 URP **14.0.12**。

## 打开与试玩

1. 安装 Git LFS，克隆后执行 `git lfs pull`，确认模型、贴图和 TerrainData 已下载。
2. 用 Unity Hub 打开项目，等待资源导入完成。
3. 打开 `Assets/WeatherHousePrototype/WeatherHouse_MapDraft.unity`，进入 Play。

WASD 移动，Shift 快走，空格跳跃，右键拖动视角，滚轮缩放，Tab 查看俯视地图。

场景为 360 × 360 米的草稿，包含湖泊、林间探索路线、山脊、高台、浅谷及四个主要地点。树木、草、水和天空使用 HIGHLANDS 资源包；场景物体及 Terrain 均可继续手动编辑。

资源包的 Demo 与原配音频已移除，预制体中的对应音频引用已清空，后续使用项目自己的音频。Build Settings 默认场景为此原型。

## 修改与验证

- `Weather House/Build Map Draft` 根据编辑器脚本重新生成草稿。它会覆盖当前生成场景及生成资产；保留手动摆放前应另存场景和 TerrainData。
- `Weather House/Validate Map Draft` 检查路线坡度、碰撞、水下路段和缺失脚本。
- `Weather House/Capture Review Views` 输出验收视角到本地 `PrototypeReview/`。
- Play 模式下 `Weather House/Check Runtime Movement` 检查三段道路上的 CharacterController 位移和接地。它不替代人工试玩。

`Assets/`（包括 `.meta` 和 `WeatherHousePrototype/Generated/`）、`Packages/`、`ProjectSettings/` 及概念设计纳入版本管理。Unity 缓存、IDE 工程文件和本地验收输出由 `.gitignore` 排除。
