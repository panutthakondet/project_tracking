"""Audit the palette emitted by the isolated C# harness; never connects to a database.
Simulation matrices: Chromium's Machado-based vision_deficiency.cc, commit 64fd31034bd3.
Linear-sRGB simulation is a diagnostic, not a clinical validation or WCAG certification.
"""
import json, math
from pathlib import Path
from itertools import combinations
matrices={
 'protanopia':[[.152,1.053,-.205],[.115,.786,.099],[-.004,-.048,1.052]],
 'deuteranopia':[[.367,.861,-.228],[.280,.673,.047],[-.012,.043,.969]],
 'tritanopia':[[1.256,-.077,-.179],[-.078,.931,.148],[.005,.691,.304]],
 'achromatopsia':[[.2126,.7152,.0722]]*3,
}
def linear(hex):
 c=[int(hex[i:i+2],16)/255 for i in (1,3,5)]
 return [x/12.92 if x<=.04045 else ((x+.055)/1.055)**2.4 for x in c]
def simulate(hex,kind):
 c=linear(hex)
 return [min(1,max(0,sum(a*b for a,b in zip(row,c)))) for row in matrices[kind]] if kind!='normal' else c
def lum(c):return sum(a*b for a,b in zip(c,[.2126,.7152,.0722]))
def contrast(a,b):
 a,b=lum(a),lum(b)
 return (max(a,b)+.05)/(min(a,b)+.05)
def hexcolor(c):
 return '#'+''.join(f'{round(255*(x*12.92 if x<=.0031308 else 1.055*x**(1/2.4)-.055)):02x}' for x in c)
palettes=json.loads(Path('/private/tmp/color-vision-palette-audit.json').read_text())
report={}
for mode,tones in palettes.items():
 kinds=['normal']+({'red-green':['protanopia','deuteranopia'],'blue-yellow':['tritanopia'],'monochrome':['achromatopsia']}[mode])
 report[mode]={}
 for kind in kinds:
  values={k:contrast(simulate(v['background'],kind),simulate(v['foreground'],kind)) for k,v in tones.items()}
  assert min(values.values())>=4.5, (mode,kind,values)
  report[mode][kind]={'textContrast':values,'simulatedColors':{k:hexcolor(simulate(v['background'],kind)) for k,v in tones.items()},'pairsNeedingNonColourCues':[f'{a}/{b}' for a,b in combinations(tones,2) if contrast(simulate(tones[a]['background'],kind),simulate(tones[b]['background'],kind))<3]}
  print(f'{mode}/{kind}: minimum text contrast {min(values.values()):.2f}:1; patterns and labels required for close colour pairs')
Path('/private/tmp/color-vision-simulation-audit.json').write_text(json.dumps(report,indent=2))
