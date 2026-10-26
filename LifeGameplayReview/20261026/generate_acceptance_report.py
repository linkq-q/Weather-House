"""Refresh the 39-task acceptance report from explicit source/evidence mappings.

Run: python LifeGameplayReview/20261026/generate_acceptance_report.py
Only reads whitelisted reports and source timestamps; never reads AI configuration.
"""
from pathlib import Path
from datetime import datetime, timezone
import argparse
import html
import json
import re
import shutil
import subprocess
from audit_dirty_preservation import summarize as summarize_dirty

ROOT = Path(__file__).resolve().parents[2]
REVIEW = ROOT / 'LifeGameplayReview/20261026'
DESKTOP = Path('C:/Users/31483/Desktop/天气屋')
BASELINE = DESKTOP / '玩法落地核查与任务拆解_20261026/任务清单.json'
LIFE = 'Assets/Game/Life/'
CORE = LIFE + 'Core/LifeSession.cs'
STATE = LIFE + 'Core/LifeState.cs'
CONTENT = LIFE + 'Content/LifeContent.cs'
WORLD = LIFE + 'World/LifeWorld.cs'
INTERACT = LIFE + 'World/LifeWorld.Interaction.cs'
OBJECT = LIFE + 'World/LifeObject.cs'
SETUP = LIFE + 'World/LifeWorld.Setup.cs'
GATHERING = LIFE + 'World/LifeWorld.Gathering.cs'
TASK_OBJECTS = LIFE + 'World/LifeWorld.TaskObjects.cs'
GESTURE = LIFE + 'Gestures/GestureCatalog.cs'
INPUT = LIFE + 'UI/LifeGestureInput.cs'
BOOK = LIFE + 'UI/Book3D/LifeJournal3D.cs'
HUD = 'Assets/Game/UI/GameplayHud.cs'
GAME = 'Assets/Game/Scripts/Gameplay/GameplaySession.cs'
NOTEBOOK = 'Assets/Game/Scripts/Gameplay/LanguageNotebook.cs'
AI = 'Assets/Game/Scripts/Gameplay/LanguageJudgementClient.cs'
PHOTO = 'Assets/Game/Scripts/Gameplay/MotionPhotoRecorder.cs'
PHOTO_RULES = LIFE + 'Photography/LifePhotoRules.cs'
PHOTO_CAPTURE = LIFE + 'Photography/LifePhotoCapture.cs'
PHOTO_BOOK = LIFE + 'UI/LifePhotoBook.cs'

