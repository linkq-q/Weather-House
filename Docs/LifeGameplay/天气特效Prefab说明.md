# 施法圈与起风特效

## 在 Unity 里调整

资源目录：`Assets/Game/Life/Resources/LifeWeather/`。

在非 Play 模式下双击 Prefab，进入 Prefab Mode 修改并保存。游戏下次创建天气时会加载保存后的版本。只在运行实例上调整的参数需要手动 Apply 才会保存。

### CastRange.prefab

根物体的 `LifeCastRange` 控制贴地圆环网格。可调参数：

| 参数 | 用途 |
| --- | --- |
| Range Material | 圈的材质，可换成兼容网格 UV 的自定义材质 |
| Rim Half Width | 贴地圈的半宽，完整宽度为该值的两倍 |
| Flame Height | 竖向光焰网格的最大高度 |
| Ground Offset | 圈相对 Terrain 表面的高度偏移 |
| Opening Duration | 沿圆周显现所需秒数 |
| Fade Speed | 淡入淡出速度 |
| Preview / Preview Flames | 编辑状态下预览圈和光焰 |
| Preview Radius | 仅编辑预览半径；正式玩法仍是 8 米 |

`CastRange.mat` 现在有七组中文外观参数，悬停可看英文属性名：

| 分组 | 可调内容 |
| --- | --- |
| 颜色与整体强度 | 主体、焰心、尖端颜色，尖端渐变、整体亮度与不透明度 |
| 火焰形状 | 数量、宽度、高度比例、高低宽窄随机程度、排列错落、收尖、尖端柔边、向上衰减、侧边柔软度 |
| 焰心与光晕 | 焰心宽度和亮度、主体亮度、外围光晕亮度和扩散、单簇火焰不透明度 |
| 摆动与流动 | 总速度、摆动幅度和速度、弯曲次数、整体倾斜、高度起伏、明暗波动幅度和速度 |
| 扰动与破碎 | 边缘扭曲、内部溶解、程序扰动细密程度和速度、可选灰度贴图及其 Tiling / Offset |
| 底部连续光幕 | 强度、高度、波浪幅度、运动速度；强度为 0 可关闭 |
| 贴地圆环 | 亮度、不透明度、亮线宽度、柔光宽度；不透明度为 0 可关闭 |

想把原来的白亮火焰改成细而轻的风焰，可先尝试：焰心亮度 `0.2`、光晕亮度 `0.04`、底部光幕强度 `0`、火焰宽度 `0.45`、向上收尖 `0.92`、边缘扰动 `0.22`。这只是调参起点，没有覆盖正式材质的原有设置。

扰动默认用程序生成，不需要额外贴图。使用自己的噪声贴图时，提高“使用纹理的比例”；纹理推荐设置 Repeat，且最好能平铺，以免圆周接缝明显。总运动速度为 0 时所有动画冻结；扰动、破碎均为 0 时噪声参数不影响画面。

Visibility、Maintained Weather、Opening Sweep 由游戏状态控制，已从材质外观面板隐藏。编辑预览始终展示完成展开的状态，完整展开动画在 Play 中查看。材质的“火焰高度比例”改变网格内轮廓，Prefab 的 Flame Height 决定网格的实际最大高度。

贴地通过 Terrain 高度采样实现；网格地面和屋顶没有额外贴合采样。根物体位置与范围由游戏控制，不要靠缩放根物体来改变玩法半径。

### Wind.prefab

层级如下，每个子项都是普通 Particle System：

```
Wind
  Weak
    Wind Streaks
    Leaves
  Strong
    Wind Streaks
    Leaves
```

用菜单 `Weather House > Weather Effects > 打开起风预制体并预览` 打开，或双击 `Wind.prefab` 后选中最上层的 Wind 根物体。编辑器会自动预热并持续播放弱风，镜头也会对准风效。不需要进入 Play；选中 Weak / Strong 空父物体不会显示控制面板。

根物体 Inspector 提供“预览弱风 / 预览强风 / 停止”和“调整弱风 / 调整强风”。一次只播放一组。集中面板直接修改原生粒子系统参数，支持撤销：

- 整体风向，风丝和碎叶各自的数量、尺寸、寿命、颜色、前进及上升速度。
- 左右／上下扰动、弯曲细密程度、扰动流动速度。
- 风丝拖尾宽度和持续比例。
- 共用风丝材质的不透明度、亮度、亮线核心宽度、边缘柔软度；这些材质参数同时影响强弱风。

更多原生模块仍可在 Weak、Strong 下的 Wind Streaks / Leaves 子物体调整：

- 风丝粗细：Main → Start Size、Trails → Width over Trail。
- 风丝长度：Trails → Lifetime，以及 Velocity over Lifetime。
- 风丝弯曲：Noise 的 Strength、Frequency、Scroll Speed。
- 风丝密度：Emission → Rate over Time。
- 风丝颜色：Main → Start Color 和 `WindRibbon.mat` 的 Tint。
- 碎叶：Leaves 的数量、颜色、尺寸、旋转和 Renderer → Mesh / Material。
- 风向：旋转 Weak、Strong，或修改各系统的 Velocity over Lifetime；两组应保持一致的总体方向。

