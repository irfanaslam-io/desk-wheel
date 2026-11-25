"""Desk Wheel icon: eight tool tiles around the orange button.
Writes SVG sources; render.js turns them into PNGs; pack.py builds AppIcon.icns / AppIcon.ico.
  python3 make_icon.py && node render.js && python3 pack.py"""
import math, os
OUT=os.path.join(os.path.dirname(__file__), "build"); os.makedirs(OUT, exist_ok=True)
C=512
def pt(r,a):
    a=math.radians(a-90); return C+r*math.cos(a), C+r*math.sin(a)
DEFS='''<defs>
<linearGradient id="navy" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#26304A"/><stop offset="1" stop-color="#0D1220"/></linearGradient>
<linearGradient id="orange" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#FF8F52"/><stop offset="1" stop-color="#E2531F"/></linearGradient>
<linearGradient id="tile" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#34405E"/><stop offset="1" stop-color="#27304A"/></linearGradient>
<linearGradient id="gloss" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#FFFFFF" stop-opacity=".10"/><stop offset=".5" stop-color="#FFFFFF" stop-opacity="0"/></linearGradient>
<filter id="sh" x="-40%" y="-40%" width="180%" height="180%"><feDropShadow dx="0" dy="12" stdDeviation="14" flood-color="#000" flood-opacity=".45"/></filter>
<filter id="tsh" x="-20%" y="-20%" width="140%" height="150%"><feDropShadow dx="0" dy="6" stdDeviation="6" flood-color="#000" flood-opacity=".35"/></filter>
</defs>'''
def glyph(kind,col):
    s=f'fill="none" stroke="{col}" stroke-width="9" stroke-linecap="round" stroke-linejoin="round"'; f=f'fill="{col}"'
    return {
     'photo': f'<rect x="-40" y="-32" width="80" height="64" rx="10" {s}/><circle cx="16" cy="-12" r="8" {f}/><path d="M-36,26 L-10,-2 L8,16 L18,6 L36,26" {s}/>',
     'video': f'<rect x="-42" y="-30" width="84" height="60" rx="12" {s}/><path d="M-10,-14 L16,0 L-10,14 Z" {f}/>',
     'music': f'<path d="M-6,22 V-34 L30,-42 V14" {s}/><circle cx="-18" cy="24" r="13" {f}/><circle cx="18" cy="16" r="13" {f}/>',
     'doc':   f'<path d="M-28,-42 H12 L32,-22 V42 H-28 Z" {s}/><path d="M-14,0 H18 M-14,20 H18" {s}/>',
     'camera':f'<path d="M-42,-20 H-20 L-12,-32 H12 L20,-20 H42 V32 H-42 Z" {s}/><circle cx="0" cy="6" r="15" {s}/>',
     'clip':  f'<rect x="-30" y="-34" width="60" height="76" rx="9" {s}/><rect x="-14" y="-44" width="28" height="18" rx="6" {f}/><path d="M-14,4 H14 M-14,22 H8" {s}/>',
     'text':  f'<path d="M-30,-34 H30 M0,-34 V38" stroke="{col}" stroke-width="13" stroke-linecap="round" fill="none"/>',
     'qr':    f'<rect x="-40" y="-40" width="32" height="32" rx="5" {s}/><rect x="8" y="-40" width="32" height="32" rx="5" {s}/><rect x="-40" y="8" width="32" height="32" rx="5" {s}/><rect x="10" y="10" width="12" height="12" {f}/><rect x="28" y="28" width="12" height="12" {f}/><rect x="28" y="10" width="12" height="12" {f}/>',
    }[kind]
KINDS=['photo','video','music','doc','camera','clip','text','qr']

def art(detail, view):
    """detail: glyphs on tiles (big sizes) or plain tiles (16-32 px). view: viewBox (Mac keeps Apple's margin)."""
    b=f'<rect x="100" y="100" width="824" height="824" rx="186" fill="url(#navy)"/>'
    b+=f'<rect x="100" y="100" width="824" height="824" rx="186" fill="url(#gloss)"/>'
    if detail:
        size,rad,ring=156,40,262
    else:
        size,rad,ring=176,48,262   # bigger, simpler tiles stay readable when tiny
    for k,kind in enumerate(KINDS):
        x,y=pt(ring,k*45)
        b+=f'<rect x="{x-size/2:.1f}" y="{y-size/2:.1f}" width="{size}" height="{size}" rx="{rad}" fill="{"url(#orange)" if k==0 else "url(#tile)"}" filter="url(#tsh)"/>'
        if detail:
            b+=f'<g transform="translate({x:.1f},{y:.1f}) scale(1.05)">{glyph(kind,"#FFFFFF" if k==0 else "#E3E8F1")}</g>'
    hub=78 if detail else 96
    b+=f'<circle cx="512" cy="512" r="{hub}" fill="url(#orange)" filter="url(#sh)"/><circle cx="512" cy="512" r="{hub*0.38:.0f}" fill="#0D1220"/>'
    return f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="{view}">{DEFS}{b}</svg>'

MAC="0 0 1024 1024"          # Apple's icon grid: 824 px tile with room for the shadow
WIN="88 88 848 848"          # Windows: the tile fills the square
open(f"{OUT}/mac.svg","w").write(art(True,MAC))
open(f"{OUT}/mac-small.svg","w").write(art(False,MAC))
open(f"{OUT}/win.svg","w").write(art(True,WIN))
open(f"{OUT}/win-small.svg","w").write(art(False,WIN))

# Menu bar (macOS template image: black + transparency, macOS tints it) and the tray.
def bar():
    b='<mask id="m"><rect width="1024" height="1024" fill="#fff"/><circle cx="512" cy="512" r="50" fill="#000"/></mask><g mask="url(#m)">'
    for k in range(8):
        x,y=pt(330,k*45); s=200; op=1 if k==0 else .55
        b+=f'<rect x="{x-s/2:.1f}" y="{y-s/2:.1f}" width="{s}" height="{s}" rx="56" fill="#000" opacity="{op}"/>'
    b+='<circle cx="512" cy="512" r="130" fill="#000"/></g>'
    return f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="40 40 944 944">{b}</svg>'
open(f"{OUT}/menubar.svg","w").write(bar())
print("svg ok")