# Every row describes concrete shipped code; status is computed separately from evidence.
MAPPING = {
 'S01': ([CORE,'ProjectSettings/EditorBuildSettings.asset','README.md'], ['core','scene','dirty'],
  '正式入口统一为水岸场景，运行时挂接生活层；隔离存档检查与原有脏文件保护已接入。',
  '从正式入口亲自进入一次；实际打包产物与新设备运行仍需验收。'),
 'S02': ([STATE,CORE,WORLD,GAME], ['core','dialogue','world_sync','progression','persistence'],
  '理解成功、接任务、近距离执行和世界完成分别记录；拒绝错误、过期、远处和重复结果，保存任务与关系。',
  '新档完成一次错误解释→正确解释→实际帮助，再退出重进继续。'),
 'S03': ([CORE,STATE,CONTENT,LIFE+'UI/LifeGestureBook.cs',PHOTO_RULES], ['core','gesture','progression','photo_model'],
  '想要/不想要开场已知；雨与收起天气直授，其余手势凭摄影或互动奖励的词义开放练习。生活主任务保留推进次序，4个摄影互动情境提前可访问。',
  '检查早到探索、旧档回访和奖励补领；确认不用先掌握全词典才能继续。'),
 'W01': ([LIFE+'Core/LifeLocalWeather.cs',OBJECT,WORLD], ['world_rules','world_sync'],
  '玩家局部雨/日照与风分渠道；强弱、关闭、水量、湿度和固定温度分别处理，停雨保水、风光逐渐干燥。',
  '观察相同范围内无/弱/强的区别，以及阴影干垫、晴天湿垫等对照。'),
 'W02': ([CORE,WORLD,LIFE+'UI/LifeCastRange.cs'], ['core','world_rules','world_sync'],
  '交谈距离3.5米，天气/帮助范围8米；天气中心只跟随玩家，圈外物件无效，动物帮助还需要已接受请求。',
  '沿圈边界移动、远处观察和切换对象，核对视觉圈与实际影响一致。'),
 'W03': ([LIFE+'Shaders/Resources/LifeGesture/GestureInk.shader',LIFE+'Shaders/Resources/LifeGesture/LocalRange.shader',LIFE+'UI/LifeStrokeGraphic.cs',LIFE+'UI/LifeCastRange.cs'], ['gesture','ui','performance'],
  'URP笔迹、识别反馈、地形范围圈和局部雨表现已接入；取消/切模式统一清理。',
  '白天、夜间、明暗背景与不同分辨率观察可读性、透明排序和过绘；性能采样不是施法极限测试。'),
 'G01': ([GATHERING,INTERACT,OBJECT,LIFE+'Content/LifePlantSpecies.cs','Assets/Game/Life/Art/Resources/LifeAssets/GrassBundle.fbx'], ['world_rules','world_sync','gathering','assets'],
  '用共享植物种类配置登记全图三个原有模型变体；靠近E采集，同种草不限定来源。近处查找走空间索引，动物交谈优先，输入模式互斥。',
  '误放、连续交互和交给羊后再回来，检查物件可找回且不复制。'),
 'G02': ([OBJECT,WORLD,INTERACT], ['world_rules','world_sync'],
  '草束与草垫共享独立湿度，制作保留湿干；雨、日照、风和遮蔽改变可见状态，历史关系不回退。',
  '确认滴水、纤维和颜色足以分辨状态，不需要中文答案代替观察。'),
 'G03': ([INTERACT,WORLD,TASK_OBJECTS,'Assets/Game/Life/Art/Resources/LifeAssets/GrassMat.fbx'], ['world_rules','world_sync','world_async','gathering'],
  '持草可在当前位置F编垫，约1.6秒，移动中断保留材料；附近兼容垫子按实际湿干与放置状态帮助动物，不再绑定专属编织点。',
  '用键盘走完采草、编垫、晾干、铺下和回收；异步回归须完成最新整段。'),
 'G04': ([OBJECT,LIFE+'World/LifeWorld.Drinking.cs',INTERACT,LIFE+'World/LifeWorld.SceneObjects.cs',TASK_OBJECTS], ['world_rules','world_sync','world_async','gathering','pose'],
  '复用地图已有容器并保留必要的近地饮水碗；支持集雨、保水、倾空、浸湿和饮用，请求按动物近处兼容物件判断。松鼠沿原控制器行走，口部接触后才扣水。',
  '饮水口部接触、返回休息位和中途读档尚需最新异步记录及画面确认。'),
 'G05': ([SETUP,OBJECT,'Assets/Game/Life/Art/Resources/LifeAssets/ShadeShelter.fbx'], ['world_rules','world_sync','assets'],
  '冷风口、暖位、遮阴与湿干分开配置；新棚有真实不透明顶和敞口，允许近距离搬垫。',
  '检查环境是否能表达冷暖与遮蔽，避免只靠动物动作猜测。'),
 'N01': ([SETUP,LIFE+'Content/LifeCoastalAnimals.cs',WORLD,LIFE+'World/LifeWorld.Roaming.cs'], ['world_rules','world_sync','roaming'],
  '关键动物保留稳定身份和容易找到的所属活动圈，次级动物在群落内自由活动；闲时释放生活站位租约，交谈／判断／Active帮助接管当前实际位置，明确邀请等原目标仍优先。新增渡口羊鹿保留牧场原角色。',
  '自由探索时能找到关键个体；相同物种的次级动物不会代接主任务。'),
 'N02': ([STATE,WORLD,OBJECT], ['core','world_sync','world_async'],
  '帮助历史和友好关系幂等保存；环境不舒适时暂离休息位，重新淋湿不抹去关系。',
  '观察拒绝、舒适、亲近和话轮是否有区别；检查还原邀请后仍能连续休息。'),
 'N03': ([WORLD,LIFE+'World/LifeWorld.Interaction.cs','Assets/Game/World/Wildlife/Runtime/WildlifeAgent.cs','Assets/Game/World/Wildlife/Journey/Runtime/JourneyHerd.cs'], ['world_sync','world_async','pose','sheep_pose'],
  '剧情目标通过原移动/碰撞控制器的短期租约驱动，面向听者或锚点；阻挡反馈，不在任务中瞬移。',
  '最新世界整套与漫游专项已核对原控制器、租约接管及实际位移；连续步态、转身自然度和镜头观感仍需人工复核。'),
 'N04': ([WORLD,'Assets/Game/World/Wildlife/Runtime/WildlifeAgent.cs'], ['world_rules','world_sync','roaming'],
  '附近次级动物按物种/个体偏好反应，复用原觅食、警觉与生活状态；圈外和关键邀请对象不受这套反应抢控。',
  '多观察几组环境与个体，判断变化是否自然、原因是否可看懂；自动检查不能替代涌现观感。'),
 'L01': ([GAME,AI,CORE,STATE,PHOTO_RULES], ['dialogue','core','photo_model'],
  '真实AI只判断玩家是否理解当前动物请求，控制帮助接取；31词的新字义由22个摄影任务及4个互动情境直接奖励，不再要求跨动物AI确字。',
  '用自己的表达复述请求，检查误解和回访；最新话轮门槛接入后需再走新档链路。'),
 'L02': ([CONTENT,STATE,CORE,PHOTO_RULES,NOTEBOOK], ['photo_model','photo_world','photo_capture','photo_ui','photo_interactions'],
  '保留原24字并新增狐狸、松鼠、羊、鹿、睡觉、吃、一起，共31个完整词；任务无已知词前置，同照多任务、补拍、旧照归属回溯与奖励去重均接入。',
  '从未知字开始自行探索；睡觉可拍合适的任意动物，吃需可见嘴和食物，一起需实际共同活动。'),
 'L03': ([PHOTO,NOTEBOOK,GAME,CORE,PHOTO_CAPTURE], ['photo','photo_capture','photo_model','dialogue','fresh_photos'],
  '无动物的自由风景照也保存；快门时固定可见物件、动作、天气和公开语境。正常收机或话轮变化保留首帧，已完成照片可回看原语境。',
  '实际拍摄后移动动物、旋转镜头、重启并回看；验证原指代仍清楚且缺帧有反馈。'),
 'L04': ([AI,GAME,LIFE+'Editor/LifeAiReview.cs',LIFE+'Editor/LifeDialoguePlayChecks.cs'], ['dialogue','core'],
  '正式HUD使用真实服务确认帮助理解，旧话轮与距离校验独立；原56次服务请求作为兼容历史记录保留，不用它证明新31词摄影奖励。',
  '真实服务检查采用预写回答，不代表所有自然表达；断网与输入手感仍要试玩。'),
 'U01': ([HUD,LIFE+'UI/LifeJournalChrome.cs',BOOK,NOTEBOOK,PHOTO_BOOK], ['ui','photo_ui','gesture','assets','fresh_photos'],
  '四个顶部页签，真实31词分页；未拍到/已拍到/已知字义以绿/蓝/黄圆底区分，选中才有绿色描边，已知含义在悬停气泡显示。J打开实体旧手账，正文贴曲页；四张独立薄页先松开，再错时落向选中页，页签常亮。',
  '亲自检查斜视、淡入淡出、快速J反向和点击投影；外观与可读性由用户验收。'),
 'U02': ([HUD,LIFE+'UI/LifePhotoCache.cs',NOTEBOOK,PHOTO_BOOK], ['photo_ui','photo_capture','ui'],
  '摄影页显示字形并通过悬停气泡提示已知含义，完成任务可回看对应照片或公开互动记录；相册保留原语境，关闭手账释放纹理。',
  '亲自播放、翻页、返回、输入猜义，并观察3D投影后命中是否准确。'),
 'U03': ([LIFE+'UI/LifeGestureBook.cs',LIFE+'UI/LifeGestureDemo.cs',CORE,BOOK], ['gesture','ui','core'],
  '第三页签提供示范、单笔练习和奖励来源；确字后开放，练习成功再学会，重复回调不重复发放。',
  '新玩家按示范完成雨手势，再验证未确认字的手势不可提前施放。'),
 'U04': ([GESTURE,LIFE+'UI/LifeGesturePad.cs',LIFE+'UI/LifeGestureDemo.cs'], ['gesture','ui'],
  '六个确定性单笔模板，固定起点方向，允许平移/等比缩放与速度变化；22正例、14负例算法检查。',
  '实际鼠标手感、相近轨迹容错和失败反馈需要新玩家操作验证。'),
 'U05': ([INPUT,CORE,HUD,BOOK], ['gesture','ui'],
  'G进入描画，与J/C/E、中文输入和Esc互斥；旧快捷施法不绕过学习；书本开合/淡入淡出期间阻止错误点击。',
  '快速切换模式、输入法选词、起笔后取消、合书中断和重新打开逐项操作。'),
 'U06': ([INPUT,CORE,STATE], ['dialogue','core','ui'],
  '右上仅显示已理解的活动帮助任务，含动物和距离、每页两个目标；无任务时隐藏，不常驻多余进度计数。',
  '正确/错误/超时回答后分别观察任务提示，确认完成、确字和学手势没有混成一次奖励。'),
 'U07': ([INPUT,INTERACT,OBJECT,GATHERING,HUD], ['world_sync','ui','gathering'],
  '附近E采集和F物件提示、手持状态、编垫反馈、摆放和取消接入真实物件；世界状态描述不直接翻译未知动物语。',
  '检查提示低干扰、物件状态可读，中文输入时不误触交互。'),
 'U08': ([CORE,STATE,INPUT,NOTEBOOK], ['progression','ui','core'],
  '事件完成与字词确认独立存储；H按当前最早未完成阶段给一句观察引导并可收起，结尾后允许回访补词。',
  'H最新版是显示/隐藏两态；不按固定停留时间泄题。提示强度和节奏最终由新玩家验收。'),
 'E00': ([CONTENT,CORE,STATE], ['dialogue','progression','gesture'],
  '院门问候理解后开放雨教学；雨不自动确字，附近狐狸提供另一问候/道别语境。',
  '从空存档走问候、解释、学雨、施雨，检查一步引入的信息量。'),
 'E01': ([CONTENT,WORLD,OBJECT], ['dialogue','world_sync','progression'],
  'E0完成后开放接水；接取后集雨、停雨保水，日照以自然明暗与跨动物偏好提供证据。',
  '确认玩家能区分雨、水和日照，并验证预先满碗与重复演示的恢复。'),
 'E02': ([CONTENT,INTERACT,LIFE+'World/LifeWorld.Feeding.cs'], ['world_sync','world_async','progression','sheep_pose'],
  'E1后开放采草—编垫—处理湿干—铺窝；羊吃相同草，原forage达到权重阈值才消费一次，真正下唇锚点带动食物视觉并保留摄影窗口。',
  '下唇审计与最新实际进食/遮挡正反例分别见证据；嘴草接触、咀嚼、卧下和整体构图仍需人工复核。'),
 'E03': ([CONTENT,WORLD,SETUP], ['world_sync','world_async','progression'],
  'E2后可探索树与住处；松鼠指树、狐狸以树作路标并回地面住处，复用现有树木。',
  '无需读策划也能区分指路树、真正住处与临时歇脚点。'),
 'E04': ([CONTENT,LIFE+'World/LifeWorld.Demonstration.cs',LIFE+'Core/LifeLocalWeather.cs'], ['world_rules','world_sync','progression'],
  '固定8米/4秒自然风雨演示，含关闭阶段；羊风与鹿雨迁移强弱，演示不冒充玩家帮助或改变任务计数。',
  '真实观察无/弱/强的差异；玩家魔法与自然演示互斥反馈需最新运行确认。'),
 'E05': ([CONTENT,SETUP,OBJECT], ['world_rules','world_sync','world_async','progression'],
  '明亮冷点与遮阴暖位分开，玩家搬垫/邀请满足偏好；真实小棚提供遮光与背风形体。',
  '冷暖线索与动物反馈是否足以推断，不靠标签直接给词义。'),
 'E06A': ([CONTENT,LIFE+'Content/LifeCoastalAnimals.cs',LIFE+'World/LifeWorld.Coast.cs'], ['world_sync','progression'],
  '渡口新增固定羊鹿互换说话者与听者，照片保留原身份；与E6b并行开放，不挪走牧场角色。',
  '切换对话、镜头和照片回放后能分辨我/你，角色朝向足够清楚。'),
 'E06B': ([CONTENT,LIFE+'World/LifeWorld.Coast.cs',WORLD], ['world_sync','world_async','progression'],
  '渡口/潮池干岸与狐狸林地提供安全短程锚点；碰撞和原移动约束保持，早到潮池不需先完成E6a。',
  '实际键盘往返、指向和来去辨认；同步碰撞采样不代表完整步行动画通过。'),
 'E07': ([CONTENT,WORLD,LIFE+'World/LifeWorld.Drinking.cs',STATE], ['world_rules','world_async','progression'],
  '前序生活事件完成后开放；接取后集雨并淋湿垫，最终有水、垫干且两只不同附近动物到达，反序以终态判定。',
  '完整检查邀请还原、饮水接触、雨后恢复和结尾；最新异步结果见下方证据，不能只凭休息截图验收。'),
 'Q01': ([LIFE+'Editor/LifePlayReview.cs',LIFE+'Editor/LifeDialoguePlayChecks.cs',LIFE+'Editor/LifeWorldPlayChecks.cs'], ['core','dialogue','photo','photo_capture','photo_ui','ui','world_sync','progression','persistence','audio_play'],
  '核心、真实AI、物件、照片、手势与隔离存档检查入口已具备，失败记录分别保留。',
  '最新代码下从新档亲自走完E0—E2；不能将不同时间的局部PASS拼成一次完整试玩。'),
 'Q02': ([LIFE+'Editor/LifeAiReview.cs',LIFE+'Editor/LifeWorldAsyncChecks.cs',LIFE+'Editor/LifeProgressionChecks.cs'], ['photo_model','photo_world','photo_capture','photo_ui','photo_interactions','world_rules','world_async','progression','ui','audio_play'],
  '31词、22摄影+4互动的规则与正式场景录制、世界动作、音效、事件门槛和异步回归均有独立记录。',
  '核对最新UI、渐进门槛与世界异步结果，并实际组合验收错序探索、漏拍、断网和完整存档流程。'),
 'Q03': (['Docs/LifeGameplay/新玩家验收记录.md'], [],
  '已准备新玩家验收表；尚未记录用户或未读策划者的正式试玩结果。',
  '由用户亲自填写目标理解、词义迁移、操作完成、节奏与视觉结果；58—85分钟仍是设计假设。'),
}

