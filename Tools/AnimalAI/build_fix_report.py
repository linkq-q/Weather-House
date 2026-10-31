"""Build an offline review page from completed Unity evidence, without inventing passes."""
from __future__ import annotations

import argparse
import html
import json
from pathlib import Path
import re
from urllib.parse import quote


DEFAULT_ROOT = Path(r"C:\Users\31483\Desktop\天气屋\动物行为修复复测_20261030")
CHECK = re.compile(r"(?:^|\s)(PASS|FAIL)\s+(.+)")
SECRET = re.compile(r"sk-[A-Za-z0-9_-]{12,}")


def safe(value):
    return html.escape(SECRET.sub("[已隐藏]", str(value)), quote=True)


def read(path):
    return path.read_text(encoding="utf-8-sig", errors="replace") if path.is_file() else ""


def checks(text):
    return [(m.group(1), m.group(2)) for line in text.splitlines() if (m := CHECK.search(line))]


def state(rows, complete=True):
    if any(mark == "FAIL" for mark, _ in rows):
        return "fail", "未通过"
    if rows and complete:
        return "pass", "断言通过"
    return "pending", "证据未齐"


def badge(rows, complete=True):
    css, text = state(rows, complete)
    return f'<span class="badge {css}">{text}</span>'


class Report:
    def __init__(self, root):
        self.root = root.resolve()
        self.linked = set()

    def href(self, path):
        rel = path.resolve().relative_to(self.root).as_posix()
        self.linked.add(rel)
        return quote(rel)

    def link(self, path, title=None):
        if not path.is_file():
            return f'<span class="muted">尚无 {safe(title or path.name)}</span>'
        return f'<a href="{self.href(path)}" target="_blank">{safe(title or path.name)}</a>'

    def gallery(self, paths, titles=None):
        titles = titles or {}
        images = [path for path in paths if path.is_file()]
        if not images:
            return '<p class="missing">这一项还没有截图，不作为已完成验收。</p>'
        cards = []
        for path in images:
            url = self.href(path)
            title = titles.get(path.name, path.stem)
            cards.append(f'<figure><a href="{url}" target="_blank"><img src="{url}" alt="{safe(title)}" loading="lazy"></a>'
                         f'<figcaption>{safe(title)} <small>{safe(path.name)}</small></figcaption></figure>')
        return '<div class="gallery">' + ''.join(cards) + '</div>'

    def evidence(self, rows, files, label="断言与原始记录"):
        table = ''.join(f'<li class="{mark.lower()}"><b>{mark}</b> {safe(text)}</li>' for mark, text in rows)
        if not table:
            table = '<li>尚未找到 PASS / FAIL 断言。</li>'
        links = ' · '.join(self.link(path) for path in files if path.is_file())
        return f'<details><summary>{safe(label)} · {len(rows)} 条</summary><ul class="checks">{table}</ul><p class="links">{links}</p></details>'

    def section(self, anchor, title, description, rows, images, files, titles=None, complete=True, extra=""):
        return f'<section id="{anchor}"><div class="section-title"><h2>{safe(title)}</h2>{badge(rows, complete)}</div>' \
            f'<p>{safe(description)}</p>{extra}{self.gallery(images, titles)}{self.evidence(rows, files)}</section>'


def vector(value):
    if not isinstance(value, dict):
        return "—"
    return " / ".join(f'{value.get(axis, 0):.4f}' for axis in ("x", "y", "z"))


