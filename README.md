# 天气屋 · Unity 场景原型

使用 Unity **2022.3.62f3c1** 和 URP **14.0.12**。

## 目录

- `Assets/Scenes/WeatherHouse_WatersideV2.unity`：正式玩法场景；`WeatherHouse_MapDraft.unity` 保留旧原型；`Scenes/Experimental/`：实验与展示场景。
- `Assets/Game/`：玩法、UI、运行时 Resources 与主场景生成资产。
- `Assets/Characters/`：森林精灵、角色预览与卡通角色资源。
- `Assets/Environment/OriginalTrees/`：原树模型、材质与预制体。
- `Assets/Experiments/`：植被、材质、环境与松鼠实验所用代码和资产。
- `Assets/HIGHLANDS - Stylized Environment/`：第三方资源，保留包内结构。
- 项目根目录各 `*Review/`：历史验收资料，不属于 Unity 运行时资产。

## 打开与试玩

1. 安装 Git LFS，克隆后执行 `git lfs pull`，确认模型、贴图和 TerrainData 已下载。
2. 用 Unity Hub 打开项目，等待资源导入完成。
3. 打开 `Assets/Scenes/WeatherHouse_WatersideV2.unity`，进入 Play。

WASD 移动，Shift 快走，空格跳跃，右键拖动视角，滚轮缩放，Tab 查看俯视地图。

走近动物后按 `E` 交谈并用中文解释它的话。真实 AI 确认理解具体请求后，右上角才显示帮助任务。`C` 打开相机，空格或快门记录照片；`J` 打开实体手账学习手势，`G` 按住鼠标描画施法，`Esc` 关闭当前界面。天气只作用于玩家身边；地图上选定的同类植物可靠近 `E` 采集，任意来源的同种草均可使用。`F` 进行附近物件交互或持草编垫，`Shift+F` 收回垫子或倒空水碗。雨和收起天气直接教学，其余手势来自识字奖励。正式玩法不使用旧 `1 / 2 / 3` 施法。

拍照使用第一人称镜头，滚轮调整 24–100 mm 焦距，点击画面物体对焦，右键转动。退出恢复探索视角。

UI 参数直接在场景 `06 观察、天气与动物` 的 `Gameplay Hud` 组件中调整，含两种气泡独立尺寸、动物语笔画粗细、控件线宽和字体粗细。Play 中即时预览，永久保存需在退出 Play 后填写并保存场景。详细参数位置见 [玩法原型验收](Docs/玩法原型验收.md)。

手账照片默认显示封面，点开后手动播放；没有动物或未命中任务的风景照也会保留。相机页签提供 22 个摄影任务和 4 个公开互动情境，直接奖励对应词义，同一张照片可完成多个任务。完整字库共 31 词，已知短含义同步显示，填写猜义不作为奖励门槛。AI 继续负责帮助理解；本地服务配置见 [AI 接入说明](Docs/AI接入说明.md)。

正式场景复用已有生态动物模型和动作。关键个体承担剧情话轮，次级动物保留自主生活；牧场动物保留，渡口另有固定羊鹿。帮助、关系、逐字确认分别记录。手账、照片与生活状态存到 `Application.persistentDataPath`，不会进入仓库。

旧 MapDraft 为 360 × 360 米草稿；正式水岸场景的玩法验收和实际运行证据见 [本轮验收入口](Docs/LifeGameplay/验收入口.md)。场景物体及 Terrain 均可继续手动编辑。

资源包的 Demo 与原配音频已移除，预制体中的对应引用已清空。正式玩法使用自己的音效目录，草地／泥土／石头区分脚步，交互按动作复用素材；动物音色由原创短音节合成，制作工具和许可记录见 [音效接入说明](Tools/LifeAudio/README.md)。Build Settings 默认场景为 WatersideV2。

## 修改与验证

正式玩法使用 `Weather House/Life/` 下的源码检查与隔离存档 Play 验收。下列 MapDraft、PrototypeReview 命令属于旧原型，根目录各历史 Review 中的外部 DLL 工具也不作为正式运行依赖；不要用旧报告代替水岸场景验收。

- `Weather House/Build Map Draft` 根据编辑器脚本重新生成草稿。它会覆盖当前生成场景及生成资产；保留手动摆放前应另存场景和 TerrainData。
- `Weather House/Validate Map Draft` 检查路线坡度、碰撞、水下路段和缺失脚本。
- `Weather House/Capture Review Views` 输出验收视角到本地 `PrototypeReview/`。
- Play 模式下 `Weather House/Check Runtime Movement` 检查三段道路上的 CharacterController 位移和接地。它不替代人工试玩。
- `Weather House/Review Procedural UI Controls` 生成程序化控件预览与 `Generated/UI Controls.prefab`。
- `Weather House/Validate Gameplay State`、`Check Animal Glyph Geometry`、`Validate AI Result Parser` 检查手账状态、原字形与离线判定解析。
- Play 模式下 `Weather House/Check Gameplay Runtime` 检查天气、动物反馈和真实动态照片。验收报告保存在本地 `PrototypeReview/`。

`Assets/`（包括 `.meta` 和 `Game/Generated/`）、`Packages/`、`ProjectSettings/` 及概念设计纳入版本管理。Unity 缓存、IDE 工程文件和本地验收输出由 `.gitignore` 排除。