def read_json(path):
    return json.loads(path.read_text(encoding='utf-8-sig'))

def clock_text(timestamp):
    return datetime.fromtimestamp(timestamp).astimezone().strftime('%Y-%m-%d %H:%M:%S %z')

def rel(path):
    return path.relative_to(ROOT).as_posix()

def evidence_specs():
    world_watch=[rel(p) for p in (ROOT/LIFE/'World').glob('*.cs')]
    photography_watch=[rel(p) for p in (ROOT/LIFE/'Photography').glob('*.cs')]
    audio_watch=[rel(p) for p in (ROOT/LIFE/'Audio').rglob('*.cs')]+[
        LIFE+'Audio/Resources/LifeAudio/Catalog.asset',
        LIFE+'Audio/Resources/LifeAudio/Clips/paper_move.wav',
        LIFE+'Audio/Resources/LifeAudio/Clips/camera_shutter.wav']
    ui_watch=[HUD]+[rel(p) for p in (ROOT/LIFE/'UI').rglob('*.cs')]
    perf_pointer=REVIEW/'performance/latest-run.txt'
    perf_path=Path(perf_pointer.read_text(encoding='utf-8-sig').strip()) if perf_pointer.exists() else REVIEW/'performance/missing'
    if not perf_path.is_absolute(): perf_path=ROOT/perf_path
    if perf_path.name!='report.json': perf_path=perf_path/'report.json'
    return {
      'core': ('核心状态回归',REVIEW/'core-checks.txt',[STATE,CORE,NOTEBOOK]),
      'scene': ('编辑器交还快照',REVIEW/'editor-state.json',[]),
      'dirty': ('原有修改保护',REVIEW/'dirty-preservation-latest.json',[]),
      'gesture': ('手势与书本资产检查',ROOT/'Docs/LifeGameplay/UI/gesture-checks.txt',[GESTURE]),
      'world_rules': ('世界规则检查',ROOT/'Docs/LifeGameplay/World/world-rule-checks.json',[OBJECT]),
      'world_sync': ('正式场景同步物件检查',REVIEW/'play/world-interactions.json',world_watch),
      'roaming': ('实际群落漫游与帮助接管',REVIEW/'play/roaming-play-result.txt',[WORLD,LIFE+'World/LifeWorld.Roaming.cs',LIFE+'Editor/LifeRoamingPlayChecks.cs','Assets/Game/World/Wildlife/Runtime/WildlifeAgent.cs']),
      'world_async': ('世界异步动作检查',REVIEW/'play/world-animation.json',world_watch+[LIFE+'Editor/LifeWorldAsyncChecks.cs']),
      'gathering': ('全图同类采集与通用物件',REVIEW/'play/gathering-report.json',world_watch+[HUD,LIFE+'Content/LifePlantSpecies.cs',LIFE+'Editor/LifeGatheringPlayChecks.cs']),
      'progression': ('事件推进与话轮门槛',REVIEW/'progression-checks.json',[STATE,CORE,CONTENT]),
      'persistence': ('独立实际存档检查',REVIEW/'play/persistence-play-result.txt',[STATE,CORE,GAME,NOTEBOOK,LIFE+'Core/LifeSaveFile.cs',LIFE+'Editor/LifePersistencePlayChecks.cs']),
      'pose': ('正式松鼠蒙皮与动作单位审计',ROOT/'Docs/LifeGameplay/World/squirrel-pose-audit.json',
               [rel(p) for p in (ROOT/LIFE/'Art/Resources/LifeAssets/Animations').glob('*.fbx')]+[LIFE+'Editor/LifeSquirrelPoseAudit.cs']),
      'sheep_pose': ('正式羊真正下唇锚点审计',REVIEW/'photo-flow/sheep-pose-audit.json',[LIFE+'World/LifeWorld.Feeding.cs']),
      'legacy_herd': ('既有JourneyHerd段数断言失败',REVIEW/'checks-existing-herd-failure.txt',[]),
      'ai': ('24字真实AI样本',REVIEW/'live-ai-56-pass.json',[AI]),
      'dialogue': ('最新正式HUD→真实AI→任务与照片整套',REVIEW/'play/dialogue-photo-checks.json',[GAME,AI,LIFE+'Editor/LifeDialoguePlayChecks.cs']),
      'dialogue_provider_history': ('历史真实AI帮助链独立通过记录',REVIEW/'play/dialogue-provider-pass.json',[GAME,AI]),
      'photo': ('正式照片与慢帧检查',REVIEW/'play/photo-only-checks.json',[PHOTO,NOTEBOOK]),
      'fresh_photos': ('四动物真实照片候选（仅隔离档）',REVIEW/'photo-replacement-staging/fresh-photos.json',[PHOTO,PHOTO_CAPTURE,LIFE+'Editor/LifeFreshPhotos.cs']),
      'photo_composition': ('四照构图复核（不计自动通过）',REVIEW/'photo-replacement-staging/visual-review.json',[]),
      'eating_snapshot': ('实际进食摄影正例快照',REVIEW/'photo-flow/actual-eating.json',photography_watch+world_watch),
      'eating_occluded': ('实际进食嘴部遮挡负例快照',REVIEW/'photo-flow/occluded-eating.json',photography_watch+world_watch),
      'photo_model': ('31词摄影奖励与迁移',REVIEW/'photo-flow/model-checks.txt',[NOTEBOOK,PHOTO_RULES]),
      'photo_world': ('正式模型的摄影可见性',REVIEW/'photo-flow/world-checks.json',photography_watch+world_watch),
      'photo_capture': ('实际相机首帧与Live保存',REVIEW/'photo-flow/capture-play-checks.txt',[PHOTO,NOTEBOOK,GAME]),
      'photo_ui': ('摄影任务与31词手账',REVIEW/'photo-flow/ui-play-checks.txt',ui_watch+[NOTEBOOK,PHOTO_BOOK]),
      'photo_interactions': ('四个真实公开互动与奖励',REVIEW/'photo-flow/interaction-play-checks.json',world_watch+photography_watch+[GAME,NOTEBOOK,LIFE+'Editor/LifePhotoInteractionChecks.cs']),
      'audio_assets': ('26段音效与地表映射',REVIEW/'audio-checks.txt',audio_watch),
      'audio_play': ('正式场景音效触发、清理与实际输出',REVIEW/'play/audio-play-result.txt',audio_watch+[LIFE+'Editor/LifeAudioPlayChecks.cs',LIFE+'Editor/LifeAudioAudibilityChecks.cs']),
      'ui': ('手账交互与生命周期',REVIEW/'play/ui-play-result.txt',ui_watch+[LIFE+'Editor/LifeUiPlayChecks.cs']),
      'assets': ('模型与贴图引用检查',ROOT/'Docs/LifeGameplay/Assets/asset-reference-check.json',[]),
      'performance': ('当前主场景性能诊断',perf_path,[]),
      'compile': ('最近离线编译',REVIEW/'offline-compile/summary.json',[]),
    }

