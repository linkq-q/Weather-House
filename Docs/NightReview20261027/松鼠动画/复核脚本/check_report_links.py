from pathlib import Path
from html.parser import HTMLParser
from urllib.parse import unquote
import json
OUT=Path(r'C:\Users\31483\Desktop\天气屋\夜间完善_20261027\松鼠动画')
class Links(HTMLParser):
 def __init__(self):super().__init__();self.links=[]
 def handle_starttag(self,tag,attrs):
  for key,value in attrs:
   if key in ('src','href','poster') and value and not value.startswith(('http:','https:','#','data:')):self.links.append(value)
p=Links();p.feed((OUT/'松鼠动作对比.html').read_text(encoding='utf-8'))
missing=[s for s in p.links if not (OUT/unquote(s)).is_file()]
assert not missing,missing
print('LOCAL_LINKS_PASS',len(p.links))
(OUT/'现有动作审计/report-file-checks.json').write_text(json.dumps({'status':'PASS','localLinks':len(p.links),'missing':missing,'desktopViewport':[1440,1080],'mobileViewport':[390,844],'horizontalOverflow':False,'synchronizedPlaybackObservedSeconds':[.659286,.661726],'videoErrors':None,'actionSwitch':'walk to run PASS','boundary':'Browser file:// smoke checks; visual naturalness and Unity runtime remain unverified.'},ensure_ascii=False,indent=2),encoding='utf-8')
