"""用 Windows GDI 光柵化字形，產生 APF 需要的灰階點陣與 ABC 間距。

只用 ctypes，不需要任何第三方套件。原始 APF 的灰階只有 0..14 且多為偶數，
與 GDI 的 GGO_GRAY8_BITMAP（0..64）縮放後吻合，等於沿用當年製作工具的做法。
"""

from __future__ import annotations

import ctypes
from ctypes import wintypes

gdi32 = ctypes.WinDLL("gdi32", use_last_error=True)
user32 = ctypes.WinDLL("user32", use_last_error=True)

GGO_METRICS = 0
GGO_GRAY8_BITMAP = 6
GDI_ERROR = 0xFFFFFFFF

ANTIALIASED_QUALITY = 4
CLEARTYPE_QUALITY = 5
DEFAULT_CHARSET = 1
OUT_TT_PRECIS = 4
CLIP_DEFAULT_PRECIS = 0
DEFAULT_PITCH = 0


class FIXED(ctypes.Structure):
    _fields_ = [("fract", wintypes.WORD), ("value", ctypes.c_short)]


class MAT2(ctypes.Structure):
    _fields_ = [("eM11", FIXED), ("eM12", FIXED), ("eM21", FIXED), ("eM22", FIXED)]


class GLYPHMETRICS(ctypes.Structure):
    _fields_ = [
        ("gmBlackBoxX", wintypes.UINT),
        ("gmBlackBoxY", wintypes.UINT),
        ("gmptGlyphOrigin", wintypes.POINT),
        ("gmCellIncX", ctypes.c_short),
        ("gmCellIncY", ctypes.c_short),
    ]


class LOGFONTW(ctypes.Structure):
    _fields_ = [
        ("lfHeight", wintypes.LONG), ("lfWidth", wintypes.LONG),
        ("lfEscapement", wintypes.LONG), ("lfOrientation", wintypes.LONG),
        ("lfWeight", wintypes.LONG), ("lfItalic", wintypes.BYTE),
        ("lfUnderline", wintypes.BYTE), ("lfStrikeOut", wintypes.BYTE),
        ("lfCharSet", wintypes.BYTE), ("lfOutPrecision", wintypes.BYTE),
        ("lfClipPrecision", wintypes.BYTE), ("lfQuality", wintypes.BYTE),
        ("lfPitchAndFamily", wintypes.BYTE), ("lfFaceName", wintypes.WCHAR * 32),
    ]


class TEXTMETRICW(ctypes.Structure):
    _fields_ = [
        ("tmHeight", wintypes.LONG), ("tmAscent", wintypes.LONG),
        ("tmDescent", wintypes.LONG), ("tmInternalLeading", wintypes.LONG),
        ("tmExternalLeading", wintypes.LONG), ("tmAveCharWidth", wintypes.LONG),
        ("tmMaxCharWidth", wintypes.LONG), ("tmWeight", wintypes.LONG),
        ("tmOverhang", wintypes.LONG), ("tmDigitizedAspectX", wintypes.LONG),
        ("tmDigitizedAspectY", wintypes.LONG), ("tmFirstChar", wintypes.WCHAR),
        ("tmLastChar", wintypes.WCHAR), ("tmDefaultChar", wintypes.WCHAR),
        ("tmBreakChar", wintypes.WCHAR), ("tmItalic", wintypes.BYTE),
        ("tmUnderlined", wintypes.BYTE), ("tmStruckOut", wintypes.BYTE),
        ("tmPitchAndFamily", wintypes.BYTE), ("tmCharSet", wintypes.BYTE),
    ]


gdi32.CreateFontIndirectW.restype = wintypes.HFONT
gdi32.CreateFontIndirectW.argtypes = [ctypes.c_void_p]
gdi32.CreateCompatibleDC.restype = wintypes.HDC
gdi32.CreateCompatibleDC.argtypes = [wintypes.HDC]
gdi32.DeleteDC.argtypes = [wintypes.HDC]
gdi32.DeleteObject.argtypes = [wintypes.HGDIOBJ]
gdi32.SelectObject.restype = wintypes.HGDIOBJ
gdi32.SelectObject.argtypes = [wintypes.HDC, wintypes.HGDIOBJ]
gdi32.GetTextMetricsW.argtypes = [wintypes.HDC, ctypes.c_void_p]
gdi32.GetTextFaceW.argtypes = [wintypes.HDC, ctypes.c_int, ctypes.c_wchar_p]
gdi32.GetGlyphOutlineW.restype = wintypes.DWORD
gdi32.GetGlyphOutlineW.argtypes = [wintypes.HDC, wintypes.UINT, wintypes.UINT,
                                   ctypes.c_void_p, wintypes.DWORD, ctypes.c_void_p,
                                   ctypes.c_void_p]

