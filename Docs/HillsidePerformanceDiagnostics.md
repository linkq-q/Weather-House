# 山林场景性能排查

入口：`Weather House > Hillside > Performance`。

- `Inspect`：只读导出当前场景、画质、显卡、Game 视图尺寸和动物网格库存。
- `Self Check`：检查中位数、P95、空样本和不可用指标的处理。
- `Start Comparison`：在已保存的 `WeatherHouse_Hillside` 场景中开始自动 Play 对照。
- `Stop`：提前停止，保留已有数据并恢复临时设置。
- `Restore Pending Session`：中断后只还原临时全局设置并退出 Play，保留场景的未保存改动。

测试约一分钟。请让 Unity 的 Game 视图保持前台，不操作人物或镜头。工具不会打开、重建或保存场景；普通开始入口会拒绝未保存的场景。文件命令 `start_current` 可以在仅打开山林场景时测量当前未保存版本，使用 Unity 正常的 Play 进入/退出恢复过程，保留进入前的未保存状态。

先记录 10 秒冷启动，再在同一静止视角依次测量全开、隐藏草、关闭动物、关闭水面反射、三项全关、重复全开。静止阶段各预热 2 秒、采样 4 秒。最后从同一起点分别跑动 6 秒，比较有草与无草；这两段单独分析，不能与静止阶段直接算耗时差。

草通过 `manualCaptureOnly` 暂停生成与绘制，保留缓存；动物按单个根对象临时关闭；水面只暂停平面反射。正常完成、手动停止和脚本重载都会尝试恢复组件开关、后台运行、垂直同步、目标帧率和 Play 起始场景。测试完成后退出 Play。

结果在项目根目录 `HillsidePerformanceReview/`：

- `latest-run.txt` 指向最近一次带时间戳的记录目录。
- `summary.json` 提供各阶段中位数、P95、最大值、采样覆盖率和前台比例。
- 时间戳目录的 `frames.csv` 保存逐帧时间、主线程时间、GC 分配、绘制调用、三角形、草缓存和动画状态。
- `state.json` 是最近一次只读库存与编辑器状态，`before-state.json` 是本次排查手动保留的运行前快照。

也可以向 `HillsidePerformanceReview/request.txt` 写入单个命令 `inspect`、`selfcheck`、`start`、`start_current`、`stop` 或 `restore`，与菜单使用同一路径。`start_gait` 只运行两段跑步；`benchmark_cell` 在编辑态单独计时当前人物所在草格的三次生成，结果不写回草缓存。每次运行目录的 `entry-state.json` 保留开始时的编辑器状态。

修复后的等价检查命令是 `grass_checks`、`cape_checks`、`runtime_checks`，分别输出 `grass-repair-checks.txt`、`cape-repair-checks.txt`、`runtime-repair-checks.txt`。这些检查只允许编辑态运行，用临时对象比较旧算法和优化算法，不保存当前场景。草检查覆盖位置/变体/矩阵、切片预算和转镜头缓存；披风检查覆盖接触计算；运行时检查覆盖动物动画图和零扰动水波。

补充检查命令 `grass_lod_checks`、`tree_lod_checks`、`grass_batching_checks` 分别检查远草网格与资源生命周期、树 LOD 阈值与恢复、跨格实例提交的矩阵完整性。输出对应 `grass-lod-checks.txt`、`tree-lod-checks.txt`、`grass-batching-checks.txt`。

这些是 Unity Editor Play 的数据，包含编辑器和等待开销，不等同于独立构建性能。冷阶段的计时起点是进入 Play 回调，但首个 `unscaledDeltaTime` 仍可能包含进入 Play 的工作，不能直接解释为草生成开销。工具不提供 GPU 时间；不可用计数器以 `-1` 表示。三角形统计包含渲染通道、阴影和反射等重复提交，不能当成模型的唯一面数。行为状态随时间变化，重复基线用来检查漂移；关闭某组没有稳定提速，也不能证明该组没有成本。`applicationFocused` 与 `focusedFraction` 用于识别前台条件。

编辑器轮询按 `Time.frameCount` 去重，`frameGap` 与 `sampleCoverage` 显示漏采情况。跑步判定同时查看原生 Animator 状态、归一化时间、接地状态和大腿旋转；脚骨局部平移固定不能单独证明动画停止。
