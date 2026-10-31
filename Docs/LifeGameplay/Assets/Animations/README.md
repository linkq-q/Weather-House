# 松鼠生活动作

2026-10-26。在项目既有 `Assets/Experiments/Squirrel/Models/turn.fbx` 的 27 骨骨架上，用 Blender 5.1.1 编写新姿态与曲线。沿用原模型、权重、绑定姿态和贴图，原资产权利不变；这些新骨骼动作未使用外部动作包。

| Resources 路径 | 秒 | 播放方式 | 表演 |
| --- | --- | --- | --- |
| LifeAssets/Animations/SquirrelIdle | 3.2 | 循环 | 后脚接地的坐立呼吸 |
| LifeAssets/Animations/SquirrelDrink | 3.0 | 单次 | 俯身、靠近水面、轻微饮水动作、回坐 |
| LifeAssets/Animations/SquirrelAccept | 2.0 | 单次 | 前爪伸向近处物件、低头检查、回坐 |
| LifeAssets/Animations/SquirrelRestDown | 2.0 | 单次 | 前爪撑地，身体伏下 |
| LifeAssets/Animations/SquirrelRestLoop | 3.2 | 循环 | 低伏呼吸，眼睛保持模型原样 |
| LifeAssets/Animations/SquirrelRestUp | 2.2 | 单次 | 先抬头再撑起，回到坐立 |

使用 `Resources.LoadAll<AnimationClip>(path)` 取同名 clip，忽略 Unity 生成的预览 clip。仅给本轮关键松鼠 `agent.squirrel/14` 和 `/19` 动态绑定，不替换原通用 prefab。它们应同时停用原 `SquirrelTailWave`，该组件的 LateUpdate 会覆盖新动作的尾骨旋转。Drink/Accept 由独立动作入口播放，不覆盖既有 forage/greet。

动作通过骨骼完成，游戏对象根不产生行走位移。两段休息转换与 Idle 端点一致。原 turn 的脚底与尾巴高度差大，因此新动作应和配套 Idle 使用；与既有 walk_biped/run_biped 的转接仍需在场景实际速度和尺寸下检查。饮水中段口鼻采样点距离归一化地面约 0.169 模型单位，原 prefab 的 0.59654 缩放下约 0.101 米，接入层据实际碗和动物朝向对齐。

源文件位于 `Tools/LifeAssets/Source/Animations/*.blend`，已打包贴图；生成、回导检查、渲染分别由 `build_squirrel_actions.py`、`verify_squirrel_actions.py`、`render_squirrel_previews.py` 执行。导出的 FBX 包含原始蒙皮网格以保留绑定姿态；运行时只使用其 AnimationClip，不实例化额外松鼠。

2026-10-26 集成时发现旧导出设置 `FBX_SCALE_ALL` 将原文件的厘米单位、100 倍 Rig 节点转换为米单位、1 倍 Rig。Blender 回导会抵消这项转换，但 Unity 把新曲线绑定到原 prefab 的蒙皮时，骨骼局部位移与原 bindpose 相差 100 倍，导致皮片缩到骨骼附近。六段动作已用 `FBX_SCALE_NONE` 重导为原单位约定：`UnitScaleFactor=1`、`Squirrel_Rig.localScale≈100`。动作设计、原 FBX、源 .blend 和原蒙皮均未改变。`reexport_squirrel_actions.py` 可从已有源直接重导；回导检查额外逐段核对原 walk 的原始 FBX 单位、Rig 缩放和 Head 局部位置。

17:26 正式 Unity `Docs/LifeGameplay/World/squirrel-pose-audit.json` 通过：隔离复制正式 prefab，原 walk/run 与六段新动作共 8 个资源、两种缩放、每段 5 个时刻，共 80 次采样；原 Rig 缩放、实际蒙皮口鼻距离和 Drink 中段根空间口点均通过。该检查直接采样动作，不覆盖场景里连续移动、动作混合或扣水时序；这些仍以世界异步检查为准。

六份源文件原本已采用 Zstandard 压缩。再次压缩保存后总大小为 159,342,777 字节，较原交付仅减少 17,776 字节，不能称为显著瘦身。`compress_animation_sources.py` 关闭备份版本，保存后重开核对网格、蒙皮、骨架、关键帧和打包纹理摘要；排除 Blender 自动丢弃的临时 Render Result，摘要相同，6 个 FBX 哈希不变。证据见 `source-compression.json`。重复打包的源纹理仍保留，未擅自拆成外链资源。

## 已完成检查

- 6 个修正单位的 FBX 独立回导，原 FBX 单位约定、骨名和父子关系、绑定姿态、时长通过；7 个采样时刻含子帧的最大骨位移误差小于 0.102 毫米。
- 休息序列与循环共 6 个接缝检查通过，最大骨位置误差小于 0.001 毫米。
- 解析姿态求解没有超出肢体长度，采样蒙皮未穿过归一化水平地面（容差 0.01 毫米）。
- 六个 MP4 已完整解码，关键动作过渡帧与三张姿态渲染已检查。

数据见 `squirrel-animation-manifest.json`、`squirrel-export-verification.json`，可打开 [动态预览](preview.html)。这些是离线证据；Unity 中的足底、水面接触、实际地形、程序动作叠加和镜头表现由集成验收确认。模型无眼睑，休息不标为闭眼睡眠。
