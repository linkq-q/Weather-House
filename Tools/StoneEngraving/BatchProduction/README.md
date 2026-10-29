# 七块石刻成品

固定使用用户确认的七组网格、投影范围、字形和排版。`approved-selection.json` 与 `approved-layout.json` 保存确认时的数据，不在重建时重新选石。

## 已完成

- 风、湿与干、强与弱、草与树、先与后、一起、这里与那里，各自独立材质、预制件、碰撞与三张技术贴图。
- `Source` 保存 ImageGen 破边候选，生成提示与原始路径保存在 `generation.json`。只在原笔画周围混入少量固定破边，保护中心线，远处空白不会产生新纹样。
- 动物语直接取项目 `AnimalGlyphsV04` 的原始 alpha。缩放后按对应插图的中轴代表线宽加粗，再检查连通部分和封闭空隙数量。字形只允许轻微向外边缘变化。
- 高度图名义槽深 80 mm；法线按每块实际刻面宽高分别计算。使用现有视差和槽内遮挡材质，不生成几何凹槽。
- 新材质启用平面石面法线，旧雨水材质默认保持原样。普通游玩相机增强发现性，真实拍照相机保持原始刻槽。

## 重建与交付

```powershell
& Tools/StoneEngraving/BatchProduction/run_review.ps1 -Python '<包含 NumPy 与 Pillow 的 Python 路径>'
```

脚本只更新 `StoneEngravingReview/20261029/UnityProject` 独立预览工程及桌面的成品预览；不覆盖主工程资产，不触碰主场景。依次进行数值测试、固定网格准备、实际刻面测量、贴图生成和 Unity 渲染。需要主项目已导入的 Unity 包缓存。

确认重建结果后，把独立工程下 `Assets/Experiments/StoneEngraving/Seven` 中各石头的资产连同 `.meta` 交付到主工程同名目录，再通过 `SevenStoneProductionReview` 的 `install` 命令附加真实游戏的相机可读性组件。不要把主工程组件带回缺少 GameplaySession 的独立工程；不要覆盖主工程已有摆放组。

已有交付位于 `Assets/Experiments/StoneEngraving/Seven`。`SevenStoneReviewGroup.prefab` 保存制作时的临时摆放。场景树为“七块石刻 · 制作预览”，后续摆放角度由用户自行调整，生成脚本不会自动同步主场景。

编辑器顶栏 `Weather House/石刻/放置七块成品预览（不保存主场景）` 可在无同名组时重建临时检查摆放。`Weather House/检查/七块石刻实际相机（独立存档）` 检查真实游戏与相机切换，结束后恢复编辑状态和存档环境。

## 验证记录与边界

2026-10-29：原 6 项贴图测试与新增 4 项线宽、字形、边缘范围、矩形像素间距测试通过。七块完整图案与字形进行 512 列刻面射线采样，全部无落空、无陡面。主工程 prefab 检查通过。

水岸主场景实际 Play 下，七块均通过游玩增强、真实拍照基准逐像素相等、退出相机过渡、恢复游玩增强和组件清理检查。证据位于 `StoneEngravingReview/20261029/SevenStoneProduction/MainScene/Play`。这些相机隔离检查不代表玩家一定能读懂图意或当前角度适合拍照；用户已接手最终角度调整。

主场景有其他未保存工作，因此只添加可撤销的检查组，没有保存场景文件。第一次放置期间场景磁盘 SHA-256 始终为 `387E387BD747DF5FCA88416BC36992E6699B2A3D12DDC952D9139A0FCA525055`。没有接入谜题奖励、照片识别或正式世界分布。
