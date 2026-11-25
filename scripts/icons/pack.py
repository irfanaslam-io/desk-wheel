"""Packs the PNGs into AppIcon.icns (macOS) and AppIcon.ico (Windows), PNG-compressed, and copies
the in-app icons. No Mac needed: both formats are simple containers."""
import os, struct, shutil
here=os.path.dirname(os.path.abspath(__file__)); b=os.path.join(here,"build")
repo=os.path.abspath(os.path.join(here,"..",".."))
rd=lambda n: open(os.path.join(b,n),"rb").read()
# ICNS: 'icns' + total length, then (type, length, PNG) entries.
icns=[("icp4","mac-16.png"),("icp5","mac-32.png"),("icp6","mac-64.png"),("ic07","mac-128.png"),("ic08","mac-256.png"),
      ("ic09","mac-512.png"),("ic10","mac-1024.png"),("ic11","mac-32.png"),("ic12","mac-64.png"),("ic13","mac-256.png"),("ic14","mac-512.png")]
body=b"".join(t.encode()+struct.pack(">I",8+len(rd(f)))+rd(f) for t,f in icns)
os.makedirs(f"{repo}/platform/icons",exist_ok=True)
open(f"{repo}/platform/icons/AppIcon.icns","wb").write(b"icns"+struct.pack(">I",8+len(body))+body)
# ICO: header, directory, then PNG images (Vista and later read PNG entries at every size).
sizes=[16,20,24,32,40,48,64,256]; imgs=[rd(f"win-{s}.png") for s in sizes]
off=6+16*len(sizes); d=b""; data=b""
for s,img in zip(sizes,imgs):
    d+=struct.pack("<BBBBHHII",0 if s==256 else s,0 if s==256 else s,0,0,1,32,len(img),off+len(data)); data+=img
open(f"{repo}/platform/icons/AppIcon.ico","wb").write(struct.pack("<HHH",0,1,len(sizes))+d+data)
assets=f"{repo}/src/Wheel.App/Assets"; os.makedirs(assets,exist_ok=True)
for n in ["app-256.png","menubar-16.png","menubar-32.png","menubar-44.png","win-16.png","win-32.png","win-64.png"]:
    shutil.copy(os.path.join(b,n),os.path.join(assets,n))
shutil.copy(os.path.join(b,"mac.svg"),f"{repo}/platform/icons/AppIcon.svg")
print("icns", os.path.getsize(f"{repo}/platform/icons/AppIcon.icns"), "ico", os.path.getsize(f"{repo}/platform/icons/AppIcon.ico"))

# Microsoft Store (MSIX) images: tiles, Start/taskbar icons at each scale, Store logo.
# The 16-32 px taskbar sizes use the simplified art; "unplated" versions sit directly on the taskbar.
from PIL import Image
msix=f"{repo}/platform/windows/msix/Assets"; os.makedirs(msix,exist_ok=True)
big=Image.open(os.path.join(b,"win-1024.png")).convert("RGBA")
def icon(px):
    if px<=32: return Image.open(os.path.join(b,f"win-{16 if px<=16 else 32}.png")).convert("RGBA").resize((px,px),Image.LANCZOS)
    return big.resize((px,px),Image.LANCZOS)
def on_canvas(w,h,art):  # icon centred with room around it (tiles)
    c=Image.new("RGBA",(w,h),(0,0,0,0)); a=icon(art); c.paste(a,((w-art)//2,(h-art)//2),a); return c
out={}
for scale in (100,125,150,200,400):
    f=scale/100
    out[f"Square44x44Logo.scale-{scale}.png"]=icon(round(44*f))
    out[f"Square150x150Logo.scale-{scale}.png"]=on_canvas(round(150*f),round(150*f),round(100*f))
    out[f"Wide310x150Logo.scale-{scale}.png"]=on_canvas(round(310*f),round(150*f),round(100*f))
    out[f"StoreLogo.scale-{scale}.png"]=icon(round(50*f))
for t in (16,20,24,30,32,36,40,48,60,64,72,80,96,256):
    out[f"Square44x44Logo.targetsize-{t}.png"]=icon(t)
    out[f"Square44x44Logo.targetsize-{t}_altform-unplated.png"]=icon(t)
    out[f"Square44x44Logo.targetsize-{t}_altform-lightunplated.png"]=icon(t)
for n,im in out.items(): im.save(os.path.join(msix,n))
print("msix assets", len(out))
