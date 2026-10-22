# 天气屋 · 草地与水体试验场

打开 `VegetationLab.unity`，或使用菜单 `Weather House > Vegetation Lab > Open or Create Grass and Water Scene`。

## 修改草地

在非 Play 的编辑模式下，选中 Hierarchy 中的 **01 草地控制器**。运行时的自动调参见下方“运行中试调”。

- `Weights` 三项依次为短草、高草、白色雏菊。按草丛数量归一化，当前场景为 78 / 20 / 2；某项为 0 不生成该种，全部为 0 清空。
- `Total Clumps` 控制总数量，当前场景为 28000，范围 0–40000。比例不是屏幕覆盖面积；高草与雏菊每丛的体积不同。
- `Seed` 决定位置，固定种子可以复现布局。
- `Wind Strength / Speed` 控制风力、速度。
- `Up Normals` 为 0 时使用真实草叶法线，为 1 时统一朝世界上方着色，默认 1。
- 修改后点击 **应用草种与比例 · 重新生成**，再点击 **保存当前测试场景**。

Inspector 下方的 **直接编辑草种形状与颜色** 可切换短草、高草、白色雏菊，就地修改对应草种资产，无需离开控制器。资产仍在 `Generated/ShortGrass.asset`、`TallGrass.asset`、`WhiteDaisy.asset`。修改后先重新生成，再保存当前测试场景；保存按钮同时保存草种资产。

可调高度、叶宽、叶尖宽度、展开半径、弯曲、叶数、高度变化与根尖颜色；雏菊还可调花数、花径、白瓣和花心颜色。

- `Width` 改变整个叶片宽度，短草和高草当前分别为 **0.036 / 0.052 米（3.6 / 5.2 cm）**。
- `Tip Width Ratio`（`tipWidthRatio`）只改变末端尖帽宽度占叶宽的比例，默认 0.11，范围 0–0.4；0 为尖头，接近 0（≤0.001）也按尖头生成。
- `Spread` 控制草叶顶部的展开半径；雏菊中控制花茎根部的分布半径。
- `Root Spread`（`rootSpread`）控制短草、高草根束半径占 `Spread` 的最大比例，默认 0.12，范围 0.025–0.4；越小越聚拢。雏菊不使用此项。

草叶使用完整网格，以直立聚拢为主，包含三档体量变化。雏菊保留花朵、细花茎与窄侧叶，已去掉额外的三角底叶。高度参数分别为 0.30 / 0.64 / 0.50 米；实际单叶尺寸含体量与随机变化。

模型保留真实几何法线，材质 `_UpNormals` 决定着色时是否统一朝上。关掉时，各片叶子朝向不同，明暗较碎；打开时，同片草地受光更一致，形成 PDF 对照图中的柔软草毯效果。A/B 比较请保持相机与光照不变。

## 运行中试调

Play 面板分为四页，参数较多时可滚动：

| 页面 | 可调整内容 |
| --- | --- |
| 草地 | 三种权重、总草丛数、分布种子，以及自然混合、低矮草坪、花草地预设 |
| 草形 | 选择草种，调整高度、宽度、叶尖、展开、弯曲；普通草可调叶数、根束、高度变化，雏菊可调花数与花径 |
| 颜色/风 | 所选草种的根尖 RGB、全场风力与风速，以及朝上法线 0–1 |
| 相机 | 环绕、行走、飞行，移动速度、加速倍率、鼠标灵敏度、视点高度与 FOV |

Play 中拖动比例、数量、形状或位置参数会自动更新，不需要应用按钮。草地按块分帧替换，面板显示进度，松手后自动完成剩余更新；颜色、风与法线直接生效。运行中的 Inspector 调参也会自动更新。法线滑杆 0 使用真实几何法线，1 统一朝世界上方着色，中间值混合两者。

`1` 全景、`2` 草近景、`3` 水岸会回到环绕模式；`4` 行走，`5` 飞行，`Tab` 隐藏面板。按住右键看向，WASD 移动，Shift 加速；松开右键或按 Esc 释放鼠标。环绕模式用滚轮调远近；飞行时 E / Space 上升，Q / Ctrl 下降。操作相机时将鼠标移出参数面板。

行走视点跟随地面高度并停在岸边；飞行可越过池塘，最低高度受地面限制。这是用于看效果的浏览相机，**树木和石头没有碰撞阻挡**。

Play 中使用独立的草种和材质副本，退出后试调恢复。要永久保留结果，先记下参数，退出 Play，再在 Inspector 中重新设置、生成并保存。

## 水体与配色

当前配色依据 `概念图/色彩参考/白天.png`，水纹另对照多视角中的池塘、溪流与远景。`Generated/PainterlyWater.mat` 控制浅岸色、深水色、波纹色、倒影强度、深度过渡和波纹速度。水体对象上的 `PainterlyWaterReflection` 使用最长边 1024 像素的实时平面倒影，排除 Water 层。