def scale_table(path):
    try:
        data = json.loads(read(path) or "{}")
    except json.JSONDecodeError:
        return '<p class="missing">尺寸记录 JSON 尚未完整写入。</p>'
    animals = data.get("animals", [])
    if not animals:
        return ""
    rows = []
    for animal in animals:
        name = {"squirrel": "松鼠", "turtle": "乌龟"}.get(animal.get("species"), animal.get("species", "动物"))
        cells = [f'{name} / {animal.get("spawnId", "?")}']
        for phase in ("before", "after", "play", "secondInitialize"):
            cells.append(vector((animal.get(phase) or {}).get("visualScale")))
        rows.append('<tr>' + ''.join(f'<td>{safe(cell)}</td>' for cell in cells) + '</tr>')
    pixel_rows = []
    for animal in animals:
        captures = {item.get("name", "").rsplit("-", 1)[-1]: item for item in animal.get("images", [])}
        if not all(phase in captures for phase in ("before", "after", "play")):
            continue
        before, after, play = (captures[phase] for phase in ("before", "after", "play"))
        name = {"squirrel": "松鼠", "turtle": "乌龟"}.get(animal.get("species"), "动物")
        change = max(abs(play.get(axis, 0) / max(1, after.get(axis, 0)) - 1) for axis in ("width", "height")) * 100
        size = lambda capture: f'{capture.get("width", 0)} × {capture.get("height", 0)}'
        pixel_rows.append('<tr>' + ''.join(f'<td>{safe(value)}</td>' for value in (
            f'{name} / {animal.get("spawnId", "?")}', size(before), size(after), size(play), f'{change:.2f}%')) + '</tr>')
    pixel_table = ''
    if pixel_rows:
        pixel_table = ('<p class="note">这里比较实际截图里的动物像素范围，而非只检查 Transform 数值。'
                       '修复后编辑态截图在保存并重新打开场景后拍摄；相机位置与正交大小保持一致。'
                       '动画姿态可能改变局部轮廓；位置移动与大小变化分别记录。</p>'
                       '<div class="table-wrap"><table><thead><tr><th>代表动物</th><th>修复前（像素）</th>'
                       '<th>重开后的编辑态</th><th>Play</th><th>宽高最大差异</th></tr></thead><tbody>' +
                       ''.join(pixel_rows) + '</tbody></table></div>')
    return (pixel_table + '<details><summary>全部尺寸记录 · ' + str(len(animals)) + ' 只（X / Y / Z）</summary>'
            '<div class="table-wrap"><table><thead><tr><th>动物</th><th>修复前编辑态</th><th>修复后编辑态</th>'
            '<th>Play</th><th>再次初始化</th></tr></thead><tbody>' + ''.join(rows) + '</tbody></table></div></details>')


def integration_section(report):
    path = report.root / "integration.json"
    try:
        data = json.loads(read(path) or "{}")
    except json.JSONDecodeError:
        data = {}
    rows = checks('\n'.join(data.get("checks", [])))
    editor = data.get("editor", {})
    complete = (bool(re.fullmatch(r'[0-9a-fA-F]{64}', data.get("sceneSha256", "")))
                and all(editor.get(key) is False for key in ("playing", "dirty", "compiling"))
                and bool(data.get("editorObservedAtUtc")))
    facts = [("主场景", data.get("scenePath", "待记录")), ("主场景 SHA256", data.get("sceneSha256", "待记录")),
             ("动物尺寸更新", f'{data.get("animalCount", "—")} 只 / {data.get("propertyOverrides", "—")} 项属性覆盖'),
             ("玻璃屋资源", f'{data.get("glasshouseMeshReferences", "—")} 个网格引用 / {data.get("glasshouseGeneratedMeshes", "—")} 个生成网格'),
             ("编辑器状态时间（UTC）", data.get("editorObservedAtUtc", "待记录")),
             ("编辑器状态", " / ".join(f'{key}={editor.get(key, "未记录")}' for key in ("playing", "dirty", "compiling")))]
    table = '<div class="table-wrap"><table><tbody>' + ''.join(f'<tr><th>{safe(label)}</th><td>{safe(value)}</td></tr>' for label, value in facts) + '</tbody></table></div>'
    return ('<section id="integration"><div class="section-title"><h2>主工程采用情况</h2>' + badge(rows, complete) + '</div>' +
            '<p>这一项独立记录修复是否已进入主工程，以及最后观察到的编辑器状态；副本测试通过不会自动替代主工程集成证明。</p>' +
            table + report.evidence(rows, [path], "集成核对记录") + '</section>')


