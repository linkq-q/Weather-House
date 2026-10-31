"""Build an evidence-led review page; never runs Unity or changes game/save data.

Example (run only after the selected evidence paths have been agreed):
  python Tools/AnimalAI/BuildStoryReviewReport.py --play-run run5 --reload-run reload-run1
Missing/incomplete evidence is rendered as pending; failed runs stay failed.
"""
from __future__ import annotations

import argparse
import hashlib
import html
import json
import os
import re
import struct
from dataclasses import dataclass
from datetime import datetime, timezone
from pathlib import Path
from urllib.parse import quote

DEFAULT_ROOT = Path("C:/Users/31483/Desktop/天气屋/动物看海与记忆复测_20261030")
PASS, FAIL, PENDING = "通过", "失败", "待验证"
SECRET = re.compile(r"sk-[A-Za-z0-9]{12,}")


def esc(value):
    return html.escape(str(value), quote=True)


def failed(text):
    return bool(re.search(r"(?:^|\s)FAIL(?:\s|$)", text))


def number(value, suffix=""):
    if isinstance(value, (int, float)) and not isinstance(value, bool) and abs(value) < float("inf"):
        return f"{value:.3f}".rstrip("0").rstrip(".") + suffix
    return "未记录"


def yes(value):
    return "是" if value is True else "否" if value is False else "未记录"


@dataclass
class Status:
    state: str
    reason: str


class Run:
    def __init__(self, path: Path):
        self.path = path
        self.errors = []

    def text(self, name):
        file = self.path / name
        if not file.is_file():
            return ""
        try:
            value = file.read_text(encoding="utf-8-sig")
            if SECRET.search(value):
                raise ValueError("疑似凭据，拒绝读取到报告")
            return value
        except (OSError, UnicodeError, ValueError) as error:
            self.errors.append(f"{name} 无法读取：{type(error).__name__}")
            return ""

    def data(self, name):
        text = self.text(name)
        if not text:
            return None
        try:
            return json.loads(text)
        except (ValueError, TypeError):
            self.errors.append(f"{name} 不是完整 JSON")
            return None


def selected_episodes(observations, card):
    selections = {}
    for line in observations.splitlines():
        match = re.search(r"SELECT watch_sea.*?scenario=([^;]+); episode=(\d+);id=([^;]+)", line)
        if match:
            selections[int(match[2])] = {"scenario": match[1], "id": match[3]}
    events = {event.get("id"): event for event in (card or {}).get("events", []) if isinstance(event, dict)}
    return [(index, selections.get(index), events.get((selections.get(index) or {}).get("id"))) for index in (1, 2, 3)]


def play_status(run):
    observations = run.text("observations.txt")
    contracts = run.text("contracts.txt")
    protection = run.text("save-protection.txt")
    process = run.data("process-result.json") or {}
    card = run.data("story-state.json") or {}
    result = re.findall(r"RESULT failures=(\d+); closedEpisodes=(\d+); networkRequests=(\d+)", observations)
    if failed(observations + contracts + protection) or process.get("exitCode", 0) != 0 or (result and int(result[-1][0]) != 0):
        return Status(FAIL, "日志或退出结果包含失败；未通过项保留在原始证据中。")
    required = ("AnimalStoryStateChecks", "AnimalStoryPlanChecks", "AnimalStoryIntegrationChecks", "AnimalStorySightlineChecks", "DemoTeachingChecks", "AnimalGlyphChecks", "LifePhotoFlowChecks")
    if not result or not process or not protection or not all(name in contracts for name in required):
        return Status(PENDING, "尚缺完整 RESULT、退出记录、保护日志或必要检查结果。")
    if tuple(map(int, result[-1])) != (0, 3, 0) or process.get("playEnded") is not True:
        return Status(FAIL, "结束记录未满足 3 次活动闭合、0 网络请求、退出 Play 的边界。")
    if "PASS Saved formal scene unchanged; Play=False" not in protection:
        return Status(PENDING, "缺少保存场景未变更的确认。")
    for index, selected, event in selected_episodes(observations, card):
        if not selected or not event:
            return Status(PENDING, f"缺少第 {index} 次选择与事件 ledger 的直接对应。")
        if (event.get("outcome") != "completed" or event.get("activity") != "watch_sea" or
                event.get("dwellSeconds", 0) < 8 or event.get("playerParticipated") is not (index > 1) or
                event.get("playerLeftEarly") is not (index == 3) or
                (event.get("companionSeconds", 0) < 3 if index > 1 else event.get("companionSeconds") != 0)):
            return Status(FAIL, f"第 {index} 次 ledger 与独自／陪伴／提前离开的验收条件不一致。")
    return Status(PASS, "3 次真实事件均完成；过程日志、事件字段、正常退出和保存保护相互对应。")


