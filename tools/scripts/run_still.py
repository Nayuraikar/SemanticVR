import sys,time,json,numpy as np,onnxruntime as ort
from PIL import Image,ImageDraw,ImageFont
COCO91={1:"person",2:"bicycle",3:"car",4:"motorcycle",5:"airplane",6:"bus",7:"train",8:"truck",9:"boat",10:"traffic light",11:"fire hydrant",13:"stop sign",14:"parking meter",15:"bench",16:"bird",17:"cat",18:"dog",19:"horse",20:"sheep",21:"cow",22:"elephant",23:"bear",24:"zebra",25:"giraffe",27:"backpack",28:"umbrella",31:"handbag",32:"tie",33:"suitcase",34:"frisbee",35:"skis",36:"snowboard",37:"sports ball",38:"kite",39:"baseball bat",40:"baseball glove",41:"skateboard",42:"surfboard",43:"tennis racket",44:"bottle",46:"wine glass",47:"cup",48:"fork",49:"knife",50:"spoon",51:"bowl",52:"banana",53:"apple",54:"sandwich",55:"orange",56:"broccoli",57:"carrot",58:"hot dog",59:"pizza",60:"donut",61:"cake",62:"chair",63:"couch",64:"potted plant",65:"bed",67:"dining table",70:"toilet",72:"tv",73:"laptop",74:"mouse",75:"remote",76:"keyboard",77:"cell phone",78:"microwave",79:"oven",80:"toaster",81:"sink",82:"refrigerator",84:"book",85:"clock",86:"vase",87:"scissors",88:"teddy bear",89:"hair drier",90:"toothbrush"}
TARGET={1,2,3,4,6,8,10,13}
S=312
mean=np.array([0.485,0.456,0.406],np.float32);std=np.array([0.229,0.224,0.225],np.float32)
sess=ort.InferenceSession("model/model.onnx",providers=["CPUExecutionProvider"])
def sig(x):return 1/(1+np.exp(-x))
def run(path,thr,tag):
    im=Image.open(path).convert("RGB");W,H=im.size
    t0=time.perf_counter()
    a=(np.asarray(im.resize((S,S),Image.BILINEAR),np.float32)/255-mean)/std
    a=a.transpose(2,0,1)[None].astype(np.float32)
    t1=time.perf_counter()
    dets,labels,masks=sess.run(None,{"input":a})
    t2=time.perf_counter()
    ts=[]
    for _ in range(20):
        s=time.perf_counter();sess.run(None,{"input":a});ts.append((time.perf_counter()-s)*1000)
    p=sig(labels[0]);cls=p.argmax(1);sc=p.max(1)
    unused=[c for c in range(91) if c not in COCO91 and p[:,c].max()>0.05]
    keep=[i for i in range(100) if sc[i]>=thr]
    keep.sort(key=lambda i:-sc[i])
    ov=im.copy().convert("RGBA");dr=ImageDraw.Draw(ov)
    rng=np.random.default_rng(1);res=[]
    mt=0
    for n,i in enumerate(keep):
        cx,cy,w,h=dets[0,i];x0,y0,x1,y1=[(cx-w/2)*W,(cy-h/2)*H,(cx+w/2)*W,(cy+h/2)*H]
        m=Image.fromarray(masks[0,i]).resize((W,H),Image.BILINEAR)  # float32 logits mode F
        ma=np.asarray(m)>0
        # restrict to box
        bx=np.zeros_like(ma);bx[max(0,int(y0)):int(y1)+1,max(0,int(x0)):int(x1)+1]=True;ma&=bx
        col=tuple(int(v) for v in rng.integers(60,255,3))
        layer=np.zeros((H,W,4),np.uint8);layer[ma]=(*col,110)
        ov=Image.alpha_composite(ov,Image.fromarray(layer))
        dr=ImageDraw.Draw(ov);dr.rectangle([x0,y0,x1,y1],outline=col+(255,),width=2)
        name=COCO91.get(int(cls[i]),f"id{cls[i]}")
        dr.text((x0+2,max(0,y0-11)),f"{name} {sc[i]:.2f}",fill=(255,255,255,255))
        res.append(dict(det_id=n,query=int(i),cls91=int(cls[i]),name=name,target=int(cls[i]) in TARGET,score=round(float(sc[i]),3),box=[round(float(v),1) for v in(x0,y0,x1,y1)],mask_px=int(ma.sum())))
    ov.convert("RGB").save(f"out/{tag}_annotated.png")
    print(f"== {tag} {W}x{H} thr={thr}")
    print(f"pre {1000*(t1-t0):.1f}ms first-run {1000*(t2-t1):.1f}ms | steady mean {np.mean(ts):.1f} median {np.median(ts):.1f} min {np.min(ts):.1f} max {np.max(ts):.1f} ms")
    print("ids with unexpected prob>0.05 (not in COCO91 named set):",unused)
    for r in res:print(r)
    json.dump(res,open(f"out/{tag}.json","w"),indent=1)
    # also: low-confidence target-class candidates
    low=[(COCO91[int(cls[i])],round(float(sc[i]),2)) for i in range(100) if 0.1<=sc[i]<thr and int(cls[i]) in TARGET]
    print("target-class candidates in [0.1,thr):",low)
if __name__=="__main__":
    run(sys.argv[1],float(sys.argv[2]),sys.argv[3])
