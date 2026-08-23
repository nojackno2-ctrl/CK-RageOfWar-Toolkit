#!/usr/bin/env python3
"""Celtic Kings: Rage of War 繁體中文化補釘程式。

用法：
    python ckpatch.py install      安裝中文化
    python ckpatch.py uninstall    還原成原版
    python ckpatch.py status       檢查目前狀態
    python ckpatch.py extract      匯出翻譯工作檔（給續譯用）
    python ckpatch.py preview 文字  在終端機預覽造字結果

原理：
  1. 遊戲的 local.pak 內以資料夾分語系（GERMAN\\、FRENCH\\…），本程式新增一個
     CHINESE\\ 語系，內容由德文版檔案當模板、把 result 換成中文產生。
  2. 遊戲字型 local/fonts/*.apf 是 Unicode 點陣字型，本程式用系統中文字型
     即時光柵化並追加 CJK 字元範圍，原有拉丁／斯拉夫字形完全不動。
  3. vxSettings.ini 的 [Language] Default 改成 chinese。
  4. 原始 local.pak 與 vxSettings.ini 會先備份，隨時可以還原。
"""

from __future__ import annotations

import argparse
import json
import os
import shutil
import sys
import time
from pathlib import Path

ROOT = Path(__file__).resolve().parent
sys.path.insert(0, str(ROOT / "tools"))

import apf                     # noqa: E402
import gdifont                 # noqa: E402
import hmmpak                  # noqa: E402
import locxml                  # noqa: E402
import makefont                # noqa: E402

TRANS_DIR = ROOT / "翻譯"
BACKUP_DIR = ROOT / "備份"
LANG = "CHINESE"                 # local.pak 內的語系資料夾名稱
LANG_KEY = "chinese"             # vxSettings.ini 內的語系代號
TEMPLATE_LANG = "GERMAN"         # 拿來當結構模板的既有語系
DEFAULT_FACE = "微軟正黑體"
PATCHED_FILES = ("local.pak", "vxSettings.ini")

STEAM_HINTS = [
    r"C:\Program Files (x86)\Steam\steamapps\common\CK_RageOfWar",
    r"C:\Program Files\Steam\steamapps\common\CK_RageOfWar",
    r"D:\Steam\steamapps\common\CK_RageOfWar",
    r"D:\SteamLibrary\steamapps\common\CK_RageOfWar",
    r"E:\SteamLibrary\steamapps\common\CK_RageOfWar",
]


# ---------------------------------------------------------------- 環境

def find_game(explicit: str | None) -> Path:
    cands = []
    if explicit:
        cands.append(Path(explicit))
    saved = BACKUP_DIR / "遊戲路徑.txt"
    if saved.exists():
        cands.append(Path(saved.read_text(encoding="utf-8").strip()))
    cands += [Path(h) for h in STEAM_HINTS]
    for c in cands:
        if (c / "local.pak").exists() and (c / "Celtic kings.exe").exists():
            return c
    raise SystemExit(
        "找不到遊戲資料夾。請用 --game 指定，例如：\n"
        '  python ckpatch.py install --game "D:\\Steam\\steamapps\\common\\CK_RageOfWar"')


def ensure_backup(game: Path) -> None:
    BACKUP_DIR.mkdir(parents=True, exist_ok=True)
    (BACKUP_DIR / "遊戲路徑.txt").write_text(str(game), encoding="utf-8")
    for name in PATCHED_FILES:
        dst = BACKUP_DIR / name
        if dst.exists():
            continue
        src = game / name
        if not src.exists():
            raise SystemExit(f"遊戲資料夾缺少 {name}")
        if name == "local.pak":
            pak = hmmpak.PakFile.load(str(src))
            if any(e.name.startswith(LANG + "\\") for e in pak.entries):
                raise SystemExit(
                    "偵測到 local.pak 已經被中文化過，但備份不存在。\n"
                    "請先用 Steam 的「驗證遊戲檔案完整性」還原原版，再重新安裝。")
        shutil.copy2(src, dst)
        print(f"  已備份 {name}")


