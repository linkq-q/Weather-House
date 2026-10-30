# 天气屋音效接入

运行时配置位于 `Assets/Game/Life/Audio/Resources/LifeAudio/Catalog.asset`。19 个语义音效条目引用 21 个环境/动作 WAV，另有 5 个物种原创短音节，共 26 个文件。原始录音、许可和裁剪记录保留在 `Source`；制作者工作台位于 `VoiceDesigner/index.html`。

`LifeAudio` 在 WatersideV2 的 GameplaySession 上随 Play 挂载。播放实例固定为 12 个，不按动物、植物、水面数量增加。它沿用现有相机的唯一 AudioListener。雨、风、描画、编垫、室外底声、岸浪各占一个固定循环；交谈或手账打开时，环境层降低音量。

2026-10-29：玩家足音使用 2D 声源。动物和世界操作音仍保留空间声像，播放代理按相机相对玩家的偏移移动，让距离衰减以玩家耳部为准，避免 18 米高的俯视镜头把 9 米传播范围的音效全部消音。岸浪按玩家到海岸的距离渐入，采用 2D 环境层；Waterside 的内陆池塘和河道不触发海浪。新录音为 jasinski 的 Alki Beach 波浪，CC0，原件、下载链接和处理参数见 `Source/shore-20261029-source.json`。五种原创动物声线保持原文件。

## 配置和触发

2026-10-30：草地足音改用 Fission9 的草叶实录，来源与裁剪参数见 `Source/grass-20261030-source.json`。五种动物语音在制作端把音节及间隔缩短一半，音高提高 4–6 个半音；狗与羊的明亮度也已提高。`VoiceDesigner` 默认值与游戏 WAV 同步。桌面 `天气屋/音效试听_20261030/打开试听.html` 提供 22 类共 70 个对照片段，其余候选待试听后选择。

2026-10-30 试听选择接入：以后续用户导出中的 `picked: true` 为准。草地改为“轻叶片摩擦”，土路用“柔软土路”，石面用“短石面”，每种地面只加载一条；雨、风、海浪、布料、饮水各取一条，水花保留两条短变化。翻书、快门、草叶采用所选版本。松鼠恢复用户选中的旧声线，其余四种动物使用“更明亮备选”。书写、界面、完成提示、户外底声及待重选的施法声音保持原配置。所选原片、来源与取舍见 `Source/approved-20261030-selection.json`。最终音量和循环接缝仍待游戏中试听。

魔法试听选择已接入：07「梦幻施法」用于成功，03「星光上扬」和 04「另一组星光」用于世界描画，06「咒语点亮」用于失败。03、04 每次新笔画轮换一条，静止渐停、继续同一笔沿用原条，松手或取消立即停；手账练习仍用纸笔声。保留固定 12 个音源。所选原片、作者、许可与 SHA-256 见 `Source/approved-magic-20261030.json`；音量与循环接缝处理见 `Source/derived_manifest.json`。候选试听页仍保留全部 8 条。

本轮隔离 Unity Play 的 21 项魔法检查、18 项资源与绑定检查通过；所有 26 条音频解码成功，无运行时报错。记录和复验入口见 `InteractionReview/20261030/MagicAudio/接入与验证.md`；最终游戏内响度与接缝仍待人耳试听。

| 内容 | 真实入口 |
| --- | --- |
| 草/土/石足音，各一条 | ForestSprite 实际位移、接地状态；Terrain alphamap 主层或碰撞网格对应材质；浅水时选两条水花变化 |
| 采草、拿草、收垫、喂草 | World 完成对应状态操作时的 Fiber 事件；喂草只在动画消费点 |
| 编垫 | World.IsCrafting 决定循环，移动中断立刻停；完成发一次 Contact |
| 放下、铺垫、制作收束 | Contact 事件 |
| 浸湿、倒非空碗 | Water 事件，空碗不响 |
| 动物喝水 | MouthTouchesWater 成功且首次 consumed 时 Drink |
| 世界施法 | LifeGestureInput.AddPoint 移动时 Casting，SubmitStroke 失败时 CastFailed；LifeLocalWeather.Cast 成功出口 Cast，单次触发；练习不响世界施法 |
| 雨、风与自然对照 | WeatherRegion 真实激活、强度、玩家是否在区域内；同通道合并为一条循环 |
| 动物说话 | 交谈打开/当前话语改变、近处有可见闲语；按实际物种读取原创声线 |
| 翻书 | LifeJournal3D 实际 Open/Close/Resume/Turn 与开场翻叶；不在重绘时发声 |
| 描画 | 新采样点发生移动才续播，松键/取消/失焦停止 |
| 相机与进度 | 实际 Recorder.IsRecording 上升沿快门；成功保存、提交、首次进度由相应操作出口触发 |
| 室外和岸边 | 一个室外底声；一个岸浪源依据 WorldWaterSurface 的实际空间边界采样距离渐变 |

