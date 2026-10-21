魔法少女独立资产预览

此目录独立于正式地图。只使用 URP 自带 Unlit/Lit 材质检查底色和普通受光，不包含最终 Toon Shader，也不生成法线、Ramp 或高光控制贴图。

当前交付状态
- 结构检查副本，不是已完成精修的生产模型。
- Character/MagicalGirl_Working.fbx 使用 Generic、41 根骨骼；沿用原 UV、Source 单材质槽和一张原底色。
- 导出的是六段 WORK 工作动作：idle、walk、run、afraid、agree、sob。Unity 中以 Armature|动作名 命名，脚本识别最后一个竖线后的名称；控制器负责移动，动画按原地片段使用。
- 当前没有 BlendShape、辅助骨链或真正的 jumpStart / airborne / land 跳跃片段。
- Face / Hair / Hair2 / Body / Leg 五区名称已确定；正式五区 UV 和底色重绘待眼睑、前发等局部结构人工修复并验证后再做。

创建预览
打开独立场景 Assets/MagicalGirlPreview/MagicalGirl_Preview.unity 进入试验。
需要重建时使用 Unity 菜单 Weather House > Magical Girl > Create Independent Preview。
工具会拒绝覆盖任何有未保存改动的打开场景，请先自行保存用户工作。
生成场景 Assets/MagicalGirlPreview/MagicalGirl_Preview.unity，不修改正式场景、项目设置或 Build Settings。
再次执行会重建本目录的 Generated 材质/Animator 与预览场景。正式手调预览前请先另存副本。

播放
进入 Play 并聚焦 Game：WASD 移动，Shift 跑，右键旋转镜头，滚轮远近，R 或“角色归位”重置位置。
Space 仅测试控制器起落位移，空中使用 idle 占位；当前不能据此验收跳跃姿态与落地衔接。
“待机 / 走路 / 跑步”按钮用于原地看动作；WASD 恢复移动。1/2/3 或面板按钮播放害怕/同意/哭泣，移动可退出。
“纯底色 / 普通光照”切换基础材质；普通光照模式可调主灯方向。
微笑、惊讶、抬眉、压眉按钮因缺少 BlendShape 而禁用；眨眼按钮、自动眨眼和闭眼滑杆目前没有可驱动的形态键，不会产生眨眼效果。

辅助运动边界
生成场景中的 springs / 辅助骨摆动默认关闭，当前辅助链数为 0；勾选开关也不会让头发或裙摆自动变软。
脚本仅提供低摆幅骨链惯性试验接口，不替代正确权重。Collision Spheres 为空，碰撞尚未拟合或验收；骨链末端约束不能保证发束表面、骨段中部和裙布之间无穿插。

后续可选接口（当前未完成）
- 跳跃片段：jumpStart、airborne、land。
- BlendShape：Blink_L、Blink_R、Smile、Surprised、BrowUp、BrowDown。
- 五区材质槽：Face、Hair、Hair2、Body、Leg；底色可命名为 Face_BaseColor.png 等并放在 Character 下，或由 FBX 材质直接引用。
- 辅助链根部与各节必须以 WH_HairBack_ 或 WH_Skirt_ 开头；链条不得分叉，末端需保留子节点定义长度。添加后仍需检查权重、拟合碰撞与逐姿态验收。
- Builder.ModelYawOffset 只改变预览 Visual 子物体的朝向，不改 FBX；重新导出后需在 Play 中确认实际面朝方向。

验证
Check Preview Logic 检查命名、材质分区与走跑相位转移。
Validate Open Preview 检查资源接线并输出 PreviewValidation.json。
这些属于编辑模式检查，不能替代 Play 中的朝向、脚滑、跳跃衔接、表情中间态、头发裙摆穿插及最终外观验收。
