# 天气屋非动物音效交付

21 条 WAV 已可接入：P0 核心 19 条 + P1 室外/岸浪 2 条。统一 48 kHz、单声道、16-bit PCM。素材文件位于 Assets 外，本任务未操作 Unity。

[clip ID / 用途 / 路径](integration_clips.md) · [派生区间与 SHA-256](derived_manifest.json) · [原始台账](asset_ledger.json) · [来源许可](source_catalog.json)

原始下载 19 个对象约 11.52 MB，Selected 保留 23 个原料含备选约 3.26 MB；Derived 导出 21 条约 5.34 MB。Archives 中未选入的旧包继续保留，不扩大游戏导入。

全部采用来源为 CC0。完整法律文本在 Licenses/CC0-1.0-legalcode.txt，作者页面在 Evidence。OGG/MP3 派生 WAV 不恢复丢失的信息，不称无损母版。所有原始文件保持不变。

脚步分草地、普通泥土、石头各两条。泥土来自真实 walking 的独立步点，未使用泥浆/落地声冒充走步。A06 是真实草摩擦；A09 是猫舔奶的液体接触，不含叫声；A10 与风声共源裁切。B03 是低电平虫声环境，B04 是岩岸水浪；无新增音乐。

21 条导出已解码、检查 PCM 格式和零削波，单次音效有边缘淡化，循环有交叉过渡。人工听感、接缝和游戏混音仍以实际游戏试听为准。可用 derive_candidates.py 复现导出，它使用本机现有 ffmpeg 与 bundled Python/numpy，无需安装新软件。
