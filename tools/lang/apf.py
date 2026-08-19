"""APF（ABCF）點陣字型讀寫函式庫。

Celtic Kings: Rage of War 的 `local/fonts/*.apf` 格式，逆向結果：

檔頭
    0x00 char[4]  "ABCF"
    0x04 i32      unk1（同 metrics[3]，字體家族相關，原樣保留）
    0x08 i32      unk2 = 32       （產生器的最大字元格寬，原樣保留）
    0x0c i32      unk3 = 39 / 44  （產生器的最大字元格高，原樣保留）
    0x10 i32      unk4 = 31       （第一個字元碼）
    0x14 i32      unk5 = -1
    0x18 i32      unk6 = 1
    0x1c u32      metrics 區的位移（= 0x20 + 兩個字串長度）
    0x20 cstr     face name（GDI 字體名）
         cstr     family name
metrics（20 個 i32，實際上是兩段拼在一起）
    [0..4] 20 bytes 的 formatting 紀錄（檔頭 0x18 說明有幾筆，這裡都是 1 筆）
        [0] 字級（像素）  [1] italic  [2] bold
        [3] 字型資料區的檔案位移（= metrics 位移 + 20）
        [4] 字型資料區長度 —— 引擎用它一次讀進整塊，**含範圍表**，
            所以加了範圍就一定要改成 60 + 16 * 範圍數，否則新範圍不會生效
    [5..19] 字型資料區前 60 bytes
        [5] 行高  [6] 最大字形寬  [10] ascent  [11] descent
        [19] 字元範圍數 n
範圍表（n × 16 bytes）
    u32 offset  u32 size  u32 first_char  u32 char_count
    offset 相對於檔案開頭。

每個範圍區塊
    u32 kern_off    = 16 + 32 * char_count
    u32 kern_count
    u32 bitmap_off  = kern_off + 12 * kern_count
    u32 bitmap_size （bitmap_off + bitmap_size == 區塊大小）
    字形描述 × char_count（各 32 bytes，8 個 i32）
        [0] A 間距   [1] B 墨寬   [2] C 間距   [3] 0
        [4] top      [5] 寬度-1   [6] bottom   [7] 點陣資料位移
        寬 = [5] + 1，高 = [6] - [4] + 1；空白字元以 [5] = -1 表示
    字距對 × kern_count（各 12 bytes：first, second, amount）
    點陣資料（RLE）

點陣 RLE（整個 w×h 依列優先串接，run 可跨列）
    b < 0x20   → 灰階 0，長度 (b & 0x1F) + 1      （1..32）
    b >= 0xE0  → 灰階 14，長度 (b & 0x1F) + 1     （1..32）
    其他       → 灰階 b >> 4（2..13），長度 (b & 0x0F) + 1  （1..16）
    灰階範圍 0..14（14 為全不透明），無法表示 1 與 15。
"""

from __future__ import annotations

import struct
from dataclasses import dataclass, field

MAGIC = b"ABCF"
MAX_LEVEL = 14


# ------------------------------------------------------------------ RLE

def rle_decode(data: bytes, npixels: int) -> list[int]:
    out: list[int] = []
    for b in data:
        if b < 0x20:
            out += [0] * ((b & 0x1F) + 1)
        elif b >= 0xE0:
            out += [MAX_LEVEL] * ((b & 0x1F) + 1)
        else:
            out += [b >> 4] * ((b & 0x0F) + 1)
    if len(out) < npixels:          # 容忍原檔中極少數截斷的字形
        out += [0] * (npixels - len(out))
    return out[:npixels]


def rle_encode(pixels: list[int]) -> bytes:
    out = bytearray()
    i = 0
    n = len(pixels)
    while i < n:
        v = pixels[i]
        j = i
        while j < n and pixels[j] == v:
            j += 1
        count = j - i
        if v == 0:
            base, cap = 0x00, 32
        elif v >= MAX_LEVEL:
            base, cap = 0xE0, 32
        else:
            base, cap = v << 4, 16
        while count:
            take = min(count, cap)
            out.append(base | (take - 1))
            count -= take
        i = j
    return bytes(out)


# ------------------------------------------------------------------ 資料類別

@dataclass
class Glyph:
    a: int = 0            # 左間距（GDI 的 A）
    b: int = 0            # 墨寬（GDI 的 B）
    c: int = 0            # 右間距（GDI 的 C）
    top: int = 0          # 由行首往下的列
    width: int = 0        # 點陣寬（0 表示無點陣）
    height: int = 0       # 點陣高
    pixels: list[int] = field(default_factory=list)   # 長度 width*height，值 0..14

    @property
    def advance(self) -> int:
        return self.a + self.b + self.c


