import sys, math, json, numpy as np
from PIL import Image, ImageDraw
# Usage: python trace_serpent.py <output dir>   (run from the repo root)
# Traces the Serpent Arena centre line on ref_track_stadium.png and maps it to pitch metres (T4).
S=sys.argv[1]
# Homography pitch metres (x east, z north) -> picture pixels, fitted on goal-area corners at both ends of the pitch
# (6-yard boxes: front line x = +-47, sides z = +-9.16) and the far penalty-box corner (36, -20.16).
W=[(-47,9.16),(-52.5,9.16),(47,9.16),(47,-9.16),(36,-20.16)]
Px=[(105,434),(42,466),(820,66),(952,112),(992,180)]
A=[]
for (x,z),(u,v) in zip(W,Px):
    A.append([x,z,1,0,0,0,-u*x,-u*z,-u]); A.append([0,0,0,x,z,1,-v*x,-v*z,-v])
H=np.linalg.svd(np.array(A))[2][-1].reshape(3,3); Hi=np.linalg.inv(H)
def w(u,v):
    p=Hi@[u,v,1]; return (p[0]/p[2], p[1]/p[2])
def img(x,z):
    z=z/0.85
    p=H@[x,z,1]; return (p[0]/p[2],p[1]/p[2])
# Traced centre line (reference px), in flying order. 'H' marks elevated knots.
T=[
 # D tail: start/finish straight heading east below the start pad
 (760,338,''),(840,345,''),(930,340,''),
 # white-pad loop (anticlockwise in the picture)
 (990,300,''),(1000,245,''),(975,195,''),(930,165,''),(895,160,''),(862,188,''),(852,228,''),(862,262,''),
 # A: back west through the centre
 (825,290,''),(760,288,''),(690,270,''),(640,254,''),(600,244,''),(540,240,''),(470,233,''),(410,236,''),(360,250,''),(320,268,''),(285,290,''),(255,312,''),(228,330,''),
 # left S
 (195,337,''),(158,336,''),(127,320,''),(110,290,''),(118,252,''),(150,220,''),(195,200,''),(240,192,''),
 # B: bridge south over the S, sweep along the bottom, cube, climb over the centre
 (264,214,'H'),(254,252,'H'),(240,292,'H'),(229,332,'H'),(233,380,'H'),(252,430,''),(290,470,''),(345,500,''),(425,512,''),(520,505,''),(605,482,''),(660,447,''),(680,400,''),
 (678,345,'H'),(662,292,'H'),(626,252,'H'),(598,222,'H'),(588,195,''),(605,172,''),
 # pad2 loop
 (660,168,''),(720,187,''),(780,193,''),(795,175,''),(792,145,''),(780,122,''),(735,110,''),
 # top loop
 (700,92,''),(700,62,''),(668,40,''),(615,33,''),(575,47,''),(570,75,''),(590,96,''),
 # top straight west, descend over A
 (530,106,''),(470,104,''),(420,118,''),(386,140,'H'),(358,180,'H'),(335,222,'H'),(316,262,'H'),
 # pad1 loop
 (300,300,'H'),(322,335,''),(360,368,''),(395,392,''),(450,398,''),(510,384,''),(552,355,''),(585,335,''),
 # under B, back to the start straight
 (640,335,''),(700,336,''),
]
pts=[(w(u,v)[0],w(u,v)[1]*0.85,h) for u,v,h in T]
L=sum(math.dist(pts[i][:2],pts[(i+1)%len(pts)][:2]) for i in range(len(pts)))
print('knots',len(pts),'polyline length',round(L,1))
print('x range',round(min(p[0] for p in pts),1),round(max(p[0] for p in pts),1),'z range',round(min(p[1] for p in pts),1),round(max(p[1] for p in pts),1))
# self-crossings of the polyline
segs=[(pts[i],pts[(i+1)%len(pts)],i) for i in range(len(pts))]
def inter(a,b,c,d):
    (x1,y1),(x2,y2),(x3,y3),(x4,y4)=a[:2],b[:2],c[:2],d[:2]
    den=(x1-x2)*(y3-y4)-(y1-y2)*(x3-x4)
    if abs(den)<1e-9: return None
    t=((x1-x3)*(y3-y4)-(y1-y3)*(x3-x4))/den; u=((x1-x3)*(y1-y2)-(y1-y3)*(x1-x2))/den
    return (x1+t*(x2-x1),y1+t*(y2-y1)) if 0<=t<=1 and 0<=u<=1 else None
for i in range(len(segs)):
    for j in range(i+2,len(segs)):
        if i==0 and j==len(segs)-1: continue
        p=inter(segs[i][0],segs[i][1],segs[j][0],segs[j][1])
        if p: print('cross knots',i,'and',j,'at',(round(p[0],1),round(p[1],1)),'elev',segs[i][0][2] or segs[i][1][2],'/',segs[j][0][2] or segs[j][1][2])
json.dump([[round(x,2),round(z,2),h] for x,z,h in pts],open(S+'/serpent_knots_s.json','w'))
# top-down plot
im=Image.new('RGB',(1100,760),(40,90,40)); d=ImageDraw.Draw(im)
def P(x,z): return (550+x*9, 380-z*9)
d.rectangle([P(-52.5,34),P(52.5,-34)],outline=(255,255,255))
d.line([P(0,34),P(0,-34)],fill=(255,255,255))
for i in range(len(pts)):
    a,b=pts[i],pts[(i+1)%len(pts)]
    d.line([P(*a[:2]),P(*b[:2])],fill=(255,80,80) if (a[2] or b[2]) else (255,230,120),width=3)
    d.text(P(*a[:2]),str(i),fill=(255,255,255))
for name,(u,v) in {'pad1':(450,300),'pad2':(700,165),'white':(890,200),'start':(890,312),'cube':(680,400)}.items():
    x,z=w(u,v); d.ellipse([P(x-2,z+2),P(x+2,z-2)],outline=(0,0,0)); d.text(P(x,z),name,fill=(0,0,0))
im.save(S+'/serpent_top.png')
# overlay on the reference
ref=Image.open('Docs/reference/ref_track_stadium.png').convert('RGB'); d=ImageDraw.Draw(ref)
d.line([img(x,z) for x,z,h in pts]+[img(*pts[0][:2])],fill=(0,200,255),width=3)
ref.save(S+'/serpent_overlay.png')
