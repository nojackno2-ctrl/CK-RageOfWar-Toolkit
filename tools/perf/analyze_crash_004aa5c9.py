"""
Disassembly and analysis of Celtic kings.exe around crash site 0x004AA5C9 using Capstone.

This tool reads 'Celtic kings.exe' in read-only binary mode. It never writes to the executable.
It disassembles the target function, verifies instruction bytes and mnemonics at EIP=0x004AA5C9,
identifies function boundaries, and scans the .text section for callers (xrefs).
"""

import os
import struct
import sys
import time

DEFAULT_GAME_DIR = r"C:\Program Files (x86)\Steam\steamapps\common\CK_RageOfWar"
EXE_NAME = "Celtic kings.exe"
TARGET_EIP = 0x004AA5C9
DISASM_RANGE_START = 0x004AA400
DISASM_RANGE_END = 0x004AA700

# Range of .text section to scan for xrefs
TEXT_START = 0x00401000
TEXT_END = 0x00706000


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
        name = data[o : o + 8].rstrip(b"\x00").decode("latin-1", errors="replace")
        vsize, vaddr, rawsz, rawoff = struct.unpack_from("<IIII", data, o + 8)
        out.append((name, imagebase + vaddr, vsize, rawoff, rawsz))
    return out


def va_to_offset(sections, va):
    for name, vaddr, vsize, rawoff, rawsz in sections:
        if vaddr <= va < vaddr + vsize:
            delta = va - vaddr
            return rawoff + delta if delta < rawsz else None
    return None


def run_analysis(exe_path):
    if not os.path.isfile(exe_path):
        print("找不到執行檔：%s" % exe_path)
        return None

    # Open strictly read-only
    with open(exe_path, "rb") as f:
        data = f.read()

    sections = section_table(data)

    try:
        from capstone import Cs, CS_ARCH_X86, CS_MODE_32
        md = Cs(CS_ARCH_X86, CS_MODE_32)
        md.detail = True
    except ImportError:
        print("錯誤：未安裝 capstone 套件")
        return None

    # 1. Disassemble 0x004AA400..0x004AA700
    off_range = va_to_offset(sections, DISASM_RANGE_START)
    if off_range is None:
        print("無法計算 0x%08X 的檔案位移" % DISASM_RANGE_START)
        return None

    length = DISASM_RANGE_END - DISASM_RANGE_START
    code_range = data[off_range : off_range + length]

    instructions = list(md.disasm(code_range, DISASM_RANGE_START))

    eip_ins = None
    for ins in instructions:
        if ins.address == TARGET_EIP:
            eip_ins = ins
            break

    # 2. Identify function containing 0x004AA5C9
    # Target function starts at 0x004AA4F0 (preceded by ret at 0x004AA4E8 + 7 nops)
    # Target function ends at 0x004AA69B (ret 8, followed by 2 nops, next func starts at 0x004AA6A0)
    func_start_va = 0x004AA4F0
    func_end_va = 0x004AA69E # exclusive

    func_off = va_to_offset(sections, func_start_va)
    func_bytes = data[func_off : func_off + (func_end_va - func_start_va)]
    func_instructions = list(md.disasm(func_bytes, func_start_va))

    # 3. Cross-reference (xrefs) search across .text section for calls to func_start_va
    text_off = va_to_offset(sections, TEXT_START)
    text_len = TEXT_END - TEXT_START
    text_bytes = data[text_off : text_off + text_len]

    xrefs = []
    target_va = func_start_va

    t0 = time.time()
    for i in range(len(text_bytes) - 5):
        if text_bytes[i] == 0xE8:
            rel32 = struct.unpack_from("<i", text_bytes, i + 1)[0]
            call_va = TEXT_START + i
            dest_va = (call_va + 5 + rel32) & 0xFFFFFFFF
            if dest_va == target_va:
                xrefs.append(call_va)
    t1 = time.time()

    verified_xrefs = []
    xref_contexts = {}
    for call_va in xrefs:
        win_start = max(TEXT_START, call_va - 32)
        win_off = va_to_offset(sections, win_start)
        win_bytes = data[win_off : win_off + 64]
        win_ins = list(md.disasm(win_bytes, win_start))

        is_valid_ins = any(ins.address == call_va and ins.mnemonic == "call" for ins in win_ins)
        if is_valid_ins:
            verified_xrefs.append(call_va)
            xref_contexts[call_va] = [ins for ins in win_ins if call_va - 24 <= ins.address <= call_va + 24]

    return {
        "sections": sections,
        "instructions_range": instructions,
        "eip_ins": eip_ins,
        "func_start_va": func_start_va,
        "func_end_va": func_end_va,
        "func_instructions": func_instructions,
        "scan_time": t1 - t0,
        "verified_xrefs": verified_xrefs,
        "xref_contexts": xref_contexts,
    }