def parse_time(value):
    return datetime.fromisoformat(value.replace("Z", "+00:00"))


def reload_status(run, play):
    checks = run.text("reload-checks.txt")
    marker = "PASS Normal scene load/save reports no error and sends no network requests"
    base = checks.split(marker)[0] + (marker if marker in checks else "")
    if failed(base):
        return Status(FAIL, "正常场景重新加载阶段存在失败检查。")
    context = run.data("reload-context.json") or {}
    loaded = run.data("loaded-card.json")
    result = run.text("result.txt")
    protection = run.text("save-protection.txt")
    if marker not in checks or not context or not loaded or "RESULT exit=" not in result or not protection:
        return Status(PENDING, "尚未取得完整的新进程、真实加载卡片和结束／保护记录。")
    if failed(protection):
        return Status(FAIL, "重载保护记录报告文件变更。")
    try:
        same_source = Path(context["sourceDirectory"]).resolve() == (play.path / "isolated-save").resolve()
        new_process = context["processId"] != context["previousProcessId"] and parse_time(context["processStartedUtc"]) > parse_time(context["previousFinishedUtc"])
    except (KeyError, ValueError, TypeError):
        return Status(PENDING, "进程或来源字段不完整，不能用 JSON 往返测试代替重新打开游戏。")
    if not same_source or not new_process:
        return Status(FAIL, "重载来源并非本页选择的 Play 存档，或未证明是完成后启动的新进程。")
    needed = ("Actual harbor sheep binds", "Stable identity, personality", "Actual loaded preferences retain", "Actual history preserves", "Actual observation sent to the planner")
    if not all(any(line.startswith("PASS ") and item in line for line in base.splitlines()) for item in needed):
        return Status(PENDING, "缺少真实绑定、性格／偏好／历史或 planner 观察快照的一项验证。")
    return Status(PASS, "新的 Unity 进程从所选存档副本加载，正式 LifeSession 与动物实例绑定了保留的记忆。")


def live_status(run):
    text = run.text("live-network.txt")
    checks = run.text("reload-checks.txt")
    final = run.text("result.txt")
    if not text:
        return Status(PENDING, "尚无正式运行中真实网络调用的记录。"), {}
    states = re.findall(r"requests=(\d+) accepted=(\d+) busy=(True|False) appliedDelta=(-?\d+) rejectedDelta=(-?\d+) gatePending=(True|False)", text + "\n" + checks)
    metrics = dict(zip(("requests", "accepted", "busy", "appliedDelta", "rejectedDelta", "gatePending"), states[-1])) if states else {}
    endings = re.findall(r"elapsed=([\d.]+).*?requests=\d+.*?action=([^\s]+).*?story=([^\s]+)", text + "\n" + checks)
    if endings:
        metrics.update(zip(("elapsed", "action", "story"), endings[-1]))
    distance = re.search(r"INFO player=.*?distance=([\d.]+)", text)
    if distance:
        metrics["fixtureDistance"] = distance[1]
    relevant = [line for line in checks.splitlines() if any(word in line for word in ("network request returned", "normal apply callback", "Live network stage", "Live integration reached", "actual demo gate"))]
    if failed("\n".join(relevant) + text):
        return Status(FAIL, "真实网络／回调验证失败，不能用离线固定回复替代此结果。"), metrics
    accepted = any(line.startswith("PASS ") and "Exactly one actual network request returned a production-validated plan" in line for line in relevant)
    applied = any(line.startswith("PASS ") and "normal apply callback" in line for line in relevant)
    if accepted and applied and "RESULT exit=" in final and metrics.get("requests") == "1" and metrics.get("accepted") == "1" and metrics.get("gatePending") == "False" and metrics.get("rejectedDelta") == "0":
        return Status(PASS, "真实请求通过生产校验，并经正常上下文 gate 执行应用回调；不等同于模型动作完成。"), metrics
    return Status(PENDING, "目前不足以同时证明真实请求被接受、上下文 gate 通过和正常应用回调完成。"), metrics


