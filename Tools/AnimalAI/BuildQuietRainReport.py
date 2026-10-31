"""Build a local evidence report from completed Unity runs; never infer success from screenshots.

Run only after the final run folders have been selected, for example:
  python BuildQuietRainReport.py --guidance guidance-run1 --shelter shelter-run2 --rain-contracts rain-contracts2
The newest matching folder is used when an argument is omitted. An unfinished newest run
remains unfinished; the generator does not silently select an older successful run.
"""
from __future__ import annotations

import argparse
import html
import json
import re
from datetime import datetime
from html.parser import HTMLParser
from pathlib import Path
from urllib.parse import quote, unquote, urlsplit

DEFAULT_ROOT = Path("C:/Users/31483/Desktop/天气屋/无任务圈引导与避雨停留复测_20261031")
SECRET = re.compile(r"\bsk-[A-Za-z0-9_-]{20,}|\"apiKey\"\s*:\s*\"[^\"]+\"", re.I)
PASS = re.compile(r"(?:^|\s|:)PASS(?=\s|\d)", re.M)
FAIL = re.compile(r"(?:^|\s|:)(?:FAIL)\s", re.M)
GUIDE_IMAGES = [
    ("01-before-listen", "主动交流前", "玩家一次性站在安全位置；羊保持真实出生后的状态。"),
    ("03-actual-short-walk", "实际短程带路", "通过真实 Listen 入口开始；轨迹和速度由运行日志核对。"),
    ("05-released-with-active-record", "回头等候后释放", "用本图和朝向、等待时长日志核对回头；04 是刚进入等待的瞬间。"),
    ("06-free-after-release", "恢复自主控制", "任务记录仍在，观察后续 12 秒是否自动重启带路或持有手动控制。"),
]
SHELTER_IMAGES = [
    ("00-strong-rain-start", "正常 8 米雨区", "只安排玩家位置并施雨；不传送动物、不修改其速度和性格。"),
    ("01-moving-to-shelter", "实际前往遮挡处", "沿正式地图的碰撞和地面移动，不以进入 shelter 状态代替到达。"),
    ("02-arrived", "抵达并停稳", "同时检查真实移动、遮挡来源、位置误差与实际速度。"),
    ("03-still-sheltered-after20s", "持续强雨 20 秒", "连续检查目标、控制权和位置漂移。"),
    ("04-still-sheltered-in-weaker-rain", "转弱雨后再等 10 秒", "同一场雨持续，检查不重新抽签、不提前离开。"),
    ("05-after-clear", "收雨后解除避雨", "检查实际天气关闭及避雨状态释放。"),
    ("06-ten-seconds-after-clear", "收雨后继续观察 10 秒", "核对没有旧避雨状态或控制权重新占用；是否已经移动以本轮轨迹日志为准，释放控制不自动等于恢复步行。"),
    ("07-natural-movement-resumed", "后续自然恢复移动", "仅在该图与完成日志实际存在时展示；不追加动物目标、不传送动物，检查正常决策后的真实位移。"),
]


def read(path: Path) -> str:
    if not path.is_file():
        return ""
    value = path.read_text(encoding="utf-8-sig")
    if SECRET.search(value):
        raise ValueError(f"Possible credential in evidence file: {path.name}")
    return value


def pick(root: Path, requested: str | None, pattern: str) -> Path | None:
    if requested:
        value = (root / requested).resolve()
        if value.parent != root.resolve() or not value.is_dir():
            raise ValueError(f"Expected an existing immediate evidence directory: {requested}")
        return value
    values = [p for p in root.glob(pattern) if p.is_dir()]
    return max(values, key=lambda p: p.stat().st_mtime_ns) if values else None