地面绑定见 `surface-bindings.json`，证据为 `surface-audit.tsv`。当前 Terrain 是 A04RouteAndEntryGrassV1，Meadow、Path、Rock_3 分别映射草、土、石；Sand_1 复用软土地类。石桥、礁石、遗迹石地和 A03/A06 石阶按实测材质引用绑定。未纳入当前三类足音的木面保持静音。运行时不按名字猜地面，也没有全地图默认土音。

所有正常操作使用 `LifeAudioCue`，资源路径和物种绑定集中在 catalog。未知 cue/地面/物种不猜测素材。音频组件禁用或销毁时停止固定源，不排队补播短声。

## 重建与验证

- `node Tools/LifeAudio/VoiceDesigner/voice-engine.test.cjs` 检查原创样本、PCM、参数边界和五个已选预设。松鼠、狐、鹿明亮度为 100，狗 70、羊 65。
- `node Tools/LifeAudio/install-clips.cjs` 从派生成品和当前原创预设重建本模块的 WAV 与 catalog，保留稳定 GUID。文件和材质引用取自集中清单；不会改母版或场景。
- `node Tools/LifeAudio/verify-imports.cjs [报告路径]` 检查 26 个成品的字节、魔法用途与变体顺序、无多余运行时 WAV 和带对象类型的 YAML 引用；Unity 导入后是否解析为有效对象仍由 `LifeAudioChecks.Run()` 检查。
- `python Tools/LifeAudio/Source/derive_candidates.py paper_move camera_shutter` 只重导出指定两条并更新清单，保留其他成品与来源记录。
- `WeatherHouse.Life.Editor.LifeAudioSurfaceAudit.Inspect()` 只读导出当前 Terrain 与 Collider 材质。
- `LifeAudioChecks.Run()` 验证资源、26 个唯一文件、施法两条、独立失败音、三地面各一条、水花两条及真实 TerrainData alphamap 选择逻辑。夹具仅创建内存数据。
- `LifeAudioChecks.Runtime()` 检查 12 个实例、唯一监听者和重复事件限流。
- `LifeAudioPlayChecks.Run(life, completed)` 由主验收协程在隔离存档的 Play 内执行，使用真实地面和控制器验证足音、天气、编垫、翻书和停止路径。结果写入 `LifeGameplayReview/20261026/play/audio-play-result.txt`。
- `LifeAudioAudibilityChecks.Run(life, completed)` 从实际开书、四叶翻页、手动翻页和 `GameplaySession.TakePhoto` 入口，测量声源与监听输出的最大块 RMS/峰值；结果写入 `LifeGameplayReview/20261026/play/audio-audibility-result.txt`。测试临时静音其他声源后还原，数字输出不代替系统音量、输出设备和人耳验收。

自动检查证明导入、触发和停止条件；最终耳机/扬声器上的明亮度、响度平衡、循环接缝和岸边声像仍需人工试听。此模块没有新增背景音乐。

## 本轮实际证据

2026-10-26 21:00（UTC+8），Unity `audio-checks` 全部通过：26 个可播放文件、4 个 TerrainLayer 和 8 个 Material 引用有效，草/土/石各两条候选与 alphamap 选择通过。记录位于 `LifeGameplayReview/20261026/audio-checks.txt`。材质引用按真正的 Material 对象选择，不再误取同文件内的 URP 版本子资产。

2026-10-26 21:04:19（UTC+8），实际 `audio-only` 完整通过，记录位于 `LifeGameplayReview/20261026/play/audio-play-result.txt`：

