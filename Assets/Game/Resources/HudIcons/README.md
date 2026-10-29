# 快捷图标

Keyboard、Camera、Journal 是本次用内置 imagegen 生成的黑白图像。Unity 导入设置使用灰度生成 Alpha；黑色区域变为透明，白色区域由 GameplayHud 着成米色，原始 PNG 保留。

生成提示的共同约束：纯黑背景、纯白实心剪影、干净镂空、无描边、无渐变、无发光、无纹理，适合约 40 像素显示。Keyboard 为 W 在上、ASD 在下的四键布局；Camera 为相机和圆形镜头镂空；Journal 为书脊、书签和叶片镂空。

不要把 alphaUsage 改回读取原图 Alpha，否则黑底会显示出来。

SpellCast 为 G 施法入口的白色魔杖与星芒剪影，由 `Tools/SpellInk/build_icon.py` 绘制，原生透明 RGBA，使用读取原图 Alpha。显示尺寸 44×44，与现有 HUD 共用浅米色着色。
