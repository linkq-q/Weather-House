# 动物柔和卡渲与受光侧描边试验

这是独立羊模型对比样例，不替换正式地图的材质、预制体或渲染管线。

## 查看

独立工程位于仓库根目录 `AnimalToonReview/20261029/UnityProject`。
用 Unity 2022.3.62f3c1 打开，在 `Assets/AnimalToonSample/AnimalToonSample.unity` 进入 Play。
左到右依次为原 URP Lit、柔和色阶、柔和色阶加受光侧描边。
屏幕左下角提供光照方位、描边宽度、色阶强度、明暗过渡、播放行走、进入阴影和前景遮挡。

## 实现

- `AnimalPaper.shader` 保留原动物纹理，把主方向光分成柔和的三档明暗。
- `AnimalLightOutlineFeature` 只绘制 `AnimalLightOutline` Pass，普通材质不受影响。
- 外扩壳采用源表面的世界位置采样阴影，以平滑法线与主光方向的点积控制亮边显现。
- 线宽以 1080p 像素为基准，随输出分辨率缩放，受深度遮挡；光强为零时不发光。
- 独立 Renderer 使用模板缓冲的最低位屏蔽动物内部，避免脸和身体上出现外扩壳接缝。未来接入其他 Renderer 时需检查模板位冲突。
- 样例生成独立网格，把接缝平滑方向编码在 UV4 的切线空间；骨骼动画使用变形后的法线与切线重建描边方向。原网格未修改。
- Play 调参使用临时材质实例，不保存到材质资产。

当前试验针对不透明、主方向光照明的动物。未加入附加点光源描边、透明水体覆盖处理或全屏后处理。贴图自带的块状明暗仍然保留。

## 复现验证

`Editor/AnimalToonReview.cs` 仅允许在带 `.animal-toon-review` 标记的独立工程运行，入口为 `WeatherHouse.AnimalToon.Editor.AnimalToonReview.BuildAndReview`。
验证器进入实际 Play，输出同镜头截图，并比较开关描边的像素差，检查光源左右切换、阴影消隐、零光强、前景遮挡和动画顶点变化。
结果保存到独立工程上一级的 `checks.txt` 和 `captures`。完成后退出 Play 并关闭验证用 Unity。