- 三类真实地面上的控制器移动均选到两条变体；静止和腾空不重复发声。固定 12 个声源、唯一监听者和同事件限流通过。
- 雨风独立循环，日照停雨保风，停息停止两者；实际采草、编垫中断及完成均通过。手账四张独立薄页完成翻落时共触发两次纸声，静止后不补播。
- 手账练习的移动信号跨过人为 150 ms 阻塞后仍启动笔触，`AudioSource.isPlaying` 为 true；无新移动渐停，结束练习停止。
- 世界描画调用实际 `AddPoint`、`SubmitStroke`、`Cancel` 路径：待画但未移动时静音，移动启动，松笔立即停止且不补播，新一笔恢复，取消清空并停止。
- 失焦停止全部声源；后台操作不能重启声音；恢复焦点不补播短声或旧笔迹，仍有效的天气循环恢复；禁用组件停止全部声音。无运行时错误。

这些证据来自隔离存档、真实场景地面、控制器和 AudioSource 状态。世界描画的模式状态、玩家位置、焦点回调及初始天气由测试夹具控制，未模拟操作系统鼠标或切换系统窗口。两种描画共用 `LifeSession.GestureInputActive`，不依赖启动时缓存动态添加的 UI 组件。人耳听感与完整人工操作验收另行确认。

## 翻书与快门的独立响度修订

根据试玩反馈重新测量，两条旧素材叠加 cue 增益后的整段 RMS 分别为翻书 −55.77 dBFS、快门 −49.64 dBFS。翻书的孤立尾部重击使峰值归一压低了纸张摩擦，明显起音约在 0.59 秒；快门前约 0.10 秒也主要是低电平底噪。两者本来就在二维通道，不受玩家与书本距离衰减。

本次仅裁剪并调整这两条：翻书保留母版 0.275–0.575 秒的摩擦段，cue 增益 0.65；快门保留 0.112–0.320 秒的机关段，cue 增益 0.70。成品有效 RMS 分别为 −29.66、−34.62 dBFS；超过 −40 dBFS 幅值的起音分别为 12.5、2.1 毫秒，均无削波。其余 24 条 WAV 的 SHA-256 完全未变，未调整全局音量。离线记录见 `paper-camera-tuning.json`。

2026-10-26 23:25:19（UTC+8），新一轮 `audio-only` 与 `LifeAudioAudibilityChecks` 全部通过，无运行时错误。真实开书、开场四叶翻落、手动翻页各发出一次 Paper cue；`GameplaySession.TakePhoto` 发出一次快门并保存实际照片。上述四段的 source 与 listener 最大块 RMS 均分别为 −30.03、−30.95、−30.44、−35.33 dBFS；纸声峰值为 −17.42 dBFS，快门为 −15.79 dBFS，均无削波。记录见 `LifeGameplayReview/20261026/play/audio-play-result.txt` 与 `audio-audibility-result.txt`。

测量使用隔离存档与焦点夹具，并临时静音其他声源后恢复；它证明实际产品入口有数字输出，不证明系统输出路由、扬声器音量或人耳听感。普通扬声器与耳机试听仍待人工确认。

## 人耳验收入口（待确认）

制作工具从桌面 `天气屋/音效规划_20261026/动物声工作台.html` 打开；该文件跳转到项目中的同一份离线工具。页面没有浏览器参数存储。刷新先显示狗的 35%，再选松鼠、狐或鹿时应显示 100%；恢复预设同样读取新默认值。上次选中的动物与微调不会跨刷新保存。

本轮 Unity 实际 Play 验证已通过，接着用普通扬声器和耳机各试听一次：

1. 在草地、土路、石桥上走动，再静止和跳跃，确认三组声音有区别、两条变体自然、空中不会连响。沙地当前归软土；木栈道尚无专用素材，保持静音。
2. 走近狗、松鼠、狐狸、羊、鹿交谈，确认听到原创短音节；重点听明亮度 100 的三个声线是否刺耳。渡口羊与其他羊共用同一物种声线。
3. E 采一束草，F 编垫后走开，再完成一次编垫；确认摩擦随实际动作停止，完成时只有一次收束。浸湿与真实饮水应有不同水声。
4. 学会对应手势后，先唤雨再唤风；日照应停雨并保留风，停息应收掉两条循环。描画时移动鼠标才有笔触，松开和取消后应停止。
5. J 打开手账，听多张纸页错时落下与关书，不应过密叠音或在页面静止后补播；C 真正开拍时响快门，录制期间不连响。
6. 从陆地走近岸边，再退远；听岸浪渐变与相机模式切换后的声像，检查底声和雨风是否盖住近处操作。循环接缝、整体响度和声线喜好由人工判断。

Play 检查结果以 `LifeGameplayReview/20261026/play/audio-play-result.txt` 为准；本轮自动回归已通过，人工试听仍待确认。
