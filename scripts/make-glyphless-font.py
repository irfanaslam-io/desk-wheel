# Makes src/Wheel.Core/Pdf/GlyphlessFont.ttf, the font of the invisible text layer in searchable PDFs.
# Two glyphs, both a flat line (no area, so nothing is ever drawn) with a full-size box, 500 units wide.
# Needs fontTools:  pip install fonttools   then:  python3 scripts/make-glyphless-font.py src/Wheel.Core/Pdf/GlyphlessFont.ttf
from fontTools.fontBuilder import FontBuilder
from fontTools.pens.ttGlyphPen import TTGlyphPen
import sys
fb = FontBuilder(1000, isTTF=True)
fb.setupGlyphOrder([".notdef", "space"])
fb.setupCharacterMap({0x20: "space"})
def flat():
    # Three points on one line: a contour with no area, so nothing is ever drawn, but a full-size box
    # (PDF readers use glyph boxes to find rotated text).
    pen = TTGlyphPen(None)
    pen.moveTo((0, -200)); pen.lineTo((250, 300)); pen.lineTo((500, 800)); pen.closePath()
    return pen.glyph()
fb.setupGlyf({".notdef": flat(), "space": flat()})
fb.setupHorizontalMetrics({".notdef": (500, 0), "space": (500, 0)})
fb.setupHorizontalHeader(ascent=800, descent=-200)
fb.setupNameTable({"familyName": "DeskWheelOcr", "styleName": "Regular", "copyright": "Copyright (c) 2026 Muhammad Irfan Aslam", "psName": "DeskWheelOcr-Regular"})
fb.setupOS2(sTypoAscender=800, sTypoDescender=-200, usWinAscent=800, usWinDescent=200)
fb.setupPost()
fb.save(sys.argv[1])
