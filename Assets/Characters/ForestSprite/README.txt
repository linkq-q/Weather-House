森林小精灵 · 开襟展翼披风与接触回弹（2026-10-22）

入口
Assets/Scenes/Experimental/ForestSprite_Demo.unity
停止旧 Play 后重新运行；右侧“实时调节 · 展翼披风”可即时改变效果。
角色预制体：Assets/Characters/ForestSprite/Generated/ForestSprite.prefab。

移动与动作
默认走路 1.5 m/s，跑步 3.0 m/s，约为本次修改前已保存速度的 1.5 倍。
实际脚踝前后行程：走路约 36 cm，跑步约 44 cm；脚步频率与地面位移匹配。
走路步幅滑条保留，100% 使用新增大步动作；走速范围 0.3–2，跑速范围 1–4 m/s。
跳跃高度参数 .48 m，重力 -8 m/s²；起落比原设置舒缓，动画按同一腾空时长播放。
走路与跑步叠加小幅头部点头和左右转动，最大额外转角约 1.8° / 2.5°。

披风实时调节
原雕刻FBX保留，生成的翼式披风前襟分开、两侧加宽15%。
左右前襟各自绑定，肩至后缘用六段引导形成大波浪；走路小幅，跑步展开并向后抬起。
“展翼推荐”只恢复披风参数，保留当前移动速度和步幅；点击保存才写入已存参数。
“站立试风”可原地观察；风力、抬起、展翼和 S 形弯曲调整大轮廓。
波浪幅度、波浪速度调整起伏；自由摆幅调整动画轮廓之外的布料活动范围。
回落时间控制停下后收拢；细调里有阻尼、弯曲刚度、接触回弹和回弹响应时间。
“碰撞检查”显示穿入与避让毫米数；“显示接触位置”标记最深的顶点和布面采样点。
“导出最近碰撞记录”保存最近180帧到角色参数目录的cape-contact-last.json。
大轮廓由引导骨骼控制，Unity Cloth负责布料跟随和身体接触。接触弹簧小幅避让、随后带阻尼恢复，节点修正最多2.5cm。
检测覆盖布顶点、三角边中点与重心，对照身体/手臂碰撞范围；采样不是所有三角形的连续精确求交，仍需观察特殊姿势。
参考《光·遇》官方公开画面中的展翼和转身轮廓，并非该游戏未公开算法的复制。

保存
%USERPROFILE%\AppData\LocalLow\DefaultCompany\Weather House\ForestSprite\cape-flow-v1.json
每台电脑单独保存。旧参数会覆盖默认值；本轮检查未改写用户已有JSON；缺少的新参数在载入时补默认值。
拖滑条不会改写模型、场景或动作资产；“保存参数”后重启 Play 仍然生效。

操作
WASD 走路，Shift 跑步，Space 跳跃；右键转视角，滚轮缩放。
1 待机、2 走路、3 跑步、4 跳跃、5 循环、0 手动、R 归位。
P 开关披风布料与帽尖、发梢物理。小窗口可展开/滚动调节面板。
保留原用户雕刻模型、贴图、帽尖与头发弹簧参数。

验证
本轮独立Unity原生Play运行报告和连续帧保存在交付目录“走跑披风分层_20261022”。
wing-contact-v2-verified-runtime包含走、跑、跳、转身、强风、实时调节和保存迁移的完整通过报告。
0.07m机制对照试验与最终版本分开标注；最终连续帧和接触报告见同目录验收记录。
最终实测结果以该目录的验收说明和报告为准；源码检查、运行数值与主观观感分开记录。

重建
Unity 菜单 Weather House > Forest Sprite > Build Independent Demo。
先关闭独立演示场景。只重建本角色生成资产，不修改主地图或项目渲染设置。
身体使用显式碰撞球对；演示地面是跟随角色的局部球面近似。
其他场景需另配碰撞体，Unity Cloth 不会自动碰撞任意 MeshCollider。
公开实现参考
https://matthias-research.github.io/pages/tenMinutePhysics/14-cloth.html
https://obi.virtualmethodstudio.com/manual/7.0/skinconstraints.html
https://nvidiagameworks.github.io/NvCloth/1.1/Solver/Index.html#wind-simulation