def load_evidence(key,spec,output):
    label,path,watch=spec
    result={'id':key,'label':label,'sourcePath':str(path),'sourceUrl':path.as_uri(),'present':path.is_file(),
            'status':'MISSING','stale':False,'checks':0,'time':'未生成','boundary':'','error':'','detail':''}
    if not path.is_file(): return result
    result['modifiedEpoch']=path.stat().st_mtime;result['time']=clock_text(path.stat().st_mtime)
    raw=path.read_text(encoding='utf-8-sig')
    data=read_json(path) if path.suffix=='.json' else None
    if isinstance(data,dict):
        result['status']=data.get('status','RECORDED')
        result['boundary']=data.get('boundary','');result['error']=data.get('error','').splitlines()[0] if data.get('error') else ''
        result['checks']=len(data.get('checks',[]));result['recordedUtc']=data.get('utc','')
        if key=='ai': result['detail']=f"{len(data.get('results',[]))} 次真实请求；隔离词典确认 {data.get('confirmedWords',0)} 字"
        if key=='scene':
            result['status']='PASS' if not data.get('playing') and not data.get('dirty') and not data.get('compiling') else 'RECORDED'
            result['detail']=f"{data.get('scene','')}；Play={data.get('playing')}，Dirty={data.get('dirty')}。这是文件时间点的快照。"
        if key=='performance':
            result['detail']=data.get('gpuAvailability','')
            result['phases']=[{k:p[k] for k in ['name','sampledFrames','wallMs','mainMs','renderMs','gcBytes','gpuMs']} for p in data.get('phases',[])]
        if key=='assets': result['detail']=f"{len(data.get('models',[]))} 个FBX；{len(data.get('textures',[]))} 张运行时贴图及导入设置"
        if key=='pose': result['detail']=f"{len(data.get('poses',[]))} 次正式prefab隔离采样；原walk/run和六个新动作、两种缩放。验证蒙皮与口点，不代表世界动作混合或扣水流程。"
        if key=='sheep_pose':
            calibrated=[s['calibratedMuzzleDistance'] for s in data.get('samples',[]) if s.get('useScale') is True and 'calibratedMuzzleDistance' in s]
            result['detail']=f"{len(data.get('samples',[]))} 次正式羊预览采样；正确BakeMesh模式 {len(calibrated)} 次调用生产TryMouthPoint，比较实际下唇顶点5492/5570。最大距离 {max(calibrated):.6g} 米。" if calibrated else '未记录真正下唇距离，不能以旧selectedPoint代替。'
            result['detail']+=' 报告保留旧selectedPoint和旧boundary对照；几何审计不代表咀嚼、嘴草接触的人工视觉通过。'
        if key=='dialogue': result['detail']='03:08最新整套32项PASS，涵盖真实provider问候、含糊拒绝、过期回调、正确接任务、实际施雨及原说话者／锚点、话轮快门与磁盘重读。旧整体FAIL保留历史，不再替代新结果；实际站位／选话题为夹具，真人键盘与新玩家体验另验。'
        if key=='dialogue_provider_history': result['detail']='历史独立PASS，只证明其记录时间的帮助链；最新03:08整套32项PASS单独读取，不用旧记录替代。'
        if key=='fresh_photos': result['detail']=f"{len(data.get('shots',[]))} 段正式动物实际录制；文件保存在隔离目录 {data.get('saveDirectory','')}。PASS覆盖首帧主体可见及完整Live文件，不代表构图或全程主体留框通过。未写真实存档、未替换原四照，主体与帧数以逐张记录为准。"
        if key in {'eating_snapshot','eating_occluded'}: result['detail']='快照记录03:06真实forage、主体／嘴／食物可见性；正反例通过断言另看最新world_async，其57项及可见P22已PASS。快照本身无status，不据此生成PASS，也不代替人工嘴草接触与咀嚼观感验收。'
    elif isinstance(data,list):
        if key=='compile':
            result['status']='PASS' if all(x.get('ExitCode')==0 and x.get('ErrorCount')==0 for x in data) else 'FAIL'
            result['detail']='；'.join(f"{x.get('Assembly')}: {x.get('Sources')} 源文件 / {x.get('ErrorCount')} 错误 / {x.get('WarningCount')} 警告" for x in data)
    else:
        result['checks']=len(re.findall(r'^PASS\b',raw,re.M))
        result['status']='FAIL' if re.search(r'^FAIL\b',raw,re.M) else 'PASS' if result['checks'] or raw.startswith('PASS:') else 'RECORDED'
        if result['status']=='FAIL': result['error']=next((l for l in raw.splitlines() if l.startswith('FAIL')),'')
        if key=='roaming': result['detail']='03:02漫游专项PASS；24.01模拟秒，关键狗最远2.576877米、11只次级动物位移，闲时释放租约、Active帮助接管当前实际位置及解除归还通过。初始安全玩家站位属于夹具，真实动物不挪位；自然度与互动观感仍须人工。'
        if key=='persistence': result['detail']='独立persistence-only记录：隔离生活/词典文件连续十二次覆盖；独占锁失败逐字节保留旧档、平台替换与中文路径、真实阻塞写入经AI回调呈现并保留内存任务；不发送服务请求、不接触AI配置。以本记录状态及源码时间为准。'
        if key=='legacy_herd':
            result['status']='FAIL' if 'checks FAIL' in raw else result['status']
            result['error']=next((l for l in raw.splitlines() if 'InvalidOperationException' in l),'')
            result['detail']='旧JourneyHerdChecks要求变化前后段数相同；当前HEAD的JourneyRouteVariation按0.7米控制点压缩路段，与该断言不一致。保留失败记录，不归因于本轮新Move动作；也不宣称整套旧回归通过。'
    if key=='dirty': result['status'],result['detail']=summarize_dirty(data,ROOT)
    observed=[ROOT/p for p in watch if (ROOT/p).is_file()]
    if observed:
        latest=max(observed,key=lambda p:p.stat().st_mtime)
        result['latestSource']=rel(latest);result['latestSourceTime']=clock_text(latest.stat().st_mtime)
        result['stale']=latest.stat().st_mtime>path.stat().st_mtime+1
    target=output/'evidence'/(key+path.suffix)
    target.parent.mkdir(parents=True,exist_ok=True);shutil.copy2(path,target)
    result['snapshot']=target.relative_to(output).as_posix()
    if result['status']=='FAIL':
        history=output/'evidence/history';history.mkdir(exist_ok=True)
        stamp=datetime.fromtimestamp(path.stat().st_mtime).strftime('%Y%m%d-%H%M%S')
        archive=history/(key+'-'+stamp+path.suffix)
        if not archive.exists():shutil.copy2(path,archive)
    return result