def source_pak(game: Path) -> hmmpak.PakFile:
    """一律以備份的原版 local.pak 為基礎重建，確保可重複安裝。"""
    path = BACKUP_DIR / "local.pak"
    return hmmpak.PakFile.load(str(path if path.exists() else game / "local.pak"))


# ---------------------------------------------------------------- 翻譯資料

class Translations:
    def __init__(self):
        self.by_text: dict[str, str] = {}       # 以英文原文為鍵
        self.by_key: dict[str, str] = {}        # 以完整 text（含 @context）為鍵
        self.help: dict[str, str] = {}
        self.credits: str | None = None
        self.loaded: list[str] = []

    @classmethod
    def load(cls, quiet: bool = False) -> "Translations":
        t = cls()
        if not TRANS_DIR.exists():
            return t
        for path in sorted(TRANS_DIR.rglob("*.json")):
            try:
                data = json.loads(path.read_text(encoding="utf-8"))
            except Exception as exc:
                print(f"  ! 略過 {path.name}：{exc}")
                continue
            if not isinstance(data, dict):
                continue
            data = {k: v for k, v in data.items()
                    if not k.startswith("_") and isinstance(v, str) and v}
            target = t.by_key if path.stem.endswith("-context") else t.by_text
            if path.stem == "help":
                target = t.help
            target.update(data)
            t.loaded.append(f"{path.relative_to(TRANS_DIR)}({len(data)})")
        cred = TRANS_DIR / "credits.txt"
        if cred.exists():
            t.credits = cred.read_text(encoding="utf-8")
        if t.loaded and not quiet:
            print("  翻譯資料：" + "、".join(t.loaded))
        return t

    def lookup(self, attrs: dict) -> str | None:
        key = attrs.get("text", "")
        if key in self.by_key:
            return self.by_key[key]
        return self.by_text.get(locxml.source_text(attrs))

    def all_text(self):
        yield from self.by_text.values()
        yield from self.by_key.values()
        yield from self.help.values()
        if self.credits:
            yield self.credits


# ---------------------------------------------------------------- 建置

def build_help(english: bytes, table: dict[str, str]) -> tuple[bytes, int, int]:
    """翻譯 help.xml 裡 <entry ...>文字</entry> 的內容。"""
    import re
    done = total = 0

    def repl(m):
        nonlocal done, total
        inner = m.group(2)
        stripped = inner.strip()
        if not stripped:
            return m.group(0)
        total += 1
        zh = table.get(locxml.unescape(stripped))
        if not zh:
            return m.group(0)
        done += 1
        lead = inner[:len(inner) - len(inner.lstrip())]
        tail = inner[len(inner.rstrip()):]
        return m.group(1) + lead + locxml.escape(zh) + tail + m.group(3)

    pat = re.compile(rb"(<entry\b(?![^>]*?/>)[^>]*>)(.*?)(</entry>)", re.S)
    out = pat.sub(lambda m: _bytes_repl(m, repl), english)
    return out, done, total


def _bytes_repl(m, fn):
    class S:
        def __init__(self, groups):
            self._g = groups

        def group(self, i=0):
            return self._g[i]
    g = [m.group(0).decode("utf-8"), m.group(1).decode("utf-8"),
         m.group(2).decode("utf-8"), m.group(3).decode("utf-8")]
    return fn(S(g)).encode("utf-8")


