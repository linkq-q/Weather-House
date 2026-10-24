# 初始动物落点只读审计

完整源码和外编译 DLL 均在本目录，主项目 Assets 未新增脚本。由主地图 owner 在 **Edit 模式、仅一个普通场景加载**时调用；此目录没有自动运行入口。

```csharp
var audit = System.Reflection.Assembly.LoadFile(
    @"C:\unity project\Weather House\JourneyReview\Revision_20261024\InitialPlacement\WildlifeInitialPlacementAudit.dll");
var directory = (string)audit.GetType("WeatherHouse.Review.WildlifeInitialPlacementAudit")
    .GetMethod("Run").Invoke(null, null);
UnityEngine.Debug.Log(directory);
```

`Run()` 不调用动物/种群初始化，不进入 Play，不创建临时场景 GameObject，不移动或保存场景。它使用已落地的 `WildlifeBodyEnvelope.Measure` 和真实 `Physics.OverlapBox`；临时烘焙 Mesh 由原 Measure 方法销毁。每次输出到 `Reports/UTC时间戳/`，包含 `report.json`、`animals.csv`、`contacts.csv`、`candidates.csv`、`summary.txt`，并报告场景 dirty 前后和所有 Transform 是否保持不变。

- 仅收集启用的地面 WildlifeAgent 与启用 JourneyHerd 的活动成员，优先按羊群成员收集，再按 Transform 去重。禁用的旧羊控制器不会重复报告。
- `measuredCenter/measuredHalf` 是 Edit 当前身体；`bodyCenter/bodyHalf` 是预测运行身体。羊保持原 yaw，并应用 `member.bodyLength`。Legacy 按真实首次随机数预测缩放，并按 `TryLandingPosition` 的世界边界、坡度、预测半径和水域条件预测启动时投到 Terrain 的位置；报告 `position` 与 `predictedStartPosition`。
- Legacy 只有在 visualRoot 所属关系、未捕获基准缩放、renderer 与 skin bones 全部归属等条件明确时才保证预测。不能安全推断的实例标 `runtimePredictionReliable=false`；该实例以及受未知邻居尺寸影响的候选不发布安全保证。
- `contacts.csv` 分 `currentEdit` 和 `predictedRuntime`。全层查询数是 `queriedStaticOverlapCount`；真正落在该动物运行 obstacleMask 中的阻挡数是 `staticBlockerCount`，避免把非运行障碍误认为必须改场景的缺陷。
- Terrain、自身、玩家从静态障碍统计中排除。桥踏面只有被该动物自己的 JourneyNavigation 选为注册支撑时才豁免。Legacy 没有踏面豁免；未注册的脚下 Collider 单列为 `unregistered_ground_contact`，保留阻挡状态供人工判断，石头不会自动被当成地面。
- 候选保留 yaw/scale，仅报告平移。搜索半径 3 m，0.25 m 环间距，按实际三维偏移排序保留最多三个已采样候选；所有层身体查询、0.25 m 足迹网格干地/坡度/高差、其它预测动物的保守身体间距均需满足。坐标、偏移、到 Edit 原点高差、坡度、足迹高度范围、原触发 Collider 路径均输出。
- 候选是独立的目的位置建议，不保证从已经穿模的原点存在连续逃出路径，也不能把多只动物各自的第一个候选同时应用而不复查。owner 逐个核查并仅调整必要落点后，应重新运行报告。

外编译命令 `./Build.ps1` 默认读取主项目已刷新后的 `Library/ScriptAssemblies/Assembly-CSharp.dll`。构建只在本目录生成 DLL、rsp、log，不导入 Assets。

`WildlifeInitialPlacementSelfChecks.Run()` 是单独的临时工程自检入口，严格要求 `.wildlife-journey-validation` 标记。它会创建并清理自己的测试场景，因此 **不能用于主项目**。自检结果见 `self-checks.txt` 与 `self-check-unity.log`；主场景审计只能调用前述 `WildlifeInitialPlacementAudit.Run()`。

已在 Unity 2022.3.62f3c1 独立工程实测通过并以退出码 0 结束：审计前后所有 Transform 相同，场景 dirty 保持 `False`；带根旋转、父 pivot 偏移/旋转和不均匀 visual scale 的预测，与实际 `Agent.Initialize` 身体包围盒及落地位置一致。实测随机尺度 `1.064978`，Edit Y `5.000`，预测与实际启动 Y 均为 `3.000`。`VerifiedSelfCheck/` 保存完整结果，JSON/CSV 已在 Unity 外实际解析，含动物嵌套记录。候选筛选与桥支撑规则另经独立代码复审；主图结果仍由 owner 执行本工具获取。

首次自检发现动态加载 DLL 的嵌套自定义数组被 Unity JsonUtility 静默省略，保留了 `self-checks-json-red.txt` / `self-check-unity-json-red.log`。当前使用仅针对报告公开字段的 JSON 写出，不依赖 Unity 类型缓存，也没有引入新的包。
