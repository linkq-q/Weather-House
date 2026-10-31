"""Create the local review from actual rain and expression test evidence."""
import argparse
from pathlib import Path
import re
from build_fix_report import Report, safe, read, checks, badge

p = argparse.ArgumentParser()
p.add_argument('--root', type=Path, required=True)
p.add_argument('--run', required=True)
a = p.parse_args()
root = a.root.resolve()
run = root / a.run
r = Report(root)
contracts = read(run / 'contracts.txt')
observations = read(run / 'observations.txt')
saves = read(run / 'save-protection.txt')
glyphs = read(root / 'expressions/checks.txt')
complete = ('RESULT contract groups failed=0' in contracts and 'failures=0; subjects=2' in observations
            and 'Saved scene unchanged; Play=False' in saves
            and not any(mark == 'FAIL' for mark, _ in checks(contracts + observations + saves + glyphs)))

def evidence(text, filename):
    return '<details><summary>查看断言和原始记录</summary><pre>' + safe(text) + '</pre>' + r.link(filename) + '</details>'

names = {'Heart': '爱心 · 亲近', 'Happy': 'hh · 开心', 'Cry': 'qq · 哭泣（预留）',
         'Anger': '怒筋 · 三条弧线（预留）', 'Question': '问号（预留）',
         'Exclamation': '叹号 · 受惊', 'Sweat': '汗滴 · 紧张', 'Music': '音符 · 放松'}
icons = []
for name, label in names.items():
    path = root / 'expressions' / f'emotion-0-{name}.png'
    icons.append('<figure><img src="' + r.href(path) + '" alt="' + safe(label) + '"><figcaption>' + safe(label) + '</figcaption></figure>')

stage_titles = {'outside': '玩家带雨走近前：动物在雨圈外', 'entered': '走近后：当前雨圈已覆盖动物',
                'weak-response': '弱雨：本次进入只抽一次避雨意愿', 'strong-response': '同一场雨升级为强雨：紧急避雨移动',
                'left': '玩家带走雨圈：离圈并释放避雨目标'}
all_glyphs = [root / 'expressions' / f'emotion-{camera}-{name}.png' for camera in (0, 1) for name in names]
all_glyphs += [root / 'expressions' / name for name in ('emotions-camera-0.png', 'emotions-camera-1.png', 'sleep-priority.png')]
required = [run / name for name in ('contracts.txt', 'observations.txt', 'save-protection.txt')]
required += [root / 'expressions' / name for name in ('checks.txt', 'visual-review.txt')]
required += [root / 'gameplay-visual-review.txt']
required += all_glyphs + [run / f'animal{number}-{stage}.png' for number in (1, 2) for stage in stage_titles]
missing = [str(path) for path in required if not path.is_file()]
if missing or not complete:
    raise SystemExit(f'Incomplete acceptance evidence: complete={complete}; missing={missing}')
for path in required:
    if path.suffix == '.png':
        pixels = path.read_bytes()
        if not pixels.startswith(b'\x89PNG\r\n\x1a\n') or not pixels.endswith(b'IEND\xaeB`\x82'):
            raise SystemExit('Invalid PNG: ' + str(path))
scenes = []
subject_ids = re.findall(r'\bSUBJECT (.+)', observations)
for number in (1, 2):
    subject_id = subject_ids[number - 1].strip()
    species = subject_id.split('/')[-2]
    subject_label = f'动物 {number}（' + {'sheep': '羊', 'deer': '鹿'}.get(species, species) + '）'
    images = [run / f'animal{number}-{stage}.png' for stage in stage_titles]
    titles = {f'animal{number}-{stage}.png': f'{subject_label} · {title}' for stage, title in stage_titles.items()}
    scenes.append(f'<h3>{safe(subject_label)}</h3>' + r.gallery(images, titles))