def build_language(pak: hmmpak.PakFile, trans: Translations, verbose: bool = True):
    """產生 CHINESE\\ 語系底下所有檔案。回傳 [(路徑, 內容)]。"""
    out: list[tuple[str, bytes]] = []
    done = total = 0

    for entry in pak.entries:
        name = entry.name
        parts = name.split("\\")
        # 根目錄語系檔：GERMAN\XXX
        if parts[0] == TEMPLATE_LANG:
            rel = "\\".join(parts[1:])
            new = LANG + "\\" + rel
        # 戰役／劇本語系檔：ADVENTURES\<名稱>\GERMAN\...
        elif len(parts) > 2 and parts[2] == TEMPLATE_LANG:
            new = "\\".join(parts[:2] + [LANG] + parts[3:])
        else:
            continue

        data = pak.read(entry)
        if rel_upper(new).endswith(".XML") and b"<translationtable" in data[:64]:
            data, d, t = locxml.rebuild(data, trans.lookup)
            done += d
            total += t
        out.append((new, data))

    # help.xml 以英文版為底翻譯（德文版是德文，不能當原文）
    eng_help = pak.find("ENGLISH\\HELP.XML")
    if eng_help:
        data, d, t = build_help(pak.read(eng_help), trans.help)
        out = [(n, v) for n, v in out if not n.upper().endswith("\\HELP.XML")]
        out.append((LANG + "\\HELP.XML", data))
        if verbose:
            print(f"  說明文件：{d}/{t} 段已翻譯")

    # credits
    eng_cred = pak.find("ENGLISH\\CREDITS.TXT")
    if eng_cred:
        text = trans.credits or pak.read(eng_cred).decode("utf-8", "replace")
        out = [(n, v) for n, v in out if not n.upper().endswith("\\CREDITS.TXT")]
        out.append((LANG + "\\CREDITS.TXT", text.encode("utf-8")))

    if verbose:
        print(f"  文字條目：{done}/{total} 已翻譯（其餘保留英文）")
    return out, done, total


def build_fonts(pak: hmmpak.PakFile, charset: set[int], face: str,
                size_delta: int = 0, verbose: bool = True):
    out: list[tuple[str, bytes]] = []
    missing_all: set[int] = set()
    for entry in pak.entries:
        if not entry.name.startswith("FONTS\\"):
            continue
        data = pak.read(entry)
        if entry.name.upper().endswith(".APF"):
            font = apf.ApfFont.load(data)
            added, missing = makefont.add_cjk(font, charset, face, size_delta=size_delta)
            missing_all.update(missing)
            data = font.dump()
            if verbose:
                print(f"    {entry.name:26s} +{added:5d} 字形  {len(data)/1024:8.1f} KB")
        out.append((entry.name, data))
    return out, missing_all


def rel_upper(name: str) -> str:
    return name.upper()


# ---------------------------------------------------------------- 指令

def cmd_install(args):
    game = find_game(args.game)
    print(f"遊戲資料夾：{game}")

    running = [p for p in ("Celtic kings.exe",) if _is_locked(game / "local.pak")]
    if running:
        raise SystemExit("local.pak 正在被使用，請先關閉遊戲再安裝。")

    print("備份原始檔…")
    ensure_backup(game)

    trans = Translations.load()
    if not trans.by_text and not trans.by_key:
        print("  ! 翻譯資料夾是空的，這次只會裝中文字型與空的中文語系。")

    pak = source_pak(game)

    print("產生中文語系檔…")
    lang_files, done, total = build_language(pak, trans)

    charset = set(makefont.BASE_EXTRA)
    for text in trans.all_text():
        charset.update(ord(c) for c in text if ord(c) > 0x2FF)
    for _name, data in lang_files:
        try:
            charset.update(ord(c) for c in data.decode("utf-8") if ord(c) > 0x2FF)
        except UnicodeDecodeError:
            pass
    if args.full_cjk:
        charset.update(makefont.FULL_CJK)
    print(f"造字字元集：{len(charset)} 個碼位"
          f"{'（含完整 CJK 基本區）' if args.full_cjk else ''}")

    print("產生中文字型…")
    t0 = time.time()
    font_files, missing = build_fonts(pak, charset, args.font, args.size_delta)
    print(f"  耗時 {time.time() - t0:.1f} 秒"
          + (f"，字型缺 {len(missing)} 個字形" if missing else ""))

    files = {e.name: (pak.read(e), e.timestamp) for e in pak.entries}
    ts = pak.entries[0].timestamp
    for name, data in font_files:
        files[name] = (data, ts)                       # 主字型也換成含中文的版本
    for name, data in lang_files:
        files[name] = (data, ts)

    blob = hmmpak.PakFile.build(
        [(n, d, t) for n, (d, t) in sorted(files.items())])
    _write(game / "local.pak", blob)
    print(f"寫入 local.pak：{len(files)} 個檔案，{len(blob)/1024/1024:.1f} MB")

    _set_language(game, LANG_KEY)
    print(f"vxSettings.ini 語言 -> {LANG_KEY}")
    print("\n完成！直接啟動遊戲即可。要還原請執行： python ckpatch.py uninstall")