def protocol_status(run):
    checks = run.text("checks.txt")
    result = re.findall(r"RESULT failures=(\d+)", checks)
    if failed(checks) or (result and int(result[-1]) != 0):
        return Status(FAIL, "独立网络协议检查失败。")
    if (result and int(result[-1]) == 0 and "PASS Production ReadResponse/AnimalPlan accepted a valid plan" in checks and
            "PASS Exactly one production network request" in checks and run.data("response-plan.json")):
        return Status(PASS, "真实 API 请求获得合法结构化计划；仅证明协议接受，没有运行现场动物行为。")
    return Status(PENDING, "未完成独立网络协议检查，或缺少已接受的 response-plan。")


class Page:
    def __init__(self, root):
        self.root = root.resolve()
        self.resources = {}
        self.missing = set()

    def href(self, path):
        path = path.resolve()
        if not path.is_file():
            self.missing.add(str(path))
            return None
        content = path.read_bytes()
        if not content:
            self.missing.add(str(path) + " (空文件)")
            return None
        item = {"path": str(path), "bytes": len(content), "sha256": hashlib.sha256(content).hexdigest()}
        if path.suffix.lower() == ".png":
            if content[:8] != b"\x89PNG\r\n\x1a\n" or content[-12:] != b"\0\0\0\0IEND\xaeB`\x82":
                self.missing.add(str(path) + " (PNG 不完整)")
                return None
            item["width"], item["height"] = struct.unpack(">II", content[16:24])
        elif path.suffix.lower() in (".json", ".txt", ".html") and SECRET.search(content.decode("utf-8-sig", errors="replace")):
            self.missing.add(str(path) + " (疑似凭据，已拒绝链接)")
            return None
        self.resources[str(path)] = item
        return quote(os.path.relpath(path, self.root).replace("\\", "/"), safe="/")

    def link(self, path, label):
        href = self.href(path)
        return f'<a href="{href}" target="_blank" rel="noopener">{esc(label)}</a>' if href else f'<span class="pending">{esc(label)}（未取得）</span>'

    def image(self, path, caption):
        href = self.href(path)
        if not href:
            return f'<p class="pending">{esc(caption)}：尚无完整截图。</p>'
        return f'<figure><a href="{href}" target="_blank" rel="noopener"><img src="{href}" loading="lazy" alt="{esc(caption)}"></a><figcaption>{esc(caption)} · 点击查看原图</figcaption></figure>'


def badge(status):
    css = "pass" if status.state == PASS else "fail" if status.state == FAIL else "pending"
    return f'<span class="badge {css}">{esc(status.state)}</span>'


def panel(title, status, body=""):
    return f'<section><h2>{esc(title)} {badge(status)}</h2><p>{esc(status.reason)}</p>{body}</section>'


def logs(page, run, names):
    return '<div class="links">' + " · ".join(page.link(run.path / name, label) for name, label in names) + "</div>"


def ledger_html(observations, card):
    titles = {1: "独自看海", 2: "玩家持续陪伴", 3: "玩家参与后提前离开"}
    rows, raw = [], []
    for index, selection, event in selected_episodes(observations, card):
        event = event or {}
        rows.append("<tr>" + "".join(f"<td>{esc(value)}</td>" for value in (
            titles[index], (selection or {}).get("id", "未找到选择记录"), event.get("outcome", "未闭合／未记录"),
            number(event.get("dwellSeconds"), " s"), number(event.get("companionSeconds"), " s"),
            yes(event.get("playerParticipated")), yes(event.get("playerLeftEarly")), event.get("reason") or "—")) + "</tr>")
        if event:
            raw.append(f"<h4>{titles[index]}</h4><pre>{esc(json.dumps(event, ensure_ascii=False, indent=2))}</pre>")
    return '<div class="tablewrap"><table><thead><tr><th>试验</th><th>事件 ID</th><th>结果</th><th>有效停留</th><th>陪伴</th><th>玩家参与</th><th>提前离开</th><th>原因</th></tr></thead><tbody>' + "".join(rows) + '</tbody></table></div><details><summary>查看对应原始 ledger 字段</summary>' + ("".join(raw) or "<p>暂无可对应的事件记录。</p>") + "</details>"