_IDENTITY = MAT2(FIXED(0, 1), FIXED(0, 0), FIXED(0, 0), FIXED(0, 1))

GRAY8_TO_LEVEL = [min(((v + 4) // 9) * 2, 14) for v in range(65)]


class GdiFont:
    """以指定字體名與像素字級開啟一個 GDI 字型。"""

    def __init__(self, face: str, pixel_size: int, bold: bool = False,
                 italic: bool = False, cleartype: bool = False,
                 baseline: int | None = None):
        lf = LOGFONTW()
        lf.lfHeight = -abs(pixel_size)
        lf.lfWeight = 700 if bold else 400
        lf.lfItalic = 1 if italic else 0
        lf.lfCharSet = DEFAULT_CHARSET
        lf.lfOutPrecision = OUT_TT_PRECIS
        lf.lfQuality = CLEARTYPE_QUALITY if cleartype else ANTIALIASED_QUALITY
        lf.lfFaceName = face
        self.hfont = gdi32.CreateFontIndirectW(ctypes.byref(lf))
        if not self.hfont:
            raise OSError(f"無法建立字型 {face} {pixel_size}px")
        self.hdc = gdi32.CreateCompatibleDC(None)
        self._old = gdi32.SelectObject(self.hdc, self.hfont)

        tm = TEXTMETRICW()
        gdi32.GetTextMetricsW(self.hdc, ctypes.byref(tm))
        self.tm = tm
        self.ascent = tm.tmAscent
        self.descent = tm.tmDescent
        self.height = tm.tmHeight
        # 混排時所有字型必須共用同一條基線，否則中文會相對英文上下亂跳。
        self.baseline = tm.tmAscent if baseline is None else baseline
        buf = ctypes.create_unicode_buffer(64)
        gdi32.GetTextFaceW(self.hdc, 64, buf)
        self.actual_face = buf.value

    def close(self):
        if self.hdc:
            gdi32.SelectObject(self.hdc, self._old)
            gdi32.DeleteDC(self.hdc)
            self.hdc = None
        if self.hfont:
            gdi32.DeleteObject(self.hfont)
            self.hfont = None

    def __enter__(self):
        return self

    def __exit__(self, *a):
        self.close()

    def glyph(self, codepoint: int):
        """回傳 (a, b, c, top, width, height, levels)，levels 為 0..14 的一維串列。

        找不到字形時回傳 None。
        """
        gm = GLYPHMETRICS()
        need = gdi32.GetGlyphOutlineW(self.hdc, codepoint, GGO_GRAY8_BITMAP,
                                      ctypes.byref(gm), 0, None, ctypes.byref(_IDENTITY))
        if need == GDI_ERROR:
            return None
        if need == 0:                                   # 無墨（空白字元）
            adv = gm.gmCellIncX
            return (0, max(adv, 0), 0, self.baseline, 0, 0, [])

        buf = (ctypes.c_ubyte * need)()
        got = gdi32.GetGlyphOutlineW(self.hdc, codepoint, GGO_GRAY8_BITMAP,
                                     ctypes.byref(gm), need, buf, ctypes.byref(_IDENTITY))
        if got == GDI_ERROR:
            return None

        w, h = gm.gmBlackBoxX, gm.gmBlackBoxY
        pitch = (w + 3) & ~3
        levels = []
        for y in range(h):
            row = buf[y * pitch:y * pitch + w]
            # GGO_GRAY8 的值域是 0..64。原始字型的灰階只有 0,2,4,...,14 八階，
            # 用原檔逐像素比對還原出的量化規則就是 ((raw + 4) // 9) * 2。
            levels.extend(GRAY8_TO_LEVEL[v] for v in row)

        a = gm.gmptGlyphOrigin.x
        c = gm.gmCellIncX - a - w
        top = self.baseline - gm.gmptGlyphOrigin.y
        return (a, w, c, top, w, h, levels)


def font_exists(face: str) -> bool:
    with GdiFont(face, 16) as f:
        return f.actual_face.lower() == face.lower()