def cmd_uninstall(args):
    game = find_game(args.game)
    if not (BACKUP_DIR / "local.pak").exists():
        raise SystemExit("找不到備份，無法還原。請用 Steam 驗證遊戲檔案完整性。")
    if _is_locked(game / "local.pak"):
        raise SystemExit("local.pak 正在被使用，請先關閉遊戲。")
    for name in PATCHED_FILES:
        src = BACKUP_DIR / name
        if src.exists():
            shutil.copy2(src, game / name)
            print(f"  已還原 {name}")
    print("已還原成原版。")


def cmd_status(args):
    game = find_game(args.game)
    print(f"遊戲資料夾：{game}")
    pak = hmmpak.PakFile.load(str(game / "local.pak"))
    langs = sorted({e.name.split("\\")[0] for e in pak.entries
                    if "\\" in e.name and e.name.split("\\")[0] not in
                    ("ADVENTURES", "SCENARIOS", "FONTS")})
    patched = LANG in langs
    print(f"local.pak 語系：{'、'.join(langs)}")
    print(f"中文化狀態：{'已安裝' if patched else '未安裝'}")
    print(f"備份：{'有' if (BACKUP_DIR / 'local.pak').exists() else '沒有'}"
          f"（{BACKUP_DIR}）")
    ini = (game / "vxSettings.ini")
    if ini.exists():
        for line in ini.read_text(encoding="cp1252", errors="replace").splitlines():
            if line.lower().startswith("default="):
                print(f"目前語言設定：{line.split('=', 1)[1].strip()}")
                break
    if patched:
        font = apf.ApfFont.load(pak.read(pak.find("FONTS\\TAHOMA13.APF")))
        n = sum(r.count for r in font.ranges)
        print(f"字型：{font.face}，{len(font.ranges)} 個字元範圍，{n} 個字形")
    trans = Translations.load(quiet=True)
    print(f"翻譯資料：{len(trans.by_text) + len(trans.by_key)} 條詞條、"
          f"{len(trans.help)} 段說明")


def cmd_extract(args):
    """把原版的英文原文匯出成翻譯工作檔。"""
    game = find_game(args.game)
    pak = source_pak(game)
    outdir = ROOT / "翻譯範本"
    outdir.mkdir(exist_ok=True)

    buckets: dict[str, dict[str, str]] = {}
    ctx_note: dict[str, list[str]] = {}
    for entry in pak.entries:
        parts = entry.name.split("\\")
        if parts[0] == TEMPLATE_LANG:
            bucket = "ui"
        elif len(parts) > 2 and parts[2] == TEMPLATE_LANG:
            bucket = "campaign-" + parts[1].lower().replace(" ", "-")
        else:
            continue
        data = pak.read(entry)
        if b"<translationtable" not in data[:64]:
            continue
        for _tag, attrs in locxml.entries(data):
            src = locxml.source_text(attrs)
            if not src.strip():
                continue
            buckets.setdefault(bucket, {})[src] = ""
            if attrs.get("context"):
                ctx_note.setdefault(bucket, []).append(attrs["text"])

    existing = Translations.load(quiet=True)
    for name, table in sorted(buckets.items()):
        keep = {k: existing.by_text.get(k, "") for k in table}
        path = outdir / f"{name}.json"
        path.write_text(json.dumps(keep, ensure_ascii=False, indent=1),
                        encoding="utf-8")
        filled = sum(1 for v in keep.values() if v)
        print(f"  {path.name:34s} {len(keep):5d} 條，已翻 {filled}")

    eng_help = pak.find("ENGLISH\\HELP.XML")
    if eng_help:
        import re
        text = pak.read(eng_help).decode("utf-8")
        segs = {}
        for m in re.finditer(r"<entry\b(?![^>]*?/>)[^>]*>(.*?)</entry>", text, re.S):
            s = locxml.unescape(m.group(1).strip())
            if s:
                segs[s] = existing.help.get(s, "")
        (outdir / "help.json").write_text(
            json.dumps(segs, ensure_ascii=False, indent=1), encoding="utf-8")
        print(f"  help.json                          {len(segs):5d} 段")
    print(f"\n工作檔在 {outdir}\n翻好後把檔案（或其中一部分）放進「翻譯」資料夾再 install 即可。")


