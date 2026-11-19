"""Independent re-implementation of the Phase 3 baseline colour math (numpy), compared with the matrices and
shader outputs that Unity's ColourAutoTest wrote to colour_report.json. Matrices: Machado et al. 2009 severity 1.0.
Usage: verify_colour_math.py colour_report.json"""
import json,sys,numpy as np
P=np.array([[0.152286,1.052583,-0.204868],[0.114503,0.786281,0.099216],[-0.003882,-0.048116,1.051998]])
D=np.array([[0.367322,0.860646,-0.227968],[0.280085,0.672501,0.047413],[-0.011820,0.042940,0.968881]])
T=np.array([[1.255528,-0.076749,-0.178779],[-0.078411,0.930809,0.147602],[0.004733,0.691367,0.303900]])
A=np.tile([0.2126,0.7152,0.0722],(3,1)); I=np.eye(3)
EPD=np.array([[0,0,0],[0.7,1,0],[0.7,0,1]],float); ET=np.array([[1,0,0.7],[0,1,0.7],[0,0,0]],float)
def sim(t,s):
    full={"Protanopia":P,"Deuteranopia":D,"Tritanopia":T}
    if t in full:return full[t]
    base={"Protanomaly":P,"Deuteranomaly":D,"Tritanomaly":T,"Achromatopsia":A}
    return I+s*(base[t]-I) if t in base else I
def shift(t): return EPD if t in("Protanopia","Protanomaly","Deuteranopia","Deuteranomaly") else ET if t in("Tritanopia","Tritanomaly") else np.zeros((3,3))
MODE={"Balanced":(1,1),"MaxDistinguishability":(1.6,1),"NaturalAppearance":(.5,1),"HighContrast":(1,1.3)}
def enc(c):c=np.clip(c,0,1);return np.where(c<=0.0031308,12.92*c,1.055*np.power(c,1/2.4)-0.055)
def dec(c):return np.where(c<=0.04045,c/12.92,((c+0.055)/1.055)**2.4)
r=json.load(open(sys.argv[1]));worst_m=0;worst_px=0;n=0
for c in r["cases"]:
    S=sim(c["type"],c["severity"]);mul,con=MODE[c["mode"]];k=min(2,c["strength"]*mul)
    C=I+k*shift(c["type"])@(I-S)
    um_s=np.array(c["sim_matrix"]).reshape(3,3);um_c=np.array(c["corr_matrix"]).reshape(3,3)
    worst_m=max(worst_m,np.abs(S-um_s).max(),np.abs(C-um_c).max())
    for p in c["patches"]:   # patches: srgb in, srgb out (8-bit) from the shader for sim and corrected
        lin=dec(np.array(p["in"])/255.0)
        exp_s=enc(S@lin)*255; l=C@lin; l=0.214+(l-0.214)*con; exp_c=enc(l)*255
        worst_px=max(worst_px,np.abs(exp_s-np.array(p["sim_out"])).max(),np.abs(exp_c-np.array(p["corr_out"])).max());n+=1
print(f"cases {len(r['cases'])}, patches {n}: max |matrix diff| {worst_m:.2e}, max |8-bit shader diff| {worst_px:.2f} levels")
print("PASS" if worst_m<1e-4 and worst_px<=1.5 else "FAIL")