def main(argv):
    path = argv[1] if len(argv) > 1 else os.path.join(DEFAULT_GAME_DIR, EXE_NAME)
    res = run_analysis(path)
    if res is None:
        return 1

    print("=== Celtic kings.exe Section Table ===")
    for name, vaddr, vsize, rawoff, rawsz in res["sections"]:
        print("  %-8s VA: 0x%08X..0x%08X (Size: 0x%X), FileOffset: 0x%08X..0x%08X (RawSize: 0x%X)"
              % (name, vaddr, vaddr + vsize, vsize, rawoff, rawoff + rawsz, rawsz))
    print()

    print("=== Linear Disassembly [0x%08X..0x%08X] ===" % (DISASM_RANGE_START, DISASM_RANGE_END))
    for ins in res["instructions_range"]:
        raw_hex = ins.bytes.hex().upper()
        marker = "===> [EIP FAULT] " if ins.address == TARGET_EIP else "                 "
        print("%s0x%08X:  %-16s  %-8s %s" % (marker, ins.address, raw_hex, ins.mnemonic, ins.op_str))
    print()

    print("=== EIP = 0x%08X 指令核對結果 ===" % TARGET_EIP)
    eip_ins = res["eip_ins"]
    if eip_ins:
        print("  指令位址  : 0x%08X" % eip_ins.address)
        print("  指令位元組: %s (長度 %d bytes)" % (eip_ins.bytes.hex().upper(), eip_ins.size))
        print("  助記符    : %s" % eip_ins.mnemonic)
        print("  運算元    : %s" % eip_ins.op_str)
        print("  完整反組譯: %s %s" % (eip_ins.mnemonic, eip_ins.op_str))

        guess_bytes = bytes.fromhex("83 3A 00")
        is_exact_match = (eip_ins.bytes == guess_bytes) and (eip_ins.mnemonic == "cmp")
        print("  手動粗讀核對: %s" % ("100% 精確吻合 (cmp dword ptr [edx], 0 / 83 3A 00)" if is_exact_match else "有出入"))
    else:
        print("  警告：在線性反組譯串流中未找到精確落在 0x%08X 的指令邊界！" % TARGET_EIP)
    print()

    print("=== 所屬函式邊界與完整反組譯 ===")
    print("  函式起始位址 (Entry VA) : 0x%08X" % res["func_start_va"])
    print("  函式結束位址 (End VA)   : 0x%08X" % res["func_end_va"])
    print("  函式大小 (Byte length)  : %d bytes" % (res["func_end_va"] - res["func_start_va"]))
    print("  指令總數 (Instruction count): %d 條" % len(res["func_instructions"]))
    print()
    print("=== 所屬函式反組譯 (0x%08X..0x%08X) ===" % (res["func_start_va"], res["func_end_va"]))
    for ins in res["func_instructions"]:
        raw_hex = ins.bytes.hex().upper()
        marker = "===> [EIP FAULT] " if ins.address == TARGET_EIP else "                 "
        print("%s0x%08X:  %-16s  %-8s %s" % (marker, ins.address, raw_hex, ins.mnemonic, ins.op_str))
    print()

    print("=== .text 區段呼叫點 (Xrefs to 0x%08X) ===" % res["func_start_va"])
    print("  掃描耗時: %.3f 秒，有效呼叫點數量: %d" % (res["scan_time"], len(res["verified_xrefs"])))
    for va in res["verified_xrefs"]:
        print("  - 0x%08X" % va)
    print()

    for call_va, ctx_insns in res["xref_contexts"].items():
        print("--- 呼叫點 [XREF] 0x%08X 上下文 ---" % call_va)
        for ins in ctx_insns:
            raw_hex = ins.bytes.hex().upper()
            marker = "===> [CALL SITE] " if ins.address == call_va else "                 "
            print("%s0x%08X:  %-16s  %-8s %s" % (marker, ins.address, raw_hex, ins.mnemonic, ins.op_str))
        print()

    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
