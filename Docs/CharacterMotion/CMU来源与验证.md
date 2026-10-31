# Mage 录制动作来源与验证

本批使用真实 CMU 动捕，离线重定向到当前 Mage 的 23 骨 Generic 骨架。已接入起步、停步、左右 90° 转身的短承重姿态，以及低频上半身轻快变化；原宽步行走、跑跳、胶囊、水边界和 Cloth 参数保留。2026-10-25 主项目编译和场景真实人物绑定检查通过；用户随后在主场景体验并反馈“没啥问题”，确认本轮动作观感接受；动作实现已提交 `05de414f`。该接受依据用户反馈，不以原生数值通过代替主观验收。

## 来源与许可

数据来自 [CMU Motion Capture Database](https://mocap.cs.cmu.edu/)，其 [官方 FAQ](https://mocap.cs.cmu.edu/faqs.php) 允许复制、修改和再分发。BVH 采用 CMU 官方资源页指向的 [Bruce Hahne 2010 MotionBuilder conversion](https://sites.google.com/a/cgspeed.com/cgspeed/motion-capture/the-motionbuilder-friendly-bvh-conversion-release-of-cmus-motion-capture-database)，作者允许免费用于任何用途。转换包首帧参考姿态仅用于骨架标定，未放入播放片段。原压缩包、许可页面、原始 ASF/AMC/BVH 和 SHA256 保存在本批 ExternalSources 中。

| 最终动作 | 原始动作 | 原始截取帧（120 fps） | Unity 最终裁段 |
| --- | --- | --- | --- |
| CMU_Start | 82_08，站立后向前走 | 1030–1240 | 原截取片 0.30–1.10 秒 |
| CMU_Stop | 16_33，慢走后停下 | 154–286 | 原截取片 0–0.55 秒 |
| CMU_TurnLeft90 | 16_17，左转 90° | 250–355 | 原截取片 |
| CMU_TurnRight90 | 16_19，右转 90° | 202–300 | 原截取片 |
| CMU_Happy | 104_14，HappyStartWalk | 290–450 | 上半身覆盖层，最大 0.3 权重 |

Quaternius 免费包已下载核对，但不含本次专用起停转身片段，未以普通 walk cycle 替代。ATLAS 需结账登记，因此未提交个人资料或下载；本次不依赖它。

## 最小接入

起步速度在 0.18 秒内接回原 2.925 m/s，停步减速最多 0.10 秒；Run、Jump、反向和碰撞仍以原控制器为准。录制姿态窗口独立为 Start 0.32 秒、Stop/Turn 0.28 秒，在窗口 40% 起回混原宽步或 Idle。退出相位来自当前真实 Mage 双脚采样匹配，传给固定时间混合接口前乘以实际 Walk clip 秒长。长帧将剩余混合时间钳为非负。

轻快变化沿原 9–14 秒间隔，持续 1.1 秒，上身权重呈弧形升降，下肢保持原宽步。它包含录制的上身动作，不等于完整小跳或腾空 skip。披风通过一次性每帧输入增加不超过 4° 的既有展开角，不改 Cloth 参数、solver 或碰撞。水中、Run、Jump、交互、急转、driver/presentation 禁用当帧清除变化。

主安装只复制三个运行时源、五个已验证 `.anim`、独立 controller/mask 和对应 meta，并对 Mage prefab 做文本精确增量。原 controller、Generic 骨架、三格新上衣材质、原 prefab NaN 字节均保留。不在主场景运行生成器或 Configure，不调用全局 SaveAssets。

## 实际验证与边界

独立 MotionValidation 项目使用真实 Mage prefab、Animator、原胶囊与真实水深接口。NativeBindingsV3 的五片各 240 个 Transform 曲线路径全部绑定；真实 BakeMesh(true) 与独立 CPU 蒙皮逐顶点校准在 0.1 mm 内。早期错误路径导入失败保留于 NativeBindingsV1。

NativePlayV1（60 fps）及 NativePlay30V2（30 fps）各 14 项通过：五种实际非零动画片段、活动动画在反向/Run/Jump/真实水/交互/禁用/实体墙前的同帧取消，以及默认关闭新功能时原 1.1 秒观察动作。30 fps 首轮 wall 检查失败保留：原胶囊被新放墙正常向后推出 15 mm；修正夹具要求首帧取消且无朝墙前进，三帧后实际速度仍须小于 0.02 m/s，未修改生产碰撞。

60 fps 平地原生数据的承重脚最低网格高度约为地面 -2.54 至 +2.62 mm，步行峰值 2.925 m/s 保留。这是接地及速度证据，不是无脚滑或主观自然程度证明。完整人物观感已由用户在主游戏反馈接受；没有新增自动连续录像或测量，因此不把用户接受外推为所有步相零滑脚、帽子与披风在任意场景都无问题。

主项目原生编译于 UTC 03:33 完成，已有水反射 API 弃用警告保留；本批没有新增编译错误。UTC 03:34:08 只读检查场景内实际“06 森林精灵”：新 controller、五片、两个动画层、recordedMotionEnabled、起停 0.18/0.10 秒、walk 2.925 / run 4.5 均正确，原 Mage 安全检查通过。主场景文件 SHA256 仍为 06230DA2CB9C1B4CCBD30BDA476072EE193D3C1302819A12E1F629053545B26F，检查时处于 clean Edit；临时审查入口已还原。见本批 root-main-preflight.json 和 MainPreflightTransport/receipt.json。本轮未追加主场景自动 Play 验收，也未安装新增的连续观察脚本。

NativeLongFrameV1 用真实 Animator 执行了 8 个 0.33333334 秒长帧，起步/转向/停止安全回到原基准态且无动画错误。首轮新增夹具采样组件在 Play 重载时丢失的日志另行保留；拆成独立 Unity 脚本文件后才获得上述结果，未修改生产控制器。

本批全部原始日志、CSV、独立源码审查与失败记录位于 `JourneyReview/Revision_20261024/CharacterMotionCompletionV1/`。性能任务保持封版。