保持 Play On Awake 关闭。两个组都保持激活，控制器负责选择播放哪个组。可以在组内增加粒子子物体，下次实例化时会自动纳入控制。运行时只选择强弱组、播放／清除以及裁掉超出施法范围的粒子，不重写颜色、材质、速度、发射曲线等美术参数。强弱切换会清除旧组，收起天气会清除所有粒子与拖尾。

`WindLeaf.asset` 是本项目生成的小叶片网格，可以替换成自己的模型。风丝使用原生粒子 Trails 加柔边透明材质，不需要 VFX Graph。

## 参考资料与实现边界

用户选择的是白色弯曲风丝与少量碎叶，本次没有扩展全场景植被联动。

[《羊蹄山之魂》官方辅助功能](https://www.playstation.com/en-ca/games/ghost-of-yotei/accessibility-options/)确认了指引之风、增强风可见度和风向相机选项，但没有公开风丝的完整渲染算法。

[Sucker Punch 首席特效师的《对马岛之魂》技术介绍](https://blog.playstation.com/2021/01/12/how-stunning-visual-effects-bring-ghost-of-tsushima-to-life/)说明了共享风向与强度、粒子扰动、地形前向采样，以及两层程序化阵风驱动植被等方法。这是前作公开技术，不能当作《羊蹄山之魂》的完整实现证据。

本项目用少量半透明粒子拖尾与叶片实现所选视觉方向，不宣称复刻原作算法，也不包含任务导航或完整空气动力学模拟。

缺失资源可用菜单 `Weather House > Weather Effects > Create missing prefabs` 补建。已有 Prefab 不会被该菜单覆盖；日常调效果直接编辑 Prefab，无需运行生成菜单。

旧菜单“选中起风预制体”现在也直接打开预览。“选中施法圈材质”仍在 Project 窗口定位材质。

## 当前存档的天气测试解锁

菜单 `Weather House > Weather Effects > 解锁当前存档天气魔法（测试）` 会先备份已有的 `life-progress.json`，再解锁并学会雨、晴、日照、风、弱、强。备份放在同目录，文件名带 `before-weather-test` 和时间。

测试解锁记录在该存档的 `weatherMagicTestUnlocked` 字段中，不改变照片、任务、动物关系或世界状态，也不改变其他存档和新游戏的正常解锁条件。编辑状态下操作后，下次进入 Play 即会读取。当前存档已经执行过，无需重复点击。

## 验证记录（2026-10-30）

- 相关运行时代码和资源生成脚本编译通过；运行时代码编译只有既有水面渲染 API 的弃用警告。
- 独立 Unity 2022.3 / URP 14 工程的 17 项 Play 检查通过，覆盖生产 `LifeLocalWeather`、`WeatherRegion`、圈与风效控制器的启停、强弱切换、天气并存、跟随、粒子边界和 Shader 编译。测试工程的世界物件与音频是空替身，因此这次不据此声称验证了任务、干湿变化或音效。
- Prefab 编辑检查通过，实际修改临时预览实例的圈宽和光焰高度并检查网格响应，确认序列化引用与粒子材质有效。可在非 Play 模式运行 `Weather House > Weather Effects > Check editable prefabs` 重查，不保存临时修改。
- 弱风、强风已在独立平地场景渲染检查。尚未完成正式地图中的人工试玩和观感验收。
- 本地验证记录与截图在 `Temp/WeatherPrefabReview/`，最终 Play 日志为 `unity2.log`，结果为 `play-checks.txt`，编辑检查日志为 `authoring.log`。交付资源与被验证的 Prefab、材质、网格文件逐字节一致。
- 后续火焰材质扩展增加了七组、43 项外观参数。独立 Unity GPU 渲染的 43 项检查通过，覆盖旧默认画面的像素对比、形状／颜色／光晕／光幕／自定义噪声贴图／动画速度的响应，以及施法显隐和展开控制。中文材质面板脚本编译通过；报告在 `Temp/WeatherPrefabReview/ShaderControls/checks.txt`，最终日志为该目录的 `final.log`。参数示例图使用展开的平面网格验证 Shader，不能当作正式场景观感验收。
- 风效编辑预览和存档解锁的 16 项检查通过：复现原 Prefab 无粒子、根面板自动预览、强弱切换与停止、预览不改写 Prefab、圈预览重载恢复、六种手势的存档往返、未知手势仍锁定、其他进度保留、新存档仍按正常规则解锁。报告为 `Temp/WeatherPrefabReview/wind-authoring-checks.txt`。
- 实际当前存档与备份逐字段比较，仅 `weatherMagicTestUnlocked` 和 `learnedGestures` 改变。
- 主工程 Prefab Mode 中已看到持续播放的风丝，并打开了中文根面板。最后核查时，主工程仍在报告动物模块的 `AnimalStoryState` / `AnimalStoryReflection` 编译错误，尚未完成主场景 Play 验证；本次没有修改该模块。最新风效脚本在独立工程中已编译并通过上述 16 项检查，日志为 `wind-authoring-final2.log`。
