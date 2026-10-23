# 山林地图的动物群体行为

2026-10-23。目标是让约 50–80 只骨骼动物形成可读的生活片段，同时保持区域、水陆边界、动画与运行开销可控。

## 调研结论与来源

- Reynolds 将行为拆为动作选择、转向和身体运动三个层次；分离、凝聚和对齐可以组合出群聚。这里用小状态机选择活动，用邻居分离和种群中心选择移动目标，骨骼动画只表现动作，避免动画直接拖动角色坐标。[Steering Behaviors for Autonomous Characters](https://www.red3d.com/cwr/steer/gdc99/)
- Unity 官方 Boids 示例把动物按空间格归并后计算邻域，避免每只动物扫描全群。这里只借鉴空间索引；当前工程没有 Entities，几十只动物不需要引入 ECS、Jobs 或额外包。[Unity 官方 BoidSystem 源码](https://github.com/Unity-Technologies/EntityComponentSystemSamples/blob/master/EntitiesSamples/Assets/Boids/Scripts/BoidSystem.cs)
- Unity 的 `NavMesh.SamplePosition` 仅找附近网格点，不检查障碍，所以“落点合法”不能证明整条路线合法。当前地面动物只在小栖地内游走，采用整段地形、水域采样和碰撞扫掠；被挡住就另选目标。需要绕山、过桥等长距离通行时再引入完整路径查询。[Unity 2022.3 SamplePosition](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/AI.NavMesh.SamplePosition.html)
- 如后续需要导航，项目已有内建 AI 模块，可使用 `NavMeshBuilder.BuildNavMeshData` 从地形、障碍和水域排除体构建导航数据。现在没有为未使用的导航功能新增包。[Unity 2022.3 BuildNavMeshData](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/AI.NavMeshBuilder.BuildNavMeshData.html)

这些是源码和文档依据；以下状态、阈值与活动节奏是本项目的设计取舍，不是生态学模拟结论。

## 当前实现

`WildlifePopulation` 管理固定种子、动物登记、种群中心、空间格、水域和惊吓事件。`WildlifeAgent` 保留个体的家域、活动时钟、胆量、社交倾向及独立随机流。地图总种子、稳定 `spawnId` 和物种共同决定体型与节奏；重建会得到相同的个体差异，也不会污染 Unity 全局随机数。

状态为 Idle、Forage、Wander、Greet、Alert、Flee、ReturnHome、Takeoff、Fly、Landing、Swim。不同物种使用不同参数与实际可用的动作。地面动物在停留、进食和散步间交替，离群后回到家域。鱼持续在指定池塘有效水深内活动。鸟跨区域飞行后寻找安全旱地落下，停留、唱歌再起飞，受整个地图边界约束。

群体每 0.2 秒更新一次感知，移动每帧更新。每个个体最多访问 48 个邻域候选、接收 12 个邻居；同群中心统一累计一次。密集群落也有明确的邻域访问上限。这个上限证明的是计算规模，不能代替实际帧率测试。

惊吓来自玩家快速靠近、极近距离接触，或具有惊扰属性的动物移动到附近。事件保留原始位置、半径、强度、编号与到期时间，只派发一次；接收者不再广播。个体先警觉，再短距离逃离，随后回归家域。重复编号、过期事件、冷却期事件被忽略，避免无限传染和持续逃跑。

友好招呼需要双方空闲、物种兼容、距离合适且路径安全。双方互相占用同一个短暂互动，然后进入冷却，避免一只动物同时回应多只。缺少真实招呼片段时使用待机与朝向，不将攻击动作当成友好动作。

骨骼使用 Playables 混合已导入的真实动画，关闭 root motion。缺失动作使用语义近邻：进食回退待机、奔跑回退行走、招呼回退待机。步行加速只是缺少奔跑资源时的保底表现，不等同于已经制作了独立跑步动画。

知更鸟的起落素材还包含 `body` 根骨平移。鸟的世界高度由状态控制，所以在动画求值后将这根骨的局部平移恢复为绑定姿势，保留翅膀和腿的旋转，防止素材位移与外层升降叠加。起落时长取实际动画长度。该处理会去除该根骨的附带平移摆动，须通过镜头检查是否需要补充轻微飞行起伏。

Unity 官方说明 Generic 动画在未指定 Avatar 时可由 Animator 内部创建 Avatar；这不等于当前资源已经绑定成功。`RunRigBindingChecks(population)` 会在预览场景克隆每种动物的模型，用实际 Playables 对各片段采样，并要求移动片段改变骨骼变换。[Unity Avatar 文档](https://docs.unity.com/en-us/engine/6000.7/script-reference/unityengine/avatar)

## 水陆限制和已知边界

水域为 `HabitatVolume { id, center, radii }`，`center.y` 是水面高度，`radii` 为 x/z 方向椭圆半径。地面动物避开扩大了身体半径的水域；鱼限制在缩小后的椭圆内，并同时检查水面和真实地形底部。跨越窄水沟的安全终点也必须通过整段检查。鸟按地表或水面上方的飞行高度投影。

当前移动是局部避障，不保证绕过复杂障碍找到另一侧；遇到无法通过的路线会停下并重选。没有繁殖、捕食伤害、持久存档或全生态系统模拟。惊吓与友好关系是可调的游戏规则。地图变化后需要更新水域、家域与地形引用。

## 验证入口

编辑器菜单 `Weather House/Hillside/Check Wildlife Logic`，或 `WeatherHouse.Hillside.Editor.WildlifeChecks.RunLogicChecks()`。检查在独立临时对象上运行，最后清理对象并恢复全局随机状态；不打开、保存或替换场景。

自检覆盖固定种子、个体体型、重复初始化、水陆空限制、跨池塘路径、浅水拒绝、惊吓去重与恢复、成对招呼与冷却、所有状态的动画保底、鸟的完整起落循环、80 只密集动物的邻域访问上限。运行场景可调用 `ValidateConfiguration()` 检查动画和出生点，使用 `EmitStartle()`、`TryGreet()` 和 `Simulate()` 验证状态。

还需实际 Play 检查骨骼绑定、足底、转向、群体密度、摄像机可读性和帧率；逻辑自检不证明这些视觉结果。