def error_summary(item):
    error=item['error']
    if 'Actual mouth contact precedes measured water consumption' in error:
        return '饮水口部接触与扣水先后断言未通过。'
    if 'Restored invitations did not renew normally' in error:
        return '读档还原后邀请未正常续约。'
    if error and item['id']=='ui':return '手账交互检查失败，具体断言见原始记录。'
    if error and item['id']=='legacy_herd':return '既有路径段数断言与HEAD压缩规则不一致，尚未修订该旧测试。'
    return error

def evidence_label(item):
    if item['status']=='MISSING':return '待运行'
    if item['stale']: return '旧记录 '+item['status']+' · 最新待复测'
    return {'PASS':'通过（限定检查范围）','FAIL':'未通过','RECORDED':'已记录'}.get(item['status'],item['status'])

def file_link(path,label=None):
    p=Path(path)
    if not p.is_absolute():p=ROOT/p
    return '<a href="'+html.escape(p.as_uri(),quote=True)+'">'+html.escape(label or p.name)+'</a>'

def generate(output):
    output.mkdir(parents=True,exist_ok=True)
    audit=read_json(BASELINE)
    ids={t['id'] for t in audit['tasks']}
    if ids!=set(MAPPING):raise RuntimeError('Task mapping mismatch '+str(ids^set(MAPPING)))
    evidence={k:load_evidence(k,v,output) for k,v in evidence_specs().items()}
    tasks=[]
    for task in audit['tasks']:
        paths,keys,summary,manual=MAPPING[task['id']]
        for path in paths:
            if not (ROOT/path).is_file():raise RuntimeError('Missing implementation '+path)
        checks=[evidence[k] for k in keys]
        failed=[e for e in checks if e['status']=='FAIL' and not e['stale']]
        pending=[e for e in checks if e['status'] in {'MISSING','RECORDED'} or e['stale']]
        # A failed integration suite does not establish that every mapped feature failed.
        state='manual' if task['id']=='Q03' else 'blocked' if failed and task['id']=='Q02' else 'retest' if failed or pending else 'evidenced'
        tasks.append({**task,'baselineStatus':task['status'],'status':state,
            'statusLabel':{'manual':'用户待验','blocked':'检查未通过','retest':'已落盘 · 待复核','evidenced':'已有范围内证据'}[state],
            'implementation':summary,'files':[{'path':p,'url':(ROOT/p).as_uri()} for p in paths],
            'evidenceIds':keys,'manualAcceptance':manual,'humanStatus':'未验收',
            'pendingEvidence':[e['id'] for e in pending],'failedEvidence':[e['id'] for e in failed]})
    screenshots=[]
    choices=[('最新手账阅读画面',['play/ui-book-oblique-reading.png','play/02-journal.png'],'ui'),
             ('多张松页同时弯曲翻落',['play/ui-book-opening-fan-2.png','play/ui-book-oblique-opening.png','play/02a-book-opening.png'],'ui'),
             ('摄影任务未知字页面',['photo-flow/photography-unlearned.png'],'photo_ui'),
             ('31词第二页（隔离UI测试状态）',['photo-flow/photography-glyphs-2.png'],'photo_ui'),
             ('正式AI理解后出现任务',['play/05-real-ai-task.png'],'dialogue'),
             ('两只松鼠休息的已完成片段',['play/05-two-resting-squirrels.png'],'world_async')]
    for title,options,key in choices:
        source=next((REVIEW/p for p in options if (REVIEW/p).is_file()),None)
        if not source:continue
        dest=output/'images'/source.name;dest.parent.mkdir(exist_ok=True);shutil.copy2(source,dest)
        screenshots.append({'title':title,'path':dest.relative_to(output).as_posix(),'source':str(source),
            'time':clock_text(source.stat().st_mtime),'note':'仅支持截图中的状态；'+evidence_label(evidence[key])})
    git=lambda *a:subprocess.check_output(['git',*a],cwd=ROOT,text=True,encoding='utf-8').strip()
    counts={s:sum(t['status']==s for t in tasks) for s in ['evidenced','retest','blocked','manual']}
    report={'generatedAt':datetime.now().astimezone().isoformat(),'repository':str(ROOT),'branch':git('branch','--show-current'),
      'head':git('rev-parse','--short','HEAD'),'baseline':str(BASELINE),'baselineThread':audit['baselineThread'],
      'scope':'原39项任务、3D旧手账修订、全图同类采集、26段音效及31词摄影玩法修订（22摄影+4互动）。',
      'conclusion':'这是实现与证据映射，不能视为39项全部验收通过。Q03始终由用户实际试玩完成。',
      'counts':counts,'evidence':evidence,'tasks':tasks,'screenshots':screenshots,
      'photographyTasks':read_json(DESKTOP/'拍照与文字玩法修订_20261026/任务清单.json'),
      'refreshCommand':'python LifeGameplayReview/20261026/generate_acceptance_report.py',
      'freshnessRule':'证据晚于所列相关源码才能代表当前版本；旧PASS/FAIL保留原结果并标待复测。该时间规则是保守提示，最终仍需核对检查范围。',
      'latestDecisions':audit['latestUserDecisions']+[
          'J手账真实3D厚旧书、弯曲翻页、书沟、纸纤维与封皮布纹；四页签同排。未拍到绿色、拍到蓝色、已知黄色，绿色描边仅在选中时出现，无呼吸光。',
          '字格底部不再常驻中文；悬停已知字时，右上方复用动物聊天气泡展示字义。03:29:30已在隔离档完成狗/松鼠/狐狸/鹿四段18帧真实候选；尚未把候选替换进真实存档，原识字进度不因准备候选改变。',
          '最新书本采用开合斜视与整书淡入淡出，透明输入命中须随相机投影；以最新UI运行记录为准。',
          '书本裁切、普通翻页页签常亮及条纹修复；四张独立薄页在封面打开时松开，再错时翻落，前导维持左侧四张未拍照占位图与右侧字形学习格，最后一张才露出选中的真实目标页。',
          '2026-10-27 00:02:11主Unity整套UI回归PASS且无运行时错误；照片、手势、任务三类目标的固定前导与目标各捕获一次，前导像素一致；动画中不再捕获、解码照片或重建页面，Notebook与生活进度不变。禁用颜色快照、输入互斥及资源释放也通过；连续动画观感和新玩家操作仍待人工验收。',
          '动物气泡字形按不含尖尾的主体垂直居中；已确认的中文释义在气泡上方用白色显示，未知字不显示释义。00:02:11整套UI回归包含真实气泡截图像素、已知/未知切换及生态气泡不能冒充未显示生活话语的归属检查；23:42禁用按钮浅色复现断言的旧失败已由本轮完整PASS覆盖。',
          '摄影修订以31个完整词为准，想要/不想要已知；照片和公开互动直接奖励短词义，AI仍负责帮助理解。',
          '正式羊下唇审计在00:14:26通过60次预览采样，正确BakeMesh模式30次直接核对生产口点与真正下唇顶点；几何通过与实际进食摄影正反例、人类咀嚼观感分别记录。03:06最新世界57项整套PASS，包含真实共同饮水可见P22与进食／嘴部遮挡正反例；旧P22和E7夹具失败保留历史，人工构图与动作观感仍未验收。',
          '03:08最新真实AI／对话整套32项PASS，03:10摄影整套PASS、03:12公开互动46项PASS。03:02漫游在24.01模拟秒观察到关键狗最远位移2.576877米与11只次级动物移动，并通过Active帮助接管／解除归还。各套件保留隔离夹具和时间，不拼成完整新档试玩。',
          '03:14最新UI采图53张、PASS_WITH_GAPS；采用短句／长句回声及退出G后的世界圈3张，原53图与早批2补图保留，共58张实际PNG。U22新手专用UI未实现，IME、变体、夜晚、其他分辨率与真人滚动仍有明确缺口。',
          '脚步按草地/泥土/石头复用素材；动物原创音色制作工具不对玩家开放，松鼠/狐狸/鹿明亮值100。',
          '生活帮助按原事件推进，摄影互动允许先到当地开始；已理解或已有任务可回访。H为当前生活阶段观察提示的显示/隐藏。']}
    (output/'验收映射.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8')
    render_html(report,output)
    print(json.dumps({'html':str(output/'天气屋_玩法实现验收.html'),'json':str(output/'验收映射.json'),'counts':counts,'tasks':len(tasks)},ensure_ascii=False))