def assess(folder: Path | None, kind: str) -> dict:
    if folder is None:
        return dict(folder=None, status="pending", reason="尚无证据目录", passed=0, failed=0, files={})
    files = {p.name: read(p) for p in sorted(folder.glob("*.txt"))
             if p.name not in {"visual-review.txt", "report-static-checks.txt"}}
    proof_names = [name for name in files if name in {"observations.txt", "contracts.txt", "checks.txt", "result.txt", "save-protection.txt"}
                   or name.endswith("-checks.txt")]
    text = "\n".join(files[name] for name in proof_names)
    passed, failed = len(PASS.findall(text)), len(FAIL.findall(text))
    exits = re.findall(r"\bexit\s*=\s*(-?\d+)", files.get("result.txt", ""))
    final = re.findall(r"\bRESULT\s+(?:failures|failedGroups)\s*=\s*(\d+)", text)
    if failed or any(int(v) for v in exits + final):
        status, reason = "fail", "有失败断言或非零结束码；保留原始结果"
    elif kind in {"guidance", "shelter"}:
        required = ["result.txt", "observations.txt", "contracts.txt", "save-protection.txt"]
        images = GUIDE_IMAGES if kind == "guidance" else SHELTER_IMAGES
        missing = [name for name in required if not files.get(name)]
        missing += [stem + ".png" for stem, _, _ in images if stem not in {"06-ten-seconds-after-clear", "07-natural-movement-resumed"} and not (folder / (stem + ".png")).is_file()]
        complete = exits and exits[-1] == "0" and final and final[-1] == "0"
        safe = bool(PASS.search(files.get("save-protection.txt", "")))
        if complete and safe and not missing:
            status, reason = "pass", "已结束，结果码、行为断言、保护检查及阶段截图齐全"
        else:
            status, reason = "pending", "未完成或证据未齐" + ("：" + "、".join(missing) if missing else "（缺少成功结束标记）")
    else:
        complete = exits and exits[-1] == "0" or final and final[-1] == "0"
        if complete and passed:
            status, reason = "pass", "检查已结束，未发现失败断言"
        else:
            status, reason = "pending", "只有部分检查记录，尚无明确成功结束标记"
    return dict(folder=folder, status=status, reason=reason, passed=passed, failed=failed, files=files)


def escape(value: object) -> str:
    return html.escape(str(value), quote=True)


def href(root: Path, file: Path) -> str:
    return quote(file.relative_to(root).as_posix(), safe="/")


def badge(status: str) -> str:
    label = {"pass": "通过", "fail": "未通过", "pending": "未完成"}[status]
    return f'<span class="badge {status}">{label}</span>'


def raw_details(root: Path, result: dict) -> str:
    folder = result["folder"]
    if not folder:
        return ""
    links = []
    for name in result["files"]:
        links.append(f'<a href="{href(root, folder / name)}">{escape(name)}</a>')
    for name in ["visual-review.txt"]:
        if (folder / name).is_file():
            read(folder / name)
            links.append(f'<a href="{href(root, folder / name)}">{name}</a>')
    for path in sorted(folder.glob("*.png")):
        links.append(f'<a href="{href(root, path)}">{escape(path.name)}</a>')
    return '<details><summary>原始日志与检查</summary><p class="links">' + " · ".join(links) + "</p></details>"


def event_lines(result: dict) -> list[str]:
    text = result["files"].get("observations.txt", "")
    return [line for line in text.splitlines() if re.search(r"\b(?:PASS(?=\b|\d)|FAIL\b|RESULT\b)", line)]


def gallery(root: Path, result: dict, spec: list[tuple[str, str, str]], close: bool = False) -> str:
    folder = result["folder"]
    if not folder:
        return ""
    items = []
    for stem, title, detail in spec:
        wide = folder / (stem + ".png")
        near = folder / (stem + "-close.png")
        shown = near if close and near.is_file() else wide
        if not shown.is_file():
            items.append(f'<div class="missing">{escape(title)}：截图尚未生成</div>')
            continue
        other = f' <a href="{href(root, wide)}">查看环境全景</a>' if shown != wide and wide.is_file() else ""
        items.append(f'<figure><a href="{href(root, shown)}"><img loading="lazy" src="{href(root, shown)}" alt="{escape(title)}"></a><figcaption><b>{escape(title)}</b><br>{escape(detail)}{other}</figcaption></figure>')
    return '<div class="gallery">' + "".join(items) + "</div>"


def section(root: Path, result: dict, title: str, intro: str, images=None, close=False) -> str:
    name = result["folder"].name if result["folder"] else "待运行"
    body = f'<section><div class="section-head"><h2>{escape(title)}</h2>{badge(result["status"])}</div><p>{intro}</p><p class="meta">{escape(name)} · {escape(result["reason"])}</p>'
    if images:
        body += gallery(root, result, images, close)
    selected = event_lines(result)
    if selected:
        body += '<details><summary>关键断言与实测数值</summary><pre>' + escape("\n".join(selected)) + '</pre></details>'
    elif result["files"]:
        values = "\n".join(v for k, v in result["files"].items() if "contract" in k or "checks" in k)
        body += '<details><summary>检查结果</summary><pre>' + escape(values) + '</pre></details>'
    body += raw_details(root, result)
    return body + "</section>"


