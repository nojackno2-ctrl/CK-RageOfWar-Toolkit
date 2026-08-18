# -*- coding: utf-8 -*-
"""解開 Celtic Kings 的 LZIS / LZSS 壓縮檔（例如 config.ini）。

做法不是重寫演算法，而是**直接執行遊戲自己的解壓縮函式**：把
Celtic kings.exe 的區段載入 Unicorn 模擬器，呼叫 0x417340，
再把結果讀回來。這樣不必還原那套 bit-level + Huffman 的邏輯，
也不會因為猜錯參數而產生看似正確的錯誤輸出。

需求：  py -m pip install unicorn

用法：
    py tools/lzis_decompress.py <壓縮檔> [輸出檔]

檔案格式（逆向自 CCompressedFile / CLzisFile / CLzssFile）::

    0x00  magic "LZIS" 或 "LZSS"
    0x04  u32  未壓縮總長度
    0x08  u32  區塊大小（實際為 0x8000）
    0x0C  u8   壓縮等級（LZIS 為 1~3，config.ini 是 2）
    0x0D  u8   保留
    0x0E  u32[] 區塊位移表，table[0] 即壓縮資料的起始位移

已驗證：config.ini（LZIS 等級 2，1595 bytes）可完整還原。
多區塊的大檔（minimap.pak、randommap.pak）只解第一個區塊。

順帶一提：遊戲的檔案工廠在 magic 不是 LZIS/LZSS 時會倒回檔頭並直接
使用原始檔案（Celtic kings.exe 0x418796），所以**未壓縮的純文字檔
一樣讀得進去**，改設定不需要重新壓縮。
"""

import struct
import sys

try:
    from unicorn import Uc, UC_ARCH_X86, UC_MODE_32, UC_HOOK_CODE, UcError
    from unicorn.x86_const import UC_X86_REG_EAX, UC_X86_REG_EBP, \
        UC_X86_REG_EIP, UC_X86_REG_ESP
except ImportError:
    sys.exit('需要 unicorn 模擬器：py -m pip install unicorn')

DEFAULT_EXE = (r'C:\Program Files (x86)\Steam\steamapps\common'
               r'\CK_RageOfWar\Celtic kings.exe')

IMAGE_BASE = 0x400000
DECOMPRESS = 0x417340       # CCompressedFile 的解壓縮進入點
GET_ALLOCATOR = 0x41B480    # 取得記憶體配置器（這裡接管掉）

STACK, STACK_SIZE = 0x10000000, 0x100000
HEAP, HEAP_SIZE = 0x20000000, 0x800000
IO, IO_SIZE = 0x30000000, 0x400000

ALLOC_OBJ, ALLOC_VTBL = IO + 0x1000, IO + 0x1100
ALLOC_FN, FREE_FN, MAGIC_RET = IO + 0x2000, IO + 0x2010, IO + 0x3000
SRC, OUTLEN, DST = IO + 0x10000, IO + 0x8000, IO + 0x100000


def _load_image(uc, exe_path):
    data = open(exe_path, 'rb').read()
    pe = struct.unpack_from('<I', data, 0x3C)[0]
    nsec = struct.unpack_from('<H', data, pe + 6)[0]
    optsz = struct.unpack_from('<H', data, pe + 20)[0]
    image_size = struct.unpack_from('<I', data, pe + 24 + 56)[0]

    uc.mem_map(IMAGE_BASE, (image_size + 0xFFF) & ~0xFFF)
    table = pe + 24 + optsz
    for i in range(nsec):
        base = table + i * 40
        _vsize, va, rawsize, rawoff = struct.unpack_from('<IIII', data, base + 8)
        uc.mem_write(IMAGE_BASE + va, data[rawoff:rawoff + rawsize])


