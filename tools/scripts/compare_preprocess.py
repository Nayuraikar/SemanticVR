import numpy as np,onnxruntime as ort,sys
from PIL import Image
S=312
mean=np.array([0.485,0.456,0.406],np.float32);std=np.array([0.229,0.224,0.225],np.float32)
sess=ort.InferenceSession("model/model.onnx",providers=["CPUExecutionProvider"])
COCO={1:"person",2:"bicycle",3:"car",4:"motorcycle",6:"bus",8:"truck",10:"traffic light",13:"stop sign"}
def top(t,n=8):
    d,l,m=sess.run(None,{"input":t});p=1/(1+np.exp(-l[0]));c=p.argmax(1);s=p.max(1)
    return [(COCO.get(int(c[i]),str(c[i])),round(float(s[i]),2)) for i in np.argsort(-s)[:n] if s[i]>0.3]
for mode in sys.argv[2:]:
    d=sys.argv[1]
    im=Image.open(f"{d}/frame_{mode}.png").convert("RGB")
    py=((np.asarray(im.resize((S,S),Image.BILINEAR),np.float32)/255-mean)/std).transpose(2,0,1)[None].astype(np.float32)
    un=np.fromfile(f"{d}/input_{mode}.bin",np.float32).reshape(1,3,S,S)
    print(f"== {mode} frame {im.size}")
    print(" mean abs diff per channel:",np.abs(py-un).mean((0,2,3)).round(3)," overall corr %.4f"%np.corrcoef(py.ravel(),un.ravel())[0,1])
    print(" channel means py",py.mean((0,2,3)).round(3),"unity",un.mean((0,2,3)).round(3))
    fl=un[:,:,::-1,:]
    print(" corr if unity flipped vertically: %.4f"%np.corrcoef(py.ravel(),np.ascontiguousarray(fl).ravel())[0,1])
    print(" ORT on python-preprocessed:",top(py))
    print(" ORT on unity tensor       :",top(un))
