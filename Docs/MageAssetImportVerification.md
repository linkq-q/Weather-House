# Mage 资产导入核验

核验日期：2026-10-25（香港时间）。下文保留初次资产导入单元的核验记录；随后已完成主场景替换、起停接地修复和主 Play，见末尾“主场景集成”。

## 文件与引用

- [main-import-manifest.json](../JourneyReview/Revision_20261024/MageCandidate/Review/main-import-manifest.json) 的 29 个文件，在 staging 与主 `Assets` 中的 SHA256、字节数全部一致。清单 SHA256：`d94ed4b46cc91aa729e0236b59fdc10c1f403ad47599ce0fab5d6f78285f47bc`。
- 主目录另有 Unity 生成的 3 个许可证 `.meta`，详见下表；没有其他清单外文件。提交范围为 32 个资产文件及本文档。
- [delivery-audit.json](../JourneyReview/Revision_20261024/MageCandidate/Review/delivery-audit.json) 声明的 34 个依赖均存在。对新资产、这些依赖及其 `.meta` 的静态扫描得到 42 个非内置 GUID，均唯一解析到主 `Assets` 或当前 URP 14.0.12 包；无缺失或重复 GUID。原生候选审计记录为 missingScripts = 0。静态 GUID 核对本身不证明运行效果。
- 候选 prefab 仍继承原 ForestSprite；沿用原帽、头发、前裙、披风及相应材质、控制器和物理脚本。原 prefab 当前 SHA256 仍为 `efb0b37c602a0460d780bee1030e37e5c8cd55d636aeea4ca66b43e64c9714c9`，不在本单元提交范围。候选 prefab SHA256 为 `d88e65c2176375b73d003a4eaa79b57978ed2e507e29d5269ffc73812247425d`。
- 当前已保存的主 `Assets` 场景中未发现候选 prefab GUID `e68f66c36f7fd2e4ab60f0aaca8610c9` 的引用。隔离 Review 场景、测试脚本、渲染管线资产、中间动作和 Blender 源保留在 `JourneyReview`，不纳入本单元。

| 清单外的必要文件 | SHA256 |
| --- | --- |
| `Assets/Characters/ForestSpriteMage/Licenses.meta` | `6000e03a361b55d3ee3158253cd53e13ca45b45208a2fc4ee531ac3705e48f80` |
| `Assets/Characters/ForestSpriteMage/Licenses/KayKit_Adventurers_CC0.txt.meta` | `3bdea185c78132fadcf0e4dad18f6b224c9735604a6728d004c2c72b30c1c06c` |
| `Assets/Characters/ForestSpriteMage/Licenses/KayKit_Animations_CC0.txt.meta` | `4d7f3ef3acbfe2eaa9ded824452b4c07689a91844b7f862df2b579e6ded6ba18` |

## 已有验证及边界

| 证据 | 结果与适用范围 |
| --- | --- |
| [候选资产审计](../JourneyReview/Revision_20261024/MageCandidate/Review/delivery-audit.json) | PASS：411 个 Cloth 粒子、13 根弹性骨、11 个绑定碰撞体；原相机默认值、胶囊、披风系数保留。步行速度 2.925，步行周期距离 1.7，跑速 4.5。 |
| [最终隔离 Play](../JourneyReview/Revision_20261024/MageCandidate/Review/UnityMotionAuthoritative/result.json)／[24 秒原速视频](../JourneyReview/Revision_20261024/MageCandidate/Review/UnityMotionAuthoritative/MageCandidate_24s_original_speed.mp4) | PASS：1,440 帧，480 Hz Cloth solver，无非有限顶点。独立骨矩阵与 `BakeMesh(true)` 世界坐标最大差 0.007865 mm；行走鞋底样本约 −0.609 至 +0.150 mm，停止 Idle 约 0 至 +0.012 mm。画面为隔离场景和隔离相机。旧 `BakeMesh(false)` 的约 −8 cm 字段为尺度错误诊断，不能作为实际鞋底高度。 |
| [可打断动作回归](../JourneyReview/Revision_20261024/MageCandidate/Review/UnityMotionFinal/interrupt-regression.json) | PASS：2,081 帧；交互、涉水、跳跃、90° 转向四种中断，28 次重新启用；取消后观察权重为 0，稳定姿态差为 0，控制器运动最大差约 0.00012 mm。 |
| [隔离原生替换与 Undo](../JourneyReview/Revision_20261024/MageCandidate/Review/MainIntegration/probe-v2-verification.json) | Unity 2022.3.62f3c1 普通临时 Additive 场景 PASS，Undo PASS。PreviewScene 会被原生 API 视为修改 Variant 父级而拒绝，失败记录保留，后续入口使用普通 Additive 场景。 |
| 主项目 [Additive Probe](../JourneyReview/Revision_20261024/MageCandidate/Review/MainIntegration/probe-additive.txt)／[Undo Probe](../JourneyReview/Revision_20261024/MageCandidate/Review/MainIntegration/probe-undo.txt) | 2026-10-24 18:57:41／18:57:58 UTC 的两份结果均 PASS；临时实例原对象身份、场景 override、额外支撑碰撞体、外部引用、相机和速度保留；Undo 恢复原 prefab 与 Animator。测试未修改主地图，只证明主项目内原生替换路径可用。 |