草与地面的 `DepthNormals` Pass 继续保留，水深由深度纹理重建的水底世界坐标计算。岸线跟随岸边和相交石头，叠加局部宽窄不同、带断口的近岸笔触与较细的外层笔触。水面叠加大小两层宽短破边印章，每笔尺寸在 0.6–1.4 倍之间变化。印章默认锚定世界坐标，局部起伏的明暗与倒影随观察角度改变。倒影先按大笔触中心取色铺底，再比较大小笔触中心颜色，在差异明显处叠加小笔触。表面小笔触也跟随这个细节需求，让平色水域保留粗底。当前为单次着色中的局部近似，增加一次倒影采样。

- `_Opacity = 0.70`：水层与可见水底合成约 70% / 30%，岸线另做边缘渐隐。
- `_ShoreStrength / _ShoreWidth`：岸线明显程度与深度范围。
- `_SurfaceStrokeStrength / _SurfaceStrokeDensity`：水面笔触强度与数量。
- `_WaterBrushAtlas / _UseWaterBrushAtlas`：四格笔触图集及开关；默认复用材质实验的 AI 破边印章。
- `_SmallStrokeScale / _SmallStrokeWeight`：小笔触相对尺度与权重；默认 3.8 / 0.75，尺度越大，小笔触越小。权重设 0 可单独看粗底，两项同时影响倒影与表面笔触。
- `_BrushViewResponse`：笔触随视角变化的明暗响应；默认 0.85。`_ViewAlignedStrokes=0` 保持世界锚定，避免转相机时整片纹理跟着旋转。
- `_BrushGradientStrength`：每笔沿自身刷动方向的渐变强度。
- `_ShadowPigmentColor / _ShadowPigmentBlend`：暗部颜料色与混合强度，默认较饱和的蓝绿色；保留局部明度，亮部保持浅青/奶油色。
- `_ReflectionBrushStrength / _ReflectionBrushSize / _ReflectionPosterize`：倒影分笔程度、大笔触尺度、色阶强度。大笔触尺度默认 1.35，表面笔触强度默认 0.84。
- `_CausticStrength / _CausticScale / _CausticSpeed / _CausticDepth`：水底焦散的强弱、大小、速度与深度衰减。焦散跟随重建的水底，并受主光阴影影响。
- `_RefractionStrength`：轻微折射；深度检查会拒绝采到水面前方草叶和岸石的偏移样本。

完整透底、折射与焦散在 Game 相机中验收，该相机单独开启 Depth/Opaque Texture。当前全局 Opaque Texture 关闭时，Scene 视图使用普通透明回退；不会读取其他相机留下的颜色纹理。