def performance_runs(root: Path, requested: list[str] | None) -> list[tuple[dict, dict]]:
    folders = [pick(root, name, "") for name in requested] if requested else sorted(
        {p for pattern in ("performance-*", "perf-*") for p in root.glob(pattern) if p.is_dir()},
        key=lambda p: p.stat().st_mtime_ns)
    runs = []
    for folder in folders:
        result = assess(folder, "contracts")
        file = folder / "summary.json"
        data = json.loads(read(file)) if file.is_file() else {}
        if data.get("status") == "FAIL":
            result["status"], result["reason"] = "fail", "性能采样或保护检查失败"
        elif result["status"] == "pass":
            phases = data.get("phases", [])
            ready = data.get("status") == "PASS" and len(phases) == 2 and all(
                p.get("frames", 0) >= 300 and p.get("simSeconds", 0) >= 20 for p in phases)
            ready = ready and bool(PASS.search(result["files"].get("save-protection.txt", "")))
            ready = ready and all((folder / name).is_file() for name in ("frames.csv", "animals.csv", "available-counters.csv"))
            if not ready:
                result["status"], result["reason"] = "pending", "采样结束标记、阶段长度或原始数据未齐"
        runs.append((result, data))
    if not requested:
        runs.sort(key=lambda item: item[1].get("utcStart") or "9999")
    return runs


def health_section(root: Path, result: dict) -> str:
    text = result["files"].get("contracts.txt", "")
    groups = set(re.findall(r"^(?:FAIL )?([A-Za-z0-9]+\.[A-Za-z0-9]+):", text, re.M))
    failed = re.findall(r"RESULT failedGroups=(\d+)", text)
    body = section(root, result, "动物行为、AI 协议与表情回归", "独立 Edit 场景逐组检查避雨、寻路、控制权、预算、动作与情绪、故事事实和教学；另含表情渲染。契约与渲染夹具不代替正式地图的持续 Play。")[:-10]
    body += f'<p class="meta">日志包含 {len(groups)} 个方法入口、{result["passed"]} 个 PASS 标记；结束失败组数：{escape(failed[-1] if failed else "尚未结束")}。</p>'
    red = root / "population-fairness-red.txt"
    if red.is_file():
        body += '<h3>跨种群思考机会</h3><p>旧版固定更新顺序在受控 20 FPS 夹具中，第一秒三个实际种群的 Think 次数为 80 / 0 / 0，后面的种群拿不到机会。新调度为等待种群保留轮次，仍保持共享的每帧 4 次启动、1 毫秒软额度，单次操作仍可能超过时间额度。</p><p>公平性测试调用真实种群 Simulate，但帧与预算时钟受控；不能冒充正式地图长期公平性的现场统计。</p>'
        resources = [root / name for name in ["population-fairness-red.txt", "population-fairness-red-source.json", "population-fairness-red-fixture.cs"] if (root / name).is_file()]
        body += '<p class="links">' + ' · '.join(f'<a href="{href(root, p)}">{escape(p.name)}</a>' for p in resources) + '</p>'
        green = [line for line in text.splitlines() if line.startswith("PopulationDecisionFairnessChecks.Run:")]
        if green:
            body += '<details><summary>本轮公平调度断言（整套状态以上方结束结果为准）</summary><pre>' + escape('\n'.join(green)) + '</pre></details>'
        else:
            body += '<p class="meta">所选运行尚无公平调度的绿色完成证据。</p>'
    if result["folder"]:
        glyph_folder = result["folder"] / "emotion-rendering"
        previews = [
            ("emotion-rendering/emotions-camera-0", "表情 · 正面相机", "同一套生产符号：实心心形、hh、qq、怒号、问号、单个感叹号、水滴与音符。独立渲染夹具，包含未自动触发的保留接口。"),
            ("emotion-rendering/emotions-camera-1", "表情 · 俯侧相机", "相同对象换到另一实际相机，仍保持符号朝向与可读性。"),
            ("sleep-glyph-checks-angle-1", "睡眠 Z · 角度一", "白色、粗圆笔画；独立测试相机截图。"),
            ("sleep-glyph-checks-angle-2", "睡眠 Z · 角度二", "相机变化后仍正向可读；不等同于整场景动物已触发所有情绪。"),
        ]
        body += gallery(root, result, previews)
        resources = [p for p in sorted(glyph_folder.glob("*")) if p.suffix.lower() in {".png", ".txt"}]
        if resources:
            body += '<details><summary>表情夹具原图与检查（独立测试构图）</summary><p class="links">' + ' · '.join(f'<a href="{href(root, p)}">{escape(p.name)}</a>' for p in resources) + '</p></details>'
    audit = root / "animal-health-audit.txt"
    if audit.is_file():
        read(audit)
        body += f'<p><a href="{href(root, audit)}">动物 AI、情绪与记忆链路代码审查</a>。该文件保留其记录时点，最终运行状态以上方检查结果为准。</p>'
    update = root / "audit-runtime-update.txt"
    if update.is_file():
        body += f'<p><a href="{href(root, update)}">审查后的最终行为复测补记</a></p>'
    return body + '</section>'