初次导入时尚未执行主地图替换；后续集成结果单独记录如下，不能将早期隔离场景数据当作主图数据。

## 精确提交清单

以下 33 个文件为本单元范围；不包含主场景、原 ForestSprite、主图验证器或其他并行改动。核验期间仅新写本文档，未操作 Unity、暂存或提交 Git。

```text
Assets/Characters/ForestSpriteMage.meta
Assets/Characters/ForestSpriteMage/Generated.meta
Assets/Characters/ForestSpriteMage/Generated/ForestSpriteMage.prefab
Assets/Characters/ForestSpriteMage/Generated/ForestSpriteMage.prefab.meta
Assets/Characters/ForestSpriteMage/Generated/Idle_A_MageSoles.anim
Assets/Characters/ForestSpriteMage/Generated/Idle_A_MageSoles.anim.meta
Assets/Characters/ForestSpriteMage/Generated/Mage.controller
Assets/Characters/ForestSpriteMage/Generated/Mage.controller.meta
Assets/Characters/ForestSpriteMage/Generated/MageOriginalPalette.mat
Assets/Characters/ForestSpriteMage/Generated/MageOriginalPalette.mat.meta
Assets/Characters/ForestSpriteMage/Generated/OriginalHeadOnly.asset
Assets/Characters/ForestSpriteMage/Generated/OriginalHeadOnly.asset.meta
Assets/Characters/ForestSpriteMage/Generated/Running_A_MageSoles.anim
Assets/Characters/ForestSpriteMage/Generated/Running_A_MageSoles.anim.meta
Assets/Characters/ForestSpriteMage/Generated/Walking_MageBroad_MageSoles.anim
Assets/Characters/ForestSpriteMage/Generated/Walking_MageBroad_MageSoles.anim.meta
Assets/Characters/ForestSpriteMage/Licenses.meta
Assets/Characters/ForestSpriteMage/Licenses/KayKit_Adventurers_CC0.txt
Assets/Characters/ForestSpriteMage/Licenses/KayKit_Adventurers_CC0.txt.meta
Assets/Characters/ForestSpriteMage/Licenses/KayKit_Animations_CC0.txt
Assets/Characters/ForestSpriteMage/Licenses/KayKit_Animations_CC0.txt.meta
Assets/Characters/ForestSpriteMage/Model.meta
Assets/Characters/ForestSpriteMage/Model/MageBody.fbx
Assets/Characters/ForestSpriteMage/Model/MageBody.fbx.meta
Assets/Characters/ForestSpriteMage/Runtime.meta
Assets/Characters/ForestSpriteMage/Runtime/MageAccessoryPose.cs
Assets/Characters/ForestSpriteMage/Runtime/MageAccessoryPose.cs.meta
Assets/Characters/ForestSpriteMage/Runtime/MageLocomotionPresentation.cs
Assets/Characters/ForestSpriteMage/Runtime/MageLocomotionPresentation.cs.meta
Assets/Characters/ForestSpriteMage/Textures.meta
Assets/Characters/ForestSpriteMage/Textures/mage_texture.png
Assets/Characters/ForestSpriteMage/Textures/mage_texture.png.meta
Docs/MageAssetImportVerification.md
```

## 主场景集成

主玩家已使用 Mage 身体和大步幅动画，保留原头部、头发、帽子、前裙、月亮披风、控制器与外部引用。场景提交只替换 6436656—6436659 四个精确 fileID 文档，其余并行场景编辑未暂存；原 ForestSprite prefab 未修改。

主图验证发现 Idle/Walk 混合可使鞋底额外陷地约 38 mm。现有 Presentation 脚本仅在落地起停过渡中补偿视觉髋部高度；不改变玩家根、胶囊、动画速度、帽子或披风物理参数。5,042 个隔离原生姿态及 570 帧隔离 Play 通过。

[主场景 Play 记录](../JourneyReview/Revision_20261024/MageCandidate/Review/MainPlay/Runs/20261024_203229_655_lifecycle_mage/result-203254321.json)通过：原控制器走出、停步、180° 掉头并返回，实际位移 5.546 m，步速 2.925，足部前后摆幅 0.460 m。53 个观测样本中鞋底最低约 −10.863 mm，Idle 最大离地 8.094 mm，独立 CPU/BakeMesh 世界坐标差 0.012 mm。411 个布料粒子、13 根弹性骨、11 个绑定及 480 Hz 布料设置保持。

已逐张检查该记录的 6 张原始 Game 截图，角色轮廓、饰物和起停姿态正常；静帧不证明所有连续帽尖运动的主观自然程度。早期失败及其诊断保留在相邻运行目录。退出 Play 后场景保持原保存哈希、无脏编辑，手账与临时选项恢复。当前主图包含并行地图改动，四文档索引提交自身未在另一份干净 checkout 运行。
