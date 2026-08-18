# -*- coding: utf-8 -*-
"""ckpak — HMMSYS PackFile 命令列工具。

給想自己動手改的人用：把 pak 拆開、改完再打包回去。
遊戲的 .pak 與 .bfhp（戰役／地圖）都是同一種格式。

用法：
    py tools/ckpak.py list    <pak> [關鍵字]
    py tools/ckpak.py cat     <pak> <內部路徑>
    py tools/ckpak.py extract <pak> <輸出資料夾> [關鍵字]
    py tools/ckpak.py replace <pak> <內部路徑> <來源檔> [-o 輸出pak]
    py tools/ckpak.py pack    <來源資料夾> <輸出pak>
    py tools/ckpak.py verify  <pak>

範例：
    py tools/ckpak.py list "…/CK_RageOfWar/data.pak" hero
    py tools/ckpak.py cat  "…/CK_RageOfWar/data.pak" CLASSES\\HERO.SC.XML
"""

import argparse
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

from hmmpak import Pak       # noqa: E402


def cmd_list(args):
    pak = Pak.load(args.pak)
    needle = (args.filter or '').upper()
    shown = 0
    for entry in pak.entries:
        if needle and needle not in entry.name.upper():
            continue
        print('%-58s %8d' % (entry.name, entry.size))
        shown += 1
    print('-- %d / %d 個檔案' % (shown, len(pak)))


def cmd_cat(args):
    pak = Pak.load(args.pak)
    sys.stdout.write(pak.read_text(args.name))


def cmd_extract(args):
    pak = Pak.load(args.pak)
    needle = (args.filter or '').upper()
    n = 0
    for entry in pak.entries:
        if needle and needle not in entry.name.upper():
            continue
        dst = os.path.join(args.outdir, entry.name.replace('\\', os.sep))
        os.makedirs(os.path.dirname(dst), exist_ok=True)
        with open(dst, 'wb') as f:
            f.write(pak.read(entry.name))
        n += 1
    print('已取出 %d 個檔案到 %s' % (n, args.outdir))


def cmd_replace(args):
    pak = Pak.load(args.pak)
    if args.name not in pak:
        print('警告：pak 內原本沒有 %s，會以新檔案加入' % args.name)
    with open(args.source, 'rb') as f:
        pak.write(args.name, f.read())
    out = args.output or args.pak
    pak.save(out)
    print('已寫入 %s' % out)


def cmd_pack(args):
    """把資料夾打包成 pak。檔名一律轉大寫、以反斜線分隔並排序。"""
    files = []
    root = os.path.abspath(args.srcdir)
    for dirpath, _dirnames, filenames in os.walk(root):
        for fn in filenames:
            full = os.path.join(dirpath, fn)
            rel = os.path.relpath(full, root).replace(os.sep, '\\').upper()
            files.append((rel, full))
    files.sort()

    pak = Pak([], {})
    for rel, full in files:
        with open(full, 'rb') as f:
            pak.write(rel, f.read())
    pak.save(args.outpak)
    print('已打包 %d 個檔案 -> %s' % (len(files), args.outpak))


def cmd_verify(args):
    """讀進來再寫出去，確認與原檔逐位元組相同。"""
    with open(args.pak, 'rb') as f:
        original = f.read()
    pak = Pak.from_bytes(original)
    same = pak.to_bytes() == original
    print('%s：%d 個檔案，往返一致 = %s' % (args.pak, len(pak), same))
    return 0 if same else 1


def main(argv=None):
    ap = argparse.ArgumentParser(
        prog='ckpak', description='HMMSYS PackFile（Celtic Kings .pak / .bfhp）工具')
    sub = ap.add_subparsers(dest='cmd', required=True)

    p = sub.add_parser('list', help='列出內容')
    p.add_argument('pak'); p.add_argument('filter', nargs='?')
    p.set_defaults(func=cmd_list)

    p = sub.add_parser('cat', help='印出單一檔案內容')
    p.add_argument('pak'); p.add_argument('name')
    p.set_defaults(func=cmd_cat)

    p = sub.add_parser('extract', help='解出檔案到資料夾')
    p.add_argument('pak'); p.add_argument('outdir'); p.add_argument('filter', nargs='?')
    p.set_defaults(func=cmd_extract)

    p = sub.add_parser('replace', help='替換 pak 內的單一檔案')
    p.add_argument('pak'); p.add_argument('name'); p.add_argument('source')
    p.add_argument('-o', '--output')
    p.set_defaults(func=cmd_replace)

    p = sub.add_parser('pack', help='把資料夾打包成 pak')
    p.add_argument('srcdir'); p.add_argument('outpak')
    p.set_defaults(func=cmd_pack)

    p = sub.add_parser('verify', help='驗證讀寫往返一致')
    p.add_argument('pak')
    p.set_defaults(func=cmd_verify)

    args = ap.parse_args(argv)
    return args.func(args) or 0


if __name__ == '__main__':
    sys.exit(main())