def performance_section(root: Path, runs: list[tuple[dict, dict]]) -> str:
    body = '<section><h2>动物运行性能对照</h2><p>本轮性能基线已经包含避雨正确性修复，对照其尚未性能优化的版本与各次优化候选；ABAB保留为上一阶段数据，后续公平调度版本C单列，不是相对最初错误版本的帧率比较。固定正式地图、玩家与相机，网络关闭；晴天、正常 8 米强雨各至少采样 20 个模拟秒、不少于 300 帧。水面、渲染和远处动物持续运行。</p><p>这是批处理编辑器的同条件诊断，不是正式构建 FPS。Main Thread 包含等待，BehaviourUpdate 也包含所有脚本，不能单独当作 NPC 成本。下方“通过”表示采样完整，不表示达到某个性能门槛。</p>'
    body += '<p class="meta">第二次优化采样前，主编辑器恢复窗口布局，持续的零尺寸 SceneView RenderTexture 报错停止；后台负载可能随之改变。测试场景与相机配置一致，并不代表其他进程负载完全一致。</p>'
    if not runs:
        return body + '<p class="meta">性能结果尚未生成。</p></section>'
    variants = {data.get("variant") for result, data in runs if result["status"] == "pass" and result["folder"].name != "performance-baseline-run1"}
    if len(variants) < 2:
        body += '<p class="meta">正式对照版本尚未齐全，当前数据不能形成优化前后结论。</p>'
    compared = [(r, d) for r, d in runs if r["folder"].name != "performance-baseline-run1"]
    completed = [d for r, d in compared if r["status"] == "pass"]
    columns = '<th>阶段／指标</th>' + ''.join('<th>' + escape(r["folder"].name.replace("performance-", "")) + '<br><small>' + escape(d.get("variant", "尚无版本记录")) + '</small><br>' + badge(r["status"]) + '</th>' for r, d in compared)
    body += '<div class="table-wrap"><table class="comparison"><thead><tr>' + columns + '</tr></thead><tbody>'
    rows = [
        ("全场 BehaviourUpdate 均值", "metrics", "behaviourUpdateMs", "mean", " ms"),
        ("全场 BehaviourUpdate p95", "metrics", "behaviourUpdateMs", "p95", " ms"),
        ("全场 BehaviourUpdate p99", "metrics", "behaviourUpdateMs", "p99", " ms"),
        ("编辑器帧间隔均值", "metrics", "editorFrameIntervalMs", "mean", " ms"),
        ("编辑器帧间隔 p95", "metrics", "editorFrameIntervalMs", "p95", " ms"),
        ("Main Thread p95（含等待）", "metrics", "mainThreadMs", "p95", " ms"),
        ("GC 每帧分配均值", "metrics", "gcAllocatedBytes", "mean", " B"),
        ("全场活动动物均值", "workload", "allSceneAnimals", "mean", ""),
        ("8 米内动物均值", "workload", "nearbyAnimals8m", "mean", ""),
        ("避雨动作动物均值", "workload", "shelterActions", "mean", ""),
        ("待规划动物均值", "workload", "pendingRouteAnimals", "mean", ""),
        ("跟随路线动物均值", "workload", "routeFollowingAnimals", "mean", ""),
        ("实际运动动物均值", "workload", "movingAnimals", "mean", ""),
    ]
    for phase_name, title in [("sunny", "晴天"), ("strong-rain-8m", "正常强雨")]:
        phases = [next((p for p in d.get("phases", []) if p.get("name") == phase_name), {}) for _, d in compared]
        body += f'<tr><th>{title} · 帧数</th>' + ''.join('<td>' + escape(p.get("frames", "未完成")) + '</td>' for p in phases) + '</tr>'
        for label, group, name, stat, suffix in rows:
            body += '<tr><td>' + escape(label) + '</td>'
            for phase in phases:
                metric = next((m for m in phase.get(group, []) if m.get("name") == name), {})
                v = metric.get(stat, -1)
                value = "不可用" if not metric.get("samples", 0) or v is None or v < 0 else f'{v:.2f}{suffix}'
                body += '<td>' + value + '</td>'
            body += '</tr>'
    body += '</tbody></table></div>'
    if len(completed) >= 2:
        fields = ["unityVersion", "graphicsDevice", "graphicsApi", "width", "height", "quality", "vSync", "targetFrameRate", "timeScale", "fixedDeltaTime", "harnessSha256", "playerPosition", "cameraPosition", "cameraRotation"]
        different = [key for key in fields if any(d.get(key) != completed[0].get(key) for d in completed[1:]) or completed[0].get(key) is None]
        scene_hashes = [{f.get("path"): f.get("sha256") for f in d.get("sourceFiles", [])}.get("Assets/Scenes/WeatherHouse_WatersideV2.unity") for d in completed]
        if not all(scene_hashes) or "MISSING" in scene_hashes or len(set(scene_hashes)) > 1:
            different.append("sceneSha256")
        body += '<p class="meta">' + ("对照条件存在差异，不能直接归因于本次优化：" + escape("、".join(different)) if different else "已完成运行的记录条件一致；仍需同时比较下方工作量，且自然活动、其他进程负载和编辑器调度可能波动。") + '</p>'
    review = root / "performance-analysis.txt"
    if review.is_file():
        body += '<p>五轮 CPU 数据有明显波动，最后一轮较低的数值不能证明稳定提速。每帧分配在重复观测中较低，幅度随阶段与轮次变化；完整数据和工作量同时保留。</p><details><summary>性能对照解读、分组方法与限制</summary><p><a href="' + href(root, review) + '">打开解读原文</a></p><pre>' + escape(read(review)) + '</pre></details>'
    recomputed = root / "performance-recomputed-checks.txt"
    if recomputed.is_file():
        body += f'<p><a href="{href(root, recomputed)}">原始 CSV 重算、条件与源码核对</a></p>'
    for result, data in runs:
        folder = result["folder"]
        body += f'<details><summary>{escape(folder.name)} · 完整分位、工作量与原始数据 {badge(result["status"])}</summary><p>{escape(result["reason"])}</p>'
        if folder.name == "performance-baseline-run1":
            body += '<p>仅计数器探测，不进入性能对照：本轮发现实际时间单位为 TimeNanoseconds，旧测试器未识别，时间计数器均记为不可用；GC 计数正常。正式对照须使用后续同一冻结版本的测试器。</p>'
        links = [f'<a href="{href(root, folder / name)}">{name}</a>' for name in ("summary.json", "frames.csv", "animals.csv", "available-counters.csv", "source-stage.json") if (folder / name).is_file()]
        body += '<p class="links">' + ' · '.join(links) + '</p><pre>' + escape(json.dumps(data, ensure_ascii=False, indent=2)) + '</pre>' + raw_details(root, result) + '</details>'
    return body + '</section>'


