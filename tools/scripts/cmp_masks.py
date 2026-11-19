import numpy as np,sys
be=sys.argv[1]
r=np.fromfile("ref/masks.bin",np.float32).reshape(100,78,78)
u=np.fromfile(f"ref/unity_{be}_masks.bin",np.float32)
lab=np.fromfile("ref/labels.bin",np.float32).reshape(100,91);sc=(1/(1+np.exp(-lab))).max(1)
direct=np.corrcoef(u.reshape(100,78,78).ravel(),r.ravel())[0,1]
fixed=u.reshape(78,100,78).transpose(1,2,0)
print(f"{be}: direct corr {direct:.4f} | permuted corr {np.corrcoef(fixed.ravel(),r.ravel())[0,1]:.4f}")
