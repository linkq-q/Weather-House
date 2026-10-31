# 生活玩法模型与动作接入清单

2026-10-26。六件新增物件由 Blender 5.1.1 脚本实际建模、保存源文件、导出 FBX、渲染预览并重新导入检查。另有六段同骨架松鼠动作，见 [动画接入说明](Animations/README.md)。本资源工作未操作主 Unity Editor 或保存场景。

后续追加的真实三维手账见 [模型及动画说明](Journal/README.md) 和 [动态预览](Journal/preview.html)，包含实际开合、弯曲翻页、原设计纹理与正文映射接口。

## 模型接口

Unity 可用 `Resources.Load<GameObject>("LifeAssets/" + id)` 加载。源文件在 `Tools/LifeAssets/Source`，生成脚本在 `Tools/LifeAssets/build_life_props.py`。

| id | 用途 | Unity 尺寸 X/Y/Z（米） | 三角面数 |
| --- | --- | --- | --- |
| GrassPatch | 一种明确可采的草料 | 0.513 / 0.578 / 0.498 | 1028 |
| GrassBundle | 可拿取、编垫或给羊的单份草束 | 0.678 / 0.229 / 0.254 | 2100 |
| GrassMat | 草料编成的可搬动草垫 | 1.056 / 0.048 / 0.762 | 8532 |
| WaterBowl | 固定浅水碗 | 0.720 / 0.162 / 0.720 | 910 |
| RibbonFlag | 固定于木杆的两条风力布条 | 0.698 / 1.250 / 0.089 | 156 |
| ShadeShelter | 草垫休息用三面背风小棚，具有不透明斜顶 | 1.898 / 1.478 / 1.560 | 432 |

所有物件根在地面中心、单位米、缩放 1。模型为本次原创几何及纯色材质，不使用外部贴图，也不增加药草系统。湿干是同一物件的状态，调用方可用 MaterialPropertyBlock 改色与光滑度；需要滴水时由状态表现层添加粒子，不复制为两种模型。

- `WaterBowl` 的 `WaterSurface` 是独立子网格，初始半径 0.29 米，中心高度 0.12 米。空碗时隐藏。建议水位在 0.06—0.14 米间变化，并将半径从约 0.23 变至 0.30 米，保持水面在碗内。只沿 Y 移动会在低水位穿过倾斜碗壁。
- `RibbonFlag` 的 `Ribbon` 是独立子网格，系结轴心为本地 `(0.025, 1.13, 0)`，向 +X 延展约 0.64 米。顶点色 `WindWeight` 的红通道从系结点 0 到末端 1，供风力形变 Shader 使用；也可用小角度旋转表现风力，避免旋转整个杆子。
- `ShadeShelter` 敞口朝 Unity +Z，原点在地面中心；碰撞由玩法接入层创建。屋顶为有厚度的不透明几何，可投射真实阴影。
- FBX 带有一个 `*Root` 空节点。子对象应递归按名称查找，兼容 Unity 导入器折叠根节点的差别。
- 专用导入器 `Assets/Game/Life/Art/Editor/LifeArtImporter.cs` 仅处理这一路径，将 FBX 色值保留并转换为 URP Lit。水面是独立不透明模型接口；如换专用水 Shader，不需要改碗。
- 固定水槽复用 `Assets/HIGHLANDS - Stylized Environment/Prefabs/Camp/Trough.prefab`；晾架、温室、冷石面优先复用地图已有物件。另制 `ShadeShelter`，为本轮草垫休息提供有实际屋顶的遮阴与背风载体。

## 已执行检查

`model-manifest.json` 记录每个网格的顶点、三角面、材质、包围盒和重新导入结果。六个 FBX 全部通过以下检查：非空网格与 UV、有限坐标、合理米制尺寸、几何不低于地面、回导网格数量与顶点数一致、回导尺寸误差小于 0.1 毫米。六张预览已逐一视觉检查，无破面或错位子对象。

该检查是离线资源证据。Unity 材质导入、实际镜头中的辨识度、交互碰撞和水位变化由集成验证负责，不把 Blender 图片视为 Unity Play 证据。

## 模型预览

![可采草](GrassPatch.png)
![草束](GrassBundle.png)
![草垫](GrassMat.png)
![水碗](WaterBowl.png)
![风力布条](RibbonFlag.png)
![背风遮阴棚](ShadeShelter.png)

## 动作复用

`animal-clip-audit.json` 由 `Tools/LifeAssets/audit_animal_clips.py` 实际导入 17 个现有 FBX 得出。松鼠 27 骨、羊 29 骨、鹿 29 骨、狐狸 32 骨。同一物种所列动作的骨名与父子层级全部一致，均有非零长度动作。此项不证明脚底接触或表演自然度。