class ResourceParser(HTMLParser):
    def __init__(self):
        super().__init__()
        self.paths = []

    def handle_starttag(self, tag, attrs):
        for key, value in attrs:
            if key in {"src", "href"} and value:
                self.paths.append(value)


def validate(root: Path, document: str, output: Path) -> list[str]:
    from PIL import Image

    if SECRET.search(document):
        raise ValueError("Possible credential in generated HTML")
    parser = ResourceParser()
    parser.feed(document)
    images = set()
    for value in parser.paths:
        url = urlsplit(value)
        if url.scheme or url.netloc or value.startswith("//"):
            raise ValueError("External resource is not allowed: " + value)
        if not url.path:
            continue
        path = (output.parent / unquote(url.path)).resolve()
        if not path.is_relative_to(root.resolve()) or not path.is_file():
            raise ValueError("Missing or out-of-evidence resource: " + value)
        if path.suffix.lower() == ".png":
            images.add(path)
        elif path.suffix.lower() in {".txt", ".json", ".csv", ".cs"}:
            read(path)
    for path in sorted(images):
        with Image.open(path) as im:
            im.verify()
        with Image.open(path) as im:
            im.load()
            if im.width < 320 or im.height < 180:
                raise ValueError("Unexpectedly small evidence image: " + path.name)
    return [f"PASS {len(parser.paths)} local resource references exist", f"PASS {len(images)} referenced PNG files decode", "PASS no external resources or recognized credentials in HTML/linked text", "NOTE static validation only; no browser rendering or visual approval implied"]


