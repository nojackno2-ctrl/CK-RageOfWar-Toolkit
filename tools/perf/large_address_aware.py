"""
Toggle the IMAGE_FILE_LARGE_ADDRESS_AWARE flag on "Celtic kings.exe".

The game is a 32-bit PE (MSVC 6, built 2004-02-19) without the flag, so on
64-bit Windows it is limited to a 2 GB user address space. It loads rle.mmp
(170 MB of hicolor RLE sprites) plus assets.pak (136 MB) and does its own
caching, so long sessions on large maps are the realistic place to hit that
ceiling. Setting the flag raises the limit to 4 GB.

Caveats worth knowing before enabling:
  * This is a one-bit change to the COFF characteristics field. It is safe
    for code that never treats pointers as signed, which is the normal case,
    but it is not provably safe for a binary this old -- test it.
  * The executable is Authenticode-signed; flipping the bit invalidates the
    signature. Windows does not enforce signatures on ordinary applications.
  * It does not make anything faster on its own. It only removes a ceiling.

Usage:
    py -3 large_address_aware.py --show
    py -3 large_address_aware.py --enable
    py -3 large_address_aware.py --restore
"""

import os
import shutil
import struct
import sys

GAME = r"C:\Program Files (x86)\Steam\steamapps\common\CK_RageOfWar"
NAME = 'Celtic kings.exe'
PROJECT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
BACKUP = os.path.join(PROJECT, 'backup')
LAA = 0x0020


def characteristics_offset(data):
    e_lfanew = struct.unpack_from('<I', data, 0x3c)[0]
    if data[e_lfanew:e_lfanew + 4] != b'PE\0\0':
        raise RuntimeError('not a PE file')
    return e_lfanew + 4 + 18          # COFF header + 18 = Characteristics


def show():
    data = open(os.path.join(GAME, NAME), 'rb').read()
    o = characteristics_offset(data)
    ch = struct.unpack_from('<H', data, o)[0]
    print('%s: Characteristics = 0x%04x' % (NAME, ch))
    print('  LARGE_ADDRESS_AWARE: %s' % ('ON' if ch & LAA else 'off'))


def enable():
    live = os.path.join(GAME, NAME)
    b = os.path.join(BACKUP, NAME + '.orig')
    os.makedirs(BACKUP, exist_ok=True)
    if not os.path.exists(b):
        shutil.copy2(live, b)
        print('backed up %s -> %s' % (NAME, b))
    data = bytearray(open(b, 'rb').read())
    o = characteristics_offset(data)
    ch = struct.unpack_from('<H', data, o)[0]
    struct.pack_into('<H', data, o, ch | LAA)
    with open(live, 'wb') as f:
        f.write(data)
    print('Characteristics 0x%04x -> 0x%04x (LARGE_ADDRESS_AWARE on)' % (ch, ch | LAA))


def restore():
    b = os.path.join(BACKUP, NAME + '.orig')
    if not os.path.exists(b):
        print('no backup at', b)
        return
    shutil.copy2(b, os.path.join(GAME, NAME))
    print('restored', NAME)


if __name__ == '__main__':
    a = sys.argv[1:]
    if not a or a[0] == '--show':
        show()
    elif a[0] == '--enable':
        enable()
    elif a[0] == '--restore':
        restore()
    else:
        print(__doc__)
