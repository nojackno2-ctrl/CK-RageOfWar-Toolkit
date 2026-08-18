# -*- coding: utf-8 -*-
"""HMMSYS PackFile (.pak / .bfhp) 讀寫器。

格式（逆向自 Celtic Kings: Rage of War，已對全部 6 個 pak 做過 byte-exact 往返驗證）::

    0x00  magic  b"HMMSYS PackFile\\n\\x1a"，補零到 0x20
    0x20  u32    fileCount
    0x24  u32    dirSize      目錄位元組數，從 0x28 起算
    0x28  目錄，每筆：
              u8   nameLen     完整檔名長度
              u8   prefixLen   與「前一筆檔名」共用的前綴長度
              u8   suffix[nameLen - prefixLen]
              u32  offset      payload 在檔案中的絕對位移
              u32  size        payload 位元組數
          u32  mtime[fileCount]    每檔一筆 DOS 日期時間
          payload 區，依目錄順序連續排列，無對齊、無間隙

檔名一律大寫、以反斜線分隔，對應遊戲內的 ``data/...`` 虛擬路徑。
"""

import struct

MAGIC = b'HMMSYS PackFile\n\x1a'
HEADER_SIZE = 0x28
DEFAULT_MTIME = 0x303765D0  # 2004-01-23 12:46:32，原版 data.pak 的時間戳


class PakError(Exception):
    pass


class Entry(object):
    __slots__ = ('name', 'offset', 'size', 'mtime')

    def __init__(self, name, offset, size, mtime=DEFAULT_MTIME):
        self.name = name
        self.offset = offset
        self.size = size
        self.mtime = mtime

    def __repr__(self):
        return '<Entry %s off=0x%X size=%d>' % (self.name, self.offset, self.size)


def _common_prefix(a, b):
    n = min(len(a), len(b), 255)
    i = 0
    while i < n and a[i] == b[i]:
        i += 1
    return i


def read_directory(data):
    """解析目錄，回傳 list[Entry]。"""
    if data[:len(MAGIC)] != MAGIC:
        raise PakError('不是 HMMSYS PackFile（magic 不符：%r）' % data[:16])

    count, dirsize = struct.unpack_from('<II', data, 0x20)
    dir_end = HEADER_SIZE + dirsize
    if dir_end > len(data):
        raise PakError('目錄長度超出檔案大小')

    entries = []
    pos = HEADER_SIZE
    prev = ''
    for _ in range(count):
        name_len = data[pos]
        prefix_len = data[pos + 1]
        if prefix_len > name_len:
            raise PakError('目錄損毀：prefixLen > nameLen @0x%X' % pos)
        suffix_end = pos + 2 + (name_len - prefix_len)
        name = prev[:prefix_len] + data[pos + 2:suffix_end].decode('latin-1')
        offset, size = struct.unpack_from('<II', data, suffix_end)
        if offset + size > len(data):
            raise PakError('項目 %s 的資料超出檔案範圍' % name)
        entries.append(Entry(name, offset, size))
        prev = name
        pos = suffix_end + 8

    if pos != dir_end:
        raise PakError('目錄尾端不對齊：0x%X != 0x%X' % (pos, dir_end))

    mtimes = struct.unpack_from('<%dI' % count, data, dir_end)
    for entry, mtime in zip(entries, mtimes):
        entry.mtime = mtime
    return entries


class Pak(object):
    """一個載入到記憶體的 pak。名稱查詢不分大小寫。"""

    def __init__(self, entries, blobs):
        self.entries = entries          # list[Entry]，保持原始順序
        self._blobs = blobs             # dict: 正規化名稱 -> bytes
        self._index = dict((e.name.upper(), e) for e in entries)

    # ---- 建立 ----------------------------------------------------------
    @classmethod
    def from_bytes(cls, data):
        entries = read_directory(data)
        blobs = {}
        for e in entries:
            blobs[e.name.upper()] = data[e.offset:e.offset + e.size]
        return cls(entries, blobs)

    @classmethod
    def load(cls, path):
        with open(path, 'rb') as f:
            return cls.from_bytes(f.read())

    # ---- 讀取 ----------------------------------------------------------
    def __contains__(self, name):
        return name.upper() in self._blobs

    def __len__(self):
        return len(self.entries)

    def names(self):
        return [e.name for e in self.entries]

    def read(self, name):
        key = name.upper()
        if key not in self._blobs:
            raise KeyError('pak 內沒有 %s' % name)
        return self._blobs[key]

    def read_text(self, name, encoding='latin-1'):
        return self.read(name).decode(encoding)

    # ---- 寫入 ----------------------------------------------------------
    def write(self, name, blob):
        """覆寫既有檔案，或新增一筆（新增時排在目錄最後）。"""
        if not isinstance(blob, bytes):
            raise TypeError('blob 必須是 bytes')
        key = name.upper()
        if key in self._blobs:
            self._blobs[key] = blob
            self._index[key].size = len(blob)
        else:
            entry = Entry(key, 0, len(blob))
            self.entries.append(entry)
            self._index[key] = entry
            self._blobs[key] = blob

    def write_text(self, name, text, encoding='latin-1'):
        self.write(name, text.encode(encoding))

    # ---- 序列化 --------------------------------------------------------
    def to_bytes(self):
        names = [e.name for e in self.entries]

        dirsize = 0
        prev = ''
        for name in names:
            dirsize += 2 + (len(name) - _common_prefix(prev, name)) + 8
            prev = name

        data_start = HEADER_SIZE + dirsize + 4 * len(names)

        out = bytearray(MAGIC)
        out += b'\x00' * (0x20 - len(MAGIC))
        out += struct.pack('<II', len(names), dirsize)

        offset = data_start
        prev = ''
        for entry in self.entries:
            name = entry.name
            prefix_len = _common_prefix(prev, name)
            blob = self._blobs[name.upper()]
            out += bytes((len(name), prefix_len))
            out += name[prefix_len:].encode('latin-1')
            out += struct.pack('<II', offset, len(blob))
            offset += len(blob)
            prev = name

        out += struct.pack('<%dI' % len(names), *[e.mtime for e in self.entries])
        assert len(out) == data_start, (len(out), data_start)

        for entry in self.entries:
            out += self._blobs[entry.name.upper()]
        return bytes(out)

    def save(self, path):
        with open(path, 'wb') as f:
            f.write(self.to_bytes())
