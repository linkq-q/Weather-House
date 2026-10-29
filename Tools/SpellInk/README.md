# 施法笔迹与 G 图标

游戏中的入口是左下角 G 魔杖图标或键盘 G，再次点击／按 G 退出，Esc 收起。白天采用白金光晕，WatersideMood 夜间采用青绿流光。主笔迹半径是已确认预览参数的 3 倍；星尘仍保持细小。现有项目没有连续运行的 TOD，这次接入现有日夜状态，效果组件保留 0–1 的 Daylight 参数。

只给世界施法的 LifeStrokeGraphic 启用新效果。手账示范和练习沿用纸上笔迹，识别、解锁、施法结果及 2.2 秒收尾时限沿用现有逻辑。圆头线段先用 Max 合成，再作为预乘 Alpha 图层绘制，避免折角重复叠亮。成功时沿线回响，失败保留暖橙色反馈。

G 图标依据现有 Camera、MagicBook、DictionaryBook、PhotoTasksBook 的实心白色剪影风格制作，显示为 44×44，按钮热区为 72×86，沿用 HUD 浅米色。运行 `python Tools/SpellInk/build_icon.py` 可生成原生透明 PNG。

## 验证

2026-10-29 已通过 C# 编译和独立 Unity 2022.3.62f3c1 编辑模式检查，19 项断言覆盖实际 GPU 纹理、最终 UI 合成像素、3 倍粗度、直角／锐角／回折、消散、手账旧网格、G 图标资源、按钮进入／退出、书本模态阻止施法、两个 shader 支持。首次 UI 合成检查发现缺少 CanvasRenderer，修复后最终截图和像素检查通过；编辑模式资源释放错误也已修正。

在 Unity 编辑器顶部菜单运行 `Weather House > Life > Check spell ink` 可复核，必须处于非 Play 状态。它创建并关闭临时预览场景，不保存游戏场景。输出位于 `SpellInkReview/20261029/`。

主编辑器被其他工作占用时，可运行 `python Tools/SpellInk/prepare_review.py`，它用本机 Unity 编译器检查当前源文件，并生成任务专用的小型验证工程。然后用 Unity 对该工程执行 `WeatherHouse.Life.Editor.LifeSpellInkChecks.RunBatch`；批处理输出位于这个小型工程内部的同名目录。准备脚本沿用本项目 Unity 安装位置及 Bee 响应文件路径，版本迁移时需更新。

本轮使用独立工程完成检查。主编辑器未切换场景或 Play 状态，没有保存主场景、修改正常游戏存档或进行推送。主场景 Play、真人鼠标手感、实际场景亮度与性能验收尚未完成。编译只有项目既有 PainterlyWaterReflection 过时 API 警告。