events = '\n'.join(line for line in observations.splitlines() if re.search(r'\b(PASS|FAIL|EVENT|CAST|RESULT)\b', line))
movement_delays = re.findall(r'Subject (\d+) strong-rain movement begins within10s of upgrade; actual=([\d.]+)s', observations)
timing = '；'.join(f'动物 {subject}：{float(seconds):.2f} 秒' for subject, seconds in movement_delays)
html = '''<!doctype html><html lang="zh-CN"><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1">
<title>动物雨区与表情复测</title><style>
*{box-sizing:border-box}body{margin:0;background:#f3f2ed;color:#28332f;font:16px/1.7 system-ui,"Microsoft YaHei",sans-serif}main{max-width:1180px;margin:auto;padding:36px 24px 72px}h1{font-size:32px;margin:8px 0}h2{font-size:24px;margin-top:38px}h3{font-size:19px}.intro{max-width:900px;color:#52625b}section{background:#fff;border:1px solid #dde2da;border-radius:14px;padding:24px;margin-top:22px}.badge{display:inline-block;border-radius:6px;padding:2px 9px;background:#e7ece8;font-size:14px}.pass{color:#285b3b;background:#e1f0e3}.fail{color:#a33929;background:#fde8df}.pending{background:#fff0cc}.icons{display:grid;grid-template-columns:repeat(4,1fr);gap:12px}.gallery{display:grid;grid-template-columns:repeat(2,1fr);gap:16px}figure{margin:0}img{width:100%;display:block;border-radius:8px;background:#242d31}figcaption{font-size:14px;margin:8px 0 18px}small{display:block;color:#718078}a{color:#306c58}pre{white-space:pre-wrap;overflow-wrap:anywhere;font-size:12px;line-height:1.6;max-height:440px;overflow:auto;background:#eff2ee;padding:14px}details{margin-top:16px}table{border-collapse:collapse;width:100%;font-size:15px}td,th{padding:10px;text-align:left;border-bottom:1px solid #e4e7e1}.note{color:#616b65;font-size:14px}@media(max-width:700px){.icons{grid-template-columns:repeat(2,1fr)}.gallery{grid-template-columns:1fr}main{padding:20px 14px}section{padding:16px}}
</style><main>'''
html += '<p class="note">天气屋 · 2026-10-30 · 独立工程复测</p><h1>移动雨圈、避雨强度与动物表情</h1>'
html += '<p class="intro">普通下雨原先被“强度至少为 2”的条件挡住。本次统一按当前雨圈覆盖范围感知雨强，并按确认的弱雨／强雨规则决定避雨；表情由本地事件和稳定个体性格控制。</p>'
html += '<section><h2>1. 雨圈与行为 ' + badge(checks(contracts + observations + saves), complete) + '</h2>'
if timing:
    html += '<p><strong>本轮强雨后开始避雨移动：' + safe(timing) + '。</strong>两只均满足新增的 10 秒内启动检查。</p>'
if (run / 'route-before-checks.txt').is_file() and (run / 'route-after-checks.txt').is_file():
    html += '<p>此前实景中鹿等了约 46 秒才开始避雨。已优化绕障后的分段通行检查，并让多个避雨候选轮流搜索，避免困难目标独占寻路；固定 28 米窄障碍用例由 373 次查询降到 30 次，仍逐段验证身体能否通过。'
    html += r.link(run / 'route-before-checks.txt', '优化前记录') + ' · ' + r.link(run / 'route-after-checks.txt', '优化后记录') + '。</p>'
