# 天气屋 · Unity 场景原型

使用 Unity **2022.3.62f3c1** 和 URP **14.0.12**。

## 目录

- `Assets/Scenes/WeatherHouse_MapDraft.unity`：主场景；`Scenes/Experimental/`：全部实验与展示场景。
- `Assets/Game/`：玩法、UI、运行时 Resources 与主场景生成资产。
- `Assets/Characters/`：森林精灵、角色预览与卡通角色资源。
- `Assets/Environment/OriginalTrees/`：原树模型、材质与预制体。
- `Assets/Experiments/`：植被、材质、环境与松鼠实验所用代码和资产。
- `Assets/HIGHLANDS - Stylized Environment/`：第三方资源，保留包内结构。
- 项目根目录各 `*Review/`：历史验收资料，不属于 Unity 运行时资产。

## 打开与试玩

1. 安装 Git LFS，克隆后执行 `git lfs pull`，确认模型、贴图和 TerrainData 已下载。
2. 用 Unity Hub 打开项目，等待资源导入完成。
3. 打开 `Assets/Scenes/WeatherHouse_MapDraft.unity`，进入 Play。

WASD 移动，Shift 快走，空格跳跃，右键拖动视角，滚轮缩放，Tab 查看俯视地图。

玩法已绑定到同一场景。走近并看向动物可观察原字语；`E` 输入中文回应，`C` 打开相机，空格或点击快门记录 3 秒动态照片，`J` 打开手账，`Esc` 关闭当前界面。`1 / 2 / 3` 在附近动物生活区域施雨、放晴、起风，没有法力消耗或冷却。

拍照使用第一人称镜头，滚轮调整 24–100 mm 焦距，点击画面物体对焦，右键转动。退出恢复探索视角。

UI 参数直接在场景 `06 观察、天气与动物` 的 `Gameplay Hud` 组件中调整，含两种气泡独立尺寸、动物语笔画粗细、控件线宽和字体粗细。Play 中即时预览，永久保存需在退出 Play 后填写并保存场景。详细参数位置见 [玩法原型验收](Docs/玩法原型验收.md)。

手账照片默认显示封面，点开后手动播放；点击右侧遇见过的字可筛选照片并自由填写猜义。猜义不即时判对错。后续不同动物的请求才允许通过 AI 确认词义；尚未配置真实服务，填写方法见 [AI 接入说明](Docs/AI接入说明.md)。

当前动物为可替换的程序化占位造型。庭院松鼠、湖边小蛙、高台小鸟和林中小兔使用可编辑的临时请求，便于先验收完整交互。天气任务与语言理解独立。手账与照片存到 `Application.persistentDataPath`，不会进入仓库。

场景为 360 × 360 米的草稿，包含湖泊、林间探索路线、山脊、高台、浅谷及四个主要地点。树木、草、水和天空使用 HIGHLANDS 资源包；场景物体及 Terrain 均可继续手动编辑。

资源包的 Demo 与原配音频已移除，预制体中的对应音频引用已清空，后续使用项目自己的音频。Build Settings 默认场景为此原型。

## 修改与验证

- `Weather House/Build Map Draft` 根据编辑器脚本重新生成草稿。它会覆盖当前生成场景及生成资产；保留手动摆放前应另存场景和 TerrainData。
- `Weather House/Validate Map Draft` 检查路线坡度、碰撞、水下路段和缺失脚本。
- `Weather House/Capture Review Views` 输出验收视角到本地 `PrototypeReview/`。
- Play 模式下 `Weather House/Check Runtime Movement` 检查三段道路上的 CharacterController 位移和接地。它不替代人工试玩。
- `Weather House/Review Procedural UI Controls` 生成程序化控件预览与 `Generated/UI Controls.prefab`。
- `Weather House/Validate Gameplay State`、`Check Animal Glyph Geometry`、`Validate AI Result Parser` 检查手账状态、原字形与离线判定解析。
- Play 模式下 `Weather House/Check Gameplay Runtime` 检查天气、动物反馈和真实动态照片。验收报告保存在本地 `PrototypeReview/`。

`Assets/`（包括 `.meta` 和 `Game/Generated/`）、`Packages/`、`ProjectSettings/` 及概念设计纳入版本管理。Unity 缓存、IDE 工程文件和本地验收输出由 `.gitignore` 排除。
