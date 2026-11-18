import collections,sys
d=open("model/model.onnx","rb").read()
def rv(b,i):
    r=0;s=0
    while True:
        c=b[i];i+=1;r|=(c&127)<<s;s+=7
        if c<128:return r,i
def fields(b):
    i=0;n=len(b)
    while i<n:
        k,i=rv(b,i);f,w=k>>3,k&7
        if w==0:v,i=rv(b,i)
        elif w==2:
            l,i=rv(b,i);v=b[i:i+l];i+=l
        elif w==1:v=b[i:i+8];i+=8
        elif w==5:v=b[i:i+4];i+=4
        yield f,w,v
ops=collections.Counter();opset=None
for f,w,v in fields(d):
    if f==8:
        for g,_,x in fields(v):
            if g==2:opset=x
    if f==7:
        for g,_,x in fields(v):
            if g==1:
                for h,_,y in fields(x):
                    if h==4:ops[y.decode()]+=1
print("opset",opset);print(sorted(ops.items(),key=lambda t:-t[1]))
