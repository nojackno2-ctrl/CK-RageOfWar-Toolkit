"""
Cross-checks the nine bytes that CKPerf's kCellSites rewrite touches (src/CKPerf/hires.cpp).

The rewrite enlarges the CVXVisible dirty-cell from 16x16 to 32x32 pixels. CVXVisible+0x10
is a 75-row x 128-column bit grid; one row is one 16-byte slot (4 dwords = 128 bits) and one
bit is one screen cell, so at 16 px per cell the grid only spans 128 * 16 = 2048 px. Every
screen column at x >= 2048 therefore has no bit to be marked dirty with, is never repainted,
and smears as the camera scrolls. At 32 px per cell the same grid spans 4096 x 2400 px.

Producer 0x0047ABF0 converts pixels to cells, consumer 0x0047A020 converts cells back to
pixels; both directions have to move together, which is what these nine bytes do. Every other
`shl/sar reg,4` in 0x00478000..0x0047C600 is 16-byte slot or rectangle addressing and must be
left alone -- that classification is what this script exists to keep honest.

This script never writes to the executable. It reads the file, checks that all nine sites hold
the stock bytes, applies the rewrite to an in-memory copy, and (when capstone is installed)
proves that every patched site decodes to the intended instruction at the intended length, so
no instruction boundary can have shifted.

    python tools/perf/verify_cell_sites.py [path to "Celtic kings.exe"]

Exit code 0 = every check passed.
"""

import os
import struct
import sys

DEFAULT_GAME_DIR = r"C:\Program Files (x86)\Steam\steamapps\common\CK_RageOfWar"
EXE_NAME = "Celtic kings.exe"

# (VA, stock bytes, patched bytes, expected disassembly after patching, what it computes)
SITES = [
    (0x0047AC64, "C1F804", "C1F805", "sar eax, 5", "producer: startCol = (rect.left - viewLeft) / cell"),
    (0x0047AC78, "C1F904", "C1F905", "sar ecx, 5", "producer: endCol"),
    (0x0047AEE6, "C1FA04", "C1FA05", "sar edx, 5", "producer: firstRow"),
    (0x0047AF07, "C1FA04", "C1FA05", "sar edx, 5", "producer: lastRow"),
    (0x0047A7F1, "C1E304", "C1E305", "shl ebx, 5", "consumer: left = viewLeft + startCol * cell"),
    (0x0047A802, "C1E304", "C1E305", "shl ebx, 5", "consumer: right"),
    (0x0047A805, "8D5C2B0F", "8D5C2B1F", "lea ebx, [ebx + ebp + 0x1f]", "consumer: right += cell - 1"),
    (0x0047A814, "C1E304", "C1E305", "shl ebx, 5", "consumer: top"),
    (0x0047A822, "C1E104", "C1E105", "shl ecx, 5", "consumer: bottom"),
]

# Deliberately NOT patched, and listed here so a future reader does not "fix" them:
#   0x0047A825  lea ecx,[ecx+ebx-1]  -- the -1 is exclusive-to-inclusive, not the cell size
#   0x0047A122  cmp esi,0x7F         -- caps a bit index inside a 128-bit row mask
#   0x0047AC6F  mov esi,0x7F         -- saturating end column; the emitted rect is clamped
#                                       back to the view rect at 0x0047A846 anyway
#   every `add reg,0x10` in the two functions -- 16-byte slot / rectangle strides


def section_table(data):
    e_lfanew = struct.unpack_from("<I", data, 0x3C)[0]
    coff = e_lfanew + 4
    nsec = struct.unpack_from("<H", data, coff + 2)[0]
    optsz = struct.unpack_from("<H", data, coff + 16)[0]
    opt = coff + 20
    imagebase = struct.unpack_from("<I", data, opt + 28)[0]
    first = opt + optsz
    out = []
    for i in range(nsec):
        o = first + i * 40
        vsize, vaddr, rawsz, rawoff = struct.unpack_from("<IIII", data, o + 8)
        out.append((imagebase + vaddr, vsize, rawoff, rawsz))
    return out


def va_to_offset(sections, va):
    for vaddr, vsize, rawoff, rawsz in sections:
        if vaddr <= va < vaddr + vsize:
            delta = va - vaddr
            return rawoff + delta if delta < rawsz else None
    return None


def main(argv):
    path = argv[1] if len(argv) > 1 else os.path.join(DEFAULT_GAME_DIR, EXE_NAME)
    if not os.path.isfile(path):
        print("找不到執行檔：%s" % path)
        return 2

    data = open(path, "rb").read()
    sections = section_table(data)
    patched = bytearray(data)
    failures = 0

    try:
        from capstone import Cs, CS_ARCH_X86, CS_MODE_32
        md = Cs(CS_ARCH_X86, CS_MODE_32)
    except ImportError:
        md = None
        print("capstone 未安裝，略過反組譯驗證（位元組比對仍會執行）\n")

    for va, stock_hex, patch_hex, expect, what in SITES:
        stock = bytes.fromhex(stock_hex)
        patch = bytes.fromhex(patch_hex)
        off = va_to_offset(sections, va)
        if off is None:
            print("FAIL  0x%08X 無法對應到檔案位移" % va)
            failures += 1
            continue
        if len(patch) != len(stock):
            print("FAIL  0x%08X 長度改變 %d -> %d" % (va, len(stock), len(patch)))
            failures += 1
            continue
        have = data[off:off + len(stock)]
        if have != stock:
            print("FAIL  0x%08X 原始位元組不符：實際 %s，預期 %s（此執行檔不是預期的 Steam 版，"
                  "或已被其他工具改過）" % (va, have.hex().upper(), stock_hex))
            failures += 1
            continue
        patched[off:off + len(patch)] = patch
        note = ""
        if md is not None:
            ins = next(md.disasm(bytes(patched[off:off + 8]), va))
            got = "%s %s" % (ins.mnemonic, ins.op_str)
            if got != expect or ins.size != len(patch):
                print("FAIL  0x%08X 反組譯為 '%s'（長度 %d），預期 '%s'（長度 %d）"
                      % (va, got, ins.size, expect, len(patch)))
                failures += 1
                continue
            note = "  ->  %s" % got
        print("ok    0x%08X  %s -> %s%s\n              %s"
              % (va, stock_hex, patch_hex, note, what))

    changed = sum(1 for a, b in zip(data, patched) if a != b)
    print("\n改動位元組數：%d（預期 9）" % changed)
    if changed != len(SITES):
        failures += 1
    print("檔案大小未變：%s" % (len(patched) == len(data)))

    # No instruction boundary anywhere in either function may move.
    if md is not None:
        for start, end, name in ((0x0047A020, 0x0047A8C0, "consumer 0x0047A020"),
                                 (0x0047ABF0, 0x0047AF30, "producer 0x0047ABF0")):
            off = va_to_offset(sections, start)
            n = end - start
            before = [(i.address, i.size) for i in md.disasm(data[off:off + n], start)]
            after = [(i.address, i.size) for i in md.disasm(bytes(patched[off:off + n]), start)]
            same = before == after
            print("%s：%d 條指令，邊界完全一致：%s" % (name, len(after), same))
            if not same:
                failures += 1

    print("\n結果：%s" % ("通過" if failures == 0 else "失敗（%d 項）" % failures))
    return 0 if failures == 0 else 1


if __name__ == "__main__":
    sys.exit(main(sys.argv))
