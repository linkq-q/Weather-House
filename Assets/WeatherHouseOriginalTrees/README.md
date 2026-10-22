# 原作者树木 · Unity 静态移植

本批将 Anastasia Kasyanik 的三棵树与静态材质移植到 Unity 2022.3 / URP 14。独立对照场景用于确认原树形、叶缘和明暗层次，尚未接入天气屋主地图。

## 入口

- 菜单 `Weather House → Original Trees → 打开原作者树木对照`。
- 场景 `Assets/WeatherHouseOriginalTrees/OriginalTreesComparison.unity`。
- `Prefabs/Author_Birch.prefab`、`Author_Beech.prefab`、`Author_FirTree.prefab` 可直接拖入场景，材质与轴向已配置；薄叶位移的包围盒余量在加载时自动恢复。
- `验证并拍摄对照` 在实际 Play 中检查、拍摄并返回原保存场景；复用现有场景，保留保存过的手调。
- `重建源参数并验证对照` 会重建本目录的对照场景与原始材质参数，用于重新生成本批基线。
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

URP 没有原 UE Subsurface 的独立透射阴影和厚度数据。本批使用包裹光和薄叶透射近似，因而不能声称像素级还原 UE。相机由原图估计，背景和棋盘地面为对照布景。天空大气、云、曝光和间接光也与原引擎不同。

本批是静态移植。原 `F_BendWind` / `SimpleGrassWind` 暂停，风的参数与动画需要另行移植验证。画面对照确认接近后再继续森林布局。

## 验证范围

实际 Play 检查三树的局部/世界尺寸、位置、三角形数量、材质槽、Shader 编译，以及从源 FBX 抽取的叶片顶点位置、法线、切线、副切线和 UV0。GPU 检查原树冠形变与叶片裁切确实改变画面，并确认临时探针不修改资产材质。实拍和检查记录放在 `OriginalTreesReview`。

检查通过只表示上述行为已验证。美术接近程度由原图/实拍并排查看，不用像素变化数量代替视觉验收。

## 署名

作者模型、九张纹理及原材质设计 © 2025 Anastasia Kasyanik，MIT 许可原文保留于 `Source/AUTHOR_LICENSE.txt`。

`Textures/TilingNoise05.png` 是原材质依赖的 Epic 引擎纹理，本地从 `/Engine/MapTemplates/TilingNoise05` 导出，单独标记来源，不纳入作者 MIT 资产的署名范围。URP 材质、测试与对照布景为本项目移植代码。