本轮参考了 [Ameye 的 URP 14 风格水体](https://ameye.dev/notes/stylized-water-shader/)、[Cyanilux 的水体拆解](https://www.cyanilux.com/tutorials/water-shader-breakdown/)、[Unity 官方 BoatAttack](https://github.com/Unity-Technologies/BoatAttack)。油画方向参考 [Hertzmann 多尺寸笔触研究](https://mrl.cs.nyu.edu/publications/painterly98/) 的由粗到细分层，以及 [Alla 的笔触中心着色](https://allarakik.com/stroke-rendering)。[Sagit 的 Unity 6 实验](https://www.sagitsoftware.com/projects/oil-paint-shader/) 用于对照方向分析、厚涂和时域稳定的工作划分。本项目采用轻量反射分笔，没有移植完整图像重绘或时域管线。

菜单 **Apply Day Concept Style** 会把这个测试场景的配色与草形参数恢复到白天概念图方案；它会覆盖测试场景中的手工调整。

## 角色涉水法线尾迹

进入原实验场 Play 后，点击底部 **池塘浅滩 · 向前走入水，查看波纹拖尾**，再用 WASD 沿浅水行走，停下观察消散。F6 切换角色与草地浏览。入口只移动角色到岸边，不改变行走、跑动或重力。

水面上的 `PainterlyWaterInteraction` 跟踪现有角色碰撞体与水面交叉，把有方向的扰动写入 GPU 水波高度/速度纹理，再生成动态法线图，影响水体折射、倒影与亮暗。停下后继续传播和衰减。干地、完全离开水面的跳跃、完全潜入水底不会持续写入；瞬移只重置轨迹起点，不连接跨池塘长线。本轮未增加游泳或物理阻力。

- `Strength`：法线影响强度，0–3，默认 1。
- `Radius`：写入扰动半径，默认 0.38 米。
- `Decay`：衰减速度，越大消散越快，默认 1.25。
- `Wave Speed`：波传播速度，默认 1.15。
- `Resolution`：默认 256，实际使用 64–512 的二次幂尺寸；大范围水面需同时考虑采样密度与性能。

这些组件参数在 Play Inspector 中可即时调整；退出 Play 后恢复，永久值应在编辑状态保存场景。组件和反射共用但保留彼此的 MaterialPropertyBlock 字段，禁用组件会清理自己的纹理引用并释放 RT。

菜单 **角色涉水 · 接入法线尾迹** 可为既有实验场恢复引用，不重建草地；**角色涉水 · 实际 Play 验证并截图** 运行真实角色移动及 GPU 法线场检查。检查记录在 `VegetationLabReview/water-interaction-checks.txt`。

## 阔叶树样本

独立场景新增 `07 阔叶主样`、`08 阔叶副样`，使用弯曲枝干与实体叶簇，可调整树高、干径、弯曲、冠幅、疏密、分枝数和种子。选中后修改 `PainterlyTree`，点击 **更新当前树 · 保存网格**。叶簇和树干各用一份材质，包含冷绿阴影、暖黄绿受光以及轻微风动。

样本用来确认树形与材质方向。正式主树建议在 SpeedTree 手调主干走势和主枝，再用同一配色验收。已有 HIGHLANDS 松树、灌木和石头仍用于背景对照；这不是完整概念图地编。

菜单 **Apply Painted Water and Tree Samples** 应用本轮水体默认值，并补建缺少的两棵树；保留已有草形和树形手动调整。

## 验证

菜单 **Run Checks** 执行编辑器检查。`VegetationLabReview/` 保存实际 GPU 渲染和 Play 验证记录。

检查覆盖比例端点、归一化取整、全零清空、连续重建、确定性草与树网格、白色花瓣、可编辑形状、岸线排除和四种 Shader。新增 `live-grass-checks.txt` 记录实际 Update 驱动的连续变更，`water-style-checks.txt` 记录相同镜头下逐项开关水体功能的 GPU 像素差异。功能生效检查不代表美术效果已经获得认可。相机检查调用真实移动入口，未逐键人工验证键盘和鼠标输入。

实际结果以 `VegetationLabReview/` 中本轮 `editor-checks.txt`、`runtime-checks.txt` 与截图为准；旧轮截图不代表当前效果。运行环境为 Windows / Unity 2022.3 / URP 14 / RTX 5060 Laptop，未测移动端性能。

修改仅在本资产目录与独立场景中，不需要重新生成主地图。

## 水草画风默认值与运行时调节

直接打开 `Assets/WeatherHouseVegetationLab/VegetationLab.unity` 即使用用户截图中的水草画风，不再需要临时应用预设。参数保存在 `Generated/ShortGrass.mat`、`TallGrass.mat`、`PainterlyGround.mat`、`PainterlyWater.mat`；颜色按截图中的 8 位 RGB 色块写入。

Play 中按 **F6** 切回草地浏览，在左侧面板选择 **水草画风**，再切换 **水体 / 草地 / 地面 / 天空**。Tab 可隐藏或显示面板。颜色按钮展开 RGB 滑条，所有调节即时生效；原有草形、数量、根尖色和风仍在原页签调节。

- **保存当前区域画风为默认值**：在 Unity 编辑器的 Play 中即可保存到对应材质，支持 Undo；退出 Play 或重新打开场景后保留。保存画风不会覆盖原有草种的根尖色、风和法线设置。
- **恢复已保存的画风**：丢弃当前区域未保存的试调。未保存的调节在退出 Play 后恢复；独立播放器支持实时调节，默认值写回需使用 Unity 编辑器。
- 短草、高草、地面的颜色和色块频率分别保留。只有 **边界硬度 / 边缘起伏强度 / 起伏分组尺度** 在当前区域草与地面间同步。

截图默认值包括水体不透明度 **0.73**、俯视/掠视倒影 **0.845 / 0.345**；短草混合/逐叶对比/频率 **0.94 / 0.325 / 0.17**；高草 **0.73 / 0 / 0.19**；地面频率 **0.15**；共用边界参数 **0.71 / 0.121 / 1.48 米**。未出现在截图中的参数沿用材质原值。

编辑模式仍可使用 **Weather House > Vegetation Lab > 水草画风 · 实时调节**：开始试调、读取已保存默认画风，调整后保存试调到原材质。关闭窗口会恢复材质引用。Play 中该窗口提供直接打开 Game 内画风面板的按钮。

`VegetationLabReview/DefaultsRevision/checks.txt` 记录截图数值与颜色、真实 Play GPU 变化、运行中写回、退出 Play 后重开场景的检查；`runtime_panel.png` 是 Game 内实际面板截图。原先的 `WaterGrassRevision/` 保存之前几轮对比，旧截图不代表最新默认值。视觉效果仍需用户确认，未测试移动端性能。

本轮参考：[Chiasera 的 Unity 2022.3 / URP 吉卜力草地项目](https://github.com/Chiasera/Stylized-grass)，以及 [Daniel Shervheim 的风格水体 Shader](https://github.com/danielshervheim/unity-stylized-water/blob/main/Assets/Stylized%20Water/Shaders/Stylized%20Water.shader) 中将底色、Fresnel 倒影、流动泡沫和接触泡沫分开的结构。没有导入第三方包或复制其源码。B 站已通过本机浏览器检索到 [草地渲染学习记录](https://www.bilibili.com/video/BV1eH4y177YA/) 等视频；视频详情页连接超时，未观看或据此推断实现。