html += '<table><tr><th>情况</th><th>现在的规则</th></tr><tr><td>弱雨</td><td>每次进入雨区，独立抽取一次 50% 的避雨意愿；留在同一场雨中不会反复重抽。</td></tr><tr><td>强雨</td><td>可自主行动的动物全部进入避雨流程，普通动物使用已有跑速，结伴成员使用 1.6 倍步速。</td></tr><tr><td>弱雨变强雨</td><td>同一暴露过程升级为必须避雨；找到可达路线后奔向遮蔽，已在避雨途中的会加速。</td></tr><tr><td>离圈／雨停</td><td>释放避雨任务，恢复有效的原活动。路径、碰撞和玩家交互优先级仍然有效。</td></tr></table>'
html += '<p class="note">实景来自保存场景的独立副本 Play。雨圈使用正常 8 米半径，测试脚本连续移动玩家，经过实际 Cast／Apply 入口；它验证雨圈和动物反应，不验证键盘控制器路径。截图使用专用取证相机。网络请求关闭，AI 不是本地避雨和表情的前提。</p>'
html += '<p class="note">第二组的目标是鹿，强雨图中左侧跑远的羊不是该组目标。响应时间、加速与任务释放由连续日志证明；单张截图只显示取证瞬间，不能证明已抵达遮蔽。' + r.link(root / 'gameplay-visual-review.txt', '10 张实景审图记录') + '</p>'
html += ''.join(scenes) + evidence(events, run / 'observations.txt') + '</section>'
html += '<section><h2>2. 八种表情 ' + badge(checks(glyphs)) + '</h2><p>开心为小写 hh，哭泣为小写 qq。叹号主体加粗为原线宽的 1.7 倍，圆点为 2 倍。白色、粗圆、朝向当前镜头，沿用 Z 的漂浮节奏。</p><div class="icons">' + ''.join(icons) + '</div>'
html += '<p class="note">上图是独立 Unity 特效预览，不是自然触发频率的实景证明。每图同时显示同一轮中的三个漂浮符号。淡出描边交接处仍可能有轻微透明叠加亮点。</p>'
html += r.gallery([root / 'expressions/emotions-camera-1.png', root / 'expressions/sleep-priority.png'], {'emotions-camera-1.png': '更高的侧面相机角度：仍正面朝向相机', 'sleep-priority.png': '睡眠优先：普通表情隐藏，保留原有 Z'})
html += '<details><summary>全部原始表情图（19 张）与审图记录</summary><ul>' + ''.join('<li>' + r.link(path) + '</li>' for path in all_glyphs) + '</ul>' + r.link(root / 'expressions/visual-review.txt') + '</details>'
html += '<table><tr><th>触发</th><th>频率控制</th></tr><tr><td>主动亲近／跟随并实际靠近后→爱心；打招呼／社交→hh；受惊→叹号；逃离→汗滴；安静休息→音符</td><td>性格影响概率。普通表情每个体间隔 45～90 秒；同一种至少隔 120 秒。每次只播一轮约 2.4 秒。</td></tr><tr><td>qq、怒筋、问号</td><td>显示接口已做好，当前不自动触发。</td></tr><tr><td>睡眠 Z</td><td>睡眠期间持续显示，其它短时表情都不显示。</td></tr></table>' + evidence(glyphs, root / 'expressions/checks.txt') + '</section>'
html += '<section><h2>3. 动画与验证范围</h2><p>狐狸和当前 OGA 松鼠已有完整趴下／睡眠／起身片段，本次检查实际绑定与播放过程。其它缺少完整睡眠动画的物种按你的要求暂不新增动画，仍会静止并显示 Z。</p>'
html += evidence(contracts, run / 'contracts.txt') + evidence(saves, run / 'save-protection.txt')
if (root / 'main-editor-verification.txt').is_file():
    html += '<p>主 Unity 已导入最新脚本，保持编辑模式，场景没有未保存修改，保存的场景文件哈希未变。' + r.link(root / 'main-editor-verification.txt', '主工程核对记录') + '</p><p class="note">编辑器窗口重绘仍显示既有 RenderTexture／Camera stack 报错，本轮未处理该项，也未声称主工程 Console 零错误。</p>'
html += '<p class="note">概率统计和同路径速度对照属于可重复测试；它们不保证每五只动物正好有固定数量避雨，也不代表整场景性能基准。避雨需找到可达遮蔽处，不会绕过碰撞或瞬移。</p></section></main></html>'
out = root / '动物雨区与表情复测报告.html'
out.write_text(html, encoding='utf-8')
missing = [link for link in r.linked if not (root / link).is_file()]
if missing:
    raise SystemExit('Missing evidence: ' + str(missing))
if re.search(r'sk-[A-Za-z0-9_-]{12,}', html):
    raise SystemExit('Unexpected credential-like text')
print(str(out))
print(f'Linked evidence files: {len(r.linked)}; all present; report complete={complete}')