def build(args):
    root = Path(args.root).resolve()
    def selected(name):
        path = (root / name).resolve()
        if path.parent != root:
            raise ValueError("运行目录参数必须是报告根目录下的一个目录名称")
        return Run(path)
    play, reload, network = selected(args.play_run), selected(args.reload_run), selected(args.network_run)
    page = Page(root)
    pstatus, rstatus = play_status(play), reload_status(reload, play)
    lstatus, metrics = live_status(reload)
    observations, card = play.text("observations.txt"), play.data("story-state.json") or {}
    sections = []
    scope = '<p>在正式已保存场景的隔离副本中，测试选择合法的看海动作、安排玩家站位，并通过原有镜头控制取景。动物移动与到达／停留记录走生产执行路径；原始性格参数不人为调整。<strong>这组 Play 关闭网络，固定偏好话语只是输入 fixture，不能当作真实大模型输出。</strong></p><p class="notice">本页 run5 图片来自圈线／文字引导调整前的场景快照，仍可见旧圈。取消任务圈和文字引导属于后续独立改动，本页图片不能作为该新需求的效果验收。</p>'
    personality = "PASS Animal personality, habitat and scale remain unchanged" in observations
    path = re.findall(r"PASS First episode genuinely walked from its original position; path=([\d.]+)", observations)
    scope += f'<p class="small">参数原样保留检查：{"已找到 PASS" if personality else "尚无通过证据"}；首轮实走路径：{esc(path[-1] + " m" if path else "尚未记录通过值")}。后两轮可从已到达位置开始，不能宣称三轮都重新走了一段长路。</p>'
    scope += ledger_html(observations, card)
    scope += logs(page, play, (("observations.txt", "过程时序"), ("story-state.json", "真实事件 ledger"), ("contracts.txt", "规则与字库检查"), ("run-context.json", "进程启动信息"), ("process-result.json", "退出结果"), ("save-protection.txt", "存档／场景保护")))
    sections.append(panel("正式场景副本中的本地移动与记忆", pstatus, scope))

    shots = (("00-original.png", "原场景动物位置与测试取景"), ("episode1-walking.png", "第一轮实际行走途中"),
             ("episode1-complete.png", "独自看海活动闭合时"), ("episode2-participated.png", "玩家持续参与被记录时"),
             ("episode3-player-left.png", "第三轮玩家离开后"), ("episode3-complete.png", "提前离开情境的活动闭合时"),
             ("04-preference-heart-fixed-response.png", "固定合法回复启动的爱心特效 fixture（不宣称图中显示了台词气泡）"))
    figures = "".join(page.image(play.path / file, caption) for file, caption in shots if (play.path / file).exists())
    all_pngs = sorted(play.path.glob("*.png"))
    png_index = " · ".join(page.link(path, path.name) for path in all_pngs) or "尚未生成截图。"
    visual = '<p>' + page.link(root / "story-visual-review.txt", "本轮 7 张关键截图的只读目视审查记录") + '</p>' if (root / "story-visual-review.txt").exists() else ''
    sections.append('<section><h2>关键原始截图</h2><p class="small">图片展示位置与画面；移动、持续停留和参与时长由上方时序及 ledger 共同验证。固定回复画面不属于真实网络响应。脚本仅校验文件完整性；目视审查记录另列。</p><div class="gallery">' + (figures or '<p class="pending">本轮尚无完整原始截图。</p>') + '</div>' + visual + '<details><summary>全部原图清单（' + str(len(all_pngs)) + ' 张）</summary><div class="links">' + png_index + '</div></details></section>')

    context = reload.data("reload-context.json") or {}
    loaded = reload.data("loaded-card.json") or {}
    reload_body = f'<p>新进程 PID：<code>{esc(context.get("processId", "未记录"))}</code>；上一次 PID：<code>{esc(context.get("previousProcessId", "未记录"))}</code>。实际加载卡片：<code>{esc(loaded.get("animalId", "未取得"))}</code>，事件数 {len(loaded.get("events", [])) if loaded else "未取得"}。</p><p class="small">这里要求正常打开场景、LifeSession 读取存档并绑定动物，单独 JSON 序列化往返不算重载成功。若后续网络子阶段失败，重载本身的结果与网络结果分别列出。</p>'
    reload_body += logs(page, reload, (("reload-context.json", "重载进程与来源"), ("reload-checks.txt", "实际重载／回调检查"), ("expected-card.json", "源存档预期卡片"), ("loaded-card.json", "实际加载卡片"), ("loaded-observation.json", "恢复后 planner 观察快照"), ("result.txt", "最终退出结果"), ("save-protection.txt", "源数据保护")))
    sections.append(panel("关闭后由新进程重新读取记忆", rstatus, reload_body))

    live_body = '<p>该阶段只安排请求调度和一个玩家测试站位，模型回复、上下文校验与正常应用回调均须来自真实调用。没有动态话语可表示模型沉默，也可表示本地表达门控；仅凭没有气泡不能推断是哪一种。</p>'
    live_body += '<div class="tablewrap"><table><tbody>' + "".join(f'<tr><th>{esc(name)}</th><td>{esc(metrics.get(key, "未取得"))}</td></tr>' for key, name in (("elapsed", "请求与回调完成时的计时（秒）"), ("fixtureDistance", "fixture 初始玩家站距（米）"), ("requests", "实际请求数"), ("accepted", "生产校验接受数"), ("appliedDelta", "AppliedPlans 增量"), ("rejectedDelta", "拒绝计数增量"), ("gatePending", "是否仍等待上下文 gate"), ("action", "回调后的动作状态"), ("story", "回调后的动态话语"))) + '</tbody></table></div>'
    applied = metrics.get("appliedDelta")
    live_body += '<p class="small">' + ("动作应用计数已增加；仍需后续实际移动／完成证据才能称所选动作完成。" if applied and int(applied) > 0 else "目前没有动作应用计数增加的证据；正常回调通过不等于动物完成了模型所选动作。") + '</p>'
    if metrics.get("action") == "resume" and metrics.get("story") == "none":
        live_body += '<p><strong>本轮结束时 action=resume、story=none：验证了真实计划的接收和应用回调，没有据此宣称真实 AI 发表了台词或完成了新的看海动作。</strong></p>'
    live_body += logs(page, reload, (("live-network.txt", "真实网络计数与状态"), ("live-observation-at-arm.json", "请求安排时快照（非逐字网络正文）"), ("live-card-after.json", "回调后的卡片"), ("live-speaker-after.json", "回调后的说话状态")))
    if (reload.path / "live-request.json").exists():
        live_body += '<p>' + page.link(reload.path / "live-request.json", "采样到的真实请求正文（不含凭据／请求头）") + '</p>'
    sections.append(panel("运行中的真实 DeepSeek 接受与应用回调", lstatus, live_body))
    if args.prior_reload_run and args.prior_reload_run != args.reload_run:
        prior = selected(args.prior_reload_run)
        if prior.path.exists():
            prior_reload = reload_status(prior, play)
            prior_live, prior_metrics = live_status(prior)
            prior_text = prior.text("live-network.txt") + prior.text("reload-checks.txt")
            prior_body = f'<p>此前 <code>{esc(args.prior_reload_run)}</code> 的真实读档为 {badge(prior_reload)}，网络验收为 {badge(prior_live)}。原日志与退出失败结果完整保留，没有被新一轮覆盖。</p>'
            if "action=avoid" in prior_text and "过期计划已取消" in prior_text:
                prior_body += f'<p>日志显示 fixture 玩家初始站距为 {esc(prior_metrics.get("fixtureDistance", "未记录"))} 米，动物随后进入 avoid，计划版本发生变化；过期计划被取消，accepted={esc(prior_metrics.get("accepted", "未记录"))}、rejectedDelta={esc(prior_metrics.get("rejectedDelta", "未记录"))}。该取消保护生效，但这一轮网络验收仍记为失败。</p>'
            if metrics.get("fixtureDistance"):
                prior_body += f'<p>后续 <code>{esc(args.reload_run)}</code> fixture 的玩家站距为 {esc(metrics["fixtureDistance"])} 米。测试点位调整到个人空间之外；本次复测没有为通过验收而修改生产行为逻辑，也没有强制模型回复或动物动作。</p>'
            prior_body += logs(page, prior, (("reload-checks.txt", "首轮读档通过与网络失败"), ("live-network.txt", "首轮距离／avoid／取消时序"), ("result.txt", "首轮 exit=1 结果"), ("save-protection.txt", "首轮保护记录")))
            sections.append(panel("保留的前一次失败记录", prior_live, prior_body))
    if network.path.exists():
        nstatus = protocol_status(network)
        sections.append(panel("额外的独立协议快照请求", nstatus, '<p class="small">使用前一轮真实 ledger 组装受控观察快照；与运行中的动物回调是不同证据。即使通过，也不能替代上面的实际应用测试。</p>' + logs(page, network, (("checks.txt", "协议检查"), ("review-context.json", "快照控制条件"), ("input-observation.json", "输入快照"), ("response-plan.json", "生产校验接受的计划")))))

    photo = Run(root / args.photo_run)
    photo_summary = photo.text("summary.txt")
    photo_status = Status(FAIL if failed(photo_summary) else PASS if "RESULT failed=0" in photo_summary and all("PASS " + x in photo_summary for x in ("photo-model", "photo-ui", "photo-detail-render", "inscription")) else PENDING, "照片详情使用真实组件与人工旧 P03 数据；与主场景实际按快门测试分开。")
    sections.append(panel("照片图下字形与碑文组件", photo_status, '<p>' + page.link(root / "照片字形修复复测.html", "打开两张大图对照和逐项日志") + '</p><p>原照片话语／碑文顺序保留，再追加该照片匹配任务实际发现的字；未知字只显示字形，确认后才显示中文。历史照片需有匹配记录，不会对没有证据的旧图片凭空补字。</p>'))
    emotion = root.parent / "动物实心爱心与单叹号复测_20261030"
    design = root.parent / "字符设计/动物语字符设计_v05_四字扩展_20261030"
    sections.append('<section><h2>关联视觉与设计交付</h2><p>' + page.link(emotion / "实心爱心与单叹号复测报告.html", "实心爱心与单叹号独立 Unity 预览报告") + '：这是显示能力的独立预览，不能代替本轮正式场景触发证据。</p><p>' + page.link(design / "旧33字与新增4字对照.png", "作者交付：原 33 字与新增 4 字对照") + ' · ' + page.link(design / "新增四字预览.png", "作者交付：like / look / sea / past 四字预览") + ' · ' + page.link(design / "四字构形与对照.html", "作者设计说明") + '</p><p class="small">以上字形预览来自独立字符设计交付；其作者验证不代替本轮 Unity 检查。like 用于动物表达；sea / look / past 三座新碑仍在原石碑对话接受用户审阅，本报告不宣称它们已进入正式场景或完成玩家现场学习验收。</p></section>')

    warnings = sorted(set(play.errors + reload.errors + network.errors + photo.errors))
    if warnings or page.missing:
        sections.append('<section><h2>证据缺口／读取问题</h2><ul>' + "".join(f'<li>{esc(line)}</li>' for line in warnings + sorted(page.missing)) + '</ul><p class="small">缺失项没有被替换为猜测或通过结论。</p></section>')
    states = {"play": pstatus.__dict__, "reload": rstatus.__dict__, "liveNetwork": lstatus.__dict__, "photo": photo_status.__dict__}
    generated = datetime.now(timezone.utc).isoformat()
    document = '<!doctype html><html lang="zh-CN"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>动物看海与记忆复测报告</title><style>' + CSS + '</style></head><body><main><div class="small">天气屋 · 证据汇编</div><h1>动物看海与记忆复测报告</h1><p>本页将实际场景执行、固定输入 fixture、真实网络与重新加载分开记录。结果来自所选目录中的日志与字段，未完成部分保持待验证。</p><p class="small">Play：' + esc(args.play_run) + ' · 重载：' + esc(args.reload_run) + ' · 生成时间 UTC：' + esc(generated) + '</p><div class="overview">' + "".join(f'<div>{esc(title)} {badge(status)}</div>' for title, status in (("本地移动与 ledger", pstatus), ("实际新进程重载", rstatus), ("真实网络应用回调", lstatus), ("照片详情组件", photo_status))) + '</div>' + "".join(sections) + '<footer>所有截图链接指向原始文件；报告生成脚本不运行 Unity、不修改游戏代码或存档。图片文件完整性检查不等于人工视觉验收。</footer></main></body></html>'
    # Only write after all sources have been inspected; incomplete evidence remains visible in the page.
    root.mkdir(parents=True, exist_ok=True)
    output = root / args.output
    if output.parent.resolve() != root:
        raise ValueError("报告文件必须写在选定证据根目录")
    output.write_text(document, encoding="utf-8")
    audit = {"generatedUtc": generated, "selectedRuns": {"play": args.play_run, "reload": args.reload_run, "network": args.network_run}, "statuses": states,
             "resources": list(page.resources.values()), "missingOrRejected": sorted(page.missing), "readErrors": warnings, "htmlSha256": hashlib.sha256(output.read_bytes()).hexdigest()}
    (root / "报告资源核验.json").write_text(json.dumps(audit, ensure_ascii=False, indent=2), encoding="utf-8")
    print(json.dumps({"report": str(output), "statuses": states, "linkedResources": len(page.resources), "missingOrRejected": len(page.missing), "readErrors": len(warnings)}, ensure_ascii=False, indent=2))


