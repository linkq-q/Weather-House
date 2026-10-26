from pathlib import Path
from datetime import datetime
from html import escape
import json
import argparse
import re
from urllib.parse import urlsplit, unquote

root=Path(r'C:\Users\31483\Desktop\天气屋\夜间完善_20261027')
state_path=root/'交付状态.json'
state=json.loads(state_path.read_text('utf-8')) if state_path.exists() else {}
entries=[
 ('原对话玩法与UI收尾','../玩法实现验收_20261026/天气屋_玩法实现验收.html','六个专项和当前图性能采样均通过；原39项按各证据范围记录，Q03仍待人工'),
 ('生活经营、采集、建筑与内景','生活经营与建筑/生活经营与建筑功能策划.html','策划已成稿；新增机制未实施'),
 ('第一次施法与问候引导','新手引导/新手引导策划.html','历史对话已核对；策划已成稿；未实施'),
 ('地图、关卡与植物群落','地图与群落/地图与群落_研究与候选.html','首批96株、11家具、12花丛已保存，六项Play回归PASS；宏观连路、船屋及大面积群落待完善'),
 ('动物自由活动与NPC参考','动物与NPC/动物行为诊断与UI盘点.html','关键／次级活动分工已实施，漫游和帮助接管专项PASS；自然度仍需人工观察'),
 ('松鼠骨骼动画与替换比较','松鼠动画/松鼠动作对比.html','免费授权、原骨架修复与整套替换对比已成稿，9段实骨骼视频可比较'),
 ('尚未美化的UI截图','UI截图/UI截图汇总.html','基础53张加两批5补图，共58张实际游戏截图；短长回声补齐，U22／IME／变体等缺口保留')
]

docs=Path(r'C:\unity project\Weather House\Docs\NightReview20261027')
PREFIX='<!doctype html><html lang="zh-CN"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>天气屋 · 夜间工作总览</title><style>body{margin:0;background:#f6faf7;color:#173f42;font:17px/1.9 "Microsoft YaHei",sans-serif}main{max-width:1000px;margin:auto;padding:48px 30px}h1,h2{font-family:SimSun,serif}h1{font-size:38px}a{color:#20596c;text-underline-offset:5px}a:focus-visible{outline:3px solid #487d70}table{border-collapse:collapse;width:100%}td,th{padding:18px 14px;border-bottom:1px solid #b2cbb4;text-align:left;vertical-align:top}th{width:42%}.scope{border-left:6px solid #e7c76b;background:white;padding:18px 24px}small{font-size:14px}code{overflow-wrap:anywhere}@media(max-width:650px){main{padding:24px 16px}h1{font-size:28px}table,tbody,tr,th,td{display:block}th{width:auto;padding-bottom:0}td{padding-top:5px}}</style><main><p>2026年10月27日</p><h1>天气屋夜间工作总览</h1><p>按实际成果逐项打开。源码检查、Unity运行证据、画面核对和用户验收分别记录。</p><div class="scope"><b>本次执行范围</b><p>保留中央小岛与天气屋布局；优化外围节点、群落和动物活动。经营、生存、建筑内景与新手引导先出策划。松鼠保留外形修复与整套替换同时比较，尚未选定的外观方案保持候选。</p><p>每个独立完成并验证的部分分别本地提交。未推送，真实玩家存档不随测试导入。</p></div><h2>交付入口</h2><table>'
HISTORY_NOTE='<p id="local-history-sources"><strong>来源打开方式</strong>：Codex 会话链接是本机应用入口，需要相应会话仍可访问；旧24字与摄影修订报告是本机历史资料，不随项目 Docs 打包。它们用于追溯旧要求，不能覆盖本页标明的当前修订与建议。</p>'

