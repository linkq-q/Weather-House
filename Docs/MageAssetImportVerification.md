# Mage 资产导入核验

核验日期：2026-10-25（香港时间）。本单元仅导入 Mage 候选资产与许可证，尚未替换主场景玩家，尚未完成主场景 Mage Play 验证。隔离候选通过和临时场景 API 通过不代表主地图运行或最终视觉验收通过。

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

后续仍需在真实主地图替换玩家后，通过原控制器输入完成起停、转向、行走及两脚 Idle 接地验证，并检查真实 Game 画面和披风/帽子效果。主图集成与该 Play 生命周期验证属于后续单元。

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