CSS = """
:root{color-scheme:light;--ink:#293e34;--muted:#67766c;--line:#dbe3d9}*{box-sizing:border-box}body{margin:0;background:#f2f5ef;color:var(--ink);font:16px/1.75 system-ui,'Microsoft YaHei',sans-serif}main{max-width:1220px;margin:auto;padding:34px 22px 60px}h1{font-size:clamp(28px,4vw,40px);line-height:1.3}h2{font-size:23px;margin:0 0 14px}h4{margin:20px 0 8px}p{margin:12px 0}section{background:white;border:1px solid var(--line);border-radius:14px;padding:24px;margin:22px 0}a{color:#365f49;text-underline-offset:4px}code,pre{font-family:Consolas,monospace}code{background:#edf1e9;padding:2px 5px;overflow-wrap:anywhere}pre{white-space:pre-wrap;overflow-wrap:anywhere;background:#f4f6f0;padding:15px;border-radius:8px;font-size:13px}.small,figcaption{color:var(--muted);font-size:14px}.notice{padding:12px 16px;border-left:4px solid #a8996c;background:#f5f2e7}.badge{display:inline-block;font-size:14px;border:1px solid currentColor;border-radius:6px;padding:1px 9px;margin-left:7px}.pass{color:#376349}.fail{color:#9b443d}.pending{color:#876b34}.overview{display:flex;gap:12px 24px;flex-wrap:wrap;padding:15px 0}.gallery{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:20px}figure{margin:0}img{width:100%;height:auto;display:block;border:1px solid var(--line);border-radius:8px}figcaption{margin-top:7px}.tablewrap{overflow:auto}table{border-collapse:collapse;width:100%;font-size:14px}th,td{padding:10px;border-bottom:1px solid var(--line);text-align:left;vertical-align:top}th{background:#f2f5ee}td{overflow-wrap:anywhere}details{margin-top:18px}summary{cursor:pointer;font-weight:600}.links{line-height:2.1;margin:12px 0;overflow-wrap:anywhere}footer{color:var(--muted);font-size:14px;border-top:1px solid var(--line);padding-top:18px}@media(max-width:700px){main{padding:22px 12px 40px}section{padding:17px}.gallery{grid-template-columns:1fr}h2{font-size:20px}}
"""


def parser():
    result = argparse.ArgumentParser(description=__doc__)
    result.add_argument("--root", default=str(DEFAULT_ROOT))
    result.add_argument("--play-run", required=True)
    result.add_argument("--reload-run", required=True)
    result.add_argument("--network-run", default="network-run1")
    result.add_argument("--prior-reload-run", default="reload-run1")
    result.add_argument("--photo-run", default="photo-detail-run1")
    result.add_argument("--output", default="动物看海与记忆复测报告.html")
    return result


if __name__ == "__main__":
    build(parser().parse_args())