@dataclass
class Range:
    first: int
    glyphs: list[Glyph]
    kerning: bytes = b""          # 原樣保留的字距資料
    kern_count: int = 0

    @property
    def count(self) -> int:
        return len(self.glyphs)


class ApfFont:
    def __init__(self):
        self.unk = [0, 32, 44, 31, -1, 1]
        self.face = ""
        self.family = ""
        self.metrics = [0] * 20
        self.ranges: list[Range] = []

    # -------------------------------------------------------------- 讀取

    @classmethod
    def load(cls, data: bytes) -> "ApfFont":
        if data[:4] != MAGIC:
            raise ValueError("不是 APF 字型")
        f = cls()
        f.unk = list(struct.unpack_from("<6i", data, 4))
        mo = struct.unpack_from("<I", data, 0x1C)[0]
        p = 0x20
        e = data.index(b"\0", p); f.face = data[p:e].decode("cp1252"); p = e + 1
        e = data.index(b"\0", p); f.family = data[p:e].decode("cp1252"); p = e + 1
        if p != mo:
            raise ValueError("metrics 位移不符")
        f.metrics = list(struct.unpack_from("<20i", data, mo))
        rt = mo + 80
        for i in range(f.metrics[19]):
            off, size, first, count = struct.unpack_from("<IIII", data, rt + 16 * i)
            f.ranges.append(_load_range(data[off:off + size], first, count))
        return f

    # -------------------------------------------------------------- 寫出

    def dump(self) -> bytes:
        names = self.face.encode("cp1252") + b"\0" + self.family.encode("cp1252") + b"\0"
        mo = 0x20 + len(names)
        unk = list(self.unk)
        unk[0] = mo + 20                       # 與 metrics[3] 相同的字型資料區位移
        head = bytearray(MAGIC)
        head += struct.pack("<6i", *unk)
        head += struct.pack("<I", mo)
        head += names
        met = list(self.metrics)
        met[19] = len(self.ranges)
        # metrics[0..4] 其實是 20 bytes 的 formatting 紀錄，其中 [3] 是接在後面那塊
        # 「字型資料區」的檔案位移、[4] 是它的長度。引擎就照 [4] 這個長度把整塊
        # 讀進記憶體，範圍表也在裡面 —— 忘了更新的話新加的範圍根本不會被載入。
        met[3] = mo + 20
        met[4] = 60 + 16 * len(self.ranges)
        head += struct.pack("<20i", *met)

        blocks = [_dump_range(r) for r in self.ranges]
        base = len(head) + 16 * len(blocks)
        table = bytearray()
        off = base
        for r, blk in zip(self.ranges, blocks):
            table += struct.pack("<IIII", off, len(blk), r.first, r.count)
            off += len(blk)
        return bytes(head) + bytes(table) + b"".join(blocks)

    # -------------------------------------------------------------- 工具

    def covered(self) -> set[int]:
        out: set[int] = set()
        for r in self.ranges:
            out.update(range(r.first, r.first + r.count))
        return out

    def sort_ranges(self) -> None:
        self.ranges.sort(key=lambda r: r.first)


def _load_range(blk: bytes, first: int, count: int) -> Range:
    kern_off, kern_count, bmp_off, bmp_size = struct.unpack_from("<4I", blk, 0)
    raw = [struct.unpack_from("<8i", blk, 16 + 32 * i) for i in range(count)]
    glyphs = []
    for i, g in enumerate(raw):
        start = g[7]
        end = raw[i + 1][7] if i + 1 < count else bmp_size
        w = g[5] + 1
        h = g[6] - g[4] + 1
        if w <= 0 or h <= 0:
            w = h = 0
        gl = Glyph(a=g[0], b=g[1], c=g[2], top=g[4], width=w, height=h)
        gl.pixels = rle_decode(blk[bmp_off + start:bmp_off + end], w * h) if w * h else []
        glyphs.append(gl)
    return Range(first, glyphs, blk[kern_off:kern_off + 12 * kern_count], kern_count)


def _dump_range(r: Range) -> bytes:
    bitmaps = []
    table = bytearray()
    off = 0
    for g in r.glyphs:
        enc = rle_encode(g.pixels) if g.width and g.height else b""
        if g.width and g.height:
            top, bottom, wm1 = g.top, g.top + g.height - 1, g.width - 1
        else:
            top, bottom, wm1 = g.top, g.top - 1, -1
        table += struct.pack("<8i", g.a, g.b, g.c, 0, top, wm1, bottom, off)
        bitmaps.append(enc)
        off += len(enc)

    kern_off = 16 + 32 * len(r.glyphs)
    bmp_off = kern_off + len(r.kerning)
    blob = b"".join(bitmaps)
    head = struct.pack("<4I", kern_off, r.kern_count, bmp_off, len(blob))
    return head + bytes(table) + r.kerning + blob
