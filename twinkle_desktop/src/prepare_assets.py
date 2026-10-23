from pathlib import Path
from PIL import Image,ImageDraw,ImageFont
import json,numpy as np,math,shutil
root=Path(__file__).resolve().parent.parent;library=root.parent/'twinkle_twinkle';catalog=json.loads((library/'catalog.json').read_text(encoding='utf-8'))['assets'];byid={x['id']:x for x in catalog}
size=512
# Source-derived animation: inverse mesh warp. No painted/generated replacement pixels.
source=Image.open(library/byid['base_15']['frame_folder']/'frame_001.png').convert('RGBA')
master=Image.new('RGBA',(size,size));pic=source.resize((448,448),Image.Resampling.LANCZOS);master.paste(pic,(32,32));master.save(root/'assets'/'master_walk.png')
arr=np.asarray(master,dtype=np.float32)/255;prem=arr.copy();prem[:,:,:3]*=prem[:,:,3:4]
yy,xx=np.mgrid[0:size,0:size].astype(np.float32)
def sample(sx,sy):
 x0=np.floor(sx).astype(int);y0=np.floor(sy).astype(int);fx=(sx-x0)[...,None];fy=(sy-y0)[...,None];out=np.zeros_like(prem)
 for ox,oy,w in [(0,0,(1-fx)*(1-fy)),(1,0,fx*(1-fy)),(0,1,(1-fx)*fy),(1,1,fx*fy)]:
  ix=x0+ox;iy=y0+oy;valid=(ix>=0)&(ix<size)&(iy>=0)&(iy<size)
  out[valid]+=prem[np.clip(iy[valid],0,size-1),np.clip(ix[valid],0,size-1)]*w[valid]
 alpha=out[:,:,3:4];out[:,:,:3]=np.divide(out[:,:,:3],alpha,out=np.zeros_like(out[:,:,:3]),where=alpha>0.00001)
 return Image.fromarray(np.clip(np.round(out*255),0,255).astype('uint8'),'RGBA')
walk=[]
for i in range(12):
 phase=i/12*2*math.pi;s=math.sin(phase)
 # Head and torso stay pixel-identical. Only the bottom of the two feet moves.
 leg=np.clip((yy-425)/48,0,1);leg=leg*leg*(3-2*leg)
 region=np.clip((xx-175)/16,0,1)*np.clip((329-xx)/16,0,1)
 leg*=region
 side=np.tanh((xx-259)/12)
 lift=(np.maximum(0,s)*(1-side)/2+np.maximum(0,-s)*(1+side)/2)*9*leg
 sx=xx-3*math.cos(phase)*side*leg
 sy=yy+lift
 frame=sample(sx,sy)
 frame.paste(master.crop((0,0,512,425)),(0,0))
 walk.append(frame)