| 动物 | 当前实有动作及路径 | 可立即复用的表演 | 明确限制 |
| --- | --- | --- | --- |
| 松鼠 | `Assets/Experiments/Squirrel/Models/{turn,wave,walk_biped,run_biped}.fbx` | wave 用于招呼；转身、走近、面对指认物 | 原 prefab 的 turn 同时作为 idle/forage；本轮另制 Idle、Drink、Accept、RestDown、RestLoop、RestUp，只绑定关键松鼠 |
| 羊 | `Assets/Experiments/QuadrupedMotion/EightAnimals/Models/sheep/{idle,eat,react,walk}.fbx` | 接受草料后 eat；停步转向及温和反应 | greet 当前复用 idle；不能把 eat 自动视为准确碰水的饮水；没有完整卧起 |
| 鹿 | `Assets/Experiments/QuadrupedMotion/EightAnimals/Models/deer/{idle,eat,react,walk_v3}.fbx` | 看向天气、转向说话者、短程往返及进食 | greet 当前复用 idle；无独立饮水或卧起 |
| 狐狸 | `Assets/Game/World/Wildlife/Animation/Fox/fox_{eat,restdown,restsleep,restup}.fbx`，以及 EightAnimals 下的 idle_v3 | 已有进食、卧下、卧地呼吸、起身，可作 E5/E7 休息反应 | 原模型无眼睑；休息为睁眼卧地，不声称闭眼睡眠。四足接触与场景地形仍要检验 |

玩法可将现有动作与明确的朝向、对象高亮、话轮相结合表达检查、接受、拒绝、邀请和指认。不需要把每句话都新增为一段骨骼动画。但实际饮水必须检查口部靠近水面；对话相互朝向必须保留指定说话者与听者，不能只朝向最近对象。

原 `WildlifeAgent.LateUpdate` 对独居动物的 Drink 使用约 10 度头部俯仰和小幅摇动。本轮提供专用松鼠饮水和完整卧起片段；羊、鹿仍使用原有动作，不把其停步称作卧地。当前结尾由关键松鼠及另一只松鼠表演休息，因此不额外引入未使用的羊鹿卧起资源。不能拿狐狸 clip 直接绑到其他骨架。

## 免费资源检索与许可

本轮优先核查作者原站与当前项目已有源文件，没有为扩大清单下载无关大包。下列检索结论不等于已重定向接入。

| 来源 | 2026-10-26 核到的许可与内容 | 本项目处理 |
| --- | --- | --- |
| [Quaternius Ultimate Animated Animal Pack](https://quaternius.com/packs/ultimateanimatedanimals.html) | 作者页标 CC0，12 种动物、多段动画，允许个人和商业项目，提供 FBX/Blend/glTF | 狐狸 Eating 之前已下载并适配本项目 32 骨。当前重核来源，继续复用，见现有 `Animation/Fox/SOURCES.md` |
| [Quaternius Farm Animal Pack](https://quaternius.com/packs/farmanimal.html) | 作者页标 CC0，7 种农场动物与动画，FBX/OBJ/Blend | 有羊候选，但作者页不保证饮水和完整卧起；骨架不同，不新增未经适配的资源 |
| [Fox 作者分发页](https://poly.pizza/m/Bc97C66HKi) | 当前分发页仍标 Public Domain (CC0) | 已有本地原始下载 SHA256 与来源记录，无需再下载重复内容 |
| [Undead Squirrel](https://opengameart.org/content/undead-squirrel-animated) | CDmir / TinyWorlds，作者发布页标 CC0，3D 有骨骼和动画 | 僵尸题材且未明确包含饮水、休息，造型与当前直立松鼠不同，不直接引入 |
| [Mixamo 官方 FAQ](https://helpx.adobe.com/creative-cloud/faq/mixamo-faq.html) | 免费用于项目，但动画库与自动绑定仅支持双足类人 | 不用于羊、鹿、狐四足重定向；不声称能直接复用 |

既有狐狸休息还使用 Apache-2.0 的 `lhns/steam-frame-nix` 姿态采样代码，已随 `Assets/Game/World/Wildlife/Animation/Fox` 保存完整许可。项目模型原始权利不因新增动作而改变。本轮原创六件物件不引入第三方许可证依赖。

## 集成验收仍要观察

1. 草束与装饰草能区分；采集、编垫和恢复供应的动作清楚。
2. 湿干颜色不抹掉原色；水位保持在碗内，雨停后仍可见。
3. 弱风与停风区别可见；木杆固定，只有布条变形。
4. 招呼、指认和回应朝向正确，NPC 不在表演时被生态移动拖走。
5. 狐狸和关键松鼠卧起接地自然；松鼠口部能接近水面，尾巴不被原程序摆尾覆盖；新 Idle 与既有行走切换要在 Unity 中观察。
