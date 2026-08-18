"""把 CJK 字形追加進原版 APF 字型。

原有的拉丁／斯拉夫字形一個位元組都不動，只在範圍表尾端接上新的字元範圍，
所以英文介面看起來與原版完全相同，中文則用系統中文字型即時光柵化產生。
"""

from __future__ import annotations

import apf
import gdifont

# 一定會收錄的標點與符號（不論翻譯內容用不用得到）
BASE_EXTRA = (
    list(range(0x3000, 0x3020))        # 全形空白、、。〈〉《》「」『』【】
    + list(range(0x3105, 0x312A))      # 注音符號
    + list(range(0xFF01, 0xFF5F))      # 全形 ASCII
    + [0xFF61, 0xFFE0, 0xFFE1, 0xFFE5]
    + [0x00B7, 0x2014, 0x2015, 0x2026]
)

FULL_CJK = list(range(0x4E00, 0xA000))

# 範圍之間允許的空隙；空隙內的字元會一併造字，換取更短的範圍表。
# 引擎查字形是線性掃描範圍表（提早在 first > ch 時中止），4 是檔案大小與
# 查表成本的折衷：約 680 個範圍、比實際用到的字多 29%。
RANGE_GAP = 4


def make_ranges(codepoints, gap: int = RANGE_GAP) -> list[tuple[int, int]]:
    """把散落的碼位合併成 (first, count) 範圍。"""
    cps = sorted(set(codepoints))
    out: list[tuple[int, int]] = []
    if not cps:
        return out
    start = prev = cps[0]
    for cp in cps[1:]:
        if cp - prev > gap:
            out.append((start, prev - start + 1))
            start = cp
        prev = cp
    out.append((start, prev - start + 1))
    return out


def add_cjk(font: apf.ApfFont, codepoints, face: str,
            size_delta: int = 0, bold: bool | None = None,
            on_progress=None) -> tuple[int, list[int]]:
    """就地把 codepoints 加進 font，回傳 (新增字形數, 缺字清單)。"""
    size = font.metrics[0] + size_delta
    is_bold = bool(font.metrics[2]) if bold is None else bold
    baseline = font.metrics[10]                    # 沿用原字型的 ascent 當基線

    already = font.covered()
    wanted = sorted(cp for cp in set(codepoints) if cp not in already)
    if not wanted:
        return 0, []

    wanted_set = set(wanted)
    missing: list[int] = []
    added = 0
    done = 0
    max_width = font.metrics[6]

    gf = gdifont.GdiFont(face, size, bold=is_bold, baseline=baseline)
    try:
        if gf.actual_face.lower() != face.lower():
            raise OSError(f"系統沒有字型「{face}」（實際取得「{gf.actual_face}」）")
        for first, count in make_ranges(wanted):
            glyphs = []
            for cp in range(first, first + count):
                res = gf.glyph(cp)
                if res is None:
                    if cp in wanted_set:
                        missing.append(cp)
                    glyphs.append(apf.Glyph(top=baseline))
                    continue
                a, b, c, top, w, h, levels = res
                glyphs.append(apf.Glyph(a=a, b=b, c=c, top=top,
                                        width=w, height=h, pixels=levels))
                if w:
                    max_width = max(max_width, w)
                added += 1
                done += 1
                if on_progress and done % 500 == 0:
                    on_progress(done)
            font.ranges.append(apf.Range(first, glyphs))
    finally:
        gf.close()

    font.metrics[6] = max_width
    font.sort_ranges()
    return added, missing


def charset_from_text(*texts) -> set[int]:
    """從翻譯文字收集需要造字的碼位（只收非 ASCII 的部分）。"""
    out: set[int] = set()
    for t in texts:
        for ch in t:
            cp = ord(ch)
            if cp > 0x2FF:
                out.add(cp)
    out.update(BASE_EXTRA)
    return out