animations={}
def save_state(name,frames,durations,provenance):
 d=root/'assets'/name;d.mkdir(exist_ok=True)
 for i,f in enumerate(frames):f.save(d/f'{i:03}.png')
 for p in d.glob('[0-9][0-9][0-9].png'):
  assert p.resolve().is_relative_to(d.resolve())
  if int(p.stem)>=len(frames):p.unlink()
 cols=min(6,len(frames));rows=math.ceil(len(frames)/cols);sheet=Image.new('RGBA',(size*cols,size*rows))
 for i,f in enumerate(frames):sheet.paste(f,(i%cols*size,i//cols*size))
 sheet.save(root/'assets'/(name+'_sheet.png'))
 frames[0].save(root/'assets'/(name+'.png'),save_all=True,append_images=frames[1:],duration=durations,loop=0,disposal=0,blend=0)
 animations[name]={'files':[f'assets/{name}/{i:03}.png' for i in range(len(frames))],'durations_ms':durations,'loop':True,'frame_width':512,'frame_height':512,'provenance':provenance}
save_state('walk_left',walk,[85]*12,'base_15 frame_001; foot-only inverse mesh below y=425; lift <=9px; stride <=3px; head and torso fixed')
save_state('walk_right',[f.transpose(Image.Transpose.FLIP_LEFT_RIGHT) for f in walk],[85]*12,'walk_left, horizontal mirror to face right')
save_state('idle',[master],[1000],'base_15 frame_001; original neutral strawberry pose; no body sway')
save_state('reaction',[master],[800],'base_15 frame_001; calm neutral pose')
# Normalize each activity's requested source expression, retaining original proportions.
for state,source_id in [('focus','base_14'),('rest','base_01'),('meeting','base_03'),('farewell','daily_01')]:
 item=byid[source_id];original=Image.open(library/item['original_file']);fs=[];ds=[]
 for i in range(original.n_frames):
  original.seek(i);f=original.convert('RGBA');f.thumbnail((448,448),Image.Resampling.LANCZOS);canvas=Image.new('RGBA',(512,512));canvas.paste(f,((512-f.width)//2,480-f.height));fs.append(canvas)
  # GIF zero delay is interpreted as 100ms by mainstream decoders; keep app timing nonzero.
  raw=float(original.info.get('duration',100));ds.append(raw if raw>=20 else 100)
 save_state(state,fs,ds,f'{source_id}; original RGBA frames; fit 448px height; baseline y=480; zero-delay frames clamped to 100ms')
(root/'assets'/'animations.json').write_text(json.dumps(animations,ensure_ascii=False,indent=2),encoding='utf-8')
# Native tray/window icon is derived from the source pose.
master.save(root/'assets'/'twinkle.ico',sizes=[(16,16),(24,24),(32,32),(48,48),(64,64),(128,128),(256,256)])
provenance={'source_library':'../twinkle_twinkle','ai_image_generation_used':False,'processing':'source frame resampling; mesh deformation limited to feet; mirroring; sprite packing','activity_map':{'walk':'base_15 frame_001: 딸기 모자, 작은 발걸음','farewell':'daily_01: 커튼 뒤 빼꼼, 원본 배경 유지','focus':'base_14: 두 별 응원','rest':'base_01: 졸림 · 하품','meeting':'base_03: 갸우뚱 · 의아함'},'sources':[{k:byid[s][k] for k in ['id','source_page','image_url','sha256','uploader_label','source_status']} for s in ['base_15','daily_01','base_14','base_01','base_03']]}
(root/'ASSET_SOURCES.json').write_text(json.dumps(provenance,ensure_ascii=False,indent=2),encoding='utf-8')
# Viewable walk cycle on a light background for QA.
sheet=Image.new('RGB',(6*200,2*210),'#f2f4f5');draw=ImageDraw.Draw(sheet);font=ImageFont.truetype('C:/Windows/Fonts/arial.ttf',14)
for i,f in enumerate(walk):
 p=f.copy();p.thumbnail((190,190));x=i%6*200;y=i//6*210;sheet.paste(p,(x+5,y),p);draw.text((x+8,y+190),str(i),fill='#333333',font=font)
sheet.save(root/'walk_cycle_review.png')
# Moving preview uses the exact exported frame images.
frames=[]
for i in range(96):
 canvas=Image.new('RGB',(700,330),'#f8f7f2');d=ImageDraw.Draw(canvas);d.line((25,294,675,294),fill='#e3dfd4',width=2)
 t=i/95;right=t<.5;progress=(t*2 if right else 2-t*2);x=round(25+progress*440)
 p=walk[i%12].transpose(Image.Transpose.FLIP_LEFT_RIGHT) if right else walk[i%12];p=p.resize((240,240),Image.Resampling.LANCZOS);canvas.paste(p,(x,66),p)
 frames.append(canvas)
frames[0].save(root/'walk_preview.gif',save_all=True,append_images=frames[1:],duration=85,loop=0)
# Guard the requested quiet upper body and a non-static, alternating foot cycle.
reference=np.asarray(master)
assert all(np.array_equal(np.asarray(f)[:425],reference[:425]) for f in walk)
assert len({f.tobytes() for f in walk})>=10
(root/'WALK_ASSET_TEST_RESULTS.txt').write_text('PASS: 12 phases per direction; every pixel above y=425 unchanged; feet alternate with <=9px lift and <=3px stride at 512px; left/right frames mirror exactly; no AI image generation.\n',encoding='utf-8')
print('States',[(k,len(v['files'])) for k,v in animations.items()])
print('Files',sum(1 for p in root.rglob('*') if p.is_file()))
