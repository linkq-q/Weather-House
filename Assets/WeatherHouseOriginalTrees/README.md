# 原作者树木 · Unity 静态移植

本批将 Anastasia Kasyanik 的三棵树与静态材质移植到 Unity 2022.3 / URP 14。独立对照场景用于确认原树形、叶缘和明暗层次，尚未接入天气屋主地图。

## 入口

- 菜单 `Weather House → Original Trees → 打开原作者树木对照`。
- 场景 `Assets/WeatherHouseOriginalTrees/OriginalTreesComparison.unity`。
- `Prefabs/Author_Birch.prefab`、`Author_Beech.prefab`、`Author_FirTree.prefab` 可直接拖入场景，材质与轴向已配置；薄叶位移的包围盒余量在加载时自动恢复。
- `验证并拍摄对照` 在实际 Play 中检查、拍摄并返回原保存场景；复用现有场景，保留保存过的手调。
- `重建源参数并验证对照` 会重建本目录的对照场景与原始材质参数，用于重新生成本批基线。
- `应用光照校准并验证对照` 恢复当前的光照校准参数并进入实际 Play 验证；普通验证保留已保存的调整。
- 并排原图和实拍见项目根目录 `OriginalTreesReview/comparison.html`。

## 原始资产与坐标

来源：https://github.com/AnastasiaKasyanik/stylized-cozy-trees

固定版本 `b1544aed529df930863c6abc289eaddcbad1b3ad`。作者工程标记 UE 5.1；本机使用 UE 5.7.4 的只读导出器取得模型、纹理和材质节点，没有保存或升级原始资产。

- 三个 FBX 来自原 `SM_Birch`、`SM_Beech`、`SM_FirTree`，没有重新建模或统一高度。
- 九张作者纹理由 TGA 无损转 PNG，逐像素一致；包含三张叶片、三张树皮底色、三张 AO。
- FBX 与 PNG 的 SHA-256 记录在 `Source/manifest.json`；材质原数值记录在 `Source/author-materials.json`。
- 导入保留原法线、切线、UV0—2；厘米换米，原 pivot 与地下树根位置保留。源关卡树木间隔各 4 米。
- UE 世界 `(X,Y,Z)` 对应本对照场景 Unity `(Y,Z,X)`。实测 FBX 导入先产生 `(X,Z,-Y)`，实例再旋转 Y=-90°。
- UE 导出会翻转 UV 的 V 和副切线。叶片程序位移恢复两者；图像采样遵守 Unity 的纹理坐标约定。

## 材质对应

`AuthorFoliage.shader` 保留世界法线渐变、原噪声混色和两次切线到世界变换。白桦、山毛榉启用原 `isFluffy`，冷杉关闭。叶片取 RGB 的 R 通道做 0.3333 裁切，三张叶片贴图没有 Alpha。`Opacity=0.05` 用于叶片散射。三种颜色存为线性 Vector，避免再次做颜色空间转换。

`AuthorTrunk.shader` 使用 UV0 原底色和 AO，Specular=0、Roughness=0.5。原纹理的 sRGB 标记全部保留，包括 AO 和叶片遮罩。

Unity 普通漫反射省略 UE Lambert 中的 `1/π`，所以源方向光强度 6 换算为 Unity `6/π`；叶片独立散射项补乘 π，以保留原来的归一化。没有为了调色修改作者的材质颜色常量。

## 跨引擎差异

URP 没有原 UE Subsurface 的独立透射阴影和厚度数据。第二批从现有主光阴影贴图读取最近遮挡深度，按当前级联矩阵换算光线穿过树冠的距离，再进行指数衰减。这样完全遮挡的内部不再固定保留 95% 的散射光。厚度倍率为 0.75；它是跨引擎校准值，原作者的 `Opacity=0.05` 与三组颜色不变。

该方法参考本机 UE 5.7 的散射公式，原项目标记为 UE 5.1；最近遮挡距离也不等同于真实薄叶厚度，不能声称逐像素还原。透射深度采用单点采样，低分辨率阴影或级联交界仍可能产生跳动；屏幕空间阴影路径退回表面遮挡。当前验证环境为 URP 14 的主方向光阴影贴图、Windows Direct3D11。

对照场景采用曝光 +0.65 EV、白平衡温度 +10、饱和度 +10、方向光阴影强度 0.9、SMAA High。棋盘地面反射乘数为 1.12，仅调整自建的对照地面。所有曝光与色调设置保存在本场景独立 Volume 中，没有修改项目全局 URP 配置。叶片补齐现有 SSAO 的编译变体；静态形变和裁切逻辑保持不变。

相机由原图估计，背景和棋盘地面为对照布景。天空大气、云、间接光和叶片柔和感仍与原图存在差异。