def prepare_pages(destination,kind):
 source=root/'新手引导/新手引导策划.html'
 if source.exists():
  text=source.read_text('utf-8-sig')
  if 'id="local-history-sources"' not in text:
   marker='<section id="sources">'
   assert marker in text,'Onboarding source section changed.'
   text=text.replace(marker,marker+HISTORY_NOTE,1)
  if kind=='docs':
   def historical(match):
    ref=match.group(1)
    if not ref.startswith('../../'):return match.group(0)
    actual=(source.parent/unquote(urlsplit(ref).path)).resolve()
    assert actual.is_file(),'Missing historical source: '+str(actual)
    return 'href="'+escape(actual.as_uri(),quote=True)+'" data-source-scope="machine-history"'
   text=re.sub(r'href="([^"]+)"',historical,text)
  target=destination/'新手引导/新手引导策划.html';target.parent.mkdir(parents=True,exist_ok=True)
  if not target.exists() or target.read_text('utf-8-sig')!=text:target.write_text(text,'utf-8')
 if kind=='docs':
  # This report has no sibling resources. The active map author owns the map freeze/package.
  source=root/'动物与NPC/动物行为诊断与UI盘点.html'
  if source.exists():
   target=destination/'动物与NPC/动物行为诊断与UI盘点.html';target.parent.mkdir(parents=True,exist_ok=True)
   text=source.read_text('utf-8-sig')
   if not target.exists() or target.read_text('utf-8-sig')!=text:target.write_text(text,'utf-8')

def render(destination,kind):
 rows=[]
 for title,link,default in entries:
  if title=='原对话玩法与UI收尾' and kind=='docs':link='../LifeGameplay/验收入口.md'
  target=destination/link;local_note=''
  if target.exists():href=link
  elif kind=='docs' and (root/link).is_file():
   href=(root/link).resolve().as_uri();local_note='（本机专题来源，尚未纳入此 Docs 副本）'
  else:href=None
  label=('<a href="'+escape(href,quote=True)+'">'+escape(title)+'</a>') if href else escape(title)
  rows.append('<tr><th>'+label+'</th><td>'+escape(state.get(title,default))+('<br><small>'+escape(local_note)+'</small>' if local_note else '')+'</td></tr>')
 note='本页为项目 Docs 入口；标明本机来源的专题与 UI 完整原图需要本机桌面或 Review 目录，不随轻量报告副本交付。' if kind=='docs' else '本页为桌面交付入口；各专题的本机历史来源与实际运行证据在对应页面单独注明。'
 page=PREFIX.replace('</div><h2>交付入口','<p>'+escape(note)+'</p></div><h2>交付入口',1)+''.join(rows)
 page+='</table><h2>提交与验证记录</h2><p>'+escape(state.get('提交记录','正在完成各单元。提交哈希将在对应单元通过后补入。'))+'</p><p>'
 page+=escape(state.get('验证记录','保存后的正式场景六项回归均PASS：漫游03:02、世界03:06含可见同饮P22、真实AI对话03:08、摄影03:10、公开互动03:12、UI采图03:14（PASS_WITH_GAPS）。四张实拍仍只在隔离目录，Q03未完成；03:21改后A/B/A性能采样完成并还原设置，使用新报告；它证明采样流程，不代表性能目标或旧图对比通过。'))
 page+='</p><p><small>更新于 '+datetime.now().strftime('%Y-%m-%d %H:%M:%S')+'（本机时间）。待验证或尚未采图的项目保留明确状态，不以方案或离线编译代替完成。</small></p></main></html>'
 destination.mkdir(parents=True,exist_ok=True);(destination/'夜间工作总览.html').write_text(page,'utf-8')

def main():
 parser=argparse.ArgumentParser(description='Render Desktop/Docs separately; never changes status JSON or evidence.')
 parser.add_argument('--target',choices=['desktop','docs','both'],default='both');args=parser.parse_args()
 for kind,destination in [('desktop',root),('docs',docs)]:
  if args.target not in {kind,'both'}:continue
  prepare_pages(destination,kind);render(destination,kind)
 if args.target in {'docs','both'}:
  source=Path(__file__).read_text('utf-8-sig');target=docs/'生成夜间总览.py'
  if not target.exists() or target.read_text('utf-8-sig')!=source:target.write_text(source,'utf-8')
 print('Night index links rendered separately: '+args.target)

if __name__=='__main__':main()
