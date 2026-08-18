"""
Add extra resolutions to Celtic Kings: Rage of War.

The engine builds its resolution list at runtime from data/vxConst.ini:

    Res1_x = 1024
    Res1_y = 768
    ...

The reader (0x0065_82d0 in Celtic kings.exe) is an open-ended loop -- it keeps
asking for "Res%d_x" until the key is missing -- so the list has no built-in
limit. vxConst.ini lives inside data.pak as VXCONST.INI.

Existing entries are never renumbered: vxSettings.ini stores the chosen
resolution as an *index* into this list, so new entries are only ever appended.

Usage:
    py -3 add_resolutions.py --list
    py -3 add_resolutions.py --apply
    py -3 add_resolutions.py --apply 1920x1080 2560x1440
    py -3 add_resolutions.py --restore
"""

import os
import re
import shutil
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from hmmpak import Pak

GAME = r"C:\Program Files (x86)\Steam\steamapps\common\CK_RageOfWar"
PROJECT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
BACKUP = os.path.join(PROJECT, 'backup')

DEFAULT_ADD = [(1280, 720), (1600, 900), (1920, 1080), (1920, 1200), (2560, 1440)]


def backup_of(filename):
    return os.path.join(BACKUP, filename + '.orig')


def ensure_backup(filename):
    """Back up the pristine file once, and always patch *from* that backup so
    repeated runs stay idempotent instead of stacking edits."""
    src = os.path.join(GAME, filename)
    dst = backup_of(filename)
    os.makedirs(BACKUP, exist_ok=True)
    if not os.path.exists(dst):
        shutil.copy2(src, dst)
        print('backed up %s -> %s' % (filename, dst))
    return dst


def parse_resolutions(text):
    """Return [(index, w, h)] from the [Resolutions] section, in index order."""
    body = section_body(text)
    xs, ys = {}, {}
    for m in re.finditer(r'(?im)^\s*Res(\d+)_([xy])\s*=\s*(\d+)', body):
        (xs if m.group(2).lower() == 'x' else ys)[int(m.group(1))] = int(m.group(3))
    return [(i, xs[i], ys[i]) for i in sorted(xs) if i in ys]


def section_body(text):
    m = re.search(r'(?im)^\[Resolutions\][^\[]*', text)
    if not m:
        raise RuntimeError('[Resolutions] section not found in vxConst.ini')
    return m.group(0)


def build_patched(text, wanted):
    existing = parse_resolutions(text)
    have = {(w, h) for _, w, h in existing}
    next_idx = max((i for i, _, _ in existing), default=0) + 1

    added, lines = [], []
    for w, h in wanted:
        if (w, h) in have:
            print('  skip %dx%d (already present)' % (w, h))
            continue
        lines.append('Res%d_x = %d' % (next_idx, w))
        lines.append('Res%d_y = %d' % (next_idx, h))
        added.append((next_idx, w, h))
        have.add((w, h))
        next_idx += 1
    if not added:
        return text, added

    body = section_body(text)
    # Append inside the section: after the last ResN_y line, before the section ends.
    last = None
    for m in re.finditer(r'(?im)^\s*Res\d+_y\s*=\s*\d+[^\r\n]*', body):
        last = m
    insert_at = last.end()
    new_body = body[:insert_at] + '\r\n' + '\r\n'.join(lines) + body[insert_at:]
    return text.replace(body, new_body, 1), added


def show():
    path = backup_of('data.pak')
    if not os.path.exists(path):
        path = os.path.join(GAME, 'data.pak')
    text = Pak(path).read('VXCONST.INI').decode('latin1')
    print('current [Resolutions] in %s:' % path)
    for i, w, h in parse_resolutions(text):
        print('  Res%-2d  %5d x %-5d   (vxSettings.ini  Resolution=%d)' % (i, w, h, i))


def apply(wanted):
    src = ensure_backup('data.pak')
    pak = Pak(src)
    text = pak.read('VXCONST.INI').decode('latin1')
    new_text, added = build_patched(text, wanted)
    if not added:
        print('nothing to add; game files left untouched')
        return
    pak.replace('VXCONST.INI', new_text.encode('latin1'))
    out = os.path.join(GAME, 'data.pak')
    pak.save(out)
    print('wrote %s (%d bytes)' % (out, os.path.getsize(out)))
    print('added:')
    for i, w, h in added:
        print('  Res%-2d  %5d x %-5d   -> set Resolution=%d in vxSettings.ini' % (i, w, h, i))


def restore():
    n = 0
    for name in ('data.pak', 'Celtic kings Launcher.exe', 'Celtic kings.exe'):
        b = backup_of(name)
        if os.path.exists(b):
            shutil.copy2(b, os.path.join(GAME, name))
            print('restored', name)
            n += 1
    if not n:
        print('no backups found in', BACKUP)


if __name__ == '__main__':
    args = sys.argv[1:]
    if not args or args[0] == '--list':
        show()
    elif args[0] == '--restore':
        restore()
    elif args[0] == '--apply':
        wanted = DEFAULT_ADD
        if len(args) > 1:
            wanted = [tuple(int(v) for v in a.lower().split('x')) for a in args[1:]]
        apply(wanted)
    else:
        print(__doc__)