def generate(root, behavior, draft):
    report = Report(root)
    run = root / behavior
    observations = read(run / "observations.txt")
    behavior_rows = checks(observations)
    protection = checks(read(run / "save-protection.txt"))
    finished = "RESULT behavior failures=" in observations and bool(protection)
    movement_rows = [(mark, text) for mark, text in behavior_rows if any(word in text for word in ("Shelter:", "Rain stopped:", "Outgoing approach:"))]
    if any(mark == "FAIL" for mark, _ in behavior_rows) and not movement_rows:
        movement_rows = behavior_rows
    movement_complete = finished and all(any(word in text for _, text in movement_rows) for word in ("Shelter:", "Rain stopped:", "Outgoing approach:"))
    contract_dir, scale_dir, glass_dir = (root / part for part in ("contracts-final", "scale", "glasshouse-v2"))
    contract_rows = checks(read(contract_dir / "contracts.txt"))
    budget_rows = checks(read(contract_dir / "budget-checks.txt"))
    glyph_rows = [(mark, text) for mark, text in contract_rows if re.search(r"glyph|\bZ\b|camera|摄像机", text, re.I)]
    glyph_rows += checks(read(contract_dir / "glyph.txt"))
    glyph_rows += [(mark, text) for mark, text in behavior_rows if "Sleep selected and executed" in text]
    scale_rows = checks(read(scale_dir / "scale-checks.txt"))
    glass_rows = checks(read(glass_dir / "checks.txt")) + checks(read(glass_dir / "visual-review.txt"))
    glass_images = sorted(glass_dir.glob("pillar-*.png"), key=lambda path: (path.stem.rsplit('-', 1)[-1], 0 if '-before-' in path.stem else 1))
    glass_complete = (len(glass_images) == 8 and any(mark == "PASS" and "eight nonblank" in text for mark, text in glass_rows)
                      and all(any(mark == "PASS" and text.startswith(f'LOD{level} ') for mark, text in glass_rows) for level in range(3))
                      and any(mark == "PASS" and text.startswith("visual review:") for mark, text in glass_rows))
    # A positive partial fixture does not establish a completed whole-scene run.
    scale_result = ""
    try:
        scale_result = json.loads(read(scale_dir / "scale-evidence.json") or "{}").get("result", "")
    except json.JSONDecodeError:
        pass
    scale_complete = scale_result.startswith("PASS") and (scale_dir / "save-protection.txt").is_file()
    if scale_result.startswith("FAIL") and not any(mark == "FAIL" for mark, _ in scale_rows):
        scale_rows.append(("FAIL", scale_result[5:]))
    behavior_summary = ''
    approach = next((text for mark, text in movement_rows if mark == 'PASS' and text.startswith('Outgoing approach:')), '')
    measurement = re.search(r'moved=([\d.]+); playerDistance=([\d.]+)', approach)
    if measurement and movement_complete and all(mark == 'PASS' for mark, _ in movement_rows):
        behavior_summary = (f'<p><b>本轮结果：</b>羊实际到达避雨目标，雨停后恢复原活动。外向兔实际移动 '
                            f'{safe(measurement.group(1))} 米，与玩家的距离从测试起始的 4 米缩短到 {safe(measurement.group(2))} 米。</p>')
    parts = []
    parts.append(report.section(
        "behavior", "避雨与接近玩家", "正式地图保存副本中的实际 Play。判断是否完成，以动物到达位置、雨停后状态、接近时实际位移为准。",
        movement_rows,
        [run / name for name in ("01_before_rain.png", "02_shelter.png", "03_rain_stopped.png", "06_approach_before.png", "07_approach_after.png")],
        [run / name for name in ("observations.txt", "visual-review.txt", "diagnostics-before-rain.txt", "diagnostics-after-rain.txt", "diagnostics-before-approach.txt", "diagnostics-after-approach.txt", "save-protection.txt", "source-hashes.txt", "source-hashes.json", "unity.log")],
        {"01_before_rain.png": "强雨前 · 原动物位置", "02_shelter.png": "避雨阶段结束 · 是否到达以断言为准", "03_rain_stopped.png": "雨停后状态", "06_approach_before.png": "外向兔 · 玩家靠近前", "07_approach_after.png": "外向兔 · 测试结束"},
        complete=movement_complete,
        extra=behavior_summary + '<p>避雨搜索在 32 米内分层查找树冠和屋檐，检查整只动物能否沿路径通过；新路线准备好后再接管移动。'
              '已接受的路径点在每帧移动中执行，不再等待低频思考；距离玩家远近不影响执行资格。矮地板按实际支撑高度处理，上、下台阶也经过移动验证。</p>'
              '<p>普通决策与路线规划各自每帧最多启动 4 次工作，分别使用 1 毫秒软额度。单次 Unity 物理操作不能中途抢占，可能超过额度。</p>'
              '<p class="note">复测安排：脚本设置玩家位置，临时把雨的判定半径扩大到 80 米，视觉范围仍为 8 米；动物没有被传送，网络请求已禁用。'
              '本项验证本地行为执行，不作为全场景 FPS、手势输入或 DeepSeek 服务验收。</p>'))
    parts.append(report.section(
        "glyph", "睡眠 Z · 面向实际渲染摄像机", "同一动物的两个拍摄角度，以及独立摄像机回归截图。截图来自 Unity 渲染，点击可查看原图。",
        glyph_rows, [run / "04_sleep_camera_a.png", run / "05_sleep_camera_b.png"] + sorted(contract_dir.glob("*.png")),
        [contract_dir / "contracts.txt", contract_dir / "glyph.txt", run / "observations.txt", run / "visual-review.txt"],
        {"04_sleep_camera_a.png": "睡眠 · 摄像机 A", "05_sleep_camera_b.png": "睡眠 · 摄像机 B", "glyph.png": "独立摄像机回归"},
        extra='<p class="note">为缩短等待，疲劳值临时设为 0.95。白色 Z 的朝向与睡眠状态分别验证；某个物种缺少趴卧动画时，Z 正常不代表已新增睡觉动作。</p>'))
    parts.append(report.section(
        "scale", "松鼠与乌龟 · 编辑态和 Play 尺寸", "尺寸数据覆盖测试记录中的动物。对比图使用固定正交检查摄像机与 0.25 米网格，属于尺寸诊断视图，不是正常游玩镜头。",
        scale_rows,
        [scale_dir / (species + '-' + phase + '.png') for species in ("squirrel", "turtle") for phase in ("edit-before", "edit-after", "play")],
        [scale_dir / name for name in ("scale-checks.txt", "scale-evidence.json", "save-protection.txt", "unity.log")],
        {f'{species}-{phase}.png': f'{chinese} · {label}' for species, chinese in (("squirrel", "松鼠"), ("turtle", "乌龟")) for phase, label in (("edit-before", "修复前编辑态"), ("edit-after", "保存并重开后的编辑态"), ("play", "Play"))},
        complete=scale_complete, extra=scale_table(scale_dir / "scale-evidence.json")))
    parts.append(report.section(
        "glass", "玻璃屋立柱 · 重叠表面", "同角度检查修复前后立柱接缝。模型检查与截图是两类证据；静态截图本身不代表已验证全部游玩视角下的闪烁。",
        glass_rows, glass_images,
        [glass_dir / name for name in ("checks.txt", "visual-review.txt", "sha256.txt", "unity.log")] + [root / "baseline" / "trim_offline_checks.json"],
        {path.name: "立柱 · " + path.stem.removeprefix("pillar-").replace("before", "修复前").replace("after", "修复后") for path in glass_images},
        complete=glass_complete,
        extra=f'<p class="note">V2 视觉复测要求四个角度的前后对照，共 8 张图；当前已有 {len(glass_images)} 张。每一对使用同一摄像机角度，仅替换修复前后的网格。'
              '截图固定 LOD0；LOD1 / LOD2 由共面重叠面积断言验证。本节证明独立副本中的模型修复效果。</p>'))
    history = []
    for previous in sorted(root.glob("run*")):
        if not previous.is_dir() or previous.name == behavior:
            continue
        text = read(previous / "observations.txt")
        rows = checks(text)
        if not text:
            continue
        label = ('有失败记录' if any(mark == "FAIL" for mark, _ in rows) else
                 ('通过的前一轮' if rows and "RESULT behavior failures=0" in text else
                  ('已结束，结论见原始记录' if "RESULT" in text else '未形成完整结果')))
        history.append(f'<h3>{safe(previous.name)} · {label}</h3>' + report.evidence(rows, [previous / "observations.txt", previous / "unity.log"]))
    old_glass = root / "glasshouse"
    if (old_glass / "checks.txt").is_file():
        history.append('<h3>玻璃屋首次修复 · 保留的旧版本</h3><p>首次模型与四张图记录保留供追溯；最终结论采用上方 V2 的两个前角、四个角度及八张对照图。</p>' +
                       report.evidence(checks(read(old_glass / "checks.txt")) + checks(read(old_glass / "capture-checks.txt")),
                                       [old_glass / "checks.txt", old_glass / "capture-checks.txt", old_glass / "capture.log"]) +
                       report.gallery(sorted(old_glass.glob("pillar-*.png"))))
    other_contracts = [(mark, text) for mark, text in contract_rows if (mark, text) not in glyph_rows]
    parts.append(integration_section(report))
    parts.append('<section id="records"><h2>测试记录与边界</h2>' +
                 f'<p>最终合同检查：{sum(mark == "PASS" for mark, _ in contract_rows)} 条通过，{sum(mark == "FAIL" for mark, _ in contract_rows)} 条失败；'
                 f'其中工作预算检查：{sum(mark == "PASS" for mark, _ in budget_rows)} 条通过，{sum(mark == "FAIL" for mark, _ in budget_rows)} 条失败（已包含在最终合同检查中）。各项范围见原始断言。</p>' +
                 report.evidence(other_contracts, [contract_dir / "contracts.txt", contract_dir / "source-hashes.json"], "行为、寻路与尺寸合同检查") +
                 report.evidence(budget_rows, [contract_dir / "budget-checks.txt"], "每帧计算预算回归") +
                 report.evidence(protection, [run / "save-protection.txt"], "本轮存档与场景保护检查") +
                 '<details><summary>保留的早期尝试</summary>' + (''.join(history) or '<p>没有其他运行记录。</p>') + '</details>' +
                 '<p class="note">页面上的通过状态由日志里的断言生成。缺失、运行中、失败的记录不会转成通过；人工观感仍以你检查截图为准。此报告没有全场景帧率验收结论。</p></section>')
    references = list(root.glob("*用户反馈*.png")) + list(root.glob("*用户反馈*.jpg"))
    if references:
        parts.append('<section><h2>用户反馈参考</h2><p>原始反馈截图，不计入修复后的验证证据。</p>' + report.gallery(references) + '</section>')
    title = ('草稿 · ' if draft else '') + '动物与场景问题复测'
    doc = f'''<!doctype html><html lang="zh-CN"><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1">
<title>{title}</title><style>
:root{{color-scheme:light;--ink:#302e2a;--muted:#726e65;--line:#ddd8ce;--paper:#f4f2ed;--card:#fffefa}}
*{{box-sizing:border-box}}body{{margin:0;background:var(--paper);color:var(--ink);font:16px/1.65 system-ui,"Microsoft YaHei",sans-serif}}main{{max-width:1220px;margin:auto;padding:38px 24px 64px}}h1{{font-size:32px;line-height:1.3;margin:8px 0 12px}}h2{{font-size:22px;margin:0}}h3{{font-size:17px}}p{{margin:12px 0}}a{{color:#4e635d;text-underline-offset:3px}}header>p{{color:var(--muted);max-width:900px}}.eyebrow{{font-size:13px;letter-spacing:.08em;color:var(--muted)}}nav{{display:flex;flex-wrap:wrap;gap:12px 24px;margin:22px 0 30px}}nav a{{text-decoration:none}}section{{background:var(--card);border:1px solid var(--line);border-radius:14px;padding:24px;margin:18px 0}}.section-title{{display:flex;align-items:center;justify-content:space-between;gap:14px}}.badge{{white-space:nowrap;font-size:13px;padding:4px 10px;border-radius:20px;background:#ebe5d7}}.badge.pass{{background:#e4ece3;color:#35533b}}.badge.fail{{background:#f0dfd9;color:#7a3c30}}.badge.pending{{color:#756344}}.gallery{{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:16px;margin:20px 0}}figure{{margin:0;min-width:0;border:1px solid var(--line);border-radius:9px;overflow:hidden;background:#e7e4dd}}figure img{{width:100%;display:block;aspect-ratio:16/9;object-fit:contain;background:#252826}}figcaption{{padding:12px 14px;font-size:14px;background:var(--card)}}small{{display:block;color:var(--muted);font-size:11px;overflow-wrap:anywhere}}.note,.missing{{padding:12px 15px;border-left:3px solid #b6b1a6;background:#f0eee8;font-size:14px;color:#625e55}}.missing{{color:#7d684c}}details{{border-top:1px solid var(--line);margin-top:16px;padding-top:12px}}summary{{cursor:pointer;font-size:14px;font-weight:600}}.checks{{padding-left:22px;font-size:13px;overflow-wrap:anywhere}}.checks li{{margin:9px 0}}.checks .pass b{{color:#47684a}}.checks .fail b{{color:#914f40}}.links{{font-size:13px;overflow-wrap:anywhere}}.muted{{color:var(--muted)}}.table-wrap{{overflow:auto}}table{{width:100%;border-collapse:collapse;font-size:12px;margin:14px 0}}th,td{{border-bottom:1px solid var(--line);padding:9px;text-align:left;white-space:nowrap}}footer{{font-size:12px;color:var(--muted);padding-top:16px}}@media(max-width:700px){{main{{padding:22px 14px}}section{{padding:18px}}.gallery{{grid-template-columns:1fr}}h1{{font-size:26px}}h2{{font-size:19px}}.section-title{{align-items:flex-start}}}}
#scale .gallery{{grid-template-columns:repeat(3,minmax(0,1fr))}}@media(max-width:800px){{#scale .gallery{{grid-template-columns:1fr}}}}
</style><main><header><div class="eyebrow">WEATHER HOUSE / UNITY 复测证据</div><h1>{title}</h1>
<p>本页采用 <b>{safe(behavior)}</b> 作为动物行为轮次，{('已取得结束记录与存档保护结果' if finished else '尚未取得完整结束记录，正在等待本轮证据')}。四项问题分别展示结果与原始证据；截图可点击放大，日志保留在每项下方。</p>
<nav><a href="#behavior">避雨与接近</a><a href="#glyph">睡眠 Z</a><a href="#scale">动物尺寸</a><a href="#glass">玻璃屋立柱</a><a href="#integration">主工程采用</a><a href="#records">原始记录</a></nav></header>
{''.join(parts)}<footer>本地离线报告 · 无外部字体、脚本或图片资源 · 截图与日志保留原文件</footer></main></html>'''
    missing = [rel for rel in report.linked if not (root / rel).is_file()]
    if missing:
        raise RuntimeError("Missing local report references: " + ', '.join(missing))
    return doc, len(report.linked)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", type=Path, default=DEFAULT_ROOT)
    parser.add_argument("--behavior", default="run6", help="Chosen behavior evidence subdirectory, e.g. run6")
    parser.add_argument("--draft", action="store_true", help="Write an explicitly labeled draft")
    parser.add_argument("--output", help="Output HTML filename within evidence root")
    args = parser.parse_args()
    root = args.root.resolve()
    behavior = (root / args.behavior).resolve()
    behavior.relative_to(root)
    doc, references = generate(root, behavior.relative_to(root).as_posix(), args.draft)
    output = root / (args.output or ("草稿_" if args.draft else "") + "动物行为修复复测报告.html")
    output.resolve().relative_to(root)
    output.write_text(doc, encoding="utf-8")
    print(f"Wrote {output}; verified {references} local references")


if __name__ == "__main__":
    main()
