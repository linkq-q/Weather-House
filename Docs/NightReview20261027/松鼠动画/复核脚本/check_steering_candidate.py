"""A numeric model of the staged steering-speed gate, not Unity collision/animation proof."""
import math,json
from pathlib import Path
OUT=Path(r'C:\Users\31483\Desktop\天气屋\夜间完善_20261027\松鼠动画\现有动作审计')
def simulate(start,gate,can_rotate=True,species='squirrel'):
 angle=start;position=0;reverse_distance=0;rows=[];dt=1/60;speed=.65
 for frame in range(90):
  proposed=max(0,angle-150*dt) if can_rotate else angle
  alignment=math.cos(math.radians(proposed))
  travel=speed*max(0,alignment) if gate and species=='squirrel' and can_rotate else speed
  step=travel*dt;position+=step
  if alignment<0:reverse_distance+=step
  rows.append({'seconds':(frame+1)*dt,'angle':proposed,'distance':position,'speed':travel})
  angle=proposed
 return {'startAngle':start,'gate':gate,'canRotate':can_rotate,'species':species,'reverseTravel':reverse_distance,'distance':position,'samples':rows}
old=simulate(180,False);candidate=simulate(180,True)
assert old['reverseTravel']>.35
assert candidate['reverseTravel']==0
assert abs(simulate(0,True)['distance']-simulate(0,False)['distance'])<1e-12
assert simulate(180,True,False)['distance']==simulate(180,False,False)['distance']
assert simulate(180,True,True,'fox')['distance']==old['distance']
report={'status':'PASS','boundary':'Offline arithmetic fixture. Target lies 180 degrees behind, 150 deg/s turn, .65 m/s travel, no animation or Unity physics exercised. Candidate keeps original motion when CanRotate reports false so the existing backing-out escape branch remains available.','checks':5,'cases':[old,candidate,simulate(90,False),simulate(90,True)]}
(OUT/'steering-model-checks.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
print('STEERING_NUMERIC_PASS',report['checks'],'old reverse metres',old['reverseTravel'],'candidate',candidate['reverseTravel'])
