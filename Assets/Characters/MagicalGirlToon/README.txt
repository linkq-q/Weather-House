iPad 重绘角色 · 天气屋卡渲预览

预览入口
Assets/MagicalGirlToonPreview/WeatherHouse_iPad_Toon.unity
首次创建菜单：Weather House > iPad Character > Build Toon Scene Copy。
菜单不会覆盖已经存在的预览场景，也不会关闭有未保存修改的场景。

进入 Play 后
沿用天气屋原有 WASD、Shift、空格、右键拖动、滚轮缩放和 Tab 俯视操作。
F6：卡渲加描边；F7：仅卡渲；F8：原贴图底色。
这份 OBJ 没有骨骼和动画，因此角色会随控制器移动，但身体姿态不会播放走跑动画。

本次直接复用
ZenlessZoneZeroCombatDemo/Assets/Rendering/Shaders/CharacterToon.shader 及六个配套 HLSL。
ZenlessZoneZeroCombatDemo/Assets/Rendering/CharacterOutlineFeature.cs。
上述渲染代码逐文件原样复制；本项目只设置材质参数和 Renderer Feature 接线。
描边只绘制第 8 层，并依赖预览相机选择新 ToonRenderer。
新 Renderer 追加到三档 URP 配置，原有默认 Renderer 的索引及内容保留。
使用 iPad 回传的 12 张颜色贴图，保留原 UV 和几何形状。

按要求暂不套用
原角色专用的脸部 SDF/解析阴影、鼻子 UV 校正、刘海投影系统、头发方向高光与控制图。
专用身体顶点通道、描边法线烘焙、原角色的打包材质通道和皮肤散射。
Procreate 的 roughness/metallic 独立贴图没有直接接到 ZZZ 的打包通道，避免误解释其数据。
保留场景原有主光、天气、地形及全局后处理。角色另加两盏跟随玩家的柔和暖色补光，遮罩仅包含第 8 层。
亮度调整保存在预览场景和这套角色材质中：主光响应 0.78，环境补光 0.42，局部补光响应 0.65，上限 0.60。
正面补光强度 3.5，背面补光强度 1.6；降低亮面亮度并保留深色衣物层次。
这些参数已在正面、背面及游戏相机中检查；角色专用补光属于这份外观预览的设置。

验证记录
项目根目录 ToonPreviewReview：编译结果、源文件哈希、材质及 UV 接线检查、Unity 渲染对比图。
validation.txt 记录 Unity 材质、UV、几何及 Shader 检查，runtime-check.txt 记录 Play 模式下的显示模式与相机切换检查。
PNG 为 Unity 相机实际渲染，BeforeWarm/WarmStrong/WarmReduced 保留之前的灯光对照；根目录 PNG 为最近一次检查结果。