本批是静态移植。原 `F_BendWind` / `SimpleGrassWind` 暂停，风的参数与动画需要另行移植验证。画面对照确认接近后再继续森林布局。

## 验证范围

实际 Play 检查三树的局部/世界尺寸、位置、三角形数量、材质槽、Shader 编译，以及从源 FBX 抽取的叶片顶点位置、法线、切线、副切线和 UV0。GPU 检查原树冠形变与叶片裁切确实改变画面，并确认临时探针不修改资产材质。实拍和检查记录放在 `OriginalTreesReview`。

第二批增加透光 GPU 对照：只替换叶片的运行时材质副本，对比厚度倍率 0.5 与 1，要求变厚后受影响叶区亮度下降，并用重复帧检查捕获稳定性。检查后恢复材质绑定和参数；它验证光照响应，不将像素变化数量当作美术相似度。

检查通过只表示上述行为已验证。美术接近程度由原图/实拍并排查看，不用像素变化数量代替视觉验收。

## 署名

作者模型、九张纹理及原材质设计 © 2025 Anastasia Kasyanik，MIT 许可原文保留于 `Source/AUTHOR_LICENSE.txt`。

`Textures/TilingNoise05.png` 是原材质依赖的 Epic 引擎纹理，本地从 `/Engine/MapTemplates/TilingNoise05` 导出，单独标记来源，不纳入作者 MIT 资产的署名范围。URP 材质、测试与对照布景为本项目移植代码。

## 阴影偏移修复

URP 14 默认 `UniversalAdditionalLightData.usePipelineSettings=true`，会忽略灯光自己的 Bias/Normal Bias，使用管线资产的 1/1。独立对照太阳现在显式关闭此选项，并保存深度偏移 0.02、法线偏移 0.15。软阴影仍沿用管线 Medium；URP 会在渲染时自动创建附加灯光数据，缺少序列化组件并不意味着原来使用 Low。

修复前在实际 Play 复现覆盖断言失败。`AuthorTreeBiasProbe` 固定相机，验证全局 1/1 与同值局部设置图像一致、重复帧稳定，并拍摄两组较小偏移。结束后恢复所有临时灯光参数；原树模型、贴图、曝光和材质颜色不变。固定原图/修复前/修复后见 `OriginalTreesReview/shadow-bias.html`，GPU 记录见同目录 `shadow-bias-checks.txt`。

## 半影与环境光校准

独立太阳改用 High（7×7 tent PCF），原先为 Medium（5×5）。URP 同时将 caster bias 核半径从 2.5 放大到 3.5；实际比较时按该比例补偿两组偏移，避免将位置变化混进柔化判断。High 配合过小偏移会在无遮挡地面产生条纹，因此最终局部深度/法线偏移取 0.15/0.35，仍显式关闭管线偏移覆盖。

Trilight 天空 RGB 改为 (0.53,0.60,0.72)，赤道 (0.34,0.37,0.43)，地面 (0.253,0.231,0.20)。曝光、阴影强度、白平衡和材质颜色保留前批值。实际 Play 中 SH L0 从 (0.114171,0.153935,0.219843) 变为 (0.112245,0.135561,0.188266)，确认环境进入渲染。地影核心冷色减弱；白桦冠内仍偏暗，需要继续单独校准下侧补光。

`VerifySoftShadowQuality` 比较等效偏移下的 Medium/High，检查重复帧稳定与底部无遮蔽地面 600×60 区域无新增阴影差异；并结合实拍审阅条纹与接地。原作者太阳光源角只读导出值为 0.735700°，记录于 `Source/author-directional-light.json`（原工程 UE 5.1，由本机 UE 5.7.4 读取）。固定 PCF 不等同于原 UE 虚拟阴影的距离相关半影。第二批档案见 `OriginalTreesReview/shadow-softness.html`。

## 下侧补光最终值

在半影与地影冷暖校准基础上，只把 Trilight 地面颜色改为 (1.0,0.9,0.65)，补足树冠下侧的暖色环境光；天空、赤道、曝光和阴影均保持前批值。它是本对照场景对间接光的近似校准，不是从 UE 复制的参数。白桦主冠暗部均值由约 (170,55,11) 提升至 (180,71,14)，原图对应区域约 (191,72,25)；地影核心基本保持 (81,87,100)。当前保留此档，继续增加会放大个别叶片的米白平片感。

第三批和本轮总览分别见 `OriginalTreesReview/ambient-fill.html`、`comparison.html`。实际 Play 检查与全部近景已更新，固定 ROI 及源图哈希记录见 `fill-review.json`。原图/Unity 视觉区域没有逐叶配准，这些数值用于跟踪调节方向。当前仍有叶片平片感和局部色彩差异，尚未通过完整移动视角验收；继续保留独立场景，未开始森林布局。
