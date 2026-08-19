"""HMMSYS PackFile (.pak) 讀寫函式庫。

格式（Haemimont Games / Celtic Kings: Rage of War）:

    0x00  char[16]  "HMMSYS PackFile\n"
    0x10  u8        0x1A
    0x11  u8[15]    0
    0x20  u32       檔案數量 n
    0x24  u32       目錄區大小（bytes）
    0x28  ...       目錄區，n 筆:
                        u8  name_len      完整路徑長度
                        u8  prefix_len    與前一筆共用的前置字元數
                        u8[name_len - prefix_len] 路徑後綴（大寫、反斜線）
                        u32 offset        檔案資料位移（自檔頭起算）
                        u32 size          檔案資料長度
          u32[n]    每個檔案的時間戳
          ...       檔案資料

檔案資料為未壓縮的原始位元組。
"""

from __future__ import annotations

import os
import struct
from dataclasses import dataclass

MAGIC = b"HMMSYS PackFile\n"
HEADER_SIZE = 0x28
DEFAULT_TIMESTAMP = 0x2D7661D3


@dataclass
class PakEntry:
    name: str
    offset: int
    size: int
    timestamp: int = DEFAULT_TIMESTAMP


class PakFile:
    def __init__(self, entries: list[PakEntry], data: bytes = b""):
        self.entries = entries
        self._data = data

    # ---------------------------------------------------------------- 讀取

    @classmethod
    def load(cls, path: str) -> "PakFile":
        with open(path, "rb") as fh:
            raw = fh.read()
        return cls.from_bytes(raw)

    @classmethod
    def from_bytes(cls, raw: bytes) -> "PakFile":
        if raw[:16] != MAGIC:
            raise ValueError("不是 HMMSYS PackFile")
        if raw[0x10] != 0x1A:
            raise ValueError("檔頭標記錯誤")

        count, dir_size = struct.unpack_from("<II", raw, 0x20)
        pos = HEADER_SIZE
        dir_end = HEADER_SIZE + dir_size

        entries: list[PakEntry] = []
        prev = ""
        for _ in range(count):
            name_len = raw[pos]
            prefix_len = raw[pos + 1]
            pos += 2
            suffix_len = name_len - prefix_len
            suffix = raw[pos:pos + suffix_len].decode("cp437")
            pos += suffix_len
            offset, size = struct.unpack_from("<II", raw, pos)
            pos += 8
            name = prev[:prefix_len] + suffix
            prev = name
            entries.append(PakEntry(name, offset, size))

        if pos != dir_end:
            raise ValueError(f"目錄區長度不符: 讀到 {pos - HEADER_SIZE}，宣告 {dir_size}")

        for entry in entries:
            entry.timestamp = struct.unpack_from("<I", raw, pos)[0]
            pos += 4

        return cls(entries, raw)

    def read(self, entry: PakEntry) -> bytes:
        return self._data[entry.offset:entry.offset + entry.size]

    def find(self, name: str) -> PakEntry | None:
        key = name.upper().replace("/", "\\")
        for entry in self.entries:
            if entry.name.upper() == key:
                return entry
        return None

    def items(self):
        for entry in self.entries:
            yield entry.name, self.read(entry)

    # ---------------------------------------------------------------- 寫出

    @staticmethod
    def build(files: list[tuple[str, bytes, int]]) -> bytes:
        """files: (路徑, 內容, 時間戳) 的串列，順序即為目錄順序。"""
        # 目錄區大小需先算出，才能決定資料起始位移。
        dir_size = 0
        prev = ""
        encoded: list[tuple[int, int, bytes]] = []
        for name, _data, _ts in files:
            name = name.upper().replace("/", "\\")
            common = 0
            limit = min(len(prev), len(name), 255)
            while common < limit and prev[common] == name[common]:
                common += 1
            if len(name) > 255:
                raise ValueError(f"路徑過長（>255）: {name}")
            suffix = name[common:].encode("cp437")
            encoded.append((len(name), common, suffix))
            dir_size += 2 + len(suffix) + 8
            prev = name

        data_start = HEADER_SIZE + dir_size + 4 * len(files)

        out = bytearray()
        out += MAGIC
        out += bytes([0x1A]) + b"\x00" * 15
        out += struct.pack("<II", len(files), dir_size)

        offset = data_start
        for (name_len, prefix_len, suffix), (_n, data, _ts) in zip(encoded, files):
            out += bytes([name_len, prefix_len]) + suffix
            out += struct.pack("<II", offset, len(data))
            offset += len(data)

        for _n, _d, ts in files:
            out += struct.pack("<I", ts)

        for _n, data, _ts in files:
            out += data

        return bytes(out)

    def save_as(self, path: str, overrides: dict[str, bytes] | None = None) -> None:
        """以原有順序寫出，overrides 可置換個別檔案內容（鍵為大寫路徑）。"""
        overrides = {k.upper().replace("/", "\\"): v for k, v in (overrides or {}).items()}
        files = []
        for entry in self.entries:
            data = overrides.get(entry.name.upper(), self.read(entry))
            files.append((entry.name, data, entry.timestamp))
        blob = self.build(files)
        tmp = path + ".tmp"
        with open(tmp, "wb") as fh:
            fh.write(blob)
        os.replace(tmp, path)