def main() -> None:
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("--root", type=Path, default=DEFAULT_ROOT)
    ap.add_argument("--guidance")
    ap.add_argument("--shelter")
    ap.add_argument("--rain-contracts")
    ap.add_argument("--health", help="Final consolidated contract folder; otherwise newest health-run*")
    ap.add_argument("--performance", action="append", help="Repeat for selected baseline/fixed folders; otherwise discover perf-* and performance-*")
    ap.add_argument("--output", default="无任务圈引导与避雨停留复测.html")
    args = ap.parse_args()
    root = args.root.resolve()
    if not root.is_dir():
        raise ValueError("Evidence root does not exist")
    guide = assess(pick(root, args.guidance, "guidance-run*"), "guidance")
    shelter = assess(pick(root, args.shelter, "shelter-run*"), "shelter")
    rain_folder = pick(root, args.rain_contracts, "rain-contracts*")
    rain = assess(rain_folder or shelter["folder"], "contracts")
    health = assess(pick(root, args.health, "health-run*"), "contracts")
    results = [guide, shelter, rain, health]
    perf = performance_runs(root, args.performance)
    results += [result for result, _ in perf]
    overall = "fail" if any(r["status"] == "fail" for r in results) else "pending" if any(r["status"] != "pass" for r in results) else "pass"
    comparison_ready = len({data.get("variant") for result, data in perf if result["status"] == "pass" and result["folder"].name != "performance-baseline-run1"}) >= 2
    if overall == "pass" and not comparison_ready:
        overall = "pending"
    intro = "本页区分实际场景行为、运行时结构检查和画面证据。所有结论取自所选运行的最终文件，缺少结束标记的运行不会标为通过。"
    parts = [f'<header><p class="eyebrow">天气屋 · 2026-10-31</p><h1>动物行为链路复测<br>引导、避雨与性能</h1>{badge(overall)}<p>{intro}</p></header>']
    parts.append('<section><h2>修复原因与当前行为</h2><p>动物之前一走出玩家身边的 8 米雨区，就会取消避雨。现在会记住这次避雨的出发点：只要原活动地仍下雨，就在已到达的遮挡处停留；强雨转弱也保留这次避雨选择，真正收雨后释放控制。</p><p>已有移动控制权不再因每次重新接收目标被拒或规划额度耗尽而中断；结伴动物朝当前绕路点转向，避免路径目标与转身目标不一致。避雨搜索先尝试附近已知遮挡，困难候选轮换推进，不再先等全部屋顶探测结束。</p><p>固定更新顺序还会让后面的种群长期得不到思考机会；新增调度为等待种群保留轮次，没有扩大共享预算。</p><p>模型请求仍异步返回；动物继续由本地能力、身体检查和分帧规划执行，原有性格与弱雨概率保留。引导由主动交流开始，等待后释放；地点圈与任务文字移除，摄影动物字目标和操作提示保留。</p></section>')
    parts.append(section(root, guide, "主动交流后的短程引导", "正式地图 Play，测试仅解锁一条已编写的邀请、安排安全玩家站位，并调用真实 Listen 入口。动物保留原位置、性格和运动参数；玩家不跟随时检查停步回头、实际等待 4 秒后释放，以及后续 12 秒不自动重启。网络关闭。", GUIDE_IMAGES))
    parts.append('<section><h2>界面与保留功能</h2><p>地点圈、世界地点标签、右上任务便笺与目标进度文字的移除，由实际构造及 Play 层级检查核对。上方图片是世界游戏相机渲染，不包含屏幕叠加 UI，不能单靠画面空白证明 HUD 已移除。</p><p>摄影目标页保留动物字和完成对勾，不显示中文答案；照片详情与词典保留已确认词义。操作提示、施法笔迹及日夜强度有定向检查。物件刷新、饮水、喂食及休息邀请链路经源代码复审保留，本轮未重新实测完整饮水和休息动画。</p></section>')
    parts.append(section(root, shelter, "抵达遮挡后持续停留", "正式地图中，玩家一次性站在原始绵羊附近的安全地面，使用正常 8 米雨区施强雨。必须先实际走到有遮挡的位置，再连续观察强雨 20 秒、转弱雨 10 秒，最后收雨检查释放。动物可能走出玩家的雨圈到树下，而原活动地仍下雨；此时近景没有雨滴是正常的，需对照全景中的原雨区和日志覆盖记录。全景亮圈是保留的施法作用范围，不是任务到达圈。近景和全景使用额外取证相机，展示真实世界状态，不代表玩家 UI 或固定游戏构图。", SHELTER_IMAGES, True))
    parts.append(section(root, rain, "避雨状态与控制权回归", "独立契约检查与真实场景测试互相补充。此节只计所选检查运行的实际结束结果；全部逐项输出保留在原日志中。"))
    parts.append(health_section(root, health))
    parts.append(performance_section(root, perf))
    parts.append('<section><h2>场景、存档与范围</h2><p>各次运行使用独立验证项目和隔离存档；保护项目与数量以各自 save-protection.txt 为准。本页不把本轮结果扩展为全场景帧率、网络模型服务或所有动物行为验收。</p>')
    for result in [guide, shelter]:
        if result["folder"]:
            protection = result["files"].get("save-protection.txt", "")
            parts.append(f'<details><summary>{escape(result["folder"].name)} · 保护检查 {len(PASS.findall(protection))} 通过 / {len(FAIL.findall(protection))} 失败</summary><pre>{escape(protection or "尚无保护结果")}</pre></details>')
    integration = root / "integration.json"
    if integration.is_file():
        data = json.loads(read(integration))
        parts.append('<details><summary>正式项目集成记录（独立于副本行为证据）</summary><pre>' + escape(json.dumps(data, ensure_ascii=False, indent=2)) + '</pre></details>')
    integration_links = [p for name in ["integration.json", "final-source-manifest.json", "final-verification.txt", "source-protection-before.json", "source-protection-after.json"] if (p := root / name).is_file()]
    if integration_links:
        parts.append('<p class="links">' + ' · '.join(f'<a href="{href(root, p)}">{escape(p.name)}</a>' for p in integration_links) + '</p>')
    parts.append('</section><section><h2>前次运行记录</h2><p>保留失败与未完成运行；不以新结果覆盖旧证据。</p>')
    rain_red = root / "rain-regression-red.txt"
    if rain_red.is_file():
        parts.append(f'<p><a href="{href(root, rain_red)}">旧版避雨的失败复现</a>，与后续绿色结果分别保留。</p>')
    chosen = {r["folder"] for r in results}
    history = sorted({p for pattern in ["contracts-run*", "guidance-run*", "shelter-run*", "rain-contracts*", "health-run*", "performance-*", "perf-*"] for p in root.glob(pattern) if p.is_dir()} - chosen, key=lambda p: p.stat().st_mtime_ns)
    for folder in history:
        kind = "guidance" if folder.name.startswith("guidance-") else "shelter" if folder.name.startswith("shelter-") else "contracts"
        old = assess(folder, kind)
        parts.append(f'<details><summary>{escape(folder.name)} {badge(old["status"])}</summary><p>{escape(old["reason"])}</p>')
        if folder.name == "contracts-run1":
            parts.append('<p>此轮 Edit 检查把等待起点设为 Time.time − 4.1；编辑模式的时间过小，负数被实际逻辑视为尚未开始。已移除这项伪造时间推论，真实等待时长改由 Play 的 4–6 秒断言核对；原失败仍保留。</p>')
        if folder.name == "health-run1":
            parts.append('<p>旧回归夹具期待避雨扫描足够久、旧 follow 搜索会反复重建；新调度较早拿到可用路线，触发了夹具前提失败。后续改用真实屋顶保留原核心断言，结果单列在 health-run2；没有删除此失败。</p>')
        if folder.name == "performance-baseline-run1":
            parts.append('<p>仅用于计数器探测，不进入性能对照。实际时间单位 TimeNanoseconds 当时未被测试器识别，时间计数器不可用；后续已补明确单位映射与毫秒换算，正式两版使用相同测试器。</p>')
        if folder.name == "shelter-run3":
            parts.append('<p>这一轮收雨后 10.010 秒行走距离为 0，只证明释放避雨控制。最终 shelter-run4 另记录了自然行走 6.918 米；没有改写本轮旧结果。</p>')
        failures = [line for text in old["files"].values() for line in text.splitlines() if FAIL.search(line)]
        if failures:
            parts.append('<pre>' + escape("\n".join(failures)) + '</pre>')
        parts.append(raw_details(root, old) + '</details>')
    if not history:
        parts.append('<p>暂无其他运行。</p>')
    parts.append('</section>')
    css = """*{box-sizing:border-box}body{margin:0;background:#efede7;color:#30312e;font:16px/1.7 system-ui,'Microsoft YaHei',sans-serif}main{max-width:1160px;margin:auto;padding:38px 24px 64px}header{padding:8px 0 26px}h1{font-size:32px;line-height:1.35;margin:5px 0 15px}h2{font-size:22px;margin:0}p{margin:12px 0}.eyebrow,.meta,footer{color:#6a6c64;font-size:14px}.section-head{display:flex;align-items:center;justify-content:space-between;gap:16px}section{background:#faf9f5;border:1px solid #dbd9d0;border-radius:12px;padding:25px;margin:20px 0}.badge{display:inline-block;border-radius:30px;padding:2px 12px;font-size:13px;font-weight:600;white-space:nowrap}.pass{background:#dce7db;color:#32543c}.fail{background:#efdbd5;color:#803a2b}.pending{background:#ece1c6;color:#70591f}.gallery{display:grid;grid-template-columns:1fr 1fr;gap:18px;margin-top:20px}figure{margin:0;border:1px solid #ddd9ce;background:#f1efe8;border-radius:8px;overflow:hidden}img{display:block;width:100%;height:auto}figcaption{padding:13px 16px;font-size:14px;color:#5b5e56}figcaption b{color:#30312e}a{color:#526b52;text-underline-offset:3px}details{border-top:1px solid #dedbd2;padding:12px 0;margin-top:12px}summary{cursor:pointer;color:#4f594e}pre{font-size:12px;line-height:1.65;white-space:pre-wrap;overflow-wrap:anywhere;background:#eeece5;padding:15px;border-radius:6px}.links{font-size:14px}.missing{padding:30px;background:#ece1c6}footer{margin-top:28px}@media(max-width:720px){main{padding:20px 12px}section{padding:18px}.gallery{grid-template-columns:1fr}h1{font-size:27px}.section-head{align-items:flex-start}}"""
    css += "\n.table-wrap{overflow-x:auto}table{width:100%;border-collapse:collapse;font-size:13px}th,td{text-align:left;vertical-align:top;border-bottom:1px solid #dedbd2;padding:10px;white-space:nowrap}th{background:#eeece5}.comparison th{white-space:normal}.comparison small{display:inline-block;max-width:230px;font-size:11px;overflow-wrap:anywhere}\n"
    document = '<!doctype html><html lang="zh-CN"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>动物行为链路复测：引导、避雨与性能</title><style>' + css + '</style><main>' + "".join(parts) + f'<footer>生成于 {datetime.now().astimezone().isoformat(timespec="seconds")} · 本地静态报告，无外部资源。图片点击可查看原图。</footer></main></html>'
    output = root / args.output
    if output.parent.resolve() != root:
        raise ValueError("Output must be directly inside evidence root")
    checks = validate(root, document, output)
    checks += [f"STATUS guidance={guide['status']} shelter={shelter['status']} rain-contracts={rain['status']} health={health['status']} performanceComparison={'ready' if comparison_ready else 'pending'} overall={overall}"]
    output.write_text(document, encoding="utf-8")
    (root / "report-static-checks.txt").write_text("\n".join(checks) + "\n", encoding="utf-8")
    print(output)
    print("\n".join(checks))


if __name__ == "__main__":
    main()
