# 羊的水面游泳动作

`sheep_swim.fbx` 来自本项目原羊骨架的离线动画制作，保留原有 29 骨、蒙皮和材质绑定。

- 来源为 UniMate 上身候选与重新编写的四腿循环划水轨迹；不是未经修整的原始生成结果。
- 原始交付：`C:\Users\31483\Desktop\天气屋\3d资产\羊游泳动画_20261024`，其中保留候选、Blend、校验报告和双视角视频。
- Generic 动画，60 fps，完整周期 1.2 秒，固定骨架对象及 Hips；不应用根运动。
- 导入设置由 `JourneySwimImport.Load()` 读取默认完整 take 并开启 Loop Time，不再叠加 Loop Pose 修正。
- 源模型本地 Y = 0.64 为水线，场景构造时计入 Visual 的缩放和垂直偏移；每只羊单独设置 `profile.waterlineHeight`。
- 有该片段时，JourneyHerd 不叠加备用的程序划腿、抬头或浮动，以免双重驱动。

离线素材验证与 Unity 运行验证分开记录。运行中的状态过渡、实际头部净空与完整循环图见独立工程的 `WildlifeJourneyReview`。
