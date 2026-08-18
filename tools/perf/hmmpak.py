"""
HMMSYS PackFile (.pak) reader / in-place patcher.

Format (uncompressed variant, used by data.pak / local.pak / assets.pak /
outlines.pak / PATCH1.PAK / update.pak):

    0x00  char[16]  "HMMSYS PackFile\n"
    0x10  u32       header size (0x1a)
    0x14  u32[3]    reserved / zero
    0x20  u32       file count
    0x24  u32       directory size in bytes
    0x28  ...       directory entries, then raw file data

Each directory entry uses front-coding against the previous name:

    u8   total_len     total length of this entry's name
    u8   shared_len    number of leading chars shared with the previous name
    u8[total_len - shared_len]  the remaining name characters
    u32  offset        absolute file offset of the data
    u32  size          size in bytes

Note: minimap.pak and randommap.pak start with "LZIS"/"LZSS" instead and are
whole-file compressed containers; this module does not handle those.
"""

import struct

MAGIC = b'HMMSYS PackFile'


class Pak:
    def __init__(self, path):
        self.path = path
        with open(path, 'rb') as f:
            self.data = bytearray(f.read())
        if bytes(self.data[:15]) != MAGIC:
            raise ValueError('%s is not an HMMSYS PackFile' % path)
        self.count, self.dirsize = struct.unpack_from('<II', self.data, 0x20)
        self.entries = []          # [name, offset, size, entry_offset]
        o = 0x28
        prev = ''
        for _ in range(self.count):
            total, shared = self.data[o], self.data[o + 1]
            o += 2
            new = total - shared
            name = prev[:shared] + self.data[o:o + new].decode('latin1')
            o += new
            off, size = struct.unpack_from('<II', self.data, o)
            self.entries.append([name, off, size, o])
            o += 8
            prev = name
        self.dir_end = o

    def find(self, name):
        key = name.upper().replace('/', '\\')
        for e in self.entries:
            if e[0].upper().replace('/', '\\') == key:
                return e
        return None

    def read(self, name):
        e = self.find(name)
        if e is None:
            raise KeyError(name)
        return bytes(self.data[e[1]:e[1] + e[2]])

    def replace(self, name, content):
        """Append `content` at the end of the archive and repoint the directory
        entry. The old bytes stay behind as dead space -- this keeps every other
        entry's offset untouched, so the edit is minimal and easy to reason about.
        """
        e = self.find(name)
        if e is None:
            raise KeyError(name)
        new_off = len(self.data)
        self.data.extend(content)
        struct.pack_into('<II', self.data, e[3], new_off, len(content))
        e[1], e[2] = new_off, len(content)

    def save(self, path=None):
        with open(path or self.path, 'wb') as f:
            f.write(self.data)


if __name__ == '__main__':
    import sys
    p = Pak(sys.argv[1])
    pat = (sys.argv[2] if len(sys.argv) > 2 else '').upper()
    print('%d files, directory ends at 0x%06x' % (p.count, p.dir_end))
    for name, off, size, _ in p.entries:
        if pat and pat not in name.upper():
            continue
        print('  %-56s off %08x size %8d' % (name, off, size))
