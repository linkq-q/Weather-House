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

草与地面的 `DepthNormals` Pass 继续保留，水深由深度纹理重建的水底世界坐标计算。岸线跟随岸边和相交石头，分成断续近岸线与向外的细线。水面另有稀疏、带收尖的横向短笔触；倒影按横向小块取样并简化亮度层次。

- `_Opacity = 0.70`：水层与可见水底合成约 70% / 30%，岸线另做边缘渐隐。
- `_ShoreStrength / _ShoreWidth`：岸线明显程度与深度范围。
- `_SurfaceStrokeStrength / _SurfaceStrokeDensity`：水面短横纹亮度与数量。
- `_ReflectionBrushStrength / _ReflectionBrushSize / _ReflectionPosterize`：倒影分笔程度、笔触尺度、色阶强度。
- `_CausticStrength / _CausticScale / _CausticSpeed / _CausticDepth`：水底焦散的强弱、大小、速度与深度衰减。焦散跟随重建的水底，并受主光阴影影响。
- `_RefractionStrength`：轻微折射；深度检查会拒绝采到水面前方草叶和岸石的偏移样本。

完整透底、折射与焦散在 Game 相机中验收，该相机单独开启 Depth/Opaque Texture。当前全局 Opaque Texture 关闭时，Scene 视图使用普通透明回退；不会读取其他相机留下的颜色纹理。

本轮参考了 [Ameye 的 URP 14 风格水体](https://ameye.dev/notes/stylized-water-shader/)、[Cyanilux 的水体拆解](https://www.cyanilux.com/tutorials/water-shader-breakdown/)、[Unity 官方 BoatAttack](https://github.com/Unity-Technologies/BoatAttack)。油画方向参考 [Kyprianidis 的定向绘画抽象研究](https://www.kyprianidis.com/p/pg2009/) 中保留边界、简化局部细节的思想。本项目采用轻量反射分笔，没有复现完整 Kuwahara 后处理，也没有整包导入这些工程。

菜单 **Apply Day Concept Style** 会把这个测试场景的配色与草形参数恢复到白天概念图方案；它会覆盖测试场景中的手工调整。

## 阔叶树样本

独立场景新增 `07 阔叶主样`、`08 阔叶副样`，使用弯曲枝干与实体叶簇，可调整树高、干径、弯曲、冠幅、疏密、分枝数和种子。选中后修改 `PainterlyTree`，点击 **更新当前树 · 保存网格**。叶簇和树干各用一份材质，包含冷绿阴影、暖黄绿受光以及轻微风动。

样本用来确认树形与材质方向。正式主树建议在 SpeedTree 手调主干走势和主枝，再用同一配色验收。已有 HIGHLANDS 松树、灌木和石头仍用于背景对照；这不是完整概念图地编。

菜单 **Apply Painted Water and Tree Samples** 应用本轮水体默认值，并补建缺少的两棵树；保留已有草形和树形手动调整。

## 验证

菜单 **Run Checks** 执行编辑器检查。`VegetationLabReview/` 保存实际 GPU 渲染和 Play 验证记录。

检查覆盖比例端点、归一化取整、全零清空、连续重建、确定性草与树网格、白色花瓣、可编辑形状、岸线排除和四种 Shader。新增 `live-grass-checks.txt` 记录实际 Update 驱动的连续变更，`water-style-checks.txt` 记录相同镜头下逐项开关水体功能的 GPU 像素差异。功能生效检查不代表美术效果已经获得认可。相机检查调用真实移动入口，未逐键人工验证键盘和鼠标输入。

实际结果以 `VegetationLabReview/` 中本轮 `editor-checks.txt`、`runtime-checks.txt` 与截图为准；旧轮截图不代表当前效果。运行环境为 Windows / Unity 2022.3 / URP 14 / RTX 5060 Laptop，未测移动端性能。

修改仅在本资产目录与独立场景中，不需要重新生成主地图。