def decompress(src_bytes, capacity, exe_path=DEFAULT_EXE):
    """呼叫遊戲的解壓縮函式，回傳 (回傳碼, 解出的位元組)。回傳碼 0 為成功。"""
    uc = Uc(UC_ARCH_X86, UC_MODE_32)
    _load_image(uc, exe_path)
    for base, size in ((STACK, STACK_SIZE), (HEAP, HEAP_SIZE), (IO, IO_SIZE)):
        uc.mem_map(base, size)

    # 假的配置器：物件 -> vtable -> { +4: Alloc, +0xC: Free }
    uc.mem_write(ALLOC_OBJ, struct.pack('<I', ALLOC_VTBL))
    uc.mem_write(ALLOC_VTBL, struct.pack('<4I', 0, ALLOC_FN, 0, FREE_FN))
    uc.mem_write(ALLOC_FN, b'\xC3')
    uc.mem_write(FREE_FN, b'\xC3')
    uc.mem_write(MAGIC_RET, b'\xC3')

    uc.mem_write(SRC, src_bytes)
    uc.mem_write(OUTLEN, struct.pack('<I', capacity))

    next_free = [HEAP]

    def on_code(u, address, _size, _user):
        if address == GET_ALLOCATOR:
            esp = u.reg_read(UC_X86_REG_ESP)
            ret = struct.unpack('<I', u.mem_read(esp, 4))[0]
            u.reg_write(UC_X86_REG_EAX, ALLOC_OBJ)
            u.reg_write(UC_X86_REG_ESP, esp + 4)
            u.reg_write(UC_X86_REG_EIP, ret)
        elif address == ALLOC_FN:
            esp = u.reg_read(UC_X86_REG_ESP)
            ret, want = struct.unpack('<II', u.mem_read(esp, 8))
            ptr = next_free[0]
            next_free[0] = (ptr + want + 0xFFF) & ~0xFFF
            u.mem_write(ptr, b'\x00' * want)
            u.reg_write(UC_X86_REG_EAX, ptr)
            u.reg_write(UC_X86_REG_ESP, esp + 8)
            u.reg_write(UC_X86_REG_EIP, ret)
        elif address == FREE_FN:
            esp = u.reg_read(UC_X86_REG_ESP)
            ret = struct.unpack('<I', u.mem_read(esp, 4))[0]
            u.reg_write(UC_X86_REG_EAX, 0)
            u.reg_write(UC_X86_REG_ESP, esp + 8)
            u.reg_write(UC_X86_REG_EIP, ret)

    for addr in (GET_ALLOCATOR, ALLOC_FN, FREE_FN):
        uc.hook_add(UC_HOOK_CODE, on_code, begin=addr, end=addr)

    esp = STACK + STACK_SIZE - 0x1000
    args = struct.pack('<IIII', SRC, len(src_bytes), DST, OUTLEN)
    esp -= len(args)
    uc.mem_write(esp, args)
    esp -= 4
    uc.mem_write(esp, struct.pack('<I', MAGIC_RET))
    uc.reg_write(UC_X86_REG_ESP, esp)
    uc.reg_write(UC_X86_REG_EBP, esp)

    uc.emu_start(DECOMPRESS, MAGIC_RET, timeout=60_000_000)

    code = uc.reg_read(UC_X86_REG_EAX)
    length = struct.unpack('<I', uc.mem_read(OUTLEN, 4))[0]
    out = bytes(uc.mem_read(DST, min(length, capacity))) if length else b''
    return code, out


def main():
    if len(sys.argv) < 2:
        sys.exit(__doc__)

    path = sys.argv[1]
    data = open(path, 'rb').read()
    magic = data[:4]
    if magic not in (b'LZIS', b'LZSS'):
        sys.exit(f'{path} 不是 LZIS/LZSS 檔（magic={magic!r}）——'
                 f'可能本來就是未壓縮的，直接讀即可。')

    total = struct.unpack_from('<I', data, 4)[0]
    block = struct.unpack_from('<I', data, 8)[0]
    level = data[12]
    start = struct.unpack_from('<I', data, 14)[0]
    blocks = (total + block - 1) // block

    print(f'{path}')
    print(f'  格式 {magic.decode()}  等級 {level}  未壓縮 {total} bytes  '
          f'區塊 0x{block:X} × {blocks}')
    print(f'  壓縮資料起始位移 {start}')
    if blocks > 1:
        print(f'  注意：這個檔有 {blocks} 個區塊，本工具只解第一個。')

    code, out = decompress(data[start:], block)
    if code != 0 or not out:
        sys.exit(f'解壓縮失敗（回傳碼 {code}）')

    print(f'  解出 {len(out)} bytes')

    if len(sys.argv) > 2:
        open(sys.argv[2], 'wb').write(out)
        print(f'  已寫入 {sys.argv[2]}')
    else:
        sys.stdout.write(out.decode('latin-1'))


if __name__ == '__main__':
    main()
