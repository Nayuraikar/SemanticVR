"""Creates the golden reference used by MaskLayoutValidator (Unity side).
Input: fixed public-domain photo, stretch-resized to 312x312 with PIL bilinear, stored as raw uint8 RGB.
Outputs: ONNX Runtime CPU results for that exact input (dets, labels, masks of the top-N queries).
Normalisation is applied in float32 by BOTH sides from the uint8 bytes, so inputs are bit-identical."""
import numpy as np,onnxruntime as ort,struct,sys,os
from PIL import Image
out=sys.argv[1];os.makedirs(out,exist_ok=True)
S=312;TOPN=16
im=Image.open("images/Bus_congestion_on_New_Oxford_Street.jpg").convert("RGB").resize((S,S),Image.BILINEAR)
u8=np.asarray(im,np.uint8)
mean=np.array([0.485,0.456,0.406],np.float32);std=np.array([0.229,0.224,0.225],np.float32)
a=((u8.astype(np.float32)/np.float32(255)-mean)/std).transpose(2,0,1)[None].astype(np.float32)
sess=ort.InferenceSession("model/model.onnx",providers=["CPUExecutionProvider"])
dets,labels,masks=sess.run(None,{"input":a})
sc=(1/(1+np.exp(-labels[0]))).max(1);top=np.argsort(-sc)[:TOPN].astype(np.int32)
open(f"{out}/golden_input_rgb.bytes","wb").write(u8.tobytes())
open(f"{out}/golden_dets.bytes","wb").write(dets.astype(np.float32).tobytes())
open(f"{out}/golden_labels.bytes","wb").write(labels.astype(np.float32).tobytes())
with open(f"{out}/golden_masks_top.bytes","wb") as f:
    f.write(struct.pack("<i",TOPN));f.write(top.tobytes());f.write(masks[0][top].astype(np.float32).tobytes())
print("top queries",top.tolist());print("scores",np.round(sc[top],2).tolist())
for n in os.listdir(out):print(n,os.path.getsize(f"{out}/{n}"))