def render_html(report,output):
    esc=html.escape;ev=report['evidence'];counts=report['counts']
    cards=[]
    for t in report['tasks']:
        source_links=' · '.join(file_link(f['path']) for f in t['files'])
        evidence_links=''.join('<li><a href="'+esc(ev[k].get('snapshot',ev[k]['sourceUrl']),quote=True)+'">'+esc(ev[k]['label'])+'</a><span>'+esc(evidence_label(ev[k]))+'</span></li>' for k in t['evidenceIds']) or '<li>等待用户实际试玩与填写记录。</li>'
        criteria=''.join('<li>'+esc(x)+'</li>' for x in t['acceptance'])
        cards.append(f'''<article class="task" data-id="{t['id']}" data-group="{esc(t['group'])}" data-status="{t['status']}">
<header><span class="id">{t['id']}</span><h3>{esc(t['title'])}</h3><span class="badge {t['status']}">{esc(t['statusLabel'])}</span></header>
<p>{esc(t['implementation'])}</p><div class="task-columns"><div><h4>实现文件</h4><p class="files">{source_links}</p><h4>已运行证据</h4><ul class="evidence-list">{evidence_links}</ul></div><div class="manual"><h4>人工验收 · 未验</h4><p>{esc(t['manualAcceptance'])}</p><details><summary>原任务的验收标准</summary><ol>{criteria}</ol></details></div></div></article>''')
    evidence_rows=[]
    for e in ev.values():
        detail=e['detail'] or (str(e['checks'])+' 项检查' if e['checks'] else '')
        error=('<p class="error">'+esc(error_summary(e))+'</p>') if e['error'] else ''
        stale=('<small>相关源码最近变化：'+esc(e['latestSourceTime'])+'</small>') if e['stale'] else ''
        evidence_rows.append('<tr><td>'+file_link(e['sourcePath'],e['label'])+'</td><td>'+esc(evidence_label(e))+'</td><td>'+esc(e['time'])+stale+'</td><td>'+esc(detail)+error+'</td></tr>')
    figures=''.join('<figure><a href="'+s['path']+'"><img src="'+s['path']+'" loading="lazy" alt="'+esc(s['title'])+'"></a><figcaption><b>'+esc(s['title'])+'</b><small>'+esc(s['time'])+'</small><p>'+esc(s['note'])+'</p></figcaption></figure>' for s in report['screenshots'])
    groups=''.join('<option>'+esc(g)+'</option>' for g in dict.fromkeys(t['group'] for t in report['tasks']))
    perf=ev['performance'];performance=''
    if perf.get('phases'):
        rows=''.join('<tr><td>'+esc(p['name'])+'</td><td>'+str(p['sampledFrames'])+'</td><td>'+f"{p['wallMs']['mean']:.2f}"+'</td><td>'+f"{p['wallMs']['p95']:.2f}"+'</td><td>'+f"{p['gpuMs']['mean']:.2f}"+'</td></tr>' for p in perf['phases'])
        performance=f'<h2>性能与照片记录</h2><p>当前水岸主图的固定视角 A/B/A 诊断：20 秒原反射、10 秒关闭、10 秒恢复，切换前后预热排除。{esc(perf["detail"])} 渲染线程记录不可得。此结果不是旧水版本的 FPS 对比，Editor 与后台负载仍会影响间隔。</p><div class="table-scroll"><table><thead><tr><th>阶段</th><th>帧样本</th><th>平均帧间隔 ms</th><th>P95 ms</th><th>平均 GPU ms</th></tr></thead><tbody>{rows}</tbody></table></div><p class="muted">动态照片保留18帧；正式照片检查包含180ms慢捕获注入，逐Unity帧采样上限为1，避免同帧追赶。读回、编码与写盘仍有同步代价，具体耗时见照片证据。</p>'
    pending=[e for e in ev.values() if e['id'] in {'ui','world_async','photo_world','photo_capture','photo_ui','photo_interactions','audio_play','persistence','legacy_herd'}]
    pending_html=''.join('<li><b>'+esc(e['label'])+'</b>：'+esc(evidence_label(e))+(('。'+esc(error_summary(e))) if e['error'] else '')+'</li>' for e in pending)
    photo_rows=''.join('<tr><td>'+esc(t['id'])+'</td><td>'+esc(t['title'])+'</td><td>'+esc(t['mode'])+'</td><td>'+esc(' / '.join(t['rewards']))+'</td><td>'+esc(t['accept'])+'</td></tr>' for t in report['photographyTasks'])
    photo_section='<h2>追加修订 · 31词摄影与音效</h2><p>下表为批准的22个摄影任务与4个公开互动情境。29个可获取词均有奖励路径；想要／不想要初始已知。已知词不作为拍照门槛，AI只负责动物帮助理解。原39项中的旧跨动物确字要求已被本次修订替代。</p><p>'+file_link('Docs/LifeGameplay/摄影修订验收.md','摄影11个案例与证据边界')+' · '+file_link('Tools/LifeAudio/README.md','音效接入与试听说明')+' · '+file_link('Tools/LifeAudio/VoiceDesigner/index.html','动物音色制作工具')+'</p><p>共26段音频，三类脚步各2段，交互按动作复用素材；五种动物使用原创短音节。松鼠、狐狸、鹿明亮度均为100。音效自动检查验证触发、停止和地表选择，音色与响度留待实际试听。</p><details><summary>展开26个任务的最终验收条件</summary><div class="table-scroll"><table><thead><tr><th>ID</th><th>任务</th><th>方式</th><th>奖励ID</th><th>验收条件</th></tr></thead><tbody>'+photo_rows+'</tbody></table></div></details>'
    page='''<!doctype html><html lang="zh-CN"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>天气屋 · 玩法实现验收</title><style>
:root{--ink:#233c37;--muted:#5c6c65;--green:#285749;--line:#d7dfd3;--paper:#f4f2e9;--amber:#976722}*{box-sizing:border-box}body{margin:0;background:var(--paper);color:var(--ink);font:16px/1.7 system-ui,"Microsoft YaHei",sans-serif}a{color:#276656;text-underline-offset:3px}a:hover{color:#844e24}main{max-width:1240px;margin:auto;padding:0 28px 64px}.hero{padding:50px 0 28px;border-bottom:2px solid #9eaf99}.eyebrow{font-size:13px;letter-spacing:.15em;color:var(--green)}h1{font-size:42px;letter-spacing:.04em;margin:7px 0 14px;font-weight:650}h2{font-size:25px;margin:36px 0 14px}h3{font-size:19px;margin:0}h4{font-size:13px;color:var(--muted);margin:12px 0 6px}.hero p{max-width:930px;margin:10px 0}.meta,small,.muted{color:var(--muted);font-size:13px}.meta{line-height:1.9}.summary{display:grid;grid-template-columns:repeat(4,1fr);gap:12px;margin:24px 0}.metric{border:1px solid var(--line);background:#fffdf6;padding:16px 18px}.metric b{display:block;font-size:30px;line-height:1.25}.metric span{font-size:14px}.notice{padding:18px 24px;background:#ede6d4;border-left:4px solid #a77b35}.notice p{margin:5px 0}.notice ul{margin:8px 0;padding-left:20px}.controls{display:flex;gap:10px;align-items:center;position:sticky;top:0;background:#f4f2e9f7;padding:16px 0;z-index:2;border-bottom:1px solid var(--line)}input,select{border:1px solid #aebdac;border-radius:4px;background:#fffdf6;padding:10px 12px;font:inherit;color:var(--ink)}input{flex:1;min-width:150px}label{font-size:13px}.task{background:#fffdf7;border:1px solid var(--line);border-left:4px solid #8fa68b;margin:16px 0;padding:20px 23px}.task[hidden]{display:none}.task header{display:flex;gap:12px;align-items:center;flex-wrap:wrap}.id{font:700 14px/1.3 ui-monospace,monospace;color:var(--green);min-width:48px}.badge{margin-left:auto;padding:3px 9px;border-radius:3px;font-size:12px;white-space:nowrap}.evidenced{background:#e1ecd9;color:#365b37}.retest{background:#f4e8cf;color:#865d24}.blocked{background:#f2d9d1;color:#9a4035}.manual{background:#f2f1e8}.badge.manual{background:#e6e4f0;color:#5b5477}.task>p{margin:12px 0}.task-columns{display:grid;grid-template-columns:1.05fr 1fr;gap:24px}.task-columns>.manual{padding:0 16px 13px}.files{font-size:13px;overflow-wrap:anywhere;margin:0}.evidence-list{list-style:none;padding:0;margin:0;font-size:13px}.evidence-list li{padding:4px 0;border-bottom:1px dotted #dde1d6}.evidence-list span{display:block;color:var(--muted);font-size:12px}.task details{font-size:13px}.task summary{cursor:pointer;color:#4f6e59}.task details ol{padding-left:20px}.shots{display:grid;grid-template-columns:1fr 1fr;gap:20px}figure{margin:0;background:#fffdf6;border:1px solid var(--line)}figure img{display:block;width:100%;aspect-ratio:16/10;object-fit:contain;background:#e4e5da}figcaption{padding:13px 17px}figcaption small{display:block}figcaption p{font-size:13px;margin:5px 0}.table-scroll{overflow:auto}table{width:100%;border-collapse:collapse;font-size:13px;background:#fffdf7}th,td{text-align:left;vertical-align:top;border-bottom:1px solid var(--line);padding:12px}th{background:#e9eddf;font-weight:600}td small{display:block;min-width:165px}.error{font-size:12px;color:#9a4035;max-width:420px;overflow-wrap:anywhere;margin:4px 0}footer{border-top:1px solid var(--line);margin-top:35px;padding-top:18px;font-size:13px}.links{display:flex;gap:18px;flex-wrap:wrap}button{font:inherit;border:1px solid #99aa94;background:#e4ebdc;color:var(--ink);border-radius:4px;padding:9px 13px;cursor:pointer}@media(max-width:760px){main{padding:0 16px 35px}.hero{padding:28px 0 18px}h1{font-size:31px}.summary{grid-template-columns:1fr 1fr}.controls{flex-wrap:wrap}.controls input{flex-basis:100%}.task-columns,.shots{grid-template-columns:1fr}.task{padding:16px}.badge{margin-left:0}.task-columns{gap:10px}.controls select{max-width:47%}}@media print{.controls{display:none}.task{break-inside:avoid}.task[hidden]{display:block}.shots{display:none}a{color:inherit}body{background:white}}
</style></head><body><main>'''
    page+=f'''<header class="hero"><div class="eyebrow">WEATHER HOUSE / IMPLEMENTATION REVIEW</div><h1>天气屋 · 玩法实现验收</h1><p>{esc(report['conclusion'])}</p><p class="meta">刷新于 {esc(report['generatedAt'])}<br>分支 {esc(report['branch'])} · 当前提交 {esc(report['head'])} · 正式入口 WeatherHouse_WatersideV2<br>原策划任务39项。原清单的“缺失/部分实现”保留在JSON中作为基线，不代表当前状态。</p><div class="links"><a href="#tasks">逐项验收</a><a href="#evidence">查看证据时间</a><a href="验收映射.json">下载JSON映射</a>{file_link('Docs/LifeGameplay/新玩家验收记录.md','打开新玩家验收表')}</div></header>
<section class="summary" aria-label="任务状态统计"><div class="metric"><b>39</b><span>原任务逐项映射</span></div><div class="metric"><b>{counts['evidenced']}</b><span>已有范围内证据</span></div><div class="metric"><b>{counts['retest']+counts['blocked']}</b><span>待复核 / 检查未通过</span></div><div class="metric"><b>Q03</b><span>用户实际试玩待验</span></div></section>
<aside class="notice"><b>关键检查结果</b><ul>{pending_html}</ul><p>“已有范围内证据”只表示所列自动检查支持对应结论，人工验收栏均未预填通过。旧记录不覆盖最新源码，也不能把不同时间的局部通过拼成一次完整新档试玩。</p></aside>
<h2>当前可用画面</h2><p class="muted">截图按实际文件时间展示。最新斜视/淡入淡出画面生成后，刷新脚本会自动优先采用；松鼠截图仅证明当时休息片段。</p><section class="shots">{figures}</section>
<h2 id="tasks">39项任务与验收边界</h2><div class="controls"><input id="search" type="search" aria-label="搜索任务" placeholder="搜索编号、玩法或文件名"><select id="group" aria-label="按任务分组筛选"><option value="">全部分组</option>{groups}</select><select id="status" aria-label="按验证状态筛选"><option value="">全部状态</option><option value="evidenced">已有范围内证据</option><option value="pending">待复核 / 未通过</option><option value="manual">用户待验</option></select><span id="shown" class="muted" aria-live="polite">39 / 39</span></div><section id="taskList">{''.join(cards)}</section>
<h2 id="evidence">证据索引与时间</h2><p class="muted">点击证据可读原始结果；任务卡中的链接为本次报告快照。{esc(report['freshnessRule'])} 编辑器交还状态是记录时间点的快照，并非此刻的编辑器状态。</p><div class="table-scroll"><table><thead><tr><th>证据</th><th>记录状态</th><th>文件时间</th><th>范围 / 结果</th></tr></thead><tbody>{''.join(evidence_rows)}</tbody></table></div>{performance}
<h2>建议实际验收顺序</h2><ol><li>新档从院门问候开始，自己解释动物的话；错误不接任务，理解正确后才帮助。</li><li>J练雨、G施雨、接水停雨，再采草编垫，检查近距离、圈边界和中断恢复。</li><li>自然探索林地、牧场与海岸；E6a/b可交换。拍照、记猜测、跨动物找证据，再学新手势。</li><li>前序生活事件完成后回河岛，实际观察两只动物到达、休息、饮水与误淋湿后的恢复。</li><li>验收书本外观、翻页、输入、照片与淡入淡出；记录卡住的位置，再退出重进检查存档。</li></ol>
<footer><p>新增模型、原骨架派生动作、材质与Blender预览：{file_link('Docs/LifeGameplay/Assets/README.md','资产说明')} · {file_link('Docs/LifeGameplay/Assets/Journal/preview.html','实体书预览')}。实现提交与验证状态分别记录，以本页顶部提交号和证据时间为准。</p><p>新增只读调查：{file_link(str(DESKTOP/'草地闪烁与项目体积排查_20261026/排查报告.html'),'编辑器草地闪烁与工程体积排查')}。候选资源尚未删除，未把静态依赖大小当作实际 Player 包体。</p><p>生成器：{file_link(str(Path(__file__).resolve()))}<br>刷新命令 <code>{esc(report['refreshCommand'])}</code>。报告只读取明确列出的证据与源码时间，不读取AI配置。</p></footer></main>
<script>const tasks=[...document.querySelectorAll('.task')],search=document.getElementById('search'),group=document.getElementById('group'),status=document.getElementById('status');function filter(){{let n=0;const q=search.value.trim().toLocaleLowerCase();for(const t of tasks){{const ok=(!q||t.textContent.toLocaleLowerCase().includes(q))&&(!group.value||t.dataset.group===group.value)&&(!status.value||(status.value==='pending'?['retest','blocked'].includes(t.dataset.status):t.dataset.status===status.value));t.hidden=!ok;if(ok)n++;}}document.getElementById('shown').textContent=n+' / '+tasks.length;}}[search,group,status].forEach(x=>x.addEventListener('input',filter));</script></body></html>'''
    page=page.replace('<h2 id="tasks">',photo_section+'<h2 id="tasks">',1)
    page=page.replace('拍照、记猜测、跨动物找证据，再学新手势。','自由拍照，直接获得任务词义，回看公开互动，再学新手势。')
    (output/'天气屋_玩法实现验收.html').write_text(page,encoding='utf-8')

if __name__=='__main__':
    parser=argparse.ArgumentParser();parser.add_argument('--output',type=Path,default=DESKTOP/'玩法实现验收_20261026')
    generate(parser.parse_args().output)