def cmd_preview(args):
    game = find_game(args.game)
    pak = source_pak(game)
    text = args.text or "凱爾特之王：戰爭狂怒"
    ramp = " .:-=+*#@"
    for name in ("FONTS\\TAHOMA13.APF", "FONTS\\TAHOMA16B.APF"):
        font = apf.ApfFont.load(pak.read(pak.find(name)))
        makefont.add_cjk(font, {ord(c) for c in text}, args.font,
                         size_delta=args.size_delta)
        print(f"--- {name}（{args.font} {font.metrics[0] + args.size_delta}px）")
        rows = [[] for _ in range(font.metrics[5] + 2)]
        for ch in text:
            g = None
            for r in font.ranges:
                if r.first <= ord(ch) < r.first + r.count:
                    g = r.glyphs[ord(ch) - r.first]
                    break
            if g is None or not g.width:
                continue
            for y in range(len(rows)):
                if g.top <= y < g.top + g.height:
                    line = "".join(ramp[min(8, g.pixels[(y - g.top) * g.width + x] * 8 // 14)]
                                   for x in range(g.width))
                else:
                    line = " " * g.width
                rows[y].append(line + " ")
        for r in rows:
            print("  " + "".join(r))


# ---------------------------------------------------------------- 小工具

def _is_locked(path: Path) -> bool:
    if not path.exists():
        return False
    try:
        with open(path, "r+b"):
            return False
    except OSError:
        return True


def _write(path: Path, data: bytes) -> None:
    tmp = path.with_suffix(path.suffix + ".tmp")
    tmp.write_bytes(data)
    os.replace(tmp, path)


def _set_language(game: Path, lang: str) -> None:
    path = game / "vxSettings.ini"
    src = BACKUP_DIR / "vxSettings.ini"
    text = (src if src.exists() else path).read_text(encoding="cp1252", errors="replace")
    out, hit = [], False
    section = ""
    for line in text.splitlines():
        s = line.strip()
        if s.startswith("["):
            section = s.lower()
        if section == "[language]" and s.lower().startswith("default="):
            line = f"Default={lang}"
            hit = True
        out.append(line)
    if not hit:
        out = ["[Language]", f"Default={lang}", ""] + out
    path.write_text("\n".join(out) + "\n", encoding="cp1252", errors="replace")


def main(argv=None):
    ap = argparse.ArgumentParser(
        prog="ckpatch", description="Celtic Kings: Rage of War 繁體中文化補釘程式")
    ap.add_argument("--game", help="遊戲安裝資料夾")
    ap.add_argument("--font", default=DEFAULT_FACE, help=f"造字用的中文字型（預設 {DEFAULT_FACE}）")
    ap.add_argument("--size-delta", type=int, default=0, help="中文字級微調（像素，可為負）")
    sub = ap.add_subparsers(dest="cmd", required=True)

    p = sub.add_parser("install", help="安裝中文化")
    p.add_argument("--full-cjk", action="store_true",
                   help="收錄整個 CJK 基本區（檔案較大，適合自行擴充翻譯）")
    p.set_defaults(func=cmd_install)

    sub.add_parser("uninstall", help="還原原版").set_defaults(func=cmd_uninstall)
    sub.add_parser("status", help="檢查狀態").set_defaults(func=cmd_status)
    sub.add_parser("extract", help="匯出翻譯工作檔").set_defaults(func=cmd_extract)

    p = sub.add_parser("preview", help="預覽造字效果")
    p.add_argument("text", nargs="?")
    p.set_defaults(func=cmd_preview)

    args = ap.parse_args(argv)
    try:
        args.func(args)
    except gdifont.ctypes.ArgumentError as exc:
        raise SystemExit(f"字型處理失敗：{exc}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
