# 动物卡渲 V2：粗描边与色块合并

独立试验，只修改样例代码。正式地图、动物原材质、原网格和预制体未替换。

## 打开与调节

双击仓库根目录 `AnimalToonReview/20261029/Revision2/打开新版卡渲.cmd`。
新版工程为 `AnimalToonReview/20261029/Revision2/UnityProject`，场景为
`Assets/AnimalToonSample/AnimalToonSampleV2.unity`。在 Unity 顶部点击 Play；控件在 **Game 画面左下角**。

- 描边宽度：0–16，默认 8，以 1080p 像素为基准。
- 色块合并：0–24，默认 12。0 保留原始局部明暗；增大后，较小阴影和毛刺会合并。大块形状仍随模型和灯光变化。
- 明暗过渡：控制分档边界的软硬，和合并范围独立。默认保留用户的 0.01。
- 光照方位默认 125.88，色阶强度默认 1。播放行走默认关闭。
- 点击顶部材质按钮放大对应动物，再点同一按钮回到三只对比。
- Play 使用临时材质，不会将调参保存到资产。退出 Play 回到样例默认值。

## 实现

`AnimalPaper.shader` 输出动物专用数据（明暗、主光阴影、视空间深度、描边宽度）。
`AnimalLightOutlineFeature` 仅在挂有 `AnimalToonView` 的相机执行，只处理拥有指定 Pass 的不透明材质。

1. 先对明暗及阴影做水平/垂直局部滤波，再按阈值重新分档；不是模糊最终画面或贴图。
2. 以覆盖范围、深度差和材质线宽作为邻域边界，避免背景及前景混入动物色块。
3. 由实际屏幕轮廓沿主方向光的投影方向连续扩张，不再膨胀法线外壳。无需改网格或烘焙 UV4。
4. 用深度检查阻止描边穿过前景，主光被遮挡或关闭时抑制亮边。
5. 沿用独立 Renderer 的模板缓冲最低位，按 MSAA 采样覆盖保护模型内部，避免描边与模型之间出现暗缝；接入其他 Renderer 前需确认该位未被占用。

色块合并是局部滤波后重新分档，会去掉小岛并平顺边界，不是将轮廓拟合成几何直线/贝塞尔曲线。
贴图内烘焙的色块、模型外轮廓折角仍然保留。当前共用相机级描边颜色，宽度在材质上。
试验范围是普通正交/透视相机、主方向光、不透明动物；未验证 XR、相机叠加、透明水体覆盖及正式兽群性能。
屏幕空间合并随画面比例工作，远处小动物应减小合并范围；不是世界空间恒定半径。

## 验证

运行 `AnimalToonReview/20261029/RunReview.ps1` 重建新版独立工程（运行前关闭该新版工程）。
需要 Unity 2022.3.62f3c1，脚本只允许带 `.animal-toon-review` 标记的目标目录。

验证器进入实际 Play，保留同模型/贴图/相机条件，输出 20 张截图到 `Revision2/captures`：
左右光照、关闭描边、进入阴影、关闭光源、前景遮挡、用户参数合并前后、8/16 宽描边、动画和透视相机。
`AnimalPaperFilterChecks` 使用实际 GPU 滤波 Shader 检查已知锯齿边界、小阴影岛、空白遮罩和深度断层。
结果在 `Revision2/checks.txt`。验证完成后退出 Play、关闭验证 Unity；视觉风格仍需用户验收。

URP 14 的 RTHandle / Blitter 接口依据本地包源码及官方示例：
https://docs.unity3d.com/Packages/com.unity.render-pipelines.universal@14.0/manual/renderer-features/how-to-fullscreen-blit.html
