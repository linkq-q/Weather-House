# 天气屋 · 苔藓、远景雾与统一风样板

三批内容均为独立验收场景，尚未接入主地图。Build Settings 仍使用 `WeatherHouse_MapDraft.unity`。先保存当前工作、退出 Play，再从 **Weather House > Environment Lab** 打开样板；菜单在场景已存在时直接打开，不重新生成。

| 菜单 | 场景 | Play 中可调整 |
| --- | --- | --- |
| 01 苔藓 · 打开样板 | [01_MossStudy.unity](01_MossStudy.unity) | 覆盖量、边缘清晰度、纹理重复、中央石块旋转、显示开关 |
| 02 雾层次 · 打开样板 | [02_FogStudy.unity](02_FogStudy.unity) | 基础距离雾开关、远景雾片透明度、仅距离雾对照 |
| 03 风动 · 打开样板 | [03_WindStudy.unity](03_WindStudy.unity) | 风力倍率、风速、世界风向，以及静风、微风、强风预设 |

**浏览与保存**

进入 Play 后使用左侧面板。`Home` 回到样板全景，`4` 行走，`5` 飞行；右键看向，WASD 移动，Shift 加速，飞行时 E/Q 升降，`Tab` 隐藏面板。环绕模式可用滚轮调距离。相机是平地浏览工具，树木、石头没有碰撞阻挡。

Play 调参使用材质副本，退出后恢复。要保留参数，退出 Play 后在 Inspector 修改对应的 `MossCoverage`、各片 `FogCard` 或 `EnvironmentWind`，再保存场景。雾片的颜色、尺寸与摆放也可在 Inspector 调整。

**效果来源与使用范围**

- 苔藓复用 HIGHLANDS 的 Surface 材质及苔藓贴图，通过世界法线控制朝上覆盖。同一石块旋转后会重新判断朝上的表面；这是一层材质效果，没有新增苔藓几何体。
- 雾样板使用 Unity/URP 基础距离雾，加三片复用原包 Fog 材质与 Smoke 透明贴图的平面，带靠近渐隐。参考中的雾片方法适合固定或受控视角的远景布置；侧看时平面会变薄，透明排序也会随视角变化。**透明水面交叠及倒影尚未验收**，靠近渐隐不能替代全方向体积雾。
- 风样板复用 Painterly Grass、Painterly Tree 和现成植物网格。当前只控制明确绑定的 3 棵树、3 个合并草批次，统一风力、风速和世界 XZ 风向，保留原有摆幅比例、波形与空间频率。没有接管整个项目的材质，也没有连接主地图的局部起风玩法。已有 MaterialPropertyBlock 的 Renderer 会被跳过并提示。

**本轮已有验收证据**

环境为实际 Play / NVIDIA GeForce RTX 5060 Laptop GPU。以下结果摘自当前报告，画面风格仍需人工判断。

| 样板 | 已记录结果 | 报告与画面 |
| --- | --- | --- |
| 苔藓 | 运行材质隔离通过；0°、90°、180° 旋转的 GPU 探针均仅在世界上半部检出苔藓，关闭后覆盖消失 | [报告](../../EnvironmentLabReview/01_Moss_checks.txt) · [全景](../../EnvironmentLabReview/01_Moss_Overview.png) · [近景](../../EnvironmentLabReview/03_Moss_Close.png) |
| 雾 | 距离雾与附加雾片均产生可测画面差异；相机距雾片 2 米时，正常近距渐隐与关闭雾片的对照为零像素差异 | [报告](../../EnvironmentLabReview/02_Fog_checks.txt) · [仅距离雾](../../EnvironmentLabReview/02_Fog_B_DistanceOnly.png) · [叠加雾片](../../EnvironmentLabReview/02_Fog_C_DistanceAndCards.png) |
| 风 | 静风间隔 0.3 秒两帧零差异；风力 2、间隔 0.5 秒时，全景、草独立探针、树独立探针均检测到摆动；材质隔离及关闭恢复检查通过 | [报告](../../EnvironmentLabReview/03_Wind_checks.txt) · [实际录帧动图](../../EnvironmentLabReview/03_Wind_Preview.gif) · [静风](../../EnvironmentLabReview/03_Wind_Still_A.png) · [有风 A](../../EnvironmentLabReview/03_Wind_On_A.png) · [有风 B](../../EnvironmentLabReview/03_Wind_On_B.png) |

像素比较证明效果生效与可关闭，不代表性能评级；未测移动端。草、树探针会暂时隐藏另一组，避免把一组的运动误算为另一组。

另外已在真实风动测试中主动提前退出 Play，确认迭代器清理执行、测试状态清除、原场景和编辑器设置恢复，见[取消检查](../../EnvironmentLabReview/cancel-check.txt)。三个样板验证后均返回原先的主地图场景。

**接下来由你判断**

- 苔藓颜色、覆盖量与边缘是否符合天气屋的自然感，哪些石头、倒木和屋顶适合使用。
- 雾的颜色与浓度是否保留了探索可读性，主视角的前中后景应在哪里分层，雾片放在哪些远景位置。
- 草与树的摆动是否过强、过快，平日背景风应取什么程度；最终布局、构图及合入主地图后的效果仍需你确认。
